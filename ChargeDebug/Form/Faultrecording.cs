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
using Log;
using System.ComponentModel;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Text;

namespace ChargeDebug.Form
{
    public class FaultRecord
    {
        public string Passage { get; set; }
        public string State { get; set; }
    }

    public partial class Faultrecording : XtraUserControl
    {
        private GridControl leftGridControl;
        private GridView leftGridView;
        private GridControl rightGridControl;
        private GridView rightGridView;
        private ComboBoxEdit comboBoxEdit;
        private SimpleButton btnQuery;
        private SimpleButton btnExport;
        private WaitDialogForm waitDialog;

        private List<EquipmentModel> faultrecordingList;
        private List<FaultRecording> _faultTemplates = new List<FaultRecording>();
        private string dbcPath = "";
        private List<FaultSignals> acfaultTemplate;
        private List<FaultSignals> dcfaultTemplate;

        private List<FaultRecord> faultRecord = new List<FaultRecord>();

        private Dictionary<string, Dictionary<uint, List<byte[]>>> _channelRawData = new Dictionary<string, Dictionary<uint, List<byte[]>>>();
        private Dictionary<string, List<FaultRecording>> _channelTemplates = new Dictionary<string, List<FaultRecording>>();

        // 后台工作相关成员
        private BackgroundWorker _queryWorker;
        private CancellationTokenSource _cancellationTokenSource;

