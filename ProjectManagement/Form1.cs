using DevExpress.XtraEditors;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid;
using DevExpress.XtraLayout;
using DevExpress.Utils;
using DevExpress.XtraLayout.Utils;
using DevExpress.Utils.Layout;
using DevExpress.XtraRichEdit.Model;
using DevExpress.XtraEditors.Controls;

namespace ProjectManagement
{
    public partial class ProjectManagement : XtraForm
    {
        private GridControl gridControl;
        private GridView gridView;
        private SimpleButton btnNew;
        private SimpleButton btnEdit;
        private SimpleButton btnDelete;

        public ProjectManagement()
        {
            InitializeComponent();
            InitializeUI();
        }

        private void InitializeUI()
        {
            // 创建主布局控件
            LayoutControl layoutControl = new LayoutControl
            {
                Dock = DockStyle.Fill,
                Parent = this,
                //Appearance = { BackColor = Color.White }
            };

            // 创建根布局组（水平排列）
            LayoutControlGroup rootGroup = new LayoutControlGroup
            {
                TextVisible = false,
                DefaultLayoutType = LayoutType.Horizontal,
                GroupBordersVisible = false,
                Padding = new DevExpress.XtraLayout.Utils.Padding(0)
            };
            layoutControl.Root = rootGroup;

            // === 左侧网格区域 ===
            LayoutControlItem gridItem = new LayoutControlItem();
            gridControl = new GridControl
            {
                Name = "projectGrid",
                Margin = new System.Windows.Forms.Padding(5),
                Location = new Point(0, 0),
                //Appearance = { BackColor = Color.White }
            };

            gridView = new GridView(gridControl);
            gridControl.MainView = gridView;

            // 网格视图设置
            gridView.OptionsView.ShowGroupPanel = false;
            gridView.OptionsView.ShowVerticalLines = DefaultBoolean.False;
            gridView.OptionsView.ShowHorizontalLines = DefaultBoolean.True;
            gridView.OptionsView.EnableAppearanceEvenRow = true;
            gridView.OptionsView.EnableAppearanceOddRow = true;
            gridView.Appearance.EvenRow.BackColor = ColorTranslator.FromHtml("#F9F9F9");
            gridView.Appearance.OddRow.BackColor = Color.White;
            gridView.Appearance.Row.BackColor2 = Color.White;
            gridView.Appearance.HeaderPanel.BackColor = ColorTranslator.FromHtml("#F0F5FF");
            gridView.Appearance.HeaderPanel.Font = new Font("Tahoma", 12, FontStyle.Bold);
            gridView.Appearance.HeaderPanel.Options.UseBackColor = true;
            gridView.Appearance.HeaderPanel.Options.UseFont = true;
            gridView.Appearance.HeaderPanel.TextOptions.HAlignment = HorzAlignment.Center;
            gridView.Appearance.Row.TextOptions.HAlignment = HorzAlignment.Center;
            gridView.OptionsSelection.MultiSelect = true;
            gridView.OptionsSelection.MultiSelectMode = GridMultiSelectMode.RowSelect;
            gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
            gridView.FocusRectStyle = DrawFocusRectStyle.RowFocus;

            // 添加列
            var columns = new[]
            {
                new GridColumn{FieldName = "序号",Caption = "序号",Visible = true,Width = 80,OptionsColumn = { AllowEdit = false }},
                new GridColumn{FieldName = "项目名称",Caption = "项目名称",Visible = true,Width = 180,OptionsColumn = { AllowEdit = false }},
                new GridColumn{FieldName = "电池包名称",Caption = "电池包名称",Visible = true,Width = 150,OptionsColumn = { AllowEdit = false }},
                new GridColumn{FieldName = "电池包特征码",Caption = "电池包特征码",Visible = true,Width = 200,OptionsColumn = { AllowEdit = false }},
                new GridColumn{FieldName = "通讯方式",Caption = "通讯方式",Visible = true,Width = 120,OptionsColumn = { AllowEdit = false }},
                new GridColumn{FieldName = "创建时间",Caption = "创建时间",Visible = true,Width = 180,OptionsColumn = { AllowEdit = false }}
            };

            gridView.Columns.AddRange(columns);

            gridItem.Control = gridControl;
            gridItem.TextVisible = false;
            gridItem.Padding = new DevExpress.XtraLayout.Utils.Padding(5);
            gridItem.SizeConstraintsType = SizeConstraintsType.Custom;
            //gridItem.MinSize = new Size(700, 0); // 最小宽度
            rootGroup.AddItem(gridItem);

            // === 右侧按钮区域 ===
            LayoutControlGroup buttonGroup = new LayoutControlGroup
            {
                GroupBordersVisible = false,
                TextVisible = false,
                DefaultLayoutType = LayoutType.Vertical,
                Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 0),
                //SizeConstraintsType = SizeConstraintsType.Custom,
                //MinSize = new Size(150, 0)
            };
            rootGroup.AddItem(buttonGroup);

            // 创建按钮容器（垂直栈面板）
            StackPanel buttonPanel = new StackPanel
            {
                AutoSize = true,
                LayoutDirection = StackPanelLayoutDirection.TopDown,
                Padding = new System.Windows.Forms.Padding(0, 40, 0, 0),
                AutoSizeMode = AutoSizeMode.GrowOnly,
                //Spacing = 20 // 按钮间距
            };

            // 创建按钮 - 现代扁平化风格
            btnNew = CreateModernButton("新建项目", ColorTranslator.FromHtml("#4CAF50")); // Material Green
            btnEdit = CreateModernButton("编辑项目", ColorTranslator.FromHtml("#2196F3")); // Material Blue
            btnDelete = CreateModernButton("删除项目", ColorTranslator.FromHtml("#F44336")); // Material Red

            // 添加按钮到面板
            buttonPanel.Controls.Add(btnNew);
            buttonPanel.Controls.Add(btnEdit);
            buttonPanel.Controls.Add(btnDelete);

            // 添加按钮面板到布局项
            LayoutControlItem buttonItem = new LayoutControlItem
            {
                Control = buttonPanel,
                TextVisible = false,
                SizeConstraintsType = SizeConstraintsType.Custom,
                Padding = new DevExpress.XtraLayout.Utils.Padding(5),
                ControlMinSize = new Size(120, 120)
            };

            buttonGroup.AddItem(buttonItem);
        }

        private SimpleButton CreateModernButton(string text, Color baseColor)
        {
            SimpleButton btn = new SimpleButton
            {
                Text = text,
                Size = new Size(100, 30),
                Margin = new System.Windows.Forms.Padding(0,0,0,20),
                Appearance =
                {
                    Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                    BackColor = baseColor,
                    ForeColor = Color.White,
                    BorderColor = baseColor,
                    Options = {
                        UseBackColor = true,
                        UseForeColor = true,
                        UseBorderColor = true
                    }
                },
                ButtonStyle = BorderStyles.Flat,
                //CornerRadius = 21, // 圆形按钮
                ShowFocusRectangle = DefaultBoolean.False
            };

            // 悬停效果
            btn.MouseEnter += (s, e) =>
            {
                btn.Appearance.BackColor = ControlPaint.Light(baseColor, 0.1f);
            };

            btn.MouseLeave += (s, e) =>
            {
                btn.Appearance.BackColor = baseColor;
            };

            // 按下效果
            btn.MouseDown += (s, e) =>
            {
                btn.Appearance.BackColor = ControlPaint.Dark(baseColor, 0.2f);
            };

            btn.MouseUp += (s, e) =>
            {
                btn.Appearance.BackColor = ControlPaint.Light(baseColor, 0.1f);
            };

            return btn;
        }
    }
}