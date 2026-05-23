using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using System.Drawing;
using System.Windows.Forms;

namespace FaultDiagnosis
{
    public partial class MainForm : XtraForm
    {
        // 左侧控件
        private MemoEdit memoFaultInput;
        private SimpleButton btnDiagnose;
        private MemoEdit memoResult;

        // 右侧控件
        private GridControl gridControl1;
        private GridView gridView1;

        public MainForm()
        {
            InitializeComponent();
            CreateUI();
            InitGrid();
        }

        private void CreateUI()
        {
            // 窗体设置
            Text = "大功率充放电故障诊断系统 · AI自动学习版";
            Size = new Size(1200, 750);
            StartPosition = FormStartPosition.CenterScreen;
            LookAndFeel.SetSkinStyle("DevExpress Style");

            // 顶部标题
            LabelControl lblTitle = new LabelControl();
            lblTitle.Text = "充放电设备故障诊断 · 输入现象 → AI排查 → 自动学习";
            lblTitle.Location = new Point(10, 10);
            lblTitle.Width = 1160;
            lblTitle.Height = 40;
            lblTitle.Appearance.Font = new Font("微软雅黑", 14, FontStyle.Bold);
            lblTitle.Appearance.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
            Controls.Add(lblTitle);

            // ==========================================
            // 左侧区域（固定在左边，宽度不变）
            // ==========================================
            // 故障现象输入框
            LabelControl lblInput = new LabelControl { Text = "故障现象描述", Location = new Point(10, 60) };
            memoFaultInput = new MemoEdit { Location = new Point(10, 80), Width = 550, Height = 250 };
            Controls.Add(lblInput);
            Controls.Add(memoFaultInput);

            // AI诊断按钮
            btnDiagnose = new SimpleButton { Text = "AI诊断排查方向", Location = new Point(10, 340), Width = 550, Height = 40 };
            Controls.Add(btnDiagnose);

            // 排查结果框
            LabelControl lblResult = new LabelControl { Text = "排查原因/方向", Location = new Point(10, 390) };
            memoResult = new MemoEdit { Location = new Point(10, 410), Width = 550, Height = 280, Properties = { ReadOnly = true } };
            Controls.Add(lblResult);
            Controls.Add(memoResult);

            // ==========================================
            // 右侧区域（紧贴左侧，间距缩到最小）
            // ==========================================
            LabelControl lblHistory = new LabelControl { Text = "自动学习故障历史库", Location = new Point(570, 60) };
            gridControl1 = new GridControl { Location = new Point(570, 80), Width = 610, Height = 610 };
            gridView1 = new GridView();
            gridControl1.MainView = gridView1;
            gridControl1.ViewCollection.Add(gridView1);
            Controls.Add(lblHistory);
            Controls.Add(gridControl1);
        }

        private void InitGrid()
        {
            gridView1.OptionsBehavior.Editable = false;
            gridView1.OptionsView.ShowGroupPanel = false;
            gridView1.Columns.AddVisible("CreateTime", "诊断时间");
            gridView1.Columns.AddVisible("FaultDesc", "故障现象");
            gridView1.Columns.AddVisible("AiResult", "排查方向");
        }
    }
}