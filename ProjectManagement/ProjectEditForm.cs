using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;

namespace ProjectManagement
{
    public partial class ProjectEditForm : XtraForm
    {
        private TextEdit projectNameEdit;
        private ComboBoxEdit batteryNameEdit;
        private TextEdit batteryCodeEdit;
        private ComboBoxEdit communicationTypeEdit;
        private SimpleButton btnOK;
        private SimpleButton btnCancel;

        public string ProjectName => projectNameEdit.Text;
        public string BatteryName => batteryNameEdit.Text;
        public string BatteryCode => batteryCodeEdit.Text;
        public string CommunicationType => communicationTypeEdit.Text;

        public ProjectEditForm(ProjectData projectData = null)
        {
            InitializeComponent();
            this.Text = projectData == null ? "新建项目" : "编辑项目";
            InitializeUI();

            if (projectData != null)
            {
                LoadProjectData(projectData);
            }
        }

        private void LoadProjectData(ProjectData projectData)
        {
            projectNameEdit.Text = projectData.ProjectName;
            batteryNameEdit.Text = projectData.BatteryName;
            batteryCodeEdit.Text = projectData.BatteryCode;
            communicationTypeEdit.Text = projectData.CommunicationType;
        }

        private void InitializeUI()
        {
            this.Size = new Size(700, 350);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // 创建标签和输入控件
            LabelControl labelProjectName = new LabelControl
            {
                Text = "项目名称:",
                Location = new Point(70, 22),
                Font = new Font("Tahoma", 12)
            };

            projectNameEdit = new TextEdit
            {
                Location = new Point(175, 20),
                Width = 150,
                Font = new Font("Tahoma", 12)
            };

            LabelControl labelBatteryName = new LabelControl
            {
                Text = "电池包名称:",
                Location = new Point(360, 22),
                Font = new Font("Tahoma", 12)
            };

            batteryNameEdit = new ComboBoxEdit
            {
                Location = new Point(450, 20),
                Width = 150,
                Font = new Font("Tahoma", 12)
            };
            batteryNameEdit.Properties.Items.AddRange(new string[] { "CANET-2E-U", "CANFDNET_200U_TCP" });
            batteryNameEdit.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;

            LabelControl labelBatteryCode = new LabelControl
            {
                Text = "电池包特征码:",
                Location = new Point(70, 62),
                Font = new Font("Tahoma", 12)
            };

            batteryCodeEdit = new TextEdit
            {
                Location = new Point(175, 60),
                Width = 150,
                Font = new Font("Tahoma", 12)
            };

            LabelControl labelCommunicationType = new LabelControl
            {
                Text = "通讯方式:",
                Location = new Point(360, 62),
                Font = new Font("Tahoma", 12)
            };

            communicationTypeEdit = new ComboBoxEdit
            {
                Location = new Point(450, 60),
                Width = 150,
                Font = new Font("Tahoma", 12)
            };
            communicationTypeEdit.Properties.Items.AddRange(new string[] { "CAN", "CAN FD", "Ethernet" });
            communicationTypeEdit.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;

            // 创建按钮
            btnOK = new SimpleButton
            {
                Text = "确定",
                Location = new Point(200, 270),
                Size = new Size(100, 30),
                DialogResult = DialogResult.None
            };
            btnCancel = new SimpleButton
            {
                Text = "取消",
                Location = new Point(400, 270),
                Size = new Size(100, 30),
                DialogResult = DialogResult.Cancel
            };

            btnOK.Click += BtnOK_Click; // 添加点击事件

            // 添加控件到窗体
            this.Controls.AddRange(new Control[]
            {
                labelProjectName, labelBatteryName, labelBatteryCode, labelCommunicationType,
                projectNameEdit, batteryNameEdit, batteryCodeEdit, communicationTypeEdit,
                btnOK, btnCancel
            });
        }

        private void BtnOK_Click(object? sender, EventArgs e)
        {
            // 验证输入
            if (string.IsNullOrWhiteSpace(projectNameEdit.Text))
            {
                XtraMessageBox.Show("请输入项目名称", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                projectNameEdit.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(batteryNameEdit.Text))
            {
                XtraMessageBox.Show("请选择电池包名称", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                batteryNameEdit.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(batteryCodeEdit.Text))
            {
                XtraMessageBox.Show("请输入电池包特征码", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                batteryCodeEdit.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(communicationTypeEdit.Text))
            {
                XtraMessageBox.Show("请选择通讯方式", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                communicationTypeEdit.Focus();
                return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }

    // 新增的数据传输对象
    public class ProjectData
    {
        public int? ProjectID { get; set; }
        public string ProjectName { get; set; }
        public string BatteryName { get; set; }
        public string BatteryCode { get; set; }
        public string CommunicationType { get; set; }
        public string ModificationTime { get; set; }
    }
}
