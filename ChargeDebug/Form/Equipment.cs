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
        private DataTable _equipmentTable;   // 新增：保留内存表引用

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
            gridview.Appearance.HeaderPanel.TextOptions.HAlignment = HorzAlignment.Center;
            gridview.Appearance.Row.TextOptions.HAlignment = HorzAlignment.Center;
            gridview.OptionsSelection.MultiSelect = true;
            gridview.OptionsSelection.MultiSelectMode = GridMultiSelectMode.RowSelect;
            gridview.OptionsSelection.EnableAppearanceFocusedCell = false;
            gridview.FocusRectStyle = DrawFocusRectStyle.RowFocus;

            // 焦点行不画灰色背景，只保留左边箭头指示
            gridview.Appearance.FocusedRow.Options.UseBackColor = false;
            gridview.Appearance.FocusedRow.Options.UseFont = false;

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

            SimpleButton btnInsert = new SimpleButton();
            btnInsert.Location = new Point(10, 170);
            btnInsert.Name = "btnInsert";
            btnInsert.Size = new Size(100, 30);
            btnInsert.TabIndex = 3;
            btnInsert.Text = "插入设备";

            // 上移按钮
            SimpleButton btnMoveUp = new SimpleButton();
            btnMoveUp.Location = new Point(10, 220);
            btnMoveUp.Name = "btnMoveUp";
            btnMoveUp.Size = new Size(100, 30);
            btnMoveUp.TabIndex = 4;
            btnMoveUp.Text = "上移设备";
            // 下移按钮
            SimpleButton btnMoveDown = new SimpleButton();
            btnMoveDown.Location = new Point(10, 270);
            btnMoveDown.Name = "btnMoveDown";
            btnMoveDown.Size = new Size(100, 30);
            btnMoveDown.TabIndex = 5;
            btnMoveDown.Text = "下移设备";

            PanelControl panelControl = new PanelControl();
            panelControl.Dock = DockStyle.Right;
            panelControl.Width = 120;
            panelControl.Controls.AddRange(new[] { 
                simpleButton, simpleButton1, simpleButton2,
                btnInsert, btnMoveUp, btnMoveDown
            });

            this.Controls.Add(gridControl);
            this.Controls.Add(panelControl);

            // 为按钮添加点击事件
            simpleButton.Click += AddDevice_Click;
            simpleButton1.Click += EditDevice_Click;
            simpleButton2.Click += DeleteDevice_Click;
            btnInsert.Click += InsertDevice_Click;   // 新增
            btnMoveUp.Click += MoveUp_Click;         // 新增
            btnMoveDown.Click += MoveDown_Click;       // 新增
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
                    const string query = @"SELECT 
                        EquipmentID, DeviceType, DeviceNumber, DeviceName, CanType, 
                        DeviceIP, DevicePort, ComPort, BaudRate, DataBits, Parity, StopBits,
                        DeviceIndex, CanIndex, ACNumber, ACAddress, DCNumber, DCAddress, 
                        CommunicationProtocols, CanMode, TermResistor, ArbBaud, DataBaud, Whether 
                        FROM Equipment ORDER BY [DeviceNumber] ASC";
                    using (var cmd = new SQLiteCommand(query, conn))
                    using (var adapter = new SQLiteDataAdapter(cmd))
                    {
                        adapter.Fill(dataTable);
                    }
                }

                if (!dataTable.Columns.Contains("ConnectionStatus"))
                {
                    dataTable.Columns.Add("ConnectionStatus", typeof(string));
                    foreach (DataRow row in dataTable.Rows)
                        row["ConnectionStatus"] = "未知";
                }

                _equipmentTable = dataTable;
                _equipmentTable.DefaultView.Sort = "DeviceNumber ASC";  // 让视图按序号排序
                gridControl.DataSource = _equipmentTable;               // 不要先设 null
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"Error loading data: {ex.Message}", "Error",
                                 MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SwapRowsInMemory(DataRow rowA, int numA, DataRow rowB, int numB)
        {
            rowA["DeviceNumber"] = numB;
            rowB["DeviceNumber"] = numA;
        }

        // 根据 DeviceNumber 定位并选中对应行
        private void FocusRowByDeviceNumber(int deviceNumber)
        {
            for (int i = 0; i < gridview.DataRowCount; i++)
            {
                var drv = (DataRowView)gridview.GetRow(i);
                if (Convert.ToInt32(drv["DeviceNumber"]) == deviceNumber)
                {
                    gridview.FocusedRowHandle = i;
                    gridview.ClearSelection();
                    gridview.SelectRow(i);
                    break;
                }
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
                                DeviceIndex, CanIndex, ACNumber, ACAddress, DCNumber, DCAddress, CommunicationProtocols, 
                                CanMode, TermResistor, ArbBaud, DataBaud, Whether)
                                VALUES 
                                (@DeviceNumber, @DeviceName, @DeviceType, @CanType, 
                                @DeviceIP, @DevicePort, @ComPort, @BaudRate, @DataBits, @Parity, @StopBits,
                                @DeviceIndex, @CanIndex, @ACNumber, @ACAddress, @DCNumber, @DCAddress, @CommunicationProtocols, 
                                @CanMode, @TermResistor, @ArbBaud, @DataBaud, @Whether)";

                            using (var cmd = new SQLiteCommand(insertQuery, conn))
                            {
                                cmd.Parameters.AddWithValue("@DeviceType", form.DeviceType);
                                cmd.Parameters.AddWithValue("@DeviceNumber", newDeviceNumber);
                                cmd.Parameters.AddWithValue("@DeviceName", form.DeviceName);
                                cmd.Parameters.AddWithValue("@CanType", form.CanType);
                                cmd.Parameters.AddWithValue("@CommunicationProtocols", form.CommunicationProtocols);
                                cmd.Parameters.AddWithValue("@Whether", form.Whether);

                                // 根据通讯类型设置相应的配置
                                if (form.CanType == "ZCAN_CANETTCP" || form.CanType == "ZCAN_CANFDNET_200U_TCP" || form.CanType == "GCAN‑GT‑418" || form.CanType == "TCP-MODBUS")
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
                                    cmd.Parameters.AddWithValue("@CanMode", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@TermResistor", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ArbBaud", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DataBaud", DBNull.Value);
                                }
                                else if(form.DeviceType == "电池BMS")
                                {
                                    cmd.Parameters.AddWithValue("@ACNumber", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ACAddress", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DCNumber", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DCAddress", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@CanMode", form.CanMode);
                                    cmd.Parameters.AddWithValue("@TermResistor", form.TermResistor);
                                    cmd.Parameters.AddWithValue("@ArbBaud", form.ArbBaud);
                                    cmd.Parameters.AddWithValue("@DataBaud", form.DataBaud);
                                }
                                else
                                {
                                    cmd.Parameters.AddWithValue("@ACNumber", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ACAddress", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DCNumber", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DCAddress", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@CanMode", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@TermResistor", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ArbBaud", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DataBaud", DBNull.Value);
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
                                        CanMode = @CanMode,
                                        TermResistor = @TermResistor,
                                        ArbBaud = @ArbBaud,
                                        DataBaud = @DataBaud,
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
                                if (form.CanType == "ZCAN_CANETTCP" || form.CanType == "ZCAN_CANFDNET_200U_TCP" || form.CanType == "GCAN‑GT‑418" || form.CanType == "TCP-MODBUS")
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
                                    cmd.Parameters.AddWithValue("@CanMode", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@TermResistor", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ArbBaud", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DataBaud", DBNull.Value);
                                }
                                else if (form.DeviceType == "电池BMS")
                                {
                                    cmd.Parameters.AddWithValue("@ACNumber", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ACAddress", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DCNumber", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DCAddress", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@CanMode", form.CanMode);
                                    cmd.Parameters.AddWithValue("@TermResistor", form.TermResistor);
                                    cmd.Parameters.AddWithValue("@ArbBaud", form.ArbBaud);
                                    cmd.Parameters.AddWithValue("@DataBaud", form.DataBaud);
                                }
                                else
                                {
                                    cmd.Parameters.AddWithValue("@ACNumber", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ACAddress", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DCNumber", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DCAddress", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@CanMode", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@TermResistor", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@ArbBaud", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@DataBaud", DBNull.Value);
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

        // 插入设备
        private void InsertDevice_Click(object sender, EventArgs e)
        {
            try
            {
                var gridView = gridControl.MainView as GridView;
                var selectedRowHandle = gridView.FocusedRowHandle;

                // 未选中行时默认追加到末尾
                int insertNumber;
                if (selectedRowHandle == GridControl.InvalidRowHandle)
                {
                    // 没选中行，取当前最大序号+1 作为新号
                    insertNumber = GetMaxDeviceNumber() + 1;
                }
                else
                {
                    DataRow row = ((DataRowView)gridView.GetRow(selectedRowHandle)).Row;
                    insertNumber = Convert.ToInt32(row["DeviceNumber"]);
                }

                using (var form = new DeviceEditForm(dbcPath, insertNumber))
                {
                    if (form.ShowDialog() != DialogResult.OK) return;

                    using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                    {
                        conn.Open();
                        using (var transaction = conn.BeginTransaction())
                        {
                            try
                            {
                                // 1) 把 DeviceNumber >= insertNumber 的记录整体后移一位，腾出 insertNumber 位置
                                //    分两步走，避免 UNIQUE 约束冲突：先加大偏移，再减回来
                                const int offset = 100000;

                                using (var shiftUp = new SQLiteCommand(
                                    "UPDATE Equipment SET DeviceNumber = DeviceNumber + @Offset WHERE DeviceNumber >= @Num",
                                    conn, transaction))
                                {
                                    shiftUp.Parameters.AddWithValue("@Offset", offset);
                                    shiftUp.Parameters.AddWithValue("@Num", insertNumber);
                                    shiftUp.ExecuteNonQuery();
                                }

                                using (var shiftDown = new SQLiteCommand(
                                    "UPDATE Equipment SET DeviceNumber = DeviceNumber - @Offset + 1 WHERE DeviceNumber >= @Num + @Offset",
                                    conn, transaction))
                                {
                                    shiftDown.Parameters.AddWithValue("@Offset", offset);
                                    shiftDown.Parameters.AddWithValue("@Num", insertNumber);
                                    shiftDown.ExecuteNonQuery();
                                }

                                // 2) 插入新记录（与 AddDevice_Click 里的字段赋值逻辑一致）
                                const string insertQuery = @"INSERT INTO Equipment
                                                (DeviceNumber, DeviceName, DeviceType, CanType,
                                                 DeviceIP, DevicePort, ComPort, BaudRate, DataBits, Parity, StopBits,
                                                 DeviceIndex, CanIndex, ACNumber, ACAddress, DCNumber, DCAddress,
                                                 CommunicationProtocols, CanMode, TermResistor, ArbBaud, DataBaud, Whether)
                                                VALUES
                                                (@DeviceNumber, @DeviceName, @DeviceType, @CanType,
                                                 @DeviceIP, @DevicePort, @ComPort, @BaudRate, @DataBits, @Parity, @StopBits,
                                                 @DeviceIndex, @CanIndex, @ACNumber, @ACAddress, @DCNumber, @DCAddress,
                                                 @CommunicationProtocols, @CanMode, @TermResistor, @ArbBaud, @DataBaud, @Whether)";

                                using (var cmd = new SQLiteCommand(insertQuery, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@DeviceType", form.DeviceType);
                                    cmd.Parameters.AddWithValue("@DeviceNumber", insertNumber);
                                    cmd.Parameters.AddWithValue("@DeviceName", form.DeviceName);
                                    cmd.Parameters.AddWithValue("@CanType", form.CanType);
                                    cmd.Parameters.AddWithValue("@CommunicationProtocols", form.CommunicationProtocols);
                                    cmd.Parameters.AddWithValue("@Whether", form.Whether);

                                    if (form.CanType == "ZCAN_CANETTCP" || form.CanType == "ZCAN_CANFDNET_200U_TCP"
                                        || form.CanType == "GCAN‑GT‑418" || form.CanType == "TCP-MODBUS")
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

                                    if (form.DeviceType == "充放电设备")
                                    {
                                        cmd.Parameters.AddWithValue("@ACNumber", form.ACNumber);
                                        cmd.Parameters.AddWithValue("@ACAddress", form.ACAddress);
                                        cmd.Parameters.AddWithValue("@DCNumber", form.DCNumber);
                                        cmd.Parameters.AddWithValue("@DCAddress", form.DCAddress);
                                        cmd.Parameters.AddWithValue("@CanMode", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@TermResistor", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@ArbBaud", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@DataBaud", DBNull.Value);
                                    }
                                    else if (form.DeviceType == "电池BMS")
                                    {
                                        cmd.Parameters.AddWithValue("@ACNumber", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@ACAddress", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@DCNumber", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@DCAddress", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@CanMode", form.CanMode);
                                        cmd.Parameters.AddWithValue("@TermResistor", form.TermResistor);
                                        cmd.Parameters.AddWithValue("@ArbBaud", form.ArbBaud);
                                        cmd.Parameters.AddWithValue("@DataBaud", form.DataBaud);
                                    }
                                    else
                                    {
                                        cmd.Parameters.AddWithValue("@ACNumber", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@ACAddress", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@DCNumber", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@DCAddress", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@CanMode", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@TermResistor", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@ArbBaud", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@DataBaud", DBNull.Value);
                                    }

                                    cmd.ExecuteNonQuery();
                                }

                                transaction.Commit();
                            }
                            catch
                            {
                                transaction.Rollback();
                                throw;
                            }
                        }
                    }

                    ConfigUpdated?.Invoke(this, EventArgs.Empty);
                    ReloadAndFocus(insertNumber);

                    //LoadData();
                    //// 选中刚插入的那一行
                    //gridView.FocusedRowHandle = (selectedRowHandle == GridControl.InvalidRowHandle)
                    //                            ? gridView.DataRowCount - 1
                    //                            : selectedRowHandle;
                    //ConfigUpdated?.Invoke(this, EventArgs.Empty);
                    XtraMessageBox.Show("设备插入成功！", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"插入失败：{ex.Message}", "错误",
                                 MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private int GetMaxDeviceNumber()
        {
            using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT MAX(DeviceNumber) FROM Equipment", conn))
                {
                    object result = cmd.ExecuteScalar();
                    return result == DBNull.Value ? 0 : Convert.ToInt32(result);
                }
            }
        }

        // 上移设备
        private void MoveUp_Click(object sender, EventArgs e)
        {
            try
            {
                var gridView = gridControl.MainView as GridView;
                var selectedRowHandle = gridView.FocusedRowHandle;

                if (selectedRowHandle == GridControl.InvalidRowHandle)
                {
                    XtraMessageBox.Show("请先选择要移动的设备！", "提示",
                                     MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (selectedRowHandle <= 0)
                {
                    XtraMessageBox.Show("已经是第一个设备，无法上移！", "提示",
                                     MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                DataRow currentRow = ((DataRowView)gridView.GetRow(selectedRowHandle)).Row;
                DataRow aboveRow = ((DataRowView)gridView.GetRow(selectedRowHandle - 1)).Row;

                int currentNumber = Convert.ToInt32(currentRow["DeviceNumber"]);
                int aboveNumber = Convert.ToInt32(aboveRow["DeviceNumber"]);
                int currentId = Convert.ToInt32(currentRow["EquipmentID"]);
                int aboveId = Convert.ToInt32(aboveRow["EquipmentID"]);

                // 1) 数据库里交换序号
                SwapDeviceNumber(currentId, aboveId, currentNumber, aboveNumber);

                // 2) 直接改内存里的两个值，DataView 自动重排，不重新查库
                SwapRowsInMemory(currentRow, currentNumber, aboveRow, aboveNumber);

                // 3) 定位到移动后的行（当前设备上移后，它的新序号是 aboveNumber）
                FocusRowByDeviceNumber(aboveNumber);

                //ConfigUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"上移失败：{ex.Message}", "错误",
                                 MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 下移设备
        private void MoveDown_Click(object sender, EventArgs e)
        {
            try
            {
                var gridView = gridControl.MainView as GridView;
                var selectedRowHandle = gridView.FocusedRowHandle;

                if (selectedRowHandle == GridControl.InvalidRowHandle)
                {
                    XtraMessageBox.Show("请先选择要移动的设备！", "提示",
                                     MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (selectedRowHandle >= gridView.DataRowCount - 1)
                {
                    XtraMessageBox.Show("已经是最后一个设备，无法下移！", "提示",
                                     MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                DataRow currentRow = ((DataRowView)gridView.GetRow(selectedRowHandle)).Row;
                DataRow belowRow = ((DataRowView)gridView.GetRow(selectedRowHandle + 1)).Row;

                int currentNumber = Convert.ToInt32(currentRow["DeviceNumber"]);
                int belowNumber = Convert.ToInt32(belowRow["DeviceNumber"]);
                int currentId = Convert.ToInt32(currentRow["EquipmentID"]);
                int belowId = Convert.ToInt32(belowRow["EquipmentID"]);

                // 1) 数据库里交换序号
                SwapDeviceNumber(currentId, belowId, currentNumber, belowNumber);

                // 2) 直接改内存里的两个值
                SwapRowsInMemory(currentRow, currentNumber, belowRow, belowNumber);

                // 3) 定位到移动后的行（当前设备下移后，它的新序号是 belowNumber）
                FocusRowByDeviceNumber(belowNumber);

                //ConfigUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"下移失败：{ex.Message}", "错误",
                                 MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SwapDeviceNumber(int id1, int id2, int num1, int num2)
        {
            using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
            {
                conn.Open();
                // 单条语句同时交换两行，SQLite 在语句级别处理，不会触发唯一约束冲突
                const string sql = @"UPDATE Equipment
                               SET DeviceNumber = CASE EquipmentID
                                                     WHEN @Id1 THEN @Num2
                                                     WHEN @Id2 THEN @Num1
                                                 END
                             WHERE EquipmentID IN (@Id1, @Id2)";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id1", id1);
                    cmd.Parameters.AddWithValue("@Id2", id2);
                    cmd.Parameters.AddWithValue("@Num1", num1);
                    cmd.Parameters.AddWithValue("@Num2", num2);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // 刷新数据后，选中指定 DeviceNumber 对应的行
        private void ReloadAndFocus(int deviceNumber)
        {
            LoadData();
            // 数据绑定是异步完成的，用 BeginInvoke 延迟到下一帧再定位
            this.BeginInvoke(new Action(() =>
            {
                var gv = gridControl.MainView as GridView;
                for (int i = 0; i < gv.DataRowCount; i++)
                {
                    var row = (DataRowView)gv.GetRow(i);
                    if (Convert.ToInt32(row["DeviceNumber"]) == deviceNumber)
                    {
                        gv.FocusedRowHandle = i;
                        gv.ClearSelection();
                        gv.SelectRow(i);
                        break;
                    }
                }
            }));
        }
    }
}