using ChargeDebug.Service;
using DataModel;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout;
using DevExpress.XtraLayout.Utils;
using DevExpress.XtraRichEdit.API.Layout;
using System;
using System.Data.SQLite;
using System.Drawing;
using System.Windows.Forms;

namespace ChargeDebug.Form
{
    public partial class AddEditCalibrationSignalForm : XtraForm
    {
        private long? signalId;
        private string sqladdress;

        // UI控件声明
        private ComboBoxEdit devicename;
        private TextEdit signalname;
        private ComboBoxEdit signaltype;
        private TextEdit readtime;
        private TextEdit ratingVoltagecurrent;
        private TextEdit calibrationnumber;

        private LabelControl labeldevicename;
        private LabelControl labelsignalname;
        private LabelControl labelsignaltype;
        private LabelControl labelreadtime;
        private LabelControl labelratingVoltagecurrent;
        private LabelControl labelcalibrationnumber;

        private SimpleButton btnOK;
        private SimpleButton btnCancel;

        public AddEditCalibrationSignalForm(long? signalId, string sqladdress)
        {
            this.signalId = signalId;
            this.sqladdress = sqladdress;
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

            labeldevicename = new LabelControl { Text = "设备名称:", Location = new Point(70, 22) };
            devicename = new ComboBoxEdit { Location = new Point(220, 20), Width = 100 };

            labelsignalname = new LabelControl { Text = "信号名称:", Location = new Point(370, 22) };
            signalname = new TextEdit { Location = new Point(510, 20), Width = 100 };

            labelsignaltype = new LabelControl { Text = "信号类型:", Location = new Point(70, 62) };
            signaltype = new ComboBoxEdit { Location = new Point(220, 60), Width = 100 };
            signaltype.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            signaltype.Properties.Items.AddRange(new[] { "电压", "电流" });

            labelreadtime = new LabelControl { Text = "稳定读取时间(ms):", Location = new Point(370, 62) };
            readtime = new TextEdit { Location = new Point(510, 60), Width = 100 };

            labelratingVoltagecurrent = new LabelControl { Text = "额定电压/电流(V/A):", Location = new Point(70, 102) };
            ratingVoltagecurrent = new TextEdit { Location = new Point(220, 100), Width = 100 };

            labelcalibrationnumber = new LabelControl { Text = "校准点个数:", Location = new Point(370, 102) };
            calibrationnumber = new TextEdit { Location = new Point(510, 100), Width = 100 };

            // 添加确定按钮
            btnOK = new SimpleButton
            {
                Text = "确定",
                DialogResult = DialogResult.None,
                Location = new Point(200, ratingVoltagecurrent.Bottom + 30),
                Size = new Size(80, 30)
            };
            btnCancel = new SimpleButton
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(400, ratingVoltagecurrent.Bottom + 30),
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
                btnOK, btnCancel
            });
        }

        // 其余方法保持不变...
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

            // 保存数据
            SaveSignalData();
        }

        private void LoadSignalData()
        {
            LoadDeviceNames(); // 加载设备列表

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
                    }
                }
            }
        }

        private void LoadDeviceNames()
        {
            try
            {
                string connectionString = $"Data Source={sqladdress};Version=3;";

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    var deviceNames = SQLite_Service.GetDeviceNames(conn);

                    devicename.Properties.Items.Clear();
                    foreach (var deviceName in deviceNames)
                    {
                        devicename.Properties.Items.Add(deviceName);
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"加载设备列表失败: {ex.Message}");
            }
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
                        ReadTime = Convert.ToInt64(readtime.Text),
                        RatingVoltageCurrent = ratingVoltagecurrent.Text,
                        CalibrationNumber = Convert.ToInt64(calibrationnumber.Text)
                    };

                    if (signalId.HasValue)
                    {
                        // 更新现有信号
                        signal.SignalID = signalId.Value;
                        SQLite_Service.UpdateCalibrationSignal(conn, signal);
                        //XtraMessageBox.Show("信号更新成功！");
                    }
                    else
                    {
                        // 插入新信号
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