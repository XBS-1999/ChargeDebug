using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;
using DataModel;
using Log;

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

                // 订阅数据接收事件
                serialPort.DataReceived += SerialPort_DataReceived;

                // 打开串口
                serialPort.Open();

                // 将串口添加到字典中管理
                _serialPorts.TryAdd(channelconfig.ComPort, serialPort);

                LogService.Log($"RS485通道 {channelconfig.ComPort} 注册成功");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"RS485设备连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 串口数据接收事件处理函数
        /// </summary>
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort serialPort = (SerialPort)sender;

            try
            {
                // 读取所有可用数据
                int bytesToRead = serialPort.BytesToRead;
                byte[] buffer = new byte[bytesToRead];
                serialPort.Read(buffer, 0, bytesToRead);

                // 触发数据接收事件
                OnDataReceived(new DataReceivedEventArgs(serialPort.PortName, buffer));

                LogService.Log($"从 {serialPort.PortName} 接收到 {bytesToRead} 字节数据");
            }
            catch (Exception ex)
            {
                LogService.Log($"读取RS485数据时发生错误: {ex.Message}");
            }
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
        /// <returns>成功返回true，失败返回false</returns>
        public bool SendData(string portName, byte[] data)
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
                // CRC16校验
                ushort crc = CalculateCRC16(data);
                byte[] fullData = new byte[data.Length + 2];
                Array.Copy(data, 0, fullData, 0, data.Length);
                fullData[data.Length] = (byte)(crc & 0xFF);       // 低字节
                fullData[data.Length + 1] = (byte)(crc >> 8);    // 高字节

                // 发送数据
                serialPort.Write(fullData, 0, fullData.Length);

                LogService.Log($"向 {portName} 发送 {data.Length} 字节数据");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"向RS485通道 {portName} 发送数据时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// CRC16校验
        /// </summary>
        /// <param name="data">要发送的数据字节数组</param>
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
        /// 向指定RS485通道发送字符串数据
        /// </summary>
        /// <param name="portName">串口名称</param>
        /// <param name="message">要发送的字符串</param>
        /// <param name="encoding">编码格式，默认为UTF-8</param>
        /// <returns>成功返回true，失败返回false</returns>
        public bool SendString(string portName, string message, Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.UTF8;
            byte[] data = encoding.GetBytes(message);
            return SendData(portName, data);
        }

        /// <summary>
        /// 从指定RS485通道同步读取数据
        /// </summary>
        /// <param name="portName">串口名称</param>
        /// <param name="buffer">存储读取数据的缓冲区</param>
        /// <param name="offset">缓冲区中的偏移量</param>
        /// <param name="count">要读取的字节数</param>
        /// <returns>实际读取的字节数</returns>
        public int ReadData(string portName, byte[] buffer, int offset, int count)
        {
            if (!_serialPorts.TryGetValue(portName, out SerialPort serialPort))
            {
                LogService.Log($"找不到RS485通道: {portName}");
                return -1;
            }

            if (!serialPort.IsOpen)
            {
                LogService.Log($"RS485通道未打开: {portName}");
                return -1;
            }

            try
            {
                return serialPort.Read(buffer, offset, count);
            }
            catch (Exception ex)
            {
                LogService.Log($"从RS485通道 {portName} 读取数据时发生错误: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 从指定RS485通道同步读取所有可用数据
        /// </summary>
        /// <param name="portName">串口名称</param>
        /// <returns>读取到的数据字节数组，失败返回null</returns>
        public byte[] ReadAllData(string portName)
        {
            if (!_serialPorts.TryGetValue(portName, out SerialPort serialPort))
            {
                LogService.Log($"找不到RS485通道: {portName}");
                return null;
            }

            if (!serialPort.IsOpen)
            {
                LogService.Log($"RS485通道未打开: {portName}");
                return null;
            }

            try
            {
                int bytesToRead = serialPort.BytesToRead;
                if (bytesToRead == 0)
                    return new byte[0];

                byte[] buffer = new byte[bytesToRead];
                serialPort.Read(buffer, 0, bytesToRead);
                return buffer;
            }
            catch (Exception ex)
            {
                LogService.Log($"从RS485通道 {portName} 读取数据时发生错误: {ex.Message}");
                return null;
            }
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

        /// <summary>
        /// 关闭所有RS485通道
        /// </summary>
        public void CloseAllChannels()
        {
            foreach (var portName in _serialPorts.Keys)
            {
                CloseChannel(portName);
            }
        }

        /// <summary>
        /// 检查指定RS485通道是否已打开
        /// </summary>
        /// <param name="portName">串口名称</param>
        /// <returns>已打开返回true，否则返回false</returns>
        public bool IsChannelOpen(string portName)
        {
            if (_serialPorts.TryGetValue(portName, out SerialPort serialPort))
            {
                return serialPort.IsOpen;
            }
            return false;
        }

        /// <summary>
        /// 获取所有已注册的RS485通道名称
        /// </summary>
        /// <returns>通道名称列表</returns>
        public List<string> GetRegisteredChannels()
        {
            return _serialPorts.Keys.ToList();
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