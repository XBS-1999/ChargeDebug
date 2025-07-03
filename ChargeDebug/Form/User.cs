using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using System.Data;
using System.Data.SQLite;

namespace ChargeDebug.Form
{
    public partial class User : XtraUserControl
    {
        private GridControl gridControl;
        private GridView gridview;
        private string dbPath = "";

        public User(string dbcPath)
        {
            dbPath = dbcPath;
            InitializeComponent();
            InitializeUI();
            this.Load += User_Load;
        }

        private void User_Load(object? sender, EventArgs e)
        {
            // 加载数据
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                string connectionString = $"Data Source={dbPath};Version=3;";
                DataTable dataTable = new DataTable();

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    const string query = "SELECT * FROM User";
                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(dataTable);
                        }
                    }
                }

                gridControl.DataSource = dataTable;
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"Error loading data: {ex.Message}", "Error",
                                 MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InitializeUI()
        {
            gridControl = new GridControl();
            gridview = new GridView();
            gridControl.MainView = gridview;
            gridControl.Dock = DockStyle.Left;
            gridControl.Width = 1790;
            gridview.OptionsView.ShowGroupPanel = false;
            gridview.OptionsBehavior.Editable = false;
            // 确保整个网格的单元格内容居中
            gridview.Appearance.Row.TextOptions.HAlignment = HorzAlignment.Center;
            gridview.Appearance.HeaderPanel.TextOptions.HAlignment = HorzAlignment.Center;


            GridColumn number = new GridColumn();
            number.FieldName = "Number";
            number.Caption = "序号";
            number.Visible = true;
            //number.Width = 80;

            GridColumn username = new GridColumn();
            username.FieldName = "UserName";
            username.Caption = "用户名";
            username.Visible = true;
            //username.Width = 100;

            GridColumn password = new GridColumn();
            password.FieldName = "PassWord";
            password.Caption = "用户密码";
            password.Visible = false;

            GridColumn userpermissions = new GridColumn();
            userpermissions.FieldName = "UserPermissions";
            userpermissions.Caption = "用户权限";
            userpermissions.Visible = true;
            //userpermissions.Width = 100;

            GridColumn userphone = new GridColumn();
            userphone.FieldName = "UserPhone";
            userphone.Caption = "用户电话";
            userphone.Visible = true;
            //userphone.Width = 100;

            GridColumn useremail = new GridColumn();
            useremail.FieldName = "UserEmail";
            useremail.Caption = "用户邮箱";
            useremail.Visible = true;
            //useremail.Width = 100;

            gridview.Columns.AddRange(new[] { number, username, password, userpermissions, userphone, useremail });

            SimpleButton simpleButton = new SimpleButton();
            simpleButton.Location = new Point(10, 20);
            simpleButton.Name = "simpleButton";
            simpleButton.Size = new Size(100, 30);
            simpleButton.TabIndex = 0;
            simpleButton.Text = "增加用户";

            SimpleButton simpleButton1 = new SimpleButton();
            simpleButton1.Location = new Point(10, 70);
            simpleButton1.Name = "simpleButton1";
            simpleButton1.Size = new Size(100, 30);
            simpleButton1.TabIndex = 1;
            simpleButton1.Text = "编辑用户";

            SimpleButton simpleButton2 = new SimpleButton();
            simpleButton2.Location = new Point(10, 120);
            simpleButton2.Name = "simpleButton2";
            simpleButton2.Size = new Size(100, 30);
            simpleButton2.TabIndex = 2;
            simpleButton2.Text = "删除用户";

            PanelControl panelControl = new PanelControl();
            panelControl.Dock = DockStyle.Right;
            panelControl.Width = 120;
            panelControl.Controls.AddRange(new[] { simpleButton, simpleButton1, simpleButton2 });

            this.Controls.Add(gridControl);
            this.Controls.Add(panelControl);

            // 为按钮添加点击事件
            simpleButton.Click += AddUser_Click;
            simpleButton1.Click += EditUser_Click;
            simpleButton2.Click += DeleteUser_Click;
        }

        private void DeleteUser_Click(object? sender, EventArgs e)
        {
            try
            {
                var selectedRowHandle = gridview.FocusedRowHandle;

                if (selectedRowHandle == GridControl.InvalidRowHandle)
                {
                    XtraMessageBox.Show("请先选择要删除的用户！", "提示",
                                     MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 获取用户ID
                var userId = gridview.GetRowCellValue(selectedRowHandle, "Number").ToString();

                // 确认对话框
                if (XtraMessageBox.Show($"确定要删除选中的用户吗？", "确认删除",
                                      MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                // 执行删除操作
                using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    const string query = "DELETE FROM User WHERE Number = @Number";

                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Number", userId);
                        cmd.ExecuteNonQuery();
                    }
                }

                // 刷新数据
                LoadData();
                XtraMessageBox.Show("用户删除成功！", "提示",
                                 MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"删除失败：{ex.Message}", "错误",
                                 MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void EditUser_Click(object? sender, EventArgs e)
        {
            try
            {
                var selectedRowHandle = gridview.FocusedRowHandle;

                if (selectedRowHandle == GridControl.InvalidRowHandle)
                {
                    XtraMessageBox.Show("请先选择要编辑的用户！", "提示",
                                     MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 获取当前用户数据
                DataRow row = ((DataRowView)gridview.GetRow(selectedRowHandle)).Row;

                // 创建并显示编辑表单 (需实现UserEditForm)
                using (var form = new UserEditForm(row))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        // 执行更新操作
                        using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                        {
                            conn.Open();
                            const string query = @"UPDATE User SET
                                UserName = @UserName,
                                PassWord = @PassWord,
                                UserPermissions = @UserPermissions,
                                UserPhone = @UserPhone,
                                UserEmail = @UserEmail
                                WHERE Number = @Number";

                            using (var cmd = new SQLiteCommand(query, conn))
                            {
                                cmd.Parameters.AddWithValue("@Number", row["Number"]);
                                cmd.Parameters.AddWithValue("@UserName", form.UserName);
                                cmd.Parameters.AddWithValue("@PassWord", form.PassWord);
                                cmd.Parameters.AddWithValue("@UserPermissions", form.UserPermissions);
                                cmd.Parameters.AddWithValue("@UserPhone", form.UserPhone);
                                cmd.Parameters.AddWithValue("@UserEmail", form.UserEmail);

                                cmd.ExecuteNonQuery();
                            }
                        }

                        // 刷新数据
                        LoadData();
                        XtraMessageBox.Show("用户修改成功！", "提示",
                                           MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"修改失败：{ex.Message}", "错误",
                                 MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddUser_Click(object? sender, EventArgs e)
        {
            try
            {
                // 获取当前最大用户号
                int maxNumber = 0;
                using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    const string query = "SELECT MAX(Number) FROM User";

                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        object result = cmd.ExecuteScalar();
                        if (result != DBNull.Value) maxNumber = Convert.ToInt32(result);
                    }
                }

                // 生成新用户号
                int newNumber = maxNumber + 1;

                // 打开用户编辑窗口
                using (var form = new UserEditForm(newNumber))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        // 执行插入操作
                        using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                        {
                            conn.Open();
                            const string insertQuery = @"INSERT INTO User 
                            (Number, UserName, PassWord, UserPermissions, UserPhone, UserEmail)
                            VALUES 
                            (@Number, @UserName, @PassWord, @UserPermissions, @UserPhone, @UserEmail)";

                            using (var cmd = new SQLiteCommand(insertQuery, conn))
                            {
                                cmd.Parameters.AddWithValue("@Number", newNumber);
                                cmd.Parameters.AddWithValue("@UserName", form.UserName);
                                cmd.Parameters.AddWithValue("@PassWord", form.PassWord);
                                cmd.Parameters.AddWithValue("@UserPermissions", form.UserPermissions);
                                cmd.Parameters.AddWithValue("@UserPhone", form.UserPhone);
                                cmd.Parameters.AddWithValue("@UserEmail", form.UserEmail);

                                cmd.ExecuteNonQuery();
                            }
                        }

                        LoadData();
                        XtraMessageBox.Show("用户添加成功！", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"添加失败：{ex.Message}", "错误",
                                 MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
