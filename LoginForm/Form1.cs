using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraRichEdit.Model;

namespace LoginForm
{
    public partial class Loginform : XtraForm
    {
        private TextEdit txtUsername;
        private TextEdit txtPassword;
        private SimpleButton btnLogin;
        private SimpleButton btnCancel;
        private LabelControl lblUsername;
        private LabelControl lblPassword;
        private PictureEdit logoPicture;

        public string Username => txtUsername.Text.Trim();
        public string Password => txtPassword.Text;

        public Loginform()
        {
            InitializeComponent();
            InitializeUI();
            this.StartPosition = FormStartPosition.CenterScreen;
        }

        private void InitializeUI()
        {
            // 窗体基本设置
            this.Text = "用户登录";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(400, 300);
            this.Padding = new Padding(20);
            this.BackColor = Color.White;

            // 创建Logo图片区域 - 如果没有资源图片，可以使用默认图标
            logoPicture = new PictureEdit
            {
                Size = new Size(80, 80),
                Location = new Point((this.ClientSize.Width - 80) / 2, 20),
                Properties = {
                    SizeMode = PictureSizeMode.Zoom,
                    BorderStyle = BorderStyles.NoBorder
                }
            };

            // 设置默认图标（如果没有资源图片）
            logoPicture.Image = SystemIcons.Application.ToBitmap();

            // 用户名标签
            lblUsername = new LabelControl
            {
                Text = "用户名:",
                Location = new Point(50, 140),
                Font = new Font("Tahoma", 10),
                AutoSize = true
            };

            // 用户名输入框 - 修正此处
            txtUsername = new TextEdit
            {
                Location = new Point(100, 138),
                Size = new Size(230, 30),
                Font = new Font("Tahoma", 10),
                Properties = {
                    NullText = "请输入用户名", // 正确属性名
                    NullValuePromptShowForEmptyValue = true
                }
            };

            // 密码标签
            lblPassword = new LabelControl
            {
                Text = "密　码:",
                Location = new Point(50, 185),
                Font = new Font("Tahoma", 10),
                AutoSize = true
            };

            // 密码输入框 - 修正此处
            txtPassword = new TextEdit
            {
                Location = new Point(100, 183),
                Size = new Size(230, 30),
                Font = new Font("Tahoma", 10),
                Properties = {
                    PasswordChar = '•',
                    NullText = "1", // 正确属性名
                    NullValuePromptShowForEmptyValue = true,
                    UseSystemPasswordChar = true
                }
            };

            // 登录按钮
            btnLogin = new SimpleButton
            {
                Text = "登录",
                Location = new Point(120, 240),
                Size = new Size(100, 35),
                Font = new Font("Tahoma", 10, FontStyle.Bold),
                Appearance = {
                    BackColor = ColorTranslator.FromHtml("#2196F3"),
                    ForeColor = Color.White,
                    BorderColor = ColorTranslator.FromHtml("#1976D2")
                }
            };
            btnLogin.Click += btnLogin_Click;

            // 取消按钮
            btnCancel = new SimpleButton
            {
                Text = "取消",
                Location = new Point(250, 240),
                Size = new Size(100, 35),
                Font = new Font("Tahoma", 10),
                Appearance = {
                    BackColor = ColorTranslator.FromHtml("#F5F5F5"),
                    ForeColor = Color.Black
                }
            };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            // 设置回车键触发登录
            this.AcceptButton = btnLogin;
            this.CancelButton = btnCancel;

            // 添加控件到窗体
            this.Controls.AddRange(new Control[] {
                logoPicture,
                lblUsername,
                txtUsername,
                lblPassword,
                txtPassword,
                btnLogin,
                btnCancel
            });
        }

        private void btnLogin_Click(object sender, System.EventArgs e)
        {
            // 基本验证
            if (string.IsNullOrWhiteSpace(Username))
            {
                XtraMessageBox.Show("请输入用户名", "验证错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtUsername.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                XtraMessageBox.Show("请输入密码", "验证错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtPassword.Focus();
                return;
            }

            // 实际验证逻辑（需要根据您的系统实现）
            if (AuthenticateUser(Username, Password))
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                XtraMessageBox.Show("用户名或密码不正确", "登录失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                txtPassword.SelectAll();
                txtPassword.Focus();
            }
        }

        // 用户验证方法 - 需要根据您的实际系统实现
        private bool AuthenticateUser(string username, string password)
        {
            // 示例验证逻辑
            // 实际应用中应替换为数据库验证或AD验证

            // 示例1：硬编码测试账户（仅用于开发测试）
            // return (username == "admin" && password == "admin123");

            // 示例2：从配置文件读取（更安全的方式）
            /*
            string validUser = ConfigurationManager.AppSettings["AdminUser"];
            string validPass = ConfigurationManager.AppSettings["AdminPass"];
            return (username == validUser && password == validPass);
            */

            // 示例3：数据库验证
            /*
            using (var db = new AppDbContext())
            {
                var user = db.Users.FirstOrDefault(u => u.Username == username);
                if (user == null) return false;
                
                // 使用BCrypt验证密码
                return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
            }
            */

            // 测试时始终返回true
            return true;
        }
    }
}
