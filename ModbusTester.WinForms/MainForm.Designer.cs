#nullable enable
namespace ModbusTester
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer? components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing) components?.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.pnlTop     = new System.Windows.Forms.Panel();
            this.btnAddTab  = new System.Windows.Forms.Button();
            this.tabControl = new System.Windows.Forms.TabControl();

            this.pnlTop.SuspendLayout();
            this.SuspendLayout();

            // --- pnlTop (üst kontrol çubuğu) ---
            this.pnlTop.Dock   = System.Windows.Forms.DockStyle.Top;
            this.pnlTop.Height = 44;
            this.pnlTop.Controls.Add(this.btnAddTab);

            // --- btnAddTab ---
            this.btnAddTab.Text     = "Yeni Sekme Ekle";
            this.btnAddTab.Location = new System.Drawing.Point(10, 8);
            this.btnAddTab.Size     = new System.Drawing.Size(150, 28);
            this.btnAddTab.UseVisualStyleBackColor = true;
            this.btnAddTab.Click   += new System.EventHandler(this.BtnAddTab_Click);

            // --- tabControl ---
            this.tabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl.Name = "tabControl";

            // --- MainForm ---
            this.Controls.Add(this.tabControl);
            this.Controls.Add(this.pnlTop);
            this.ClientSize   = new System.Drawing.Size(500, 760);
            this.Name         = "MainForm";
            this.Text         = "Modbus TCP Master Tester — Sekmeli";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);

            this.pnlTop.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel      pnlTop     = null!;
        private System.Windows.Forms.Button     btnAddTab  = null!;
        private System.Windows.Forms.TabControl tabControl = null!;
    }
}