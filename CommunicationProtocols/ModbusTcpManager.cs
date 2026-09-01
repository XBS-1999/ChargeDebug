using DataModel;
using Log;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

#pragma warning disable
namespace ChargeDebug.Service
{
    /*
     * TCP Modbus管理器核心类
     * 对齐CANManager全套架构逻辑
     * 共用：EquipmentModel 设备配置模型
     * 独立：ModbusSignalInfo 寄存器信号模型，不与CAN SignalInfo混用
     */
    public sealed class TcpModbusManager : IDisposable
    {
        #region Modbus TCP 底层常量 & 报文结构体
        public static class ModbusDefine
        {
            // 功能码
            public const byte FN_READ_COIL = 0x01;
            public const byte FN_READ_DISCRETE_INPUT = 0x02;
            public const byte FN_READ_HOLD_REG = 0x03;
            public const byte FN_READ_INPUT_REG = 0x04;
            public const byte FN_WRITE_SINGLE_COIL = 0x05;
            public const byte FN_WRITE_SINGLE_REG = 0x06;
            public const byte FN_WRITE_MULTI_COIL = 0x0F;
            public const byte FN_WRITE_MULTI_REG = 0x10;

            public const int STATUS_ERR = 0;
            public const int STATUS_OK = 1;
            public const int DEFAULT_PORT = 502;
            public const int RECV_BUFFER_SIZE = 2048;
            public const int SEND_BUFFER_SIZE = 1024;
        }

        /// <summary>
        /// Modbus接收报文（对应CAN ZCAN_Receive_Data）
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ModbusTcpReceiveData
        {
            public ushort TransactionId;
            public ushort ProtocolId;
            public ushort Length;
            public byte SlaveId;
            public byte FuncCode;
            public byte[] Data;
            public ulong Timestamp;

            public bool IsEmpty() => Data == null || Data.Length == 0;
        }

        /// <summary>
        /// Modbus发送报文（对应CAN ZCAN_Transmit_Data）
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ModbusTcpTransmitData
        {
            public ushort TransactionId;
            public byte SlaveId;
            public byte FuncCode;
            public ushort StartAddr;
            public ushort Count;
            public byte[] Data;
        }
        #endregion

        #region 单例实现（完全复刻CANManager）
        private static readonly Lazy<TcpModbusManager> _instance = new Lazy<TcpModbusManager>(() => new TcpModbusManager());
        public static TcpModbusManager Instance => _instance.Value;

        private TcpModbusManager()
        {
        }

        public void Init()
        {
            LogService.Log("ModbusTCP管理器初始化开始");
            _isRunning = true;

            _reconnectTimer?.Dispose();
            _reconnectTimer = new System.Threading.Timer(ReconnectCheckCallback, null, RECONNECT_INTERVAL, RECONNECT_INTERVAL);
        }

