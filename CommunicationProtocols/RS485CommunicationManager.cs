using DataModel;
using Log;
using System.Collections.Concurrent;
using System.IO.Ports;

#pragma warning disable
namespace ChargeDebug.Service
{
    /// <summary>
    /// RS485通讯管理器，负责管理所有RS485通道的创建、数据发送和接收
    /// 采用单例模式确保全局唯一访问点
    /// </summary>
    public class RS485Manager
    {
        private static RS485Manager _instance;
        private static readonly object _lock = new object();

        // 存储所有已注册的RS485通道，键为串口名称，值为SerialPort对象
        private ConcurrentDictionary<string, SerialPort> _serialPorts = new ConcurrentDictionary<string, SerialPort>();
        // 数据接收事件，当从任何RS485通道接收到数据时触发
        public event EventHandler<DataReceivedEventArgs> DataReceived;

        // 增加接收超时和缓冲区处理
        private ConcurrentDictionary<string, System.Timers.Timer> _receiveTimers = new ConcurrentDictionary<string, System.Timers.Timer>();
        private ConcurrentDictionary<string, MemoryStream> _receiveBuffers = new ConcurrentDictionary<string, MemoryStream>();
        private const int RECEIVE_TIMEOUT_MS = 1000; // 接收超时时间(毫秒)

        // 为每个端口添加同步锁，确保线程安全
        private ConcurrentDictionary<string, object> _bufferLocks = new ConcurrentDictionary<string, object>();

        /// <summary>
        /// 获取RS485Manager的单例实例
        /// </summary>
        public static RS485Manager Instance
        {
            get
            {
                lock (_lock)
                {
                    return _instance ??= new RS485Manager();
                }
            }
        }

        // 私有构造函数，防止外部实例化
        private RS485Manager() { }

