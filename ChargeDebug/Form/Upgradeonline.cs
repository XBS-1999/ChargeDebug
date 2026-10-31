using ChargeDebug.Service;
using DataModel;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class Upgradeonline : XtraUserControl
    {
        // 新增进度条声明
        private ProgressBarControl progressBar;

        // 左侧控件声明
        private CheckedComboBoxEdit cbDevice;  // 原为 ComboBoxEdit
        private ComboBoxEdit cbChannel;
        private ComboBoxEdit cbCpu;
        private ButtonEdit btnSelectFile;
        private SimpleButton btnEnterBoot;
        private SimpleButton btnUpgrade;
        private SimpleButton btnStopUpgrade;   //新增停止升级按钮
        // 右侧控件声明
        private MemoEdit txtInfoDisplay;

        private Dictionary<string, EquipmentModel> deviceMap = new Dictionary<string, EquipmentModel>();

        private List<EquipmentModel> upgradeonlineList = new List<EquipmentModel>();

        // 在类级别添加以下字段
        private Dictionary<string, HexFileData> hexFileCache = new Dictionary<string, HexFileData>();

        // 新增：取消令牌源，用于停止升级
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isUpgrading = false;
        // ===== 新增：烧写类型 + 烧录器参数 =====
        private ComboBoxEdit cbBurnType;          // 烧写类型：CAN总线 / 烧录器
        private ComboBoxEdit cbCpuCore;           // 烧录器目标 CPU：CPU1 / CPU2 / CPU1+CPU2
        private ButtonEdit btnSelectBootFile;     // 烧录器 Boot 文件(.out)（可多选，CPU1/CPU2 各 ≤1）
        private ButtonEdit btnSelectAppFile;      // 烧录器 App 文件(.out)（可多选，CPU1/CPU2 各 ≤1）
        private List<string> _bootFiles = new List<string>();   // 已选 Boot 文件列表
        private List<string> _appFiles = new List<string>();    // 已选 App 文件列表
        private TextEdit txtSn;                   // 烧录器 SN
        private UniFlashFlasherConfig _uniCfg;
        private int _uniJobIndex = 0;   // 当前烧写任务序号（0-based，多核循环用）
        private int _uniJobCount = 1;   // 本次烧写任务总数
        private UniFlashFlasher _uniFlasher;
        private CancellationTokenSource _uniCts;
        private bool _uniBusy;
        // 布局项引用（类型切换显隐用）
        private LayoutControlItem deviceItem, channelItem, cpuItem, fileItem,
            cpuCoreItem, bootFileItem, appFileItem, snItem;

        public Upgradeonline(List<EquipmentModel> equipmentList)
        {
            DeviceConfig(equipmentList);
            //upgradeonlineList = equipmentList;
            InitializeComponent();
            InitializeUI();
            InitializeUniFlash();
        }

        private void DeviceConfig(List<EquipmentModel> equipmentList)
        {
            upgradeonlineList.Clear();
            foreach (var equipmentLists in equipmentList)
            {
                if (equipmentLists.DeviceType == "充放电设备")
                {
                    upgradeonlineList.Add(equipmentLists);
                }
            }
        }

        public void UpdateDcNumber(List<EquipmentModel> equipmentList)
        {
            DeviceConfig(equipmentList);
            //清除所有旧布局
            this.Controls.Clear();
            InitializeUI();
            cbDevice.EditValueChanged += CbDevice_EditValueChanged;
            cbChannel.SelectedIndexChanged += CbChannel_SelectedIndexChanged;
            cbCpu.SelectedIndexChanged += CbCpu_SelectedIndexChanged;
        }

        private void InitializeUI()
        {
            SplitContainer splitContainer = new SplitContainer();
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.Orientation = Orientation.Vertical;
            splitContainer.FixedPanel = FixedPanel.None;
            splitContainer.SplitterDistance = splitContainer.Width / 6;
            // 禁止拖拽分割条
            splitContainer.IsSplitterFixed = true;  //关键设置

            this.Controls.Add(splitContainer);

            // 创建左侧布局容器
            LayoutControl leftLayout = new LayoutControl();
            leftLayout.Dock = DockStyle.Fill;
            splitContainer.Panel1.Controls.Add(leftLayout);

            // 设置整个布局的边距
            leftLayout.Root.Padding = new DevExpress.XtraLayout.Utils.Padding(10); // 所有边10像素边距

            // 创建右侧文本框
            txtInfoDisplay = new MemoEdit();
            txtInfoDisplay.Dock = DockStyle.Fill;
            txtInfoDisplay.Properties.ReadOnly = true;
            txtInfoDisplay.Properties.Appearance.Font = new Font("Tahoma", 9);
            splitContainer.Panel2.Controls.Add(txtInfoDisplay);

            // ===== 新增：烧写类型 =====
            cbBurnType = new ComboBoxEdit();
            cbBurnType.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            cbBurnType.Properties.Items.AddRange(new object[] { "CAN总线", "烧录器(XDS100V3)" });
            cbBurnType.SelectedIndex = 0;
            cbBurnType.SelectedIndexChanged += (s, e) => SwitchBurnType();

            // 初始化左侧控件
            cbDevice = new CheckedComboBoxEdit();
            cbDevice.Properties.DropDownRows = 50;                 // 下拉显示行数
            cbDevice.Properties.SelectAllItemVisible = true;       // 显示全选/全不选

            cbChannel = new ComboBoxEdit();
            cbCpu = new ComboBoxEdit();
            btnSelectFile = new ButtonEdit();
            btnEnterBoot = new SimpleButton();
            btnUpgrade = new SimpleButton();
            btnStopUpgrade = new SimpleButton(); // 初始化停止升级按钮

            // ===== 新增：烧录器参数控件 =====
            cbCpuCore = new ComboBoxEdit();
            btnSelectBootFile = new ButtonEdit();
            btnSelectAppFile = new ButtonEdit();
            txtSn = new TextEdit();

            // 生成设备选项并建立设备映射
            GenerateDeviceOptions();
            ConfigureComboBox(cbCpu, "CPU1", "CPU2", "CPU3", "ARM");

            // 配置文件选择按钮
            btnSelectFile.Properties.Buttons.Add(new EditorButton(ButtonPredefines.Ellipsis));
            btnSelectFile.ButtonPressed += (s, e) => SelectFile();
            btnSelectFile.Properties.ReadOnly = true;
            btnSelectFile.Text = "点击选择文件";

            // ===== 新增：烧录器 CPU 与双文件框、SN =====
            cbCpuCore.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            cbCpuCore.Properties.Items.AddRange(new object[] { "CPU1", "CPU2", "CPU1+CPU2" });
            cbCpuCore.SelectedIndex = 2; // 默认 CPU1+CPU2（同时烧写两套文件）
            cbCpuCore.SelectedIndexChanged += (s, e) => ValidateLoadedFiles(); // 切换CPU时校验已加载文件

            // Boot/App 各一个文件框：CPU1/CPU2 单选 1 个；CPU1+CPU2 可多选（各 ≤2 个，CPU1/CPU2 各一）
            // 选择时按文件名自动识别 CPU 归属/类型并校验；Boot 可选、App 必选
            InitFileButton(btnSelectBootFile, "Boot", () => SelectFile(true));
            AddClearButton(btnSelectBootFile, "Boot", () => _bootFiles.Clear());
            InitFileButton(btnSelectAppFile, "App", () => SelectFile(false));
            AddClearButton(btnSelectAppFile, "App", () => _appFiles.Clear());
            txtSn.Properties.NullText = "如：SN20261030-001";

            // 配置操作按钮
            btnUpgrade.Text = "开始升级";
            btnUpgrade.Click += (s, e) => StartUpgradeOrFlash();
            btnUpgrade.Appearance.BackColor = Color.LightGreen;

            // 配置停止升级按钮
            btnStopUpgrade.Text = "停止升级";
            btnStopUpgrade.Click += (s, e) => StopUpgradeOrFlash();
            btnStopUpgrade.Appearance.BackColor = Color.LightCoral;
            btnStopUpgrade.Enabled = false; // 初始不可用

            // 类型选择（置顶）
            LayoutControlItem typeItem = leftLayout.AddItem("烧写类型:", cbBurnType);
            typeItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20);

            // 添加控件到布局 - 每个项之间保持20px间隔
            deviceItem = leftLayout.AddItem("选择设备:", cbDevice);
            deviceItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20); // 底部20px间隔

            channelItem = leftLayout.AddItem("选择通道:", cbChannel);
            channelItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20); // 底部20px间隔

            cpuItem = leftLayout.AddItem("CPU型号:", cbCpu);
            cpuItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20); // 底部20px间隔

            fileItem = leftLayout.AddItem("选择文件:", btnSelectFile);
            fileItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20); // 底部20px间隔

            // ===== 新增：烧录器参数（默认隐藏，切到烧录器时显示）=====
            cpuCoreItem = leftLayout.AddItem("烧录CPU:", cbCpuCore);
            cpuCoreItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20);
            cpuCoreItem.Visibility = DevExpress.XtraLayout.Utils.LayoutVisibility.Never;

            bootFileItem = leftLayout.AddItem("Boot文件(.out):", btnSelectBootFile);
            bootFileItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20);
            bootFileItem.Visibility = DevExpress.XtraLayout.Utils.LayoutVisibility.Never;

            appFileItem = leftLayout.AddItem("App文件(.out):", btnSelectAppFile);
            appFileItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20);
            appFileItem.Visibility = DevExpress.XtraLayout.Utils.LayoutVisibility.Never;

            snItem = leftLayout.AddItem("序列号SN:", txtSn);
            snItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20);
            snItem.Visibility = DevExpress.XtraLayout.Utils.LayoutVisibility.Never;

            // 修复：添加按钮时指定标签文本（可以设置为空字符串）
            leftLayout.AddItem("", btnUpgrade).Padding = new DevExpress.XtraLayout.Utils.Padding(20, 20, 0, 0);
            leftLayout.AddItem("", btnStopUpgrade).Padding = new DevExpress.XtraLayout.Utils.Padding(20, 20, 20, 0);

            // 新增进度条控件
            progressBar = new ProgressBarControl();
            progressBar.Properties.Minimum = 0;
            progressBar.Properties.Maximum = 100;
            progressBar.Properties.ShowTitle = true;  // 显示百分比文本
            progressBar.Properties.PercentView = true; // 百分比模式
            progressBar.Dock = DockStyle.Top;          // 顶部停靠
            progressBar.Height = 50;                   // 设置高度
            progressBar.Visible = false;               // 初始不可见
            // 在布局中添加进度条
            LayoutControlItem progressItem = leftLayout.AddItem("", progressBar);
            progressItem.Padding = new DevExpress.XtraLayout.Utils.Padding(10, 10, 20, 0); // 上边距20px
            progressItem.TextVisible = false; // 隐藏标签文本
        }

        private void CbDevice_EditValueChanged(object? sender, EventArgs e)
        {
            var selectedDevices = GetSelectedDevices();
            if (selectedDevices.Count == 0) return;

            // 收集所有选中设备的通道
            HashSet<string> channelSet = new HashSet<string>();
            foreach (var device in selectedDevices)
            {
                int acBase = Convert.ToInt32(device.ACAddress.Substring(device.ACAddress.Length - 1));
                int dcBase = Convert.ToInt32(device.DCAddress.Substring(device.DCAddress.Length - 1));

                for (int i = 0; i < device.ACNumber; i++)
                    channelSet.Add($"AC{acBase + i + 1}");
                for (int i = 0; i < device.DCNumber; i++)
                    channelSet.Add($"DC{dcBase + i + 1}");
            }

            // 更新通道下拉框
            cbChannel.Properties.Items.Clear();
            cbChannel.Properties.Items.AddRange(channelSet.ToArray());

            if (cbChannel.Properties.Items.Count > 0)
                cbChannel.SelectedIndex = 0; // 默认选择第一个
            else
                cbChannel.SelectedIndex = -1;
        }

        // 提取文件验证逻辑到独立方法
        private bool ValidateFile(out string errorMessage)
        {
            errorMessage = string.Empty;
            string filePath = btnSelectFile.Text;

            // 检查是否选择了文件
            if (filePath == "点击选择文件" || !File.Exists(filePath))
            {
                errorMessage = "❌ 请先选择有效的固件文件";
                return false;
            }

            // 获取当前选中的通道和CPU型号
            string channelText = cbChannel.SelectedItem?.ToString();
            string cpuText = cbCpu.SelectedItem?.ToString();
            string fileName = Path.GetFileName(filePath);

            // 验证文件名包含必要标识
            bool hasChannel = !string.IsNullOrEmpty(channelText) &&
                             fileName.IndexOf(channelText, StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasCpu = !string.IsNullOrEmpty(cpuText) &&
                           fileName.IndexOf(cpuText, StringComparison.OrdinalIgnoreCase) >= 0;

            if (!hasChannel || !hasCpu)
            {
                errorMessage = "❌ 文件验证失败:";
                if (!hasChannel) errorMessage += $" 缺少通道标识 '{channelText}'";
                if (!hasCpu) errorMessage += $" 缺少CPU型号 '{cpuText}'";
                return false;
            }

            return true;
        }

        private void GenerateDeviceOptions()
        {
            cbDevice.Properties.Items.Clear();
            deviceMap.Clear(); // 仍可保留映射

            foreach (var item in upgradeonlineList)
            {
                if (item != null)
                {
                    string displayText = $"{item.DeviceName}";
                    // 创建 CheckedListBoxItem，第二个参数表示初始未选中
                    var checkItem = new CheckedListBoxItem(displayText, false);
                    // 将设备对象存入 Tag 属性
                    checkItem.Tag = item;
                    cbDevice.Properties.Items.Add(checkItem);
                    deviceMap[displayText] = item; // 如果仍需要快速查找可以保留
                }
            }
        }

        private List<EquipmentModel> GetSelectedDevices()
        {
            List<EquipmentModel> selected = new List<EquipmentModel>();
            foreach (CheckedListBoxItem item in cbDevice.Properties.Items)
            {
                if (item.CheckState == CheckState.Checked)
                {
                    selected.Add(item.Tag as EquipmentModel);
                }
            }
            return selected;
        }

        // 添加设备选择变更事件
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            cbDevice.EditValueChanged += CbDevice_EditValueChanged;
            cbChannel.SelectedIndexChanged += CbChannel_SelectedIndexChanged;
            cbCpu.SelectedIndexChanged += CbCpu_SelectedIndexChanged;
        }

        private void CbCpu_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cbCpu.SelectedItem == null) return;

            // 获取当前选中的设备
            string cpuText = cbCpu.SelectedItem?.ToString();
            AppendInfo($"已选择CPU型号: {cpuText}");
        }

        private void CbChannel_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cbChannel.SelectedItem == null) return;

            // 获取当前选中的设备
            string channelText = cbChannel.SelectedItem?.ToString();
            AppendInfo($"已选择通道: {channelText}");
        }

        private void ConfigureComboBox(ComboBoxEdit combo, params string[] items)
        {
            combo.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            combo.Properties.Items.AddRange(items);
            if (items.Length > 0)
            {
                combo.SelectedIndex = 0;
                string cpuText = combo.SelectedItem?.ToString();
                AppendInfo($"已选择CPU型号: {cpuText}");
            }
        }

        private void SelectFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "固件文件|*.hex";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = dialog.FileName;
                    btnSelectFile.Text = filePath;

                    // 使用统一的验证方法
                    if (!ValidateFile(out string errorMessage))
                    {
                        AppendInfo(errorMessage);
                        btnSelectFile.Text = "点击选择文件";
                        XtraMessageBox.Show(errorMessage.Replace("❌ 文件验证失败:", "文件名必须包含:") +
                                           $"当前文件: {filePath}",
                                           "文件验证失败",
                                           MessageBoxButtons.OK,
                                           MessageBoxIcon.Error);
                    }
                    else
                    {
                        AppendInfo($"✅ 文件验证通过: {filePath}");
                        try
                        {
                            // 解析HEX文件并缓存
                            HexFileData hexData = ParseHexFile(filePath, cbCpu.SelectedItem?.ToString() ?? "");
                            hexFileCache[filePath] = hexData;

                            // 计算总字节数 (MaxAddress - MinAddress + 1)
                            uint totalBytes = hexData.MaxAddress - hexData.MinAddress + 1;

                            // +++ 新增：显示总块数 +++
                            int totalBlocks = hexData.Blocks.Count;

                            //// 计算当前块的CRC16校验值 (使用MODBUS CRC16算法)
                            //byte[] blockData = GetBlockData(hexData, 257); // 需要实现GetBlockData方法
                            //ushort crc = CalculateCrc16(blockData);

                            //string abc = "";
                            //for (int i = 0; i < blockData.Length; i++)
                            //{
                            //    abc += blockData[i].ToString("X2");
                            //}

                            //AppendInfo($"0x{crc:X8}");
                            //AppendInfo($"{abc}");
                            AppendInfo($"✅ HEX解析成功: 起始地址 0x{hexData.MinAddress:X8}, " +
                                       $"结束地址 0x{hexData.MaxAddress:X8}, " +
                                       $"总长度 {totalBytes} 字节, " +
                                       $"共分为 {totalBlocks} 个数据块"); // 新增块数提示
                        }
                        catch (Exception ex)
                        {
                            AppendInfo($"❌ HEX解析失败: {ex.Message}");
                            btnSelectFile.Text = "点击选择文件";
                            XtraMessageBox.Show($"HEX文件解析失败: {ex.Message}", "解析错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }

                    }
                }
            }
        }

        private class HexFileData
        {
            public List<HexRecord> Records { get; set; } // 存储所有数据记录
            public uint MinAddress { get; set; }         // 最小地址
            public uint MaxAddress { get; set; }         // 最大地址
                                                         // 添加块数据列表
            public List<DataBlock> Blocks { get; set; } = new List<DataBlock>();
        }

        private class DataBlock
        {
            public uint StartAddress { get; set; }
            public byte[] Data { get; set; }
            public int BlockIndex { get; set; }
        }

        private class HexRecord
        {
            public uint Address { get; }
            public byte[] Data { get; }
            public int Length => Data.Length;

            public HexRecord(uint address, byte[] data)
            {
                Address = address;
                Data = data;
            }
        }

        // HEX文件解析方法
        private HexFileData ParseHexFile(string filePath, string firmwareModel)
        {
            List<HexRecord> records = new List<HexRecord>();
            uint upperAddress = 0;
            uint minAddr = uint.MaxValue;
            uint maxAddr = 0;

            // 第一遍：收集记录并确定地址范围
            foreach (string line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] != ':')
                    continue;

                byte[] bytes = HexStringToBytes(line.Substring(1));
                byte dataLength = bytes[0];
                ushort address = (ushort)((bytes[1] << 8) | bytes[2]);
                byte recordType = bytes[3];
                byte[] data = new byte[dataLength];
                Array.Copy(bytes, 4, data, 0, dataLength);
                byte checksum = bytes[4 + dataLength];

                if (CalculateChecksum(bytes, 0, bytes.Length - 1) != checksum)
                    throw new Exception($"校验和错误: {line}");

                switch (recordType)
                {
                    case 0x00: // 数据记录
                        uint fullAddress = (upperAddress << 16) + address;
                        HexRecord record = new HexRecord(fullAddress, data);
                        records.Add(record);
                        minAddr = Math.Min(minAddr, fullAddress);
                        maxAddr = Math.Max(maxAddr, fullAddress + ((uint)dataLength) / 2);
                        break;
                    case 0x04: // 扩展线性地址记录
                        upperAddress = (uint)((data[0] << 8) | data[1]);
                        break;
                    case 0x01: // 文件结束
                        break;
                }
            }

            records.Sort((a, b) => a.Address.CompareTo(b.Address));

            // 第二遍：构建块并填充间隙（按双字节填充0x0000）
            List<DataBlock> blocks = new List<DataBlock>();
            List<byte> currentBlockData = new List<byte>();
            uint currentBlockStartAddress = minAddr;
            uint currentAddress = minAddr;

            foreach (var record in records)
            {
                // 当检测到地址间隙时，直接结束当前块并开始新块
                if (record.Address > currentAddress)
                {
                    // 结束当前块（如果有数据）
                    if (currentBlockData.Count > 0)
                    {
                        FinalizeCurrentBlock(blocks, ref currentBlockData, ref currentBlockStartAddress, currentAddress);
                    }
                    // 开始新块
                    currentBlockStartAddress = record.Address;
                    currentAddress = record.Address;
                }

                // 检查剩余空间是否足够
                if (currentBlockData.Count + record.Data.Length > 256)
                {
                    // 空间不足，结束当前块
                    FinalizeCurrentBlock(blocks, ref currentBlockData, ref currentBlockStartAddress, currentAddress);
                    currentBlockStartAddress = record.Address; // 新块从当前记录开始
                    currentAddress = record.Address;
                }

                // 添加数据
                currentBlockData.AddRange(record.Data);
                if (firmwareModel.Substring(0, 3) == "ARM")
                {
                    currentAddress += (uint)record.Data.Length;
                }
                else
                {
                    currentAddress += (uint)record.Data.Length / 2;
                }

                // 检查是否达到块大小限制
                if (currentBlockData.Count >= 256)
                {
                    FinalizeCurrentBlock(blocks, ref currentBlockData, ref currentBlockStartAddress, currentAddress);
                }
            }

            // 添加最后一个块（如果有剩余数据）
            if (currentBlockData.Count > 0)
            {
                FinalizeCurrentBlock(blocks, ref currentBlockData, ref currentBlockStartAddress, currentAddress);
            }

            return new HexFileData
            {
                Records = records,
                Blocks = blocks,
                MinAddress = minAddr,
                MaxAddress = maxAddr
            };
        }

        // 新增方法：填充数据到8的倍数
        private void PadToMultipleOf8(ref List<byte> data)
        {
            int remainder = data.Count % 8;
            if (remainder != 0)
            {
                int paddingBytes = 8 - remainder;
                for (int i = 0; i < paddingBytes; i++)
                {
                    data.Add(0x00); // 填充0x00
                }
                AppendInfo($"填充 {paddingBytes} 字节使块大小对齐到8的倍数");
            }
        }

        // 完成当前块的创建并重置
        private void FinalizeCurrentBlock(List<DataBlock> blocks, ref List<byte> currentData,
            ref uint startAddress, uint nextAddress)
        {
            if (currentData.Count == 0) return;

            // 确保数据大小是8的倍数（调用处已处理，此处为双重保障）
            //PadToMultipleOf8(ref currentData);

            blocks.Add(new DataBlock
            {
                StartAddress = startAddress,
                Data = currentData.ToArray(),
                BlockIndex = blocks.Count + 1
            });

            //AppendInfo($"✅ HEX解析成功: 第{blocks.Count}块-" +
            //           $"起始地址: 0x{startAddress:X8}, " +
            //           $"结束地址{nextAddress:X8}, " +
            //           $"共有 {currentData.Count} 个字节"); // 新增块数提示

            // 准备新块
            startAddress = nextAddress;
            currentData.Clear();
        }

        private byte[] HexStringToBytes(string hex)
        {
            int length = hex.Length / 2;
            byte[] bytes = new byte[length];
            for (int i = 0; i < length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        private byte CalculateChecksum(byte[] data, int start, int end)
        {
            byte sum = 0;
            for (int i = start; i < end; i++)
                sum += data[i];
            return (byte)((0x100 - sum) & 0xFF);
        }

        private async void StartUpgrade()
        {
            if (_isUpgrading) return;

            // 步骤1: 全局文件验证
            if (!ValidateFile(out string errorMessage))
            {
                AppendInfo(errorMessage);
                XtraMessageBox.Show(errorMessage, "文件验证失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var selectedDevices = GetSelectedDevices();
            if (selectedDevices.Count == 0)
            {
                AppendInfo("❌ 请至少选择一个设备");
                return;
            }

            string selectedChannel = cbChannel.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedChannel))
            {
                AppendInfo("❌ 请选择通道");
                return;
            }

            // 过滤出真正拥有该通道的设备
            var validDevices = selectedDevices.Where(d => DeviceHasChannel(d, selectedChannel)).ToList();
            if (validDevices.Count == 0)
            {
                AppendInfo($"❌ 没有设备包含通道 {selectedChannel}");
                return;
            }

            // 解析 HEX 文件，获取每个设备的块数（所有设备使用同一文件）
            string filePath = btnSelectFile.Text;
            if (!hexFileCache.TryGetValue(filePath, out HexFileData hexData))
            {
                hexData = ParseHexFile(filePath, cbCpu.SelectedItem?.ToString() ?? "");
                hexFileCache[filePath] = hexData;
            }
            int blocksPerDevice = hexData.Blocks.Count;
            int totalBlocksOverall = validDevices.Count * blocksPerDevice;

            _isUpgrading = true;
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            btnUpgrade.Enabled = false;
            btnStopUpgrade.Enabled = true;
            progressBar.Visible = true;
            progressBar.Properties.Maximum = totalBlocksOverall;
            progressBar.Properties.Step = 1;
            progressBar.EditValue = 0;

            // 并发控制：同时最多升级 10 个设备
            using var semaphore = new SemaphoreSlim(10);
            int completedBlocks = 0;   // 线程安全计数器

            // 定义块完成回调（更新进度条）
            Action blockCompleted = () =>
            {
                int current = Interlocked.Increment(ref completedBlocks);
                this.Invoke((Action)(() => { progressBar.EditValue = current; }));
            };

            try
            {
                var tasks = validDevices.Select(async device =>
                {
                    await semaphore.WaitAsync(token);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        AppendInfo($"========== 开始升级设备: {device.DeviceName} ==========");

                        // 传入块完成回调
                        await UpgradeSingleDeviceAsync(device, selectedChannel, token, blockCompleted);

                        AppendInfo($"✅ 设备 {device.DeviceName} 升级完成");
                    }
                    catch (OperationCanceledException)
                    {
                        AppendInfo($"🛑 设备 {device.DeviceName} 升级被取消");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AppendInfo($"❌ 设备 {device.DeviceName} 升级失败: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
                AppendInfo("所有设备处理完毕");
            }
            catch (OperationCanceledException)
            {
                AppendInfo("🛑 批量升级已被用户取消");
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 批量升级异常: {ex.Message}");
                XtraMessageBox.Show($"升级异常: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnUpgrade.Enabled = true;
                btnStopUpgrade.Enabled = false;
                progressBar.Visible = false;
                _isUpgrading = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task UpgradeSingleDeviceAsync(EquipmentModel device, string channel, CancellationToken token, Action blockCompleted)
        {
            string cpuType = cbCpu.SelectedItem?.ToString();
            string filePath = btnSelectFile.Text;

            // ========== 步骤1: 进入Boot模式 ==========
            if (!await SendAndVerifyCommand(device, 0x0000AA01, 0x0000BB01,
                new byte[] { 0x05, GetCpuByte(cpuType), GetChannelByte(channel), 0x00, 0x00, 0x00, 0x00, 0x00 },
                "进入Bootloader指令", "进入Bootloader", token))
            {
                AppendInfo($"⚠️ 设备 {device.DeviceName} 尝试再次进入Boot模式...");

                // 增加延迟后重试
                await Task.Delay(500, token);

                if (!await SendAndVerifyCommand(device, 0x0000AA01, 0x0000BB01,
                    new byte[] { 0x05, GetCpuByte(cpuType), GetChannelByte(channel), 0x00, 0x00, 0x00, 0x00, 0x00 },
                    "进入Bootloader指令", "进入Bootloader", token))
                {
                    throw new Exception($"设备 {device.DeviceName} 进入Boot模式失败");
                }
            }

            // ========== 步骤2: 发送请求升级指令 (0x01) ==========
            if (!await SendAndVerifyCommand(device, 0x0000AA01, 0x0000BB01,
                       new byte[] { 0x01, GetCpuByte(cpuType), GetChannelByte(channel), 0x00, 0x00, 0x00, 0x00, 0x00 },
                       "请求升级指令", "请求升级", token))
            {
                throw new Exception($"设备 {device.DeviceName} 请求升级失败");
            }

            // ========== 步骤3: 发送启动升级指令 (0x03) ==========
            if (!await SendAndVerifyCommand(device, 0x0000AA01, 0x0000BB01,
                new byte[] { 0x03, 0xA1, 0xB2, 0xC3, 0xD4, 0x00, 0x00, 0x00 },
                "启动升级指令", "启动升级", token))
            {
                AppendInfo($"⚠️ 设备 {device.DeviceName}启动升级第一次失败，尝试第二次...");

                await Task.Delay(500, token);

                if (!await SendAndVerifyCommand(device, 0x0000AA01, 0x0000BB01,
                    new byte[] { 0x03, 0xA1, 0xB2, 0xC3, 0xD4, 0x00, 0x00, 0x00 },
                    "启动升级指令", "启动升级", token))
                {
                    throw new Exception($"设备 {device.DeviceName} 启动升级失败");
                }
            }

            // ========== 步骤4-6: 分块传输固件数据 ==========
            string channelKey = CANManager.GetChannelKey(device.DeviceIndex, device.CanIndex);
            await TransferFirmwareDataInBlocks(device, channelKey, filePath, token, blockCompleted);
        }

        // 辅助方法：判断设备是否有指定通道
        private bool DeviceHasChannel(EquipmentModel device, string channel)
        {
            int acBase = Convert.ToInt32(device.ACAddress.Substring(device.ACAddress.Length - 1));
            int dcBase = Convert.ToInt32(device.DCAddress.Substring(device.DCAddress.Length - 1));

            if (channel.StartsWith("AC"))
            {
                int num = int.Parse(channel.Substring(2));
                return num > acBase && num <= acBase + device.ACNumber;
            }
            else if (channel.StartsWith("DC"))
            {
                int num = int.Parse(channel.Substring(2));
                return num > dcBase && num <= dcBase + device.DCNumber;
            }
            return false;
        }

        private async void StopUpgrade()
        {
            if (!_isUpgrading || _cancellationTokenSource == null) return;
            AppendInfo("🛑 正在停止批量升级...");
            _cancellationTokenSource.Cancel();
            btnStopUpgrade.Enabled = false;
        }

        // ===== 新增：烧写类型切换（动态显隐参数区）=====
        private void SwitchBurnType()
        {
            bool isUni = cbBurnType.SelectedIndex == 1; // 1=烧录器

            deviceItem.Visibility = isUni ? DevExpress.XtraLayout.Utils.LayoutVisibility.Never : DevExpress.XtraLayout.Utils.LayoutVisibility.Always;
            channelItem.Visibility = isUni ? DevExpress.XtraLayout.Utils.LayoutVisibility.Never : DevExpress.XtraLayout.Utils.LayoutVisibility.Always;
            cpuItem.Visibility = isUni ? DevExpress.XtraLayout.Utils.LayoutVisibility.Never : DevExpress.XtraLayout.Utils.LayoutVisibility.Always;
            fileItem.Visibility = isUni ? DevExpress.XtraLayout.Utils.LayoutVisibility.Never : DevExpress.XtraLayout.Utils.LayoutVisibility.Always;
            cpuCoreItem.Visibility = isUni ? DevExpress.XtraLayout.Utils.LayoutVisibility.Always : DevExpress.XtraLayout.Utils.LayoutVisibility.Never;
            bootFileItem.Visibility = isUni ? DevExpress.XtraLayout.Utils.LayoutVisibility.Always : DevExpress.XtraLayout.Utils.LayoutVisibility.Never;
            appFileItem.Visibility = isUni ? DevExpress.XtraLayout.Utils.LayoutVisibility.Always : DevExpress.XtraLayout.Utils.LayoutVisibility.Never;
            snItem.Visibility = isUni ? DevExpress.XtraLayout.Utils.LayoutVisibility.Always : DevExpress.XtraLayout.Utils.LayoutVisibility.Never;

            btnUpgrade.Text = isUni ? "开始烧写" : "开始升级";
            btnStopUpgrade.Text = isUni ? "停止烧写" : "停止升级";
            AppendInfo(isUni ? "已切换：烧录器(XDS100V3) 烧写 .out（Boot 可选，CPU1/CPU2 按文件名识别）"
                             : "已切换：CAN 总线在线升级（.hex）");
        }

        // 统一入口：按类型分发
        private void StartUpgradeOrFlash()
        {
            if (_isUpgrading || _uniBusy) return;
            if (cbBurnType.SelectedIndex == 1) StartUniFlashAsync();
            else StartUpgrade();
        }

        // 统一停止入口
        private void StopUpgradeOrFlash()
        {
            if (cbBurnType.SelectedIndex == 1)
            {
                if (_uniBusy && _uniCts != null)
                {
                    AppendInfo("🛑 正在停止烧录器烧写...");
                    _uniCts.Cancel();
                    btnStopUpgrade.Enabled = false;
                }
            }
            else
            {
                StopUpgrade();
            }
        }

        // 配置固件文件选择按钮（统一样式；仅响应 … 选择按钮）
        private void InitFileButton(ButtonEdit btn, string placeholder, Action onSelect)
        {
            btn.Properties.Buttons.Add(new EditorButton(ButtonPredefines.Ellipsis));
            btn.ButtonPressed += (s, e) =>
            {
                if (e.Button.Kind == ButtonPredefines.Ellipsis) onSelect();
            };
            btn.Properties.ReadOnly = true;
            btn.Text = "点击选择" + placeholder + "文件";
        }

        // 附加"清除"按钮（点击后清空该槽位文件列表并重置文本）
        private void AddClearButton(ButtonEdit btn, string placeholder, Action onClear)
        {
            btn.Properties.Buttons.Add(new EditorButton(ButtonPredefines.Clear));
            btn.ButtonPressed += (s, e) =>
            {
                if (e.Button.Kind == ButtonPredefines.Clear)
                {
                    onClear();
                    btn.Text = "点击选择" + placeholder + "文件";
                    AppendInfo("✅ 已清除文件选择: " + placeholder);
                }
            };
        }

        // 通过文件名识别 CPU：文件名含 cpu1/c1 → 1，cpu2/c2 → 2，无法识别 → 0
        private static int DetectCpuFromFileName(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            if (name.Contains("cpu1") || Regex.IsMatch(name, @"(^|[^a-z0-9])c1([^a-z0-9]|$)")) return 1;
            if (name.Contains("cpu2") || Regex.IsMatch(name, @"(^|[^a-z0-9])c2([^a-z0-9]|$)")) return 2;
            return 0;
        }

        // 通过文件名识别类型：含 boot → Boot，否则视为 App
        private static bool IsBootFile(string fileName)
            => Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant().Contains("boot");

        // 烧录器文件选择（isBoot: true=Boot 框）；CPU1/CPU2 单选 1 个，CPU1+CPU2 多选（各 ≤2 个）
        // 按文件名自动识别 CPU 归属/类型并校验；烧写时再按识别拆分到对应核
        private void SelectFile(bool isBoot)
        {
            using var dlg = new OpenFileDialog();
            dlg.Filter = "TI COFF 映像|*.out|所有文件|*.*";
            int sel = cbCpuCore.SelectedIndex;          // 0=CPU1，1=CPU2，2=CPU1+CPU2
            dlg.Multiselect = (sel == 2);               // CPU1+CPU2 允许多选
            if (dlg.ShowDialog() != DialogResult.OK) return;

            var files = dlg.FileNames.ToList();
            string slotLabel = isBoot ? "Boot" : "App";

            // 数量校验：CPU1/CPU2 各只能 1 个；CPU1+CPU2 各最多 2 个（CPU1/CPU2 各一）
            int max = sel == 2 ? 2 : 1;
            if (files.Count > max)
            {
                XtraMessageBox.Show($"{slotLabel} 文件最多选择 {max} 个（当前模式: {cbCpuCore.SelectedItem}）。\nCPU1+CPU2 时请分别选择 CPU1 与 CPU2 各一个文件。",
                    "文件数量超限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var accepted = new List<string>();
            foreach (var path in files)
            {
                string fileName = Path.GetFileName(path);
                int fileCpu = DetectCpuFromFileName(fileName);
                bool fileBoot = IsBootFile(fileName);

                // 类型校验
                if (isBoot != fileBoot)
                {
                    XtraMessageBox.Show($"文件名识别为 {(fileBoot ? "Boot" : "App")} 文件，与 {slotLabel} 栏位不符。\nBoot 文件名应包含 boot，App 文件名应包含 app。",
                        "类型不匹配", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                // CPU 归属校验
                if (sel != 2)
                {
                    int target = sel + 1;
                    if (fileCpu != 0 && fileCpu != target)
                    {
                        XtraMessageBox.Show($"文件名识别为 CPU{fileCpu} 的固件，与当前烧录CPU选择（{cbCpuCore.SelectedItem}）不匹配。",
                            "CPU 不匹配", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else if (fileCpu == 0)
                {
                    XtraMessageBox.Show($"文件名未识别出 CPU 标识（建议命名包含 CPU1/CPU2）: {fileName}",
                        "CPU 未识别", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                accepted.Add(path);
            }

            // CPU1+CPU2 多选时：同一 CPU 同类型只能 1 个
            if (sel == 2 && accepted.Count == 2)
            {
                int c1 = DetectCpuFromFileName(Path.GetFileName(accepted[0]));
                int c2 = DetectCpuFromFileName(Path.GetFileName(accepted[1]));
                if (c1 == c2)
                {
                    XtraMessageBox.Show($"已选择两个 {slotLabel} 文件均识别为 CPU{c1}，请分别选择 CPU1 与 CPU2 各一个文件。",
                        "重复 CPU", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            if (isBoot) _bootFiles = accepted;
            else _appFiles = accepted;
            UpdateFileSlotText(isBoot);
            AppendInfo($"✅ 已选择 {slotLabel} 文件: {string.Join(" ; ", accepted.Select(Path.GetFileName))}（共 {accepted.Count} 个）");
        }

        // 刷新 Boot/App 框显示文本（多个文件以 "; " 分隔）
        private void UpdateFileSlotText(bool isBoot)
        {
            if (isBoot)
                btnSelectBootFile.Text = _bootFiles.Count == 0 ? "点击选择Boot文件" : string.Join("; ", _bootFiles);
            else
                btnSelectAppFile.Text = _appFiles.Count == 0 ? "点击选择App文件" : string.Join("; ", _appFiles);
        }

        // 切换烧录CPU时校验已加载文件的 CPU 归属（仅日志提示；开始烧写前按模式拆分并硬校验）
        private void ValidateLoadedFiles()
        {
            int sel = cbCpuCore.SelectedIndex; // 0=CPU1，1=CPU2，2=CPU1+CPU2
            foreach (var (label, files) in new (string, List<string>)[]
                     { ("Boot", _bootFiles), ("App", _appFiles) })
            {
                foreach (var f in files)
                {
                    int fileCpu = DetectCpuFromFileName(Path.GetFileName(f));
                    if (fileCpu == 0) continue;
                    if (sel != 2 && fileCpu != sel + 1)
                        AppendInfo($"⚠️ {label} 文件 {Path.GetFileName(f)} 识别为 CPU{fileCpu}，与当前选择（{cbCpuCore.SelectedItem}）不匹配，将不参与烧写");
                }
            }
        }

        // 初始化 UniFlash 烧录器（路径自动检测：UniFlash CLI 包放软件目录下即可）
        private void InitializeUniFlash()
        {
            // 配置已内置于驱动代码默认值（不再依赖 uniflash_config.json）：
            // ChipModel=TMS320F28377D，Boot 0x080000-0x083FFF / App 0x084000-0x0BFFFF，
            // 核索引 CPU1=0 / CPU2=2，关校验提速（VerifyAfterFlash=false）
            _uniCfg = new UniFlashFlasherConfig();
            AppendInfo("ℹ 使用内置默认配置：TMS320F28377D，分区 Boot 0x080000-0x083FFF / App 0x084000-0x0BFFFF，核索引 CPU1=0 / CPU2=2，关校验提速");

            // 自动定位 UniFlash CLI 包（软件目录下，无需配置路径）
            var loc = UniFlashFlasher.Locate(AppDomain.CurrentDomain.BaseDirectory);
            if (loc == null)
            {
                AppendInfo($"⚠ 未在软件目录下找到 dslite.bat/dslite.exe，请将 UniFlash CLI 包（dslite.bat + user_files）放入:");
                AppendInfo($"   {AppDomain.CurrentDomain.BaseDirectory}");
            }
            else
            {
                AppendInfo($"✅ 自动定位 dslite: {loc.Value.DslitePath}");
                AppendInfo($"   CLI 包目录: {loc.Value.CliPackagePath}");
            }

            _uniFlasher = new UniFlashFlasher(_uniCfg);
            _uniFlasher.Output += line => AppendInfo(line);
            _uniFlasher.Progress += (p, m) =>
            {
                if (progressBar == null) return;
                try
                {
                    int overall = (_uniJobIndex * 100 + Math.Clamp(p, 0, 100)) / Math.Max(1, _uniJobCount);
                    progressBar.BeginInvoke(new Action(() => { progressBar.EditValue = overall; }));
                }
                catch { }
            };
        }

        // 烧录器烧写流程（UniFlash CLI，Boot/App 文件框按文件名识别拆分到 CPU1/CPU2，按核依次烧写）
        private async void StartUniFlashAsync()
        {
            int sel = cbCpuCore.SelectedIndex;          // 0=CPU1，1=CPU2，2=CPU1+CPU2
            bool need1 = sel != 1;
            bool need2 = sel != 0;
            string coreLabel = cbCpuCore.SelectedItem?.ToString() ?? "CPU1+CPU2";

            // —— 按文件名识别把已选文件拆分到 CPU1/CPU2 ——
            var cpu1Boots = new List<string>(); var cpu2Boots = new List<string>();
            var cpu1Apps = new List<string>(); var cpu2Apps = new List<string>();
            foreach (var f in _bootFiles)
            {
                int c = DetectCpuFromFileName(Path.GetFileName(f));
                if (c == 1) cpu1Boots.Add(f);
                else if (c == 2) cpu2Boots.Add(f);
                else if (need1 && !need2) cpu1Boots.Add(f);   // 单 CPU 模式：未识别文件归该 CPU
                else if (!need1 && need2) cpu2Boots.Add(f);
            }
            foreach (var f in _appFiles)
            {
                int c = DetectCpuFromFileName(Path.GetFileName(f));
                if (c == 1) cpu1Apps.Add(f);
                else if (c == 2) cpu2Apps.Add(f);
                else if (need1 && !need2) cpu1Apps.Add(f);
                else if (!need1 && need2) cpu2Apps.Add(f);
            }

            // —— 存在性校验：各 CPU 的 Boot/App 至少选一个（支持单独刷 Boot 或单独刷 App）；Boot、App 各 ≤1 ——
            if (need1)
            {
                if (cpu1Boots.Count == 0 && cpu1Apps.Count == 0)
                { AppendInfo("❌ 请至少选择 CPU1 的 Boot 或 App 文件 (.out)"); return; }
                if (cpu1Boots.Count > 1)
                { AppendInfo($"❌ CPU1 的 Boot 文件最多 1 个（当前 {cpu1Boots.Count} 个）"); return; }
                if (cpu1Apps.Count > 1)
                { AppendInfo($"❌ CPU1 的 App 文件最多 1 个（当前 {cpu1Apps.Count} 个）"); return; }
            }
            if (need2)
            {
                if (cpu2Boots.Count == 0 && cpu2Apps.Count == 0)
                { AppendInfo("❌ 请至少选择 CPU2 的 Boot 或 App 文件 (.out)"); return; }
                if (cpu2Boots.Count > 1)
                { AppendInfo($"❌ CPU2 的 Boot 文件最多 1 个（当前 {cpu2Boots.Count} 个）"); return; }
                if (cpu2Apps.Count > 1)
                { AppendInfo($"❌ CPU2 的 App 文件最多 1 个（当前 {cpu2Apps.Count} 个）"); return; }
            }
            string sn = txtSn.Text.Trim();
            if (sn.Length == 0) { AppendInfo("❌ 请填写序列号 SN"); return; }

            _uniBusy = true;
            _uniCts = new CancellationTokenSource();
            btnUpgrade.Enabled = false;
            btnStopUpgrade.Enabled = true;
            progressBar.Visible = true;
            progressBar.Properties.Maximum = 100;
            progressBar.Properties.PercentView = true;
            progressBar.Properties.ShowTitle = true;
            progressBar.EditValue = 0;
            AppendInfo($"========== 开始烧录器烧写 SN={sn} | 目标 {coreLabel} ==========");

            try
            {
                await _uniFlasher.OpenAsync(_uniCts.Token);
                // 烧写任务：CPU1→core0，CPU2→core1（Boot 空=单文件合并映像；CPU1+CPU2 最多同时烧 4 个 .out）
                var jobs = new List<(string Label, string? Boot, string? App, int Core)>();
                if (need1) jobs.Add(("CPU1", cpu1Boots.Count == 0 ? null : cpu1Boots[0], cpu1Apps.Count == 0 ? null : cpu1Apps[0], 0));
                if (need2) jobs.Add(("CPU2", cpu2Boots.Count == 0 ? null : cpu2Boots[0], cpu2Apps.Count == 0 ? null : cpu2Apps[0], 1));
                _uniJobCount = jobs.Count;

                var checksums = new List<string>();
                bool allPass = true;
                for (int i = 0; i < jobs.Count; i++)
                {
                    var job = jobs[i];
                    _uniJobIndex = i;   // 当前任务序号 → 整体进度 = (i*100 + 核内进度)/总数
                    _uniCts.Token.ThrowIfCancellationRequested();
                    string fileDesc = job.Boot != null && job.App != null ? "Boot+App 双文件"
                        : job.Boot != null ? "单文件（仅 Boot）" : "单文件（仅 App）";
                    AppendInfo($"---------- 开始烧写 {job.Label}（core {job.Core}，{fileDesc}）----------");
                    var result = await _uniFlasher.FlashAsync(job.Boot, job.App, sn, job.Core, _uniCts.Token);
                    AppendInfo(result.Pass
                        ? $"✅ {job.Label} 烧写成功: {result.Message}"
                        : $"❌ {job.Label} 烧写失败: {result.Message}");
                    if (result.Pass)
                        checksums.Add($"{job.Label}:{result.Checksum}");
                    else { allPass = false; break; }
                }

                string checksumText = string.Join(" / ", checksums);
                XtraMessageBox.Show(allPass
                        ? $"烧写并校验成功\nSN: {sn}\n目标: {coreLabel}\n校验码: {checksumText}"
                        : $"烧写失败\nSN: {sn}\n目标: {coreLabel}\n详见右侧日志",
                    allPass ? "烧写成功" : "烧写失败",
                    MessageBoxButtons.OK,
                    allPass ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            }
            catch (OperationCanceledException)
            {
                AppendInfo("🛑 烧录器烧写已取消");
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 烧录器烧写异常: {ex.Message}");
                string msg = ex.Message ?? "";
                string hint;
                if (msg.Contains("dslite", StringComparison.OrdinalIgnoreCase))
                {
                    hint = "\n\n请准备 UniFlash CLI 包：\n1) 在 UniFlash 中为 28377D 生成 Standalone Command Line 包，得到 dslite.bat 与 user_files\n2) 将整个 CLI 包放入软件目录（exe 同目录）或其 UniFlashCli 子目录，程序自动检测\n3) 重新打开本窗体或重启程序后再次烧写";
                }
                else if (msg.Contains("ccxml", StringComparison.OrdinalIgnoreCase))
                {
                    hint = "\n\n缺少 ccxml（目标芯片配置）：\n1) 在 UniFlash GUI 中为 TMS320F28377D 新建会话（选 XDS100V3 USB Emulator）并保存\n2) 把生成的 .ccxml 复制到 CLI 包目录下 user_files\\configs\\\n3) 重新打开本窗体后再次烧写";
                }
                else if (msg.Contains("ufsettings", StringComparison.OrdinalIgnoreCase))
                {
                    hint = "\n\n缺少烧写设置 .ufsettings：\n1) 在 UniFlash GUI 中配置 28377D 烧写策略（Program/Verify、Erase By Address Range 0x080000-0x0BFFFF）并保存\n2) 把生成的 .ufsettings 复制到 CLI 包目录下 user_files\\settings\\\n3) 重新打开本窗体后再次烧写";
                }
                else
                {
                    hint = "";
                }
                XtraMessageBox.Show($"烧录器烧写异常: {ex.Message}{hint}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                progressBar.Visible = false;
                btnUpgrade.Enabled = true;
                btnStopUpgrade.Enabled = false;
                _uniBusy = false;
                _uniCts?.Dispose();
                _uniCts = null;
            }
        }

        private async Task TransferFirmwareDataInBlocks(EquipmentModel device, string channelKey, string filePath, CancellationToken cancellationToken, Action blockCompleted)
        {
            try
            {
                if (!hexFileCache.TryGetValue(filePath, out HexFileData hexData))
                {
                    hexData = ParseHexFile(filePath, cbCpu.SelectedItem?.ToString() ?? "");
                    hexFileCache[filePath] = hexData;
                }

                // 直接使用解析时生成的块
                for (int i = 0; i < hexData.Blocks.Count; i++)
                {
                    // 检查是否已取消
                    cancellationToken.ThrowIfCancellationRequested();

                    DataBlock block = hexData.Blocks[i];
                    bool blockSuccess = false;
                    int retryCount = 0;
                    const int maxRetries = 5;
                    int times = 25;

                    // 重试机制：最多尝试5次
                    while (!blockSuccess && retryCount < maxRetries)
                    {
                        try
                        {
                            // 检查是否已取消
                            cancellationToken.ThrowIfCancellationRequested();

                            await SendBlock(device, channelKey,
                                           block.StartAddress,
                                           block.Data,
                                           block.BlockIndex,
                                           hexData.Blocks.Count,
                                           times,
                                           cancellationToken);

                            blockSuccess = true; // 标记成功
                        }
                        catch (OperationCanceledException)
                        {
                            throw; // 重新抛出取消异常
                        }
                        catch (Exception ex)
                        {
                            retryCount++;
                            times += 5;
                            AppendInfo($"❌ 设备 {device.DeviceName} 第 {block.BlockIndex} 包数据第 {retryCount} 次重试失败: {ex.Message}");

                            if (retryCount >= maxRetries)
                            {
                                AppendInfo($"❌ 设备 {device.DeviceName} 第 {block.BlockIndex} 包数据重试{maxRetries}次均失败，停止升级！");
                                throw new Exception($"第 {block.BlockIndex} 包数据重试{maxRetries}次均失败，升级已停止。", ex);
                            }

                            // 重试前延迟
                            await Task.Delay(100, cancellationToken);
                        }
                    }

                    // 每成功完成一个块，调用回调更新总体进度
                    blockCompleted?.Invoke();

                    await Task.Delay(20, cancellationToken);
                }
                AppendInfo($"✅ 设备 {device.DeviceName} 所有数据包传输完成，升级成功！");
            }
            catch (OperationCanceledException)
            {
                AppendInfo($"🛑 设备 {device.DeviceName} 数据传输已被取消");
                throw; // 重新抛出取消异常
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 设备 {device.DeviceName} 数据传输异常: {ex.Message}");
                throw; // 重新抛出异常，由 StartUpgrade 方法处理
            }
        }

        private async Task SendBlock(EquipmentModel device, string channelKey,
            uint startAddress, byte[] blockData, int blockIndex, int totalBlocks, int times, CancellationToken cancellationToken)
        {
            // 1. 发送烧写地址和长度
            if (!await SendAddressAndLength(device, channelKey,
                                   startAddress,
                                   (uint)blockData.Length,
                                   cancellationToken)) // 这里传入字节长度
            {
                throw new Exception($"设备 {device.DeviceName} 第 {blockIndex} 包数据地址和长度设置失败");
            }

            // 2. 发送数据
            if (!await SendDataPackets(device, channelKey, blockData, blockIndex, totalBlocks, times, cancellationToken))
            {
                throw new Exception($"设备 {device.DeviceName} 第 {blockIndex} 包数据传输失败");
            }

            // 3. 校验数据
            if (!await VerifyDataBlock(device, channelKey, blockIndex, totalBlocks, cancellationToken))
            {
                throw new Exception($"设备 {device.DeviceName} 第 {blockIndex} 包数据校验失败");
            }
        }

        private async Task<bool> SendAddressAndLength(EquipmentModel device, string channelKey,
             uint startAddress, uint byteLength, CancellationToken cancellationToken)
        {
            try
            {
                // 检查是否已取消
                cancellationToken.ThrowIfCancellationRequested();

                // 准备指令数据
                byte[] data = new byte[8];
                data[0] = 0x06; // 指令码

                // 地址 (小端序: 低字节在前)
                data[1] = (byte)(startAddress & 0xFF);         // LSB
                data[2] = (byte)((startAddress >> 8) & 0xFF);
                data[3] = (byte)((startAddress >> 16) & 0xFF);
                data[4] = (byte)((startAddress >> 24) & 0xFF); // MSB

                // 字节长度
                data[5] = (byte)(byteLength & 0xFF);
                data[6] = (byte)((byteLength >> 8) & 0xFF);
                data[7] = 0x00; // 保留

                // 发送指令
                CANManager.Instance.ClearQueue(channelKey);
                CANManager.Instance.SendCommand(
                    device.DeviceIndex,
                    device.CanIndex,
                    0x0000AA01,
                    data
                );

                // 接收响应
                var response = await CANManager.Instance.ReceiveFrameAsync(channelKey, 0x0000BB01, 2000);
                uint canId = response.can_id & 0x1FFFFFFF;

                if (canId == 0x0000BB01)
                {
                    if (response.data[0] == 0x06 && response.data[1] == 0x00)
                    {
                        return true;
                    }
                    else
                    {
                        AppendInfo($"❌ 设备 {device.DeviceName} 地址和包数设置失败: 错误代码 0x{response.data[1]:X2}");
                        return false;
                    }
                }
                else
                {
                    AppendInfo($"❌ 设备 {device.DeviceName} 未收到地址和包数设置的响应");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 设备 {device.DeviceName} 地址和包数设置异常: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SendDataPackets(EquipmentModel device, string channelKey,
            byte[] blockData, int blockIndex, int totalBlocks, int times, CancellationToken cancellationToken)
        {
            try
            {
                int packetsNeeded = (blockData.Length + 7) / 8;

                for (int packetIndex = 0; packetIndex < packetsNeeded; packetIndex++)
                {
                    // 检查是否已取消
                    cancellationToken.ThrowIfCancellationRequested();

                    int offset = packetIndex * 8;
                    int length = Math.Min(8, blockData.Length - offset);

                    byte[] packetData = new byte[8];
                    Array.Copy(blockData, offset, packetData, 0, length);

                    // 填充剩余字节
                    for (int i = length; i < 8; i++)
                    {
                        packetData[i] = 0x00;
                    }

                    // ===== 每两个字节交换顺序 =====
                    byte[] swappedData = new byte[8];
                    for (int i = 0; i < 8; i += 2)
                    {
                        if (i + 1 < 8)      // 确保有下一个字节可以交换
                        {
                            swappedData[i] = packetData[i + 1];
                            swappedData[i + 1] = packetData[i];
                        }
                        else
                        {
                            swappedData[i] = packetData[i];    // 奇数位置保留原值
                        }
                    }

                    CANManager.Instance.ClearQueue(channelKey);
                    CANManager.Instance.SendCommand(
                        device.DeviceIndex,
                        device.CanIndex,
                        0x0000AA02,
                        swappedData
                    );

                    //string hexDataStr = BitConverter.ToString(swappedData).Replace("-", " ");
                    //AppendInfo($"{device.DeviceNumber} | 发送烧写地址和长度指令 | " +
                    //           $"CAN ID: 0x{0x0000AA02:X8} | 数据: {hexDataStr}");

                    // 更新进度
                    //int packetProgress = (packetIndex + 1) * 100 / packetsNeeded;
                    //int totalProgress = (blockIndex - 1) * 100 / totalBlocks +
                    //                     packetIndex * 100 / (totalBlocks * packetsNeeded);

                    // 添加少量延迟防止CAN总线过载
                    await Task.Delay(times, cancellationToken);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 数据包发送异常: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> VerifyDataBlock(EquipmentModel device, string channelKey, int blockIndex, int totalBlocks, CancellationToken cancellationToken)
        {
            try
            {
                // 检查是否已取消
                cancellationToken.ThrowIfCancellationRequested();

                // 获取当前块的数据
                string filePath = btnSelectFile.Text;
                if (!hexFileCache.TryGetValue(filePath, out HexFileData hexData))
                {
                    hexData = ParseHexFile(filePath, cbCpu.SelectedItem?.ToString() ?? "");
                    hexFileCache[filePath] = hexData;
                }

                // 计算当前块的CRC16校验值 (使用MODBUS CRC16算法)
                byte[] blockData = GetBlockData(hexData, blockIndex); // 需要实现GetBlockData方法
                ushort crc = CalculateCrc16(blockData);

                // 准备校验指令
                byte[] verifyData = new byte[8];
                verifyData[0] = (byte)((blockIndex - 1) & 0xFF); // 块序号低字节
                verifyData[1] = (byte)(((blockIndex - 1) >> 8) & 0xFF); // 块序号高字节
                verifyData[2] = (byte)(crc & 0xFF); // CRC低字节
                verifyData[3] = (byte)((crc >> 8) & 0xFF); // CRC高字节
                verifyData[4] = (byte)(totalBlocks & 0xFF); // 总块数低字节
                verifyData[5] = (byte)((totalBlocks >> 8) & 0xFF); // 总块数高字节
                verifyData[6] = 0x00; // 保留
                verifyData[7] = 0x00; // 保留

                CANManager.Instance.ClearQueue(channelKey);
                CANManager.Instance.SendCommand(
                    device.DeviceIndex,
                    device.CanIndex,
                    0x0000AA03,
                    verifyData
                );

                // 等待校验响应
                var verifyResponse = await CANManager.Instance.ReceiveFrameAsync(channelKey, 0x0000BB03, 3000);
                uint verifyCanId = verifyResponse.can_id & 0x1FFFFFFF;

                if (verifyCanId == 0x0000BB03)
                {
                    // 修改点：正确解析两个字节的块序号
                    ushort receivedBlockIndex = (ushort)(verifyResponse.data[0] | (verifyResponse.data[1] << 8));
                    if ((receivedBlockIndex == blockIndex - 1) && (verifyResponse.data[2] == 0x00))
                    {
                        AppendInfo($"✅ 设备 {device.DeviceName} 第 {blockIndex} 包数据烧写成功");
                        return true;
                    }
                    else
                    {
                        AppendInfo($"❌ 设备 {device.DeviceName} 块 {blockIndex} 校验失败: 错误代码 0x{verifyResponse.data[2]:X2}-{receivedBlockIndex + 1}");
                        return false;
                    }
                }
                else
                {
                    AppendInfo($"❌ 设备 {device.DeviceName} 未收到块校验响应");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 设备 {device.DeviceName} 块校验异常: {ex.Message}");
                return false;
            }
        }

        private ushort CalculateCrc16(byte[] data)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    bool lsb = (crc & 1) != 0;
                    crc >>= 1;
                    if (lsb)
                        crc ^= 0xA001;
                }
            }
            return crc;
        }

        private byte[] GetBlockData(HexFileData hexData, int blockIndex)
        {
            // 这里需要根据您的数据块管理逻辑实现
            // 示例：假设hexData包含所有块的数据列表
            if (blockIndex > 0 && blockIndex <= hexData.Blocks.Count)
            {
                return hexData.Blocks[blockIndex - 1].Data;
            }
            throw new ArgumentException($"无效的块索引: {blockIndex}");
        }

        // 辅助方法：发送命令并验证响应
        private async Task<bool> SendAndVerifyCommand(
            EquipmentModel device,
            uint sendCanId,
            uint receiveCanId,
            byte[] data,
            string commandName,
            string operationName,
            CancellationToken cancellationToken)
        {
            int maxRetries = 5;
            int retryCount = 0;

            while (retryCount <= maxRetries)
            {
                try
                {
                    // 检查是否已取消
                    cancellationToken.ThrowIfCancellationRequested();

                    // 发送命令
                    string channelKey = CANManager.GetChannelKey(device.DeviceIndex, device.CanIndex);
                    CANManager.Instance.ClearQueue(channelKey);
                    CANManager.Instance.SendCommand(
                        device.DeviceIndex,
                        device.CanIndex,
                        sendCanId,
                        data
                    );

                    string hexData = BitConverter.ToString(data).Replace("-", " ");
                    AppendInfo($"设备 {device.DeviceName} | 发送{commandName} | " +
                               $"CAN ID: 0x{sendCanId:X8} | 数据: {hexData}");

                    // 接收响应
                    var response = await CANManager.Instance.ReceiveFrameAsync(channelKey, receiveCanId, 5000);
                    uint canId = response.can_id & 0x1FFFFFFF;

                    if (canId == receiveCanId)
                    {
                        string responseHex = BitConverter.ToString(response.data).Replace("-", " ");
                        AppendInfo($"设备 {device.DeviceName} | 接收响应 | " +
                                   $"CAN ID: 0x{canId:X8} | 数据: {responseHex}");

                        if (response.data[0] == data[0] && response.data[1] == 0x00)
                        {
                            AppendInfo($"✅ 设备 {device.DeviceName} {operationName}成功");
                            return true;
                        }
                        else
                        {
                            AppendErrorResponse(operationName, response.data[1]);
                        }
                    }
                    else
                    {
                        if (commandName == "进入Bootloader指令")
                        {
                            maxRetries = 1;
                            AppendInfo("✅ 已进入Bootloader模式，请开始升级");
                            return true;
                        }
                        AppendInfo($"❌ {operationName}失败: 未收到响应");
                    }
                }
                catch (OperationCanceledException)
                {
                    AppendInfo($"❌ 设备 {device.DeviceName} {operationName}被取消");
                    throw;
                }
                catch (Exception ex)
                {
                    AppendInfo($"❌ 设备 {device.DeviceName} {operationName}异常: {ex.Message}");
                }

                // 重试前等待
                retryCount++;
                if (retryCount <= maxRetries)
                {
                    AppendInfo($"↻ 设备 {device.DeviceName} {operationName} 重试中 ({retryCount}/{maxRetries})...");
                    await Task.Delay(100, cancellationToken); // 指数退避
                }
            }
            AppendInfo($"❌ 设备 {device.DeviceName} {operationName} 失败: 超过最大重试次数({maxRetries})");
            return false;
        }

        // 辅助方法：解析错误响应
        private void AppendErrorResponse(string operation, byte errorCode)
        {
            string errorMessage = $"❌ {operation}失败: ";

            switch (errorCode)
            {
                case 0x01:
                    errorMessage += "系统型号错误";
                    break;
                case 0x02:
                    errorMessage += "软件版本错误";
                    break;
                case 0x03:
                    errorMessage += "文件长度错误";
                    break;
                case 0x04:
                    errorMessage += "其他错误，不能升级";
                    break;
                case 0x05:
                    errorMessage += "密钥错误";
                    break;
                case 0x06:
                    errorMessage += "Flash操作失败";
                    break;
                default:
                    errorMessage += $"未知错误代码 0x{errorCode:X2}";
                    break;
            }

            AppendInfo(errorMessage);
        }

        // 辅助方法：获取CPU字节
        private byte GetCpuByte(string cpuType)
        {
            if (cpuType.Substring(0, 3) == "CPU")
            {
                uint cpunum = Convert.ToUInt32(cpuType.Substring(3, 1));
                return (byte)cpunum;
            }
            return 0x04; // ARM
        }

        // 辅助方法：获取通道字节
        private byte GetChannelByte(string channel)
        {
            if (channel.Substring(0, 2) == "AC")
            {
                uint num = Convert.ToUInt32(channel.Substring(2, 1));
                return (byte)(0xA0 + num - 1);
            }
            else if (channel.Substring(0, 2) == "DC")
            {
                uint num = Convert.ToUInt32(channel.Substring(2, 1));
                return (byte)(0x20 + num - 1);
            }
            return 0x00;
        }

        private void AppendInfo(string message)
        {
            if (txtInfoDisplay.InvokeRequired)
            {
                txtInfoDisplay.BeginInvoke(new Action<string>(AppendInfo), message);
                return;
            }

            // 添加双缓冲支持
            SetDoubleBuffered(txtInfoDisplay);
            // 添加带时间戳的日志
            string logMessage = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";

            // 使用AppendText代替直接修改Text属性
            txtInfoDisplay.AppendText(logMessage);

            // 自动滚动到末尾
            txtInfoDisplay.SelectionStart = txtInfoDisplay.Text.Length;
            txtInfoDisplay.Font = new Font("Tahoma", 14, FontStyle.Regular);
            txtInfoDisplay.ScrollToCaret();
        }

        // 添加双缓冲支持
        private static void SetDoubleBuffered(Control control)
        {
            if (SystemInformation.TerminalServerSession) return;

            var prop = typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            prop?.SetValue(control, true, null);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 释放取消令牌源
                _cancellationTokenSource?.Dispose();
                components.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}