using ChargeDebug.Form;
using ChargeDebug.Service;
using DataModel;
using DevExpress.Utils;
using DevExpress.XtraBars;
using DevExpress.XtraBars.Navigation;
using DevExpress.XtraBars.Ribbon;
using DevExpress.XtraEditors;
using Log;
using System.ComponentModel;
using System.Data.SQLite;
using System.Runtime.InteropServices;

#pragma warning disable
namespace ChargeDebug
{
    /// <summary>
    /// 主窗体类 - 充放电调试软件主界面
    /// 负责管理所有功能模块的导航和显示
    /// </summary>
    public partial class MainForm : RibbonForm
    {
        #region 私有字段
        private Type _currentPageType;
        private static string dbPath = "";       // 数据库文件路径
        private string _userPermissions;         // 当前用户权限
        private bool _isDisposing = false;       // 防止重复清理的标志

        // 设备配置列表
        private List<EquipmentModel> equipmentList = new List<EquipmentModel>();

        // UI控件
        private LogViewer logViewer;  // 日志查看器实例
        private NavigationFrame navigationFrame = new NavigationFrame();

        // 功能按钮
        private BarButtonItem buttonItem1, buttonItem2, buttonItem3, buttonItem4, buttonItem5;
        private BarButtonItem buttonItem6, buttonItem7, buttonItem8, buttonItem9, buttonItem10, buttonItem11;
        private BarButtonItem logButton, skinButton;

        // 页面缓存字典
        private Dictionary<Type, XtraUserControl> pages = new Dictionary<Type, XtraUserControl>();
        #endregion

        #region 构造函数
        /// <summary>
        /// 主窗体构造函数
        /// </summary>
        /// <param name="dbcPath">数据库文件路径</param>
        /// <param name="userPermissions">用户权限</param>
        /// <param name="username">用户名</param>
        public MainForm(string dbcPath, string userPermissions, string username)
        {
            dbPath = dbcPath;
            _userPermissions = userPermissions;

            InitializeComponent();

            // 订阅日志事件 - 注意：需要在Dispose中取消订阅
            LogService.LogAdded += OnLogAdded;

            // 初始化设备配置
            ReadDeviceConfig();

            // 初始化用户界面
            InitializeUI();

            // 记录启动日志
            LogStartupInfo();
        }

