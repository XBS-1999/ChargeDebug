using ChargeDebug.Service;
using DataModel;
using DevExpress.XtraEditors;
using DevExpress.XtraLayout;
using DevExpress.XtraLayout.Utils;
using System.Data.SQLite;

namespace ChargeDebug.Form
{
    public partial class Surveillance : XtraUserControl
    {
        private LayoutControl layoutControl;
        private LayoutControlGroup rootGroup;
        private LayoutControlGroup horizontalGroup;
        private EmptySpaceItem leftSpaceItem;
        private EmptySpaceItem middleSpaceItem;
        private EmptySpaceItem topSpaceItem;

        private System.Threading.Timer _sendTimer;
        private List<EquipmentModel> surequipmentList = new List<EquipmentModel>();
        // 添加设备定时器字典
        private readonly Dictionary<int, System.Threading.Timer> _deviceTimers = 
            new Dictionary<int, System.Threading.Timer>();
        private static uint dcnumber = 0;        //总DC通道数
        private string dbcPath = "";
        // 在 Surveillance 类中
        private bool _enabled = true;

        // 存储创建的模块引用
        private readonly List<Module> _modules = new List<Module>();

        public Surveillance(string dbPath, List<EquipmentModel> equipmentList)
        {
            
            dbcPath = dbPath;
            surequipmentList = equipmentList;
            InitializeComponent();
            
            //InitializeUI();
            // 修改Load事件处理
            this.Load += (s, e) => AddDynamicUserControls();

        }

        private void InitializeUI()
        {
            layoutControl = new LayoutControl();
            layoutControl.Dock = DockStyle.Fill;
            layoutControl.AllowCustomization = false;
            this.Controls.Add(layoutControl);
            
            //主组：垂直布局
            rootGroup = new LayoutControlGroup
            {
                GroupBordersVisible = false,
                TextVisible = false,
                DefaultLayoutType = LayoutType.Vertical, //垂直排列
            };
            layoutControl.Root = rootGroup;

            //顶部空白
            topSpaceItem = new EmptySpaceItem
            {
                SizeConstraintsType = SizeConstraintsType.Custom,
                MaxSize = new Size(0, 70),
                MinSize = new Size(0, 70)
            };
            rootGroup.Add(topSpaceItem);

            //水平组
            horizontalGroup = new LayoutControlGroup
            {
                GroupBordersVisible = false,
                TextVisible = false,
                DefaultLayoutType = LayoutType.Horizontal,
            };
            rootGroup.Add(horizontalGroup);
        }

        private void DeviceConfig()
        {
            dcnumber = 0;  //清空DC通道
            // 遍历所有设备进行统计
            foreach (var equipment in surequipmentList)
            {
                dcnumber += (uint)equipment.DCNumber;
            }
        }

        // 新增更新方法
        public void UpdateDcNumber(List<EquipmentModel> equipmentList)
        {
            // 释放所有旧的 Module 控件资源
            DisposeOldModules();
            surequipmentList = equipmentList;
            //清除所有旧布局
            this.Controls.Clear();
            AddDynamicUserControls();
        }

        // 新增方法：释放旧的 Module 资源
        private void DisposeOldModules()
        {
            // 先释放所有旧模块
            foreach (var module in _modules)
            {
                module.Dispose(); // 这会调用我们修改后的Dispose方法
            }
            _modules.Clear();
        }

        // 辅助方法：递归释放布局组中的项目
        //private void DisposeGroupItems(LayoutControlGroup group)
        //{
        //    foreach (var item in group.Items.ToArray())
        //    {
        //        if (item is LayoutControlItem layoutItem && layoutItem.Control is Module module)
        //        {
        //            module.Dispose();
        //            group.Remove(item);
        //            item.Dispose();
        //        }
        //        else if (item is LayoutControlGroup subGroup)
        //        {
        //            DisposeGroupItems(subGroup);
        //        }
        //    }
        //}

        // 辅助方法：克隆信号对象
        private SignalInfo CloneSignal(SignalInfo original)
        {
            return new SignalInfo
            {
                SystemName = original.SystemName,
                Unit = original.Unit,
                StartBit = original.StartBit,
                Length = original.Length,
                ByteOrder = original.ByteOrder,
                Signed = original.Signed,
                Factor = original.Factor,
                Offset = original.Offset,
                MinMax = original.MinMax,
                CANID = original.CANID, // 注意：CANID将在调用处修改
                ReuseSignals = original.ReuseSignals
            };
        }

