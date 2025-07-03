using ChargeDebug.Service;
//using CommunicationProtocols;
using DataModel;
using DevExpress.XtraEditors;
using DevExpress.XtraLayout;
using DevExpress.XtraLayout.Utils;
using DevExpress.XtraTab;
using Log;
using System.Data.SQLite;

namespace ChargeDebug.Form
{
    public partial class Parameter : XtraUserControl
    {
        private LayoutControl layoutControl;
        private LayoutControlGroup rootGroup;
        private LayoutControlItem item;

        private List<EquipmentModel> parequipmentList = new List<EquipmentModel>();
        private string dbcPath = "";
        // 加载ReuseSignals
        private List<ReuseSignal> reuseSignal = new List<ReuseSignal>();
        // 加载Signals
        private List<SignalInfo> signals = new List<SignalInfo>();
        private long dbcFileId = 0;
        private long messageId = 0;
        private int totalWidth = 0;

        // 在类开头添加 TabControlInfo 内部类
        private class TabPageInfo
        {
            public int DeviceIndex { get; set; }
            public int CanIndex { get; set; }
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
            parequipmentList = equipmentList;
            InitializeComponent();

            InitializeUI();
        }

        private void InitializeUI()
        {
            // 创建主Tab控件
            XtraTabControl tabControl = new XtraTabControl
            {
                Dock = DockStyle.Fill,
                HeaderLocation = TabHeaderLocation.Top,
                HeaderOrientation = TabOrientation.Horizontal
            };
            this.Controls.Add(tabControl);

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

                    // AC TabPages
                    for (int b = 0; b < equipment.ACNumber; b++)
                    {
                        totalWidth = 0;
                        var tabPageInfo = new TabPageInfo
                        {
                            WriteCANID = canIds["调试AC写入"].Replace("X", b.ToString()),
                            ReadCANID = canIds["调试AC读取"].Replace("X", b.ToString()),
                            ReceiveCANID = canIds["调试AC接收"].Replace("X", b.ToString()),
                            DeviceIndex = equipment.DeviceIndex,
                            CanIndex = equipment.CanIndex,
                            DeviceNumber = equipment.DeviceNumber
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

                        AddTabPageWithPanels(tabControl, $"{equipment.DeviceNumber}-AC{b + 1}", reuseSignals, signalCache, tabPageInfo);
                    }

                    // DC TabPages
                    for (int c = 0; c < equipment.DCNumber; c++)
                    {
                        totalWidth = 0;
                        var tabPageInfo = new TabPageInfo
                        {
                            WriteCANID = canIds["调试DC写入"].Replace("X", c.ToString()),
                            ReadCANID = canIds["调试DC读取"].Replace("X", c.ToString()),
                            ReceiveCANID = canIds["调试DC接收"].Replace("X", c.ToString()),
                            DeviceIndex = equipment.DeviceIndex,
                            CanIndex = equipment.CanIndex,
                            DeviceNumber = equipment.DeviceNumber
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

                        AddTabPageWithPanels(tabControl, $"{equipment.DeviceNumber}-DC{c + 1}", reuseSignals, signalCache, tabPageInfo);
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
            btnRead.Click += (sender, e) => ReadParameters(group);
            btnWrite.Click += (sender, e) => WriteParameters(group);

            totalWidth += item.MaxSize.Width;
            group.Controls.Add(btnRead);
            group.Controls.Add(btnWrite);
        }

        private async void ReadParameters(GroupControl group)
        {
            try
            {
                // 获取组信息
                var groupInfo = group.Tag as GroupInfo;
                if (groupInfo == null) return;

                // 获取Tab页信息
                var tabInfo = groupInfo.TabInfo;
                if (tabInfo == null) return;

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
                    return;
                }

                // 验证响应命令字
                if (response.data[0] != groupInfo.Command)
                {
                    ShowToast("读取失败", Color.Red);
                    LogService.Log($"响应命令字不匹配: 期望0x{groupInfo.Command:X2}, 收到0x{response.data[0]:X2}");
                    return;
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
            }
            catch (Exception ex)
            {
                ShowToast("读取失败", Color.Red);
                LogService.Log($"读取失败:{ex.Message}");
            }
        }

        private async void WriteParameters(GroupControl group)
        {
            try
            {
                // 获取组信息
                var groupInfo = group.Tag as GroupInfo;
                if (groupInfo == null) return;

                // 获取Tab页信息
                var tabInfo = groupInfo.TabInfo;
                if (tabInfo == null) return;

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
                        return;
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
                    return;
                }

                // 验证响应命令字
                //if (response.data[0] != groupInfo.Command)
                //{
                //    ShowToast("写入失败", Color.Red);
                //    LogService.Log($"响应命令字不匹配: 期望0x{groupInfo.Command:X2}, 收到0x{response.data[0]:X2}");
                //    return;
                //}

                // 验证数据是否写入
                if (!writeData.SequenceEqual(response.data))
                {
                    ShowToast("写入失败", Color.Red);
                    LogService.Log("写入失败!");
                    return;
                }

                ShowToast("写入成功", Color.Green);
                LogService.Log("写入成功!");
            }
            catch (Exception ex)
            {
                ShowToast("写入失败", Color.Red);
                LogService.Log($"写入失败:{ex.Message}");
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
            parequipmentList = equipmentList;
            this.Controls.Clear(); // 清除当前控件
            InitializeUI();       // 重新生成界面
        }
    }
}