using DataModel;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.Utils;
using System.ComponentModel;

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
        private SimpleButton btnExport;
        private WaitDialogForm waitDialog;

        private string dbcPath = "";
        private List<EquipmentModel> faultrecordingList;
        private List<FaultRecord> faultRecord = new List<FaultRecord>();

        private BackgroundWorker _queryWorker;
        private CancellationTokenSource _cancellationTokenSource;

        public FaultRecording(string dbPath, List<EquipmentModel> equipmentList)
        {
            dbcPath = dbPath;
            faultrecordingList = equipmentList;

            InitializeComponent();
            InitializeUI();
            InitializeComponent();
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
            //btnExport.Click += BtnExport_Click;

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
            //InitializeRightGridColumns();
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

            // 显示等待对话框
            ShowWaitDialog("查询中", "正在查询故障录波，请稍候...");

            // 开始后台查询
            _queryWorker.RunWorkerAsync(selectedEquipment);
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

        public void UpdateDcNumber(List<EquipmentModel> equipmentList)
        {
            //DeviceConfig(equipmentList);
            //// 正确释放现有控件
            //CleanupExistingControls();

            //InitializeUI();       // 重新生成界面
        }
    }
}
