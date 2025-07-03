using ChargeDebug.Service;
using DataModel;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout;
using System.IO;

namespace ChargeDebug.Form
{
    public partial class Upgradeonline : XtraUserControl
    {
        // 新增进度条声明
        private ProgressBarControl progressBar;

        // 左侧控件声明
        private ComboBoxEdit cbDevice;
        private ComboBoxEdit cbChannel;
        private ComboBoxEdit cbCpu;
        private ButtonEdit btnSelectFile;
        private SimpleButton btnEnterBoot;
        private SimpleButton btnUpgrade;
        // 右侧控件声明
        private MemoEdit txtInfoDisplay;

        private Dictionary<string, EquipmentModel> deviceMap = new Dictionary<string, EquipmentModel>();

        private List<EquipmentModel> upgradeonlineList;

        // 在类级别添加以下字段
        private Dictionary<string, HexFileData> hexFileCache = new Dictionary<string, HexFileData>();

        // 添加HexFileData类定义（内部类）

        public Upgradeonline(List<EquipmentModel> equipmentList)
        {
            upgradeonlineList = equipmentList;
            InitializeComponent();
            InitializeUI();
        }

        public void UpdateDcNumber(List<EquipmentModel> equipmentList)
        {
            upgradeonlineList = equipmentList;
            //清除所有旧布局
            this.Controls.Clear();
            InitializeUI();
            cbDevice.SelectedIndexChanged += CbDevice_SelectedIndexChanged;
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

            // 初始化左侧控件
            cbDevice = new ComboBoxEdit();
            cbChannel = new ComboBoxEdit();
            cbCpu = new ComboBoxEdit();
            btnSelectFile = new ButtonEdit();
            btnEnterBoot = new SimpleButton();
            btnUpgrade = new SimpleButton();

            // 生成设备选项并建立设备映射
            GenerateDeviceOptions();
            ConfigureComboBox(cbCpu, "CPU1", "CPU2", "CPU3", "ARM");

            // 配置文件选择按钮
            btnSelectFile.Properties.Buttons.Add(new EditorButton(ButtonPredefines.Ellipsis));
            btnSelectFile.ButtonPressed += (s, e) => SelectFile();
            btnSelectFile.Properties.ReadOnly = true;
            btnSelectFile.Text = "点击选择文件";

            // 配置操作按钮
            btnEnterBoot.Text = "进入Boot模式";
            btnEnterBoot.Click += (s, e) => EnterBootMode(); // 修改为调用验证方法

            btnUpgrade.Text = "开始升级";
            btnUpgrade.Click += (s, e) => StartUpgrade();
            btnUpgrade.Appearance.BackColor = Color.LightGreen;

            // 添加控件到布局 - 每个项之间保持20px间隔
            LayoutControlItem deviceItem = leftLayout.AddItem("选择设备:", cbDevice);
            deviceItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20); // 底部20px间隔

            LayoutControlItem channelItem = leftLayout.AddItem("选择通道:", cbChannel);
            channelItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20); // 底部20px间隔

