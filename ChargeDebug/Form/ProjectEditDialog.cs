using DataModel;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DocumentFormat.OpenXml.Office2010.Excel;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class ProjectEditDialog : XtraForm
    {
        public TestProjectModel ProjectData { get; private set; }

        // 控件声明
        private LabelControl labelId;
        private LabelControl labelProjectName;
        private LabelControl labelTestVoltage;
        private LabelControl labelTestTime;
        private LabelControl labelRampUpTime;
        private LabelControl labelRampDownTime;
        private LabelControl labelCurrentLimit;
        private LabelControl labelResistanceLimit;

        private TextEdit txtId;
        private ComboBoxEdit cmbProjectName;
        private TextEdit txtTestVoltage;
        private TextEdit txtTestTime;
        private TextEdit txtRampUpTime;
        private TextEdit txtRampDownTime;
        private TextEdit txtCurrentLimit;
        private TextEdit txtResistanceLimit;
        private SimpleButton btnOK;
        private SimpleButton btnCancel;
        
        /// <summary>
        /// 构造函数（用于添加新项目）
        /// </summary>
        public ProjectEditDialog(int newDeviceNumber)
        {
            ProjectData = new TestProjectModel();
            InitializeComponent();
            InitializeUI();
            txtId.Text = (ProjectData.ProjectId = newDeviceNumber).ToString();
        }

        /// <summary>
        /// 构造函数（用于编辑现有项目）
        /// </summary>
        /// <param name="existingProject">要编辑的项目</param>
        public ProjectEditDialog(TestProjectModel existingProject)
        {
            ProjectData = existingProject ?? new TestProjectModel();
            InitializeComponent();
            InitializeUI();
            LoadDataToForm();
        }

        private void InitializeUI()
        {
            this.Text = ProjectData.Id == 0 ? "添加项目" : "编辑项目";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Size = new Size(600, 300); // 调整窗体大小以适应新布局
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;

            labelId = new LabelControl { Text = "序号:", Location = new Point(65, 22) };
            txtId = new TextEdit { Location = new Point(185, 17), Width = 120 };
            // 设置序号文本框为只读，禁止编辑
            txtId.Properties.ReadOnly = true;

            labelProjectName = new LabelControl { Text = "项目名称:", Location = new Point(370, 22) };
            cmbProjectName = new ComboBoxEdit { Location = new Point(475, 17), Width = 120 };
            cmbProjectName.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            cmbProjectName.Properties.Items.AddRange(new[] { "AC耐压测试", "DC耐压测试", "绝缘电阻测试", "等电位测试" });
            cmbProjectName.SelectedIndex = 0;
            cmbProjectName.SelectedIndexChanged += CmbProjectName_SelectedIndexChanged;

            labelTestVoltage = new LabelControl { Text = "测试电压/测试电流:", Location = new Point(65, 62) };
            txtTestVoltage = new TextEdit { Location = new Point(185, 57), Width = 120 };
            LabelControl unitTestVoltage = new LabelControl { Text = "V/A", Location = new Point(310, 62) };

            labelTestTime = new LabelControl { Text = "测试时间:", Location = new Point(370, 62) };
            txtTestTime = new TextEdit { Location = new Point(475, 57), Width = 120 };
            LabelControl unitTestTime = new LabelControl { Text = "S", Location = new Point(600, 62) };

            labelRampUpTime = new LabelControl { Text = "电压上升时间:", Location = new Point(65, 102) };
            txtRampUpTime = new TextEdit { Location = new Point(185, 97), Width = 120 };
            LabelControl unitRampUpTime = new LabelControl { Text = "S", Location = new Point(310, 102) };

            labelRampDownTime = new LabelControl { Text = "电压下降时间:", Location = new Point(370, 102) };
            txtRampDownTime = new TextEdit { Location = new Point(475, 97), Width = 120 };
            LabelControl unitRampDownTime = new LabelControl { Text = "S", Location = new Point(600, 102) };

            labelCurrentLimit = new LabelControl { Text = "电流上下限范围:", Location = new Point(65, 142) };
            txtCurrentLimit = new TextEdit { Location = new Point(185, 137), Width = 120 };
            LabelControl unitCurrentLimit = new LabelControl { Text = "mA", Location = new Point(310, 142) };

            labelResistanceLimit = new LabelControl { Text = "电阻上下限范围:", Location = new Point(370, 142) };
            txtResistanceLimit = new TextEdit { Location = new Point(475, 137), Width = 120 };
            LabelControl unitResistanceLimit = new LabelControl { Text = "MΩ", Location = new Point(600, 142) };

            txtCurrentLimit.Properties.NullText = "例如 0.1-10";
            txtResistanceLimit.Properties.NullText = "例如 1-100";

            UpdateInputsState(cmbProjectName.SelectedItem?.ToString());

            // 添加确定按钮
            btnOK = new SimpleButton
            {
                Text = "确定",
                DialogResult = DialogResult.None,
                Location = new Point(200, txtResistanceLimit.Bottom + 30),
                Size = new Size(80, 30)
            };
            btnCancel = new SimpleButton
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(400, txtResistanceLimit.Bottom + 30),
                Size = new Size(80, 30)
            };

            btnOK.Click += BtnOK_Click;

            this.Size = new Size(700, btnCancel.Bottom + 60); // 调整窗体大小以适应新布局

            // 添加所有控件到表单
            this.Controls.AddRange(new Control[]
            {
                labelId, txtId,
                labelProjectName, cmbProjectName,
                labelTestVoltage, txtTestVoltage, unitTestVoltage,
                labelTestTime, txtTestTime, unitTestTime,
                labelRampUpTime, txtRampUpTime, unitRampUpTime,
                labelRampDownTime, txtRampDownTime, unitRampDownTime,
                labelCurrentLimit, txtCurrentLimit, unitCurrentLimit,
                labelResistanceLimit, txtResistanceLimit, unitResistanceLimit,
                btnOK, btnCancel
            });
        }

        private void UpdateInputsState(string? projectType)
        {
            if (projectType == "绝缘电阻测试")
            {
                txtResistanceLimit.Enabled = true;
                txtRampUpTime.Enabled = true;
                txtRampDownTime.Enabled = true;

                txtCurrentLimit.Enabled = false;
                txtCurrentLimit.Text = string.Empty;
            }
            else if (projectType == "等电位测试")
            {
                txtResistanceLimit.Enabled = true;

                txtCurrentLimit.Enabled = false;
                txtCurrentLimit.Text = string.Empty;

                txtRampUpTime.Enabled = false;
                txtRampUpTime.Text = string.Empty;

                txtRampDownTime.Enabled = false;
                txtRampDownTime.Text = string.Empty;
            }
            else // AC耐压测试 或 DC耐压测试
            {
                txtCurrentLimit.Enabled = true;
                txtRampUpTime.Enabled = true;
                txtRampDownTime.Enabled = true;

                txtResistanceLimit.Enabled = false;
                txtResistanceLimit.Text = string.Empty;
            }
        }

        private void CmbProjectName_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string selectedProject = cmbProjectName.SelectedItem?.ToString();
            UpdateInputsState(selectedProject);
        }

        private void LoadDataToForm()
        {
            txtId.Text = ProjectData.ProjectId.ToString();
            cmbProjectName.Text = ProjectData.ProjectName.ToString();
            txtTestVoltage.Text = ProjectData.TestVoltage.ToString();
            txtTestTime.Text = ProjectData.TestTime.ToString();
            txtRampUpTime.Text = ProjectData.RampUpTime.ToString();
            txtRampDownTime.Text = ProjectData.RampDownTime.ToString();
            txtCurrentLimit.Text = ProjectData.CurrentLimit;
            txtResistanceLimit.Text = ProjectData.ResistanceLimit;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            // 基本验证
            if (string.IsNullOrWhiteSpace(cmbProjectName.Text))
            {
                XtraMessageBox.Show("项目名称不能为空！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cmbProjectName.Focus();
                return;
            }

            // 数值字段验证
            if (!double.TryParse(txtTestVoltage.Text, out double voltage))
            {
                XtraMessageBox.Show("测试电压必须是有效数字！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtTestVoltage.Focus();
                return;
            }

            if (!double.TryParse(txtTestTime.Text, out double time))
            {
                XtraMessageBox.Show("测试时间必须是有效数字！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtTestTime.Focus();
                return;
            }

            double rampUp = double.NaN;
            double rampDown = double.NaN;
            if (cmbProjectName.Text != "等电位测试")
            {
                if (!double.TryParse(txtRampUpTime.Text, out rampUp) && cmbProjectName.Text != "等电位测试")
                {
                    XtraMessageBox.Show("电压上升时间必须是有效数字！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtRampUpTime.Focus();
                    return;
                }

                if (!double.TryParse(txtRampDownTime.Text, out rampDown) && cmbProjectName.Text != "等电位测试")
                {
                    XtraMessageBox.Show("电压下降时间必须是有效数字！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtRampDownTime.Focus();
                    return;
                }
            }

            // 保存数据
            ProjectData.ProjectName = cmbProjectName.Text.Trim();
            ProjectData.TestVoltage = voltage;
            ProjectData.TestTime = time;
            ProjectData.RampUpTime = rampUp;
            ProjectData.RampDownTime = rampDown;
            ProjectData.CurrentLimit = txtCurrentLimit.Text.Trim();
            ProjectData.ResistanceLimit = txtResistanceLimit.Text.Trim();

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
