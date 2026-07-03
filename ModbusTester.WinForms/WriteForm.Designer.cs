#nullable enable
namespace ModbusTester
{
    partial class WriteForm
    {
        private System.ComponentModel.IContainer? components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing) components?.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.lblInfo   = new System.Windows.Forms.Label();
            this.btnWrite  = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();

            // --- lblInfo ---
            this.lblInfo.AutoSize  = false;
            this.lblInfo.Location  = new System.Drawing.Point(12, 10);
            this.lblInfo.Name      = "lblInfo";
            this.lblInfo.Size      = new System.Drawing.Size(450, 55);
            this.lblInfo.Font      = new System.Drawing.Font("Segoe UI", 9f);
            this.lblInfo.Text      = ""; // Populated dynamically in the constructor.

            // --- btnWrite ---
            this.btnWrite.Name               = "btnWrite";
            this.btnWrite.Text               = "Write";
            this.btnWrite.Size               = new System.Drawing.Size(90, 28);
            this.btnWrite.Location           = new System.Drawing.Point(12, 130); // Repositioned by BuildDynamicUi.
            this.btnWrite.UseVisualStyleBackColor = true;
            this.btnWrite.Click             += new System.EventHandler(this.BtnWrite_Click);

            // --- btnCancel ---
            this.btnCancel.Name               = "btnCancel";
            this.btnCancel.Text               = "Cancel";
            this.btnCancel.Size               = new System.Drawing.Size(90, 28);
            this.btnCancel.Location           = new System.Drawing.Point(115, 130); // Repositioned by BuildDynamicUi.
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click             += new System.EventHandler(this.BtnCancel_Click);

            this.Controls.Add(this.lblInfo);
            this.Controls.Add(this.btnWrite);
            this.Controls.Add(this.btnCancel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox     = false;
            this.MinimizeBox     = false;
            this.StartPosition   = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text            = "Write Value";
            this.ClientSize      = new System.Drawing.Size(300, 175);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Label  lblInfo   = null!;
        private System.Windows.Forms.Button btnWrite  = null!;
        private System.Windows.Forms.Button btnCancel = null!;
    }
}