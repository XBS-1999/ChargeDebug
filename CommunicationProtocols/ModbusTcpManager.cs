using Log;
using System.Collections.Concurrent;
using TcpCommunicationLib;
using DataModel;

#pragma warning disable
namespace CommunicationProtocols
{
    /// <summary>
    /// 全局Modbus TCP管理器，逻辑完全对齐CANManager
    /// </summary>
    public sealed class ModbusTcpManager
    {
        #region 单例
        private static readonly ModbusTcpManager _instance = new ModbusTcpManager();
        public static ModbusTcpManager Instance => _instance;
        private ModbusTcpManager() { }
        #endregion

        #region 通道管理（模仿CAN通道注册）
        // Key: 设备索引_从站标识(ip:port_slaveId)
        private readonly ConcurrentDictionary<string, TcpClientHelper> _tcpChannels = new();
        // 信号注册：key=通道标识，值=该通道所有Modbus信号
        private readonly ConcurrentDictionary<string, List<ModbusSignal>> _channelSignals = new();
        // 帧接收回调：模块注册的数据分发事件
        public event Action<string, byte[]> OnModbusFrameReceived;
        // 连接状态回调
        public event Action<string, bool> OnConnectionStatusChanged;
        // 全局轮询定时器（统一周期轮询所有注册通道）
        private System.Threading.Timer _globalPollTimer;
        private const int PollInterval = 500;
        private volatile bool _running = false;
        private readonly object _lockObj = new();
        #endregion

        #region 对外API（对齐CANManager接口）
        /// <summary>
        /// 初始化全局轮询，程序启动调用
        /// </summary>
        public void Init()
        {
            lock (_lockObj)
            {
                if (_running) return;
                _running = true;
                _globalPollTimer = new System.Threading.Timer(GlobalPollTask, null, 1000, PollInterval);
                LogService.Log("ModbusTcpManager 全局轮询已启动");
            }
        }

        /// <summary>
        /// 注册Modbus通道（对应CAN RegisterChannel）
        /// key格式：设备索引_IP:端口_从站ID
        /// </summary>
        public void RegisterChannel(string channelKey, List<ModbusSignal> signals)
        {
            if (_tcpChannels.ContainsKey(channelKey)) return;

            var firstSig = signals.First();
            TcpClientHelper tcp = new TcpClientHelper();
            // 绑定状态回调
            tcp.ConnectionStatusChanged += (s, conn) =>
            {
                OnConnectionStatusChanged?.Invoke(channelKey, conn);
                if (!conn)
                {
                    // 断线自动重连
                    Task.Run(async () =>
                    {
                        await Task.Delay(3000);
                        ReconnectChannel(channelKey);
                    });
                }
            };
            tcp.LogMessage += (s, msg) => LogService.Log($"[Modbus{channelKey}] {msg}");

            // 绑定接收回调，全局分发帧
            tcp.DataReceived += (s, frame) =>
            {
                OnModbusFrameReceived?.Invoke(channelKey, frame);
            };

            // 建立连接
            try
            {
                tcp.Connect(firstSig.ModbusIp, firstSig.ModbusPort);
                _tcpChannels[channelKey] = tcp;
                _channelSignals[channelKey] = signals;
                LogService.Log($"Modbus通道注册成功：{channelKey}");
            }
            catch (Exception ex)
            {
                LogService.Log($"Modbus通道{channelKey}注册失败：{ex.Message}");
                tcp.Dispose();
            }
        }

        /// <summary>
        /// 注销通道（对应CAN UnregisterChannel）
        /// </summary>
        public void UnregisterChannel(string channelKey)
        {
            if (_tcpChannels.TryRemove(channelKey, out var tcp))
            {
                tcp.Disconnect();
                tcp.Dispose();
            }
            _channelSignals.TryRemove(channelKey, out _);
        }

        /// <summary>
        /// 注册信号定义（对应CAN RegisterSignals）
        /// </summary>
        public void RegisterSignals(string channelKey, List<ModbusSignal> signals)
        {
            _channelSignals[channelKey] = signals;
        }

