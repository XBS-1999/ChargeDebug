// ImportProgressForm.cs
using DevExpress.XtraEditors;

#pragma warning disable
namespace ChargeDebug.Form
{
    public partial class ImportProgressForm : XtraForm
    {
        private ProgressBarControl progressBarFile;
        private ProgressBarControl progressBarRecord;
        private LabelControl lblFile;
        private LabelControl lblRecord;

        public ImportProgressForm(int totalFiles)
        {
            InitializeComponent();
            InitializeUI();
            progressBarFile.Properties.Maximum = totalFiles;
            progressBarFile.Properties.Minimum = 0;
            progressBarFile.Position = 0;

            Text = "导入进度";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
        }

        private void InitializeUI()
        {
            this.progressBarFile = new ProgressBarControl();
            this.progressBarRecord = new ProgressBarControl();
            this.lblFile = new LabelControl();
            this.lblRecord = new LabelControl();
            ((System.ComponentModel.ISupportInitialize)(this.progressBarFile.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.progressBarRecord.Properties)).BeginInit();
            this.SuspendLayout();

            // lblFile
            this.lblFile.Location = new System.Drawing.Point(12, 15);
            this.lblFile.Name = "lblFile";
            this.lblFile.Size = new System.Drawing.Size(48, 14);
            this.lblFile.Text = "文件进度:";

            // progressBarFile
            this.progressBarFile.Location = new System.Drawing.Point(12, 35);
            this.progressBarFile.Name = "progressBarFile";
            this.progressBarFile.Size = new System.Drawing.Size(360, 18);

            // lblRecord
            this.lblRecord.Location = new System.Drawing.Point(12, 65);
            this.lblRecord.Name = "lblRecord";
            this.lblRecord.Size = new System.Drawing.Size(48, 14);
            this.lblRecord.Text = "记录进度:";

            // progressBarRecord
            this.progressBarRecord.Location = new System.Drawing.Point(12, 85);
            this.progressBarRecord.Name = "progressBarRecord";
            this.progressBarRecord.Size = new System.Drawing.Size(360, 18);
            this.progressBarRecord.Properties.Maximum = 100;
            this.progressBarRecord.Properties.Minimum = 0;

            // ImportProgressForm
            this.ClientSize = new System.Drawing.Size(384, 121);
            this.Controls.Add(this.progressBarRecord);
            this.Controls.Add(this.lblRecord);
            this.Controls.Add(this.progressBarFile);
            this.Controls.Add(this.lblFile);
            this.Name = "ImportProgressForm";
            ((System.ComponentModel.ISupportInitialize)(this.progressBarFile.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.progressBarRecord.Properties)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        public void UpdateProgress(int currentFile, int totalFiles, string message = null)
        {
            if (!IsDisposed)
            {
                Invoke(new Action(() =>
                {
                    progressBarFile.Position = currentFile;
                    if (!string.IsNullOrEmpty(message))
                    {
                        lblFile.Text = message;
                    }
                    else
                    {
                        lblFile.Text = $"文件进度: {currentFile}/{totalFiles}";
                    }
                }));
            }
        }

        public void UpdateRecordProgress(int currentRecords, int totalRecords)
        {
            if (!IsDisposed)
            {
                Invoke(new Action(() =>
                {
                    int percent = totalRecords > 0 ? (currentRecords * 100 / totalRecords) : 0;
                    progressBarRecord.Position = percent;
                    lblRecord.Text = $"记录进度: {currentRecords}/{totalRecords} ({percent}%)";
                }));
            }
        }

        public void SetCurrentFile(string fileName)
        {
            if (!IsDisposed)
            {
                Invoke(new Action(() =>
                {
                    lblFile.Text = $"正在处理: {fileName}";
                }));
            }
        }
    }
}