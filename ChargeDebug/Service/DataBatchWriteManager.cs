// DataWriteManager.cs
using System.Collections.Concurrent;
using System.Data.SQLite;
using System.Data;
using DataModel;
using Log;
using System.IO;
using ClosedXML.Excel;

#pragma warning disable
namespace ChargeDebug.Service
{
    public class DataBatchWriteManager : IDisposable
    {
        private readonly string _connectionString;
        private readonly ConcurrentQueue<DataBatchItem> _dataQueue;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly SemaphoreSlim _queueSemaphore;
        private readonly int _batchSize = 1000;
        private Task _writeTask;
        private volatile bool _isRunning;
        private volatile bool _isStopping;
        private int _totalProcessedCount = 0;
        private readonly object _startStopLock = new object();

        // 记录类型映射字典
        private readonly Dictionary<string, Type> _recordTypeMapping;

        // Sign管理：文件ID -> 当前Sign值
        private readonly ConcurrentDictionary<int, int> _fileSignMapping;

        // 事件
        public event EventHandler<BatchWriteEventArgs> BatchWritten;
        public event EventHandler<string> WriteCompleted;
        public event EventHandler<string> WriteError;

        public DataBatchWriteManager(string connectionString, int batchSize = 100)
        {
            _connectionString = connectionString;
            _batchSize = batchSize;
            _dataQueue = new ConcurrentQueue<DataBatchItem>();
            _cancellationTokenSource = new CancellationTokenSource();
            _queueSemaphore = new SemaphoreSlim(0);
            _isRunning = false;
            _isStopping = false;
            _fileSignMapping = new ConcurrentDictionary<int, int>();

            // 初始化记录类型映射
            _recordTypeMapping = new Dictionary<string, Type>
            {
                { "Cal7", typeof(Cal7Record) },
                { "Cal5", typeof(Cal5Record) },
                { "Cal6", typeof(Cal6Record) },
                { "Cal2", typeof(Cal2Record) },
                { "Cal1", typeof(Cal1Record) },
                { "Cal3", typeof(Cal3Record) },
                { "Cal4", typeof(Cal4Record) },
                { "None", typeof(NoneRecord) }
            };
        }

        /// <summary>
        /// 启动写入服务
        /// </summary>
        public void StartIfNeeded()
        {
            lock (_startStopLock)
            {
                if (_isRunning || _isStopping) return;

                _isRunning = true;
                _isStopping = false;
                _writeTask = Task.Run(() => ProcessWriteQueueAsync(),
                    _cancellationTokenSource.Token);

                LogService.Log("数据批量写入服务已启动");
            }
        }

        /// <summary>
        /// 停止写入服务
        /// </summary>
        public async Task StopAsync()
        {
            lock (_startStopLock)
            {
                _isRunning = false;
                _isStopping = false;
            }
            LogService.Log("数据批量写入服务已停止");
        }

        /// <summary>
        /// 添加数据到写入队列
        /// </summary>
        public void EnqueueData(int fileId, Dictionary<string, List<object>> recordsByType, bool autoDetectSignGroups)
        {
            if (recordsByType == null || recordsByType.Count == 0) return;

            var batchItem = new DataBatchItem
            {
                FileId = fileId,
                RecordsByType = recordsByType,
                AutoDetectSignGroups = autoDetectSignGroups,
                EnqueueTime = DateTime.Now
            };

            _dataQueue.Enqueue(batchItem);
            _queueSemaphore.Release();

            // 如果有数据，确保服务正在运行
            StartIfNeeded();

            LogService.Log($"已添加文件 {fileId} 的数据到写入队列，包含 {recordsByType.Count} 种记录类型");
        }