        private void ReconnectCheckCallback(object state)
        {
            try
            {
                var channelKeys = _equipmentInfo.Keys.ToList();
                foreach (var channelKey in channelKeys)
                {
                    bool isRegistered = _socketClients.ContainsKey(channelKey);
                    if (isRegistered && CheckTimeout(channelKey))
                    {
                        LogService.Log($"Modbus设备{channelKey} 通信超时，尝试重连...");
                        if (_equipmentInfo.TryGetValue(channelKey, out var equipment))
                        {
                            RegisterChannel(equipment, true);
                        }
                    }
                    else if (!isRegistered)
                    {
                        LogService.Log($"Modbus设备{channelKey} 未连接，尝试重连...");
                        if (_equipmentInfo.TryGetValue(channelKey, out var equipment))
                        {
                            RegisterChannel(equipment, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"Modbus重连检查异常: {ex.Message}");
            }
        }

        private bool CheckTimeout(string channelKey)
        {
            return _lastReceiveTime.TryGetValue(channelKey, out var lastTime) &&
                   (DateTime.Now - lastTime).TotalSeconds > 3;
        }

        private void UpdateConnectionStatus(string channelKey, bool isConnected)
        {
            _connectionStatus[channelKey] = isConnected;
            OnConnectionStatusChanged?.Invoke(channelKey, isConnected);
        }
        #endregion

        #region 字段容器（结构与CANManager一一对应）
        private static bool _globalSendingEnabled = true;
        private Thread _receiveThread;
        private bool _isRunning;
        private static readonly object _registerLock = new object();

        public event Action<string, bool> OnConnectionStatusChanged;

        private readonly ConcurrentDictionary<string, bool> _connectionStatus = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, DateTime> _lastReceiveTime = new ConcurrentDictionary<string, DateTime>();
        // 共用设备模型：EquipmentModel
        private readonly Dictionary<string, EquipmentModel> _equipmentInfo = new Dictionary<string, EquipmentModel>();
        private readonly Dictionary<string, TcpClient> _socketClients = new Dictionary<string, TcpClient>();

        private System.Threading.Timer _reconnectTimer;
        private const int RECONNECT_INTERVAL = 5000;

        public readonly ConcurrentDictionary<string, ConcurrentQueue<ModbusTcpReceiveData>> _receiveQueues =
            new ConcurrentDictionary<string, ConcurrentQueue<ModbusTcpReceiveData>>();

        private readonly ConcurrentDictionary<string, Action<List<ModbusTcpReceiveData>>> _dataHandlers =
            new ConcurrentDictionary<string, Action<List<ModbusTcpReceiveData>>>();

        // 独立信号字典：key=寄存器地址，value=Modbus专属信号列表（不和CAN SignalInfo混用）
        private readonly ConcurrentDictionary<ushort, List<ModbusSignalInfo>> _signalDefinitions =
            new ConcurrentDictionary<ushort, List<ModbusSignalInfo>>();
        #endregion

        #region 公共接口
        public bool IsChannelConnected(string channelKey)
        {
            try
            {
                if (_connectionStatus.TryGetValue(channelKey, out bool isConnected))
                    return isConnected;

                if (_socketClients.ContainsKey(channelKey))
                {
                    if (_receiveQueues.TryGetValue(channelKey, out var queue))
                        return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LogService.Log($"检查Modbus通道 {channelKey} 状态失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 注册/重连Modbus通道，入参共用EquipmentModel
        /// </summary>
        public void RegisterChannel(EquipmentModel equipment, bool isReconnect = false)
        {
            lock (_registerLock)
            {
                string key = GetChannelKey(equipment.DeviceIP, int.Parse(equipment.DevicePort), byte.Parse(equipment.CanIndex.ToString()));

                if (_socketClients.ContainsKey(key) && !isReconnect)
                {
                    UpdateConnectionStatus(key, true);
                    LogService.Log($"{equipment.DeviceName}已在线，跳过重复连接");
                    return;
                }
                else
                {
                    LogService.Log($"{equipment.DeviceName}断开，执行重连流程");
                }

                try
                {
                    if (isReconnect || _socketClients.ContainsKey(key))
                    {
                        UnregisterChannel(equipment.DeviceIP, int.Parse(equipment.DevicePort), byte.Parse(equipment.CanIndex.ToString()));
                        Thread.Sleep(10);
                    }

                    _equipmentInfo[key] = equipment;
                    UpdateConnectionStatus(key, false);

                    TcpClient client = new TcpClient();
                    client.ReceiveBufferSize = ModbusDefine.RECV_BUFFER_SIZE;
                    client.SendBufferSize = ModbusDefine.SEND_BUFFER_SIZE;
                    client.NoDelay = true;

                    bool connectOk = client.ConnectAsync(equipment.DeviceIP, int.Parse(equipment.DevicePort)).Wait(100);
                    if (!connectOk || !client.Connected)
                    {
                        LogService.Log($"{equipment.DeviceName} TCP连接失败");
                        client.Close();
                        return;
                    }

                    _socketClients[key] = client;
                    UpdateConnectionStatus(key, true);
                    _lastReceiveTime[key] = DateTime.Now;
                    _receiveQueues.GetOrAdd(key, new ConcurrentQueue<ModbusTcpReceiveData>());

                    LogService.Log($"{equipment.DeviceName}{(isReconnect ? "重连" : "连接")}成功: {key}");
                }
                catch (Exception ex)
                {
                    UpdateConnectionStatus(key, false);
                    LogService.Log($"{(isReconnect ? "重连" : "注册")}{equipment.DeviceName}失败: {ex.Message}");
                    throw new ApplicationException(ex.Message);
                }

                EnsureReceiveThreadRunning();
            }
        }

        public bool RegisterChannels(EquipmentModel equipment)
        {
            lock (_registerLock)
            {
                string key = GetChannelKey(equipment.DeviceIP, int.Parse(equipment.DevicePort), byte.Parse(equipment.CanIndex.ToString()));
                try
                {
                    TcpClient client = new TcpClient();
                    client.NoDelay = true;
                    bool conn = client.ConnectAsync(equipment.DeviceIP, int.Parse(equipment.DevicePort)).Wait(100);
                    if (!conn || !client.Connected)
                    {
                        LogService.Log($"{equipment.DeviceName} TCP连接失败");
                        client.Close();
                        return false;
                    }

                    _socketClients[key] = client;
                    UpdateConnectionStatus(key, true);
                    _lastReceiveTime[key] = DateTime.Now;
                    _receiveQueues.GetOrAdd(key, new ConcurrentQueue<ModbusTcpReceiveData>());
                    EnsureReceiveThreadRunning();
                    return true;
                }
                catch (Exception ex)
                {
                    UpdateConnectionStatus(key, false);
                    throw new ApplicationException(ex.Message);
                }
            }
        }

        public void EnsureReceiveThreadRunning()
        {
            if (_receiveThread == null || !_receiveThread.IsAlive)
            {
                _isRunning = true;
                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Highest
                };
                _receiveThread.Start();
                LogService.Log("ModbusTCP接收线程已启动");
            }
        }

        public void RegisterDataHandler(string ip, int port, byte slaveId, Action<List<ModbusTcpReceiveData>> handler)
        {
            string key = GetChannelKey(ip, port, slaveId);
            _dataHandlers.AddOrUpdate(key, handler, (k, old) => old + handler);
        }

        public void UnregisterDataHandler(string ip, int port, byte slaveId, Action<List<ModbusTcpReceiveData>> handler)
        {
            string key = GetChannelKey(ip, port, slaveId);
            if (_dataHandlers.TryGetValue(key, out var exist))
            {
                var newDel = (Action<List<ModbusTcpReceiveData>>)Delegate.Remove(exist, handler);
                if (newDel != null)
                    _dataHandlers[key] = newDel;
                else
                    _dataHandlers.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// 注册Modbus专属信号（独立ModbusSignalInfo，不共用CAN SignalInfo）
        /// </summary>
        public void RegisterSignals(ushort regAddr, List<ModbusSignalInfo> signals)
        {
            _signalDefinitions.AddOrUpdate(regAddr, signals, (addr, old) => signals);
        }

        #region Modbus独立信号解析方法（内部逻辑复刻CAN，但入参为ModbusSignalInfo）
        public ulong ExtractRawValue(byte[] regData, ModbusSignalInfo signal)
        {
            ulong rawVal = 0;
            int totalBits = regData.Length * 8;
            if (signal.StartBit + signal.Length > totalBits)
                throw new ArgumentException("Modbus信号超出寄存器数据范围");

            if (signal.ByteOrder == "Inter")
            {
                for (int i = 0; i < signal.Length; i++)
                {
                    int bitIdx = signal.StartBit + i;
                    int byteOff = bitIdx / 8;
                    int bitOff = bitIdx % 8;
                    if ((regData[byteOff] & (1 << bitOff)) != 0)
                        rawVal |= 1UL << i;
                }
            }
            else
            {
                for (int i = 0; i < signal.Length; i++)
                {
                    int globalBit = signal.StartBit + i;
                    int byteOff = globalBit / 8;
                    int bitInByte = 7 - (globalBit % 8);
                    if ((regData[byteOff] & (1 << bitInByte)) != 0)
                        rawVal |= 1UL << (signal.Length - 1 - i);
                }
            }
            return rawVal;
        }

        public byte[] SetRawValue(byte[] regData, ModbusSignalInfo signal, long rawValue)
        {
            int totalBits = regData.Length * 8;
            if (signal.StartBit + signal.Length > totalBits)
                throw new ArgumentException("Modbus信号超出寄存器数据范围");

            if (signal.ByteOrder == "Inter")
            {
                for (int i = 0; i < signal.Length; i++)
                {
                    int bitIdx = signal.StartBit + i;
                    int byteOff = bitIdx / 8;
                    int bitOff = bitIdx % 8;
                    if ((rawValue & (1L << i)) != 0)
                        regData[byteOff] |= (byte)(1 << bitOff);
                    else
                        regData[byteOff] &= (byte)~(1 << bitOff);
                }
            }
            else
            {
                for (int i = 0; i < signal.Length; i++)
                {
                    int globalBit = signal.StartBit + i;
                    int byteOff = globalBit / 8;
                    int bitInByte = 7 - (globalBit % 8);
                    if ((rawValue & (1L << (signal.Length - 1 - i))) != 0)
                        regData[byteOff] |= (byte)(1 << bitInByte);
                    else
                        regData[byteOff] &= (byte)~(1 << bitInByte);
                }
            }
            return regData;
        }

        public double ConvertToPhysicalValue(ulong rawValue, ModbusSignalInfo signal)
        {
            bool isSigned = signal.Signed == "Signed";
            long rawSigned = isSigned
                ? (long)(rawValue << (64 - signal.Length)) >> (64 - signal.Length)
                : (long)rawValue;
            return rawSigned * signal.Factor + signal.Offset;
        }

        public int GetNumberOfDecimalPlaces(decimal decimalV)
        {
            string[] split = decimalV.ToString().Split('.');
            if (split.Length == 2 && split[1].Length > 0)
            {
                int idx = split[1].Length - 1;
                while (split[1][idx] == '0' && idx-- > 0) ;
                return idx + 1;
            }
            return 0;
        }
        #endregion

        public void SendCommand(string ip, int port, byte slaveId, ModbusTcpTransmitData transmitFrame)
        {
            if (!_globalSendingEnabled) return;
            string key = GetChannelKey(ip, port, slaveId);
            if (!_socketClients.TryGetValue(key, out TcpClient client) || !client.Connected)
                return;

            List<byte> sendBuf = new List<byte>();
            sendBuf.AddRange(BitConverter.GetBytes(transmitFrame.TransactionId).Reverse());
            sendBuf.Add(0x00);
            sendBuf.Add(0x00);
            sendBuf.Add(0x00);
            sendBuf.Add(0x00);
            sendBuf.Add(transmitFrame.SlaveId);
            sendBuf.Add(transmitFrame.FuncCode);

            switch (transmitFrame.FuncCode)
            {
                case ModbusDefine.FN_READ_HOLD_REG:
                case ModbusDefine.FN_READ_INPUT_REG:
                    sendBuf.AddRange(BitConverter.GetBytes(transmitFrame.StartAddr).Reverse());
                    sendBuf.AddRange(BitConverter.GetBytes(transmitFrame.Count).Reverse());
                    break;
                case ModbusDefine.FN_WRITE_SINGLE_REG:
                    sendBuf.AddRange(BitConverter.GetBytes(transmitFrame.StartAddr).Reverse());
                    sendBuf.AddRange(BitConverter.GetBytes((ushort)BitConverter.ToUInt16(transmitFrame.Data, 0)).Reverse());
                    break;
                case ModbusDefine.FN_WRITE_MULTI_REG:
                    sendBuf.AddRange(BitConverter.GetBytes(transmitFrame.StartAddr).Reverse());
                    sendBuf.AddRange(BitConverter.GetBytes(transmitFrame.Count).Reverse());
                    sendBuf.Add((byte)transmitFrame.Data.Length);
                    sendBuf.AddRange(transmitFrame.Data);
                    break;
            }

            ushort pduLen = (ushort)(sendBuf.Count - 6);
            byte[] lenBytes = BitConverter.GetBytes(pduLen).Reverse().ToArray();
            sendBuf[4] = lenBytes[0];
            sendBuf[5] = lenBytes[1];

            byte[] sendBytes = sendBuf.ToArray();
            try
            {
                client.Client.Send(sendBytes);
                string hex = BitConverter.ToString(sendBytes).Replace("-", " ");
                LogService.Log($"Modbus通道{key} 发送成功 | 事务ID:{transmitFrame.TransactionId:X4} 报文:{hex}");
            }
            catch (Exception ex)
            {
                string hex = BitConverter.ToString(sendBytes).Replace("-", " ");
                LogService.Log($"Modbus通道{key} 发送失败 {ex.Message} | 报文:{hex}");
            }
        }

        public void ClearQueue(string channelKey)
        {
            try
            {
                if (_receiveQueues.TryGetValue(channelKey, out var queue))
                {
                    _receiveQueues[channelKey] = new ConcurrentQueue<ModbusTcpReceiveData>();
                    LogService.Log($"Modbus通道{channelKey} 接收队列已清空");
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"清空队列异常: {ex.Message}");
            }
        }
        #endregion

        #region 私有底层方法
        public static string GetChannelKey(string ip, int port, byte slaveId)
        {
            return $"{ip}:{port}-{slaveId}";
        }

        private void ReceiveLoop()
        {
            int cleanupCounter = 0;
            const int CLEANUP_INTERVAL = 1000;
            byte[] recvBuffer = new byte[ModbusDefine.RECV_BUFFER_SIZE];

            while (_isRunning)
            {
                foreach (var clientItem in _socketClients.ToList())
                {
                    string key = clientItem.Key;
                    TcpClient client = clientItem.Value;
                    if (!client.Connected || client.Client.Available == 0)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    try
                    {
                        int readLen = client.Client.Receive(recvBuffer, SocketFlags.None);
                        if (readLen <= 0) continue;

                        ModbusTcpReceiveData frame = ParseModbusFrame(recvBuffer, readLen);
                        if (frame.IsEmpty()) continue;

                        var queue = _receiveQueues.GetOrAdd(key, _ => new ConcurrentQueue<ModbusTcpReceiveData>());
                        queue.Enqueue(frame);
                        _lastReceiveTime[key] = DateTime.Now;

                        if (frame.Data != null && frame.Data.Length >= 2)
                        {
                            ushort regAddr = BitConverter.ToUInt16(frame.Data, 0);
                            if (_signalDefinitions.ContainsKey(regAddr))
                            {
                                List<ModbusTcpReceiveData> batch = new List<ModbusTcpReceiveData>() { frame };
                                if (_dataHandlers.TryGetValue(key, out var handler))
                                {
                                    Task.Run(() =>
                                    {
                                        try { handler(batch); }
                                        catch (Exception ex)
                                        {
                                            LogService.Log($"Modbus通道{key} 数据处理异常:{ex.Message}");
                                        }
                                    });
                                }
                            }
                        }
                    }
                    catch (SocketException)
                    {
                        UpdateConnectionStatus(key, false);
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"通道{key}接收异常:{ex.Message}");
                    }
                }

                cleanupCounter++;
                if (cleanupCounter >= CLEANUP_INTERVAL)
                {
                    CleanupQueues();
                    cleanupCounter = 0;
                }
            }
        }

        private ModbusTcpReceiveData ParseModbusFrame(byte[] buffer, int length)
        {
            ModbusTcpReceiveData frame = new ModbusTcpReceiveData();
            if (length < 7) return frame;

            frame.TransactionId = (ushort)((buffer[0] << 8) | buffer[1]);
            frame.ProtocolId = (ushort)((buffer[2] << 8) | buffer[3]);
            frame.Length = (ushort)((buffer[4] << 8) | buffer[5]);
            frame.SlaveId = buffer[6];
            frame.FuncCode = buffer[7];

            int dataLen = length - 8;
            frame.Data = new byte[dataLen];
            Array.Copy(buffer, 8, frame.Data, 0, dataLen);
            frame.Timestamp = (ulong)(DateTime.UtcNow.Ticks / 10);
            return frame;
        }

        public async Task<ModbusTcpReceiveData> ReceiveFrameAsync(string channelKey, ushort expectRegAddr, int timeoutMs)
        {
            var start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                if (_receiveQueues.TryGetValue(channelKey, out var queue))
                {
                    List<ModbusTcpReceiveData> tempCache = new List<ModbusTcpReceiveData>();
                    while (queue.TryDequeue(out var frame))
                    {
                        if (frame.Data == null || frame.Data.Length < 2)
                        {
                            tempCache.Add(frame);
                            continue;
                        }
                        ushort reg = BitConverter.ToUInt16(frame.Data, 0);
                        if (reg == expectRegAddr)
                        {
                            tempCache.ForEach(f => queue.Enqueue(f));
                            string hex = BitConverter.ToString(frame.Data).Replace("-", " ");
                            LogService.Log($"Modbus通道{channelKey} 匹配报文 寄存器:0x{expectRegAddr:X4} 数据:{hex}");
                            return frame;
                        }
                        tempCache.Add(frame);
                    }
                    tempCache.ForEach(f => queue.Enqueue(f));
                }
                await Task.Delay(0);
            }
            return new ModbusTcpReceiveData();
        }

        public async Task<List<ModbusTcpReceiveData>> ReceiveAllFramesAsync(string channelKey, ushort expectRegAddr, int timeoutMs)
        {
            List<ModbusTcpReceiveData> result = new List<ModbusTcpReceiveData>();
            var start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                if (!_receiveQueues.TryGetValue(channelKey, out var q) || q.Count == 0)
                {
                    await Task.Delay(10);
                    continue;
                }
                List<ModbusTcpReceiveData> temp = new List<ModbusTcpReceiveData>();
                bool hit = false;
                while (q.TryDequeue(out var frame))
                {
                    if (frame.Data != null && frame.Data.Length >= 2)
                    {
                        ushort reg = BitConverter.ToUInt16(frame.Data, 0);
                        if (reg == expectRegAddr)
                        {
                            result.Add(frame);
                            hit = true;
                            continue;
                        }
                    }
                    temp.Add(frame);
                }
                temp.ForEach(f => q.Enqueue(f));
                if (!hit) await Task.Delay(5);
            }
            LogService.Log($"Modbus通道{channelKey} 接收结束 寄存器0x{expectRegAddr:X4} 共{result.Count}帧");
            return result;
        }

        public async Task<Dictionary<ushort, List<ModbusTcpReceiveData>>> ReceiveMultipleFramesAsync(
            string channelKey, List<ushort> expectRegList, int timeoutMs)
        {
            Dictionary<ushort, List<ModbusTcpReceiveData>> ret = new Dictionary<ushort, List<ModbusTcpReceiveData>>();
            foreach (var r in expectRegList) ret[r] = new List<ModbusTcpReceiveData>();
            HashSet<ushort> regSet = new HashSet<ushort>(expectRegList);
            Stopwatch sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (!_receiveQueues.TryGetValue(channelKey, out var q))
                {
                    await Task.Delay(5);
                    continue;
                }
                int proc = 0;
                const int MAX_BATCH = 1000;
                while (q.TryDequeue(out var f) && proc++ < MAX_BATCH)
                {
                    if (f.Data == null || f.Data.Length < 2) continue;
                    ushort reg = BitConverter.ToUInt16(f.Data, 0);
                    if (regSet.Contains(reg)) ret[reg].Add(f);
                }
                bool allDone = true;
                foreach (var r in expectRegList)
                {
                    if (ret[r].Count < 1000) { allDone = false; break; }
                }
                if (allDone) break;
            }
            return ret;
        }

        private void CleanupQueues()
        {
            Task.Run(() =>
            {
                foreach (var kv in _receiveQueues)
                {
                    var q = kv.Value;
                    const int MAX_QUEUE = 1000;
                    while (q.Count > MAX_QUEUE)
                        q.TryDequeue(out _);
                }
            });
        }
        #endregion

        #region 通道注销、资源释放
        public void UnregisterChannel(string ip, int port, byte slaveId)
        {
            string key = GetChannelKey(ip, port, slaveId);
            lock (_registerLock)
            {
                if (_socketClients.TryGetValue(key, out var client))
                {
                    try
                    {
                        client.Client?.Shutdown(SocketShutdown.Both);
                        client.Close();
                        _socketClients.Remove(key);
                        _lastReceiveTime.TryRemove(key, out _);
                        _receiveQueues.TryRemove(key, out _);
                        _connectionStatus.TryRemove(key, out _);
                        _equipmentInfo.Remove(key);
                        LogService.Log($"Modbus通道 {key} 已关闭释放");
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"关闭通道{key}失败:{ex.Message}");
                    }
                }
            }
        }

        public void FullReset()
        {
            lock (_registerLock)
            {
                _isRunning = false;
                _reconnectTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _reconnectTimer?.Dispose();
                _reconnectTimer = null;

                if (_receiveThread != null && _receiveThread.IsAlive)
                {
                    _receiveThread.Join(2000);
                    if (_receiveThread.IsAlive)
                    {
                        try { _receiveThread.Interrupt(); } catch { }
                    }
                    _receiveThread = null;
                }

                var keys = _socketClients.Keys.ToList();
                foreach (var k in keys)
                {
                    try
                    {
                        var cli = _socketClients[k];
                        cli.Client?.Shutdown(SocketShutdown.Both);
                        cli.Close();
                    }
                    catch { }
                }

                _socketClients.Clear();
                _dataHandlers.Clear();
                _receiveQueues.Clear();
                _equipmentInfo.Clear();
                _lastReceiveTime.Clear();
                _connectionStatus.Clear();
                _signalDefinitions.Clear();

                LogService.Log("ModbusTCP管理器已完全重置释放资源");
            }
        }

        public void Dispose()
        {
            FullReset();
        }
        #endregion
    }

    #region Modbus独立信号模型（不和CAN SignalInfo共用）
    /// <summary>
    /// Modbus寄存器专属信号，独立实体，与CAN SignalInfo隔离
    /// </summary>
    public class ModbusSignalInfo
    {
        public ushort StartRegAddr { get; set; }
        public int StartBit { get; set; }
        public int Length { get; set; }
        public string ByteOrder { get; set; } // Inter小端 / Motorola大端
        public string Signed { get; set; }     // Signed / Unsigned
        public double Factor { get; set; }
        public double Offset { get; set; }
    }
    #endregion
}