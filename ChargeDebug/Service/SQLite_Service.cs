using DataModel;
using DbcParserLib.Model;
using DevExpress.Pdf.Native.BouncyCastle.Cms;
using System.Data;
using System.Data.SQLite;

namespace ChargeDebug.Service
{
    public class SQLite_Service
    {
        #region DBC文件操作

        public static string GetProtocolType(SQLiteConnection conn, long fileId)
        {
            string sql = "SELECT AgreementsTypes FROM DbcFile WHERE DbcFileID = @DbcFileID";

            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@DbcFileID", fileId);

                var result = cmd.ExecuteScalar();
                return result?.ToString() ?? "CAN总线"; // 默认返回 "CAN" 以防万一
            }
        }

        /// <summary>
        /// 获取DBC文件ID
        /// </summary>
        public static long GetDbcFileId(SQLiteConnection conn, string? fileName)
        {
            const string sql = "SELECT DbcFileID FROM DbcFile WHERE DbcFileName = @name";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@name", fileName);
                object result = cmd.ExecuteScalar();
                return result != null ? (long)result : -1;
            }
        }

        /// <summary>
        /// 获取带完整字段的DBC文件列表（兼容旧代码）
        /// </summary>
        public static DataTable GetDbcFilesWithFullColumns(SQLiteConnection conn)
        {
            const string sql = @"SELECT 
                            DbcFileID AS 文件ID,
                            DbcFileName AS 文件名称,
                            AgreementsTypes AS 协议类型,
                            datetime(CreationTime, 'localtime') AS 创建时间
                            FROM DbcFile
                            ORDER BY CreationTime DESC";

            DataTable dt = new DataTable();
            using (var adapter = new SQLiteDataAdapter(sql, conn))
            {
                adapter.Fill(dt);
            }
            return dt;
        }

        /// <summary>
        /// 插入或更新DBC文件记录
        /// </summary>
        public static long UpsertDbcFile(SQLiteConnection conn, string fileName, string types)
        {
            const string sql = @"INSERT INTO DbcFile (DbcFileName,AgreementsTypes) 
                           VALUES (@name,@types)
                           ON CONFLICT(DbcFileName) DO UPDATE SET DbcFileName=DbcFileName
                           RETURNING DbcFileID;";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@name", fileName);
                cmd.Parameters.AddWithValue("@types", types);
                return (long)cmd.ExecuteScalar();
            }
        }

        /// <summary>
        /// 删除DBC文件及关联数据
        /// </summary>
        public static void DeleteDbcFile(SQLiteConnection conn, long fileId, SQLiteTransaction transaction = null)
        {
            const string deleteReuseSignalsSql = @"DELETE FROM ReuseSignals 
                                        WHERE SignalID IN (
                                            SELECT SignalID FROM Signals 
                                            WHERE MessageID IN (
                                            SELECT MessageID FROM Messages
                                            WHERE DbcFileID = @fileId))";
            const string deleteSignalsSql = @"DELETE FROM Signals 
                                        WHERE MessageID IN (
                                            SELECT MessageID FROM Messages 
                                            WHERE DbcFileID = @fileId
                                        )";
            const string deleteMessagesSql = "DELETE FROM Messages WHERE DbcFileID = @fileId";
            const string deleteFileSql = "DELETE FROM DbcFile WHERE DbcFileID = @fileId";
            
            using (var cmd = new SQLiteCommand(deleteReuseSignalsSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@fileId", fileId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(deleteSignalsSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@fileId", fileId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(deleteMessagesSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@fileId", fileId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(deleteFileSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@fileId", fileId);
                cmd.ExecuteNonQuery();
            }
        }

        public static void DeleteDbcFileMessages(SQLiteConnection conn, long fileId, SQLiteTransaction transaction = null)
        {
            const string deleteReuseSignalsSql = @"DELETE FROM ReuseSignals 
                                        WHERE SignalID IN (
                                            SELECT SignalID FROM Signals 
                                            WHERE MessageID IN (
                                            SELECT MessageID FROM Messages
                                            WHERE DbcFileID = @fileId))";
            const string deleteSignalsSql = @"DELETE FROM Signals 
                                        WHERE MessageID IN (
                                            SELECT MessageID FROM Messages 
                                            WHERE DbcFileID = @fileId
                                        )";
            const string deleteMessagesSql = "DELETE FROM Messages WHERE DbcFileID = @fileId";
            //const string deleteFileSql = "DELETE FROM DbcFile WHERE DbcFileID = @fileId";

            using (var cmd = new SQLiteCommand(deleteReuseSignalsSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@fileId", fileId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(deleteSignalsSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@fileId", fileId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(deleteMessagesSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@fileId", fileId);
                cmd.ExecuteNonQuery();
            }

            //using (var cmd = new SQLiteCommand(deleteFileSql, conn, transaction))
            //{
            //    cmd.Parameters.AddWithValue("@fileId", fileId);
            //    cmd.ExecuteNonQuery();
            //}
        }
        #endregion

        #region CalibrationSignals表操作
        /// <summary>
        /// 获取CalibrationSignals表
        /// </summary>
        public static List<CalibrationSignals> GetCalibrationSignals(SQLiteConnection conn)
        {
            var signals = new List<CalibrationSignals>();

            using (var cmd = new SQLiteCommand(@"SELECT * FROM CalibrationSignals ORDER BY [Orders] ASC", conn))
            {
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        signals.Add(new CalibrationSignals
                        {
                            SignalID = (long)reader["SignalID"],
                            DeviceName = reader["DeviceName"].ToString(),
                            SignalName = reader["SignalName"].ToString(),
                            SignalType = reader["SignalType"].ToString(),
                            ReadTime = Convert.ToInt32(reader["ReadTime"]),
                            RatingVoltageCurrent = Convert.ToInt32(reader["RatingVoltageCurrent"]),
                            CalibrationNumber = Convert.ToInt32(reader["CalibrationNumber"]),
                            Orders = Convert.ToInt32(reader["Orders"]),
                        });
                    }
                }
            }

            return signals;
        }

        /// <summary>
        /// 删除CalibrationSignals表
        /// </summary>
        public static bool DeleteCalibrationSignals(SQLiteConnection conn, List<long> signalIds)
        {
            try
            {
                // 使用IN子句批量删除
                string ids = string.Join(",", signalIds);
                string deleteSql = $"DELETE FROM CalibrationSignals WHERE SignalID IN ({ids})";

                using (var cmd = new SQLiteCommand(deleteSql, conn))
                {
                    int rowsAffected = cmd.ExecuteNonQuery();
                    return rowsAffected > 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool ReorderCalibrationSignals(SQLiteConnection conn)
        {
            try
            {
                // 获取所有剩余的信号，按当前Orders排序
                string selectSql = "SELECT SignalID FROM CalibrationSignals ORDER BY Orders";

                List<long> remainingSignalIds = new List<long>();
                using (var cmd = new SQLiteCommand(selectSql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        remainingSignalIds.Add(reader.GetInt64(0));
                    }
                }

                // 更新Orders为连续的顺序
                for (int i = 0; i < remainingSignalIds.Count; i++)
                {
                    string updateSql = "UPDATE CalibrationSignals SET Orders = @NewOrder WHERE SignalID = @SignalID";
                    using (var updateCmd = new SQLiteCommand(updateSql, conn))
                    {
                        updateCmd.Parameters.AddWithValue("@NewOrder", i);
                        updateCmd.Parameters.AddWithValue("@SignalID", remainingSignalIds[i]);
                        updateCmd.ExecuteNonQuery();
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 更新CalibrationSignals表
        /// </summary>

        public static long UpsertCalibrationSignals(SQLiteConnection conn, long signalID, string? deviceName,
            string? signalName, string? signalType, long readTime, string? ratingVoltageCurrent, 
            long calibrationNumber, long orders, SQLiteTransaction? transaction = null)
        {
            if (signalID < 0) // 新增
            {
                const string insertSql = @"INSERT INTO CalibrationSignals 
                                        (DeviceName, SignalName, SignalType, ReadTime, 
                                         RatingVoltageCurrent, CalibrationNumber, Orders)
                                        VALUES (@deviceName, @signalName, @signalType, @readTime, 
                                        @ratingVoltageCurrent, @calibrationNumber, @orders)
                                        RETURNING SignalID;";
                using (var cmd = new SQLiteCommand(insertSql, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@deviceName", deviceName);
                    cmd.Parameters.AddWithValue("@signalName", signalName);
                    cmd.Parameters.AddWithValue("@signalType", signalType);
                    cmd.Parameters.AddWithValue("@readTime", readTime);
                    cmd.Parameters.AddWithValue("@ratingVoltageCurrent", ratingVoltageCurrent);
                    cmd.Parameters.AddWithValue("@calibrationNumber", calibrationNumber);
                    cmd.Parameters.AddWithValue("@orders", orders);
                    return (long)cmd.ExecuteScalar();
                }
            }
            else // 更新
            {
                const string updateSql = @"UPDATE CalibrationSignals SET 
                                      DeviceName = @deviceName,
                                      SignalName = @signalName,
                                      SignalType = @signalType,
                                      ReadTime = @readTime,
                                      RatingVoltageCurrent = @ratingVoltageCurrent,
                                      CalibrationNumber = @calibrationNumber,
                                      Orders = @orders
                                      WHERE SignalID = @signalID";
                using (var cmd = new SQLiteCommand(updateSql, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@deviceName", deviceName);
                    cmd.Parameters.AddWithValue("@signalName", signalName);
                    cmd.Parameters.AddWithValue("@signalType", signalType);
                    cmd.Parameters.AddWithValue("@readTime", readTime);
                    cmd.Parameters.AddWithValue("@ratingVoltageCurrent", ratingVoltageCurrent);
                    cmd.Parameters.AddWithValue("@calibrationNumber", calibrationNumber);
                    cmd.Parameters.AddWithValue("@orders", orders);
                    cmd.Parameters.AddWithValue("@signalID", signalID);
                    cmd.ExecuteNonQuery();
                    return signalID;
                }
            }
        }

        // 根据ID获取校准信号
        public static CalibrationSignals GetCalibrationSignalById(SQLiteConnection conn, long signalId)
        {
            string query = "SELECT * FROM CalibrationSignals WHERE SignalID = @Id";

            using (var cmd = new SQLiteCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@Id", signalId);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new CalibrationSignals
                        {
                            SignalID = Convert.ToInt64(reader["SignalID"]),
                            DeviceName = reader["DeviceName"].ToString(),
                            SignalName = reader["SignalName"].ToString(),
                            SignalType = reader["SignalType"].ToString(),
                            ReadTime = Convert.ToInt32(reader["ReadTime"]),
                            RatingVoltageCurrent = Convert.ToInt32(reader["RatingVoltageCurrent"]),
                            CalibrationNumber = Convert.ToInt32(reader["CalibrationNumber"]),
                            Orders = Convert.ToInt32(reader["Orders"]) // 读取排序字段
                        };
                    }
                }
            }
            return null;
        }

        // 获取所有设备名称
        public static List<string> GetDeviceNames(SQLiteConnection conn)
        {
            var deviceNames = new List<string>();
            string query = "SELECT DISTINCT DeviceName FROM CalibrationSignals";

            using (var cmd = new SQLiteCommand(query, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    deviceNames.Add(reader["DeviceName"].ToString());
                }
            }
            return deviceNames;
        }

        // 获取最大排序值
        public static int GetMaxOrderValue(SQLiteConnection conn)
        {
            string query = "SELECT MAX(Orders) FROM CalibrationSignals";

            using (var cmd = new SQLiteCommand(query, conn))
            {
                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    return Convert.ToInt32(result);
                }
            }
            return 0; // 如果没有记录，返回0
        }

        // 更新校准信号
        public static void UpdateCalibrationSignal(SQLiteConnection conn, CalibrationSignals signal)
        {
            string updateSql = @"
                UPDATE CalibrationSignals 
                SET DeviceName = @DeviceName,
                SignalName = @SignalName,
                SignalType = @SignalType,
                ReadTime = @ReadTime,
                RatingVoltageCurrent = @RatingVoltageCurrent,
                CalibrationNumber = @CalibrationNumber,
                Orders = @Orders
                WHERE SignalID = @Id";

            using (var cmd = new SQLiteCommand(updateSql, conn))
            {
                cmd.Parameters.AddWithValue("@DeviceName", signal.DeviceName);
                cmd.Parameters.AddWithValue("@SignalName", signal.SignalName);
                cmd.Parameters.AddWithValue("@SignalType", signal.SignalType);
                cmd.Parameters.AddWithValue("@ReadTime", signal.ReadTime);
                cmd.Parameters.AddWithValue("@RatingVoltageCurrent", signal.RatingVoltageCurrent);
                cmd.Parameters.AddWithValue("@CalibrationNumber", signal.CalibrationNumber);
                cmd.Parameters.AddWithValue("@Orders", signal.Orders);
                cmd.Parameters.AddWithValue("@Id", signal.SignalID);

                cmd.ExecuteNonQuery();
            }
        }

        // 插入新校准信号
        public static void InsertCalibrationSignal(SQLiteConnection conn, CalibrationSignals signal)
        {
            string insertSql = @"
            INSERT INTO CalibrationSignals 
                (DeviceName, SignalName, SignalType, ReadTime, RatingVoltageCurrent, CalibrationNumber, Orders )
            VALUES 
                (@DeviceName, @SignalName, @SignalType, @ReadTime, @RatingVoltageCurrent, @CalibrationNumber, @Orders )";

            using (var cmd = new SQLiteCommand(insertSql, conn))
            {
                cmd.Parameters.AddWithValue("@DeviceName", signal.DeviceName);
                cmd.Parameters.AddWithValue("@SignalName", signal.SignalName);
                cmd.Parameters.AddWithValue("@SignalType", signal.SignalType);
                cmd.Parameters.AddWithValue("@ReadTime", signal.ReadTime);
                cmd.Parameters.AddWithValue("@RatingVoltageCurrent", signal.RatingVoltageCurrent);
                cmd.Parameters.AddWithValue("@CalibrationNumber", signal.CalibrationNumber);
                cmd.Parameters.AddWithValue("@Orders", signal.Orders);

                cmd.ExecuteNonQuery();
            }
        }

        // 获取信号的所有校准点数量
        public static int GetCalibrationPointsCount(SQLiteConnection conn, long signalId)
        {
            string query = "SELECT COUNT(*) FROM CalibrationPoints WHERE SignalID = @SignalID";

            using (var cmd = new SQLiteCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@SignalID", signalId);
                var result = cmd.ExecuteScalar();
                return Convert.ToInt32(result);
            }
        }

        // 删除信号的所有校准点
        public static void DeleteCalibrationPoints(SQLiteConnection conn, long signalId)
        {
            string deleteSql = "DELETE FROM CalibrationPoints WHERE SignalID = @SignalID";

            using (var cmd = new SQLiteCommand(deleteSql, conn))
            {
                cmd.Parameters.AddWithValue("@SignalID", signalId);
                cmd.ExecuteNonQuery();
            }
        }

        #endregion

        #region ModbusSignals表操作

        /// <summary>
        /// 获取指定DBC文件的所有信号
        /// </summary>
        public static List<ModbusSignal> GetModbusSignalsByDbc(SQLiteConnection conn, long dbcFileId)
        {
            var signals = new List<ModbusSignal>();

            using (var cmd = new SQLiteCommand(@"
                       SELECT * FROM ModbusSignals 
                       WHERE DbcFileId = @dbcFileId 
                       ORDER BY Orders", conn))
            {
                cmd.Parameters.AddWithValue("@dbcFileId", dbcFileId);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        signals.Add(new ModbusSignal
                        {
                            SignalID = reader.GetInt64(0),
                            SignalName = reader.GetString(2),
                            CorrespondenceAddress = reader.GetString(3),
                            FunctionCode = reader.GetString(4),
                            RegisterAddress = reader.GetString(5),
                            RegisterCount = reader.GetInt32(6),
                            SystemVariableName = reader.IsDBNull(7) ? "" : reader.GetString(7),
                            Unit = reader.IsDBNull(8) ? "" : reader.GetString(8),
                            ByteOrder = reader.IsDBNull(9) ? "Inter" : reader.GetString(9),
                            Signed = reader.IsDBNull(10) ? "Unsigned" : reader.GetString(10),
                            Factor = reader.IsDBNull(11) ? 1.0 : reader.GetDouble(11),
                            Offset = reader.IsDBNull(12) ? 0.0 : reader.GetDouble(12),
                            ValueRange = reader.IsDBNull(13) ? "" : reader.GetString(13),
                            Orders = reader.GetInt32(14)
                        });
                    }
                }
            }

            return signals;
        }

        /// <summary>
        /// 插入或更新Modbus寄存器
        /// </summary>
        public static long UpsertModbusSignal(SQLiteConnection conn, 
            long signalId, long dbcFileId,string signalName, string correspondenceAddress, string functionCode, string registerAddress,
            int registerCount, string systemVariableName, string unit,string byteOrder, string signed, 
            double factor, double offset,string valueRange, int orders, SQLiteTransaction transaction = null)
        {
            if (signalId < 0) // 新增
            {
                const string insertSql = @"INSERT INTO ModbusSignals 
                                      (DbcFileId, SignalName, CorrespondenceAddress, FunctionCode, RegisterAddress, RegisterCount, 
                                       SystemVariableName, Unit, ByteOrder, Signed, Factor, Offset, ValueRange, Orders)
                                      VALUES (@dbcFileId, @signalName, @correspondenceAddress, @functionCode, @registerAddress, @registerCount,
                                              @systemVariableName, @unit, @byteOrder, @signed, @factor, @offset, @valueRange, @orders)
                                      RETURNING SignalID;";

                using (var cmd = new SQLiteCommand(insertSql, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@dbcFileId", dbcFileId);
                    cmd.Parameters.AddWithValue("@signalName", signalName);
                    cmd.Parameters.AddWithValue("@correspondenceAddress", correspondenceAddress);
                    cmd.Parameters.AddWithValue("@functionCode", functionCode);
                    cmd.Parameters.AddWithValue("@registerAddress", registerAddress);
                    cmd.Parameters.AddWithValue("@registerCount", registerCount);
                    cmd.Parameters.AddWithValue("@systemVariableName", systemVariableName);
                    cmd.Parameters.AddWithValue("@unit", unit);
                    cmd.Parameters.AddWithValue("@byteOrder",byteOrder);
                    cmd.Parameters.AddWithValue("@signed", signed);
                    cmd.Parameters.AddWithValue("@factor", factor);
                    cmd.Parameters.AddWithValue("@offset", offset);
                    cmd.Parameters.AddWithValue("@valueRange", valueRange);
                    cmd.Parameters.AddWithValue("@orders", orders);
                    return (long)cmd.ExecuteScalar();
                }
            }
            else
            {
                const string updateSql = @"UPDATE ModbusSignals SET
                                           DbcFileId = @dbcFileId,
                                           SignalName = @signalName,
                                           CorrespondenceAddress = @correspondenceAddress,
                                           FunctionCode = @functionCode,
                                           RegisterAddress = @registerAddress, 
                                           RegisterCount = @registerCount, 
                                           SystemVariableName = @systemVariableName, 
                                           Unit = @unit, 
                                           ByteOrder = @byteOrder, 
                                           Signed = @signed, 
                                           Factor = @factor, 
                                           Offset = @offset,
                                           ValueRange = @valueRange, 
                                           Orders = @orders
                                           WHERE SignalID = @id";
                
                using (var cmd = new SQLiteCommand(updateSql, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@id", signalId);
                    cmd.Parameters.AddWithValue("@dbcFileId", dbcFileId);
                    cmd.Parameters.AddWithValue("@signalName", signalName);
                    cmd.Parameters.AddWithValue("@correspondenceAddress", correspondenceAddress);
                    cmd.Parameters.AddWithValue("@functionCode", functionCode);
                    cmd.Parameters.AddWithValue("@registerAddress", registerAddress);
                    cmd.Parameters.AddWithValue("@registerCount", registerCount);
                    cmd.Parameters.AddWithValue("@systemVariableName", systemVariableName);
                    cmd.Parameters.AddWithValue("@unit", unit);
                    cmd.Parameters.AddWithValue("@byteOrder", byteOrder);
                    cmd.Parameters.AddWithValue("@signed", signed);
                    cmd.Parameters.AddWithValue("@factor", factor);
                    cmd.Parameters.AddWithValue("@offset", offset);
                    cmd.Parameters.AddWithValue("@valueRange", valueRange);
                    cmd.Parameters.AddWithValue("@orders", orders);
                    cmd.ExecuteNonQuery();
                    return signalId;
                }
            }
        }

        /// <summary>
        /// 删除Modbus寄存器
        /// </summary>
        public static void DeleteModbusSignals(SQLiteConnection conn, long signalID, SQLiteTransaction transaction = null)
        {
            using (var cmd = new SQLiteCommand("DELETE FROM ModbusSignals WHERE SignalID = @signalID", conn, transaction))
            {
                cmd.Parameters.AddWithValue("@signalID", signalID);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 更新Modbus寄存器排序
        /// </summary>
        public static void UpdateModbusRegisterOrder(SQLiteConnection conn, long registerId, int order, SQLiteTransaction transaction = null)
        {
            using (var cmd = new SQLiteCommand("UPDATE ModbusRegisters SET Orders = @order WHERE RegisterID = @registerId", conn, transaction))
            {
                cmd.Parameters.AddWithValue("@registerId", registerId);
                cmd.Parameters.AddWithValue("@order", order);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 获取Modbus寄存器的最大排序值
        /// </summary>
        public static int GetMaxModbusRegisterOrder(SQLiteConnection conn, long dbcFileId)
        {
            using (var cmd = new SQLiteCommand("SELECT MAX(Orders) FROM ModbusRegisters WHERE DbcFileId = @dbcFileId", conn))
            {
                cmd.Parameters.AddWithValue("@dbcFileId", dbcFileId);
                var result = cmd.ExecuteScalar();
                return result == DBNull.Value ? 0 : Convert.ToInt32(result);
            }
        }
        #endregion

        #region 报文操作

        /// <summary>
        /// 更新报文顺序
        /// </summary>
        public static void UpdateMessageOrder(SQLiteConnection conn, long messageId, int newOrder, SQLiteTransaction transaction = null)
        {
            const string sql = @"UPDATE Messages SET [Orders] = @order WHERE MessageID = @id";
            using (var cmd = new SQLiteCommand(sql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@order", newOrder);
                cmd.Parameters.AddWithValue("@id", messageId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 根据DBC文件ID查询所有相关的MessageID
        /// </summary>
        /// <param name="conn">SQLite连接</param>
        /// <param name="dbcFileId">DBC文件ID</param>
        /// <returns>MessageID列表，如果没有找到则返回空列表</returns>
        public static List<long> GetMessageIds(SQLiteConnection conn, long dbcFileId)
        {
            List<long> messageIds = new List<long>();

            string sql = @"SELECT MessageID FROM Messages
                   WHERE DbcFileID = @DbcFileID";

            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@DbcFileID", dbcFileId);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                        {
                            messageIds.Add(Convert.ToInt64(reader["MessageID"]));
                        }
                    }
                }
            }

            return messageIds;
        }

        /// <summary>
        /// 根据messageName查询MessageId的方法
        /// </summary>
        public static long GetMessageId(SQLiteConnection conn, long dbcFileId, string messageName)
        {
            string sql = @"SELECT MessageID FROM Messages
                           WHERE MessageName = @MessageName
                           AND DbcFileID = @DbcFileID";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@MessageName", messageName);
                cmd.Parameters.AddWithValue("@DbcFileID", dbcFileId);
                object result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt64(result) : -1;
            }
        }

        /// <summary>
        /// 获取指定DBC的所有报文
        /// </summary>
        public static List<MessageInfo> GetMessagesByDbc(SQLiteConnection conn, long dbcFileId)
        {
            var messages = new List<MessageInfo>();
            const string sql = @"SELECT * FROM Messages 
                                 WHERE DbcFileID = @fileId
                                 ORDER BY [orders] ASC";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@fileId", dbcFileId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        messages.Add(new MessageInfo
                        {
                            MessageID = (long)reader["MessageID"],
                            CANID = reader["CANID"].ToString(),
                            FrameType = reader["FrameType"].ToString(),
                            MessageName = reader["MessageName"].ToString(),
                            DataLength = Convert.ToInt32(reader["DataLength"]),
                            Orders = Convert.ToInt32(reader["orders"])
                        });
                    }
                }
            }
            return messages;
        }

        /// <summary>
        /// 插入或更新报文
        /// </summary>
        public static long UpsertMessage(SQLiteConnection conn, long messageId, string? canId,
            string? frameType, string? messageName, int dataLength, int orders, long dbcFileId,
            SQLiteTransaction? transaction = null)
        {
            if (messageId < 0) // 新增
            {
                const string insertSql = @"INSERT INTO Messages 
                                      (DbcFileID, CANID, FrameType, MessageName, DataLength, Orders)
                                      VALUES (@fileId, @canId, @frameType, @name, @len, @orders)
                                      RETURNING MessageID;";
                using (var cmd = new SQLiteCommand(insertSql, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@fileId", dbcFileId);
                    cmd.Parameters.AddWithValue("@canId", canId);
                    cmd.Parameters.AddWithValue("@frameType", frameType);
                    cmd.Parameters.AddWithValue("@name", messageName);
                    cmd.Parameters.AddWithValue("@len", dataLength);
                    cmd.Parameters.AddWithValue("@orders", orders);
                    return (long)cmd.ExecuteScalar();
                }
            }
            else // 更新
            {
                const string updateSql = @"UPDATE Messages SET 
                                      CANID = @canId,
                                      FrameType = @frameType,
                                      MessageName = @name,
                                      DataLength = @len,
                                      orders = @orders
                                      WHERE MessageID = @id";
                using (var cmd = new SQLiteCommand(updateSql, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@canId", canId);
                    cmd.Parameters.AddWithValue("@frameType", frameType);
                    cmd.Parameters.AddWithValue("@name", messageName);
                    cmd.Parameters.AddWithValue("@len", dataLength);
                    cmd.Parameters.AddWithValue("@orders", orders);
                    cmd.Parameters.AddWithValue("@id", messageId);
                    cmd.ExecuteNonQuery();
                    return messageId;
                }
            }
        }

        /// <summary>
        /// 删除指定报文及关联的信号
        /// </summary>
        public static void DeleteMessage(SQLiteConnection conn, long messageId, SQLiteTransaction transaction = null)
        {
            const string deleteReuseSignalSql = @"DELETE FROM ReuseSignals 
                                            WHERE SignalID IN (
                                            SELECT SignalID FROM Signals 
                                            WHERE MessageID = @id)";
            const string deleteSignalSql = "DELETE FROM Signals WHERE MessageID = @id";
            const string deleteMessageSql = "DELETE FROM Messages WHERE MessageID = @id";
            
            using (var cmd = new SQLiteCommand(deleteReuseSignalSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@id", messageId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(deleteSignalSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@id", messageId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(deleteMessageSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@id", messageId);
                cmd.ExecuteNonQuery();
            }
        }

        // 在SQLite_Service类中添加以下方法

        /* 更新报文排序 */
        public static void UpdateMessageSortOrder(SQLiteConnection conn, long messageId, int orders)
        {
            const string sql = @"UPDATE Messages SET Orders = @orders WHERE MessageID = @messageId";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@orders", orders);
                cmd.Parameters.AddWithValue("@messageId", messageId);
                cmd.ExecuteNonQuery();
            }
        }

        /* 更新信号排序 */
        public static void UpdateSignalSortOrder(SQLiteConnection conn, long signalId, int order)
        {
            const string sql = @"UPDATE Signals SET orders = @order WHERE SignalID = @signalId";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@order", order);
                cmd.Parameters.AddWithValue("@signalId", signalId);
                cmd.ExecuteNonQuery();
            }
        }

        /* 获取最大排序值 */
        public static int GetMaxSortOrder(SQLiteConnection conn, string tableName, long? parentId = null)
        {
            string sql = $"SELECT MAX(Orders) FROM {tableName}";
            if (parentId.HasValue)
            {
                sql += " WHERE MessageID = @messageId";
            }

            using (var cmd = new SQLiteCommand(sql, conn))
            {
                if (parentId.HasValue)
                {
                    cmd.Parameters.AddWithValue("@messageId", parentId.Value);
                }

                object result = cmd.ExecuteScalar();
                return result == DBNull.Value ? 0 : Convert.ToInt32(result);
            }
        }

        #endregion

        #region 信号操作

        /// <summary>
        /// 更新信号顺序
        /// </summary>
        public static void UpdateSignalOrder(SQLiteConnection conn, long signalId, int newOrder, SQLiteTransaction transaction = null)
        {
            const string sql = @"UPDATE Signals SET [Orders] = @order WHERE SignalID = @id";
            using (var cmd = new SQLiteCommand(sql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@order", newOrder);
                cmd.Parameters.AddWithValue("@id", signalId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 根据multiplexSignals查询SignalId的方法
        /// </summary>
        public static long GetSignalId(SQLiteConnection conn, long messageId, string name, string type)
        {
            string sql = "";
            if (type == "MultiplexSignals")
            {
                sql = @"SELECT SignalID FROM Signals
                           WHERE MessageID = @messageID
                           AND MultiplexSignals = @multiplexSignals";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@messageID", messageId);
                    cmd.Parameters.AddWithValue("@multiplexSignals", name);
                    object result = cmd.ExecuteScalar();
                    return result != null ? Convert.ToInt64(result) : -1;
                }
            }
            else if (type == "SignalName")
            {
                sql = @"SELECT SignalID FROM Signals
                           WHERE MessageID = @messageID
                           AND SignalName = @signalName";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@messageID", messageId);
                    cmd.Parameters.AddWithValue("@signalName", name);
                    object result = cmd.ExecuteScalar();
                    return result != null ? Convert.ToInt64(result) : -1;
                }
            }
            else
                return 0;
        }

        /// <summary>
        /// 获取报文下的所有信号
        /// </summary>
        public static List<SignalInfo> GetSignalsByMessage(SQLiteConnection conn, long messageId)
        {
            var signals = new List<SignalInfo>();
            const string sql = @"SELECT 
                            s.SignalID, s.SignalName,
                            s.MultiplexSignals, s.SystemName,
                            s.Unit, s.StartBit,
                            s.Length, s.ByteOrder,
                            s.Signed, s.Factor,
                            s.Offset, s.MinMax,
                            s.orders, m.CANID 
                            FROM Signals s JOIN Messages m 
                            ON s.MessageID = m.MessageID
                            WHERE s.MessageID = @msgId
                            ORDER BY s.orders ASC";

            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@msgId", messageId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var signal = new SignalInfo
                        {
                            SignalID = (long)reader["SignalID"],
                            SignalName = reader["SignalName"].ToString(),
                            MultiplexSignals = reader["MultiplexSignals"].ToString(),
                            SystemName = reader["SystemName"].ToString(),
                            Unit = reader["Unit"].ToString(),
                            StartBit = Convert.ToInt32(reader["StartBit"]),
                            Length = Convert.ToInt32(reader["Length"]),
                            ByteOrder = reader["ByteOrder"].ToString(),
                            Signed = reader["Signed"].ToString(),
                            Factor = Convert.ToDecimal(reader["Factor"]),
                            Offset = Convert.ToDecimal(reader["Offset"]),
                            MinMax = reader["MinMax"].ToString(),
                            Orders = Convert.ToInt32(reader["orders"]),
                            CANID = reader["CANID"].ToString()
                        };
                        // 加载复用信号配置
                        signal.ReuseSignals = GetReuseSignalsBySignals(conn, signal.SignalID);
                        signals.Add(signal);
                    }
                }
            }
            return signals;
        }

        /// <summary>
        /// 根据MessageID和SystemName查询特定信号的所有信息
        /// </summary>
        public static SignalInfo GetSignalByMessageAndSystemName(SQLiteConnection conn, long messageId, string systemName)
        {
            const string sql = @"SELECT 
                    s.SignalID, s.SignalName,
                    s.MultiplexSignals, s.SystemName,
                    s.Unit, s.StartBit,
                    s.Length, s.ByteOrder,
                    s.Signed, s.Factor,
                    s.Offset, s.MinMax,
                    s.orders, m.CANID 
                    FROM Signals s JOIN Messages m 
                    ON s.MessageID = m.MessageID
                    WHERE s.MessageID = @msgId AND s.SystemName = @systemName";

            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@msgId", messageId);
                cmd.Parameters.AddWithValue("@systemName", systemName);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var signal = new SignalInfo
                        {
                            SignalID = (long)reader["SignalID"],
                            SignalName = reader["SignalName"].ToString(),
                            MultiplexSignals = reader["MultiplexSignals"].ToString(),
                            SystemName = reader["SystemName"].ToString(),
                            Unit = reader["Unit"].ToString(),
                            StartBit = Convert.ToInt32(reader["StartBit"]),
                            Length = Convert.ToInt32(reader["Length"]),
                            ByteOrder = reader["ByteOrder"].ToString(),
                            Signed = reader["Signed"].ToString(),
                            Factor = Convert.ToDecimal(reader["Factor"]),
                            Offset = Convert.ToDecimal(reader["Offset"]),
                            MinMax = reader["MinMax"].ToString(),
                            Orders = Convert.ToInt32(reader["orders"]),
                            CANID = reader["CANID"].ToString()
                        };
                        // 加载复用信号配置
                        signal.ReuseSignals = GetReuseSignalsBySignals(conn, signal.SignalID);
                        return signal;
                    }
                }
            }

            return null; // 如果没有找到匹配的信号，返回null
        }

        /// <summary>
        /// 根据SystemName获取报文下的信号
        /// </summary>
        public static List<SignalInfo> GetSignalsByMessage(SQLiteConnection conn, long messageID, string? systemName)
        {
            List<SignalInfo> signals = new List<SignalInfo>();

            const string query = @"SELECT * FROM Signals 
                                   WHERE MessageID = @messageID
                                   AND SystemName == @systemName
                                   ORDER BY [orders] ASC";

            using (var cmd = new SQLiteCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@messageID", messageID);
                cmd.Parameters.AddWithValue("@systemName", systemName);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        signals.Add(new SignalInfo
                        {
                            //SignalID = (long)reader["SignalID"],
                            SignalName = reader["SignalName"].ToString(),
                            //MultiplexSignals = reader["MultiplexSignals"].ToString(),
                            SystemName = reader["SystemName"].ToString(),
                            Unit = reader["Unit"].ToString(),
                            StartBit = Convert.ToInt32(reader["StartBit"]),
                            Length = Convert.ToInt32(reader["Length"]),
                            ByteOrder = reader["ByteOrder"].ToString(),
                            Signed = reader["Signed"].ToString(),
                            Factor = Convert.ToDecimal(reader["Factor"]),
                            Offset = Convert.ToDecimal(reader["Offset"]),
                            MinMax = reader["MinMax"].ToString(),
                            //Orders = Convert.ToInt32(reader["orders"]),
                            //CANID = reader["CANID"].ToString()
                        });
                    }
                }
            }
            return signals;
        }

        /// <summary>
        /// 插入或更新信号
        /// </summary>
        public static long UpsertSignal(SQLiteConnection conn, long signalId, long messageId,
            string? signalName, string? multiplexSignals, string? systemName, string? unit,
            int startBit, int length, string? byteOrder, string? signed, decimal factor,
            decimal offset, string? minMax, int orders, SQLiteTransaction? transaction = null)
        {
            if (signalId < 0) // 新增
            {
                const string insertSql = @"INSERT INTO Signals 
                                      (MessageID, SignalName, MultiplexSignals, SystemName, Unit,
                                      StartBit, Length, ByteOrder, Signed, Factor, Offset, MinMax, orders)
                                      VALUES (@msgId, @name, @multi, @sysName, @unit, @start,
                                      @len, @order, @signed, @factor, @offset, @minmax, @orders)
                                      RETURNING SignalID;";
                using (var cmd = new SQLiteCommand(insertSql, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@msgId", messageId);
                    cmd.Parameters.AddWithValue("@name", signalName);
                    cmd.Parameters.AddWithValue("@multi", multiplexSignals);
                    cmd.Parameters.AddWithValue("@sysName", systemName);
                    cmd.Parameters.AddWithValue("@unit", unit);
                    cmd.Parameters.AddWithValue("@start", startBit);
                    cmd.Parameters.AddWithValue("@len", length);
                    cmd.Parameters.AddWithValue("@order", byteOrder);
                    cmd.Parameters.AddWithValue("@signed", signed);
                    cmd.Parameters.AddWithValue("@factor", factor);
                    cmd.Parameters.AddWithValue("@offset", offset);
                    cmd.Parameters.AddWithValue("@minmax", minMax);
                    cmd.Parameters.AddWithValue("@orders", orders);
                    return (long)cmd.ExecuteScalar();
                }
            }
            else // 更新
            {
                const string updateSql = @"UPDATE Signals SET
                                      SignalName = @name,
                                      MultiplexSignals = @multi,
                                      SystemName = @sysName,
                                      Unit = @unit,
                                      StartBit = @start,
                                      Length = @len,
                                      ByteOrder = @order,
                                      Signed = @signed,
                                      Factor = @factor,
                                      Offset = @offset,
                                      MinMax = @minmax,
                                      Orders = @orders
                                      WHERE SignalID = @id";
                using (var cmd = new SQLiteCommand(updateSql, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@name", signalName);
                    cmd.Parameters.AddWithValue("@multi", multiplexSignals);
                    cmd.Parameters.AddWithValue("@sysName", systemName);
                    cmd.Parameters.AddWithValue("@unit", unit);
                    cmd.Parameters.AddWithValue("@start", startBit);
                    cmd.Parameters.AddWithValue("@len", length);
                    cmd.Parameters.AddWithValue("@order", byteOrder);
                    cmd.Parameters.AddWithValue("@signed", signed);
                    cmd.Parameters.AddWithValue("@factor", factor);
                    cmd.Parameters.AddWithValue("@offset", offset);
                    cmd.Parameters.AddWithValue("@minmax", minMax);
                    cmd.Parameters.AddWithValue("@orders", orders);
                    cmd.Parameters.AddWithValue("@id", signalId);
                    cmd.ExecuteNonQuery();
                    return signalId;
                }
            }
        }

        /// <summary>
        /// 删除指定信号
        /// </summary>
        public static void DeleteSignal(SQLiteConnection conn, long signalId, SQLiteTransaction transaction = null)
        {
            const string sql = "DELETE FROM Signals WHERE SignalID = @id";
            using (var cmd = new SQLiteCommand(sql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@id", signalId);
                cmd.ExecuteNonQuery();
            }
        }
        #endregion

        #region 复用信号操作
        /// <summary>
        /// 获取信号的复用信号配置
        /// </summary>
        public static List<ReuseSignal> GetReuseSignalsBySignals(SQLiteConnection conn, long signalsID)
        {
            List<ReuseSignal> reuseSignal = new List<ReuseSignal>();

            const string query = @"SELECT * FROM ReuseSignals 
                                   WHERE SignalID = @signalID
                                   ORDER BY [orders] ASC";

            using (var cmd = new SQLiteCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@signalID", signalsID);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        reuseSignal.Add(new ReuseSignal
                        {
                            Value = reader["Value"].ToString(),
                            Description = reader["Description"].ToString(),
                            Orders = Convert.ToInt32(reader["orders"])
                        });
                    }
                }
            }
            return reuseSignal;
        }

        /// <summary>
        /// 保存复用信号配置
        /// </summary>
        public static void SaveReuseSignals(SQLiteConnection conn, long signalId,
            IEnumerable<ReuseSignal> reusesignals, SQLiteTransaction transaction = null)
        {
            // 先清空旧数据
            const string deleteSql = "DELETE FROM ReuseSignals WHERE SignalID = @sigId";
            using (var cmd = new SQLiteCommand(deleteSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@sigId", signalId);
                cmd.ExecuteNonQuery();
            }

            // 插入新数据
            const string insertSql = @"INSERT INTO ReuseSignals 
                                    (SignalID, Value, Description, orders)
                                    VALUES (@sigId, @value, @desc, @orders)";
            foreach (var signal in reusesignals)
            {
                using (var cmd = new SQLiteCommand(insertSql, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@sigId", signalId);
                    cmd.Parameters.AddWithValue("@value", signal.Value);
                    cmd.Parameters.AddWithValue("@desc", signal.Description);
                    cmd.Parameters.AddWithValue("@orders", signal.Orders);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 删除指定复用信号
        /// </summary>
        public static int DeleteReuseSignals(SQLiteConnection conn, long signalID)
        {
            const string query = @"DELETE FROM ReuseSignals 
                                   WHERE SignalID = @sigID";
            using (var cmdDelete = new SQLiteCommand(query, conn))
            {
                cmdDelete.Parameters.AddWithValue("@sigID", signalID);
                return cmdDelete.ExecuteNonQuery();
            }
        }
        #endregion

        #region Equipment设备管理操作
        /// <summary>
        /// 查询Equipment表中启用的设备配置
        /// </summary>
        public static List<EquipmentModel> GetEquipment(SQLiteConnection conn)
        {
            List<EquipmentModel> equipment = new List<EquipmentModel>();

            const string query = "SELECT * FROM Equipment ORDER BY [DeviceNumber] ASC";

            using (var cmd = new SQLiteCommand(query, conn))
            {
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        //if ((reader["Whether"].ToString() == "启用") || (reader["DeviceType"].ToString() == "充放电设备"))
                        if (reader["Whether"].ToString() == "启用")
                        {
                            // 创建模型实例
                            var model = new EquipmentModel
                            {
                                EquipmentID = Convert.ToInt32(reader["EquipmentID"]),
                                DeviceNumber = reader["DeviceNumber"]?.ToString(),
                                DeviceName = reader["DeviceName"]?.ToString(),
                                DeviceType = reader["DeviceType"]?.ToString(),
                                CanType = reader["CanType"]?.ToString(),
                                CommunicationProtocols = reader["CommunicationProtocols"]?.ToString()
                            };

                            // 处理可能为null的字段
                            model.DeviceIP = reader["DeviceIP"] is DBNull ? null : reader["DeviceIP"].ToString();
                            model.DevicePort = reader["DevicePort"] is DBNull ? null : reader["DevicePort"].ToString();
                            model.ACAddress = reader["ACAddress"] is DBNull ? null : reader["ACAddress"].ToString();
                            model.DCAddress = reader["DCAddress"] is DBNull ? null : reader["DCAddress"].ToString();
                            model.ComPort = reader["ComPort"] is DBNull ? null : reader["ComPort"].ToString();
                            model.BaudRate = reader["BaudRate"] is DBNull ? null : reader["BaudRate"].ToString();
                            model.DataBits = reader["DataBits"] is DBNull ? null : reader["DataBits"].ToString();
                            model.Parity = reader["Parity"] is DBNull ? null : reader["Parity"].ToString();
                            model.StopBits = reader["StopBits"] is DBNull ? null : reader["StopBits"].ToString();
                            // 处理可能为null的整数字段
                            model.DeviceIndex = reader["DeviceIndex"] is DBNull ? null : Convert.ToInt32(reader["DeviceIndex"]);
                            model.CanIndex = reader["CanIndex"] is DBNull ? null : Convert.ToInt32(reader["CanIndex"]);
                            model.ACNumber = reader["ACNumber"] is DBNull ? null : Convert.ToInt32(reader["ACNumber"]);
                            model.DCNumber = reader["DCNumber"] is DBNull ? null : Convert.ToInt32(reader["DCNumber"]);

                            equipment.Add(model);
                        }
                    }
                }
            }
            return equipment;
        }

        /// <summary>
        /// 根据设备名称及是否开启设备获取协议名称
        /// </summary>
        /// <param name="conn">SQLite连接</param>
        /// <param name="deviceName">设备名称</param>
        /// <returns>协议名称，如果找不到则返回null</returns>
        public static string GetProtocolNameByDeviceName(SQLiteConnection conn, string deviceName)
        {
            const string query = "SELECT CommunicationProtocols FROM Equipment WHERE DeviceName = @deviceName AND Whether = '启用'";

            using (var cmd = new SQLiteCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@deviceName", deviceName);

                object result = cmd.ExecuteScalar();
                return result != null ? result.ToString() : null;
            }
        }

        #endregion

        #region FaultRecording表操作
        public static List<FaultRecording> GetFaultRecording(SQLiteConnection conn)
        {
            var faultRecordings = new List<FaultRecording>();
            const string sql = "SELECT * FROM FaultRecording";

            using (var cmd = new SQLiteCommand(sql, conn))
            {
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        faultRecordings.Add(new FaultRecording
                        {
                            SignalName = reader["SignalName"].ToString(),
                            CANID = reader["CANID"].ToString(),
                            StartBit = Convert.ToInt32(reader["StartBit"]),
                            Length = Convert.ToInt32(reader["Length"]),
                            ByteOrder = reader["ByteOrder"].ToString(),
                            Signed = reader["Signed"].ToString(),
                            Factor = Convert.ToDecimal(reader["Factor"]),
                            Offset = Convert.ToDecimal(reader["Offset"]),
                            Unit = reader["Unit"].ToString()
                        });
                    }
                }
            }
            return faultRecordings;
        }

        public static List<FaultSignals> GetFaultSignals(SQLiteConnection conn)
        {
            var faultSignals = new List<FaultSignals>();
            const string sql = "SELECT * FROM FaultSignals";

            using (var cmd = new SQLiteCommand(sql, conn))
            {
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        faultSignals.Add(new FaultSignals
                        {
                            Signalname = reader["Signalname"].ToString(),
                            Startbit = Convert.ToInt32(reader["Startbit"]),
                            Length = Convert.ToInt32(reader["Length"]),
                            ByteOrder = reader["ByteOrder"].ToString(),
                            Signed = reader["Signed"].ToString(),
                            Factor = Convert.ToDecimal(reader["Factor"]),
                            Offset = Convert.ToDecimal(reader["Offset"])
                        });
                    }
                }
            }
            return faultSignals;
        }
        #endregion
    }
}
