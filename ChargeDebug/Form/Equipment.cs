using DevExpress.XtraEditors;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using System.Data.SQLite;
using System.Data;

namespace ChargeDebug.Form
{
    public partial class Equipment : XtraUserControl
    {
        private string dbcPath = "";
        private GridControl gridControl;
        private GridView gridview;

        public event EventHandler ConfigUpdated;
        public Equipment(string dbPath)
        {
            dbcPath = dbPath;
            InitializeComponent();
            InitializeUI();
            this.Load += Equipment_Load;
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

            GridColumn devicenumber = new GridColumn();
            devicenumber.FieldName = "DeviceNumber";
            devicenumber.Caption = "设备序号";
            devicenumber.Visible = true;

            GridColumn cantype = new GridColumn();
            cantype.FieldName = "CanType";
            cantype.Caption = "CAN盒类型";
            cantype.Visible = true;

            GridColumn deviceip = new GridColumn();
            deviceip.FieldName = "DeviceIP";
            deviceip.Caption = "设备IP";
            deviceip.Visible = true;

            GridColumn deviceport = new GridColumn();
            deviceport.FieldName = "DevicePort";
            deviceport.Caption = "设备端口";
            deviceport.Visible = true;

            GridColumn deviceindex = new GridColumn();
            deviceindex.FieldName = "DeviceIndex";
            deviceindex.Caption = "设备索引";
            deviceindex.Visible = true;

            GridColumn canindex = new GridColumn();
            canindex.FieldName = "CanIndex";
            canindex.Caption = "CAN索引";
            canindex.Visible = true;

            GridColumn acnumber = new GridColumn();
            acnumber.FieldName = "ACNumber";
            acnumber.Caption = "AC数量";
            acnumber.Visible = true;

            GridColumn dcnumber = new GridColumn();
            dcnumber.FieldName = "DCNumber";
            dcnumber.Caption = "DC数量";
            dcnumber.Visible = true;

            GridColumn communicationprotocols = new GridColumn();
            communicationprotocols.FieldName = "CommunicationProtocols";
            communicationprotocols.Caption = "通讯协议";
            communicationprotocols.Visible = true;

            GridColumn whether = new GridColumn();
            whether.FieldName = "Whether";
            whether.Caption = "是否启用设备";
            whether.Visible = true;

            gridview.Columns.AddRange(new[] { devicenumber, cantype, deviceip,
                  deviceport, deviceindex, canindex, acnumber, dcnumber, communicationprotocols, whether });

            SimpleButton simpleButton = new SimpleButton();
            simpleButton.Location = new Point(10, 20);
            simpleButton.Name = "simpleButton";
            simpleButton.Size = new Size(100, 30);
            simpleButton.TabIndex = 0;
            simpleButton.Text = "增加通道";

            SimpleButton simpleButton1 = new SimpleButton();
            simpleButton1.Location = new Point(10, 70);
            simpleButton1.Name = "simpleButton1";
            simpleButton1.Size = new Size(100, 30);
            simpleButton1.TabIndex = 1;
            simpleButton1.Text = "编辑通道";

            SimpleButton simpleButton2 = new SimpleButton();
            simpleButton2.Location = new Point(10, 120);
            simpleButton2.Name = "simpleButton2";
            simpleButton2.Size = new Size(100, 30);
            simpleButton2.TabIndex = 2;
            simpleButton2.Text = "删除通道";

            PanelControl panelControl = new PanelControl();
            panelControl.Dock = DockStyle.Right;
            panelControl.Width = 120;
            panelControl.Controls.AddRange(new[] { simpleButton, simpleButton1 });

            this.Controls.Add(gridControl);
            this.Controls.Add(panelControl);

            // 为按钮添加点击事件
            simpleButton.Click += AddDevice_Click;
            simpleButton1.Click += EditDevice_Click;
            //simpleButton2.Click += DeleteDevice_Click;
        }