        /// <summary>
        /// 重连单个通道
        /// </summary>
        private void ReconnectChannel(string channelKey)
        {
            UnregisterChannel(channelKey);
            if (_channelSignals.TryGetValue(channelKey, out var sigs))
            {
                RegisterChannel(channelKey, sigs);
            }
        }

        /// <summary>
        /// 全局轮询任务：批量发送Modbus读寄存器请求
        /// </summary>
        private async void GlobalPollTask(object state)
        {
            if (!_running) return;

            foreach (var kv in _tcpChannels)
            {
                string chKey = kv.Key;
                TcpClientHelper tcp = kv.Value;
                if (!tcp.IsConnected) continue;
                if (!_channelSignals.TryGetValue(chKey, out var sigList)) continue;

                // 按从站合并连续寄存器
                // 全局轮询任务内分组代码替换
                var groupBySlave = sigList.GroupBy(s => new
                {
                    s.SlaveId,
                    FuncByte = byte.Parse(s.FunctionCode!)
                });
                foreach (var group in groupBySlave)
                {
                    byte slaveId = group.Key.SlaveId;
                    byte funcCode = group.Key.FuncByte;
                    var groupSigs = group.ToList();

                    ushort minAddr = ushort.Parse(groupSigs.Min(s => s.CorrespondenceAddress)!);
                    ushort maxAddr = ushort.Parse(groupSigs.Max(s => s.CorrespondenceAddress)!);
                    ushort readLen = (ushort)(maxAddr - minAddr + 1);

                    // 构造标准Modbus读寄存器帧
                    byte[] req = new byte[6];
                    req[0] = slaveId;
                    req[1] = funcCode;
                    req[2] = (byte)(minAddr >> 8);
                    req[3] = (byte)(minAddr & 0xFF);
                    req[4] = (byte)(readLen >> 8);
                    req[5] = (byte)(readLen & 0xFF);

                    // 发送请求
                    tcp.Send(false, req);
                    // 等待应答
                    byte[] resp = await tcp.ReceiveCommandAsync(funcCode, 800);
                    if (resp == null || resp.Length < 3) continue;

                    // 解析寄存器数组
                    int dataByteCnt = resp[2];
                    ushort[] regs = new ushort[dataByteCnt / 2];
                    for (int i = 0; i < regs.Length; i++)
                    {
                        regs[i] = (ushort)(resp[3 + i * 2] << 8 | resp[4 + i * 2]);
                    }

                    // 回调分发原始帧+寄存器数据，交给Module解析
                    var parsePack = new ModbusParsePacket
                    {
                        MinAddress = minAddr,
                        Registers = regs,
                        Signals = groupSigs
                    };
                    ModbusFrameDistribute(chKey, parsePack);
                }
            }
        }

        /// <summary>
        /// 分发解析数据包给对应模块回调
        /// </summary>
        private void ModbusFrameDistribute(string channelKey, ModbusParsePacket packet)
        {
            // 向外抛事件，Module订阅统一处理（完全对齐CAN HandleCANFrame）
            OnModbusFrameReceived?.Invoke(channelKey, SerializePacket(packet));
        }

        // 序列化简易数据包透传
        private byte[] SerializePacket(ModbusParsePacket pkt)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(pkt.MinAddress);
            bw.Write(pkt.Registers.Length);
            foreach (var r in pkt.Registers) bw.Write(r);
            return ms.ToArray();
        }

        /// <summary>
        /// 释放全部资源（程序退出调用）
        /// </summary>
        public void Dispose()
        {
            _running = false;
            _globalPollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _globalPollTimer?.Dispose();

            foreach (var kv in _tcpChannels)
            {
                kv.Value.Disconnect();
                kv.Value.Dispose();
            }
            _tcpChannels.Clear();
            _channelSignals.Clear();
            OnModbusFrameReceived = null;
            OnConnectionStatusChanged = null;
        }
        #endregion
    }

    /// <summary>
    /// Modbus轮询解析数据包，用于全局分发
    /// </summary>
    public class ModbusParsePacket
    {
        public ushort MinAddress { get; set; }
        public ushort[] Registers { get; set; }
        public List<ModbusSignal> Signals { get; set; }
    }
}
