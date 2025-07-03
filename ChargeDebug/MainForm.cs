using DevExpress.XtraBars.Ribbon;
using DevExpress.XtraBars;
using DevExpress.XtraEditors;
using DevExpress.Utils;
using DevExpress.XtraBars.Navigation;
using ChargeDebug.Form;
using System.Data.SQLite;
using ChargeDebug.Service;
using System.ComponentModel;
using System.Runtime.InteropServices;
using DataModel;
using Log;

namespace ChargeDebug
{
    public partial class MainForm : RibbonForm
    {
        private Type _currentPageType;

        private static string dbPath = "";       //���ݿ��ַ
        private string _userPermissions;

        private List<EquipmentModel> equipmentList = new List<EquipmentModel>();

        private LogViewer logViewer;  // ��־�鿴��

        private BarButtonItem buttonItem1;
        private BarButtonItem buttonItem2;
        private BarButtonItem buttonItem3;
        private BarButtonItem buttonItem4;
        private BarButtonItem buttonItem5;
        private BarButtonItem buttonItem6;
        private BarButtonItem buttonItem7;
        private BarButtonItem logButton; // ������־��ť
        private BarButtonItem buttonItem8;

        public MainForm(string dbcPath, string userPermissions, string username)
        {
            dbPath = dbcPath;
            _userPermissions = userPermissions;

            InitializeComponent();
            
            // ������־�¼�
            LogService.LogAdded += OnLogAdded;
            
            ReadDeviceConfig();       //��ʼ����ȡ�豸��������
            
            InitializeUI(); 
            LogService.Log("====== Ӧ�ó������� ======");
            LogService.Log($"����ʱ��: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            LogService.Log($"����ϵͳ: {RuntimeInformation.OSDescription}");
            LogService.Log($"����ID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
            LogService.Log($"����Ŀ¼: {Environment.CurrentDirectory}");
            
            //this.Load += MainForm_Load;
        }

        private void OnLogAdded(string logEntry)
        {
            if(logViewer == null || logViewer.IsDisposed)
            {
                return;
            }
            try
            {
                if (logViewer.InvokeRequired)
                {
                    // ʹ��BeginInvoke����������־�߳�
                    logViewer.BeginInvoke(new Action(() =>
                    {
                        if (!logViewer.IsDisposed)
                        {
                            logViewer.AddLogEntry(logEntry);
                        }
                    }));
                }
                else
                {
                    if (!logViewer.IsDisposed)
                    {
                        logViewer.AddLogEntry(logEntry);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // �������ͷŵĶ���
            }
            catch (InvalidOperationException)
            {
                // ������Ч������������ڹرգ�
            }
            catch (Exception ex)
            {
                // ��¼�����쳣��������ݹ���ã�
                XtraMessageBox.Show($"��־����ʧ��: {ex.Message}");
            }
        }

        private void MainForm_Load(object? sender, EventArgs e)
        {
            //�����ݿ��ȡ�豸����
            //ReadDeviceConfig();
            //InitializeUI();
        }

        // ˢ���豸����
        public void RefreshDeviceConfig(bool a,bool b, bool c)
        {
            LogService.Log("�����Ѹ��£����¼��أ�");
            ReadDeviceConfig();       // ���¶�ȡ����
            // ����Surveillanceҳ��
            if (a)
            {
                if (pages.TryGetValue(typeof(Surveillance), out var surveillancePage)
                && surveillancePage is Surveillance surveillance)
                {
                    surveillance.UpdateDcNumber(equipmentList);
                    LogService.Log("��ع���ҳ���Ѹ���");
                }
            }

            // ����Parameterҳ��
            if (b)
            {
                if (pages.TryGetValue(typeof(Parameter), out var paramPage)
                    && paramPage is Parameter parameter)
                {
                    parameter.UpdateParameters(equipmentList);
                    LogService.Log("��������ҳ���Ѹ���");
                }

                if (pages.TryGetValue(typeof(Faultrecording), out var faultrecordingPage)
                && faultrecordingPage is Faultrecording faultrecording)
                {
                    faultrecording.UpdateDcNumber(equipmentList);
                    LogService.Log("����¼��ҳ���Ѹ���");
                }

                if (pages.TryGetValue(typeof(Upgradeonline), out var upgradeonlinePage)
                && upgradeonlinePage is Upgradeonline upgradeonline)
                {
                    upgradeonline.UpdateDcNumber(equipmentList);
                    LogService.Log("��������ҳ���Ѹ���");
                }
            }

            // ����Agreementҳ��
            if(c)
            {
                if (pages.TryGetValue(typeof(Agreement), out var agreementPage)
                && agreementPage is Agreement agreement)
                {
                    agreement.UpdateAgreements(equipmentList);
                    LogService.Log("Э�����ҳ���Ѹ���");
                }
            }
        }

        //��ȡ�豸��������
        private void ReadDeviceConfig()
        {
            equipmentList.Clear(); // ��վ�����

            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    equipmentList = SQLite_Service.GetEquipment(conn);
                }
            }
            catch (Exception ex)
            {
                throw new ApplicationException("��������ʱ��������", ex);
            }
        }

        private void InitializeUI()
        {
            //����������
            this.Text = "��ŵ�������";
            this.ClientSize = new Size(1200, 800);
            
            //��ʼ��RibbonControl
            InitializeRibbon();
            
            //��ʼ��TabbedMdiManager
            InitializeTabbedMdi();
        }

        private void InitializeRibbon()
        {
            // ����RibbonControl
            RibbonControl ribbon = new RibbonControl();
            ribbon.ToolbarLocation = RibbonQuickAccessToolbarLocation.Hidden;
            ribbon.AllowMinimizeRibbon = false;
            ribbon.ShowExpandCollapseButton = DefaultBoolean.False;
            ribbon.ShowApplicationButton = DefaultBoolean.False;
            ribbon.ShowFullScreenButton = DefaultBoolean.False;
            //ribbon.ShowPageHeadersMode = ShowPageHeadersMode.Hide;
            this.Controls.Add(ribbon);

            // ������ҳǩ��ӵ�Ribbon
            RibbonPage homePage1 = new RibbonPage("��ҳ");
            ribbon.Pages.AddRange(new[] { homePage1 });

            // ����������
            RibbonPageGroup group1 = new RibbonPageGroup("���ù���");
            //RibbonPageGroup group2 = new RibbonPageGroup("ϵͳ����");
            // ����������ӵ���ҳǩ
            homePage1.Groups.AddRange(new[] { group1 });

            // ������ͼ��İ�ť��
            buttonItem1 = new BarButtonItem();
            buttonItem2 = new BarButtonItem();
            buttonItem3 = new BarButtonItem();
            buttonItem4 = new BarButtonItem();
            buttonItem5 = new BarButtonItem();
            buttonItem6 = new BarButtonItem();
            buttonItem7 = new BarButtonItem();
            logButton = new BarButtonItem(); // ������־��ť
            buttonItem8 = new BarButtonItem();

            buttonItem1.Caption = "��ع���";
            buttonItem2.Caption = "��������";
            buttonItem3.Caption = "�豸����";
            buttonItem4.Caption = "Э�����";
            buttonItem5.Caption = "�û�����";
            buttonItem8.Caption = "�û��л�";
            buttonItem6.Caption = "����¼��";
            buttonItem7.Caption = "��������";
            logButton.Caption = "ϵͳ��־"; // ������ť
            
            buttonItem1.RibbonStyle = RibbonItemStyles.All;
            buttonItem2.RibbonStyle = RibbonItemStyles.All;
            buttonItem3.RibbonStyle = RibbonItemStyles.All;
            buttonItem4.RibbonStyle = RibbonItemStyles.All;
            buttonItem5.RibbonStyle = RibbonItemStyles.All;
            buttonItem8.RibbonStyle = RibbonItemStyles.All;
            buttonItem6.RibbonStyle = RibbonItemStyles.All;
            buttonItem7.RibbonStyle = RibbonItemStyles.All;
            logButton.RibbonStyle = RibbonItemStyles.All;
            
            //buttonItem1.RibbonStyle = RibbonItemStyles.Large;
            //buttonItem1.ButtonStyle = BarButtonStyle.DropDown;
            //buttonItem2.ButtonStyle = BarButtonStyle.DropDown;
            //buttonItem3.ButtonStyle = BarButtonStyle.DropDown;
            //buttonItem4.ButtonStyle = BarButtonStyle.DropDown;
            buttonItem1.ImageOptions.Image = Properties.Resources.���ݼ��;
            buttonItem2.ImageOptions.Image = Properties.Resources.ʵʱ����;
            buttonItem3.ImageOptions.Image = Properties.Resources.ϵͳ����;
            buttonItem4.ImageOptions.Image = Properties.Resources.Э����Ϣ;
            buttonItem5.ImageOptions.Image = Properties.Resources.�û�����;
            buttonItem8.ImageOptions.Image = Properties.Resources.�û��л�;
            buttonItem6.ImageOptions.Image = Properties.Resources.����ͳ��;
            buttonItem7.ImageOptions.Image = Properties.Resources.��������;
            logButton.ImageOptions.Image = Properties.Resources.��־����; // ��������־ͼ����Դ

            group1.ItemLinks.AddRange(new[] { buttonItem1, buttonItem2, buttonItem3, buttonItem4, buttonItem6, buttonItem7, buttonItem5, buttonItem8, logButton });

            buttonItem1.ItemClick += (s, e) => ShowPage(typeof(Surveillance));
            buttonItem2.ItemClick += (s, e) => ShowPage(typeof(Parameter));
            buttonItem3.ItemClick += (s, e) => ShowPage(typeof(Equipment));
            buttonItem4.ItemClick += (s, e) => ShowPage(typeof(Agreement));
            buttonItem5.ItemClick += (s, e) => ShowPage(typeof(User));
            buttonItem6.ItemClick += (s, e) => ShowPage(typeof(Faultrecording));
            buttonItem7.ItemClick += (s, e) => ShowPage(typeof(Upgradeonline));
            logButton.ItemClick += (s, e) => ShowPage(typeof(LogViewer)); // ��־��ť�¼�

            buttonItem8.ItemClick += (s, e) => SwitchUser();

            // �����û�Ȩ�����ð�ť�ɼ���
            SetPermissions();
        }

        private void SwitchUser()
        {
            DialogResult confirm = XtraMessageBox.Show("ȷ��Ҫ�л��û���",
                "�л��û�",  MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                LogService.Log("�û������л��˺�");
                this.DialogResult = DialogResult.Retry; // �����ʶ
                this.Close();
            }
        }

        private void SetPermissions()
        {
            // ����Ա����ʾ���й���
            if (_userPermissions == "����Ա")
            {
                // ���а�ťĬ�Ͽɼ�
                return;
            }

            // ����Ա�������û�����
            if (_userPermissions == "����Ա")
            {
                buttonItem4.Visibility = BarItemVisibility.Never;  // Э�����
                buttonItem5.Visibility = BarItemVisibility.Never;  // �û�����
                buttonItem6.Visibility = BarItemVisibility.Never;  // ����¼��
                //buttonItem7.Visibility = BarItemVisibility.Never;  // ��������
            }
            // ��ͨ�û���ֻ��ʾ���ֹ���
            else if (_userPermissions == "��ͨ�û�")
            {
                buttonItem2.Visibility = BarItemVisibility.Never;  // ��������
                buttonItem3.Visibility = BarItemVisibility.Never;  // �豸����
                buttonItem4.Visibility = BarItemVisibility.Never;  // Э�����
                buttonItem5.Visibility = BarItemVisibility.Never;  // �û�����
                buttonItem6.Visibility = BarItemVisibility.Never;  // ����¼��
                buttonItem7.Visibility = BarItemVisibility.Never;  // ��������
            }

            // ��״̬����ʾ��ǰ�û���Ϣ
            //statusBar.Caption = $"��ǰ�û�: {_username} ({_userPermissions})";
        }

        private void InitializeTabbedMdi()
        {
            //�����������
            navigationFrame.Dock = DockStyle.Fill;
            navigationFrame.BringToFront();
            this.Controls.Add(navigationFrame);
            
            // ������־�鿴��ʵ��
            logViewer = new LogViewer();

            //ע��ҳ��
            RegisterPage(typeof(Surveillance), new Surveillance(dbPath,equipmentList));
            RegisterPage(typeof(Parameter), new Parameter(dbPath, equipmentList));
            RegisterPage(typeof(Equipment), new Equipment(dbPath));
            RegisterPage(typeof(Agreement), new Agreement(dbPath, equipmentList));
            RegisterPage(typeof(User), new User(dbPath));
            RegisterPage(typeof(Faultrecording), new Faultrecording(dbPath, equipmentList));
            RegisterPage(typeof(Upgradeonline), new Upgradeonline(equipmentList));
            RegisterPage(typeof(LogViewer), logViewer); // ע����־ҳ��
            
            // ������ʷ��־
            //logViewer.LoadInitialLogs(LogService.GetAllLogs());

            //��ʾ��ҳ
            ShowPage(typeof(Surveillance));
        }

        private Dictionary<Type, XtraUserControl> pages = new Dictionary<Type, XtraUserControl>();
        private NavigationFrame navigationFrame = new NavigationFrame();

        // �޸�RegisterPage����
        private void RegisterPage(Type pageType, XtraUserControl page)
        {
            if (page is Equipment equipmentPage)
            {
                equipmentPage.ConfigUpdated += (s, e) => RefreshDeviceConfig(true,true,true);
            }
            else if (page is Agreement agreementPage)
            {
                agreementPage.ConfigUpdated += (s, e) => RefreshDeviceConfig(true, true, false);
            }

            pages[pageType] = page;
            page.Dock = DockStyle.Fill;
        }

        // ���һ����������������ģ��ķ���״̬
        public void SetAllPagesSendingEnabled(bool enabled, Type exceptPageType = null)
        {
            foreach (Control control in navigationFrame.Controls)
            {
                // ����ָ�����͵�ҳ��
                if (exceptPageType != null && control.GetType() == exceptPageType)
                    continue;

                // ���� Surveillance ҳ��
                if (control is Surveillance surveillance)
                {
                    surveillance.SetModuleSendingEnabled(enabled);
                }
            }
        }

        // �޸� ShowPage ����
        private void ShowPage(Type pageType)
        {
            // ����ҳ���л�ǰ��״̬
            HandlePageLeaving(_currentPageType);

            if (pages.TryGetValue(pageType, out XtraUserControl page))
            {
                if (!navigationFrame.Controls.Contains(page))
                {
                    navigationFrame.Controls.Add(page);
                }
                page.BringToFront();

                // ������ҳ�����״̬
                HandlePageEntering(pageType);

                _currentPageType = pageType;
            }
        }

        private void HandlePageLeaving(Type leavingPageType)
        {
            if (leavingPageType == typeof(Upgradeonline))
            {
                // �뿪��������ҳ�� - ��������ҳ�淢��
                SetAllPagesSendingEnabled(true);
                LogService.Log("�뿪��������ҳ�棬��������ҳ�淢��");
            }
        }

        private void HandlePageEntering(Type enteringPageType)
        {
            if (enteringPageType == typeof(Upgradeonline))
            {
                // ������������ҳ�� - ��������ҳ�淢��
                SetAllPagesSendingEnabled(false, typeof(Upgradeonline));
                LogService.Log("������������ҳ�棬��������ҳ�淢��");
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // �û��л���������ʾ�˳�ȷ��
            if (this.DialogResult != DialogResult.Retry)
            {
                DialogResult result = XtraMessageBox.Show("ȷ��Ҫ�˳�����ϵͳ��",
                    "�˳�ȷ��",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                LogService.Log("====== Ӧ�ó���ر� ======");
            }
            else
            {
                LogService.Log("====== �û��л����� ======");
            }

            LogService.Log($"�ر�ʱ��: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            LogService.Flush();
            CANManager.Instance.Dispose();

            base.OnClosing(e);
        }
    }
}
