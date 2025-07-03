using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using System.Data;
using System.Data.SQLite;

namespace ChargeDebug.Form
{
    public partial class DeviceEditForm : XtraForm
    {
        // 表单控件定义（根据实际需要添加）
        private TextEdit devicenumber;
        private ComboBoxEdit cantype;
        private TextEdit deviceip;
        private TextEdit deviceport;
        private TextEdit deviceindex;
        private TextEdit canindex;
        private TextEdit acnumber;
        private TextEdit dcnumber;
        private ComboBoxEdit communicationprotocols;
        private ComboBoxEdit whether;

        public string DeviceNumber => devicenumber.Text;
        public string CanType => cantype.Text;
        public string DeviceIP => deviceip.Text;
        public string DevicePort => deviceport.Text;
        public string DeviceIndex => deviceindex.Text;
        public string CanIndex => canindex.Text;
        public string ACNumber => acnumber.Text;
        public string DCNumber => dcnumber.Text;
        public string CommunicationProtocols => communicationprotocols.Text;
        public string Whether => whether.Text;

        private string dbcPath = "";


        // 构造函数（新增模式）
        public DeviceEditForm(string deviceNumber,string dbPath)
        {
            dbcPath = dbPath;
            InitializeComponent();
            this.Text = "增加设备";
            InitializeControls();
            devicenumber.Text = deviceNumber;
        }

        // 构造函数（编辑模式）
        public DeviceEditForm(DataRow row, string dbPath)
        {
            dbcPath = dbPath;
            InitializeComponent();
            this.Text = "编辑设备";
            InitializeControls();
            LoadData(row);
        }

        private void InitializeControls()
        {
            // 初始化控件布局和配置
            this.Size = new Size(700, 300);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;

            // 创建并配置控件
            LabelControl labelcontrol1 = new LabelControl { Text = "设备序号:", Location = new Point(70, 22) };
            devicenumber = new TextEdit{ Location = new Point(150, 20), Width = 150 };
            devicenumber.Properties.ReadOnly = true;
            devicenumber.Properties.AllowFocused = false;     // 禁止获得焦点
            LabelControl labelcontrol2 = new LabelControl { Text = "CAN盒类型:", Location = new Point(340, 22) };
            cantype = new ComboBoxEdit { Location = new Point(450, 20), Width = 150 };
            LabelControl labelcontrol3 = new LabelControl { Text = "设备IP:", Location = new Point(70, 62) };
            deviceip = new TextEdit { Location = new Point(150, 60), Width = 150 };
            LabelControl labelcontrol4 = new LabelControl { Text = "设备端口:", Location = new Point(340, 62) };
            deviceport = new TextEdit { Location = new Point(450, 60), Width = 150 };
            LabelControl labelcontrol5 = new LabelControl { Text = "设备索引:", Location = new Point(70, 102) };
            deviceindex = new TextEdit { Location = new Point(150, 100), Width = 150 };
            LabelControl labelcontrol6 = new LabelControl { Text = "CAN索引:", Location = new Point(340, 102) };
            canindex = new TextEdit { Location = new Point(450, 100), Width = 150 };
            LabelControl labelcontrol7 = new LabelControl { Text = "AC数量:", Location = new Point(70, 142) };
            acnumber = new TextEdit { Location = new Point(150, 140), Width = 150 };
            LabelControl labelcontrol8 = new LabelControl { Text = "DC数量:", Location = new Point(340, 142) };
            dcnumber = new TextEdit { Location = new Point(450, 140), Width = 150 };
            LabelControl labelcontrol9 = new LabelControl { Text = "通讯协议:", Location = new Point(70, 182) };
            communicationprotocols = new ComboBoxEdit { Location = new Point(150, 180), Width = 150 };
            LabelControl labelcontrol10 = new LabelControl { Text = "是否启用设备:", Location = new Point(340, 182) };
            whether = new ComboBoxEdit { Location = new Point(450, 180), Width = 150 };

            cantype.Properties.Items.AddRange(new object[]
            { "CANET-2E-U" });
            cantype.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;

            whether.Properties.Items.AddRange(new object[]
            { "启用","禁用" });
            whether.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;

            communicationprotocols.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            
            //加载通讯协议列表
            LoadProtocol();

            // 添加确定/取消按钮
            SimpleButton btnOK = new SimpleButton
            {
                Text = "确定",
                DialogResult = DialogResult.None, // 先不直接返回OK
                Location = new Point(200, 230)
            };
            SimpleButton btnCancel = new SimpleButton
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(400, 230)
            };

