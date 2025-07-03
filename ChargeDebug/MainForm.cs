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

        private static string dbPath = "";       //数据库地址
        private string _userPermissions;

        private List<EquipmentModel> equipmentList = new List<EquipmentModel>();

        private LogViewer logViewer;  // 日志查看器

        private BarButtonItem buttonItem1;
        private BarButtonItem buttonItem2;
        private BarButtonItem buttonItem3;
        private BarButtonItem buttonItem4;
        private BarButtonItem buttonItem5;
        private BarButtonItem buttonItem6;
        private BarButtonItem buttonItem7;
        private BarButtonItem logButton; // 新增日志按钮
        private BarButtonItem buttonItem8;

        public MainForm(string dbcPath, string userPermissions, string username)
        {
            dbPath = dbcPath;
            _userPermissions = userPermissions;

            InitializeComponent();
            
            // 订阅日志事件
            LogService.LogAdded += OnLogAdded;
            
            ReadDeviceConfig();       //初始化读取设备管理配置
            
            InitializeUI(); 
            LogService.Log("====== 应用程序启动 ======");
            LogService.Log($"启动时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            LogService.Log($"操作系统: {RuntimeInformation.OSDescription}");
            LogService.Log($"进程ID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
            LogService.Log($"工作目录: {Environment.CurrentDirectory}");
            
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
                    // 使用BeginInvoke避免阻塞日志线程
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
                // 忽略已释放的对象
            }
            catch (InvalidOperationException)
            {
                // 忽略无效操作（如表单正在关闭）
            }
            catch (Exception ex)
            {
                // 记录其他异常（但避免递归调用）
                XtraMessageBox.Show($"日志更新失败: {ex.Message}");
            }
        }

        private void MainForm_Load(object? sender, EventArgs e)
        {
            //从数据库读取设备配置
            //ReadDeviceConfig();
            //InitializeUI();
        }

        // 刷新设备配置
        public void RefreshDeviceConfig(bool a,bool b, bool c)
        {
            LogService.Log("数据已更新，重新加载！");
            ReadDeviceConfig();       // 重新读取配置
            // 更新Surveillance页面
            if (a)
            {
                if (pages.TryGetValue(typeof(Surveillance), out var surveillancePage)
                && surveillancePage is Surveillance surveillance)
                {
                    surveillance.UpdateDcNumber(equipmentList);
                    LogService.Log("监控管理页面已更新");
                }
            }

            // 更新Parameter页面
            if (b)
            {
                if (pages.TryGetValue(typeof(Parameter), out var paramPage)
                    && paramPage is Parameter parameter)
                {
                    parameter.UpdateParameters(equipmentList);
                    LogService.Log("参数管理页面已更新");
                }

                if (pages.TryGetValue(typeof(Faultrecording), out var faultrecordingPage)
                && faultrecordingPage is Faultrecording faultrecording)
                {
                    faultrecording.UpdateDcNumber(equipmentList);
                    LogService.Log("故障录波页面已更新");
                }

                if (pages.TryGetValue(typeof(Upgradeonline), out var upgradeonlinePage)
                && upgradeonlinePage is Upgradeonline upgradeonline)
                {
                    upgradeonline.UpdateDcNumber(equipmentList);
                    LogService.Log("在线升级页面已更新");
                }
            }

            // 更新Agreement页面
            if(c)
            {
                if (pages.TryGetValue(typeof(Agreement), out var agreementPage)
                && agreementPage is Agreement agreement)
                {
                    agreement.UpdateAgreements(equipmentList);
                    LogService.Log("协议管理页面已更新");
                }
            }
        }

        //读取设备管理配置
        private void ReadDeviceConfig()
        {
            equipmentList.Clear(); // 清空旧数据

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
                throw new ApplicationException("加载数据时发生错误", ex);
            }
        }

        private void InitializeUI()
        {
            //主窗体设置
            this.Text = "充放电调试软件";
            this.ClientSize = new Size(1200, 800);
            
            //初始化RibbonControl
            InitializeRibbon();
            
            //初始化TabbedMdiManager
            InitializeTabbedMdi();
        }

        private void InitializeRibbon()
        {
            // 创建RibbonControl
            RibbonControl ribbon = new RibbonControl();
            ribbon.ToolbarLocation = RibbonQuickAccessToolbarLocation.Hidden;
            ribbon.AllowMinimizeRibbon = false;
            ribbon.ShowExpandCollapseButton = DefaultBoolean.False;
            ribbon.ShowApplicationButton = DefaultBoolean.False;
            ribbon.ShowFullScreenButton = DefaultBoolean.False;
            //ribbon.ShowPageHeadersMode = ShowPageHeadersMode.Hide;
            this.Controls.Add(ribbon);

            // 创建主页签添加到Ribbon
            RibbonPage homePage1 = new RibbonPage("主页");
            ribbon.Pages.AddRange(new[] { homePage1 });

            // 创建功能组
            RibbonPageGroup group1 = new RibbonPageGroup("常用功能");
            //RibbonPageGroup group2 = new RibbonPageGroup("系统管理");
            // 将功能组添加到主页签
            homePage1.Groups.AddRange(new[] { group1 });

            // 创建带图标的按钮项
            buttonItem1 = new BarButtonItem();
            buttonItem2 = new BarButtonItem();
            buttonItem3 = new BarButtonItem();
            buttonItem4 = new BarButtonItem();
            buttonItem5 = new BarButtonItem();
            buttonItem6 = new BarButtonItem();
            buttonItem7 = new BarButtonItem();
            logButton = new BarButtonItem(); // 新增日志按钮
            buttonItem8 = new BarButtonItem();

            buttonItem1.Caption = "监控管理";
            buttonItem2.Caption = "参数管理";
            buttonItem3.Caption = "设备管理";
            buttonItem4.Caption = "协议管理";
            buttonItem5.Caption = "用户管理";
            buttonItem8.Caption = "用户切换";
            buttonItem6.Caption = "故障录波";
            buttonItem7.Caption = "在线升级";
            logButton.Caption = "系统日志"; // 新增按钮
            
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
            buttonItem1.ImageOptions.Image = Properties.Resources.数据监控;
            buttonItem2.ImageOptions.Image = Properties.Resources.实时参数;
            buttonItem3.ImageOptions.Image = Properties.Resources.系统配置;
            buttonItem4.ImageOptions.Image = Properties.Resources.协议信息;
            buttonItem5.ImageOptions.Image = Properties.Resources.用户管理;
            buttonItem8.ImageOptions.Image = Properties.Resources.用户切换;
            buttonItem6.ImageOptions.Image = Properties.Resources.故障统计;
            buttonItem7.ImageOptions.Image = Properties.Resources.在线升级;
            logButton.ImageOptions.Image = Properties.Resources.日志管理; // 假设有日志图标资源

            group1.ItemLinks.AddRange(new[] { buttonItem1, buttonItem2, buttonItem3, buttonItem4, buttonItem6, buttonItem7, buttonItem5, buttonItem8, logButton });

            buttonItem1.ItemClick += (s, e) => ShowPage(typeof(Surveillance));
            buttonItem2.ItemClick += (s, e) => ShowPage(typeof(Parameter));
            buttonItem3.ItemClick += (s, e) => ShowPage(typeof(Equipment));
            buttonItem4.ItemClick += (s, e) => ShowPage(typeof(Agreement));
            buttonItem5.ItemClick += (s, e) => ShowPage(typeof(User));
            buttonItem6.ItemClick += (s, e) => ShowPage(typeof(Faultrecording));
            buttonItem7.ItemClick += (s, e) => ShowPage(typeof(Upgradeonline));
            logButton.ItemClick += (s, e) => ShowPage(typeof(LogViewer)); // 日志按钮事件

            buttonItem8.ItemClick += (s, e) => SwitchUser();

            // 根据用户权限设置按钮可见性
            SetPermissions();
        }

        private void SwitchUser()
        {
            DialogResult confirm = XtraMessageBox.Show("确定要切换用户吗？",
                "切换用户",  MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                LogService.Log("用户请求切换账号");
                this.DialogResult = DialogResult.Retry; // 特殊标识
                this.Close();
            }
        }

        private void SetPermissions()
        {
            // 管理员：显示所有功能
            if (_userPermissions == "管理员")
            {
                // 所有按钮默认可见
                return;
            }

            // 操作员：隐藏用户管理
            if (_userPermissions == "操作员")
            {
                buttonItem4.Visibility = BarItemVisibility.Never;  // 协议管理
                buttonItem5.Visibility = BarItemVisibility.Never;  // 用户管理
                buttonItem6.Visibility = BarItemVisibility.Never;  // 故障录波
                //buttonItem7.Visibility = BarItemVisibility.Never;  // 在线升级
            }
            // 普通用户：只显示部分功能
            else if (_userPermissions == "普通用户")
            {
                buttonItem2.Visibility = BarItemVisibility.Never;  // 参数管理
                buttonItem3.Visibility = BarItemVisibility.Never;  // 设备管理
                buttonItem4.Visibility = BarItemVisibility.Never;  // 协议管理
                buttonItem5.Visibility = BarItemVisibility.Never;  // 用户管理
                buttonItem6.Visibility = BarItemVisibility.Never;  // 故障录波
                buttonItem7.Visibility = BarItemVisibility.Never;  // 在线升级
            }

            // 在状态栏显示当前用户信息
            //statusBar.Caption = $"当前用户: {_username} ({_userPermissions})";
        }

        private void InitializeTabbedMdi()
        {
            //创建导航框架
            navigationFrame.Dock = DockStyle.Fill;
            navigationFrame.BringToFront();
            this.Controls.Add(navigationFrame);
            
            // 创建日志查看器实例
            logViewer = new LogViewer();

            //注册页面
            RegisterPage(typeof(Surveillance), new Surveillance(dbPath,equipmentList));
            RegisterPage(typeof(Parameter), new Parameter(dbPath, equipmentList));
            RegisterPage(typeof(Equipment), new Equipment(dbPath));
            RegisterPage(typeof(Agreement), new Agreement(dbPath, equipmentList));
            RegisterPage(typeof(User), new User(dbPath));
            RegisterPage(typeof(Faultrecording), new Faultrecording(dbPath, equipmentList));
            RegisterPage(typeof(Upgradeonline), new Upgradeonline(equipmentList));
            RegisterPage(typeof(LogViewer), logViewer); // 注册日志页面
            
            // 加载历史日志
            //logViewer.LoadInitialLogs(LogService.GetAllLogs());

            //显示首页
            ShowPage(typeof(Surveillance));
        }

        private Dictionary<Type, XtraUserControl> pages = new Dictionary<Type, XtraUserControl>();
        private NavigationFrame navigationFrame = new NavigationFrame();

        // 修改RegisterPage方法
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

        // 添加一个方法来控制所有模块的发送状态
        public void SetAllPagesSendingEnabled(bool enabled, Type exceptPageType = null)
        {
            foreach (Control control in navigationFrame.Controls)
            {
                // 跳过指定类型的页面
                if (exceptPageType != null && control.GetType() == exceptPageType)
                    continue;

                // 处理 Surveillance 页面
                if (control is Surveillance surveillance)
                {
                    surveillance.SetModuleSendingEnabled(enabled);
                }
            }
        }

        // 修改 ShowPage 方法
        private void ShowPage(Type pageType)
        {
            // 处理页面切换前的状态
            HandlePageLeaving(_currentPageType);

            if (pages.TryGetValue(pageType, out XtraUserControl page))
            {
                if (!navigationFrame.Controls.Contains(page))
                {
                    navigationFrame.Controls.Add(page);
                }
                page.BringToFront();

                // 处理新页面进入状态
                HandlePageEntering(pageType);

                _currentPageType = pageType;
            }
        }

        private void HandlePageLeaving(Type leavingPageType)
        {
            if (leavingPageType == typeof(Upgradeonline))
            {
                // 离开在线升级页面 - 启用其他页面发送
                SetAllPagesSendingEnabled(true);
                LogService.Log("离开在线升级页面，启用其他页面发送");
            }
        }

        private void HandlePageEntering(Type enteringPageType)
        {
            if (enteringPageType == typeof(Upgradeonline))
            {
                // 进入在线升级页面 - 禁用其他页面发送
                SetAllPagesSendingEnabled(false, typeof(Upgradeonline));
                LogService.Log("进入在线升级页面，禁用其他页面发送");
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // 用户切换操作不显示退出确认
            if (this.DialogResult != DialogResult.Retry)
            {
                DialogResult result = XtraMessageBox.Show("确定要退出调试系统吗？",
                    "退出确认",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                LogService.Log("====== 应用程序关闭 ======");
            }
            else
            {
                LogService.Log("====== 用户切换操作 ======");
            }

            LogService.Log($"关闭时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            LogService.Flush();
            CANManager.Instance.Dispose();

            base.OnClosing(e);
        }
    }
}
