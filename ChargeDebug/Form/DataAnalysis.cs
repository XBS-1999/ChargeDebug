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
using System.Collections.Concurrent;
using DevExpress.DataProcessing;
using DevExpress.XtraPrinting;

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

        private bool _isDragging;
        private Point _dragStartPoint;          // 鼠标按下时的屏幕坐标（客户端坐标）
        private double _dragStartXMinOa;         // 按下时 X 轴最小值（OADate）
        private double _dragStartXMaxOa;         // 按下时 X 轴最大值（OADate）
        private double _dragStartV0;             // 按下时鼠标所在点的 X 轴数值（OADate）

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

            chartControl.MouseDown += ChartControl_MouseDown;
            chartControl.MouseMove += ChartControl_MouseMove;
            chartControl.MouseUp += ChartControl_MouseUp;
        }

        private void ChartControl_MouseUp(object? sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                chartControl.Cursor = Cursors.Default;
            }
        }

        private void ChartControl_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            if (e.Button != MouseButtons.Left) // 如果左键意外松开，停止拖动
            {
                _isDragging = false;
                chartControl.Cursor = Cursors.Default;
                return;
            }
            if (!(chartControl.Diagram is XYDiagram diagram)) return;

            // 获取当前鼠标位置对应的数据值
            Point currentPoint = chartControl.PointToClient(Cursor.Position);
            DiagramCoordinates currentCoords = diagram.PointToDiagram(currentPoint);
            if (currentCoords == null || currentCoords.IsEmpty) return;

            double currentV = currentCoords.DateTimeArgument.ToOADate();

            // 计算偏移量（OADate）
            double diff = currentV - _dragStartV0;

            // 计算新的 X 轴范围
            double newMinOa = _dragStartXMinOa - diff;
            double newMaxOa = _dragStartXMaxOa - diff;

            // 可选：添加边界限制（防止平移超出数据范围）
            // 如果您有数据边界，可以在此限制 newMinOa 和 newMaxOa

            // 转换为 DateTime 并应用
            DateTime newMin = DateTime.FromOADate(newMinOa);
            DateTime newMax = DateTime.FromOADate(newMaxOa);

            diagram.AxisX.VisualRange.SetMinMaxValues(newMin, newMax);
            diagram.AxisX.VisualRange.Auto = false;

            chartControl.RefreshData();
        }

        private void ChartControl_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (!(chartControl.Diagram is XYDiagram diagram)) return;

            // 获取当前 X 轴范围
            var xRange = diagram.AxisX.VisualRange;
            if (xRange.MinValue == null || xRange.MaxValue == null) return;

            DateTime xMinDate = (DateTime)xRange.MinValue;
            DateTime xMaxDate = (DateTime)xRange.MaxValue;
            double xMinOa = xMinDate.ToOADate();
            double xMaxOa = xMaxDate.ToOADate();

            // 获取鼠标点对应的数据值
            Point clientPoint = chartControl.PointToClient(Cursor.Position);
            DiagramCoordinates coords = diagram.PointToDiagram(clientPoint);
            if (coords == null || coords.IsEmpty) return;

            // 记录拖动起始信息
            _dragStartPoint = clientPoint;
            _dragStartXMinOa = xMinOa;
            _dragStartXMaxOa = xMaxOa;
            _dragStartV0 = coords.DateTimeArgument.ToOADate();  // X 轴为日期时间类型
            _isDragging = true;

            // 可选：改变光标样式
            chartControl.Cursor = Cursors.SizeWE;
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
        /// 确保 TargetTable 的列与 Template 配置一致，缺失则自动创建
        /// </summary>
        private void EnsureTargetTableColumns()
        {
            // 如果 template 未加载，重新加载
            if (template == null || template.Count == 0)
            {
                LoadTemplateConfiguration();
            }

            using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
            {
                conn.Open();

                // 检查 TargetTable 是否存在
                string checkTableSql = "SELECT name FROM sqlite_master WHERE type='table' AND name='TargetTable'";

                using (var cmd = new SQLiteCommand(checkTableSql, conn))
                {
                    var result = cmd.ExecuteScalar();
                    bool tableExists = result != null && result.ToString() == "TargetTable";

                    if (!tableExists)
                    {
                        CreateTargetTable(conn);
                    }
                    else
                    {
                        AddMissingColumns(conn);
                    }
                }
            }
        }

        /// <summary>
        /// 创建 TargetTable 表（包含 ID、FileID 和所有 Template 列）
        /// </summary>
        private void CreateTargetTable(SQLiteConnection conn)
        {
            var columnDefinitions = new List<string>
            {
                "ID INTEGER PRIMARY KEY AUTOINCREMENT",
                "FileID INTEGER"
            };

            foreach (var item in template)
            {
                // 根据 Field_Type 决定列类型，若为空则默认 TEXT
                string columnType = string.IsNullOrEmpty(item.Field_Type) ? "TEXT" : item.Field_Type.ToUpperInvariant();
                // 可选：对特殊列（如 Time）强制 TEXT，避免用户误配置
                //if (item.Name.Equals("Time", StringComparison.OrdinalIgnoreCase))
                //{
                //    columnType = "TEXT";
                //}
                columnDefinitions.Add($"\"{item.Name}\" {columnType}");
            }

            string createSql = $"CREATE TABLE TargetTable ({string.Join(", ", columnDefinitions)})";
            using (var cmd = new SQLiteCommand(createSql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 为现有 TargetTable 添加缺失的列
        /// </summary>
        private void AddMissingColumns(SQLiteConnection conn)
        {
            // 获取现有列名
            var existingColumns = new List<string>();
            string pragmaSql = "PRAGMA table_info(TargetTable)";
            using (var cmd = new SQLiteCommand(pragmaSql, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string colName = reader["name"].ToString();
                    existingColumns.Add(colName);
                }
            }

            // 找出缺失的列（忽略大小写）
            var missingColumns = template.Select(t => t.Name)
                                         .Except(existingColumns, StringComparer.OrdinalIgnoreCase)
                                         .ToList();

            foreach (string colName in missingColumns)
            {
                // 从 template 中找到对应项
                var templateItem = template.FirstOrDefault(t => t.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
                if (templateItem == null) continue;

                string columnType = string.IsNullOrEmpty(templateItem.Field_Type) ? "TEXT" : templateItem.Field_Type.ToUpperInvariant();
                if (templateItem.Name.Equals("Time", StringComparison.OrdinalIgnoreCase))
                {
                    columnType = "TEXT";
                }

                string alterSql = $"ALTER TABLE TargetTable ADD COLUMN \"{colName}\" {columnType}";
                using (var cmd = new SQLiteCommand(alterSql, conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
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

                    // 确保 TargetTable 列与 Template 一致
                    await Task.Run(() => EnsureTargetTableColumns());

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
                            _dataWriteManager.WriteToTargetTableAsync(_currentFileId, template));

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

                // ====== 新增：检查时间连续性 ======
                CheckTimeContinuity(dataTable, fileId);

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
                        if (templates.Field_Type == "double")
                        {
                            channelColumn.DisplayFormat.FormatType = FormatType.Numeric;
                        }
                        else
                        {
                            channelColumn.DisplayFormat.FormatType = FormatType.None;
                        }
                        //channelColumn.DisplayFormat.FormatString = "F1";
                        
                    }

                    // 设置内容居中
                    channelColumn.AppearanceCell.TextOptions.HAlignment = HorzAlignment.Center;
                    // 标题居中（此处为列级别设置，可选，若已全局设置则可省略）
                    channelColumn.AppearanceHeader.TextOptions.HAlignment = HorzAlignment.Center;
                    dataGridView.Columns.Add(channelColumn);
                }
            }

            // 配置网格外观
            dataGridView.OptionsView.ShowGroupPanel = false;
            dataGridView.OptionsView.ShowAutoFilterRow = false;
            dataGridView.OptionsView.ShowFooter = true;
            dataGridView.OptionsBehavior.Editable = false;

            // 设置行高
            dataGridView.RowHeight = 26;

            // 设置偶数行背景色
            dataGridView.OptionsView.EnableAppearanceEvenRow = true;
            dataGridView.Appearance.EvenRow.BackColor = Color.FromArgb(245, 245, 245);

            dataGridView.Appearance.Row.TextOptions.HAlignment = HorzAlignment.Center;

            // 全局设置标题居中（作为默认值，不影响已显式设置的列）
            dataGridView.Appearance.HeaderPanel.TextOptions.HAlignment = HorzAlignment.Center;
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

        private void UpdateChartWithData(DataTable dataTable)
        {
            chartControl.Series.Clear();

            if (dataTable.Rows.Count == 0)
                return;

            // 收集所有需要绘制的列（Type_Chart == "true" 且不是 Time）
            List<string> valueColumns = new List<string>();
            foreach (var templates in template)
            {
                if (templates.Type_Chart == "true" && templates.Name != "Time")
                {
                    valueColumns.Add(templates.Name);
                }
            }

            // 检查 Time 列是否存在
            if (!dataTable.Columns.Contains("Time"))
            {
                LogService.Log("数据表中缺少 Time 列，无法生成图表");
                return;
            }

            // 创建只包含所需列的新 DataTable
            DataTable finalTable = new DataTable();
            finalTable.Columns.Add("Time", typeof(DateTime));
            foreach (string colName in valueColumns)
            {
                finalTable.Columns.Add(colName, typeof(double));
            }

            // 填充数据：逐行解析 Time 和数值列
            foreach (DataRow row in dataTable.Rows)
            {
                DataRow newRow = finalTable.NewRow();

                // 解析 Time（支持字符串或 DateTime 类型）
                if (!DateTime.TryParse(row["Time"].ToString(), out DateTime time))
                    continue; // 时间无效则跳过整行

                newRow["Time"] = time;

                // 解析各数值列
                foreach (string colName in valueColumns)
                {
                    if (row[colName] != DBNull.Value &&
                        double.TryParse(row[colName].ToString(), out double val))
                    {
                        newRow[colName] = val;
                    }
                    else
                    {
                        newRow[colName] = DBNull.Value; // 无法转换或空值
                    }
                }

                finalTable.Rows.Add(newRow);
            }

            // 创建图表系列（仍然使用模板中的列名）
            foreach (var templates in template)
            {
                if (templates.Type_Chart == "true" && templates.Name != "Time")
                {
                    Series series = new Series(templates.ChineseName, ViewType.Line);
                    series.ArgumentScaleType = ScaleType.DateTime;
                    series.ArgumentDataMember = "Time";
                    series.ValueDataMembers.AddRange(templates.Name);
                    series.CrosshairLabelPattern = "{A:HH:mm:ss} {V:F1}";
                    chartControl.Series.Add(series);
                }
            }

            // 绑定数据源
            chartControl.DataSource = null;
            chartControl.DataSource = finalTable;

            ConfigureChartDisplay();

            // 可选日志（原代码保留）
            //if (chartControl.Series.Count > 0)
            //{
            //    var series = chartControl.Series[0];
            //    LogService.Log($"系列 {series.Name} 点数: {series.Points.Count}");
            //    LogService.Log($"数据表行数: {finalTable.Rows.Count}");
            //}
        }

        // 辅助方法：判断类型是否为数值类型
        private bool IsNumericType(Type type)
        {
            return type == typeof(int) || type == typeof(double) || type == typeof(decimal) ||
                   type == typeof(float) || type == typeof(long) || type == typeof(short) ||
                   type == typeof(byte) || type == typeof(sbyte) || type == typeof(uint) ||
                   type == typeof(ulong) || type == typeof(ushort);
        }

        /// <summary>
        /// 配置图表显示格式
        /// </summary>
        private void ConfigureChartDisplay()
        {
            XYDiagram diagram = (XYDiagram)chartControl.Diagram;

            if (diagram != null)
            {
                diagram.AxisX.DateTimeScaleOptions.ScaleMode = ScaleMode.Continuous;
                diagram.AxisX.Title.Text = "时间";
                diagram.AxisX.Title.Visible = true;
                diagram.AxisX.Label.TextPattern = "{A:HH:mm:ss}";
                diagram.AxisX.Label.Angle = 45;
                diagram.AxisX.Label.ResolveOverlappingOptions.AllowRotate = true;
                diagram.AxisX.Label.ResolveOverlappingOptions.AllowStagger = true;
                diagram.AxisX.GridLines.Visible = true;
                diagram.AxisX.WholeRange.SideMarginsValue = 0;

                diagram.AxisY.Title.Text = "功率(KW)/SOC(%)";
                diagram.AxisY.Title.Visible = true;
                diagram.AxisY.Label.TextPattern = "{F1}";
                diagram.AxisY.WholeRange.SideMarginsValue = 0;

                // 启用滚动和缩放（关键）
                diagram.ScrollingOptions.UseMouse = true;
                diagram.ScrollingOptions.UseKeyboard = true;
                diagram.ZoomingOptions.UseMouseWheel = true;
                diagram.ZoomingOptions.UseKeyboard = true;
                diagram.ZoomingOptions.AxisXMaxZoomPercent = 10000; // X轴最大缩放百分比
                diagram.ZoomingOptions.AxisYMaxZoomPercent = 10000; // Y轴最大缩放百分比

                diagram.AxisX.VisualRange.Auto = true;
                diagram.AxisY.VisualRange.Auto = true;
            }

            // 确保图表获得焦点
            chartControl.MouseEnter += (s, e) => chartControl.Focus();

            chartControl.MouseWheel += (s, e) => {
                if (chartControl.Diagram is XYDiagram diagram)
                {
                    // 获取当前坐标轴范围
                    var xRange = diagram.AxisX.VisualRange;
                    var yRange = diagram.AxisY.VisualRange;

                    if (xRange.MinValue == null || xRange.MaxValue == null ||
                        yRange.MinValue == null || yRange.MaxValue == null)
                        return;

                    // 将 X 轴端点转为 OADate（天数）
                    DateTime xMinDate = (DateTime)xRange.MinValue;
                    DateTime xMaxDate = (DateTime)xRange.MaxValue;
                    double xMinOa = xMinDate.ToOADate();
                    double xMaxOa = xMaxDate.ToOADate();

                    // Y 轴仍为数值
                    double yMin, yMax;
                    try
                    {
                        yMin = Convert.ToDouble(yRange.MinValue);
                        yMax = Convert.ToDouble(yRange.MaxValue);
                    }
                    catch
                    {
                        return;
                    }

                    double zoomFactor = (e.Delta > 0) ? 0.8 : 1.25;

                    // ----- 获取鼠标位置对应的图表坐标（缩放中心）-----
                    Point mousePos = chartControl.PointToClient(Cursor.Position);
                    DiagramCoordinates diagramCoords = diagram.PointToDiagram(mousePos);
                    double xCenterOa, yCenter;

                    if (diagramCoords != null && !diagramCoords.IsEmpty)
                    {
                        // X 轴为日期时间类型 → 使用 DateTimeArgument
                        DateTime xCenterDate = diagramCoords.DateTimeArgument;
                        xCenterOa = xCenterDate.ToOADate();

                        // Y 轴为数值类型 → 使用 NumericalValue
                        yCenter = diagramCoords.NumericalValue;
                    }
                    else
                    {
                        // 若无法获取（如鼠标移出绘图区），回退到视图中心
                        xCenterOa = (xMinOa + xMaxOa) / 2;
                        yCenter = (yMin + yMax) / 2;
                    }

                    // 确保缩放中心位于当前视图范围内（防止越界计算）
                    if (xCenterOa < xMinOa) xCenterOa = xMinOa;
                    if (xCenterOa > xMaxOa) xCenterOa = xMaxOa;
                    if (yCenter < yMin) yCenter = yMin;
                    if (yCenter > yMax) yCenter = yMax;

                    bool shiftPressed = (Control.ModifierKeys & Keys.Shift) != 0;

                    if (!shiftPressed)
                    {
                        // ---------- X 轴缩放（日期时间）----------
                        // 以鼠标点为中心计算新范围（OADate 单位）
                        double newXMinOa = xCenterOa - (xCenterOa - xMinOa) * zoomFactor;
                        double newXMaxOa = xCenterOa + (xMaxOa - xCenterOa) * zoomFactor;

                        // 边界限制：避免范围过小或过大
                        double minSpan = 1.0 / 1440.0;                     // 最小跨度：1分钟（1天=1440分钟）
                        double maxSpan = (xMaxOa - xMinOa) * 5;             // 最大跨度：当前范围的5倍

                        double newSpan = newXMaxOa - newXMinOa;
                        if (newSpan < minSpan)
                        {
                            // 强制保持最小跨度，中心点不变
                            newXMinOa = xCenterOa - minSpan / 2;
                            newXMaxOa = xCenterOa + minSpan / 2;
                        }
                        else if (newSpan > maxSpan)
                        {
                            newXMinOa = xCenterOa - maxSpan / 2;
                            newXMaxOa = xCenterOa + maxSpan / 2;
                        }

                        // 转回 DateTime 并应用
                        DateTime newXMin = DateTime.FromOADate(newXMinOa);
                        DateTime newXMax = DateTime.FromOADate(newXMaxOa);

                        diagram.AxisX.VisualRange.SetMinMaxValues(newXMin, newXMax);
                        diagram.AxisX.VisualRange.Auto = false;
                    }
                    else
                    {
                        // ---------- Y 轴缩放（数值）----------
                        // 以鼠标点为中心计算新范围
                        double newYMin = yCenter - (yCenter - yMin) * zoomFactor;
                        double newYMax = yCenter + (yMax - yCenter) * zoomFactor;

                        // 可选：Y 轴范围限制（防止负数等，根据实际需求调整）
                        double minYRange = 0.001; // 例如最小范围0.001
                        double maxYRange = (yMax - yMin) * 5; // 最大跨度5倍

                        double newYRange = newYMax - newYMin;
                        if (newYRange < minYRange)
                        {
                            newYMin = yCenter - minYRange / 2;
                            newYMax = yCenter + minYRange / 2;
                        }
                        else if (newYRange > maxYRange)
                        {
                            newYMin = yCenter - maxYRange / 2;
                            newYMax = yCenter + maxYRange / 2;
                        }

                        diagram.AxisY.VisualRange.SetMinMaxValues(newYMin, newYMax);
                        diagram.AxisY.VisualRange.Auto = false;
                    }

                    chartControl.RefreshData();

                    if (e is HandledMouseEventArgs hme)
                        hme.Handled = true;
                }
            };

            chartControl.Legend.Visibility = DevExpress.Utils.DefaultBoolean.True;
            chartControl.Legend.AlignmentHorizontal = LegendAlignmentHorizontal.Right;
            chartControl.Legend.AlignmentVertical = LegendAlignmentVertical.Top;

            chartControl.BorderOptions.Visibility = DevExpress.Utils.DefaultBoolean.False;

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

            //tabControl.MouseWheel += (s, e) => {
            //    // 获取鼠标下的控件
            //    Point mousePos = tabControl.PointToClient(Cursor.Position);
            //    Control ctrl = tabControl.GetChildAtPoint(mousePos);
            //    if (ctrl == chartControl)
            //    {
            //        Console.WriteLine("XtraTabControl 拦截了图表的滚轮事件");
            //        // 可选：将事件手动转发给图表
            //        // chartControl.Focus();
            //        // SendKeys.SendWait(e.Delta > 0 ? "{PGUP}" : "{PGDN}"); // 不推荐
            //    }
            //};

            // 添加控件到界面
            Controls.AddRange(new Control[] { fileGridControl, tabControl });
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

        private void ExportFile_Click(object? sender, EventArgs e)
        {
            try
            {
                // 检查数据源是否存在且包含数据
                if (dataGridControl.DataSource == null)
                {
                    XtraMessageBox.Show("没有可导出的数据", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 获取数据表（已知在 LoadTableData 中设置为 DataTable）
                DataTable dt = dataGridControl.DataSource as DataTable;
                if (dt == null || dt.Rows.Count == 0)
                {
                    XtraMessageBox.Show("没有可导出的数据", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 让用户选择保存路径和格式
                using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.Filter = "Excel 工作簿 (*.xlsx)|*.xlsx|CSV 文件 (*.csv)|*.csv";
                    saveFileDialog.Title = "导出数据";
                    saveFileDialog.FileName = $"数据导出_{DateTime.Now:yyyyMMddHHmmss}";

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        string fileName = saveFileDialog.FileName;
                        string extension = Path.GetExtension(fileName).ToLower();

                        // 显示等待对话框（避免界面假死）
                        //WaitDialogForm waitDialog = new WaitDialogForm("正在导出数据", "请稍候...");
                        //waitDialog.Show();
                        //waitDialog.TopMost = true;

                        try
                        {
                            // 根据文件扩展名选择导出方式
                            switch (extension)
                            {
                                case ".xlsx":
                                    XlsxExportOptionsEx xlsxOptions = new XlsxExportOptionsEx();
                                    xlsxOptions.ExportType = DevExpress.Export.ExportType.DataAware;
                                    dataGridView.ExportToXlsx(fileName, xlsxOptions);

                                    // 使用 ClosedXML 创建工作簿
                                    //using (var workbook = new XLWorkbook())
                                    //{
                                    //    // 添加工作表，命名为“数据”
                                    //    var worksheet = workbook.Worksheets.Add("数据");

                                    //    // 将 DataTable 导入工作表（包括列标题）
                                    //    worksheet.Cell(1, 1).InsertTable(dt);

                                    //    // 可选：调整列宽以适应内容
                                    //    worksheet.Columns().AdjustToContents();

                                    //    // 保存文件
                                    //    workbook.SaveAs(fileName);
                                    //}
                                    break;
                                case ".csv":
                                    CsvExportOptionsEx csvOptions = new CsvExportOptionsEx();
                                    csvOptions.ExportType = DevExpress.Export.ExportType.DataAware;
                                    dataGridView.ExportToCsv(fileName, csvOptions);
                                    break;
                                default:
                                    XtraMessageBox.Show("不支持的文件格式", "错误",
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    return;
                            }

                            XtraMessageBox.Show($"数据导出成功！\n文件保存至：{fileName}", "完成",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            XtraMessageBox.Show($"导出失败：{ex.Message}", "错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        finally
                        {
                            //waitDialog.Close();
                            //waitDialog.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"导出过程中发生错误：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        /// <summary>
        /// 检查数据表中时间列是否连续（严格递增，无重复）
        /// </summary>
        /// <param name="dt">数据表</param>
        /// <param name="fileId">当前文件ID，用于日志记录</param>
        private void CheckTimeContinuity(DataTable dt, int fileId)
        {
            if (dt == null || dt.Rows.Count < 2) return;
            if (!dt.Columns.Contains("Time")) return;

            DateTime? prevTime = null;
            int rowIndex = 0;
            const double maxAllowedSeconds = 2.0;

            foreach (DataRow row in dt.Rows)
            {
                string timeStr = row["Time"]?.ToString();
                if (DateTime.TryParse(timeStr, out DateTime currTime))
                {
                    if (prevTime.HasValue)
                    {
                        double diffSeconds = (currTime - prevTime.Value).TotalSeconds;
                        if (diffSeconds > maxAllowedSeconds || diffSeconds < 0)
                        {
                            // 时间差超过1秒，或时间倒退（虽然应由ORDER BY保证，但以防万一）
                            LogService.Log($"警告：文件ID {fileId} 时间不连续，第 {rowIndex - 1} 行到第 {rowIndex} 行间隔 {diffSeconds:F3} 秒，上一时间: {prevTime.Value:yyyy-MM-dd HH:mm:ss}，当前时间: {currTime:yyyy-MM-dd HH:mm:ss}");
                        }
                    }
                    prevTime = currTime;
                }
                else
                {
                    LogService.Log($"警告：文件ID {fileId} 第 {rowIndex} 行时间格式无法解析: {timeStr}");
                }
                rowIndex++;
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