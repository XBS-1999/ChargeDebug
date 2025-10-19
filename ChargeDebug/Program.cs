using ChargeDebug.Form;
using ChargeDebug.Service;
using DevExpress.XtraEditors;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ChargeDebug
{
    internal static class Program
    {
        private static Mutex? mutex;
        private const string AppMutexName = "ChargeDebug2.0_UniqueMutex";
        private static string dbPath = string.Empty;

        [STAThread]
        static void Main()
        {
            // ===== 初始化阶段 =====
            // 1. 必须在创建任何窗口之前设置兼容文本渲染
            Application.SetCompatibleTextRenderingDefault(false);

            // 2. 注册全局异常处理
            RegisterGlobalExceptionHandlers();

            // 3. 初始化应用程序配置
            ApplicationConfiguration.Initialize();

            // 4. 设置DevExpress全局配置（简化版本，避免字体问题）
            ConfigureDevExpressSettings();

            // ===== 应用程序主循环 =====
            RunApplicationLoop();
        }

        /// <summary>
        /// 注册全局异常处理器
        /// </summary>
        private static void RegisterGlobalExceptionHandlers()
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnApplicationThreadException;
                AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"注册异常处理器失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 配置DevExpress全局设置 - 修复字体问题版本
        /// </summary>
        private static void ConfigureDevExpressSettings()
        {
            try
            {
                WindowsFormsSettings.DefaultFont = new Font("Tahoma", 10, FontStyle.Regular);
                DevExpress.LookAndFeel.UserLookAndFeel.Default.SetSkinStyle("WXI");

                Debug.WriteLine("DevExpress配置完成");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DevExpress配置失败: {ex.Message}");
                // 继续运行，不使用自定义字体
            }
        }

        /// <summary>
        /// 获取安全的字体对象，避免参数无效异常
        /// </summary>
        private static Font? GetSafeFont()
        {
            try
            {
                // 检查字体是否存在
                var fontCollection = new System.Drawing.Text.InstalledFontCollection();
                var fontFamilies = fontCollection.Families;

                string preferredFont = "Microsoft Sans Serif"; // 更安全的字体选择
                string fallbackFont = "Tahoma";
                string systemFont = "Segoe UI";

                // 按优先级尝试创建字体
                foreach (var fontName in new[] { preferredFont, fallbackFont, systemFont })
                {
                    if (fontFamilies.Any(f => f.Name.Equals(fontName, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new Font(fontName, 9f, FontStyle.Regular); // 使用更小的字号
                    }
                }

                // 如果所有字体都不存在，使用默认字体
                return SystemFonts.DefaultFont;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"创建字体失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 应用程序主运行循环
        /// </summary>
        private static void RunApplicationLoop()
        {
            bool restartRequested;

            do
            {
                restartRequested = false;

                // 使用Mutex确保单实例运行
                if (!AcquireApplicationMutex())
                {
                    return;
                }

                try
                {
                    // 执行应用程序主逻辑
                    restartRequested = RunApplicationCore();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"应用程序主逻辑执行失败: {ex}");
                    XtraMessageBox.Show($"应用程序启动失败: {ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    ReleaseApplicationMutex();
                    Thread.Sleep(100);
                }

            } while (restartRequested);
        }

        /// <summary>
        /// 获取应用程序Mutex
        /// </summary>
        private static bool AcquireApplicationMutex()
        {
            try
            {
                bool mutexCreated;
                mutex = new Mutex(true, AppMutexName, out mutexCreated);

                if (!mutexCreated)
                {
                    if (IsApplicationAlreadyRunning())
                    {
                        XtraMessageBox.Show("软件已在运行中，请勿重复打开！", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }

                    mutex?.Dispose();
                    mutex = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"创建Mutex失败: {ex.Message}");
                // 如果Mutex创建失败，允许程序继续运行
                return true;
            }
        }

        /// <summary>
        /// 释放应用程序Mutex资源
        /// </summary>
        private static void ReleaseApplicationMutex()
        {
            try
            {
                if (mutex != null)
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Mutex释放异常: {ex.Message}");
            }
            finally
            {
                mutex = null;
            }
        }

        /// <summary>
        /// 应用程序核心逻辑执行
        /// </summary>
        private static bool RunApplicationCore()
        {
            // 验证数据库文件
            if (!ValidateDatabaseFile())
            {
                return false;
            }

            // 显示登录窗口
            LoginForm? loginForm = null;
            try
            {
                loginForm = new LoginForm(dbPath);
                if (loginForm.ShowDialog() != DialogResult.OK)
                {
                    return false;
                }

                // 运行主应用程序
                return RunMainApplication(loginForm);
            }
            finally
            {
                loginForm?.Dispose();
            }
        }

        /// <summary>
        /// 验证数据库文件
        /// </summary>
        private static bool ValidateDatabaseFile()
        {
            try
            {
                dbPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "ChargeDebug.db3");

                if (!File.Exists(dbPath))
                {
                    XtraMessageBox.Show("数据库文件不存在，请检查安装完整性！", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                // 验证文件可访问性
                //FileInfo fileInfo = new FileInfo(dbPath);
                //using (var fileStream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
                //{
                //    // 文件可以正常打开
                //}

                return true;
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"数据库文件访问失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// 运行主应用程序窗口
        /// </summary>
        private static bool RunMainApplication(LoginForm loginForm)
        {
            MainForm? mainForm = null;
            try
            {
                mainForm = new MainForm(dbPath, loginForm.UserPermissions, loginForm.Username);
                var result = mainForm.ShowDialog();

                if (result == DialogResult.Retry)
                {
                    ResetGlobalState();
                    return true;
                }

                return false;
            }
            finally
            {
                mainForm?.Dispose();
            }
        }

        /// <summary>
        /// 重置全局状态
        /// </summary>
        private static void ResetGlobalState()
        {
            try
            {
                CANManager.Instance.FullReset();

                // 清理DevExpress缓存
                DevExpress.Utils.AppearanceObject.DefaultFont = null;

                GC.Collect();
                GC.WaitForPendingFinalizers();

                Debug.WriteLine("全局状态重置完成");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"全局状态重置异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查应用程序是否已在运行
        /// </summary>
        private static bool IsApplicationAlreadyRunning()
        {
            Process currentProcess = Process.GetCurrentProcess();

            try
            {
                var processes = Process.GetProcessesByName(currentProcess.ProcessName);

                foreach (var process in processes)
                {
                    if (process.Id == currentProcess.Id)
                        continue;

                    try
                    {
                        if (IsSameApplicationProcess(process, currentProcess))
                        {
                            ActivateExistingWindow(process);
                            return true;
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"检查运行实例异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 判断是否为同一应用程序进程
        /// </summary>
        private static bool IsSameApplicationProcess(Process process1, Process process2)
        {
            try
            {
                string? file1 = process1.MainModule?.FileName;
                string? file2 = process2.MainModule?.FileName;

                return !string.IsNullOrEmpty(file1) &&
                       !string.IsNullOrEmpty(file2) &&
                       file1.Equals(file2, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return process1.ProcessName == process2.ProcessName;
            }
        }

        /// <summary>
        /// 激活已存在的应用程序窗口
        /// </summary>
        private static void ActivateExistingWindow(Process existingProcess)
        {
            try
            {
                IntPtr hWnd = existingProcess.MainWindowHandle;
                if (hWnd != IntPtr.Zero)
                {
                    if (NativeMethods.IsIconic(hWnd))
                    {
                        NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
                    }
                    NativeMethods.SetForegroundWindow(hWnd);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"激活现有窗口失败: {ex.Message}");
            }
        }

        /// <summary>
        /// UI线程异常处理
        /// </summary>
        private static void OnApplicationThreadException(object sender, ThreadExceptionEventArgs e)
        {
            string errorMsg = $"UI线程异常:\n{e.Exception.GetType().Name}: {e.Exception.Message}";
            Debug.WriteLine(errorMsg);

            try
            {
                XtraMessageBox.Show(errorMsg, "UI线程错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // 如果UI操作失败，至少记录到调试输出
                Debug.WriteLine("无法显示错误对话框");
            }
        }

        /// <summary>
        /// 应用程序域未处理异常处理
        /// </summary>
        private static void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception? ex = e.ExceptionObject as Exception;
            string errorMsg = $"未处理异常:\n{(ex?.GetType().Name ?? "未知类型")}: {ex?.Message ?? "未知错误"}";

            Debug.WriteLine(errorMsg);

            try
            {
                XtraMessageBox.Show(errorMsg, "严重错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                Debug.WriteLine("无法显示严重错误对话框");
            }
        }

        /// <summary>
        /// 原生Windows API方法封装
        /// </summary>
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