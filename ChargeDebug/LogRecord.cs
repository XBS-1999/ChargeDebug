using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ChargeDebug.LogProcessor
{
    // 日志记录实体类
    public class LogEntry
    {
        public DateTime LogTime { get; set; }
        public string RecordType { get; set; }
        public string RawData { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }

    public class LogFileParser
    {
        public List<LogEntry> ParseLogFile(string filePath)
        {
            var entries = new List<LogEntry>();

            if (!File.Exists(filePath))
                return entries;

            var lines = File.ReadAllLines(filePath);

            foreach (var line in lines)
            {
                var entry = ParseLogLine(line);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        private LogEntry ParseLogLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            // 解析时间戳和RecordType
            var parts = line.Split(new[] { ',' }, 2);
            if (parts.Length < 2)
                return null;

            var timePart = parts[0].Trim();
            var recordTypeMatch = Regex.Match(parts[1], @"RecordType\s*[:：]\s*(\w+)");

            if (!DateTime.TryParseExact(timePart, "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var logTime))
                return null;

            if (!recordTypeMatch.Success)
                return null;

            var record = new LogEntry
            {
                LogTime = logTime,
                RecordType = recordTypeMatch.Groups[1].Value,
                RawData = line
            };

            // 解析参数
            ParseParameters(parts[1], record);

            return record;
        }

        private void ParseParameters(string dataPart, LogEntry record)
        {
            // 处理特殊格式：OneTimePeriodConfig
            var configMatch = Regex.Match(dataPart, @"OneTimePeriodConfig\s*[:：]\s*(.+)");
            if (configMatch.Success)
            {
                ParseConfigParameters(configMatch.Groups[1].Value, record);
                dataPart = dataPart.Replace(configMatch.Value, "");
            }

            // 处理Json格式
            var jsonMatch = Regex.Match(dataPart, @"Json\s*[:：]\s*(\[.*\])");
            if (jsonMatch.Success)
            {
                record.Parameters["Json"] = jsonMatch.Groups[1].Value;
                dataPart = dataPart.Replace(jsonMatch.Value, "");
            }

            // 处理Receivers格式
            var receiversMatch = Regex.Match(dataPart, @"Receivers\s*[:：]\s*(【.*】)");
            if (receiversMatch.Success)
            {
                record.Parameters["Receivers"] = receiversMatch.Groups[1].Value;
                dataPart = dataPart.Replace(receiversMatch.Value, "");
            }

            // 解析普通参数
            var paramPattern = @"(\w+)\s*[:=]\s*([^,]+)";
            var matches = Regex.Matches(dataPart, paramPattern);

            foreach (Match match in matches)
            {
                var key = match.Groups[1].Value.Trim();
                var value = match.Groups[2].Value.Trim();

                if (!record.Parameters.ContainsKey(key))
                {
                    record.Parameters[key] = value;
                }
            }
        }

        private void ParseConfigParameters(string configStr, LogEntry record)
        {
            var pairs = configStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var pair in pairs)
            {
                var keyValue = pair.Split('=');
                if (keyValue.Length == 2)
                {
                    record.Parameters[keyValue[0]] = keyValue[1];
                }
            }
        }
    }

    public class DatabaseWriter
    {
        private readonly string _connectionString;

        public DatabaseWriter(string dbPath)
        {
            _connectionString = $"Data Source={dbPath};Version=3;";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // 创建表结构的SQL语句（如上面所示）
                // 这里可以执行创建表的SQL
            }
        }

        public void WriteLogRecords(List<LogEntry> records)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var record in records)
                        {
                            var logId = InsertLogRecord(connection, record);

                            switch (record.RecordType)
                            {
                                case "Cal6":
                                    InsertCal6Record(connection, logId, record);
                                    break;
                                case "Cal7":
                                    InsertCal7Record(connection, logId, record);
                                    break;
                                case "Cal2":
                                    InsertCal2Record(connection, logId, record);
                                    break;
                                case "Cal1":
                                    InsertCal1Record(connection, logId, record);
                                    break;
                                case "Cal5":
                                    InsertCal5Record(connection, logId, record);
                                    break;
                                case "Cal3":
                                    InsertCal3Record(connection, logId, record);
                                    break;
                                case "Cal4":
                                    InsertCal4Record(connection, logId, record);
                                    break;
                                case "None":
                                    InsertNoneRecord(connection, logId, record);
                                    break;
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        private long InsertLogRecord(SQLiteConnection connection, LogEntry record)
        {
            var sql = @"
                INSERT INTO LogRecords (LogTime, RecordType, RawData)
                VALUES (@LogTime, @RecordType, @RawData);
                SELECT last_insert_rowid();";

            using (var cmd = new SQLiteCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@LogTime", record.LogTime);
                cmd.Parameters.AddWithValue("@RecordType", record.RecordType);
                cmd.Parameters.AddWithValue("@RawData", record.RawData);

                return (long)cmd.ExecuteScalar();
            }
        }

        private void InsertCal6Record(SQLiteConnection connection, long logId, LogEntry record)
        {
            var sql = @"
                INSERT INTO Cal6Records (
                    LogRecordId, CurrentDate, StartTime, EndTime, MaxFrequencyPower,
                    Paclm5, Paclm6, Paclm1, Paclm2, SocMin, SocMax, KP,
                    LimitChargeCurrent, LimitDischargeCurrent
                ) VALUES (
                    @LogRecordId, @CurrentDate, @StartTime, @EndTime, @MaxFrequencyPower,
                    @Paclm5, @Paclm6, @Paclm1, @Paclm2, @SocMin, @SocMax, @KP,
                    @LimitChargeCurrent, @LimitDischargeCurrent
                )";

            using (var cmd = new SQLiteCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@LogRecordId", logId);

                // 解析参数
                if (record.Parameters.TryGetValue("CurrentDate", out var currentDate))
                    cmd.Parameters.AddWithValue("@CurrentDate", ParseDateTime(currentDate));

                if (record.Parameters.TryGetValue("StartTime", out var startTime))
                    cmd.Parameters.AddWithValue("@StartTime", TimeSpan.Parse(startTime));

                if (record.Parameters.TryGetValue("EndTime", out var endTime))
                    cmd.Parameters.AddWithValue("@EndTime", TimeSpan.Parse(endTime));

                AddDoubleParameter(cmd, "@MaxFrequencyPower", "MaxFrequencyPower", record);
                AddDoubleParameter(cmd, "@Paclm5", "Paclm5", record);
                AddDoubleParameter(cmd, "@Paclm6", "Paclm6", record);
                AddDoubleParameter(cmd, "@Paclm1", "Paclm1", record);
                AddDoubleParameter(cmd, "@Paclm2", "Paclm2", record);
                AddDoubleParameter(cmd, "@SocMin", "SocMin", record);
                AddDoubleParameter(cmd, "@SocMax", "SocMax", record);
                AddDoubleParameter(cmd, "@KP", "KP", record);
                AddDoubleParameter(cmd, "@LimitChargeCurrent", "LimitChargeCurrent", record);
                AddDoubleParameter(cmd, "@LimitDischargeCurrent", "LimitDischargeCurrent", record);

                cmd.ExecuteNonQuery();
            }
        }

        private void InsertCal7Record(SQLiteConnection connection, long logId, LogEntry record)
        {
            var sql = @"
                INSERT INTO Cal7Records (
                    LogRecordId, Power, ChargeEnergy, DeviceStatus,
                    DisChargeEnergy, ChannelNum, DeviceKey
                ) VALUES (
                    @LogRecordId, @Power, @ChargeEnergy, @DeviceStatus,
                    @DisChargeEnergy, @ChannelNum, @DeviceKey
                )";

            using (var cmd = new SQLiteCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@LogRecordId", logId);
                cmd.Parameters.AddWithValue("@DeviceKey", record.Parameters.GetValueOrDefault("Key"));

                AddDoubleParameter(cmd, "@Power", "Power", record);
                AddDoubleParameter(cmd, "@ChargeEnergy", "ChargeEnergy", record);
                AddDoubleParameter(cmd, "@DisChargeEnergy", "DisChargeEnergy", record);

                if (record.Parameters.TryGetValue("DeviceStatus", out var status))
                    cmd.Parameters.AddWithValue("@DeviceStatus", int.Parse(status));

                if (record.Parameters.TryGetValue("ChannelNum", out var channel))
                    cmd.Parameters.AddWithValue("@ChannelNum", int.Parse(channel));

                cmd.ExecuteNonQuery();
            }
        }

        private void InsertCal5Record(SQLiteConnection connection, long logId, LogEntry record)
        {
            var sql = @"
                INSERT INTO Cal5Records (
                    LogRecordId, SOH, Current, Power, ChargeEnergy,
                    Voltage, DeviceStatus, SOC, DisChargeEnergy,
                    ChannelNum, DeviceKey
                ) VALUES (
                    @LogRecordId, @SOH, @Current, @Power, @ChargeEnergy,
                    @Voltage, @DeviceStatus, @SOC, @DisChargeEnergy,
                    @ChannelNum, @DeviceKey
                )";

            using (var cmd = new SQLiteCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@LogRecordId", logId);
                cmd.Parameters.AddWithValue("@DeviceKey", record.Parameters.GetValueOrDefault("Key"));

                AddDoubleParameter(cmd, "@SOH", "SOH", record);
                AddDoubleParameter(cmd, "@Current", "Current", record);
                AddDoubleParameter(cmd, "@Power", "Power", record);
                AddDoubleParameter(cmd, "@ChargeEnergy", "ChargeEnergy", record);
                AddDoubleParameter(cmd, "@Voltage", "Voltage", record);
                AddDoubleParameter(cmd, "@SOC", "SOC", record);
                AddDoubleParameter(cmd, "@DisChargeEnergy", "DisChargeEnergy", record);

                if (record.Parameters.TryGetValue("DeviceStatus", out var status))
                    cmd.Parameters.AddWithValue("@DeviceStatus", int.Parse(status));

                if (record.Parameters.TryGetValue("ChannelNum", out var channel))
                    cmd.Parameters.AddWithValue("@ChannelNum", int.Parse(channel));

                cmd.ExecuteNonQuery();
            }
        }

        private void InsertCal2Record(SQLiteConnection connection, long logId, LogEntry record)
        {
            var sql = @"
                INSERT INTO Cal2Records (
                    LogRecordId, Condition, Pac0, PreviewPAC,
                    PacMin, PacMax, KP, MaxFrequencyPower, Pace0
                ) VALUES (
                    @LogRecordId, @Condition, @Pac0, @PreviewPAC,
                    @PacMin, @PacMax, @KP, @MaxFrequencyPower, @Pace0
                )";

            using (var cmd = new SQLiteCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@LogRecordId", logId);
                cmd.Parameters.AddWithValue("@Condition", record.Parameters.GetValueOrDefault("Condition"));

                AddDoubleParameter(cmd, "@Pac0", "pac0", record);
                AddDoubleParameter(cmd, "@PreviewPAC", "previewPAC", record);
                AddDoubleParameter(cmd, "@PacMin", "pacMin", record);
                AddDoubleParameter(cmd, "@PacMax", "pacMax", record);
                AddDoubleParameter(cmd, "@KP", "kp", record);
                AddDoubleParameter(cmd, "@MaxFrequencyPower", "maxFrequencyPower", record);
                AddDoubleParameter(cmd, "@Pace0", "pace0", record);

                cmd.ExecuteNonQuery();
            }
        }

        private void InsertCal1Record(SQLiteConnection connection, long logId, LogEntry record)
        {
            var sql = @"
                INSERT INTO Cal1Records (
                    LogRecordId, Condition, Pac0, PreviewPAC, Csoc,
                    Paclm1, Paclm2, Paclm5, Paclm6, KP,
                    SocMin, SocMax, MaxFrequencyPower
                ) VALUES (
                    @LogRecordId, @Condition, @Pac0, @PreviewPAC, @Csoc,
                    @Paclm1, @Paclm2, @Paclm5, @Paclm6, @KP,
                    @SocMin, @SocMax, @MaxFrequencyPower
                )";

            using (var cmd = new SQLiteCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@LogRecordId", logId);
                cmd.Parameters.AddWithValue("@Condition", record.Parameters.GetValueOrDefault("Condition"));

                AddDoubleParameter(cmd, "@Pac0", "pac0", record);
                AddDoubleParameter(cmd, "@PreviewPAC", "previewPAC", record);
                AddDoubleParameter(cmd, "@Csoc", "csoc", record);
                AddDoubleParameter(cmd, "@Paclm1", "paclm1", record);
                AddDoubleParameter(cmd, "@Paclm2", "paclm2", record);
                AddDoubleParameter(cmd, "@Paclm5", "paclm5", record);
                AddDoubleParameter(cmd, "@Paclm6", "paclm6", record);
                AddDoubleParameter(cmd, "@KP", "kp", record);
                AddDoubleParameter(cmd, "@SocMin", "socMin", record);
                AddDoubleParameter(cmd, "@SocMax", "socMax", record);
                AddDoubleParameter(cmd, "@MaxFrequencyPower", "maxFrequencyPower", record);

                cmd.ExecuteNonQuery();
            }
        }

        private void InsertCal3Record(SQLiteConnection connection, long logId, LogEntry record)
        {
            var sql = @"
                INSERT INTO Cal3Records (
                    LogRecordId, Condition, Pc1n, TotalScale,
                    CurrentValue, Soc
                ) VALUES (
                    @LogRecordId, @Condition, @Pc1n, @TotalScale,
                    @CurrentValue, @Soc
                )";

            using (var cmd = new SQLiteCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@LogRecordId", logId);
                cmd.Parameters.AddWithValue("@Condition", record.Parameters.GetValueOrDefault("Condition"));

                AddDoubleParameter(cmd, "@Pc1n", "pc1n", record);
                AddDoubleParameter(cmd, "@TotalScale", "totalScale", record);
                AddDoubleParameter(cmd, "@CurrentValue", "CurrentValue", record);
                AddDoubleParameter(cmd, "@Soc", "soc", record);

                cmd.ExecuteNonQuery();
            }
        }

        private void InsertCal4Record(SQLiteConnection connection, long logId, LogEntry record)
        {
            var sql = @"
                INSERT INTO Cal4Records (
                    LogRecordId, Condition, DataType, JsonData,
                    Receivers, CurrentValue, LimitCurrent, ChannelNum
                ) VALUES (
                    @LogRecordId, @Condition, @DataType, @JsonData,
                    @Receivers, @CurrentValue, @LimitCurrent, @ChannelNum
                )";

            using (var cmd = new SQLiteCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@LogRecordId", logId);
                cmd.Parameters.AddWithValue("@Condition", record.Parameters.GetValueOrDefault("Condition"));
                cmd.Parameters.AddWithValue("@JsonData", record.Parameters.GetValueOrDefault("Json"));
                cmd.Parameters.AddWithValue("@Receivers", record.Parameters.GetValueOrDefault("Receivers"));

                if (record.Parameters.TryGetValue("DataType", out var dataType))
                    cmd.Parameters.AddWithValue("@DataType", int.Parse(dataType.Replace("DataType：", "")));

                if (record.Parameters.TryGetValue("CurrentValue", out var currentValue))
                    cmd.Parameters.AddWithValue("@CurrentValue", double.Parse(currentValue));

                if (record.Parameters.TryGetValue("LimitCurrent", out var limitCurrent))
                    cmd.Parameters.AddWithValue("@LimitCurrent", double.Parse(limitCurrent));

                if (record.Parameters.TryGetValue("ChannelNum", out var channel))
                    cmd.Parameters.AddWithValue("@ChannelNum", int.Parse(channel));

                cmd.ExecuteNonQuery();
            }
        }

        private void InsertNoneRecord(SQLiteConnection connection, long logId, LogEntry record)
        {
            var sql = "INSERT INTO NoneRecords (LogRecordId, ScheduleInfo) VALUES (@LogRecordId, @ScheduleInfo)";

            using (var cmd = new SQLiteCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@LogRecordId", logId);

                // 提取schedule信息
                var scheduleMatch = Regex.Match(record.RawData, @"Start one schedual：(.+)$");
                if (scheduleMatch.Success)
                {
                    cmd.Parameters.AddWithValue("@ScheduleInfo", scheduleMatch.Groups[1].Value);
                }
                else
                {
                    cmd.Parameters.AddWithValue("@ScheduleInfo", DBNull.Value);
                }

                cmd.ExecuteNonQuery();
            }
        }

        private DateTime ParseDateTime(string dateTimeStr)
        {
            if (DateTime.TryParseExact(dateTimeStr, "yyyy-MM-dd HH:mm:ss-fff",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            {
                return result;
            }

            return DateTime.Parse(dateTimeStr);
        }

        private void AddDoubleParameter(SQLiteCommand cmd, string paramName, string key, LogEntry record)
        {
            if (record.Parameters.TryGetValue(key, out var value))
            {
                if (double.TryParse(value, out var doubleValue))
                {
                    cmd.Parameters.AddWithValue(paramName, doubleValue);
                }
                else
                {
                    cmd.Parameters.AddWithValue(paramName, DBNull.Value);
                }
            }
            else
            {
                cmd.Parameters.AddWithValue(paramName, DBNull.Value);
            }
        }
    }
}