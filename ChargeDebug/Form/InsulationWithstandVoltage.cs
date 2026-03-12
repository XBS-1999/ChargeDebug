using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout;
using DevExpress.XtraLayout.Utils;
using DevExpress.XtraTreeList.Columns;
using System.Data;
using DataModel;
using CommunicationProtocols;
using ChargeDebug.Service;
using System.Data.SQLite;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid.ViewInfo;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class InsulationWithstandVoltage : XtraUserControl
    {
        private string sqladdress = "";
        private List<EquipmentModel> equipmentList;

        // 组合框控件
        private ComboBoxEdit cbVoltageSource;
        private ComboBoxEdit cbVoltmeter;

        private SimpleButton btnVoltageCalibration;
        private SimpleButton btnVoltageMeasurement;
        private SimpleButton btnCurrentMeasurement;
        private SimpleButton btnStopCalibration;
        private SimpleButton btnQueryData;
        private SimpleButton btnClearQuery;

        private GridControl gridControl;
        private GridView gridView;

        // 进度条控件
        private ProgressBarControl progressBar;
        private LabelControl progressLabel;

        private CancellationTokenSource _testCts;

        public InsulationWithstandVoltage(string sqladdress, List<EquipmentModel> equipmentList)
        {
            this.equipmentList = equipmentList;
            this.sqladdress = sqladdress;
            InitializeComponent();
            InitializeUI();
            LoadProjects();  // 加载数据库中的项目
        }

        /// <summary>
        /// 更新设备列表
        /// </summary>
        /// <param name="equipmentList">新的设备列表</param>
        public void UpdateDcNumber(List<EquipmentModel> equipmentList)
        {
            this.equipmentList = equipmentList;
            // 清除所有旧布局
            this.Controls.Clear();
            InitializeUI();
            LoadProjects();  // 加载数据库中的项目
        }

        private void LoadProjects()
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    conn.Open();
                    DataTable dt = SQLite_Service.GetAllTestProjects(conn);

                    // 添加测试结果相关列（如果不存在）
                    if (!dt.Columns.Contains("TestData"))
                        dt.Columns.Add("TestData", typeof(string));
                    if (!dt.Columns.Contains("TestResult"))
                        dt.Columns.Add("TestResult", typeof(string));

                    gridControl.DataSource = dt;
                    gridView.ExpandAllGroups(); // 如果有分组可展开，此处无分组
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"加载项目失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }   
        }

        /// <summary>
        /// 初始化用户界面
        /// </summary>
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

            LayoutControlItem gridItem = rootGroup.AddItem();
            gridItem.Control = CreateGridControl();
            gridItem.TextVisible = false;
            gridItem.SizeConstraintsType = SizeConstraintsType.Custom;
            gridItem.MinSize = new Size(0, 0);
            gridItem.MaxSize = new Size(0, 800);

            // 6. 添加TreeList到根组
            //LayoutControlItem layoutControlItemForTreeList = rootGroup.AddItem();
            //layoutControlItemForTreeList.Control = CreateTreeList();
            //layoutControlItemForTreeList.TextVisible = false;
            //layoutControlItemForTreeList.SizeConstraintsType = SizeConstraintsType.Custom;
            //layoutControlItemForTreeList.MinSize = new Size(0, 0);
            //layoutControlItemForTreeList.MaxSize = new Size(0, 800);

            // 7. 添加底部进度条面板到根组
            LayoutControlItem progressGroupItem = rootGroup.AddItem();
            progressGroupItem.Control = CreateProgressContainer();
            progressGroupItem.TextVisible = false;
            progressGroupItem.SizeConstraintsType = SizeConstraintsType.Custom;
            progressGroupItem.MinSize = new Size(0, 60);
            progressGroupItem.MaxSize = new Size(0, 60);
        }

        /// <summary>
        /// 创建按钮容器
        /// </summary>
        /// <returns>按钮容器控件</returns>
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
                Text = "绝缘耐压表型号:",
                Size = new Size(100, 30),
                Location = new Point(10, 15)
            };
            buttonPanel.Controls.Add(lblVoltageSource);

            cbVoltageSource = new ComboBoxEdit
            {
                Size = new Size(160, 30),
                Location = new Point(lblVoltageSource.Right + 10, 10),
                Properties = { TextEditStyle = TextEditStyles.DisableTextEditor }
            };

            // 从equipmentList中筛选电压源型号
            var voltageSources = equipmentList
                .Where(e => e.DeviceType == "绝缘耐压表")
                .Select(e => e.DeviceName)
                .ToList();
            cbVoltageSource.Properties.Items.AddRange(voltageSources);
            cbVoltageSource.SelectedIndex = 0;
            buttonPanel.Controls.Add(cbVoltageSource);

            // 电压表型号
            LabelControl lblVoltmeter = new LabelControl
            {
                Text = "等电位表型号:",
                Location = new Point(cbVoltageSource.Right + 10, 15),
                AutoSize = true
            };
            buttonPanel.Controls.Add(lblVoltmeter);

            cbVoltmeter = new ComboBoxEdit
            {
                Size = new Size(160, 30),
                Location = new Point(lblVoltmeter.Right + 10, 10),
                Properties = { TextEditStyle = TextEditStyles.DisableTextEditor }
            };

            // 从equipmentList中筛选电压表型号
            var voltmeters = equipmentList
                .Where(e => e.DeviceType == "等电位表")
                .Select(e => e.DeviceName)
                .ToList();
            cbVoltmeter.Properties.Items.AddRange(voltmeters);
            cbVoltmeter.SelectedIndex = 0;
            buttonPanel.Controls.Add(cbVoltmeter);

            // 添加按钮到面板
            SimpleButton btnAddSignal = new SimpleButton
            {
                Text = "添加项目",
                Size = new Size(80, 30),
                Location = new Point(cbVoltmeter.Right + 50, 10)
            };
            btnAddSignal.Click += BtnAddSignal_Click;
            buttonPanel.Controls.Add(btnAddSignal);

            SimpleButton btnEditSignal = new SimpleButton
            {
                Text = "编辑项目",
                Size = new Size(80, 30),
                Location = new Point(btnAddSignal.Right + 10, btnAddSignal.Location.Y)
            };
            btnEditSignal.Click += BtnEditSignal_Click;
            buttonPanel.Controls.Add(btnEditSignal);

            SimpleButton btnDeleteSignal = new SimpleButton
            {
                Text = "删除项目",
                Size = new Size(80, 30),
                Location = new Point(btnEditSignal.Right + 10, btnEditSignal.Location.Y)
            };
            btnDeleteSignal.Click += BtnDeleteSignal_Click;
            buttonPanel.Controls.Add(btnDeleteSignal);

            btnVoltageCalibration = new SimpleButton
            {
                Text = "开始测试",
                Size = new Size(80, 30),
                Location = new Point(btnDeleteSignal.Right + 10, 10)
            };
            btnVoltageCalibration.Click += Start_Test_Click;
            buttonPanel.Controls.Add(btnVoltageCalibration);

            //btnVoltageMeasurement = new SimpleButton
            //{
            //    Text = "电压计量",
            //    Size = new Size(80, 30),
            //    Location = new Point(btnCurrentCalibration.Right + 10, 10)
            //};
            //btnVoltageMeasurement.Click += BtnVoltageMeasurement_Click;
            //buttonPanel.Controls.Add(btnVoltageMeasurement);

            //btnCurrentMeasurement = new SimpleButton
            //{
            //    Text = "电流计量",
            //    Size = new Size(80, 30),
            //    Location = new Point(btnVoltageMeasurement.Right + 10, 10)
            //};
            //btnCurrentMeasurement.Click += BtnCurrentMeasurement_Click;
            //buttonPanel.Controls.Add(btnCurrentMeasurement);

            btnStopCalibration = new SimpleButton
            {
                Text = "停止测试",
                Size = new Size(80, 30),
                Location = new Point(btnVoltageCalibration.Right + 10, 10)
            };
            btnStopCalibration.Click += BtnStopCalibration_Click;
            buttonPanel.Controls.Add(btnStopCalibration);

            SimpleButton btnImportData = new SimpleButton
            {
                Text = "导入数据",
                Size = new Size(80, 30),
                Location = new Point(btnStopCalibration.Right + 10, 10)
            };
            //btnImportData.Click += BtnImportData_Click;
            buttonPanel.Controls.Add(btnImportData);

            SimpleButton btnExportData = new SimpleButton
            {
                Text = "导出数据",
                Size = new Size(80, 30),
                Location = new Point(btnImportData.Right + 10, 10)
            };
            //btnExportData.Click += BtnExportData_Click;
            buttonPanel.Controls.Add(btnExportData);

            // 新增查询数据按钮
            btnQueryData = new SimpleButton
            {
                Text = "查询数据",
                Size = new Size(80, 30),
                Location = new Point(btnExportData.Right + 10, 10)
            };
            //btnQueryData.Click += BtnQueryData_Click;
            buttonPanel.Controls.Add(btnQueryData);

            btnClearQuery = new SimpleButton
            {
                Text = "清空查询",
                Size = new Size(80, 30),
                Location = new Point(btnQueryData.Right + 10, 10)
            };
            //btnClearQuery.Click += BtnClearQuery_Click;
            buttonPanel.Controls.Add(btnClearQuery);

            return buttonPanel;
        }

        private void BtnStopCalibration_Click(object? sender, EventArgs e)
        {
            if (_testCts != null && !_testCts.IsCancellationRequested)
            {
                _testCts.Cancel();   // 取消任务令牌
                progressLabel.Text = "正在停止测试...";
            }
        }

        private void BtnDeleteSignal_Click(object? sender, EventArgs e)
        {
            int[] selectedRows = gridView.GetSelectedRows();
            if (selectedRows.Length == 0)
            {
                XtraMessageBox.Show("请勾选要删除的项目！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult result = XtraMessageBox.Show($"确定要删除选中的 {selectedRows.Length} 个项目吗？", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            List<int> idsToDelete = new List<int>();
            foreach (int handle in selectedRows)
            {
                DataRowView rowView = gridView.GetRow(handle) as DataRowView;
                if (rowView != null)
                {
                    idsToDelete.Add(Convert.ToInt32(rowView["Id"]));
                }
            }

            try
            {
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    conn.Open();
                    using (var trans = conn.BeginTransaction())
                    {
                        SQLite_Service.DeleteTestProjects(conn, idsToDelete, trans);
                        trans.Commit();
                    }
                }
                LoadProjects();
                XtraMessageBox.Show($"成功删除 {idsToDelete.Count} 个项目。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"删除项目失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnEditSignal_Click(object? sender, EventArgs e)
        {
            int focusedHandle = gridView.FocusedRowHandle;
            if (focusedHandle < 0)
            {
                XtraMessageBox.Show("请先选中要编辑的项目！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DataRowView rowView = gridView.GetRow(focusedHandle) as DataRowView;
            if (rowView == null) return;

            int id = Convert.ToInt32(rowView["Id"]);

            TestProjectModel project;
            using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
            {
                conn.Open();
                project = SQLite_Service.GetTestProjectById(conn, id);
            }

            if (project == null)
            {
                XtraMessageBox.Show("未找到对应项目！", "错误");
                return;
            }

            using (var dlg = new ProjectEditDialog(project))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                        {
                            conn.Open();
                            SQLite_Service.UpdateTestProject(conn, dlg.ProjectData);
                        }
                        LoadProjects(); // 刷新
                    }
                    catch (Exception ex)
                    {
                        XtraMessageBox.Show($"更新项目失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void BtnAddSignal_Click(object? sender, EventArgs e)
        {
            try
            {
                // 获取当前最大项目号
                int maxNumber = 0;
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    conn.Open();
                    string query = "SELECT MAX(ProjectId) FROM TestProject";

                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        object result = cmd.ExecuteScalar();
                        if (result != DBNull.Value) maxNumber = Convert.ToInt32(result);
                    }
                }

                int newDeviceNumber = maxNumber + 1;

                using (var dlg = new ProjectEditDialog(newDeviceNumber))
                {
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                        {
                            conn.Open();
                            SQLite_Service.AddTestProject(conn, dlg.ProjectData);
                        }
                        LoadProjects(); // 刷新列表
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"添加项目失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void Start_Test_Click(object? sender, EventArgs e)
        {
            // 1. 获取用户选择的设备型号（代码不变）
            string selectedVoltageSource = cbVoltageSource.SelectedItem?.ToString();
            string selectedVoltmeter = cbVoltmeter.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedVoltageSource) || string.IsNullOrEmpty(selectedVoltmeter))
            {
                XtraMessageBox.Show("请选择绝缘耐压表和等电位表型号！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            EquipmentModel voltageDevice = equipmentList.FirstOrDefault(d => d.DeviceName == selectedVoltageSource);
            EquipmentModel voltmeterDevice = equipmentList.FirstOrDefault(d => d.DeviceName == selectedVoltmeter);
            if (voltageDevice == null || voltmeterDevice == null)
            {
                XtraMessageBox.Show("未找到所选表的设备信息！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 2. 获取勾选的项目
            int[] selectedRows = gridView.GetSelectedRows();
            if (selectedRows.Length == 0)
            {
                XtraMessageBox.Show("请至少勾选一个测试项目！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 3. 输入设备编号
            string deviceNumber;
            using (var inputForm = new DeviceNumberInputForm())
            {
                if (inputForm.ShowDialog() != DialogResult.OK)
                    return;
                deviceNumber = inputForm.DeviceNumber;
            }

            // 4. 初始化进度条：最大值 = 项目数 * 100（每个项目100%）
            btnVoltageCalibration.Enabled = false;
            progressBar.Properties.Maximum = selectedRows.Length * 100;
            progressBar.Position = 0;
            progressLabel.Text = "准备开始测试...";

            _testCts = new CancellationTokenSource();

            // 5. 遍历每个项目
            try
            {
                int completedProjects = 0;

                foreach (int handle in selectedRows)
                {
                    if (_testCts.Token.IsCancellationRequested)
                    {
                        progressLabel.Text = "用户已停止测试";
                        break;
                    }

                    DataRowView row = gridView.GetRow(handle) as DataRowView;
                    if (row == null) continue;

                    // 提取项目参数
                    string projectName = row["ProjectName"].ToString();
                    double testVoltage = Convert.ToDouble(row["TestVoltage"]);
                    double testTime = Convert.ToDouble(row["TestTime"]);
                    double rampUp = 0, rampDown = 0;
                    double limitHigh = 0, limitLow = 0;
                    if (projectName == "AC耐压测试" || projectName == "DC耐压测试")
                    {
                        rampUp = Convert.ToDouble(row["RampUpTime"]);
                        rampDown = Convert.ToDouble(row["RampDownTime"]);
                        string[] current = row["CurrentLimit"].ToString().Split('-');
                        limitHigh = Convert.ToDouble(current[1]);
                        limitLow = Convert.ToDouble(current[0]);
                    }
                    else if (projectName == "绝缘电阻测试")
                    {
                        rampUp = Convert.ToDouble(row["RampUpTime"]);
                        rampDown = Convert.ToDouble(row["RampDownTime"]);
                        string[] resistance = row["ResistanceLimit"].ToString().Split('-');
                        limitHigh = Convert.ToDouble(resistance[1]);
                        limitLow = Convert.ToDouble(resistance[0]);
                    }
                    else if(projectName == "等电位测试")
                    {
                        string[] resistance = row["ResistanceLimit"].ToString().Split('-');
                        limitHigh = Convert.ToDouble(resistance[1]) / 1000.0;
                        limitLow = Convert.ToDouble(resistance[0]) / 1000.0;
                    }

                    // 计算预计总时间（秒）
                    double totalExpectedSeconds = rampUp + testTime + rampDown + 1;

                    if (projectName == "等电位测试")
                    {
                        totalExpectedSeconds += 7;
                    }

                    // 更新标签
                    progressLabel.Text = $"正在测试：{projectName}";

                    // 进度条计时器（同原有代码）
                    var progressTimer = new System.Windows.Forms.Timer { Interval = 100 };
                    DateTime startTime = DateTime.Now;
                    bool testCompleted = false;
                    progressTimer.Tick += (s, args) =>
                    {
                        if (testCompleted) return;
                        double elapsed = (DateTime.Now - startTime).TotalSeconds;
                        double ratio = Math.Min(elapsed / (totalExpectedSeconds), 1.0);
                        int pos = (int)(completedProjects * 100 + ratio * 100);
                        pos = Math.Min(pos, progressBar.Properties.Maximum);
                        progressBar.Invoke(() => progressBar.Position = pos);
                    };

                    // 使用 Task.Run 在后台执行测试
                    var testTask = Task.Run<object>(() =>
                    {
                        if (projectName == "AC耐压测试")
                        {
                            var comm = Chroma19073_RS232Communicator.Instance;
                            try
                            {
                                if (!comm.Connect(voltageDevice.ComPort,
                                                  baudRate: int.Parse(voltageDevice.BaudRate),
                                                  dataBits: int.Parse(voltageDevice.DataBits),
                                                  stopBits: voltageDevice.StopBits,
                                                  parity: voltageDevice.Parity,
                                                  flowControl: voltageDevice.FlowControl))
                                {
                                    throw new Exception("连接绝缘耐压表失败");
                                }

                                // 连接成功后，可执行一些预设配置（根据实际需要）
                                // 例如：设置系统参数（蜂鸣器时间、屏幕显示等）
                                // comm.SetSystemSetting(...); 

                                // 执行测试
                                return comm.PerformACWithstandTest(testVoltage, limitHigh, limitLow,
                                                                   rampUp, testTime, rampDown, 0, 1, _testCts.Token);
                            }
                            finally
                            {
                                if (comm.IsConnected)
                                    comm.Disconnect();
                            }
                        } 
                        else if (projectName == "DC耐压测试")
                        {
                            var comm = Chroma19073_RS232Communicator.Instance;
                            try
                            {
                                if (!comm.Connect(voltageDevice.ComPort,
                                                  baudRate: int.Parse(voltageDevice.BaudRate),
                                                  dataBits: int.Parse(voltageDevice.DataBits),
                                                  stopBits: voltageDevice.StopBits,
                                                  parity: voltageDevice.Parity,
                                                  flowControl: voltageDevice.FlowControl))
                                {
                                    throw new Exception("连接绝缘耐压表失败");
                                }

                                // 连接成功后，可执行一些预设配置

                                return comm.PerformDCWithstandTest(testVoltage, limitHigh, limitLow,
                                                                   rampUp, testTime, rampDown, 0, false, 1, _testCts.Token);
                            }
                            finally
                            {
                                if (comm.IsConnected)
                                    comm.Disconnect();
                            }
                        }
                        else if (projectName == "绝缘电阻测试")
                        {
                            var comm = Chroma19073_RS232Communicator.Instance;
                            try
                            {
                                if (!comm.Connect(voltageDevice.ComPort,
                                                  baudRate: int.Parse(voltageDevice.BaudRate),
                                                  dataBits: int.Parse(voltageDevice.DataBits),
                                                  stopBits: voltageDevice.StopBits,
                                                  parity: voltageDevice.Parity,
                                                  flowControl: voltageDevice.FlowControl))
                                {
                                    throw new Exception("连接绝缘耐压表失败");
                                }

                                // 执行绝缘电阻测试
                                return comm.PerformInsulationTest(testVoltage, limitHigh, limitLow,
                                                                  rampUp, testTime, rampDown, 1, _testCts.Token);
                            }
                            finally
                            {
                                if (comm.IsConnected)
                                    comm.Disconnect();
                            }
                        }
                        else if (projectName == "等电位测试")
                        {
                            var comm = Chroma19572_RS232Communicator.Instance;
                            try
                            {
                                if (!comm.Connect(voltmeterDevice.ComPort,
                                                  baudRate: int.Parse(voltmeterDevice.BaudRate),
                                                  dataBits: int.Parse(voltmeterDevice.DataBits),
                                                  stopBits: voltmeterDevice.StopBits,
                                                  parity: voltmeterDevice.Parity,
                                                  flowControl: voltmeterDevice.FlowControl))
                                {
                                    throw new Exception("连接等电位表失败");
                                }

                                // 连接成功后发送其他指令（示例）
                                //comm.SetGBFrequency(50.0);
                                //comm.SetGBVoltage(6.0);
                                //comm.SetSoftwareAGC(true);
                                //comm.SetFailContinuity(false);
                                return comm.PerformSingleStepTest(1, testVoltage, limitHigh, limitLow,
                                                                  testTime, _testCts.Token);
                            }
                            finally
                            {
                                if (comm.IsConnected)
                                    comm.Disconnect();
                            }
                        }
                        else
                        {
                            throw new NotSupportedException($"未知项目类型: {projectName}");
                        }
                    }, _testCts.Token);

                    progressTimer.Start();

                    try
                    {
                        object result = await testTask;

                        if (result is Chroma19073Result r19073)
                        {
                            row["TestData"] = (r19073.Mode == "IR") ? $"{r19073.InsulationResistance} GΩ"
                                                                    : $"{r19073.ActualCurrent} mA";
                            row["TestResult"] = r19073.Passed ? "通过" : $"失败：{r19073.FailReason}";
                        }
                        else if (result is Chroma19572StepResult r19572)
                        {
                            row["TestData"] = $"{r19572.MeasureMeter:F3} Ω";
                            row["TestResult"] = r19572.Passed ? "通过" : $"失败：{r19572.FailReason}";
                        }
                        else
                        {
                            row["TestData"] = DBNull.Value;
                            row["TestResult"] = $"未知结果类型: {result?.GetType()}";
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        row["TestResult"] = "用户取消";
                        row["TestData"] = DBNull.Value;
                        throw;   // 重新抛出，让外层捕获并终止循环
                    }
                    catch (Exception ex)
                    {
                        row["TestResult"] = $"失败：{ex.Message}";
                        row["TestData"] = DBNull.Value;
                    }
                    finally
                    {
                        progressTimer.Stop();
                        progressTimer.Dispose();
                        testCompleted = true;
                    }

                    gridView.RefreshRow(handle);
                    completedProjects++;
                    progressBar.Position = completedProjects * 100;
                }
            }
            catch (OperationCanceledException)
            {
                progressLabel.Text = "测试已被取消";
                return;   // 直接返回，不弹出完成框
            }
            finally
            {
                _testCts?.Dispose();
                _testCts = null;
                btnVoltageCalibration.Enabled = true;
                btnStopCalibration.Enabled = false;
            }

            // 6. 恢复按钮状态
            progressLabel.Text = "测试完成";
            XtraMessageBox.Show("所有项目测试完成！", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// 创建树形列表
        /// </summary>
        /// <returns>树形列表控件</returns>
        private Control CreateGridControl()
        {
            gridControl = new GridControl();
            gridView = new GridView();

            gridControl.MainView = gridView;
            gridControl.ViewCollection.Add(gridView);

            // 基本设置
            gridView.OptionsBehavior.Editable = false;
            gridView.OptionsSelection.MultiSelect = true;
            gridView.OptionsSelection.MultiSelectMode = GridMultiSelectMode.CheckBoxRowSelect;
            gridView.OptionsSelection.CheckBoxSelectorColumnWidth = 30; // 复选框列宽度
            gridView.OptionsView.ShowGroupPanel = false; // 不显示分组面板
            gridView.FocusRectStyle = DrawFocusRectStyle.RowFocus;
            gridView.OptionsView.ShowVerticalLines = DefaultBoolean.True;
            gridView.OptionsView.ShowHorizontalLines = DefaultBoolean.True;
            gridView.OptionsView.EnableAppearanceEvenRow = true;
            gridView.OptionsView.EnableAppearanceOddRow = true;
            gridView.Appearance.EvenRow.BackColor = ColorTranslator.FromHtml("#F9F9F9");
            gridView.Appearance.OddRow.BackColor = Color.White;
            gridView.Appearance.Row.BackColor2 = Color.White;
            gridView.Appearance.HeaderPanel.BackColor = ColorTranslator.FromHtml("#F0F5FF");
            gridView.Appearance.HeaderPanel.Options.UseBackColor = true;
            gridView.Appearance.HeaderPanel.Options.UseFont = true;
            gridView.Appearance.HeaderPanel.TextOptions.HAlignment = HorzAlignment.Center;
            gridView.Appearance.Row.TextOptions.HAlignment = HorzAlignment.Center;
            gridView.OptionsSelection.EnableAppearanceFocusedCell = false;

            // 定义列
            AddGridColumn("序号", "ProjectId", 50);
            AddGridColumn("项目名称", "ProjectName", 150);
            AddGridColumn("测试电压(V)", "TestVoltage", 100);
            AddGridColumn("测试时间(s)", "TestTime", 100);
            AddGridColumn("电压上升时间(s)", "RampUpTime", 120);
            AddGridColumn("电压下降时间(s)", "RampDownTime", 120);
            AddGridColumn("电流上下限范围(mA)", "CurrentLimit", 150);
            AddGridColumn("电阻上下限范围(MΩ/mΩ)", "ResistanceLimit", 150);
            AddGridColumn("测试数据", "TestData", 150);
            AddGridColumn("测试结果", "TestResult", 150);

            // 设置所有列内容居中
            foreach (GridColumn col in gridView.Columns)
            {
                col.AppearanceCell.TextOptions.HAlignment = HorzAlignment.Center;
                col.AppearanceHeader.TextOptions.HAlignment = HorzAlignment.Center;
            }

            return gridControl;
        }

        /// <summary>
        /// 辅助方法：添加网格列
        /// </summary>
        private void AddGridColumn(string caption, string fieldName, int width)
        {
            GridColumn column = gridView.Columns.AddVisible(fieldName);
            column.Caption = caption;
            column.Width = width;
            column.OptionsColumn.AllowEdit = false;
        }

        /// <summary>
        /// 创建进度条容器
        /// </summary>
        /// <returns>进度条容器控件</returns>
        private Control CreateProgressContainer()
        {
            // 创建进度条容器面板
            PanelControl progressPanel = new PanelControl
            {
                Padding = new System.Windows.Forms.Padding(0),
                Height = 60,
                BorderStyle = BorderStyles.NoBorder // 隐藏边框
            };

            // 添加进度标签
            progressLabel = new LabelControl
            {
                Text = "准备开始测试...",
                AutoSizeMode = LabelAutoSizeMode.None,
                Size = new Size(180, 40),
                Location = new Point(0, (progressPanel.Height - 40) / 2), // 计算垂直居中位置
                //Appearance = { TextOptions = { HAlignment = HorzAlignment.Near } }
            };
            progressPanel.Controls.Add(progressLabel);

            // 添加进度条
            progressBar = new ProgressBarControl
            {
                Size = new Size(1670, 40),
                Location = new Point(progressLabel.Right + 20, (progressPanel.Height - 40) / 2),
                Properties = {
                    ShowTitle = true,
                    PercentView = true,
                    Minimum = 0,
                    Maximum = 100
                }
            };
            progressPanel.Controls.Add(progressBar);

            return progressPanel;
        }
    }
}
