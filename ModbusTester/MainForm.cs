using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ModbusTester.Core;
using ModbusTester.Exceptions;
using ModbusTester.Protocol;

namespace ModbusTester
{
    public partial class MainForm : Form
    {
        private ModbusClient? _modbusClient;
        private CancellationTokenSource? _pollingCts;
        private Task? _pollingTask;
        
        
        // Fonksiyon kodu ComboBox öğelerini (görünen ad ↔ enum) eşleştiren liste.
        // Bu sayede ComboBox'ta boşluklu okunabilir isimler gösterilebilir ve
        // arka planda enum değerine karışıklık yaşanmadan erişilebilir.
        private readonly System.Collections.Generic.List<(string Display, ModbusFunctionCode Code)> _functionCodeItems = new()
        {
            ("Read Coils (FC01)",             ModbusFunctionCode.ReadCoils),
            ("Read Discrete Inputs (FC02)",   ModbusFunctionCode.ReadDiscreteInputs),
            ("Read Holding Registers (FC03)", ModbusFunctionCode.ReadHoldingRegisters),
            ("Read Input Registers (FC04)",   ModbusFunctionCode.ReadInputRegisters),
        };

        private ushort[]? _oldValues;
        private bool[]? _oldBitValues; // FC01/FC02 için ayrı bir "önceki değer" hafızası.

        private byte? _lastProtocolErrorCode = null;

        // Genel (protokol dışı) hataların log spam'ini önlemek için son mesajı saklıyoruz.
        // Başarılı okumada _lastProtocolErrorCode ile birlikte sıfırlanır.
        private string? _lastGeneralErrorMessage = null;

        // Otomatik yeniden bağlanma için sabitler.
        private const int ReconnectMaxAttempts  = 5;
        private const int ReconnectDelayMs      = 2000;
        
        private struct PollingParameters
        {
            public byte SlaveId;
            public ushort StartAddress;
            public string DataType;
            public ModbusFunctionCode FunctionCode;
            public int UserQuantity;
            public int IntervalMs;
        }

        public MainForm()
        {
            InitializeComponent();
            InitializeComboBoxes();
        }

        private void InitializeComboBoxes()
        {
            cmbFunctionCode.Items.Clear();
            foreach (var item in _functionCodeItems)
                cmbFunctionCode.Items.Add(item.Display);
            cmbFunctionCode.SelectedIndex = 2; // Varsayılan: Read Holding Registers (FC03)

            cmbDataType.Items.Clear();
            cmbDataType.Items.AddRange(new object[]
            {
                "Unsigned (16-bit)",
                "Signed (16-bit)",
                "Hex",
                "Binary",
                "Float (32-bit)",
                "Float Inverse (32-bit)",
                "Long (32-bit)",
                "Long Inverse (32-bit)",
                "Double (64-bit)",
                "Double Inverse (64-bit)"
            });
            cmbDataType.SelectedIndex = 0;

            numPollingInterval.Value = 200;
            numQuantity.Value        = 1;
            numQuantity.Minimum      = 1;
            numQuantity.Maximum      = 500;

            cmbFunctionCode.SelectedIndexChanged += CmbFunctionCode_SelectedIndexChanged;
        }
        
        /// <summary>
        /// Seçili ComboBox öğesini, _functionCodeItems listesi üzerinden güvenle ModbusFunctionCode'a çevirir.
        /// </summary>
        private ModbusFunctionCode GetSelectedFunctionCode()
        {
            int idx = cmbFunctionCode.SelectedIndex;
            return (idx >= 0 && idx < _functionCodeItems.Count)
                ? _functionCodeItems[idx].Code
                : ModbusFunctionCode.ReadHoldingRegisters;
        }

        /// <summary>
        /// FC01/FC02 (bit tabanlı) seçildiğinde Veri Tipi alanı anlamsız kaldığı için pasif hale getirilir
        /// ve sabit "Boolean (0/1)" gösterimine geçilir; FC03/FC04 seçilince tekrar aktif olur.
        /// </summary>
        private void CmbFunctionCode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            var selectedFc = GetSelectedFunctionCode();
            bool isBitBased = selectedFc == ModbusFunctionCode.ReadCoils ||
                              selectedFc == ModbusFunctionCode.ReadDiscreteInputs;
            cmbDataType.Enabled = !isBitBased;
        }

