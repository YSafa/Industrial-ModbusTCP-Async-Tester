using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using System.ComponentModel;
using ModbusTester.Core;
using ModbusTester.Core.Core;
using ModbusTester.Core.Exceptions;
using ModbusTester.Core.Protocol;

namespace ModbusTester
{
    public partial class WriteForm : Form
    {
        private readonly ModbusClient    _client;
        private readonly byte            _slaveId;
        private readonly ushort          _address;
        private readonly string          _dataType;
        private readonly ModbusFunctionCode _functionCode;
        private readonly ushort[]?       _currentValues;
        private readonly bool[]?         _currentBitValues;

        // Dynamic controls — non-null only while the corresponding mode is active.
        private CheckBox[]? _bitCheckBoxes; // Binary mode: 16 CheckBoxes
        private CheckBox?   _coilCheckBox;  // Coil mode: single CheckBox
        private TextBox?    _txtValue;       // Numeric/Hex mode: text input

        /// <summary>
        /// Callback invoked by MainForm to append a green log line to its own log panel
        /// after a successful write.
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Action<string>? OnSuccessLog { get; set; }

        public WriteForm(
            ModbusClient        client,
            byte                slaveId,
            ushort              address,
            string              dataType,
            ModbusFunctionCode  functionCode,
            ushort[]?           currentValues,
            bool[]?             currentBitValues)
        {
            // Fields must be assigned BEFORE InitializeComponent; pre-fill logic in
            // BuildDynamicUi depends on these values.
            _client           = client;
            _slaveId          = slaveId;
            _address          = address;
            _dataType         = dataType;
            _functionCode     = functionCode;
            _currentValues    = currentValues;
            _currentBitValues = currentBitValues;

            InitializeComponent();

            // Safe to populate the header text now that fields are assigned.
            lblInfo.Text = $"Address: {_address}    |    Data Type: {_dataType}" +
                           $"\nFunction: {_functionCode}    |    Slave ID: {_slaveId}";

            // Disable writing for read-only function codes.
            if (_functionCode == ModbusFunctionCode.ReadDiscreteInputs ||
                _functionCode == ModbusFunctionCode.ReadInputRegisters)
            {
                btnWrite.Enabled  = false;
                lblInfo.Text     += "\n⚠ This is a read-only register; writing is not supported.";
            }

            BuildDynamicUi();
        }

        // ---------------------------------------------------------
        // DYNAMIC UI CONSTRUCTION
        // ---------------------------------------------------------

        private void BuildDynamicUi()
        {
            bool isBitBased = _functionCode == ModbusFunctionCode.ReadCoils ||
                              _functionCode == ModbusFunctionCode.ReadDiscreteInputs;

            if (isBitBased)
            {
                BuildCoilUi();
            }
            else if (_dataType == "Binary")
            {
                BuildBinaryUi();
            }
            else
            {
                BuildTextUi();
            }
        }

        /// <summary>
        /// FC01/FC02 mode: a single CheckBox to display/control the coil state.
        /// </summary>
        private void BuildCoilUi()
        {
            _coilCheckBox = new CheckBox
            {
                Text     = "On  (1)  /  Off  (0)",
                Location = new Point(20, 75),
                AutoSize = true,
                Font     = new Font("Segoe UI", 10f)
            };

            if (_currentBitValues != null && _currentBitValues.Length > 0)
                _coilCheckBox.Checked = _currentBitValues[0];

            this.Controls.Add(_coilCheckBox);
            this.ClientSize = new Size(310, 175);
            RepositionButtons(130);
        }

        /// <summary>
        /// Binary mode: 16 CheckBoxes representing each bit of a single register.
        /// Boxes are laid out MSB (B15) first, in two rows of 8.
        /// </summary>
        private void BuildBinaryUi()
        {
            _bitCheckBoxes = new CheckBox[16];

            ushort currentVal = (_currentValues != null && _currentValues.Length > 0)
                ? _currentValues[0]
                : (ushort)0;

            var lblBits = new Label
            {
                Text      = "B15 → B8                              B7 → B0",
                Location  = new Point(15, 68),
                AutoSize  = true,
                Font      = new Font("Segoe UI", 8f, FontStyle.Italic),
                ForeColor = Color.Gray
            };
            this.Controls.Add(lblBits);

            for (int i = 0; i < 16; i++)
            {
                int bitIndex = 15 - i; // Left to right: B15 → B0
                int col      = i % 8;
                int row      = i / 8;

                bool bitValue = ((currentVal >> bitIndex) & 1) == 1;

                var chk = new CheckBox
                {
                    Text     = $"B{bitIndex}",
                    Checked  = bitValue,
                    Location = new Point(15 + col * 57, 88 + row * 30),
                    Width    = 54,
                    Font     = new Font("Segoe UI", 8f)
                };

                _bitCheckBoxes[i] = chk;
                this.Controls.Add(chk);
            }

            this.ClientSize = new Size(480, 230);
            RepositionButtons(185);
        }

