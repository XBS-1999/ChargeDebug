using ChargeDebug.Service;
using DataModel;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using System.Data.SQLite;

namespace ChargeDebug.Form
{
    public partial class AddEditCalibrationSignalForm : XtraForm
    {
        private long? signalId;
        private string sqladdress;

        // UI控件声明
        private ComboBoxEdit devicename;
        private ComboBoxEdit signalname;
        private ComboBoxEdit signaltype;
        private TextEdit readtime;
        private TextEdit ratingVoltagecurrent;
        private TextEdit calibrationnumber;
        private TextEdit calibrationaccuracy;

        private LabelControl labeldevicename;
        private LabelControl labelsignalname;
        private LabelControl labelsignaltype;
        private LabelControl labelreadtime;
        private LabelControl labelratingVoltagecurrent;
        private LabelControl labelcalibrationnumber;
        private LabelControl labelcalibrationaccuracy;

        private SimpleButton btnOK;
        private SimpleButton btnCancel;

        private List<EquipmentModel> equipmentList;

        public AddEditCalibrationSignalForm(long? signalId, string sqladdress, List<EquipmentModel> equipmentList)
        {
            this.signalId = signalId;
            this.sqladdress = sqladdress;
            this.equipmentList = equipmentList
                .Where(e => e.DeviceType == "充放电设备")
                .ToList();

            InitializeComponent();
            InitializeUI();
            LoadSignalData();
        }

        private void InitializeUI()
        {
            // 设置窗体属性
            this.Text = signalId.HasValue ? "编辑信号" : "添加信号";
            this.Size = new Size(700, 300); // 调整窗体大小以适应新布局
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            labeldevicename = new LabelControl { Text = "设备名称及通道号:", Location = new Point(50, 22) };
            devicename = new ComboBoxEdit { Location = new Point(210, 20), Width = 120 };
            devicename.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            devicename.SelectedIndexChanged += Devicename_SelectedIndexChanged;

            labelsignalname = new LabelControl { Text = "信号名称:", Location = new Point(370, 22) };
            signalname = new ComboBoxEdit { Location = new Point(480, 20), Width = 150 };
            signalname.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            
            labelsignaltype = new LabelControl { Text = "信号类型:", Location = new Point(50, 62) };
            signaltype = new ComboBoxEdit { Location = new Point(210, 60), Width = 120 };
            signaltype.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            signaltype.Properties.Items.AddRange(new[] { "电压", "电流" });
            signaltype.SelectedIndex = 0;

            labelreadtime = new LabelControl { Text = "稳定读取时间(ms):", Location = new Point(370, 62) };
            readtime = new TextEdit { Location = new Point(510, 60), Width = 120 };

            labelratingVoltagecurrent = new LabelControl { Text = "额定电压/电流(V/A):", Location = new Point(50, 102) };
            ratingVoltagecurrent = new TextEdit { Location = new Point(210, 100), Width = 120 };

            labelcalibrationnumber = new LabelControl { Text = "校准点个数:", Location = new Point(370, 102) };
            calibrationnumber = new TextEdit { Location = new Point(510, 100), Width = 120 };

            labelcalibrationaccuracy = new LabelControl { Text = "校准精度范围:", Location = new Point(50, 142) };
            calibrationaccuracy = new TextEdit { Location = new Point(210, 140), Width = 120 };

            // 添加确定按钮
            btnOK = new SimpleButton
            {
                Text = "确定",
                DialogResult = DialogResult.None,
                Location = new Point(200, calibrationaccuracy.Bottom + 30),
                Size = new Size(80, 30)
            };
            btnCancel = new SimpleButton
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(400, calibrationaccuracy.Bottom + 30),
                Size = new Size(80, 30)
            };

            btnOK.Click += BtnOK_Click;

            this.Size = new Size(700, btnCancel.Bottom + 60); // 调整窗体大小以适应新布局

