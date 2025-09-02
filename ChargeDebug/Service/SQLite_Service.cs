using DataModel;
using DbcParserLib.Model;
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

        #region ModbusSignals表操作

        /// <summary>
        /// 获取指定DBC文件的所有信号
        /// </summary>
        public static List<ModbusSignal> GetModbusRegistersByDbc(SQLiteConnection conn, long dbcFileId)
        {
            var signals = new List<ModbusSignal>();

            using (var cmd = new SQLiteCommand(@"
                       SELECT SignalID, SignalName, FunctionCode, RegisterAddress, RegisterCount, 
                       SystemVariableName, Unit, ByteOrder, Signed, Factor, Offset, ValueRange, Orders,
                       CreatedDate, ModifiedDate
                       FROM ModbusSignals 
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
                            SignalName = reader.GetString(1),
                            FunctionCode = reader.GetString(2),
                            RegisterAddress = reader.GetString(3),
                            RegisterCount = reader.GetInt32(4),
                            SystemVariableName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Unit = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            ByteOrder = reader.IsDBNull(7) ? "Inter" : reader.GetString(7),
                            Signed = reader.IsDBNull(8) ? "Unsigned" : reader.GetString(8),
                            Factor = reader.IsDBNull(9) ? 1.0 : reader.GetDouble(9),
                            Offset = reader.IsDBNull(10) ? 0.0 : reader.GetDouble(10),
                            ValueRange = reader.IsDBNull(11) ? "" : reader.GetString(11),
                            Orders = reader.GetInt32(12),
                            CreatedDate = reader.GetDateTime(13),
                            ModifiedDate = reader.GetDateTime(14)
                        });
                    }
                }
            }

            return signals;
        }

        /// <summary>
        /// 插入或更新Modbus寄存器
        /// </summary>
        public static long UpsertModbusRegister(SQLiteConnection conn, long registerId, string address, string description,
                                              string dataType, string accessType, string value, string notes,
                                              int orders, long dbcFileId, SQLiteTransaction transaction = null)
        {
            SQLiteCommand cmd;

            if (registerId == -1)
            {
                // 插入新记录
                cmd = new SQLiteCommand(@"
                INSERT INTO ModbusRegisters 
                (DbcFileId, Address, Description, DataType, AccessType, Value, Notes, Orders) 
                VALUES (@dbcFileId, @address, @description, @dataType, @accessType, @value, @notes, @orders);
                SELECT last_insert_rowid();", conn, transaction);
            }
            else
            {
                // 更新现有记录
                cmd = new SQLiteCommand(@"
                UPDATE ModbusRegisters SET 
                Address = @address, Description = @description, DataType = @dataType, 
                AccessType = @accessType, Value = @value, Notes = @notes, Orders = @orders
                WHERE RegisterID = @registerId;
                SELECT @registerId;", conn, transaction);
                cmd.Parameters.AddWithValue("@registerId", registerId);
            }

            cmd.Parameters.AddWithValue("@dbcFileId", dbcFileId);
            cmd.Parameters.AddWithValue("@address", address);
            cmd.Parameters.AddWithValue("@description", description);
            cmd.Parameters.AddWithValue("@dataType", dataType);
            cmd.Parameters.AddWithValue("@accessType", accessType);
            cmd.Parameters.AddWithValue("@value", string.IsNullOrEmpty(value) ? DBNull.Value : (object)value);
            cmd.Parameters.AddWithValue("@notes", string.IsNullOrEmpty(notes) ? DBNull.Value : (object)notes);
            cmd.Parameters.AddWithValue("@orders", orders);

            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        /// <summary>
        /// 删除Modbus寄存器
        /// </summary>
        public static void DeleteModbusRegister(SQLiteConnection conn, long registerId, SQLiteTransaction transaction = null)
        {
            using (var cmd = new SQLiteCommand("DELETE FROM ModbusRegisters WHERE RegisterID = @registerId", conn, transaction))
            {
                cmd.Parameters.AddWithValue("@registerId", registerId);
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
                            model.BaudRate = reader["BaudRate"] is DBNull ? null : reader["BaudRate"].ToString();
                            model.DataBits = reader["DataBits"] is DBNull ? null : reader["DataBits"].ToString();
                            model.Parity = reader["Parity"] is DBNull ? null : reader["Parity"].ToString();
                            model.StopBits = reader["StopBits"] is DBNull ? null : reader["StopBits"].ToString();
                            // 处理可能为null的整数字段
                            model.DeviceIndex = reader["DeviceIndex"] is DBNull ? 0 : Convert.ToInt32(reader["DeviceIndex"]);
                            model.CanIndex = reader["CanIndex"] is DBNull ? 0 : Convert.ToInt32(reader["CanIndex"]);
                            model.ACNumber = reader["ACNumber"] is DBNull ? 0 : Convert.ToInt32(reader["ACNumber"]);
                            model.DCNumber = reader["DCNumber"] is DBNull ? 0 : Convert.ToInt32(reader["DCNumber"]);
                            
                            equipment.Add(model);
                        }
                    }
                }
            }
            return equipment;
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
