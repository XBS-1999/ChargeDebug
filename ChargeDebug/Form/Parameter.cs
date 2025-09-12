using ChargeDebug.Service;
//using CommunicationProtocols;
using DataModel;
using DevExpress.XtraEditors;
using DevExpress.XtraLayout;
using DevExpress.XtraLayout.Utils;
using DevExpress.XtraTab;
using Log;
using System.Data.SQLite;
using System.IO;

namespace ChargeDebug.Form
{
    public partial class Parameter : XtraUserControl
    {
        private LayoutControl layoutControl;
        private LayoutControlGroup rootGroup;
        private LayoutControlItem item;
        private XtraTabControl mainTabControl; // 添加对主TabControl的引用
        private ContextMenuStrip contextMenu; // 新增右键菜单成员

        // 添加工具栏成员变量
        private Panel toolStripPanel;
        private SimpleButton btnExport;
        private SimpleButton btnImport;

        private List<EquipmentModel> parequipmentList = new List<EquipmentModel>();
        private string dbcPath = "";
        // 加载ReuseSignals
        private List<ReuseSignal> reuseSignal = new List<ReuseSignal>();
        // 加载Signals
        private List<SignalInfo> signals = new List<SignalInfo>();
        private long dbcFileId = 0;
        private long messageId = 0;
        private int totalWidth = 0;

        private Dictionary<string, Dictionary<string, string>> exportData = new Dictionary<string, Dictionary<string, string>>();
        private Dictionary<string, Dictionary<string, string>> importData = new Dictionary<string, Dictionary<string, string>>();

        // 在类开头添加 TabControlInfo 内部类
        private class TabPageInfo
        {
            public int? DeviceIndex { get; set; }
            public int? CanIndex { get; set; }
            public string WriteCANID { get; set; }
            public string ReadCANID { get; set; }
            public string ReceiveCANID { get; set; }
            public string DeviceNumber { get; set; }
            //public string Command { get; set; }
            //public List<SignalInfo> Signals { get; set; }
        }
        private class GroupInfo
        {
            public byte Command { get; set; }
            public List<SignalInfo> Signals { get; set; }
            public TabPageInfo TabInfo { get; set; }
            public List<TextEdit> TextEdits { get; set; }
        }

        public Parameter(string dbPath, List<EquipmentModel> equipmentList)
        {
            dbcPath = dbPath;
            DeviceConfig(equipmentList);
            //parequipmentList = equipmentList;
            InitializeComponent();

            InitializeUI();
            InitializeContextMenu(); // 初始化右键菜单
        }

        private void DeviceConfig(List<EquipmentModel> equipmentList)
        {
            parequipmentList.Clear();
            foreach (var equipmentLists in equipmentList)
            {
                if (equipmentLists.DeviceType == "充放电设备")
                {
                    parequipmentList.Add(equipmentLists);
                }
            }
        }

        // ==================== 新增右键菜单初始化 ====================
        private void InitializeContextMenu()
        {
            contextMenu = new ContextMenuStrip();

            // 添加"导入参数"菜单项
            ToolStripMenuItem importItem = new ToolStripMenuItem("导入参数");
            importItem.Click += BtnImport_Click; // 关联点击事件
            contextMenu.Items.Add(importItem);

            // 添加"导出参数"菜单项
            ToolStripMenuItem exportItem = new ToolStripMenuItem("导出参数");
            exportItem.Click += BtnExport_Click; // 关联点击事件
            contextMenu.Items.Add(exportItem);

            // 将右键菜单绑定到UserControl
            this.ContextMenuStrip = contextMenu;
        }