        private void AddDynamicUserControls()
        {
            DeviceConfig();
            //重新布局
            InitializeUI();
            int totalWidth = 0;
            int value = 0;
            if (dcnumber <= 4)
                value = 256 / (int)dcnumber;
            else
                value = 64;

            //模块左边空白
            leftSpaceItem = new EmptySpaceItem
            {
                SizeConstraintsType = SizeConstraintsType.Custom,
                MinSize = new Size(64, 700),
                MaxSize = new Size(64, 700)
            };
            
            //动态添加模块控件
            using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
            {
                conn.Open();
                foreach (var equipment in surequipmentList)
                {
                    CreateDeviceTimers();

                    // 步骤1：获取DbcFileId
                    long dbcFileId = SQLite_Service.GetDbcFileId(conn, equipment.CommunicationProtocols);

                    // 步骤2：获取该DBC文件下的所有消息
                    var messages = SQLite_Service.GetMessagesByDbc(conn, dbcFileId);
                    //List<SignalInfo> allSignals = new List<SignalInfo>();

                    // 步骤3：收集所有信号
                    List<SignalInfo> allSignals = new List<SignalInfo>();
                    foreach (var message in messages)
                    {
                        if (message.CANID != "0x" && !message.MessageName.StartsWith("调试"))
                        {
                            var signals = SQLite_Service.GetSignalsByMessage(conn, message.MessageID);
                            allSignals.AddRange(signals.Where(s => !string.IsNullOrEmpty(s.SystemName)));
                        }
                    }

                    // 获取最大通道数（AC和DC中的较大值）
                    int maxChannels = Math.Max(equipment.ACNumber, equipment.DCNumber);

                    for (int i = 0; i < maxChannels; i++)
                    {
                        List<SignalInfo> channelSignals = new List<SignalInfo>();

                        // 处理DC通道信号（如果存在）
                        if (i < equipment.DCNumber)
                        {
                            foreach (var signal in allSignals)
                            {
                                // 只处理DC相关信号
                                if (signal.CANID.Contains("2X"))
                                {
                                    var newSignal = CloneSignal(signal);
                                    newSignal.CANID = signal.CANID.Replace("2X", "2" + i);
                                    channelSignals.Add(newSignal);
                                }
                            }
                        }

                        // 处理AC通道信号
                        if (i < equipment.ACNumber)
                        {
                            foreach (var signal in allSignals)
                            {
                                // 只处理AC相关信号
                                if (signal.CANID.Contains("AX"))
                                {
                                    var newSignal = CloneSignal(signal);
                                    newSignal.CANID = signal.CANID.Replace("AX", "A" + i);
                                    channelSignals.Add(newSignal);
                                }
                            }
                        }

                        foreach (var signal in allSignals)
                        {
                            // 只处理共用信号相关信号
                            if (!signal.CANID.Contains("AX") && !signal.CANID.Contains("2X"))
                            {
                                channelSignals.Add(CloneSignal(signal));
                            }
                        }
                        

                        // 创建模块（同时包含AC和DC通道）
                        string title = $"{equipment.DeviceNumber}-通道";
                        title += i < equipment.ACNumber ? $"A{i + 1}" : "";
                        title += i < equipment.ACNumber && i < equipment.DCNumber ? "/" : "";
                        title += i < equipment.DCNumber ? $"DC{i + 1}" : "";

                        //var userControl = new Module($"{equipment.DeviceNumber}-通道{i + 1}")
                        // 传递所有必需参数：标题、设备号、通道索引、信号列表
                        var userControl = new Module(title, equipment, channelSignals);
                        _modules.Add(userControl); // 存储引用

                        userControl.Margin = new System.Windows.Forms.Padding(0);
                        userControl.MaximumSize = new Size(400, 700);
                        userControl.MinimumSize = new Size(400, 700);

                        // 关键更新：传递当前DBC文件所有信号
                        //userControl.UpdateSignals(channelSignals);

                        //模块相邻空白
                        middleSpaceItem = new EmptySpaceItem
                        {
                            SizeConstraintsType = SizeConstraintsType.Custom,
                            MinSize = new Size(value, 700),
                            MaxSize = new Size(value, 700)
                        };
                        LayoutControlItem item = new LayoutControlItem
                        {
                            Control = userControl,
                            TextVisible = false,
                            SizeConstraintsType = SizeConstraintsType.Custom,
                            MinSize = new Size(400, 700),
                            MaxSize = new Size(400, 700),
                            Padding = new DevExpress.XtraLayout.Utils.Padding(0)
                        };
                        //换行判断
                        totalWidth += 400;
                        if (totalWidth > 1600)
                        {
                            topSpaceItem = new EmptySpaceItem
                            {
                                //Size = new Size(0, 0),
                                SizeConstraintsType = SizeConstraintsType.Custom,
                                MaxSize = new Size(0, 20),
                                MinSize = new Size(0, 20)
                            };
                            rootGroup.Add(topSpaceItem);

                            horizontalGroup = new LayoutControlGroup
                            {
                                GroupBordersVisible = false,
                                TextVisible = false,
                                DefaultLayoutType = LayoutType.Horizontal
                            };
                            rootGroup.Add(horizontalGroup);
                            totalWidth = 400;
                        }

                        horizontalGroup.Add(leftSpaceItem);
                        horizontalGroup.Add(item);
                        horizontalGroup.Add(middleSpaceItem);

                        //userControl.Start();
                    }
                }

            }
            if (dcnumber <= 4)
            {
                int Width = (int)(dcnumber * (400 + value) - value);
                int remainingWidth = (rootGroup.Width - Width) / 2;
                leftSpaceItem.MinSize = new Size(remainingWidth, 700);
                leftSpaceItem.MaxSize = new Size(remainingWidth, 700);
            }
            layoutControl.BeginUpdate();
            layoutControl.EndUpdate();
        }