            // 添加所有控件到表单
            this.Controls.AddRange(new Control[]
            {
                labeldevicename, devicename,
                labelsignalname, signalname,
                labelsignaltype, signaltype,
                labelreadtime, readtime,
                labelratingVoltagecurrent, ratingVoltagecurrent,
                labelcalibrationnumber, calibrationnumber,
                labelcalibrationaccuracy, calibrationaccuracy,
                btnOK, btnCancel
            });
        }

        private void Devicename_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(devicename.Text))
                return;

            // 从设备名称中提取基础设备名称（去掉通道号部分）
            string baseDeviceName = devicename.Text.Split('-')[0];
            string baseChannelName = devicename.Text.Split('-')[1];
            // 在equipmentList中查找对应设备并获取其通信协议
            EquipmentModel selectedEquipment = equipmentList
                .FirstOrDefault(e => e.DeviceName == baseDeviceName);

            if (selectedEquipment != null)
            {
                // 获取设备的通信协议
                string communicationProtocol = selectedEquipment.CommunicationProtocols;

                // 根据通信协议加载相应的信号名称
                LoadSignalNamesByProtocol(baseChannelName,communicationProtocol);

                signalname.SelectedIndex = 0;
            }
        }

        private void LoadSignalNamesByProtocol(string baseChannelName, string communicationProtocol)
        {
            // 清空现有信号名称
            signalname.Properties.Items.Clear();

            using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
            {
                conn.Open();
                long fileId = SQLite_Service.GetDbcFileId(conn, communicationProtocol);
                var messages = SQLite_Service.GetMessagesByDbc(conn, fileId);

                // 使用HashSet来避免重复的信号名称
                HashSet<string> signalNames = new HashSet<string>();

                // 获取通道类型（AC或DC）
                string channelType = baseChannelName.Substring(0, 2);

                foreach (var msg in messages)
                {
                    var signals = SQLite_Service.GetSignalsByMessage(conn, msg.MessageID);
                    foreach (var signal in signals)
                    {
                        // 根据通道类型和信号的CANID筛选信号
                        bool shouldAdd = false;

                        if (channelType == "AC")
                        {
                            // 检查信号的CANID是否包含"AX"（AC通道）
                            if (signal.CANID.Contains("AX"))
                            {
                                shouldAdd = true;
                            }
                        }
                        else if (channelType == "DC")
                        {
                            // 检查信号的CANID是否包含"2X"（DC通道）
                            if (signal.CANID.Contains("2X"))
                            {
                                shouldAdd = true;
                            }
                        }

                        if (!shouldAdd)
                            continue;

                        // 检查信号名称的后两位是否包含"电压"或"电流"
                        if (signal.SystemName.Length >= 2)
                        {
                            string lastTwoChars = signal.SystemName.Substring(signal.SystemName.Length - 2);
                            if (lastTwoChars.Contains("电压") || lastTwoChars.Contains("电流"))
                            {
                                signalNames.Add(signal.SystemName);
                            }
                        }
                    }
                }

                // 将信号名称添加到下拉框
                foreach (string name in signalNames)
                {
                    signalname.Properties.Items.Add(name);
                }
            }
        }

        private void BtnOK_Click(object? sender, EventArgs e)
        {
            // 验证输入
            if (string.IsNullOrEmpty(devicename.Text))
            {
                XtraMessageBox.Show("请选择设备名称!");
                devicename.Focus();
                return;
            }

            if (string.IsNullOrEmpty(signalname.Text))
            {
                XtraMessageBox.Show("请输入信号名称!");
                signalname.Focus();
                return;
            }

            if (string.IsNullOrEmpty(signaltype.Text))
            {
                XtraMessageBox.Show("请选择信号类型!");
                signaltype.Focus();
                return;
            }

            if (string.IsNullOrEmpty(readtime.Text) || !int.TryParse(readtime.Text, out _))
            {
                XtraMessageBox.Show("请输入有效的稳定读取时间(整数)!");
                readtime.Focus();
                return;
            }

            if (string.IsNullOrEmpty(ratingVoltagecurrent.Text) || !double.TryParse(ratingVoltagecurrent.Text, out _))
            {
                XtraMessageBox.Show("请输入有效的额定电压/电流值(数字)!");
                ratingVoltagecurrent.Focus();
                return;
            }

            if (Convert.ToInt32(calibrationnumber.Text) < 3)
            {
                XtraMessageBox.Show("校准点个数至少为3!");
                calibrationnumber.Focus();
                return;
            }

            // 验证校准精度格式
            if (string.IsNullOrEmpty(calibrationaccuracy.Text))
            {
                XtraMessageBox.Show("校准精度不能为空!");
                calibrationaccuracy.Focus();
                return;
            }

            // 验证格式是否为数字后跟百分号
            if (!System.Text.RegularExpressions.Regex.IsMatch(calibrationaccuracy.Text, @"^\d+(\.\d+)?%$"))
            {
                XtraMessageBox.Show("校准精度格式不正确，应为数字后跟百分号，例如: 0.05%");
                calibrationaccuracy.Focus();
                return;
            }

            // 保存数据
            SaveSignalData();
        }

        private void LoadSignalData()
        {
            LoadDropdownBox();

            if (signalId.HasValue)
            {
                string connectionString = $"Data Source={sqladdress};Version=3;";

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    var signal = SQLite_Service.GetCalibrationSignalById(conn, signalId.Value);

                    if (signal != null)
                    {
                        // 填充表单控件
                        devicename.Text = signal.DeviceName;
                        signalname.Text = signal.SignalName;
                        signaltype.Text = signal.SignalType;
                        readtime.Text = signal.ReadTime.ToString();
                        ratingVoltagecurrent.Text = signal.RatingVoltageCurrent.ToString();
                        calibrationnumber.Text = signal.CalibrationNumber.ToString();
                        calibrationaccuracy.Text = signal.CalibrationAccuracy;
                    }
                }
            }
        }

        private void LoadDropdownBox()
        {

            // 清空现有项
            devicename.Properties.Items.Clear();

            //加载设备名称及通道号下拉框
            // 从equipmentList中筛选设备类型为"充放电设备"的设备
            
            foreach (var equipment in equipmentList)
            {
                //devicename.Properties.Items.Add($"{equipment.DeviceName}");
                //// 处理AC通道
                for (int i = 0; i < equipment.ACNumber; i++)
                {
                    devicename.Properties.Items.Add($"{equipment.DeviceName}-AC{i + 1}");
                }

                // 处理DC通道
                for (int i = 0; i < equipment.DCNumber; i++)
                {
                    devicename.Properties.Items.Add($"{equipment.DeviceName}-DC{i + 1}");
                }
            }
            devicename.SelectedIndex = 0;
        }

        private void SaveSignalData()
        {
            string connectionString = $"Data Source={sqladdress};Version=3;";

            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    var signal = new CalibrationSignals
                    {
                        DeviceName = devicename.Text,
                        SignalName = signalname.Text,
                        SignalType = signaltype.Text,
                        ReadTime = Convert.ToInt32(readtime.Text),
                        RatingVoltageCurrent = Convert.ToInt32(ratingVoltagecurrent.Text),
                        CalibrationNumber = Convert.ToInt32(calibrationnumber.Text),
                        CalibrationAccuracy = calibrationaccuracy.Text
                    };

                    if (signalId.HasValue)
                    {
                        // 更新现有信号
                        signal.SignalID = signalId.Value;

                        // 获取现有信号的排序值
                        var existingSignal = SQLite_Service.GetCalibrationSignalById(conn, signalId.Value);

                        if (existingSignal != null)
                        {
                            signal.Orders = existingSignal.Orders;
                        }

                        SQLite_Service.UpdateCalibrationSignal(conn, signal);
                        //XtraMessageBox.Show("信号更新成功！");
                    }
                    else
                    {
                        // 插入新信号，自动设置排序值为最大值+1
                        int maxOrder = SQLite_Service.GetMaxOrderValue(conn);
                        signal.Orders = maxOrder + 1;
                        SQLite_Service.InsertCalibrationSignal(conn, signal);
                        //XtraMessageBox.Show("信号添加成功！");
                    }
                }

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"保存信号时出错: {ex.Message}");
            }
        }
    }
}