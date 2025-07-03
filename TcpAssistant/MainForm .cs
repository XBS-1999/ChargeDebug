using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TcpCommunicationLib;

namespace TcpAssistant
{
    public partial class MainForm : XtraForm
    {
        // 新增进度条声明
        private ProgressBarControl progressBar;

        // 左侧控件声明
        private ComboBoxEdit cbProtocolType;
        private TextEdit txtIPAddress;
        private TextEdit txtPort;
        private SimpleButton btnOpenAndClose;

        private ComboBoxEdit cbFirmwareModel;
        private ComboBoxEdit cbSystemModell;
        private ButtonEdit btnSelectFile;
        private SimpleButton btnUpgrade;
        // 右侧控件声明
        private Panel rightPanel;
        private MemoEdit txtInfoDisplay;

        private TcpClientHelper tcpHelper;
        //private bool isConnected = false;

        // 在类级别添加以下字段
        private Dictionary<string, HexFileData> hexFileCache = new Dictionary<string, HexFileData>();

        public MainForm()
        {
            InitializeComponent();
            InitializeUI();
        }

        private void InitializeUI()
        {
            // 主窗体设置
            this.Text = "在线升级助手";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterScreen;

            SplitContainer splitContainer = new SplitContainer();
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.Orientation = Orientation.Vertical;
            splitContainer.FixedPanel = FixedPanel.None;
            splitContainer.SplitterDistance = splitContainer.Width / 5;

            // 禁止拖拽分割条
            splitContainer.IsSplitterFixed = true;  //关键设置

            this.Controls.Add(splitContainer);

            // 创建左侧布局容器
            LayoutControl leftLayout = new LayoutControl();
            leftLayout.Dock = DockStyle.Fill;
            splitContainer.Panel1.Controls.Add(leftLayout);

            // 设置整个布局的边距
            leftLayout.Root.Padding = new DevExpress.XtraLayout.Utils.Padding(10); // 所有边10像素边距

            // 创建右侧容器面板
            rightPanel = new Panel();
            rightPanel.Dock = DockStyle.Fill;
            splitContainer.Panel2.Controls.Add(rightPanel);

            // 创建标题标签
            LabelControl lblTitle = new LabelControl();
            lblTitle.Text = "操作日志";
            lblTitle.Dock = DockStyle.Top;
            lblTitle.Appearance.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
            lblTitle.Appearance.Font = new Font("Tahoma", 9, FontStyle.Bold);
            lblTitle.Height = 30; // 设置标题高度
            //lblTitle.Appearance.BackColor = Color.LightGray; // 设置标题背景色
            lblTitle.Appearance.Options.UseBackColor = true;

            // 创建信息显示文本框
            txtInfoDisplay = new MemoEdit();
            txtInfoDisplay.Dock = DockStyle.Fill;
            txtInfoDisplay.Properties.ReadOnly = true;
            txtInfoDisplay.Properties.Appearance.Font = new Font("Tahoma", 12);
            // 自动滚动到末尾
            txtInfoDisplay.SelectionStart = txtInfoDisplay.Text.Length;
            txtInfoDisplay.ScrollToCaret();

            // 将控件添加到右侧面板（注意添加顺序）
            rightPanel.Controls.Add(txtInfoDisplay);
            rightPanel.Controls.Add(lblTitle); // 标题最后添加，确保显示在最上方

            // 初始化左侧控件
            cbProtocolType = new ComboBoxEdit();
            txtIPAddress = new TextEdit();
            txtPort = new TextEdit();
            btnOpenAndClose = new SimpleButton();

            cbFirmwareModel = new ComboBoxEdit();
            cbSystemModell = new ComboBoxEdit();
            
            btnSelectFile = new ButtonEdit();
            btnUpgrade = new SimpleButton();

            // 设置默认值
            ConfigureComboBox(cbProtocolType, "TCP Server", "TCP Client", "UDP", "CAN");
            txtIPAddress.Text = "192.168.1.8";
            txtPort.Text = "4001";

            ConfigureComboBox(cbFirmwareModel, "CPU1", "CPU2", "CPU3", "ARM1", "ARM2", "FPGA1", "FPGA2");
            ConfigureComboBox(cbSystemModell, "AC1", "AC2", "DC1", "DC2", "DC3", "测功机", "AI板卡", "AO板卡", "DI板卡", "DO板卡");


            // 配置文件选择按钮
            btnSelectFile.Properties.Buttons.Add(new EditorButton(ButtonPredefines.Ellipsis));
            btnSelectFile.ButtonPressed += (s, e) => SelectFile();
            btnSelectFile.Properties.ReadOnly = true;
            btnSelectFile.Text = "点击选择文件";

            // 配置操作按钮
            btnOpenAndClose.Text = "打开";
            btnOpenAndClose.Click += (s, e) => BtnOpenAndClose_Click(); // 修改为调用验证方法

            btnUpgrade.Text = "开始升级";
            btnUpgrade.Click += (s, e) => StartUpgrade();
            btnUpgrade.Appearance.BackColor = Color.LightGreen;

            // 添加控件到布局 - 每个项之间保持20px间隔
            LayoutControlItem deviceItem = leftLayout.AddItem("协议类型:", cbProtocolType);
            deviceItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20); // 底部20px间隔

