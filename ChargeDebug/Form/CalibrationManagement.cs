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

namespace ChargeDebug.Form
{
    public partial class CalibrationManagement : XtraUserControl
    {
        private string sqladdress = "";
        private TreeList treeList = new TreeList();

        public CalibrationManagement(string sqladdress)
        {
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
                using (var addForm = new AddEditCalibrationSignalForm(null, sqladdress))
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
                using (var editForm = new AddEditCalibrationSignalForm(signalId, sqladdress))
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

            ComboBoxEdit cbVoltageSource = new ComboBoxEdit
            {
                Size = new Size(120, 30),
                Location = new Point(lblVoltageSource.Right + 10, 12),
                Properties = { TextEditStyle = TextEditStyles.DisableTextEditor }
            };
            buttonPanel.Controls.Add(cbVoltageSource);

            // 电压表型号
            LabelControl lblVoltmeter = new LabelControl
            {
                Text = "电压表型号:",
                Location = new Point(cbVoltageSource.Right + 10, 14),
                AutoSize = true
            };
            buttonPanel.Controls.Add(lblVoltmeter);

            ComboBoxEdit cbVoltmeter = new ComboBoxEdit
            {
                Size = new Size(120, 30),
                Location = new Point(lblVoltmeter.Right + 10, 12),
                Properties ={ TextEditStyle = TextEditStyles.DisableTextEditor }
            };
            buttonPanel.Controls.Add(cbVoltmeter);

            // 电流表型号
            LabelControl lblAmmeter = new LabelControl
            {
                Text = "电流表型号:",
                Location = new Point(cbVoltmeter.Right + 10, 14)
            };
            buttonPanel.Controls.Add(lblAmmeter);

            ComboBoxEdit cbAmmeter = new ComboBoxEdit
            {
                Size = new Size(120, 30),
                Location = new Point(lblAmmeter.Right + 10, 12),
                Properties = { TextEditStyle = TextEditStyles.DisableTextEditor }
            };
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

            SimpleButton btnStartCalibration = new SimpleButton
            {
                Text = "开始校准",
                Size = new Size(80, 30),
                Location = new Point(btnDeleteSignal.Right + 10, 10)
            };
            buttonPanel.Controls.Add(btnStartCalibration);

            SimpleButton btnStopCalibration = new SimpleButton
            {
                Text = "停止校准",
                Size = new Size(80, 30),
                Location = new Point(btnStartCalibration.Right + 10, 10)
            };
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
    }
}