using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using DataModel;
using Log;

namespace ChargeDebug.Service
{
    /*
     * CAN管理器核心类 (使用周立功最新API)
     * 
     * 设计目标：
     * 1. 使用周立功最新的ZCAN API进行CAN通信
     * 2. 集中管理所有CAN设备通信
     * 3. 提供高效的信号解析和数据分发
     * 4. 支持多设备多通道同时工作
     * 5. 实现线程安全操作
     */

    public sealed class CANManager : IDisposable
    {
        #region ZLG ZCAN API封装
        /* 
         * 导入周立功最新CAN库函数 (zcanpro.dll)
         * 注意：新版API使用ZCAN_前缀
         */
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_SetValue(IntPtr device_handle, string path, byte[] value);

        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr ZCAN_OpenDevice(uint deviceType, uint deviceIndex, uint reserved);

        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr ZCAN_InitCAN(IntPtr deviceHandle, uint channelIndex, ref ZCAN_CHANNEL_INIT_CONFIG config);

        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint ZCAN_StartCAN(IntPtr channel_handle);

        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint ZCAN_GetReceiveNum(IntPtr channel_handle, byte type);

        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint ZCAN_Receive(IntPtr channel_handle, IntPtr data, uint len, int wait_time);

        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool ZCAN_CloseDevice(IntPtr deviceHandle);

        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint ZCAN_Transmit(IntPtr channel_handle, IntPtr pTransmit, uint len);

        /* 
         * CAN初始化配置结构体 (新版API)
         */
        [StructLayout(LayoutKind.Sequential)]
        private struct ZCAN_CHANNEL_INIT_CONFIG
        {
            public uint can_type;        //type:TYPE_CAN TYPE_CANFD
            public uint acc_code;
            public uint acc_mask;
            public uint reserved;
            public byte filter;
            public byte timing0;
            public byte timing1;
            public byte mode;
        }

        public static class Define
        {
            public const int TYPE_CAN = 0;
            public const int TYPE_CANFD = 1;
            public const int ZCAN_USBCAN1 = 3;
            public const int ZCAN_USBCAN2 = 4;
            public const int ZCAN_PCI9820I = 16;
            public const int ZCAN_CANETUDP = 12;
            public const int ZCAN_CANETTCP = 17;
            public const int ZCAN_CANWIFI_TCP = 25;
            public const int ZCAN_USBCAN_E_U = 20;
            public const int ZCAN_USBCAN_2E_U = 21;
            public const int ZCAN_USBCAN_4E_U = 31;
            public const int ZCAN_PCIECANFD_100U = 38;
            public const int ZCAN_PCIECANFD_200U = 39;
            public const int ZCAN_PCIECANFD_200U_EX = 62;
            public const int ZCAN_PCIECANFD_400U = 61;
            public const int ZCAN_USBCANFD_200U = 41;
            public const int ZCAN_USBCANFD_400U = 76;
            public const int ZCAN_USBCANFD_100U = 42;
            public const int ZCAN_USBCANFD_MINI = 43;
            public const int ZCAN_USBCANFD_800U = 59;
            public const int ZCAN_CLOUD = 46;
            public const int ZCAN_CANFDNET_200U_TCP = 48;
            public const int ZCAN_CANFDNET_200U_UDP = 49;
            public const int ZCAN_CANFDNET_400U_TCP = 52;
            public const int ZCAN_CANFDNET_400U_UDP = 53;
            public const int ZCAN_CANFDNET_800U_TCP = 57;
            public const int ZCAN_CANFDNET_800U_UDP = 58;
            public const int STATUS_ERR = 0;
            public const int STATUS_OK = 1;
        };

        /* 
         * CAN数据帧结构体 (新版API)
         * 注意：新版API使用更简洁的结构
         */
        [StructLayout(LayoutKind.Sequential)]
        public struct ZCAN_Receive_Data
        {
            public uint can_id;  /* 32 bit MAKE_CAN_ID + EFF/RTR/ERR flags */
            public byte can_dlc; /* frame payload length in byte (0 .. CAN_MAX_DLEN) */
            public byte __pad;   /* padding */
            public byte __res0;  /* reserved / padding */
            public byte __res1;  /* reserved / padding */
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] data/* __attribute__((aligned(8)))*/;
            public UInt64 timestamp;//us

