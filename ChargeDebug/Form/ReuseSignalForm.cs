using DevExpress.XtraEditors;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using System.ComponentModel;
using System.Text.RegularExpressions;
using ChargeDebug.Service;
using DataModel;

namespace ChargeDebug.Form
{
    public partial class ReuseSignalForm : XtraForm
    {
        public BindingList<ReuseSignal> reuseSignals { get; private set; }
        private GridControl gridControl;
        private int currentHexValue = 0;
        private int currentOrder = 1; // 当前顺序值
        public ReuseSignalForm(BindingList<ReuseSignal> signals)
        {
            currentHexValue = 0;
            reuseSignals = new BindingList<ReuseSignal>(signals.ToList());
            // 初始化当前顺序值
            if (signals.Count > 0)
            {
                currentOrder = signals.Max(s => s.Orders) + 1;
            }
            else
            {
                currentOrder = 1;
            }

            InitializeComponent();
            InitializeUI();
        }

        private void InitializeUI()
        {
            // 窗体布局和控件初始化
            this.Text = "复用信号配置";
            this.Size = new Size(600, 500);

            //创建GridControl
            gridControl = new GridControl();
            gridControl.Dock = DockStyle.Top;
            gridControl.Height = 400;

            //绑定数据源
            gridControl.DataSource = reuseSignals;

            GridView gridView = new GridView();
            gridControl.MainView = gridView;
            gridView.OptionsView.ShowGroupPanel = false;

            // 添加列
            var gridcolumns = new[]
            {
                new GridColumn { FieldName = "Value", Caption = "值", Width = 100, Visible = true,OptionsColumn = {AllowEdit = true}},
                new GridColumn { FieldName = "Description", Caption = "描述", Width = 200, Visible = true,OptionsColumn = {AllowEdit = true}},
                new GridColumn { FieldName = "Orders", Caption = "顺序", Width = 20, Visible = false,OptionsColumn = {AllowEdit = true}}
            };
            gridView.Columns.AddRange(gridcolumns);

            // 添加确定/取消按钮
            SimpleButton btnAdd = new SimpleButton
            {
                Text = "添加",
                Size = new Size(80,30),
                Location = new Point(35, 420)
            };

            SimpleButton btnDelete = new SimpleButton
            {
                Text = "删除",
                Size = new Size(80, 30),
                Location = new Point(btnAdd.Right + 10, 420)
            };

            SimpleButton btnOK = new SimpleButton
            {
                Text = "确定",
                Size = new Size(80, 30),
                Location = new Point(btnDelete.Right + 10, 420)
            };
            SimpleButton btnCancel = new SimpleButton
            {
                Text = "取消",
                Size = new Size(80, 30),
                Location = new Point(btnOK.Right + 10, 420)
            };
            SimpleButton btnUp = new SimpleButton
            {
                Text = "上移",
                Size = new Size(80, 30),
                Location = new Point(btnCancel.Right + 10, 420)
            };
            SimpleButton btnUnder = new SimpleButton
            {
                Text = "下移",
                Size = new Size(80, 30),
                Location = new Point(btnUp.Right + 10, 420)
            };

            this.Controls.AddRange(new Control[]
            {
                gridControl,btnAdd,btnDelete,
                btnOK,btnCancel,btnUp,btnUnder
            });

            btnOK.Click += BtnOK_Click;
            btnCancel.Click += BtnCancel_Click;
            btnAdd.Click += BtnAdd_Click;
            btnDelete.Click += BtnDelete_Click;
            btnUp.Click += BtnUp_Click;
            btnUnder.Click += BtnUnder_Click;
        }

        private void BtnUp_Click(object? sender, EventArgs e)
        {
            GridView view = gridControl.MainView as GridView;
            if (view == null) return;

            int rowHandle = view.FocusedRowHandle;
            if (rowHandle < 0) return; // 没有选中的行

            int dataSourceIndex = view.GetDataSourceRowIndex(rowHandle);
            if (dataSourceIndex <= 0) return; // 已经是第一行

            // 交换当前行与上一行
            ReuseSignal currentItem = reuseSignals[dataSourceIndex];
            ReuseSignal prevItem = reuseSignals[dataSourceIndex - 1];

            // 修复：直接交换整数值（不使用 HasValue）
            int tempOrder = currentItem.Orders;
            currentItem.Orders = prevItem.Orders;
            prevItem.Orders = tempOrder;

            // 在数据源中交换位置
            reuseSignals[dataSourceIndex] = prevItem;
            reuseSignals[dataSourceIndex - 1] = currentItem;

            // 刷新Grid并保持选中状态
            gridControl.RefreshDataSource();
            view.FocusedRowHandle = view.GetRowHandle(dataSourceIndex - 1);
        }

        private void BtnUnder_Click(object? sender, EventArgs e)
        {
            GridView view = gridControl.MainView as GridView;
            if (view == null) return;

            int rowHandle = view.FocusedRowHandle;
            if (rowHandle < 0) return; // 没有选中的行

            int dataSourceIndex = view.GetDataSourceRowIndex(rowHandle);
            if (dataSourceIndex >= reuseSignals.Count - 1) return; // 已经是最后一行

            // 交换当前行与下一行
            ReuseSignal currentItem = reuseSignals[dataSourceIndex];
            ReuseSignal nextItem = reuseSignals[dataSourceIndex + 1];

            // 修复：直接交换整数值（不使用 HasValue）
            int tempOrder = currentItem.Orders;
            currentItem.Orders = nextItem.Orders;
            nextItem.Orders = tempOrder;

            // 在数据源中交换位置
            reuseSignals[dataSourceIndex] = nextItem;
            reuseSignals[dataSourceIndex + 1] = currentItem;

            // 刷新Grid并保持选中状态
            gridControl.RefreshDataSource();
            view.FocusedRowHandle = view.GetRowHandle(dataSourceIndex + 1);
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            this.Close(); 
        }

        private void BtnOK_Click(object? sender, EventArgs e)
        {
            // 验证数据格式
            foreach (var signal in reuseSignals)
            {
                if (!IsValidHex(signal.Value))
                {
                    XtraMessageBox.Show($"无效的十六进制值：{signal.Value}");
                    return;
                }
            }

            // 检查重复值
            var duplicates = reuseSignals
                .GroupBy(s => s.Value)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);


            if (duplicates.Any())
            {
                XtraMessageBox.Show($"存在重复值：{string.Join(",", duplicates)}");
                return;
            }

            this.DialogResult = DialogResult.OK;
        }

        private bool IsValidHex(string input)
        {
            return Regex.IsMatch(input, @"^0x[0-9A-Fa-f]+$");
        }

        private void BtnDelete_Click(object? sender, EventArgs e)
        {
            GridView view = gridControl.MainView as GridView;
            if (view == null) return;

            int rowHandle = view.FocusedRowHandle;

            // 验证行索引有效性
            if (rowHandle < 0) return;

            // 获取数据源索引并删除
            int dataSourceIndex = view.GetDataSourceRowIndex(rowHandle);
            if (dataSourceIndex >= 0 && dataSourceIndex < reuseSignals.Count)
            {
                reuseSignals.RemoveAt(dataSourceIndex);
            }
        }

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            string hexValue = $"0x{currentHexValue:X2}";
            reuseSignals.Add(new ReuseSignal
            {
                Value = hexValue,
                Description = "",
                Orders = currentOrder++ // 设置顺序值
            });
            currentHexValue++;
        }
    }
}
