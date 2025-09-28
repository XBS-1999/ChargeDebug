using ChargeDebug.Service;
using DataModel;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraLayout;
using DevExpress.XtraLayout.Utils;
using Log;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;

#pragma warning disable
namespace ChargeDebug.Form
{
    /*
     * 监控模块类 (集成新版ZCAN API)
     * 负责设备监控、CAN通信、故障检测和用户界面管理
     */
    public partial class Module : XtraUserControl, IDisposable
    {
        // ==================== 字段声明区域 ====================
        #region 字段声明

        public static readonly Dictionary<string, StartupManager> StartupManagers = new Dictionary<string, StartupManager>();

        // 模块控制字段
        private bool _moduleSendingEnabled = true;
        private bool _initialized = false;
        private volatile bool _disposed = false;
        private bool _isConnected;
        private bool _stopCommandSent = false;
        private bool _isFaultDisplayActive;

        // CAN通信相关字段
        private uint _dcfaultCanId;
        private uint _acfaultCanId;
        private uint _voltageCanId;
        private uint _acreadFaultCanId;
        private uint _dcreadFaultCanId;

        // 设备状态字段
        private uint _acRunStatus = 0;
        private uint _dcRunStatus = 0;
        private uint _acRunMode = 0;
        private uint _dcRunMode = 0;
        //private uint currentStatus;

        // 时间管理字段
        private DateTime _startTime;
        private DateTime _stepStartTime;
        private Showdata _totalTimeData;
        private Showdata _stepTimeData;

        // 信号处理字段
        private readonly Dictionary<string, SignalInfo> _faultSignalDefinitions = new();
        private readonly Dictionary<string, string> _activeFaults = new();
        private Dictionary<string, SignalInfo> signalDefinitions = new Dictionary<string, SignalInfo>();
        private readonly Dictionary<string, Dictionary<string, string>> _reuseMappings = new();
        private Dictionary<uint, List<SignalInfo>> canIdSignals = new Dictionary<uint, List<SignalInfo>>();
        private readonly ConcurrentDictionary<string, double> _signalValues = new ConcurrentDictionary<string, double>();

        // 故障显示字段
        private readonly Queue<string> _faultDisplayQueue = new Queue<string>();
        private readonly object _faultQueueLock = new object();

        // UI组件字段
        private GridControl gridControl;
        private GridView gridview;
        private PanelControl panelControl;
        private LabelControl lblConnectionStatus;
        private LabelControl lblACStatus, lblACMode;
        private LabelControl lblDCStatus, lblDCMode;
        private string _title;
        private static readonly object _canInitLock = new object();
        private BindingList<Showdata> signalData = new BindingList<Showdata>();
        private ContextMenuStrip contextMenu;
        private List<Showdata> allSignals = new List<Showdata>();
        private StartConfiguration? _paramForm;

        // 管理类字段
        private StartupManager _startupManager;
        private EquipmentModel _equipment;
        private ConfigurationData _protectionParameters;

        // 定时器字段
        private System.Threading.Timer _uiUpdateTimer;
        private System.Threading.Timer _readFaultTimer;
        private const int UI_UPDATE_INTERVAL = 1000;

        // 常量定义
        private const string VOLTAGE_SIGNAL = "蓄电池电压";
        private const string CURRENT_SIGNAL = "蓄电池电流";
        private const string POWER_SIGNAL = "蓄电池功率";

        public ConfigurationData ProtectionParameters { get; set; }

        public event Action<string> FaultDetected; // 专门用于故障通知的事件

        #endregion

        // ==================== 属性区域 ====================
        #region 属性

        /// <summary>
        /// 模块发送使能状态
        /// </summary>
        public bool ModuleSendingEnabled
        {
            get => _moduleSendingEnabled;
            set => _moduleSendingEnabled = value;
        }

        /// <summary>
        /// 设备连接状态
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;