        // ---------------------------------------------------------
        // BAĞLAN / DURDUR BUTONLARI (Değişmedi)
        // ---------------------------------------------------------

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            if (_modbusClient != null && _modbusClient.IsConnected)
            {
                LogMessage("Zaten bağlı durumdasınız.", Color.Orange);
                return;
            }

            string ip = txtIpAddress.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                LogMessage("IP adresi boş olamaz.", Color.Red);
                return;
            }

            if (!int.TryParse(txtPort.Text.Trim(), out int port) || port <= 0 || port > 65535)
            {
                LogMessage("Geçersiz port numarası.", Color.Red);
                return;
            }

            btnConnect.Enabled = false;

            try
            {
                _modbusClient = new ModbusClient(ip, port)
                {
                    ConnectTimeoutMs = 3000,
                    IoTimeoutMs = 3000
                };

                LogMessage($"'{ip}:{port}' adresine bağlanılıyor...", Color.Gray);

                await _modbusClient.ConnectAsync();

                LogMessage("Bağlantı başarılı.", Color.Green);

                SetParameterControlsEnabled(false);
                
                _oldValues = null;
                _oldBitValues = null;
                
                StartPolling();
                btnStop.Enabled = true;
            }
            catch (ModbusConnectionException ex)
            {
                LogMessage($"BAĞLANTI HATASI: {ex.Message}", Color.Red);
            }
            catch (ModbusTimeoutException ex)
            {
                LogMessage($"BAĞLANTI ZAMAN AŞIMI: {ex.Message}", Color.Red);
            }
            catch (Exception ex)
            {
                LogMessage($"Beklenmeyen hata: {ex.Message}", Color.Red);
            }
            finally
            {
                btnConnect.Enabled = true;
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopPolling();

            _modbusClient?.Disconnect();
            LogMessage("Bağlantı kesildi, polling durduruldu.", Color.Orange);

            SetParameterControlsEnabled(true);

            btnStop.Enabled = false;
        }

        private void SetParameterControlsEnabled(bool enabled)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => SetParameterControlsEnabled(enabled)));
                return;
            }

            txtIpAddress.Enabled      = enabled;
            txtPort.Enabled           = enabled;
            numSlaveId.Enabled        = enabled;
            numStartAddress.Enabled   = enabled;
            numQuantity.Enabled       = enabled;
            numPollingInterval.Enabled = enabled;
            cmbFunctionCode.Enabled   = enabled;

            // Bit tabanlı fonksiyon seçiliyken cmbDataType zaten pasifti; bu durumu koruyoruz.
            var fc       = GetSelectedFunctionCode();
            bool bitMode = fc == ModbusFunctionCode.ReadCoils || fc == ModbusFunctionCode.ReadDiscreteInputs;
            cmbDataType.Enabled = enabled && !bitMode;
        }

        // ---------------------------------------------------------
        // POLLING DÖNGÜSÜ
        // ---------------------------------------------------------

        private void StartPolling()
        {
            StopPolling();

            _pollingCts = new CancellationTokenSource();
            var token = _pollingCts.Token;

            _pollingTask = Task.Run(() => PollingLoopAsync(token), token);
        }

        private void StopPolling()
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
        }

        private async Task PollingLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                PollingParameters parameters = GetPollingParametersSafe();

                try
                {
                    await ReadAndDisplayAsync(parameters);

                    // Başarılı okumada her iki hata hafızasını da sıfırla;
                    // sistem düzelince aynı hata yeniden gelirse bir kez daha loglanabilsin.
                    _lastProtocolErrorCode  = null;
                    _lastGeneralErrorMessage = null;
                }
                catch (ModbusProtocolException ex)
                {
                    // Protokol hatası: bağlantı sağlam, sadece slave mantıksal hata bildirdi.
                    // Aynı hata kodu tekrar ediyorsa log ekranını kirletmiyoruz.
                    if (_lastProtocolErrorCode == null || _lastProtocolErrorCode.Value != ex.ExceptionCode)
                    {
                        LogMessage($"PROTOKOL HATASI (Kod: {ex.ExceptionCode}): {ex.Message}", Color.Red);
                        _lastProtocolErrorCode = ex.ExceptionCode;
                    }

                    ClearGridSafe();
                }
                catch (ModbusTimeoutException ex)
                {
                    // Fiziksel bağlantı kopması: döngüyü kırmak yerine yeniden bağlanmayı deniyoruz.
                    LogMessage($"ZAMAN AŞIMI: {ex.Message}", Color.Red);
                    ClearGridSafe();

                    bool reconnected = await TryReconnectAsync(token);
                    if (!reconnected)
                    {
                        // 5 deneme sonunda da bağlanamazsak döngüyü kırıp arayüzü serbest bırakıyoruz.
                        SetParameterControlsEnabled(true);
                        SetConnectionButtonsAfterDrop();
                        break;
                    }

                    // Başarıyla yeniden bağlandıysak önbelleği temizleyip kaldığımız yerden devam ediyoruz.
                    _lastProtocolErrorCode   = null;
                    _lastGeneralErrorMessage = null;
                    _oldValues               = null;
                    _oldBitValues            = null;
                    continue;
                }
                catch (ModbusConnectionException ex)
                {
                    LogMessage($"BAĞLANTI HATASI: {ex.Message}", Color.Red);
                    ClearGridSafe();

                    bool reconnected = await TryReconnectAsync(token);
                    if (!reconnected)
                    {
                        SetParameterControlsEnabled(true);
                        SetConnectionButtonsAfterDrop();
                        break;
                    }

                    _lastProtocolErrorCode   = null;
                    _lastGeneralErrorMessage = null;
                    _oldValues               = null;
                    _oldBitValues            = null;
                    continue;
                }
                catch (Exception ex)
                {
                    // Yerel C# istisnaları (ör: ArgumentOutOfRangeException - adet sınırı aşıldı).
                    // Aynı mesaj art arda tekrar ediyorsa log ekranına yalnızca 1 kez basıyoruz.
                    if (_lastGeneralErrorMessage == null || _lastGeneralErrorMessage != ex.Message)
                    {
                        LogMessage($"HATA: {ex.Message}", Color.Red);
                        _lastGeneralErrorMessage = ex.Message;
                    }

                    ClearGridSafe();
                }

                try
                {
                    await Task.Delay(parameters.IntervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Bağlantı koptuğunda maksimum <see cref="ReconnectMaxAttempts"/> kez yeniden bağlanmayı dener.
        /// Her denemede log ekranına sarı renkli bilgi mesajı basar.
        /// Başarıda true, tüm denemeler tükenince false döner.
        /// </summary>
        private async Task<bool> TryReconnectAsync(CancellationToken token)
        {
            if (_modbusClient == null) return false;

            for (int attempt = 1; attempt <= ReconnectMaxAttempts; attempt++)
            {
                if (token.IsCancellationRequested) return false;

                LogMessage($"Yeniden bağlanılıyor... Deneme {attempt}/{ReconnectMaxAttempts}", Color.Yellow);

                try
                {
                    await Task.Delay(ReconnectDelayMs, token);
                    await _modbusClient.ConnectAsync();

                    LogMessage("Yeniden bağlantı başarılı, polling devam ediyor.", Color.Green);
                    return true;
                }
                catch (TaskCanceledException)
                {
                    // Kullanıcı Durdur butonuna bastı; sessizce çıkıyoruz.
                    return false;
                }
                catch (Exception ex)
                {
                    // Bu deneme başarısız; bir sonraki turda tekrar denenecek.
                    LogMessage($"Deneme {attempt} başarısız: {ex.Message}", Color.OrangeRed);
                }
            }

            LogMessage($"{ReconnectMaxAttempts} deneme sonunda bağlantı kurulamadı. Lütfen manuel olarak yeniden bağlanın.", Color.Red);
            return false;
        }

        private void SetConnectionButtonsAfterDrop()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(SetConnectionButtonsAfterDrop));
                return;
            }

            btnConnect.Enabled = true;
            btnStop.Enabled = false;
        }

        private void ClearGridSafe()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(ClearGridSafe));
                return;
            }

            dgvRegisters.Rows.Clear();
            _oldValues = null;
            _oldBitValues = null;
        }

        private PollingParameters GetPollingParametersSafe()
        {
            if (this.InvokeRequired)
            {
                return (PollingParameters)this.Invoke(new Func<PollingParameters>(ReadPollingParametersFromUi));
            }
            return ReadPollingParametersFromUi();
        }

        private PollingParameters ReadPollingParametersFromUi()
        {
            return new PollingParameters
            {
                SlaveId      = (byte)numSlaveId.Value,
                StartAddress = (ushort)numStartAddress.Value,
                DataType     = cmbDataType.SelectedItem?.ToString() ?? "Unsigned (16-bit)",
                FunctionCode = GetSelectedFunctionCode(), // Artık enum'a güvenle eşleniyor.
                UserQuantity = (int)numQuantity.Value,
                IntervalMs   = (int)numPollingInterval.Value
            };
        }

        // ---------------------------------------------------------
        // OKUMA + EKRANA YANSITMA
        // ---------------------------------------------------------

        private async Task ReadAndDisplayAsync(PollingParameters parameters)
        {
            // Seçilen fonksiyon koduna göre bit tabanlı mı (FC01/FC02) yoksa register tabanlı mı (FC03/FC04) ayrıştırıyoruz.
            bool isBitBased = parameters.FunctionCode == ModbusFunctionCode.ReadCoils ||
                               parameters.FunctionCode == ModbusFunctionCode.ReadDiscreteInputs;

            if (isBitBased)
            {
                await ReadAndDisplayBitsAsync(parameters);
            }
            else
            {
                await ReadAndDisplayRegistersAsync(parameters);
            }
        }

        /// <summary>
        /// FC01 (Read Coils) ve FC02 (Read Discrete Inputs) için okuma ve ekrana yansıtma akışı.
        /// </summary>
        private async Task ReadAndDisplayBitsAsync(PollingParameters parameters)
        {
            if (parameters.UserQuantity > 2000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters.UserQuantity), "Bit miktarı 2000'i aşıyor.");
            }

            if (_modbusClient == null) return;
            
            bool[] bits = parameters.FunctionCode == ModbusFunctionCode.ReadCoils
                ? await _modbusClient.ReadCoilsAsync(parameters.SlaveId, parameters.StartAddress, (ushort)parameters.UserQuantity)
                : await _modbusClient.ReadDiscreteInputsAsync(parameters.SlaveId, parameters.StartAddress, (ushort)parameters.UserQuantity);

            _lastProtocolErrorCode = null;

            bool hasChanged = HasBitValuesChanged(_oldBitValues, bits);

            if (hasChanged)
            {
                _oldBitValues = (bool[])bits.Clone();

                this.Invoke(new Action(() =>
                {
                    LogMessage("Veri Değişti.", Color.Green);
                    DisplayBitsInGrid(bits, parameters.StartAddress);
                }));
            }
        }

        /// <summary>
        /// FC03 (Read Holding Registers) ve FC04 (Read Input Registers) için okuma ve ekrana yansıtma akışı.
        /// </summary>
        private async Task ReadAndDisplayRegistersAsync(PollingParameters parameters)
        {
            int registerSizePerItem = GetRegisterSizeForDataType(parameters.DataType);
            int totalQuantity = parameters.UserQuantity * registerSizePerItem;

            /*if (totalQuantity > 125)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalQuantity), "Toplam register sayısı 125'i aşıyor; adet veya veri tipini azaltın.");
            }*/

            if (_modbusClient == null) return;
            
            ushort[] registers = parameters.FunctionCode == ModbusFunctionCode.ReadHoldingRegisters
                ? await _modbusClient.ReadHoldingRegistersAsync(parameters.SlaveId, parameters.StartAddress, (ushort)totalQuantity)
                : await _modbusClient.ReadInputRegistersAsync(parameters.SlaveId, parameters.StartAddress, (ushort)totalQuantity);

            _lastProtocolErrorCode = null;

            bool hasChanged = HasValuesChanged(_oldValues, registers);

            if (hasChanged)
            {
                _oldValues = (ushort[])registers.Clone();

                this.Invoke(new Action(() =>
                {
                    LogMessage("Veri Değişti.", Color.Green);
                    DisplayRegistersInGrid(registers, parameters.DataType, parameters.StartAddress, registerSizePerItem);
                }));
            }
        }

        private bool HasValuesChanged(ushort[]? oldValues, ushort[] newValues)
        {
            if (oldValues == null || oldValues.Length != newValues.Length) return true;

            for (int i = 0; i < oldValues.Length; i++)
            {
                if (oldValues[i] != newValues[i]) return true;
            }
            return false;
        }

        private bool HasBitValuesChanged(bool[]? oldValues, bool[] newValues)
        {
            if (oldValues == null || oldValues.Length != newValues.Length) return true;

            for (int i = 0; i < oldValues.Length; i++)
            {
                if (oldValues[i] != newValues[i]) return true;
            }
            return false;
        }

        private int GetRegisterSizeForDataType(string dataType)
        {
            switch (dataType)
            {
                case "Float (32-bit)":
                case "Float Inverse (32-bit)":
                case "Long (32-bit)":
                case "Long Inverse (32-bit)":
                    return 2;

                case "Double (64-bit)":
                case "Double Inverse (64-bit)":
                    return 4;

                default:
                    return 1;
            }
        }

        /// <summary>
        /// FC01/FC02 sonucu gelen bool[] dizisini DataGridView'e "1"/"0" olarak satır satır basar.
        /// </summary>
        private void DisplayBitsInGrid(bool[] bits, ushort startAddress)
        {
            dgvRegisters.Rows.Clear();

            for (int i = 0; i < bits.Length; i++)
            {
                ushort itemAddress = (ushort)(startAddress + i);

                // mbslave ve çoğu SCADA aracı bit durumlarını 1/0 olarak gösterir; okunabilirlik için bu formatı kullanıyoruz.
                dgvRegisters.Rows.Add(itemAddress, bits[i] ? "1" : "0");
            }
        }

        private void DisplayRegistersInGrid(ushort[] registers, string dataType, ushort startAddress, int registerSizePerItem)
        {
            dgvRegisters.Rows.Clear();

            for (int i = 0; i < registers.Length; i += registerSizePerItem)
            {
                ushort itemAddress = (ushort)(startAddress + i);

                switch (dataType)
                {
                    case "Unsigned (16-bit)":
                        dgvRegisters.Rows.Add(itemAddress, registers[i]);
                        break;

                    case "Signed (16-bit)":
                        short signed = ModbusDataConverter.ToSigned(registers[i]);
                        dgvRegisters.Rows.Add(itemAddress, signed);
                        break;

                    case "Hex":
                        string hex = ModbusDataConverter.ToHex(registers[i]);
                        dgvRegisters.Rows.Add(itemAddress, hex);
                        break;

                    case "Binary":
                        string binary = ModbusDataConverter.ToBinary(registers[i]);
                        dgvRegisters.Rows.Add(itemAddress, binary);
                        break;

                    case "Float (32-bit)":
                    {
                        ushort[] slice = SliceRegisters(registers, i, registerSizePerItem);
                        float val = ModbusDataConverter.ToFloat(slice, inverse: true);
                        dgvRegisters.Rows.Add($"{itemAddress}-{itemAddress + 1}", val);
                        break;
                    }

                    case "Float Inverse (32-bit)":
                    {
                        ushort[] slice = SliceRegisters(registers, i, registerSizePerItem);
                        float val = ModbusDataConverter.ToFloat(slice, inverse: false);
                        dgvRegisters.Rows.Add($"{itemAddress}-{itemAddress + 1}", val);
                        break;
                    }

                    case "Long (32-bit)":
                    {
                        ushort[] slice = SliceRegisters(registers, i, registerSizePerItem);
                        int val = ModbusDataConverter.ToLong(slice, inverse: true);
                        dgvRegisters.Rows.Add($"{itemAddress}-{itemAddress + 1}", val);
                        break;
                    }

                    case "Long Inverse (32-bit)":
                    {
                        ushort[] slice = SliceRegisters(registers, i, registerSizePerItem);
                        int val = ModbusDataConverter.ToLong(slice, inverse: false);
                        dgvRegisters.Rows.Add($"{itemAddress}-{itemAddress + 1}", val);
                        break;
                    }

                    case "Double (64-bit)":
                    {
                        ushort[] slice = SliceRegisters(registers, i, registerSizePerItem);
                        double val = ModbusDataConverter.ToDouble(slice, inverse: true);
                        dgvRegisters.Rows.Add($"{itemAddress}-{itemAddress + 3}", val);
                        break;
                    }

                    case "Double Inverse (64-bit)":
                    {
                        ushort[] slice = SliceRegisters(registers, i, registerSizePerItem);
                        double val = ModbusDataConverter.ToDouble(slice, inverse: false);
                        dgvRegisters.Rows.Add($"{itemAddress}-{itemAddress + 3}", val);
                        break;
                    }
                }
            }
        }

        private ushort[] SliceRegisters(ushort[] source, int startIndex, int length)
        {
            ushort[] slice = new ushort[length];
            Array.Copy(source, startIndex, slice, 0, length);
            return slice;
        }

        // ---------------------------------------------------------
        // LOG YAZDIRMA
        // ---------------------------------------------------------

        private void LogMessage(string message, Color color)
        {
            if (rtbLogs.InvokeRequired)
            {
                rtbLogs.Invoke(new Action(() => AppendLog(message, color)));
            }
            else
            {
                AppendLog(message, color);
            }
        }

        private void AppendLog(string message, Color color)
        {
            rtbLogs.SelectionStart = rtbLogs.TextLength;
            rtbLogs.SelectionLength = 0;

            rtbLogs.SelectionColor = color;
            rtbLogs.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            rtbLogs.SelectionColor = rtbLogs.ForeColor;

            rtbLogs.ScrollToCaret();
        }

        // ---------------------------------------------------------
        // FORM KAPANIRKEN TEMİZLİK
        // ---------------------------------------------------------

        /// <summary>
        /// DataGridView'de bir satıra çift tıklandığında WriteForm'u açar.
        /// Tıklanan satırın adresi ve mevcut değeri otomatik olarak forma aktarılır.
        /// </summary>
        private void DgvRegisters_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (_modbusClient == null || !_modbusClient.IsConnected) return;

            // UI thread'indeyiz, Invoke gerekmez.
            var parameters = ReadPollingParametersFromUi();

            bool isBitBased   = parameters.FunctionCode == ModbusFunctionCode.ReadCoils ||
                                parameters.FunctionCode == ModbusFunctionCode.ReadDiscreteInputs;
            int registerSize  = isBitBased ? 1 : GetRegisterSizeForDataType(parameters.DataType);

            // Hücredeki adres değerini parse ediyoruz ("0", "0-1", "0-3" gibi formatlara karşı koruma).
            string addrCell   = dgvRegisters.Rows[e.RowIndex].Cells[0].Value?.ToString() ?? "0";
            string firstPart  = addrCell.Split('-')[0].Trim();
            if (!ushort.TryParse(firstPart, out ushort itemAddress)) return;

            // İlgili satırın ham register veya bit dilimini önceki okumadan alıyoruz.
            ushort[]? slice    = null;
            bool[]?   bitSlice = null;
            int        rowIdx  = e.RowIndex;

            if (isBitBased)
            {
                if (_oldBitValues != null && rowIdx < _oldBitValues.Length)
                    bitSlice = new[] { _oldBitValues[rowIdx] };
            }
            else
            {
                if (_oldValues != null)
                {
                    int startIdx = rowIdx * registerSize;
                    if (startIdx + registerSize <= _oldValues.Length)
                    {
                        slice = new ushort[registerSize];
                        Array.Copy(_oldValues, startIdx, slice, 0, registerSize);
                    }
                }
            }

            using var writeForm = new WriteForm(
                _modbusClient,
                parameters.SlaveId,
                itemAddress,
                parameters.DataType,
                parameters.FunctionCode,
                slice,
                bitSlice);

            // Başarılı yazma logu doğrudan MainForm'un log paneline düşer.
            writeForm.OnSuccessLog = msg => LogMessage(msg, Color.Green);

            if (writeForm.ShowDialog(this) == DialogResult.OK)
            {
                // Yazma sonrası bir sonraki polling adımında tabloyu yenile.
                _oldValues    = null;
                _oldBitValues = null;
            }
        }
        
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopPolling();
            _modbusClient?.Disconnect();
        }
    }
}