        // ==================== 导出参数到CSV ====================
        private async Task<bool> ExportParametersToCsv(string filePath, XtraTabPage tabPage)
        {
            try
            {
                // 获取指定Tab页中的所有GroupControl
                var groups = GetGroupControlsInTabPage(tabPage);
                exportData.Clear();

                // 遍历所有参数组
                foreach (var group in groups)
                {
                    var groupInfo = group.Tag as GroupInfo;
                    if (groupInfo == null) continue;

                    string groupTitle = group.Text;
                    var groupData = new Dictionary<string, string>();

                    // 重试读取参数（最多3次）
                    bool readSuccess = false;
                    int retryCount = 0;
                    while (!readSuccess && retryCount < 1)
                    {
                        readSuccess = await ReadParameters(group);
                        retryCount++;
                        if (!readSuccess)
                        {
                            LogService.Log($"第{retryCount}次读取{groupTitle}失败，重试中...");
                            await Task.Delay(200); // 延迟200ms后重试
                        }
                    }

                    if (!readSuccess)
                    {
                        LogService.Log($"{groupTitle}读取失败，跳过该组");
                        continue;
                    }

                    // 收集参数值
                    for (int i = 0; i < groupInfo.Signals.Count; i++)
                    {
                        string signalName = groupInfo.Signals[i].SignalName;
                        string value = groupInfo.TextEdits[i].Text;
                        groupData[signalName] = value;
                    }

                    exportData[groupTitle] = groupData;
                }

                // 写入CSV文件
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    // 写入标题行
                    writer.WriteLine("Group,Signal,Value");

                    // 写入数据
                    foreach (var group in exportData)
                    {
                        foreach (var signal in group.Value)
                        {
                            writer.WriteLine($"\"{group.Key}\",\"{signal.Key}\",\"{signal.Value}\"");
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"导出失败: {ex.Message}");
                return false;
            }
        }

        // ==================== 从CSV导入参数 ====================
        private async Task<bool> ImportParametersFromCsv(string filePath, XtraTabPage tabPage)
        {
            try
            {
                importData.Clear();
                string currentGroup = "";

                using (StreamReader reader = new StreamReader(filePath))
                {
                    // 跳过标题行
                    await reader.ReadLineAsync();

                    while (!reader.EndOfStream)
                    {
                        string line = await reader.ReadLineAsync();
                        string[] parts = line.Split(',');

                        if (parts.Length < 3) continue;

                        // 移除引号
                        string group = parts[0].Trim('"');
                        string signal = parts[1].Trim('"');
                        string value = parts[2].Trim('"');

                        // 新组
                        if (!importData.ContainsKey(group))
                        {
                            importData[group] = new Dictionary<string, string>();
                            currentGroup = group;
                        }

                        importData[currentGroup][signal] = value;
                    }
                }

                // 获取指定Tab页中的所有GroupControl
                var groups = GetGroupControlsInTabPage(tabPage);

                // 应用参数值
                foreach (var group in groups)
                {
                    string groupTitle = group.Text;
                    if (!importData.ContainsKey(groupTitle)) continue;

                    var groupInfo = group.Tag as GroupInfo;
                    if (groupInfo == null) continue;

                    // 设置参数值到文本框
                    for (int i = 0; i < groupInfo.Signals.Count; i++)
                    {
                        string signalName = groupInfo.Signals[i].SignalName;
                        if (importData[groupTitle].TryGetValue(signalName, out string value))
                        {
                            if (groupInfo.TextEdits[i].InvokeRequired)
                            {
                                groupInfo.TextEdits[i].Invoke(new Action(() =>
                                    groupInfo.TextEdits[i].Text = value));
                            }
                            else
                            {
                                groupInfo.TextEdits[i].Text = value;
                            }
                        }
                    }
                }

                // 写入所有参数（重试机制）
                bool allSuccess = true;
                foreach (var group in groups)
                {
                    string groupTitle = group.Text;
                    if (!importData.ContainsKey(groupTitle)) continue;

                    bool writeSuccess = false;
                    int retryCount = 0;
                    while (!writeSuccess && retryCount < 1)
                    {
                        writeSuccess = await WriteParameters(group);
                        retryCount++;
                        if (!writeSuccess)
                        {
                            LogService.Log($"第{retryCount}次写入{groupTitle}失败，重试中...");
                            await Task.Delay(200); // 延迟200ms后重试
                        }
                    }

                    if (!writeSuccess)
                    {
                        allSuccess = false;
                        LogService.Log($"{groupTitle}写入失败");
                    }
                }

                return allSuccess;
            }
            catch (Exception ex)
            {
                LogService.Log($"导入失败: {ex.Message}");
                return false;
            }
        }

        // ==================== 获取指定Tab页中的所有参数组 ====================
        private List<GroupControl> GetGroupControlsInTabPage(XtraTabPage tabPage)
        {
            var groups = new List<GroupControl>();

            // 获取布局控件
            var layoutControl = tabPage.Controls.OfType<LayoutControl>().FirstOrDefault();
            if (layoutControl == null) return groups;

            // 获取所有GroupControl
            foreach (Control control in layoutControl.Controls)
            {
                if (control is GroupControl group)
                {
                    groups.Add(group);
                }
            }

            return groups;
        }

        // ==================== 获取所有参数组 ====================
        private List<GroupControl> GetAllGroupControls()
        {
            var groups = new List<GroupControl>();

            // 遍历所有Tab页
            foreach (XtraTabPage tabPage in mainTabControl.TabPages)
            {
                // 获取布局控件
                var layoutControl = tabPage.Controls.OfType<LayoutControl>().FirstOrDefault();
                if (layoutControl == null) continue;

                // 获取所有GroupControl
                foreach (Control control in layoutControl.Controls)
                {
                    if (control is GroupControl group)
                    {
                        groups.Add(group);
                    }
                }
            }

            return groups;
        }

        // ==================== 修改导出按钮事件 ====================
        private async void BtnExport_Click(object? sender, EventArgs e)
        {
            try
            {
                // 获取当前选中的Tab页
                XtraTabPage currentTab = mainTabControl.SelectedTabPage;
                if (currentTab == null)
                {
                    ShowToast("请选择一个通道", Color.Red);
                    return;
                }

                SaveFileDialog saveDialog = new SaveFileDialog();
                saveDialog.Filter = "CSV文件|*.csv|所有文件|*.*";
                saveDialog.Title = "导出参数";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = saveDialog.FileName;
                    // 传递当前选中的Tab页
                    bool success = await ExportParametersToCsv(filePath, currentTab);

                    if (success)
                    {
                        ShowToast("参数导出成功", Color.Green);
                        LogService.Log($"参数已导出到: {filePath}");
                    }
                    else
                    {
                        ShowToast("参数导出失败", Color.Red);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowToast("参数导出失败", Color.Red);
                LogService.Log($"导出失败: {ex.Message}");
            }
        }

        // ==================== 修改导入按钮事件 ====================
        private async void BtnImport_Click(object? sender, EventArgs e)
        {
            try
            {
                // 获取当前选中的Tab页
                XtraTabPage currentTab = mainTabControl.SelectedTabPage;
                if (currentTab == null)
                {
                    ShowToast("请选择一个通道", Color.Red);
                    return;
                }

                OpenFileDialog openDialog = new OpenFileDialog();
                openDialog.Filter = "CSV文件|*.csv|所有文件|*.*";
                openDialog.Title = "导入参数";

                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openDialog.FileName;
                    // 传递当前选中的Tab页
                    bool success = await ImportParametersFromCsv(filePath, currentTab);

                    if (success)
                    {
                        ShowToast("参数导入成功", Color.Green);
                        LogService.Log($"参数已从文件导入: {filePath}");
                    }
                    else
                    {
                        ShowToast("参数导入失败", Color.Red);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowToast("参数导入失败", Color.Red);
                LogService.Log($"导入失败: {ex.Message}");
            }
        }

        private void InitializeUI()
        {
            // ==================== 1. 创建工具栏 ====================
            //Panel toolPanel = new Panel
            //{
            //    Dock = DockStyle.Fill,
            //    BackColor = Color.WhiteSmoke,
            //    Padding = new System.Windows.Forms.Padding(0)
            //};
            //this.Controls.Add(toolPanel);

            // ==================== 2. 创建主Tab控件 ====================
            // ==================== 初始化 mainTabControl ====================
            mainTabControl = new XtraTabControl
            {
                Dock = DockStyle.Fill,
                HeaderLocation = TabHeaderLocation.Top,
                HeaderOrientation = TabOrientation.Horizontal
            };
            this.Controls.Add(mainTabControl);

            using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
            {
                conn.Open();
                foreach (var equipment in parequipmentList)
                {
                    long dbcFileId = SQLite_Service.GetDbcFileId(conn, equipment.CommunicationProtocols);
                    var accanid = SQLite_Service.GetMessagesByDbc(conn, dbcFileId);
                    // 预加载CANID映射
                    var canIds = accanid.GroupBy(msg => msg.MessageName)
                                        .ToDictionary(g => g.Key, g => g.First().CANID);

                    var messageid = accanid.GroupBy(msg => msg.MessageName)
                                        .ToDictionary(g => g.Key, g => g.First().MessageID);

                    // 检查必要的消息名称
                    string[] requiredMessageNames = new string[]
                    {
                        "调试AC写入", "调试AC读取", "调试AC接收",
                        "调试DC写入", "调试DC读取", "调试DC接收"
                    };
                    bool hasAllKeys = true;
                    foreach (var key in requiredMessageNames)
                    {
                        if (!canIds.ContainsKey(key) || !messageid.ContainsKey(key))
                        {
                            LogService.Log($"{equipment.DeviceNumber}的dbc文件中缺少消息'{key}'");
                            hasAllKeys = false;
                        }
                    }
                    if (!hasAllKeys)
                    {
                        continue; // 跳过当前设备，不为其添加AC/DC Tab页
                    }

                    //获取AC,DC起始地址
                    int acnum = Convert.ToInt32(equipment.ACAddress.Substring(equipment.ACAddress.Length - 1));
                    int dcnum = Convert.ToInt32(equipment.DCAddress.Substring(equipment.DCAddress.Length - 1));

                    // AC TabPages
                    for (int b = 0; b < equipment.ACNumber; b++)
                    {
                        acnum ++;
                        totalWidth = 0;
                        var tabPageInfo = new TabPageInfo
                        {
                            WriteCANID = canIds["调试AC写入"].Replace("X", (acnum - 1).ToString()),
                            ReadCANID = canIds["调试AC读取"].Replace("X", (acnum - 1).ToString()),
                            ReceiveCANID = canIds["调试AC接收"].Replace("X", (acnum - 1).ToString()),
                            DeviceIndex = equipment.DeviceIndex,
                            CanIndex = equipment.CanIndex,
                            DeviceNumber = equipment.DeviceName
                        };

                        // 预加载信号数据
                        long signalId = SQLite_Service.GetSignalId(conn, messageid["调试AC写入"], "是", "MultiplexSignals");
                        var reuseSignals = SQLite_Service.GetReuseSignalsBySignals(conn, signalId);
                        var signalCache = new Dictionary<string, List<SignalInfo>>();
                        foreach (var reuse in reuseSignals)
                        {
                            signalCache[reuse.Description] = SQLite_Service.GetSignalsByMessage(
                                conn, messageid["调试AC写入"], reuse.Description);
                        }

                        AddTabPageWithPanels(mainTabControl, $"{equipment.DeviceName}-AC{acnum}", reuseSignals, signalCache, tabPageInfo);
                    }

                    // DC TabPages
                    for (int c = 0; c < equipment.DCNumber; c++)
                    {
                        dcnum ++;
                        totalWidth = 0;
                        var tabPageInfo = new TabPageInfo
                        {
                            WriteCANID = canIds["调试DC写入"].Replace("X", (dcnum - 1).ToString()),
                            ReadCANID = canIds["调试DC读取"].Replace("X", (dcnum - 1).ToString()),
                            ReceiveCANID = canIds["调试DC接收"].Replace("X", (dcnum - 1).ToString()),
                            DeviceIndex = equipment.DeviceIndex,
                            CanIndex = equipment.CanIndex,
                            DeviceNumber = equipment.DeviceName
                        };
                        
                        // 预加载信号数据
                        long signalId = SQLite_Service.GetSignalId(conn, messageid["调试DC写入"], "是", "MultiplexSignals");
                        var reuseSignals = SQLite_Service.GetReuseSignalsBySignals(conn, signalId);
                        var signalCache = new Dictionary<string, List<SignalInfo>>();
                        foreach (var reuse in reuseSignals)
                        {
                            signalCache[reuse.Description] = SQLite_Service.GetSignalsByMessage(
                                conn, messageid["调试DC写入"], reuse.Description);
                        }

                        AddTabPageWithPanels(mainTabControl, $"{equipment.DeviceName}-DC{dcnum}", reuseSignals, signalCache, tabPageInfo);
                    }
                }
            }

        }

        private void AddTabPageWithPanels(XtraTabControl tabControl, string pageTitle,
        List<ReuseSignal> reuseSignals, Dictionary<string, List<SignalInfo>> signalCache,TabPageInfo tabInfo)
        {
            // 创建Tab页面
            XtraTabPage tabPage = new XtraTabPage
            {
                Text = pageTitle,
                Padding = new System.Windows.Forms.Padding(3),
                Tag = tabInfo
            };
            tabControl.TabPages.Add(tabPage);

            // 创建布局控件
            layoutControl = new LayoutControl
            {
                Dock = DockStyle.Fill,
                AllowCustomization = false,
                Tag = tabInfo
            };
            tabPage.Controls.Add(layoutControl);

            // 主根组（垂直排列）
            rootGroup = new LayoutControlGroup
            {
                GroupBordersVisible = false,
                TextVisible = false,
                DefaultLayoutType = LayoutType.Vertical, //垂直排列
                Padding = new DevExpress.XtraLayout.Utils.Padding(0)
            };
            layoutControl.Root = rootGroup;

            // 创建水平分组容器
            LayoutControlGroup currentHorizontalGroup = null;
            
            // 添加多个AddGroupControl
            for (int i = 0; i < reuseSignals.Count; i++)
            {
                // 每7个创建新的水平组
                if((totalWidth > 1800) || (totalWidth == 0) || 
                   (reuseSignals[i].Description.Remove(0, 3) == "调试模式设定"))
                   //(reuseSignals[i].Description.Remove(0, 3) == "PI参数设置"))
                {
                    totalWidth = 0;
                    currentHorizontalGroup = new LayoutControlGroup
                    {
                        GroupBordersVisible = false,
                        TextVisible = false,
                        DefaultLayoutType = LayoutType.Horizontal, //水平排列
                        Padding = new DevExpress.XtraLayout.Utils.Padding(0),
                        Spacing = new DevExpress.XtraLayout.Utils.Padding(0)
                    };
                    rootGroup.Add(currentHorizontalGroup);
                }
                // 添加GroupControl到当前水平组
                //GetSignal(messageId, reuseSignal[i].Description);
                // 直接从缓存获取信号数据
                List<SignalInfo> signals = signalCache[reuseSignals[i].Description];
                AddGroupControl(layoutControl, currentHorizontalGroup, reuseSignals[i].Description, signals, tabInfo);
            }
        }

        private void ShowToast(string message, Color color)
        {
            // 获取当前鼠标位置
            Point mousePos = Control.MousePosition;

            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() =>
                {
                    using (ToastForm toast = new ToastForm(message, color, mousePos))
                    {
                        toast.ShowDialog(this);
                    }
                }));
            }
            else
            {
                using (ToastForm toast = new ToastForm(message, color, mousePos))
                {
                    toast.ShowDialog(this);
                }
            }
        }

