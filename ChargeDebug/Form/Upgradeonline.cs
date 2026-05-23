using ChargeDebug.Service;
using DataModel;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout;
using System.IO;
using System.Reflection;
using System.Text;

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

        // 日志缓冲，解决高频刷屏闪烁
        private StringBuilder _logBuffer = new StringBuilder();
        private System.Windows.Forms.Timer _logTimer;

        public Upgradeonline(List<EquipmentModel> equipmentList)
        {
            DeviceConfig(equipmentList);
            //upgradeonlineList = equipmentList;
            InitializeComponent();
            InitializeUI();

            // 初始化日志防抖定时器（100ms 刷新一次）
            _logTimer = new System.Windows.Forms.Timer();
            _logTimer.Interval = 100;
            _logTimer.Tick += (s, e) => FlushLogBuffer();
            _logTimer.Start();
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
            txtInfoDisplay.Properties.Appearance.Font = new Font("Tahoma", 14);

            txtInfoDisplay.Properties.WordWrap = false;         // 关闭自动换行（减少重绘）
            SetDoubleBuffered(txtInfoDisplay);                  // 双缓冲

            splitContainer.Panel2.Controls.Add(txtInfoDisplay);

            // 初始化左侧控件
            cbDevice = new CheckedComboBoxEdit();
            cbDevice.Properties.DropDownRows = 50;                 // 下拉显示行数
            cbDevice.Properties.SelectAllItemVisible = true;       // 显示全选/全不选
            //cbDevice.Properties.ShowSeparator = true;               // 选中项之间显示分隔符
            //cbDevice.EditValueChanged += CbDevice_EditValueChanged; // 选中变化事件

            cbChannel = new ComboBoxEdit();
            cbCpu = new ComboBoxEdit();
            btnSelectFile = new ButtonEdit();
            btnEnterBoot = new SimpleButton();
            btnUpgrade = new SimpleButton();
            btnStopUpgrade = new SimpleButton(); // 初始化停止升级按钮

            // 生成设备选项并建立设备映射
            GenerateDeviceOptions();
            ConfigureComboBox(cbCpu, "CPU1", "CPU2", "CPU3", "ARM");

            // 配置文件选择按钮
            btnSelectFile.Properties.Buttons.Add(new EditorButton(ButtonPredefines.Ellipsis));
            btnSelectFile.ButtonPressed += (s, e) => SelectFile();
            btnSelectFile.Properties.ReadOnly = true;
            btnSelectFile.Text = "点击选择文件";

            // 配置操作按钮
            //btnEnterBoot.Text = "进入Boot模式";
            //btnEnterBoot.Click += (s, e) => EnterBootMode(); // 修改为调用验证方法

            btnUpgrade.Text = "开始升级";
            btnUpgrade.Click += (s, e) => StartUpgrade();
            btnUpgrade.Appearance.BackColor = Color.LightGreen;

            // 配置停止升级按钮
            btnStopUpgrade.Text = "停止升级";
            btnStopUpgrade.Click += (s, e) => StopUpgrade();
            btnStopUpgrade.Appearance.BackColor = Color.LightCoral;
            btnStopUpgrade.Enabled = false; // 初始不可用

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
            if (selectedDevices.Count == 0)
            {
                AppendInfo("📶 已取消所有设备选择，通道列表已清空");
                cbChannel.Properties.Items.Clear();
                cbChannel.SelectedIndex = -1;
                return;
            }

            // 输出选中设备日志
            AppendInfo($"📶 已选择设备数量：{selectedDevices.Count} 台");
            foreach (var dev in selectedDevices)
            {
                AppendInfo($"设备：{dev.DeviceName}");
            }

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
            {
                cbChannel.SelectedIndex = 0;
                AppendInfo($"✅ 通道加载完成，共 {channelSet.Count} 个通道，默认选中：{cbChannel.SelectedItem}");
            }
            else
            {
                cbChannel.SelectedIndex = -1;
                AppendInfo("⚠️ 未获取到任何可用通道");
            }
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
            string log = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";

            if (txtInfoDisplay.InvokeRequired)
            {
                txtInfoDisplay.BeginInvoke(new Action(() =>
                {
                    lock (_logBuffer)
                        _logBuffer.Append(log);
                }));
                return;
            }

            lock (_logBuffer)
                _logBuffer.Append(log);
        }

        /// <summary>
        /// 批量刷新日志，只重绘一次，彻底解决闪烁
        /// </summary>
        private void FlushLogBuffer()
        {
            if (txtInfoDisplay == null || _logBuffer.Length == 0)
                return;

            string text;
            lock (_logBuffer)
            {
                text = _logBuffer.ToString();
                _logBuffer.Clear();
            }

            // 【DevExpress 正确写法】用 Properties.BeginUpdate / EndUpdate
            txtInfoDisplay.Properties.BeginUpdate();
            try
            {
                txtInfoDisplay.AppendText(text);

                // 只在最后一行才滚动，避免疯狂跳闪
                if (txtInfoDisplay.SelectionStart >= txtInfoDisplay.Text.Length - 1)
                {
                    txtInfoDisplay.SelectionStart = txtInfoDisplay.Text.Length;
                    txtInfoDisplay.ScrollToCaret();
                }
            }
            finally
            {
                txtInfoDisplay.Properties.EndUpdate();
            }
        }

        /// <summary>
        /// 开启控件双缓冲，解决日志刷屏闪烁
        /// </summary>
        private void SetDoubleBuffered(Control control, bool enable = true)
        {
            if (control == null) return;

            // 通过反射开启双缓冲（DevExpress 必须用这个方式）
            PropertyInfo prop = typeof(Control).GetProperty(
                "DoubleBuffered",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (prop != null)
            {
                prop.SetValue(control, enable, null);
            }

            // 额外优化：减少重绘
            typeof(Control).InvokeMember(
                "SetStyle",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.InvokeMethod,
                null,
                control,
                new object[]
                {
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint,
            true
                });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _logTimer?.Dispose(); // 加这行
                _cancellationTokenSource?.Dispose();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}