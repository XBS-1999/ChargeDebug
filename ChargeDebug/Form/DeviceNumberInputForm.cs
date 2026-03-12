using DevExpress.XtraEditors;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class DeviceNumberInputForm : XtraForm
    {
        // 控件声明
        private LabelControl lblDeviceNumber;
        private TextEdit txtDeviceNumber;
        private SimpleButton btnOK;
        private SimpleButton btnCancel;

        // 公共属性
        public string DeviceNumber { get; private set; }
        public bool OverwriteExisting { get; private set; }

        public DeviceNumberInputForm(string text)
        {
            InitializeComponent();
            InitializeUI(text);
        }

        // 初始化控件
        private void InitializeUI(string text)
        {
            // 窗体设置
            this.Text = $"输入{text}";
            this.Size = new Size(400, 150);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Padding = new Padding(10);

            // 设备编号标签
            lblDeviceNumber = new LabelControl();
            lblDeviceNumber.Text = $"{text}:";
            lblDeviceNumber.Location = new Point(10, 25);
            lblDeviceNumber.Parent = this;

            // 设备编号文本框
            txtDeviceNumber = new TextEdit();
            txtDeviceNumber.Location = new Point(90, 22);
            txtDeviceNumber.Size = new Size(280, 20);
            txtDeviceNumber.Parent = this;
            txtDeviceNumber.Properties.MaxLength = 50;
            txtDeviceNumber.KeyDown += TxtDeviceNumber_KeyDown;

            // 确定按钮
            btnOK = new SimpleButton();
            btnOK.Text = "确定";
            btnOK.Size = new Size(80, 30);
            btnOK.Location = new Point(80, 70);
            btnOK.Parent = this;
            btnOK.Click += BtnOK_Click;

            // 取消按钮
            btnCancel = new SimpleButton();
            btnCancel.Text = "取消";
            btnCancel.Size = new Size(80, 30);
            btnCancel.Location = new Point(240, 70);
            btnCancel.Parent = this;
            btnCancel.Click += BtnCancel_Click;

            // 设置回车和ESC键响应
            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }

        // 确定按钮点击事件
        private void BtnOK_Click(object sender, EventArgs e)
        {
            DeviceNumber = txtDeviceNumber.Text.Trim();

            if (string.IsNullOrEmpty(DeviceNumber))
            {
                XtraMessageBox.Show("请输入设备编号!", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtDeviceNumber.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        // 取消按钮点击事件
        private void BtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        // 文本框按键事件（支持回车键确认）
        private void TxtDeviceNumber_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                BtnOK_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}