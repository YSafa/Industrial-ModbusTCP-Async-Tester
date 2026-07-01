using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using System.ComponentModel;
using ModbusTester.Core;
using ModbusTester.Exceptions;
using ModbusTester.Protocol;

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

        // Dinamik kontroller — yalnızca ilgili mod aktifken null değildir.
        private CheckBox[]? _bitCheckBoxes; // Binary modu: 16 CheckBox
        private CheckBox?   _coilCheckBox;  // Coil modu: tek CheckBox
        private TextBox?    _txtValue;       // Sayısal/Hex modu: metin kutusu

        
        /// <summary>
        /// Başarılı yazma sonrasında MainForm'un log paneline yeşil satır düşürmesi için
        /// dışarıdan bağlanan callback delegate.
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
            // Alanlar InitializeComponent'den ÖNCE atanmalıdır;
            // BuildDynamicUi içindeki pre-fill işlemleri bu değerlere bağlıdır.
            _client           = client;
            _slaveId          = slaveId;
            _address          = address;
            _dataType         = dataType;
            _functionCode     = functionCode;
            _currentValues    = currentValues;
            _currentBitValues = currentBitValues;

            InitializeComponent();

            // Header bilgisini alanlar atandıktan sonra güvenle dolduruyoruz.
            lblInfo.Text = $"Adres: {_address}    |    Veri Tipi: {_dataType}" +
                           $"\nFonksiyon: {_functionCode}    |    Slave ID: {_slaveId}";

            // Salt okunur fonksiyon kodlarında yazmayı devre dışı bırakıyoruz.
            if (_functionCode == ModbusFunctionCode.ReadDiscreteInputs ||
                _functionCode == ModbusFunctionCode.ReadInputRegisters)
            {
                btnWrite.Enabled  = false;
                lblInfo.Text     += "\n⚠ Bu alan salt okunurdur; yazma işlemi desteklenmez.";
            }

            BuildDynamicUi();
        }

        // ---------------------------------------------------------
        // DİNAMİK UI İNŞASI
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
        /// FC01/FC02 modu: tek bir CheckBox ile coil durumunu gösterir/yönetir.
        /// </summary>
        private void BuildCoilUi()
        {
            _coilCheckBox = new CheckBox
            {
                Text     = "Açık  (1)  /  Kapalı  (0)",
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
        /// Binary modu: 16 adet CheckBox ile tek bir register'ın her bitini ayrı ayrı gösterir.
        /// Kutucuklar MSB (B15) soldan başlayacak şekilde 8+8 satır halinde dizilir.
        /// </summary>
        private void BuildBinaryUi()
        {
            _bitCheckBoxes = new CheckBox[16];

            ushort currentVal = (_currentValues != null && _currentValues.Length > 0)
                ? _currentValues[0]
                : (ushort)0;

            // Bit etiketleri başlığı
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
                int bitIndex = 15 - i; // Soldan sağa: B15 → B0
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
        /// Sayısal/Hex modu: klavye filtreli tek bir TextBox ile değer girişi sağlar.
        /// </summary>
        private void BuildTextUi()
        {
            var lblInput = new Label
            {
                Text     = "Yazılacak Değer:",
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
            
            _txtValue.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true; 
                    BtnWrite_Click(sender, e);
                }
            };

            this.Controls.Add(lblInput);
            this.Controls.Add(_txtValue);
            
            this.ActiveControl = _txtValue;
            
            this.ClientSize = new Size(300, 175);
            RepositionButtons(130);
        }

        /// <summary>
        /// Butonları form boyutu netleştikten sonra doğru Y konumuna taşır.
        /// </summary>
        private void RepositionButtons(int y)
        {
            btnWrite.Location  = new Point(12,  y);
            btnCancel.Location = new Point(115, y);
        }

        // ---------------------------------------------------------
        // MEVCUT DEĞERİ METİNE DÖNÜŞTÜRME
        // ---------------------------------------------------------

        private string GetCurrentValueAsString()
        {
            if (_currentValues == null || _currentValues.Length == 0) return "";

            return _dataType switch
            {
                "Unsigned (16-bit)"       => _currentValues[0].ToString(),
                "Signed (16-bit)"         => ModbusDataConverter.ToSigned(_currentValues[0]).ToString(),
                "Hex"                     => _currentValues[0].ToString("X4"),
                // Inverse kuralı: "Float (32-bit)" okuma sırasında inverse:true kullanır.
                "Float (32-bit)"          => ModbusDataConverter.ToFloat(_currentValues, inverse: true).ToString("G", CultureInfo.InvariantCulture),
                "Float Inverse (32-bit)"  => ModbusDataConverter.ToFloat(_currentValues, inverse: false).ToString("G", CultureInfo.InvariantCulture),
                "Long (32-bit)"           => ModbusDataConverter.ToLong(_currentValues, inverse: true).ToString(),
                "Long Inverse (32-bit)"   => ModbusDataConverter.ToLong(_currentValues, inverse: false).ToString(),
                "Double (64-bit)"         => ModbusDataConverter.ToDouble(_currentValues, inverse: true).ToString("G", CultureInfo.InvariantCulture),
                "Double Inverse (64-bit)" => ModbusDataConverter.ToDouble(_currentValues, inverse: false).ToString("G", CultureInfo.InvariantCulture),
                _                         => _currentValues[0].ToString()
            };
        }

        // ---------------------------------------------------------
        // KLAVYE FİLTRELEME (KeyPress Masking)
        // ---------------------------------------------------------

        private void TxtValue_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar)) return; // Backspace, Delete vb. her zaman geçer.

            string current   = _txtValue?.Text ?? "";
            int    cursorPos = _txtValue?.SelectionStart ?? 0;

            switch (_dataType)
            {
                case "Hex":
                    // Yalnızca 0-9, A-F, a-f karakterlerine izin ver.
                    if (!IsHexChar(e.KeyChar)) e.Handled = true;
                    break;

                case "Unsigned (16-bit)":
                    // Yalnızca rakam.
                    if (!char.IsDigit(e.KeyChar)) e.Handled = true;
                    break;

                case "Signed (16-bit)":
                case "Long (32-bit)":
                case "Long Inverse (32-bit)":
                    // Rakam + en başta tek eksi işareti.
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
                    // Rakam + tek eksi + tek ondalık ayracı (, veya .)
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

        private static bool IsHexChar(char c)
            => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

        // ---------------------------------------------------------
        // YAZMA BUTONU
        // ---------------------------------------------------------

        private async void BtnWrite_Click(object? sender, EventArgs e)
        {
            btnWrite.Enabled = false; // Çift tıklamayı ve paralel yazma denemelerini engelle.

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
                MessageBox.Show($"Protokol hatası: {ex.Message}", "Yazma Hatası",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (ModbusConnectionException ex)
            {
                MessageBox.Show($"Bağlantı hatası: {ex.Message}", "Yazma Hatası",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (ModbusTimeoutException ex)
            {
                MessageBox.Show($"Zaman aşımı: {ex.Message}", "Yazma Hatası",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Beklenmeyen hata: {ex.Message}", "Yazma Hatası",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnWrite.Enabled = true;
            }
        }

        // ---------------------------------------------------------
        // MOD BAZLI YAZMA METOTLARI
        // ---------------------------------------------------------

        private async System.Threading.Tasks.Task WriteCoilAsync()
        {
            if (_coilCheckBox == null) return;

            bool value = _coilCheckBox.Checked;
            await _client.WriteSingleCoilAsync(_slaveId, _address, value);
            HandleSuccess($"[FC05] Coil {_address} → {(value ? "1 (Açık)" : "0 (Kapalı)")} yazıldı.");
        }

        private async System.Threading.Tasks.Task WriteBinaryAsync()
        {
            if (_bitCheckBoxes == null) return;

            ushort result = 0;
            for (int i = 0; i < 16; i++)
            {
                int bitIndex = 15 - i; // B15 solda, B0 sağda
                if (_bitCheckBoxes[i].Checked)
                    result |= (ushort)(1 << bitIndex);
            }

            await _client.WriteSingleRegisterAsync(_slaveId, _address, result);
            HandleSuccess($"[FC06] Register {_address} → 0b{Convert.ToString(result, 2).PadLeft(16, '0')} yazıldı.");
        }

        private async System.Threading.Tasks.Task WriteValueAsync()
        {
            // Virgülü noktaya çevirip InvariantCulture ile parse ediyoruz; Türkçe Windows'ta "," decimal ayracıdır.
            string input = (_txtValue?.Text ?? "").Replace(',', '.');

            switch (_dataType)
            {
                case "Unsigned (16-bit)":
                {
                    if (!ushort.TryParse(input, out ushort val))
                    { ShowRangeError("Unsigned 16-bit", "0", "65535"); return; }
                    await _client.WriteSingleRegisterAsync(_slaveId, _address, val);
                    HandleSuccess($"[FC06] Register {_address} → {val} yazıldı.");
                    break;
                }

                case "Signed (16-bit)":
                {
                    if (!short.TryParse(input, out short val))
                    { ShowRangeError("Signed 16-bit", "-32768", "32767"); return; }
                    await _client.WriteSingleRegisterAsync(_slaveId, _address, unchecked((ushort)val));
                    HandleSuccess($"[FC06] Register {_address} → {val} yazıldı.");
                    break;
                }

                case "Hex":
                {
                    if (!ushort.TryParse(input, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort val))
                    { MessageBox.Show("Geçersiz hex değeri. Örnek: 1A2B", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    await _client.WriteSingleRegisterAsync(_slaveId, _address, val);
                    HandleSuccess($"[FC06] Register {_address} → 0x{val:X4} yazıldı.");
                    break;
                }

                case "Float (32-bit)":
                case "Float Inverse (32-bit)":
                {
                    if (!float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                    { ShowRangeError("Float 32-bit", float.MinValue.ToString("G"), float.MaxValue.ToString("G")); return; }
                    // Okuma inverse kuralının tersi: "Float (32-bit)" inverse:true okur → yazarken de swap uygulanır.
                    bool inverse  = _dataType == "Float (32-bit)";
                    ushort[] regs = FloatToRegisters(val, inverse);
                    await _client.WriteMultipleRegistersAsync(_slaveId, _address, regs);
                    HandleSuccess($"[FC16] Register {_address}-{_address + 1} → {val} yazıldı.");
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
                    HandleSuccess($"[FC16] Register {_address}-{_address + 1} → {val} yazıldı.");
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
                    HandleSuccess($"[FC16] Register {_address}-{_address + 3} → {val} yazıldı.");
                    break;
                }
            }
        }

        // ---------------------------------------------------------
        // DEĞER → REGISTER DİZİSİ DÖNÜŞTÜRÜCÜLER
        // Okuma mantığının tam tersi: ModbusDataConverter.ToFloat/ToLong/ToDouble metodlarının simetriği.
        // ---------------------------------------------------------

        /// <summary>
        /// float → ushort[2].
        /// inverse=true ("Float 32-bit" modu): okuma sırasında registers[0]=lowWord, registers[1]=highWord idi;
        /// yazdığımızda da aynı swap uygulanır.
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
        /// inverse=true ("Double 64-bit" modu): okuma sırasında w0=registers[3],...,w3=registers[0] idi;
        /// yazdığımızda registers[0]=w3, registers[1]=w2, registers[2]=w1, registers[3]=w0 olur.
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
        // YARDIMCI METOTLAR
        // ---------------------------------------------------------

        private void HandleSuccess(string message)
        {
            // Callback üzerinden MainForm'un log paneline yeşil satır düşürüyoruz.
            OnSuccessLog?.Invoke(message);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void ShowRangeError(string typeName, string min, string max)
        {
            MessageBox.Show(
                $"Girilen değer {typeName} sınırları dışında.\nGeçerli aralık: {min}  →  {max}",
                "Sınır Aşımı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}