                if (!_isConnected)
                {
                    UpdateConnectionStatusUI("已断开(重连中...)", Color.Red);
                }
                //UpdateConnectionStatusUI("", Color.White);
            }
        }

        #endregion

        // ==================== 构造函数与初始化区域 ====================
        #region 构造函数与初始化

        /// <summary>
        /// 模块构造函数
        /// </summary>
        /// <param name="title">模块标题</param>
        /// <param name="equipment">设备模型</param>
        /// <param name="signals">信号列表</param>
        public Module(string title, EquipmentModel equipment, List<SignalInfo> signals)
        {
            _equipment = equipment;
            _title = title;
            InitializeComponent();
            InitializeUI();
            ProcessSignals(signals);

            // 初始化UI更新定时器
            _uiUpdateTimer = new System.Threading.Timer(_ =>
            {
                UpdateUIFromCache();
                UpdateTimeDisplay();
            }, null, UI_UPDATE_INTERVAL, UI_UPDATE_INTERVAL);

            // 初始化启动管理器
            _startupManager = new StartupManager(equipment, title);
            string channelPart = ExtractStandardDeviceName(title);
            StartupManagers[channelPart] = _startupManager;

            // 初始化读取故障定时器（初始不启动）
            _readFaultTimer = new System.Threading.Timer(SendReadFaultCommand, null, Timeout.Infinite, Timeout.Infinite);

            this.Load += Module_Load;
        }

        /// <summary>
        /// 初始化用户界面
        /// </summary>
        private void InitializeUI()
        {
            // 设置模块容器大小
            this.ClientSize = new Size(400, 700);

            // 通道容器
            GroupControl groupControl = new GroupControl();
            groupControl.Text = _title;
            groupControl.Dock = DockStyle.Fill;
            groupControl.Padding = new System.Windows.Forms.Padding(-3);
            groupControl.Margin = new System.Windows.Forms.Padding(0);

            // 主布局容器
            LayoutControl layoutControl = new LayoutControl();
            layoutControl.Dock = DockStyle.Fill;
            layoutControl.Root.Padding = new DevExpress.XtraLayout.Utils.Padding(-3);
            groupControl.Controls.Add(layoutControl);

            // 添加控件项
            AddACDCStatusRows(layoutControl);
            AddParameters(layoutControl);
            AddExceptionAlert(layoutControl);

            // 添加上下文菜单
            InitializeContextMenu();

            this.Controls.Add(groupControl);
        }

        /// <summary>
        /// 初始化上下文菜单
        /// </summary>
        private void InitializeContextMenu()
        {
            contextMenu = new ContextMenuStrip();
            var dataselection = new ToolStripMenuItem("数据选择");
            var poweron = new ToolStripMenuItem("启动测试");
            var parameterset = new ToolStripMenuItem("参数设置");
            var shutDown = new ToolStripMenuItem("停止测试");
            var clearfault = new ToolStripMenuItem("清除故障");
            var lowvoltage = new ToolStripMenuItem("电压低档");
            var gavoltage = new ToolStripMenuItem("电压高档");

            dataselection.Click += ShowSignalSelector;
            poweron.Click += PoweronAsync;
            parameterset.Click += ParameterSet;
            shutDown.Click += ShutDown;
            clearfault.Click += Clearfault;
            lowvoltage.Click += Wvoltage;
            gavoltage.Click += Gavoltage;

            contextMenu.Items.AddRange(new[] { dataselection, poweron, parameterset, shutDown, clearfault });
            gridControl.ContextMenuStrip = contextMenu;
        }

        #endregion

        // ==================== 事件处理区域 ====================
        #region 事件处理

        /// <summary>
        /// 模块加载事件处理
        /// </summary>
        private void Module_Load(object? sender, EventArgs e)
        {
            if (_disposed) return;

            // 确保只初始化一次
            if (!_initialized)
            {
                Task.Run(() => InitializeCAN(_title));
                _initialized = true;
            }
        }

        /// <summary>
        /// 连接状态变化事件处理
        /// </summary>
        private void HandleConnectionStatusChanged(string key, bool status)
        {
            if (key == CANManager.GetChannelKey(_equipment.DeviceIndex, _equipment.CanIndex))
            {
                this.BeginInvoke((Action)(() => IsConnected = status));
            }
        }

        #endregion

        // ==================== CAN通信区域 ====================
        #region CAN通信

        /// <summary>
        /// 初始化CAN通信
        /// </summary>
        private void InitializeCAN(string title)
        {
            lock (_canInitLock)
            {
                // 双重检查防止重复初始化
                if (_disposed || !this.IsHandleCreated) return;

                try
                {
                    CANManager.Instance.Init();

                    // 1. 注册数据处理器
                    CANManager.Instance.RegisterDataHandler(_equipment.DeviceIndex, _equipment.CanIndex, HandleCANFrame);

                    // 2. 向CAN管理器注册信号定义
                    foreach (var kvp in canIdSignals)
                    {
                        CANManager.Instance.RegisterSignals(kvp.Key, kvp.Value);
                    }

                    // 3. 注册连接状态变化事件
                    CANManager.Instance.OnConnectionStatusChanged += HandleConnectionStatusChanged;

                    // 4. 向CAN管理器注册通道
                    CANManager.Instance.RegisterChannel(_equipment);
                    LogService.Log($"{title}注册成功:{_equipment.DeviceIP}");

                    // 初始化前先发送停机指令，只发一次
                    //SendStopCommandIfNeeded();
                }
                catch (Exception ex)
                {
                    LogService.Log($"{title}注册失败：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// CAN数据帧处理函数
        /// </summary>
        private void HandleCANFrame(List<CANManager.ZCAN_Receive_Data> frames)
        {
            // 检查销毁状态和句柄
            if (_disposed || this.IsDisposed || !this.IsHandleCreated)
                return;

            foreach (var frame in frames)
            {
                // 提取标准CAN ID（去除扩展位）
                uint canId = frame.can_id & 0x1FFFFFFF;

                // 检查该CAN ID是否有注册的信号
                if (!canIdSignals.TryGetValue(canId, out var signals)) continue;

                foreach (var signal in signals)
                {
                    if (!signalDefinitions.TryGetValue(signal.SystemName, out var signalDef)) continue;

                    // 1. 从CAN帧中提取原始值
                    ulong rawValue = CANManager.Instance.ExtractRawValue(frame.data, signalDef);

                    // 2. 转换为物理值
                    double value = CANManager.Instance.ConvertToPhysicalValue(rawValue, signalDef);

                    // 3. 根据精度要求四舍五入
                    int decimalPlaces = CANManager.Instance.GetNumberOfDecimalPlaces(signalDef.Factor);
                    double physicalValue = Math.Round(value, decimalPlaces);

                    // 4. 更新缓存（实时更新）
                    _signalValues[signal.SystemName] = physicalValue;

                    // 5. 实时处理故障信号（不等待UI更新）
                    if (_faultSignalDefinitions.ContainsKey(signal.SystemName))
                    {
                        ProcessFaultSignal(signal.SystemName, physicalValue);
                    }

                    // 6. 实时监控关键信号（电压、电流、功率）
                    if (_protectionParameters != null && _stopCommandSent)
                    {
                        CheckCriticalSignals(signal.SystemName, physicalValue);
                    }

                    // 7. 实时更新运行状态
                    UpdateDeviceStatus(signal.SystemName, physicalValue);
                }
            }
        }

        /// <summary>
        /// 发送读取故障指令
        /// </summary>
        private void SendReadFaultCommand(object? state)
        {
            // 检查模块发送状态
            if (!_moduleSendingEnabled)
            {
                return;
            }

            if ((_acRunStatus == 0xFF) && (_acreadFaultCanId != 0))
            {
                // 构造读取故障指令
                byte[] data = new byte[8];
                data[0] = 0x01;
                CANManager.Instance.SendCommand(
                        _equipment.DeviceIndex,
                        _equipment.CanIndex,
                        _acreadFaultCanId,
                        data
                    );
            }

            if ((_dcRunStatus == 0xFF) && (_dcreadFaultCanId != 0))
            {
                // 构造读取故障指令
                byte[] data = new byte[8];
                data[0] = 0x01;
                CANManager.Instance.SendCommand(
                        _equipment.DeviceIndex,
                        _equipment.CanIndex,
                        _dcreadFaultCanId,
                        data
                    );
            }
        }

        #endregion

        // ==================== 信号处理区域 ====================
        #region 信号处理

        /// <summary>
        /// 从设备名称字符串中提取标准化的设备标识符
        /// 例如："设备1-通道A1/DCn" → "设备1-DCn"
        /// </summary>
        /// <param name="deviceName">原始设备名称</param>
        /// <returns>标准化的设备标识符</returns>
        public static string ExtractStandardDeviceName(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
                return deviceName;

            try
            {
                // 查找"/DC"的位置
                int dcIndex = deviceName.IndexOf("/DC");
                if (dcIndex < 0)
                {
                    // 如果没有找到"/DC"，尝试查找"DC"（可能没有斜杠）
                    dcIndex = deviceName.IndexOf("DC");
                    if (dcIndex < 0)
                    {
                        // 如果连"DC"都找不到，返回原始名称
                        return deviceName;
                    }

                    // 提取设备前缀和DC部分
                    string devicePrefix = deviceName.Substring(0, deviceName.IndexOf('-') + 1);
                    string dcPart = deviceName.Substring(dcIndex);
                    return $"{devicePrefix}{dcPart}";
                }

                // 提取设备前缀（"设备1-"部分）
                string prefix = deviceName.Substring(0, deviceName.IndexOf('-') + 1);

                // 提取DC部分（"DCn"部分）
                string dcPartWithSlash = deviceName.Substring(dcIndex + 1); // 去掉斜杠

                return $"{prefix}{dcPartWithSlash}";
            }
            catch (Exception ex)
            {
                // 记录错误但返回原始名称，避免中断流程
                LogService.Log($"提取标准化设备名称时出错: {ex.Message}");
                return deviceName;
            }
        }

        /// <summary>
        /// 处理信号定义
        /// </summary>
        private void ProcessSignals(List<SignalInfo> signals)
        {
            // 添加时间显示项到信号列表
            allSignals.Add(new Showdata
            {
                SystemName = "累计运行时间",
                Unit = "时:分:秒",
            });

            allSignals.Add(new Showdata
            {
                SystemName = "工步运行时间",
                Unit = "时:分:秒",
            });

            signalData.Add(new Showdata
            {
                SystemName = "累计运行时间",
                Unit = "时:分:秒",
            });

            signalData.Add(new Showdata
            {
                SystemName = "工步运行时间",
                Unit = "时:分:秒",
            });

            // 保存时间显示项的引用
            _totalTimeData = signalData.First(s => s.SystemName == "累计运行时间");
            _stepTimeData = signalData.First(s => s.SystemName == "工步运行时间");

            foreach (var signal in signals)
            {
                // 将十六进制CAN ID转换为整数
                if (uint.TryParse(signal.CANID.Replace("0x", ""),
                    System.Globalization.NumberStyles.HexNumber,
                    null, out uint canId))
                {
                    // 按CAN ID分组
                    if (!canIdSignals.ContainsKey(canId))
                    {
                        canIdSignals[canId] = new List<SignalInfo>();
                    }
                    canIdSignals[canId].Add(signal);

                    // 添加到信号定义字典
                    signalDefinitions[signal.SystemName] = signal;

                    if (signal.SystemName.Contains("故障-"))
                    {
                        _faultSignalDefinitions[signal.SystemName] = signal;
                    }
                    else
                    {
                        ProcessNonFaultSignal(signal, canId);
                    }
                }

                // 存储复用信号映射
                if (signal.ReuseSignals != null && signal.ReuseSignals.Count > 0)
                {
                    var mapping = new Dictionary<string, string>();
                    foreach (var reuse in signal.ReuseSignals)
                    {
                        mapping[reuse.Value] = reuse.Description;
                    }
                    _reuseMappings[signal.SystemName] = mapping;
                }
            }

            // 新增故障信号统计
            LogService.Log($"共发现 {_faultSignalDefinitions.Count} 个故障信号");

            // 加载默认全部信号到表格
            gridControl.DataSource = signalData;
        }

        /// <summary>
        /// 处理非故障信号
        /// </summary>
        private void ProcessNonFaultSignal(SignalInfo signal, uint canId)
        {
            switch (signal.SystemName)
            {
                case "AC运行状态":
                    lblACStatus.Text = $"{signal.SystemName}:";
                    break;
                case "AC运行模式":
                    lblACMode.Text = $"{signal.SystemName}:";
                    break;
                case "DC运行状态":
                    lblDCStatus.Text = $"{signal.SystemName}:";
                    break;
                case "DC运行模式":
                    lblDCMode.Text = $"{signal.SystemName}:";
                    break;
                case "生命帧":
                    break;
                case "清除故障DC":
                    _dcfaultCanId = uint.Parse(signal.CANID.Replace("0x", ""),
                        System.Globalization.NumberStyles.HexNumber);
                    break;
                case "清除故障AC":
                    _acfaultCanId = uint.Parse(signal.CANID.Replace("0x", ""),
                        System.Globalization.NumberStyles.HexNumber);
                    break;
                case "读取AC故障":
                    _acreadFaultCanId = uint.Parse(signal.CANID.Replace("0x", ""),
                        System.Globalization.NumberStyles.HexNumber);
                    break;
                case "读取DC故障":
                    _dcreadFaultCanId = uint.Parse(signal.CANID.Replace("0x", ""),
                        System.Globalization.NumberStyles.HexNumber);
                    break;
                case "电压档位选择":
                    _voltageCanId = uint.Parse(signal.CANID.Replace("0x", ""),
                        System.Globalization.NumberStyles.HexNumber);
                    break;
                default:
                    allSignals.Add(new Showdata
                    {
                        SystemName = signal.SystemName,
                        Unit = signal.Unit,
                    });
                    signalData.Add(new Showdata
                    {
                        SystemName = signal.SystemName,
                        Unit = signal.Unit,
                    });
                    break;
            }
        }

        /// <summary>
        /// 获取复用信号显示文本
        /// </summary>
        private string GetReuseSignalDisplayText(string signalName, double value)
        {
            if (_reuseMappings.TryGetValue(signalName, out var mapping))
            {
                // 将数值转换为十六进制键
                string key = $"0x{Convert.ToInt32(value).ToString("X2")}";

                if (mapping.TryGetValue(key, out var description))
                {
                    return description;
                }
                return $"未知值({value})";
            }

            // 非复用信号：根据信号定义格式化
            if (signalDefinitions.TryGetValue(signalName, out var signalDef))
            {
                int decimalPlaces = CANManager.Instance.GetNumberOfDecimalPlaces(signalDef.Factor);
                return Math.Round(value, decimalPlaces).ToString();
            }

            return value.ToString();
        }

        #endregion

        // ==================== 设备状态管理区域 ====================
        #region 设备状态管理

        /// <summary>
        /// 更新设备状态
        /// </summary>
        private void UpdateDeviceStatus(string signalName, double physicalValue)
        {
            uint oldDcRunStatus = _dcRunStatus;
            uint oldAcRunStatus = _acRunStatus;

            switch (signalName)
            {
                case "AC运行状态":
                    _acRunStatus = Convert.ToUInt32(physicalValue);
                    break;
                case "DC运行状态":
                    _dcRunStatus = Convert.ToUInt32(physicalValue);
                    _startupManager.DCRunStatus(_dcRunStatus);
                    break;
                case "AC运行模式":
                    _acRunMode = Convert.ToUInt32(physicalValue);
                    break;
                case "DC运行模式":
                    _dcRunMode = Convert.ToUInt32(physicalValue);
                    break;
            }

            // 检查状态变化并通知
            if (signalName == "DC运行状态" && oldDcRunStatus != _dcRunStatus)
            {
                // 如果是故障状态，触发故障事件
                if (_dcRunStatus == 0xFF)
                {
                    string faultMessage = "DC设备故障状态检测";
                    FaultDetected?.Invoke(faultMessage);
                    LogService.Log(faultMessage);
                }
            }

            CheckAndControlFaultTimer(oldDcRunStatus, oldAcRunStatus);

            if ((_acRunStatus == 0xFF) || (_dcRunStatus == 0xFF))
            {
                // 触发停机指令，只发一次
                SendStopCommandIfNeeded();
            }
        }

        /// <summary>
        /// 检查和控制故障读取定时器
        /// </summary>
        private void CheckAndControlFaultTimer(uint oldDcStatus, uint oldAcStatus)
        {
            // 检查DC运行状态是否变为故障状态
            bool dcBecameFault = (oldDcStatus != 0xFF && _dcRunStatus == 0xFF);
            bool dcRecovered = (oldDcStatus == 0xFF && _dcRunStatus != 0xFF);

            // 检查AC运行状态是否变为故障状态
            bool acBecameFault = (oldAcStatus != 0xFF && _acRunStatus == 0xFF);
            bool acRecovered = (oldAcStatus == 0xFF && _acRunStatus != 0xFF);

            // 如果有任一设备进入故障状态，启动定时器
            if ((dcBecameFault || acBecameFault) && (_acreadFaultCanId != 0 || _dcreadFaultCanId != 0))
            {
                // 立即发送一次读取故障指令
                SendReadFaultCommand(null);

                // 启动定时器，每隔2秒发送一次读取故障指令
                _readFaultTimer.Change(2000, 2000);
                LogService.Log("检测到故障状态，启动故障读取定时器");
            }
            // 如果所有设备都恢复正常，停止定时器
            else if ((dcRecovered && _acRunStatus != 0xFF) || (acRecovered && _dcRunStatus != 0xFF))
            {
                _readFaultTimer.Change(Timeout.Infinite, Timeout.Infinite);
                LogService.Log("设备恢复正常，停止故障读取定时器");

                // 清除故障显示
                lock (_faultQueueLock)
                {
                    _activeFaults.Clear();
                    _faultDisplayQueue.Clear();
                }

                // 恢复正常状态显示
                //UpdateConnectionStatusUI("已连接", Color.White);
                _isFaultDisplayActive = false;
            }
        }

        /// <summary>
        /// 检查关键信号是否超出阈值
        /// </summary>
        private void CheckCriticalSignals(string signalName, double value)
        {
            try
            {
                // 检查电压信号
                if (signalName.Contains(VOLTAGE_SIGNAL) && _protectionParameters != null)
                {
                    if (double.TryParse(_protectionParameters.OverVoltage, out double overVoltage) &&
                        value > overVoltage)
                    {
                        SendStopCommandIfNeeded();
                        LogService.Log($"电压异常: {value} > {overVoltage} (过压保护值)");
                        //XtraMessageBox.Show($"电压异常: {value} > {overVoltage} (过压保护值)");
                    }
                    else if (double.TryParse(_protectionParameters.UnderVoltage, out double underVoltage) &&
                             value < underVoltage)
                    {
                        SendStopCommandIfNeeded();
                        LogService.Log($"电压异常: {value} < {underVoltage} (欠压保护值)");
                        //XtraMessageBox.Show($"电压异常: {value} < {underVoltage} (欠压保护值)");
                    }
                }

                // 检查电流信号
                if (signalName.Contains(CURRENT_SIGNAL) && _protectionParameters != null)
                {
                    if (double.TryParse(_protectionParameters.OverCurrent, out double overCurrent) &&
                        value > overCurrent)
                    {
                        SendStopCommandIfNeeded();
                        LogService.Log($"电流异常: {value} > {overCurrent} (过流保护值)");
                        //XtraMessageBox.Show($"电流异常: {value} > {overCurrent} (过流保护值)");
                    }
                    else if (double.TryParse(_protectionParameters.UnderCurrent, out double underCurrent) &&
                             value < underCurrent)
                    {
                        SendStopCommandIfNeeded();
                        LogService.Log($"电流异常: {value} < {underCurrent} (欠流保护值)");
                        //XtraMessageBox.Show($"电流异常: {value} < {underCurrent} (欠流保护值)");
                    }
                }

                // 检查功率信号
                if (signalName.Contains(POWER_SIGNAL) && _protectionParameters != null)
                {
                    if (double.TryParse(_protectionParameters.OverPower, out double overPower) &&
                        value > overPower)
                    {
                        SendStopCommandIfNeeded();
                        LogService.Log($"功率异常: {value} > {overPower} (过功率保护值)");
                        //XtraMessageBox.Show($"功率异常: {value} > {overPower} (过功率保护值)");
                    }
                    else if (double.TryParse(_protectionParameters.UnderPower, out double underPower) &&
                             value < underPower)
                    {
                        SendStopCommandIfNeeded();
                        LogService.Log($"功率异常: {value} < {underPower} (欠功率保护值)");
                        //XtraMessageBox.Show($"功率异常: {value} < {underPower} (欠功率保护值)");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"检查保护参数时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送停机指令（如果需要）
        /// </summary>
        private void SendStopCommandIfNeeded()
        {
            if (_stopCommandSent)
            {
                _stopCommandSent = false;

                // 异步发送停机指令，避免阻塞CAN处理线程
                Task.Run(async () =>
                {
                    try
                    {
                        bool success = await _startupManager.StopDeviceAsync();
                        if (success)
                        {
                            // 在停止设备时重置时间
                            _startTime = DateTime.MinValue;
                            _stepStartTime = DateTime.MinValue;
                            LogService.Log("停机指令发送成功");
                        }
                        else
                        {
                            LogService.Log("停机指令发送失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"发送停机指令时出错: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// 检查新参数是否安全（当前信号值不会超出新阈值）
        /// </summary>
        private async Task<bool> CheckParametersSafe(ConfigurationData newParams)
        {
            try
            {
                // 等待获取最新的信号值
                await Task.Delay(100); // 给一点时间确保信号值已更新

                // 检查电压信号
                var voltageSignals = _signalValues.Where(kv => kv.Key.Contains(VOLTAGE_SIGNAL)).ToList();
                foreach (var signal in voltageSignals)
                {
                    double value = signal.Value;
                    if (double.TryParse(newParams.OverVoltage, out double overVoltage) &&
                        value > overVoltage)
                    {
                        LogService.Log($"参数检查失败: 当前电压{value} > 新过压保护值{overVoltage}");
                        return false;
                    }
                    if (double.TryParse(newParams.UnderVoltage, out double underVoltage) &&
                        value < underVoltage)
                    {
                        LogService.Log($"参数检查失败: 当前电压{value} < 新欠压保护值{underVoltage}");
                        return false;
                    }
                }

                // 检查电流信号
                var currentSignals = _signalValues.Where(kv => kv.Key.Contains(CURRENT_SIGNAL)).ToList();
                foreach (var signal in currentSignals)
                {
                    double value = signal.Value;
                    if (double.TryParse(newParams.OverCurrent, out double overCurrent) &&
                        value > overCurrent)
                    {
                        LogService.Log($"参数检查失败: 当前电流{value} > 新过流保护值{overCurrent}");
                        return false;
                    }
                    if (double.TryParse(newParams.UnderCurrent, out double underCurrent) &&
                        value < underCurrent)
                    {
                        LogService.Log($"参数检查失败: 当前电流{value} < 新欠流保护值{underCurrent}");
                        return false;
                    }
                }

                // 检查功率信号
                var powerSignals = _signalValues.Where(kv => kv.Key.Contains(POWER_SIGNAL)).ToList();
                foreach (var signal in powerSignals)
                {
                    double value = signal.Value;
                    if (double.TryParse(newParams.OverPower, out double overPower) &&
                        value > overPower)
                    {
                        LogService.Log($"参数检查失败: 当前功率{value} > 新过功率保护值{overPower}");
                        return false;
                    }
                    if (double.TryParse(newParams.UnderPower, out double underPower) &&
                        value < underPower)
                    {
                        LogService.Log($"参数检查失败: 当前功率{value} < 新欠功率保护值{underPower}");
                        return false;
                    }
                }

                return true; // 所有检查通过
            }
            catch (Exception ex)
            {
                LogService.Log($"参数安全检查错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 启动前检查关键信号是否超出阈值
        /// </summary>
        private async Task<bool> CheckCriticalSignalsBeforeStart()
        {
            try
            {
                // 等待获取最新的信号值
                await Task.Delay(100); // 给一点时间确保信号值已更新

                // 检查电压信号
                var voltageSignals = _signalValues.Where(kv => kv.Key.Contains(VOLTAGE_SIGNAL)).ToList();

                foreach (var signal in voltageSignals)
                {
                    double value = signal.Value;
                    if (_protectionParameters != null)
                    {
                        if (double.TryParse(_protectionParameters.OverVoltage, out double overVoltage) &&
                            value > overVoltage)
                        {
                            LogService.Log($"启动前电压异常: {value} > {overVoltage} (过压保护值)");
                            return false;
                        }
                        else if (double.TryParse(_protectionParameters.UnderVoltage, out double underVoltage) &&
                                 value < underVoltage)
                        {
                            LogService.Log($"启动前电压异常: {value} < {underVoltage} (欠压保护值)");
                            return false;
                        }
                    }
                }

                // 检查电流信号
                var currentSignals = _signalValues.Where(kv => kv.Key.Contains(CURRENT_SIGNAL)).ToList();

                foreach (var signal in currentSignals)
                {
                    double value = signal.Value;
                    if (_protectionParameters != null)
                    {
                        if (double.TryParse(_protectionParameters.OverCurrent, out double overCurrent) &&
                            value > overCurrent)
                        {
                            LogService.Log($"启动前电流异常: {value} > {overCurrent} (过流保护值)");
                            return false;
                        }
                        else if (double.TryParse(_protectionParameters.UnderCurrent, out double underCurrent) &&
                                 value < underCurrent)
                        {
                            LogService.Log($"启动前电流异常: {value} < {underCurrent} (欠流保护值)");
                            return false;
                        }
                    }
                }

                // 检查功率信号
                var powerSignals = _signalValues.Where(kv => kv.Key.Contains(POWER_SIGNAL)).ToList();
                foreach (var signal in powerSignals)
                {
                    double value = signal.Value;
                    if (_protectionParameters != null)
                    {
                        if (double.TryParse(_protectionParameters.OverPower, out double overPower) &&
                            value > overPower)
                        {
                            LogService.Log($"启动前功率异常: {value} > {overPower} (过功率保护值)");
                            return false;
                        }
                        else if (double.TryParse(_protectionParameters.UnderPower, out double underPower) &&
                                 value < underPower)
                        {
                            LogService.Log($"启动前功率异常: {value} < {underPower} (欠功率保护值)");
                            return false;
                        }
                    }
                }

                return true; // 所有关键信号正常
            }
            catch (Exception ex)
            {
                LogService.Log($"启动前检查关键信号时出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检测设备状态是否改变
        /// </summary>
        private async Task<bool> CheckDeviceStatusChange(TimeSpan timeout)
        {
            DateTime startTime = DateTime.Now;

            while (DateTime.Now - startTime < timeout)
            {
                // 检查状态是否变为 0x01 (启动过程中) 或 0x02 (运行)
                if (_dcRunStatus == 0x01 || _dcRunStatus == 0x02)
                {
                    return true; // 状态已改变
                }

                // 等待一段时间再检查
                await Task.Delay(100);
            }

            // 超时，状态未改变
            return false;
        }

        /// <summary>
        /// 检查运行模式
        /// </summary>
        private Task<bool> CheckRunMode(string workingMode)
        {
            uint mode = 0;
            switch (workingMode)
            {
                case "恒流充电":
                    mode = 0x03;
                    break;
                case "恒流放电":
                    mode = 0x23;
                    break;
                case "恒压充电":
                    mode = 0x02;
                    break;
                case "恒压放电":
                    mode = 0x22;
                    break;
                case "恒功率充电":
                    mode = 0x01;
                    break;
                case "恒功率放电":
                    mode = 0x21;
                    break;
                case "搁置":
                    mode = 0x05;
                    break;
                case "静置":
                    mode = 0x06;
                    break;
                case "停机":
                    mode = 0x00;
                    break;
            }

            if (mode == _dcRunMode)
            {
                return Task.FromResult(true);
            }
            else
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 比较参数是否有变化
        /// </summary>
        private bool CompareParameters(ConfigurationData oldParams, ConfigurationData newParams)
        {
            // 比较保护参数
            if (oldParams.OverVoltage != newParams.OverVoltage ||
                oldParams.UnderVoltage != newParams.UnderVoltage ||
                oldParams.OverCurrent != newParams.OverCurrent ||
                oldParams.UnderCurrent != newParams.UnderCurrent ||
                oldParams.OverPower != newParams.OverPower ||
                oldParams.UnderPower != newParams.UnderPower)
            {
                return true;
            }

            return false;
        }

        #endregion

        // ==================== 故障处理区域 ====================
        #region 故障处理

        /// <summary>
        /// 处理故障信号（实时）
        /// </summary>
        private void ProcessFaultSignal(string signalName, double value)
        {
            if (value != 0) // 非0值表示故障
            {
                string faultDescription = GetReuseSignalDisplayText(signalName, value);

                lock (_faultQueueLock)
                {
                    string name = "";
                    if (faultDescription == "故障")
                    {
                        name = signalName.Remove(0, 3);
                    }
                    else
                    {
                        //name = faultDescription;
                        name = signalName.Remove(0, 3);
                    }

                    if (!_activeFaults.ContainsKey(signalName))
                    {
                        _activeFaults[signalName] = name;
                        _faultDisplayQueue.Enqueue(name);
                        LogService.Log($"检测到新故障: {signalName} → {name}");
                    }
                    else if (_activeFaults[signalName] != name)
                    {
                        _activeFaults[signalName] = name;
                        LogService.Log($"故障更新: {signalName} → {name}");
                    }
                }
            }
            else // 值为0表示故障清除
            {
                lock (_faultQueueLock)
                {
                    if (_activeFaults.Remove(signalName))
                    {
                        RemoveFaultFromDisplayQueue(signalName);
                        LogService.Log($"故障清除: {signalName}");
                    }
                }
            }
        }

        /// <summary>
        /// 从显示队列中移除特定故障
        /// </summary>
        private void RemoveFaultFromDisplayQueue(string signalName)
        {
            lock (_faultQueueLock)
            {
                if (!_activeFaults.TryGetValue(signalName, out var faultDescription))
                    return;

                // 创建临时队列，只保留其他故障
                var newQueue = new Queue<string>();
                while (_faultDisplayQueue.Count > 0)
                {
                    var fault = _faultDisplayQueue.Dequeue();
                    if (fault != faultDescription)
                    {
                        newQueue.Enqueue(fault);
                    }
                }

                // 将临时队列复制回原始队列
                while (newQueue.Count > 0)
                {
                    _faultDisplayQueue.Enqueue(newQueue.Dequeue());
                }

                LogService.Log($"从显示队列中移除故障: {signalName}");
            }
        }

        /// <summary>
        /// 重建显示队列（确保只包含当前活跃故障）
        /// </summary>
        private void RebuildFaultDisplayQueue()
        {
            lock (_faultQueueLock)
            {
                // 清除现有队列
                _faultDisplayQueue.Clear();

                // 重新添加所有活跃故障
                foreach (var fault in _activeFaults.Values)
                {
                    _faultDisplayQueue.Enqueue(fault);
                }

                LogService.Log($"重建显示队列，当前活跃故障数: {_activeFaults.Count}");
            }
        }

        /// <summary>
        /// 显示下一个故障
        /// </summary>
        private void DisplayNextFault(object state)
        {
            // 添加销毁状态检查
            if (_disposed) return;

            // 检查是否处于故障模式
            bool isFaultMode = (_acRunStatus == 0xFF) || (_dcRunStatus == 0xFF);
            if (!isFaultMode) return;

            try
            {
                string nextFault = null;

                lock (_faultQueueLock)
                {
                    // 如果没有活跃故障，清空队列并重置状态
                    if (_activeFaults.Count == 0)
                    {
                        if (_faultDisplayQueue.Count > 0)
                        {
                            _faultDisplayQueue.Clear();
                        }

                        if (_isFaultDisplayActive)
                        {
                            _isFaultDisplayActive = false;
                            //UpdateConnectionStatusUI("已连接", Color.White);
                        }
                        return;
                    }

                    // 如果队列为空但仍有活跃故障，重建队列
                    if (_faultDisplayQueue.Count == 0)
                    {
                        RebuildFaultDisplayQueue();
                    }

                    // 确保队列中有数据
                    if (_faultDisplayQueue.Count > 0)
                    {
                        nextFault = _faultDisplayQueue.Dequeue();

                        // 检查故障是否仍然活跃
                        if (_activeFaults.ContainsValue(nextFault))
                        {
                            // 放回队列尾部实现循环
                            _faultDisplayQueue.Enqueue(nextFault);
                        }
                        else
                        {
                            // 如果故障已清除，跳过本次显示
                            nextFault = null;
                            LogService.Log($"跳过已清除的故障: {nextFault}");
                        }
                    }
                }

                // 更新UI显示
                if (!string.IsNullOrEmpty(nextFault))
                {
                    _isFaultDisplayActive = true;
                    this.BeginInvoke((Action)(() =>
                    {
                        UpdateConnectionStatusUI(nextFault, Color.Red);
                    }));
                }
                else if (_activeFaults.Count > 0)
                {
                    // 如果没有显示故障但仍有活跃故障，立即尝试再次显示
                    DisplayNextFault(null);
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"故障显示错误: {ex.Message}");
            }
        }

        #endregion

        // ==================== 用户界面更新区域 ====================
        #region 用户界面更新

        /// <summary>
        /// 从缓存更新UI
        /// </summary>
        private void UpdateUIFromCache()
        {
            //if (!_uiMode) return; // 无UI模式不更新界面

            if (_disposed || this.IsDisposed || !this.IsHandleCreated) return;

            // 切换到UI线程
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateUIFromCache));
                return;
            }

            try
            {
                // 创建信号值的快照
                var snapshot = _signalValues.ToArray();

                foreach (var kv in snapshot)
                {
                    string signalName = kv.Key;
                    double value = kv.Value;

                    // 获取显示文本
                    string displayValue = GetReuseSignalDisplayText(signalName, value);

                    // 更新表格
                    var dataItem = signalData.FirstOrDefault(s => s.SystemName == signalName);
                    if (dataItem != null)
                    {
                        dataItem.Value = displayValue;
                    }

                    // 检查设备状态
                    //currentStatus = _startupManager.CheckDeviceStatus(_acRunStatus, _dcRunStatus);

                    // 更新状态标签
                    UpdateStatusLabels(signalName, displayValue);
                    UpdateStatusDisplay(_dcRunStatus);
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"UI更新错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新状态显示
        /// </summary>
        private void UpdateStatusDisplay(uint status)
        {
            if (_dcRunStatus == 0xFF || _acRunStatus == 0xFF || !_isConnected)
            {
                return;
            }

            switch (status)
            {
                case 0x00: // 待机
                    UpdateConnectionStatusUI("待机", Color.Yellow);
                    break;
                case 0x01: // 启动过程中
                    UpdateConnectionStatusUI("启动过程中", Color.YellowGreen);
                    break;
                case 0x02: // 运行
                    UpdateConnectionStatusUI("运行", Color.Green);
                    break;
                case 0x03: // 停机过程中
                    UpdateConnectionStatusUI("停机过程中", Color.YellowGreen);
                    break;
                //case 0xFF:
                //    // 查询故障显示方法
                //    DisplayNextFault(null);
                //    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// 更新连接状态UI
        /// </summary>
        private void UpdateConnectionStatusUI(string text, Color color)
        {
            // 添加销毁状态检查
            if (_disposed || this.IsDisposed || !this.IsHandleCreated) return;

            this.BeginInvoke((Action)(() =>
            {
                // 再次检查，因为可能在调用过程中被销毁
                if (_disposed || this.IsDisposed || !this.IsHandleCreated) return;

                panelControl.Appearance.BackColor = color;
                panelControl.BorderStyle = BorderStyles.NoBorder;
                lblConnectionStatus.Text = text;
            }));
        }

        /// <summary>
        /// 更新时间显示
        /// </summary>
        private void UpdateTimeDisplay()
        {
            if (_disposed || this.IsDisposed || !this.IsHandleCreated) return;

            try
            {
                // 切换到UI线程更新
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(UpdateTimeDisplay));
                    return;
                }

                // 更新累计运行时间
                if (_startTime != DateTime.MinValue)
                {
                    TimeSpan totalTime = DateTime.Now - _startTime;
                    _totalTimeData.Value = $"{totalTime.Hours:00}:{totalTime.Minutes:00}:{totalTime.Seconds:00}";
                }

                // 更新工步运行时间
                if (_stepStartTime != DateTime.MinValue)
                {
                    TimeSpan stepTime = DateTime.Now - _stepStartTime;
                    _stepTimeData.Value = $"{stepTime.Hours:00}:{stepTime.Minutes:00}:{stepTime.Seconds:00}";
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"更新时间显示错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新状态标签
        /// </summary>
        private void UpdateStatusLabels(string signalName, string displayValue)
        {
            switch (signalName)
            {
                case "AC运行状态":
                    lblACStatus.Text = $"AC运行状态: {displayValue}";
                    break;
                case "AC运行模式":
                    lblACMode.Text = $"AC运行模式: {displayValue}";
                    break;
                case "DC运行状态":
                    lblDCStatus.Text = $"DC运行状态: {displayValue}";
                    break;
                case "DC运行模式":
                    lblDCMode.Text = $"DC运行模式: {displayValue}";
                    break;
            }
        }

        /// <summary>
        /// 动态调整标签字体大小以适应可用空间
        /// </summary>
        private void AdjustFontSizeToFitLabel()
        {
            // 确保标签已初始化且有文本内容
            if (lblConnectionStatus == null ||
                string.IsNullOrEmpty(lblConnectionStatus.Text) ||
                lblConnectionStatus.Width <= 0)
            {
                return;
            }

            try
            {
                using (Graphics g = lblConnectionStatus.CreateGraphics())
                {
                    SizeF textSize;
                    float maxFontSize = 72f;
                    float minFontSize = 8f;
                    float currentFontSize = maxFontSize;
                    Font testFont;

                    // 使用二分查找确定最佳字体大小
                    while (maxFontSize - minFontSize > 0.1f)
                    {
                        currentFontSize = (maxFontSize + minFontSize) / 2;
                        testFont = new Font(lblConnectionStatus.Font.FontFamily,
                                           currentFontSize,
                                           lblConnectionStatus.Font.Style);

                        // 测量文本所需空间
                        textSize = g.MeasureString(lblConnectionStatus.Text, testFont);

                        // 检查文本是否适合标签宽度（保留10%边距）
                        if (textSize.Width < lblConnectionStatus.Width * 0.9f)
                        {
                            minFontSize = currentFontSize;
                        }
                        else
                        {
                            maxFontSize = currentFontSize;
                        }

                        testFont.Dispose();
                    }

                    // 应用新字体大小
                    lblConnectionStatus.Font = new Font(
                        lblConnectionStatus.Font.FontFamily,
                        minFontSize,
                        lblConnectionStatus.Font.Style
                    );
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"字体调整错误: {ex.Message}");
            }
        }

        #endregion

        // ==================== 设备控制区域 ====================
        #region 设备控制

        /// <summary>
        /// 异步启动设备
        /// </summary>
        private async void PoweronAsync(object? sender, EventArgs e)
        {
            try
            {
                // 检查设备状态,待机、停机过程情况下才能启动
                if (_dcRunStatus == 0x00 || _dcRunStatus == 0x03)
                {
                    // 显示启动配置对话框
                    using (var configForm = new StartConfiguration(_title, "启动配置",true))
                    {
                        if (configForm.ShowDialog() == DialogResult.OK)
                        {
                            // 获取用户设置的配置数据
                            _protectionParameters = configForm.Configuration;

                            // ============ 新增：启动前检查关键信号 ============
                            if (!await CheckParametersSafe(_protectionParameters))
                            {
                                LogService.Log("启动前检查失败：关键信号超出阈值");
                                XtraMessageBox.Show("启动前检查失败：关键信号超出阈值，请检查设备状态");
                                return;
                            }
                            // ============ 新增结束 ============

                            // 开始启动
                            bool run = await _startupManager.StartDeviceAsync(_protectionParameters, true);

                            if (run)
                            {
                                _stopCommandSent = true;

                                // 检测设备状态变化
                                bool statusChanged = await CheckDeviceStatusChange(TimeSpan.FromSeconds(3));

                                // 检测设备运行工步码是否一致
                                bool runmode = await CheckRunMode(configForm.Configuration.WorkingMode);

                                if ((!statusChanged) || (!runmode))
                                {
                                    // 发送停机指令
                                    bool stopSuccess = await _startupManager.StopDeviceAsync();
                                    if (!stopSuccess)
                                    {
                                        XtraMessageBox.Show("停机指令发送失败");
                                        return;
                                    }

                                    // 状态没有变化，启动失败
                                    LogService.Log($"设备启动失败，运行状态{statusChanged} - 运行模式{runmode}");
                                    XtraMessageBox.Show($"设备启动失败，运行状态{statusChanged} - 运行模式{runmode}");
                                    return;
                                }
                                else
                                {
                                    // 状态已改变，启动成功
                                    _totalTimeData.Value = "00:00:00";
                                    _startTime = DateTime.Now;
                                    
                                    LogService.Log("设备启动成功");
                                }

                                // 异步等待设备进入运行状态
                                await WaitForRunStatus(0x02, TimeSpan.FromSeconds(30));

                                // 设置工步开始时间
                                _stepTimeData.Value = "00:00:00";
                                _stepStartTime = DateTime.Now;
                            }
                            else
                            {
                                LogService.Log("设备启动失败!");
                                XtraMessageBox.Show("设备启动失败!");
                            }
                        }
                    }
                }
                else
                {
                    LogService.Log("设备状态异常，禁止启动!");
                    XtraMessageBox.Show("设备状态异常，禁止启动");
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"设备启动失败:{ex.Message}");
                XtraMessageBox.Show($"设备启动失败:{ex.Message}");
            }
        }

        /// <summary>
        /// 异步等待设备达到指定状态
        /// </summary>
        /// <param name="targetStatus">目标状态</param>
        /// <param name="timeout">超时时间</param>
        /// <returns>是否成功达到目标状态</returns>
        private async Task<bool> WaitForRunStatus(uint targetStatus, TimeSpan timeout)
        {
            DateTime startTime = DateTime.Now;

            while (DateTime.Now - startTime < timeout)
            {
                if (_dcRunStatus == targetStatus)
                    return true;

                // 如果设备进入故障状态，立即返回
                if (_dcRunStatus == 0xFF)
                    return false;

                // 等待一段时间再检查
                await Task.Delay(100);
            }

            return false; // 超时
        }

        /// <summary>
        /// 参数设置
        /// </summary>
        private void ParameterSet(object? sender, EventArgs e)
        {
            try
            {
                // 检查设备状态,运行情况下才能设置参数
                if (_dcRunStatus != 0x02)
                {
                    XtraMessageBox.Show("设备状态异常，禁止设置参数");
                    return;
                }

                // 检查是否已有窗体实例存在
                if (_paramForm != null && !_paramForm.IsDisposed)
                {
                    _paramForm.Activate(); // 激活已有窗体
                    return;
                }

                // 保存当前保护参数以便比较
                ConfigurationData currentParams = _protectionParameters;

                _paramForm = new StartConfiguration(_title, "参数配置",true);

                // 设置窗体位置居中
                CenterFormToParent(_paramForm);

                // 订阅窗体关闭事件以便清理引用
                _paramForm.FormClosed += (s, args) =>
                {
                    _paramForm = null;
                };

                _paramForm.Applied += async (s, args) =>
                {
                    // 获取用户设置的新参数
                    ConfigurationData newParams = _paramForm.Configuration;

                    // ============ 新增：检查新参数是否会导致当前信号值超出阈值 ============
                    if (!await CheckParametersSafe(newParams))
                    {
                        XtraMessageBox.Show("新参数设置会导致当前信号值超出保护阈值，请调整参数或设备状态");
                        return;
                    }
                    // ============ 新增结束 ============

                    // 比较参数是否有变化
                    bool hasChanges = CompareParameters(currentParams, newParams);

                    // 发送新的参数
                    bool success = await _startupManager.StartDeviceAsync(newParams, hasChanges);

                    if (success)
                    {
                        _stepStartTime = DateTime.MinValue;
                        _stepTimeData.Value = "00:00:00";
                        _stepStartTime = DateTime.Now;

                        await Task.Delay(100);

                        // 检测设备运行工步码是否一致
                        bool runmode = await CheckRunMode(newParams.WorkingMode);

                        if (runmode)
                        {
                            // 更新当前保护参数
                            _protectionParameters = newParams;
                            LogService.Log("参数设置成功");
                            XtraMessageBox.Show("参数设置成功");
                        }
                        else
                        {
                            LogService.Log("参数发送失败");
                            XtraMessageBox.Show("参数发送失败");
                        }
                    }
                    else
                    {
                        LogService.Log("参数发送失败");
                        XtraMessageBox.Show("参数发送失败");
                    }
                };

                _paramForm.Show(); // 非模态显示
            }
            catch (Exception ex)
            {
                LogService.Log($"设置参数失败: {ex.Message}");
                XtraMessageBox.Show($"设置参数失败: {ex.Message}");
            }
        }

        // 辅助方法：居中窗体
        private void CenterFormToParent(XtraForm formToCenter)
        {
            var ownerForm = this.FindForm();
            if (ownerForm != null && ownerForm.Visible)
            {
                formToCenter.StartPosition = FormStartPosition.Manual;
                formToCenter.Location = new Point(
                    ownerForm.Location.X + (ownerForm.Width - formToCenter.Width) / 2,
                    ownerForm.Location.Y + (ownerForm.Height - formToCenter.Height) / 2
                );
            }
            else
            {
                formToCenter.StartPosition = FormStartPosition.CenterScreen;
            }
        }

        /// <summary>
        /// 停止设备
        /// </summary>
        private async void ShutDown(object? sender, EventArgs e)
        {
            try
            {
                // 检查设备状态,运行、和启动中情况下才能停机
                if (_dcRunStatus == 0x02 || _dcRunStatus == 0x01)
                {
                    // 确认对话框
                    if (XtraMessageBox.Show("确定要停止测试吗？", "确认停机",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    {
                        return;
                    }

                    // 发送停机指令
                    bool success = await _startupManager.StopDeviceAsync();

                    if (success)
                    {
                        // 在停止设备时重置时间
                        _startTime = DateTime.MinValue;
                        _stepStartTime = DateTime.MinValue;

                        await Task.Delay(100);

                        // 检测设备运行工步码是否一致
                        bool runmode = await CheckRunMode("停机");

                        if (runmode)
                        {
                            LogService.Log("设备停机成功");
                        }
                        else
                        {
                            LogService.Log("设备停止失败，请检查设备状态");
                            XtraMessageBox.Show("设备停止失败，请检查设备状态");
                        }
                    }
                    else
                    {
                        LogService.Log("设备停止失败，请检查设备状态");
                        XtraMessageBox.Show("设备停止失败，请检查设备状态");
                    }
                }
                else
                {
                    LogService.Log("设备状态异常，禁止停机");
                    XtraMessageBox.Show("设备状态异常，禁止停机");
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"停机操作失败: {ex.Message}");
                XtraMessageBox.Show($"停机操作失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 清除故障
        /// </summary>
        private void Clearfault(object? sender, EventArgs e)
        {
            if (_acfaultCanId != 0)
            {
                byte[] data = new byte[8];
                data[0] = 0x01;

                CANManager.Instance.SendCommand(
                    _equipment.DeviceIndex,
                    _equipment.CanIndex,
                    _acfaultCanId,
                    data
                );
            }

            if (_dcfaultCanId != 0)
            {
                byte[] data = new byte[8];
                data[0] = 0x01;

                CANManager.Instance.SendCommand(
                    _equipment.DeviceIndex,
                    _equipment.CanIndex,
                    _dcfaultCanId,
                    data
                );
            }

            XtraMessageBox.Show("清除故障成功");
            LogService.Log("清除故障成功");
        }

        /// <summary>
        /// 电压低档切换
        /// </summary>
        private void Wvoltage(object? sender, EventArgs e)
        {
            if (_voltageCanId != 0)
            {
                byte[] data = new byte[8];
                data[0] = 0x01;
                CANManager.Instance.SendCommand(
                _equipment.DeviceIndex,
                _equipment.CanIndex,
                _voltageCanId,
                data
                );
            }
            XtraMessageBox.Show("电压低档位切换成功");
        }

        /// <summary>
        /// 电压高档切换
        /// </summary>
        private void Gavoltage(object? sender, EventArgs e)
        {
            if (_voltageCanId != 0)
            {
                byte[] data = new byte[8];
                data[0] = 0x02;
                CANManager.Instance.SendCommand(
                _equipment.DeviceIndex,
                _equipment.CanIndex,
                _voltageCanId,
                data
                );
            }
            XtraMessageBox.Show("电压高档位切换成功");
        }

        #endregion

        // ==================== UI组件创建区域 ====================
        #region UI组件创建

        /// <summary>
        /// 添加AC/DC状态行
        /// </summary>
        private void AddACDCStatusRows(LayoutControl layoutControl)
        {
            // 创建垂直排列的容器
            var container = new LayoutControlGroup
            {
                DefaultLayoutType = LayoutType.Vertical,
                GroupStyle = GroupStyle.Light,
                TextVisible = false,
                Padding = new DevExpress.XtraLayout.Utils.Padding(-3),
            };
            layoutControl.AddItem(container);

            // 添加AC状态行
            container.AddItem(CreateStatusRow(out lblACStatus, out lblACMode));
            // 添加DC状态行
            container.AddItem(CreateStatusRow(out lblDCStatus, out lblDCMode));
        }

        /// <summary>
        /// 创建状态行
        /// </summary>
        private LayoutControlItem CreateStatusRow(out LabelControl lblStatus, out LabelControl lblMode)
        {
            // 状态标签
            lblStatus = new LabelControl
            {
                AutoSizeMode = LabelAutoSizeMode.None,
                Size = new Size(150, 30)
            };

            // 模式标签
            lblMode = new LabelControl
            {
                AutoSizeMode = LabelAutoSizeMode.None,
                Size = new Size(180, 30)
            };

            // 使用流式布局实现水平排列
            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new System.Windows.Forms.Padding(20, 0, 0, 0),
                Controls = { lblStatus, lblMode }
            };

            return new LayoutControlItem
            {
                Control = panel,
                TextVisible = false,
                SizeConstraintsType = SizeConstraintsType.Custom,
                MinSize = new Size(0, 35),
                MaxSize = new Size(0, 35)
            };
        }

        /// <summary>
        /// 添加参数表格
        /// </summary>
        private void AddParameters(LayoutControl layoutControl)
        {
            gridControl = new GridControl();
            gridview = new GridView();
            gridControl.MainView = gridview;
            gridview.OptionsView.ShowGroupPanel = false;

            var gridcolumns = new[]
            {
                new GridColumn { FieldName = "SystemName", Caption = "信号名称", Width = 150, Visible = true, OptionsColumn = {AllowEdit = false}},
                new GridColumn { FieldName = "Value", Caption = "值",Width = 100, Visible = true,OptionsColumn = {AllowEdit = false}},
                new GridColumn { FieldName = "Unit", Caption = "单位", Visible = true,OptionsColumn = {AllowEdit = false}},
            };
            gridview.Columns.AddRange(gridcolumns);

            layoutControl.AddItem("参数列表", gridControl);
        }

        /// <summary>
        /// 添加异常警报
        /// </summary>
        private void AddExceptionAlert(LayoutControl layoutControl)
        {
            panelControl = new PanelControl();
            panelControl.Appearance.TextOptions.HAlignment = HorzAlignment.Center;
            panelControl.Appearance.TextOptions.VAlignment = VertAlignment.Center;

            LayoutControlItem item = new LayoutControlItem
            {
                Control = panelControl,
                TextVisible = false,
                SizeConstraintsType = SizeConstraintsType.Custom,
                MinSize = new Size(0, 100),
                MaxSize = new Size(0, 100)
            };
            layoutControl.AddItem(item);

            // 创建状态标签
            lblConnectionStatus = new LabelControl
            {
                Dock = DockStyle.Fill,
                AutoSizeMode = LabelAutoSizeMode.None,
                Appearance =
                {
                    TextOptions =
                    {
                        HAlignment = HorzAlignment.Center,
                        VAlignment = VertAlignment.Center,
                        WordWrap = WordWrap.NoWrap
                    }
                }
            };

            // 设置初始字体
            lblConnectionStatus.Font = new Font("Tahoma", 12, FontStyle.Bold);
            lblConnectionStatus.Appearance.Font = new Font("Tahoma", 24F, FontStyle.Bold);

            // 绑定事件
            lblConnectionStatus.TextChanged += (s, e) => AdjustFontSizeToFitLabel();
            lblConnectionStatus.SizeChanged += (s, e) => AdjustFontSizeToFitLabel();

            panelControl.Controls.Add(lblConnectionStatus);
        }

        /// <summary>
        /// 显示信号选择器
        /// </summary>
        private void ShowSignalSelector(object? sender, EventArgs e)
        {
            var form = new SignalSelectorForm(allSignals, signalData.ToList());
            if (form.ShowDialog() == DialogResult.OK)
            {
                signalData.Clear();
                foreach (var signal in form.SelectedSignals)
                {
                    signalData.Add(new Showdata
                    {
                        SystemName = signal.SystemName,
                        Value = "",
                        Unit = signal.Unit
                    });
                }
            }
        }

        #endregion

        // ==================== 资源清理区域 ====================
        #region 资源清理

        /// <summary>
        /// 释放资源
        /// </summary>
        public new void Dispose()
        {
            //GC.SuppressFinalize(this);
            //GC.Collect();
            //GC.WaitForPendingFinalizers();

            if (_disposed) return;
            _disposed = true;

            try
            {
                // 清理时从字典中移除
                if (StartupManagers.ContainsKey(_equipment.DeviceName))
                {
                    StartupManagers.Remove(_equipment.DeviceName);
                }

                // 关闭参数设置窗体
                if (_paramForm != null && !_paramForm.IsDisposed)
                {
                    _paramForm.Close();
                    _paramForm.Dispose();
                }

                // 停止并释放UI更新定时器
                _uiUpdateTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _uiUpdateTimer?.Dispose();

                // 释放读取故障定时器
                _readFaultTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _readFaultTimer?.Dispose();

                // 注销CAN通道
                CANManager.Instance.UnregisterChannel(
                    _equipment.DeviceIndex,
                    _equipment.CanIndex
                );

                // 注销CAN处理器
                CANManager.Instance.UnregisterDataHandler(
                    _equipment.DeviceIndex,
                    _equipment.CanIndex,
                    HandleCANFrame
                );

                // 注销连接状态事件
                CANManager.Instance.OnConnectionStatusChanged -= HandleConnectionStatusChanged;

                CANManager.Instance.Dispose();

                LogService.Log($"模块 {_title} 已注销");
            }
            catch (Exception ex)
            {
                LogService.Log($"模块注销错误: {ex.Message}");
            }
            finally
            {
                // 确保调用基类Dispose
                base.Dispose();
            }
        }

        #endregion
    }

    /// <summary>
    /// 数据显示类
    /// 实现属性变更通知接口
    /// </summary>
    public class Showdata : INotifyPropertyChanged
    {
        private string _systemName;
        private string _value;
        private string _unit;

        public string SystemName
        {
            get => _systemName;
            set
            {
                _systemName = value;
                OnpropertyChanged();
            }
        }

        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                OnpropertyChanged();
            }
        }

        public string Unit
        {
            get => _unit;
            set
            {
                _unit = value;
                OnpropertyChanged();
            }
        }

        public bool IsSelected { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnpropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