        /// <summary>
        /// Numeric/Hex mode: a single TextBox with keystroke filtering for value entry.
        /// </summary>
        private void BuildTextUi()
        {
            var lblInput = new Label
            {
                Text     = "Value to Write:",
                Location = new Point(20, 72),
                AutoSize = true,
                Font     = new Font("Segoe UI", 9f)
            };

            _txtValue = new TextBox
            {
                Location = new Point(20, 92),
                Width    = 220,
                Font     = new Font("Segoe UI", 10f)
            };

            _txtValue.Text      = GetCurrentValueAsString();
            _txtValue.KeyPress += TxtValue_KeyPress;
            _txtValue.KeyDown  += TxtValue_KeyDown;

            this.Controls.Add(lblInput);
            this.Controls.Add(_txtValue);
            this.ClientSize = new Size(300, 175);
            RepositionButtons(130);

            // Auto-focus: cursor lands directly in the text box as soon as the form opens.
            this.ActiveControl = _txtValue;
        }

        /// <summary>
        /// Repositions the buttons once the final form height is known.
        /// </summary>
        private void RepositionButtons(int y)
        {
            btnWrite.Location  = new Point(12,  y);
            btnCancel.Location = new Point(115, y);
        }

        // ---------------------------------------------------------
        // FORMATTING THE CURRENT VALUE AS TEXT
        // ---------------------------------------------------------

        private string GetCurrentValueAsString()
        {
            if (_currentValues == null || _currentValues.Length == 0) return "";

            ushort[] values = _currentValues;

            return _dataType switch
            {
                "Unsigned (16-bit)"       => values[0].ToString(),
                "Signed (16-bit)"         => ModbusDataConverter.ToSigned(values[0]).ToString(),
                "Hex"                     => values[0].ToString("X4"),
                "Float (32-bit)"          => ModbusDataConverter.ToFloat(values.AsSpan(), inverse: true).ToString("G", CultureInfo.InvariantCulture),
                "Float Inverse (32-bit)"  => ModbusDataConverter.ToFloat(values.AsSpan(), inverse: false).ToString("G", CultureInfo.InvariantCulture),
                "Long (32-bit)"           => ModbusDataConverter.ToLong(values.AsSpan(), inverse: true).ToString(),
                "Long Inverse (32-bit)"   => ModbusDataConverter.ToLong(values.AsSpan(), inverse: false).ToString(),
                "Double (64-bit)"         => ModbusDataConverter.ToDouble(values.AsSpan(), inverse: true).ToString("G", CultureInfo.InvariantCulture),
                "Double Inverse (64-bit)" => ModbusDataConverter.ToDouble(values.AsSpan(), inverse: false).ToString("G", CultureInfo.InvariantCulture),
                _                         => values[0].ToString()
            };
        }

        // ---------------------------------------------------------
        // KEYSTROKE FILTERING (KeyPress Masking)
        // ---------------------------------------------------------

