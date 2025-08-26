using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraLayout;
using DevExpress.XtraLayout.Utils;
using DevExpress.Utils;
using DevExpress.Utils.Layout;
using System.Data.SQLite;
<<<<<<< HEAD
using System.Data;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid.Columns;
using DevExpress.Data;
using DevExpress.XtraGrid.Views.Grid.ViewInfo;
=======
using DevExpress.XtraEditors.Controls;
using System.Data;
>>>>>>> 9a38e72691fb335083a79d2214f9d102a7fa3eb9

namespace ProjectManagement
{
    public partial class ProjectManagement : XtraForm
    {
        private GridControl gridControl;
        private GridView gridView;
        private SimpleButton btnNew;
        private SimpleButton btnEdit;
        private SimpleButton btnDelete;
        private SQLiteConnection connection;
        private string dbPath;

<<<<<<< HEAD
        public ProjectManagement(string dbPath)
=======
        public ProjectManagement(string dbcPath)
>>>>>>> 9a38e72691fb335083a79d2214f9d102a7fa3eb9
        {
            this.dbPath = dbPath;
            InitializeComponent();
            InitializeUI();
            this.Load += ProjectManagement_Load;
        }

        private void ProjectManagement_Load(object? sender, EventArgs e)
        {
            LoadProjects();
        }

        private void InitializeUI()
        {
            // 创建主布局控件
            LayoutControl layoutControl = new LayoutControl
            {
                Dock = DockStyle.Fill,
                Parent = this,
                //Appearance = { BackColor = Color.White }
            };

            // 创建根布局组（水平排列）
            LayoutControlGroup rootGroup = new LayoutControlGroup
            {
                TextVisible = false,
                DefaultLayoutType = LayoutType.Horizontal,
                GroupBordersVisible = false,
                Padding = new DevExpress.XtraLayout.Utils.Padding(0)
            };
            layoutControl.Root = rootGroup;

            // === 左侧网格区域 ===
            LayoutControlItem gridItem = new LayoutControlItem();
            gridControl = new GridControl
            {
                Name = "projectGrid",
                Margin = new System.Windows.Forms.Padding(5),
                Location = new Point(0, 0),
                //Appearance = { BackColor = Color.White }
            };

            gridView = new GridView(gridControl);
            gridControl.MainView = gridView;

            // 网格视图设置
            gridView.OptionsView.ShowGroupPanel = false;
            gridView.OptionsView.ShowVerticalLines = DefaultBoolean.True;
            gridView.OptionsView.ShowHorizontalLines = DefaultBoolean.True;
            gridView.OptionsView.EnableAppearanceEvenRow = true;
            gridView.OptionsView.EnableAppearanceOddRow = true;
            gridView.Appearance.EvenRow.BackColor = ColorTranslator.FromHtml("#F9F9F9");
            gridView.Appearance.OddRow.BackColor = Color.White;
            gridView.Appearance.Row.BackColor2 = Color.White;
            gridView.Appearance.HeaderPanel.BackColor = ColorTranslator.FromHtml("#F0F5FF");
            gridView.Appearance.HeaderPanel.Font = new Font("Tahoma", 12, FontStyle.Bold);
            gridView.Appearance.HeaderPanel.Options.UseBackColor = true;
            gridView.Appearance.HeaderPanel.Options.UseFont = true;
            gridView.Appearance.HeaderPanel.TextOptions.HAlignment = HorzAlignment.Center;
            gridView.Appearance.Row.TextOptions.HAlignment = HorzAlignment.Center;
            gridView.OptionsSelection.MultiSelect = true;
            gridView.OptionsSelection.MultiSelectMode = GridMultiSelectMode.RowSelect;
            gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
            gridView.FocusRectStyle = DrawFocusRectStyle.RowFocus;

            // 添加列
            var columns = new[]
            {
                new GridColumn{FieldName = "ProjectID", Caption = "项目ID", Visible = false},
                new GridColumn{FieldName = "序号",Caption = "序号",Visible = true,Width = 80,OptionsColumn = { AllowEdit = false }},
                new GridColumn{FieldName = "ProjectName",Caption = "项目名称",Visible = true,Width = 180,OptionsColumn = { AllowEdit = false }},
                new GridColumn{FieldName = "BatteryName",Caption = "电池包名称",Visible = true,Width = 150,OptionsColumn = { AllowEdit = false }},
                new GridColumn{FieldName = "BatteryCode",Caption = "电池包特征码",Visible = true,Width = 200,OptionsColumn = { AllowEdit = false }},
                new GridColumn{FieldName = "CommunicationType",Caption = "通讯方式",Visible = true,Width = 120,OptionsColumn = { AllowEdit = false }},
                new GridColumn{FieldName = "CreationTime",Caption = "创建时间",Visible = true,Width = 180,OptionsColumn = { AllowEdit = false }},
                new GridColumn{FieldName = "ModificationTime",Caption = "修改时间",Visible = true,Width = 180,OptionsColumn = { AllowEdit = false }}
            };

            gridView.Columns.AddRange(columns);

            // 添加双击事件处理
            gridView.DoubleClick += GridView_DoubleClick;

            gridItem.Control = gridControl;
            gridItem.TextVisible = false;
            gridItem.Padding = new DevExpress.XtraLayout.Utils.Padding(5);
            gridItem.SizeConstraintsType = SizeConstraintsType.Custom;
            gridItem.MinSize = new Size(700, 0); // 最小宽度
            rootGroup.AddItem(gridItem);

            // === 右侧按钮区域 ===
            LayoutControlGroup buttonGroup = new LayoutControlGroup
            {
                GroupBordersVisible = false,
                TextVisible = false,
                DefaultLayoutType = LayoutType.Vertical,
                Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 0),
                //SizeConstraintsType = SizeConstraintsType.Custom,
                //MinSize = new Size(150, 0)
            };
            rootGroup.AddItem(buttonGroup);