        /// <summary>
        /// 处理写入队列（优化版本）
        /// </summary>
        private async Task ProcessWriteQueueAsync()
        {
            LogService.Log("写入队列处理任务开始");

            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        // 等待有数据可处理（使用CancellationToken）
                        await _queueSemaphore.WaitAsync(_cancellationTokenSource.Token);

                        // 处理队列中的所有批次
                        bool processedData = false;
                        while (_dataQueue.TryDequeue(out DataBatchItem batchItem))
                        {
                            await ProcessBatchAsync(batchItem);
                            processedData = true;

                            // 每次处理后检查取消请求
                            if (_cancellationTokenSource.Token.IsCancellationRequested)
                                break;
                        }

                        // 如果处理了数据且队列为空，检查是否需要停止
                        if (processedData && _dataQueue.IsEmpty)
                        {
                            // 检查取消令牌是否已请求取消
                            if (_cancellationTokenSource.Token.IsCancellationRequested)
                                break;

                            // 可以添加空闲检查，如果连续多次空闲则停止
                            await CheckAndStopIfIdle();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常取消，退出循环
                        break;
                    }
                    catch (Exception ex)
                    {
                        OnWriteError($"写入队列处理错误: {ex.Message}");

                        // 短暂等待后继续，除非取消令牌被触发
                        try
                        {
                            await Task.Delay(2000, _cancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"写入任务异常终止: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查并停止空闲的服务
        /// </summary>
        private async Task CheckAndStopIfIdle()
        {
            // 如果队列为空且没有正在处理的批次，则停止服务
            if (_dataQueue.IsEmpty && !_isStopping)
            {
                LogService.Log("队列为空，准备停止写入服务");
                OnWriteCompleted("所有数据已写入完成");
                await StopAsync();
            }
        }

        /// <summary>
        /// 处理一个批次的数据
        /// </summary>
        private async Task ProcessBatchAsync(DataBatchItem batchItem)
        {
            try
            {
                if (batchItem.RecordsByType == null || batchItem.RecordsByType.Count == 0) return;

                int totalInserted = 0;

                using (var connection = new SQLiteConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // 按记录类型处理
                    foreach (var kvp in batchItem.RecordsByType)
                    {
                        var recordTypeName = kvp.Key;
                        var records = kvp.Value;

                        if (records.Count == 0) continue;

                        // 获取记录类型
                        if (!_recordTypeMapping.TryGetValue(recordTypeName, out Type recordType))
                        {
                            LogService.Log($"警告：未知的记录类型 {recordTypeName}");
                            continue;
                        }

                        // 分批插入（每_batchSize条一个批次）
                        for (int i = 0; i < records.Count; i += _batchSize)
                        {
                            var batchRecords = records.Skip(i).Take(_batchSize).ToList();
                            await InsertRecordsBatchAsync(connection, recordType, batchItem.FileId, batchRecords);

                            totalInserted += batchRecords.Count;
                            _totalProcessedCount += batchRecords.Count;

                            // 触发批次写入事件
                            BatchWritten?.Invoke(this, new BatchWriteEventArgs
                            {
                                FileId = batchItem.FileId,
                                RecordType = recordTypeName,
                                Count = batchRecords.Count,
                                Timestamp = DateTime.Now
                            });
                        }
                    }

                    // 更新文件记录数
                    await UpdateFileRecordCountAsync(connection, batchItem.FileId, totalInserted);
                }

                LogService.Log($"已处理文件 {batchItem.FileId} 的 {totalInserted} 条记录");
            }
            catch (Exception ex)
            {
                LogService.Log($"处理批次数据失败: {ex.Message}");
                throw new Exception($"处理批次数据失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 处理剩余数据
        /// </summary>
        private async Task ProcessRemainingDataAsync()
        {
            int remainingCount = 0;
            while (_dataQueue.TryDequeue(out DataBatchItem remainingItem))
            {
                try
                {
                    await ProcessBatchAsync(remainingItem);
                    remainingCount++;
                }
                catch (Exception ex)
                {
                    OnWriteError($"处理剩余数据失败: {ex.Message}");
                }
            }

            if (remainingCount > 0)
            {
                LogService.Log($"处理了 {remainingCount} 批剩余数据");
            }
        }

        /// <summary>
        /// 检查TargetTable表中是否有指定文件ID的数据
        /// </summary>
        public async Task<bool> CheckTargetTableHasDataAsync(int fileId)
        {
            try
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string sql = "SELECT COUNT(1) FROM TargetTable WHERE FileID = @FileID";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@FileID", fileId);
                        var result = await cmd.ExecuteScalarAsync();
                        return Convert.ToInt32(result) > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"检查TargetTable数据失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将读取的数据写入TargetTable，不进行合并
        /// </summary>
        /// <param name="fileId">文件ID</param>
        /// <returns>写入的记录数</returns>
        public async Task<int> WriteToTargetTableAsync(int fileId)
        {
            int insertedCount = 0;

            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // 开始事务
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 按Sign分组获取所有记录类型的数据
                            var signGroups = await GetDataGroupedBySignAsync(connection, fileId);

                            // 准备批量插入
                            var insertSql = GetBatchInsertSql();
                            using (var cmd = new SQLiteCommand(insertSql, connection, transaction))
                            {
                                // 添加参数
                                AddBatchInsertParameters(cmd);

                                // 处理每个Sign分组
                                foreach (var signGroup in signGroups)
                                {
                                    var sign = signGroup.Key;

                                    // 跳过Sign为0或null的分组
                                    if (sign == 0) continue;

                                    // 提取并处理该Sign分组的数据
                                    var recordData = await ProcessSignGroupForBatchAsync(signGroup.Value);

                                    if (recordData != null)
                                    {
                                        // 设置参数值
                                        SetBatchInsertParameterValues(cmd, fileId, recordData);

                                        // 执行插入
                                        await cmd.ExecuteNonQueryAsync();
                                        insertedCount++;
                                    }
                                }
                            }

                            // 提交事务
                            transaction.Commit();
                            LogService.Log($"成功将 {insertedCount} 条记录批量写入TargetTable");
                        }
                        catch
                        {
                            // 回滚事务
                            transaction.Rollback();
                            throw;
                        }
                    }
                }

                return insertedCount;
            }
            catch (Exception ex)
            {
                LogService.Log($"写入TargetTable失败: {ex.Message}");
                throw new Exception($"写入TargetTable失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取批量插入的SQL语句
        /// </summary>
        private string GetBatchInsertSql()
        {
            return @"
                INSERT INTO TargetTable 
                (Time, AC_TotalPower, DC_TotalPower, DC1_Power, DC2_Power,
                 DC3_Power, DC4_Power, SOC1, SOC2, SOC3, SOC4, FileID)
                VALUES 
                (@Time, @AC_TotalPower, @DC_TotalPower, @DC1_Power, @DC2_Power,
                 @DC3_Power, @DC4_Power, @SOC1, @SOC2, @SOC3, @SOC4, @FileID)";
        }

        /// <summary>
        /// 添加批量插入的参数
        /// </summary>
        private void AddBatchInsertParameters(SQLiteCommand cmd)
        {
            cmd.Parameters.Add("@Time", DbType.DateTime);
            cmd.Parameters.Add("@AC_TotalPower", DbType.Double);
            cmd.Parameters.Add("@DC_TotalPower", DbType.Double);
            cmd.Parameters.Add("@FileID", DbType.Int32);

            // 通道1-4的Power
            cmd.Parameters.Add("@DC1_Power", DbType.Double);
            cmd.Parameters.Add("@DC2_Power", DbType.Double);
            cmd.Parameters.Add("@DC3_Power", DbType.Double);
            cmd.Parameters.Add("@DC4_Power", DbType.Double);

            // 通道1-4的SOC
            cmd.Parameters.Add("@SOC1", DbType.Double);
            cmd.Parameters.Add("@SOC2", DbType.Double);
            cmd.Parameters.Add("@SOC3", DbType.Double);
            cmd.Parameters.Add("@SOC4", DbType.Double);
        }

        /// <summary>
        /// 设置批量插入的参数值
        /// </summary>
        private void SetBatchInsertParameterValues(
            SQLiteCommand cmd,
            int fileId,
            TargetTableRecordData recordData)
        {
            cmd.Parameters["@Time"].Value = recordData.CreateTime ?? (object)DBNull.Value;
            cmd.Parameters["@AC_TotalPower"].Value = recordData.AC_TotalPower ?? (object)DBNull.Value;
            cmd.Parameters["@DC_TotalPower"].Value = recordData.DC_TotalPower ?? (object)DBNull.Value;
            cmd.Parameters["@FileID"].Value = fileId;

            // 通道1-4的Power
            cmd.Parameters["@DC1_Power"].Value = recordData.ChannelPowers[0] ?? (object)DBNull.Value;
            cmd.Parameters["@DC2_Power"].Value = recordData.ChannelPowers[1] ?? (object)DBNull.Value;
            cmd.Parameters["@DC3_Power"].Value = recordData.ChannelPowers[2] ?? (object)DBNull.Value;
            cmd.Parameters["@DC4_Power"].Value = recordData.ChannelPowers[3] ?? (object)DBNull.Value;

            // 通道1-4的SOC
            cmd.Parameters["@SOC1"].Value = recordData.ChannelSocs[0] ?? (object)DBNull.Value;
            cmd.Parameters["@SOC2"].Value = recordData.ChannelSocs[1] ?? (object)DBNull.Value;
            cmd.Parameters["@SOC3"].Value = recordData.ChannelSocs[2] ?? (object)DBNull.Value;
            cmd.Parameters["@SOC4"].Value = recordData.ChannelSocs[3] ?? (object)DBNull.Value;
        }

        /// <summary>
        /// 按Sign分组获取所有记录类型的数据
        /// </summary>
        private async Task<Dictionary<int, Dictionary<string, List<object>>>> GetDataGroupedBySignAsync(
            SQLiteConnection connection, int fileId)
        {
            var result = new Dictionary<int, Dictionary<string, List<object>>>();

            // 定义需要读取的记录类型
            var recordTypes = new[]
            {
                ("None", typeof(NoneRecord)),
                ("Cal7", typeof(Cal7Record)),
                ("Cal5", typeof(Cal5Record))
            };

            foreach (var (typeName, recordType) in recordTypes)
            {
                var tableName = $"RecordType_{typeName}";
                var records = await GetRecordsByTableAsync(connection, tableName, recordType, fileId);

                // 按Sign分组
                foreach (var record in records)
                {
                    var sign = GetSignValue(record) ?? 0;

                    if (!result.ContainsKey(sign))
                    {
                        result[sign] = new Dictionary<string, List<object>>();
                    }

                    if (!result[sign].ContainsKey(typeName))
                    {
                        result[sign][typeName] = new List<object>();
                    }

                    result[sign][typeName].Add(record);
                }
            }

            return result;
        }

        /// <summary>
        /// 处理单个Sign分组的数据（返回数据对象，不直接插入）
        /// </summary>
        private async Task<TargetTableRecordData> ProcessSignGroupForBatchAsync(
            Dictionary<string, List<object>> recordsByType)
        {
            var recordData = new TargetTableRecordData();

            // 提取None记录的CreateTime（取第一条）
            if (recordsByType.TryGetValue("None", out var noneRecords) && noneRecords.Count > 0)
            {
                var firstNoneRecord = noneRecords[0] as NoneRecord;
                recordData.CreateTime = firstNoneRecord?.CreateTime;
            }

            // 提取Cal7记录的Power（汇总所有记录的Power）
            if (recordsByType.TryGetValue("Cal7", out var cal7Records) && cal7Records.Count > 0)
            {
                recordData.AC_TotalPower = 0;
                foreach (Cal7Record record in cal7Records.Cast<Cal7Record>())
                {
                    if (record.Power.HasValue)
                    {
                        recordData.AC_TotalPower += record.Power.Value;
                    }
                }
            }

            // 处理Cal5记录的通道数据
            if (recordsByType.TryGetValue("Cal5", out var cal5Records) && cal5Records.Count > 0)
            {
                recordData.DC_TotalPower = 0;

                // 按通道分组处理
                var channelGroups = cal5Records
                    .Cast<Cal5Record>()
                    .GroupBy(r => r.ChannelNum ?? 0)
                    .Where(g => g.Key >= 1 && g.Key <= 4);

                foreach (var group in channelGroups)
                {
                    int channelIndex = group.Key - 1; // 转换为0-based索引

                    // 计算该通道的总Power
                    double? channelPower = null;
                    foreach (var record in group)
                    {
                        if (record.Power.HasValue)
                        {
                            channelPower = (channelPower ?? 0) + record.Power.Value;
                        }
                    }

                    // 计算该通道的平均SOC（如果有多个记录）
                    double? channelSoc = null;
                    if (group.Any(r => r.SOC.HasValue))
                    {
                        channelSoc = group
                            .Where(r => r.SOC.HasValue)
                            .Average(r => r.SOC.Value);
                    }

                    // 更新总功率
                    if (channelPower.HasValue)
                    {
                        recordData.DC_TotalPower += channelPower.Value;
                    }

                    // 存储通道数据
                    recordData.ChannelPowers[channelIndex] = channelPower;
                    recordData.ChannelSocs[channelIndex] = channelSoc;
                }
            }

            return recordData;
        }

        /// <summary>
        /// 从指定表读取记录
        /// </summary>
        private async Task<List<object>> GetRecordsByTableAsync(SQLiteConnection connection, string tableName, Type recordType, int fileId)
        {
            var records = new List<object>();

            string sql = $"SELECT * FROM {tableName} WHERE FileID = @FileID";

            using (var cmd = new SQLiteCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@FileID", fileId);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var record = ConvertDataRowToObject((SQLiteDataReader)reader, recordType);
                        if (record != null)
                        {
                            records.Add(record);
                        }
                    }
                }
            }

            return records;
        }

        /// <summary>
        /// 将DataReader行转换为对象
        /// </summary>
        private object ConvertDataRowToObject(SQLiteDataReader reader, Type recordType, HashSet<string> columnNames = null)
        {
            try
            {
                var record = Activator.CreateInstance(recordType);

                // 如果未提供列名集合，则创建一个
                if (columnNames == null)
                {
                    columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        columnNames.Add(reader.GetName(i));
                    }
                }

                // 遍历所有属性并赋值
                var properties = recordType.GetProperties();
                foreach (var property in properties)
                {
                    // 检查列是否存在（不区分大小写）
                    if (columnNames.Contains(property.Name))
                    {
                        try
                        {
                            var value = reader[property.Name];
                            if (value != DBNull.Value)
                            {
                                // 处理可为空类型
                                var propertyType = property.PropertyType;
                                var underlyingType = Nullable.GetUnderlyingType(propertyType);

                                if (underlyingType != null)
                                {
                                    property.SetValue(record, Convert.ChangeType(value, underlyingType));
                                }
                                else
                                {
                                    property.SetValue(record, Convert.ChangeType(value, propertyType));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"为属性 {property.Name} 赋值时发生错误: {ex.Message}");
                        }
                    }
                }

                return record;
            }
            catch (Exception ex)
            {
                LogService.Log($"转换数据行失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取记录的Sign值
        /// </summary>
        private int? GetSignValue(object record)
        {
            var property = record.GetType().GetProperty("Sign");
            if (property != null)
            {
                var value = property.GetValue(record);
                if (value != null && value != DBNull.Value)
                {
                    return Convert.ToInt32(value);
                }
            }
            return null;
        }

        /// <summary>
        /// 批量插入记录（更新版本，包含Sign）
        /// </summary>
        private async Task InsertRecordsBatchAsync(SQLiteConnection connection, Type recordType, int fileId, List<object> records)
        {
            if (records.Count == 0) return;

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    // 根据记录类型选择插入方法
                    switch (recordType.Name)
                    {
                        case nameof(Cal7Record):
                            await InsertCal7RecordsAsync(transaction, fileId, records.Cast<Cal7Record>().ToList());
                            break;
                        case nameof(Cal5Record):
                            await InsertCal5RecordsAsync(transaction, fileId, records.Cast<Cal5Record>().ToList());
                            break;
                        case nameof(Cal6Record):
                            await InsertCal6RecordsAsync(transaction, fileId, records.Cast<Cal6Record>().ToList());
                            break;
                        case nameof(Cal2Record):
                            await InsertCal2RecordsAsync(transaction, fileId, records.Cast<Cal2Record>().ToList());
                            break;
                        case nameof(Cal1Record):
                            await InsertCal1RecordsAsync(transaction, fileId, records.Cast<Cal1Record>().ToList());
                            break;
                        case nameof(Cal3Record):
                            await InsertCal3RecordsAsync(transaction, fileId, records.Cast<Cal3Record>().ToList());
                            break;
                        case nameof(Cal4Record):
                            await InsertCal4RecordsAsync(transaction, fileId, records.Cast<Cal4Record>().ToList());
                            break;
                        case nameof(NoneRecord):
                            await InsertNoneRecordsAsync(transaction, fileId, records.Cast<NoneRecord>().ToList());
                            break;
                        default:
                            throw new NotSupportedException($"不支持的记录类型: {recordType.Name}");
                    }

                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    LogService.Log($"批量插入失败: {ex.Message}");
                    throw;
                }
            }
        }

        // 批量插入方法 - 每种记录类型一个方法

        /// <summary>
        /// 插入文件信息
        /// </summary>
        public async Task<int> InsertFileInfo(string fileName, string filePath, long fileSize, DateTime? createTime = null)
        {
            try
            {
                int fileId = 0;

                using (var connection = new SQLiteConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string sql = @"
                        INSERT INTO FileInfo (FileName, FilePath, FileSize, CreateTime)
                        VALUES (@FileName, @FilePath, @FileSize, @CreateTime);
                        SELECT last_insert_rowid();";

                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@FileName", fileName);
                        cmd.Parameters.AddWithValue("@FilePath", filePath);
                        cmd.Parameters.AddWithValue("@FileSize", fileSize);
                        cmd.Parameters.AddWithValue("@CreateTime", createTime ?? (object)DBNull.Value);

                        fileId = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    return fileId;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"保存文件信息失败: {ex.Message}", ex);
            }
        }

        private async Task InsertCal7RecordsAsync(SQLiteTransaction transaction, int fileId, List<Cal7Record> records)
        {
            string sql = @"
                INSERT INTO RecordType_Cal7 
                (FileID, CreateTime, ChannelNum, Power, ChargeEnergy, DisChargeEnergy, DeviceStatus, KeyValue, Sign)
                VALUES 
                (@FileID, @CreateTime, @ChannelNum, @Power, @ChargeEnergy, @DisChargeEnergy, @DeviceStatus, @KeyValue, @Sign)";

            using (var cmd = new SQLiteCommand(sql, transaction.Connection, transaction))
            {
                cmd.Parameters.Add("@FileID", DbType.Int32);
                cmd.Parameters.Add("@CreateTime", DbType.DateTime);
                cmd.Parameters.Add("@ChannelNum", DbType.Int32);
                cmd.Parameters.Add("@Power", DbType.Double);
                cmd.Parameters.Add("@ChargeEnergy", DbType.Double);
                cmd.Parameters.Add("@DisChargeEnergy", DbType.Double);
                cmd.Parameters.Add("@DeviceStatus", DbType.String);
                cmd.Parameters.Add("@KeyValue", DbType.String);
                cmd.Parameters.Add("@Sign", DbType.Int32);

                foreach (var record in records)
                {
                    cmd.Parameters["@FileID"].Value = fileId;
                    cmd.Parameters["@CreateTime"].Value = record.CreateTime;
                    cmd.Parameters["@ChannelNum"].Value = record.ChannelNum;
                    cmd.Parameters["@Power"].Value = record.Power;
                    cmd.Parameters["@ChargeEnergy"].Value = record.ChargeEnergy ?? (object)DBNull.Value;
                    cmd.Parameters["@DisChargeEnergy"].Value = record.DischargeEnergy ?? (object)DBNull.Value;
                    cmd.Parameters["@DeviceStatus"].Value = record.DeviceStatus ?? (object)DBNull.Value;
                    cmd.Parameters["@KeyValue"].Value = record.KeyValue ?? (object)DBNull.Value;
                    cmd.Parameters["@Sign"].Value = record.Sign ?? (object)DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task InsertCal5RecordsAsync(SQLiteTransaction transaction, int fileId, List<Cal5Record> records)
        {
            string sql = @"
                INSERT INTO RecordType_Cal5 
                (FileID, CreateTime, ChannelNum, SOH, Current, Power, ChargeEnergy, Voltage, DeviceStatus, SOC, DisChargeEnergy, KeyValue, Sign)
                VALUES 
                (@FileID, @CreateTime, @ChannelNum, @SOH, @Current, @Power, @ChargeEnergy, @Voltage, @DeviceStatus, @SOC, @DisChargeEnergy, @KeyValue, @Sign)";

            using (var cmd = new SQLiteCommand(sql, transaction.Connection, transaction))
            {
                // 添加所有参数
                cmd.Parameters.Add("@FileID", DbType.Int32);
                cmd.Parameters.Add("@CreateTime", DbType.DateTime);
                cmd.Parameters.Add("@ChannelNum", DbType.Int32);
                cmd.Parameters.Add("@SOH", DbType.Double);
                cmd.Parameters.Add("@Current", DbType.Double);
                cmd.Parameters.Add("@Power", DbType.Double);
                cmd.Parameters.Add("@ChargeEnergy", DbType.Double);
                cmd.Parameters.Add("@Voltage", DbType.Double);
                cmd.Parameters.Add("@DeviceStatus", DbType.String);
                cmd.Parameters.Add("@SOC", DbType.Double);
                cmd.Parameters.Add("@DisChargeEnergy", DbType.Double);
                cmd.Parameters.Add("@KeyValue", DbType.String);
                cmd.Parameters.Add("@Sign", DbType.Int32);

                foreach (var record in records)
                {
                    cmd.Parameters["@FileID"].Value = fileId;
                    cmd.Parameters["@CreateTime"].Value = record.CreateTime;
                    cmd.Parameters["@ChannelNum"].Value = record.ChannelNum;
                    cmd.Parameters["@SOH"].Value = record.SOH ?? (object)DBNull.Value;
                    cmd.Parameters["@Current"].Value = record.Current ?? (object)DBNull.Value;
                    cmd.Parameters["@Power"].Value = record.Power ?? (object)DBNull.Value;
                    cmd.Parameters["@ChargeEnergy"].Value = record.ChargeEnergy ?? (object)DBNull.Value;
                    cmd.Parameters["@Voltage"].Value = record.Voltage ?? (object)DBNull.Value;
                    cmd.Parameters["@DeviceStatus"].Value = record.DeviceStatus ?? (object)DBNull.Value;
                    cmd.Parameters["@SOC"].Value = record.SOC ?? (object)DBNull.Value;
                    cmd.Parameters["@DisChargeEnergy"].Value = record.DischargeEnergy ?? (object)DBNull.Value;
                    cmd.Parameters["@KeyValue"].Value = record.KeyValue ?? (object)DBNull.Value;
                    cmd.Parameters["@Sign"].Value = record.Sign ?? (object)DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task InsertCal6RecordsAsync(SQLiteTransaction transaction, int fileId, List<Cal6Record> records)
        {
            string sql = @"
                INSERT INTO RecordType_Cal6 
                (FileID, CreateTime, CurrentDate, StartTime, EndTime, MaxFrequencyPower, 
                 Paclm5, Paclm6, Paclm1, Paclm2, SocMin, SocMax, KP, 
                 LimitChargeCurrent, LimitDischargeCurrent, Sign)
                VALUES 
                (@FileID, @CreateTime, @CurrentDate, @StartTime, @EndTime, @MaxFrequencyPower, 
                 @Paclm5, @Paclm6, @Paclm1, @Paclm2, @SocMin, @SocMax, @KP, 
                 @LimitChargeCurrent, @LimitDischargeCurrent, @Sign)";

            using (var cmd = new SQLiteCommand(sql, transaction.Connection, transaction))
            {
                // 添加所有参数
                cmd.Parameters.Add("@FileID", DbType.Int32);
                cmd.Parameters.Add("@CreateTime", DbType.DateTime);
                cmd.Parameters.Add("@CurrentDate", DbType.DateTime);
                cmd.Parameters.Add("@StartTime", DbType.DateTime);
                cmd.Parameters.Add("@EndTime", DbType.DateTime);
                cmd.Parameters.Add("@MaxFrequencyPower", DbType.Double);
                cmd.Parameters.Add("@Paclm5", DbType.Double);
                cmd.Parameters.Add("@Paclm6", DbType.Double);
                cmd.Parameters.Add("@Paclm1", DbType.Double);
                cmd.Parameters.Add("@Paclm2", DbType.Double);
                cmd.Parameters.Add("@SocMin", DbType.Double);
                cmd.Parameters.Add("@SocMax", DbType.Double);
                cmd.Parameters.Add("@KP", DbType.Double);
                cmd.Parameters.Add("@LimitChargeCurrent", DbType.Double);
                cmd.Parameters.Add("@LimitDischargeCurrent", DbType.Double);
                cmd.Parameters.Add("@Sign", DbType.Int32);

                foreach (var record in records)
                {
                    cmd.Parameters["@FileID"].Value = fileId;
                    cmd.Parameters["@CreateTime"].Value = record.CreateTime;
                    cmd.Parameters["@CurrentDate"].Value = record.CurrentDate;
                    cmd.Parameters["@StartTime"].Value = record.StartTime;
                    cmd.Parameters["@EndTime"].Value = record.EndTime;
                    cmd.Parameters["@MaxFrequencyPower"].Value = record.MaxFrequencyPower;
                    cmd.Parameters["@Paclm5"].Value = record.Paclm5;
                    cmd.Parameters["@Paclm6"].Value = record.Paclm6;
                    cmd.Parameters["@Paclm1"].Value = record.Paclm1;
                    cmd.Parameters["@Paclm2"].Value = record.Paclm2;
                    cmd.Parameters["@SocMin"].Value = record.SocMin;
                    cmd.Parameters["@SocMax"].Value = record.SocMax;
                    cmd.Parameters["@KP"].Value = record.KP;
                    cmd.Parameters["@LimitChargeCurrent"].Value = record.LimitChargeCurrent;
                    cmd.Parameters["@LimitDischargeCurrent"].Value = record.LimitDischargeCurrent;
                    cmd.Parameters["@Sign"].Value = record.Sign ?? (object)DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task InsertCal2RecordsAsync(SQLiteTransaction transaction, int fileId, List<Cal2Record> records)
        {
            string sql = @"
                INSERT INTO RecordType_Cal2 
                (FileID, CreateTime, Condition, Pac0, PreviewPAC, PacMin, PacMax, KP, 
                 MaxFrequencyPower, Pace0, Sign)
                VALUES 
                (@FileID, @CreateTime, @Condition, @Pac0, @PreviewPAC, @PacMin, @PacMax, @KP, 
                 @MaxFrequencyPower, @Pace0, @Sign)";

            using (var cmd = new SQLiteCommand(sql, transaction.Connection, transaction))
            {
                // 添加所有参数
                cmd.Parameters.Add("@FileID", DbType.Int32);
                cmd.Parameters.Add("@CreateTime", DbType.DateTime);
                cmd.Parameters.Add("@Condition", DbType.String);
                cmd.Parameters.Add("@Pac0", DbType.Double);
                cmd.Parameters.Add("@PreviewPAC", DbType.Double);
                cmd.Parameters.Add("@PacMin", DbType.Double);
                cmd.Parameters.Add("@PacMax", DbType.Double);
                cmd.Parameters.Add("@KP", DbType.Double);
                cmd.Parameters.Add("@MaxFrequencyPower", DbType.Double);
                cmd.Parameters.Add("@Pace0", DbType.Double);
                cmd.Parameters.Add("@Sign", DbType.Int32);

                foreach (var record in records)
                {
                    cmd.Parameters["@FileID"].Value = fileId;
                    cmd.Parameters["@CreateTime"].Value = record.CreateTime;
                    cmd.Parameters["@Condition"].Value = record.Condition ?? (object)DBNull.Value;
                    cmd.Parameters["@Pac0"].Value = record.Pac0 ?? (object)DBNull.Value;
                    cmd.Parameters["@PreviewPAC"].Value = record.PreviewPAC ?? (object)DBNull.Value;
                    cmd.Parameters["@PacMin"].Value = record.PacMin ?? (object)DBNull.Value;
                    cmd.Parameters["@PacMax"].Value = record.PacMax ?? (object)DBNull.Value;
                    cmd.Parameters["@KP"].Value = record.KP ?? (object)DBNull.Value;
                    cmd.Parameters["@MaxFrequencyPower"].Value = record.MaxFrequencyPower ?? (object)DBNull.Value;
                    cmd.Parameters["@Pace0"].Value = record.Pace0 ?? (object)DBNull.Value;
                    cmd.Parameters["@Sign"].Value = record.Sign ?? (object)DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task InsertCal1RecordsAsync(SQLiteTransaction transaction, int fileId, List<Cal1Record> records)
        {
            string sql = @"
                INSERT INTO RecordType_Cal1 
                (FileID, CreateTime, Condition, Pac0, PreviewPAC, Csoc, Paclm1, Paclm2, 
                 Paclm5, Paclm6, KP, SocMin, SocMax, MaxFrequencyPower, Sign)
                VALUES 
                (@FileID, @CreateTime, @Condition, @Pac0, @PreviewPAC, @Csoc, @Paclm1, @Paclm2, 
                 @Paclm5, @Paclm6, @KP, @SocMin, @SocMax, @MaxFrequencyPower, @Sign)";

            using (var cmd = new SQLiteCommand(sql, transaction.Connection, transaction))
            {
                // 添加所有参数
                cmd.Parameters.Add("@FileID", DbType.Int32);
                cmd.Parameters.Add("@CreateTime", DbType.DateTime);
                cmd.Parameters.Add("@Condition", DbType.String);
                cmd.Parameters.Add("@Pac0", DbType.Double);
                cmd.Parameters.Add("@PreviewPAC", DbType.Double);
                cmd.Parameters.Add("@Csoc", DbType.Double);
                cmd.Parameters.Add("@Paclm1", DbType.Double);
                cmd.Parameters.Add("@Paclm2", DbType.Double);
                cmd.Parameters.Add("@Paclm5", DbType.Double);
                cmd.Parameters.Add("@Paclm6", DbType.Double);
                cmd.Parameters.Add("@KP", DbType.Double);
                cmd.Parameters.Add("@SocMin", DbType.Double);
                cmd.Parameters.Add("@SocMax", DbType.Double);
                cmd.Parameters.Add("@MaxFrequencyPower", DbType.Double);
                cmd.Parameters.Add("@Sign", DbType.Int32);

                foreach (var record in records)
                {
                    cmd.Parameters["@FileID"].Value = fileId;
                    cmd.Parameters["@CreateTime"].Value = record.CreateTime;
                    cmd.Parameters["@Condition"].Value = record.Condition ?? (object)DBNull.Value;
                    cmd.Parameters["@Pac0"].Value = record.Pac0 ?? (object)DBNull.Value;
                    cmd.Parameters["@PreviewPAC"].Value = record.PreviewPAC ?? (object)DBNull.Value;
                    cmd.Parameters["@Csoc"].Value = record.Csoc ?? (object)DBNull.Value;
                    cmd.Parameters["@Paclm1"].Value = record.Paclm1 ?? (object)DBNull.Value;
                    cmd.Parameters["@Paclm2"].Value = record.Paclm2 ?? (object)DBNull.Value;
                    cmd.Parameters["@Paclm5"].Value = record.Paclm5 ?? (object)DBNull.Value;
                    cmd.Parameters["@Paclm6"].Value = record.Paclm6 ?? (object)DBNull.Value;
                    cmd.Parameters["@KP"].Value = record.KP ?? (object)DBNull.Value;
                    cmd.Parameters["@SocMin"].Value = record.SocMin ?? (object)DBNull.Value;
                    cmd.Parameters["@SocMax"].Value = record.SocMax ?? (object)DBNull.Value;
                    cmd.Parameters["@MaxFrequencyPower"].Value = record.MaxFrequencyPower ?? (object)DBNull.Value;
                    cmd.Parameters["@Sign"].Value = record.Sign ?? (object)DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task InsertCal3RecordsAsync(SQLiteTransaction transaction, int fileId, List<Cal3Record> records)
        {
            string sql = @"
                INSERT INTO RecordType_Cal3 
                (FileID, CreateTime, Condition, Pc1n, TotalScale, CurrentValue, SOC, Sign)
                VALUES 
                (@FileID, @CreateTime, @Condition, @Pc1n, @TotalScale, @CurrentValue, @SOC, @Sign)";

            using (var cmd = new SQLiteCommand(sql, transaction.Connection, transaction))
            {
                // 添加所有参数
                cmd.Parameters.Add("@FileID", DbType.Int32);
                cmd.Parameters.Add("@CreateTime", DbType.DateTime);
                cmd.Parameters.Add("@Condition", DbType.String);
                cmd.Parameters.Add("@Pc1n", DbType.Double);
                cmd.Parameters.Add("@TotalScale", DbType.Double);
                cmd.Parameters.Add("@CurrentValue", DbType.Double);
                cmd.Parameters.Add("@SOC", DbType.Double);
                cmd.Parameters.Add("@Sign", DbType.Int32);

                foreach (var record in records)
                {
                    cmd.Parameters["@FileID"].Value = fileId;
                    cmd.Parameters["@CreateTime"].Value = record.CreateTime;
                    cmd.Parameters["@Condition"].Value = record.Condition ?? (object)DBNull.Value;
                    cmd.Parameters["@Pc1n"].Value = record.Pc1n ?? (object)DBNull.Value;
                    cmd.Parameters["@TotalScale"].Value = record.TotalScale ?? (object)DBNull.Value;
                    cmd.Parameters["@CurrentValue"].Value = record.CurrentValue ?? (object)DBNull.Value;
                    cmd.Parameters["@SOC"].Value = record.SOC ?? (object)DBNull.Value;
                    cmd.Parameters["@Sign"].Value = record.Sign ?? (object)DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task InsertCal4RecordsAsync(SQLiteTransaction transaction, int fileId, List<Cal4Record> records)
        {
            string sql = @"
                INSERT INTO RecordType_Cal4 
                (FileID, CreateTime, Condition, DataType, JsonData, Receivers, Sign)
                VALUES 
                (@FileID, @CreateTime, @Condition, @DataType, @JsonData, @Receivers, @Sign)";

            using (var cmd = new SQLiteCommand(sql, transaction.Connection, transaction))
            {
                // 添加所有参数
                cmd.Parameters.Add("@FileID", DbType.Int32);
                cmd.Parameters.Add("@CreateTime", DbType.DateTime);
                cmd.Parameters.Add("@Condition", DbType.String);
                cmd.Parameters.Add("@DataType", DbType.String);
                cmd.Parameters.Add("@JsonData", DbType.String);
                cmd.Parameters.Add("@Receivers", DbType.String);
                cmd.Parameters.Add("@Sign", DbType.Int32);

                foreach (var record in records)
                {
                    cmd.Parameters["@FileID"].Value = fileId;
                    cmd.Parameters["@CreateTime"].Value = record.CreateTime;
                    cmd.Parameters["@Condition"].Value = record.Condition ?? (object)DBNull.Value;
                    cmd.Parameters["@DataType"].Value = record.DataType ?? (object)DBNull.Value;
                    cmd.Parameters["@JsonData"].Value = record.JsonData ?? (object)DBNull.Value;
                    cmd.Parameters["@Receivers"].Value = record.Receivers ?? (object)DBNull.Value;
                    cmd.Parameters["@Sign"].Value = record.Sign ?? (object)DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task InsertNoneRecordsAsync(SQLiteTransaction transaction, int fileId, List<NoneRecord> records)
        {
            string sql = @"
                INSERT INTO RecordType_None 
                (FileID, CreateTime, RecordContent, SequenceNumber, Sign)
                VALUES 
                (@FileID, @CreateTime, @RecordContent, @SequenceNumber, @Sign)";

            using (var cmd = new SQLiteCommand(sql, transaction.Connection, transaction))
            {
                // 添加所有参数
                cmd.Parameters.Add("@FileID", DbType.Int32);
                cmd.Parameters.Add("@CreateTime", DbType.DateTime);
                cmd.Parameters.Add("@RecordContent", DbType.String);
                cmd.Parameters.Add("@SequenceNumber", DbType.Int32);
                cmd.Parameters.Add("@Sign", DbType.Int32);

                foreach (var record in records)
                {
                    cmd.Parameters["@FileID"].Value = fileId;
                    cmd.Parameters["@CreateTime"].Value = record.CreateTime;
                    cmd.Parameters["@RecordContent"].Value = record.RecordContent ?? (object)DBNull.Value;
                    cmd.Parameters["@SequenceNumber"].Value = record.SequenceNumber ?? (object)DBNull.Value;
                    cmd.Parameters["@Sign"].Value = record.Sign ?? (object)DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// 删除文件及相关数据
        /// </summary>
        public async Task<bool> DeleteFileAndDataAsync(int fileId)
        {
            try
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // 开始事务
                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        try
                        {
                            // 获取记录类型列表
                            var recordTypes = new[]
                            {
                                "RecordType_Cal1", "RecordType_Cal2", "RecordType_Cal3",
                                "RecordType_Cal4", "RecordType_Cal5", "RecordType_Cal6",
                                "RecordType_Cal7", "RecordType_None", "TargetTable"
                            };

                            // 删除所有相关记录类型的数据
                            foreach (var tableName in recordTypes)
                            {
                                string deleteSql = $"DELETE FROM {tableName} WHERE FileID = @FileID";
                                using (var cmd = new SQLiteCommand(deleteSql, conn, (SQLiteTransaction)transaction))
                                {
                                    cmd.Parameters.AddWithValue("@FileID", fileId);
                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }

                            // 删除FileInfo表中的记录
                            string deleteFileInfoSql = "DELETE FROM FileInfo WHERE FileID = @FileID";
                            using (var cmd = new SQLiteCommand(deleteFileInfoSql, conn, (SQLiteTransaction)transaction))
                            {
                                cmd.Parameters.AddWithValue("@FileID", fileId);
                                await cmd.ExecuteNonQueryAsync();
                            }

                            // 提交事务
                            await transaction.CommitAsync();
                            return true;
                        }
                        catch
                        {
                            await transaction.RollbackAsync();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"删除文件ID {fileId} 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 更新文件记录数
        /// </summary>
        private async Task UpdateFileRecordCountAsync(SQLiteConnection connection, int fileId, int recordCount)
        {
            string sql = "UPDATE FileInfo SET RecordCount = RecordCount + @AddedCount WHERE FileID = @FileID";

            using (var cmd = new SQLiteCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@AddedCount", recordCount);
                cmd.Parameters.AddWithValue("@FileID", fileId);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// 立即停止服务并等待完成
        /// </summary>
        public async Task StopImmediatelyAsync()
        {
            lock (_startStopLock)
            {
                if (!_isRunning) return;
                _cancellationTokenSource.Cancel();
                _isStopping = true;
            }

            try
            {
                // 立即处理剩余数据
                await ProcessRemainingDataAsync();

                if (_writeTask != null && !_writeTask.IsCompleted)
                {
                    await Task.WhenAny(_writeTask, Task.Delay(10000));
                }
            }
            finally
            {
                lock (_startStopLock)
                {
                    _isRunning = false;
                    _isStopping = false;
                }
            }
        }

        /// <summary>
        /// 等待所有数据写入完成
        /// </summary>
        public async Task WaitForCompletionAsync(TimeSpan timeout)
        {
            var startTime = DateTime.Now;

            while (!_dataQueue.IsEmpty && (DateTime.Now - startTime) < timeout)
            {
                await Task.Delay(100);
            }

            if (!_dataQueue.IsEmpty)
            {
                LogService.Log($"警告：在 {timeout.TotalSeconds} 秒内未完成所有数据写入");
            }
        }

        // 事件触发方法
        private void OnWriteCompleted(string message)
        {
            WriteCompleted?.Invoke(this, message);
        }

        private void OnWriteError(string errorMessage)
        {
            WriteError?.Invoke(this, errorMessage);
        }

        public void Dispose()
        {
            try
            {
                if (_isRunning)
                {
                    // 同步停止，因为Dispose不能异步
                    StopImmediatelyAsync().Wait(TimeSpan.FromSeconds(10));
                }

                _cancellationTokenSource?.Dispose();
                _queueSemaphore?.Dispose();

                LogService.Log("DataBatchWriteManager 已释放");
            }
            catch (Exception ex)
            {
                LogService.Log($"释放资源时发生错误: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 数据批次项
    /// </summary>
    public class DataBatchItem
    {
        public int FileId { get; set; }
        public Dictionary<string, List<object>> RecordsByType { get; set; }
        public bool AutoDetectSignGroups { get; set; }

        public DateTime EnqueueTime { get; set; }
    }

    /// <summary>
    /// 批次写入事件参数
    /// </summary>
    public class BatchWriteEventArgs : EventArgs
    {
        public int FileId { get; set; }
        public string RecordType { get; set; }
        public int Count { get; set; }
        public DateTime Timestamp { get; set; }
    }
}