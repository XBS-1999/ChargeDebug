using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using System.Data;
using System.Data.SQLite;
using System.IO.Ports;

namespace ChargeDebug.Form
{
    public partial class DeviceEditForm : XtraForm
    {
        // 表单控件定义
        private TextEdit devicenumber;
        private TextEdit devicename;
        private ComboBoxEdit deviceType;
        private ComboBoxEdit cantype;
        private ComboBoxEdit communicationprotocols;
        private ComboBoxEdit whether;

        private LabelControl labelDeviceNumber;
        private LabelControl labelDeviceName;
        private LabelControl labelDeviceType;
        private LabelControl labelCanType;
        private LabelControl labelCommunicationProtocols;
        private LabelControl labelWhether;

        // 网口配置
        private TextEdit deviceip;
        private TextEdit deviceport;
        private TextEdit deviceindex;
        private TextEdit canindex;
        private TextEdit acnumber;
        private TextEdit acaddress;
        private TextEdit dcnumber;
        private TextEdit dcaddress;

        private LabelControl labelDeviceIP;
        private LabelControl labelDevicePort;
        private LabelControl labelDeviceIndex;
        private LabelControl labelCanIndex;
        private LabelControl labelACNumber;
        private LabelControl labelACAddress;
        private LabelControl labelDCNumber;
        private LabelControl labelDCAddress;

        // 串口配置
        private ComboBoxEdit comPort;
        private ComboBoxEdit baudRate;
        private ComboBoxEdit dataBits;
        private ComboBoxEdit parity;
        private ComboBoxEdit stopBits;

        private LabelControl labelComPort;
        private LabelControl labelBaudRate;
        private LabelControl labelDataBits;
        private LabelControl labelParity;
        private LabelControl labelStopBits;

        private SimpleButton btnOK;
        private SimpleButton btnCancel;

        // 分组框
        private GroupControl equipmentGroup;
        private GroupControl networkGroup;
        private GroupControl serialGroup;

        // 属性
        public string DeviceNumber => devicenumber.Text;
        public string DeviceType => deviceType.Text;
        public string DeviceName => devicename.Text;
        public string CanType => cantype.Text;
        public string DeviceIP => deviceip.Text;
        public string DevicePort => deviceport.Text;
        public string ComPort => comPort.Text;
        public string BaudRate => baudRate.Text;
        public string DataBits => dataBits.Text;
        public string Parity => parity.Text;
        public string StopBits => stopBits.Text;
        public string DeviceIndex => deviceindex.Text;
        public string CanIndex => canindex.Text;
        public string ACNumber => acnumber.Text;
        public string ACAddress => acaddress.Text;
        public string DCNumber => dcnumber.Text;
        public string DCAddress => dcaddress.Text;
        public string CommunicationProtocols => communicationprotocols.Text;
        public string Whether => whether.Text;

        private string dbcPath = "";

        // 构造函数（新增模式）
        public DeviceEditForm(string dbPath, int newDeviceNumber)
        {
            dbcPath = dbPath;
            InitializeComponent();
            this.Text = "增加设备";
            InitializeControls();
            devicenumber.Text = newDeviceNumber.ToString();
        }

        // 构造函数（编辑模式）
        public DeviceEditForm(DataRow row, string dbPath)
        {
            dbcPath = dbPath;
            InitializeComponent();
            this.Text = "编辑设备";
            InitializeControls();
            LoadData(row);
            devicenumber.Text = row["DeviceNumber"].ToString();
        }