        // 添加模块时注册
        public void AddModule(Module module)
        {
            _modules.Add(module);
            this.Controls.Add(module);
        }

        // 设置发送状态
        public void SetModuleSendingEnabled(bool enabled)
        {
            _enabled = enabled;
            foreach (var module in _modules)
            {
                module.ModuleSendingEnabled = enabled;
            }
        }

        // 新增方法：为每个设备创建定时器
        private void CreateDeviceTimers()
        {
            // 移除旧定时器
            foreach (var timer in _deviceTimers.Values)
            {
                timer?.Change(Timeout.Infinite, Timeout.Infinite);
                timer?.Dispose();
            }
            _deviceTimers.Clear();

            // 为每个设备创建新定时器
            foreach (var equipment in surequipmentList)
            {
                var timer = new System.Threading.Timer(SendDevicePeriodicMessage, equipment, 1000, 1000);
                _deviceTimers[equipment.DeviceIndex] = timer;
            }
        }

        // 新增方法：发送设备时间同步帧
        private void SendDevicePeriodicMessage(object state)
        {
            // 检查模块发送状态
            if (!_enabled)
            {
                // 使用日志代替消息框（线程安全）
                //LogService.Log($"设备{((EquipmentModel)state).DeviceNumber}的时间同步发送被阻止");
                return;
            }

            if (state is EquipmentModel equipment)
            {
                byte[] data = new byte[8];
                data[0] = (byte)(int.Parse(DateTime.Now.ToString("yyyy")) - 2000); // 年
                data[1] = (byte)DateTime.Now.Month;   // 月
                data[2] = (byte)DateTime.Now.Day;     // 日
                data[3] = (byte)DateTime.Now.Hour;    // 时
                data[4] = (byte)DateTime.Now.Minute;  // 分
                data[5] = (byte)DateTime.Now.Second;  // 秒

                CANManager.Instance.SendCommand(
                    equipment.DeviceIndex,
                    equipment.CanIndex,
                    0xADCC,
                    data
                );
            }
        }

        // 在 Dispose 中释放定时器
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);

            if (disposing)
            {
                // 释放设备定时器
                foreach (var timer in _deviceTimers.Values)
                {
                    timer?.Change(Timeout.Infinite, Timeout.Infinite);
                    timer?.Dispose();
                }
                _deviceTimers.Clear();

                DisposeOldModules();
            }
            base.Dispose(disposing);
        }
    }
}
