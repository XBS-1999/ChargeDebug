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
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;
using ZLGAPI;

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
        #region 数据保存字段

        // ==================== 实时数据保存相关字段 ====================
        // 实时数据保存队列
        private readonly ConcurrentQueue<StartupDataItem> _realtimeDataQueue = new ConcurrentQueue<StartupDataItem>();
        private readonly AutoResetEvent _realtimeDataEvent = new AutoResetEvent(false);
        private CancellationTokenSource _realtimeDataCancellationTokenSource;
        private Task _realtimeDataProcessingTask;
        // 启动数据保存队列
        private readonly ConcurrentQueue<StartupDataItem> _startupDataQueue = new ConcurrentQueue<StartupDataItem>();
        private readonly AutoResetEvent _startupDataEvent = new AutoResetEvent(false);
        private CancellationTokenSource _startupDataCancellationTokenSource;
        private Task _startupDataProcessingTask;

        // 启动数据项定义
        private struct StartupDataItem
        {
            public CANManager.ZCAN_Receive_Data Frame;
            public Dictionary<string, double> SignalValues;
            public DateTime Timestamp;
        }

        // 启动数据保存相关字段
        private bool _isStartupSaving = false;
        private StreamWriter _startupFileWriter;
        private string _startupFilePath;
        private DateTime _startupStartTime;

        // 实时数据保存相关字段
        private bool _isRealTimeSaving = false;
        private StreamWriter _realtimeFileWriter;
        private string _realtimeFilePath;
        private DateTime _realtimeStartTime;

        // 静态字典，用于管理同一设备下的数据保存状态和文件写入器
        private static readonly ConcurrentDictionary<string, bool> _deviceSaveStatus = new ConcurrentDictionary<string, bool>();
        private static readonly ConcurrentDictionary<string, StreamWriter> _deviceFileWriters = new ConcurrentDictionary<string, StreamWriter>();
        private static readonly ConcurrentDictionary<string, object> _deviceFileLocks = new ConcurrentDictionary<string, object>();
        private static readonly ConcurrentDictionary<string, bool> _deviceHeaderWritten = new ConcurrentDictionary<string, bool>();
        private CheckEdit _chkRealTimeSave;
        private string _deviceKey; // 设备标识键

        //模块选中标记，主窗体按钮只执行勾选模块
        public bool IsModuleSelected { get; set; }

        //右上角选择框
        private CheckEdit _chkSelectModule;

        // 全局保存共用保护参数
        public static ConfigurationData GlobalProtectionParam { get; set; }

        // 保存目录管理
        private static string _baseSaveDirectory;
        private string _deviceSaveDirectory;

        private string _lastStartupType = ""; // "AC" 或 "DC"

        #endregion

        #region 字段声明

        public static readonly Dictionary<string, StartupManager> StartupManagers = new Dictionary<string, StartupManager>();

        // 模块控制字段
        private bool _moduleSendingEnabled = true;
        private bool _initialized = false;
        private volatile bool _disposed = false;
        private bool _isConnected;
        private bool _stopCommandSent = false;

        // CAN通信相关字段
        private uint _dcfaultCanId;
        private uint _acfaultCanId;
        private uint _voltageCanId;
        private uint _acreadFaultCanId;
        private uint _dcreadFaultCanId;

        // 设备状态字段
        private uint _acRunStatus;
        private uint _dcRunStatus;
        private uint _acRunMode;
        private uint _dcRunMode;
        private bool softwareprotection = false;

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
        private ACStartConfiguration? _acParamForm; // 新增：AC参数配置窗体

        // 定时器字段
        private System.Threading.Timer _uiUpdateTimer;
        private System.Threading.Timer _readFaultTimer;
        private const int UI_UPDATE_INTERVAL = 1000;

        // 常量定义
        private const string VOLTAGE_SIGNAL = "蓄电池电压";
        private const string CURRENT_SIGNAL = "蓄电池电流";
        private const string POWER_SIGNAL = "蓄电池功率";

        // 通道类型标识
        public bool _hasACChannel = false;
        public bool _hasDCChannel = false;

        // 风扇控制相关字段
        private uint _fanCanId = 0x0400A0CC;          // 风扇控制CAN ID
        private System.Threading.Timer _fanTimer;                       // 风扇循环定时器
        private bool _fanCycleActive;                   // 风扇循环是否激活
        private bool _fanIsRunning;                     // 当前风扇是否运行
        private readonly object _fanLock = new object(); // 线程锁

        public ConfigurationData ProtectionParameters { get; set; }

        public event Action<string> FaultDetected; // 专门用于故障通知的事件

        #endregion

        #region 文件管理字段

        // 文件大小限制
        private const long MAX_REALTIME_FILE_SIZE = 100 * 1024 * 1024; // 100MB
        private const long MAX_BASE_FOLDER_SIZE = 2000 * 1024 * 1024; // 2000MB

        // 文件管理相关字段
        private string _currentRealtimeFilePath;
        private long _currentRealtimeFileSize = 0;
        private DateTime _currentRealtimeFileCreateTime;

        private string _userPermissions;

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
        //public Module(string title, EquipmentModel equipment, List<SignalInfo> signals, string userPermissions)
        public Module(string title, EquipmentModel equip, List<SignalInfo> canSignals, List<ModbusSignal> modbusSignals, string protocolType, string userPerm)
        {
            //byte[] data = { 0x55, 0xCA, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }; // 0x55CA000000000000
            //SignalInfo signal1 = new SignalInfo { StartBit = 7, Length = 16, ByteOrder = "1"};
            //ulong result1 = CANManager.Instance.ExtractRawValue(data, signal1);

            //return;
            _equipment = equip;
            _title = title;
            _userPermissions = userPerm;

            // 生成设备标识键（基于设备IP和索引）
            _deviceKey = $"{_equipment.DeviceName}_{_equipment.DeviceIP}_{_equipment.DeviceIndex}";

            // 初始化设备文件锁
            _deviceFileLocks.GetOrAdd(_deviceKey, new object());

            // 初始化保存目录
            InitializeSaveDirectories();

            // 根据模块标识判断通道类型
            if (protocolType == "CAN总线")
                DetectChannelTypes(title, canSignals);

            InitializeComponent();
            InitializeUI();
            ProcessSignals(canSignals);

            // 初始化UI更新定时器
            _uiUpdateTimer = new System.Threading.Timer(_ =>
            {
                UpdateUIFromCache();
            }, null, UI_UPDATE_INTERVAL, UI_UPDATE_INTERVAL);

            // 初始化启动管理器
            _startupManager = new StartupManager(_equipment, title);
            string channelPart = ExtractStandardDeviceName(title);
            StartupManagers[channelPart] = _startupManager;

            // 初始化读取故障定时器（初始不启动）
            _readFaultTimer = new System.Threading.Timer(SendReadFaultCommand, null, Timeout.Infinite, Timeout.Infinite);

            this.Load += Module_Load;
        }

        /// <summary>
        /// 初始化保存目录
        /// </summary>
        private void InitializeSaveDirectories()
        {
            try
            {
                // 基础保存目录：软件运行目录下的RealTimeData文件夹
                _baseSaveDirectory = Path.Combine(Application.StartupPath, "RealTimeData");
                if (!Directory.Exists(_baseSaveDirectory))
                {
                    Directory.CreateDirectory(_baseSaveDirectory);
                }

                // 设备专用目录：基于设备名称和IP
                string safeDeviceName = string.Join("_", _equipment.DeviceName.Split(Path.GetInvalidFileNameChars()));
                string safeDeviceIP = _equipment.DeviceIP.Replace(".", "_");
                _deviceSaveDirectory = Path.Combine(_baseSaveDirectory, $"{safeDeviceName}_{safeDeviceIP}");

                if (!Directory.Exists(_deviceSaveDirectory))
                {
                    Directory.CreateDirectory(_deviceSaveDirectory);
                }

                // 启动时检查基础文件夹大小并清理
                //CheckAndCleanBaseFolder();

                //LogService.Log($"初始化保存目录: {_deviceSaveDirectory}");
            }
            catch (Exception ex)
            {
                LogService.Log($"初始化保存目录失败: {ex.Message}");
                // 如果创建目录失败，使用基础目录作为备用
                _deviceSaveDirectory = _baseSaveDirectory;
            }
        }

        /// <summary>
        /// 检查并清理基础文件夹
        /// </summary>
        private void CheckAndCleanBaseFolder()
        {
            try
            {
                long currentSize = CalculateFolderSize(_baseSaveDirectory);
                //LogService.Log($"RealTimeData文件夹当前大小: {currentSize / 1024 / 1024}MB");

                if (currentSize > MAX_BASE_FOLDER_SIZE)
                {
                    LogService.Log("RealTimeData文件夹超过2000MB，开始清理...");
                    CleanOldFiles(_baseSaveDirectory);
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"检查基础文件夹大小时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 清理旧文件
        /// </summary>
        private void CleanOldFiles(string folderPath)
        {
            try
            {
                // 获取所有文件，按创建时间排序
                var allFiles = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
                                       .Select(f => new FileInfo(f))
                                       .OrderBy(f => f.CreationTime)
                                       .ToList();

                long currentSize = CalculateFolderSize(folderPath);
                int filesDeleted = 0;

                // 从最旧的文件开始删除，直到文件夹大小小于限制
                foreach (var file in allFiles)
                {
                    if (currentSize <= MAX_BASE_FOLDER_SIZE * 0.5) // 清理到50%的限制大小
                        break;

                    try
                    {
                        long fileSize = file.Length;
                        file.Delete();
                        currentSize -= fileSize;
                        filesDeleted++;
                        LogService.Log($"删除旧文件: {file.FullName}, 大小: {fileSize / 1024 / 1024}MB");
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"删除文件失败 {file.FullName}: {ex.Message}");
                    }
                }

                LogService.Log($"清理完成，删除了 {filesDeleted} 个文件，当前文件夹大小: {currentSize / 1024 / 1024}MB");
            }
            catch (Exception ex)
            {
                LogService.Log($"清理旧文件时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 计算文件夹大小
        /// </summary>
        private long CalculateFolderSize(string folderPath)
        {
            long size = 0;
            try
            {
                if (!Directory.Exists(folderPath)) return 0;

                foreach (string file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        FileInfo fileInfo = new FileInfo(file);
                        size += fileInfo.Length;
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"计算文件大小失败 {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"计算文件夹大小失败 {folderPath}: {ex.Message}");
            }
            return size;
        }

        /// <summary>
        /// 初始化用户界面
        /// </summary>
        private void InitializeUI()
        {
            // 设置模块容器大小
            this.ClientSize = new Size(400, 700);

            // 通道容器
            CustomGroupControl groupControl = new CustomGroupControl();
            groupControl.Text = _title;
            groupControl.Dock = DockStyle.Fill;
            groupControl.Padding = new System.Windows.Forms.Padding(-3);
            groupControl.Margin = new System.Windows.Forms.Padding(0);

            // 在标题栏添加实时数据保存开关
            AddRealTimeSaveToTitle(groupControl);

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
        /// 在GroupControl标题栏添加实时数据保存开关
        /// </summary>
        private void AddRealTimeSaveToTitle(CustomGroupControl groupControl)
        {
            // 创建实时保存复选框
            _chkRealTimeSave = new CheckEdit();
            _chkRealTimeSave.Properties.Caption = "实时保存";
            _chkRealTimeSave.Properties.AllowGrayed = false;
            _chkRealTimeSave.BackColor = Color.Transparent;
            // 固定尺寸，防止自适应挤压重叠
            _chkRealTimeSave.Size = new Size(90, 24);
            _chkRealTimeSave.CheckedChanged += ChkRealTimeSave_CheckedChanged;

            //新增：模块选中复选框，放在最右侧
            _chkSelectModule = new CheckEdit();
            _chkSelectModule.Properties.Caption = "选择";
            _chkSelectModule.Properties.AllowGrayed = false;
            _chkSelectModule.BackColor = Color.Transparent;
            // 固定宽度
            _chkSelectModule.Size = new Size(70, 24);
            _chkSelectModule.CheckedChanged += (s, e) =>
            {
                IsModuleSelected = _chkSelectModule.Checked;
            };


            //加入标题栏
            groupControl.AddControlToTitle(_chkRealTimeSave);
            groupControl.AddControlToTitle(_chkSelectModule);
        }

        /// <summary>
        /// 检测模块包含的通道类型
        /// </summary>
        private void DetectChannelTypes(string title, List<SignalInfo> signals)
        {
            // 根据模块标题判断通道类型
            _hasACChannel = title.Contains("AC") || signals.Any(s =>
                s.SystemName.Contains("AC") && (s.SystemName.Contains("运行状态") || s.SystemName.Contains("运行模式")));

            _hasDCChannel = title.Contains("DC") || signals.Any(s =>
                s.SystemName.Contains("DC") && (s.SystemName.Contains("运行状态") || s.SystemName.Contains("运行模式")));

            // 如果无法从标题判断，尝试从信号列表中判断
            if (!_hasACChannel && !_hasDCChannel)
            {
                _hasACChannel = signals.Any(s => s.SystemName == "AC运行状态" || s.SystemName == "AC运行模式");
                _hasDCChannel = signals.Any(s => s.SystemName == "DC运行状态" || s.SystemName == "DC运行模式");
            }

            //LogService.Log($"模块 '{title}' 通道检测: AC={_hasACChannel}, DC={_hasDCChannel}");
        }

        /// <summary>
        /// 初始化上下文菜单
        /// </summary>
        private void InitializeContextMenu()
        {
            contextMenu = new ContextMenuStrip();
            var dataselection = new ToolStripMenuItem("数据选择");
            var parameterset = new ToolStripMenuItem("参数设置");
            var shutDown = new ToolStripMenuItem("停止测试");
            var clearfault = new ToolStripMenuItem("清除故障");
            var lowvoltage = new ToolStripMenuItem("电压低档");
            var gavoltage = new ToolStripMenuItem("风扇启动");
            var fanStop = new ToolStripMenuItem("风扇停止");
            

            dataselection.Click += ShowSignalSelector;
            parameterset.Click += ParameterSet;
            shutDown.Click += ShutDown;
            clearfault.Click += Clearfault;
            lowvoltage.Click += Wvoltage;
            gavoltage.Click += Gavoltage;
            fanStop.Click += FanStop;

            contextMenu.Items.AddRange(new[] { dataselection, parameterset, shutDown, clearfault, gavoltage, fanStop });

            // 根据通道类型动态创建启动菜单项
            // 如果包含AC通道，添加AC启动选项
            if (_hasACChannel)
            {
                var acPowerOn = new ToolStripMenuItem("AC侧启动");
                acPowerOn.Click += ACPoweronAsync;
                contextMenu.Items.Insert(1, acPowerOn); // 插入到第二个位置
            }

            if (_hasDCChannel)
            {
                var dcPowerOn = new ToolStripMenuItem("启动测试");
                dcPowerOn.Click += PoweronAsync;
                if (_hasACChannel)
                {
                    contextMenu.Items.Insert(2, dcPowerOn); // 插入到第三个位置
                }
                else
                {
                    contextMenu.Items.Insert(1, dcPowerOn); // 插入到第二个位置
                }
            }

            gridControl.ContextMenuStrip = contextMenu;
        }

        /// <summary>
        /// 批量启动：执行启动（不再弹窗）
        /// </summary>
        public async Task<bool> RunStartWithSharedParam()
        {
            try
            {
                if (_dcRunStatus != 0x00 && _dcRunStatus != 0x03)
                {
                    LogService.Log($"{_title}状态异常，跳过启动");
                    return false;
                }

                _protectionParameters = GlobalProtectionParam;

                bool run = await _startupManager.StartDeviceAsync(_protectionParameters, false);
                if (!run) return false;

                _stopCommandSent = true;
                bool statusChanged = await CheckDeviceStatusChange(TimeSpan.FromSeconds(3), "DC");
                bool runmode = await CheckRunMode(TimeSpan.FromSeconds(5), _protectionParameters.WorkingMode, "DC");

                if (!statusChanged || !runmode)
                {
                    await _startupManager.StopDeviceAsync();
                    return false;
                }

                _startTime = DateTime.Now;
                _stepStartTime = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"{_title}启动异常：{ex.Message}");
                return false;
            }
        }

        public async Task<bool> RunACStartWithSharedParam()
        {
            // AC模块同理，只执行下发，不再弹窗
            try
            {
                if (_acRunStatus != 0x00 && _acRunStatus != 0x03)
                    return false;

                // 共用全局参数自行补充
                _stopCommandSent = true;
                bool ok = await _startupManager.ACStartDeviceAsync(null);
                return ok;
            }
            catch
            {
                return false;
            }
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

        /// <summary>
        /// 实时数据保存复选框状态改变事件
        /// </summary>
        private void ChkRealTimeSave_CheckedChanged(object sender, EventArgs e)
        {
            //if (_acRunStatus == 0x01 || _acRunStatus == 0x02 || _dcRunStatus == 0x01 || _dcRunStatus == 0x02)
            //{
            //    XtraMessageBox.Show($"设备运行中，数据已开启保存！", "错误",
            //        MessageBoxButtons.OK, MessageBoxIcon.Error);
            //    return;
            //}

            bool shouldSave = _chkRealTimeSave.Checked;

            // 更新设备级别的保存状态
            UpdateDeviceSaveStatus(shouldSave);

            if (shouldSave)
            {
                StartRealTimeSave();
                //LogService.Log($"{_title} 开始实时数据保存");
            }
            else
            {
                StopRealTimeSave();
                //LogService.Log($"{_title} 停止实时数据保存");
            }
        }

        /// <summary>
        /// 更新设备级别的数据保存状态
        /// </summary>
        private void UpdateDeviceSaveStatus(bool shouldSave)
        {
            // 更新设备状态
            _deviceSaveStatus.AddOrUpdate(_deviceKey, shouldSave, (key, oldValue) => shouldSave);

            // 查找同一设备下的其他模块并开启保存
            var otherModules = FindOtherModulesInSameDevice();
            foreach (var module in otherModules)
            {
                if (module != this && module.IsHandleCreated)
                {
                    module.BeginInvoke(new Action(() =>
                    {
                        // 先移除事件处理程序避免递归调用
                        module._chkRealTimeSave.CheckedChanged -= module.ChkRealTimeSave_CheckedChanged;

                        // 设置复选框状态
                        module._chkRealTimeSave.Checked = shouldSave;

                        // 重新添加事件处理程序
                        module._chkRealTimeSave.CheckedChanged += module.ChkRealTimeSave_CheckedChanged;

                        // 更新实例的保存状态
                        module._isRealTimeSaving = shouldSave;

                        LogService.Log($"同步模块 {module._title} 实时保存状态: {shouldSave}");
                    }));
                }
            }

            // 更新当前实例的保存状态
            //_isRealTimeSaving = shouldSave;

            // 如果没有模块在保存，关闭文件写入器
            if (!shouldSave)
            {
                bool anyModuleSaving = otherModules.Any(m => m != this && m._isRealTimeSaving);
                if (!anyModuleSaving)
                {
                    //CloseDeviceFileWriter();
                }
            }
        }

        /// <summary>
        /// 查找同一设备下的其他模块
        /// </summary>
        private List<Module> FindOtherModulesInSameDevice()
        {
            var modules = new List<Module>();

            try
            {
                var parentForm = this.FindForm();
                if (parentForm != null)
                {
                    // 查找窗体中的所有模块
                    FindModulesInForm(parentForm, modules);
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"查找同一设备模块时出错: {ex.Message}");
            }

            return modules;
        }

        /// <summary>
        /// 在窗体中查找模块
        /// </summary>
        private void FindModulesInForm(Control parent, List<Module> modules)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is Module module)
                {
                    // 检查是否属于同一设备
                    if (module._deviceKey == this._deviceKey)
                    {
                        modules.Add(module);
                    }
                }
                else if (control.HasChildren)
                {
                    // 递归查找子控件
                    FindModulesInForm(control, modules);
                }
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

                //保存解析的信号
                Dictionary<string, double> signalValue = new Dictionary<string, double>();

                foreach (var signal in signals)
                {
                    if (!signalDefinitions.TryGetValue(signal.SystemName, out var signalDef)) continue;

                    // 1. 从CAN帧中提取原始值ZDBC_CalcActualValue
                    ulong rawValue = CANManager.Instance.ExtractRawValue(frame.data, signalDef);
                    //ulong rawValue = ZDBC.ZDBC_CalcActualValue(signalDef, frame.data);

                    // 2. 转换为物理值
                    double value = CANManager.Instance.ConvertToPhysicalValue(rawValue, signalDef);

                    // 3. 根据精度要求四舍五入
                    int decimalPlaces = CANManager.Instance.GetNumberOfDecimalPlaces(signalDef.Factor);
                    double physicalValue = Math.Round(value, decimalPlaces);

                    // 4. 更新缓存（实时更新）
                    _signalValues[signal.SystemName] = physicalValue;
                    signalValue[signal.SystemName] = physicalValue;

                    // 5. 实时更新运行状态
                    UpdateDeviceStatus(signal.SystemName, physicalValue);
                    
                    // 6. 实时监控关键信号（电压、电流、功率）
                    if (_protectionParameters != null && _stopCommandSent)
                    {
                        CheckCriticalSignals(signal.SystemName, physicalValue);
                    }

                    // 7. 实时处理故障信号（不等待UI更新）
                    if (_faultSignalDefinitions.ContainsKey(signal.SystemName))
                    {
                        ProcessFaultSignal(signal.SystemName, physicalValue);
                    }
                }

                // 实时保存数据（如果启用）- 使用新线程避免阻塞
                if (_isRealTimeSaving)
                {
                    var realtimesaveDataItem = new StartupDataItem
                    {
                        Frame = frame,
                        SignalValues = new Dictionary<string, double>(signalValue), // 创建副本避免后续修改
                        Timestamp = DateTime.Now
                    };

                    _realtimeDataQueue.Enqueue(realtimesaveDataItem);
                    _realtimeDataEvent.Set(); // 通知处理线程有新的数据
                }

                // 保存启动数据（如果启动保存启用）- 使用新线程避免阻塞
                //if (_isStartupSaving)
                //{
                //    var startupDataItem = new StartupDataItem
                //    {
                //        Frame = frame,
                //        SignalValues = new Dictionary<string, double>(signalValue), // 创建副本避免后续修改
                //        Timestamp = DateTime.Now
                //    };

                //    _startupDataQueue.Enqueue(startupDataItem);
                //    _startupDataEvent.Set(); // 通知处理线程有新的数据
                //}
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
                string devicePrefix = deviceName.Split("-")[0];
                string dcPart = deviceName.Split("-")[1];

                // 查找"DC"的位置
                int dcIndex = dcPart.IndexOf("DC");
                if (dcIndex < 0)
                {
                    return deviceName;
                }
                else
                {
                    // 提取DC部分
                    dcPart = dcPart.Substring(dcIndex); // 去掉斜杠
                }

                return $"{devicePrefix}-{dcPart}";
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
            if (_userPermissions != "普通用户")
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
            }

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
            //LogService.Log($"共发现 {_faultSignalDefinitions.Count} 个故障信号");

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

            if ((_acRunStatus == 0xFF) || (_dcRunStatus == 0xFF))
            {
                // 触发停机指令，只发一次
                SendStopCommandIfNeeded();
            }

            // 检查状态变化并通知
            if (signalName == "DC运行状态" && oldDcRunStatus != _dcRunStatus)
            {
                // 如果是故障状态，触发故障事件
                if (_dcRunStatus == 0xFF)
                {
                    string faultMessage = "DC设备故障状态检测";
                    FaultDetected?.Invoke(faultMessage);
                    //LogService.Log(faultMessage);
                }
            }

            CheckAndControlFaultTimer(oldDcRunStatus, oldAcRunStatus);
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
                LogService.Log($"{_title} 检测到故障状态，启动故障读取定时器");
            }
            // 如果所有设备都恢复正常，停止定时器
            else if ((dcRecovered && _acRunStatus != 0xFF) || (acRecovered && _dcRunStatus != 0xFF))
            {
                _readFaultTimer.Change(Timeout.Infinite, Timeout.Infinite);
                LogService.Log($"{_title} 恢复正常，停止故障读取定时器");

                // 清除故障显示
                lock (_faultQueueLock)
                {
                    _activeFaults.Clear();
                    _faultDisplayQueue.Clear();
                }
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
                        softwareprotection = true;
                        string name = $"过压保护: {value} > {overVoltage}";
                        _activeFaults["软件_过压"] = name;
                        _faultDisplayQueue.Enqueue(name);
                        LogService.Log(name);
                        //XtraMessageBox.Show($"电压异常: {value} > {overVoltage} (过压保护值)");
                    }
                    else if (double.TryParse(_protectionParameters.UnderVoltage, out double underVoltage) &&
                             value < underVoltage)
                    {
                        //SendStopCommandIfNeeded();
                        //softwareprotection = true;
                        //string name = $"欠压保护: {value} > {underVoltage}";
                        //_activeFaults["软件_欠压"] = name;
                        //_faultDisplayQueue.Enqueue(name);
                        //LogService.Log(name);
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
                        softwareprotection = true;
                        string name = $"充电过流保护: {value} > {overCurrent}";
                        _activeFaults["软件_充电过流"] = name;
                        _faultDisplayQueue.Enqueue(name);
                        LogService.Log(name);
                        //XtraMessageBox.Show($"电流异常: {value} > {overCurrent} (过流保护值)");
                    }
                    else if (double.TryParse(_protectionParameters.UnderCurrent, out double underCurrent) &&
                             value < underCurrent)
                    {
                        SendStopCommandIfNeeded();
                        softwareprotection = true;
                        string name = $"放电过流保护: {value} > {underCurrent}";
                        _activeFaults["软件_放电过流"] = name;
                        _faultDisplayQueue.Enqueue(name);
                        LogService.Log(name);
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
                        softwareprotection = true;
                        string name = $"充电过功率保护: {value} > {overPower}";
                        _activeFaults["软件_充电过功率"] = name;
                        _faultDisplayQueue.Enqueue(name);
                        LogService.Log(name);
                        //XtraMessageBox.Show($"功率异常: {value} > {overPower} (过功率保护值)");
                    }
                    else if (double.TryParse(_protectionParameters.UnderPower, out double underPower) &&
                             value < underPower)
                    {
                        SendStopCommandIfNeeded();
                        softwareprotection = true;
                        string name = $"放电过功率保护: {value} > {overPower}";
                        _activeFaults["软件_放电过功率"] = name;
                        _faultDisplayQueue.Enqueue(name);
                        LogService.Log(name);
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
                        if (_title.Contains("DC"))
                        {
                            bool success = await _startupManager.StopDeviceAsync();
                            if (success)
                            {
                                // ============ 新增：停止启动数据保存 ============
                                //StopStartupDataSave();
                                // ============ 新增结束 ============

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
                        else
                        {
                            // 发送停机指令
                            bool stopSuccess = await _startupManager.ACStopDeviceAsync();
                            if (stopSuccess)
                            {
                                // ============ 新增：停止启动数据保存 ============
                                //StopStartupDataSave();
                                // ============ 新增结束 ============

                                // 在停止设备时重置时间
                                _startTime = DateTime.MinValue;
                                _stepStartTime = DateTime.MinValue;
                                LogService.Log("AC停机指令发送成功");
                            }
                            else
                            {
                                LogService.Log("AC停机指令发送失败");
                            }
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
        private async Task<bool> CheckDeviceStatusChange(TimeSpan timeout, string channel)
        {
            DateTime startTime = DateTime.Now;

            while (DateTime.Now - startTime < timeout)
            {
                if (channel == "AC")
                {
                    // 检查状态是否变为 0x01 (启动过程中) 或 0x02 (运行)
                    if (_acRunStatus == 0x01 || _acRunStatus == 0x02)
                    {
                        return true; // 状态已改变
                    }
                }
                else
                {
                    // 检查状态是否变为 0x01 (启动过程中) 或 0x02 (运行)
                    if (_dcRunStatus == 0x01 || _dcRunStatus == 0x02)
                    {
                        return true; // 状态已改变
                    }
                }

                // 等待一段时间再检查
                await Task.Delay(200);
            }

            // 超时，状态未改变
            return false;
        }

        /// <summary>
        /// 检查运行模式
        /// </summary>
        private async Task<bool> CheckRunMode(TimeSpan timeout, string workingMode, string channel)
        {
            uint mode = 0;
            DateTime startTime = DateTime.Now;

            while (DateTime.Now - startTime < timeout)
            {
                if (channel == "AC")
                {
                    return true;
                    switch (workingMode)
                    {
                        case "恒定并网直流恒压运行":
                            mode = 0x01;
                            break;

                        case "交流恒功率运行":
                            mode = 0x02;
                            break;

                        case "电池电压恒定母线运行":
                            mode = 0x11;
                            break;

                        case "停机":
                            mode = 0x00;
                            break;
                    }

                    if (mode == _acRunMode)
                    {
                        return true;
                    }
                }
                else
                {
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
                        return true;
                    }
                }

                // 等待一段时间再检查
                await Task.Delay(200);
            }

            // 超时，状态未改变
            return false;
        }

        /// <summary>
        /// 比较参数是否有变化
        /// </summary>
        private bool CompareParameters(ConfigurationData oldParams, ConfigurationData newParams)
        {
            if (oldParams != null && newParams != null)
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
            }
            else
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
                    //name = signalName.Remove(0, 3);
                    if (faultDescription == "故障")
                    {
                        name = signalName.Remove(0, 3);
                    }
                    else
                    {
                        name = faultDescription;
                        //name = signalName.Remove(0, 3);
                    }

                    if (!_activeFaults.ContainsKey(signalName))
                    {
                        _activeFaults[signalName] = name;
                        _faultDisplayQueue.Enqueue(name);
                        LogService.Log($"{_title} 检测到新故障: {signalName} → {name}");
                    }
                    else if (_activeFaults[signalName] != name)
                    {
                        _activeFaults[signalName] = name;
                        //_faultDisplayQueue.Enqueue(name);
                        LogService.Log($"{_title} 故障更新: {signalName} → {name}");
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
                        LogService.Log($"{_title} 故障清除: {signalName}");
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
        /// 获取下一个要显示的故障
        /// </summary>
        private string GetNextFaultToDisplay()
        {
            lock (_faultQueueLock)
            {
                // 如果没有活跃故障，清空队列并返回空
                if (_activeFaults.Count == 0)
                {
                    if (_faultDisplayQueue.Count > 0)
                    {
                        _faultDisplayQueue.Clear();
                    }
                    return string.Empty;
                }

                // 如果队列为空但仍有活跃故障，重建队列
                if (_faultDisplayQueue.Count == 0)
                {
                    RebuildFaultDisplayQueue();
                }

                // 确保队列中有数据
                if (_faultDisplayQueue.Count > 0)
                {
                    string nextFault = _faultDisplayQueue.Dequeue();

                    // 检查故障是否仍然活跃
                    if (_activeFaults.ContainsValue(nextFault))
                    {
                        // 放回队列尾部实现循环
                        _faultDisplayQueue.Enqueue(nextFault);
                        return nextFault;
                    }
                    else
                    {
                        // 如果故障已清除，跳过本次显示
                        LogService.Log($"跳过已清除的故障: {nextFault}");
                        return GetNextFaultToDisplay(); // 递归获取下一个
                    }
                }

                return string.Empty;
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
            if (_disposed || this.IsDisposed || !this.IsHandleCreated) return;

            // 切换到UI线程
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateUIFromCache));
                return;
            }

            try
            {
                // 1. 更新时间显示
                UpdateTimeDisplay();

                // 2. 获取故障显示内容
                
                // 3. 创建信号值的快照
                var snapshot = _signalValues.ToArray();

                // 4. 更新表格数据和状态显示
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

                    // 更新状态标签
                    UpdateStatusLabels(signalName, displayValue);
                }

                // 5. 更新状态显示（整合故障显示）
                string faultDisplay = string.Empty;
                bool isFaultMode = (_acRunStatus == 0xFF || _dcRunStatus == 0xFF || softwareprotection);
               
                if (isFaultMode)
                {
                    faultDisplay = GetNextFaultToDisplay();
                    if (!string.IsNullOrEmpty(faultDisplay))
                    {
                        UpdateConnectionStatusUI(faultDisplay, Color.Red);
                    }
                }
                else
                {
                    // 正常状态显示
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

            if (!_hasDCChannel)
            {
                switch (_acRunStatus)
                {
                    case 0x00:
                        UpdateConnectionStatusUI("待机", Color.Yellow);
                        break;

                    case 0x01:
                        UpdateConnectionStatusUI("启动过程中", Color.YellowGreen);
                        break;

                    case 0x02: // 运行
                        UpdateConnectionStatusUI("运行", Color.Green);
                        break;
                    case 0x03: // 停机过程中
                        UpdateConnectionStatusUI("停机过程中", Color.YellowGreen);
                        break;

                    default:
                        break;
                }
            }
            else
            {
                switch (_dcRunStatus)
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
                        //UpdateConnectionStatusUI("未知状态", Color.Gray);
                        break;
                }
            }
        }

        /// <summary>
        /// 更新连接状态UI
        /// </summary>
        private void UpdateConnectionStatusUI(string text, Color color)
        {
            // 添加销毁状态检查
            if (_disposed || this.IsDisposed || !this.IsHandleCreated) return;

            try
            {
                // 切换到UI线程
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(UpdateUIFromCache));
                    return;
                }

                panelControl.Appearance.BackColor = color;
                panelControl.BorderStyle = BorderStyles.NoBorder;
                lblConnectionStatus.Text = text;

                // 根据需要调整字体大小
                AdjustFontSizeToFitLabel();
            }
            catch (Exception ex)
            {
                LogService.Log($"更新连接状态UI错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新时间显示
        /// </summary>
        private void UpdateTimeDisplay()
        {
            if (_disposed || this.IsDisposed || !this.IsHandleCreated) return;

            try
            {
                // 切换到UI线程
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(UpdateUIFromCache));
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
                    using (var configForm = new StartConfiguration(_title, "DC启动配置", true))
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
                            bool run = await _startupManager.StartDeviceAsync(_protectionParameters, false);

                            if (run)
                            {
                                // ============ 新增：开始启动数据保存 ============
                                //StartStartupDataSave();

                                _stopCommandSent = true;

                                // 检测设备状态变化
                                bool statusChanged = await CheckDeviceStatusChange(TimeSpan.FromSeconds(3), "DC");

                                // 检测设备运行工步码是否一致
                                bool runmode = await CheckRunMode(TimeSpan.FromSeconds(5), configForm.Configuration.WorkingMode, "DC");

                                if ((!statusChanged) || (!runmode))
                                {
                                    // 发送停机指令
                                    bool stopSuccess = await _startupManager.StopDeviceAsync();
                                    if (!stopSuccess)
                                    {
                                        XtraMessageBox.Show("停机指令发送失败");
                                        return;
                                    }

                                    // 停止启动数据保存
                                    //StopStartupDataSave();

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
                                }

                                // 异步等待设备进入运行状态
                                await WaitForRunStatus(0x02, TimeSpan.FromSeconds(30));

                                // 设置工步开始时间
                                _stepTimeData.Value = "00:00:00";
                                _stepStartTime = DateTime.Now;
                                _lastStartupType = "DC";
                                LogService.Log("设备启动成功");
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
                // 停止启动数据保存
                //StopStartupDataSave();
                LogService.Log($"设备启动失败:{ex.Message}");
                XtraMessageBox.Show($"设备启动失败:{ex.Message}");
            }
        }

        /// <summary>
        /// 异步启动AC设备
        /// </summary>
        private async void ACPoweronAsync(object? sender, EventArgs e)
        {
            try
            {
                // 检查设备状态,待机、停机过程情况下才能启动
                if (_acRunStatus == 0x00 || _acRunStatus == 0x03)
                {
                    // 显示AC启动配置对话框
                    using (var configForm = new ACStartConfiguration(_title, "AC启动配置", true))
                    {
                        if (configForm.ShowDialog() == DialogResult.OK)
                        {
                            var acConfig = configForm.ACConfiguration;

                            // 记录AC启动配置
                            LogService.Log($"AC启动配置 - 启动方式: {acConfig.StartMode}, 运行模式: {acConfig.RunMode}");

                            // 这里可以添加AC设备启动的具体逻辑
                            bool success = await _startupManager.ACStartDeviceAsync(acConfig);

                            if (success)
                            {

                                // ============ 新增：开始启动数据保存 ============
                                //StartStartupDataSave();
                                // ============ 新增结束 ============

                                _stopCommandSent = true;

                                // 检测设备状态变化
                                bool statusChanged = await CheckDeviceStatusChange(TimeSpan.FromSeconds(3), "AC");

                                // 检测设备运行工步码是否一致
                                bool runmode = await CheckRunMode(TimeSpan.FromSeconds(5), acConfig.RunMode, "AC");

                                if (!statusChanged || !runmode)
                                {
                                    // 发送停机指令
                                    bool stopSuccess = await _startupManager.ACStopDeviceAsync();
                                    if (!stopSuccess)
                                    {
                                        XtraMessageBox.Show("停机指令发送失败");
                                        return;
                                    }

                                    // 状态没有变化，启动失败
                                    LogService.Log($"设备控制失败，运行状态{statusChanged} - 运行模式{runmode}");
                                    XtraMessageBox.Show($"设备控制失败，运行状态{statusChanged} - 运行模式{runmode}");
                                    // 停止启动数据保存
                                    //StopStartupDataSave();
                                    return;
                                }

                                //XtraMessageBox.Show("AC通道控制成功");

                                // 设置AC运行时间
                                _totalTimeData.Value = "00:00:00";
                                _startTime = DateTime.Now;
                                _lastStartupType = "AC";

                                LogService.Log("AC通道控制成功");
                            }
                            else
                            {
                                LogService.Log("AC通道控制失败!");
                                XtraMessageBox.Show("AC通道控制失败!");
                            }
                        }
                    }
                }
                else
                {
                    LogService.Log("AC通道状态异常，禁止控制!");
                    XtraMessageBox.Show("AC通道状态异常，禁止控制");
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"AC通道控制失败:{ex.Message}");
                XtraMessageBox.Show($"AC通道控制失败:{ex.Message}");
                // 停止启动数据保存
                //StopStartupDataSave();
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
                if (_dcRunStatus != 0x02 && _acRunStatus != 0x02)
                {
                    XtraMessageBox.Show("设备状态异常，禁止设置参数");
                    return;
                }

                // 根据上次启动类型决定使用哪个配置窗体
                if (_lastStartupType == "AC")
                {
                    OpenACParameterConfiguration();
                }
                else if (_lastStartupType == "DC")
                {
                    OpenDCParameterConfiguration();
                }
                else
                {
                    if (_dcRunStatus == 0x02)   //DC运行中
                    {
                        OpenDCParameterConfiguration();
                    }
                    else
                    {
                        OpenACParameterConfiguration();
                    }
                    //XtraMessageBox.Show("请先启动设备再进行参数设置", "提示",
                    //    MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                //// 检查是否已有窗体实例存在
                //if (_paramForm != null && !_paramForm.IsDisposed)
                //{
                //    _paramForm.Activate(); // 激活已有窗体
                //    return;
                //}

                //// 保存当前保护参数以便比较
                //ConfigurationData currentParams = _protectionParameters;

                //_paramForm = new StartConfiguration(_title, "参数配置", true);

                //// 设置窗体位置居中
                //CenterFormToParent(_paramForm);

                //// 订阅窗体关闭事件以便清理引用
                //_paramForm.FormClosed += (s, args) =>
                //{
                //    _paramForm = null;
                //};

                //_paramForm.Applied += async (s, args) =>
                //{
                //    // 获取用户设置的新参数
                //    ConfigurationData newParams = _paramForm.Configuration;

                //    // ============ 新增：检查新参数是否会导致当前信号值超出阈值 ============
                //    if (!await CheckParametersSafe(newParams))
                //    {
                //        XtraMessageBox.Show("新参数设置会导致当前信号值超出保护阈值，请调整参数或设备状态");
                //        return;
                //    }
                //    // ============ 新增结束 ============

                //    // 比较参数是否有变化
                //    bool hasChanges = CompareParameters(currentParams, newParams);

                //    // 发送新的参数
                //    bool success = await _startupManager.StartDeviceAsync(newParams, hasChanges);

                //    if (success)
                //    {
                //        _stepStartTime = DateTime.MinValue;
                //        _stepTimeData.Value = "00:00:00";
                //        _stepStartTime = DateTime.Now;

                //        await Task.Delay(100);

                //        // 检测设备运行工步码是否一致
                //        bool runmode = await CheckRunMode(TimeSpan.FromSeconds(5), newParams.WorkingMode, "DC");

                //        if (runmode)
                //        {
                //            // 更新当前保护参数
                //            _protectionParameters = newParams;
                //            LogService.Log("参数设置成功");
                //            XtraMessageBox.Show("参数设置成功");
                //        }
                //        else
                //        {
                //            LogService.Log("参数发送失败");
                //            XtraMessageBox.Show("参数发送失败");
                //        }
                //    }
                //    else
                //    {
                //        LogService.Log("参数发送失败");
                //        XtraMessageBox.Show("参数发送失败");
                //    }
                //};

                //_paramForm.Show(); // 非模态显示
            }
            catch (Exception ex)
            {
                LogService.Log($"设置参数失败: {ex.Message}");
                XtraMessageBox.Show($"设置参数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 打开DC参数配置
        /// </summary>
        private void OpenDCParameterConfiguration()
        {
            // 检查是否已有窗体实例存在
            if (_paramForm != null && !_paramForm.IsDisposed)
            {
                _paramForm.Activate();
                return;
            }

            // 保存当前保护参数以便比较
            ConfigurationData currentParams = _protectionParameters;

            _paramForm = new StartConfiguration(_title, "DC参数配置", true); // false表示不是启动配置

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

                // 检查新参数是否会导致当前信号值超出阈值
                if (!await CheckParametersSafe(newParams))
                {
                    XtraMessageBox.Show("新参数设置会导致当前信号值超出保护阈值，请调整参数或设备状态");
                    return;
                }

                // 比较保护参数是否有变化
                bool hasChanges = CompareParameters(currentParams, newParams);

                // 发送新的参数
                bool success = await _startupManager.DeviceSetParameters(newParams, hasChanges);

                if (success)
                {
                    // 更新工步开始时间
                    _stepStartTime = DateTime.MinValue;
                    _stepTimeData.Value = "00:00:00";
                    _stepStartTime = DateTime.Now;

                    await Task.Delay(100);

                    // 检测设备运行工步码是否一致
                    bool runmode = await CheckRunMode(TimeSpan.FromSeconds(5), newParams.WorkingMode, "DC");

                    if (runmode)
                    {
                        // 更新当前保护参数
                        _protectionParameters = newParams;
                        LogService.Log("DC参数设置成功");
                        XtraMessageBox.Show("DC参数设置成功");
                    }
                    else
                    {
                        await _startupManager.StopDeviceAsync();
                        LogService.Log("DC参数发送失败");
                        XtraMessageBox.Show("DC参数发送失败");
                    }
                }
                else
                {
                    await _startupManager.StopDeviceAsync();
                    LogService.Log("DC参数发送失败");
                    XtraMessageBox.Show("DC参数发送失败");
                }
            };

            _paramForm.Show();
        }

        /// <summary>
        /// 打开AC参数配置
        /// </summary>
        private void OpenACParameterConfiguration()
        {
            // 检查是否已有窗体实例存在
            if (_acParamForm != null && !_acParamForm.IsDisposed)
            {
                _acParamForm.Activate();
                return;
            }

            // 创建AC参数配置窗体
            _acParamForm = new ACStartConfiguration(_title, "AC参数配置", true);

            // 设置窗体位置居中
            CenterFormToParent(_acParamForm);

            // 订阅窗体关闭事件以便清理引用
            _acParamForm.FormClosed += (s, args) =>
            {
                _acParamForm = null;
            };

            // 订阅应用按钮事件
            _acParamForm.Applied += async (s, args) =>
            {
                // 获取AC配置参数
                var acConfig = _acParamForm.ACConfiguration;

                try
                {
                    // 发送AC参数设置命令
                    bool success = await _startupManager.ACStartDeviceAsync(acConfig);

                    if (success)
                    {
                        // 更新工步开始时间
                        _stepStartTime = DateTime.MinValue;
                        _stepTimeData.Value = "00:00:00";
                        _stepStartTime = DateTime.Now;

                        await Task.Delay(100);

                        // 检测设备运行模式是否一致
                        bool runmode = await CheckRunMode(TimeSpan.FromSeconds(5), acConfig.RunMode, "AC");

                        if (runmode)
                        {
                            LogService.Log("AC参数设置成功");
                            XtraMessageBox.Show("AC参数设置成功");
                        }
                        else
                        {
                            LogService.Log("AC参数发送失败");
                            XtraMessageBox.Show("AC参数发送失败");
                        }
                    }
                    else
                    {
                        LogService.Log("AC参数发送失败");
                        XtraMessageBox.Show("AC参数发送失败");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Log($"AC参数设置失败: {ex.Message}");
                    XtraMessageBox.Show($"AC参数设置失败: {ex.Message}");
                }
            };

            _acParamForm.Show();
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
                // 确认对话框
                if (XtraMessageBox.Show("确定要停止测试吗？", "确认停机",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                //任何状态下都可以停机
                _stopCommandSent = true;
                SendStopCommandIfNeeded();
            }
            catch (Exception ex)
            {
                LogService.Log($"停机操作失败: {ex.Message}");
                XtraMessageBox.Show($"停机操作失败: {ex.Message}");
            }
        }

        public async void Stop(object? sender, EventArgs e)
        {
            try
            {
                //任何状态下都可以停机
                _stopCommandSent = true;
                SendStopCommandIfNeeded();
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

            // 清除软件保护故障
            ClearSoftwareProtectionFaults();

            // 清除故障显示
            //lock (_faultQueueLock)
            //{
            //    _activeFaults.Clear();
            //    _faultDisplayQueue.Clear();
            //}

            softwareprotection = false; // 重置软件保护标志

            XtraMessageBox.Show("清除故障成功");
            LogService.Log("清除故障成功");
        }

        /// <summary>
        /// 清除软件保护故障（由CheckCriticalSignals方法检测的故障）
        /// </summary>
        private void ClearSoftwareProtectionFaults()
        {
            try
            {
                lock (_faultQueueLock)
                {
                    // 移除所有以"软件_"开头的故障
                    var softwareFaults = _activeFaults.Where(kv => kv.Key.StartsWith("软件_")).ToList();
                    foreach (var fault in softwareFaults)
                    {
                        _activeFaults.Remove(fault.Key);
                        LogService.Log($"清除软件保护故障: {fault.Key}");
                    }

                    // 重建显示队列，确保只包含当前活跃故障
                    RebuildFaultDisplayQueue();
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"清除软件保护故障时出错: {ex.Message}");
            }
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
        /// 风扇启动（开始120秒转/停循环）
        /// </summary>
        private void Gavoltage(object? sender, EventArgs e)
        {
            lock (_fanLock)
            {
                if (_fanCycleActive)
                {
                    // 如果循环已激活，先停止当前循环再重新开始
                    StopFanCycle();
                }

                // 开始新的循环
                _fanCycleActive = true;
                _fanIsRunning = true;
                SendFanCommand(true); // 发送启动指令

                // 设置120秒后切换状态
                _fanTimer?.Dispose();
                _fanTimer = new System.Threading.Timer(FanTimerCallback, null, 120000, Timeout.Infinite);
            }
        }

        /// <summary>
        /// 停止风扇循环
        /// </summary>
        private void FanStop(object? sender, EventArgs e)
        {
            lock (_fanLock)
            {
                StopFanCycle();
            }
        }

        /// <summary>
        /// 内部停止循环逻辑
        /// </summary>
        private void StopFanCycle()
        {
            if (_fanCycleActive)
            {
                _fanCycleActive = false;
                _fanTimer?.Dispose();
                _fanTimer = null;
                SendFanCommand(false); // 确保风扇停止
                LogService.Log("风扇循环已停止");
            }
        }

        /// <summary>
        /// 风扇定时器回调，每120秒切换一次状态
        /// </summary>
        private void FanTimerCallback(object? state)
        {
            lock (_fanLock)
            {
                if (!_fanCycleActive) return;

                // 切换运行状态
                _fanIsRunning = !_fanIsRunning;
                SendFanCommand(_fanIsRunning);

                // 重新设置定时器
                _fanTimer?.Dispose();
                _fanTimer = new System.Threading.Timer(FanTimerCallback, null, 120000, Timeout.Infinite);
            }
        }

        /// <summary>
        /// 发送风扇控制指令
        /// </summary>
        /// <param name="start">true=启动(0xF0,0x08)，false=停止(0xF0,0x00)</param>
        private void SendFanCommand(bool start)
        {
            byte[] data = new byte[8];
            data[0] = 0xF0;
            data[1] = start ? (byte)0x08 : (byte)0x00;
            // 其余字节默认为0

            CANManager.Instance.SendCommand(
                _equipment.DeviceIndex,
                _equipment.CanIndex,
                _fanCanId,
                data
            );

            LogService.Log($"发送风扇指令: {(start ? "启动" : "停止")}");
        }

        #endregion

        // ==================== 实时数据保存方法 ====================
        #region 数据保存
        /// <summary>
        /// 开始实时数据保存
        /// </summary>
        private void StartRealTimeSave()
        {
            try
            {
                // 弹出保存文件对话框
                using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.Filter = "CSV文件 (*.csv)|*.csv";
                    saveFileDialog.Title = "选择实时数据保存路径";

                    // 使用设备目录作为初始目录
                    //saveFileDialog.InitialDirectory = _deviceSaveDirectory;
                    saveFileDialog.FileName = GenerateRealtimeFileName();

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        string filePath = saveFileDialog.FileName;

                        // 初始化设备文件写入器
                        InitializeDeviceFileWriter(filePath);

                        // 启动数据处理任务
                        StartRealTimeDataProcessing();

                        //LogService.Log($"开始实时数据保存: {filePath}");
                        XtraMessageBox.Show($"开始实时数据保存到: {Path.GetFileName(filePath)}", "实时保存",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);

                        _isRealTimeSaving = true;

                        LogService.Log($"开始实时数据保存: {_title} -> {filePath}");
                    }
                    else
                    {
                        // 用户取消选择文件，取消复选框勾选
                        _chkRealTimeSave.Checked = false;
                        _isRealTimeSaving = false;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"开始实时数据保存失败: {ex.Message}");
                XtraMessageBox.Show($"开始实时数据保存失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _chkRealTimeSave.Checked = false;
                _isRealTimeSaving = false;
            }
        }

        /// <summary>
        /// 启动实时数据处理任务
        /// </summary>
        private void StartRealTimeDataProcessing()
        {
            try
            {
                // 如果已有处理任务，先停止
                if (_realtimeDataCancellationTokenSource != null)
                {
                    _realtimeDataCancellationTokenSource.Cancel();
                    _realtimeDataEvent.Set();

                    if (_realtimeDataProcessingTask != null)
                    {
                        _realtimeDataProcessingTask.Wait(3000);
                        _realtimeDataProcessingTask.Dispose();
                    }
                    _realtimeDataCancellationTokenSource.Dispose();
                }

                // 创建新的处理任务
                _realtimeDataCancellationTokenSource = new CancellationTokenSource();
                _realtimeDataProcessingTask = Task.Run(() => ProcessRealTimeDataQueue(_realtimeDataCancellationTokenSource.Token));

                //LogService.Log("实时数据处理任务已启动");
            }
            catch (Exception ex)
            {
                LogService.Log($"启动实时数据处理任务失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理实时数据队列
        /// </summary>
        private void ProcessRealTimeDataQueue(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 等待数据到达或取消请求
                    if (_realtimeDataEvent.WaitOne(1000)) // 1秒超时，定期检查取消令牌
                    {
                        // 处理队列中的所有数据
                        while (_realtimeDataQueue.TryDequeue(out var dataItem))
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            SaveSignalData(dataItem.Frame, dataItem.SignalValues);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 任务被取消，正常退出
                    break;
                }
                catch (Exception ex)
                {
                    LogService.Log($"处理实时数据队列时出错: {ex.Message}");
                    // 短暂延迟后继续处理
                    Thread.Sleep(100);
                }
            }

            // 任务取消后，处理队列中剩余的数据
            //LogService.Log("实时数据处理任务结束，处理剩余数据...");
            while (_realtimeDataQueue.TryDequeue(out var dataItem))
            {
                SaveSignalData(dataItem.Frame, dataItem.SignalValues);
            }
            //LogService.Log("实时数据处理任务已完全停止");
        }

        /// <summary>
        /// 初始化设备文件写入器
        /// </summary>
        private void InitializeDeviceFileWriter(string filePath)
        {
            lock (_deviceFileLocks[_deviceKey])
            {
                try
                {
                    // 如果设备文件写入器已存在，先关闭
                    if (_deviceFileWriters.TryGetValue(_deviceKey, out var existingWriter))
                    {
                        try
                        {
                            existingWriter?.Close();
                            existingWriter?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"关闭现有文件写入器时出错: {ex.Message}");
                        }
                    }

                    // 创建新的文件写入器
                    var writer = new StreamWriter(filePath, true, System.Text.Encoding.UTF8)
                    {
                        AutoFlush = true // 设置自动刷新，确保数据及时写入
                    };
                    _deviceFileWriters[_deviceKey] = writer;

                    // 重置表头写入状态
                    _deviceHeaderWritten[_deviceKey] = false;

                    // 记录当前文件信息
                    _currentRealtimeFilePath = filePath;
                    _currentRealtimeFileSize = new FileInfo(filePath).Length;
                    _currentRealtimeFileCreateTime = DateTime.Now;
                }
                catch (Exception ex)
                {
                    LogService.Log($"初始化设备文件写入器失败: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// 写入CSV文件表头
        /// </summary>
        private void WriteCsvHeader()
        {
            if (!_deviceFileWriters.TryGetValue(_deviceKey, out var writer)) return;

            lock (_deviceFileLocks[_deviceKey])
            {
                try
                {
                    if (!_deviceHeaderWritten[_deviceKey])
                    {
                        // 写入CSV表头
                        string header = "时间戳,设备名称,通道号,CANID,帧类型,帧格式,CAN类型,长度,数据,数据解析";
                        writer.WriteLine(header);
                        writer.Flush();

                        _deviceHeaderWritten[_deviceKey] = true;
                        //LogService.Log($"写入CSV表头: {_deviceKey}");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Log($"写入CSV表头失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 保存信号数据到CSV文件
        /// </summary>
        private void SaveSignalData(CANManager.ZCAN_Receive_Data frame, Dictionary<string, double> signalValue)
        {
            if (!_isRealTimeSaving) return;

            // 在锁外部检查写入器状态，避免死锁
            if (!_deviceFileWriters.TryGetValue(_deviceKey, out var writer) || writer == null)
            {
                LogService.Log("文件写入器不可用，停止保存");
                // 在UI线程中更新状态
                this.BeginInvoke(new Action(() => StopRealTimeSave()));
                return;
            }

            lock (_deviceFileLocks[_deviceKey])
            {
                try
                {
                    // 再次检查写入器状态（在锁内）
                    if (!_deviceFileWriters.TryGetValue(_deviceKey, out writer) || writer == null || writer.BaseStream == null)
                    {
                        LogService.Log("文件写入器在锁内检查不可用，停止保存");
                        this.BeginInvoke(new Action(() => StopRealTimeSave()));
                        return;
                    }

                    // 检查文件大小，如果超过100MB则轮转文件
                    if (_currentRealtimeFileSize > MAX_REALTIME_FILE_SIZE)
                    {
                        RotateRealtimeFile();
                        // 重新获取writer
                        if (!_deviceFileWriters.TryGetValue(_deviceKey, out writer) || writer == null)
                        {
                            LogService.Log("文件轮转后无法获取写入器，停止保存");
                            this.BeginInvoke(new Action(() => StopRealTimeSave()));
                            return;
                        }
                    }

                    // 确保表头已写入
                    if (!_deviceHeaderWritten[_deviceKey])
                    {
                        WriteCsvHeader();
                        // 更新文件大小
                        try
                        {
                            _currentRealtimeFileSize = new FileInfo(_currentRealtimeFilePath).Length;
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"获取文件大小失败: {ex.Message}");
                            _currentRealtimeFileSize = 0;
                        }
                    }

                    // 获取当前时间戳
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss fff");

                    // 根据CAN ID获取通道号
                    string channelNumber = GetChannelNumberByCanId(frame.can_id & 0x1FFFFFFF);

                    // 提取CAN帧的详细信息
                    uint canId = frame.can_id & 0x1FFFFFFF; // 去除扩展位
                    string frameType = GetFrameType(frame.can_id);     // 帧类型
                    string frameFormat = GetFrameFormat(frame.can_id); // 帧格式
                    //string canType = GetCanType(frame.can_id); // CAN类型
                    int dataLength = frame.can_dlc; // 数据长度
                    string dataHex = BitConverter.ToString(frame.data.Take(dataLength).ToArray()).Replace("-", " "); // 数据字节的十六进制表示
                    string analyzedata = string.Join("; ", signalValue.Select(kv => $"{kv.Key}={kv.Value}"));

                    // 构建数据行
                    var dataRow = new List<string>
                    {
                        timestamp, // 时间戳
                        _equipment.DeviceName, // 设备名称
                        channelNumber, // 通道号
                        $"0x{canId.ToString("X")}", //CANID
                        frameType,     //帧类型
                        frameFormat,   //帧格式
                        "CAN",         //CAN类型
                        dataLength.ToString(),//长度
                        dataHex,        //数据
                        analyzedata
                    };

                    // 写入CSV行
                    string csvLine = string.Join(",", dataRow);
                    long lineLength = System.Text.Encoding.UTF8.GetByteCount(csvLine + Environment.NewLine);

                    // 检查写入器状态
                    if (writer != null && writer.BaseStream != null && writer.BaseStream.CanWrite)
                    {
                        writer.WriteLine(csvLine);
                        writer.Flush(); // 立即刷新缓冲区，确保数据写入磁盘

                        // 更新当前文件大小
                        _currentRealtimeFileSize += lineLength;
                    }
                    else
                    {
                        LogService.Log("写入器状态异常，停止保存");
                        this.BeginInvoke(new Action(() => StopRealTimeSave()));
                    }
                }
                catch (ObjectDisposedException ex)
                {
                    LogService.Log($"写入器已被释放: {ex.Message}");
                    this.BeginInvoke(new Action(() => StopRealTimeSave()));
                }
                catch (Exception ex)
                {
                    LogService.Log($"保存信号数据失败: {ex.Message}");
                    // 保存失败时停止保存
                    this.BeginInvoke(new Action(() => StopRealTimeSave()));
                }
            }
        }

        /// <summary>
        /// 生成实时数据文件名
        /// </summary>
        private string GenerateRealtimeFileName()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return $"实时数据_{_equipment.DeviceName}_{timestamp}.csv";
        }

        /// <summary>
        /// 轮转实时数据文件
        /// </summary>
        private void RotateRealtimeFile()
        {
            lock (_deviceFileLocks[_deviceKey])
            {
                try
                {
                    if (_deviceFileWriters.TryGetValue(_deviceKey, out var oldWriter))
                    {
                        // 记录旧文件信息
                        string oldFilePath = _currentRealtimeFilePath;
                        long oldFileSize = _currentRealtimeFileSize;

                        // 生成新文件名
                        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        string newFileName = GenerateRealtimeFileName();
                        string newFilePath = Path.Combine(desktopPath, newFileName);

                        // 先创建新文件写入器
                        var newWriter = new StreamWriter(newFilePath, true, System.Text.Encoding.UTF8)
                        {
                            AutoFlush = true
                        };

                        // 更新字典中的写入器引用
                        _deviceFileWriters[_deviceKey] = newWriter;

                        // 然后安全关闭旧写入器
                        try
                        {
                            if (oldWriter != null)
                            {
                                oldWriter.Flush();
                                oldWriter.Close();
                                oldWriter.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"关闭旧文件写入器时出错: {ex.Message}");
                        }

                        // 删除旧文件（确保只保留一个文件）
                        try
                        {
                            if (File.Exists(oldFilePath))
                            {
                                File.Delete(oldFilePath);
                                LogService.Log($"删除旧文件: {oldFilePath} ({oldFileSize / 1024 / 1024}MB)");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"删除旧文件失败: {ex.Message}");
                        }

                        // 重置表头写入状态
                        _deviceHeaderWritten[_deviceKey] = false;

                        // 更新当前文件信息
                        _currentRealtimeFilePath = newFilePath;
                        _currentRealtimeFileSize = 0;
                        _currentRealtimeFileCreateTime = DateTime.Now;

                        LogService.Log($"实时数据文件轮转完成: 删除旧文件({oldFileSize / 1024 / 1024}MB)，创建新文件: {newFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Log($"轮转实时数据文件失败: {ex.Message}");
                    StopRealTimeSave();
                }
            }
        }

        /// <summary>
        /// 定期检查基础文件夹大小
        /// </summary>
        private void CheckBaseFolderPeriodically()
        {
            // 在后台线程中检查，避免阻塞数据保存
            Task.Run(() =>
            {
                try
                {
                    long currentSize = CalculateFolderSize(_baseSaveDirectory);
                    if (currentSize > MAX_BASE_FOLDER_SIZE)
                    {
                        LogService.Log("检测到RealTimeData文件夹超过2000MB，开始清理...");
                        CleanOldFiles(_baseSaveDirectory);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Log($"定期检查基础文件夹大小时出错: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 获取帧类型
        /// </summary>
        private string GetFrameType(uint canId)
        {
            if ((canId & 0x80000000) != 0)
                return "扩展帧";
            else
                return "标准帧";
        }

        /// <summary>
        /// 获取帧格式
        /// </summary>
        private string GetFrameFormat(uint canId)
        {
            if ((canId & 0x40000000) != 0)
                return "远程帧";
            else
                return "数据帧";
        }

        /// <summary>
        /// 根据CAN ID获取通道号
        /// CAN ID最后两位:
        ///   A0 -> AC1, A1 -> AC2
        ///   20 -> DC1, 21 -> DC2
        /// </summary>
        private string GetChannelNumberByCanId(uint canId)
        {
            try
            {
                // 获取CAN ID的最后两位（十六进制）
                byte lastByte = (byte)(canId & 0xFF);
                string lastTwoHex = lastByte.ToString("X2");

                switch (lastTwoHex)
                {
                    case "A0": return "AC1";
                    case "A1": return "AC2";
                    case "20": return "DC1";
                    case "21": return "DC2";
                    default:
                        // 如果不在预定义列表中，尝试从CAN ID推断
                        if (lastByte >= 0xA0 && lastByte <= 0xAF)
                            return $"AC{lastByte - 0xA0 + 1}";
                        else if (lastByte >= 0x20 && lastByte <= 0x2F)
                            return $"DC{lastByte - 0x20 + 1}";
                        else
                            return $"Unknown_{lastTwoHex}";
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"根据CAN ID获取通道号失败: {ex.Message}, CAN ID: 0x{canId:X}");
                return "Unknown";
            }
        }

        /// <summary>
        /// 停止实时数据保存
        /// </summary>
        private void StopRealTimeSave()
        {
            try
            {
                _isRealTimeSaving = false;

                // 停止数据处理任务
                if (_realtimeDataCancellationTokenSource != null)
                {
                    _realtimeDataCancellationTokenSource.Cancel();
                    _realtimeDataEvent.Set(); // 唤醒处理线程以便它能够退出

                    // 等待处理任务完成（最多等待5秒）
                    if (_realtimeDataProcessingTask != null && !_realtimeDataProcessingTask.Wait(5000))
                    {
                        LogService.Log("实时数据处理任务停止超时");
                    }

                    _realtimeDataCancellationTokenSource.Dispose();
                    _realtimeDataCancellationTokenSource = null;
                    _realtimeDataProcessingTask = null;
                }

                // 更新UI状态
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        if (_chkRealTimeSave != null && !_chkRealTimeSave.IsDisposed)
                            _chkRealTimeSave.Checked = false;
                    }));
                }
                else
                {
                    if (_chkRealTimeSave != null && !_chkRealTimeSave.IsDisposed)
                        _chkRealTimeSave.Checked = false;
                }

                // 立即关闭文件写入器
                CloseDeviceFileWriterImmediately();

                // 重置文件大小信息
                _currentRealtimeFileSize = 0;
                _currentRealtimeFilePath = null;

                LogService.Log($"停止实时数据保存: {_title}");
            }
            catch (Exception ex)
            {
                LogService.Log($"停止实时数据保存时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 立即关闭设备文件写入器
        /// </summary>
        private void CloseDeviceFileWriterImmediately()
        {
            lock (_deviceFileLocks[_deviceKey])
            {
                try
                {
                    if (_deviceFileWriters.TryRemove(_deviceKey, out var writer))
                    {
                        try
                        {
                            if (writer != null)
                            {
                                writer.Flush();
                                writer.Close();
                                writer.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"关闭文件写入器时出错: {ex.Message}");
                        }
                        //LogService.Log($"立即关闭设备文件写入器: {_deviceKey}");
                    }

                    _deviceHeaderWritten.TryRemove(_deviceKey, out _);
                    _deviceSaveStatus.TryRemove(_deviceKey, out _);
                }
                catch (Exception ex)
                {
                    LogService.Log($"立即关闭设备文件写入器时出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 关闭设备文件写入器
        /// </summary>
        private void CloseDeviceFileWriter()
        {
            lock (_deviceFileLocks[_deviceKey])
            {
                try
                {
                    // 检查是否还有其他模块在使用该设备文件
                    var otherModules = FindOtherModulesInSameDevice();
                    bool otherModuleSaving = otherModules.Any(m => m != this && m._isRealTimeSaving);

                    if (!otherModuleSaving)
                    {
                        if (_deviceFileWriters.TryRemove(_deviceKey, out var writer))
                        {
                            writer?.Close();
                            writer?.Dispose();
                            LogService.Log($"关闭设备文件写入器: {_deviceKey}");
                        }

                        _deviceHeaderWritten.TryRemove(_deviceKey, out _);
                        _deviceSaveStatus.TryRemove(_deviceKey, out _);
                    }
                    else
                    {
                        LogService.Log($"设备 {_deviceKey} 仍有其他模块在保存数据，不关闭文件写入器");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Log($"关闭设备文件写入器时出错: {ex.Message}");
                }
            }
        }

        #endregion

        #region 启动数据保存方法

        /// <summary>
        /// 开始启动数据保存
        /// </summary>
        private void StartStartupDataSave()
        {
            try
            {
                lock (_deviceFileLocks[_deviceKey])
                {
                    if (_isStartupSaving)
                    {
                        // 如果已经在保存，先停止之前的保存
                        StopStartupDataSave();
                    }

                    // 创建日期子文件夹
                    string dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
                    string dateSaveDirectory = Path.Combine(_baseSaveDirectory, dateFolder);
                    if (!Directory.Exists(dateSaveDirectory))
                    {
                        Directory.CreateDirectory(dateSaveDirectory);
                    }

                    // 生成启动数据文件名
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string fileName = $"启动数据_{_equipment.DeviceName}_{timestamp}.csv";
                    _startupFilePath = Path.Combine(dateSaveDirectory, fileName);

                    // 创建文件写入器
                    _startupFileWriter = new StreamWriter(_startupFilePath, true, System.Text.Encoding.UTF8)
                    {
                        AutoFlush = true
                    };

                    // 写入CSV表头
                    WriteStartupDataHeader();

                    // 检查基础文件夹大小
                    CheckAndCleanBaseFolder();

                    // 启动数据处理任务
                    StartStartupDataProcessing();

                    _isStartupSaving = true;
                    _startupStartTime = DateTime.Now;
                    LogService.Log($"启动数据保存开始: {_startupFilePath}");
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"启动数据保存失败: {ex.Message}");
                _isStartupSaving = false;
            }
        }

        /// <summary>
        /// 启动启动数据处理任务
        /// </summary>
        private void StartStartupDataProcessing()
        {
            _startupDataCancellationTokenSource = new CancellationTokenSource();
            _startupDataProcessingTask = Task.Run(() => ProcessStartupDataQueue(_startupDataCancellationTokenSource.Token));
            //LogService.Log("启动数据处理任务已启动");
        }

        /// <summary>
        /// 处理启动数据队列
        /// </summary>
        private void ProcessStartupDataQueue(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 等待数据到达或取消请求
                    if (_startupDataEvent.WaitOne(1000)) // 1秒超时，定期检查取消令牌
                    {
                        // 处理队列中的所有数据
                        while (_startupDataQueue.TryDequeue(out var dataItem))
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            SaveStartupDataToFile(dataItem);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 任务被取消，正常退出
                    break;
                }
                catch (Exception ex)
                {
                    LogService.Log($"处理启动数据队列时出错: {ex.Message}");
                    // 短暂延迟后继续处理
                    Thread.Sleep(100);
                }
            }

            // 任务取消后，处理队列中剩余的数据
            //LogService.Log("启动数据处理任务结束，处理剩余数据...");
            while (_startupDataQueue.TryDequeue(out var dataItem))
            {
                SaveStartupDataToFile(dataItem);
            }
            //LogService.Log("启动数据处理任务已完全停止");
        }

        /// <summary>
        /// 将启动数据保存到文件
        /// </summary>
        private void SaveStartupDataToFile(StartupDataItem dataItem)
        {
            if (!_isStartupSaving || _startupFileWriter == null) return;

            lock (_deviceFileLocks[_deviceKey])
            {
                try
                {
                    // 计算运行时间
                    TimeSpan runTime = dataItem.Timestamp - _startupStartTime;
                    string runTimeStr = runTime.TotalSeconds.ToString("F3");

                    // 获取时间戳
                    string timestamp = dataItem.Timestamp.ToString("yyyy-MM-dd HH:mm:ss fff");

                    // 根据CAN ID获取通道号
                    string channelNumber = GetChannelNumberByCanId(dataItem.Frame.can_id & 0x1FFFFFFF);

                    // 提取CAN帧的详细信息
                    uint canId = dataItem.Frame.can_id & 0x1FFFFFFF;
                    string frameType = GetFrameType(dataItem.Frame.can_id);
                    string frameFormat = GetFrameFormat(dataItem.Frame.can_id);
                    int dataLength = dataItem.Frame.can_dlc;
                    string dataHex = BitConverter.ToString(dataItem.Frame.data.Take(dataLength).ToArray()).Replace("-", " ");

                    // 构建数据解析字符串
                    string analyzedata = string.Join("; ", dataItem.SignalValues.Select(kv => $"{kv.Key}={kv.Value}"));

                    // 构建数据行
                    var dataRow = new List<string>
                    {
                        timestamp,
                        runTimeStr,
                        _equipment.DeviceName,
                        channelNumber,
                        $"0x{canId:X}",
                        frameType,
                        frameFormat,
                        dataLength.ToString(),
                        dataHex,
                        analyzedata
                    };

                    // 写入CSV行
                    string csvLine = string.Join(",", dataRow);
                    _startupFileWriter.WriteLine(csvLine);
                    _startupFileWriter.Flush();

                    // 定期检查基础文件夹大小
                    CheckBaseFolderPeriodically();
                }
                catch (Exception ex)
                {
                    LogService.Log($"保存启动数据到文件失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 写入启动数据表头
        /// </summary>
        private void WriteStartupDataHeader()
        {
            try
            {
                string header = "时间戳,运行时间(秒),设备名称,通道号,CANID,帧类型,帧格式,长度,数据,数据解析";
                _startupFileWriter.WriteLine(header);
                _startupFileWriter.Flush();
            }
            catch (Exception ex)
            {
                LogService.Log($"写入启动数据表头失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存启动数据
        /// </summary>
        private void SaveStartupData(CANManager.ZCAN_Receive_Data frame, Dictionary<string, double> signalValue)
        {
            if (!_isStartupSaving || _startupFileWriter == null) return;

            lock (_deviceFileLocks[_deviceKey])
            {
                try
                {
                    // 计算运行时间
                    TimeSpan runTime = DateTime.Now - _startupStartTime;
                    string runTimeStr = runTime.TotalSeconds.ToString("F3");

                    // 获取当前时间戳
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss fff");

                    // 根据CAN ID获取通道号
                    string channelNumber = GetChannelNumberByCanId(frame.can_id & 0x1FFFFFFF);

                    // 提取CAN帧的详细信息
                    uint canId = frame.can_id & 0x1FFFFFFF;
                    string frameType = GetFrameType(frame.can_id);
                    string frameFormat = GetFrameFormat(frame.can_id);
                    int dataLength = frame.can_dlc;
                    string dataHex = BitConverter.ToString(frame.data.Take(dataLength).ToArray()).Replace("-", " ");

                    // 构建数据解析字符串
                    string analyzedata = string.Join("; ", signalValue.Select(kv => $"{kv.Key}={kv.Value}"));

                    // 构建数据行
                    var dataRow = new List<string>
                    {
                        timestamp,
                        runTimeStr,
                        _equipment.DeviceName,
                        channelNumber,
                        $"0x{canId:X}",
                        frameType,
                        frameFormat,
                        dataLength.ToString(),
                        dataHex,
                        analyzedata
                    };

                    // 写入CSV行
                    string csvLine = string.Join(",", dataRow);
                    _startupFileWriter.WriteLine(csvLine);
                    _startupFileWriter.Flush();

                    // 定期检查基础文件夹大小（在后台线程中）
                    CheckBaseFolderPeriodically();
                }
                catch (Exception ex)
                {
                    LogService.Log($"保存启动数据失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 停止启动数据保存
        /// </summary>
        private void StopStartupDataSave()
        {
            try
            {
                lock (_deviceFileLocks[_deviceKey])
                {
                    Thread.Sleep(100);

                    // 停止数据处理任务
                    if (_startupDataCancellationTokenSource != null)
                    {
                        _startupDataCancellationTokenSource.Cancel();
                        _startupDataEvent.Set(); // 唤醒处理线程以便它能够退出

                        // 等待处理任务完成（最多等待5秒）
                        if (_startupDataProcessingTask != null && !_startupDataProcessingTask.Wait(5000))
                        {
                            LogService.Log("启动数据处理任务停止超时");
                        }

                        _startupDataCancellationTokenSource.Dispose();
                        _startupDataCancellationTokenSource = null;
                        _startupDataProcessingTask = null;
                    }

                    // 关闭文件写入器
                    if (_isStartupSaving && _startupFileWriter != null)
                    {
                        _startupFileWriter.Close();
                        _startupFileWriter.Dispose();
                        _startupFileWriter = null;

                        // 记录保存信息
                        TimeSpan saveDuration = DateTime.Now - _startupStartTime;
                        LogService.Log($"启动数据保存结束: {_startupFilePath}, 持续时间: {saveDuration.TotalSeconds:F1}秒, 文件大小: {new FileInfo(_startupFilePath).Length}字节");
                    }

                    _isStartupSaving = false;
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"停止启动数据保存时出错: {ex.Message}");
                _isStartupSaving = false;
            }
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
            if (_disposed) return;
            _disposed = true;

            try
            {
                _chkSelectModule?.Dispose();

                // 停止实时数据保存
                StopRealTimeSave();

                // 停止启动数据保存
                //StopStartupDataSave();

                // 确保文件流完全关闭
                CloseDeviceFileWriterImmediately();

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

                // 新增：关闭AC参数设置窗体
                if (_acParamForm != null && !_acParamForm.IsDisposed)
                {
                    _acParamForm.Close();
                    _acParamForm.Dispose();
                }

                // 停止并释放UI更新定时器
                _uiUpdateTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _uiUpdateTimer?.Dispose();

                // 释放读取故障定时器
                _readFaultTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _readFaultTimer?.Dispose();

                StopFanCycle();
                _fanTimer?.Dispose();

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
    /// 自定义GroupControl，支持在标题栏添加控件
    /// </summary>
    public class CustomGroupControl : GroupControl
    {
        private List<Control> _titleControls = new List<Control>();

        /// <summary>
        /// 添加控件到标题栏
        /// </summary>
        public void AddControlToTitle(Control control)
        {
            if (control == null) return;

            _titleControls.Add(control);
            this.Controls.Add(control);
            control.BringToFront();

            // 设置初始位置
            UpdateTitleControlPositions();

            // 订阅尺寸变化事件
            this.SizeChanged += (s, e) => UpdateTitleControlPositions();
            this.TextChanged += (s, e) => UpdateTitleControlPositions();
        }

        /// <summary>
        /// 更新标题栏控件位置
        /// </summary>
        private void UpdateTitleControlPositions()
        {
            if (_titleControls.Count == 0) return;

            // 计算标题文本的宽度
            using (Graphics g = this.CreateGraphics())
            {
                // 标题栏高度
                int titleHeight = this.AppearanceCaption.Font.Height + 10;

                // 从右向左排列控件
                int rightMargin = 10;
                for (int i = _titleControls.Count - 1; i >= 0; i--)
                {
                    var control = _titleControls[i];
                    if (control.Visible)
                    {
                        control.Location = new Point(
                            this.Width - rightMargin - control.Width,
                            (titleHeight - control.Height) / 2
                        );
                        rightMargin += control.Width + 5;
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UpdateTitleControlPositions();
        }
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
