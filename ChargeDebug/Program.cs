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
            // 1. �����ڴ����κδ���֮ǰ���ü����ı���Ⱦ
            Application.SetCompatibleTextRenderingDefault(false);

            // 2. �ڴ����κοؼ�֮ǰ�����쳣����
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // 3. ��ʼ��Ӧ�ó�������
            ApplicationConfiguration.Initialize();

            bool restartApp = true;

            while (restartApp)
            {
                restartApp = false;
                bool createdNew;
                mutex = new Mutex(true, AppMutexName, out createdNew);

                if (!createdNew && IsApplicationAlreadyRunning())
                {
                    XtraMessageBox.Show("������������У������ظ��򿪣�", "��ʾ",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    // 4. ����DevExpress������ã��ڳ�ʼ��֮��
                    DevExpress.XtraEditors.WindowsFormsSettings.DefaultFont =
                        new Font("Tahoma", 12, FontStyle.Regular);
                    DevExpress.XtraEditors.WindowsFormsSettings.DefaultMenuFont =
                        new Font("Tahoma", 12, FontStyle.Regular);
                    DevExpress.LookAndFeel.UserLookAndFeel.Default.SetSkinStyle("Office 2019 Colorful");

                    // 5. �������ݿ�·��
                    dbPath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase + "ChargeDebug.db3";

                    if (!File.Exists(dbPath))
                    {
                        XtraMessageBox.Show("���ݿⲻ���ڣ�");
                        return;
                    }

                    // 6. ��������ʾ��¼����
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
                                // +++ �ؼ����û��л�ǰ���� CAN ������ +++
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
            string errorMsg = $"UI�߳��쳣:\n\n{e.Exception}";
            XtraMessageBox.Show(errorMsg, "UI�̴߳���",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            string errorMsg = "����δ������쳣:\n\n" +
                             (ex != null ? ex.ToString() : "δ֪����");

            XtraMessageBox.Show(errorMsg, "���ش���",
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