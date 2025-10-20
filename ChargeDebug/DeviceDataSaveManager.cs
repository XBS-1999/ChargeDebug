using Log;
using System.Collections.Concurrent;
using System.IO;

#pragma warning disable
namespace ChargeDebug.Service
{
    /// <summary>
    /// 设备数据保存管理器 - 负责管理同一设备下的所有数据保存操作
    /// </summary>
    public class DeviceDataSaveManager : IDisposable
    {
        #region 单例模式

        private static readonly Lazy<DeviceDataSaveManager> _instance =
            new Lazy<DeviceDataSaveManager>(() => new DeviceDataSaveManager());

        public static DeviceDataSaveManager Instance => _instance.Value;

        #endregion

        #region 字段声明

        // 设备级别的保存状态和文件写入器
        private readonly ConcurrentDictionary<string, DeviceSaveContext> _deviceContexts =
            new ConcurrentDictionary<string, DeviceSaveContext>();

        // 文件管理常量
        public const long MAX_REALTIME_FILE_SIZE = 100 * 1024 * 1024; // 100MB
        public const long MAX_BASE_FOLDER_SIZE = 2000 * 1024 * 1024; // 2000MB

        // 基础保存目录
        public static readonly string _baseSaveDirectory;

        #endregion

        #region 构造函数

        static DeviceDataSaveManager()
        {
            // 初始化基础保存目录
            _baseSaveDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RealTimeData");
            if (!Directory.Exists(_baseSaveDirectory))
            {
                Directory.CreateDirectory(_baseSaveDirectory);
            }
        }

