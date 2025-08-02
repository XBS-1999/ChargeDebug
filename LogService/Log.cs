using System.Collections.Concurrent;
using System.Text;

namespace Log
{
    public static class LogService
    {
        private static readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        private static StreamWriter logWriter;
        private static string currentLogFilePath;
        private static readonly object initLock = new object();
        private static bool isInitialized;

        public static event Action<string> LogAdded;

        public static void Initialize()
        {
            lock (initLock)
            {
                if (isInitialized) return;

                try
                {
                    // 创建日志根目录
                    string logRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    Directory.CreateDirectory(logRoot);

                    // 创建日期子目录
                    string dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
                    string datedLogPath = Path.Combine(logRoot, dateFolder);
                    Directory.CreateDirectory(datedLogPath);

                    // 生成带时间戳的日志文件名
                    string timestamp = DateTime.Now.ToString("HHmmss");
                    string fileName = $"SystemLog_{timestamp}.txt";
                    currentLogFilePath = Path.Combine(datedLogPath, fileName);

                    // 初始化日志文件
                    logWriter = new StreamWriter(currentLogFilePath, true, Encoding.UTF8);
                    logWriter.AutoFlush = true;

                    isInitialized = true;
                    Log("日志系统初始化完成");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"日志初始化失败: {ex.Message}");
                }
            }
        }

        public static void Log(string message)
        {
            if (!isInitialized) Initialize();

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:fff");
            string logEntry = $"[{timestamp}] {message}";

            logQueue.Enqueue(logEntry);

            try
            {
                logWriter?.WriteLine(logEntry);
            }
            catch
            {
                // 忽略写入错误
            }

            // 触发事件
            LogAdded?.Invoke(logEntry);
        }

        public static void Flush()
        {
            logWriter?.Flush();
        }

        public static string GetCurrentLogContent()
        {
            try
            {
                return File.ReadAllText(currentLogFilePath);
            }
            catch
            {
                return "无法读取日志文件";
            }
        }
    }
}
