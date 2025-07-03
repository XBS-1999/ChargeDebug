using System.Collections.Concurrent;
using System.Net.Sockets;

namespace TcpCommunicationLib
{
    public class TcpClientHelper : IDisposable
    {
        private TcpListener? _server;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private bool _isServer;
        private volatile bool _isDisposing;
        private Thread? _receiveThread;
        private Thread? _serverThread;

        // 接收缓冲区和锁
        private readonly List<byte> _receiveBuffer = new List<byte>();
        private readonly object _bufferLock = new object();

        // 接收队列
        private readonly ConcurrentQueue<byte[]> _receiveQueue = new ConcurrentQueue<byte[]>();

        public bool IsConnected => _client?.Connected == true;
        public string? ClientIP { get; private set; }

        public event EventHandler<byte[]> DataReceived;
        public event EventHandler<string> LogMessage;
        public event EventHandler<bool> ConnectionStatusChanged;

        public void StartServer(string ip, int port)
        {
            _server = new TcpListener(System.Net.IPAddress.Parse(ip), port);
            _server.Start();
            _isServer = true;

            _serverThread = new Thread(() =>
            {
                try
                {
                    _client = _server.AcceptTcpClient();
                    _stream = _client.GetStream();
                    ClientIP = ((System.Net.IPEndPoint)_client.Client.RemoteEndPoint).Address.ToString();
                    OnConnectionStatusChanged(true);
                    LogMessage?.Invoke(this, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Client {ClientIP} connected");

                    StartReceiving();
                }
                catch (Exception)
                {
                    if (!_isDisposing)
                    {
                        OnConnectionStatusChanged(false);
                    }
                }
            })
            { IsBackground = true };

            _serverThread.Start();
        }

        public void Connect(string ip, int port)
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(ip, port);
                _stream = _client.GetStream();
                _isServer = false;
                ClientIP = ip;
                OnConnectionStatusChanged(true);

                StartReceiving();
            }
            catch (Exception)
            {
                OnConnectionStatusChanged(false);
                throw;
            }
        }

        private void StartReceiving()
        {
            _receiveThread = new Thread(() =>
            {
                byte[] buffer = new byte[4096]; // 增大缓冲区
                try
                {
                    while (IsConnected && !_isDisposing)
                    {
                        int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;

                        lock (_bufferLock)
                        {
                            _receiveBuffer.AddRange(buffer.Take(bytesRead));
                        }

                        ProcessReceivedData();
                    }
                }
                catch (Exception ex)
                {
                    if (!_isDisposing)
                    {
                        LogMessage?.Invoke(this, $"接收错误: {ex.Message}");
                    }
                }
                finally
                {
                    if (!_isDisposing)
                    {
                        OnConnectionStatusChanged(false);
                        CleanupResources();
                    }
                }
            })
            { IsBackground = true };

            _receiveThread.Start();
        }

