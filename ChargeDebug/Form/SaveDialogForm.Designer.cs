namespace ChargeDebug.Form
{
    partial class SaveDialogForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            SuspendLayout();
            // 
            // SaveDialogForm
            // 
            AutoScaleDimensions = new SizeF(7F, 14F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(298, 68);
            MaximizeBox = false;
            MaximumSize = new Size(400, 100);
            MinimizeBox = false;
            MinimumSize = new Size(300, 100);
            Name = "SaveDialogForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "保存文件";
            ResumeLayout(false);
        }

        #endregion
    }
}