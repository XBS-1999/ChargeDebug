using Aspose.Pdf.Devices;
using DevExpress.Pdf.Native.BouncyCastle.Asn1.X509;
using DevExpress.XtraEditors;
using DevExpress.XtraRichEdit.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChargeDebug.Form
{
    public partial class UserEditForm : XtraForm
    {
        private readonly DataRow _userRow;
        private readonly bool _isNewUser;
        private string _originalPassword = ""; // 保存原始加密密码

        // 控件声明
        private TextEdit txtNumber;
        private TextEdit txtUserName;
        private TextEdit txtPassword;   // 密码输入框
        private TextEdit txtPassword1;  // 确认密码输入框
        private ComboBoxEdit cmbPermissions;
        private TextEdit txtPhone;
        private TextEdit txtEmail;

        // 公共属性以便主窗体获取值
        public string UserName => txtUserName.Text;
        public string PassWord => GetFinalPassword();
        public string UserPermissions => cmbPermissions.Text;
        public string UserPhone => txtPhone.Text;
        public string UserEmail => txtEmail.Text;

        // 用于添加新用户的构造函数
        public UserEditForm(int newNumber)
        {
            _isNewUser = true;
            InitializeComponent();
            this.Text = "增加用户";
            InitializeUI();
            txtNumber.Text = newNumber.ToString();
        }

        // 用于编辑现有用户的构造函数
        public UserEditForm(DataRow row)
        {
            _isNewUser = false;
            InitializeComponent();
            this.Text = "编辑用户";
            InitializeUI();
            LoadData(row);
        }

        private void LoadData(DataRow row)
        {
            txtNumber.Text = row["Number"].ToString();
            txtUserName.Text = row["UserName"].ToString();

            // 保存原始密码但不显示真实值
            _originalPassword = row["PassWord"].ToString();
            txtPassword.Text = "********"; // 显示占位符
            txtPassword1.Text = "********"; // 显示占位符

            cmbPermissions.Text = row["UserPermissions"].ToString();
            txtPhone.Text = row["UserPhone"].ToString();
            txtEmail.Text = row["UserEmail"].ToString();
        }

        private void InitializeUI()
        {
            // 初始化控件布局和配置
            this.Size = new Size(700, 300);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;

            // 创建并配置控件
            LabelControl labelcontrol1 = new LabelControl { Text = "序号:", Location = new Point(70, 22) };
            txtNumber = new TextEdit { Location = new Point(150, 20), Width = 150 };
            txtNumber.Properties.ReadOnly = true;
            txtNumber.Properties.AllowFocused = false;     // 禁止获得焦点

            LabelControl labelcontrol2 = new LabelControl { Text = "用户名:", Location = new Point(340, 22) };
            txtUserName = new TextEdit { Location = new Point(450, 20), Width = 150 };

            LabelControl labelcontrol3 = new LabelControl { Text = "用户密码:", Location = new Point(70, 62) };
            txtPassword = new TextEdit { Location = new Point(150, 60), Width = 150 };
            //txtPassword.Properties.PasswordChar = '*'; // 设置密码字符为星号
            txtPassword.Properties.UseSystemPasswordChar = true; // 使用系统密码字符

            LabelControl labelcontrol4 = new LabelControl { Text = "确认密码:", Location = new Point(340, 62) };
            txtPassword1 = new TextEdit { Location = new Point(450, 60), Width = 150 };
            //txtPassword1.Properties.PasswordChar = '*'; // 设置密码字符为星号
            txtPassword1.Properties.UseSystemPasswordChar = true; // 使用系统密码字符

            LabelControl labelcontrol5 = new LabelControl { Text = "用户权限:", Location = new Point(70, 102) };
            cmbPermissions = new ComboBoxEdit { Location = new Point(150, 100), Width = 150 };

            LabelControl labelcontrol6 = new LabelControl { Text = "用户电话:", Location = new Point(340, 102) };
            txtPhone = new TextEdit { Location = new Point(450, 100), Width = 150 };

            LabelControl labelcontrol7 = new LabelControl { Text = "用户邮箱:", Location = new Point(70, 142) };
            txtEmail = new TextEdit { Location = new Point(150, 140), Width = 150 };

            cmbPermissions.Properties.Items.AddRange(new object[]
            { "管理员", "操作员", "普通用户"});

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

            // 添加控件到表单
            this.Controls.AddRange(new Control[] {
                labelcontrol1,txtNumber,
                labelcontrol2, txtUserName,
                labelcontrol3, txtPassword,
                labelcontrol4, txtPassword1,
                labelcontrol5, cmbPermissions,
                labelcontrol6, txtPhone,
                labelcontrol7, txtEmail,
                btnOK, btnCancel
            });

            // 事件处理
            btnOK.Click += BtnOK_Click;
        }

        private void BtnOK_Click(object? sender, EventArgs e)
        {
            if (ValidateInput())
            {
                this.DialogResult = DialogResult.OK; // 只有验证通过才关闭表单
                this.Close();
            }
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(txtUserName.Text))
            {
                ShowError("用户名不能为空！", txtUserName);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtPassword.Text))
            {
                ShowError("密码不能为空！", txtPassword);
                return false;
            }

            if (txtPassword1.Text != txtPassword.Text)
            {
                ShowError("密码不一致！", txtPassword1);
                return false;
            }

            if (string.IsNullOrWhiteSpace(cmbPermissions.Text))
            {
                ShowError("请选择用户权限！", cmbPermissions);
                return false;
            }

            // 验证电话格式
            if (!string.IsNullOrWhiteSpace(txtPhone.Text)
                && !System.Text.RegularExpressions.Regex.IsMatch(txtPhone.Text, @"^1[3-9]\d{9}$"))
            {
                ShowError("请输入有效的手机号码！", txtPhone);
                return false;
            }

            // 验证邮箱格式
            if (!string.IsNullOrWhiteSpace(txtEmail.Text)
                && !System.Text.RegularExpressions.Regex.IsMatch(txtEmail.Text, @"^[\w-\.]+@([\w-]+\.)+[\w-]{2,4}$"))
            {
                ShowError("请输入有效的电子邮箱！", txtEmail);
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

        private bool ValidateUserPermissions()
        {
            // 如果当前用户是普通用户，禁止修改管理员账户
            if (!_isNewUser && _userRow["UserPermissions"].ToString() == "管理员")
            {
                // 获取当前登录用户权限（这里需要根据实际系统实现）
                string currentUserPermission = GetCurrentUserPermission();

                if (currentUserPermission != "管理员")
                {
                    return false;
                }
            }
            return true;
        }

        private string GetFinalPassword()
        {
            // 编辑用户且未修改密码时返回原始密码
            if (!_isNewUser && (string.IsNullOrWhiteSpace(txtPassword.Text) || txtPassword.Text == "********"))
            {
                return _originalPassword;
            }
            return EncryptPassword(txtPassword.Text);
        }

        private string EncryptPassword(string password)
        {
            // 使用SHA256哈希算法加密密码
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

        private string GetCurrentUserPermission()
        {
            // 这里应该根据实际系统获取当前登录用户的权限
            // 简化实现：返回"管理员"用于演示
            return "管理员";
        }
    }
}