        public Faultrecording(string dbPath, List<EquipmentModel> equipmentList)
        {
            dbcPath = dbPath;
            faultrecordingList = equipmentList;
            InitializeComponent();
            LoadFaultTemplates();
            InitializeUI();
            InitializeBackgroundWorker();
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
            topPanel.Height = 50;
            topPanel.BorderStyle = BorderStyles.NoBorder;

            comboBoxEdit = new ComboBoxEdit();
            comboBoxEdit.Location = new Point(10, 10);
            comboBoxEdit.Size = new Size(130, 22);
            comboBoxEdit.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;

            GenerateChannelOptions();

            btnQuery = new SimpleButton();
            btnQuery.Location = new Point(150, 10);
            btnQuery.Size = new Size(75, 25);
            btnQuery.Text = "查询";
            btnQuery.Click += BtnQuery_Click;

            btnExport = new SimpleButton();
            btnExport.Location = new Point(235, 10);
            btnExport.Size = new Size(75, 25);
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
            topPanel.Controls.Add(btnExport);

            leftPanel.Controls.Add(leftGridControl);
            leftPanel.Controls.Add(topPanel);

            splitContainer.Panel1.Controls.Add(leftPanel);
            splitContainer.Panel2.Controls.Add(rightGridControl);

            this.Controls.Add(splitContainer);

            InitializeLeftGridColumns();
            InitializeRightGridColumns();
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
                    ExportToExcel(saveDialog.FileName);
                    XtraMessageBox.Show("导出成功！", "信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                ClearCache();
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

        private void ExportToExcel(string filePath)
        {
            Workbook workbook = new Workbook();
            workbook.Worksheets.Clear();

            var channels = leftGridControl.DataSource as List<FaultRecord>;
            if (channels == null || channels.Count == 0) return;

            Style defaultStyle = workbook.CreateStyle();
            defaultStyle.Number = 0;
            defaultStyle.HorizontalAlignment = TextAlignmentType.Center;
            defaultStyle.VerticalAlignment = TextAlignmentType.Center;
            defaultStyle.Font.Name = "宋体";
            defaultStyle.Font.Size = 14;

            Style headerStyle = workbook.CreateStyle();
            headerStyle.Pattern = BackgroundType.Solid;
            headerStyle.ForegroundColor = Color.LightGray;
            headerStyle.Font.IsBold = true;
            headerStyle.HorizontalAlignment = TextAlignmentType.Center;
            headerStyle.VerticalAlignment = TextAlignmentType.Center;
            headerStyle.Font.Name = "宋体";
            headerStyle.Font.Size = 14;
            headerStyle.Font.IsBold = true;

            foreach (var channel in channels)
            {
                DataTable dt = GenerateDataTableForChannel(channel.Passage);
                if (dt == null) continue;

                Worksheet sheet = workbook.Worksheets.Add(channel.Passage);
                sheet.Cells.StandardHeight = 20;
                sheet.Cells.SetRowHeight(0, 25);

                for (int col = 0; col < dt.Columns.Count; col++)
                {
                    Cell headerCell = sheet.Cells[0, col];
                    headerCell.PutValue(dt.Columns[col].ColumnName);
                    headerCell.SetStyle(headerStyle);
                }

                for (int row = 0; row < dt.Rows.Count; row++)
                {
                    sheet.Cells.SetRowHeight(row + 1, 20);
                    for (int col = 0; col < dt.Columns.Count; col++)
                    {
                        Cell cell = sheet.Cells[row + 1, col];
                        object value = dt.Rows[row][col];

                        if (value is double || value is float || value is decimal)
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

                sheet.AutoFitColumns();
                sheet.FreezePanes(1, 0, 1, dt.Columns.Count);
            }

            if (workbook.Worksheets.Count > 0)
            {
                workbook.Save(filePath);
            }
        }

        private DataTable GenerateDataTableForChannel(string passage)
        {
            if (!_channelTemplates.TryGetValue(passage, out var templates) ||
                !_channelRawData.TryGetValue(passage, out var channelData))
            {
                return null;
            }

            DataTable dataTable = new DataTable();
            dataTable.Columns.Add("序号", typeof(int));

            foreach (var signal in templates)
            {
                if (signal.SignalName != "序号")
                {
                    if (signal.SignalName == "故障信息")
                    {
                        dataTable.Columns.Add(signal.SignalName, typeof(string));
                    }
                    else
                    {
                        dataTable.Columns.Add(signal.SignalName, typeof(double));
                    }
                }
            }

            Dictionary<int, Dictionary<uint, byte[]>> indexedFrames = new Dictionary<int, Dictionary<uint, byte[]>>();

            foreach (var kv in channelData)
            {
                uint canId = kv.Key;
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

            for (int i = 1; i <= 1000; i++)
            {
                DataRow row = dataTable.NewRow();
                row["序号"] = i;

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

                                    if (signal.SignalName == "故障信息")
                                    {
                                        row[signal.SignalName] = ParseFaultInformation(
                                            rawValue,
                                            passage.StartsWith("AC") ? acfaultTemplate : dcfaultTemplate
                                        );
                                    }
                                    else
                                    {
                                        row[signal.SignalName] = physicalValue;
                                    }
                                }
                                else
                                {
                                    row[signal.SignalName] = DBNull.Value;
                                }
                            }
                            else
                            {
                                row[signal.SignalName] = DBNull.Value;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"解析信号 {signal.SignalName} 错误: {ex.Message}");
                            row[signal.SignalName] = DBNull.Value;
                        }
                    }
                }
                else
                {
                    foreach (var signal in templates)
                    {
                        if (signal.SignalName != "序号")
                        {
                            row[signal.SignalName] = DBNull.Value;
                        }
                    }
                }
                dataTable.Rows.Add(row);
            }

            return dataTable;
        }

