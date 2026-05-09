using DataModel;
using Log;
using System.Data.SQLite;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

#pragma warning disable
namespace ChargeDebug.Service
{
    /// <summary>
    /// CSV文件管理器
    /// 负责CSV文件的解析和导出，不涉及数据库操作
    /// </summary>
    public class CsvFileManager
    {
        // Sign管理：用于标记同一组数据
        private int _currentSign = 0;
        private bool _inScheduleGroup = false;
        private readonly Regex _scheduleStartRegex = new Regex(@"Start\s+one\s+schedual\s*[:：]", RegexOptions.IgnoreCase);

        /// <summary>
        /// 重置Sign计数器
        /// </summary>
        public void ResetSignCounter()
        {
            _currentSign = 0;
            _inScheduleGroup = false;
        }

        /// <summary>
        /// 获取文件时间信息
        /// </summary>
        public async Task<FileTimeInfo> GetFileTimeInfoAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    DateTime startTime = DateTime.MaxValue;
                    DateTime endTime = DateTime.MinValue;
                    bool hasValidTime = false;

                    // 读取CSV文件的前几行和后几行来获取时间范围
                    using (var reader = new StreamReader(filePath, Encoding.UTF8))
                    {
                        reader.ReadLine(); // 跳过标题行

                        // 读取前几行
                        for (int i = 0; i < 10; i++)
                        {
                            string line = reader.ReadLine();
                            if (line == null) break;

                            var time = ExtractTimeFromCsvLine(line);
                            if (time.HasValue)
                            {
                                startTime = time.Value < startTime ? time.Value : startTime;
                                //endTime = time.Value > endTime ? time.Value : endTime;
                                hasValidTime = true;
                            }
                        }

                        // 如果文件很大，也读取最后几行
                        if (fileInfo.Length > 1024 * 1024) // 大于1MB
                        {
                            reader.BaseStream.Seek(-Math.Min(1024 * 1024, fileInfo.Length), SeekOrigin.End);
                            reader.DiscardBufferedData();

                            while (!reader.EndOfStream)
                            {
                                string line = reader.ReadLine();
                                if (line == null) break;

                                var time = ExtractTimeFromCsvLine(line);
                                if (time.HasValue)
                                {
                                    //startTime = time.Value < startTime ? time.Value : startTime;
                                    endTime = time.Value > endTime ? time.Value : endTime;
                                    hasValidTime = true;
                                }
                            }
                        }
                    }

                    if (!hasValidTime)
                    {
                        // 如果没有有效时间，使用文件创建时间
                        startTime = fileInfo.CreationTime;
                        endTime = fileInfo.LastWriteTime;
                    }