        private void AddGroupControl(LayoutControl layoutControl, LayoutControlGroup parentGroup, string panelTitle, List<SignalInfo> signals,TabPageInfo tabInfo)
        {
            /// ==================== 1. 外层GroupControl设置 ====================
            GroupControl group = new GroupControl
            {
                Text = panelTitle,
                Appearance =
                {
                    BackColor = Color.FromArgb(243, 249, 255),
                    BorderColor = Color.FromArgb(80, 160, 255),
                    Options = { UseBackColor = true }
                }
            };

            var groupInfo = new GroupInfo
            {
                Command = Convert.ToByte(panelTitle.Substring(0, 2), 16),
                Signals = signals,
                TabInfo = tabInfo,
                TextEdits = new List<TextEdit>()
            };
            group.Tag = groupInfo;

            // ==================== 2. 添加分组到布局 ====================
            item = layoutControl.AddItem(panelTitle, group);
            item.Parent = parentGroup;
            item.TextVisible = false;
            item.SizeConstraintsType = SizeConstraintsType.Custom;

            switch (panelTitle.Remove(0, 3))
            {
                case "PI参数设置":
                case "校准参数设置":
                    item.MaxSize = new Size(270, 134);
                    item.MinSize = new Size(270, 134);
                    break;
                //case "调试模式设定":
                //    item.MaxSize = new Size(272 * 2, 134 * 2);
                //    item.MinSize = new Size(272 * 2, 134 * 2);
                //    break;
                default:
                    item.MaxSize = new Size(270, 134 * 2);
                    item.MinSize = new Size(270, 134 * 2);
                    break;
            }

            // ==================== 3. 动态添加输入框并居中 ====================
            //统计标签最大长度
            int length = 0;
            for (int j = 0; j < signals.Count; j++)
            {
                if (length < signals[j].SignalName.Length)
                    length = signals[j].SignalName.Length;
            }

            for (int i = 0; i < signals.Count; i++)
            {
                // 创建文本框
                LabelControl labelControl = new LabelControl
                {
                    Text = $"{signals[i].SignalName}:",
                    Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Far } }
                };
                group.Controls.Add(labelControl);
                
