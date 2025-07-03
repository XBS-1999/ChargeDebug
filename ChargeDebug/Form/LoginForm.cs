using DevExpress.Utils;
using DevExpress.XtraEditors;
using System.Data.SQLite;
using System.Security.Cryptography;
using System.Text;

namespace ChargeDebug.Form
{
    public partial class LoginForm : XtraForm
    {
        // 添加公共属性
        public string DBPath { get; private set; }
        public string UserPermissions { get; private set; }
        public string Username { get; private set; }

        private TextEdit txtUsername;
        private TextEdit txtPassword;
        private SimpleButton btnLogin;
        private SimpleButton btnCancel;
        private LabelControl lblTitle;
        private string dbPath;

        public LoginForm(string dbcPath)
        {
            dbPath = dbcPath;
            InitializeComponent();
            InitializeUI();
        }

        private void InitializeUI()
        {
            // 窗体设置
            this.Text = "";
            this.Size = new Size(450, 320);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;
            this.Padding = new Padding(20);

            // 标题标签
            lblTitle = new LabelControl();
            lblTitle.Text = "充放电调试系统";
            lblTitle.Appearance.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            lblTitle.Appearance.ForeColor = Color.FromArgb(0, 114, 198); // 主题蓝色
            lblTitle.Appearance.TextOptions.HAlignment = HorzAlignment.Center;
            lblTitle.AutoSizeMode = LabelAutoSizeMode.Vertical;
            lblTitle.Dock = DockStyle.Top;
            lblTitle.Height = 50;
            lblTitle.Margin = new Padding(0, 0, 0, 20);

            // 用户名输入
            LabelControl lblUser = new LabelControl();
            lblUser.Text = "用户名:";
            lblUser.Appearance.Font = new Font("Tahoma", 12F);
            lblUser.Location = new Point(120, 90);

            txtUsername = new TextEdit();
            txtUsername.Properties.NullValuePrompt = "请输入用户名";
            txtUsername.Properties.NullValuePromptShowForEmptyValue = true;
            txtUsername.Properties.Appearance.Font = new Font("Tahoma", 12F);
            txtUsername.Location = new Point(180, 87);
            txtUsername.Size = new Size(150, 30);
            txtUsername.Enter += (s, e) => txtUsername.BackColor = Color.AliceBlue;
            txtUsername.Leave += (s, e) => txtUsername.BackColor = Color.White;

            // 密码输入
            LabelControl lblPass = new LabelControl();
            lblPass.Text = "密　码:";
            lblPass.Appearance.Font = new Font("Tahoma", 12F);
            lblPass.Location = new Point(120, 140);

            txtPassword = new TextEdit();
            txtPassword.Properties.PasswordChar = '●';
            txtPassword.Properties.NullValuePrompt = "请输入密码";
            txtPassword.Properties.NullValuePromptShowForEmptyValue = true;
            txtPassword.Properties.Appearance.Font = new Font("Tahoma", 12F);
            txtPassword.Location = new Point(180, 137);
            txtPassword.Size = new Size(150, 30);
            txtPassword.Enter += (s, e) => txtPassword.BackColor = Color.AliceBlue;
            txtPassword.Leave += (s, e) => txtPassword.BackColor = Color.White;

            // 登录按钮
            btnLogin = new SimpleButton();
            btnLogin.Text = "登 录";
            btnLogin.Appearance.Font = new Font("Tahoma", 12F, FontStyle.Bold);
            btnLogin.Appearance.BackColor = Color.FromArgb(0, 114, 198);
            btnLogin.Appearance.ForeColor = Color.White;
            btnLogin.Appearance.Options.UseBackColor = true;
            btnLogin.Appearance.Options.UseForeColor = true;
            btnLogin.Size = new Size(150, 36);
            btnLogin.Location = new Point(70, 200);
            btnLogin.Click += BtnLogin_Click;

            // 取消按钮
            btnCancel = new SimpleButton();
            btnCancel.Text = "取 消";
            btnCancel.Appearance.Font = new Font("Microsoft YaHei UI", 10F);
            btnCancel.Appearance.BackColor = Color.FromArgb(240, 240, 240);
            btnCancel.Size = new Size(150, 36);
            btnCancel.Location = new Point(230, 200);
            btnCancel.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            // 添加控件
            this.Controls.Add(lblTitle);
            this.Controls.Add(lblUser);
            this.Controls.Add(txtUsername);
            this.Controls.Add(lblPass);
            this.Controls.Add(txtPassword);
            this.Controls.Add(btnLogin);
            this.Controls.Add(btnCancel);

            // 设置回车键触发登录
            this.AcceptButton = btnLogin;
            this.CancelButton = btnCancel;
        }

        private void BtnLogin_Click(object? sender, EventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Text;

            if (string.IsNullOrWhiteSpace(username))
            {
                XtraMessageBox.Show("请输入用户名", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtUsername.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                XtraMessageBox.Show("请输入密码", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtPassword.Focus();
                return;
            }

            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    string query = "SELECT PassWord, UserPermissions FROM User WHERE UserName = @UserName";

                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserName", username);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                string storedHash = reader["PassWord"].ToString();
                                string inputHash = EncryptPassword(password);

                                if (storedHash == inputHash)
                                {
                                    // 设置公共属性
                                    UserPermissions = reader["UserPermissions"].ToString();
                                    Username = username;

                                    // 设置登录成功状态
                                    this.DialogResult = DialogResult.OK;
                                    //LogService.Log($"用户 {username} 登录成功，权限: {UserPermissions}");
                                    return;
                                }
                            }
                        }
                    }
                }
                XtraMessageBox.Show("用户名或密码错误", "登录失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                txtPassword.SelectAll();
                txtPassword.Focus();
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"登录失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string EncryptPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
