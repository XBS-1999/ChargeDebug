using DevExpress.Utils;
using DevExpress.XtraEditors;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Text;

#pragma warning disable
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
        private CheckEdit chkRemember;
        private SimpleButton btnLogin;
        private SimpleButton btnCancel;
        private LabelControl lblTitle;
        private string dbPath;

        // 配置文件路径
        private string configFilePath;

        public LoginForm(string dbcPath)
        {
            dbPath = dbcPath;
            // 设置配置文件路径
            configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "login_config.ini");
            InitializeComponent();
            InitializeUI();
            LoadSavedCredentials(); // 加载保存的凭据
        }

        private void InitializeUI()
        {
            // 窗体设置
            this.Text = "";
            this.Size = new Size(450, 300);
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
            txtUsername.Location = new Point(180, 85);
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
            txtPassword.Location = new Point(180, 135);
            txtPassword.Size = new Size(150, 30);
            txtPassword.Enter += (s, e) => txtPassword.BackColor = Color.AliceBlue;
            txtPassword.Leave += (s, e) => txtPassword.BackColor = Color.White;

            // 记住密码复选框
            chkRemember = new CheckEdit();
            chkRemember.Text = "记住密码";
            chkRemember.Properties.Appearance.Font = new Font("Tahoma", 10F);
            chkRemember.Location = new Point(180, 185);
            chkRemember.Size = new Size(150, 25);
            chkRemember.CheckedChanged += ChkRemember_CheckedChanged;

            // 登录按钮
            btnLogin = new SimpleButton();
            btnLogin.Text = "登 录";
            btnLogin.Appearance.Font = new Font("Tahoma", 12F, FontStyle.Bold);
            btnLogin.Appearance.BackColor = Color.FromArgb(0, 114, 198);
            btnLogin.Appearance.ForeColor = Color.White;
            btnLogin.Appearance.Options.UseBackColor = true;
            btnLogin.Appearance.Options.UseForeColor = true;
            btnLogin.Size = new Size(150, 36);
            btnLogin.Location = new Point(70, 220);
            btnLogin.Click += BtnLogin_Click;

            // 取消按钮
            btnCancel = new SimpleButton();
            btnCancel.Text = "取 消";
            btnCancel.Appearance.Font = new Font("Microsoft YaHei UI", 10F);
            btnCancel.Appearance.BackColor = Color.FromArgb(240, 240, 240);
            btnCancel.Size = new Size(150, 36);
            btnCancel.Location = new Point(230, 220);
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
            this.Controls.Add(chkRemember);
            this.Controls.Add(btnLogin);
            this.Controls.Add(btnCancel);

            // 设置回车键触发登录
            this.AcceptButton = btnLogin;
            this.CancelButton = btnCancel;
        }

        /// <summary>
        /// 记住密码复选框状态改变事件
        /// </summary>
        private void ChkRemember_CheckedChanged(object sender, EventArgs e)
        {
            // 如果取消勾选记住密码，清除已保存的密码
            if (!chkRemember.Checked)
            {
                ClearSavedPassword();
            }
        }

        /// <summary>
        /// 加载保存的凭据
        /// </summary>
        private void LoadSavedCredentials()
        {
            try
            {
                if (File.Exists(configFilePath))
                {
                    string[] lines = File.ReadAllLines(configFilePath);
                    if (lines.Length >= 1 && !string.IsNullOrWhiteSpace(lines[0]))
                    {
                        txtUsername.Text = lines[0].Trim();

                        // 如果有保存的密码（第二行）
                        if (lines.Length >= 2 && !string.IsNullOrWhiteSpace(lines[1]))
                        {
                            string encryptedPassword = lines[1].Trim();
                            string decryptedPassword = DecryptPassword(encryptedPassword);
                            if (!string.IsNullOrEmpty(decryptedPassword))
                            {
                                txtPassword.Text = decryptedPassword;
                                chkRemember.Checked = true;
                                // 焦点移到登录按钮，因为密码已自动填充
                                btnLogin.Focus();
                                return;
                            }
                        }
                    }
                }
                // 如果没有保存的密码或读取失败，焦点在用户名框
                if (string.IsNullOrEmpty(txtUsername.Text))
                {
                    txtUsername.Focus();
                }
                else
                {
                    txtPassword.Focus();
                }
            }
            catch (Exception ex)
            {
                // 如果读取配置文件失败，不影响主要功能
                System.Diagnostics.Debug.WriteLine($"加载保存的凭据失败: {ex.Message}");
                txtUsername.Focus();
            }
        }

        /// <summary>
        /// 保存凭据到配置文件
        /// </summary>
        /// <param name="username">用户名</param>
        /// <param name="password">密码</param>
        private void SaveCredentials(string username, string password)
        {
            try
            {
                // 确保目录存在
                string directory = Path.GetDirectoryName(configFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                List<string> lines = new List<string>();
                lines.Add(username.Trim());

                if (chkRemember.Checked && !string.IsNullOrEmpty(password))
                {
                    string encryptedPassword = EncryptPassword(password);
                    lines.Add(encryptedPassword);
                }
                else
                {
                    lines.Add(""); // 空密码行
                }

                // 保存到文件
                File.WriteAllLines(configFilePath, lines, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // 如果保存失败，不影响主要功能
                System.Diagnostics.Debug.WriteLine($"保存凭据失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 清除保存的密码
        /// </summary>
        private void ClearSavedPassword()
        {
            try
            {
                if (File.Exists(configFilePath))
                {
                    string[] lines = File.ReadAllLines(configFilePath);
                    if (lines.Length >= 1)
                    {
                        // 只保留用户名，清除密码
                        File.WriteAllLines(configFilePath, new string[] { lines[0], "" }, Encoding.UTF8);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"清除保存的密码失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加密密码（简单的Base64编码，实际项目中应该使用更安全的加密方式）
        /// </summary>
        /// <param name="password">明文密码</param>
        /// <returns>加密后的密码</returns>
        private string EncryptPassword(string password)
        {
            try
            {
                // 使用简单的可逆加密（Base64）
                // 注意：在生产环境中应该使用更安全的加密方式，如AES
                byte[] plainTextBytes = Encoding.UTF8.GetBytes(password);
                return Convert.ToBase64String(plainTextBytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 解密密码
        /// </summary>
        /// <param name="encryptedPassword">加密后的密码</param>
        /// <returns>解密后的密码</returns>
        private string DecryptPassword(string encryptedPassword)
        {
            try
            {
                // 解密Base64编码的密码
                byte[] base64EncodedBytes = Convert.FromBase64String(encryptedPassword);
                return Encoding.UTF8.GetString(base64EncodedBytes);
            }
            catch
            {
                return string.Empty;
            }
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
                                string inputHash = HashPassword(password);

                                if (storedHash == inputHash)
                                {
                                    // 设置公共属性
                                    UserPermissions = reader["UserPermissions"].ToString();
                                    Username = username;

                                    // 保存凭据到配置文件
                                    SaveCredentials(username, password);

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

        /// <summary>
        /// 密码哈希（用于数据库验证）
        /// </summary>
        /// <param name="password">明文密码</param>
        /// <returns>哈希后的密码</returns>
        private string HashPassword(string password)
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