            public bool IsEmpty()
            {
                // 通过检查数据长度是否为0来判断是否接收到有效数据
                return data == null || data.Length == 0;
            }
        }
        public struct ZCAN_Transmit_Data
        {
            public uint can_id;  /* 32 bit MAKE_CAN_ID + EFF/RTR/ERR flags */
            public byte can_dlc; /* frame payload length in byte (0 .. CAN_MAX_DLEN) */
            public byte __pad;   /* padding */
            public byte __res0;  /* reserved / padding */
            public byte __res1;  /* reserved / padding */
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] data/* __attribute__((aligned(8)))*/;
            public uint transmit_type;
        }
        #endregion

        #region 单例实现
        /*
         * 单例模式实现
         * 确保整个应用程序只有一个CAN管理器实例
         */
        private static readonly Lazy<CANManager> _instance = new Lazy<CANManager>(() => new CANManager());
        public static CANManager Instance => _instance.Value;

        private CANManager()
        {

        }

        public void Init()
        {
            //LogService("CAN管理器初始化开始");
            _isRunning = true;

            _uiUpdateTimer?.Dispose();
            // 初始化UI更新定时器
            _uiUpdateTimer = new System.Threading.Timer(UIUpdateCallback, null, UI_UPDATE_INTERVAL, UI_UPDATE_INTERVAL);

            _reconnectTimer?.Dispose();
            // 初始化重连定时器 (取消注释)
            _reconnectTimer = new System.Threading.Timer(ReconnectCheckCallback, null, RECONNECT_INTERVAL, RECONNECT_INTERVAL);
        }

        // 重连检查回调
        private void ReconnectCheckCallback(object state)
        {
            try
            {
                var channelKeys = _equipmentInfo.Keys.ToList();
                foreach (var channelKey in channelKeys)
                {
                    // 检查是否在设备句柄字典中存在
                    bool isRegistered = _deviceHandles.ContainsKey(channelKey);

                    if (isRegistered && CheckTimeout(channelKey))
                    {
                        //logger.Info($"设备{channelKey} 通信超时，尝试重连...");
                        //LogService.Log($"CAN盒{channelKey} 通信超时，尝试重连...");
                        if (_equipmentInfo.TryGetValue(channelKey, out var equipment))
                        {
                            // 使用统一的重连方法
                            RegisterChannel(equipment, true);
                        }
                    }
                    // 情况2：从未成功注册
                    else if (!isRegistered)
                    {
                        //logger.Info($"设备{channelKey} 首次连接失败，尝试重连...");
                        //LogService.Log($"CAN盒{channelKey} 首次连接失败，尝试重连...");
                        if (_equipmentInfo.TryGetValue(channelKey, out var equipment))
                        {
                            // 使用统一的重连方法
                            RegisterChannel(equipment, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //logger.Info($"重连检查错误: {ex.Message}");
            }
        }

        // 超时检查辅助方法
        private bool CheckTimeout(string channelKey)
        {
            return _lastReceiveTime.TryGetValue(channelKey, out var lastTime) &&
                   (DateTime.Now - lastTime).TotalSeconds > 5;
        }

        // 更新连接状态
        private void UpdateConnectionStatus(string channelKey, bool isConnected)
        {
            _connectionStatus[channelKey] = isConnected;
            OnConnectionStatusChanged?.Invoke(channelKey, isConnected);
        }

        // 新增UI更新回调方法
        private void UIUpdateCallback(object state)
        {
            try
            {
                foreach (var channel in _receiveQueues.Keys)
                {
                    if (_dataHandlers.TryGetValue(channel, out var handler) && handler != null)
                    {
                        var queue = _receiveQueues[channel];
                        var frames = new List<ZCAN_Receive_Data>();

                        // 批量取出队列中的所有帧
                        while (queue.TryDequeue(out var frame))
                        {
                            frames.Add(frame);
                        }

                        if (frames.Count > 0)
                        {
                            // 批量处理帧
                            handler(frames);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //logger.Info($"UI更新错误: {ex.Message}");
            }
        }

        // CANManager.cs

        #endregion

        #region 字段和属性
        // 添加全局发送控制标志
        private static bool _globalSendingEnabled = true;
        private Thread _receiveThread;  // CAN数据接收线程
        private bool _isRunning;                // 控制接收线程运行标志
        private static readonly object _registerLock = new object();
        // 连接状态变化事件
        public event Action<string, bool> OnConnectionStatusChanged;

        private System.Threading.Timer _uiUpdateTimer;
        private const int UI_UPDATE_INTERVAL = 1000; // 1秒更新一次

        // 连接状态字典 <通道键, 连接状态>
        private readonly ConcurrentDictionary<string, bool> _connectionStatus =
            new ConcurrentDictionary<string, bool>();
        // 最后接收时间字典 <通道键, 最后接收时间>
        private readonly ConcurrentDictionary<string, DateTime> _lastReceiveTime =
            new ConcurrentDictionary<string, DateTime>();
        // 存储设备信息 <通道键, 设备信息>
        private readonly Dictionary<string, EquipmentModel> _equipmentInfo =
            new Dictionary<string, EquipmentModel>();

        // 重连定时器
        private System.Threading.Timer _reconnectTimer;
        private const int RECONNECT_INTERVAL = 5000; // 5秒尝试重连一次

        // 在CANManager类中添加以下字段
        public readonly ConcurrentDictionary<string, ConcurrentQueue<ZCAN_Receive_Data>> _receiveQueues =
            new ConcurrentDictionary<string, ConcurrentQueue<ZCAN_Receive_Data>>();

        // 存储设备句柄 <设备号-通道号, (设备句柄, 通道句柄)>
        private readonly Dictionary<string, (IntPtr deviceHandle, IntPtr channelHandle)> _deviceHandles =
            new Dictionary<string, (IntPtr, IntPtr)>();

        // 数据处理器字典 <设备号-通道号, 处理委托>
        private readonly ConcurrentDictionary<string, Action<List<ZCAN_Receive_Data>>> _dataHandlers =
            new ConcurrentDictionary<string, Action<List<ZCAN_Receive_Data>>>();

        // 信号定义字典 <CAN ID, 信号列表>
        private readonly ConcurrentDictionary<uint, List<SignalInfo>> _signalDefinitions =
            new ConcurrentDictionary<uint, List<SignalInfo>>();
        #endregion

        #region 公共方法

        // 添加发送控制方法
        //public static void SetGlobalSendingEnabled(bool enabled)
        //{
        //    _globalSendingEnabled = enabled;
        //    LogService.Log($"全局发送状态: {(enabled ? "启用" : "禁用")}");
        //}

        /*
         * 注册CAN通道
         * 
         * 功能：
         * 1. 初始化CAN设备
         * 2. 配置CAN通道参数
         * 3. 启动CAN通道
         * 
         * 参数：
         * deviceNumber - 设备号
         * channelIndex - 通道索引 (1=CAN1, 2=CAN2)
         * baudRate - 波特率 (bps)
         */
        // 修改RegisterChannel方法，添加超时控制
        public void RegisterChannel(EquipmentModel equipment, bool isReconnect = false)
        {
            //Init();
            lock (_registerLock)
            {
                int deviceIndex = equipment.DeviceIndex;
                int channelIndex = equipment.CanIndex;
                string key = GetChannelKey(deviceIndex, channelIndex);

                if (_deviceHandles.ContainsKey(key) && !isReconnect)
                {
                    UpdateConnectionStatus(key, true);
                    LogService.Log($"{equipment.DeviceNumber}已启动，跳过重复启动");
                    return;
                }
                else
                {
                    LogService.Log($"{equipment.DeviceNumber}通讯失败，重连中...");
                }

                try
                {
                    // +++ 关键修复：检查并关闭旧连接 +++
                    if (!isReconnect && _deviceHandles.ContainsKey(key))
                    {
                        UnregisterChannel(equipment.DeviceIndex, equipment.CanIndex);
                    }

                    // 保存设备信息（无论成功与否）
                    _equipmentInfo[key] = equipment;
                    UpdateConnectionStatus(key, false);

                    // 打开设备
                    IntPtr deviceHandle = ZCAN_OpenDevice(Define.ZCAN_CANETTCP, (uint)deviceIndex, 0);
                    if (deviceHandle == IntPtr.Zero)
                    {
                        LogService.Log($"{equipment.DeviceNumber}打开失败");
                        return;
                    }

                    // 设置网络参数
                    ZCAN_SetValue(deviceHandle, $"{channelIndex}/work_mode", Encoding.ASCII.GetBytes("0"));
                    ZCAN_SetValue(deviceHandle, $"{channelIndex}/ip", Encoding.ASCII.GetBytes(equipment.DeviceIP));
                    ZCAN_SetValue(deviceHandle, $"{channelIndex}/work_port", Encoding.ASCII.GetBytes(equipment.DevicePort));
                    //ZCAN_SetValue(deviceHandle, $"{channelIndex}/recv_buf_size", Encoding.ASCII.GetBytes("20000"));
                    // 优化网络参数
                    //ZCAN_SetValue(deviceHandle, $"{channelIndex}/tcp_linger", Encoding.ASCII.GetBytes("0")); // 关闭Linger
                    //ZCAN_SetValue(deviceHandle, $"{channelIndex}/tcp_no_delay", Encoding.ASCII.GetBytes("1")); // 启用Nagle

                    // 初始化通道配置
                    ZCAN_CHANNEL_INIT_CONFIG config = new ZCAN_CHANNEL_INIT_CONFIG
                    {
                        can_type = Define.TYPE_CAN,
                        filter = 0,
                        acc_code = 0,
                        acc_mask = 0xFFFFFFFF,
                        mode = 0
                    };

                    // 初始化CAN通道
                    IntPtr channelHandle = ZCAN_InitCAN(deviceHandle, (uint)channelIndex, ref config);
                    if (channelHandle == IntPtr.Zero)
                    {
                        LogService.Log($"{equipment.DeviceNumber}初始化失败");
                        ZCAN_CloseDevice(deviceHandle);
                        return;
                    }

                    uint startResult = 0;
                    bool startCompleted = false;
                    Task startTask = Task.Run(() =>
                    {
                        startResult = ZCAN_StartCAN(channelHandle);
                        startCompleted = true;
                    });
                    //等待任务完成
                    bool taskCompleted = startTask.Wait(100);
                    if (!taskCompleted || !startCompleted || startResult != Define.STATUS_OK)
                    {
                        ZCAN_CloseDevice(deviceHandle);
                        LogService.Log($"{equipment.DeviceNumber}启动失败,已关闭");
                        return;
                    }

                    // 更新连接状态
                    _deviceHandles[key] = (deviceHandle, channelHandle);
                    UpdateConnectionStatus(key, true);
                    _lastReceiveTime[key] = DateTime.Now;

                    // +++ 重连成功后关键修复 +++
                    // 1. 确保接收队列存在
                    //_receiveQueues[key] = new ConcurrentQueue<ZCAN_Receive_Data>();
                    //// 2. 重新注册数据处理器
                    //if (isReconnect && _dataHandlers.TryGetValue(key, out var handler))
                    //{
                    //    // 重新绑定处理器
                    //    _dataHandlers[key] = handler;

                    //    // 3. 重新注册信号定义
                    //    if (_equipmentInfo.TryGetValue(key, out var eq))
                    //    {
                    //        foreach (var kvp in eq.SignalDefinitions)
                    //        {
                    //            RegisterSignals(kvp.Key, kvp.Value);
                    //        }
                    //    }
                    //}

                    _receiveQueues.GetOrAdd(key, new ConcurrentQueue<ZCAN_Receive_Data>());

                    //logger.Info($"通道{(isReconnect ? "重连" : "启动")}成功: {key}");
                    LogService.Log($"{equipment.DeviceNumber}{(isReconnect ? "重连" : "启动")}成功: {key}");
                }
                catch (Exception ex)
                {
                    UpdateConnectionStatus(key, false);
                    LogService.Log($"{(isReconnect ? "重连" : "注册")}{equipment.DeviceNumber}失败: {ex.Message}");
                    throw new ApplicationException($"{ex.Message}");
                }

                // +++ 确保接收线程运行 +++
                EnsureReceiveThreadRunning();
            }
        }

        // 确保接收线程运行
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
                LogService.Log("CAN接收线程已重启");
            }
        }

        /*
         * 注册数据处理器
         * 
         * 功能：
         * 为指定通道注册数据接收处理函数
         */
        public void RegisterDataHandler(int deviceIndex, int channelIndex, Action<List<ZCAN_Receive_Data>> handler)
        {
            string key = GetChannelKey(deviceIndex, channelIndex);
            _dataHandlers.AddOrUpdate(key, handler, (k, oldHandler) => oldHandler + handler);
        }

        /*
         * 注销数据处理器
         * 
         * 功能：
         * 移除指定通道的数据处理函数
         */
        public void UnregisterDataHandler(int deviceNumber, int channelIndex, Action<List<ZCAN_Receive_Data>> handler)
        {
            string key = GetChannelKey(deviceNumber, channelIndex);
            if (_dataHandlers.TryGetValue(key, out var existingHandler))
            {
                var newHandler = (Action<List<ZCAN_Receive_Data>>)Delegate.Remove(existingHandler, handler);
                if (newHandler != null)
                    _dataHandlers[key] = newHandler;
                else
                    _dataHandlers.TryRemove(key, out _);
            }
        }

        /*
         * 注册信号定义
         * 
         * 功能：
         * 将信号定义与CAN ID关联
         */
        public void RegisterSignals(uint canId, List<SignalInfo> signals)
        {
            _signalDefinitions.AddOrUpdate(canId, signals, (id, existing) => signals);
        }

        /*
         * 解析信号值
         * 
         * 功能：
         * 从CAN帧中提取并转换信号值
         */
        //public ulong ExtractRawValue(ZCAN_Receive_Data frame, SignalInfo signal)
        public ulong ExtractRawValue(byte[] data, SignalInfo signal)
        {
            //计算起始字节和位偏移
            ulong rawValue = 0;
            int totalBits = data.Length * 8;
            if (signal.StartBit + signal.Length > totalBits)
                throw new ArgumentException("超出数据范围");

            if (signal.ByteOrder == "Inter")//小端模式
            {   //提取位域
                for (int i = 0; i < signal.Length; i++)
                {
                    int byteOffset = (signal.StartBit + i) / 8;
                    int bitOffset = (signal.StartBit + i) % 8;
                    if ((data[byteOffset] & (1 << bitOffset)) != 0)
                    {
                        rawValue |= (1UL << i);
                    }
                }
            }
            else//大端模式
            {
                for (int i = 0; i < signal.Length; i++)
                {
                    int bitIndex = signal.StartBit + i;
                    int byteOffset = bitIndex / 8;
                    int bitOffset = 7 - (bitIndex % 8);
                    if ((data[byteOffset] & (1 << bitOffset)) != 0)
                    {
                        rawValue |= (1UL << (signal.Length - 1 - i));
                    }
                }
            }
            return rawValue;
        }

        /// <summary>
        /// 获取小数位数
        /// </summary>
        /// <param name="decimalV">小数</param>
        /// <returns></returns>
        public int GetNumberOfDecimalPlaces(decimal decimalV)
        {
            string[] temp = decimalV.ToString().Split('.');
            if (temp.Length == 2 && temp[1].Length > 0)
            {
                int index = temp[1].Length - 1;
                while (temp[1][index] == '0' && index-- > 0) ;
                return index + 1;
            }
            return 0;
        }
        //处理有无符号数
        public double ConvertToPhysicalValue(ulong rawValue, SignalInfo signal)
        {
            bool dataType;
            if (signal.Signed == "Signed")
                dataType = true;
            else
                dataType = false;
            //有符号数
            long signedValue = dataType ?
                (long)(rawValue << (64 - signal.Length)) >> (64 - signal.Length) :
                (long)rawValue;

            return Convert.ToDouble(signedValue * signal.Factor + signal.Offset);
        }
        /*
         * 发送CAN命令 (新版API)
         * 
         * 功能：
         * 向指定通道发送CAN帧
         */
        public void SendCommand(int deviceNumber, int channelIndex, uint canId, byte[] data)
        {
            // 检查全局发送状态
            if (!_globalSendingEnabled)
            {
                return;
            }

            string key = GetChannelKey(deviceNumber, channelIndex);
            if (!_deviceHandles.TryGetValue(key, out var handle)) return;

            // 准备发送数据结构 (新版API)
            ZCAN_Transmit_Data can_data = new ZCAN_Transmit_Data();
            can_data.can_id = MakeCanId(canId, 1, 0, 0);
            can_data.data = data;
            can_data.can_dlc = 8;
            can_data.transmit_type = 0;
            // 复制数据
            //Array.Copy(data, frame.data, frame.dlc);

            // 分配非托管内存
            int size = Marshal.SizeOf(can_data);
            IntPtr pFrame = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(can_data, pFrame, true);

            try
            {
                // 发送帧 (新版API)
                uint result = ZCAN_Transmit(handle.channelHandle, pFrame, 1);
                uint formattedCanId = can_data.can_id & 0x1FFFFFFF;  // 提取标准CAN ID
                string hexData = BitConverter.ToString(data).Replace("-", " ");  // 字节数组转HEX字符串
                if (result > 0)
                {
                    if ((formattedCanId == 0xAA01) || (formattedCanId == 0xAA02) ||
                       (formattedCanId == 0xAA03) || (formattedCanId == 0xADCC) || (formattedCanId.ToString("X").Substring(0, 2) == "21"))
                        return;   //不记录日志

                    // 成功日志：包含完整数据
                    LogService.Log($"设备{key} | " +
                                   $"发送成功 | CAN ID: 0x{formattedCanId:X8} | " +
                                   $"数据: {hexData}");
                }
                else
                {
                    // 失败日志：包含错误码和发送内容
                    LogService.Log($"设备{key} | " +
                                   $"发送失败(错误码:{result}) | " +
                                   $"目标CAN ID: 0x{formattedCanId:X8} | " +
                                   $"数据: {hexData}");
                }
            }
            finally
            {
                // 释放非托管内存
                Marshal.FreeHGlobal(pFrame);
            }
        }

        #endregion

        #region 私有方法
        public uint MakeCanId(uint id, int eff, int rtr, int err)//1:extend frame 0:standard frame
        {
            uint ueff = (uint)(!!(Convert.ToBoolean(eff)) ? 1 : 0);
            uint urtr = (uint)(!!(Convert.ToBoolean(rtr)) ? 1 : 0);
            uint uerr = (uint)(!!(Convert.ToBoolean(err)) ? 1 : 0);
            return id | ueff << 31 | urtr << 30 | uerr << 29;
        }
        /*
         * 获取通道键
         * 
         * 功能：
         * 生成设备号-通道号的唯一标识字符串
         */
        public static string GetChannelKey(int deviceNumber, int channelIndex)
        {
            return $"{deviceNumber}-{channelIndex}";
        }

        /*
         * 接收循环
         * 
         * 功能：
         * 后台线程循环接收所有通道的CAN数据
         * 并将数据分发给注册的处理函数
         */
        // 修改接收线程逻辑 - 使用批量处理提高效率
        private void ReceiveLoop()
        {
            //LogService.Log("CAN接收线程已启动");
            const int BATCH_SIZE = 10000; // 增大批处理量
            int structSize = Marshal.SizeOf(typeof(ZCAN_Receive_Data));
            IntPtr buffer = Marshal.AllocHGlobal(BATCH_SIZE * structSize);

            while (_isRunning)
            {
                Parallel.ForEach(_deviceHandles, device =>
                {
                    string key = device.Key;
                    var channelHandle = device.Value.channelHandle;

                    // 获取待处理帧数（API调用优化）
                    uint pendingFrames = ZCAN_GetReceiveNum(channelHandle, 0);
                    if (pendingFrames == 0) return;

                    // 批量读取
                    uint framesToRead = Math.Min(pendingFrames, BATCH_SIZE);
                    uint actualRead = ZCAN_Receive(channelHandle, buffer, framesToRead, 0);

                    if (actualRead > 0)
                    {
                        var queue = _receiveQueues.GetOrAdd(key, _ => new ConcurrentQueue<ZCAN_Receive_Data>());
                        var batch = new ZCAN_Receive_Data[actualRead];

                        // 批量复制到托管内存
                        for (int i = 0; i < actualRead; i++)
                        {
                            batch[i] = Marshal.PtrToStructure<ZCAN_Receive_Data>(
                                IntPtr.Add(buffer, i * structSize));
                        }

                        // 批量入队（减少锁竞争）
                        foreach (var frame in batch)
                        {
                            queue.Enqueue(frame);
                        }

                        // 更新最后接收时间
                        _lastReceiveTime[key] = DateTime.Now;
                    }
                });

                Thread.Sleep(1); // 适当降低CPU占用
            }

            Marshal.FreeHGlobal(buffer);
        }

        public async Task<ZCAN_Receive_Data> ReceiveFrameAsync(string channelKey, uint expectedCanId, int timeoutMs)
        {
            var startTime = DateTime.Now;
            //expectedCanId &= 0x7FF;  // 确保比较时使用11位标准帧

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (_receiveQueues.TryGetValue(channelKey, out var queue))
                {
                    // 临时列表存储不匹配的帧
                    var tempList = new List<ZCAN_Receive_Data>();

                    while (queue.TryDequeue(out var frame))
                    {
                        // 提取接收帧CAN ID
                        uint receivedId = frame.can_id & 0x1FFFFFFF;
                        string hexData = BitConverter.ToString(frame.data).Replace("-", " ");  // 字节数组转HEX字符串
                        if (receivedId == expectedCanId)
                        {
                            // 将不匹配的帧重新入队
                            foreach (var f in tempList)
                            {
                                queue.Enqueue(f);
                            }

                            if ((receivedId == 0xBB01) || (receivedId == 0xBB02) ||
                               (receivedId == 0xBB03))
                            {
                                return frame;
                            }

                            // 成功日志：包含完整数据
                            LogService.Log($"设备{channelKey} | " +
                                           $"接收成功 | CAN ID: 0x{receivedId:X8} | " +
                                           $"数据: {hexData}");

                            return frame;
                        }
                        else
                        {
                            tempList.Add(frame);
                            // 成功日志：包含完整数据
                            //LogService.Log($"设备{channelKey} | " +
                            //               $"接收失败 | 目标CAN ID: 0x{expectedCanId:X8}");

                        }
                    }

                    // 没有找到匹配帧，将临时列表中的帧重新入队
                    foreach (var f in tempList)
                    {
                        queue.Enqueue(f);
                    }
                }
                await Task.Delay(0);
            }
            return new ZCAN_Receive_Data(); // 返回空帧
        }

        public async Task<Dictionary<uint, List<ZCAN_Receive_Data>>> ReceiveMultipleFramesAsync(
            string channelKey,
            List<uint> expectedCanIds,
            int timeoutMs)
        {
            var results = new Dictionary<uint, List<ZCAN_Receive_Data>>();
            var stopwatch = Stopwatch.StartNew();

            // 预先初始化结果集
            foreach (var canId in expectedCanIds)
            {
                results[canId] = new List<ZCAN_Receive_Data>();
            }

            // 使用HashSet提高查找效率
            var expectedIdsSet = new HashSet<uint>(expectedCanIds);

            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                if (!_receiveQueues.TryGetValue(channelKey, out var queue))
                {
                    await Task.Delay(5);
                    continue;
                }

                // 每次处理最大1000帧，避免阻塞
                int processed = 0;
                const int MAX_BATCH = 1000;

                while (queue.TryDequeue(out var frame) && processed++ < MAX_BATCH)
                {
                    uint receivedId = frame.can_id & 0x1FFFFFFF;
                    if (expectedIdsSet.Contains(receivedId))
                    {
                        results[receivedId].Add(frame);
                    }
                }

                // 检查是否已完成所有帧的接收
                bool allComplete = true;
                foreach (var canId in expectedCanIds)
                {
                    if (results[canId].Count < 1000) // 假设每个ID期望1000帧
                    {
                        allComplete = false;
                        break;
                    }
                }

                if (allComplete) break;
            }

            return results;
        }


        // 在CANManager类中添加以下方法
        public void ClearQueue(string channelKey)
        {
            try
            {
                if (_receiveQueues.TryGetValue(channelKey, out var queue))
                {
                    // 高效清空队列的三种方法（选择一种实现）

                    // 方法1：直接替换为新队列（最推荐）
                    _receiveQueues[channelKey] = new ConcurrentQueue<ZCAN_Receive_Data>();

                    // 方法2：循环出队直到清空（低效，但确保内存释放）
                    // while (queue.TryDequeue(out _)) { }

                    // 方法3：使用计数器和批量出队（折中方案）
                    // const int BATCH_SIZE = 1000;
                    // var temp = new ZCAN_Receive_Data[BATCH_SIZE];
                    // while (queue.TryDequeue(out var frame))
                    // {
                    //     // 无需处理，直接丢弃
                    // }

                    // 记录清空操作
                    //logger.Info($"通道 {channelKey} 队列已清空");
                    LogService.Log($"已清空 {channelKey} 接收队列");
                }
                else
                {
                    //logger.Warn($"清空队列失败: 未找到通道 {channelKey}");
                }
            }
            catch (Exception ex)
            {
                //logger.Error($"清空队列时出错: {ex.Message}");
                LogService.Log($"清空队列错误: {ex.Message}");
            }
        }
        #endregion

        #region 资源清理

        // 添加完全重置方法
        public void FullReset()
        {
            lock (_registerLock)
            {
                // 1. 停止所有活动
                _isRunning = false;

                // 2. 停止并释放定时器
                _uiUpdateTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _uiUpdateTimer?.Dispose();
                _uiUpdateTimer = null;

                _reconnectTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _reconnectTimer?.Dispose();
                _reconnectTimer = null;

                // 3. 等待接收线程结束
                if (_receiveThread != null && _receiveThread.IsAlive)
                {
                    _receiveThread.Join(2000);
                    if (_receiveThread.IsAlive)
                    {
                        try { _receiveThread.Interrupt(); } catch { }
                    }
                    _receiveThread = null;
                }

                // 4. 关闭所有设备并清理资源
                var keys = _deviceHandles.Keys.ToList();
                foreach (var key in keys)
                {
                    try
                    {
                        var handles = _deviceHandles[key];
                        if (handles.deviceHandle != IntPtr.Zero)
                        {
                            ZCAN_CloseDevice(handles.deviceHandle);
                        }
                    }
                    catch { }
                }

                // 5. 清空所有字典
                _deviceHandles.Clear();
                _dataHandlers.Clear();
                _receiveQueues.Clear();
                _equipmentInfo.Clear();
                _lastReceiveTime.Clear();
                _connectionStatus.Clear();
                _signalDefinitions.Clear();

                LogService.Log("CAN管理器已完全重置");
            }
        }
        /*
         * 释放资源
         * 
         * 功能：
         * 1. 停止接收线程
         * 2. 关闭所有CAN通道
         * 3. 关闭设备
         */
        public void Dispose()
        {
            FullReset();  // 使用统一的重置方法
        }

        public void UnregisterChannel(int deviceIndex, int canIndex)
        {
            string key = GetChannelKey(deviceIndex, canIndex);

            lock (_registerLock)
            {
                if (_deviceHandles.TryGetValue(key, out var handles))
                {
                    try
                    {
                        // 关闭设备
                        if (handles.deviceHandle != IntPtr.Zero)
                        {
                            ZCAN_CloseDevice(handles.deviceHandle);
                        }

                        // 移除相关资源
                        _deviceHandles.Remove(key);
                        _lastReceiveTime.TryRemove(key, out _);
                        _receiveQueues.TryRemove(key, out _);
                        _connectionStatus.TryRemove(key, out _);
                        _equipmentInfo.Remove(key);  // 关键修复：移除特定设备信息

                        LogService.Log($"通道 {key} 已关闭");
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"关闭通道 {key} 失败: {ex.Message}");
                    }
                }
            }
        }

        #endregion
    }
}