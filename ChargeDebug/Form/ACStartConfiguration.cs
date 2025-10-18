using DevExpress.XtraEditors;
using System.Globalization;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class ACStartConfiguration : XtraForm
    {
        // 添加通道标识字段
        private string _channelKey;

        private LabelControl labelStartMode;
        private LabelControl labelRunMode;
        private LabelControl labelBatteryVoltage;

        private ComboBoxEdit startMode;
        private ComboBoxEdit runMode;
        private TextEdit batteryVoltage;

        private SimpleButton btnOK;
        private SimpleButton btnCancel;

        // 存储所有参数值的类
        private ACConfigurationData acconfigData = new ACConfigurationData();

        //// 添加一个公共属性来暴露配置数据
        public ACConfigurationData ACConfiguration => acconfigData;

        public ACStartConfiguration(string channelKey, string text)
        {
            _channelKey = channelKey;

            InitializeComponent();
            this.Text = $"{text} - {channelKey}";
            InitializeUI();
            LoadSavedData();
        }

        private void LoadSavedData()
        {
            // 从配置存储中获取指定通道的数据
            acconfigData = ACConfigStorage.GetConfigData(_channelKey);

            batteryVoltage.Text = acconfigData.BatteryVoltage;

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
            this.Size = new Size(700, 200);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            labelStartMode = new LabelControl { Text = "AC侧启动方式:", Location = new Point(30, 22) };
            startMode = new ComboBoxEdit { Location = new Point(labelStartMode.Location.X + labelStartMode.Text.Length * 12, 17), Width = 100 };
            startMode.Properties.Items.AddRange(new[] { "自启动", "指令控制启动" });

            labelRunMode = new LabelControl { Text = "运行控制:", Location = new Point(startMode.Right + 25, 22) };
            runMode = new ComboBoxEdit { Location = new Point(labelRunMode.Location.X + labelRunMode.Text.Length * 13, 17), Width = 100 };
            runMode.Properties.Items.AddRange(new[] { "停机", "恒定并网直流恒压运行" });

            labelBatteryVoltage = new LabelControl { Text = "直流母线电压:", Location = new Point(runMode.Right + 25, 22) };
            batteryVoltage = new TextEdit { Location = new Point(labelBatteryVoltage.Location.X + labelBatteryVoltage.Text.Length * 13, 17), Width = 100 };
            batteryVoltage.Validated += PositiveParameter_Validated;
            LabelControl battery = new LabelControl { Text = "V", Location = new Point(batteryVoltage.Right + 5, 22) };

            btnOK = new SimpleButton
            {
                Text = "确定",
                DialogResult = DialogResult.None,
                Location = new Point(245, batteryVoltage.Bottom + 20),
                Size = new Size(80, 30)
            };
            btnOK.Click += BtnOK_Click;

            btnCancel = new SimpleButton
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(btnOK.Right + 50, btnOK.Location.Y),
                Size = new Size(80, 30)
            };
            btnCancel.Click += BtnCancel_Click;

            this.Height = btnCancel.Bottom + 50;

            // 添加所有控件到表单
            this.Controls.AddRange(new Control[]
            {
                labelStartMode,startMode,
                labelRunMode, runMode,
                labelBatteryVoltage,batteryVoltage,battery,
                btnOK, btnCancel
            });
        }

        private void PositiveParameter_Validated(object? sender, EventArgs e)
        {
            TextEdit? textEdit = sender as TextEdit;
            if (textEdit == null) return;

            if (double.TryParse(textEdit.Text, out double value))
            {
                textEdit.Text = Math.Abs(value).ToString("F1", CultureInfo.InvariantCulture);
            }
        }

        private void BtnOK_Click(object? sender, EventArgs e)
        {
            SaveCurrentSettings();
            this.DialogResult = DialogResult.OK;
            this.Close();
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
            acconfigData.BatteryVoltage = batteryVoltage.Text;

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
            public string BatteryVoltage { get; set; }
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