            this.Controls.AddRange(new Control[]
            {
                labelcontrol1,labelcontrol2,labelcontrol3,labelcontrol4,labelcontrol5,labelcontrol6,labelcontrol7,labelcontrol8,labelcontrol9,labelcontrol10,
                devicenumber, cantype, deviceip, deviceport,deviceindex,canindex,acnumber,dcnumber,communicationprotocols,whether,
                btnOK,btnCancel
            });

            btnOK.Click += BtnOK_Click; // 添加点击事件
        }

        private void LoadProtocol()
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();
                    string query = "SELECT DbcFileName FROM DbcFile";

                    using (SQLiteDataAdapter da = new SQLiteDataAdapter(query, conn))
                    {
                        // 绑定数据源
                        DataTable dt = new DataTable();
                        da.Fill(dt);

                        communicationprotocols.Properties.Items.Clear();
                        foreach (DataRow row in dt.Rows)
                        {
                            communicationprotocols.Properties.Items.Add(row.ItemArray[0]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"加载数据库失败：{ex.Message}");
            }
        }

        private void BtnOK_Click(object? sender, EventArgs e)
        {
            if (ValidateInput())
            {
                this.DialogResult = DialogResult.OK; // 只有验证通过才关闭表单
                this.Close();
            }
        }

        private void LoadData(DataRow row)
        {
            devicenumber.Text = row["DeviceNumber"].ToString();
            cantype.Text = row["CanType"].ToString();
            deviceip.Text = row["DeviceIP"].ToString();
            deviceport.Text = row["DevicePort"].ToString();
            deviceindex.Text = row["DeviceIndex"].ToString();
            canindex.Text = row["CanIndex"].ToString();
            acnumber.Text = row["AcNumber"].ToString();
            dcnumber.Text = row["DcNumber"].ToString();
            communicationprotocols.Text = row["CommunicationProtocols"].ToString();
            whether.Text = row["Whether"].ToString();
        }

        private bool ValidateInput()
        {
            // 验证设备编号
            if (string.IsNullOrWhiteSpace(devicenumber.Text))
            {
                ShowError("设备编号不能为空！", devicenumber);
                return false;
            }
            // 验证CAN盒类型
            if (string.IsNullOrWhiteSpace(cantype.Text))
            {
                ShowError("CAN盒类型不能为空！", cantype);
                return false;
            }
            // 验证IP地址格式
            if (!System.Text.RegularExpressions.Regex.IsMatch(deviceip.Text,
                @"^((25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(25[0-5]|2[0-4]\d|[01]?\d\d?)$"))
            {
                ShowError("IP地址格式不正确！", deviceip);
                return false;
            }
            // 验证端口号范围 (1-65535)
            if (!int.TryParse(deviceport.Text, out int port) || port < 1 || port > 65535)
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
            // 验证AC通道数
            if (string.IsNullOrWhiteSpace(acnumber.Text))
            {
                ShowError("AC通道数不能为空！", acnumber);
                return false;
            }
            // 验证DC通道数
            if (string.IsNullOrWhiteSpace(dcnumber.Text))
            {
                ShowError("DC通道数不能为空！", dcnumber);
                return false;
            }
            // 验证通讯协议
            if (string.IsNullOrWhiteSpace(communicationprotocols.Text))
            {
                ShowError("通讯协议不能为空！", communicationprotocols);
                return false;
            }
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
            focusControl.Focus(); // 将焦点定位到错误控件
            if (focusControl is TextEdit txt) txt.SelectAll(); // 如果是文本框则全选内容
        }
    }
}
