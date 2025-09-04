using DevExpress.XtraEditors;
using DevExpress.XtraTreeList;
using DevExpress.XtraTreeList.Columns;
using DevExpress.XtraLayout;
using DevExpress.Utils;
using DevExpress.XtraLayout.Utils;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid;
using System.Data.SQLite;
using System.Data;
using ClosedXML.Excel;
using ChargeDebug.Service;
using DevExpress.XtraVerticalGrid;
using DbcParserLib.Model;
using DataModel;
using Aspose.Pdf.Devices;
using DevExpress.Diagram.Core.Shapes;
using static ChargeDebug.Service.CANManager;
using System.Diagnostics.Metrics;
using Aspose.Pdf.Operators;
using Log;
using Aspose.Pdf.Annotations;

namespace ChargeDebug.Form
{
    public partial class CalibrationManagement : XtraUserControl
    {
        private string sqladdress = "";
        private TreeList treeList;
        private List<EquipmentModel> equipmentList;

        // 添加组合框字段
        private ComboBoxEdit cbVoltageSource;
        private ComboBoxEdit cbVoltmeter;
        private ComboBoxEdit cbAmmeter;

        public CalibrationManagement(string sqladdress, List<EquipmentModel> equipmentList)
        {
            this.equipmentList = equipmentList;
            this.sqladdress = sqladdress;
            InitializeComponent();
            InitializeUI();
            this.Load += CalibrationManagement_Load;
        }

        private void CalibrationManagement_Load(object? sender, EventArgs e)
        {
            // 加载数据
            LoadData();
        }

        private void LoadData()
        {
            treeList.BeginUpdate();
            treeList.ClearNodes();
            try
            {
                string connectionString = $"Data Source={sqladdress};Version=3;";

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    var calibrationsignals = SQLite_Service.GetCalibrationSignals(conn);

                    foreach (var calibrationsignal in calibrationsignals)
                    {
                        var node = treeList.AppendNode(new object[]
                        {
                            calibrationsignal.DeviceName,
                            calibrationsignal.SignalName,
                            calibrationsignal.SignalType,
                            calibrationsignal.ReadTime,
                            calibrationsignal.RatingVoltageCurrent,
                            calibrationsignal.CalibrationNumber
                        }, null);

                        // 设置节点的 Tag 为信号 ID，以便后续操作
                        node.Tag = calibrationsignal.SignalID;
                        // 设置排序值
                        node.SetValue("Orders", calibrationsignal.Orders);
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"加载校准管理数据失败: {ex.Message}");
            }
            finally
            {
                treeList.EndUpdate();
            }
        }