            // 创建按钮容器（垂直栈面板）
            StackPanel buttonPanel = new StackPanel
            {
                AutoSize = true,
                LayoutDirection = StackPanelLayoutDirection.TopDown,
                Padding = new System.Windows.Forms.Padding(0, 40, 0, 0),
                AutoSizeMode = AutoSizeMode.GrowOnly,
                //Spacing = 20 // 按钮间距
            };

            // 创建按钮 - 现代扁平化风格
            btnNew = CreateModernButton("新建项目", ColorTranslator.FromHtml("#4CAF50")); // Material Green
            btnEdit = CreateModernButton("编辑项目", ColorTranslator.FromHtml("#2196F3")); // Material Blue
            btnDelete = CreateModernButton("删除项目", ColorTranslator.FromHtml("#F44336")); // Material Red

            // 添加按钮事件
            btnNew.Click += BtnNew_Click;
            btnEdit.Click += BtnEdit_Click;
            btnDelete.Click += BtnDelete_Click;

            // 添加按钮到面板
            buttonPanel.Controls.Add(btnNew);
            buttonPanel.Controls.Add(btnEdit);
            buttonPanel.Controls.Add(btnDelete);

            // 添加按钮面板到布局项
            LayoutControlItem buttonItem = new LayoutControlItem
            {
                Control = buttonPanel,
                TextVisible = false,
                SizeConstraintsType = SizeConstraintsType.Custom,
                Padding = new DevExpress.XtraLayout.Utils.Padding(5),
                ControlMinSize = new Size(100, 100)
            };

            buttonGroup.AddItem(buttonItem);
        }

<<<<<<< HEAD
        private void GridView_DoubleClick(object? sender, EventArgs e)
        {
            // 获取鼠标点击的位置
            Point clickPoint = gridView.GridControl.PointToClient(Control.MousePosition);

            // 获取点击的行句柄
            GridHitInfo hitInfo = gridView.CalcHitInfo(clickPoint);

            // 检查是否点击在行上
            if (hitInfo.InRow || hitInfo.InRowCell)
            {
                // 获取点击的行
                int rowHandle = hitInfo.RowHandle;

                // 确保行句柄有效
                if (rowHandle >= 0)
                {
                    // 获取行数据
                    DataRowView row = gridView.GetRow(rowHandle) as DataRowView;

                    if (row != null)
                    {
                        // 获取项目ID
                        int projectId = Convert.ToInt32(row["ProjectID"]);

                        // 打开项目详情页面
                        OpenProjectDetail(projectId);
                    }
                }
            }
        }