            LayoutControlItem cpuItem = leftLayout.AddItem("CPU型号:", cbCpu);
            cpuItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20); // 底部20px间隔

            LayoutControlItem fileItem = leftLayout.AddItem("选择文件:", btnSelectFile);
            fileItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20); // 底部20px间隔

            // 修复：添加按钮时指定标签文本（可以设置为空字符串）
            leftLayout.AddItem("", btnEnterBoot).Padding = new DevExpress.XtraLayout.Utils.Padding(20, 20, 50, 0);
            leftLayout.AddItem("", btnUpgrade).Padding = new DevExpress.XtraLayout.Utils.Padding(20, 20, 10, 0);

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

        private async void EnterBootMode()
        {
            if (!ValidateFile(out string errorMessage))
            {
                AppendInfo(errorMessage);
                XtraMessageBox.Show(errorMessage, "文件验证失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 文件验证通过后执行进入Boot操作
            AppendInfo("✅ 文件验证通过，正在进入Boot模式...");

            // 这里添加实际进入Boot模式的设备操作代码
            // 1. 获取设备对象
            EquipmentModel device = deviceMap[cbDevice.SelectedItem.ToString()];
            // 2. 获取通道
            string channel = cbChannel.SelectedItem.ToString();
            // 3. 获取CPU类型
            string cpuType = cbCpu.SelectedItem.ToString();
            // 4. 获取固件文件路径
            string filePath = btnSelectFile.Text;
            string channelKey = CANManager.GetChannelKey(device.DeviceIndex, device.CanIndex);

            if (!await SendAndVerifyCommand(device, channelKey, 0x0000AA01, 0x0000BB01,
                new byte[] { 0x05, GetCpuByte(cpuType), GetChannelByte(channel), 0x00, 0x00, 0x00, 0x00, 0x00 },
                "进入Bootloader指令", "进入Bootloader"))
            {
                return;
            }
        }

        private void GenerateDeviceOptions()
        {
            // 清空设备下拉框并建立设备映射
            cbDevice.Properties.Items.Clear();
            deviceMap.Clear();

            foreach (var item in upgradeonlineList)
            {
                if (item != null)
                {
                    string displayText = $"{item.DeviceNumber}";
                    cbDevice.Properties.Items.Add(displayText);
                    deviceMap[displayText] = item;
                }
            }

            if (cbDevice.Properties.Items.Count > 0)
            {
                cbDevice.SelectedIndex = 0;
                // 默认选中第一个设备时加载通道
                LoadChannelsForSelectedDevice();
            }
        }

        private void LoadChannelsForSelectedDevice()
        {
            // 清空现有通道
            cbChannel.Properties.Items.Clear();

            if (cbDevice.SelectedItem == null) return;

            // 获取当前选中的设备
            var selectedDevice = deviceMap[cbDevice.SelectedItem.ToString()];
            AppendInfo($"已选择设备: {selectedDevice.DeviceNumber}");
            // 添加AC通道
            for (int i = 0; i < selectedDevice.ACNumber; i++)
            {
                cbChannel.Properties.Items.Add($"AC{i + 1}");
            }

            // 添加DC通道
            for (int i = 0; i < selectedDevice.DCNumber; i++)
            {
                cbChannel.Properties.Items.Add($"DC{i + 1}");
            }

            // 默认选择第一个通道
            if (cbChannel.Properties.Items.Count > 0)
            {
                cbChannel.SelectedIndex = 0;

                // 获取当前选中的设备
                string channelText = cbChannel.SelectedItem?.ToString();
                AppendInfo($"已选择通道: {channelText}");
            }
        }

        // 添加设备选择变更事件
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            cbDevice.SelectedIndexChanged += CbDevice_SelectedIndexChanged;
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

        private void CbDevice_SelectedIndexChanged(object? sender, EventArgs e)
        {
            LoadChannelsForSelectedDevice(); // 刷新通道
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
                            HexFileData hexData = ParseHexFile(filePath);
                            hexFileCache[filePath] = hexData;

                            // 计算总字节数 (MaxAddress - MinAddress + 1)
                            uint totalBytes = hexData.MaxAddress - hexData.MinAddress + 1;

                            // +++ 新增：显示总块数 +++
                            int totalBlocks = hexData.Blocks.Count;

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
        private HexFileData ParseHexFile(string filePath)
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
                        maxAddr = Math.Max(maxAddr, fullAddress + ((uint)dataLength)/2);
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
                //if (record.Address == 0x87558)
                //{
                //    int a = 0;
                //}

                // 计算当前记录需要多少字节
                int bytesNeeded = record.Data.Length;

                // 如果当前块剩余空间不足，且当前块已有数据
                if (currentBlockData.Count > 0 &&
                    currentBlockData.Count + bytesNeeded > 256)
                {
                    // 完成当前块
                    currentAddress = record.Address;
                    FinalizeCurrentBlock(blocks, ref currentBlockData, ref currentBlockStartAddress, currentAddress);
                }

                // 处理地址间隙（按双字节单位填充0x0000）
                if ((record.Address > currentAddress) && (currentBlockData.Count > 0))
                {
                    uint gapSize = record.Address - currentAddress;
                    for (int i = 0; i < gapSize; i++)
                    {
                        currentBlockData.Add(0x00);
                        currentBlockData.Add(0x00);
                        if (currentBlockData.Count >= 256)
                        {
                            FinalizeCurrentBlock(blocks, ref currentBlockData, ref currentBlockStartAddress, currentAddress + (uint)i + 1);
                        }
                    }
                    currentAddress = record.Address;
                }

                // 添加当前记录数据
                currentBlockData.AddRange(record.Data);
                currentAddress += (uint)record.Data.Length / 2;

                // 检查是否达到块大小限制（256字节）
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
            PadToMultipleOf8(ref currentData);

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
            // 显示并重置进度条
            progressBar.Visible = true;
            progressBar.EditValue = 0;

            try
            {
                // 步骤1: 文件验证
                if (!ValidateFile(out string errorMessage))
                {
                    AppendInfo(errorMessage);
                    XtraMessageBox.Show(errorMessage, "文件验证失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                AppendInfo("✅ 文件验证通过，开始升级流程...");

                // 获取设备信息
                EquipmentModel device = deviceMap[cbDevice.SelectedItem.ToString()];
                string channel = cbChannel.SelectedItem.ToString();
                string cpuType = cbCpu.SelectedItem.ToString();
                string filePath = btnSelectFile.Text;
                string channelKey = CANManager.GetChannelKey(device.DeviceIndex, device.CanIndex);

                // 步骤1: 发送请求升级指令 (0x01)
                if (!await SendAndVerifyCommand(device, channelKey, 0x0000AA01, 0x0000BB01,
                    new byte[] { 0x01, GetCpuByte(cpuType), GetChannelByte(channel), 0x00, 0x00, 0x00, 0x00, 0x00 },
                    "请求升级指令", "请求升级"))
                {
                    return;
                }

                // 步骤2: 发送启动升级指令 (0x03)
                if (!await SendAndVerifyCommand(device, channelKey, 0x0000AA01, 0x0000BB01,
                    new byte[] { 0x03, 0xA1, 0xB2, 0xC3, 0xD4, 0x00, 0x00, 0x00 },
                    "启动升级指令", "启动升级"))
                {
                    if (!await SendAndVerifyCommand(device, channelKey, 0x0000AA01, 0x0000BB01,
                    new byte[] { 0x03, 0xA1, 0xB2, 0xC3, 0xD4, 0x00, 0x00, 0x00 },
                    "启动升级指令", "启动升级"))
                    {
                        return;
                    }
                }

                // 步骤3-6: 分块传输固件数据
                await TransferFirmwareDataInBlocks(device, channelKey, filePath);

                // 完成后隐藏进度条
                progressBar.Visible = false;
            }
            catch (Exception)
            {
                progressBar.Visible = false;
            }
        }

        private async Task TransferFirmwareDataInBlocks(EquipmentModel device, string channelKey, string filePath)
        {
            try
            {
                if (!hexFileCache.TryGetValue(filePath, out HexFileData hexData))
                {
                    hexData = ParseHexFile(filePath);
                    hexFileCache[filePath] = hexData;
                }

                // 直接使用解析时生成的块
                for (int i = 0; i < hexData.Blocks.Count; i++)
                {
                    DataBlock block = hexData.Blocks[i] ;
                    bool blockSuccess = false;
                    int retryCount = 0;
                    const int maxRetries = 5;
                    int times = 10;

                    // 重试机制：最多尝试5次
                    while (!blockSuccess && retryCount < maxRetries)
                    {
                        try
                        {
                            await SendBlock(device, channelKey,
                                           block.StartAddress,
                                           block.Data,
                                           block.BlockIndex,
                                           hexData.Blocks.Count,
                                           times);

                            blockSuccess = true; // 标记成功
                        }
                        catch (Exception ex)
                        {
                            retryCount++;
                            times += 10;
                            AppendInfo($"❌ 块 {block.BlockIndex} 第 {retryCount} 次重试失败: {ex.Message}");

                            if (retryCount >= maxRetries)
                            {
                                AppendInfo($"❌ 块 {block.BlockIndex} 重试{maxRetries}次均失败，停止升级！");
                                throw; // 抛出异常终止升级
                            }

                            // 重试前延迟
                            await Task.Delay(100);
                        }
                    }

                    // 更新进度条
                    int progress = (i + 1) * 100 / hexData.Blocks.Count;
                    this.Invoke((Action)(() => { progressBar.EditValue = progress; }));

                    await Task.Delay(5);
                }
                AppendInfo("✅ 所有数据块传输完成，升级成功！");
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 数据传输异常: {ex.Message}");
                progressBar.Visible = false;
                XtraMessageBox.Show($"升级失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task SendBlock(EquipmentModel device, string channelKey,
            uint startAddress, byte[] blockData, int blockIndex, int totalBlocks, int times)
        {
            //AppendInfo($"准备发送块 {blockIndex}/{totalBlocks}, 大小: {blockData.Length}字节, " +
            //           $"起始地址: 0x{startAddress:X8}");

            // 1. 发送烧写地址和长度
            if (!await SendAddressAndLength(device, channelKey,
                                   startAddress,
                                   (uint)blockData.Length)) // 这里传入字节长度
            {
                throw new Exception($"块 {blockIndex} 地址和长度设置失败");
            }

            // 2. 发送数据
            if (!await SendDataPackets(device, channelKey, blockData, blockIndex, totalBlocks, times))
            {
                throw new Exception($"块 {blockIndex} 数据传输失败");
            }

            // 3. 校验数据
            if (!await VerifyDataBlock(device, channelKey, blockIndex, totalBlocks))
            {
                throw new Exception($"块 {blockIndex} 数据校验失败");
            }
        }

        private async Task<bool> SendAddressAndLength(EquipmentModel device, string channelKey,
             uint startAddress, uint byteLength)
        {
            try
            {
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
                        AppendInfo($"❌ 地址和包数设置失败: 错误代码 0x{response.data[1]:X2}");
                        return false;
                    }
                }
                else
                {
                    AppendInfo("❌ 未收到地址和包数设置的响应");
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 地址和包数设置异常: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SendDataPackets(EquipmentModel device, string channelKey,
            byte[] blockData, int blockIndex, int totalBlocks, int times)
        {
            try
            {
                int packetsNeeded = (blockData.Length + 7) / 8;
                //AppendInfo($"块 {blockIndex}/{totalBlocks} 大小: {blockData.Length}字节, " +
                //         $"需要 {packetsNeeded} 个数据包");

                for (int packetIndex = 0; packetIndex < packetsNeeded; packetIndex++)
                {
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
                        if (i + 1 < 8) // 确保有下一个字节可以交换
                        {
                            swappedData[i] = packetData[i + 1];
                            swappedData[i + 1] = packetData[i];
                        }
                        else
                        {
                            swappedData[i] = packetData[i]; // 奇数位置保留原值
                        }
                    }

                    //CANManager.Instance.ClearQueue(channelKey);
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
                    int packetProgress = (packetIndex + 1) * 100 / packetsNeeded;
                    int totalProgress = (blockIndex - 1) * 100 / totalBlocks +
                                         packetIndex * 100 / (totalBlocks * packetsNeeded);

                    // 添加少量延迟防止CAN总线过载
                    await Task.Delay(times);
                }

                return true;
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 数据包发送异常: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> VerifyDataBlock(EquipmentModel device, string channelKey, int blockIndex, int totalBlocks)
        {
            try
            {
                // 获取当前块的数据
                string filePath = btnSelectFile.Text;
                if (!hexFileCache.TryGetValue(filePath, out HexFileData hexData))
                {
                    hexData = ParseHexFile(filePath);
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
                    uint num = verifyResponse.data[0];
                    if ((num == blockIndex - 1) && (verifyResponse.data[2] == 0x00))
                    {
                        AppendInfo($"✅ 块 {blockIndex} 校验成功");
                        return true;
                    }
                    else
                    {
                        AppendInfo($"❌ 块 {blockIndex} 校验失败: 错误代码 0x{verifyResponse.data[2]:X2}-{num + 1}");
                        return false;
                    }
                }
                else
                {
                    AppendInfo("❌ 未收到块校验响应");
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 块校验异常: {ex.Message}");
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
            string channelKey,
            uint sendCanId,
            uint receiveCanId,
            byte[] data,
            string commandName,
            string operationName)
        {
            int maxRetries = 5;
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                try
                {
                    // 发送命令
                    CANManager.Instance.ClearQueue(channelKey);
                    CANManager.Instance.SendCommand(
                        device.DeviceIndex,
                        device.CanIndex,
                        sendCanId,
                        data
                    );

                    string hexData = BitConverter.ToString(data).Replace("-", " ");
                    AppendInfo($"{device.DeviceNumber} | 发送{commandName} | " +
                               $"CAN ID: 0x{sendCanId:X8} | 数据: {hexData}");

                    // 接收响应
                    var response = await CANManager.Instance.ReceiveFrameAsync(channelKey, receiveCanId, 2000);
                    uint canId = response.can_id & 0x1FFFFFFF;

                    if (canId == receiveCanId)
                    {
                        string responseHex = BitConverter.ToString(response.data).Replace("-", " ");
                        AppendInfo($"{device.DeviceNumber} | 接收响应 | " +
                                   $"CAN ID: 0x{canId:X8} | 数据: {responseHex}");

                        if (response.data[0] == data[0] && response.data[1] == 0x00)
                        {
                            AppendInfo($"✅ {operationName}成功");
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
                catch (Exception ex)
                {
                    AppendInfo($"❌ {operationName}异常: {ex.Message}");
                }

                // 重试前等待
                retryCount++;
                if (retryCount < maxRetries)
                {
                    AppendInfo($"↻ {operationName} 重试中 ({retryCount}/{maxRetries})...");
                    await Task.Delay(100); // 指数退避
                }
            }
            AppendInfo($"❌ {operationName} 失败: 超过最大重试次数({maxRetries})");
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
                components.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}