using DevExpress.XtraEditors;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TcpAssistant
{
    public partial class DetailedErrorForm : XtraForm
    {
        private MemoEdit errorMemo;
        private SimpleButton btnClose;
        private SimpleButton btnCopy;

        public DetailedErrorForm(Exception ex)
        {
            InitializeUI();
            DisplayException(ex);
        }

        private void InitializeUI()
        {
            this.Text = "详细错误信息";
            this.Size = new Size(600, 400);
            this.StartPosition = FormStartPosition.CenterParent;

            // 错误信息文本框
            errorMemo = new MemoEdit();
            errorMemo.Dock = DockStyle.Fill;
            errorMemo.Properties.ReadOnly = true;
            errorMemo.Properties.Appearance.Font = new Font("Consolas", 9);
            errorMemo.Properties.ScrollBars = ScrollBars.Both;

            // 按钮面板
            Panel buttonPanel = new Panel();
            buttonPanel.Dock = DockStyle.Bottom;
            buttonPanel.Height = 50;
            buttonPanel.Padding = new Padding(10);

            btnCopy = new SimpleButton();
            btnCopy.Text = "复制错误信息";
            btnCopy.Location = new Point(10, 10);
            btnCopy.Size = new Size(120, 30);
            btnCopy.Click += BtnCopy_Click;

            btnClose = new SimpleButton();
            btnClose.Text = "关闭";
            btnClose.Location = new Point(140, 10);
            btnClose.Size = new Size(80, 30);
            btnClose.Click += (s, e) => this.Close();

            buttonPanel.Controls.Add(btnCopy);
            buttonPanel.Controls.Add(btnClose);

            this.Controls.Add(errorMemo);
            this.Controls.Add(buttonPanel);
        }

        private void DisplayException(Exception ex)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== 错误详情 ===");
            sb.AppendLine($"发生时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"错误类型: {ex.GetType().FullName}");
            sb.AppendLine($"错误信息: {ex.Message}");
            sb.AppendLine();
            sb.AppendLine("=== 堆栈跟踪 ===");
            sb.AppendLine(ex.StackTrace);

            if (ex.InnerException != null)
            {
                sb.AppendLine();
                sb.AppendLine("=== 内部异常 ===");
                sb.AppendLine($"类型: {ex.InnerException.GetType().FullName}");
                sb.AppendLine($"信息: {ex.InnerException.Message}");
                sb.AppendLine("堆栈跟踪:");
                sb.AppendLine(ex.InnerException.StackTrace);
            }

            errorMemo.Text = sb.ToString();
        }

        private void BtnCopy_Click(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(errorMemo.Text);
                XtraMessageBox.Show("错误信息已复制到剪贴板", "成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"复制失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