        /// <summary>
        /// 注册并打开一个RS485通道
        /// </summary>
        /// <param name="channelconfig">通道配置信息</param>
        /// <returns>成功返回true，失败返回false</returns>
        public bool RegisterChannel(EquipmentModel channelconfig)
        {
            try
            {
                // 检查通道是否已存在
                if (_serialPorts.ContainsKey(channelconfig.ComPort))
                {
                    LogService.Log($"RS485通道 {channelconfig.ComPort} 已存在");
                    return true;
                }

                // 创建串口
                SerialPort serialPort = new SerialPort();
                serialPort.PortName = channelconfig.ComPort;
                serialPort.BaudRate = Convert.ToInt32(channelconfig.BaudRate);
                serialPort.DataBits = Convert.ToInt32(channelconfig.DataBits);

                // 设置停止位
                switch (channelconfig.StopBits)
                {
                    case "0":
                        serialPort.StopBits = StopBits.None;
                        break;
                    case "1":
                        serialPort.StopBits = StopBits.One;
                        break;
                    case "2":
                        serialPort.StopBits = StopBits.Two;
                        break;
                    case "1.5":
                        serialPort.StopBits = StopBits.OnePointFive;
                        break;
                    default:
                        serialPort.StopBits = StopBits.One;
                        break;
                }

                // 设置校验位
                switch (channelconfig.Parity)
                {
                    case "无":
                        serialPort.Parity = Parity.None;
                        break;
                    case "奇校验":
                        serialPort.Parity = Parity.Odd;
                        break;
                    case "偶校验":
                        serialPort.Parity = Parity.Even;
                        break;
                    case "Mark":
                        serialPort.Parity = Parity.Mark;
                        break;
                    case "空格校验":
                        serialPort.Parity = Parity.Space;
                        break;
                    default:
                        serialPort.Parity = Parity.None;
                        break;
                }

                // 设置读取超时
                serialPort.ReadTimeout = 500;

                // 订阅数据接收事件
                serialPort.DataReceived += SerialPort_DataReceived;

                // 打开串口
                serialPort.Open();

                // 初始化接收缓冲区和超时计时器
                _receiveBuffers[channelconfig.ComPort] = new MemoryStream();
                var timer = new System.Timers.Timer(RECEIVE_TIMEOUT_MS);
                timer.Elapsed += (s, e) => ReceiveTimeoutHandler(channelconfig.ComPort);
                timer.AutoReset = false;
                _receiveTimers[channelconfig.ComPort] = timer;

                // 为端口添加同步锁
                _bufferLocks[channelconfig.ComPort] = new object();

                // 将串口添加到字典中管理
                _serialPorts.TryAdd(channelconfig.ComPort, serialPort);

                LogService.Log($"RS485通道 {channelconfig.ComPort} 注册成功，配置: {serialPort.BaudRate}/{serialPort.DataBits}/{serialPort.Parity}/{serialPort.StopBits}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"RS485设备连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 串口数据接收事件处理函数 - 改进版本
        /// </summary>
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort serialPort = (SerialPort)sender;
            string portName = serialPort.PortName;

            try
            {
                // 检查是否有数据可读
                if (serialPort.BytesToRead == 0)
                {
                    LogService.Log($"从 {portName} 接收到数据事件，但无可读数据");
                    return;
                }

                // 读取所有可用数据
                int bytesToRead = serialPort.BytesToRead;
                byte[] buffer = new byte[bytesToRead];
                int bytesRead = serialPort.Read(buffer, 0, bytesToRead);

                if (bytesRead > 0)
                {
                    //LogService.Log($"从 {portName} 接收到 {bytesRead} 字节数据: {BitConverter.ToString(buffer)}");

                    // 使用锁确保线程安全
                    lock (_bufferLocks[portName])
                    {
                        // 将数据添加到缓冲区
                        if (_receiveBuffers.TryGetValue(portName, out MemoryStream bufferStream))
                        {
                            bufferStream.Write(buffer, 0, bytesRead);
                        }
                    }

                    // 重置并启动超时计时器
                    if (_receiveTimers.TryGetValue(portName, out System.Timers.Timer timer))
                    {
                        timer.Stop();
                        timer.Start();
                    }
                }
            }
            catch (TimeoutException)
            {
                LogService.Log($"读取 {portName} 数据超时");
            }
            catch (Exception ex)
            {
                LogService.Log($"读取RS485数据时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 接收超时处理函数
        /// </summary>
        private void ReceiveTimeoutHandler(string portName)
        {
            // 使用锁确保线程安全
            lock (_bufferLocks[portName])
            {
                if (_receiveBuffers.TryGetValue(portName, out MemoryStream bufferStream) && bufferStream.Length > 0)
                {
                    byte[] receivedData = bufferStream.ToArray();

                    // 触发数据接收事件
                    OnDataReceived(new DataReceivedEventArgs(portName, receivedData));

                    // 清空缓冲区
                    bufferStream.SetLength(0);
                    LogService.Log($"超时处理: 从 {portName} 接收完整数据帧: {BitConverter.ToString(receivedData)}");
                }
            }
        }

        /// <summary>
        /// 从接收缓冲区读取数据
        /// </summary>
        /// <param name="portName">串口名称</param>
        /// <param name="clearBuffer">读取后是否清空缓冲区</param>
        /// <returns>接收到的数据字节数组，如果没有数据则返回空数组</returns>
        public byte[] ReadBuffer(string portName, bool clearBuffer = true)
        {
            if (!_receiveBuffers.ContainsKey(portName))
            {
                LogService.Log($"找不到RS485通道的接收缓冲区: {portName}");
                return new byte[0];
            }

            // 使用锁确保线程安全
            lock (_bufferLocks[portName])
            {
                if (_receiveBuffers.TryGetValue(portName, out MemoryStream bufferStream) && bufferStream.Length > 0)
                {
                    byte[] data = bufferStream.ToArray();

                    if (clearBuffer)
                    {
                        bufferStream.SetLength(0); // 清空缓冲区
                    }

                    LogService.Log($"从 {portName} 接收缓冲区读取 {data.Length} 字节数据: {BitConverter.ToString(data)}");
                    return data;
                }
            }

            return new byte[0];
        }

        /// <summary>
        /// 触发数据接收事件
        /// </summary>
        protected virtual void OnDataReceived(DataReceivedEventArgs e)
        {
            DataReceived?.Invoke(this, e);
        }

        /// <summary>
        /// 向指定RS485通道发送数据
        /// </summary>
        /// <param name="portName">串口名称</param>
        /// <param name="data">要发送的数据字节数组</param>
        /// <param name="addCRC">是否添加CRC校验</param>
        /// <returns>成功返回true，失败返回false</returns>
        public bool SendData(string portName, byte[] data, bool addCRC = true)
        {
            if (!_serialPorts.TryGetValue(portName, out SerialPort serialPort))
            {
                LogService.Log($"找不到RS485通道: {portName}");
                return false;
            }

            if (!serialPort.IsOpen)
            {
                LogService.Log($"RS485通道未打开: {portName}");
                return false;
            }

            try
            {
                byte[] fullData;

                if (addCRC)
                {
                    // CRC16校验
                    ushort crc = CalculateCRC16(data);
                    fullData = new byte[data.Length + 2];
                    Array.Copy(data, 0, fullData, 0, data.Length);
                    fullData[data.Length] = (byte)(crc & 0xFF);       // 低字节
                    fullData[data.Length + 1] = (byte)(crc >> 8);    // 高字节
                }
                else
                {
                    fullData = data;
                }

                // 清空输入缓冲区
                serialPort.DiscardInBuffer();

                // 发送数据
                serialPort.Write(fullData, 0, fullData.Length);

                LogService.Log($"向 {portName} 发送 {fullData.Length} 字节数据: {BitConverter.ToString(fullData)}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"向RS485通道 {portName} 发送数据时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// CRC16校验 (MODBUS)
        /// </summary>
        /// <param name="data">要计算CRC的数据字节数组</param>
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


        /// <summary>
        /// 关闭指定RS485通道
        /// </summary>
        /// <param name="portName">串口名称</param>
        /// <returns>成功返回true，失败返回false</returns>
        public bool CloseChannel(string portName)
        {
            if (!_serialPorts.TryRemove(portName, out SerialPort serialPort))
            {
                LogService.Log($"找不到RS485通道: {portName}");
                return false;
            }

            try
            {
                // 取消事件订阅
                serialPort.DataReceived -= SerialPort_DataReceived;

                // 清理接收缓冲区和计时器
                if (_receiveBuffers.TryRemove(portName, out MemoryStream bufferStream))
                {
                    bufferStream.Dispose();
                }

                if (_receiveTimers.TryRemove(portName, out System.Timers.Timer timer))
                {
                    timer.Stop();
                    timer.Dispose();
                }

                // 清理同步锁
                _bufferLocks.TryRemove(portName, out _);

                // 关闭串口
                if (serialPort.IsOpen)
                    serialPort.Close();

                // 释放资源
                serialPort.Dispose();

                LogService.Log($"RS485通道 {portName} 已关闭");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"关闭RS485通道 {portName} 时发生错误: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// 数据接收事件参数类
    /// </summary>
    public class DataReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// 接收到数据的串口名称
        /// </summary>
        public string PortName { get; }

        /// <summary>
        /// 接收到的数据
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="portName">串口名称</param>
        /// <param name="data">接收到的数据</param>
        public DataReceivedEventArgs(string portName, byte[] data)
        {
            PortName = portName;
            Data = data;
        }
    }
}