            // +++ 新增IP地址和端口布局项 +++
            LayoutControlItem ipItem = leftLayout.AddItem("设备IP:", txtIPAddress);
            ipItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20);

            LayoutControlItem portItem = leftLayout.AddItem("设备端口:", txtPort);
            portItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20);

            leftLayout.AddItem("", btnOpenAndClose).Padding = new DevExpress.XtraLayout.Utils.Padding(20, 20, 0, 20);

            LayoutControlItem channelItem = leftLayout.AddItem("固件型号:", cbFirmwareModel);
            channelItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20); // 底部20px间隔

            LayoutControlItem cpuItem = leftLayout.AddItem("系统型号:", cbSystemModell);
            cpuItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20); // 底部20px间隔

            LayoutControlItem fileItem = leftLayout.AddItem("选择文件:", btnSelectFile);
            fileItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 20); // 底部20px间隔

            // 修复：添加按钮时指定标签文本（可以设置为空字符串）
            leftLayout.AddItem("", btnUpgrade).Padding = new DevExpress.XtraLayout.Utils.Padding(20, 20, 0, 20);

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
            progressItem.Padding = new DevExpress.XtraLayout.Utils.Padding(20, 20, 0, 20); // 上边距20px
            progressItem.TextVisible = false;  // 隐藏标签文本
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

                // 获取设备信息cbFirmwareModel   cbSystemModell
                // 获取设备信息
                string firmwareModel = cbFirmwareModel.SelectedItem?.ToString() ?? "";
                string systemModel = cbSystemModell.SelectedItem?.ToString() ?? "";
                string filePath = btnSelectFile.Text;

                // 步骤1: 发送升级标志指令 (0x05)
                if (!await SendCommandAndVerify("升级标志指令", 0x05, GetCpuByte(firmwareModel), GetChannelByte(systemModel)))
                {
                    AppendInfo("❌ 升级标志指令发送失败，停止升级");
                    return;
                }

                await Task.Delay(100); //延时100ms
                // 步骤2: 发送请求升级指令 (0x01)
                if (!await SendCommandAndVerify("请求升级指令", 0x01, GetCpuByte(firmwareModel), GetChannelByte(systemModel)))
                {
                    AppendInfo("❌ 请求升级指令发送失败，停止升级");
                    return;
                }

                // 步骤3: 发送启动升级指令 (0x03)
                if (!await SendCommandAndVerify("启动升级指令", 0x03, 0xA1, 0xB2, 0xC3, 0xD4))
                {
                    AppendInfo("❌ 启动升级指令发送失败，停止升级");
                    return;
                }

                // 步骤4-6: 分块传输固件数据
                await TransferFirmwareDataInBlocks(filePath);

                // 断开当前连接
                Disconnect();
                AppendInfo("✅ 固件升级完成！");
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 升级过程中发生错误: {ex.Message}");
            }
            finally
            {
                // 完成后隐藏进度条
                progressBar.Visible = false;
            }
        }

        private async Task TransferFirmwareDataInBlocks(string filePath)
        {
            try
            {
                if (!hexFileCache.TryGetValue(filePath, out HexFileData hexData))
                {
                    AppendInfo("❌ 未找到缓存的HEX文件数据");
                    return;
                }

                // 直接使用解析时生成的块
                for (int i = 0; i < hexData.Blocks.Count; i++)
                {
                    DataBlock block = hexData.Blocks[i];
                    bool blockSuccess = false;
                    int retryCount = 0;
                    const int maxRetries = 1;
                    int times = 10;

                    // 构造数据包
                    List<byte> packet = new List<byte>();
                    // 设备地址 (1 byte)
                    packet.Add(0x01);
                    // 指令码 (1 byte) - 固件数据指令
                    packet.Add(0x06);
                    // 当前块号 (2 bytes, 小端)
                    packet.AddRange(BitConverter.GetBytes((ushort)block.BlockIndex));
                    // 总包数 (2 bytes, 小端)
                    packet.AddRange(BitConverter.GetBytes((ushort)hexData.Blocks.Count));
                    // 起始地址 (4 bytes, 小端)
                    packet.AddRange(BitConverter.GetBytes(block.StartAddress));
                    // 当前块字节数 (2 bytes, 小端)
                    packet.AddRange(BitConverter.GetBytes((ushort)block.Data.Length));
                    // 数据
                    packet.AddRange(block.Data);

                    // 重试机制：最多尝试5次
                    while (!blockSuccess && retryCount < maxRetries)
                    {
                        try
                        {
                            // 发送数据包
                            tcpHelper.Send(false, packet.ToArray());

                            // 等待并验证响应
                            var response = await tcpHelper.ReceiveCommandAsync(0x06, 2000);

                            if(response != null)
                            {
                                // 检查状态字节（索引2的位置）
                                byte status = response[2];
                                if (status == 0x00)
                                {
                                    AppendInfo($"✅ 数据 {i + 1} 包响应成功");
                                    blockSuccess = true; // 标记成功
                                }
                                else
                                {
                                    // 根据状态码显示错误信息
                                    string errorMessage = status switch
                                    {
                                        0x01 => "固件类型错误",
                                        0x02 => "系统型号错误",
                                        0x03 => "秘钥校验失败",
                                        0x04 => "CRC校验失败",
                                        0x05 => "烧录地址错误",
                                        0x06 => "flash操作失败",
                                        0x07 => "指令溢出",
                                        _ => $"未知错误 (0x{status:X2})"
                                    };
                                    AppendInfo($"❌ 数据包发送失败: {errorMessage}");
                                } 
                            }
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

                    await Task.Delay(times);
                }
                AppendInfo("✅ 所有数据块传输完成！");
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 数据传输异常: {ex.Message}");
                progressBar.Visible = false;
                XtraMessageBox.Show($"升级失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private byte GetCpuByte(string firmwareModel)
        {
            // 将固件型号转换为对应的字节值
            return firmwareModel switch
            {
                "CPU1" => 0x10,
                "CPU2" => 0x11,
                "CPU3" => 0x12,
                "ARM1" => 0x20,
                "ARM2" => 0x21,
                "FPGA1" => 0x30,
                "FPGA2" => 0x31,
                _ => 0xFF,
            };
        }

        private byte GetChannelByte(string systemModel)
        {
            // 将系统型号转换为对应的字节值
            return systemModel switch
            {
                "AC1" => 0xA0,
                "AC2" => 0xA1,
                "DC1" => 0x20,
                "DC2" => 0x21,
                "DC3" => 0x22,
                "测功机" => 0x30,
                "AI板卡" => 0x10,
                "AO板卡" => 0x11,
                "DI板卡" => 0x12,
                "DO板卡" => 0x13,
                _ => 0xFF,
            };
        }

        private async Task<bool> SendCommandAndVerify(string commandName, byte command, params byte[] data)
        {
            int maxRetries = 5;
            int retryCount = 0;
            while (retryCount < maxRetries)
            {
                try
                {
                    // 创建包含设备地址和命令码的完整数据包
                    List<byte> packetData = new List<byte>();
                    packetData.Add(0x01);    // 设备地址
                    packetData.Add(command); // 命令码
                    packetData.AddRange(data);

                    // 发送命令
                    tcpHelper.Send(true, packetData.ToArray());

                    // 等待并验证响应
                    var response = await tcpHelper.ReceiveCommandAsync(command, 2000);

                    if (response != null)
                    {
                        // 检查状态字节（索引2的位置）
                        byte status = response[2];
                        if (status == 0x00)
                        {
                            // 成功发送升级标志指令后的特殊处理
                            if (command == 0x05)
                            {
                                AppendInfo("✅ 升级标志指令发送成功，设备即将重启...");
                                // 断开当前连接
                                Disconnect();
                                // 等待设备重启（5秒）
                                AppendInfo("等待设备重启（5秒）...");
                                await Task.Delay(5000);

                                AppendInfo($"🔄 正在尝试重连设备 ({txtIPAddress.Text}:{txtPort.Text})...");
                                Connect();
                                return true;
                            }
                            else
                            {
                                AppendInfo($"✅ {commandName} 响应成功");
                                return true;
                            }   
                        }
                        else
                        {
                            // 根据状态码显示错误信息
                            string errorMessage = status switch
                            {
                                0x01 => "固件类型错误",
                                0x02 => "系统型号错误",
                                0x03 => "秘钥校验失败",
                                0x04 => "CRC校验失败",
                                0x05 => "烧录地址错误",
                                0x06 => "flash操作失败",
                                0x07 => "指令溢出",
                                _ => $"未知错误 (0x{status:X2})"
                            };
                            AppendInfo($"❌ {commandName} 发送失败: {errorMessage}");
                        } 
                    }
                }
                catch (Exception ex)
                {
                    AppendInfo($"❌ {commandName} 发送失败: {ex.Message}");
                    return false;
                }

                // 重试前等待
                retryCount++;
                if (retryCount < maxRetries)
                {
                    AppendInfo($"↻ {commandName} 重试中 ({retryCount}/{maxRetries})...");
                    await Task.Delay(100); // 指数退避
                }
            }
            AppendInfo($"❌ {commandName} 失败: 超过最大重试次数({maxRetries})");
            return false;
        }

        // 实现按钮点击事件处理方法
        private void BtnOpenAndClose_Click()
        {
            // 直接根据当前TCP状态判断，而不是本地变量
            if (tcpHelper?.IsConnected == true)
            {
                Disconnect();
            }
            else
            {
                Connect();
            }
        }

        // 连接方法
        private void Connect()
        {
            // 清除旧连接（包括事件处理程序）
            if (tcpHelper != null)
            {
                tcpHelper.LogMessage -= TcpHelper_LogMessage;
                tcpHelper.ConnectionStatusChanged -= TcpHelper_ConnectionStatusChanged;
                //tcpHelper.DataReceived -= TcpHelper_DataReceived;

                tcpHelper.Dispose();
                tcpHelper = null;
            }

            string? protocol = cbProtocolType.SelectedItem?.ToString();
            string ip = txtIPAddress.Text;
            int port;

            if (!int.TryParse(txtPort.Text, out port))
            {
                AppendInfo("❌ 无效的端口号");
                return;
            }

            try
            {
                tcpHelper = new TcpClientHelper();

                // 绑定事件处理程序
                tcpHelper.LogMessage += TcpHelper_LogMessage;
                tcpHelper.ConnectionStatusChanged += TcpHelper_ConnectionStatusChanged;
                //tcpHelper.DataReceived += TcpHelper_DataReceived;

                switch (protocol)
                {
                    case "TCP Server":
                        //AppendInfo($"🔄 启动TCP服务器 ({ip}:{port})...");
                        //tcpHelper.StartServer(ip, port);
                        AppendInfo("⚠ TCP Server功能暂未实现");
                        break;
                    case "TCP Client":
                        AppendInfo($"🔄 连接TCP服务器 ({ip}:{port})...");
                        tcpHelper.Connect(ip, port);
                        break;
                    case "UDP":
                        // UDP实现留待后续
                        AppendInfo("⚠ UDP功能暂未实现");
                        break;
                    case "CAN":
                        // CAN实现留待后续
                        AppendInfo("⚠ CAN功能暂未实现");
                        break;
                    default:
                        AppendInfo("❌ 请选择协议类型");
                        return;
                }

                //btnOpenAndClose.Text = "关闭";
            }
            catch (SocketException sex)
            {
                HandleSocketException(sex);
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 连接失败: {ex.Message}");
            }
        }

        // 事件处理方法
        private void TcpHelper_LogMessage(object? sender, string msg) => AppendInfo(msg);

        private void TcpHelper_ConnectionStatusChanged(object? sender, bool isConnected)
        {
            this.BeginInvoke((Action)(() =>
            {
                btnOpenAndClose.Text = isConnected ? "关闭" : "打开";
                if (isConnected)
                {
                    AppendInfo($"✅ 连接成功 ({tcpHelper.ClientIP}:{txtPort.Text})");
                }
                else
                {
                    AppendInfo("⚠ 连接已关闭");
                }
            }));
        }

        //private void TcpHelper_DataReceived(object? sender, byte[] data) => ParseAndDisplayResponse(data);

        private void ParseAndDisplayResponse(byte[] data)
        {
            if (data == null || data.Length < 2) return;

            StringBuilder sb = new StringBuilder();
            //sb.AppendLine($"← 接收数据: {BitConverter.ToString(data).Replace("-", " ")}");

            try
            {
                // 基本字段解析
                byte deviceAddress = data[0];
                byte commandCode = data[1];

                //sb.AppendLine($"  设备地址: 0x{deviceAddress:X2}");
                //sb.AppendLine($"  指令码: 0x{commandCode:X2}");

                // 根据指令码解析不同响应
                switch (commandCode)
                {
                    case 0x05: // 升级标志响应
                        ParseUpgradeFlagResponse(data, sb);
                        break;

                    case 0x01: // 请求升级响应
                        ParseUpgradeRequestResponse(data, sb);
                        break;

                    case 0x03: // 启动升级响应
                        ParseStartUpgradeResponse(data, sb);
                        break;

                    case 0x06: // 固件数据响应
                        ParseFirmwareDataResponse(data, sb);
                        break;

                    default:
                        sb.AppendLine("⚠ 未知指令类型");
                        break;
                }

                // 显示成功信息
                //sb.AppendLine("✅ 校验成功，数据有效");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"❌ 解析错误: {ex.Message}");
            }
            AppendInfo(sb.ToString());
        }

        private void ParseUpgradeFlagResponse(byte[] data, StringBuilder sb)
        {
            if (data.Length < 4) return;

            byte status = data[2];
            byte cpuType = data[3];

            //sb.AppendLine("  指令类型: 升级标志响应");
            //sb.AppendLine($"  状态: 0x{status:X2} ({(status == 0x00 ? "成功" : "失败")})");
            //sb.AppendLine($"  CPU类型: 0x{cpuType:X2}");
        }

        private void ParseUpgradeRequestResponse(byte[] data, StringBuilder sb)
        {
            if (data.Length < 4) return;

            byte status = data[2];
            byte channel = data[3];

            sb.AppendLine("  指令类型: 请求升级响应");
            sb.AppendLine($"  状态: 0x{status:X2} ({(status == 0x00 ? "成功" : "失败")})");
            sb.AppendLine($"  通道: 0x{channel:X2}");
        }

        private void ParseStartUpgradeResponse(byte[] data, StringBuilder sb)
        {
            if (data.Length < 3) return;

            byte status = data[2];

            sb.AppendLine("  指令类型: 启动升级响应");
            sb.AppendLine($"  状态: 0x{status:X2} ({(status == 0x00 ? "成功" : "失败")})");
        }

        private void ParseFirmwareDataResponse(byte[] data, StringBuilder sb)
        {
            if (data.Length < 12) return;

            // 解析数据块信息
            ushort blockIndex = BitConverter.ToUInt16(data, 2);
            ushort totalBlocks = BitConverter.ToUInt16(data, 4);
            uint startAddress = BitConverter.ToUInt32(data, 6);
            ushort blockSize = BitConverter.ToUInt16(data, 10);
            byte status = data.Length > 12 ? data[12] : (byte)0;

            sb.AppendLine("  指令类型: 固件数据响应");
            sb.AppendLine($"  块索引: {blockIndex}/{totalBlocks}");
            sb.AppendLine($"  起始地址: 0x{startAddress:X8}");
            sb.AppendLine($"  块大小: {blockSize}字节");
            sb.AppendLine($"  状态: 0x{status:X2} ({(status == 0x00 ? "成功" : "失败")})");
        }

        // 新增：验证IP地址有效性
        private bool IsValidIPAddress(string ip)
        {
            if (ip == "0.0.0.0") return true; // 允许绑定所有接口

            if (IPAddress.TryParse(ip, out IPAddress address))
            {
                // 检查是否是有效的本地IP地址
                var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
                return hostEntry.AddressList.Any(a => a.ToString() == ip);
            }
            return false;
        }

        // 新增：处理特定的Socket异常
        private void HandleSocketException(SocketException ex)
        {
            switch (ex.SocketErrorCode)
            {
                case SocketError.AddressAlreadyInUse:
                    AppendInfo($"❌ 端口已被占用 ({txtPort.Text})");
                    break;
                case SocketError.AddressNotAvailable:
                    AppendInfo($"❌ IP地址无效或不可用 ({txtIPAddress.Text})");
                    AppendInfo("提示: 服务器模式应使用本地IP地址或0.0.0.0");
                    break;
                case SocketError.AccessDenied:
                    AppendInfo("❌ 权限不足，无法绑定端口");
                    AppendInfo("提示: 尝试使用管理员权限运行程序");
                    break;
                default:
                    AppendInfo($"❌ 网络错误: {ex.SocketErrorCode} - {ex.Message}");
                    break;
            }
        }

        // 断开连接方法
        private void Disconnect()
        {
            try
            {
                // 先更新UI状态
                //isConnected = false;
                btnOpenAndClose.Text = "打开";

                // 再断开连接
                tcpHelper?.Disconnect();

                // 确保资源释放
                tcpHelper?.Dispose();
                tcpHelper = null;
            }
            catch (Exception ex)
            {
                AppendInfo($"❌ 关闭连接时出错: {ex.Message}");
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
            string? systemmodell = cbSystemModell.SelectedItem?.ToString();
            string? firmwaremodel = cbFirmwareModel.SelectedItem?.ToString();
            string fileName = Path.GetFileName(filePath);

            // 验证文件名包含必要标识
            bool hasChannel = !string.IsNullOrEmpty(systemmodell) &&
                             fileName.IndexOf(systemmodell, StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasCpu = !string.IsNullOrEmpty(firmwaremodel) &&
                           fileName.IndexOf(firmwaremodel, StringComparison.OrdinalIgnoreCase) >= 0;

            if (!hasChannel || !hasCpu)
            {
                errorMessage = "❌ 文件验证失败:";
                if (!hasChannel) errorMessage += $" 缺少通道标识 '{systemmodell}'";
                if (!hasCpu) errorMessage += $" 缺少CPU型号 '{firmwaremodel}'";
                return false;
            }

            return true;
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
                            string firmwareModel = cbFirmwareModel.SelectedItem?.ToString() ?? "";
                            HexFileData hexData = HexFile.ParseHexFile(filePath, firmwareModel);
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

        private void ConfigureComboBox(ComboBoxEdit combo, params string[] items)
        {
            combo.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            combo.Properties.Items.AddRange(items);
            if (items.Length > 0)
            {
                combo.SelectedIndex = 0;
                if(combo == cbProtocolType)
                {
                    combo.SelectedIndex = 1;
                }
            }
        }

        // 添加设备选择变更事件
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            AppendInfo($"协议类型: {cbProtocolType.SelectedItem?.ToString()}");

            AppendInfo($"设备IP地址: {txtIPAddress.Text}");

            AppendInfo($"设备端口号: {txtPort.Text}");

            AppendInfo($"固件型号: {cbFirmwareModel.SelectedItem?.ToString()}");

            AppendInfo($"系统型号: {cbSystemModell.SelectedItem?.ToString()}");

            cbProtocolType.SelectedIndexChanged += CbProtocolType_SelectedIndexChanged;
            cbFirmwareModel.SelectedIndexChanged += CbFirmwareModel_SelectedIndexChanged;
            cbSystemModell.SelectedIndexChanged += CbSystemModell_SelectedIndexChanged;
            // ===== 新增IP和端口变化事件处理 =====
            txtIPAddress.Properties.EditValueChanged += TxtIPAddress_TextChanged;
            txtPort.Properties.EditValueChanged += TxtPort_TextChanged;
        }

        // ===== 新增IP地址变化事件 =====
        private void TxtIPAddress_TextChanged(object? sender, EventArgs e)
        {
            AppendInfo($"设备IP地址已更新为: {txtIPAddress.Text}");
        }

        // ===== 新增端口号变化事件 =====
        private void TxtPort_TextChanged(object? sender, EventArgs e)
        {
            AppendInfo($"设备端口号已更新为: {txtPort.Text}");
        }

        private void CbProtocolType_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cbProtocolType.SelectedItem == null) return;

            // 获取当前选中的设备
            AppendInfo($"协议类型已更新为: {cbProtocolType.SelectedItem?.ToString()}");
        }

        private void CbFirmwareModel_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cbFirmwareModel.SelectedItem == null) return;

            // 获取当前选中的设备
            AppendInfo($"固件型号已更新为: {cbFirmwareModel.SelectedItem?.ToString()}");
        }

        private void CbSystemModell_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cbSystemModell.SelectedItem == null) return;

            // 获取当前选中的设备
            AppendInfo($"系统型号已更新为: {cbSystemModell.SelectedItem?.ToString()}");
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
            if (disposing && (components != null))
            {
                components.Dispose();
                tcpHelper?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // 添加退出确认对话框
            DialogResult result = XtraMessageBox.Show("确定要退出在线升级系统吗？", "退出确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.No)
            {
                e.Cancel = true; // 取消关闭操作
                return;
            }
            base.OnClosing(e);
        }
    }
}