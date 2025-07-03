using DevExpress.XtraEditors;
using System.Data.SQLite;

namespace ChargeDebug.Form
{
    public partial class SaveDialogForm : XtraForm
    {
        private TextEdit txtFileName;
        public string FileName;
        private string dbcPath = "";

        public SaveDialogForm(string dbPath)
        {
            dbcPath = dbPath;
            InitializeComponent();
            InitializeUI();
            this.StartPosition = FormStartPosition.CenterParent;
        }

        private void InitializeUI()
        {
            var labelControl = new LabelControl
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
                Location = new Point(labelControl.Location.X + labelControl.Width, 8)
            };

            var btnOK = new SimpleButton
            {
                Text = "确认",
                Width = 100,
                Height = 30,
                Location = new Point(80, 50)
            };

            var btnCancel = new SimpleButton
            {
                Text = "取消",
                Width = 100,
                Height = 30,
                Location = new Point(220, 50)
            };

            btnOK.Click += (s, e) => OK();
            btnCancel.Click += (s, e) => Cancel();

            this.Controls.Add(labelControl);
            this.Controls.Add(txtFileName);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);
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
            //判断是否输入信息
            if (this.txtFileName.Text.Trim() == "")
            {
                MessageBox.Show("请输入文件名称！", "温馨提示");
                this.txtFileName.Focus();
            }
            else
            {
                FileName = this.txtFileName.Text.Trim();

                if (!CheckDbcFileExists(FileName))
                    DialogResult = DialogResult.OK;
                else
                    XtraMessageBox.Show("文件名重复！"); ;
            }
        }

        private void Cancel()
        {
            this.Close();
        }
    }
}
