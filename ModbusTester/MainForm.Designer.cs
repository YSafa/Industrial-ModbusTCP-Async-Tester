namespace ModbusTester
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Tasarımcısı tarafından oluşturulan kod

        private void InitializeComponent()
        {
            this.lblIpAddress = new System.Windows.Forms.Label();
            this.txtIpAddress = new System.Windows.Forms.TextBox();
            this.lblPort = new System.Windows.Forms.Label();
            this.txtPort = new System.Windows.Forms.TextBox();
            this.lblSlaveId = new System.Windows.Forms.Label();
            this.numSlaveId = new System.Windows.Forms.NumericUpDown();
            this.lblStartAddress = new System.Windows.Forms.Label();
            this.numStartAddress = new System.Windows.Forms.NumericUpDown();
            this.lblFunctionCode = new System.Windows.Forms.Label();
            this.cmbFunctionCode = new System.Windows.Forms.ComboBox();
            this.lblDataType = new System.Windows.Forms.Label();
            this.cmbDataType = new System.Windows.Forms.ComboBox();
            this.lblQuantity = new System.Windows.Forms.Label();
            this.numQuantity = new System.Windows.Forms.NumericUpDown();
            this.lblPollingInterval = new System.Windows.Forms.Label();
            this.numPollingInterval = new System.Windows.Forms.NumericUpDown();
            this.btnConnect = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.dgvRegisters = new System.Windows.Forms.DataGridView();
            this.dgvRegisters.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DgvRegisters_CellDoubleClick);
            this.colAddress = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colValue = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.rtbLogs = new System.Windows.Forms.RichTextBox();

            ((System.ComponentModel.ISupportInitialize)(this.numSlaveId)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numStartAddress)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numQuantity)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPollingInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvRegisters)).BeginInit();
            this.SuspendLayout();

            // ---------------------------------------------------------
            // TÜM SATIRLAR TEK SÜTUN HALİNE GETİRİLDİ (Çakışmayı önlemek için):
            // Label'lar Left=12, giriş kutuları Left=130'da hizalı.
            // ---------------------------------------------------------

            // --- lblIpAddress / txtIpAddress (satır 1) ---
            this.lblIpAddress.AutoSize = true;
            this.lblIpAddress.Location = new System.Drawing.Point(12, 15);
            this.lblIpAddress.Name = "lblIpAddress";
            this.lblIpAddress.Size = new System.Drawing.Size(56, 13);
            this.lblIpAddress.Text = "IP Adresi:";

            this.txtIpAddress.Location = new System.Drawing.Point(130, 12);
            this.txtIpAddress.Name = "txtIpAddress";
            this.txtIpAddress.Size = new System.Drawing.Size(150, 20);
            this.txtIpAddress.Text = "127.0.0.1";

            // --- lblPort / txtPort (satır 2) ---
            this.lblPort.AutoSize = true;
            this.lblPort.Location = new System.Drawing.Point(12, 45);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(29, 13);
            this.lblPort.Text = "Port:";

            this.txtPort.Location = new System.Drawing.Point(130, 42);
            this.txtPort.Name = "txtPort";
            this.txtPort.Size = new System.Drawing.Size(80, 20);
            this.txtPort.Text = "502";

            // --- lblSlaveId / numSlaveId (satır 3) ---
            this.lblSlaveId.AutoSize = true;
            this.lblSlaveId.Location = new System.Drawing.Point(12, 75);
            this.lblSlaveId.Name = "lblSlaveId";
            this.lblSlaveId.Size = new System.Drawing.Size(50, 13);
            this.lblSlaveId.Text = "Slave ID:";

            this.numSlaveId.Location = new System.Drawing.Point(130, 72);
            this.numSlaveId.Maximum = new decimal(new int[] { 255, 0, 0, 0 });
            this.numSlaveId.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numSlaveId.Name = "numSlaveId";
            this.numSlaveId.Size = new System.Drawing.Size(80, 20);
            this.numSlaveId.Value = new decimal(new int[] { 1, 0, 0, 0 });

            // --- lblStartAddress / numStartAddress (satır 4) ---
            this.lblStartAddress.AutoSize = true;
            this.lblStartAddress.Location = new System.Drawing.Point(12, 105);
            this.lblStartAddress.Name = "lblStartAddress";
            this.lblStartAddress.Size = new System.Drawing.Size(96, 13);
            this.lblStartAddress.Text = "Başlangıç Adresi:";

            this.numStartAddress.Location = new System.Drawing.Point(130, 102);
            this.numStartAddress.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numStartAddress.Name = "numStartAddress";
            this.numStartAddress.Size = new System.Drawing.Size(80, 20);

            // --- lblFunctionCode / cmbFunctionCode (satır 5) ---
            this.lblFunctionCode.AutoSize = true;
            this.lblFunctionCode.Location = new System.Drawing.Point(12, 135);
            this.lblFunctionCode.Name = "lblFunctionCode";
            this.lblFunctionCode.Size = new System.Drawing.Size(78, 13);
            this.lblFunctionCode.Text = "Fonksiyon Kodu:";

            this.cmbFunctionCode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbFunctionCode.Location = new System.Drawing.Point(130, 132);
            this.cmbFunctionCode.Name = "cmbFunctionCode";
            this.cmbFunctionCode.Size = new System.Drawing.Size(220, 21);

            // --- lblDataType / cmbDataType (satır 6) ---
            this.lblDataType.AutoSize = true;
            this.lblDataType.Location = new System.Drawing.Point(12, 165);
            this.lblDataType.Name = "lblDataType";
            this.lblDataType.Size = new System.Drawing.Size(58, 13);
            this.lblDataType.Text = "Veri Tipi:";

            this.cmbDataType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbDataType.Location = new System.Drawing.Point(130, 162);
            this.cmbDataType.Name = "cmbDataType";
            this.cmbDataType.Size = new System.Drawing.Size(220, 21);

            // --- lblQuantity / numQuantity (satır 7) ---
            this.lblQuantity.AutoSize = true;
            this.lblQuantity.Location = new System.Drawing.Point(12, 195);
            this.lblQuantity.Name = "lblQuantity";
            this.lblQuantity.Size = new System.Drawing.Size(40, 13);
            this.lblQuantity.Text = "Adet:";

            this.numQuantity.Location = new System.Drawing.Point(130, 192);
            this.numQuantity.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numQuantity.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
            this.numQuantity.Name = "numQuantity";
            this.numQuantity.Size = new System.Drawing.Size(80, 20);
            this.numQuantity.Value = new decimal(new int[] { 1, 0, 0, 0 });

            // --- lblPollingInterval / numPollingInterval (satır 8) ---
            this.lblPollingInterval.AutoSize = true;
            this.lblPollingInterval.Location = new System.Drawing.Point(12, 225);
            this.lblPollingInterval.Name = "lblPollingInterval";
            this.lblPollingInterval.Size = new System.Drawing.Size(95, 13);
            this.lblPollingInterval.Text = "Sorgulama (ms):";

            this.numPollingInterval.Location = new System.Drawing.Point(130, 222);
            this.numPollingInterval.Maximum = new decimal(new int[] { 60000, 0, 0, 0 });
            this.numPollingInterval.Minimum = new decimal(new int[] { 50, 0, 0, 0 });
            this.numPollingInterval.Name = "numPollingInterval";
            this.numPollingInterval.Size = new System.Drawing.Size(80, 20);
            this.numPollingInterval.Value = new decimal(new int[] { 200, 0, 0, 0 });

            // --- btnConnect / btnStop (satır 9) ---
            this.btnConnect.Location = new System.Drawing.Point(130, 255);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(100, 28);
            this.btnConnect.Text = "Bağlan";
            this.btnConnect.UseVisualStyleBackColor = true;
            this.btnConnect.Click += new System.EventHandler(this.btnConnect_Click);

            this.btnStop.Enabled = false;
            this.btnStop.Location = new System.Drawing.Point(250, 255);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(100, 28);
            this.btnStop.Text = "Durdur";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);

            // --- colAddress ---
            this.colAddress.HeaderText = "Adres";
            this.colAddress.Name = "colAddress";
            this.colAddress.ReadOnly = true;

            // --- colValue ---
            this.colValue.HeaderText = "Değer";
            this.colValue.Name = "colValue";
            this.colValue.ReadOnly = true;

            // --- dgvRegisters (satır 10) ---
            this.dgvRegisters.AllowUserToAddRows = false;
            this.dgvRegisters.AllowUserToDeleteRows = false;
            this.dgvRegisters.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvRegisters.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
                this.colAddress,
                this.colValue});
            this.dgvRegisters.Location = new System.Drawing.Point(12, 295);
            this.dgvRegisters.Name = "dgvRegisters";
            this.dgvRegisters.ReadOnly = true;
            this.dgvRegisters.RowHeadersVisible = false;
            this.dgvRegisters.Size = new System.Drawing.Size(420, 220);
            this.dgvRegisters.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;

            // --- rtbLogs (satır 11) ---
            this.rtbLogs.BackColor = System.Drawing.Color.Black;
            this.rtbLogs.ForeColor = System.Drawing.Color.White;
            this.rtbLogs.Location = new System.Drawing.Point(12, 525);
            this.rtbLogs.Name = "rtbLogs";
            this.rtbLogs.ReadOnly = true;
            this.rtbLogs.Size = new System.Drawing.Size(420, 150);

            // --- MainForm ---
            this.ClientSize = new System.Drawing.Size(444, 690);
            this.Controls.Add(this.lblIpAddress);
            this.Controls.Add(this.txtIpAddress);
            this.Controls.Add(this.lblPort);
            this.Controls.Add(this.txtPort);
            this.Controls.Add(this.lblSlaveId);
            this.Controls.Add(this.numSlaveId);
            this.Controls.Add(this.lblStartAddress);
            this.Controls.Add(this.numStartAddress);
            this.Controls.Add(this.lblFunctionCode);
            this.Controls.Add(this.cmbFunctionCode);
            this.Controls.Add(this.lblDataType);
            this.Controls.Add(this.cmbDataType);
            this.Controls.Add(this.lblQuantity);
            this.Controls.Add(this.numQuantity);
            this.Controls.Add(this.lblPollingInterval);
            this.Controls.Add(this.numPollingInterval);
            this.Controls.Add(this.btnConnect);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.dgvRegisters);
            this.Controls.Add(this.rtbLogs);
            this.Name = "MainForm";
            this.Text = "Modbus TCP Master Tester";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);

            ((System.ComponentModel.ISupportInitialize)(this.numSlaveId)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numStartAddress)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numQuantity)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPollingInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvRegisters)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        // ---------------------------------------------------------
        // KONTROL DEĞİŞKEN TANIMLAMALARI
        // ---------------------------------------------------------
        private System.Windows.Forms.Label lblIpAddress;
        private System.Windows.Forms.TextBox txtIpAddress;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.TextBox txtPort;
        private System.Windows.Forms.Label lblSlaveId;
        private System.Windows.Forms.NumericUpDown numSlaveId;
        private System.Windows.Forms.Label lblStartAddress;
        private System.Windows.Forms.NumericUpDown numStartAddress;
        private System.Windows.Forms.Label lblFunctionCode;
        private System.Windows.Forms.ComboBox cmbFunctionCode;
        private System.Windows.Forms.Label lblDataType;
        private System.Windows.Forms.ComboBox cmbDataType;
        private System.Windows.Forms.Label lblQuantity;
        private System.Windows.Forms.NumericUpDown numQuantity;
        private System.Windows.Forms.Label lblPollingInterval;
        private System.Windows.Forms.NumericUpDown numPollingInterval;
        private System.Windows.Forms.Button btnConnect;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.DataGridView dgvRegisters;
        private System.Windows.Forms.DataGridViewTextBoxColumn colAddress;
        private System.Windows.Forms.DataGridViewTextBoxColumn colValue;
        private System.Windows.Forms.RichTextBox rtbLogs;
    }
}