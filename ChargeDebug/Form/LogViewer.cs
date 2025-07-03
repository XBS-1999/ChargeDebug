using ChargeDebug.Service;
using DevExpress.XtraBars;
using DevExpress.XtraEditors;
using DevExpress.XtraTreeList;
using Microsoft.VisualBasic;
using System.Reflection;
using System.Text;

namespace ChargeDebug.Form
{
    public partial class LogViewer : XtraUserControl
    {
        private MemoEdit logMemo;
        //private RichTextBox logBox;
        private BarManager barManager;
        private BarButtonItem btnPause; // 新增暂停按钮
        private readonly Queue<string> logQueue = new Queue<string>();
        private System.Windows.Forms.Timer updateTimer;
        private const int UpdateInterval = 100;
        private readonly StringBuilder logBuffer = new StringBuilder();
        private bool isPaused; // 暂停状态标志
        private bool needsScroll;

        public LogViewer()
        {
            InitializeComponent();
            InitializeUI();
            InitializeUpdateTimer();
        }

        private void InitializeUpdateTimer()
        {
            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = UpdateInterval;
            updateTimer.Tick += (s, e) => ProcessLogQueue();
            updateTimer.Start();
        }

        private void InitializeUI()
        {
            // 创建日志显示区域 - 启用双缓冲
            logMemo = new MemoEdit();
            logMemo.Dock = DockStyle.Fill;
            logMemo.Properties.ReadOnly = true;
            logMemo.Properties.WordWrap = false;
            logMemo.Properties.ScrollBars = ScrollBars.Both;
            logMemo.Font = new Font("Tahoma", 12, FontStyle.Regular);
            // 启用双缓冲
            SetDoubleBuffered(logMemo);

            // 创建工具栏
            barManager = new BarManager();
            barManager.Form = this;

            Bar bar = new Bar(barManager, "日志操作");
            bar.DockStyle = BarDockStyle.Top;
            barManager.Bars.Add(bar);

            // 只保留暂停/继续按钮
            btnPause = new BarButtonItem(barManager, "暂停显示");
            bar.ItemLinks.Add(btnPause);

            // 绑定按钮点击事件
            btnPause.ItemClick += (s, e) => TogglePause();

            // 添加控件
            this.Controls.Add(logMemo);

            if(this.components == null)
                this.components = new System.ComponentModel.Container();
            this.components.Add(barManager); // 确保工具栏可见
        }

        // 新增：切换暂停状态的方法
        private void TogglePause()
        {
            isPaused = !isPaused;
            btnPause.Caption = isPaused ? "继续显示" : "暂停显示";
            updateTimer.Enabled = !isPaused;

            // 恢复时处理积压的日志
            if (!isPaused) ProcessLogQueue();
        }

        private void ProcessLogQueue()
        {
            if (logQueue.Count == 0 || isPaused) return; // 暂停时跳过处理

            int processed = 0;
            const int maxBatch = 10;

            lock (logQueue)
            {
                while (logQueue.Count > 0 && processed < maxBatch)
                {
                    logMemo.AppendText(logQueue.Dequeue() + "\r\n");
                    processed++;
                    needsScroll = true;
                }
            }

            if(needsScroll)
            {
                logMemo.SelectionStart = logMemo.Text.Length;
                logMemo.ScrollToCaret();
                needsScroll = false;
            }
        }

        public void AddLogEntry(string entry)
        {
            lock (logQueue)
            {
                logQueue.Enqueue(entry);
            }
        }

        // 启用双缓冲的辅助方法
        private static void SetDoubleBuffered(Control control)
        {
            typeof(Control).InvokeMember("DoubleBuffered",
                BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                null, control, new object[] { true });
        }
    }
}