        private void BtnAddSignal_Click(object? sender, EventArgs e)
        {
            try
            {
                // 创建并显示添加信号的对话框
                using (var addForm = new AddEditCalibrationSignalForm(null, sqladdress, equipmentList))
                {
                    if (addForm.ShowDialog() == DialogResult.OK)
                    {
                        // 刷新数据
                        LoadData();
                        XtraMessageBox.Show("信号添加成功!");
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"添加信号失败: {ex.Message}");
            }
        }

        private void BtnEditSignal_Click(object? sender, EventArgs e)
        {
            if (treeList.FocusedNode == null)
            {
                XtraMessageBox.Show("请先选择一个信号进行编辑!");
                return;
            }

            try
            {
                // 获取选中信号的ID
                long signalId = (long)treeList.FocusedNode.Tag;

                // 创建并显示编辑信号的对话框
                using (var editForm = new AddEditCalibrationSignalForm(signalId, sqladdress,equipmentList))
                {
                    if (editForm.ShowDialog() == DialogResult.OK)
                    {
                        // 刷新数据
                        LoadData();
                        XtraMessageBox.Show("信号编辑成功!");
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"编辑信号失败: {ex.Message}");
            }
        }

        private void BtnDeleteSignal_Click(object? sender, EventArgs e)
        {
            // 获取所有选中的节点
            var selectedNodes = treeList.Selection;
            int num = selectedNodes.Count;

            if (selectedNodes.Count == 0)
            {
                XtraMessageBox.Show("请先选择要删除的信号!");
                return;
            }

            // 构建确认消息
            string message = selectedNodes.Count == 1
                ? $"确定要删除设备 '{selectedNodes[0].GetValue("DeviceName")}' 的信号 '{selectedNodes[0].GetValue("SignalName")}' 吗？"
                : $"确定要删除选中的 {num} 个信号吗？";

            // 确认删除对话框
            if (XtraMessageBox.Show(message,
                                  "确认删除",
                                  MessageBoxButtons.YesNo,
                                  MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    conn.Open();

                    // 开始事务
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // 获取要删除的信号ID列表
                            var signalIds = selectedNodes
                                .Select(node => (long)node.Tag)
                                .ToList();

                            // 执行批量删除操作
                            bool deleteSuccess = SQLite_Service.DeleteCalibrationSignals(conn, signalIds);

                            if (deleteSuccess)
                            {
                                // 重新排序剩余的信号
                                bool reorderSuccess = SQLite_Service.ReorderCalibrationSignals(conn);

                                if (reorderSuccess)
                                {
                                    transaction.Commit();

                                    // 刷新数据
                                    LoadData();
                                    XtraMessageBox.Show($"成功删除 {num} 个信号!");
                                }
                                else
                                {
                                    transaction.Rollback();
                                    XtraMessageBox.Show("重新排序失败，操作已回滚!");
                                }
                            }
                            else
                            {
                                transaction.Rollback();
                                XtraMessageBox.Show("删除失败，操作已回滚!");
                            }
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"删除信号失败: {ex.Message}");
            }
        }

        private void InitializeUI()
        {
            // 主布局控件，填充整个用户控件
            LayoutControl layoutControl = new LayoutControl
            {
                Parent = this,
                Dock = DockStyle.Fill
            };

            // 4. 创建根布局组（垂直方向）
            LayoutControlGroup rootGroup = new LayoutControlGroup();
            rootGroup.TextVisible = false;
            rootGroup.GroupBordersVisible = false;
            rootGroup.DefaultLayoutType = LayoutType.Vertical; //垂直排列
            layoutControl.Root.Add(rootGroup);

            // 5. 添加按钮组到根组
            LayoutControlItem buttonGroupItem = rootGroup.AddItem();
            buttonGroupItem.Control = CreateButtonContainer();
            buttonGroupItem.TextVisible = false;
            buttonGroupItem.SizeConstraintsType = SizeConstraintsType.Custom;
            buttonGroupItem.MinSize = new Size(0, 60);
            buttonGroupItem.MaxSize = new Size(0, 60);

            // 6. 添加TreeList到根组
            LayoutControlItem layoutControlItemForTreeList = rootGroup.AddItem();
            layoutControlItemForTreeList.Control = CreateTreeList();
            layoutControlItemForTreeList.TextVisible = false;
            layoutControlItemForTreeList.SizeConstraintsType = SizeConstraintsType.Custom;
            layoutControlItemForTreeList.MinSize = new Size(0, 0);
            layoutControlItemForTreeList.MaxSize = new Size(0, 800);

            // 7. 添加底部进度条面板到根组
            LayoutControlItem progressGroupItem = rootGroup.AddItem();
            progressGroupItem.Control = CreateProgressContainer();
            progressGroupItem.TextVisible = false;
            progressGroupItem.SizeConstraintsType = SizeConstraintsType.Custom;
            progressGroupItem.MinSize = new Size(0, 60);
            progressGroupItem.MaxSize = new Size(0, 60);
        }

        

        private async void BtnVoltageCalibration_Click(object? sender, EventArgs e)
        {
            try
            {
                // 1. 获取选中的设备
                string? voltageSourceName = cbVoltageSource.SelectedItem?.ToString();
                string? voltmeterName = cbVoltmeter.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(voltageSourceName) || string.IsNullOrEmpty(voltmeterName))
                {
                    XtraMessageBox.Show("请先选择所有必要的校准设备!");
                    return;
                }

                // 2. 从设备列表中查找设备信息
                var voltageSource = equipmentList.FirstOrDefault(e => e.DeviceName == voltageSourceName);
                var voltmeter = equipmentList.FirstOrDefault(e => e.DeviceName == voltmeterName);
                if (voltageSource == null || voltmeter == null)
                {
                    XtraMessageBox.Show("未找到选定的设备配置信息!");
                    return;
                }

                // 3. 根据设备通讯类型启动设备
                bool voltageSourceStarted = await StartEquipment(voltageSource);
                bool voltmeterStarted = await StartEquipment(voltmeter);
                if (!voltageSourceStarted || !voltmeterStarted)
                {
                    XtraMessageBox.Show("设备启动失败，请检查设备连接!");
                    return;
                }

                // 4. 检查充放电设备CAN盒连接
                //bool canConnected = await CheckChargingDischargingCANConnection();
                //if (!canConnected)
                //{
                //    XtraMessageBox.Show("充放电设备CAN盒连接失败，请检查连接!");
                //    return;
                //}

                // 5. 所有设备启动成功，开始电压校准流程
                LogService.Log("所有校准设备启动成功，开始电压校准流程!");
                // 这里可以调用具体的电压校准方法
                await StartVoltageCalibration(voltageSource, voltmeter);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"电压校准失败: {ex.Message}");
            }
        }

