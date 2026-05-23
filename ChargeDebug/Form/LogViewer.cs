using DevExpress.XtraBars;
using DevExpress.XtraEditors;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class LogViewer : XtraUserControl
    {
        private MemoEdit logMemo;
        private BarManager barManager;
        private BarButtonItem btnPause;

        private readonly Queue<string> logQueue = new Queue<string>();
        private const int MaxLines = 2000;      // 最大日志行数，防止无限膨胀
        private const int BatchSize = 50;       // 批量渲染
        private const int UpdateInterval = 100; // 刷新间隔

        private readonly StringBuilder logBuffer = new StringBuilder();
        private System.Windows.Forms.Timer updateTimer;
        private bool isPaused;
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
            // 日志控件
            logMemo = new MemoEdit();
            logMemo.Dock = DockStyle.Fill;
            logMemo.Properties.ReadOnly = true;
            logMemo.Properties.WordWrap = false;
            logMemo.Properties.ScrollBars = ScrollBars.Both;
            logMemo.Font = new Font("Tahoma", 12, FontStyle.Regular);
            SetDoubleBuffered(logMemo);

            // 工具栏
            barManager = new BarManager();
            barManager.Form = this;

            Bar bar = new Bar(barManager, "日志操作");
            bar.DockStyle = BarDockStyle.Top;
            barManager.Bars.Add(bar);

            btnPause = new BarButtonItem(barManager, "暂停显示");
            bar.ItemLinks.Add(btnPause);
            btnPause.ItemClick += (s, e) => TogglePause();

            this.Controls.Add(logMemo);

            if (this.components == null)
                this.components = new System.ComponentModel.Container();
            this.components.Add(barManager);
        }

        private void TogglePause()
        {
            isPaused = !isPaused;
            btnPause.Caption = isPaused ? "继续显示" : "暂停显示";
            updateTimer.Enabled = !isPaused;
            if (!isPaused) ProcessLogQueue();
        }

        private void ProcessLogQueue()
        {
            // 安全判断：控件未创建、暂停、无日志 → 直接返回
            if (!IsHandleCreated || logMemo == null || logQueue.Count == 0 || isPaused)
                return;

            lock (logQueue)
            {
                // 限制最大行数，防止内存爆炸
                while (logQueue.Count > MaxLines)
                    logQueue.Dequeue();

                // 批量拼接日志
                int take = Math.Min(BatchSize, logQueue.Count);
                for (int i = 0; i < take; i++)
                {
                    logBuffer.AppendLine(logQueue.Dequeue());
                }

                // 安全追加文本（修复空引用）
                if (logBuffer.Length > 0)
                {
                    // 用安全的 AppendText，不使用 MaskBox
                    logMemo.AppendText(logBuffer.ToString());
                    logBuffer.Clear();
                    needsScroll = true;
                }
            }

            // 自动滚动到底
            if (needsScroll)
            {
                try
                {
                    logMemo.SelectionStart = logMemo.Text.Length;
                    logMemo.ScrollToCaret();
                }
                catch { }
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

        // 清空日志
        public void ClearLog()
        {
            lock (logQueue) logQueue.Clear();
            if (logMemo != null) logMemo.Clear();
        }

        private static void SetDoubleBuffered(Control control)
        {
            try
            {
                typeof(Control).InvokeMember("DoubleBuffered",
                    BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                    null, control, new object[] { true });
            }
            catch { }
        }
    }
}