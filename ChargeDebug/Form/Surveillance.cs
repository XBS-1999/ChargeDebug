using ChargeDebug.Service;
using DataModel;
using DevExpress.Utils.Extensions;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout;
using DevExpress.XtraLayout.Utils;
using System.Data.SQLite;

#pragma warning disable
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

        private PanelControl panelControl3;
        private PanelControl panelControl4;
        private PanelControl panelControl5;

        //顶部按钮控件
        private SimpleButton btnStartTest;
        private SimpleButton btnStopTest;
        private LayoutControlGroup btnBarGroup;

        private List<EquipmentModel> surequipmentList = new List<EquipmentModel>();
        // 添加设备定时器字典
        private readonly Dictionary<int, System.Threading.Timer> _deviceTimers =
            new Dictionary<int, System.Threading.Timer>();
        //private static uint dcnumber = 0;        //总DC通道数
        //private static uint acnumber = 0;        //总AC通道数
        private static int channels = 0;        //总通道数
        private string dbcPath = "";
        // 在 Surveillance 类中
        private bool _enabled = true;

        private string _userPermissions;

        // 全局唯一启动窗口实例
        private static StartConfiguration _globalStartConfigForm;

        // 存储创建的模块引用
        private readonly List<Module> _modules = new List<Module>();

        public Surveillance(string dbPath, List<EquipmentModel> equipmentList, string userPermissions)
        {

            dbcPath = dbPath;
            _userPermissions = userPermissions;
            DeviceConfig(equipmentList);
            InitializeComponent();

            //InitializeUI();
            // 修改Load事件处理
            this.Load += (s, e) => AddDynamicUserControls();
        }

        private void InitializeUI()
        {
            panelControl3 = new PanelControl();
            panelControl3.Dock = DockStyle.Fill;
            panelControl3.BorderStyle = BorderStyles.NoBorder;
            panelControl3.Padding = new System.Windows.Forms.Padding(0);
            panelControl3.Margin = new System.Windows.Forms.Padding(0);
            this.Controls.Add(panelControl3);

            panelControl4 = new PanelControl();
            panelControl4.Dock = DockStyle.Fill;
            panelControl4.BorderStyle = BorderStyles.NoBorder;
            panelControl4.Padding = new System.Windows.Forms.Padding(0);
            panelControl4.Margin = new System.Windows.Forms.Padding(0);
            panelControl3.Controls.Add(panelControl4);

            panelControl5 = new PanelControl();
            panelControl5.Dock = DockStyle.Top;
            panelControl5.Height = 50;
            panelControl5.BorderStyle = BorderStyles.NoBorder;
            panelControl5.Padding = new System.Windows.Forms.Padding(0);
            panelControl5.Margin = new System.Windows.Forms.Padding(0);
            panelControl3.Controls.Add(panelControl5);

            btnStartTest = new SimpleButton
            {
                Text = "开始测试",
                Width = 100,
                Height = 30,
                Location = new Point(600, 10),
                Enabled = true
            };
            btnStopTest = new SimpleButton
            {
                Text = "停止测试",
                Width = 100,
                Height = 30,
                Location = new Point(750, 10),
                Enabled = true
            };

            btnStartTest.Click += btnStartTest_Click;
            btnStopTest.Click += btnStopTest_Click;

            panelControl5.Controls.Add(btnStartTest);
            panelControl5.Controls.Add(btnStopTest);

            // 2. 下方布局区域（填满剩余所有空间）
            layoutControl = new LayoutControl();
            layoutControl.Dock = DockStyle.Fill;
            layoutControl.AllowCustomization = false;
            panelControl4.Controls.Add(layoutControl);

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
                MaxSize = new Size(0, 30),
                MinSize = new Size(0, 30)
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

        private async void btnStartTest_Click(object sender, EventArgs e)
        {
            var selectModules = GetAllModuleControls(this).Where(m => m.IsModuleSelected).ToList();
            if (selectModules.Count == 0)
            {
                XtraMessageBox.Show("请先勾选需要启动的模块！");
                return;
            }

            // ========== 只弹出一次全局配置窗口 ==========
            if (_globalStartConfigForm == null || _globalStartConfigForm.IsDisposed)
            {
                _globalStartConfigForm = new StartConfiguration("批量启动配置", "统一参数设置", true);
            }

            if (_globalStartConfigForm.ShowDialog() != DialogResult.OK)
                return;

            // 把参数保存到静态全局变量，所有模块共用
            Module.GlobalProtectionParam = _globalStartConfigForm.Configuration;

            // ========== 循环执行所有选中模块启动 ==========
            foreach (var mod in selectModules)
            {
                if (mod._hasDCChannel)
                {
                    await mod.RunStartWithSharedParam();
                }
                else if (mod._hasACChannel)
                {
                    await mod.RunACStartWithSharedParam();
                }
            }
        }

        private async void btnStopTest_Click(object sender, EventArgs e)
        {
            var selectModules = GetAllModuleControls(this).Where(m => m.IsModuleSelected).ToList();
            if (selectModules.Count == 0)
            {
                XtraMessageBox.Show("未勾选任何模块！");
                return;
            }

            // 全局只弹出一次确认
            if (XtraMessageBox.Show("确定批量停止所有选中设备？", "停机确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            // 批量停机
            foreach (var mod in selectModules)
            {
                mod.Stop(null, EventArgs.Empty);
            }
        }

        private void DeviceConfig(List<EquipmentModel> equipmentList)
        {
            channels = 0;  //先清空通道在计算
            surequipmentList.Clear();
            foreach (var equipmentLists in equipmentList)
            {
                if (equipmentLists.DeviceType == "充放电设备")
                {
                    surequipmentList.Add(equipmentLists);
                    // 获取最大通道数（AC和DC中的较大值）
                    int channel = Math.Max(Convert.ToByte(equipmentLists.ACNumber), Convert.ToByte(equipmentLists.DCNumber));
                    channels += channel;
                }
            }
        }

        // 新增更新方法
        public void UpdateDcNumber(List<EquipmentModel> equipmentList)
        {
            // 停止所有设备定时器
            foreach (var timer in _deviceTimers.Values)
            {
                timer?.Change(Timeout.Infinite, Timeout.Infinite);
                timer?.Dispose();
            }
            _deviceTimers.Clear();

            // 释放所有旧的 Module 控件资源
            DisposeOldModules();

            // 释放布局相关资源
            DisposeLayoutResources();

            DeviceConfig(equipmentList);

            // 清除所有旧控件
            this.Controls.Clear();

            // 重新创建布局和控件
            AddDynamicUserControls();
        }

        private void DisposeLayoutResources()
        {
            try
            {
                if (btnStartTest != null) btnStartTest.Dispose();
                if (btnStopTest != null) btnStopTest.Dispose();
                btnStartTest = null;
                btnStopTest = null;

                // 释放中间空白项
                if (middleSpaceItem != null)
                {
                    middleSpaceItem.Dispose();
                    middleSpaceItem = null;
                }

                // 释放左侧空白项
                if (leftSpaceItem != null)
                {
                    leftSpaceItem.Dispose();
                    leftSpaceItem = null;
                }

                // 释放顶部空白项
                if (topSpaceItem != null)
                {
                    topSpaceItem.Dispose();
                    topSpaceItem = null;
                }

                // 释放水平组及其子项
                if (horizontalGroup != null)
                {
                    // 递归释放水平组中的所有项
                    DisposeLayoutGroupItems(horizontalGroup);
                    horizontalGroup.Dispose();
                    horizontalGroup = null;
                }

                // 释放根组及其子项
                if (rootGroup != null)
                {
                    // 递归释放根组中的所有项
                    DisposeLayoutGroupItems(rootGroup);
                    rootGroup.Dispose();
                    rootGroup = null;
                }

                // 释放布局控件
                if (layoutControl != null)
                {
                    layoutControl.Dispose();
                    layoutControl = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"释放布局资源时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 递归查找所有Module模块
        /// </summary>
        private List<Module> GetAllModuleControls(Control parent)
        {
            var list = new List<Module>();
            foreach (Control c in parent.Controls)
            {
                if (c is Module mod)
                {
                    list.Add(mod);
                }
                else if (c.HasChildren)
                {
                    list.AddRange(GetAllModuleControls(c));
                }
            }
            return list;
        }

        // 递归释放布局组中的项目
        private void DisposeLayoutGroupItems(LayoutControlGroup group)
        {
            if (group?.Items == null) return;

            // 转换为数组避免在枚举时修改集合
            var items = group.Items.ToArray();

            foreach (var item in items)
            {
                try
                {
                    if (item is LayoutControlItem layoutItem)
                    {
                        // 先释放控件
                        if (layoutItem.Control != null)
                        {
                            // 如果是 Module 控件，确保已通过 DisposeOldModules 释放
                            if (layoutItem.Control is Module module && !module.IsDisposed)
                            {
                                module.Dispose();
                            }
                            else
                            {
                                layoutItem.Control.Dispose();
                            }
                        }
                        layoutItem.Dispose();
                    }
                    else if (item is EmptySpaceItem emptySpaceItem)
                    {
                        emptySpaceItem.Dispose();
                    }
                    else if (item is LayoutControlGroup subGroup)
                    {
                        // 递归释放子组
                        DisposeLayoutGroupItems(subGroup);
                        subGroup.Dispose();
                    }
                    else if (item is BaseLayoutItem baseItem)
                    {
                        baseItem.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"释放布局项时出错: {ex.Message}");
                }
            }
            // 清空项目集合
            group.Items.Clear();
        }

        // 新增方法：释放旧的 Module 资源
        private void DisposeOldModules()
        {
            try
            {
                // 先停止所有模块的发送
                foreach (var module in _modules)
                {
                    try
                    {
                        if (module != null && !module.IsDisposed)
                        {
                            module.ModuleSendingEnabled = false;
                            module.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"释放模块时出错: {ex.Message}");
                    }
                }
                _modules.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DisposeOldModules 出错: {ex.Message}");
            }
        }

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
            // 确保之前的资源已清理
            if (layoutControl != null && !layoutControl.IsDisposed)
            {
                this.Controls.Remove(layoutControl);
                layoutControl.Dispose();
                layoutControl = null;
            }

            //DeviceConfig();
            //重新布局
            InitializeUI();
            int totalWidth = 0;
            int value = 0;
            if (channels <= 4 && channels >0)
                value = 256 / channels;
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
                    int maxChannels = Math.Max(Convert.ToByte(equipment.ACNumber), Convert.ToByte(equipment.DCNumber));

                    //获取AC,DC起始地址
                    int acnum = Convert.ToInt32(equipment.ACAddress.Substring(equipment.ACAddress.Length - 1));
                    int dcnum = Convert.ToInt32(equipment.DCAddress.Substring(equipment.DCAddress.Length - 1));

                    for (int i = 0; i < maxChannels; i++)
                    {
                        List<SignalInfo> channelSignals = new List<SignalInfo>();

                        acnum++;
                        dcnum++;

                        // 处理DC通道信号（如果存在）
                        if (i < equipment.DCNumber)
                        {
                            foreach (var signal in allSignals)
                            {
                                // 只处理DC相关信号
                                if (signal.CANID.Contains("2X"))
                                {
                                    var newSignal = CloneSignal(signal);
                                    newSignal.CANID = signal.CANID.Replace("2X", "2" + (dcnum - 1));
                                    channelSignals.Add(newSignal);
                                }
                            }
                        }

                        // 处理AC通道信号
                        if (i < equipment.ACNumber)
                        {
                            //int num = equipment.ACAddress;
                            foreach (var signal in allSignals)
                            {
                                // 只处理AC相关信号
                                if (signal.CANID.Contains("AX"))
                                {
                                    var newSignal = CloneSignal(signal);
                                    newSignal.CANID = signal.CANID.Replace("AX", "A" + (acnum - 1));
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
                        string title = $"{equipment.DeviceName}-通道";
                        title += i < equipment.ACNumber ? $"AC{acnum}" : "";
                        title += i < equipment.ACNumber && i < equipment.DCNumber ? "/" : "";
                        title += i < equipment.DCNumber ? $"DC{dcnum}" : "";

                        //var userControl = new Module($"{equipment.DeviceNumber}-通道{i + 1}")
                        // 传递所有必需参数：标题、设备号、通道索引、信号列表
                        var userControl = new Module(title, equipment, channelSignals, _userPermissions);
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
            if (channels <= 4)
            {
                int Width = (int)(channels * (400 + value) - value);
                int remainingWidth = (rootGroup.Width - Width) / 2;
                leftSpaceItem.MinSize = new Size(remainingWidth, 700);
                leftSpaceItem.MaxSize = new Size(remainingWidth, 700);
            }
            layoutControl.BeginUpdate();
            layoutControl.EndUpdate();
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
                _deviceTimers[Convert.ToByte(equipment.DeviceIndex)] = timer;
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
            if (disposing)
            {
                // 释放设备定时器
                foreach (var timer in _deviceTimers.Values)
                {
                    try
                    {
                        timer?.Change(Timeout.Infinite, Timeout.Infinite);
                        timer?.Dispose();
                    }
                    catch { }
                }
                _deviceTimers.Clear();

                // 释放所有模块
                DisposeOldModules();

                // 释放布局资源
                DisposeLayoutResources();

                // 释放组件
                components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
