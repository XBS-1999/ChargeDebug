using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

#pragma warning disable
namespace ChargeDebug.Service
{
    /// <summary>
    /// 单例TCP服务器服务，支持多客户端连接（电压/温度设备）
    /// 【优化版：先接收全部数据 → 独立线程解析，支持大数据量】
    /// </summary>
    public class TcpServerService
    {
        #region 单例实现
        private static readonly Lazy<TcpServerService> _instance = new Lazy<TcpServerService>(() => new TcpServerService());
        public static TcpServerService Instance => _instance.Value;
        private TcpServerService() { }
        #endregion

        #region 配置参数（从界面传入）
        /// <summary>
        /// 数据上传频率配置值（来自界面输入框）
        /// 单位：100ms/档
        /// </summary>
        public int UploadFrequencyValue { get; set; } = 1000; // 默认1000ms
        #endregion

        #region 字段
        private TcpListener _tcpListener;
        private bool _isServerRunning;
        private Thread _listenThread;
        private readonly ConcurrentDictionary<string, ClientSession> _clientSessions = new ConcurrentDictionary<string, ClientSession>();

        // 配置
        private string _serverIp = "192.168.1.100";
        private int _port = 54321;

        // 登录/心跳
        private const string LOGIN_PASSWORD = "qyjz";
        private const byte LOGIN_SUCCESS = 0x00;
        private const byte LOGIN_FAILED = 0x01;
        private const int HEARTBEAT_INTERVAL = 10000;

        // 事件（供UI订阅）
        public event Action<string, bool> OnClientStatusChanged; // IP, 连接状态
        public event Action<DeviceType, int, double> OnDataReceived; // 设备类型, 通道, 值
        public event Action<string> OnLog; // 日志

        // 新增故障事件（供UI订阅）
        public event Action<string, DeviceType, string, byte, int> OnFaultReceived; // 客户端IP, 设备类型, 故障名称, 故障等级, 通道号(0=系统级)

        #endregion

        #region 设备类型枚举
        public enum DeviceType
        {
            Voltage, // 电压设备 0x01
            Temp     // 温度设备 0x02
        }
        #endregion

        #region 启动/停止服务
        public void StartServer(string ip, int port)
        {
            if (_isServerRunning) return;
            _serverIp = ip;
            _port = port;
            _isServerRunning = true;

            _listenThread = new Thread(ListenLoop) { IsBackground = true };
            _listenThread.Start();
            OnLog?.Invoke($"服务器启动：{ip}:{port}");
        }

        public void StopServer()
        {
            _isServerRunning = false;
            _tcpListener?.Stop();

            foreach (var session in _clientSessions.Values)
                session.Close();
            _clientSessions.Clear();
            OnLog?.Invoke("服务器已停止");
        }
        #endregion

        #region 监听循环
        private void ListenLoop()
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Parse(_serverIp), _port);
                _tcpListener.Start();
                OnClientStatusChanged?.Invoke("服务器", true);