        private void GenerateChannelOptions()
        {
            foreach (var item in faultrecordingList)
            {
                if (item != null)
                    comboBoxEdit.Properties.Items.Add($"{item.DeviceNumber}");
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

        private void CreateRightGridColumns(List<FaultRecording> signals)
        {
            rightGridView.Columns.Clear();

            rightGridView.Appearance.HeaderPanel.TextOptions.WordWrap = WordWrap.NoWrap;
            rightGridView.Appearance.HeaderPanel.TextOptions.HAlignment = HorzAlignment.Center;

            rightGridView.OptionsView.ColumnAutoWidth = false;
            rightGridView.HorzScrollVisibility = ScrollVisibility.Always;

            float dpiScaleFactor = GetDpiScaleFactor();

            rightGridView.Columns.Add(new GridColumn
            {
                Caption = "序号",
                FieldName = "序号",
                Visible = true
            });

            foreach (var signal in signals)
            {
                if (signal.SignalName != "序号")
                {
                    string caption = $"{signal.SignalName}";
                    if (!string.IsNullOrEmpty(signal.Unit))
                    {
                        caption += $" ({signal.Unit})";
                    }

                    int textWidth = TextRenderer.MeasureText(caption, rightGridView.Appearance.HeaderPanel.Font).Width;
                    int scaledWidth = (int)(textWidth * dpiScaleFactor) + 5;
                    int minWidth = 50;
                    int maxWidth = 300;
                    int finalWidth = Math.Max(minWidth, Math.Min(maxWidth, scaledWidth));

                    GridColumn column = new GridColumn
                    {
                        Caption = caption,
                        FieldName = signal.SignalName,
                        Visible = true,
                        Width = finalWidth,
                        MinWidth = minWidth,
                        OptionsColumn = { AllowEdit = false }
                    };

                    column.AppearanceHeader.TextOptions.HAlignment = HorzAlignment.Center;
                    column.AppearanceHeader.TextOptions.WordWrap = WordWrap.NoWrap;

                    rightGridView.Columns.Add(column);
                }
            }
            rightGridView.OptionsView.ColumnAutoWidth = false;
            rightGridView.HorzScrollVisibility = ScrollVisibility.Always;
        }

        private float GetDpiScaleFactor()
        {
            using (Graphics g = this.CreateGraphics())
            {
                return g.DpiX / 96f;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            leftGridView.RowClick += LeftGridView_RowClick;
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

            DataTable dataTable = new DataTable();
            dataTable.Columns.Add("序号", typeof(int));

            foreach (var signal in templates)
            {
                if (signal.SignalName != "序号")
                {
                    if (signal.SignalName == "故障信息")
                    {
                        dataTable.Columns.Add(signal.SignalName, typeof(string));
                    }
                    else
                    {
                        dataTable.Columns.Add(signal.SignalName, typeof(double));
                    }
                }
            }

            Dictionary<int, Dictionary<uint, byte[]>> indexedFrames = new Dictionary<int, Dictionary<uint, byte[]>>();

            foreach (var kv in channelData)
            {
                uint canId = kv.Key;
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

            for (int i = 1; i <= 1000; i++)
            {
                DataRow row = dataTable.NewRow();
                row["序号"] = i;

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

                                    if (signal.SignalName == "故障信息")
                                    {
                                        row[signal.SignalName] = ParseFaultInformation(
                                            rawValue,
                                            passage.StartsWith("AC") ? acfaultTemplate : dcfaultTemplate
                                        );
                                    }
                                    else
                                    {
                                        row[signal.SignalName] = physicalValue;
                                    }
                                }
                                else
                                {
                                    row[signal.SignalName] = DBNull.Value;
                                }
                            }
                            else
                            {
                                row[signal.SignalName] = DBNull.Value;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"解析信号 {signal.SignalName} 错误: {ex.Message}");
                            row[signal.SignalName] = DBNull.Value;
                        }
                    }
                }
                else
                {
                    foreach (var signal in templates)
                    {
                        if (signal.SignalName != "序号")
                        {
                            row[signal.SignalName] = DBNull.Value;
                        }
                    }
                }
                dataTable.Rows.Add(row);
            }
            rightGridControl.DataSource = dataTable;
        }

        private string ParseFaultInformation(ulong rawValue, List<FaultSignals> faultTemplate)
        {
            StringBuilder faultDescription = new StringBuilder();
            string fault = "";

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

            return fault;
        }

