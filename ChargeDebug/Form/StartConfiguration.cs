using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class StartConfiguration : XtraForm
    {
        // 添加通道标识字段
        private string _channelKey;
        private bool _flagbit; // 存储flagbit参数

        // 保护参数
        private TextEdit overVoltageValue;
        private TextEdit underVoltageValue;
        private TextEdit overCurrentValue;
        private TextEdit underCurrentValue;
        private TextEdit overPowerValue;
        private TextEdit underPowerValue;

        private LabelControl labelOverVoltageValue;
        private LabelControl labelUnderVoltageValue;
        private LabelControl labelOverCurrentValue;
        private LabelControl labelUnderCurrentValue;
        private LabelControl labelOverPowerValue;
        private LabelControl labelUnderPowerValue;

        private ComboBoxEdit workingMode;
        private LabelControl labelWorkingMode;
        private ComboBoxEdit directionammeter;
        private LabelControl labelDirectionammeter;

        private SimpleButton btnOK;
        private SimpleButton btnCancel;
        private SimpleButton btnApplication;

        // 参数设置容器
        private Panel dynamicParametersPanel;

        // 存储动态生成的控件
        private Dictionary<string, Control> dynamicControls = new Dictionary<string, Control>();

        // 存储所有参数值的类
        private ConfigurationData configData = new ConfigurationData();

        // 添加一个公共属性来暴露配置数据
        public ConfigurationData Configuration => configData;

        public event EventHandler Applied;

        public StartConfiguration(string channelKey, string text, bool flagbit)
        {
            _channelKey = channelKey;
            _flagbit = flagbit; // 保存flagbit参数
            InitializeComponent();
            this.Text = $"{text} - {channelKey}";
            InitializeUI();
            LoadSavedData();
        }

        private void InitializeUI()
        {
            // 初始化控件布局和配置
            this.Size = new Size(700, 350);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            labelOverVoltageValue = new LabelControl { Text = "蓄电池过压保护值:", Location = new Point(60, 22) };
            overVoltageValue = new TextEdit { Location = new Point(200, 20), Width = 80 };
            overVoltageValue.Validated += PositiveParameter_Validated;
            LabelControl overVoltage = new LabelControl { Text = "V", Location = new Point(290, 22) };

            labelUnderVoltageValue = new LabelControl { Text = "蓄电池欠压保护值:", Location = new Point(380, 22) };
            underVoltageValue = new TextEdit { Location = new Point(520, 20), Width = 80 };
            underVoltageValue.Validated += PositiveParameter_Validated;
            LabelControl underVoltage = new LabelControl { Text = "V", Location = new Point(610, 22) };

            labelOverCurrentValue = new LabelControl { Text = "蓄电池充电过流保护值:", Location = new Point(30, 62) };
            overCurrentValue = new TextEdit { Location = new Point(200, 60), Width = 80 };
            overCurrentValue.Validated += PositiveParameter_Validated;
            LabelControl overCurrent = new LabelControl { Text = "A", Location = new Point(290, 62) };

            labelUnderCurrentValue = new LabelControl { Text = "蓄电池放电过流保护值:", Location = new Point(350, 62) };
            underCurrentValue = new TextEdit { Location = new Point(520, 60), Width = 80 };
            underCurrentValue.Validated += NegativeParameter_Validated;
            LabelControl underCurrent = new LabelControl { Text = "A", Location = new Point(610, 62) };

            labelOverPowerValue = new LabelControl { Text = "蓄电池充电过功率保护值:", Location = new Point(15, 102) };
            overPowerValue = new TextEdit { Location = new Point(200, 100), Width = 80 };
            overPowerValue.Validated += PositiveParameter_Validated;
            LabelControl overPower = new LabelControl { Text = "KW", Location = new Point(290, 102) };

            labelUnderPowerValue = new LabelControl { Text = "蓄电池放电过功率保护值:", Location = new Point(335, 102) };
            underPowerValue = new TextEdit { Location = new Point(520, 100), Width = 80 };
            underPowerValue.Validated += NegativeParameter_Validated;
            LabelControl underPower = new LabelControl { Text = "KW", Location = new Point(610, 102) };

            labelWorkingMode = new LabelControl { Text = "工步模式:", Location = new Point(240, 162) };
            labelWorkingMode.Visible = _flagbit;
            workingMode = new ComboBoxEdit { Location = new Point(310, 160), Width = 150 };
            workingMode.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            workingMode.Visible = _flagbit; // 根据flagbit设置可见性
            workingMode.Properties.Items.AddRange(new[] { "搁置", "静置", "恒流充电", "恒流放电", "恒压充电", "恒压放电", "恒功率充电", "恒功率放电", "停机" });
            workingMode.SelectedIndexChanged += WorkingMode_SelectedIndexChanged;

            labelDirectionammeter = new LabelControl { Text = "电流表方向:", Location = new Point(240, 162) };
            labelDirectionammeter.Visible = !_flagbit;
            directionammeter = new ComboBoxEdit { Location = new Point(310, 160), Width = 150 };
            directionammeter.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            directionammeter.Visible = !_flagbit; // 根据flagbit设置可见性
            directionammeter.Properties.Items.AddRange(new[] { "正方向", "反方向" });

            // 动态参数面板
            dynamicParametersPanel = new Panel
            {
                Location = new Point(20, 200),
                Size = new Size(640, 60),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = _flagbit // 根据flagbit设置可见性
            };

            // 添加确定/取消按钮
            btnOK = new SimpleButton
            {
                Text = "确定",
                DialogResult = DialogResult.None,
                Location = new Point(150, _flagbit ? 280 : 200), // 根据flagbit调整位置
                Size = new Size(80, 30)
            };
            btnOK.Click += BtnOK_Click;

            btnApplication = new SimpleButton
            {
                Text = "应用",
                DialogResult = DialogResult.None,
                Location = new Point(300, _flagbit ? 280 : 200), // 根据flagbit调整位置
                Size = new Size(80, 30)
            };
            btnApplication.Click += BtnApplication_Click;

            btnCancel = new SimpleButton
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(450, _flagbit ? 280 : 200), // 根据flagbit调整位置
                Size = new Size(80, 30)
            };
            btnCancel.Click += BtnCancel_Click;

            // 如果flagbit为false，调整窗体高度
            if (!_flagbit)
            {
                this.Height = 280; // 减少窗体高度
            }

            // 添加所有控件到表单
            this.Controls.AddRange(new Control[]
            {
                labelOverVoltageValue, overVoltageValue,overVoltage,
                labelUnderVoltageValue,underVoltageValue,underVoltage,
                labelOverCurrentValue,overCurrentValue,overCurrent,
                labelUnderCurrentValue,underCurrentValue,underCurrent,
                labelOverPowerValue,overPowerValue,overPower,
                labelUnderPowerValue,underPowerValue,underPower,
                labelWorkingMode,workingMode,dynamicParametersPanel,
                labelDirectionammeter, directionammeter,
                btnOK, btnApplication, btnCancel
            });
        }

        // 格式化正数参数（一位小数）
        private void PositiveParameter_Validated(object? sender, EventArgs e)
        {
            TextEdit? textEdit = sender as TextEdit;
            if (textEdit == null) return;

            if (double.TryParse(textEdit.Text, out double value))
            {
                textEdit.Text = Math.Abs(value).ToString("F1", CultureInfo.InvariantCulture);
            }
        }

        // 格式化负数参数（一位小数，带负号）
        private void NegativeParameter_Validated(object? sender, EventArgs e)
        {
            TextEdit? textEdit = sender as TextEdit;
            if (textEdit == null) return;

            if (double.TryParse(textEdit.Text, out double value))
            {
                textEdit.Text = (-Math.Abs(value)).ToString("F1", CultureInfo.InvariantCulture);
            }
        }

        // 格式化正数参数（两位小数）
        private void PositiveParameter2_Validated(object? sender, EventArgs e)
        {
            TextEdit? textEdit = sender as TextEdit;
            if (textEdit == null) return;

            if (double.TryParse(textEdit.Text, out double value))
            {
                textEdit.Text = Math.Abs(value).ToString("F2", CultureInfo.InvariantCulture);
            }
        }

        // 格式化负数参数（两位小数，带负号）
        private void NegativeParameter2_Validated(object? sender, EventArgs e)
        {
            TextEdit? textEdit = sender as TextEdit;
            if (textEdit == null) return;

            if (double.TryParse(textEdit.Text, out double value))
            {
                textEdit.Text = (-Math.Abs(value)).ToString("F2", CultureInfo.InvariantCulture);
            }
        }

        private void RangeValidation(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            TextEdit? textEdit = sender as TextEdit;
            if (textEdit == null) return;

            // 根据参数类型设置范围验证
            //电压
            if (textEdit.Name == "ConstantCurrentCharge_VoltageLimit" ||
                textEdit.Name == "ConstantCurrentDischarge_VoltageLimit" ||
                textEdit.Name == "ConstantVoltageCharge_Voltage" ||
                textEdit.Name == "ConstantVoltageDischarge_Voltage")
            {
                double value = double.Parse(textEdit.Text);
                double max_value = double.Parse(overVoltageValue.Text);
                double min_value = double.Parse(underVoltageValue.Text);
                if (value < min_value || value > max_value)
                {
                    textEdit.ErrorText = $"电压值必须在 {min_value} 到 {max_value} V 之间";
                    e.Cancel = true;
                    return;
                }
            }
            else if (textEdit.Name == "ConstantCurrentCharge_Current" ||
                     textEdit.Name == "ConstantCurrentDischarge_Current" ||
                     textEdit.Name == "ConstantVoltageCharge_CurrentLimit" ||
                     textEdit.Name == "ConstantVoltageDischarge_CurrentLimit" ||
                     textEdit.Name == "ConstantPowerCharge_CurrentLimit" ||
                     textEdit.Name == "ConstantPowerDischarge_CurrentLimit")
            {
                double value = double.Parse(textEdit.Text);
                double max_value = double.Parse(overCurrentValue.Text);
                double min_value = double.Parse(underCurrentValue.Text);
                if (value < min_value || value > max_value)
                {
                    textEdit.ErrorText = $"电压值必须在 {min_value} 到 {max_value} V 之间";
                    e.Cancel = true;
                    return;
                }
            }
            else if (textEdit.Name == "ConstantPowerCharge_Power" ||
                     textEdit.Name == "ConstantPowerDischarge_Power")
            {
                double value = double.Parse(textEdit.Text);
                double max_value = double.Parse(overPowerValue.Text);
                double min_value = double.Parse(underPowerValue.Text);
                if (value < min_value || value > max_value)
                {
                    textEdit.ErrorText = $"电压值必须在 {min_value} 到 {max_value} V 之间";
                    e.Cancel = true;
                    return;
                }
            }
        }

        // 加载保存的数据
        private void LoadSavedData()
        {
            // 从配置存储中获取指定通道的数据
            configData = ConfigStorage.GetConfigData(_channelKey);

            // 设置基本参数值
            overVoltageValue.Text = configData.OverVoltage;
            underVoltageValue.Text = configData.UnderVoltage;
            overCurrentValue.Text = configData.OverCurrent;
            underCurrentValue.Text = configData.UnderCurrent;
            overPowerValue.Text = configData.OverPower;
            underPowerValue.Text = configData.UnderPower;

            // 设置工作模式
            if (!string.IsNullOrEmpty(configData.WorkingMode))
            {
                workingMode.Text = configData.WorkingMode;
            }
            else
            {
                workingMode.SelectedIndex = 0;
            }
        }

        // 保存当前设置
        private void SaveCurrentSettings()
        {
            // 验证所有输入
            if (!ValidateChildren())
            {
                XtraMessageBox.Show("请输入有效的参数值", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 保存基本参数
            configData.OverVoltage = overVoltageValue.Text;
            configData.UnderVoltage = underVoltageValue.Text;
            configData.OverCurrent = overCurrentValue.Text;
            configData.UnderCurrent = underCurrentValue.Text;
            configData.OverPower = overPowerValue.Text;
            configData.UnderPower = underPowerValue.Text;

            // 保存工作模式
            configData.WorkingMode = workingMode.Text;
            configData.Directionammeter = workingMode.Text;

            // 保存动态参数
            foreach (var control in dynamicControls)
            {
                if (control.Value is TextEdit textEdit)
                {
                    configData.DynamicParameters[control.Key] = textEdit.Text;
                }
            }

            // 保存到配置存储，使用通道标识
            ConfigStorage.SaveConfigData(_channelKey, configData);
        }

        private void WorkingMode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            // 保存当前动态参数的值
            SaveDynamicParameters();

            // 清除之前的动态控件
            dynamicParametersPanel.Controls.Clear();
            dynamicControls.Clear();

            string selectedMode = workingMode.Text;

            switch (selectedMode)
            {
                case "恒流充电":
                    CreateConstantCurrentChargeParameters();
                    break;
                case "恒流放电":
                    CreateConstantCurrentDischargeParameters();
                    break;
                case "恒压充电":
                    CreateConstantVoltageChargeParameters();
                    break;
                case "恒压放电":
                    CreateConstantVoltageDischargeParameters();
                    break;
                case "恒功率充电":
                    CreateConstantPowerChargeParameters();
                    break;
                case "恒功率放电":
                    CreateConstantPowerDischargeParameters();
                    break;
                // 搁置、静置、停机不需要参数
                default:
                    LabelControl noParamsLabel = new LabelControl
                    {
                        Text = "当前模式无需额外参数",
                        Location = new Point(245, 18),
                        Font = new Font("Tahoma", 12, FontStyle.Regular)
                    };
                    dynamicParametersPanel.Controls.Add(noParamsLabel);
                    break;
            }

            // 加载保存的动态参数值
            LoadDynamicParameters();
        }

        // 保存动态参数值
        private void SaveDynamicParameters()
        {
            foreach (var control in dynamicControls)
            {
                if (control.Value is TextEdit textEdit)
                {
                    configData.DynamicParameters[control.Key] = textEdit.Text;
                }
            }
        }

        // 加载动态参数值
        private void LoadDynamicParameters()
        {
            foreach (var control in dynamicControls)
            {
                if (control.Value is TextEdit textEdit &&
                    configData.DynamicParameters.ContainsKey(control.Key))
                {
                    textEdit.Text = configData.DynamicParameters[control.Key];
                }
            }
        }

        private void CreateConstantCurrentChargeParameters()
        {
            int yPos = 17;

            // 设定电流
            LabelControl currentLabel = new LabelControl { Text = "设定电流:", Location = new Point(50, yPos + 2) };
            TextEdit currentValue = new TextEdit { Location = new Point(130, yPos), Width = 100 };
            currentValue.Validated += PositiveParameter2_Validated;
            currentValue.Validating += RangeValidation;
            currentValue.Name = "ConstantCurrentCharge_Current";
            LabelControl currentUnit = new LabelControl { Text = "A", Location = new Point(240, yPos + 2) };

            // 电压上限
            LabelControl voltageLimitLabel = new LabelControl { Text = "电压上限:", Location = new Point(350, yPos + 2) };
            TextEdit voltageLimitValue = new TextEdit { Location = new Point(430, yPos), Width = 100 };
            voltageLimitValue.Validated += PositiveParameter2_Validated;
            voltageLimitValue.Validating += RangeValidation;
            voltageLimitValue.Name = "ConstantCurrentCharge_VoltageLimit";
            LabelControl voltageUnit = new LabelControl { Text = "V", Location = new Point(540, yPos + 2) };

            dynamicParametersPanel.Controls.AddRange(new Control[]
            {
                currentLabel, currentValue, currentUnit,
                voltageLimitLabel, voltageLimitValue, voltageUnit
            });

            // 保存引用
            dynamicControls.Add("ConstantCurrentCharge_Current", currentValue);
            dynamicControls.Add("ConstantCurrentCharge_VoltageLimit", voltageLimitValue);
        }

        private void CreateConstantCurrentDischargeParameters()
        {
            int yPos = 17;

            // 设定电流
            LabelControl currentLabel = new LabelControl { Text = "设定电流:", Location = new Point(50, yPos + 2) };
            TextEdit currentValue = new TextEdit { Location = new Point(130, yPos), Width = 100 };
            currentValue.Validated += NegativeParameter2_Validated;
            currentValue.Validating += RangeValidation;
            currentValue.Name = "ConstantCurrentDischarge_Current";
            LabelControl currentUnit = new LabelControl { Text = "A", Location = new Point(240, yPos + 2) };

            // 电压下限
            LabelControl voltageLimitLabel = new LabelControl { Text = "电压下限:", Location = new Point(350, yPos + 2) };
            TextEdit voltageLimitValue = new TextEdit { Location = new Point(430, yPos), Width = 100 };
            voltageLimitValue.Validated += PositiveParameter2_Validated;
            voltageLimitValue.Validating += RangeValidation;
            voltageLimitValue.Name = "ConstantCurrentDischarge_VoltageLimit";
            LabelControl voltageUnit = new LabelControl { Text = "V", Location = new Point(540, yPos + 2) };

            dynamicParametersPanel.Controls.AddRange(new Control[]
            {
                currentLabel, currentValue, currentUnit,
                voltageLimitLabel, voltageLimitValue, voltageUnit
            });

            // 保存引用
            dynamicControls.Add("ConstantCurrentDischarge_Current", currentValue);
            dynamicControls.Add("ConstantCurrentDischarge_VoltageLimit", voltageLimitValue);
        }

        private void CreateConstantVoltageChargeParameters()
        {
            int yPos = 17;

            // 限制充电电流
            LabelControl currentLimitLabel = new LabelControl { Text = "限制充电电流:", Location = new Point(30, yPos + 2) };
            TextEdit currentLimitValue = new TextEdit { Location = new Point(150, yPos), Width = 100 };
            currentLimitValue.Validated += PositiveParameter2_Validated;
            currentLimitValue.Validating += RangeValidation;
            currentLimitValue.Name = "ConstantVoltageCharge_CurrentLimit";
            LabelControl currentUnit = new LabelControl { Text = "A", Location = new Point(240, yPos + 2) };

            // 设定电压
            LabelControl voltageLabel = new LabelControl { Text = "设定电压:", Location = new Point(350, yPos + 2) };
            TextEdit voltageValue = new TextEdit { Location = new Point(430, yPos), Width = 100 };
            voltageValue.Validated += PositiveParameter2_Validated;
            voltageValue.Validating += RangeValidation;
            voltageValue.Name = "ConstantVoltageCharge_Voltage";
            LabelControl voltageUnit = new LabelControl { Text = "V", Location = new Point(540, yPos + 2) };

            dynamicParametersPanel.Controls.AddRange(new Control[]
            {
                currentLimitLabel, currentLimitValue, currentUnit,
                voltageLabel, voltageValue, voltageUnit
            });

            // 保存引用
            dynamicControls.Add("ConstantVoltageCharge_CurrentLimit", currentLimitValue);
            dynamicControls.Add("ConstantVoltageCharge_Voltage", voltageValue);
        }

        private void CreateConstantVoltageDischargeParameters()
        {
            int yPos = 17;

            // 限制放电电流
            LabelControl currentLimitLabel = new LabelControl { Text = "限制放电电流:", Location = new Point(30, yPos + 2) };
            TextEdit currentLimitValue = new TextEdit { Location = new Point(150, yPos), Width = 100 };
            currentLimitValue.Validated += NegativeParameter2_Validated;
            currentLimitValue.Validating += RangeValidation;
            currentLimitValue.Name = "ConstantVoltageDischarge_CurrentLimit";
            LabelControl currentUnit = new LabelControl { Text = "A", Location = new Point(240, yPos + 2) };

            // 设定电压
            LabelControl voltageLabel = new LabelControl { Text = "设定电压:", Location = new Point(350, yPos + 2) };
            TextEdit voltageValue = new TextEdit { Location = new Point(430, yPos), Width = 100 };
            voltageValue.Validated += PositiveParameter2_Validated;
            voltageValue.Validating += RangeValidation;
            voltageValue.Name = "ConstantVoltageDischarge_Voltage";
            LabelControl voltageUnit = new LabelControl { Text = "V", Location = new Point(540, yPos + 2) };

            dynamicParametersPanel.Controls.AddRange(new Control[]
            {
                currentLimitLabel, currentLimitValue, currentUnit,
                voltageLabel, voltageValue, voltageUnit
            });

            // 保存引用
            dynamicControls.Add("ConstantVoltageDischarge_CurrentLimit", currentLimitValue);
            dynamicControls.Add("ConstantVoltageDischarge_Voltage", voltageValue);
        }

        private void CreateConstantPowerChargeParameters()
        {
            int yPos = 17;

            // 设定功率
            LabelControl powerLabel = new LabelControl { Text = "设定功率:", Location = new Point(50, yPos + 2) };
            TextEdit powerValue = new TextEdit { Location = new Point(130, yPos), Width = 100 };
            powerValue.Validated += PositiveParameter2_Validated;
            powerValue.Validating += RangeValidation;
            powerValue.Name = "ConstantPowerCharge_Power";
            LabelControl powerUnit = new LabelControl { Text = "KW", Location = new Point(240, yPos + 2) };

            // 限制充电电流
            LabelControl currentLimitLabel = new LabelControl { Text = "限制充电电流:", Location = new Point(350, yPos + 2) };
            TextEdit currentLimitValue = new TextEdit { Location = new Point(470, yPos), Width = 100 };
            currentLimitValue.Validated += PositiveParameter2_Validated;
            currentLimitValue.Validating += RangeValidation;
            currentLimitValue.Name = "ConstantPowerCharge_CurrentLimit";
            LabelControl currentUnit = new LabelControl { Text = "A", Location = new Point(560, yPos + 2) };

            dynamicParametersPanel.Controls.AddRange(new Control[]
            {
                powerLabel, powerValue, powerUnit,
                currentLimitLabel, currentLimitValue, currentUnit
            });

            // 保存引用
            dynamicControls.Add("ConstantPowerCharge_Power", powerValue);
            dynamicControls.Add("ConstantPowerCharge_CurrentLimit", currentLimitValue);
        }

        private void CreateConstantPowerDischargeParameters()
        {
            int yPos = 17;

            // 设定功率
            LabelControl powerLabel = new LabelControl { Text = "设定功率:", Location = new Point(50, yPos + 2) };
            TextEdit powerValue = new TextEdit { Location = new Point(130, yPos), Width = 100 };
            powerValue.Validated += NegativeParameter2_Validated;
            powerValue.Validating += RangeValidation;
            powerValue.Name = "ConstantPowerDischarge_Power";
            LabelControl powerUnit = new LabelControl { Text = "KW", Location = new Point(240, yPos + 2) };

            // 限制放电电流
            LabelControl currentLimitLabel = new LabelControl { Text = "限制放电电流:", Location = new Point(350, yPos + 2) };
            TextEdit currentLimitValue = new TextEdit { Location = new Point(470, yPos), Width = 100 };
            currentLimitValue.Validated += NegativeParameter2_Validated;
            currentLimitValue.Validating += RangeValidation;
            currentLimitValue.Name = "ConstantPowerDischarge_CurrentLimit";
            LabelControl currentUnit = new LabelControl { Text = "A", Location = new Point(560, yPos + 2) };

            dynamicParametersPanel.Controls.AddRange(new Control[]
            {
                powerLabel, powerValue, powerUnit,
                currentLimitLabel, currentLimitValue, currentUnit
            });

            // 保存引用
            dynamicControls.Add("ConstantPowerDischarge_Power", powerValue);
            dynamicControls.Add("ConstantPowerDischarge_CurrentLimit", currentLimitValue);
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

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }

    // 配置数据类
    public class ConfigurationData
    {
        public string OverVoltage { get; set; }
        public string UnderVoltage { get; set; }
        public string OverCurrent { get; set; }
        public string UnderCurrent { get; set; }
        public string OverPower { get; set; }
        public string UnderPower { get; set; }
        public string WorkingMode { get; set; }
        public string Directionammeter { get; set; }

        public Dictionary<string, string> DynamicParameters { get; set; } = new Dictionary<string, string>();

        // 添加时间戳字段
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        public ConfigurationData()
        {
            // 设置默认值
            OverVoltage = "1000.0";
            UnderVoltage = "0.0";
            OverCurrent = "100.0";
            UnderCurrent = "-100.0";
            OverPower = "100.0";
            UnderPower = "-100.0";
            WorkingMode = "搁置";
        }
    }

    // 配置存储静态类
    public static class ConfigStorage
    {
        // 使用字典存储多个配置，键为通道标识（设备IP + CAN索引）
        private static Dictionary<string, ConfigurationData> _configDataDict = new Dictionary<string, ConfigurationData>();

        public static ConfigurationData GetConfigData(string channelKey)
        {
            // 如果不存在该通道的配置，创建一个新的
            if (!_configDataDict.ContainsKey(channelKey))
            {
                _configDataDict[channelKey] = new ConfigurationData();
            }

            return _configDataDict[channelKey];
        }

        public static void SaveConfigData(string channelKey, ConfigurationData data)
        {
            // 这里可以改为保存到文件或数据库
            _configDataDict[channelKey] = data;
        }
    }
}
