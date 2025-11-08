using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid.Views.Grid.ViewInfo;
using System.ComponentModel;
using System.Data;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class SignalSelectorForm : XtraForm
    {
        private List<Showdata> allSignals;
        private List<Showdata> currentSignals;
        private GridControl gridControl;
        private GridView gridView;
        public List<Showdata> SelectedSignals { get; private set; } = new List<Showdata>();

        // 添加自动滚动相关变量
        private System.Windows.Forms.Timer scrollTimer;
        private const int SCROLL_MARGIN = 30; // 滚动触发边界
        private const int SCROLL_STEP = 20;   // 每次滚动的像素数

        public SignalSelectorForm(List<Showdata> allSignals, List<Showdata> currentSignals)
        {
            this.allSignals = allSignals;
            this.currentSignals = currentSignals;
            InitializeComponent();
            InitializeUI();
            InitializeAutoScroll(); // 初始化自动滚动
            this.Load += SignalSelectorForm_Load;
        }

        private void InitializeAutoScroll()
        {
            scrollTimer = new System.Windows.Forms.Timer();
            scrollTimer.Interval = 50; // 50毫秒间隔
            scrollTimer.Tick += ScrollTimer_Tick;
        }

        private void ScrollTimer_Tick(object? sender, EventArgs e)
        {
            if (!gridControl.IsHandleCreated) return;

            Point clientPos = gridControl.PointToClient(Control.MousePosition);
            if (!gridControl.ClientRectangle.Contains(clientPos)) return;

            // 计算滚动方向和距离
            int scrollDelta = 0;
            if (clientPos.Y < SCROLL_MARGIN)
            {
                // 向上滚动
                scrollDelta = -Math.Min(SCROLL_STEP, SCROLL_MARGIN - clientPos.Y);
            }
            else if (clientPos.Y > gridControl.Height - SCROLL_MARGIN)
            {
                // 向下滚动
                scrollDelta = Math.Min(SCROLL_STEP, clientPos.Y - (gridControl.Height - SCROLL_MARGIN));
            }

            if (scrollDelta != 0)
            {
                // 执行滚动
                gridView.TopRowIndex = Math.Max(0, gridView.TopRowIndex + (scrollDelta > 0 ? 1 : -1));

                // 更新拖拽视觉效果
                GridHitInfo hitInfo = gridView.CalcHitInfo(clientPos);
                if (hitInfo.RowHandle != GridControl.InvalidRowHandle)
                {
                    gridView.FocusedRowHandle = hitInfo.RowHandle;
                }
            }
        }

        private void SignalSelectorForm_Load(object? sender, EventArgs e)
        {
            gridControl.DataSource = new BindingList<Showdata>(allSignals);
            SetDefaultSelections();
        }

        private void InitializeUI()
        {
            this.ClientSize = new Size(400, 600);
            this.Text = "数据选择";

            // 初始化网格
            gridControl = new GridControl();
            gridControl.Dock = DockStyle.Top;
            gridControl.Height = 540;
            gridView = new GridView();
            gridControl.MainView = gridView;
            gridView.OptionsView.ShowGroupPanel = false;
            // 启用默认复选框功能
            //gridView.OptionsSelection.MultiSelect = true;
            //gridView.OptionsSelection.MultiSelectMode = GridMultiSelectMode.CheckBoxRowSelect;
            // 关键配置 - 启用基础拖拽支持
            gridView.OptionsBehavior.Editable = true; // 必须启用编辑模式
            gridControl.AllowDrop = true; // 启用控件级拖放

            // 绑定拖拽事件
            gridView.MouseDown += GridView_MouseDown;
            gridView.MouseMove += GridView_MouseMove;
            gridControl.DragOver += GridControl_DragOver;
            gridControl.DragDrop += GridControl_DragDrop;

            // 添加列
            var gridcolumns = new[]
            {
                new GridColumn { FieldName = "IsSelected", Caption = "选择", Width = 50, Visible = true, ColumnEdit = new RepositoryItemCheckEdit()},
                new GridColumn { FieldName = "SystemName", Caption = "信号名称", Width = 300, Visible = true,OptionsColumn = { AllowEdit = false } },
                new GridColumn { FieldName = "Unit", Caption = "单位", Width = 80, Visible = true, OptionsColumn = { AllowEdit = false }}
            };
            gridView.Columns.AddRange(gridcolumns);

            this.Controls.Add(gridControl);

            // 在窗体添加全选按钮
            var btnSelectAll = new SimpleButton
            {
                Text = "全选",
                Width = 80,
                Height = 30,
                Location = new Point(10, gridControl.Bottom + 15)
            };

            var btnCounterSelection = new SimpleButton
            {
                Text = "反选",
                Width = 80,
                Height = 30,
                Location = new Point(btnSelectAll.Right + 20, gridControl.Bottom + 15)
            };

            var btnOK = new SimpleButton
            {
                Text = "确认",
                Width = 80,
                Height = 30,
                Location = new Point(btnCounterSelection.Right + 20, gridControl.Bottom + 15)
            };
            var btnCancel = new SimpleButton
            {
                Text = "取消",
                Width = 80,
                Height = 30,
                Location = new Point(btnOK.Right + 20, gridControl.Bottom + 15)
            };
            this.Controls.AddRange(new[] { btnSelectAll, btnCounterSelection, btnOK, btnCancel });

            //注册事件
            btnSelectAll.Click += BtnSelectAll_Click;
            btnCounterSelection.Click += BtnCounterSelection_Click;
            btnOK.Click += btnOK_Click;
            btnCancel.Click += btnCancel_Click;
            //gridView.CustomDrawColumnHeader += gridView_CustomDrawColumnHeader;
        }

        private void GridControl_QueryContinueDrag(object? sender, QueryContinueDragEventArgs e)
        {
            // 当拖拽结束时停止滚动计时器
            if (e.Action == DragAction.Cancel || e.Action == DragAction.Drop)
            {
                scrollTimer.Stop();
            }
        }

        private void BtnCounterSelection_Click(object? sender, EventArgs e)
        {
            if (gridControl.DataSource is BindingList<Showdata> src)
            {
                foreach (var item in src)
                {
                    item.IsSelected = !item.IsSelected; // 修正：应该是反选，不是全不选
                }
                gridView.RefreshData();
            }
        }

        private void BtnSelectAll_Click(object? sender, EventArgs e)
        {
            if (gridControl.DataSource is BindingList<Showdata> src)
            {
                foreach (var item in src)
                {
                    item.IsSelected = true;
                }
                gridView.RefreshData();
            }
        }

        // 鼠标按下时记录拖拽起始位置
        private int sourceRowHandle = GridControl.InvalidRowHandle;
        private Point mouseDownPoint;
        private void GridView_MouseDown(object? sender, MouseEventArgs e)
        {
            sourceRowHandle = gridView.CalcHitInfo(e.Location).RowHandle;
            mouseDownPoint = e.Location;
        }

        private void GridView_MouseMove(object? sender, MouseEventArgs e)
        {
            // 当满足以下条件时触发拖拽：
            // 1. 鼠标左键按下
            // 2. 已记录有效起始行
            // 3. 鼠标移动距离超过系统定义的拖拽阈值
            if (e.Button == MouseButtons.Left &&
                sourceRowHandle != GridControl.InvalidRowHandle &&
                IsDragThresholdExceeded(e.Location))
            {
                // 启动拖拽操作
                gridControl.DoDragDrop(gridView.GetRow(sourceRowHandle), DragDropEffects.Move);
            }
        }

        private bool IsDragThresholdExceeded(Point currentPosition)
        {
            // 系统默认拖拽阈值通常为4像素
            const int dragThreshold = 4;
            return Math.Abs(currentPosition.X - mouseDownPoint.X) > dragThreshold ||
                   Math.Abs(currentPosition.Y - mouseDownPoint.Y) > dragThreshold;
        }

        // 拖拽过程中更新视觉效果
        private void GridControl_DragOver(object? sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;

            Point dropPoint = gridControl.PointToClient(new Point(e.X, e.Y));

            // 检查是否需要启动自动滚动
            if (dropPoint.Y < SCROLL_MARGIN || dropPoint.Y > gridControl.Height - SCROLL_MARGIN)
            {
                if (!scrollTimer.Enabled)
                    scrollTimer.Start();
            }
            else
            {
                scrollTimer.Stop();
            }

            // 更新焦点行
            GridHitInfo hitInfo = gridView.CalcHitInfo(dropPoint);
            if (hitInfo.RowHandle != GridControl.InvalidRowHandle)
            {
                gridView.FocusedRowHandle = hitInfo.RowHandle;
            }
        }

        // 处理拖拽完成后的行交换
        private void GridControl_DragDrop(object? sender, DragEventArgs e)
        {
            // 拖拽结束时停止计时器
            scrollTimer.Stop();

            try
            {
                var draggedItem = e.Data.GetData(typeof(Showdata)) as Showdata;
                if (draggedItem == null) return;

                Point dropPoint = gridControl.PointToClient(new Point(e.X, e.Y));
                GridHitInfo hitInfo = gridView.CalcHitInfo(dropPoint);
                int targetRowHandle = hitInfo.RowHandle;

                if (sourceRowHandle != GridControl.InvalidRowHandle &&
                    targetRowHandle != GridControl.InvalidRowHandle &&
                    sourceRowHandle != targetRowHandle)
                {
                    var dataSource = gridControl.DataSource as BindingList<Showdata>;
                    if (dataSource == null) return;

                    int sourceIndex = gridView.GetDataSourceRowIndex(sourceRowHandle);
                    int targetIndex = gridView.GetDataSourceRowIndex(targetRowHandle);

                    dataSource.RemoveAt(sourceIndex);
                    dataSource.Insert(targetIndex, draggedItem);

                    gridView.FocusedRowHandle = targetRowHandle;
                }
            }
            finally
            {
                sourceRowHandle = GridControl.InvalidRowHandle;
                mouseDownPoint = Point.Empty;
            }
        }

        private void SetDefaultSelections()
        {
            if (gridControl.DataSource is not BindingList<Showdata> dataSource) return;

            foreach (var currentSignal in currentSignals)
            {
                var item = dataSource.FirstOrDefault(s => s.SystemName == currentSignal.SystemName);
                if (item != null)
                {
                    item.IsSelected = true; // 直接设置属性值
                }
            }
        }

        private void btnOK_Click(object? sender, EventArgs e)
        {
            SelectedSignals.Clear();

            // 直接从数据源获取选中项
            if (gridControl.DataSource is BindingList<Showdata> dataSource)
            {
                SelectedSignals.AddRange(dataSource.Where(item => item.IsSelected));
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnCancel_Click(object? sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            this.Close();
        }

        // 窗体关闭时释放资源
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            scrollTimer?.Stop();
            scrollTimer?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
