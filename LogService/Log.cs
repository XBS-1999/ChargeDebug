using System.Collections.Concurrent;
using System.Text;

#pragma warning disable
namespace Log
{
    public static class LogService
    {
        private static readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        private static StreamWriter logWriter;
        private static string currentLogFilePath;
        private static readonly object initLock = new object();
        private static readonly object cleanupLock = new object();
        private static bool isInitialized;
        private static long maxFolderSize = 50L * 1024 * 1024;    // 50MB
        private static long cleanupThreshold = 20L * 1024 * 1024; // 20MB - 清理到该大小

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

                    // 启动时检查文件夹大小
                    Task.Run(() => CheckAndCleanupLogs(logRoot));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"日志初始化失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 检查并清理日志文件
        /// </summary>
        private static void CheckAndCleanupLogs(string logRoot)
        {
            // 使用锁确保同一时间只有一个清理任务在执行
            if (!Monitor.TryEnter(cleanupLock))
                return;

            try
            {
                if (!Directory.Exists(logRoot))
                    return;

                // 计算日志文件夹总大小
                long totalSize = CalculateFolderSize(logRoot);

                if (totalSize > maxFolderSize)
                {
                    Log($"日志文件夹大小 {FormatFileSize(totalSize)} 超过限制，开始清理...");
                    CleanupOldLogs(logRoot, totalSize);
                }
            }
            catch (Exception ex)
            {
                // 在控制台输出清理错误，避免递归调用Log方法
                Console.WriteLine($"日志清理失败: {ex.Message}");
            }
            finally
            {
                Monitor.Exit(cleanupLock);
            }
        }

        /// <summary>
        /// 计算文件夹大小
        /// </summary>
        private static long CalculateFolderSize(string folderPath)
        {
            long size = 0;
            try
            {
                // 获取所有文件（包括子目录）
                var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.Exists)
                            size += fileInfo.Length;
                    }
                    catch
                    {
                        // 忽略无法访问的文件
                    }
                }
            }
            catch
            {
                // 忽略目录访问错误
            }
            return size;
        }

        // <summary>
        /// 清理旧的日志文件
        /// </summary>
        private static void CleanupOldLogs(string logRoot, long currentSize)
        {
            try
            {
                // 获取所有日志文件（包括子目录）
                var allFiles = Directory.GetFiles(logRoot, "*.txt", SearchOption.AllDirectories)
                    .Select(file => new FileInfo(file))
                    .Where(fi => fi.Exists)
                    .OrderBy(fi => fi.CreationTime) // 按创建时间排序，最早的在前
                    .ToList();

                long sizeToRemove = currentSize - cleanupThreshold;
                long removedSize = 0;

                foreach (var file in allFiles)
                {
                    if (removedSize >= sizeToRemove)
                        break;

                    try
                    {
                        // 跳过当前正在使用的日志文件
                        if (file.FullName == currentLogFilePath)
                            continue;

                        long fileSize = file.Length;
                        file.Delete();
                        removedSize += fileSize;

                        // 记录清理操作（使用控制台输出，避免递归）
                        Console.WriteLine($"已删除旧日志文件: {file.FullName} ({FormatFileSize(fileSize)})");

                        // 尝试删除空目录
                        TryDeleteEmptyDirectory(file.Directory);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"删除文件 {file.FullName} 失败: {ex.Message}");
                    }
                }

                if (removedSize > 0)
                {
                    long newSize = currentSize - removedSize;
                    Console.WriteLine($"日志清理完成: 删除了 {FormatFileSize(removedSize)}，当前大小: {FormatFileSize(newSize)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清理日志文件时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 尝试删除空目录
        /// </summary>
        private static void TryDeleteEmptyDirectory(DirectoryInfo directory)
        {
            try
            {
                // 如果目录不存在或者不是日志根目录的子目录，直接返回
                string logRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                if (!directory.Exists || !directory.FullName.StartsWith(logRoot) || directory.FullName == logRoot)
                    return;

                // 如果目录为空，删除它
                if (!directory.GetFiles().Any() && !directory.GetDirectories().Any())
                {
                    directory.Delete();
                    // 递归检查父目录
                    TryDeleteEmptyDirectory(directory.Parent);
                }
            }
            catch
            {
                // 忽略目录删除错误
            }
        }

        /// <summary>
        /// 格式化文件大小显示
        /// </summary>
        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        /// <summary>
        /// 设置日志文件夹大小限制
        /// </summary>
        /// <param name="maxSizeMB">最大大小（MB）</param>
        /// <param name="cleanupToMB">清理到的大小（MB）</param>
        public static void SetSizeLimit(int maxSizeMB = 1000, int cleanupToMB = 900)
        {
            if (maxSizeMB <= cleanupToMB)
                throw new ArgumentException("最大大小必须大于清理阈值");

            maxFolderSize = maxSizeMB * 1024L * 1024;
            cleanupThreshold = cleanupToMB * 1024L * 1024;
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

                // 每次写入日志后检查文件夹大小（异步执行，避免阻塞）
                Task.Run(() =>
                {
                    string logRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    CheckAndCleanupLogs(logRoot);
                });
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
