using DataModel;
using DevExpress.XtraEditors;
using DevExpress.XtraLayout;
using DevExpress.XtraLayout.Utils;
using DevExpress.XtraTreeList;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class DoublePulseTest : XtraUserControl
    {
        #region 字段声明

        private string sqladdress = "";
        private string deviceNumber = "";
        private TreeList treeList;
        private List<EquipmentModel> equipmentList;
        private StartupManager startupManager;

        // 组合框控件
        private ComboBoxEdit cbVoltageSource;
        private ComboBoxEdit cbVoltmeter;
        private ComboBoxEdit cbAmmeter;

        private SimpleButton btnVoltageCalibration;
        private SimpleButton btnCurrentCalibration;
        private SimpleButton btnStopCalibration;
        private SimpleButton btnQueryData;
        private SimpleButton btnClearQuery;

        // 协议列表
        private List<ModbusSignal> voltageSourceProtocols = new List<ModbusSignal>();
        private List<ModbusSignal> voltmeterProtocols = new List<ModbusSignal>();
        private List<SignalInfo> treeSignalProtocols = new List<SignalInfo>();
        Dictionary<string, List<SignalInfo>> debugProtocols = new Dictionary<string, List<SignalInfo>>();
        private ConfigurationData _protectionParameters;

        // 进度条控件
        private ProgressBarControl progressBar;

        // 校准状态标志
        private bool _isCalibrating = false;

        #endregion

        public DoublePulseTest()
        {
            InitializeComponent();
            InitializeUI();
        }

        /// <summary>
        /// 初始化用户界面
        /// </summary>
        private void InitializeUI()
        {
            // 主布局控件，填充整个用户控件
            LayoutControl layoutControl = new LayoutControl
            {
                Parent = this,
                Dock = DockStyle.Fill
            };

            // 4. 创建根布局组（垂直方向）
            LayoutControlGroup rootGroup = new LayoutControlGroup();
            rootGroup.TextVisible = false;
            rootGroup.GroupBordersVisible = false;
            rootGroup.DefaultLayoutType = LayoutType.Vertical; //垂直排列
            layoutControl.Root.Add(rootGroup);

            // 5. 添加按钮组到根组
            LayoutControlItem buttonGroupItem = rootGroup.AddItem();
            //buttonGroupItem.Control = CreateButtonContainer();
            buttonGroupItem.TextVisible = false;
            buttonGroupItem.SizeConstraintsType = SizeConstraintsType.Custom;
            buttonGroupItem.MinSize = new Size(0, 60);
            buttonGroupItem.MaxSize = new Size(0, 60);

            // 6. 添加TreeList到根组
            LayoutControlItem layoutControlItemForTreeList = rootGroup.AddItem();
            //layoutControlItemForTreeList.Control = CreateTreeList();
            layoutControlItemForTreeList.TextVisible = false;
            layoutControlItemForTreeList.SizeConstraintsType = SizeConstraintsType.Custom;
            layoutControlItemForTreeList.MinSize = new Size(0, 0);
            layoutControlItemForTreeList.MaxSize = new Size(0, 800);

            // 7. 添加底部进度条面板到根组
            LayoutControlItem progressGroupItem = rootGroup.AddItem();
            //progressGroupItem.Control = CreateProgressContainer();
            progressGroupItem.TextVisible = false;
            progressGroupItem.SizeConstraintsType = SizeConstraintsType.Custom;
            progressGroupItem.MinSize = new Size(0, 60);
            progressGroupItem.MaxSize = new Size(0, 60);
        }
    }
}
