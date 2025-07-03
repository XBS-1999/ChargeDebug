using System.Collections.Concurrent;
using System.Text;

namespace Log
{
    public class LogService
    {
        private static readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        private static StreamWriter logWriter;
        private static string logFilePath;

        public static event Action<string> LogAdded;

        static LogService()
        {
            string appDir = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
            logFilePath = Path.Combine(appDir, "SystemLog.txt");

            // 初始化日志文件
            try
            {
                logWriter = new StreamWriter(logFilePath, true, Encoding.UTF8);
                logWriter.AutoFlush = true;
                Log("系统日志初始化完成");
            }
            catch (Exception ex)
            {
                // 如果文件创建失败，记录到控制台
                Console.WriteLine($"日志初始化失败: {ex.Message}");
            }
        }

        public static void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:fff");
            string logEntry = $"[{timestamp}] {message}";

            // 添加到队列
            logQueue.Enqueue(logEntry);

            // 写入文件
            if (logWriter != null)
            {
                try
                {
                    logWriter.WriteLine(logEntry);
                }
                catch
                {
                    // 忽略写入错误
                }
            }

            // 触发事件
            LogAdded?.Invoke(logEntry);
        }

        public static void Flush()
        {
            logWriter?.Flush();
        }

        public static string GetAllLogs()
        {
            try
            {
                return File.ReadAllText(logFilePath);
            }
            catch
            {
                return "无法读取日志文件";
            }
        }
    }
}