                    return new FileTimeInfo
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        StartTime = startTime,
                        EndTime = endTime,
                        FileSize = fileInfo.Length
                    };
                }
                catch
                {
                    return null;
                }
            });
        }

        /// <summary>
        /// 根据时间连贯性分组文件
        /// </summary>
        public List<List<FileTimeInfo>> GroupFilesByTimeContinuity(List<FileTimeInfo> fileInfos)
        {
            var groups = new List<List<FileTimeInfo>>();

            if (fileInfos.Count == 0)
                return groups;

            // 设置时间连贯性阈值（例如：1s）

            var currentGroup = new List<FileTimeInfo> { fileInfos[0] };

            for (int i = 1; i < fileInfos.Count; i++)
            {
                var currentFile = fileInfos[i];
                var previousFile = currentGroup.Last();

                // 检查时间是否连贯：当前文件的开始时间与前一个文件的结束时间相差在阈值内
                var timeGap = (int)(currentFile.StartTime - previousFile.EndTime).TotalSeconds;

                currentGroup.Add(currentFile);
                // 允许时间重叠（负值）或微小间隙（在1秒内）
                //if (timeGap <= 2)
                //{
                //    // 时间连贯，加入当前组
                //    currentGroup.Add(currentFile);
                //}
                //else
                //{
                //    // 时间不连贯（超过1秒），开始新组
                //    groups.Add(currentGroup);
                //    currentGroup = new List<FileTimeInfo> { currentFile };
                //}
            }

            
            // 添加最后一组
            if (currentGroup.Count > 0)
            {
                groups.Add(currentGroup);
            }

            return groups;
        }

        /// <summary>
        /// 合并解析文件组
        /// </summary>
        public async Task<Dictionary<string, List<object>>> MergeParseFileGroupAsync(List<FileTimeInfo> fileGroup)
        {
            var mergedRecords = new Dictionary<string, List<object>>()
            {
                { "Cal1", new List<object>() },
                { "Cal2", new List<object>() },
                { "Cal3", new List<object>() },
                { "Cal4", new List<object>() },
                { "Cal5", new List<object>() },
                { "Cal6", new List<object>() },
                { "Cal7", new List<object>() },
                { "None", new List<object>() }
            };

            ResetSignCounter();

            foreach (var fileInfo in fileGroup)
            {
                try
                {
                    // 解析CSV文件
                    var parsedData = await Task.Run(() =>
                    {
                        try
                        {
                            return ParseCsvFileToRecords(fileInfo.FilePath);
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"解析文件 {fileInfo.FileName} 时出错: {ex.Message}");
                            return null;
                        }
                    });

                    if (parsedData != null)
                    {
                        // 合并到总记录中
                        foreach (var kvp in parsedData)
                        {
                            if (mergedRecords.ContainsKey(kvp.Key) && kvp.Value != null)
                            {
                                mergedRecords[kvp.Key].AddRange(kvp.Value);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Log($"处理文件 {fileInfo.FileName} 时出错: {ex.Message}");
                }
            }

            // 检查是否有实际数据
            bool hasData = mergedRecords.Any(kvp => kvp.Value.Count > 0);
            return hasData ? mergedRecords : null;
        }

        /// <summary>
        /// 获取文件组信息
        /// </summary>
        public FileGroupInfo GetFileGroupInfo(List<FileTimeInfo> fileGroup, int groupNumber)
        {
            if (fileGroup.Count == 1)
            {
                // 单个文件，直接使用原文件名
                var fileInfo = fileGroup[0];
                return new FileGroupInfo
                {
                    FileName = fileInfo.FileName,
                    FilePath = fileInfo.FilePath,
                    FileSize = fileInfo.FileSize
                };
            }
            else
            {
                // 多个文件合并，生成组合文件名
                var firstFile = fileGroup[0];
                var lastFile = fileGroup[fileGroup.Count - 1];

                string baseName = Path.GetFileNameWithoutExtension(firstFile.FileName);
                string groupName = $"{baseName} 等{fileGroup.Count}个文件_第{groupNumber}组";

                long totalSize = fileGroup.Sum(f => f.FileSize);

                return new FileGroupInfo
                {
                    FileName = groupName + ".csv",
                    FilePath = firstFile.FilePath, // 使用第一个文件的路径作为参考
                    FileSize = totalSize
                };
            }
        }

        /// <summary>
        /// 从CSV行中提取时间
        /// </summary>
        private DateTime? ExtractTimeFromCsvLine(string line)
        {
            try
            {
                // CSV格式通常是：时间戳,类型,其他字段...
                var parts = line.Split(',');
                if (parts.Length >= 1)
                {
                    if (DateTime.TryParse(parts[0], out DateTime result))
                    {
                        return result;
                    }
                }
            }
            catch
            {
                // 忽略解析错误
            }
            return null;
        }

        /// <summary>
        /// 解析CSV文件到不同类型的数据记录列表
        /// </summary>
        /// <param name="filePath">CSV文件路径</param>
        /// <returns>解析后的数据记录字典，按类型分类</returns>
        private Dictionary<string, List<object>> ParseCsvFileToRecords(string filePath)
        {
            var result = new Dictionary<string, List<object>>()
            {
                { "Cal1", new List<object>() },
                { "Cal2", new List<object>() },
                { "Cal3", new List<object>() },
                { "Cal4", new List<object>() },
                { "Cal5", new List<object>() },
                { "Cal6", new List<object>() },
                { "Cal7", new List<object>() },
                { "None", new List<object>() }
            };

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件不存在: {filePath}");

            try
            {
                using (var reader = new StreamReader(filePath, Encoding.UTF8))
                {
                    string line;
                    int lineNumber = 0;
                    reader.ReadLine(); // 跳过第一行

                    while ((line = reader.ReadLine()) != null)
                    {
                        lineNumber++;

                        try
                        {
                            var record = ParseCsvLineToRecord(line, lineNumber);
                            if (record != null)
                            {
                                // 设置Sign值
                                SetRecordSign(record);

                                string recordType = GetRecordType(record);
                                if (result.ContainsKey(recordType))
                                {
                                    result[recordType].Add(record);
                                }
                                else
                                {
                                    // 未知类型，添加到None
                                    var noneRecord = new NoneRecord
                                    {
                                        CreateTime = GetCreateTime(record),
                                        RecordContent = line,
                                        Sign = GetSignValue(record)
                                    };
                                    result["None"].Add(noneRecord);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"第{lineNumber}行解析错误: {ex.Message}");
                        }
                    }
                }

                //LogService.Log($"CSV文件解析完成，共检测到 {_currentSign} 个数据分组");
                return result;
            }
            catch (Exception ex)
            {
                throw new Exception($"解析CSV文件失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 设置记录的Sign值
        /// </summary>
        private void SetRecordSign(object record)
        {
            if (record is NoneRecord noneRecord)
            {
                // 检查是否为开始标记
                if (IsStartScheduleMarker(noneRecord.RecordContent))
                {
                    _currentSign++;
                    _inScheduleGroup = true;
                }

                // 为None记录设置Sign
                noneRecord.Sign = _inScheduleGroup ? _currentSign : 0;
            }
            else
            {
                // 为其他类型记录设置Sign
                SetObjectSign(record, _inScheduleGroup ? _currentSign : 0);
            }
        }

        /// <summary>
        /// 检查是否为开始计划标记
        /// </summary>
        private bool IsStartScheduleMarker(string recordContent)
        {
            if (string.IsNullOrEmpty(recordContent)) return false;

            // 使用正则表达式匹配多种格式
            return _scheduleStartRegex.IsMatch(recordContent);
        }

        /// <summary>
        /// 为对象设置Sign值
        /// </summary>
        private void SetObjectSign(object obj, int sign)
        {
            var signProperty = obj.GetType().GetProperty("Sign");
            if (signProperty != null && signProperty.CanWrite)
            {
                signProperty.SetValue(obj, sign);
            }
        }

        /// <summary>
        /// 获取对象的Sign值
        /// </summary>
        private int? GetSignValue(object obj)
        {
            var signProperty = obj.GetType().GetProperty("Sign");
            if (signProperty != null && signProperty.CanRead)
            {
                return (int?)signProperty.GetValue(obj);
            }
            return null;
        }

        /// <summary>
        /// 获取记录类型
        /// </summary>
        private string GetRecordType(object record)
        {
            if (record is Cal1Record) return "Cal1";
            if (record is Cal2Record) return "Cal2";
            if (record is Cal3Record) return "Cal3";
            if (record is Cal4Record) return "Cal4";
            if (record is Cal5Record) return "Cal5";
            if (record is Cal6Record) return "Cal6";
            if (record is Cal7Record) return "Cal7";
            return "None";
        }

        /// <summary>
        /// 获取记录创建时间
        /// </summary>
        private DateTime GetCreateTime(object record)
        {
            switch (record)
            {
                case Cal1Record cal1: return cal1.CreateTime;
                case Cal2Record cal2: return cal2.CreateTime;
                case Cal3Record cal3: return cal3.CreateTime;
                case Cal4Record cal4: return cal4.CreateTime;
                case Cal5Record cal5: return cal5.CreateTime;
                case Cal6Record cal6: return cal6.CreateTime;
                case Cal7Record cal7: return cal7.CreateTime;
                default: return DateTime.MinValue;
            }
        }

        /// <summary>
        /// 解析单行CSV数据到具体记录类型
        /// </summary>
        private object ParseCsvLineToRecord(string line, int lineNumber)
        {
            var fields = SplitCsvLine(line);
            if (fields.Length < 2) return null;

            string recordType = fields[1];
            DateTime createTime = DateTime.MinValue;

            // 解析时间戳
            if (DateTime.TryParse(fields[0], out DateTime parsedTime))
            {
                //createTime = RoundToNearestSecond(parsedTime); // 四舍五入到秒
                createTime = parsedTime;
            }

            try
            {
                switch (recordType.Split("：")[1])
                {
                    case "Cal1":
                        return ParseCal1Record(createTime, fields);
                    case "Cal2":
                        return ParseCal2Record(createTime, fields);
                    case "Cal3":
                        return ParseCal3Record(createTime, fields);
                    case "Cal4":
                        return ParseCal4Record(createTime, fields);
                    case "Cal5":
                        return ParseCal5Record(createTime, fields);
                    case "Cal6":
                        return ParseCal6Record(createTime, fields);
                    case "Cal7":
                        return ParseCal7Record(createTime, fields);
                    default:
                        // 未知类型
                        return new NoneRecord
                        {
                            CreateTime = createTime,
                            RecordContent = string.Join(",", fields),
                            SequenceNumber = lineNumber.ToString()
                        };
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"第{lineNumber}行数据格式错误: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 将 DateTime 四舍五入到最近的秒
        /// </summary>
        private DateTime RoundToNearestSecond(DateTime dt)
        {
            long ticks = dt.Ticks;
            long seconds = ticks / TimeSpan.TicksPerSecond;
            long fractionalTicks = ticks % TimeSpan.TicksPerSecond;

            // 如果剩余 ticks 超过半秒，则进位
            if (fractionalTicks >= TimeSpan.TicksPerSecond / 2)
            {
                seconds++;
            }

            return new DateTime(seconds * TimeSpan.TicksPerSecond, dt.Kind);
        }

        /// <summary>
        /// 解析Cal1类型记录
        /// </summary>
        private Cal1Record ParseCal1Record(DateTime createTime, string[] fields)
        {
            var record = new Cal1Record
            {
                CreateTime = createTime,
            };

            for (int i = 2; i < fields.Length; i++)
            {
                var field = fields[i];
                if (string.IsNullOrEmpty(field)) continue;

                var parts = field.Split("：");
                if (parts.Length < 2) continue;

                var fieldName = parts[0].Trim();
                var fieldValue = parts[1].Trim();

                switch (fieldName)
                {
                    case "Condition":
                        record.Condition = fieldValue;
                        break;
                    case "pac0":
                        if (double.TryParse(fieldValue, out double pac0))
                            record.Pac0 = pac0;
                        break;
                    case "previewPAC":
                        if (double.TryParse(fieldValue, out double previewPAC))
                            record.PreviewPAC = previewPAC;
                        break;
                    case "csoc":
                        if (double.TryParse(fieldValue, out double csoc))
                            record.Csoc = csoc;
                        break;
                    case "paclm1":
                        if (double.TryParse(fieldValue, out double paclm1))
                            record.Paclm1 = paclm1;
                        break;
                    case "paclm2":
                        if (double.TryParse(fieldValue, out double paclm2))
                            record.Paclm2 = paclm2;
                        break;
                    case "paclm5":
                        if (double.TryParse(fieldValue, out double paclm5))
                            record.Paclm5 = paclm5;
                        break;
                    case "paclm6":
                        if (double.TryParse(fieldValue, out double paclm6))
                            record.Paclm6 = paclm6;
                        break;
                    case "kp":
                        if (double.TryParse(fieldValue, out double kp))
                            record.KP = kp;
                        break;
                    case "socMin":
                        if (double.TryParse(fieldValue, out double socMin))
                            record.SocMin = socMin;
                        break;
                    case "socMax":
                        if (double.TryParse(fieldValue, out double socMax))
                            record.SocMax = socMax;
                        break;
                    case "maxFrequencyPower":
                        if (double.TryParse(fieldValue, out double maxFrequencyPower))
                            record.MaxFrequencyPower = maxFrequencyPower;
                        break;
                }
            }

            return record;
        }

        /// <summary>
        /// 解析Cal2类型记录
        /// </summary>
        private Cal2Record ParseCal2Record(DateTime createTime, string[] fields)
        {
            var record = new Cal2Record
            {
                CreateTime = createTime
            };

            for (int i = 2; i < fields.Length; i++)
            {
                var field = fields[i];
                if (string.IsNullOrEmpty(field)) continue;

                var parts = field.Split('：');
                if (parts.Length < 2) continue;

                var fieldName = parts[0].Trim();
                var fieldValue = parts[1].Trim();

                switch (fieldName)
                {
                    case "Condition":
                        record.Condition = fieldValue;
                        break;
                    case "pac0":
                        if (double.TryParse(fieldValue, out double pac0))
                            record.Pac0 = pac0;
                        break;
                    case "previewPAC":
                        if (double.TryParse(fieldValue, out double previewPAC))
                            record.PreviewPAC = previewPAC;
                        break;
                    case "pacMin":
                        if (double.TryParse(fieldValue, out double pacMin))
                            record.PacMin = pacMin;
                        break;
                    case "pacMax":
                        if (double.TryParse(fieldValue, out double pacMax))
                            record.PacMax = pacMax;
                        break;
                    case "kp":
                        if (double.TryParse(fieldValue, out double kp))
                            record.KP = kp;
                        break;
                    case "maxFrequencyPower":
                        if (double.TryParse(fieldValue, out double maxFrequencyPower))
                            record.MaxFrequencyPower = maxFrequencyPower;
                        break;
                    case "pace0":
                        if (double.TryParse(fieldValue, out double pace0))
                            record.Pace0 = pace0;
                        break;
                }
            }

            return record;
        }

        /// <summary>
        /// 解析Cal3类型记录
        /// </summary>
        private Cal3Record ParseCal3Record(DateTime createTime, string[] fields)
        {
            var record = new Cal3Record
            {
                CreateTime = createTime
            };

            for (int i = 2; i < fields.Length; i++)
            {
                var field = fields[i];
                if (string.IsNullOrEmpty(field)) continue;

                var parts = field.Split('：');
                if (parts.Length < 2) continue;

                var fieldName = parts[0].Trim();
                var fieldValue = parts[1].Trim();

                switch (fieldName)
                {
                    case "Condition":
                        record.Condition = fieldValue;
                        break;
                    case "pc1n":
                        if (double.TryParse(fieldValue, out double pc1n))
                            record.Pc1n = pc1n;
                        break;
                    case "totalScale":
                        if (double.TryParse(fieldValue, out double totalScale))
                            record.TotalScale = totalScale;
                        break;
                    case "CurrentValue":
                        if (double.TryParse(fieldValue, out double currentValue))
                            record.CurrentValue = currentValue;
                        break;
                    case "soc":
                        if (double.TryParse(fieldValue, out double soc))
                            record.SOC = soc;
                        break;
                }
            }

            return record;
        }

        /// <summary>
        /// 解析Cal4类型记录
        /// </summary>
        private Cal4Record ParseCal4Record(DateTime createTime, string[] fields)
        {
            var record = new Cal4Record
            {
                CreateTime = createTime
            };

            for (int i = 2; i < fields.Length; i++)
            {
                var field = fields[i];
                if (string.IsNullOrEmpty(field)) continue;

                var parts = field.Split("：");
                if (parts.Length < 2) continue;

                var fieldName = parts[0].Trim();
                var fieldValue = parts[1].Trim();

                switch (fieldName)
                {
                    case "Condition":
                        record.Condition = fieldValue;
                        break;
                    case "DataType":
                        record.DataType = fieldValue;
                        break;
                    case "Json":
                        record.JsonData = fieldValue;
                        break;
                    case "Receivers":
                        record.Receivers = fieldValue;
                        break;
                }
            }

            return record;
        }

        /// <summary>
        /// 解析Cal5类型记录
        /// </summary>
        private Cal5Record ParseCal5Record(DateTime createTime, string[] fields)
        {
            var record = new Cal5Record
            {
                CreateTime = createTime
            };

            for (int i = 2; i < fields.Length; i++)
            {
                var field = fields[i];
                if (string.IsNullOrEmpty(field)) continue;

                var parts = field.Split("：");
                if (parts.Length < 2) continue;

                var fieldName = parts[0].Trim();
                var fieldValue = parts[1].Trim();

                switch (fieldName)
                {
                    case "SOH":
                        if (double.TryParse(fieldValue, out double soh))
                            record.SOH = soh;
                        break;
                    case "Current":
                        if (double.TryParse(fieldValue, out double current))
                            record.Current = current;
                        break;
                    case "Power":
                        if (decimal.TryParse(fieldValue, out decimal power))
                            record.Power = power;
                        break;
                    case "EMSStatus":
                            record.EMSStatus = fieldValue;
                        break;
                    case "ChargeEnergy":
                        if (double.TryParse(fieldValue, out double chargeEnergy))
                            record.ChargeEnergy = chargeEnergy;
                        break;
                    case "Voltage":
                        if (double.TryParse(fieldValue, out double voltage))
                            record.Voltage = voltage;
                        break;
                    case "DeviceStatus":
                        if (int.TryParse(fieldValue, out int deviceStatus))
                            record.DeviceStatus = deviceStatus;
                        break;
                    case "SOC":
                        if (double.TryParse(fieldValue, out double soc))
                            record.SOC = soc;
                        break;
                    case "DisChargeEnergy":
                        if (double.TryParse(fieldValue, out double disChargeEnergy))
                            record.DischargeEnergy = disChargeEnergy;
                        break;
                    case "EMSMode":
                            record.EMSMode = fieldValue;
                        break;
                    case "ChannelNum":
                        if (int.TryParse(fieldValue, out int channelNum))
                            record.ChannelNum = channelNum;
                        break;
                    case "Key":
                        record.KeyValue = fieldValue;
                        break;
                }
            }

            return record;
        }

        /// <summary>
        /// 解析Cal6类型记录
        /// </summary>
        private Cal6Record ParseCal6Record(DateTime createTime, string[] fields)
        {
            var record = new Cal6Record
            {
                CreateTime = createTime
            };

            // Cal6记录的特殊格式：OneTimePeriodConfig包含多个配置项
            if (fields.Length > 2 && fields[3].Contains("OneTimePeriodConfig："))
            {
                var configStr = fields[3].Replace("OneTimePeriodConfig：", "");
                var configParts = configStr.Split(' ');

                foreach (var part in configParts)
                {
                    if (string.IsNullOrEmpty(part)) continue;

                    var keyValue = part.Split('=');
                    if (keyValue.Length == 2)
                    {
                        var key = keyValue[0];
                        var value = keyValue[1];

                        switch (key)
                        {
                            case "StartTime":
                                record.StartTime = value;
                                break;
                            case "EndTime":
                                record.EndTime = value;
                                break;
                            case "MaxFrequencyPower":
                                if (double.TryParse(value, out double maxFrequencyPower))
                                    record.MaxFrequencyPower = maxFrequencyPower;
                                break;
                            case "Paclm5":
                                if (double.TryParse(value, out double paclm5))
                                    record.Paclm5 = paclm5;
                                break;
                            case "Paclm6":
                                if (double.TryParse(value, out double paclm6))
                                    record.Paclm6 = paclm6;
                                break;
                            case "Paclm1":
                                if (double.TryParse(value, out double paclm1))
                                    record.Paclm1 = paclm1;
                                break;
                            case "Paclm2":
                                if (double.TryParse(value, out double paclm2))
                                    record.Paclm2 = paclm2;
                                break;
                            case "SocMin":
                                if (double.TryParse(value, out double socMin))
                                    record.SocMin = socMin;
                                break;
                            case "SocMax":
                                if (double.TryParse(value, out double socMax))
                                    record.SocMax = socMax;
                                break;
                            case "KP":
                                if (double.TryParse(value, out double kp))
                                    record.KP = kp;
                                break;
                            case "LimitChargeCurrent":
                                if (double.TryParse(value, out double limitChargeCurrent))
                                    record.LimitChargeCurrent = limitChargeCurrent;
                                break;
                            case "LimitDischargeCurrent":
                                if (double.TryParse(value, out double limitDischargeCurrent))
                                    record.LimitDischargeCurrent = limitDischargeCurrent;
                                break;
                        }
                    }
                }
            }

            return record;
        }

        /// <summary>
        /// 解析Cal7类型记录
        /// </summary>
        private Cal7Record ParseCal7Record(DateTime createTime, string[] fields)
        {
            var record = new Cal7Record
            {
                CreateTime = createTime
            };

            for (int i = 2; i < fields.Length; i++)
            {
                var field = fields[i];
                if (string.IsNullOrEmpty(field)) continue;

                var parts = field.Split("：");
                if (parts.Length < 2) continue;

                var fieldName = parts[0].Trim();
                var fieldValue = parts[1].Trim();

                switch (fieldName)
                {
                    case "Power":
                        if (decimal.TryParse(fieldValue, out decimal power))
                            record.Power = power;
                        break;
                    case "ChargeEnergy":
                        if (double.TryParse(fieldValue, out double chargeEnergy))
                            record.ChargeEnergy = chargeEnergy;
                        break;
                    case "DeviceStatus":
                        if (int.TryParse(fieldValue, out int deviceStatus))
                            record.DeviceStatus = deviceStatus;
                        break;
                    case "DisChargeEnergy":
                        if (double.TryParse(fieldValue, out double disChargeEnergy))
                            record.DischargeEnergy = disChargeEnergy;
                        break;
                    case "ChannelNum":
                        if (int.TryParse(fieldValue, out int channelNum))
                            record.ChannelNum = channelNum;
                        break;
                    case "Key":
                        record.KeyValue = fieldValue;
                        break;
                }
            }

            return record;
        }

        /// <summary>
        /// 分割CSV行，处理包含逗号的字段
        /// </summary>
        private string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            // 添加最后一个字段
            result.Add(currentField.ToString());

            return result.ToArray();
        }
    }
}