using Aspose.Cells;
using ChargeDebug.Service;
using DataModel;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraPrinting;
using Log;
using System.ComponentModel;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.ServiceModel.Channels;
using System.Text;
using System.Text.RegularExpressions;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class FaultRecording : XtraUserControl
    {
        private GridControl leftGridControl;
        private GridView leftGridView;
        private GridControl rightGridControl;
        private GridView rightGridView;
        private ComboBoxEdit comboBoxEdit;
        private SimpleButton btnQuery;
        private SimpleButton btnImport;
        private SimpleButton btnExport;
        private WaitDialogForm waitDialog;

        private List<EquipmentModel> faultrecordingList = new List<EquipmentModel>();
        private List<FaultRecordingSignals> _faultTemplates = new List<FaultRecordingSignals>();
        private string dbcPath = "";
        private List<FaultSignals> acfaultTemplate;
        private List<FaultSignals> dcfaultTemplate;

        private List<FaultRecord> faultRecord = new List<FaultRecord>();

        private Dictionary<string, Dictionary<uint, List<byte[]>>> _channelRawData = new Dictionary<string, Dictionary<uint, List<byte[]>>>();
        private Dictionary<string, List<FaultRecordingSignals>> _channelTemplates = new Dictionary<string, List<FaultRecordingSignals>>();
        private Dictionary<string, DataTable> _channelDataTables = new Dictionary<string, DataTable>();

        // 后台工作相关成员
        private BackgroundWorker _queryWorker;
        private CancellationTokenSource _cancellationTokenSource;

        private bool _isLayoutSuspended = false;
        private Size _lastSize = Size.Empty;

        public FaultRecording(string dbPath, List<EquipmentModel> equipmentList)
        {
            dbcPath = dbPath;
            DeviceConfig(equipmentList);
            //faultrecordingList = equipmentList;
            InitializeComponent();
            LoadFaultTemplates();
            InitializeUI();
            InitializeBackgroundWorker();
        }

        private void DeviceConfig(List<EquipmentModel> equipmentList)
        {
            faultrecordingList.Clear();
            foreach (var equipmentLists in equipmentList)
            {
                if (equipmentLists.DeviceType == "充放电设备")
                {
                    faultrecordingList.Add(equipmentLists);
                }
            }
        }

        public void UpdateDcNumber(List<EquipmentModel> equipmentList)
        {
            faultrecordingList = equipmentList;
            //清除所有旧布局
            this.Controls.Clear();
            //LoadFaultTemplates();
            InitializeUI();
            //InitializeBackgroundWorker();
        }

        private void InitializeBackgroundWorker()
        {
            _queryWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };

            _queryWorker.DoWork += QueryWorker_DoWork;
            _queryWorker.ProgressChanged += QueryWorker_ProgressChanged;
            _queryWorker.RunWorkerCompleted += QueryWorker_RunWorkerCompleted;
        }

        private void LoadFaultTemplates()
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();
                    _faultTemplates = SQLite_Service.GetFaultRecording(conn);
                    var faultTemplates = SQLite_Service.GetFaultSignals(conn);

                    acfaultTemplate = new List<FaultSignals>();
                    dcfaultTemplate = new List<FaultSignals>();

                    foreach (var faultTemplate in faultTemplates)
                    {
                        if (faultTemplate.Signalname.StartsWith("AC-"))
                        {
                            acfaultTemplate.Add(new FaultSignals
                            {
                                Signalname = faultTemplate.Signalname,
                                Startbit = faultTemplate.Startbit,
                                Length = faultTemplate.Length,
                                ByteOrder = faultTemplate.ByteOrder,
                                Signed = faultTemplate.Signed,
                                Factor = faultTemplate.Factor,
                                Offset = faultTemplate.Offset
                            });
                        }
                        else if (faultTemplate.Signalname.StartsWith("DC-"))
                        {
                            dcfaultTemplate.Add(new FaultSignals
                            {
                                Signalname = faultTemplate.Signalname,
                                Startbit = faultTemplate.Startbit,
                                Length = faultTemplate.Length,
                                ByteOrder = faultTemplate.ByteOrder,
                                Signed = faultTemplate.Signed,
                                Factor = faultTemplate.Factor,
                                Offset = faultTemplate.Offset
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"加载故障模板失败: {ex.Message}", "数据库错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InitializeUI()
        {
            SplitContainer splitContainer = new SplitContainer();
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.Orientation = Orientation.Vertical;
            splitContainer.FixedPanel = FixedPanel.None;
            splitContainer.SplitterDistance = splitContainer.Width / 10;
            // 禁止拖拽分割条
            splitContainer.IsSplitterFixed = true;  //关键设置

            PanelControl leftPanel = new PanelControl();
            leftPanel.Dock = DockStyle.Fill;
            leftPanel.BorderStyle = BorderStyles.NoBorder;

            PanelControl topPanel = new PanelControl();
            topPanel.Dock = DockStyle.Top;
            topPanel.Height = 120;
            topPanel.BorderStyle = BorderStyles.NoBorder;

            comboBoxEdit = new ComboBoxEdit();
            comboBoxEdit.Location = new Point(30, 20);
            comboBoxEdit.Size = new Size(260, 22);
            comboBoxEdit.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;

            GenerateChannelOptions();

            btnQuery = new SimpleButton();
            btnQuery.Location = new Point(30, comboBoxEdit.Bottom + 20);
            btnQuery.Size = new Size(80, 30);
            btnQuery.Text = "查询";
            btnQuery.Click += BtnQuery_Click;

            btnImport = new SimpleButton();
            btnImport.Location = new Point(btnQuery.Right + 10, comboBoxEdit.Bottom + 20);
            btnImport.Size = new Size(80, 30);
            btnImport.Text = "导入";
            btnImport.Click += BtnImport_Click; // 添加点击事件

            btnExport = new SimpleButton();
            btnExport.Location = new Point(btnImport.Right + 10, comboBoxEdit.Bottom + 20);
            btnExport.Size = new Size(80, 30);
            btnExport.Text = "导出";
            btnExport.Click += BtnExport_Click;

            leftGridControl = new GridControl();
            leftGridView = new GridView();
            leftGridControl.MainView = leftGridView;
            leftGridControl.Dock = DockStyle.Fill;
            leftGridView.OptionsView.ShowGroupPanel = false;

            rightGridControl = new GridControl();
            rightGridView = new GridView();
            rightGridControl.MainView = rightGridView;
            rightGridControl.Dock = DockStyle.Fill;
            rightGridView.OptionsView.ShowGroupPanel = false;

            topPanel.Controls.Add(comboBoxEdit);
            topPanel.Controls.Add(btnQuery); 
            topPanel.Controls.Add(btnImport);
            topPanel.Controls.Add(btnExport);

            leftPanel.Controls.Add(leftGridControl);
            leftPanel.Controls.Add(topPanel);

            splitContainer.Panel1.Controls.Add(leftPanel);
            splitContainer.Panel2.Controls.Add(rightGridControl);

            this.Controls.Add(splitContainer);

            InitializeLeftGridColumns();
            InitializeRightGridColumns();
        }

        private void BtnImport_Click(object sender, EventArgs e)
        {
            try
            {
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = "CSV文件|*.csv|文本文件|*.txt|所有文件|*.*";
                    openFileDialog.Title = "选择故障录波数据文件";
                    openFileDialog.Multiselect = false;

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        // 直接解析文件，不使用BackgroundWorker
                        ImportAndLoadData(openFileDialog.FileName);
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"导入失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportAndLoadData(string filePath)
        {
            try
            {
                // 清空现有数据
                faultRecord.Clear();
                _channelRawData.Clear();
                _channelTemplates.Clear();
                _channelDataTables.Clear();

                // 解析CSV文件
                ParseCsvFile(filePath);

                // 创建通道记录
                CreateChannelRecords();

                // 更新左侧表格
                leftGridControl.DataSource = faultRecord;

                // 如果有通道数据，默认选中第一个并直接调用LoadChannelData
                if (faultRecord.Count > 0)
                {
                    leftGridView.FocusedRowHandle = 0;
                    string firstChannel = faultRecord[0].Passage;
                    LoadChannelData(firstChannel);
                }

                // 显示导入结果
                int totalChannels = faultRecord.Count;
                int successChannels = faultRecord.Count(r => r.State == "成功");

                XtraMessageBox.Show($"导入成功！\n共导入{totalChannels}个通道，其中{successChannels}个通道数据完整。",
                    "信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"导入失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ParseCsvFile(string filePath)
        {
            // 读取文件所有行
            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);

            // 检查文件格式
            if (lines.Length < 2)
            {
                throw new Exception("CSV文件格式错误：文件内容为空或只有标题行");
            }

            // 用于统计CANID数量的字典
            Dictionary<string, int> canIdCounts = new Dictionary<string, int>();

            // 跳过标题行（"Time,Data"）
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                // 解析行数据
                ParseCsvLine(line, i, canIdCounts);
            }
        }

        private void ParseCsvLine(string line, int lineNumber, Dictionary<string, int> canIdCounts)
        {
            try
            {
                // 分割时间戳和数据部分
                string[] parts = line.Split(new[] { ',' }, 2);
                if (parts.Length < 2)
                {
                    LogService.Log($"第{lineNumber}行格式错误: {line}");
                    return;
                }

                string timeStamp = parts[0].Trim();
                string dataPart = parts[1].Trim();

                // 解析CAN ID和数据
                ParseCanData(dataPart, lineNumber, canIdCounts);
            }
            catch (Exception ex)
            {
                LogService.Log($"解析第{lineNumber}行时出错: {ex.Message}");
            }
        }

        private void ParseCanData(string dataPart, int lineNumber, Dictionary<string, int> canIdCounts)
        {
            // 数据格式示例: "0X0030CCA0 = 02 C9 03 00 00 00 00 00"
            string[] parts = dataPart.Split(new[] { '=' }, 2);
            if (parts.Length < 2)
            {
                LogService.Log($"第{lineNumber}行数据格式错误: {dataPart}");
                return;
            }

            string canIdStr = parts[0].Trim();
            string dataStr = parts[1].Trim();

            // 解析CAN ID
            if (!uint.TryParse(canIdStr.Replace("0X", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint canId))
            {
                LogService.Log($"第{lineNumber}行CAN ID格式错误: {canIdStr}");
                return;
            }

            // 解析数据字节
            string[] byteStrs = dataStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (byteStrs.Length != 8)
            {
                LogService.Log($"第{lineNumber}行数据字节数不正确: {dataStr}");
                return;
            }

            byte[] dataBytes = new byte[8];
            for (int i = 0; i < 8; i++)
            {
                if (!byte.TryParse(byteStrs[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out dataBytes[i]))
                {
                    LogService.Log($"第{lineNumber}行第{i + 1}个字节格式错误: {byteStrs[i]}");
                    dataBytes[i] = 0;
                }
            }

            // 统计CANID数量
            if (!canIdCounts.ContainsKey(canIdStr))
            {
                canIdCounts[canIdStr] = 0;
            }
            canIdCounts[canIdStr]++;

            // 根据CANID最后两位确定通道名称
            string channelKey = DetermineChannelByCanId(canId);

            if (string.IsNullOrEmpty(channelKey))
            {
                // 如果无法确定通道，则使用CANID作为通道名
                channelKey = canIdStr;
                LogService.Log($"第{lineNumber}行使用CANID作为通道名: {canIdStr}");
            }

            // 将数据按照CANID分类存储到对应通道
            ClassifyDataByCanId(channelKey, canId, dataBytes);
        }

        private string DetermineChannelByCanId(uint canId)
        {
            // 获取CAN ID的最后两位（最后一个字节）
            byte lastByte = (byte)(canId & 0xFF);
            string lastTwoHex = lastByte.ToString("X2");

            // 根据最后两位判断通道
            // AC通道: A0, A1, A2, ...
            // DC通道: 20, 21, 22, ...

            if (lastTwoHex.StartsWith("A", StringComparison.OrdinalIgnoreCase))
            {
                // AC通道
                if (int.TryParse(lastTwoHex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int channelNum))
                {
                    // 通道号从1开始，A0表示AC通道1
                    return $"AC{channelNum + 1}";
                }
            }
            else if (lastTwoHex.StartsWith("2", StringComparison.OrdinalIgnoreCase))
            {
                // DC通道
                if (int.TryParse(lastTwoHex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int channelNum))
                {
                    // 通道号从1开始，20表示DC通道1
                    return $"DC{channelNum + 1}";
                }
            }
            else if (lastTwoHex.StartsWith("3", StringComparison.OrdinalIgnoreCase))
            {
                // 可能还有其他类型的通道
                if (int.TryParse(lastTwoHex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int channelNum))
                {
                    return $"CH{channelNum + 1}";
                }
            }

            return null;
        }

        private void ClassifyDataByCanId(string channelKey, uint canId, byte[] data)
        {
            // 初始化通道数据结构
            if (!_channelRawData.ContainsKey(channelKey))
            {
                _channelRawData[channelKey] = new Dictionary<uint, List<byte[]>>();
                //LogService.Log($"创建新通道: {channelKey}");
            }

            var channelData = _channelRawData[channelKey];

            // 初始化该CAN ID的数据列表
            if (!channelData.ContainsKey(canId))
            {
                channelData[canId] = new List<byte[]>();
                //LogService.Log($"通道 {channelKey} 添加新CANID: 0x{canId:X8}");
            }

            // 添加数据
            channelData[canId].Add(data);

            // 为通道创建模板（如果还没有）
            if (!_channelTemplates.ContainsKey(channelKey))
            {
                CreateTemplateForChannel(channelKey);
            }
        }

        private void CreateTemplateForChannel(string channelKey)
        {
            List<FaultRecordingSignals> templates = new List<FaultRecordingSignals>();

            // 根据通道名称确定模板
            if (channelKey.StartsWith("AC"))
            {
                // AC通道模板
                int acNum = 0;
                if (int.TryParse(channelKey.Substring(2), out acNum))
                {
                    foreach (var template in _faultTemplates)
                    {
                        if (template.CANID.Contains("AX"))
                        {
                            var newTemplate = CloneSignal(template);
                            newTemplate.CANID = template.CANID.Replace("AX", $"A{acNum - 1}");
                            templates.Add(newTemplate);
                        }
                    }
                }
            }
            else if (channelKey.StartsWith("DC"))
            {
                // DC通道模板
                int dcNum = 0;
                if (int.TryParse(channelKey.Substring(2), out dcNum))
                {
                    foreach (var template in _faultTemplates)
                    {
                        if (template.CANID.Contains("2X"))
                        {
                            var newTemplate = CloneSignal(template);
                            newTemplate.CANID = template.CANID.Replace("2X", $"2{dcNum - 1}");
                            templates.Add(newTemplate);
                        }
                    }
                }
            }
            else if (channelKey.StartsWith("0X") || channelKey.StartsWith("0x"))
            {
                // 如果通道名就是CANID，尝试直接匹配模板
                string canIdStr = channelKey.Replace("0X", "").Replace("0x", "");
                if (uint.TryParse(canIdStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint canId))
                {
                    foreach (var template in _faultTemplates)
                    {
                        if (uint.TryParse(template.CANID.Replace("0X", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint templateCanId))
                        {
                            if (templateCanId == canId)
                            {
                                templates.Add(CloneSignal(template));
                            }
                        }
                    }
                }
            }

            if (templates.Count > 0)
            {
                _channelTemplates[channelKey] = templates;
                //LogService.Log($"为通道 {channelKey} 创建了 {templates.Count} 个信号模板");
            }
            else
            {
                LogService.Log($"警告: 未为通道 {channelKey} 找到匹配的模板");
            }
        }

        private void CreateChannelRecords()
        {
            // 为每个通道创建记录
            foreach (var channelKvp in _channelRawData)
            {
                string channelName = channelKvp.Key;
                var channelData = channelKvp.Value;

                // 检查通道是否有足够的数据
                bool hasEnoughData = false;

                // 统计总数据帧数
                int totalFrames = channelData.Values.Sum(list => list.Count);

                // 检查通道是否有数据
                if (totalFrames > 0)
                {
                    hasEnoughData = true;

                    // 输出通道统计信息
                    LogService.Log($"通道 {channelName} 统计:");
                    LogService.Log($"  总帧数: {totalFrames}  CANID数量: {channelData.Count}");

                    foreach (var canId in channelData.Keys.OrderBy(k => k))
                    {
                        LogService.Log($"    CANID 0x{canId:X8}: {channelData[canId].Count} 帧");
                    }
                }

                faultRecord.Add(new FaultRecord
                {
                    Passage = channelName,
                    State = hasEnoughData ? "成功" : "数据不足"
                });
            }

            // 按通道名称排序
            faultRecord = faultRecord
                .OrderBy(r => r.Passage, new ChannelNameComparer())
                .ToList();

            LogService.Log($"共创建{faultRecord.Count}个通道的记录");
        }

        // 通道名称比较器
        private class ChannelNameComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                if (x == null && y == null) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                // 分离前缀和数字部分
                string prefixX = new string(x.TakeWhile(char.IsLetter).ToArray());
                string prefixY = new string(y.TakeWhile(char.IsLetter).ToArray());

                // 比较前缀
                int prefixCompare = string.Compare(prefixX, prefixY, StringComparison.Ordinal);
                if (prefixCompare != 0) return prefixCompare;

                // 提取数字部分
                string numStrX = new string(x.Skip(prefixX.Length).TakeWhile(char.IsDigit).ToArray());
                string numStrY = new string(y.Skip(prefixY.Length).TakeWhile(char.IsDigit).ToArray());

                if (int.TryParse(numStrX, out int numX) && int.TryParse(numStrY, out int numY))
                {
                    return numX.CompareTo(numY);
                }

                return string.Compare(x, y, StringComparison.Ordinal);
            }
        }

        private void BtnExport_Click(object? sender, EventArgs e)
        {
            try
            {
                SaveFileDialog saveDialog = new SaveFileDialog();
                saveDialog.Filter = "Excel 文件|*.xlsx";
                saveDialog.Title = "保存故障记录";
                saveDialog.FileName = "故障录波" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    // 使用新的导出方法
                    ExportDataTablesToExcel(saveDialog.FileName);
                    XtraMessageBox.Show("导出成功！", "信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                string errorDetails = $"导出失败: {ex.Message}\nStackTrace: {ex.StackTrace}";
                if (ex.InnerException != null)
                {
                    errorDetails += $"\nInnerException: {ex.InnerException.Message}";
                }

                LogService.Log(errorDetails);

                XtraMessageBox.Show($"导出失败: {ex.Message}\n请检查磁盘空间和文件访问权限", "导出错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void GenerateChannelOptions()
        {
            foreach (var item in faultrecordingList)
            {
                if (item != null)
                    comboBoxEdit.Properties.Items.Add($"{item.DeviceName}");
            }

            comboBoxEdit.SelectedIndex = 0;
        }

        private void InitializeLeftGridColumns()
        {
            leftGridView.Columns.Clear();

            leftGridView.Columns.Add(new GridColumn
            {
                Caption = "通道号",
                FieldName = "Passage",
                Visible = true,
                Width = 100,
                OptionsColumn = { AllowEdit = false }
            });

            leftGridView.Columns.Add(new GridColumn
            {
                Caption = "状态",
                FieldName = "State",
                Visible = true,
                Width = 150,
                OptionsColumn = { AllowEdit = false }
            });
        }

        private void InitializeRightGridColumns()
        {
            // 留空
        }

        private void CreateRightGridColumns(List<FaultRecordingSignals> signals)
        {
            rightGridView.Columns.Clear();

            // 设置表头文本不换行
            rightGridView.Appearance.HeaderPanel.TextOptions.WordWrap = WordWrap.NoWrap;
            rightGridView.Appearance.HeaderPanel.TextOptions.HAlignment = HorzAlignment.Center;
            rightGridView.Appearance.HeaderPanel.TextOptions.VAlignment = VertAlignment.Center;
            //rightGridView.OptionsView.ColumnAutoWidth = false;
            //rightGridView.HorzScrollVisibility = ScrollVisibility.Always;

            // 设置整个表格的单元格内容居中
            rightGridView.Appearance.Row.TextOptions.HAlignment = HorzAlignment.Center;
            rightGridView.Appearance.Row.TextOptions.VAlignment = VertAlignment.Center;

            // 设置表头行高和单元格内边距
            rightGridView.Appearance.HeaderPanel.Options.UseTextOptions = true;
            rightGridView.Appearance.HeaderPanel.TextOptions.VAlignment = VertAlignment.Center;

            // 禁用列宽自动调整，允许水平滚动
            rightGridView.OptionsView.ColumnAutoWidth = false;
            rightGridView.HorzScrollVisibility = ScrollVisibility.Always;

            // 序号列
            GridColumn indexColumn = new GridColumn
            {
                Caption = "序号",
                FieldName = "序号",
                Visible = true,
                Width = 60,
                MinWidth = 40,
                MaxWidth = 80,
                OptionsColumn = { AllowEdit = false }
            };
            // 设置列居中
            SetColumnCenterAlignment(indexColumn);
            rightGridView.Columns.Add(indexColumn);

            // 动态创建信号列
            foreach (var signal in signals)
            {
                if (ShouldSkipSignal(signal)) continue;

                string caption = GetColumnCaption(signal);
                int suggestedWidth = CalculateOptimalColumnWidth(caption, signal);

                GridColumn column = new GridColumn
                {
                    Caption = caption,
                    FieldName = signal.SignalName,
                    Visible = true,
                    Width = suggestedWidth,
                    MinWidth = 60,
                    MaxWidth = 300,
                    OptionsColumn = { AllowEdit = false }
                };

                // 设置列居中
                SetColumnCenterAlignment(column);
                
                rightGridView.Columns.Add(column);
            }

            // 添加时间列
            // 添加故障信息列（合并后的列）
            AddTimeColumn("故障信息", 200);
            AddTimeColumn("中位机指令时间", 120);
            AddTimeColumn("故障时间", 120);
            
            // 设置表格整体布局
            ConfigureGridViewLayout();
        }

        private bool ShouldSkipSignal(FaultRecordingSignals signal)
        {
            return signal.SignalName == "序号" ||
                   signal.SignalName.Contains("故障时间") ||
                   signal.SignalName.Contains("中位机指令时间") ||
                   signal.SignalName.Contains("故障信息");
        }

        private string GetColumnCaption(FaultRecordingSignals signal)
        {
            if (!string.IsNullOrEmpty(signal.Unit))
            {
                return $"{signal.SignalName}\n({signal.Unit})";
            }
            return signal.SignalName;
        }

        private int CalculateOptimalColumnWidth(string caption, FaultRecordingSignals signal)
        {
            // 根据内容长度计算合适的宽度
            int baseWidth = 17;
            string caption1 = new string(caption.Where(c =>
                                 (c >= 'A' && c <= 'Z') ||  // 大写字母
                                 (c >= 'a' && c <= 'z') ||  // 小写字母
                                 (c >= '\u4e00' && c <= '\u9fa5')).ToArray());  // 汉字
            baseWidth = baseWidth * caption1.Length + 22;

            // 应用DPI缩放
            float dpiScaleFactor = GetDpiScaleFactor();
            return (int)(baseWidth * dpiScaleFactor);
        }

        private void SetColumnCenterAlignment(GridColumn column)
        {
            column.AppearanceHeader.TextOptions.WordWrap = WordWrap.Wrap;
            column.AppearanceCell.TextOptions.HAlignment = HorzAlignment.Center;
            column.AppearanceCell.TextOptions.VAlignment = VertAlignment.Center;
            column.AppearanceHeader.TextOptions.HAlignment = HorzAlignment.Center;
            column.AppearanceHeader.TextOptions.VAlignment = VertAlignment.Center;
        }

        private void AddTimeColumn(string caption, int width)
        {
            GridColumn timeColumn = new GridColumn
            {
                Caption = caption,
                FieldName = caption,
                Visible = true,
                Width = width,
                MinWidth = 100,
                MaxWidth = 180,
                OptionsColumn = { AllowEdit = false }
            };

            SetColumnCenterAlignment(timeColumn);
            rightGridView.Columns.Add(timeColumn);
        }

        private void ConfigureGridViewLayout()
        {
            rightGridView.OptionsView.ColumnAutoWidth = false;
            rightGridView.HorzScrollVisibility = ScrollVisibility.Always;

            // 设置行高和行内容对齐
            rightGridView.RowHeight = 25;
            rightGridView.Appearance.EvenRow.TextOptions.HAlignment = HorzAlignment.Center;
            rightGridView.Appearance.EvenRow.TextOptions.VAlignment = VertAlignment.Center;
            rightGridView.Appearance.OddRow.TextOptions.HAlignment = HorzAlignment.Center;
            rightGridView.Appearance.OddRow.TextOptions.VAlignment = VertAlignment.Center;

            // 设置空单元格的显示和对齐
            rightGridView.Appearance.Empty.TextOptions.HAlignment = HorzAlignment.Center;
            rightGridView.Appearance.Empty.TextOptions.VAlignment = VertAlignment.Center;

            // 设置网格线 - 显示水平和垂直网格线
            rightGridView.OptionsView.ShowHorizontalLines = DefaultBoolean.True;
            rightGridView.OptionsView.ShowVerticalLines = DefaultBoolean.True;
        }

        private float GetDpiScaleFactor()
        {
            using (Graphics g = this.CreateGraphics())
            {
                return g.DpiX / 96f;
            }
        }

        //protected override void OnResize(EventArgs e)
        //{
        //    base.OnResize(e);

        //    // 防止频繁调整
        //    if (_isLayoutSuspended || this.Width <= 0 || this.Height <= 0)
        //        return;

        //    // 检查大小是否真正改变
        //    if (_lastSize == this.Size)
        //        return;

        //    _lastSize = this.Size;

        //    // 延迟执行布局调整，避免性能问题
        //    Task.Delay(100).ContinueWith(t =>
        //    {
        //        if (this.IsHandleCreated && !this.IsDisposed)
        //        {
        //            this.BeginInvoke(new Action(() =>
        //            {
        //                try
        //                {
        //                    _isLayoutSuspended = true;

        //                    // 调整分割条位置（保持比例）
        //                    if (this.Controls.Count > 0 && this.Controls[0] is SplitContainer splitContainer)
        //                    {
        //                        // 保持左侧20%的比例
        //                        int targetSplitterDistance = (int)(splitContainer.Width * 0.2);
        //                        if (targetSplitterDistance > 100) // 最小宽度限制
        //                        {
        //                            splitContainer.SplitterDistance = targetSplitterDistance;
        //                        }
        //                    }

        //                    // 重新计算列宽
        //                    if (rightGridView != null && rightGridView.Columns.Count > 0)
        //                    {
        //                        AdjustColumnWidthsBasedOnAvailableSpace();
        //                    }
        //                }
        //                finally
        //                {
        //                    _isLayoutSuspended = false;
        //                }
        //            }));
        //        }
        //    }, TaskScheduler.FromCurrentSynchronizationContext());
        //}

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            leftGridView.RowClick += LeftGridView_RowClick;
        }

        private void AdjustColumnWidthsBasedOnAvailableSpace()
        {
            if (rightGridView == null || rightGridControl == null) return;

            // 计算可用宽度（减去滚动条宽度）
            int availableWidth = rightGridControl.Width - SystemInformation.VerticalScrollBarWidth;

            if (availableWidth <= 0) return;

            // 计算所有列的总宽度
            int totalColumnsWidth = 0;
            foreach (GridColumn column in rightGridView.Columns)
            {
                if (column.Visible)
                {
                    totalColumnsWidth += column.Width;
                }
            }

            // 如果总宽度小于可用宽度，可以适当增加列宽
            if (totalColumnsWidth < availableWidth && availableWidth - totalColumnsWidth > 50)
            {
                int extraWidth = (availableWidth - totalColumnsWidth) / rightGridView.Columns.Count;
                foreach (GridColumn column in rightGridView.Columns)
                {
                    if (column.Visible && column.MaxWidth > column.Width + extraWidth)
                    {
                        column.Width += extraWidth;
                    }
                }
            }
            // 如果总宽度大于可用宽度，启用水平滚动条
            else if (totalColumnsWidth > availableWidth)
            {
                rightGridView.HorzScrollVisibility = ScrollVisibility.Always;
            }
        }

        private void LeftGridView_RowClick(object sender, RowClickEventArgs e)
        {
            var record = (FaultRecord)leftGridView.GetRow(e.RowHandle);
            if (record != null)
            {
                LoadChannelData(record.Passage);
            }
        }

        private void LoadChannelData(string passage)
        {
            if (!_channelTemplates.TryGetValue(passage, out var templates))
                return;

            CreateRightGridColumns(templates);

            if (!_channelRawData.TryGetValue(passage, out var channelData))
            {
                XtraMessageBox.Show($"未找到 {passage} 的原始数据", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                rightGridControl.DataSource = null;
                return;
            }

            // 检查缓存中是否有该通道的数据
            if (_channelDataTables.ContainsKey(passage))
            {
                rightGridControl.DataSource = _channelDataTables[passage];

                // 过滤列：如果某列所有数据都为DBNull.Value，则不显示该列
                FilterEmptyColumns(_channelDataTables[passage]);
                return;
            }

            DataTable dataTable = new DataTable();
            dataTable.Columns.Add("序号", typeof(int));

            foreach (var signal in templates)
            {
                if (signal.SignalName != "序号" &&
                    !signal.SignalName.Contains("故障时间") &&
                    !signal.SignalName.Contains("故障信息") &&
                    !signal.SignalName.Contains("中位机指令时间"))
                {
                    dataTable.Columns.Add(signal.SignalName, typeof(double));
                }
            }

            dataTable.Columns.Add("故障信息", typeof(string));

            // 添加中位机指令时间列
            dataTable.Columns.Add("中位机指令时间", typeof(string));

            // 添加故障时间列
            dataTable.Columns.Add("故障时间", typeof(string));

            Dictionary<int, Dictionary<uint, byte[]>> indexedFrames = new Dictionary<int, Dictionary<uint, byte[]>>();

            foreach (var kv in channelData)
            {
                uint canId = kv.Key;
                if (kv.Value.Count == 1)
                {
                    foreach (byte[] frameData in kv.Value)
                    {
                        int index = 1;
                        if (index >= 1 && index <= 1000)
                        {
                            if (!indexedFrames.ContainsKey(index))
                            {
                                indexedFrames[index] = new Dictionary<uint, byte[]>();
                            }
                            indexedFrames[index][canId] = frameData;
                        }
                    }
                }
                else
                {
                    foreach (byte[] frameData in kv.Value)
                    {
                        int index = 0;
                        if (frameData.Length >= 2)
                        {
                            index = frameData[0] | (frameData[1] << 8);
                            index = index + 1;
                        }

                        if (index >= 1 && index <= 1000)
                        {
                            if (!indexedFrames.ContainsKey(index))
                            {
                                indexedFrames[index] = new Dictionary<uint, byte[]>();
                            }
                            indexedFrames[index][canId] = frameData;
                        }
                    }
                }
            }

            for (int i = 1; i <= 1000; i++)
            {
                DataRow row = dataTable.NewRow();
                row["序号"] = i;

                // 初始化时间相关变量
                int commandHour = 0, commandMinute = 0, commandSecond = 0, commandMillisecond = 0;
                int faultHour = 0, faultMinute = 0, faultSecond = 0, faultMillisecond = 0;
                bool hasCommandTime = false, hasFaultTime = false;

                // 收集当前序号下的所有故障信息原始值
                Dictionary<int, ulong> faultInfoValues = new Dictionary<int, ulong>();

                if (indexedFrames.TryGetValue(i, out var framesForIndex))
                {
                    foreach (var signal in templates)
                    {
                        if (signal.SignalName == "序号") continue;

                        try
                        {
                            if (uint.TryParse(signal.CANID, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint signalCanId))
                            {
                                if (framesForIndex.TryGetValue(signalCanId, out byte[] frameData))
                                {
                                    var signalInfo = new SignalInfo
                                    {
                                        StartBit = signal.StartBit,
                                        Length = signal.Length,
                                        ByteOrder = signal.ByteOrder,
                                        Signed = signal.Signed,
                                        Factor = signal.Factor,
                                        Offset = signal.Offset
                                    };

                                    ulong rawValue = CANManager.Instance.ExtractRawValue(frameData, signalInfo);
                                    double physicalValue = CANManager.Instance.ConvertToPhysicalValue(rawValue, signalInfo);

                                    // 检查是否为故障信息信号
                                    if (signal.SignalName.Contains("故障信息"))
                                    {
                                        // 提取故障信息编号
                                        string faultInfoStr = signal.SignalName.Replace("故障信息", "");
                                        if (int.TryParse(faultInfoStr, out int faultInfoIndex))
                                        {
                                            // 存储故障信息的原始值
                                            faultInfoValues[faultInfoIndex] = rawValue;
                                        }
                                        continue; // 不添加到数据行
                                    }

                                    // 解析故障时间信号
                                    if (signal.SignalName.Contains("故障时间-时"))
                                    {
                                        faultHour = (int)physicalValue;
                                        hasFaultTime = true;
                                        continue; // 不添加到数据行
                                    }
                                    else if (signal.SignalName.Contains("故障时间-分"))
                                    {
                                        faultMinute = (int)physicalValue;
                                        hasFaultTime = true;
                                        continue; // 不添加到数据行
                                    }
                                    else if (signal.SignalName.Contains("故障时间-秒"))
                                    {
                                        faultSecond = (int)physicalValue;
                                        hasFaultTime = true;
                                        continue; // 不添加到数据行
                                    }
                                    else if (signal.SignalName.Contains("故障时间-毫秒"))
                                    {
                                        faultMillisecond = (int)physicalValue;
                                        hasFaultTime = true;
                                        continue; // 不添加到数据行
                                    }
                                    // 解析中位机指令时间信号
                                    else if (signal.SignalName.Contains("中位机指令时间-时"))
                                    {
                                        commandHour = (int)physicalValue;
                                        hasCommandTime = true;
                                    }
                                    else if (signal.SignalName.Contains("中位机指令时间-分"))
                                    {
                                        commandMinute = (int)physicalValue;
                                        hasCommandTime = true;
                                    }
                                    else if (signal.SignalName.Contains("中位机指令时间-秒"))
                                    {
                                        commandSecond = (int)physicalValue;
                                        hasCommandTime = true;
                                    }
                                    else if (signal.SignalName.Contains("中位机指令时间-毫秒"))
                                    {
                                        commandMillisecond = (int)physicalValue;
                                        hasCommandTime = true;
                                    }
                                    else
                                    {
                                        // 普通信号列
                                        if (dataTable.Columns.Contains(signal.SignalName))
                                        {
                                            row[signal.SignalName] = physicalValue;
                                        }
                                    }
                                }
                                else
                                {
                                    // 没有数据
                                    if (dataTable.Columns.Contains(signal.SignalName) &&
                                        !signal.SignalName.Contains("故障信息") &&
                                        !signal.SignalName.Contains("时间"))
                                    {
                                        row[signal.SignalName] = DBNull.Value;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"解析信号 {signal.SignalName} 错误: {ex.Message}");
                            if (dataTable.Columns.Contains(signal.SignalName) &&
                                !signal.SignalName.Contains("故障信息") &&
                                !signal.SignalName.Contains("时间"))
                            {
                                row[signal.SignalName] = DBNull.Value;
                            }
                        }
                    }

                    // 处理合并的故障信息
                    string combinedFaultInfo = ParseCombinedFaultInfo(faultInfoValues,
                        passage.StartsWith("AC") ? acfaultTemplate : dcfaultTemplate);
                    row["故障信息"] = combinedFaultInfo;

                    // 设置中位机指令时间
                    if (hasCommandTime)
                    {
                        row["中位机指令时间"] = $"{commandHour:D2}:{commandMinute:D2}:{commandSecond:D2}:{commandMillisecond:D3}";
                    }
                    else
                    {
                        row["中位机指令时间"] = DBNull.Value;
                    }

                    // 设置故障时间
                    if (hasFaultTime)
                    {
                        row["故障时间"] = $"{faultHour:D2}:{faultMinute:D2}:{faultSecond:D2}:{faultMillisecond:D3}";
                    }
                    else
                    {
                        row["故障时间"] = DBNull.Value;
                    }
                }
                else
                {
                    // 没有数据时，为存在的列设置DBNull
                    foreach (DataColumn column in dataTable.Columns)
                    {
                        if (column.ColumnName != "序号")
                        {
                            row[column] = DBNull.Value;
                        }
                    }
                }
                dataTable.Rows.Add(row);
            }

            // 缓存数据表
            _channelDataTables[passage] = dataTable;
            rightGridControl.DataSource = dataTable;

            // 过滤列：如果某列所有数据都为DBNull.Value，则不显示该列
            FilterEmptyColumns(dataTable);
        }

        /// <summary>
        /// 将故障信息0-7的8个字节（64位）按照故障模板解析成具体的故障描述
        /// 每个故障信息是1个字节（8位），总共8个字节（64位）
        /// </summary>
        private string ParseCombinedFaultInfo(Dictionary<int, ulong> faultInfoValues, List<FaultSignals> faultTemplate)
        {
            if (faultInfoValues.Count == 0)
            {
                return "正常";
            }

            StringBuilder faultDescription = new StringBuilder();
            HashSet<string> uniqueFaults = new HashSet<string>(); // 用于去重

            // 遍历所有故障位模板
            foreach (var faultSignal in faultTemplate)
            {
                try
                {
                    // 确定这个故障位属于哪个故障信息帧
                    // 每个故障信息帧是1个字节（8位）
                    // Startbit是故障位的绝对位置（0-63）
                    int faultInfoIndex = faultSignal.Startbit / 8;  // 确定属于哪个故障信息帧（0-7）
                    int bitPosition = faultSignal.Startbit % 8;     // 确定在帧内的位位置（0-7）

                    if (faultInfoValues.TryGetValue(faultInfoIndex, out ulong faultValue))
                    {
                        // 由于每个故障信息只有1个字节，我们只需要取低8位
                        byte byteValue = (byte)(faultValue & 0xFF);

                        // 检查该位是否为1
                        int bitValue = (byteValue >> bitPosition) & 0x01;
                        if (bitValue == 1)
                        {
                            // 提取故障描述
                            string faultName = faultSignal.Signalname;

                            // 去掉AC-或DC-前缀（如果有的话）
                            if (faultName.StartsWith("AC-") || faultName.StartsWith("DC-"))
                            {
                                faultName = faultName.Substring(3);
                            }

                            // 添加到集合中（自动去重）
                            if (!uniqueFaults.Contains(faultName))
                            {
                                uniqueFaults.Add(faultName);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Log($"解析故障位 {faultSignal.Signalname} 错误: {ex.Message}");
                }
            }

            // 将去重后的故障描述拼接成字符串
            if (uniqueFaults.Count > 0)
            {
                foreach (var faultName in uniqueFaults.OrderBy(name => name))
                {
                    if (faultDescription.Length > 0)
                    {
                        faultDescription.Append(", ");
                    }
                    faultDescription.Append(faultName);
                }
            }
            else
            {
                return "正常";
            }

            return faultDescription.ToString();
        }

        private void FilterEmptyColumns(DataTable dataTable)
        {
            // 获取需要过滤的列名列表
            List<string> columnsToHide = new List<string>();

            foreach (DataColumn column in dataTable.Columns)
            {
                if (column.ColumnName == "序号") continue;

                bool allNull = true;

                // 检查该列的所有行
                for (int i = 0; i < dataTable.Rows.Count; i++)
                {
                    object value = dataTable.Rows[i][column];

                    // 如果该行不是DBNull，也不是空字符串，则说明该列有数据
                    if (value != DBNull.Value && value != null)
                    {
                        if (value is string stringValue)
                        {
                            if (!string.IsNullOrEmpty(stringValue))
                            {
                                allNull = false;
                                break;
                            }
                        }
                        else
                        {
                            allNull = false;
                            break;
                        }
                    }
                }

                if (allNull)
                {
                    columnsToHide.Add(column.ColumnName);
                }
            }

            // 隐藏全空的列
            foreach (string columnName in columnsToHide)
            {
                var column = rightGridView.Columns.ColumnByFieldName(columnName);
                if (column != null)
                {
                    column.Visible = false;
                }
            }
        }

        private string GetCurrentChannel()
        {
            if (leftGridView.FocusedRowHandle >= 0)
            {
                var record = leftGridView.GetRow(leftGridView.FocusedRowHandle) as FaultRecord;
                if (record != null)
                {
                    return record.Passage;
                }
            }
            return string.Empty;
        }

        private void ExportDataTablesToExcel(string filePath)
        {
            // 先检查当前显示的通道是否已缓存，如果没有则缓存它
            if (rightGridControl.DataSource is DataTable currentTable)
            {
                var currentChannel = GetCurrentChannel();
                if (!string.IsNullOrEmpty(currentChannel) && !_channelDataTables.ContainsKey(currentChannel))
                {
                    _channelDataTables[currentChannel] = currentTable;
                }
            }

            Workbook workbook = new Workbook();
            workbook.Worksheets.Clear();

            // 获取所有通道记录
            var channels = leftGridControl.DataSource as List<FaultRecord>;
            if (channels == null || channels.Count == 0)
            {
                XtraMessageBox.Show("没有可导出的数据", "警告",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 样式设置
            Style defaultStyle = workbook.CreateStyle();
            defaultStyle.Number = 0;
            defaultStyle.HorizontalAlignment = TextAlignmentType.Center;
            defaultStyle.VerticalAlignment = TextAlignmentType.Center;
            defaultStyle.Font.Name = "宋体";
            defaultStyle.Font.Size = 10;

            Style headerStyle = workbook.CreateStyle();
            headerStyle.Pattern = BackgroundType.Solid;
            headerStyle.ForegroundColor = Color.LightGray;
            headerStyle.Font.IsBold = true;
            headerStyle.HorizontalAlignment = TextAlignmentType.Center;
            headerStyle.VerticalAlignment = TextAlignmentType.Center;
            headerStyle.Font.Name = "宋体";
            headerStyle.Font.Size = 10;
            headerStyle.Font.IsBold = true;

            bool hasData = false;

            foreach (var channel in channels)
            {
                // 从缓存中获取数据表
                if (_channelDataTables.TryGetValue(channel.Passage, out DataTable dataTable))
                {
                    if (dataTable == null || dataTable.Rows.Count == 0)
                        continue;

                    hasData = true;

                    // 创建工作表
                    Worksheet sheet = workbook.Worksheets.Add(channel.Passage);
                    sheet.Cells.StandardHeight = 20;
                    sheet.Cells.SetRowHeight(0, 25);

                    // 确定要导出的列（过滤全空的列）
                    List<DataColumn> columnsToExport = new List<DataColumn>();

                    foreach (DataColumn column in dataTable.Columns)
                    {
                        // 检查该列是否全空
                        bool allNull = true;
                        for (int row = 0; row < dataTable.Rows.Count; row++)
                        {
                            object value = dataTable.Rows[row][column];
                            if (value != DBNull.Value && value != null)
                            {
                                if (value is string stringValue)
                                {
                                    if (!string.IsNullOrEmpty(stringValue))
                                    {
                                        allNull = false;
                                        break;
                                    }
                                }
                                else
                                {
                                    allNull = false;
                                    break;
                                }
                            }
                        }

                        // 如果列不全空，则导出
                        if (!allNull)
                        {
                            columnsToExport.Add(column);
                        }
                    }

                    // 如果没有要导出的列，跳过该工作表
                    if (columnsToExport.Count == 0)
                        continue;

                    // 写入列标题
                    for (int col = 0; col < columnsToExport.Count; col++)
                    {
                        Cell headerCell = sheet.Cells[0, col];
                        headerCell.PutValue(dataTable.Columns[col].ColumnName);
                        headerCell.SetStyle(headerStyle);
                    }

                    // 写入数据
                    int dataRowCount = 0;
                    for (int row = 0; row < dataTable.Rows.Count; row++)
                    {
                        // 跳过所有列都为空的空行
                        bool isEmptyRow = true;
                        for (int col = 0; col < columnsToExport.Count; col++)
                        {
                            object value = dataTable.Rows[row][columnsToExport[col]];
                            if (value != DBNull.Value && value != null)
                            {
                                if (value is string stringValue)
                                {
                                    if (!string.IsNullOrEmpty(stringValue))
                                    {
                                        isEmptyRow = false;
                                        break;
                                    }
                                }
                                else
                                {
                                    isEmptyRow = false;
                                    break;
                                }
                            }
                        }

                        if (isEmptyRow)
                            continue;

                        dataRowCount++;
                        sheet.Cells.SetRowHeight(dataRowCount, 20);
                        for (int col = 0; col < columnsToExport.Count; col++)
                        {
                            Cell cell = sheet.Cells[dataRowCount, col];
                            object value = dataTable.Rows[row][columnsToExport[col]];

                            if (value == DBNull.Value || value == null)
                            {
                                cell.PutValue("");
                            }
                            else if (value is double || value is float || value is decimal)
                            {
                                cell.PutValue(Convert.ToDouble(value));
                            }
                            else if (value is int || value is long || value is short)
                            {
                                cell.PutValue(Convert.ToInt32(value));
                            }
                            else
                            {
                                cell.PutValue(value.ToString());
                            }
                            cell.SetStyle(defaultStyle);
                        }
                    }

                    // 如果没有数据行，跳过这个工作表
                    if (dataRowCount == 0)
                    {
                        workbook.Worksheets.RemoveAt(workbook.Worksheets.Count - 1);
                        continue;
                    }

                    // 调整列宽
                    sheet.AutoFitColumns();

                    // 冻结表头
                    sheet.FreezePanes(1, 0, 1, dataTable.Columns.Count);
                }
            }

            if (hasData && workbook.Worksheets.Count > 0)
            {
                workbook.Save(filePath);
            }
            else
            {
                XtraMessageBox.Show("没有可导出的数据", "警告",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private string ParseFaultInformation(ulong rawValue, List<FaultSignals> faultTemplate)
        {
            StringBuilder faultDescription = new StringBuilder();
            string fault = "";

            if (rawValue != 0)
            {
                foreach (var faultSignal in faultTemplate)
                {
                    try
                    {
                        int bitValue = (int)((rawValue >> faultSignal.Startbit) & 0x01);
                        if (bitValue == 1)
                        {
                            string parts = faultSignal.Signalname.Remove(0, 3);
                            fault = fault + parts;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"解析故障位 {faultSignal.Signalname} 错误: {ex.Message}");
                    }
                }
            }
            else
            {
                fault = "正常";
            }

            return fault;
        }

        private FaultRecordingSignals CloneSignal(FaultRecordingSignals original)
        {
            return new FaultRecordingSignals
            {
                SignalName = original.SignalName,
                CANID = original.CANID,
                StartBit = original.StartBit,
                Length = original.Length,
                ByteOrder = original.ByteOrder,
                Signed = original.Signed,
                Factor = original.Factor,
                Offset = original.Offset,
                Unit = original.Unit
            };
        }

        private void BtnQuery_Click(object? sender, EventArgs e)
        {
            // 如果查询正在进行，则取消
            if (_queryWorker.IsBusy)
            {
                btnQuery.Text = "查询";
                _cancellationTokenSource?.Cancel();
                return;
            }

            // 准备新的查询
            btnQuery.Text = "取消";
            _cancellationTokenSource = new CancellationTokenSource();

            // 获取选中的设备
            string selectedDeviceNumber = comboBoxEdit.Text;
            EquipmentModel selectedEquipment = faultrecordingList
                .FirstOrDefault(e => e.DeviceName == selectedDeviceNumber);

            if (selectedEquipment == null)
            {
                XtraMessageBox.Show("未找到选中的设备", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 清空存储结构
            faultRecord.Clear();
            _channelTemplates.Clear();
            _channelRawData.Clear();
            _channelDataTables.Clear(); // 清除数据表缓存
            leftGridControl.DataSource = null;

            // 显示等待对话框
            ShowWaitDialog("查询中", "正在查询故障录波，请稍候...");

            // 开始后台查询
            _queryWorker.RunWorkerAsync(selectedEquipment);
        }

        private async void QueryWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = (BackgroundWorker)sender;
            EquipmentModel selectedEquipment = (EquipmentModel)e.Argument;
            CancellationToken cancellationToken = _cancellationTokenSource.Token;

            string deviceLogPrefix = $"{selectedEquipment.DeviceName}";

            try
            {
                // 初始进度报告
                //worker.ReportProgress(0, "开始查询故障录波数据...");

                int acnum = Convert.ToInt32(selectedEquipment.ACAddress.Substring(selectedEquipment.ACAddress.Length - 1));
                int dcnum = Convert.ToInt32(selectedEquipment.DCAddress.Substring(selectedEquipment.DCAddress.Length - 1));

                // 查询AC通道
                for (int i = 0; i < selectedEquipment.ACNumber; i++)
                {
                    bool allReceived = false;
                    cancellationToken.ThrowIfCancellationRequested();

                    acnum++;
                    worker.ReportProgress(0, $"正在查询通道AC{acnum}...");

                    string channelKey = $"AC{acnum}";
                    List<FaultRecordingSignals> acSignals = new List<FaultRecordingSignals>();

                    foreach (var faultTemplates in _faultTemplates)
                    {
                        if (faultTemplates.CANID.Contains("AX"))
                        {
                            var newSignal = CloneSignal(faultTemplates);
                            newSignal.CANID = faultTemplates.CANID.Replace("AX", "A" + (acnum - 1));
                            acSignals.Add(newSignal);
                        }
                    }
                    _channelTemplates[channelKey] = acSignals;

                    var uniqueCanIds = new HashSet<uint>();
                    foreach (var acSignal in acSignals)
                    {
                        if (uint.TryParse(acSignal.CANID, System.Globalization.NumberStyles.HexNumber, null, out uint canId))
                        {
                            uniqueCanIds.Add(canId);
                        }
                    }

                    List<uint> expectedCanIds = uniqueCanIds.ToList();

                    uint requestCanId = 0x30A000 + (uint)((acnum - 1) * 0x100) + 0xCC;
                    byte[] data = new byte[8];


                    string canChannelKey = CANManager.GetChannelKey(selectedEquipment.DeviceIndex, selectedEquipment.CanIndex);
                    CANManager.Instance.ClearQueue(channelKey);
                    CANManager.Instance.SendCommand(
                        selectedEquipment.DeviceIndex,
                        selectedEquipment.CanIndex,
                        requestCanId,
                        data
                    );

                    // 使用异步接收，支持取消
                    var receivedZcanFrames = await CANManager.Instance.ReceiveMultipleFramesAsync(
                        canChannelKey,
                        expectedCanIds,
                        15000
                    );

                    if (receivedZcanFrames.Count >= 2)
                    {
                        allReceived = true;
                    }

                    var byteFrames = new Dictionary<uint, List<byte[]>>();
                    foreach (var kv in receivedZcanFrames)
                    {
                        byteFrames[kv.Key] = kv.Value.Select(zcan => zcan.data).ToList();
                    }
                    _channelRawData[channelKey] = byteFrames;

                    LogService.Log($"通道 {channelKey} 接收帧数统计:");
                    foreach (var canId in expectedCanIds)
                    {
                        int count = receivedZcanFrames.TryGetValue(canId, out var frames) ? frames.Count : 0;
                        
                        LogService.Log($"  CAN ID: 0x{canId:X8}, 帧数: {count}");
                    }

                    faultRecord.Add(new FaultRecord
                    {
                        Passage = $"AC{acnum}",
                        State = allReceived ? "成功" : "失败"
                    });
                }

                // 查询DC通道
                for (int i = 0; i < selectedEquipment.DCNumber; i++)
                {
                    bool allReceived = false;

                    cancellationToken.ThrowIfCancellationRequested();

                    dcnum++;
                    worker.ReportProgress(0, $"正在查询通道DC{dcnum}...");

                    string channelKey = $"DC{dcnum}";
                    List<FaultRecordingSignals> dcSignals = new List<FaultRecordingSignals>();
                    foreach (var faultTemplates in _faultTemplates)
                    {
                        if (faultTemplates.CANID.Contains("2X"))
                        {
                            var newSignal = CloneSignal(faultTemplates);
                            newSignal.CANID = faultTemplates.CANID.Replace("2X", "2" + (dcnum - 1));
                            dcSignals.Add(newSignal);
                        }
                    }
                    _channelTemplates[channelKey] = dcSignals;

                    var uniqueCanIds = new HashSet<uint>();
                    foreach (var acSignal in dcSignals)
                    {
                        if (uint.TryParse(acSignal.CANID, NumberStyles.HexNumber, null, out uint canId))
                        {
                            uniqueCanIds.Add(canId);
                        }
                    }

                    List<uint> expectedCanIds = uniqueCanIds.ToList();

                    uint requestCanId = 0x302000 + (uint)((dcnum - 1) * 0x100) + 0xCC;
                    byte[] data = new byte[8];

                    string canChannelKey = CANManager.GetChannelKey(selectedEquipment.DeviceIndex, selectedEquipment.CanIndex);
                    CANManager.Instance.ClearQueue(channelKey);
                    CANManager.Instance.SendCommand(
                        selectedEquipment.DeviceIndex,
                        selectedEquipment.CanIndex,
                        requestCanId,
                        data
                    );

                    var receivedZcanFrames = await CANManager.Instance.ReceiveMultipleFramesAsync(
                        canChannelKey,
                        expectedCanIds,
                        15000
                    );

                    if (receivedZcanFrames.Values.Count >= 1)
                    {
                        allReceived = true;
                    }

                    var byteFrames = new Dictionary<uint, List<byte[]>>();

                    foreach (var kv in receivedZcanFrames)
                    {
                        byteFrames[kv.Key] = kv.Value.Select(zcan => zcan.data).ToList();
                    }
                    _channelRawData[channelKey] = byteFrames;

                    LogService.Log($"通道 {channelKey} 接收帧数统计:");
                    foreach (var canId in expectedCanIds)
                    {
                        int count = receivedZcanFrames.TryGetValue(canId, out var frames) ? frames.Count : 0;
                        
                        LogService.Log($"  CAN ID: 0x{canId:X8}, 帧数: {count}");
                    }

                    faultRecord.Add(new FaultRecord
                    {
                        Passage = $"DC{dcnum}",
                        State = allReceived ? "成功" : "失败"
                    });
                }

                LogService.Log($"{deviceLogPrefix}故障查询完成: " +
                             $"AC成功数: {faultRecord.Count(r => r.Passage.StartsWith("AC") && r.State == "成功")}, " +
                             $"DC成功数: {faultRecord.Count(r => r.Passage.StartsWith("DC") && r.State == "成功")}");

                e.Result = true;
            }
            catch (OperationCanceledException)
            {
                e.Cancel = true;
            }
            catch (Exception ex)
            {
                e.Result = ex;
            }
        }

        private void QueryWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.UserState != null)
            {
                UpdateWaitDialog(e.UserState.ToString());
            }
        }

        private void QueryWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                if (e.Cancelled)
                {
                    XtraMessageBox.Show("查询已取消", "信息",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (e.Error != null)
                {
                    LogService.Log($"查询失败: {e.Error.Message}");
                    XtraMessageBox.Show($"查询失败: {e.Error.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else if (e.Result is Exception ex)
                {
                    LogService.Log($"查询失败: {ex.Message}");
                    XtraMessageBox.Show($"查询失败: {ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    // 更新UI
                    leftGridControl.DataSource = faultRecord;
                    if (faultRecord.Count > 0)
                    {
                        leftGridView.FocusedRowHandle = 0;
                        LoadChannelData(faultRecord[0].Passage);
                    }
                }
            }
            finally
            {
                btnQuery.Text = "查询";
                CloseWaitDialog();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void ShowWaitDialog(string caption, string description)
        {
            CloseWaitDialog();
            waitDialog = new WaitDialogForm(caption, description);
        }

        private void UpdateWaitDialog(string description)
        {
            if (waitDialog != null && !waitDialog.IsDisposed)
            {
                CloseWaitDialog();
                waitDialog = new WaitDialogForm("查询中", description);
                Application.DoEvents();
            }
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

        public void ClearCache()
        {
            _channelRawData.Clear();
            _channelTemplates.Clear();
            _channelDataTables.Clear(); // 清除数据表缓存
            faultRecord.Clear();

            leftGridControl.DataSource = null;
            rightGridControl.DataSource = null;

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}