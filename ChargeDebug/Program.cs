using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ChargeDebug.Form;
using ChargeDebug.Service;
using DevExpress.XtraEditors;

namespace ChargeDebug
{
    internal static class Program
    {
        private static Mutex mutex;
        private const string AppMutexName = "ChargeDebug2.0_UniqueMutex";
        private static string dbPath = "";

        [STAThread]
        static void Main()
        {
            // 1. 必须在创建任何窗口之前设置兼容文本渲染
            Application.SetCompatibleTextRenderingDefault(false);

            // 2. 在创建任何控件之前设置异常处理
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // 3. 初始化应用程序配置
            ApplicationConfiguration.Initialize();

            bool restartApp = true;

            while (restartApp)
            {
                restartApp = false;
                bool createdNew;
                mutex = new Mutex(true, AppMutexName, out createdNew);

                if (!createdNew && IsApplicationAlreadyRunning())
                {
                    XtraMessageBox.Show("软件已在运行中，请勿重复打开！", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    // 4. 设置DevExpress相关配置（在初始化之后）
                    DevExpress.XtraEditors.WindowsFormsSettings.DefaultFont =
                        new Font("Tahoma", 12, FontStyle.Regular);
                    DevExpress.XtraEditors.WindowsFormsSettings.DefaultMenuFont =
                        new Font("Tahoma", 12, FontStyle.Regular);
                    DevExpress.LookAndFeel.UserLookAndFeel.Default.SetSkinStyle("Office 2019 Colorful");

                    // 5. 设置数据库路径
                    dbPath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase + "ChargeDebug.db3";

                    if (!File.Exists(dbPath))
                    {
                        XtraMessageBox.Show("数据库不存在！");
                        return;
                    }

                    // 6. 创建并显示登录窗口
                    using (LoginForm loginForm = new LoginForm(dbPath))
                    {
                        if (loginForm.ShowDialog() != DialogResult.OK)
                        {
                            return;
                        }

                        using (MainForm mainForm = new MainForm(dbPath, loginForm.UserPermissions, loginForm.Username))
                        {
                            if (mainForm.ShowDialog() == DialogResult.Retry)
                            {
                                // +++ 关键：用户切换前重置 CAN 管理器 +++
                                CANManager.Instance.FullReset();
                                restartApp = true;
                            }
                        }
                    }
                }
                finally
                {
                    if (mutex != null)
                    {
                        mutex.ReleaseMutex();
                        mutex.Dispose();
                        mutex = null;
                    }
                    Thread.Sleep(500);
                }
            }
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            string errorMsg = $"UI线程异常:\n\n{e.Exception}";
            XtraMessageBox.Show(errorMsg, "UI线程错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            string errorMsg = "发生未处理的异常:\n\n" +
                             (ex != null ? ex.ToString() : "未知错误");

            XtraMessageBox.Show(errorMsg, "严重错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static bool IsApplicationAlreadyRunning()
        {
            Process currentProcess = Process.GetCurrentProcess();
            Process[] processes = Process.GetProcessesByName(currentProcess.ProcessName);

            foreach (Process process in processes)
            {
                if (process.Id == currentProcess.Id)
                    continue;

                if (process.MainModule.FileName == currentProcess.MainModule.FileName)
                {
                    IntPtr hWnd = process.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        if (NativeMethods.IsIconic(hWnd))
                        {
                            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
                        }
                        NativeMethods.SetForegroundWindow(hWnd);
                    }
                    return true;
                }
            }
            return false;
        }

        internal static class NativeMethods
        {
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsIconic(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(IntPtr hWnd);

            public const int SW_RESTORE = 9;
        }
    }
}