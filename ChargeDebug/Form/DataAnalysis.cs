using DevExpress.XtraEditors;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid;
using DevExpress.Utils;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraCharts;
using DevExpress.XtraGrid.Views.Grid.ViewInfo;
using System.Data.SQLite;
using ChargeDebug.Service;
using System.Data;
using System.IO;
using Log;
using DataModel;
using DevExpress.XtraTab;
using DevExpress.XtraEditors.Controls;
using DevExpress.LookAndFeel;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Windows.Forms;
using System.Threading;
using DevExpress.DataProcessing;
using DevExpress.Office.Utils;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class DataAnalysis : XtraUserControl
    {
        private bool _disposed = false;
        private string sqladdress = "";
        private CsvFileManager csvfilemanager;
        private DataBatchWriteManager _dataWriteManager;

        private GridControl fileGridControl;
        private GridView fileGridView;
        private WaitDialogForm waitDialog;

        // 右侧TabControl和TabPage
        private XtraTabControl tabControl;
        private XtraTabPage tabPageTable;
        private XtraTabPage tabPageChart;

        // 表格数据Tab
        private GridControl dataGridControl;
        private GridView dataGridView;

        // 图表Tab
        private ChartControl chartControl;

        private ContextMenuStrip gridContextMenu;
        private ToolStripMenuItem importfiles;
        private ToolStripMenuItem exportfile;
        private ToolStripMenuItem deletefile;

        //private int _currentFileId = -1;

        private const int MAX_POINTS = 50; // 图表最大数据点数

        private ConcurrentDictionary<string, Series> keyValuePairs = new();
        List<Template> template = new List<Template>();

        public DataAnalysis(string sqlPath)
        {
            sqladdress = sqlPath;
            csvfilemanager = new CsvFileManager();

            // 初始化数据写入管理器
            _dataWriteManager = new DataBatchWriteManager($"Data Source={sqladdress};Version=3;", 1000);
            _dataWriteManager.BatchWritten += OnBatchWritten;
            _dataWriteManager.WriteCompleted += OnWriteCompleted;
            _dataWriteManager.WriteError += OnWriteError;

            InitializeComponent();
            InitializeUI();
            this.Load += Agreement_Load;
        }

        private void Agreement_Load(object? sender, EventArgs e)
        {
            // 加载Template配置
            LoadTemplateConfiguration();
            ConfigureGridSelection();
            LoadFileList();
        }

        /// <summary>
        /// 从数据库加载Template配置
        /// </summary>
        private void LoadTemplateConfiguration()
        {
            using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
            {
                conn.Open();

                template = SQLite_Service.GetTemplate(conn);

                conn.Close();
            }
        }

        private void ConfigureGridSelection()
        {
            fileGridView.FocusedRowChanged += (s, e) =>
            {
                if (e.FocusedRowHandle >= 0)
                {
                    fileGridControl.BeginInvoke(new Action(() =>
                    {
                        LoadTreeDataForSelectedRow(e.FocusedRowHandle);
                    }));
                }
            };

            /* 右键菜单处理 */
            fileGridView.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    // 获取点击位置信息
                    GridHitInfo hitInfo = fileGridView.CalcHitInfo(e.Location);

                    // 无论是否在行上都显示菜单
                    gridContextMenu.Show(fileGridControl, e.Location);

                    // 如果在行上点击，设置焦点行
                    if (hitInfo.InRow)
                    {
                        fileGridView.Focus();
                        fileGridView.FocusedRowHandle = hitInfo.RowHandle;
                    }
                    else
                    {
                        // 清空选择
                        fileGridView.ClearSelection();
                    }
                }
            };

            gridContextMenu.Opening += (s, e) =>
            {
                bool hasSelection = fileGridView.SelectedRowsCount > 0;

                importfiles.Enabled = true; // 总是允许导入
                exportfile.Enabled = hasSelection;
                deletefile.Enabled = hasSelection;
            };
        }

        /// <summary>
        /// 加载并显示文件数据
        /// </summary>
        private async void LoadTreeDataForSelectedRow(int rowHandle)
        {
            try
            {
                if (rowHandle < 0) return;

                // 获取文件ID
                int _currentFileId = Convert.ToInt32(fileGridView.GetRowCellValue(rowHandle, "文件ID"));
                string fileName = fileGridView.GetRowCellValue(rowHandle, "文件名称").ToString();

                CloseWaitDialog();

                try
                {
                    // 在后台线程中显示等待对话框
                    await Task.Run(async () =>
                    {
                        if (this.InvokeRequired)
                        {
                            this.Invoke(new Action(() =>
                            {
                                // 创建并显示等待对话框
                                waitDialog = new WaitDialogForm("正在加载数据", $"正在加载文件 {fileName} 的数据...");
                                waitDialog.Show();
                                waitDialog.TopMost = true;

                                // 强制UI更新
                                Application.DoEvents();
                            }));
                        }
                        else
                        {
                            waitDialog = new WaitDialogForm("正在加载数据", $"正在加载文件 {fileName} 的数据...");
                            waitDialog.Show();
                            waitDialog.TopMost = true;
                            Application.DoEvents();
                        }

                        // 等待一段时间确保对话框显示
                        await Task.Delay(100);
                    });

                    // 检查目标表数据
                    UpdateWaitDialog($"检查目标表数据...");

                    // 首先检查TargetTable中是否已有数据
                    bool hasData = await Task.Run(() =>
                        _dataWriteManager.CheckTargetTableHasDataAsync(_currentFileId));

                    if (!hasData)
                    {
                        UpdateWaitDialog($"正在写入数据到目标表...");

                        // 如果没有数据，则写入数据
                        await Task.Run(() =>
                            _dataWriteManager.WriteToTargetTableAsync(_currentFileId));

                        UpdateWaitDialog($"数据写入完成，正在加载表格...");
                    }
                    else
                    {
                        UpdateWaitDialog($"从现有数据加载表格...");
                    }

                    // 加载表格数据
                    UpdateWaitDialog($"加载表格数据...");
                    await Task.Run(() => LoadTableData(_currentFileId));

                    UpdateWaitDialog($"表格数据加载完成，正在加载图表...");

                    // 加载图表数据
                    await Task.Run(() => LoadChartData(_currentFileId));

                    UpdateWaitDialog($"图表数据加载完成");

                    // 短暂延迟确保用户能看到完成状态
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    XtraMessageBox.Show($"加载数据失败: {ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    // 确保关闭等待对话框
                    CloseWaitDialog();
                }
            }
            catch (Exception ex)
            {
                waitDialog.Close();
                XtraMessageBox.Show($"加载数据失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 更新等待对话框的描述文本
        /// </summary>
        private void UpdateWaitDialog(string description)
        {
            if (waitDialog != null && !waitDialog.IsDisposed && waitDialog.Visible)
            {
                if (waitDialog.InvokeRequired)
                {
                    waitDialog.BeginInvoke(new Action(() =>
                    {
                        if (!waitDialog.IsDisposed)
                        {
                            waitDialog.AccessibleDescription = description;
                            // 强制UI更新
                            Application.DoEvents();
                        }
                    }));
                }
                else
                {
                    waitDialog.AccessibleDescription = description;
                    Application.DoEvents();
                }

                // 短暂延迟，允许UI响应
                Thread.Sleep(50);
            }
        }

        /// <summary>
        /// 加载表格数据
        /// </summary>
        private void LoadTableData(int fileId)
        {
            try
            {
                DataTable dataTable = new DataTable();

                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    conn.Open();

                    string sql = @"
                       SELECT * FROM TargetTable 
                       WHERE FileID = @FileID 
                       ORDER BY Time";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@FileID", fileId);

                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(dataTable);
                        }
                    }
                }

                // 在UI线程上更新数据源
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() =>
                    {
                        dataGridControl.DataSource = dataTable;
                        dataGridView.Columns.Clear();

                        if (dataTable.Rows.Count == 0)
                        {
                            // 切换到表格Tab页显示提示
                            tabControl.SelectedTabPage = tabPageTable;
                        }
                        else
                        {
                            // 配置列
                            ConfigureDataGridViewColumns();
                            // 自动调整列宽
                            dataGridView.BestFitColumns();
                        }
                    }));
                }
                else
                {
                    dataGridControl.DataSource = dataTable;
                    dataGridView.Columns.Clear();

                    if (dataTable.Rows.Count == 0)
                    {
                        // 切换到表格Tab页显示提示
                        tabControl.SelectedTabPage = tabPageTable;
                    }
                    else
                    {
                        // 配置列
                        ConfigureDataGridViewColumns();
                        // 自动调整列宽
                        dataGridView.BestFitColumns();
                    }
                }
            }
            catch (Exception ex)
            {
                throw; // 重新抛出异常让上层处理
            }
        }

        /// <summary>
        /// 配置表格列
        /// </summary>
        private void ConfigureDataGridViewColumns()
        {
            foreach (var templates in template)
            {
                if (templates.Type_Table == "true")
                {
                    GridColumn channelColumn = new GridColumn();
                    channelColumn.FieldName = templates.Name;
                    channelColumn.Caption = $"{templates.ChineseName}({templates.Unit})";
                    channelColumn.Visible = true;
                    if (templates.Name == "Time")
                    {
                        channelColumn.DisplayFormat.FormatString = "yyyy/MM/dd HH:mm:ss";
                        channelColumn.DisplayFormat.FormatType = FormatType.DateTime;
                    }
                    else
                    {
                        channelColumn.DisplayFormat.FormatString = "F1";
                        channelColumn.DisplayFormat.FormatType = FormatType.Numeric;
                    }
                    
                    // 设置内容居中
                    channelColumn.AppearanceCell.TextOptions.HAlignment = HorzAlignment.Center;
                    channelColumn.AppearanceHeader.TextOptions.HAlignment = HorzAlignment.Center;
                    dataGridView.Columns.Add(channelColumn);
                }
            }

            // 配置网格外观
            dataGridView.OptionsView.ShowGroupPanel = false;
            dataGridView.OptionsView.ShowAutoFilterRow = true;
            dataGridView.OptionsView.ShowFooter = true;
            dataGridView.OptionsBehavior.Editable = false;

            // 设置行高
            dataGridView.RowHeight = 26;

            // 设置偶数行背景色
            dataGridView.OptionsView.EnableAppearanceEvenRow = true;
            dataGridView.Appearance.EvenRow.BackColor = Color.FromArgb(245, 245, 245);

            dataGridView.Appearance.Row.TextOptions.HAlignment = HorzAlignment.Center;
        }

        /// <summary>
        /// 加载图表数据
        /// </summary>
        private void LoadChartData(int fileId)
        {
            try
            {
                DataTable dataTable = new DataTable();

                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    conn.Open();

                    string sql = @"
                       SELECT * FROM TargetTable 
                       WHERE FileID = @FileID 
                       ORDER BY Time";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@FileID", fileId);

                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(dataTable);
                        }
                    }
                }

                // 在UI线程上更新图表
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() =>
                    {
                        UpdateChartWithData(dataTable);
                    }));
                }
                else
                {
                    UpdateChartWithData(dataTable);
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"图表加载错误: {ex.Message}");
                throw; // 重新抛出异常让上层处理
            }
        }

        // 提取图表更新逻辑到单独方法
        private void UpdateChartWithData(DataTable dataTable)
        {
            // 清除现有系列
            chartControl.Series.Clear();

            if (dataTable.Rows.Count == 0)
            {
                return;
            }

            // 确保Time列是DateTime类型
            if (dataTable.Columns["Time"] != null && dataTable.Columns["Time"].DataType != typeof(DateTime))
            {
                // 尝试转换Time列为DateTime类型
                DataTable newTable = dataTable.Clone();
                newTable.Columns["Time"].DataType = typeof(DateTime);

                foreach (DataRow row in dataTable.Rows)
                {
                    DataRow newRow = newTable.NewRow();
                    foreach (DataColumn col in dataTable.Columns)
                    {
                        if (col.ColumnName == "Time")
                        {
                            if (DateTime.TryParse(row[col].ToString(), out DateTime dt))
                            {
                                newRow[col] = dt;
                            }
                            else
                            {
                                newRow[col] = DBNull.Value;
                            }
                        }
                        else
                        {
                            newRow[col] = row[col];
                        }
                    }
                    newTable.Rows.Add(newRow);
                }
                dataTable = newTable;
            }

            foreach (var templates in template)
            {
                if (templates.Type_Chart == "true")
                {
                    if (templates.Name != "Time")
                    {
                        Series series = new Series(templates.ChineseName, ViewType.Line);
                        series.ArgumentScaleType = ScaleType.DateTime;
                        series.ArgumentDataMember = "Time";
                        series.ValueDataMembers.AddRange(new string[] { templates.Name });
                        chartControl.Series.Add(series);
                    }
                }
            }

            // 设置数据源
            chartControl.DataSource = dataTable;

            // 配置图表显示格式
            ConfigureChartDisplay();

            // 强制重新计算X轴范围
            XYDiagram diagram = (XYDiagram)chartControl.Diagram;
            if (diagram != null)
            {
                diagram.AxisX.WholeRange.Auto = true;
                diagram.AxisX.VisualRange.Auto = true;
            }
        }

        /// <summary>
        /// 配置图表显示格式
        /// </summary>
        private void ConfigureChartDisplay()
        {
            XYDiagram diagram = (XYDiagram)chartControl.Diagram;

            if (diagram != null)
            {
                // 配置X轴为日期时间轴
                // 移除 MeasureUnit 设置，使用自动配置
                diagram.AxisX.DateTimeScaleOptions.ScaleMode = ScaleMode.Automatic;

                diagram.AxisX.Title.Text = "时间";
                diagram.AxisX.Title.Visible = true;
                diagram.AxisX.Title.Font = new Font("Microsoft YaHei", 9, FontStyle.Bold);
                diagram.AxisX.WholeRange.AlwaysShowZeroLevel = false;

                // 设置时间显示格式为完整的日期时间格式
                diagram.AxisX.Label.TextPattern = "{A:HH:mm:ss}";
                // 解决标签重叠问题 - 主要修改点
                diagram.AxisX.Label.Angle = 45; // 设置标签倾斜45度，避免重叠
                diagram.AxisX.Label.ResolveOverlappingOptions.AllowRotate = true;
                diagram.AxisX.Label.ResolveOverlappingOptions.AllowStagger = true; // 启用交错排列
                diagram.AxisX.Label.ResolveOverlappingOptions.AllowHide = false; // 不要隐藏标签

                // 根据数据量调整标签显示间隔
                double labelCount = chartControl.Series[0]?.Points?.Count ?? 0;
                if (labelCount > 50)
                {
                    // 数据点多时，减少标签显示密度
                    diagram.AxisX.Label.ResolveOverlappingOptions.MinIndent = 30;

                    // 设置网格间隔，自动减少标签数量
                    diagram.AxisX.DateTimeScaleOptions.GridAlignment = DateTimeGridAlignment.Minute;
                    diagram.AxisX.DateTimeScaleOptions.GridSpacing = 5; // 每5分钟显示一个标签
                }
                else
                {
                    diagram.AxisX.Label.ResolveOverlappingOptions.MinIndent = 5;
                }

                // 配置X轴网格线
                diagram.AxisX.GridLines.Visible = true;
                diagram.AxisX.GridLines.Color = Color.LightGray;
                diagram.AxisX.GridLines.LineStyle.DashStyle = DashStyle.Dash;

                // 调整X轴范围，去掉边距
                diagram.AxisX.WholeRange.SideMarginsValue = 0;
                diagram.AxisX.VisualRange.SideMarginsValue = 0;

                // 配置Y轴
                diagram.AxisY.Title.Text = "功率(KW)/SOC(%)";
                diagram.AxisY.Title.Visible = true;
                diagram.AxisY.Title.Font = new Font("Microsoft YaHei", 9, FontStyle.Bold);
                diagram.AxisY.WholeRange.AlwaysShowZeroLevel = false;
                diagram.AxisY.Label.TextPattern = "{F1}";

                // 调整Y轴范围，去掉边距
                diagram.AxisY.WholeRange.SideMarginsValue = 0;
                diagram.AxisY.VisualRange.SideMarginsValue = 0;

                // 启用滚动和缩放
                diagram.ScrollingOptions.UseMouse = true;
                diagram.ScrollingOptions.UseKeyboard = true;
                diagram.ZoomingOptions.UseMouseWheel = true;
                diagram.ZoomingOptions.UseKeyboard = true;
            }

            chartControl.Legend.Visibility = DevExpress.Utils.DefaultBoolean.True;
            chartControl.Legend.AlignmentHorizontal = LegendAlignmentHorizontal.Right;
            chartControl.Legend.AlignmentVertical = LegendAlignmentVertical.Top;

            // 设置图表边框和背景
            chartControl.BorderOptions.Visibility = DevExpress.Utils.DefaultBoolean.False;

            // 配置图表外观
            //chartControl.PaletteName = "Office 2019";
            //chartControl.PaletteBaseColorNumber = 2;

            // 强制刷新
            //if (diagram != null)
            //{
            //    diagram.AxisX.VisualRange.Auto = true;
            //    diagram.AxisY.VisualRange.Auto = true;
            //}

            // 刷新图表
            chartControl.RefreshData();
            chartControl.Invalidate();
        }

        private void InitializeUI()
        {
            // 初始化左侧文件列表Grid
            fileGridControl = new GridControl { Dock = DockStyle.Left, Width = 350 };
            fileGridView = new GridView();
            fileGridControl.MainView = fileGridView;

            fileGridView.OptionsView.ShowGroupPanel = false;
            fileGridView.OptionsSelection.MultiSelect = true;
            fileGridView.OptionsSelection.MultiSelectMode = GridMultiSelectMode.RowSelect;
            fileGridView.Columns.AddRange(new[]
            {
                new GridColumn { FieldName = "序号", Caption = "序号",  Visible = true, OptionsColumn = { AllowEdit = false, AllowSize = false }},
                new GridColumn { FieldName = "文件名称", Caption = "文件名称", Visible = true,OptionsColumn = {AllowEdit = false, AllowSize = false}},
                new GridColumn { FieldName = "创建时间", Caption = "创建时间", Visible = true,OptionsColumn = {AllowEdit = false, AllowSize = false}},
            });

            // 初始化右击菜单
            InitializeContextMenu();

            // 初始化右侧TabControl
            tabControl = new XtraTabControl
            {
                Dock = DockStyle.Right,
                Width = 1570,
                BorderStyle = BorderStyles.NoBorder
            };

            // 创建表格数据Tab页
            tabPageTable = new XtraTabPage
            {
                Text = "表格数据",
                Padding = new Padding(5)
            };

            // 创建图表Tab页
            tabPageChart = new XtraTabPage
            {
                Text = "数据曲线图",
                Padding = new Padding(5)
            };

            // 初始化表格数据Tab页的Grid
            dataGridControl = new GridControl
            {
                Dock = DockStyle.Fill
            };

            // 对于GridControl，通过外观设置边框样式
            dataGridControl.LookAndFeel.UseDefaultLookAndFeel = false;
            dataGridControl.LookAndFeel.Style = LookAndFeelStyle.Flat;
            dataGridControl.LookAndFeel.SkinName = "DevExpress Style";

            dataGridView = new GridView();
            dataGridControl.MainView = dataGridView;
            tabPageTable.Controls.Add(dataGridControl);

            // 初始化图表Tab页的ChartControl
            chartControl = new ChartControl
            {
                Dock = DockStyle.Fill
            };

            tabPageChart.Controls.Add(chartControl);

            // 将TabPage添加到TabControl
            tabControl.TabPages.AddRange(new XtraTabPage[] { tabPageTable, tabPageChart });

            // 设置默认选中的Tab页
            tabControl.SelectedTabPage = tabPageTable;

            // 添加控件到界面
            Controls.AddRange(new Control[] { fileGridControl, tabControl });
        }

        private void AddDataPoint(IEnumerable<CenterBottomData> array)
        {
            this.Invoke(new Action(() =>
            {
                try
                {
                    foreach (var a in array)
                    {
                        if (!keyValuePairs.ContainsKey(a.Type))
                        {
                            var series = CreateSeries(a.Type);
                            keyValuePairs[a.Type] = series;
                        }
                    }
                    chartControl.BeginInit();

                    foreach (var a in array)
                    {
                        var series = keyValuePairs[a.Type];
                        series.Points.Add(new SeriesPoint(a.X, a.Y));
                        if (series.Points.Count > MAX_POINTS)
                        {
                            series.Points.RemoveAt(0);
                        }
                    }
                    chartControl.EndInit();
                }
                catch (Exception ex) 
                { 
                    LogService.Log(ex.Message); 
                }
            }));
        }

        private Series CreateSeries(string name)
        {
            Series series = new Series(name, ViewType.Line);            // 创建系列并设置为线图
            chartControl.Series.Add(series);
            XYDiagram diagram = (XYDiagram)chartControl.Diagram;            // 配置图表为XYDiagram以支持滚动

            diagram.EnableAxisXScrolling = true;            // 启用X轴滚动 
            diagram.EnableAxisXZooming = true;
            diagram.DefaultPane.BackColor = Color.Transparent;
            diagram.DefaultPane.BorderColor = Color.Transparent;
            diagram.AxisX.VisualRange.Auto = true;// 配置X轴           
            diagram.AxisY.VisualRange.Auto = true; // 配置Y轴            
            chartControl.Legend.Visibility = DevExpress.Utils.DefaultBoolean.True;// 隐藏图例（可选）
            chartControl.Legend.BackColor = Color.Transparent;
            chartControl.Legend.TextColor = Color.White;

            // 设置X轴单位和属性
            diagram.AxisX.Title.TextColor = Color.White;
            diagram.AxisX.Title.DXFont = new DevExpress.Drawing.DXFont("微软雅黑", 10);
            diagram.AxisX.Title.Visibility = DevExpress.Utils.DefaultBoolean.True;
            diagram.AxisX.Title.Alignment = StringAlignment.Far;  // 末端对齐
            diagram.AxisX.Title.Position = AxisTitlePosition.Inside;
            diagram.AxisX.Title.EnableAntialiasing = DevExpress.Utils.DefaultBoolean.True;
            diagram.AxisX.QualitativeScaleOptions.AutoGrid = false;
            diagram.AxisX.Label.ResolveOverlappingOptions.AllowHide = false;
            diagram.AxisX.Label.TextColor = Color.White;
            diagram.AxisX.Color = Color.FromArgb(0, 144, 255);

            // 设置Y轴单位和属性
            diagram.AxisY.Label.TextPattern = "KW";
            diagram.AxisY.Label.TextColor = Color.White;
            diagram.AxisY.GridLines.Color = Color.FromArgb(0, 144, 255);
            diagram.AxisY.Color = Color.FromArgb(0, 144, 255);
            return series;
        }

        private void InitializeContextMenu()
        {
            // 添加右键菜单
            gridContextMenu = new ContextMenuStrip();
            importfiles = new ToolStripMenuItem("导入文件");
            exportfile = new ToolStripMenuItem("导出文件");
            deletefile = new ToolStripMenuItem("删除文件");
            importfiles.Click += ImportFiles_Click;
            exportfile.Click += ExportFile_Click;
            deletefile.Click += DeleteFile_Click;

            gridContextMenu.Items.AddRange(new ToolStripItem[] { importfiles, exportfile, deletefile });
        }

        private async void DeleteFile_Click(object? sender, EventArgs e)
        {
            try
            {
                // 获取选中的行
                int[] selectedRows = fileGridView.GetSelectedRows();
                if (selectedRows.Length == 0)
                {
                    XtraMessageBox.Show("请选择要删除的文件", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 确认对话框
                DialogResult result = XtraMessageBox.Show(
                    $"确定要删除选中的 {selectedRows.Length} 个文件吗？此操作将同时删除所有相关数据，且不可恢复！",
                    "确认删除",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (result != DialogResult.Yes)
                    return;

                // 显示等待对话框
                waitDialog = new WaitDialogForm("正在删除文件", "请稍候...");

                // 使用异步方法显示等待对话框
                await Task.Run(() =>
                {
                    if (this.InvokeRequired)
                    {
                        this.Invoke(new Action(() =>
                        {
                            waitDialog.Show();
                            waitDialog.TopMost = true;
                        }));
                    }
                    else
                    {
                        waitDialog.Show();
                        waitDialog.TopMost = true;
                    }
                });

                try
                {
                    int successCount = 0;
                    int totalCount = selectedRows.Length;
                    int fileId = 0;

                    // 遍历所有选中的行
                    for (int i = 0; i < totalCount; i++)
                    {
                        int rowHandle = selectedRows[i];
                        if (rowHandle >= 0 && rowHandle < fileGridView.RowCount)
                        {
                            string description = $"正在删除文件 {i + 1}/{totalCount}";

                            // 更新等待对话框描述
                            UpdateWaitDialogDescription(description);

                            // 获取文件ID
                            fileId = Convert.ToInt32(fileGridView.GetRowCellValue(rowHandle, "文件ID"));

                            // 异步删除文件及相关数据
                            bool deleted = await Task.Run(() =>
                                _dataWriteManager.DeleteFileAndDataAsync(fileId));

                            if (deleted)
                            {
                                successCount++;
                                LogService.Log($"成功删除文件ID: {fileId}");
                            }
                            else
                            {
                                LogService.Log($"删除文件ID: {fileId} 失败");
                            }
                        }
                    }

                    // 关闭等待对话框
                    CloseWaitDialog();

                    if (XtraMessageBox.Show($"成功删除 {successCount}/{totalCount} 个文件",
                        "完成", MessageBoxButtons.OK, MessageBoxIcon.Information) == DialogResult.OK)
                    {
                        // 刷新文件列表
                        LoadFileList();

                        // 异步加载数据
                        await Task.Run(() =>
                        {
                            if (successCount > 0)
                            {
                                LoadTableData(fileId);
                                LoadChartData(fileId);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    waitDialog.Close();
                    XtraMessageBox.Show($"删除过程中出错: {ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                waitDialog.Close();
                XtraMessageBox.Show($"删除失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateWaitDialogDescription(string description)
        {
            if (waitDialog != null && !waitDialog.IsDisposed && waitDialog.IsHandleCreated)
            {
                if (waitDialog.InvokeRequired)
                {
                    waitDialog.BeginInvoke(new Action(() =>
                    {
                        if (!waitDialog.IsDisposed)
                        {
                            waitDialog.Caption = description;
                            waitDialog.Refresh();
                        }
                    }));
                }
                else
                {
                    waitDialog.Caption = description;
                    waitDialog.Refresh();
                }

                // 允许UI处理消息
                Application.DoEvents();
            }
        }

        private async void ExportFile_Click(object? sender, EventArgs e)
        {
            try
            {
                // 获取选中的行
                int[] selectedRows = fileGridView.GetSelectedRows();
                if (selectedRows.Length == 0)
                {
                    XtraMessageBox.Show("请选择要导出的文件", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 选择导出目录
                using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
                {
                    folderDialog.Description = "选择导出目录";
                    folderDialog.ShowNewFolderButton = true;

                    if (folderDialog.ShowDialog() == DialogResult.OK)
                    {
                        string exportPath = folderDialog.SelectedPath;

                        // 确认对话框
                        DialogResult result = XtraMessageBox.Show(
                            $"确定要导出选中的 {selectedRows.Length} 个文件到目录：\n{exportPath}",
                            "确认导出",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);

                        if (result != DialogResult.Yes)
                            return;

                        // 显示等待对话框
                        using (WaitDialogForm waitDialog = new WaitDialogForm("正在导出文件", "请稍候..."))
                        {
                            waitDialog.Show();
                            waitDialog.TopMost = true;

                            try
                            {
                                int successCount = 0;
                                int totalCount = selectedRows.Length;

                                for (int i = 0; i < totalCount; i++)
                                {
                                    int rowHandle = selectedRows[i];
                                    if (rowHandle >= 0 && rowHandle < fileGridView.RowCount)
                                    {
                                        // 获取文件信息
                                        int fileId = Convert.ToInt32(fileGridView.GetRowCellValue(rowHandle, "文件ID"));
                                        string fileName = fileGridView.GetRowCellValue(rowHandle, "文件名称").ToString();

                                        string description = $"正在导出文件: {fileName}\n({i + 1}/{totalCount})";
                                        if (waitDialog.InvokeRequired)
                                        {
                                            waitDialog.Invoke(new Action(() =>
                                            {
                                                waitDialog.AccessibleDescription = description;
                                                waitDialog.Refresh();
                                            }));
                                        }
                                        else
                                        {
                                            waitDialog.AccessibleDescription = description;
                                            waitDialog.Refresh();
                                        }
                                    }
                                }

                                XtraMessageBox.Show($"成功导出 {successCount}/{totalCount} 个文件到目录：\n{exportPath}",
                                    "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);

                                // 打开导出目录
                                try
                                {
                                    System.Diagnostics.Process.Start("explorer.exe", exportPath);
                                }
                                catch { }
                            }
                            catch (Exception ex)
                            {
                                XtraMessageBox.Show($"导出过程中出错: {ex.Message}", "错误",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"导出失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void ImportFiles_Click(object? sender, EventArgs e)
        {
            try
            {
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = "CSV文件|*.csv|所有文件|*.*";
                    openFileDialog.Title = "选择CSV文件";
                    openFileDialog.Multiselect = true;

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        string[] filePaths = openFileDialog.FileNames;
                        if (filePaths.Length == 0) return;

                        // 创建进度报告器
                        IProgress<string> progress = new Progress<string>(description =>
                        {
                            if (waitDialog != null && !waitDialog.IsDisposed)
                            {
                                waitDialog.AccessibleDescription = description;
                                waitDialog.Refresh();
                            }
                        });

                        // 显示等待对话框
                        waitDialog = new WaitDialogForm("正在导入文件", "请稍候...");
                        waitDialog.Show();
                        waitDialog.TopMost = true;

                        try
                        {
                            // 异步执行导入操作
                            await ImportFilesAsync(filePaths, progress);

                            // 等待所有数据写入完成
                            //await _dataWriteManager.WaitForCompletionAsync(TimeSpan.FromSeconds(30));

                            //XtraMessageBox.Show($"成功导入 {successfulGroups}/{totalGroups} 个文件组",
                            //    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            XtraMessageBox.Show($"导入过程出错: {ex.Message}", "错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"导入失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task ImportFilesAsync(string[] filePaths, IProgress<string> progress)
        {
            // 步骤1：获取所有文件的时间信息
            var fileInfos = new List<FileTimeInfo>();

            for (int i = 0; i < filePaths.Length; i++)
            {
                string filePath = filePaths[i];
                string description = $"正在分析文件时间信息: {Path.GetFileName(filePath)}\n({i + 1}/{filePaths.Length})";

                progress?.Report(description);

                // 添加延迟让UI有机会更新
                await Task.Delay(10);

                try
                {
                    var timeInfo = await csvfilemanager.GetFileTimeInfoAsync(filePath);
                    if (timeInfo != null)
                    {
                        fileInfos.Add(timeInfo);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Log($"分析文件时间信息失败 {Path.GetFileName(filePath)}: {ex.Message}");

                }
            }

            if (fileInfos.Count == 0)
            {
                throw new Exception("未找到有效的时间信息");
            }

            // 步骤2：按开始时间排序
            fileInfos = fileInfos.OrderBy(f => f.StartTime).ToList();

            // 步骤3：根据时间连贯性分组
            var fileGroups = csvfilemanager.GroupFilesByTimeContinuity(fileInfos);

            // 步骤4：处理每个文件组
            int totalGroups = fileGroups.Count;
            int successfulGroups = 0;

            for (int groupIndex = 0; groupIndex < totalGroups; groupIndex++)
            {
                var fileGroup = fileGroups[groupIndex];
                string groupDescription = $"正在处理第 {groupIndex + 1}/{totalGroups} 组文件\n({fileGroup.Count} 个文件)";

                progress?.Report(groupDescription);
                await Task.Delay(10);

                try
                {
                    // 步骤5：合并解析组内所有文件
                    var mergedRecords = await csvfilemanager.MergeParseFileGroupAsync(fileGroup);

                    if (mergedRecords != null && mergedRecords.Count > 0)
                    {
                        // 步骤6：获取文件组信息
                        var groupFileInfo = csvfilemanager.GetFileGroupInfo(fileGroup, groupIndex + 1);

                        // 步骤7：插入FileInfo记录
                        int fileId = await _dataWriteManager.InsertFileInfo(
                            groupFileInfo.FileName,
                            groupFileInfo.FilePath,
                            groupFileInfo.FileSize,
                            DateTime.Now);

                        if (fileId == 0) continue;

                        // 步骤8：添加到写入队列
                        _dataWriteManager.EnqueueData(fileId, mergedRecords, true);
                        successfulGroups++;

                        LogService.Log($"成功导入文件组 {groupIndex + 1}: {groupFileInfo.FileName}");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Log($"处理文件组 {groupIndex + 1} 失败: {ex.Message}");
                }
            }
        }

        // 事件处理方法
        private void OnBatchWritten(object sender, BatchWriteEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => OnBatchWritten(sender, e)));
                return;
            }

            //Console.WriteLine($"文件 {e.FileId} 的 {e.RecordType} 记录写入 {e.Count} 条");
        }

        private void OnWriteCompleted(object sender, string message)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => OnWriteCompleted(sender, message)));
                return;
            }

            waitDialog.Close();
            // 刷新文件列表
            LoadFileList();
            //XtraMessageBox.Show(message, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnWriteError(object sender, string errorMessage)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => OnWriteError(sender, errorMessage)));
                return;
            }

            XtraMessageBox.Show($"写入错误: {errorMessage}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // 取消事件订阅
                if (_dataWriteManager != null)
                {
                    _dataWriteManager.BatchWritten -= OnBatchWritten;
                    _dataWriteManager.WriteCompleted -= OnWriteCompleted;
                    _dataWriteManager.WriteError -= OnWriteError;

                    // 异步停止并等待
                    Task.Run(async () => await _dataWriteManager.StopAsync()).Wait(TimeSpan.FromSeconds(10));
                    _dataWriteManager.Dispose();
                }
            }

            base.Dispose(disposing);
            _disposed = true;
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (!_disposed)
            {
                Dispose(true);
            }
            base.OnHandleDestroyed(e);
        }

        private void LoadFileList()
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    conn.Open();

                    string sql = @"
                        SELECT 
                        f.FileID as '文件ID',
                        f.FileName as '文件名称', 
                        f.CreateTime as '创建时间',
                        f.RecordCount as '记录数',
                        f.FilePath as '文件路径',
                        f.FileSize as '文件大小'
                        FROM FileInfo f
                        ORDER BY f.CreateTime DESC";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    using (var adapter = new SQLiteDataAdapter(cmd))
                    {
                        DataTable dt = new DataTable();
                        adapter.Fill(dt);

                        // 添加序号列
                        dt.Columns.Add("序号", typeof(int));

                        // 为每一行分配序号（从1开始）
                        int sequence = 1;
                        foreach (DataRow row in dt.Rows)
                        {
                            row["序号"] = sequence++;
                        }

                        // 重新排列列的顺序，使序号列在最前面
                        dt.Columns["序号"].SetOrdinal(0);

                        fileGridControl.DataSource = dt;
                    }

                    conn.Close();
                }

                // 配置列宽和格式
                ConfigureGridViewAfterLoad();
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"加载文件列表失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ConfigureGridViewAfterLoad()
        {
            // 确保列宽设置生效
            fileGridView.OptionsView.ColumnAutoWidth = true;

            // 设置每列的最小宽度
            foreach (GridColumn col in fileGridView.Columns)
            {
                switch (col.FieldName)
                {
                    case "序号":
                        col.Width = 30;
                        col.AppearanceCell.TextOptions.HAlignment = HorzAlignment.Center;
                        col.AppearanceHeader.TextOptions.HAlignment = HorzAlignment.Center;
                        break;
                    case "文件名称":
                        col.Width = 60;
                        col.AppearanceCell.TextOptions.HAlignment = HorzAlignment.Near;
                        col.AppearanceHeader.TextOptions.HAlignment = HorzAlignment.Center;
                        break;
                    case "创建时间":
                        col.AppearanceCell.TextOptions.HAlignment = HorzAlignment.Center;
                        col.AppearanceHeader.TextOptions.HAlignment = HorzAlignment.Center;
                        col.DisplayFormat.FormatString = "yyyy-MM-dd HH:mm:ss";
                        break;
                }
            }

            fileGridView.BestFitColumns();

            // 设置行高
            fileGridView.RowHeight = 26;

            // 设置偶数行背景色（可选）
            fileGridView.OptionsView.EnableAppearanceEvenRow = true;
            fileGridView.Appearance.EvenRow.BackColor = Color.FromArgb(245, 245, 245);

            // 设置选中行样式
            fileGridView.Appearance.FocusedRow.BackColor = Color.FromArgb(220, 235, 255);
            fileGridView.Appearance.FocusedRow.Options.UseBackColor = true;
            fileGridView.Appearance.SelectedRow.BackColor = Color.FromArgb(200, 225, 255);
            fileGridView.Appearance.SelectedRow.Options.UseBackColor = true;
        }

        private void ShowWaitDialog(string caption, string description)
        {
            CloseWaitDialog();
            waitDialog = new WaitDialogForm(caption, description);
        }

        private void CloseWaitDialog()
        {
            if (waitDialog != null)
            {
                if (!waitDialog.IsDisposed)
                {
                    waitDialog.Close();
                }
                waitDialog.Dispose();
                waitDialog = null;
            }
        }
    }
}