        private void OpenProjectDetail(int projectId)
        {
            try
            {
                // 从数据库获取项目数据
                ProjectData projectData = GetProjectData(projectId);

                if (projectData != null)
                {
                    // 创建并显示项目详情表单
                    ProjectDetailForm detailForm = new ProjectDetailForm();
                    detailForm.Show();

                    // 或者使用 ShowDialog 以模态方式打开
                    // detailForm.ShowDialog();
                }
                else
                {
                    XtraMessageBox.Show("无法找到项目数据", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"打开项目详情失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnDelete_Click(object? sender, EventArgs e)
        {
            if (gridView.SelectedRowsCount == 0)
            {
                XtraMessageBox.Show("请选择要删除的项目", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (XtraMessageBox.Show("确定要删除选中的项目吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try
                {
                    int[] selectedRowHandles = gridView.GetSelectedRows();
                    using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                    {
                        conn.Open();

                        foreach (int rowHandle in selectedRowHandles)
                        {
                            DataRowView row = gridView.GetRow(rowHandle) as DataRowView;
                            if (row != null)
                            {
                                int projectId = Convert.ToInt32(row["ProjectID"]);

                                string deleteQuery = "DELETE FROM Projects WHERE ProjectID = @ProjectID";
                                using (SQLiteCommand cmd = new SQLiteCommand(deleteQuery, conn))
                                {
                                    cmd.Parameters.AddWithValue("@ProjectID", projectId);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }
                    }

                    LoadProjects(); // 刷新数据
                    XtraMessageBox.Show("删除成功", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    XtraMessageBox.Show($"删除项目失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
=======
        private void BtnDelete_Click(object? sender, EventArgs e)
        {
            throw new NotImplementedException();
>>>>>>> 9a38e72691fb335083a79d2214f9d102a7fa3eb9
        }

        private void BtnEdit_Click(object? sender, EventArgs e)
        {
<<<<<<< HEAD
            if (gridView.SelectedRowsCount == 0)
            {
                XtraMessageBox.Show("请选择要编辑的项目", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (gridView.SelectedRowsCount > 1)
            {
                XtraMessageBox.Show("只能选择一个项目进行编辑", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int selectedRowHandle = gridView.GetSelectedRows()[0];
            DataRowView row = gridView.GetRow(selectedRowHandle) as DataRowView;

            if (row != null)
            {
                int projectId = Convert.ToInt32(row["ProjectID"]);

                // 从数据库获取项目数据
                ProjectData projectData = GetProjectData(projectId);

                using (ProjectEditForm editForm = new ProjectEditForm(projectData))
                {
                    if (editForm.ShowDialog() == DialogResult.OK)
                    {
                        // 更新数据库
                        UpdateProject(projectId, editForm.ProjectName, editForm.BatteryName,
                                      editForm.BatteryCode, editForm.CommunicationType);
                        LoadProjects(); // 刷新数据
                    }
                }
            }
=======
            throw new NotImplementedException();
>>>>>>> 9a38e72691fb335083a79d2214f9d102a7fa3eb9
        }

        // 加载项目数据
        private void LoadProjects()
        {
            try
            {
<<<<<<< HEAD
                string connectionString = $"Data Source={dbPath};Version=3;";
                DataTable dataTable = new DataTable();

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    const string query = "SELECT ProjectID, ProjectName, BatteryName, " +
                                         "BatteryCode, CommunicationType, " +
                                         "datetime(CreationTime, 'localtime') as CreationTime, " +
                                         "datetime(ModificationTime, 'localtime') as ModificationTime " +
                                         "FROM Projects ORDER BY COALESCE(ModificationTime, CreationTime) DESC";
                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(dataTable);
                        }
                    }
                }

                // 添加序号列并按照显示顺序生成序号
                dataTable.Columns.Add("序号", typeof(int));
                for (int i = 0; i < dataTable.Rows.Count; i++)
                {
                    // 按照数据在网格中的显示顺序生成序号（1,2,3...）
                    dataTable.Rows[i]["序号"] = i + 1;
                }

                gridControl.DataSource = dataTable;

                // 确保网格按照创建时间降序排列
                gridView.ClearSorting();
                gridView.SortInfo.Add(new GridColumnSortInfo(gridView.Columns["ModificationTime"], ColumnSortOrder.Descending));
=======
                string query = "SELECT ProjectID, ProjectName as 项目名称, BatteryName as 电池包名称, " +
                               "BatteryCode as 电池包特征码, CommunicationType as 通讯方式, " +
                               "datetime(CreationTime, 'localtime') as 创建时间 FROM Projects ORDER BY CreationTime DESC";

                DataTable dt = new DataTable();
                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(query, connection))
                {
                    adapter.Fill(dt);
                }

                // 添加序号列
                dt.Columns.Add("序号", typeof(int));
                for (int i = 0; i < dt.Rows.Count; i++)
                {
                    dt.Rows[i]["序号"] = i + 1;
                }

                gridControl.DataSource = dt;
>>>>>>> 9a38e72691fb335083a79d2214f9d102a7fa3eb9
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"加载项目数据失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnNew_Click(object? sender, EventArgs e)
        {
<<<<<<< HEAD
            using (ProjectEditForm editForm = new ProjectEditForm())
            {
                if (editForm.ShowDialog() == DialogResult.OK)
                {
                    // 插入新项目到数据库
                    InsertProject(editForm.ProjectName, editForm.BatteryName,
                                 editForm.BatteryCode, editForm.CommunicationType);
=======
            using (ProjectEditForm editForm = new ProjectEditForm(null, connection))
            {
                if (editForm.ShowDialog() == DialogResult.OK)
                {
>>>>>>> 9a38e72691fb335083a79d2214f9d102a7fa3eb9
                    LoadProjects(); // 刷新数据
                }
            }
        }

<<<<<<< HEAD
        private ProjectData GetProjectData(int projectId)
        {
            using (SQLiteConnection connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                connection.Open();
                string query = "SELECT ProjectName, BatteryName, BatteryCode, CommunicationType, " +
                               "datetime(ModificationTime, 'localtime') as ModificationTime " +
                               "FROM Projects WHERE ProjectID = @ProjectID";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@ProjectID", projectId);

                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new ProjectData
                            {
                                ProjectID = projectId,
                                ProjectName = reader["ProjectName"].ToString(),
                                BatteryName = reader["BatteryName"].ToString(),
                                BatteryCode = reader["BatteryCode"].ToString(),
                                CommunicationType = reader["CommunicationType"].ToString(),
                                ModificationTime = reader["ModificationTime"] != DBNull.Value ?
                                          reader["ModificationTime"].ToString() : null
                            };
                        }
                    }
                }
            }
            return null;
        }

        private void UpdateProject(int projectId, string projectName, string batteryName,
                                   string batteryCode, string communicationType)
        {
            using (SQLiteConnection connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                connection.Open();
                string updateQuery = @"UPDATE Projects SET 
                                       ProjectName = @ProjectName, 
                                       BatteryName = @BatteryName, 
                                       BatteryCode = @BatteryCode, 
                                       CommunicationType = @CommunicationType,
                                       ModificationTime = datetime('now')
                                       WHERE ProjectID = @ProjectID";

                using (SQLiteCommand cmd = new SQLiteCommand(updateQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@ProjectName", projectName);
                    cmd.Parameters.AddWithValue("@BatteryName", batteryName);
                    cmd.Parameters.AddWithValue("@BatteryCode", batteryCode);
                    cmd.Parameters.AddWithValue("@CommunicationType", communicationType);
                    cmd.Parameters.AddWithValue("@ProjectID", projectId);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void InsertProject(string projectName, string batteryName,
                                   string batteryCode, string communicationType)
        {
            using (SQLiteConnection connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                connection.Open();
                string insertQuery = @"INSERT INTO Projects 
                                    (ProjectName, BatteryName, BatteryCode, CommunicationType, CreationTime, ModificationTime) 
                                    VALUES 
                                    (@ProjectName, @BatteryName, @BatteryCode, @CommunicationType, datetime('now'), datetime('now'))";

                using (SQLiteCommand cmd = new SQLiteCommand(insertQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@ProjectName", projectName);
                    cmd.Parameters.AddWithValue("@BatteryName", batteryName);
                    cmd.Parameters.AddWithValue("@BatteryCode", batteryCode);
                    cmd.Parameters.AddWithValue("@CommunicationType", communicationType);

                    cmd.ExecuteNonQuery();
                }
            }
        }

=======
>>>>>>> 9a38e72691fb335083a79d2214f9d102a7fa3eb9
        private SimpleButton CreateModernButton(string text, Color baseColor)
        {
            SimpleButton btn = new SimpleButton
            {
                Text = text,
                Size = new Size(80, 30),
                Margin = new System.Windows.Forms.Padding(0,0,0,20),
                Appearance =
                {
                    Font = new Font("Tahoma", 12, FontStyle.Bold),
                    BackColor = baseColor,
                    ForeColor = Color.White,
                    BorderColor = baseColor,
                    Options = {
                        UseBackColor = true,
                        UseForeColor = true,
                        UseBorderColor = true
                    }
                },
                ButtonStyle = BorderStyles.Flat,
                //CornerRadius = 21, // 圆形按钮
                ShowFocusRectangle = DefaultBoolean.False
            };

            // 悬停效果
            btn.MouseEnter += (s, e) =>
            {
                btn.Appearance.BackColor = ControlPaint.Light(baseColor, 0.1f);
            };

            btn.MouseLeave += (s, e) =>
            {
                btn.Appearance.BackColor = baseColor;
            };

            // 按下效果
            btn.MouseDown += (s, e) =>
            {
                btn.Appearance.BackColor = ControlPaint.Dark(baseColor, 0.2f);
            };

            btn.MouseUp += (s, e) =>
            {
                btn.Appearance.BackColor = ControlPaint.Light(baseColor, 0.1f);
            };

            return btn;
        }
    }
}