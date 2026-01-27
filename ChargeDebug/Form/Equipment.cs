using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using System.Data;
using System.Data.SQLite;

#pragma warning disable
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

            gridview.OptionsBehavior.Editable = false;
            gridview.OptionsView.ShowGroupPanel = false;
            gridview.OptionsView.ShowVerticalLines = DefaultBoolean.True;
            gridview.OptionsView.ShowHorizontalLines = DefaultBoolean.True;
            gridview.OptionsView.EnableAppearanceEvenRow = true;
            gridview.OptionsView.EnableAppearanceOddRow = true;
            gridview.Appearance.EvenRow.BackColor = ColorTranslator.FromHtml("#F9F9F9");
            gridview.Appearance.OddRow.BackColor = Color.White;
            gridview.Appearance.Row.BackColor2 = Color.White;
            gridview.Appearance.HeaderPanel.BackColor = ColorTranslator.FromHtml("#F0F5FF");
            //gridview.Appearance.HeaderPanel.Font = new Font("Tahoma", 10, FontStyle.Bold);
            gridview.Appearance.HeaderPanel.Options.UseBackColor = true;
            gridview.Appearance.HeaderPanel.Options.UseFont = true;
            gridview.Appearance.HeaderPanel.TextOptions.HAlignment = HorzAlignment.Center;
            gridview.Appearance.Row.TextOptions.HAlignment = HorzAlignment.Center;
            gridview.OptionsSelection.MultiSelect = true;
            gridview.OptionsSelection.MultiSelectMode = GridMultiSelectMode.RowSelect;
            gridview.OptionsSelection.EnableAppearanceFocusedCell = false;
            gridview.FocusRectStyle = DrawFocusRectStyle.RowFocus;

            // 设备类型列
            GridColumn deviceType = new GridColumn();
            deviceType.FieldName = "DeviceType";
            deviceType.Caption = "设备类型";
            deviceType.Visible = true;
            deviceType.Width = 50;

            GridColumn devicenumber = new GridColumn();
            devicenumber.FieldName = "DeviceNumber";
            devicenumber.Caption = "设备序号";
            devicenumber.Visible = true;
            devicenumber.Width = 35;

            GridColumn devicename = new GridColumn();
            devicename.FieldName = "DeviceName";
            devicename.Caption = "设备名称";
            devicename.Visible = true;
            devicename.Width = 70;

            GridColumn cantype = new GridColumn();
            cantype.FieldName = "CanType";
            cantype.Caption = "通讯类型";
            cantype.Visible = true;
            cantype.Width = 70;

            // 网口配置列
            GridColumn deviceip = new GridColumn();
            deviceip.FieldName = "DeviceIP";
            deviceip.Caption = "设备IP";
            deviceip.Visible = true;
            deviceip.Width = 70;

            GridColumn deviceport = new GridColumn();
            deviceport.FieldName = "DevicePort";
            deviceport.Caption = "设备端口";
            deviceport.Visible = true;
            deviceport.Width = 35;

            // 串口配置列
            GridColumn comPort = new GridColumn();
            comPort.FieldName = "ComPort";
            comPort.Caption = "串口号";
            comPort.Visible = true;
            comPort.Width = 30;

            GridColumn baudRate = new GridColumn();
            baudRate.FieldName = "BaudRate";
            baudRate.Caption = "波特率";
            baudRate.Visible = true;
            baudRate.Width = 30;

            GridColumn dataBits = new GridColumn();
            dataBits.FieldName = "DataBits";
            dataBits.Caption = "数据位";
            dataBits.Visible = true;
            dataBits.Width = 30;

            GridColumn parity = new GridColumn();
            parity.FieldName = "Parity";
            parity.Caption = "校验位";
            parity.Visible = true;
            parity.Width = 30;

            GridColumn stopBits = new GridColumn();
            stopBits.FieldName = "StopBits";
            stopBits.Caption = "停止位";
            stopBits.Visible = true;
            stopBits.Width = 30;

            GridColumn deviceindex = new GridColumn();
            deviceindex.FieldName = "DeviceIndex";
            deviceindex.Caption = "设备索引";
            deviceindex.Visible = true;
            deviceindex.Width = 35;

            GridColumn canindex = new GridColumn();
            canindex.FieldName = "CanIndex";
            canindex.Caption = "CAN索引";
            canindex.Visible = true;
            canindex.Width = 35;

            GridColumn acnumber = new GridColumn();
            acnumber.FieldName = "ACNumber";
            acnumber.Caption = "AC数量";
            acnumber.Visible = true;
            acnumber.Width = 35;

            GridColumn acaddress = new GridColumn();
            acaddress.FieldName = "ACAddress";
            acaddress.Caption = "AC起始地址";
            acaddress.Visible = true;
            acaddress.Width = 50;

            GridColumn dcnumber = new GridColumn();
            dcnumber.FieldName = "DCNumber";
            dcnumber.Caption = "DC数量";
            dcnumber.Visible = true;
            dcnumber.Width = 35;

            GridColumn dcaddress = new GridColumn();
            dcaddress.FieldName = "DCAddress";
            dcaddress.Caption = "DC起始地址";
            dcaddress.Visible = true;
            dcaddress.Width = 50;

            GridColumn communicationprotocols = new GridColumn();
            communicationprotocols.FieldName = "CommunicationProtocols";
            communicationprotocols.Caption = "通讯协议";
            communicationprotocols.Visible = true;
            communicationprotocols.Width = 90;

            GridColumn whether = new GridColumn();
            whether.FieldName = "Whether";
            whether.Caption = "是否启用设备";
            whether.Visible = true;
            whether.Width = 50;

            // 连接状态列
            //GridColumn connectionStatus = new GridColumn();
            //connectionStatus.FieldName = "ConnectionStatus";
            //connectionStatus.Caption = "连接状态";
            //connectionStatus.Visible = true;
            //connectionStatus.Width = 80;

            gridview.Columns.AddRange(new[] {
                devicenumber, devicename, deviceType, cantype,
                deviceip, deviceport, deviceindex, canindex, acnumber, acaddress, dcnumber, dcaddress,
                comPort, baudRate, dataBits, parity, stopBits,
                communicationprotocols, whether
            });

            // 设置连接状态列的显示样式
            //gridview.FormatConditions.Add(new StyleFormatCondition
            //{
            //    Condition = FormatConditionEnum.Equal,
            //    Value1 = "已连接",
            //    Column = connectionStatus,
            //    ApplyToRow = true,
            //    Appearance = { ForeColor = Color.Green, Font = new Font(gridview.Appearance.Row.Font, FontStyle.Bold) }
            //});

            //gridview.FormatConditions.Add(new StyleFormatCondition
            //{
            //    Condition = FormatConditionEnum.Equal,
            //    Value1 = "未连接",
            //    Column = connectionStatus,
            //    ApplyToRow = true,
            //    Appearance = { ForeColor = Color.Red }
            //});

            //gridview.FormatConditions.Add(new StyleFormatCondition
            //{
            //    Condition = FormatConditionEnum.Equal,
            //    Value1 = "连接中",
            //    Column = connectionStatus,
            //    ApplyToRow = true,
            //    Appearance = { ForeColor = Color.Orange }
            //});

            SimpleButton simpleButton = new SimpleButton();
            simpleButton.Location = new Point(10, 20);
            simpleButton.Name = "simpleButton";
            simpleButton.Size = new Size(100, 30);
            simpleButton.TabIndex = 0;
            simpleButton.Text = "增加设备";

            SimpleButton simpleButton1 = new SimpleButton();
            simpleButton1.Location = new Point(10, 70);
            simpleButton1.Name = "simpleButton1";
            simpleButton1.Size = new Size(100, 30);
            simpleButton1.TabIndex = 1;
            simpleButton1.Text = "编辑设备";

            SimpleButton simpleButton2 = new SimpleButton();
            simpleButton2.Location = new Point(10, 120);
            simpleButton2.Name = "simpleButton2";
            simpleButton2.Size = new Size(100, 30);
            simpleButton2.TabIndex = 2;
            simpleButton2.Text = "删除设备";

            PanelControl panelControl = new PanelControl();
            panelControl.Dock = DockStyle.Right;
            panelControl.Width = 120;
            panelControl.Controls.AddRange(new[] { simpleButton, simpleButton1, simpleButton2});

            this.Controls.Add(gridControl);
            this.Controls.Add(panelControl);

            // 为按钮添加点击事件
            simpleButton.Click += AddDevice_Click;
            simpleButton1.Click += EditDevice_Click;
            simpleButton2.Click += DeleteDevice_Click;
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
                    // 修改查询语句以包含串口配置字段
                    const string query = @"SELECT 
                        EquipmentID, DeviceType, DeviceNumber, DeviceName, CanType, 
                        DeviceIP, DevicePort, ComPort, BaudRate, DataBits, Parity, StopBits,
                        DeviceIndex, CanIndex, ACNumber, ACAddress, DCNumber, DCAddress, 
                        CommunicationProtocols, Whether 
                        FROM Equipment ORDER BY [DeviceNumber] ASC";

                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(dataTable);
                        }
                    }
                }

                // 如果没有ConnectionStatus列，添加它
                if (!dataTable.Columns.Contains("ConnectionStatus"))
                {
                    dataTable.Columns.Add("ConnectionStatus", typeof(string));
                    foreach (DataRow row in dataTable.Rows)
                    {
                        row["ConnectionStatus"] = "未知"; // 初始状态
                    }
                }
                gridControl.DataSource = null;
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
                    string query = "SELECT MAX(DeviceNumber) FROM Equipment";

                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        object result = cmd.ExecuteScalar();
                        if (result != DBNull.Value) maxNumber = Convert.ToInt32(result);
                    }
                }
                // 生成新设备号
                int newDeviceNumber = maxNumber + 1;

                // 打开编辑窗口并传递自动生成的设备号
                using (var form = new DeviceEditForm(dbcPath, newDeviceNumber))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        // 执行插入操作
                        using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                        {
                            conn.Open();
                            // 修改插入语句以包含串口配置字段
                            const string insertQuery = @"INSERT INTO Equipment 
                                (DeviceNumber, DeviceName, DeviceType, CanType, 
                                DeviceIP, DevicePort, ComPort, BaudRate, DataBits, Parity, StopBits,
                                DeviceIndex, CanIndex, ACNumber, ACAddress, DCNumber, DCAddress, CommunicationProtocols, Whether)
                                VALUES 
                                (@DeviceNumber, @DeviceName, @DeviceType, @CanType, 
                                @DeviceIP, @DevicePort, @ComPort, @BaudRate, @DataBits, @Parity, @StopBits,
                                @DeviceIndex, @CanIndex, @ACNumber, @ACAddress, @DCNumber, @DCAddress, @CommunicationProtocols, @Whether)";

                            using (var cmd = new SQLiteCommand(insertQuery, conn))
                            {
                                cmd.Parameters.AddWithValue("@DeviceType", form.DeviceType);
                                cmd.Parameters.AddWithValue("@DeviceNumber", newDeviceNumber);
                                cmd.Parameters.AddWithValue("@DeviceName", form.DeviceName);
                                cmd.Parameters.AddWithValue("@CanType", form.CanType);
                                cmd.Parameters.AddWithValue("@CommunicationProtocols", form.CommunicationProtocols);
                                cmd.Parameters.AddWithValue("@Whether", form.Whether);

                                // 根据通讯类型设置相应的配置
                                if (form.CanType == "ZCAN_CANETTCP" || form.CanType == "ZCAN_CANFDNET_200U_TCP")
                                {
                                    cmd.Parameters.AddWithValue("@DeviceIP", form.DeviceIP);
                                    cmd.Parameters.AddWithValue("@DevicePort", form.DevicePort);
                                    cmd.Parameters.AddWithValue("@DeviceIndex", form.DeviceIndex);
                                    cmd.Parameters.AddWithValue("@CanIndex", form.CanIndex);
                                    cmd.Parameters.AddWithValue("@ComPort", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@BaudRate", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DataBits", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@Parity", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@StopBits", DBNull.Value);
                                }
                                else if (form.CanType == "RS485-MODBUS")
                                {
                                    cmd.Parameters.AddWithValue("@DeviceIP", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DevicePort", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DeviceIndex", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@CanIndex", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ComPort", form.ComPort);
                                    cmd.Parameters.AddWithValue("@BaudRate", form.BaudRate);
                                    cmd.Parameters.AddWithValue("@DataBits", form.DataBits);
                                    cmd.Parameters.AddWithValue("@Parity", form.Parity);
                                    cmd.Parameters.AddWithValue("@StopBits", form.StopBits);
                                }
                                else if (form.CanType == "RS232" || form.CanType == "USB-SCPI")
                                {
                                    cmd.Parameters.AddWithValue("@DeviceIP", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DevicePort", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DeviceIndex", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@CanIndex", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ComPort", form.ComPort);
                                    cmd.Parameters.AddWithValue("@BaudRate", form.BaudRate);
                                    cmd.Parameters.AddWithValue("@DataBits", form.DataBits);
                                    cmd.Parameters.AddWithValue("@Parity", form.Parity);
                                    cmd.Parameters.AddWithValue("@StopBits", form.StopBits);
                                }
                                //else if (form.CanType == "USB-SCPI")
                                //{
                                //    cmd.Parameters.AddWithValue("@DeviceIP", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@DevicePort", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@DeviceIndex", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@CanIndex", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@ComPort", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@BaudRate", DBNull.Value);

                                //    cmd.Parameters.AddWithValue("@DataBits", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@Parity", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@StopBits", DBNull.Value);
                                //}

                                if (form.DeviceType == "充放电设备")
                                {
                                    cmd.Parameters.AddWithValue("@ACNumber", form.ACNumber);
                                    cmd.Parameters.AddWithValue("@ACAddress", form.ACAddress);
                                    cmd.Parameters.AddWithValue("@DCNumber", form.DCNumber);
                                    cmd.Parameters.AddWithValue("@DCAddress", form.DCAddress);
                                }
                                else
                                {
                                    cmd.Parameters.AddWithValue("@ACNumber", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ACAddress", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DCNumber", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DCAddress", DBNull.Value);
                                }

                                cmd.ExecuteNonQuery();
                            }
                        }

                        LoadData();
                        // 保存成功后触发事件
                        ConfigUpdated?.Invoke(this, EventArgs.Empty);
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
                            // 修改更新语句以包含串口配置字段
                            const string query = @"UPDATE Equipment SET
                                        DeviceType = @DeviceType,
                                        DeviceName = @DeviceName,
                                        CanType = @CanType,
                                        DeviceIP = @DeviceIP,
                                        DevicePort = @DevicePort,
                                        ComPort = @ComPort,
                                        BaudRate = @BaudRate,
                                        DataBits = @DataBits,
                                        Parity = @Parity,
                                        StopBits = @StopBits,
                                        DeviceIndex = @DeviceIndex,
                                        CanIndex = @CanIndex,
                                        ACNumber = @ACNumber,
                                        ACAddress = @ACAddress,
                                        DCNumber = @DCNumber,
                                        DCAddress = @DCAddress,
                                        CommunicationProtocols = @CommunicationProtocols,
                                        Whether = @Whether
                                        WHERE EquipmentID = @EquipmentID";

                            using (var cmd = new SQLiteCommand(query, conn))
                            {
                                cmd.Parameters.AddWithValue("@EquipmentID", row["EquipmentID"]);
                                cmd.Parameters.AddWithValue("@DeviceType", form.DeviceType);
                                cmd.Parameters.AddWithValue("@DeviceName", form.DeviceName);
                                cmd.Parameters.AddWithValue("@CanType", form.CanType);
                                cmd.Parameters.AddWithValue("@CommunicationProtocols", form.CommunicationProtocols);
                                cmd.Parameters.AddWithValue("@Whether", form.Whether);

                                // 根据通讯类型设置相应的配置
                                if (form.CanType == "ZCAN_CANETTCP" || form.CanType == "ZCAN_CANFDNET_200U_TCP")
                                {
                                    cmd.Parameters.AddWithValue("@DeviceIP", form.DeviceIP);
                                    cmd.Parameters.AddWithValue("@DevicePort", form.DevicePort);
                                    cmd.Parameters.AddWithValue("@DeviceIndex", form.DeviceIndex);
                                    cmd.Parameters.AddWithValue("@CanIndex", form.CanIndex);
                                    cmd.Parameters.AddWithValue("@ComPort", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@BaudRate", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DataBits", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@Parity", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@StopBits", DBNull.Value);
                                }
                                else if (form.CanType == "RS485-MODBUS")
                                {
                                    cmd.Parameters.AddWithValue("@DeviceIP", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DevicePort", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DeviceIndex", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@CanIndex", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ComPort", form.ComPort);
                                    cmd.Parameters.AddWithValue("@BaudRate", form.BaudRate);
                                    cmd.Parameters.AddWithValue("@DataBits", form.DataBits);
                                    cmd.Parameters.AddWithValue("@Parity", form.Parity);
                                    cmd.Parameters.AddWithValue("@StopBits", form.StopBits);
                                }
                                else if (form.CanType == "RS232" || form.CanType == "USB-SCPI")
                                {
                                    cmd.Parameters.AddWithValue("@DeviceIP", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DevicePort", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DeviceIndex", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@CanIndex", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ComPort", form.ComPort);
                                    cmd.Parameters.AddWithValue("@BaudRate", form.BaudRate);
                                    cmd.Parameters.AddWithValue("@DataBits", form.DataBits);
                                    cmd.Parameters.AddWithValue("@Parity", form.Parity);
                                    cmd.Parameters.AddWithValue("@StopBits", form.StopBits);
                                }
                                //else if (form.CanType == "USB-SCPI")
                                //{
                                //    cmd.Parameters.AddWithValue("@DeviceIP", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@DevicePort", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@DeviceIndex", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@CanIndex", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@ComPort", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@BaudRate", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@DataBits", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@Parity", DBNull.Value);
                                //    cmd.Parameters.AddWithValue("@StopBits", DBNull.Value);
                                //}

                                if (form.DeviceType == "充放电设备")
                                {
                                    cmd.Parameters.AddWithValue("@ACNumber", form.ACNumber);
                                    cmd.Parameters.AddWithValue("@ACAddress", form.ACAddress);
                                    cmd.Parameters.AddWithValue("@DCNumber", form.DCNumber);
                                    cmd.Parameters.AddWithValue("@DCAddress", form.DCAddress);
                                }
                                else
                                {
                                    cmd.Parameters.AddWithValue("@ACNumber", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ACAddress", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DCNumber", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DCAddress", DBNull.Value);
                                }

                                cmd.ExecuteNonQuery();
                            }
                        }

                        // 刷新数据
                        LoadData();

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

                // 获取设备ID
                DataRow row = ((DataRowView)gridView.GetRow(selectedRowHandle)).Row;
                var equipmentId = row["EquipmentID"];
                var deviceName = row["DeviceName"].ToString();

                // 确认对话框
                if (XtraMessageBox.Show($"确定要删除设备 '{deviceName}' 吗？", "确认删除",
                                      MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                // 执行删除操作
                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();
                    const string query = "DELETE FROM Equipment WHERE EquipmentID = @EquipmentID";

                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@EquipmentID", equipmentId);
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