        private void TxtValue_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar)) return; // Backspace, Delete, etc. always pass through.

            string current   = _txtValue?.Text ?? "";
            int    cursorPos = _txtValue?.SelectionStart ?? 0;

            switch (_dataType)
            {
                case "Hex":
                    // Only 0-9, A-F, a-f are allowed.
                    if (!IsHexChar(e.KeyChar)) e.Handled = true;
                    break;

                case "Unsigned (16-bit)":
                    // Digits only.
                    if (!char.IsDigit(e.KeyChar)) e.Handled = true;
                    break;

                case "Signed (16-bit)":
                case "Long (32-bit)":
                case "Long Inverse (32-bit)":
                    // Digits plus a single leading minus sign.
                    if (!char.IsDigit(e.KeyChar))
                    {
                        bool isLeadingMinus = e.KeyChar == '-' && cursorPos == 0 && !current.Contains('-');
                        if (!isLeadingMinus) e.Handled = true;
                    }
                    break;

                case "Float (32-bit)":
                case "Float Inverse (32-bit)":
                case "Double (64-bit)":
                case "Double Inverse (64-bit)":
                    // Digits, a single leading minus, and a single decimal separator (. or ,).
                    if (!char.IsDigit(e.KeyChar))
                    {
                        bool isLeadingMinus   = e.KeyChar == '-' && cursorPos == 0 && !current.Contains('-');
                        bool isDecimalSep     = (e.KeyChar == '.' || e.KeyChar == ',')
                                                && !current.Contains('.') && !current.Contains(',');
                        if (!isLeadingMinus && !isDecimalSep) e.Handled = true;
                    }
                    break;
            }
        }

        /// <summary>
        /// Lets the user press Enter to submit the write directly, instead of forcing a click on
        /// the Write button. Suppresses the keystroke to avoid the Windows error beep.
        /// </summary>
        private void TxtValue_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                BtnWrite_Click(sender, EventArgs.Empty);
            }
        }

        private static bool IsHexChar(char c)
            => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

        // ---------------------------------------------------------
        // WRITE BUTTON
        // ---------------------------------------------------------

        private async void BtnWrite_Click(object? sender, EventArgs e)
        {
            btnWrite.Enabled = false; // Prevent double-clicks and overlapping write attempts.

            try
            {
                bool isBitBased = _functionCode == ModbusFunctionCode.ReadCoils ||
                                  _functionCode == ModbusFunctionCode.ReadDiscreteInputs;

                if (isBitBased)
                    await WriteCoilAsync();
                else if (_dataType == "Binary")
                    await WriteBinaryAsync();
                else
                    await WriteValueAsync();
            }
            catch (ModbusProtocolException ex)
            {
                MessageBox.Show($"Protocol error: {ex.Message}", "Write Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (ModbusConnectionException ex)
            {
                MessageBox.Show($"Connection error: {ex.Message}", "Write Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (ModbusTimeoutException ex)
            {
                MessageBox.Show($"Timeout: {ex.Message}", "Write Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error: {ex.Message}", "Write Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnWrite.Enabled = true;
            }
        }

        // ---------------------------------------------------------
        // PER-MODE WRITE METHODS
        // ---------------------------------------------------------

        private async System.Threading.Tasks.Task WriteCoilAsync()
        {
            if (_coilCheckBox == null) return;

            bool value = _coilCheckBox.Checked;
            await _client.WriteSingleCoilAsync(_slaveId, _address, value);
            HandleSuccess($"[FC05] Coil {_address} → {(value ? "1 (On)" : "0 (Off)")} written.");
        }

        private async System.Threading.Tasks.Task WriteBinaryAsync()
        {
            if (_bitCheckBoxes == null) return;

            ushort result = 0;
            for (int i = 0; i < 16; i++)
            {
                int bitIndex = 15 - i; // B15 on the left, B0 on the right
                if (_bitCheckBoxes[i].Checked)
                    result |= (ushort)(1 << bitIndex);
            }

            await _client.WriteSingleRegisterAsync(_slaveId, _address, result);
            HandleSuccess($"[FC06] Register {_address} → 0b{Convert.ToString(result, 2).PadLeft(16, '0')} written.");
        }

        private async System.Threading.Tasks.Task WriteValueAsync()
        {
            // Comma is converted to a period and parsed with InvariantCulture, since a Turkish
            // Windows locale would otherwise treat "," as the decimal separator.
            string input = (_txtValue?.Text ?? "").Replace(',', '.');

            switch (_dataType)
            {
                case "Unsigned (16-bit)":
                {
                    if (!ushort.TryParse(input, out ushort val))
                    { ShowRangeError("Unsigned 16-bit", "0", "65535"); return; }
                    await _client.WriteSingleRegisterAsync(_slaveId, _address, val);
                    HandleSuccess($"[FC06] Register {_address} → {val} written.");
                    break;
                }

                case "Signed (16-bit)":
                {
                    if (!short.TryParse(input, out short val))
                    { ShowRangeError("Signed 16-bit", "-32768", "32767"); return; }
                    await _client.WriteSingleRegisterAsync(_slaveId, _address, unchecked((ushort)val));
                    HandleSuccess($"[FC06] Register {_address} → {val} written.");
                    break;
                }

                case "Hex":
                {
                    if (!ushort.TryParse(input, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort val))
                    { MessageBox.Show("Invalid hex value. Example: 1A2B", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    await _client.WriteSingleRegisterAsync(_slaveId, _address, val);
                    HandleSuccess($"[FC06] Register {_address} → 0x{val:X4} written.");
                    break;
                }

                case "Float (32-bit)":
                case "Float Inverse (32-bit)":
                {
                    if (!float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                    { ShowRangeError("Float 32-bit", float.MinValue.ToString("G"), float.MaxValue.ToString("G")); return; }
                    // Inverse of the read convention: "Float (32-bit)" reads with inverse:true,
                    // so writing uses the same swap.
                    bool inverse  = _dataType == "Float (32-bit)";
                    ushort[] regs = FloatToRegisters(val, inverse);
                    await _client.WriteMultipleRegistersAsync(_slaveId, _address, regs);
                    HandleSuccess($"[FC16] Register {_address}-{_address + 1} → {val} written.");
                    break;
                }

                case "Long (32-bit)":
                case "Long Inverse (32-bit)":
                {
                    if (!int.TryParse(input, out int val))
                    { ShowRangeError("Long (Int32)", "-2147483648", "2147483647"); return; }
                    bool inverse  = _dataType == "Long (32-bit)";
                    ushort[] regs = LongToRegisters(val, inverse);
                    await _client.WriteMultipleRegistersAsync(_slaveId, _address, regs);
                    HandleSuccess($"[FC16] Register {_address}-{_address + 1} → {val} written.");
                    break;
                }

                case "Double (64-bit)":
                case "Double Inverse (64-bit)":
                {
                    if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                    { ShowRangeError("Double 64-bit", double.MinValue.ToString("G"), double.MaxValue.ToString("G")); return; }
                    bool inverse  = _dataType == "Double (64-bit)";
                    ushort[] regs = DoubleToRegisters(val, inverse);
                    await _client.WriteMultipleRegistersAsync(_slaveId, _address, regs);
                    HandleSuccess($"[FC16] Register {_address}-{_address + 3} → {val} written.");
                    break;
                }
            }
        }

        // ---------------------------------------------------------
        // VALUE → REGISTER ARRAY CONVERTERS
        // Exact inverse of the read path: mirrors ModbusDataConverter.ToFloat/ToLong/ToDouble.
        // ---------------------------------------------------------

        /// <summary>
        /// float → ushort[2].
        /// inverse=true ("Float 32-bit" mode): on read, registers[0]=lowWord, registers[1]=highWord;
        /// the same swap is applied when writing.
        /// </summary>
        private static ushort[] FloatToRegisters(float value, bool inverse)
        {
            uint bits     = BitConverter.SingleToUInt32Bits(value);
            ushort high   = (ushort)(bits >> 16);
            ushort low    = (ushort)(bits & 0xFFFF);
            return inverse ? new[] { low, high } : new[] { high, low };
        }

        private static ushort[] LongToRegisters(int value, bool inverse)
        {
            uint bits   = unchecked((uint)value);
            ushort high = (ushort)(bits >> 16);
            ushort low  = (ushort)(bits & 0xFFFF);
            return inverse ? new[] { low, high } : new[] { high, low };
        }

        /// <summary>
        /// double → ushort[4].
        /// inverse=true ("Double 64-bit" mode): on read, w0=registers[3], ..., w3=registers[0];
        /// when writing, registers[0]=w3, registers[1]=w2, registers[2]=w1, registers[3]=w0.
        /// </summary>
        private static ushort[] DoubleToRegisters(double value, bool inverse)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(value);
            ushort w0  = (ushort)(bits >> 48);
            ushort w1  = (ushort)((bits >> 32) & 0xFFFF);
            ushort w2  = (ushort)((bits >> 16) & 0xFFFF);
            ushort w3  = (ushort)(bits & 0xFFFF);
            return inverse ? new[] { w3, w2, w1, w0 } : new[] { w0, w1, w2, w3 };
        }

        // ---------------------------------------------------------
        // HELPERS
        // ---------------------------------------------------------

        private void HandleSuccess(string message)
        {
            OnSuccessLog?.Invoke(message);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void ShowRangeError(string typeName, string min, string max)
        {
            MessageBox.Show(
                $"The entered value is outside the range of {typeName}.\nValid range: {min}  →  {max}",
                "Range Exceeded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}