                while (_isServerRunning)
                {
                    if (_tcpListener.Pending())
                    {
                        var client = _tcpListener.AcceptTcpClient();
                        var clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                        var session = new ClientSession(client, clientIp, EnqueueDataForParse);
                        _clientSessions.TryAdd(clientIp, session);
                        OnClientStatusChanged?.Invoke(clientIp, true);
                        OnLog?.Invoke($"客户端连接：{clientIp}");
                    }
                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"监听异常：{ex.Message}");
            }
        }
        #endregion

        #region 【核心优化】接收数据入队列 → 线程池解析
        /// <summary>
        /// 接收数据直接入队列，不解析
        /// </summary>
        private void EnqueueDataForParse(string clientIp, byte[] rawData, NetworkStream stream)
        {
            // 只接收，不解析，扔给线程池处理
            Task.Run(() => ParseClientData(clientIp, rawData, stream));
        }

        /// <summary>
        /// 真正的解析方法（独立线程执行）
        /// </summary>
        private void ParseClientData(string clientIp, byte[] data, NetworkStream stream)
        {
            try
            {
                LogReceive(clientIp, data);

                if (!_clientSessions.TryGetValue(clientIp, out var session)) return;
                if (data.Length < 19) return;
                if (data[0] != 0x74) return;

                uint frameLen = BitConverter.ToUInt32(data, 1);
                if (data.Length != frameLen) return;

                byte clientType = data[5];
                byte clientAddr = data[6];
                session.ClientType = clientType;
                session.ClientAddr = clientAddr;

                FrameType frameType = (FrameType)data[15];
                OPCode opCode = (OPCode)data[16];
                int dataLen = (int)(frameLen - 19);
                byte[] frameData = data.Skip(17).Take(dataLen).ToArray();

                // ====================== 登录 ======================
                if (frameType == FrameType.Login && opCode == OPCode.C2S_Request)
                {
                    if (frameData.Length == 4)
                    {
                        string pwd = Encoding.ASCII.GetString(frameData, 0, 4);
                        byte ret = pwd == LOGIN_PASSWORD ? LOGIN_SUCCESS : LOGIN_FAILED;
                        byte[] resp = BuildResponseFrame(clientType, clientAddr, FrameType.Login, OPCode.S2C_Response, new[] { ret });
                        LogSend(clientIp, resp);
                        stream.Write(resp, 0, resp.Length);
                        session.IsLogined = ret == LOGIN_SUCCESS;
                        OnLog?.Invoke($"{clientIp} 登录{(session.IsLogined ? "成功" : "失败")}");

                        if (session.IsLogined)
                        {
                            session.StartHeartbeat(stream, SendHeartbeat);
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(100);
                                SendHeartbeat(stream);
                                await Task.Delay(200);
                                SendConfigFrame_UploadFrequency(session.Ip, stream, session.ClientType, session.ClientAddr, UploadFrequencyValue);
                            });
                        }
                    }
                    return;
                }

                if (!session.IsLogined) return;

                // ====================== 心跳 ======================
                if (frameType == FrameType.Heartbeat && opCode == OPCode.C2S_Request)
                {
                    return;
                }

                // ====================== 实时数据（核心优化通道计算） ======================
                if (frameType == FrameType.RealTimeData && opCode == OPCode.C2S_Request)
                {
                    if (frameData.Length >= 2)
                    {
                        ushort chEnable = BitConverter.ToUInt16(frameData, 0);
                        DeviceType devType = clientType == 0x01 ? DeviceType.Voltage : DeviceType.Temp;

                        // ====================== 通道计算公式 ======================
                        // 地址 0x00 → 通道1~16
                        // 地址 0x01 → 通道17~32
                        // 地址 0x02 → 通道33~48 ...以此类推
                        int channelOffset = clientAddr * 16;

                        for (int ch = 0; ch < 16; ch++)
                        {
                            if ((chEnable & (1 << ch)) == 0) continue;
                            int dataOffset = 2 + ch * 4;
                            if (dataOffset + 4 > frameData.Length) break;

                            int rawVal = BitConverter.ToInt32(frameData, dataOffset);
                            double val = devType == DeviceType.Voltage ? rawVal * 0.0001 : rawVal * 0.01;

                            int realChannel = ch + 1 + channelOffset;

                            OnDataReceived?.Invoke(devType, realChannel, val);
                        }

                        byte[] resp = BuildResponseFrame(clientType, clientAddr, FrameType.RealTimeData, OPCode.S2C_Response, new[] { (byte)0x00 });
                        stream.Write(resp, 0, resp.Length);
                    }
                    return;
                }

                // ====================== 故障 ======================
                if (frameType == FrameType.Fault && opCode == OPCode.C2S_Request)
                {
                    if (frameData.Length == 8)
                    {
                        var fault = FaultParams.FromBytes(frameData, 0);
                        ParseFaultData(clientIp, clientType, clientAddr, fault);
                        byte[] resp = BuildResponseFrame(clientType, clientAddr, FrameType.Fault, OPCode.S2C_Response, new[] { (byte)0x00 });
                        stream.Write(resp, 0, resp.Length);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"{clientIp} 解析异常：{ex.Message}");
            }
        }
        #endregion

        #region 故障解析
        private void ParseFaultData(string clientIp, byte clientType, byte clientAddr, FaultParams fault)
        {
            DeviceType devType = clientType == 0x01 ? DeviceType.Voltage : DeviceType.Temp;
            int channelOffset = clientAddr * 16; // 通道偏移

            // 系统故障
            if ((fault.Byte0 & 0x01) != 0) OnLog?.Invoke($"设备{clientIp}-{devType} 设备采集故障");
            if ((fault.Byte0 & 0x02) != 0) OnLog?.Invoke($"设备{clientIp}-{devType} 网络通讯故障");
            if ((fault.Byte0 & 0x04) != 0) OnLog?.Invoke($"设备{clientIp}-{devType} 硬件故障");
            if ((fault.Byte0 & 0x08) != 0) OnLog?.Invoke($"设备{clientIp}-{devType} DI_0故障");
            if ((fault.Byte0 & 0x10) != 0) OnLog?.Invoke($"设备{clientIp}-{devType} DI_1故障");
            if ((fault.Byte0 & 0x20) != 0) OnLog?.Invoke($"设备{clientIp}-{devType} DI_2故障");
            if ((fault.Byte0 & 0x40) != 0) OnLog?.Invoke($"设备{clientIp}-{devType} DI_3故障");

            // 过压/过温
            for (int ch = 0; ch < 8; ch++) if ((fault.Byte1 & (1 << ch)) != 0) OnLog?.Invoke($"设备{clientIp}-{devType} 通道{ch + 1 + channelOffset}过压/过温");
            for (int ch = 0; ch < 8; ch++) if ((fault.Byte2 & (1 << ch)) != 0) OnLog?.Invoke($"设备{clientIp}-{devType} 通道{ch + 9 + channelOffset}过压/过温");

            // 欠压/欠温
            for (int ch = 0; ch < 8; ch++) if ((fault.Byte3 & (1 << ch)) != 0) OnLog?.Invoke($"设备{clientIp}-{devType} 通道{ch + 1 + channelOffset}欠压/欠温");
            for (int ch = 0; ch < 8; ch++) if ((fault.Byte4 & (1 << ch)) != 0) OnLog?.Invoke($"设备{clientIp}-{devType} 通道{ch + 9 + channelOffset}欠压/欠温");

            //OnLog?.Invoke($"{clientIp} 故障解析完成");
        }
        #endregion

        #region 帧结构、CRC、心跳、配置帧
        private byte[] BuildResponseFrame(byte clientType, byte clientAddr, FrameType frameType, OPCode opCode, byte[] data)
        {
            int dataLen = data.Length;
            int frameLen = 19 + dataLen;
            byte[] frame = new byte[frameLen];

            frame[0] = 0x74;
            Array.Copy(BitConverter.GetBytes((uint)frameLen), 0, frame, 1, 4);
            frame[5] = clientType;
            frame[6] = clientAddr;

            var now = DateTime.Now;
            frame[7] = (byte)(now.Year - 2000);
            frame[8] = (byte)now.Month;
            frame[9] = (byte)now.Day;
            frame[10] = (byte)now.Hour;
            frame[11] = (byte)now.Minute;
            frame[12] = (byte)now.Second;
            Array.Copy(BitConverter.GetBytes((ushort)now.Millisecond), 0, frame, 13, 2);

            frame[15] = (byte)frameType;
            frame[16] = (byte)opCode;

            if (dataLen > 0) Array.Copy(data, 0, frame, 17, dataLen);

            int crcLen = frameLen - 2;
            byte[] crcBuf = new byte[crcLen];
            Array.Copy(frame, 0, crcBuf, 0, crcLen);
            ushort crc = CalcCRC16(crcBuf);

            frame[frameLen - 2] = (byte)(crc & 0xFF);
            frame[frameLen - 1] = (byte)(crc >> 8);
            return frame;
        }

        private ushort CalcCRC16(byte[] buffer)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < buffer.Length; i++)
            {
                crc ^= buffer[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                        crc >>= 1;
                }
            }
            return crc;
        }

        private void SendHeartbeat(NetworkStream stream)
        {
            try
            {
                var session = _clientSessions.Values.FirstOrDefault(s => s.Client.GetStream() == stream);
                if (session == null || !session.IsLogined) return;
                byte[] time = TimeStruct.FromNow().ToBytes();
                byte[] frame = BuildResponseFrame(0x00, 0x00, FrameType.Heartbeat, OPCode.S2C_Response, time);
                LogSend(session.Ip, frame);
                stream.Write(frame, 0, frame.Length);
            }
            catch { }
        }

        private void SendConfigFrame_UploadFrequency(string clientIp, NetworkStream stream, byte clientType, byte clientAddr, int freqValue)
        {
            try
            {
                byte[] config = new byte[]
                {
                    0x02,
                    BitConverter.GetBytes((ushort)freqValue)[0],
                    BitConverter.GetBytes((ushort)freqValue)[1]
                };
                byte[] frame = BuildResponseFrame(clientType, clientAddr, FrameType.Config, OPCode.S2C_Response, config);
                LogSend(clientIp, frame);
                stream.Write(frame, 0, frame.Length);
            }
            catch { }
        }

        private void LogReceive(string clientIp, byte[] data)
        {
            return;
            string hex = BitConverter.ToString(data).Replace("-", " ");
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] [{clientIp}] ← 接收：{hex}");
        }

        private void LogSend(string clientIp, byte[] data)
        {
            return;
            string hex = BitConverter.ToString(data).Replace("-", " ");
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] [{clientIp}] → 发送：{hex}");
        }
        #endregion

        #region 客户端会话
        private class ClientSession
        {
            public TcpClient Client { get; }
            public string Ip { get; }
            public bool IsLogined { get; set; }
            public byte ClientType { get; set; }
            public byte ClientAddr { get; set; }

            private Thread _receiveThread;
            private System.Timers.Timer _heartbeatTimer;
            private readonly Action<string, byte[], NetworkStream> _onDataReceived;

            public ClientSession(TcpClient client, string ip, Action<string, byte[], NetworkStream> onDataReceived)
            {
                Client = client;
                Ip = ip;
                _onDataReceived = onDataReceived;
                _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
                _receiveThread.Start();
            }

            /// <summary>
            /// 接收线程：只负责读数据，不解析
            /// </summary>
            private void ReceiveLoop()
            {
                byte[] buffer = new byte[4096]; // 更大缓冲区
                while (Client.Connected)
                {
                    try
                    {
                        var stream = Client.GetStream();
                        if (stream.DataAvailable)
                        {
                            int len = stream.Read(buffer, 0, buffer.Length);
                            if (len > 0)
                            {
                                byte[] data = new byte[len];
                                Array.Copy(buffer, data, len);
                                _onDataReceived?.Invoke(Ip, data, stream);
                            }
                        }
                        Thread.Sleep(5);
                    }
                    catch { break; }
                }
                Close();
            }

            public void StartHeartbeat(NetworkStream stream, Action<NetworkStream> sendAction)
            {
                _heartbeatTimer = new System.Timers.Timer(HEARTBEAT_INTERVAL);
                _heartbeatTimer.Elapsed += (s, e) => sendAction(stream);
                _heartbeatTimer.Start();
            }

            public void Close()
            {
                _heartbeatTimer?.Stop();
                _heartbeatTimer?.Dispose();
                Client?.Close();
            }
        }
        #endregion

        #region 协议结构体
        public struct TimeStruct
        {
            public byte year, month, day, hour, minute, second;
            public static TimeStruct FromNow()
            {
                var now = DateTime.Now;
                return new TimeStruct
                {
                    year = (byte)(now.Year - 2000),
                    month = (byte)now.Month,
                    day = (byte)now.Day,
                    hour = (byte)now.Hour,
                    minute = (byte)now.Minute,
                    second = (byte)now.Second
                };
            }
            public byte[] ToBytes() => new[] { year, month, day, hour, minute, second };
        }

        public enum FrameType : byte
        {
            Login = 0x01,
            Heartbeat = 0x02,
            Config = 0x03,
            ProtectParam = 0x06,
            RealTimeData = 0x0A,
            Fault = 0x0F
        }

        public enum OPCode : byte
        {
            C2S_Request = 0x01,
            S2C_Response = 0x11
        }

        public struct FaultParams
        {
            public byte Byte0, Byte1, Byte2, Byte3, Byte4, Byte5, Byte6, Byte7;
            public static FaultParams FromBytes(byte[] data, int offset)
            {
                return new FaultParams
                {
                    Byte0 = data[offset],
                    Byte1 = data[offset + 1],
                    Byte2 = data[offset + 2],
                    Byte3 = data[offset + 3],
                    Byte4 = data[offset + 4],
                    Byte5 = data[offset + 5],
                    Byte6 = data[offset + 6],
                    Byte7 = data[offset + 7]
                };
            }
        }
        #endregion
    }
}