                // 创建输入框
                TextEdit textEdit = new TextEdit
                {
                    Name = $"textEdit{i}",
                    Size = new Size(80, 30)
                };
                group.Controls.Add(textEdit);
                groupInfo.TextEdits.Add(textEdit);

                // 创建单位标签
                LabelControl unitLabel = new LabelControl
                {
                    Text = signals[i].Unit, // 使用信号中的单位
                    Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Far }},
                    //AutoSizeMode = LabelAutoSizeMode.Vertical
                };
                group.Controls.Add(unitLabel);

                switch (panelTitle.Remove(0, 3))
                {
                    case "运行控制模式设定":
                    case "调试模式设定":
                        textEdit.Size = new Size(50, 30);
                        break;
                    case "系统设置":
                        textEdit.Size = new Size(80, 30);
                        break;
                    default:
                        textEdit.Size = new Size(70, 30);
                        break;
                }

                if (signals.Count > 6)
                {
                    int count = signals.Count / 6 + 1;
                    item.MaxSize = new Size(270 * count, 134 * 2);
                    item.MinSize = new Size(270 * count, 134 * 2);
                    int width = (item.MaxSize.Width - count * (textEdit.Size.Width + (16 * length + 6) + 25)) / 2;
                    if ((i > 5) && (i <= 11))
                    {
                        int hight = (i - 5) * 33;
                        width = width + 2 * (16 * length + 6) + textEdit.Size.Width + 30;
                        labelControl.Location = new Point(width - labelControl.Width, hight);
                        textEdit.Location = new Point(width, labelControl.Location.Y - 3);
                        // 设置单位标签位置（在输入框右侧）
                        unitLabel.Location = new Point(textEdit.Right + 3, labelControl.Location.Y);
                    }
                    else if(i > 11)
                    {
                        int hight = (i - 11) * 33;
                        width = width + 3 * (16 * length + 6) + textEdit.Size.Width + 50;
                        labelControl.Location = new Point(width - labelControl.Width, hight);
                        textEdit.Location = new Point(width, labelControl.Location.Y - 3);
                        // 设置单位标签位置（在输入框右侧）
                        unitLabel.Location = new Point(textEdit.Right + 3, labelControl.Location.Y);
                    }
                    else
                    {
                        width = width + (16 * length + 6);
                        labelControl.Location = new Point(width - labelControl.Width, (i + 1) * 33);
                        textEdit.Location = new Point(width, labelControl.Location.Y - 3);
                        // 设置单位标签位置（在输入框右侧）
                        unitLabel.Location = new Point(textEdit.Right + 3, labelControl.Location.Y);
                    }
                }
                else
                {
                    int width = (item.MaxSize.Width - textEdit.Size.Width - (16 * length + 6)) / 2;
                    width = width + (16 * length + 6) - 12;
                    // 设置标签位置
                    labelControl.Location = new Point(width - labelControl.Width, (i + 1) * 33);
                    
                    // 设置输入框位置（在标签右侧）
                    textEdit.Location = new Point(width, labelControl.Location.Y - 3);

                    // 设置单位标签位置（在输入框右侧）
                    unitLabel.Location = new Point(textEdit.Right + 3, labelControl.Location.Y);
                }
            }

            // ==================== 4. 动态添加输入框并居中 ====================
            // 添加“读取参数”按钮
            SimpleButton btnRead = new SimpleButton
            {
                Text = "读取参数",
                Size = new Size(100, 30),
                Appearance =
                {
                    BackColor = Color.LightSkyBlue,
                    ForeColor = Color.White
                },
                Location = new Point(26, item.MaxSize.Height - 40)
            };

            // 添加“写入参数”按钮
            SimpleButton btnWrite = new SimpleButton
            {
                Text = "写入参数",
                Size = new Size(100, 30),
                Appearance =
                {
                    BackColor = Color.LightGreen,
                    ForeColor = Color.White
                },
                Location = new Point(btnRead.Right + 20, btnRead.Location.Y)
            };
            if (signals.Count > 6)
            {
                //int count = signals.Count / 6 + 1;
                int a = (item.MaxSize.Width - btnRead.Size.Width - btnWrite.Size.Width) / 2;
                btnRead.Location = new Point(a, item.MaxSize.Height - 40);
                btnWrite.Location = new Point(btnRead.Right + 20, btnRead.Location.Y);
            }

            // 绑定按钮事件（修改后）