        private void InitializeControls()
        {
            // 初始化控件布局和配置
            this.Size = new Size(700, 400);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // 创建并配置控件
            labelDeviceNumber = new LabelControl { Text = "序号:", Location = new Point(70, 22) };
            devicenumber = new TextEdit
            {
                Location = new Point(150, 20),
                Width = 150,
                Properties =
                {
                    ReadOnly = true, // 设置为只读，禁止编辑
                }
            };

            labelDeviceName = new LabelControl { Text = "设备名称:", Location = new Point(340, 22) };
            devicename = new TextEdit { Location = new Point(450, 20), Width = 150 };

            labelDeviceType = new LabelControl { Text = "设备类型:", Location = new Point(70, 62) };
            deviceType = new ComboBoxEdit { Location = new Point(150, 60), Width = 150 };
            deviceType.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            deviceType.Properties.Items.AddRange(new[] { "充放电设备", "电池BMS", "电压源", "电压表", "电流表" });
            deviceType.SelectedIndexChanged += DeviceType_SelectedIndexChanged;

            labelCanType = new LabelControl { Text = "通讯类型:", Location = new Point(340, 62) };
            cantype = new ComboBoxEdit { Location = new Point(450, 60), Width = 150 };
            cantype.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            cantype.Properties.Items.AddRange(new[] { "ZCAN_CANETTCP", "ZCAN_USBCANFD_200U", "RS485-MODBUS", "USB-SCPI", "RS232" });
            cantype.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            cantype.SelectedIndexChanged += Cantype_SelectedIndexChanged;

            labelCommunicationProtocols = new LabelControl { Text = "通讯协议:", Location = new Point(70, 102) };
            communicationprotocols = new ComboBoxEdit { Location = new Point(150, 100), Width = 150 };
            communicationprotocols.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;


            labelWhether = new LabelControl { Text = "是否启用设备:", Location = new Point(340, 102) };
            whether = new ComboBoxEdit { Location = new Point(450, 100), Width = 150 };
            whether.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            whether.Properties.Items.AddRange(new[] { "启用", "禁用" });

            // 设备配置分组
            equipmentGroup = new GroupControl
            {
                Text = "设备配置",
                Location = new Point(0, 140),
                Size = new Size(700, 105),
                Visible = false // 默认隐藏
            };

            labelACNumber = new LabelControl { Text = "AC数量:", Location = new Point(70, 32) };
            acnumber = new TextEdit { Location = new Point(150, 30), Width = 150 };

            labelACAddress = new LabelControl { Text = "AC起始地址:", Location = new Point(340, 32) };
            acaddress = new TextEdit { Location = new Point(450, 30), Width = 150 };

            labelDCNumber = new LabelControl { Text = "DC数量:", Location = new Point(70, 72) };
            dcnumber = new TextEdit { Location = new Point(150, 70), Width = 150 };

            labelDCAddress = new LabelControl { Text = "DC起始地址:", Location = new Point(340, 72) };
            dcaddress = new TextEdit { Location = new Point(450, 70), Width = 150 };

            equipmentGroup.Controls.AddRange(new Control[] {
                labelACNumber, acnumber,
                labelACAddress, acaddress,
                labelDCNumber, dcnumber,
                labelDCAddress, dcaddress
            });

            // 网口配置分组
            networkGroup = new GroupControl
            {
                Text = "网口配置",
                Location = new Point(0, 0),
                Size = new Size(700, 105),
                Visible = false // 默认隐藏
            };

            labelDeviceIP = new LabelControl { Text = "设备IP:", Location = new Point(70, 32) };
            deviceip = new TextEdit { Location = new Point(150, 30), Width = 150 };

            labelDevicePort = new LabelControl { Text = "设备端口:", Location = new Point(340, 32) };
            deviceport = new TextEdit { Location = new Point(450, 30), Width = 150 };

            labelDeviceIndex = new LabelControl { Text = "设备索引:", Location = new Point(70, 72) };
            deviceindex = new TextEdit { Location = new Point(150, 70), Width = 150 };

            labelCanIndex = new LabelControl { Text = "CAN索引:", Location = new Point(340, 72) };
            canindex = new TextEdit { Location = new Point(450, 70), Width = 150 };

            networkGroup.Controls.AddRange(new Control[] {
                labelDeviceIP, deviceip,
                labelDevicePort, deviceport,
                labelDeviceIndex, deviceindex,
                labelCanIndex, canindex
            });

            // 串口配置分组
            serialGroup = new GroupControl
            {
                Text = "串口配置",
                Location = new Point(0, 230),
                Size = new Size(700, 145),
                Visible = false // 默认隐藏
            };

            labelComPort = new LabelControl { Text = "串口号:", Location = new Point(70, 32) };
            comPort = new ComboBoxEdit { Location = new Point(150, 30), Width = 150 };
            comPort.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            LoadComPorts();

            labelBaudRate = new LabelControl { Text = "波特率:", Location = new Point(340, 32) };
            baudRate = new ComboBoxEdit { Location = new Point(450, 30), Width = 150 };
            baudRate.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            baudRate.Properties.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
            baudRate.Text = "9600";

            labelDataBits = new LabelControl { Text = "数据位:", Location = new Point(70, 72) };
            dataBits = new ComboBoxEdit { Location = new Point(150, 70), Width = 150 };
            dataBits.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            dataBits.Properties.Items.AddRange(new object[] { "5", "6", "7", "8" });
            dataBits.Text = "8";

            labelParity = new LabelControl { Text = "校验位:", Location = new Point(340, 72) };
            parity = new ComboBoxEdit { Location = new Point(450, 70), Width = 150 };
            parity.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            parity.Properties.Items.AddRange(new object[] { "无", "奇校验", "偶校验", "Mark", "空格校验" });
            parity.Text = "无";

            labelStopBits = new LabelControl { Text = "停止位:", Location = new Point(70, 112) };
            stopBits = new ComboBoxEdit { Location = new Point(150, 110), Width = 150 };
            stopBits.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            stopBits.Properties.Items.AddRange(new object[] { "1", "1.5", "2" });
            stopBits.Text = "1";

            serialGroup.Controls.AddRange(new Control[] {
                labelComPort, comPort,
                labelBaudRate, baudRate,
                labelDataBits, dataBits,
                labelParity, parity,
                labelStopBits, stopBits
            });

            // 添加确定/取消按钮
            btnOK = new SimpleButton
            {
                Text = "确定",
                DialogResult = DialogResult.None,
                Location = new Point(200, 320),
                Size = new Size(80, 30)
            };
            btnCancel = new SimpleButton
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(400, 320),
                Size = new Size(80, 30)
            };

