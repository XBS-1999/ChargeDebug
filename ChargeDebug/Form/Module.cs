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
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChargeDebug.Form
{
    /*
     * 监控模块类 (集成新版ZCAN API)
     */
    public partial class Module : XtraUserControl, IDisposable
    {
        // 添加模块级发送控制
        private bool _moduleSendingEnabled = true;
        public bool ModuleSendingEnabled
        {
            get => _moduleSendingEnabled;
            set
            {
                _moduleSendingEnabled = value;
            }
        }

        // 添加初始化状态标志
        private bool _initialized = false;
        private uint _dcfaultCanId;
        private uint _acfaultCanId;
        private uint _voltageCanId;
        private uint _acRunStatus = 0; // 初始化为正常状态
        private uint _dcRunStatus = 0; // 初始化为正常状态
        private uint _acreadFaultCanId; // 读取AC故障指令的CAN ID
        private uint _dcreadFaultCanId; // 读取DC故障指令的CAN ID
        private System.Threading.Timer _readFaultTimer; // 读取故障定时器
        private bool _isFaultMode; // 是否处于故障模式

        // 添加销毁状态标志
        private volatile bool _disposed = false;

        // 在Module类中添加字段
        private readonly Dictionary<string, SignalInfo> _faultSignalDefinitions = new();
        private readonly Dictionary<string, string> _activeFaults = new(); // <信号名, 故障描述>

        // 信号定义字典 <信号名称, 信号定义>
        private Dictionary<string, SignalInfo> signalDefinitions = new Dictionary<string, SignalInfo>();
        // 添加复用信号映射字典
        private readonly Dictionary<string, Dictionary<string, string>> _reuseMappings = new();

        // +++ 新增故障显示相关字段 +++
        private readonly Queue<string> _faultDisplayQueue = new Queue<string>(); // 故障描述队列
        //private System.Threading.Timer _faultDisplayTimer; // 故障显示定时器
        private readonly object _faultQueueLock = new object(); // 队列访问锁
        //private const int FaultDisplayInterval = 1000; // 故障显示间隔(毫秒)
        private bool _isFaultDisplayActive; // 当前是否有故障显示

        // CAN ID对应的信号列表 <CAN ID, 信号列表>
        private Dictionary<uint, List<SignalInfo>> canIdSignals = new Dictionary<uint, List<SignalInfo>>();

        private GridControl gridControl;
        private GridView gridview;
        private PanelControl panelControl;
        // 新增字段存储状态标签
        private LabelControl lblConnectionStatus;

        // 新增DC状态标签字段
        private LabelControl lblACStatus, lblACMode;
        private LabelControl lblDCStatus, lblDCMode;
        // 添加标题字段
        private string _title;
        private static readonly object _canInitLock = new object();

        private BindingList<Showdata> signalData = new BindingList<Showdata>();

        private ContextMenuStrip contextMenu;
        private List<Showdata> allSignals = new List<Showdata>();

        //储存设备信息
        private EquipmentModel _equipment;
        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                UpdateConnectionStatusUI("已连接",Color.White);
            }
        }

        private void UpdateConnectionStatusUI(string text, Color color)
        {
            // 添加销毁状态检查
            if (_disposed || this.IsDisposed || !this.IsHandleCreated) return;

            this.BeginInvoke((Action)(() =>
            {
                // 再次检查，因为可能在调用过程中被销毁
                if (_disposed || this.IsDisposed || !this.IsHandleCreated) return;

                if (_isConnected)
                {
                    panelControl.Appearance.BackColor = color;
                    //panelControl.Appearance.Options.UseBackColor = true;
                    panelControl.BorderStyle = BorderStyles.NoBorder;
                    lblConnectionStatus.Text = text;
                }
                else
                {
                    panelControl.Appearance.BackColor = Color.Red;
                    //panelControl.Appearance.Options.UseBackColor = true;
                    panelControl.BorderStyle = BorderStyles.NoBorder;
                    lblConnectionStatus.Text = "已断开";

                    // 添加重连指示器
                    lblConnectionStatus.Text += " (重连中...)";
                }
            }));
        }

        public Module(string title, EquipmentModel equipment, List<SignalInfo> signals)
        {
            _equipment = equipment;
            _title = title; // 保存标题
            InitializeComponent();
            InitializeUI();
            ProcessSignals(signals);       // 处理信号定义
            // 创建发送定时器（1秒间隔）
            //_sendTimer = new System.Threading.Timer(SendPeriodicMessage, null, 1000, 1000);

            // 初始化故障显示定时器 (1秒间隔)
            //_faultDisplayTimer = new System.Threading.Timer(DisplayNextFault, null, Timeout.Infinite, Timeout.Infinite);

            // 初始化读取故障定时器（初始不启动）
            _readFaultTimer = new System.Threading.Timer(SendReadFaultCommand, null, Timeout.Infinite, Timeout.Infinite);
            
            this.Load += Module_Load;
            // 移除 Load 事件中的异步初始化
            // 改为在首次显示时初始化
            //this.VisibleChanged += OnVisibleChanged;
        }

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

        // Module.cs
        public new void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                // 停止并释放定时器
                //_sendTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                //_sendTimer?.Dispose();

                // 释放故障显示定时器
                //_faultDisplayTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                //_faultDisplayTimer?.Dispose();

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

        private void HandleConnectionStatusChanged(string key, bool status)
        {
            if (key == CANManager.GetChannelKey(_equipment.DeviceIndex, _equipment.CanIndex))
            {
                this.BeginInvoke((Action)(() => IsConnected = status));
            }
        }

        /*
         * 处理信号定义
         */
        private void ProcessSignals(List<SignalInfo> signals)
        {
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

                    if(signal.SystemName.Contains("故障-"))
                    {
                        _faultSignalDefinitions[signal.SystemName] = signal;
                        
                        // 记录故障信号信息
                        LogService.Log($"发现故障信号: {signal.SystemName} (CAN ID: 0x{canId:X})");
                    }
                    else
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
                                //_sendCanId = uint.Parse(signal.CANID.Replace("0x", ""),
                                    //System.Globalization.NumberStyles.HexNumber);
                                break;
                            case "清除故障DC":
                                _dcfaultCanId = uint.Parse(signal.CANID.Replace("0x", ""),
                                    System.Globalization.NumberStyles.HexNumber);
                                break;
                            case "清除故障AC":
                                _acfaultCanId = uint.Parse(signal.CANID.Replace("0x", ""),
                                    System.Globalization.NumberStyles.HexNumber);
                                break;
                            // +++ 新增读取故障信号识别 +++
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

            // +++ 新增故障信号统计 +++
            LogService.Log($"共发现 {_faultSignalDefinitions.Count} 个故障信号");

            // 加载默认全部信号到表格
            gridControl.DataSource = signalData;
        }

        /*
         * 初始化CAN通信 (使用新版API)
         */
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

                    // 3.注册连接状态变化事件
                    CANManager.Instance.OnConnectionStatusChanged += HandleConnectionStatusChanged;

                    // 4.向CAN管理器注册通道
                    CANManager.Instance.RegisterChannel(_equipment);
                    LogService.Log($"{title}注册成功:{_equipment.DeviceIP}");
                }
                catch (Exception ex)
                {
                    //logger.Info($"{title}注册失败：{ex.Message}");
                    LogService.Log($"{title}注册失败：{ex.Message}");
                    //this.BeginInvoke((Action)(() =>
                    //{
                    //    XtraMessageBox.Show($"{title}CAN盒打开失败:{ex.Message}");
                    //}));
                }
            }
        }

        /*
         * CAN数据帧处理函数 (新版API)
         */
        private void HandleCANFrame(List<CANManager.ZCAN_Receive_Data> frames)
        {
            // 检查销毁状态和句柄
            if (_disposed || this.IsDisposed || !this.IsHandleCreated)
                return;

            // 聚合信号值（信号名 → 最新值）
            var signalValues = new Dictionary<string, double>();

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

                    // 存储信号值
                    signalValues[signal.SystemName] = physicalValue;

                    // 检测运行状态信号
                    switch (signal.SystemName)
                    {
                        case "AC运行状态":
                            _acRunStatus = Convert.ToUInt32(physicalValue);
                            break;
                        case "DC运行状态":
                            _dcRunStatus = Convert.ToUInt32(physicalValue);
                            break;
                    }
                }
            }

            // 检测是否进入/退出故障模式
            bool isFaultMode = (_acRunStatus == 0xFF) || (_dcRunStatus == 0xFF);

            // 状态变化处理
            if ((isFaultMode) || (_isFaultMode))
            {
                _isFaultMode = isFaultMode;
                if(_isFaultMode)
                {
                    // 进入故障模式：启动读取故障定时器
                    _readFaultTimer.Change(0, 1000); // 立即开始，每秒发送一次
                    //LogService.Log("进入故障模式，启动故障读取定时器");
                    // 只在故障模式下处理故障信号
                    foreach (var kv in signalValues)
                    {
                        string signalName = kv.Key;
                        double value = kv.Value;

                        // 只处理故障信号
                        if (!_faultSignalDefinitions.ContainsKey(signalName))
                            continue;

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
                                    name = faultDescription;
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
                }
                else
                {
                    // 退出故障模式：停止定时器
                    _readFaultTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    LogService.Log("退出故障模式，停止故障读取定时器");

                    lock (_faultQueueLock)
                    {
                        if (_activeFaults.Count > 0)
                        {
                            _activeFaults.Clear();
                            _faultDisplayQueue.Clear();
                            _isFaultDisplayActive = false;
                            this.BeginInvoke((Action)(() =>
                                UpdateConnectionStatusUI("已连接", Color.White)));
                        }
                    }
                }
            }

            // 6. 更新UI（确保在UI线程执行）
            // 使用更安全的 Invoke 方式
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() =>
                {
                    if (_disposed || this.IsDisposed || !this.IsHandleCreated) return;
                    UpdateUI(signalValues);
                }));
            }
            else
            {
                UpdateUI(signalValues);
            }
        }

        private void UpdateUI(Dictionary<string, double> signalValues)
        {
            if (_disposed || this.IsDisposed || !this.IsHandleCreated) return;

            // 更新信号值显示
            foreach (var kv in signalValues)
            {
                string signalName = kv.Key;
                double value = kv.Value;

                // 获取显示文本（复用信号映射或普通格式）
                string displayValue = GetReuseSignalDisplayText(signalName, value);

                // 更新表格中的数据项
                var dataItem = signalData.FirstOrDefault(s => s.SystemName == signalName);
                if (dataItem != null)
                {
                    dataItem.Value = displayValue;
                }

                // 更新状态标签（运行状态/模式）
                UpdateStatusLabels(signalName, displayValue);
            }
        }

        // 新增方法：发送读取故障指令
        private void SendReadFaultCommand(object state)
        {
            // 检查模块发送状态
            if (!_moduleSendingEnabled)
            {
                //XtraMessageBox.Show("当前发送被禁用，无法清除故障");
                return;
            }

            if ((_acRunStatus == 0xFF) && (_acreadFaultCanId != 0))
            {
                // 构造读取故障指令（根据协议可能需要特定数据）
                byte[] data = new byte[8];
                // 示例：第一个字节为0x01表示读取故障
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
                // 构造读取故障指令（根据协议可能需要特定数据）
                byte[] data = new byte[8];
                // 示例：第一个字节为0x01表示读取故障
                data[0] = 0x01;
                CANManager.Instance.SendCommand(
                        _equipment.DeviceIndex,
                        _equipment.CanIndex,
                        _dcreadFaultCanId,
                        data
                    );
            }
            
            //查询故障显示方法
            DisplayNextFault(null);
        }

        // +++ 新增方法：从显示队列中移除特定故障 +++
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

        // +++ 新增方法：重建显示队列（确保只包含当前活跃故障） +++
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

        // +++ 新增故障显示方法 +++
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
                            UpdateConnectionStatusUI("已连接", Color.White);
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
        private string GetReuseSignalDisplayText(string signalName, double value)
        {
            if (_reuseMappings.TryGetValue(signalName, out var mapping))
            {
                // 将数值转换为十六进制键（如0x01）
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

        private void InitializeUI()
        {
            //设置模块容器大小
            this.ClientSize = new Size(400, 700);
            //通道容器
            GroupControl groupControl = new GroupControl();
            groupControl.Text = _title;
            groupControl.Dock = DockStyle.Fill;
            groupControl.Padding = new System.Windows.Forms.Padding(-3);
            groupControl.Margin = new System.Windows.Forms.Padding(0);

            //主布局容器
            LayoutControl layoutControl = new LayoutControl();
            layoutControl.Dock = DockStyle.Fill;
            layoutControl.Root.Padding = new DevExpress.XtraLayout.Utils.Padding(-3);
            groupControl.Controls.Add(layoutControl);

            //添加控件项
            //AddACStatusRow(layoutControl); // 新增状态行
            //AddDCStatusRow(layoutControl); // 新增状态行
            AddACDCStatusRows(layoutControl); // 新增状态行
            AddParameters(layoutControl);//参数表格
            AddExceptionAlert(layoutControl);//异常信息

            // 添加上下文菜单
            contextMenu = new ContextMenuStrip();
            var dataselection = new ToolStripMenuItem("数据选择");
            var clearfault = new ToolStripMenuItem("清除故障");
            var lowvoltage = new ToolStripMenuItem("电压低档");
            var gavoltage = new ToolStripMenuItem("电压高档");
            dataselection.Click += ShowSignalSelector;
            clearfault.Click += Clearfault;
            lowvoltage.Click += Wvoltage;
            gavoltage.Click += Gavoltage;
            contextMenu.Items.AddRange(new[] { dataselection, clearfault });
            gridControl.ContextMenuStrip = contextMenu;

            this.Controls.Add(groupControl);
        }

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

        private void AddACDCStatusRows(LayoutControl layoutControl)
        {
            // 创建垂直排列的容器
            var container = new LayoutControlGroup
            {
                DefaultLayoutType = LayoutType.Vertical, //垂直排列
                GroupStyle = GroupStyle.Light,
                TextVisible = false,
                Padding = new DevExpress.XtraLayout.Utils.Padding(-3),
            };
            layoutControl.AddItem(container);

            // 添加AC状态行
            container.AddItem(CreateStatusRow(out lblACStatus, out lblACMode));
            // 添加DC状态行
            container.AddItem(CreateStatusRow(out lblDCStatus, out lblDCMode));
            // 添加系统时间行
            //container.AddItem(CreateStatusRow(out lblACTime, out lblDCTime));
        }

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
                        WordWrap = WordWrap.NoWrap // 禁止自动换行
                    }
                }
            };

            // 设置初始字体（稍后会动态调整）
            lblConnectionStatus.Font = new Font("Tahoma", 12, FontStyle.Bold);

            lblConnectionStatus.Appearance.Font = new Font("Tahoma", 24F, FontStyle.Bold);

            // 绑定事件
            lblConnectionStatus.TextChanged += (s, e) => AdjustFontSizeToFitLabel();
            lblConnectionStatus.SizeChanged += (s, e) => AdjustFontSizeToFitLabel();

            panelControl.Controls.Add(lblConnectionStatus);
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
                    float maxFontSize = 72f;  // 最大字体大小
                    float minFontSize = 8f;   // 最小字体大小
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
                            minFontSize = currentFontSize;  // 可以尝试更大字体
                        }
                        else
                        {
                            maxFontSize = currentFontSize;  // 需要更小字体
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
    }

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

        public bool IsSelected { get; set; } // 新增选中状态属性

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnpropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
