using DevExpress.DashboardWeb;
using DevExpress.XtraEditors;
using System.Globalization;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class ACStartConfiguration : XtraForm
    {
        // 添加通道标识字段
        private string _channelKey;
        private bool _flagbit;    // 存储flagbit参数

        public event EventHandler Applied;

        private LabelControl labelStartMode;
        private LabelControl labelRunMode;
        private LabelControl labelBatteryVoltage;
        private LabelControl labelActivePower;
        private LabelControl labelReactivePower;

        private ComboBoxEdit startMode;
        private ComboBoxEdit runMode;
        private TextEdit batteryVoltage;
        private TextEdit activePower;
        private TextEdit reactivePower;

        private SimpleButton btnOK;
        private SimpleButton btnApplication;
        private SimpleButton btnCancel;

        // 参数设置容器
        private Panel dynamicParametersPanel;

        // 存储动态生成的控件
        private Dictionary<string, Control> dynamicControls = new Dictionary<string, Control>();

        // 存储所有参数值的类
        private ACConfigurationData acconfigData = new ACConfigurationData();

        //// 添加一个公共属性来暴露配置数据
        public ACConfigurationData ACConfiguration => acconfigData;

        public Dictionary<string, string> DynamicParameters { get; set; } = new Dictionary<string, string>();

        public ACStartConfiguration(string channelKey, string text, bool flagbit)
        {
            _channelKey = channelKey;
            _flagbit = flagbit; // 保存flagbit参数
            InitializeComponent();
            this.Text = $"{text} - {channelKey}";
            InitializeUI();
            LoadSavedData();
        }

        private void LoadSavedData()
        {
            // 从配置存储中获取指定通道的数据
            acconfigData = ACConfigStorage.GetConfigData(_channelKey);

            // 设置工作模式
            if (!string.IsNullOrEmpty(acconfigData.StartMode))
            {
                startMode.Text = acconfigData.StartMode;
            }
            else
            {
                startMode.SelectedIndex = 0;
            }

            if (!string.IsNullOrEmpty(acconfigData.RunMode))
            {
                runMode.Text = acconfigData.RunMode;
            }
            else
            {
                runMode.SelectedIndex = 0;
            }
        }

        private void InitializeUI()
        {
            // 初始化控件布局和配置
            this.Size = new Size(500, 200);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            labelStartMode = new LabelControl { Text = "AC侧启动方式:", Location = new Point(30, 22) };
            startMode = new ComboBoxEdit { Location = new Point(labelStartMode.Location.X + labelStartMode.Text.Length * 12, 17), Width = 100 };
            startMode.Properties.Items.AddRange(new[] { "自启动", "指令控制启动" });

            labelRunMode = new LabelControl { Text = "运行控制:", Location = new Point(startMode.Right + 50, 22) };
            runMode = new ComboBoxEdit { Location = new Point(labelRunMode.Location.X + labelRunMode.Text.Length * 13, 17), Width = 100 };
            runMode.Properties.Items.AddRange(new[] { "恒定并网直流恒压运行", "交流恒功率运行", "停机" });
            runMode.SelectedIndexChanged += RunMode_SelectedIndexChanged;

            // 动态参数面板
            dynamicParametersPanel = new Panel
            {
                Location = new Point(20, 60),
                Size = new Size(440, 50),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = _flagbit // 根据flagbit设置可见性
            };

            btnOK = new SimpleButton
            {
                Text = "确定",
                DialogResult = DialogResult.None,
                Location = new Point(70, _flagbit ? 125 : 110),
                Size = new Size(80, 30)
            };
            btnOK.Click += BtnOK_Click;

            btnApplication = new SimpleButton
            {
                Text = "应用",
                DialogResult = DialogResult.None,
                Location = new Point(btnOK.Right + 50, _flagbit ? 125 : 110), // 根据flagbit调整位置
                Size = new Size(80, 30)
            };
            btnApplication.Click += BtnApplication_Click;

            btnCancel = new SimpleButton
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(btnApplication.Right + 50, _flagbit ? 125 : 110),
                Size = new Size(80, 30)
            };
            btnCancel.Click += BtnCancel_Click;

            // 如果flagbit为false，调整窗体高度
            if (!_flagbit)
            {
                this.Height = 290; // 减少窗体高度
            }

            // 添加所有控件到表单
            this.Controls.AddRange(new Control[]
            {
                labelStartMode,startMode,
                labelRunMode, runMode,
                dynamicParametersPanel,
                btnOK, btnApplication, btnCancel
            });
        }

        // 保存动态参数值
        private void SaveDynamicParameters()
        {
            foreach (var control in dynamicControls)
            {
                if (control.Value is TextEdit textEdit)
                {
                    acconfigData.DynamicParameters[control.Key] = textEdit.Text;
                }
            }
        }

        private void RunMode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            // 保存当前动态参数的值
            SaveDynamicParameters();

            // 清除之前的动态控件
            dynamicParametersPanel.Controls.Clear();
            dynamicControls.Clear();

            string selectedMode = runMode.Text;

            switch (selectedMode)
            {
                case "恒定并网直流恒压运行":
                    CreateConstantVoltageParameters();
                    break;
                case "交流恒功率运行":
                    CreateConstantPowerParameters();
                    break;
                default:
                    LabelControl noParamsLabel = new LabelControl
                    {
                        Text = "当前模式无需额外参数",
                        Location = new Point(150, 18),
                        Font = new Font("Tahoma", 12, FontStyle.Regular)
                    };
                    dynamicParametersPanel.Controls.Add(noParamsLabel);
                    break;
            }

            // 加载保存的动态参数值
            LoadDynamicParameters();
        }

        // 格式化正数参数（两位小数）
        private void PositiveParameter2_Validated(object? sender, EventArgs e)
        {
            TextEdit? textEdit = sender as TextEdit;
            if (textEdit == null) return;

            if (double.TryParse(textEdit.Text, out double value))
            {
                textEdit.Text = Math.Abs(value).ToString("F1", CultureInfo.InvariantCulture);
            }
        }

        private void PositiveParameter1_Validated(object? sender, EventArgs e)
        {
            TextEdit? textEdit = sender as TextEdit;
            if (textEdit == null) return;

            if (double.TryParse(textEdit.Text, out double value))
            {
                textEdit.Text = value.ToString("F1", CultureInfo.InvariantCulture);
            }
        }

        private void CreateConstantVoltageParameters()
        {
            int yPos = 15;

            // 设置母线电压
            LabelControl voltageLabel = new LabelControl { Text = "母线电压:", Location = new Point(30, yPos) };
            TextEdit voltageValue = new TextEdit { Location = new Point(voltageLabel.Right - 5, yPos - 5), Width = 100 };
            voltageValue.Validated += PositiveParameter2_Validated;
            //voltageValue.Validating += RangeValidation;
            voltageValue.Name = "ConstantVoltage";
            LabelControl voltageUnit = new LabelControl { Text = "V", Location = new Point(voltageValue.Right + 5, yPos) };

            dynamicParametersPanel.Controls.AddRange(new Control[]
            {
                voltageLabel, voltageValue, voltageUnit
            });

            // 保存引用
            dynamicControls.Add("ConstantVoltage", voltageValue);
        }

        private void CreateConstantPowerParameters()
        {
            int yPos = 17;

            // 设置有功功率
            LabelControl activepowerLabel = new LabelControl { Text = "有功功率:", Location = new Point(10, yPos) };
            TextEdit activepowerValue = new TextEdit { Location = new Point(activepowerLabel.Right - 5, yPos - 5), Width = 100 };
            activepowerValue.Validated += PositiveParameter1_Validated;
            //voltageValue.Validating += RangeValidation;
            activepowerValue.Name = "ConstantActivePower";
            LabelControl activepowerUnit = new LabelControl { Text = "KW", Location = new Point(activepowerValue.Right + 5, yPos) };

            // 设置无功功率
            LabelControl reactivepowerLabel = new LabelControl { Text = "无功功率:", Location = new Point(230, yPos) };
            TextEdit reactivepowerValue = new TextEdit { Location = new Point(reactivepowerLabel.Right - 5, yPos - 5), Width = 100 };
            reactivepowerValue.Validated += PositiveParameter1_Validated;
            //voltageLimitValue.Validating += RangeValidation;
            reactivepowerValue.Name = "ConstantReactivePower";
            LabelControl reactivepowerUnit = new LabelControl { Text = "KVar", Location = new Point(reactivepowerValue.Right + 5, yPos) };

            dynamicParametersPanel.Controls.AddRange(new Control[]
            {
                activepowerLabel, activepowerValue, activepowerUnit,
                reactivepowerLabel, reactivepowerValue, reactivepowerUnit
            });

            // 保存引用
            dynamicControls.Add("ConstantActivePower", activepowerValue);
            dynamicControls.Add("ConstantReactivePower", reactivepowerValue);
        }

        // 加载动态参数值
        private void LoadDynamicParameters()
        {
            foreach (var control in dynamicControls)
            {
                if (control.Value is TextEdit textEdit &&
                    acconfigData.DynamicParameters.ContainsKey(control.Key))
                {
                    textEdit.Text = acconfigData.DynamicParameters[control.Key];
                }
            }
        }

        private void BtnOK_Click(object? sender, EventArgs e)
        {
            SaveCurrentSettings();
            Applied?.Invoke(this, EventArgs.Empty);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void BtnApplication_Click(object? sender, EventArgs e)
        {
            SaveCurrentSettings();
            Applied?.Invoke(this, EventArgs.Empty);
        }

        private void SaveCurrentSettings()
        {
            // 验证所有输入
            if (!ValidateChildren())
            {
                XtraMessageBox.Show("请输入有效的参数值", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 保存基本参数
            acconfigData.StartMode = startMode.Text;
            acconfigData.RunMode = runMode.Text;

            // 保存动态参数
            foreach (var control in dynamicControls)
            {
                if (control.Value is TextEdit textEdit)
                {
                    acconfigData.DynamicParameters[control.Key] = textEdit.Text;
                }
            }

            // 保存到配置存储，使用通道标识
            ACConfigStorage.SaveConfigData(_channelKey, acconfigData);
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        // 配置数据类
        public class ACConfigurationData
        {
            public string StartMode { get; set; }
            public string RunMode { get; set; }
            public Dictionary<string, string> DynamicParameters { get; set; } = new Dictionary<string, string>();

            // 添加时间戳字段
            public DateTime LastUpdated { get; set; } = DateTime.Now;

            public ACConfigurationData()
            {
                // 设置默认值
                StartMode = "指令控制启动";
                RunMode = "恒定并网直流恒压运行";
            }
        }

        // 配置存储静态类
        public static class ACConfigStorage
        {
            // 使用字典存储多个配置，键为通道标识（设备IP + CAN索引）
            private static Dictionary<string, ACConfigurationData> _configDataDict = new Dictionary<string, ACConfigurationData>();

            public static ACConfigurationData GetConfigData(string channelKey)
            {
                // 如果不存在该通道的配置，创建一个新的
                if (!_configDataDict.ContainsKey(channelKey))
                {
                    _configDataDict[channelKey] = new ACConfigurationData();
                }

                return _configDataDict[channelKey];
            }

            public static void SaveConfigData(string channelKey, ACConfigurationData data)
            {
                // 这里可以改为保存到文件或数据库
                _configDataDict[channelKey] = data;
            }
        }
    }
}