        private async Task<bool> StartEquipment(EquipmentModel equipment)
        {
            try
            {
                // 根据设备通讯类型调用不同的启动方法
                switch (equipment.CanType)
                {
                    case "CANET-2E-U":
                        // 使用CAN管理器启动CAN设备
                        CANManager.Instance.RegisterChannel(equipment);
                        // 等待设备连接确认
                        await Task.Delay(500); // 给设备一些时间连接
                        // 检查设备是否成功连接
                        string key = CANManager.GetChannelKey(equipment.DeviceIndex, equipment.CanIndex);
                        return CANManager.Instance.IsChannelConnected(key);

                    case "RS485-MODBUS":
                        // 使用RS485管理器启动设备
                        bool connected = RS485Manager.Instance.RegisterChannel(equipment);
                        if (!connected)
                        {
                            return false;
                        }
                        return true;

                    default:
                        XtraMessageBox.Show($"不支持的通讯类型: {equipment.CanType}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"启动设备 {equipment.DeviceName} 失败: {ex.Message}");
                return false;
            }
        }

        private async Task StartVoltageCalibration(EquipmentModel voltageSource, EquipmentModel voltmeter)
        {
            try
            {
                byte[] calibrationCommand = new byte[] { 0x63, 0x10, 0x00, 0x02, 0x00, 0x01, 0x02, 0x00, 0x01 };
                // 发送校准命令
                bool sendSuccess = RS485Manager.Instance.SendData(voltageSource.ComPort, calibrationCommand);
            }
            catch (Exception)
            {
                throw;
            }
        }


        private void BtnStopCalibration_Click(object? sender, EventArgs e)
        {
            try
            {
                byte[] calibrationCommand = new byte[] { 0x63, 0x10, 0x00, 0x02, 0x00, 0x01, 0x02, 0x00, 0x00 };
                // 发送校准命令
                bool sendSuccess = RS485Manager.Instance.SendData("COM3", calibrationCommand);
            }
            catch (Exception)
            {
                throw;
            }
        }

        private Control CreateButtonContainer()
        {
            // 创建按钮容器面板
            PanelControl buttonPanel = new PanelControl
            {
                Padding = new System.Windows.Forms.Padding(0),
                Height = 60,
                BorderStyle = BorderStyles.NoBorder // 隐藏边框
            };

            // 电压源型号
            LabelControl lblVoltageSource = new LabelControl
            {
                Text = "电压源型号:",
                Size = new Size(100, 30),
                Location = new Point(10, 14)
            };
            buttonPanel.Controls.Add(lblVoltageSource);

            cbVoltageSource = new ComboBoxEdit
            {
                Size = new Size(160, 30),
                Location = new Point(lblVoltageSource.Right + 10, 12),
                Properties = { TextEditStyle = TextEditStyles.DisableTextEditor }
            };

            // 从equipmentList中筛选电压源型号
            var voltageSources = equipmentList
                .Where(e => e.DeviceType == "电压源")
                .Select(e => e.DeviceName)
                .ToList();
            cbVoltageSource.Properties.Items.AddRange(voltageSources);
            cbVoltageSource.SelectedIndex = 0;
            buttonPanel.Controls.Add(cbVoltageSource);

            // 电压表型号
            LabelControl lblVoltmeter = new LabelControl
            {
                Text = "电压表型号:",
                Location = new Point(cbVoltageSource.Right + 10, 14),
                AutoSize = true
            };
            buttonPanel.Controls.Add(lblVoltmeter);

            cbVoltmeter = new ComboBoxEdit
            {
                Size = new Size(160, 30),
                Location = new Point(lblVoltmeter.Right + 10, 12),
                Properties = { TextEditStyle = TextEditStyles.DisableTextEditor }
            };

            // 从equipmentList中筛选电压表型号
            var voltmeters = equipmentList
                .Where(e => e.DeviceType == "电压表")
                .Select(e => e.DeviceName)
                .ToList();
            cbVoltmeter.Properties.Items.AddRange(voltmeters);
            cbVoltmeter.SelectedIndex = 0;
            buttonPanel.Controls.Add(cbVoltmeter);

            // 电流表型号
            LabelControl lblAmmeter = new LabelControl
            {
                Text = "电流表型号:",
                Location = new Point(cbVoltmeter.Right + 10, 14)
            };
            buttonPanel.Controls.Add(lblAmmeter);

            cbAmmeter = new ComboBoxEdit
            {
                Size = new Size(160, 30),
                Location = new Point(lblAmmeter.Right + 10, 12),
                Properties = { TextEditStyle = TextEditStyles.DisableTextEditor }
            };

            // 从equipmentList中筛选电流表型号
            var ammeters = equipmentList
                .Where(e => e.DeviceType == "电流表")
                .Select(e => e.DeviceName)
                .ToList();
            cbAmmeter.Properties.Items.AddRange(ammeters);
            cbAmmeter.SelectedIndex = 0;
            buttonPanel.Controls.Add(cbAmmeter);

            // 添加按钮到面板
            SimpleButton btnAddSignal = new SimpleButton
            {
                Text = "添加信号",
                Size = new Size(80, 30),
                Location = new Point(cbAmmeter.Right + 50, 10)
            };
            btnAddSignal.Click += BtnAddSignal_Click;
            buttonPanel.Controls.Add(btnAddSignal);

            SimpleButton btnEditSignal = new SimpleButton
            {
                Text = "编辑信号",
                Size = new Size(80, 30),
                Location = new Point(btnAddSignal.Right + 10, btnAddSignal.Location.Y)
            };
            btnEditSignal.Click += BtnEditSignal_Click;
            buttonPanel.Controls.Add(btnEditSignal);

            SimpleButton btnDeleteSignal = new SimpleButton
            {
                Text = "删除信号",
                Size = new Size(80, 30),
                Location = new Point(btnEditSignal.Right + 10, btnEditSignal.Location.Y)
            };
            btnDeleteSignal.Click += BtnDeleteSignal_Click;
            buttonPanel.Controls.Add(btnDeleteSignal);

            //SimpleButton btnCalibrationEquipment = new SimpleButton
            //{
            //    Text = "打开校准设备",
            //    Size = new Size(110, 30),
            //    Location = new Point(btnDeleteSignal.Right + 10, 10)
            //};
            //btnCalibrationEquipment.Click += BtnCalibrationEquipment_Click;
            //buttonPanel.Controls.Add(btnCalibrationEquipment);

            SimpleButton btnVoltageCalibration = new SimpleButton
            {
                Text = "开始电压校准",
                Size = new Size(110, 30),
                Location = new Point(btnDeleteSignal.Right + 10, 10)
            };
            btnVoltageCalibration.Click += BtnVoltageCalibration_Click;
            buttonPanel.Controls.Add(btnVoltageCalibration);

            SimpleButton btnChargingCurrentCalibration = new SimpleButton
            {
                Text = "开始充电电流校准",
                Size = new Size(140, 30),
                Location = new Point(btnVoltageCalibration.Right + 10, 10)
            };
            buttonPanel.Controls.Add(btnChargingCurrentCalibration);

            SimpleButton btnDischargingCurrentCalibration = new SimpleButton
            {
                Text = "开始放电电流校准",
                Size = new Size(140, 30),
                Location = new Point(btnChargingCurrentCalibration.Right + 10, 10)
            };
            buttonPanel.Controls.Add(btnDischargingCurrentCalibration);

            SimpleButton btnStopCalibration = new SimpleButton
            {
                Text = "停止校准",
                Size = new Size(80, 30),
                Location = new Point(btnDischargingCurrentCalibration.Right + 10, 10)
            };
            btnStopCalibration.Click += BtnStopCalibration_Click;
            buttonPanel.Controls.Add(btnStopCalibration);

            SimpleButton btnExportData = new SimpleButton
            {
                Text = "导出数据",
                Size = new Size(80, 30),
                Location = new Point(btnStopCalibration.Right + 10, 10)
            };
            buttonPanel.Controls.Add(btnExportData);

            return buttonPanel;
        }

        private Control CreateTreeList()
        {
            treeList = new TreeList();
            // 启用多选
            treeList.OptionsSelection.MultiSelect = true;
            treeList.OptionsSelection.UseIndicatorForSelection = true;
            treeList.OptionsBehavior.Editable = false;

            // 添加列
            TreeListColumn column;

            // 设备名称列
            column = treeList.Columns.Add();
            column.Caption = "设备名称";
            column.FieldName = "DeviceName";
            column.VisibleIndex = 0;
            column.Width = 120;

            // 信号名称列
            column = treeList.Columns.Add();
            column.Caption = "信号名称";
            column.FieldName = "SignalName";
            column.VisibleIndex = 1;
            column.Width = 120;

            // 信号类型列
            column = treeList.Columns.Add();
            column.Caption = "信号类型";
            column.FieldName = "SignalType";
            column.VisibleIndex = 2;
            column.Width = 60;

            column = treeList.Columns.Add();
            column.Caption = "稳定读取时间";
            column.FieldName = "ReadTime";
            column.VisibleIndex = 3;
            column.Width = 100;

            // 额定电压/电流
            column = treeList.Columns.Add();
            column.Caption = "额定电压/电流";
            column.FieldName = "RatingVoltageCurrent";
            column.VisibleIndex = 4;
            column.Width = 100;

            column = treeList.Columns.Add();
            column.Caption = "校准点个数";
            column.FieldName = "CalibrationNumber";
            column.VisibleIndex = 5;
            column.Width = 80;

            // 比例系数列
            column = treeList.Columns.Add();
            column.Caption = "比例系数";
            column.FieldName = "ScaleFactor";
            column.VisibleIndex = 6;
            column.Width = 100;

            // 零点系数列
            column = treeList.Columns.Add();
            column.Caption = "零点系数";
            column.FieldName = "ZeroFactor";
            column.VisibleIndex = 7;
            column.Width = 100;

            // 设备电压采样值列
            column = treeList.Columns.Add();
            column.Caption = "设备电压采样值";
            column.FieldName = "DeviceVoltageSample";
            column.VisibleIndex = 8;
            column.Width = 120;

            // 实际电压测量值列
            column = treeList.Columns.Add();
            column.Caption = "实际电压测量值";
            column.FieldName = "ActualVoltageMeasurement";
            column.VisibleIndex = 9;
            column.Width = 120;

            // 设备电流采样值列
            column = treeList.Columns.Add();
            column.Caption = "设备电流采样值";
            column.FieldName = "DeviceCurrentSample";
            column.VisibleIndex = 10;
            column.Width = 120;

            // 实际电流测量值列
            column = treeList.Columns.Add();
            column.Caption = "实际电流测量值";
            column.FieldName = "ActualCurrentMeasurement";
            column.VisibleIndex = 11;
            column.Width = 120;

            // 校准精度列
            column = treeList.Columns.Add();
            column.Caption = "校准精度";
            column.FieldName = "CalibrationAccuracy";
            column.VisibleIndex = 12;
            column.Width = 80;

            // 校准结果列
            column = treeList.Columns.Add();
            column.Caption = "校准结果";
            column.FieldName = "CalibrationResult";
            column.VisibleIndex = 13;
            column.Width = 80;

            // 设置所有列的内容和标题居中显示
            foreach (TreeListColumn col in treeList.Columns)
            {
                // 设置列内容居中
                col.AppearanceCell.TextOptions.HAlignment = HorzAlignment.Center;

                // 设置列标题居中
                col.AppearanceHeader.TextOptions.HAlignment = HorzAlignment.Center;

                // 可选：设置标题字体样式
                //col.AppearanceHeader.Font = new Font(treeList.Font, FontStyle.Bold);
            }

            // 启用水平滚动条
            treeList.HorzScrollVisibility = DevExpress.XtraTreeList.ScrollVisibility.Auto;

            // 展开所有节点
            //treeList.ExpandAll();

            return treeList;
        }

        private Control CreateProgressContainer()
        {
            // 创建进度条容器面板
            PanelControl progressPanel = new PanelControl
            {
                Padding = new System.Windows.Forms.Padding(0),
                Height = 60,
                BorderStyle = BorderStyles.NoBorder // 隐藏边框
            };

            LabelControl label = new LabelControl
            {
                Text = "校准进度:",
                AutoSizeMode = LabelAutoSizeMode.None, // 设置为None以便自定义大小
                Size = new Size(80, 40),
                Location = new Point(0, (progressPanel.Height - 40) / 2), // 计算垂直居中位置
            };

            // 添加进度条
            ProgressBarControl progressBar = new ProgressBarControl
            {
                Size = new Size(2000, 40),
                Location = new Point(label.Left + 10, (progressPanel.Height - 40) / 2),
            };
            progressBar.Properties.ShowTitle = true;
            progressBar.Properties.PercentView = true;
            progressPanel.Controls.Add(label);
            progressPanel.Controls.Add(progressBar);

            return progressPanel;
        }

        public void UpdateDcNumber(List<EquipmentModel> equipmentList)
        {
            this.equipmentList = equipmentList;
            //清除所有旧布局
            this.Controls.Clear();
            InitializeUI();
            // 加载数据
            LoadData();
        }
    }
}