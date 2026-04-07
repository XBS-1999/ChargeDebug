using ChargeDebug.Service;
using DevExpress.Pdf.Native.BouncyCastle.Ocsp;
using DevExpress.XtraEditors;
using DevExpress.XtraTab;
using Log;
using System.Net;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class VoltageTempMonitor : XtraUserControl
    {
        #region 原有字段（完全保留）
        // 配置
        private string _ip = "192.168.1.100";
        private int _port = 54321;
        private int _voltageCount = 128;
        private int _tempCount = 128;
        private int _freq = 1000;

        // 动态控件
        private FlowLayoutPanel _panelVoltage;
        private FlowLayoutPanel _panelTemp;
        private Dictionary<int, Label> _dicVoltage = new();
        private Dictionary<int, Label> _dicTemp = new();

        // UI
        private TextEdit txtIp, txtPort, txtVoltageCount, txtTempCount, txtFreq;
        private SimpleButton btnStart, btnStop;
        private Label lblStatus, lblCount;
        private long _recvCount;
        #endregion

        public VoltageTempMonitor()
        {
            InitializeComponent();
            InitUI(); // UI完全不变
            LoadDefaultConfig();
            SubscribeServerEvents(); // 新增：订阅服务事件
        }

        #region 初始化界面（完全保留原有代码）
        private void InitUI()
        {
            // 配置区
            var groupConfig = new GroupControl
            {
                Text = "采集仪服务器配置",
                Dock = DockStyle.Top,
                Height = 120
            };
            
            // 第一行：IP + 端口 + 按钮
            Label label1 = new Label { Text = "服务器IP：", Location = new(20, 40), AutoSize = true };
            txtIp = new TextEdit { Location = new(100, 35), Width = 160 };
            groupConfig.Controls.Add(label1);
            groupConfig.Controls.Add(txtIp);

            Label label2 = new Label { Text = "端口：", Location = new(280, 40), AutoSize = true };
            txtPort = new TextEdit { Location = new(330, 35), Width = 100 };
            groupConfig.Controls.Add(label2);
            groupConfig.Controls.Add(txtPort);

            btnStart = new SimpleButton { Text = "启动服务", Location = new(480, 40), Width = 120 };
            btnStop = new SimpleButton { Text = "停止服务", Location = new(610, 40), Width = 120, Enabled = false };
            groupConfig.Controls.Add(btnStart);
            groupConfig.Controls.Add(btnStop);

            // 第二行：电压/温度通道数
            Label label3 = new Label { Text = "电压通道数：", Location = new(20, 85), AutoSize = true };
            txtVoltageCount = new TextEdit { Location = new(label3.Right + 10, 80), Width = 100 };
            groupConfig.Controls.Add(label3);
            groupConfig.Controls.Add(txtVoltageCount);

            Label label4 = new Label { Text = "温度通道数：", Location = new(txtVoltageCount.Right +10, 85), AutoSize = true };
            txtTempCount = new TextEdit { Location = new(label4.Right + 10, 80), Width = 100 };
            groupConfig.Controls.Add(label4);
            groupConfig.Controls.Add(txtTempCount);

            Label label5 = new Label { Text = "数据上传速率：", Location = new(txtTempCount.Right + 10, 85), AutoSize = true };
            txtFreq = new TextEdit { Location = new(label5.Right + 10, 80), Width = 100 };
            groupConfig.Controls.Add(label5);
            groupConfig.Controls.Add(txtFreq);

            // 状态区
            var groupState = new GroupControl
            {
                Text = "运行状态",
                Dock = DockStyle.Bottom,
                Height = 50
            };
            
            lblStatus = new Label { Text = "未连接", Location = new(20, 30), AutoSize = true, ForeColor = Color.Red };
            lblCount = new Label { Text = "接收：0 条", Location = new(300, 30), AutoSize = true };
            groupState.Controls.Add(lblStatus);
            groupState.Controls.Add(lblCount);

            // 数据分页（完全保留）
            XtraTabControl tabControl = new XtraTabControl();
            tabControl.Dock = DockStyle.Fill;
            Controls.Add(tabControl);
            Controls.Add(groupState);
            Controls.Add(groupConfig);

            XtraTabPage tabVoltage = new XtraTabPage { Text = "电压实时数据 (V)" };
            tabControl.TabPages.Add(tabVoltage);
            _panelVoltage = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            tabVoltage.Controls.Add(_panelVoltage);

            XtraTabPage tabTemp = new XtraTabPage { Text = "温度实时数据 (℃)" };
            tabControl.TabPages.Add(tabTemp);
            _panelTemp = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            tabTemp.Controls.Add(_panelTemp);

            // 按钮事件
            btnStart.Click += BtnStart_Click;
            btnStop.Click += BtnStop_Click;
        }

        private void LoadDefaultConfig()
        {
            txtIp.Text = _ip;
            txtPort.Text = _port.ToString();
            txtVoltageCount.Text = _voltageCount.ToString();
            txtTempCount.Text = _tempCount.ToString();
            txtFreq.Text = _freq.ToString();
        }
        #endregion

        #region 新增：订阅服务事件
        private void SubscribeServerEvents()
        {
            TcpServerService.Instance.OnDataReceived += Instance_OnDataReceived;
            TcpServerService.Instance.OnClientStatusChanged += Instance_OnClientStatusChanged;
            TcpServerService.Instance.OnLog += msg => LogService.Log(msg);
        }

        private void Instance_OnDataReceived(TcpServerService.DeviceType type, int channel, double value)
        {
            Invoke(new Action(() =>
            {
                _recvCount++;
                UpdateCount();
                if (type == TcpServerService.DeviceType.Voltage)
                    UpdateVoltage(channel, value);
                else
                    UpdateTemp(channel, value);
            }));
        }

        private void Instance_OnClientStatusChanged(string ip, bool connected)
        {
            Invoke(new Action(() =>
            {
                string status = connected ? "已连接" : "已断开";
                SetStatus(true, $"服务器运行中 | {ip} {status}");
            }));
        }
        #endregion

        #region 按钮事件（改为调用单例服务）
        private void BtnStart_Click(object sender, EventArgs e)
        {
            try
            {
                if (!IPAddress.TryParse(txtIp.Text, out _) ||
                    !int.TryParse(txtPort.Text, out int port) ||
                    !int.TryParse(txtVoltageCount.Text, out int vCount) ||
                    !int.TryParse(txtTempCount.Text, out int tCount) ||
                    !int.TryParse(txtFreq.Text, out int frep))
                {
                    XtraMessageBox.Show("请输入有效配置");
                    return;
                }

                _voltageCount = vCount;
                _tempCount = tCount;
                _freq = frep;
                GenerateVoltageControls(_voltageCount);
                GenerateTempControls(_tempCount);

                TcpServerService.Instance.StartServer(txtIp.Text, port);
                TcpServerService.Instance.UploadFrequencyValue = _freq;

                btnStart.Enabled = false;
                btnStop.Enabled = true;
                SetStatus(true, "服务器已启动");
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"启动失败：{ex.Message}");
            }
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            TcpServerService.Instance.StopServer();
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            SetStatus(false, "已断开");
        }
        #endregion

        #region 动态创建控件（完全保留）
        private void GenerateVoltageControls(int count)
        {
            _panelVoltage.Controls.Clear();
            _dicVoltage.Clear();
            for (int i = 1; i <= count; i++)
            {
                var group = new GroupBox { Text = $"电压 {i:D2}", Width = 112, Height = 70 };
                var label = new Label
                {
                    Text = "0.0000",
                    Font = new Font("Arial", 12, FontStyle.Bold),
                    Location = new(25, 30),
                    ForeColor = Color.Blue
                };
                group.Controls.Add(label);
                _panelVoltage.Controls.Add(group);
                _dicVoltage[i] = label;
            }
        }

        private void GenerateTempControls(int count)
        {
            _panelTemp.Controls.Clear();
            _dicTemp.Clear();
            for (int i = 1; i <= count; i++)
            {
                var group = new GroupBox { Text = $"温度 {i:D2}", Width = 112, Height = 70 };
                var label = new Label
                {
                    Text = "00.00",
                    Font = new Font("Arial", 12, FontStyle.Bold),
                    Location = new(25, 30),
                    ForeColor = Color.DarkRed
                };
                group.Controls.Add(label);
                _panelTemp.Controls.Add(group);
                _dicTemp[i] = label;
            }
        }
        #endregion

        #region UI刷新（完全保留）
        private void UpdateVoltage(int channel, double value)
        {
            if (_dicVoltage.ContainsKey(channel))
                _dicVoltage[channel].Text = value.ToString("F4");
        }

        private void UpdateTemp(int channel, double value)
        {
            if (_dicTemp.ContainsKey(channel))
                _dicTemp[channel].Text = value.ToString("F2");
        }

        private void UpdateCount()
        {
            lblCount.Text = $"接收：{_recvCount} 条";
        }

        private void SetStatus(bool connected, string text)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = connected ? Color.Green : Color.Red;
        }
        #endregion

        #region 释放（完全保留）
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                TcpServerService.Instance.StopServer();
            }
            base.Dispose(disposing);
        }
        #endregion
    }
}