            // 添加所有控件到表单
            this.Controls.AddRange(new Control[]
            {
                labelDeviceNumber, devicenumber,
                labelDeviceName, devicename,
                labelDeviceType, deviceType,
                labelCanType, cantype,
                labelCommunicationProtocols, communicationprotocols,
                labelWhether, whether,
                equipmentGroup, networkGroup, serialGroup,
                btnOK, btnCancel
            });

            btnOK.Click += BtnOK_Click;

            // 设置默认设备类型
            deviceType.SelectedIndex = 0;
            // 设置默认通讯类型
            cantype.SelectedIndex = 0;
            // 根据设备类型和通讯类型更新UI
            UpdateUI();
        }

        // 加载可用串口
        private void LoadComPorts()
        {
            comPort.Properties.Items.Clear();
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                comPort.Properties.Items.Add(port);
            }
            if (comPort.Properties.Items.Count > 0)
            {
                comPort.SelectedIndex = 0;
            }
        }

        // 设备类型选择变化事件
        private void DeviceType_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (deviceType.SelectedItem == null) return;

            string selectedType = deviceType.SelectedItem.ToString();

            // 清空协议列表
            communicationprotocols.Properties.Items.Clear();

            // 根据设备类型设置默认的通讯类型
            if (selectedType == "充放电设备" || selectedType == "电池BMS")
            {
                cantype.SelectedIndex = 0; // 
            }
            else
            {
                cantype.SelectedIndex = 1; // RS485-MODBUS
            }

            // 更新UI
            UpdateUI();
        }

        // 通讯类型选择变化事件
        private void Cantype_SelectedIndexChanged(object? sender, EventArgs e)
        {
            // 更新UI
            UpdateUI();

            // 加载通讯协议列表
            LoadProtocol();
        }

        // 更新UI显示
        private void UpdateUI()
        {
            // 根据设备类型显示或隐藏设备配置组
            if (deviceType.Text == "充放电设备")
            {
                equipmentGroup.Visible = true;
                equipmentGroup.Location = new Point(0, 140);
            }
            else
            {
                equipmentGroup.Visible = false;
            }

            // 根据通讯类型显示或隐藏网口/串口配置组
            if (cantype.Text == "ZCAN_CANETTCP")
            {
                // 显示网口配置，隐藏串口配置
                networkGroup.Visible = true;
                serialGroup.Visible = false;

                // 设置位置
                if (deviceType.Text == "充放电设备")
                {
                    networkGroup.Location = new Point(0, 245);
                }
                else
                {
                    networkGroup.Location = new Point(0, 140);
                }
            }
            else if (cantype.Text == "ZCAN_USBCANFD_200U")
            {
                // 显示网口配置，隐藏串口配置
                //networkGroup.Visible = true;
                //serialGroup.Visible = false;

                // 设置位置
                if (deviceType.Text == "充放电设备")
                {
                    networkGroup.Location = new Point(0, 245);
                }
                else
                {
                    networkGroup.Location = new Point(0, 140);
                }
            }
            else if (cantype.Text == "RS485-MODBUS")
            {
                // 显示串口配置，隐藏网口配置
                networkGroup.Visible = false;
                serialGroup.Visible = true;

                // 设置位置
                if (deviceType.Text == "充放电设备")
                {
                    serialGroup.Location = new Point(0, 245);
                }
                else
                {
                    serialGroup.Location = new Point(0, 140);
                }
            }
            else if (cantype.Text == "RS232")
            {
                // 显示串口配置，隐藏网口配置
                networkGroup.Visible = false;
                serialGroup.Visible = true;

                // 设置位置
                if (deviceType.Text == "充放电设备")
                {
                    serialGroup.Location = new Point(0, 245);
                }
                else
                {
                    serialGroup.Location = new Point(0, 140);
                }
            }
            else if (cantype.Text == "USB-SCPI")
            {
                // 隐藏网口和串口配置
                networkGroup.Visible = false;
                serialGroup.Visible = false;
            }

            // 调整按钮位置
            int buttonY = 0;
            if (deviceType.Text == "充放电设备")
            {
                buttonY = equipmentGroup.Bottom;

                if (cantype.Text == "ZCAN_CANETTCP")
                {
                    buttonY = networkGroup.Bottom;
                }
                else if (cantype.Text == "ZCAN_USBCANFD_200U")
                {
                    buttonY = networkGroup.Bottom;
                }
                else if (cantype.Text == "RS485-MODBUS")
                {
                    buttonY = serialGroup.Bottom;
                }
                else if (cantype.Text == "RS232")
                {
                    buttonY = serialGroup.Bottom;
                }
            }
            else
            {
                buttonY = 140;

                if (cantype.Text == "ZCAN_CANETTCP")
                {
                    buttonY = networkGroup.Bottom;
                }
                else if (cantype.Text == "ZCAN_USBCANFD_200U")
                {
                    buttonY = networkGroup.Bottom;
                }
                else if (cantype.Text == "RS485-MODBUS")
                {
                    buttonY = serialGroup.Bottom;
                }
                else if (cantype.Text == "RS232")
                {
                    buttonY = serialGroup.Bottom;
                }
            }

            btnOK.Location = new Point(200, buttonY + 20);
            btnCancel.Location = new Point(400, buttonY + 20);

            // 调整窗体高度
            this.Height = buttonY + 100;
        }

        private void LoadProtocol()
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();

                    // 根据通讯类型确定协议类型
                    string agreementType = "";
                    if (cantype.Text == "ZCAN_CANETTCP" || cantype.Text == "ZCAN_USBCANFD_200U")
                    {
                        agreementType = "CAN总线";
                    }
                    else if (cantype.Text == "RS485-MODBUS")
                    {
                        agreementType = "MODBUS";
                    }
                    else if (cantype.Text == "RS232")
                    {
                        agreementType = "RS232";
                    }
                    else if (cantype.Text == "USB-SCPI")
                    {
                        agreementType = "SCPI";
                    }

                    // 如果没有选择通讯类型，则返回
                    if (string.IsNullOrEmpty(agreementType))
                        return;

                    // 查询数据库获取协议名称
                    string query = "SELECT DbcFileName FROM DbcFile WHERE AgreementsTypes = @AgreementType";

                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@AgreementType", agreementType);

                        using (SQLiteDataAdapter da = new SQLiteDataAdapter(cmd))
                        {
                            DataTable dt = new DataTable();
                            da.Fill(dt);

                            // 清空协议列表
                            communicationprotocols.Properties.Items.Clear();

                            foreach (DataRow row in dt.Rows)
                            {
                                string protocolName = row.ItemArray[0].ToString();
                                if (!communicationprotocols.Properties.Items.Contains(protocolName))
                                {
                                    communicationprotocols.Properties.Items.Add(protocolName);
                                }
                            }

                            // 默认选择第一个协议
                            if (communicationprotocols.Properties.Items.Count > 0)
                            {
                                communicationprotocols.SelectedIndex = 0;
                            }
                            else
                            {
                                communicationprotocols.Text = string.Empty;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"加载数据库失败：{ex.Message}");
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (ValidateInput())
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        private void LoadData(DataRow row)
        {
            // 先设置设备类型，这会触发设备类型变更事件
            if (row.Table.Columns.Contains("DeviceType") && !string.IsNullOrEmpty(row["DeviceType"].ToString()))
            {
                deviceType.Text = row["DeviceType"].ToString();
            }
            else
            {
                deviceType.SelectedIndex = 0;
            }

            devicename.Text = row["DeviceName"].ToString();

            // 设置通讯类型
            if (row.Table.Columns.Contains("CanType") && !string.IsNullOrEmpty(row["CanType"].ToString()))
            {
                cantype.Text = row["CanType"].ToString();
            }

            // 根据通讯类型加载相应的配置
            if (cantype.Text == "ZCAN_CANETTCP")
            {
                deviceip.Text = row.Table.Columns.Contains("DeviceIP") ? row["DeviceIP"].ToString() : "";
                deviceport.Text = row.Table.Columns.Contains("DevicePort") ? row["DevicePort"].ToString() : "";
            }
            else if (cantype.Text == "RS485-MODBUS")
            {
                comPort.Text = row.Table.Columns.Contains("ComPort") ? row["ComPort"].ToString() : "";
                baudRate.Text = row.Table.Columns.Contains("BaudRate") ? row["BaudRate"].ToString() : "9600";
                dataBits.Text = row.Table.Columns.Contains("DataBits") ? row["DataBits"].ToString() : "8";
                parity.Text = row.Table.Columns.Contains("Parity") ? row["Parity"].ToString() : "无";
                stopBits.Text = row.Table.Columns.Contains("StopBits") ? row["StopBits"].ToString() : "1";
            }
            else if (cantype.Text == "RS232")
            {
                comPort.Text = row.Table.Columns.Contains("ComPort") ? row["ComPort"].ToString() : "";
                baudRate.Text = row.Table.Columns.Contains("BaudRate") ? row["BaudRate"].ToString() : "9600";
                dataBits.Text = row.Table.Columns.Contains("DataBits") ? row["DataBits"].ToString() : "8";
                parity.Text = row.Table.Columns.Contains("Parity") ? row["Parity"].ToString() : "无";
                stopBits.Text = row.Table.Columns.Contains("StopBits") ? row["StopBits"].ToString() : "1";
            }
            else if (cantype.Text == "USB-SCPI")
            {

            }

            deviceindex.Text = row["DeviceIndex"].ToString();
            canindex.Text = row["CanIndex"].ToString();
            acnumber.Text = row["ACNumber"].ToString();
            acaddress.Text = row["ACAddress"].ToString();
            dcnumber.Text = row["DCNumber"].ToString();
            dcaddress.Text = row["DCAddress"].ToString();
            communicationprotocols.Text = row["CommunicationProtocols"].ToString();
            whether.Text = row["Whether"].ToString();

            // 更新UI
            UpdateUI();
        }

        private bool ValidateInput()
        {
            // 验证设备类型
            if (string.IsNullOrWhiteSpace(deviceType.Text))
            {
                ShowError("设备类型不能为空！", deviceType);
                return false;
            }

            // 验证设备名称
            if (string.IsNullOrWhiteSpace(devicename.Text))
            {
                ShowError("设备名称不能为空！", devicename);
                return false;
            }

            // 验证CAN盒类型
            if (string.IsNullOrWhiteSpace(cantype.Text))
            {
                ShowError("通讯类型不能为空！", cantype);
                return false;
            }

            // 根据通讯类型验证相应的配置
            if (cantype.Text == "ZCAN_CANETTCP")
            {
                // 验证IP地址格式
                if (!string.IsNullOrWhiteSpace(deviceip.Text) &&
                    !System.Text.RegularExpressions.Regex.IsMatch(deviceip.Text,
                    @"^((25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(25[0-5]|2[0-4]\d|[01]?\d\d?)$"))
                {
                    ShowError("IP地址格式不正确！", deviceip);
                    return false;
                }

                // 验证端口号范围 (1-65535)
                if (!string.IsNullOrWhiteSpace(deviceport.Text) &&
                    (!int.TryParse(deviceport.Text, out int port) || port < 1 || port > 65535))
                {
                    ShowError("端口号必须为1-65535之间的数字！", deviceport);
                    return false;
                }

                // 验证设备索引
                if (string.IsNullOrWhiteSpace(deviceindex.Text))
                {
                    ShowError("设备索引不能为空！", deviceindex);
                    return false;
                }

                // 验证CAN索引
                if (string.IsNullOrWhiteSpace(canindex.Text))
                {
                    ShowError("CAN索引不能为空！", canindex);
                    return false;
                }
            }
            else if (cantype.Text == "RS485-MODBUS")
            {
                // 验证串口号
                if (string.IsNullOrWhiteSpace(comPort.Text))
                {
                    ShowError("串口号不能为空！", comPort);
                    return false;
                }

                // 验证波特率
                if (string.IsNullOrWhiteSpace(baudRate.Text) ||
                    !int.TryParse(baudRate.Text, out int baud) || baud <= 0)
                {
                    ShowError("波特率必须为有效的正数！", baudRate);
                    return false;
                }

                // 验证数据位
                if (string.IsNullOrWhiteSpace(dataBits.Text) ||
                    !int.TryParse(dataBits.Text, out int bits) || bits < 5 || bits > 8)
                {
                    ShowError("数据位必须是5-8之间的整数！", dataBits);
                    return false;
                }

                // 验证校验位
                if (string.IsNullOrWhiteSpace(parity.Text))
                {
                    ShowError("校验位不能为空！", parity);
                    return false;
                }

                // 验证停止位
                if (string.IsNullOrWhiteSpace(stopBits.Text))
                {
                    ShowError("停止位不能为空！", stopBits);
                    return false;
                }
            }
            else if (cantype.Text == "RS232")
            {
                // 验证串口号
                if (string.IsNullOrWhiteSpace(comPort.Text))
                {
                    ShowError("串口号不能为空！", comPort);
                    return false;
                }

                // 验证波特率
                if (string.IsNullOrWhiteSpace(baudRate.Text) ||
                    !int.TryParse(baudRate.Text, out int baud) || baud <= 0)
                {
                    ShowError("波特率必须为有效的正数！", baudRate);
                    return false;
                }

                // 验证数据位
                if (string.IsNullOrWhiteSpace(dataBits.Text) ||
                    !int.TryParse(dataBits.Text, out int bits) || bits < 5 || bits > 8)
                {
                    ShowError("数据位必须是5-8之间的整数！", dataBits);
                    return false;
                }

                // 验证校验位
                if (string.IsNullOrWhiteSpace(parity.Text))
                {
                    ShowError("校验位不能为空！", parity);
                    return false;
                }

                // 验证停止位
                if (string.IsNullOrWhiteSpace(stopBits.Text))
                {
                    ShowError("停止位不能为空！", stopBits);
                    return false;
                }
            }
            else if (cantype.Text == "USB-SCPI")
            {
                // USB-SCPI不需要额外的网络或串口配置验证
                // 可以在这里添加特定于USB-SCPI的验证逻辑
            }

            // 如果设备类型是充放电设备，验证AC/DC配置
            if (deviceType.Text == "充放电设备")
            {
                // 验证AC通道数
                if (string.IsNullOrWhiteSpace(acnumber.Text))
                {
                    ShowError("AC通道数不能为空！", acnumber);
                    return false;
                }

                // 验证AC起始地址
                if (string.IsNullOrWhiteSpace(acaddress.Text))
                {
                    ShowError("AC起始地址不能为空！", acaddress);
                    return false;
                }

                // 验证DC通道数
                if (string.IsNullOrWhiteSpace(dcnumber.Text))
                {
                    ShowError("DC通道数不能为空！", dcnumber);
                    return false;
                }

                // 验证DC起始地址
                if (string.IsNullOrWhiteSpace(dcaddress.Text))
                {
                    ShowError("DC起始地址不能为空！", dcaddress);
                    return false;
                }
            }

            // 验证通讯协议
            //if (string.IsNullOrWhiteSpace(communicationprotocols.Text))
            //{
            //    ShowError("通讯协议不能为空！", communicationprotocols);
            //    return false;
            //}

            // 验证是否启用设备
            if (string.IsNullOrWhiteSpace(whether.Text))
            {
                ShowError("是否启用设备不能为空！", whether);
                return false;
            }

            return true;
        }

        private void ShowError(string message, Control focusControl)
        {
            XtraMessageBox.Show(message, "输入错误",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning);
            focusControl.Focus();
            if (focusControl is TextEdit txt) txt.SelectAll();
        }
    }
}