using DevExpress.XtraEditors;
using System.Data.SQLite;

namespace ChargeDebug.Form
{
    public partial class SaveDialogForm : XtraForm
    {
        private TextEdit txtFileName;
        private ComboBoxEdit cmbProtocolType;
        public string FileName;
        public string ProtocolType;
        private string dbcPath = "";

        public SaveDialogForm(string dbPath)
        {
            dbcPath = dbPath;
            InitializeComponent();
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.StartPosition = FormStartPosition.CenterParent;
            this.Size = new Size(360, 160); // 调整窗体大小以适应新控件
            this.Text = "新建文件";

            // 文件名称标签和文本框
            var labelFileName = new LabelControl
            {
                Text = "文件名称:",
                Width = 80,
                Height = 30,
                Location = new Point(40, 10)
            };

            txtFileName = new TextEdit
            {
                Name = "txtFileName",
                Width = 200,
                Height = 30,
                Location = new Point(labelFileName.Location.X + labelFileName.Width, 8)
            };

            // 协议类型标签和下拉框
            var labelProtocolType = new LabelControl
            {
                Text = "协议类型:",
                Width = 80,
                Height = 30,
                Location = new Point(40, 45)
            };

            cmbProtocolType = new ComboBoxEdit
            {
                Name = "cmbProtocolType",
                Width = 200,
                Height = 30,
                Location = new Point(labelProtocolType.Location.X + labelProtocolType.Width, 43)
            };

            // 添加协议类型选项
            cmbProtocolType.Properties.Items.AddRange(new string[] { "CAN总线", "MODBUS" });
            cmbProtocolType.SelectedIndex = 0; // 默认选择CAN总线

            // 按钮
            var btnOK = new SimpleButton
            {
                Text = "确认",
                Width = 100,
                Height = 30,
                Location = new Point(50, 80)
            };

            var btnCancel = new SimpleButton
            {
                Text = "取消",
                Width = 100,
                Height = 30,
                Location = new Point(210, 80)
            };

            btnOK.Click += (s, e) => OK();
            btnCancel.Click += (s, e) => Cancel();

            this.Controls.AddRange(new Control[] {
                labelFileName, txtFileName,
                labelProtocolType, cmbProtocolType,
                btnOK, btnCancel
            });
        }

        // 检查文件是否存在的数据库查询
        private bool CheckDbcFileExists(string fileName)
        {
            using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
            {
                conn.Open();
                const string checkSql = "SELECT COUNT(1) FROM DbcFile WHERE DbcFileName = @name";

                using (var cmd = new SQLiteCommand(checkSql, conn))
                {
                    cmd.Parameters.AddWithValue("@name", fileName);
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
            }
        }

        private void OK()
        {
            // 判断是否输入信息
            if (string.IsNullOrWhiteSpace(this.txtFileName.Text.Trim()))
            {
                MessageBox.Show("请输入文件名称！", "温馨提示");
                this.txtFileName.Focus();
                return;
            }

            FileName = this.txtFileName.Text.Trim();
            ProtocolType = cmbProtocolType.SelectedItem.ToString();

            if (!CheckDbcFileExists(FileName))
                DialogResult = DialogResult.OK;
            else
                XtraMessageBox.Show("文件名重复！");
        }

        private void Cancel()
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}