        // 实现CRC16校验
        private ushort CalculateCRC16(byte[] data)
        {
            ushort crc = 0xFFFF;
            const ushort polynomial = 0xA001; // CRC-16/MODBUS多项式

            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc = (ushort)((crc >> 1) ^ polynomial);
                    }
                    else
                    {
                        crc = (ushort)(crc >> 1);
                    }
                }
            }
            return crc;
        }

        // 处理接收数据并解析数据包
        private void ProcessReceivedData()
        {
            lock (_bufferLock)
            {
                // 至少需要4字节（设备地址+指令码+2字节CRC16）
                while (_receiveBuffer.Count >= 4)
                {
                    // 尝试查找可能的包头位置
                    for (int startIndex = 0; startIndex <= _receiveBuffer.Count - 4; startIndex++)
                    {
                        // 提取可能的数据包
                        int remainingBytes = _receiveBuffer.Count - startIndex;
                        if (remainingBytes < 4) break;

                        // 提取数据部分（不包括最后2字节CRC）
                        int dataLength = remainingBytes - 2;
                        byte[] candidateData = _receiveBuffer.Skip(startIndex).Take(dataLength).ToArray();

                        // 计算CRC16
                        ushort calculatedCrc = CalculateCRC16(candidateData);

                        // 提取接收到的CRC（小端模式）
                        ushort receivedCrc = (ushort)(_receiveBuffer[startIndex + dataLength] |
                                                     (_receiveBuffer[startIndex + dataLength + 1] << 8));

                        // 验证CRC
                        if (calculatedCrc == receivedCrc)
                        {
                            // 提取有效数据
                            byte[] packetData = candidateData;

                            // 移除处理过的数据（包括包头前面的无效数据）
                            _receiveBuffer.RemoveRange(0, startIndex + dataLength + 2);

                            // 入队
                            _receiveQueue.Enqueue(packetData);

                            // 触发事件
                            DataReceived?.Invoke(this, packetData);

                            // 重置索引（因为缓冲区已改变）
                            startIndex = -1; // 下次循环会从0开始
                        }
                    }

                    // 如果没有找到有效包，清除缓冲区
                    if (_receiveBuffer.Count > 0)
                    {
                        _receiveBuffer.Clear();
                    }
                }
            }
        }

        // 类似CAN的接收方法
        public async Task<byte[]> ReceiveCommandAsync(byte expectedCommand, int timeoutMs)
        {
            var startTime = DateTime.Now;
            var tempList = new List<byte[]>();

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (_receiveQueue.TryDequeue(out var packet))
                {
                    // 验证数据包长度
                    if (packet.Length < 2)
                    {
                        LogMessage?.Invoke(this, $"❌ 无效数据包长度: {packet.Length}字节");
                        continue;
                    }

                    // 提取命令码
                    byte receivedCommand = packet[1];
                    string hexData = BitConverter.ToString(packet).Replace("-", " ");

                    // 匹配预期命令
                    if (receivedCommand == expectedCommand)
                    {
                        // 将不匹配的数据包重新入队
                        foreach (var tempPacket in tempList)
                        {
                            _receiveQueue.Enqueue(tempPacket);
                        }

                        if (expectedCommand != 0x06)
                        {
                            //LogMessage?.Invoke(this, $"✅ 成功接收命令: 0x{expectedCommand:X2}");
                            LogMessage?.Invoke(this, $"← 接收数据: {hexData}");
                        }

                        return packet;
                    }
                    else
                    {
                        // 暂时保存不匹配的数据包
                        tempList.Add(packet);
                        LogMessage?.Invoke(this, $"⚠ 跳过不匹配命令: 0x{receivedCommand:X2} (期望: 0x{expectedCommand:X2})");
                        LogMessage?.Invoke(this, $"← 接收数据: {hexData}");
                    }
                }
                else
                {
                    // 没有数据时短暂等待
                    await Task.Delay(10);
                }
            }

            // 超时处理
            LogMessage?.Invoke(this, $"⏰ 接收命令 0x{expectedCommand:X2} 超时 ({timeoutMs}ms)");
            return null;
        }

        // 发送带CRC16校验的数据
        public void Send(bool flags, byte[] data)
        {
            if (IsConnected && _stream != null)
            {
                // 计算CRC16
                ushort crc = CalculateCRC16(data);

                // 添加CRC（小端模式）
                byte[] fullData = new byte[data.Length + 2];
                Array.Copy(data, 0, fullData, 0, data.Length);
                fullData[data.Length] = (byte)(crc & 0xFF);       // 低字节
                fullData[data.Length + 1] = (byte)(crc >> 8);    // 高字节

                // 发送
                _stream.Write(fullData, 0, fullData.Length);

                // 记录日志
                string hexData = BitConverter.ToString(fullData).Replace("-", " ");
                if (flags)
                {
                    LogMessage?.Invoke(this, $"→ 发送数据: {hexData}");
                }
            }
        }

        // 发送原始数据（不带CRC）
        public void SendRaw(byte[] data)
        {
            if (IsConnected && _stream != null)
            {
                _stream.Write(data, 0, data.Length);

                // 记录日志
                string hexData = BitConverter.ToString(data).Replace("-", " ");
                LogMessage?.Invoke(this, $"→ 发送原始数据: {hexData}");
            }
        }

        public void Disconnect()
        {
            _isDisposing = true;
            OnConnectionStatusChanged(false);
            CleanupResources();
        }

        private void CleanupResources()
        {
            try
            {
                _stream?.Close();
                _client?.Close();
                _server?.Stop();
            }
            catch { }
        }

        private void OnConnectionStatusChanged(bool connected)
        {
            ConnectionStatusChanged?.Invoke(this, connected);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}