        /// <summary>
        /// 记录应用程序启动信息
        /// </summary>
        private void LogStartupInfo()
        {
            LogService.Log("====== 应用程序启动 ======");
            LogService.Log($"启动时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            LogService.Log($"操作系统: {RuntimeInformation.OSDescription}");
            LogService.Log($"进程ID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
            LogService.Log($"工作目录: {Environment.CurrentDirectory}");
        }
        #endregion

        #region 事件处理方法
        /// <summary>
        /// 日志添加事件处理 - 将日志条目添加到日志查看器
        /// 使用线程安全的方式更新UI
        /// </summary>
        /// <param name="logEntry">日志条目</param>
        private void OnLogAdded(string logEntry)
        {
            // 如果正在清理或日志查看器不可用，直接返回
            if (_isDisposing || logViewer == null || logViewer.IsDisposed)
            {
                return;
            }

            try
            {
                // 跨线程调用处理
                if (logViewer.InvokeRequired)
                {
                    // 使用BeginInvoke避免阻塞日志线程
                    logViewer.BeginInvoke(new Action(() =>
                    {
                        SafeAddLogEntry(logEntry);
                    }));
                }
                else
                {
                    SafeAddLogEntry(logEntry);
                }
            }
            catch (ObjectDisposedException)
            {
                // 忽略已释放的对象 - 正常关闭过程
            }
            catch (InvalidOperationException)
            {
                // 忽略无效操作（如表单正在关闭）
            }
            catch (Exception ex)
            {
                // 记录其他异常（但避免递归调用）
                // 注意：这里不使用LogService.Log避免可能的递归
                System.Diagnostics.Debug.WriteLine($"日志更新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 安全添加日志条目 - 包含额外的安全检查
        /// </summary>
        /// <param name="logEntry">日志条目</param>
        private void SafeAddLogEntry(string logEntry)
        {
            if (_isDisposing || logViewer == null || logViewer.IsDisposed)
            {
                return;
            }

            try
            {
                logViewer.AddLogEntry(logEntry);
            }
            catch (ObjectDisposedException)
            {
                // 忽略已释放的对象
            }
        }
        #endregion

        #region 设备配置管理
        /// <summary>
        /// 刷新设备配置 - 根据参数更新不同的功能模块
        /// </summary>
        /// <param name="updateSurveillance">是否更新监控管理</param>
        /// <param name="updateParameters">是否更新参数相关页面</param>
        /// <param name="updateAgreements">是否更新协议管理</param>
        public void RefreshDeviceConfig(bool updateSurveillance, bool updateParameters, bool updateAgreements)
        {
            LogService.Log("数据已更新，重新加载设备配置！");

            // 重新读取设备配置
            ReadDeviceConfig();

            // 根据参数更新相应的页面
            if (updateSurveillance)
            {
                UpdatePage<Surveillance>(page => page.UpdateDcNumber(equipmentList), "监控管理");
            }

            if (updateParameters)
            {
                UpdatePage<Parameter>(page => page.UpdateParameters(equipmentList), "参数管理");
                UpdatePage<FaultRecording>(page => page.UpdateDcNumber(equipmentList), "故障录波");
                UpdatePage<Upgradeonline>(page => page.UpdateDcNumber(equipmentList), "在线升级");
                UpdatePage<CalibrationManagement>(page => page.UpdateDcNumber(equipmentList), "校准管理");
            }

            if (updateAgreements)
            {
                UpdatePage<Agreement>(page => page.UpdateAgreements(equipmentList), "协议管理");
            }
        }

        /// <summary>
        /// 通用页面更新方法 - 减少重复代码
        /// </summary>
        /// <typeparam name="TPage">页面类型</typeparam>
        /// <param name="updateAction">更新操作</param>
        /// <param name="pageName">页面名称（用于日志）</param>
        private void UpdatePage<TPage>(Action<TPage> updateAction, string pageName) where TPage : class
        {
            if (pages.TryGetValue(typeof(TPage), out var page) && page is TPage typedPage)
            {
                try
                {
                    updateAction(typedPage);
                    LogService.Log($"{pageName}页面已更新");
                }
                catch (Exception ex)
                {
                    LogService.Log($"更新{pageName}页面时发生错误: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 读取设备管理配置 - 从数据库加载设备列表
        /// </summary>
        private void ReadDeviceConfig()
        {
            equipmentList.Clear(); // 清空旧数据

            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    equipmentList = SQLite_Service.GetEquipment(conn);
                    LogService.Log($"成功加载 {equipmentList.Count} 个设备配置");
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"加载设备配置时发生错误: {ex.Message}");
                throw new ApplicationException("加载设备数据时发生错误", ex);
            }
        }
        #endregion

        #region UI初始化
        /// <summary>
        /// 初始化用户界面
        /// </summary>
        private void InitializeUI()
        {
            // 主窗体设置
            this.Text = "充放电调试软件";
            this.ClientSize = new Size(1200, 800);

            // 初始化功能区
            InitializeRibbon();

            // 初始化标签式MDI管理器
            InitializeTabbedMdi();
        }

        /// <summary>
        /// 初始化Ribbon功能区
        /// </summary>
        private void InitializeRibbon()
        {
            // 创建RibbonControl
            RibbonControl ribbon = new RibbonControl();
            ConfigureRibbon(ribbon);
            this.Controls.Add(ribbon);

            // 创建主页签和功能组
            RibbonPage homePage = CreateHomePage(ribbon);
            RibbonPageGroup functionGroup = CreateFunctionGroup(homePage);

            // 创建功能按钮
            CreateFunctionButtons(functionGroup);

            // 根据用户权限设置按钮可见性
            SetPermissions();
        }

        /// <summary>
        /// 配置Ribbon控件属性
        /// </summary>
        /// <param name="ribbon">Ribbon控件</param>
        private void ConfigureRibbon(RibbonControl ribbon)
        {
            ribbon.ToolbarLocation = RibbonQuickAccessToolbarLocation.Hidden;
            ribbon.AllowMinimizeRibbon = false;
            ribbon.ShowExpandCollapseButton = DefaultBoolean.False;
            ribbon.ShowApplicationButton = DefaultBoolean.False;
            ribbon.ShowFullScreenButton = DefaultBoolean.False;
            ribbon.ShowPageHeadersMode = ShowPageHeadersMode.Hide;
        }

        /// <summary>
        /// 创建主页签
        /// </summary>
        /// <param name="ribbon">Ribbon控件</param>
        /// <returns>主页签对象</returns>
        private RibbonPage CreateHomePage(RibbonControl ribbon)
        {
            RibbonPage homePage = new RibbonPage("");
            ribbon.Pages.Add(homePage);
            return homePage;
        }

        /// <summary>
        /// 创建功能组
        /// </summary>
        /// <param name="homePage">主页签</param>
        /// <returns>功能组对象</returns>
        private RibbonPageGroup CreateFunctionGroup(RibbonPage homePage)
        {
            RibbonPageGroup group = new RibbonPageGroup("调试管理");
            group.ShowCaptionButton = false;
            homePage.Groups.Add(group);
            return group;
        }

        /// <summary>
        /// 创建功能按钮
        /// </summary>
        /// <param name="group">功能组</param>
        private void CreateFunctionButtons(RibbonPageGroup group)
        {
            // 初始化按钮
            InitializeButtons();

            // 配置按钮样式和图标
            ConfigureButtonStyles();
            ConfigureButtonIcons();

            // 添加按钮到功能组
            AddButtonsToGroup(group);

            // 绑定按钮点击事件
            BindButtonEvents();
        }

        /// <summary>
        /// 初始化所有功能按钮
        /// </summary>
        private void InitializeButtons()
        {
            buttonItem1 = new BarButtonItem { Caption = "监控管理" };
            buttonItem2 = new BarButtonItem { Caption = "参数管理" };
            buttonItem3 = new BarButtonItem { Caption = "设备管理" };
            buttonItem4 = new BarButtonItem { Caption = "协议管理" };
            buttonItem11 = new BarButtonItem { Caption = "数据分析" };
            buttonItem5 = new BarButtonItem { Caption = "用户管理" };
            buttonItem6 = new BarButtonItem { Caption = "故障录波" };
            buttonItem7 = new BarButtonItem { Caption = "在线升级" };
            buttonItem8 = new BarButtonItem { Caption = "用户切换" };
            buttonItem9 = new BarButtonItem { Caption = "校准管理" };
            buttonItem10 = new BarButtonItem { Caption = "测试管理" };
            logButton = new BarButtonItem { Caption = "系统日志" };
        }

        /// <summary>
        /// 配置按钮样式
        /// </summary>
        private void ConfigureButtonStyles()
        {
            BarButtonItem[] buttons = { buttonItem1, buttonItem2, buttonItem3, buttonItem4, buttonItem11, buttonItem5,
                                      buttonItem6, buttonItem7, buttonItem8, buttonItem9, buttonItem10, logButton };

            foreach (var button in buttons)
            {
                button.RibbonStyle = RibbonItemStyles.All;
            }
        }

        /// <summary>
        /// 配置按钮图标
        /// </summary>
        private void ConfigureButtonIcons()
        {
            buttonItem1.ImageOptions.Image = Properties.Resources.数据监控;
            buttonItem2.ImageOptions.Image = Properties.Resources.实时参数;
            buttonItem3.ImageOptions.Image = Properties.Resources.系统配置;
            buttonItem4.ImageOptions.Image = Properties.Resources.协议信息;
            buttonItem5.ImageOptions.Image = Properties.Resources.用户管理;
            buttonItem6.ImageOptions.Image = Properties.Resources.故障统计;
            buttonItem7.ImageOptions.Image = Properties.Resources.在线升级;
            buttonItem8.ImageOptions.Image = Properties.Resources.用户切换;
            buttonItem9.ImageOptions.Image = Properties.Resources.校准管理;
            buttonItem10.ImageOptions.Image = Properties.Resources.校准管理;
            buttonItem11.ImageOptions.Image = Properties.Resources.校准管理;
            logButton.ImageOptions.Image = Properties.Resources.日志管理;
        }

        /// <summary>
        /// 将按钮添加到功能组
        /// </summary>
        /// <param name="group">功能组</param>
        private void AddButtonsToGroup(RibbonPageGroup group)
        {
            group.ItemLinks.AddRange(new[] {
                buttonItem1, buttonItem2, buttonItem9, buttonItem3, buttonItem4, buttonItem11,
                buttonItem6, buttonItem7, buttonItem5, buttonItem8, logButton
            });
        }

        /// <summary>
        /// 绑定按钮点击事件
        /// </summary>
        private void BindButtonEvents()
        {
            buttonItem1.ItemClick += (s, e) => ShowPage(typeof(Surveillance));
            buttonItem2.ItemClick += (s, e) => ShowPage(typeof(Parameter));
            buttonItem3.ItemClick += (s, e) => ShowPage(typeof(Equipment));
            buttonItem4.ItemClick += (s, e) => ShowPage(typeof(Agreement));
            buttonItem5.ItemClick += (s, e) => ShowPage(typeof(User));
            buttonItem6.ItemClick += (s, e) => ShowPage(typeof(FaultRecording));
            buttonItem7.ItemClick += (s, e) => ShowPage(typeof(Upgradeonline));
            buttonItem9.ItemClick += (s, e) => ShowPage(typeof(CalibrationManagement));
            buttonItem10.ItemClick += (s, e) => ShowPage(typeof(TestManagement));
            buttonItem11.ItemClick += (s, e) => ShowPage(typeof(DataAnalysis));
            logButton.ItemClick += (s, e) => ShowPage(typeof(LogViewer));
            buttonItem8.ItemClick += (s, e) => SwitchUser(); 
        }

        /// <summary>
        /// 初始化标签式MDI界面
        /// </summary>
        private void InitializeTabbedMdi()
        {
            // 配置导航框架
            navigationFrame.Dock = DockStyle.Fill;
            this.Controls.Add(navigationFrame);

            // 创建日志查看器实例
            logViewer = new LogViewer();

            // 注册所有功能页面
            RegisterAllPages();

            // 显示默认首页
            ShowPage(typeof(Surveillance));
        }

        /// <summary>
        /// 注册所有功能页面
        /// </summary>
        private void RegisterAllPages()
        {
            RegisterPage(typeof(Surveillance), new Surveillance(dbPath, equipmentList, _userPermissions));
            RegisterPage(typeof(Parameter), new Parameter(dbPath, equipmentList));
            RegisterPage(typeof(Equipment), new Equipment(dbPath));
            RegisterPage(typeof(Agreement), new Agreement(dbPath, equipmentList));
            RegisterPage(typeof(User), new User(dbPath));
            RegisterPage(typeof(FaultRecording), new FaultRecording(dbPath, equipmentList));
            RegisterPage(typeof(Upgradeonline), new Upgradeonline(equipmentList));
            RegisterPage(typeof(LogViewer), logViewer);
            RegisterPage(typeof(CalibrationManagement), new CalibrationManagement(dbPath, equipmentList));
            RegisterPage(typeof(TestManagement), new TestManagement());
            RegisterPage(typeof(DataAnalysis), new DataAnalysis(dbPath)); 
        }
        #endregion

        #region 页面管理
        /// <summary>
        /// 注册页面到缓存
        /// </summary>
        /// <param name="pageType">页面类型</param>
        /// <param name="page">页面控件</param>
        private void RegisterPage(Type pageType, XtraUserControl page)
        {
            // 订阅配置更新事件
            if (page is Equipment equipmentPage)
            {
                equipmentPage.ConfigUpdated += (s, e) => RefreshDeviceConfig(true, true, true);
            }
            else if (page is Agreement agreementPage)
            {
                agreementPage.ConfigUpdated += (s, e) => RefreshDeviceConfig(true, true, false);
            }

            pages[pageType] = page;
            page.Dock = DockStyle.Fill;
        }

        /// <summary>
        /// 显示指定类型的页面
        /// </summary>
        /// <param name="pageType">要显示的页面类型</param>
        private void ShowPage(Type pageType)
        {
            // 处理离开当前页面的逻辑
            HandlePageLeaving(_currentPageType);

            // 显示新页面
            if (pages.TryGetValue(pageType, out XtraUserControl page))
            {
                if (!navigationFrame.Controls.Contains(page))
                {
                    navigationFrame.Controls.Add(page);
                }
                page.BringToFront();

                // 处理进入新页面的逻辑
                HandlePageEntering(pageType);

                _currentPageType = pageType;
            }
        }

        /// <summary>
        /// 处理离开页面的逻辑
        /// </summary>
        /// <param name="leavingPageType">正在离开的页面类型</param>
        private void HandlePageLeaving(Type leavingPageType)
        {
            if (leavingPageType == typeof(Upgradeonline))
            {
                // 离开在线升级页面时启用其他页面的发送功能
                SetAllPagesSendingEnabled(true);
                LogService.Log("离开在线升级页面，启用其他页面发送功能");
            }
        }

        /// <summary>
        /// 处理进入页面的逻辑
        /// </summary>
        /// <param name="enteringPageType">正在进入的页面类型</param>
        private void HandlePageEntering(Type enteringPageType)
        {
            if (enteringPageType == typeof(Upgradeonline))
            {
                // 进入在线升级页面时禁用其他页面的发送功能
                SetAllPagesSendingEnabled(false, typeof(Upgradeonline));
                LogService.Log("进入在线升级页面，禁用其他页面发送功能");
            }
        }

        /// <summary>
        /// 设置所有页面的发送状态
        /// </summary>
        /// <param name="enabled">是否启用发送</param>
        /// <param name="exceptPageType">排除的页面类型</param>
        public void SetAllPagesSendingEnabled(bool enabled, Type exceptPageType = null)
        {
            foreach (Control control in navigationFrame.Controls)
            {
                // 跳过排除的页面
                if (exceptPageType != null && control.GetType() == exceptPageType)
                    continue;

                // 设置Surveillance页面的发送状态
                if (control is Surveillance surveillance)
                {
                    surveillance.SetModuleSendingEnabled(enabled);
                }
            }
        }
        #endregion

        #region 用户权限管理
        /// <summary>
        /// 根据用户权限设置按钮可见性
        /// </summary>
        private void SetPermissions()
        {
            switch (_userPermissions)
            {
                case "管理员":
                    // 管理员显示所有功能，无需调整
                    break;

                case "操作员":
                    SetOperatorPermissions();
                    break;

                case "普通用户":
                    SetNormalUserPermissions();
                    break;

                default:
                    SetNormalUserPermissions(); // 默认按普通用户处理
                    break;
            }
        }

        /// <summary>
        /// 设置操作员权限
        /// </summary>
        private void SetOperatorPermissions()
        {
            buttonItem4.Visibility = BarItemVisibility.Never;  // 协议管理
            buttonItem5.Visibility = BarItemVisibility.Never;  // 用户管理
            buttonItem6.Visibility = BarItemVisibility.Never;  // 故障录波
        }

        /// <summary>
        /// 设置普通用户权限
        /// </summary>
        private void SetNormalUserPermissions()
        {
            buttonItem4.Visibility = BarItemVisibility.Never;  // 协议管理
            buttonItem5.Visibility = BarItemVisibility.Never;  // 用户管理
            buttonItem6.Visibility = BarItemVisibility.Never;  // 故障录波
            buttonItem7.Visibility = BarItemVisibility.Never;  // 在线升级
            buttonItem8.Visibility = BarItemVisibility.Never;  // 用户切换
            buttonItem9.Visibility = BarItemVisibility.Never;  // 校准管理
            buttonItem10.Visibility = BarItemVisibility.Never;  // 测试管理
            buttonItem11.Visibility = BarItemVisibility.Never;  // 数据分析
        }

        /// <summary>
        /// 切换用户
        /// </summary>
        private void SwitchUser()
        {
            DialogResult confirm = XtraMessageBox.Show("确定要切换用户吗？",
                "切换用户", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                LogService.Log("用户请求切换账号");
                this.DialogResult = DialogResult.Retry; // 特殊标识，用于主程序识别
                this.Close();
            }
        }
        #endregion

        #region 资源清理和析构
        /// <summary>
        /// 窗体关闭事件处理
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            // 用户切换操作不显示退出确认
            if (this.DialogResult != DialogResult.Retry)
            {
                DialogResult result = XtraMessageBox.Show("确定要退出调试系统吗？",
                    "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

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

            // 记录关闭信息并清理资源
            LogService.Log($"关闭时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            //CleanupResources();

            base.OnClosing(e);
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        private void CleanupResources()
        {
            try
            {
                // 取消事件订阅 - 防止内存泄漏
                LogService.LogAdded -= OnLogAdded;

                // 清理CAN管理器
                CANManager.Instance?.Dispose();

                // 刷新日志
                LogService.Flush();

                // 清理页面资源
                foreach (var page in pages.Values)
                {
                    if (page is IDisposable disposablePage)
                    {
                        disposablePage.Dispose();
                    }
                }
                pages.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"资源清理时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (!_isDisposing)
            {
                _isDisposing = true;

                if (disposing)
                {
                    CleanupResources();

                    // 释放组件
                    components?.Dispose();
                }
            }

            base.Dispose(disposing);
        }
        #endregion
    }
}