#pragma warning disable CS4014 // 由于此调用不会等待，因此在调用完成前将继续执行当前方法
            btnRead.Click += (sender, e) => ReadParameters(group);
#pragma warning restore CS4014 // 由于此调用不会等待，因此在调用完成前将继续执行当前方法
#pragma warning disable CS4014 // 由于此调用不会等待，因此在调用完成前将继续执行当前方法
            btnWrite.Click += (sender, e) => WriteParameters(group);
#pragma warning restore CS4014 // 由于此调用不会等待，因此在调用完成前将继续执行当前方法

            totalWidth += item.MaxSize.Width;
            group.Controls.Add(btnRead);
            group.Controls.Add(btnWrite);
        }

        //private async void ReadParameters(GroupControl group)
        private async Task<bool> ReadParameters(GroupControl group)
        {
            try
            {
                // 获取组信息
                var groupInfo = group.Tag as GroupInfo;
                if (groupInfo == null) return false;

                // 获取Tab页信息
                var tabInfo = groupInfo.TabInfo;
                if (tabInfo == null) return false;

                // 构造读取报文（命令字）
                byte[] readCommand = new byte[8];
                readCommand[0] = groupInfo.Command;

                // 将十六进制CAN ID转换为整数
                uint readCANID = uint.Parse(tabInfo.ReadCANID.Replace("0x", ""),
                    System.Globalization.NumberStyles.HexNumber);
                uint receiveCANID = uint.Parse(tabInfo.ReceiveCANID.Replace("0x", ""),
                    System.Globalization.NumberStyles.HexNumber);

                // 发送读取指令
                CANManager.Instance.SendCommand(tabInfo.DeviceIndex, tabInfo.CanIndex, readCANID, readCommand);

                // 构造通道键
                string channelKey = CANManager.GetChannelKey(tabInfo.DeviceIndex, tabInfo.CanIndex);

                // 等待并接收响应
                //await Task.Delay(100);
                var response = await CANManager.Instance.ReceiveFrameAsync(channelKey, receiveCANID, 500);

                uint formattedCanId = response.can_id & 0x1FFFFFFF;  // 提取标准CAN ID

                // 检查响应有效性
                if (formattedCanId != receiveCANID)
                {
                    ShowToast("读取失败", Color.Red);
                    LogService.Log("读取失败:未收到响应或响应超时");
                    return false;
                }

                // 验证响应命令字
                if (response.data[0] != groupInfo.Command)
                {
                    ShowToast("读取失败", Color.Red);
                    LogService.Log($"响应命令字不匹配: 期望0x{groupInfo.Command:X2}, 收到0x{response.data[0]:X2}");
                    return false;
                }

                // 解析响应数据并更新UI
                CANManager canManager = CANManager.Instance;
                for (int i = 0; i < groupInfo.Signals.Count; i++)
                {
                    SignalInfo signal = groupInfo.Signals[i];
                    ulong rawValue = canManager.ExtractRawValue(response.data, signal);
                    double physicalValue = canManager.ConvertToPhysicalValue(rawValue, signal);

                    if (signal.SignalName == "出厂日期")
                    {
                        int bytenian = signal.StartBit / 8;
                        int bitnian = signal.Length / 8;
                        ulong yue = response.data[bytenian + bitnian];
                        ulong ri = response.data[bytenian + bitnian + 1];
                        ulong nian = (ulong)physicalValue;
                        physicalValue = nian * 10000 + +yue * 100 + ri;
                    }

                    // 根据精度格式化显示
                    int decimalPlaces = canManager.GetNumberOfDecimalPlaces((decimal)signal.Factor);
                    string text = physicalValue.ToString($"F{decimalPlaces}");

                    // 获取对应的文本框并更新
                    TextEdit textEdit = groupInfo.TextEdits[i];
                    if (textEdit.InvokeRequired)
                    {
                        textEdit.Invoke(new Action(() => textEdit.Text = text));
                    }
                    else
                    {
                        textEdit.Text = text;
                    }
                }
                ShowToast("读取成功", Color.Green);
                LogService.Log("读取成功!");
                return true;
            }
            catch (Exception ex)
            {
                ShowToast("读取失败", Color.Red);
                LogService.Log($"读取失败:{ex.Message}");
                return false;
            }
        }

        //private async void WriteParameters(GroupControl group)
        private async Task<bool> WriteParameters(GroupControl group)
        {
            try
            {
                // 获取组信息
                var groupInfo = group.Tag as GroupInfo;
                if (groupInfo == null) return false;

                // 获取Tab页信息
                var tabInfo = groupInfo.TabInfo;
                if (tabInfo == null) return false;

                // 收集参数值
                byte[] writeData = new byte[8];
                writeData[0] = groupInfo.Command; // 命令字

                // 填充参数值
                for (int i = 0; i < groupInfo.Signals.Count; i++)
                {
                    SignalInfo signal = groupInfo.Signals[i];
                    TextEdit textEdit = groupInfo.TextEdits[i];

                    if (decimal.TryParse(textEdit.Text, out decimal physicalValue))
                    {
                        if(signal.SignalName == "出厂日期")
                        {
                            int bytenian = signal.StartBit / 8;
                            int bitnian = signal.Length / 8;
                            writeData[bytenian + bitnian + 1] = (byte)int.Parse(textEdit.Text.Substring(6, 2));
                            writeData[bytenian + bitnian] = (byte)int.Parse(textEdit.Text.Substring(4, 2));
                            physicalValue = int.Parse(textEdit.Text.Substring(0, 4));
                        }

                        // 计算原始值
                        decimal rawValueDouble = (physicalValue - signal.Offset) / signal.Factor;
                        long rawValue = (long)rawValueDouble;

                        // 将原始值放入数据数组
                        SetRawValue(writeData, signal, rawValue);
                    }
                    else
                    {
                        //MessageBox.Show($"参数格式错误: {signal.SignalName}");
                        LogService.Log($"参数格式错误: {signal.SignalName}");
                        return false;
                    }
                }

                // 将十六进制CAN ID转换为整数
                uint readCANID = uint.Parse(tabInfo.ReadCANID.Replace("0x", ""),
                    System.Globalization.NumberStyles.HexNumber);
                uint writeCANID = uint.Parse(tabInfo.WriteCANID.Replace("0x", ""),
                    System.Globalization.NumberStyles.HexNumber);
                uint receiveCANID = uint.Parse(tabInfo.ReceiveCANID.Replace("0x", ""),
                    System.Globalization.NumberStyles.HexNumber);

                // 发送写入指令
                CANManager.Instance.SendCommand(tabInfo.DeviceIndex, tabInfo.CanIndex, writeCANID, writeData);

                // 等待100ms
                await Task.Delay(100);

                // 发送读取指令
                byte[] readData = new byte[8];
                readData[0] = groupInfo.Command; // 命令字
                CANManager.Instance.SendCommand(tabInfo.DeviceIndex, tabInfo.CanIndex, readCANID, readData);
                
                // 构造通道键
                string channelKey = CANManager.GetChannelKey(tabInfo.DeviceIndex, tabInfo.CanIndex);

                // 等待并接收响应
                var response = await CANManager.Instance.ReceiveFrameAsync(channelKey, receiveCANID, 500);

                uint formattedCanId = response.can_id & 0x1FFFFFFF;  // 提取标准CAN ID
                // 检查响应有效性
                if (formattedCanId != receiveCANID)
                {
                    ShowToast("写入失败", Color.Red);
                    LogService.Log("写入失败:未收到响应或响应超时");
                    return false;
                }

                // 验证数据是否写入
                if (!writeData.SequenceEqual(response.data))
                {
                    ShowToast("写入失败", Color.Red);
                    LogService.Log("写入失败!");
                    return false;
                }

                ShowToast("写入成功", Color.Green);
                LogService.Log("写入成功!");
                return true;
            }
            catch (Exception ex)
            {
                ShowToast("写入失败", Color.Red);
                LogService.Log($"写入失败:{ex.Message}");
                return false;
            }
        }

        // 辅助方法：将原始值设置到数据数组中
        private void SetRawValue(byte[] data, SignalInfo signal, long rawValue)
        {
            int totalBits = data.Length * 8;
            if (signal.StartBit + signal.Length > totalBits)
                throw new ArgumentException("超出数据范围");

            if (signal.ByteOrder == "Inter") // 小端模式
            {
                for (int i = 0; i < signal.Length; i++)
                {
                    int byteOffset = (signal.StartBit + i) / 8;
                    int bitOffset = (signal.StartBit + i) % 8;

                    if ((rawValue & (1L << i)) != 0)
                    {
                        data[byteOffset] |= (byte)(1 << bitOffset);
                    }
                    else
                    {
                        data[byteOffset] &= (byte)~(1 << bitOffset);
                    }
                }
            }
            else // 大端模式
            {
                for (int i = 0; i < signal.Length; i++)
                {
                    int bitIndex = signal.StartBit + i;
                    int byteOffset = bitIndex / 8;
                    int bitOffset = 7 - (bitIndex % 8); // 大端高位在前

                    if ((rawValue & (1L << (signal.Length - 1 - i))) != 0)
                    {
                        data[byteOffset] |= (byte)(1 << bitOffset);
                    }
                    else
                    {
                        data[byteOffset] &= (byte)~(1 << bitOffset);
                    }
                }
            }
        }

        public void UpdateParameters(List<EquipmentModel> equipmentList)
        {
            DeviceConfig(equipmentList);
            //parequipmentList = equipmentList;
            this.Controls.Clear(); // 清除当前控件
            InitializeUI();       // 重新生成界面
        }
    }
}