        private FaultRecording CloneSignal(FaultRecording original)
        {
            return new FaultRecording
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
                .FirstOrDefault(e => e.DeviceNumber == selectedDeviceNumber);

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

            string deviceLogPrefix = $"{selectedEquipment.DeviceNumber}";

            try
            {
                // 查询AC通道
                for (int i = 0; i < selectedEquipment.ACNumber; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    worker.ReportProgress(0, $"正在查询通道AC{i + 1}...");
                    string channelKey = $"AC{i + 1}";
                    List<FaultRecording> acSignals = new List<FaultRecording>();

                    foreach (var faultTemplates in _faultTemplates)
                    {
                        if (faultTemplates.CANID.Contains("AX"))
                        {
                            var newSignal = CloneSignal(faultTemplates);
                            newSignal.CANID = faultTemplates.CANID.Replace("AX", "A" + i);
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

                    uint requestCanId = 0x30A000 + (uint)(i * 0x100) + 0xCC;
                    byte[] data = new byte[8];

                    CANManager.Instance.ClearQueue(channelKey);
                    CANManager.Instance.SendCommand(
                        selectedEquipment.DeviceIndex,
                        selectedEquipment.CanIndex,
                        requestCanId,
                        data
                    );

                    string canChannelKey = CANManager.GetChannelKey(selectedEquipment.DeviceIndex, selectedEquipment.CanIndex);

                    // 使用异步接收，支持取消
                    var receivedZcanFrames = await CANManager.Instance.ReceiveMultipleFramesAsync(
                        canChannelKey,
                        expectedCanIds,
                        15000
                    );

                    var byteFrames = new Dictionary<uint, List<byte[]>>();
                    foreach (var kv in receivedZcanFrames)
                    {
                        byteFrames[kv.Key] = kv.Value.Select(zcan => zcan.data).ToList();
                    }
                    _channelRawData[channelKey] = byteFrames;

                    bool allReceived = true;
                    LogService.Log($"通道 {channelKey} 接收帧数统计:");
                    foreach (var canId in expectedCanIds)
                    {
                        int count = receivedZcanFrames.TryGetValue(canId, out var frames) ? frames.Count : 0;
                        if (count == 0)
                        {
                            allReceived = false;
                        }
                        LogService.Log($"  CAN ID: 0x{canId:X8}, 帧数: {count}");
                    }

                    faultRecord.Add(new FaultRecord
                    {
                        Passage = $"AC{i + 1}",
                        State = allReceived ? "成功" : "失败"
                    });

                    // 报告进度
                    int progress = (int)((i + 1) * 100.0 / (selectedEquipment.ACNumber + selectedEquipment.DCNumber));
                    worker.ReportProgress(progress);
                }

                // 查询DC通道
                for (int i = 0; i < selectedEquipment.DCNumber; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    worker.ReportProgress(0, $"正在查询通道DC{i + 1}...");
                    string channelKey = $"DC{i + 1}";
                    List<FaultRecording> dcSignals = new List<FaultRecording>();
                    foreach (var faultTemplates in _faultTemplates)
                    {
                        if (faultTemplates.CANID.Contains("2X"))
                        {
                            var newSignal = CloneSignal(faultTemplates);
                            newSignal.CANID = faultTemplates.CANID.Replace("2X", "2" + i);
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

                    uint requestCanId = 0x302000 + (uint)(i * 0x100) + 0xCC;
                    byte[] data = new byte[8];

                    CANManager.Instance.ClearQueue(channelKey);
                    CANManager.Instance.SendCommand(
                        selectedEquipment.DeviceIndex,
                        selectedEquipment.CanIndex,
                        requestCanId,
                        data
                    );

                    string canChannelKey = CANManager.GetChannelKey(selectedEquipment.DeviceIndex, selectedEquipment.CanIndex);
                    var receivedZcanFrames = await CANManager.Instance.ReceiveMultipleFramesAsync(
                        canChannelKey,
                        expectedCanIds,
                        15000
                    );

                    var byteFrames = new Dictionary<uint, List<byte[]>>();
                    
                    foreach (var kv in receivedZcanFrames)
                    {
                        byteFrames[kv.Key] = kv.Value.Select(zcan => zcan.data).ToList();
                    }
                    _channelRawData[channelKey] = byteFrames;

                    bool allReceived = true;
                    LogService.Log($"通道 {channelKey} 接收帧数统计:");
                    foreach (var canId in expectedCanIds)
                    {
                        int count = receivedZcanFrames.TryGetValue(canId, out var frames) ? frames.Count : 0;
                        if (count == 0)
                        {
                            allReceived = false;
                        }
                        LogService.Log($"  CAN ID: 0x{canId:X8}, 帧数: {count}");
                    }

                    faultRecord.Add(new FaultRecord
                    {
                        Passage = $"DC{i + 1}",
                        State = allReceived ? "成功" : "失败"
                    });

                    // 报告进度
                    int progress = (int)((selectedEquipment.ACNumber + i + 1) * 100.0 /
                                        (selectedEquipment.ACNumber + selectedEquipment.DCNumber));
                    worker.ReportProgress(progress);
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
            faultRecord.Clear();

            leftGridControl.DataSource = null;
            rightGridControl.DataSource = null;

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}