        private DeviceDataSaveManager()
        {
            // 启动定期清理任务
            StartPeriodicCleanup();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 注册模块到设备保存管理器
        /// </summary>
        public void RegisterModule(string deviceKey, string moduleTitle, Action<bool> saveStatusChangedCallback)
        {
            var context = _deviceContexts.GetOrAdd(deviceKey, key => new DeviceSaveContext(key));
            context.RegisterModule(moduleTitle, saveStatusChangedCallback);

            LogService.Log($"模块 '{moduleTitle}' 已注册到设备 '{deviceKey}' 的数据保存管理器");
        }

        /// <summary>
        /// 注销模块
        /// </summary>
        public void UnregisterModule(string deviceKey, string moduleTitle)
        {
            if (_deviceContexts.TryGetValue(deviceKey, out var context))
            {
                context.UnregisterModule(moduleTitle);

                // 如果没有模块在使用，清理上下文
                if (context.ModuleCount == 0)
                {
                    _deviceContexts.TryRemove(deviceKey, out _);
                    context.Dispose();
                    LogService.Log($"设备 '{deviceKey}' 的数据保存管理器已清理");
                }
            }
        }

        /// <summary>
        /// 更新设备保存状态
        /// </summary>
        public void UpdateDeviceSaveStatus(string deviceKey, bool shouldSave, string initiatorModule = null)
        {
            if (_deviceContexts.TryGetValue(deviceKey, out var context))
            {
                context.UpdateSaveStatus(shouldSave, initiatorModule);
            }
        }

        /// <summary>
        /// 保存实时数据
        /// </summary>
        public void SaveRealTimeData(string deviceKey, CANManager.ZCAN_Receive_Data frame, Dictionary<string, double> signalValues)
        {
            if (_deviceContexts.TryGetValue(deviceKey, out var context) && context.IsRealTimeSaving)
            {
                context.QueueRealTimeData(frame, signalValues);
            }
        }

        /// <summary>
        /// 保存启动数据
        /// </summary>
        public void SaveStartupData(string deviceKey, CANManager.ZCAN_Receive_Data frame, Dictionary<string, double> signalValues)
        {
            if (_deviceContexts.TryGetValue(deviceKey, out var context) && context.IsStartupSaving)
            {
                context.QueueStartupData(frame, signalValues);
            }
        }

        /// <summary>
        /// 开始启动数据保存
        /// </summary>
        public void StartStartupSave(string deviceKey, string filePath = null)
        {
            if (_deviceContexts.TryGetValue(deviceKey, out var context))
            {
                context.StartStartupSave(filePath);
            }
        }

        /// <summary>
        /// 停止启动数据保存
        /// </summary>
        public void StopStartupSave(string deviceKey)
        {
            if (_deviceContexts.TryGetValue(deviceKey, out var context))
            {
                context.StopStartupSave();
            }
        }

        /// <summary>
        /// 获取设备保存状态
        /// </summary>
        public bool GetDeviceSaveStatus(string deviceKey)
        {
            return _deviceContexts.TryGetValue(deviceKey, out var context) && context.IsRealTimeSaving;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 启动定期清理任务
        /// </summary>
        private void StartPeriodicCleanup()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        CheckAndCleanBaseFolder();
                        await Task.Delay(TimeSpan.FromMinutes(30)); // 每30分钟检查一次
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"定期清理任务出错: {ex.Message}");
                        await Task.Delay(TimeSpan.FromMinutes(5));
                    }
                }
            });
        }

        /// <summary>
        /// 检查并清理基础文件夹
        /// </summary>
        private void CheckAndCleanBaseFolder()
        {
            try
            {
                long currentSize = CalculateFolderSize(_baseSaveDirectory);
                if (currentSize > MAX_BASE_FOLDER_SIZE)
                {
                    LogService.Log("RealTimeData文件夹超过2000MB，开始清理...");
                    CleanOldFiles(_baseSaveDirectory);
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"检查基础文件夹大小时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 计算文件夹大小
        /// </summary>
        private long CalculateFolderSize(string folderPath)
        {
            long size = 0;
            try
            {
                if (!Directory.Exists(folderPath)) return 0;

                foreach (string file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        FileInfo fileInfo = new FileInfo(file);
                        size += fileInfo.Length;
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"计算文件大小失败 {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"计算文件夹大小失败 {folderPath}: {ex.Message}");
            }
            return size;
        }

        /// <summary>
        /// 清理旧文件
        /// </summary>
        private void CleanOldFiles(string folderPath)
        {
            try
            {
                var allFiles = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
                                       .Select(f => new FileInfo(f))
                                       .OrderBy(f => f.CreationTime)
                                       .ToList();

                long currentSize = CalculateFolderSize(folderPath);
                int filesDeleted = 0;

                foreach (var file in allFiles)
                {
                    if (currentSize <= MAX_BASE_FOLDER_SIZE * 0.5)
                        break;

                    try
                    {
                        long fileSize = file.Length;
                        file.Delete();
                        currentSize -= fileSize;
                        filesDeleted++;
                        LogService.Log($"删除旧文件: {file.FullName}, 大小: {fileSize / 1024 / 1024}MB");
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"删除文件失败 {file.FullName}: {ex.Message}");
                    }
                }

                LogService.Log($"清理完成，删除了 {filesDeleted} 个文件，当前文件夹大小: {currentSize / 1024 / 1024}MB");
            }
            catch (Exception ex)
            {
                LogService.Log($"清理旧文件时出错: {ex.Message}");
            }
        }

        #endregion

        #region 清理资源

        public void Dispose()
        {
            foreach (var context in _deviceContexts.Values)
            {
                context.Dispose();
            }
            _deviceContexts.Clear();
        }

        #endregion
    }

    /// <summary>
    /// 设备保存上下文 - 管理单个设备的数据保存
    /// </summary>
    public class DeviceSaveContext : IDisposable
    {
        #region 字段声明

        private readonly string _deviceKey;
        private readonly ConcurrentDictionary<string, Action<bool>> _modules = new ConcurrentDictionary<string, Action<bool>>();

        // 实时数据保存
        private readonly ConcurrentQueue<DataItem> _realtimeQueue = new ConcurrentQueue<DataItem>();
        private readonly AutoResetEvent _realtimeDataEvent = new AutoResetEvent(false);
        private CancellationTokenSource _realtimeCancellationTokenSource;
        private Task _realtimeProcessingTask;
        private StreamWriter _realtimeFileWriter;
        private bool _isRealTimeSaving = false;
        private string _currentRealtimeFilePath;
        private long _currentRealtimeFileSize = 0;
        private bool _realtimeHeaderWritten = false;

        // 启动数据保存
        private readonly ConcurrentQueue<DataItem> _startupQueue = new ConcurrentQueue<DataItem>();
        private readonly AutoResetEvent _startupDataEvent = new AutoResetEvent(false);
        private CancellationTokenSource _startupCancellationTokenSource;
        private Task _startupProcessingTask;
        private StreamWriter _startupFileWriter;
        private bool _isStartupSaving = false;
        private string _currentStartupFilePath;
        private DateTime _startupStartTime;

        // 设备信息（用于构建数据行）
        private string _deviceName;
        private string _deviceIP;

        // 锁对象
        private readonly object _fileLock = new object();

        #endregion

        #region 属性

        public string DeviceKey => _deviceKey;
        public int ModuleCount => _modules.Count;
        public bool IsRealTimeSaving => _isRealTimeSaving;
        public bool IsStartupSaving => _isStartupSaving;

        #endregion

        #region 构造函数

        public DeviceSaveContext(string deviceKey)
        {
            _deviceKey = deviceKey;

            // 从设备键中解析设备名称和IP
            ParseDeviceInfoFromKey(deviceKey);

            StartProcessingTasks();
        }

        /// <summary>
        /// 从设备键解析设备名称和IP
        /// 格式: {DeviceName}_{DeviceIP}_{DeviceIndex}
        /// </summary>
        private void ParseDeviceInfoFromKey(string deviceKey)
        {
            try
            {
                var parts = deviceKey.Split('_');
                if (parts.Length >= 2)
                {
                    _deviceName = parts[0];
                    _deviceIP = parts[1];
                }
                else
                {
                    _deviceName = deviceKey;
                    _deviceIP = "Unknown";
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"解析设备信息失败: {ex.Message}");
                _deviceName = deviceKey;
                _deviceIP = "Unknown";
            }
        }

        #endregion

        #region 模块管理

        public void RegisterModule(string moduleTitle, Action<bool> saveStatusChangedCallback)
        {
            _modules[moduleTitle] = saveStatusChangedCallback;
        }

        public void UnregisterModule(string moduleTitle)
        {
            _modules.TryRemove(moduleTitle, out _);
        }

        public void UpdateSaveStatus(bool shouldSave, string initiatorModule = null)
        {
            if (_isRealTimeSaving == shouldSave) return;

            _isRealTimeSaving = shouldSave;

            if (shouldSave)
            {
                StartRealTimeSave();
            }
            else
            {
                StopRealTimeSave();
            }

            // 通知所有模块更新状态
            foreach (var callback in _modules.Values)
            {
                try
                {
                    callback(shouldSave);
                }
                catch (Exception ex)
                {
                    LogService.Log($"通知模块保存状态变化时出错: {ex.Message}");
                }
            }
        }

        #endregion

        #region 数据处理

        public void QueueRealTimeData(CANManager.ZCAN_Receive_Data frame, Dictionary<string, double> signalValues)
        {
            var dataItem = new DataItem
            {
                Frame = frame,
                SignalValues = new Dictionary<string, double>(signalValues),
                Timestamp = DateTime.Now
            };

            _realtimeQueue.Enqueue(dataItem);
            _realtimeDataEvent.Set();
        }

        public void QueueStartupData(CANManager.ZCAN_Receive_Data frame, Dictionary<string, double> signalValues)
        {
            var dataItem = new DataItem
            {
                Frame = frame,
                SignalValues = new Dictionary<string, double>(signalValues),
                Timestamp = DateTime.Now
            };

            _startupQueue.Enqueue(dataItem);
            _startupDataEvent.Set();
        }

        #endregion

        #region 实时数据保存

        private void StartRealTimeSave()
        {
            try
            {
                lock (_fileLock)
                {
                    if (_isRealTimeSaving && _realtimeFileWriter == null)
                    {
                        // 生成文件名
                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string fileName = $"实时数据_{_deviceKey}_{timestamp}.csv";
                        _currentRealtimeFilePath = Path.Combine(DeviceDataSaveManager._baseSaveDirectory, fileName);

                        // 创建文件写入器
                        _realtimeFileWriter = new StreamWriter(_currentRealtimeFilePath, true, System.Text.Encoding.UTF8)
                        {
                            AutoFlush = true
                        };

                        _realtimeHeaderWritten = false;
                        _currentRealtimeFileSize = 0;

                        LogService.Log($"开始实时数据保存: {_currentRealtimeFilePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"开始实时数据保存失败: {ex.Message}");
                _isRealTimeSaving = false;
            }
        }

        private void StopRealTimeSave()
        {
            lock (_fileLock)
            {
                _isRealTimeSaving = false;

                if (_realtimeFileWriter != null)
                {
                    try
                    {
                        _realtimeFileWriter.Close();
                        _realtimeFileWriter.Dispose();
                        _realtimeFileWriter = null;

                        LogService.Log($"停止实时数据保存: {_currentRealtimeFilePath}, 最终大小: {_currentRealtimeFileSize / 1024 / 1024}MB");
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"关闭实时数据文件写入器时出错: {ex.Message}");
                    }
                }

                _realtimeHeaderWritten = false;
                _currentRealtimeFileSize = 0;
                _currentRealtimeFilePath = null;
            }
        }

        #endregion

        #region 启动数据保存

        public void StartStartupSave(string filePath = null)
        {
            lock (_fileLock)
            {
                if (_isStartupSaving)
                {
                    StopStartupSave();
                }

                if (string.IsNullOrEmpty(filePath))
                {
                    // 生成默认文件名
                    string dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
                    string dateSaveDirectory = Path.Combine(DeviceDataSaveManager._baseSaveDirectory, dateFolder);
                    if (!Directory.Exists(dateSaveDirectory))
                    {
                        Directory.CreateDirectory(dateSaveDirectory);
                    }

                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string fileName = $"启动数据_{_deviceKey}_{timestamp}.csv";
                    _currentStartupFilePath = Path.Combine(dateSaveDirectory, fileName);
                }
                else
                {
                    _currentStartupFilePath = filePath;
                }

                // 创建文件写入器
                _startupFileWriter = new StreamWriter(_currentStartupFilePath, true, System.Text.Encoding.UTF8)
                {
                    AutoFlush = true
                };

                // 写入表头
                WriteStartupDataHeader();

                _isStartupSaving = true;
                _startupStartTime = DateTime.Now;

                LogService.Log($"启动数据保存开始: {_currentStartupFilePath}");
            }
        }

        public void StopStartupSave()
        {
            lock (_fileLock)
            {
                _isStartupSaving = false;

                if (_startupFileWriter != null)
                {
                    try
                    {
                        _startupFileWriter.Close();
                        _startupFileWriter.Dispose();
                        _startupFileWriter = null;

                        TimeSpan saveDuration = DateTime.Now - _startupStartTime;
                        LogService.Log($"启动数据保存结束: {_currentStartupFilePath}, 持续时间: {saveDuration.TotalSeconds:F1}秒");
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"关闭启动数据文件写入器时出错: {ex.Message}");
                    }
                }

                _currentStartupFilePath = null;
            }
        }

        private void WriteStartupDataHeader()
        {
            try
            {
                string header = "时间戳,运行时间(秒),设备名称,通道号,CANID,帧类型,帧格式,长度,数据,数据解析";
                _startupFileWriter.WriteLine(header);
                _startupFileWriter.Flush();
            }
            catch (Exception ex)
            {
                LogService.Log($"写入启动数据表头失败: {ex.Message}");
            }
        }

        #endregion

        #region 数据处理辅助方法

        /// <summary>
        /// 写入实时数据表头
        /// </summary>
        private void WriteRealTimeDataHeader()
        {
            try
            {
                string header = "时间戳,设备名称,通道号,CANID,帧类型,帧格式,CAN类型,长度,数据,数据解析";
                _realtimeFileWriter.WriteLine(header);
                _realtimeFileWriter.Flush();
                _realtimeHeaderWritten = true;

                // 更新文件大小
                _currentRealtimeFileSize += System.Text.Encoding.UTF8.GetByteCount(header + Environment.NewLine);

                LogService.Log($"实时数据表头已写入: {_deviceKey}");
            }
            catch (Exception ex)
            {
                LogService.Log($"写入实时数据表头失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 构建实时数据行
        /// </summary>
        private string BuildRealTimeDataLine(DataItem dataItem)
        {
            try
            {
                // 获取时间戳
                string timestamp = dataItem.Timestamp.ToString("yyyy-MM-dd HH:mm:ss fff");

                // 根据CAN ID获取通道号
                string channelNumber = GetChannelNumberByCanId(dataItem.Frame.can_id & 0x1FFFFFFF);

                // 提取CAN帧的详细信息
                uint canId = dataItem.Frame.can_id & 0x1FFFFFFF; // 去除扩展位
                string frameType = GetFrameType(dataItem.Frame.can_id);
                string frameFormat = GetFrameFormat(dataItem.Frame.can_id);
                int dataLength = dataItem.Frame.can_dlc;
                string dataHex = BitConverter.ToString(dataItem.Frame.data.Take(dataLength).ToArray()).Replace("-", " ");
                string analyzedata = BuildSignalAnalysisString(dataItem.SignalValues);

                // 构建数据行
                var dataRow = new List<string>
                {
                    timestamp,                  // 时间戳
                    _deviceName,                // 设备名称
                    channelNumber,              // 通道号
                    $"0x{canId:X}",             // CANID
                    frameType,                  // 帧类型
                    frameFormat,                // 帧格式
                    "CAN",                      // CAN类型
                    dataLength.ToString(),      // 长度
                    dataHex,                    // 数据
                    analyzedata                 // 数据解析
                };

                return string.Join(",", dataRow);
            }
            catch (Exception ex)
            {
                LogService.Log($"构建实时数据行失败: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 构建启动数据行
        /// </summary>
        private string BuildStartupDataLine(DataItem dataItem)
        {
            try
            {
                // 计算运行时间
                TimeSpan runTime = dataItem.Timestamp - _startupStartTime;
                string runTimeStr = runTime.TotalSeconds.ToString("F3");

                // 获取时间戳
                string timestamp = dataItem.Timestamp.ToString("yyyy-MM-dd HH:mm:ss fff");

                // 根据CAN ID获取通道号
                string channelNumber = GetChannelNumberByCanId(dataItem.Frame.can_id & 0x1FFFFFFF);

                // 提取CAN帧的详细信息
                uint canId = dataItem.Frame.can_id & 0x1FFFFFFF;
                string frameType = GetFrameType(dataItem.Frame.can_id);
                string frameFormat = GetFrameFormat(dataItem.Frame.can_id);
                int dataLength = dataItem.Frame.can_dlc;
                string dataHex = BitConverter.ToString(dataItem.Frame.data.Take(dataLength).ToArray()).Replace("-", " ");
                string analyzedata = BuildSignalAnalysisString(dataItem.SignalValues);

                // 构建数据行
                var dataRow = new List<string>
                {
                    timestamp,                  // 时间戳
                    runTimeStr,                 // 运行时间(秒)
                    _deviceName,                // 设备名称
                    channelNumber,              // 通道号
                    $"0x{canId:X}",             // CANID
                    frameType,                  // 帧类型
                    frameFormat,                // 帧格式
                    dataLength.ToString(),      // 长度
                    dataHex,                    // 数据
                    analyzedata                 // 数据解析
                };

                return string.Join(",", dataRow);
            }
            catch (Exception ex)
            {
                LogService.Log($"构建启动数据行失败: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 构建信号分析字符串
        /// </summary>
        private string BuildSignalAnalysisString(Dictionary<string, double> signalValues)
        {
            if (signalValues == null || signalValues.Count == 0)
                return string.Empty;

            return string.Join("; ", signalValues.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        /// <summary>
        /// 获取帧类型
        /// </summary>
        private string GetFrameType(uint canId)
        {
            if ((canId & 0x80000000) != 0)
                return "扩展帧";
            else
                return "标准帧";
        }

        /// <summary>
        /// 获取帧格式
        /// </summary>
        private string GetFrameFormat(uint canId)
        {
            if ((canId & 0x40000000) != 0)
                return "远程帧";
            else
                return "数据帧";
        }

        /// <summary>
        /// 根据CAN ID获取通道号
        /// CAN ID最后两位:
        ///   A0 -> AC1, A1 -> AC2
        ///   20 -> DC1, 21 -> DC2
        /// </summary>
        private string GetChannelNumberByCanId(uint canId)
        {
            try
            {
                // 获取CAN ID的最后两位（十六进制）
                byte lastByte = (byte)(canId & 0xFF);
                string lastTwoHex = lastByte.ToString("X2");

                switch (lastTwoHex)
                {
                    case "A0": return "AC1";
                    case "A1": return "AC2";
                    case "20": return "DC1";
                    case "21": return "DC2";
                    default:
                        // 如果不在预定义列表中，尝试从CAN ID推断
                        if (lastByte >= 0xA0 && lastByte <= 0xAF)
                            return $"AC{lastByte - 0xA0 + 1}";
                        else if (lastByte >= 0x20 && lastByte <= 0x2F)
                            return $"DC{lastByte - 0x20 + 1}";
                        else
                            return $"Unknown_{lastTwoHex}";
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"根据CAN ID获取通道号失败: {ex.Message}, CAN ID: 0x{canId:X}");
                return "Unknown";
            }
        }

        /// <summary>
        /// 轮转实时数据文件
        /// </summary>
        private void RotateRealtimeFile()
        {
            lock (_fileLock)
            {
                try
                {
                    if (_realtimeFileWriter == null) return;

                    // 记录旧文件信息
                    string oldFilePath = _currentRealtimeFilePath;
                    long oldFileSize = _currentRealtimeFileSize;

                    // 生成新文件名
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string newFileName = $"实时数据_{_deviceKey}_{timestamp}.csv";
                    string newFilePath = Path.Combine(DeviceDataSaveManager._baseSaveDirectory, newFileName);

                    // 先创建新文件写入器
                    var newWriter = new StreamWriter(newFilePath, true, System.Text.Encoding.UTF8)
                    {
                        AutoFlush = true
                    };

                    // 更新字典中的写入器引用
                    _realtimeFileWriter = newWriter;

                    // 重置表头写入状态
                    _realtimeHeaderWritten = false;

                    // 更新当前文件信息
                    _currentRealtimeFilePath = newFilePath;
                    _currentRealtimeFileSize = 0;

                    LogService.Log($"实时数据文件轮转完成: 旧文件({oldFileSize / 1024 / 1024}MB) -> 新文件: {newFilePath}");

                    // 尝试删除旧文件（如果存在且不是当前正在写入的文件）
                    Task.Run(() =>
                    {
                        try
                        {
                            if (File.Exists(oldFilePath) && oldFilePath != newFilePath)
                            {
                                File.Delete(oldFilePath);
                                LogService.Log($"删除旧实时数据文件: {oldFilePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"删除旧实时数据文件失败 {oldFilePath}: {ex.Message}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogService.Log($"轮转实时数据文件失败: {ex.Message}");
                    StopRealTimeSave();
                }
            }
        }

        #endregion

        #region 处理任务

        private void StartProcessingTasks()
        {
            // 启动实时数据处理任务
            _realtimeCancellationTokenSource = new CancellationTokenSource();
            _realtimeProcessingTask = Task.Run(() => ProcessRealTimeData(_realtimeCancellationTokenSource.Token));

            // 启动启动数据处理任务
            _startupCancellationTokenSource = new CancellationTokenSource();
            _startupProcessingTask = Task.Run(() => ProcessStartupData(_startupCancellationTokenSource.Token));

            LogService.Log($"设备 '{_deviceKey}' 的数据处理任务已启动");
        }

        private void ProcessRealTimeData(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_realtimeDataEvent.WaitOne(1000))
                    {
                        while (_realtimeQueue.TryDequeue(out var dataItem))
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            SaveRealTimeDataToFile(dataItem);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogService.Log($"处理实时数据队列时出错: {ex.Message}");
                    Thread.Sleep(100);
                }
            }

            // 处理剩余数据
            LogService.Log($"实时数据处理任务结束，处理剩余数据: {_realtimeQueue.Count} 条");
            while (_realtimeQueue.TryDequeue(out var dataItem))
            {
                SaveRealTimeDataToFile(dataItem);
            }
        }

        private void ProcessStartupData(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_startupDataEvent.WaitOne(1000))
                    {
                        while (_startupQueue.TryDequeue(out var dataItem))
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            SaveStartupDataToFile(dataItem);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogService.Log($"处理启动数据队列时出错: {ex.Message}");
                    Thread.Sleep(100);
                }
            }

            // 处理剩余数据
            LogService.Log($"启动数据处理任务结束，处理剩余数据: {_startupQueue.Count} 条");
            while (_startupQueue.TryDequeue(out var dataItem))
            {
                SaveStartupDataToFile(dataItem);
            }
        }

        private void SaveRealTimeDataToFile(DataItem dataItem)
        {
            if (!_isRealTimeSaving || _realtimeFileWriter == null) return;

            lock (_fileLock)
            {
                try
                {
                    // 检查文件大小，如果超过100MB则轮转文件
                    if (_currentRealtimeFileSize > DeviceDataSaveManager.MAX_REALTIME_FILE_SIZE)
                    {
                        RotateRealtimeFile();
                    }

                    // 确保表头已写入
                    if (!_realtimeHeaderWritten)
                    {
                        WriteRealTimeDataHeader();
                    }

                    // 构建数据行并保存
                    string csvLine = BuildRealTimeDataLine(dataItem);
                    if (string.IsNullOrEmpty(csvLine)) return;

                    long lineLength = System.Text.Encoding.UTF8.GetByteCount(csvLine + Environment.NewLine);

                    if (_realtimeFileWriter != null && _realtimeFileWriter.BaseStream != null && _realtimeFileWriter.BaseStream.CanWrite)
                    {
                        _realtimeFileWriter.WriteLine(csvLine);
                        _realtimeFileWriter.Flush();
                        _currentRealtimeFileSize += lineLength;
                    }
                    else
                    {
                        LogService.Log("实时数据文件写入器状态异常，停止保存");
                        StopRealTimeSave();
                    }
                }
                catch (ObjectDisposedException ex)
                {
                    LogService.Log($"实时数据文件写入器已被释放: {ex.Message}");
                    StopRealTimeSave();
                }
                catch (Exception ex)
                {
                    LogService.Log($"保存实时数据到文件失败: {ex.Message}");
                }
            }
        }

        private void SaveStartupDataToFile(DataItem dataItem)
        {
            if (!_isStartupSaving || _startupFileWriter == null) return;

            lock (_fileLock)
            {
                try
                {
                    // 构建数据行并保存
                    string csvLine = BuildStartupDataLine(dataItem);
                    if (string.IsNullOrEmpty(csvLine)) return;

                    _startupFileWriter.WriteLine(csvLine);
                    _startupFileWriter.Flush();
                }
                catch (ObjectDisposedException ex)
                {
                    LogService.Log($"启动数据文件写入器已被释放: {ex.Message}");
                    StopStartupSave();
                }
                catch (Exception ex)
                {
                    LogService.Log($"保存启动数据到文件失败: {ex.Message}");
                }
            }
        }

        #endregion

        #region 清理资源

        public void Dispose()
        {
            // 停止实时数据保存
            StopRealTimeSave();

            // 停止启动数据保存
            StopStartupSave();

            // 停止处理任务
            _realtimeCancellationTokenSource?.Cancel();
            _startupCancellationTokenSource?.Cancel();

            _realtimeDataEvent?.Set();
            _startupDataEvent?.Set();

            try
            {
                _realtimeProcessingTask?.Wait(5000);
                _startupProcessingTask?.Wait(5000);
            }
            catch (Exception ex)
            {
                LogService.Log($"等待处理任务停止时出错: {ex.Message}");
            }

            _realtimeCancellationTokenSource?.Dispose();
            _startupCancellationTokenSource?.Dispose();
            _realtimeDataEvent?.Dispose();
            _startupDataEvent?.Dispose();

            LogService.Log($"设备保存上下文 '{_deviceKey}' 已释放");
        }

        #endregion
    }

    /// <summary>
    /// 数据项结构
    /// </summary>
    public struct DataItem
    {
        public CANManager.ZCAN_Receive_Data Frame;
        public Dictionary<string, double> SignalValues;
        public DateTime Timestamp;
    }
}