        private void Equipment_Load(object? sender, EventArgs e)
        {
            // 加载数据
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                string connectionString = $"Data Source={dbcPath};Version=3;";
                DataTable dataTable = new DataTable();

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    const string query = "SELECT * FROM Equipment";
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

        //增加设备
        private void AddDevice_Click(object sender, EventArgs e)
        {
            try
            {
                // 获取当前最大设备号
                int maxNumber = 0;
                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();
                    string query = @"
                       SELECT MAX(CAST(SUBSTR(DeviceNumber, 3) AS INTEGER))
                       FROM Equipment
                       WHERE DeviceNumber LIKE '设备%'";

                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        object result = cmd.ExecuteScalar();
                        if (result != DBNull.Value) maxNumber = Convert.ToInt32(result);
                    }
                }
                // 生成新设备号
                string newDeviceNumber = $"设备{maxNumber + 1}";

                // 打开编辑窗口并传递自动生成的设备号
                using (var form = new DeviceEditForm(newDeviceNumber, dbcPath))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        // 执行插入操作
                        using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                        {
                            conn.Open();
                            const string insertQuery = @"INSERT INTO Equipment 
                                (DeviceNumber, CanType, DeviceIP, DevicePort, 
                                 DeviceIndex, CanIndex, ACNumber, DCNumber, CommunicationProtocols, Whether)
                                 VALUES 
                               (@DeviceNumber, @CanType, @DeviceIP, @DevicePort, 
                                @DeviceIndex, @CanIndex, @ACNumber, @DCNumber, @CommunicationProtocols, @Whether)";

                            using (var cmd = new SQLiteCommand(insertQuery, conn))
                            {
                                cmd.Parameters.AddWithValue("@DeviceNumber", newDeviceNumber);
                                cmd.Parameters.AddWithValue("@CanType", form.CanType);
                                cmd.Parameters.AddWithValue("@DeviceIP", form.DeviceIP);
                                cmd.Parameters.AddWithValue("@DevicePort", form.DevicePort);
                                cmd.Parameters.AddWithValue("@DeviceIndex", form.DeviceIndex);
                                cmd.Parameters.AddWithValue("@CanIndex", form.CanIndex);
                                cmd.Parameters.AddWithValue("@ACNumber", form.ACNumber);
                                cmd.Parameters.AddWithValue("@DCNumber", form.DCNumber);
                                cmd.Parameters.AddWithValue("@CommunicationProtocols", form.CommunicationProtocols);
                                cmd.Parameters.AddWithValue("@Whether", form.Whether);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        LoadData();
                        XtraMessageBox.Show("设备添加成功！", "提示",
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

        //编辑设备
        private void EditDevice_Click(object sender, EventArgs e)
        {
            try
            {
                // 获取当前选中行
                var gridView = gridControl.MainView as GridView;
                var selectedRowHandle = gridView.FocusedRowHandle;

                if (selectedRowHandle == GridControl.InvalidRowHandle)
                {
                    XtraMessageBox.Show("请先选择要编辑的设备！", "提示",
                                     MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 获取当前设备数据
                DataRow row = ((DataRowView)gridView.GetRow(selectedRowHandle)).Row;

                // 创建并显示编辑表单
                using (var form = new DeviceEditForm(row, dbcPath))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        // 执行更新操作
                        using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                        {
                            conn.Open();
                            const string query = @"UPDATE Equipment SET
                                        DeviceNumber = @DeviceNumber,
                                        CanType = @CanType,
                                        DeviceIP = @DeviceIP,
                                        DevicePort = @DevicePort,
                                        DeviceIndex = @DeviceIndex,
                                        CanIndex = @CanIndex,
                                        ACNumber = @ACNumber,
                                        DCNumber = @DCNumber,
                                        CommunicationProtocols = @CommunicationProtocols,
                                        Whether = @Whether
                                        WHERE EquipmentID = @EquipmentID";

                            using (var cmd = new SQLiteCommand(query, conn))
                            {
                                cmd.Parameters.AddWithValue("@EquipmentID", row["EquipmentID"]);
                                cmd.Parameters.AddWithValue("@DeviceNumber", form.DeviceNumber);
                                cmd.Parameters.AddWithValue("@CanType", form.CanType);
                                cmd.Parameters.AddWithValue("@DeviceIP", form.DeviceIP);
                                cmd.Parameters.AddWithValue("@DevicePort", form.DevicePort);
                                cmd.Parameters.AddWithValue("@DeviceIndex", form.DeviceIndex);
                                cmd.Parameters.AddWithValue("@CanIndex", form.CanIndex);
                                cmd.Parameters.AddWithValue("@ACNumber", form.ACNumber);
                                cmd.Parameters.AddWithValue("@DCNumber", form.DCNumber);
                                cmd.Parameters.AddWithValue("@CommunicationProtocols", form.CommunicationProtocols);
                                cmd.Parameters.AddWithValue("@Whether", form.Whether);
                                
                                cmd.ExecuteNonQuery();
                            }
                        }

                        // 刷新数据
                        LoadData();
                        // 保存成功后触发事件
                        ConfigUpdated?.Invoke(this, EventArgs.Empty);

                        XtraMessageBox.Show("设备修改成功！", "提示",
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

        //删除设备
        private void DeleteDevice_Click(object sender, EventArgs e)
        {
            try
            {
                var gridView = gridControl.MainView as GridView;
                var selectedRowHandle = gridView.FocusedRowHandle;

                if (selectedRowHandle == GridControl.InvalidRowHandle)
                {
                    XtraMessageBox.Show("请先选择要删除的设备！", "提示",
                                     MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 获取设备编号
                var deviceNumber = gridView.GetRowCellValue(selectedRowHandle, "DeviceNumber").ToString();

                // 确认对话框
                if (XtraMessageBox.Show($"确定要删除选中的{deviceNumber}吗？", "确认删除",
                                      MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                

                // 执行删除操作
                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();
                    const string query = "DELETE FROM Equipment WHERE DeviceNumber = @DeviceNumber";

                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@DeviceNumber", deviceNumber);
                        cmd.ExecuteNonQuery();
                    }
                }

                // 刷新数据
                LoadData();
                XtraMessageBox.Show("设备删除成功！", "提示",
                                 MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"删除失败：{ex.Message}", "错误",
                                 MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
