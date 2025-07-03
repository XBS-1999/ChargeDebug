using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraExport.Helpers;
using DevExpress.XtraVerticalGrid;
using System.ComponentModel;
using DevExpress.XtraGrid.Views.Grid.ViewInfo;
using DevExpress.XtraRichEdit;
using System.Data;
using DevExpress.XtraEditors.Repository;

namespace ChargeDebug.Form
{
    public partial class SignalSelectorForm : XtraForm
    {
        private List<Showdata> allSignals;
        private List<Showdata> currentSignals;
        private GridControl gridControl;
        private GridView gridView;
        public List<Showdata> SelectedSignals { get; private set; } = new List<Showdata>();

        public SignalSelectorForm(List<Showdata> allSignals, List<Showdata> currentSignals)
        {
            this.allSignals = allSignals;
            this.currentSignals = currentSignals;
            InitializeComponent();
            InitializeUI();
            this.Load += SignalSelectorForm_Load;
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
                Location = new Point(btnOK.Right + 20 , gridControl.Bottom + 15)
            };
            this.Controls.AddRange(new[] { btnSelectAll,btnCounterSelection, btnOK, btnCancel });

            //注册事件
            btnSelectAll.Click += BtnSelectAll_Click;
            btnCounterSelection.Click += BtnCounterSelection_Click;
            btnOK.Click += btnOK_Click;
            btnCancel.Click += btnCancel_Click;
            //gridView.CustomDrawColumnHeader += gridView_CustomDrawColumnHeader;
        }

        private void BtnCounterSelection_Click(object? sender, EventArgs e)
        {
            if (gridControl.DataSource is BindingList<Showdata> src)
            {
                foreach (var item in src)
                {
                    item.IsSelected = false;
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
            // 显示移动光标
            e.Effect = DragDropEffects.Move;

            // 可选：高亮目标行
            Point dropPoint = gridControl.PointToClient(new Point(e.X, e.Y));
            GridHitInfo hitInfo = gridView.CalcHitInfo(dropPoint);
            if (hitInfo.RowHandle != GridControl.InvalidRowHandle)
            {
                gridView.FocusedRowHandle = hitInfo.RowHandle;
            }

        }

        // 处理拖拽完成后的行交换
        private void GridControl_DragDrop(object? sender, DragEventArgs e)
        {
            try
            {
                // 获取拖拽数据
                var draggedItem = e.Data.GetData(typeof(Showdata)) as Showdata;
                if (draggedItem == null) return;

                // 获取目标位置
                Point dropPoint = gridControl.PointToClient(new Point(e.X, e.Y));
                GridHitInfo hitInfo = gridView.CalcHitInfo(dropPoint);
                int targetRowHandle = hitInfo.RowHandle;

                // 执行数据交换
                if (sourceRowHandle != GridControl.InvalidRowHandle &&
                    targetRowHandle != GridControl.InvalidRowHandle &&
                    sourceRowHandle != targetRowHandle)
                {
                    var dataSource = gridControl.DataSource as BindingList<Showdata>;
                    if (dataSource == null) return;

                    // 计算实际数据索引
                    int sourceIndex = gridView.GetDataSourceRowIndex(sourceRowHandle);
                    int targetIndex = gridView.GetDataSourceRowIndex(targetRowHandle);

                    // 执行数据移动
                    dataSource.RemoveAt(sourceIndex);
                    dataSource.Insert(targetIndex, draggedItem);

                    // 刷新界面焦点
                    gridView.FocusedRowHandle = targetRowHandle;
                }
            }
            finally
            {
                // 重置状态
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
    }
}
