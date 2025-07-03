using DevExpress.Utils;
using DevExpress.XtraEditors;
using System.Drawing.Drawing2D;

namespace ChargeDebug.Form
{
    public partial class ToastForm : XtraForm
    {
        private System.Windows.Forms.Timer closeTimer;
        private int cornerRadius = 10; // 圆角半径
        private int borderWidth = 2;    // 边框宽度
        private Color color;
        private LabelControl label;

        public ToastForm(string message, Color color, Point? mousePosition = null)
        {
            this.color = color;
            InitializeComponent();

            // 设置窗体属性
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.DoubleBuffered = true;
            this.BackColor = Color.White; // 设置白色背景确保圆角可见

            // 创建标签 - 不使用 Dock 属性
            label = new LabelControl
            {
                Text = message,
                Font = new Font("Tahoma", 12, FontStyle.Bold), // 减小字体以适应小尺寸
                ForeColor = color,
                AutoSizeMode = LabelAutoSizeMode.None,
                Dock = DockStyle.Fill,
                Appearance = { TextOptions = { HAlignment = HorzAlignment.Center } },
                //Location = new Point(5, 15), // 固定位置
                //Size = new Size(70, 30) // 固定大小
            };
            this.Controls.Add(label);

            // 固定窗体大小为 100x60
            this.Size = new Size(70, 30);

            // 设置位置在鼠标附近
            Point location = mousePosition ?? Cursor.Position;
            location.Offset(-20, -70); // 偏移一点避免遮挡光标
            // 确保窗口不会超出屏幕边界
            Rectangle screen = Screen.FromPoint(location).WorkingArea;
            if (location.X + Width > screen.Right)
                location.X = screen.Right - Width;
            if (location.Y + Height > screen.Bottom)
                location.Y = screen.Bottom - Height;

            this.Location = location;

            // 设置圆角区域
            //SetRoundedRegion();

            // 设置关闭计时器
            closeTimer = new System.Windows.Forms.Timer { Interval = 500 };
            closeTimer.Tick += (s, e) => CloseForm();
            closeTimer.Start();
        }

        private void SetRoundedRegion()
        {
            // 使用精确的路径计算方法
            GraphicsPath path = new GraphicsPath();

            // 左上角
            path.AddArc(0, 0, cornerRadius * 2, cornerRadius * 2, 180, 90);
            // 右上角
            path.AddArc(Width - cornerRadius * 2, 0, cornerRadius * 2, cornerRadius * 2, 270, 90);
            // 右下角 - 修复坐标计算
            path.AddArc(Width - cornerRadius * 2, Height - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 0, 90);
            // 左下角
            path.AddArc(0, Height - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 90, 90);

            path.CloseFigure();
            this.Region = new Region(path);
        }

        private void CloseForm()
        {
            if (closeTimer != null)
            {
                closeTimer.Stop();
                closeTimer.Dispose();
            }
            this.Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // 绘制边框
            using (Pen pen = new Pen(color, borderWidth))
            {
                e.Graphics.DrawRectangle(pen, new Rectangle(borderWidth - 1, borderWidth - 1, Width - borderWidth, Height - borderWidth));
            }
        }
    }
}
