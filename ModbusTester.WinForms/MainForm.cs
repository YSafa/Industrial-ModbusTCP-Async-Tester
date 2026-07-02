using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic;
using ModbusTester.Core;
using ModbusTester.Core.Core;
using ModbusTester.Core.Exceptions;
using ModbusTester.Core.Protocol;

namespace ModbusTester
{
    public partial class MainForm : Form
    {
        // Açık her sekmenin bağımsız durumunu (client, polling görevi, kontroller) tutan liste.
        private readonly List<TabSession> _sessions = new();

        // Dinamik sekmelere isim önerisi üretmek için sayaç.
        private int _dynamicTabCounter = 1;

        
        // FormClosing'in Windows tarafından erken sonlandırılmasını engellemek için kullanılan bayrak.
        // İki aşamalı kapanma deseninin (Two-Stage Close) çekirdeği: async void event handler'da
        // await kontrolü bırakır bırakmaz Windows formu yok etmeye devam edebileceği için,
        // gerçek kapanışa yalnızca temizlik %100 bittiğinde izin veriyoruz.
        private bool _readyToClose = false;
        
        // Tüm sekmelerin ortak kullandığı fonksiyon kodu eşleme listesi (Display <-> enum).
        private readonly List<(string Display, ModbusFunctionCode Code)> _functionCodeItems = new()
        {
            ("Read Coils (FC01)",             ModbusFunctionCode.ReadCoils),
            ("Read Discrete Inputs (FC02)",   ModbusFunctionCode.ReadDiscreteInputs),
            ("Read Holding Registers (FC03)", ModbusFunctionCode.ReadHoldingRegisters),
            ("Read Input Registers (FC04)",   ModbusFunctionCode.ReadInputRegisters),
        };

        private const int ReconnectMaxAttempts = 5;
        private const int ReconnectDelayMs     = 2000;

        public MainForm()
        {
            InitializeComponent();

            // Uygulama açılışında kapatılamayan varsayılan "Ana Cihaz" sekmesi oluşturulur.
            CreateAndAddSession("Ana Cihaz", closable: false);
        }

        // ---------------------------------------------------------
        // YENİ SEKME EKLEME
        // ---------------------------------------------------------

        private void BtnAddTab_Click(object? sender, EventArgs e)
        {
            string tabName = Interaction.InputBox(
                "Yeni sekme için bir isim girin:", "Sekme Adı", $"Cihaz {_dynamicTabCounter + 1}");

            if (string.IsNullOrWhiteSpace(tabName)) return;

            _dynamicTabCounter++;
            CreateAndAddSession(tabName.Trim(), closable: true);
        }

        private void CreateAndAddSession(string tabName, bool closable)
        {
            TabSession session = BuildSession(tabName, closable);
            _sessions.Add(session);

            tabControl.TabPages.Add(session.Page);
            tabControl.SelectedTab = session.Page;
        }

        // ---------------------------------------------------------
        // SEKME İÇİ UI İNŞASI (Orijinal Düzenin Birebir Klonu)
        // ---------------------------------------------------------

        private TabSession BuildSession(string tabName, bool closable)
        {
            var page = new TabPage(tabName);
            var session = new TabSession(page);

            // --- IP / Port ---
            var lblIp = new Label { Text = "IP Adresi:", Location = new Point(12, 15), AutoSize = true };
            session.TxtIp = new TextBox { Location = new Point(130, 12), Size = new Size(150, 20), Text = "127.0.0.1" };

            var lblPort = new Label { Text = "Port:", Location = new Point(12, 45), AutoSize = true };
            session.TxtPort = new TextBox { Location = new Point(130, 42), Size = new Size(80, 20), Text = "502" };

            // --- Slave ID ---
            var lblSlaveId = new Label { Text = "Slave ID:", Location = new Point(12, 75), AutoSize = true };
            session.NumSlaveId = new NumericUpDown
            {
                Location = new Point(130, 72), Size = new Size(80, 20), Minimum = 1, Maximum = 255, Value = 1
            };

            // --- Başlangıç Adresi ---
            var lblStartAddress = new Label { Text = "Başlangıç Adresi:", Location = new Point(12, 105), AutoSize = true };
            session.NumStartAddress = new NumericUpDown
            {
                Location = new Point(130, 102), Size = new Size(80, 20), Minimum = 0, Maximum = 65535, Value = 0
            };

            // --- Fonksiyon Kodu ---
            var lblFunctionCode = new Label { Text = "Fonksiyon Kodu:", Location = new Point(12, 135), AutoSize = true };
            session.CmbFunctionCode = new ComboBox
            {
                Location = new Point(130, 132), Size = new Size(220, 21), DropDownStyle = ComboBoxStyle.DropDownList
            };

            // --- Veri Tipi ---
            var lblDataType = new Label { Text = "Veri Tipi:", Location = new Point(12, 165), AutoSize = true };
            session.CmbDataType = new ComboBox
            {
                Location = new Point(130, 162), Size = new Size(220, 21), DropDownStyle = ComboBoxStyle.DropDownList
            };

            // --- Adet ---
            var lblQuantity = new Label { Text = "Adet:", Location = new Point(12, 195), AutoSize = true };
            session.NumQuantity = new NumericUpDown
            {
                Location = new Point(130, 192), Size = new Size(80, 20), Minimum = 1, Maximum = 500, Value = 1
            };

            // --- Sorgulama Hızı ---
            var lblPollingInterval = new Label { Text = "Sorgulama (ms):", Location = new Point(12, 225), AutoSize = true };
            session.NumPollingInterval = new NumericUpDown
            {
                Location = new Point(130, 222), Size = new Size(80, 20), Minimum = 50, Maximum = 60000, Value = 200
            };

            // --- Bağlan / Durdur ---
            session.BtnConnect = new Button
            {
                Text = "Bağlan", Location = new Point(130, 255), Size = new Size(90, 28), UseVisualStyleBackColor = true
            };
            session.BtnConnect.Click += (s, e) => BtnConnect_Click(session);

            session.BtnStop = new Button
            {
                Text = "Durdur", Location = new Point(228, 255), Size = new Size(90, 28),
                UseVisualStyleBackColor = true, Enabled = false
            };
            session.BtnStop.Click += (s, e) => BtnStop_Click(session);

            // --- DataGridView ---
            var colAddress = new DataGridViewTextBoxColumn { HeaderText = "Adres", Name = "colAddress", ReadOnly = true };
            var colValue   = new DataGridViewTextBoxColumn { HeaderText = "Değer", Name = "colValue", ReadOnly = true };

            session.Dgv = new DataGridView
            {
                Location = new Point(12, 295),
                Size = new Size(440, 220),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            session.Dgv.Columns.AddRange(colAddress, colValue);
            session.Dgv.CellDoubleClick += (s, e) => DgvRegisters_CellDoubleClick(session, e);

            // --- Terminal / Log ---
            session.RtbLogs = new RichTextBox
            {
                Location = new Point(12, 525),
                Size = new Size(440, 150),
                BackColor = Color.Black,
                ForeColor = Color.White,
                ReadOnly = true
            };

            page.Controls.Add(lblIp);
            page.Controls.Add(session.TxtIp);
            page.Controls.Add(lblPort);
            page.Controls.Add(session.TxtPort);
            page.Controls.Add(lblSlaveId);
            page.Controls.Add(session.NumSlaveId);
            page.Controls.Add(lblStartAddress);
            page.Controls.Add(session.NumStartAddress);
            page.Controls.Add(lblFunctionCode);
            page.Controls.Add(session.CmbFunctionCode);
            page.Controls.Add(lblDataType);
            page.Controls.Add(session.CmbDataType);
            page.Controls.Add(lblQuantity);
            page.Controls.Add(session.NumQuantity);
            page.Controls.Add(lblPollingInterval);
            page.Controls.Add(session.NumPollingInterval);
            page.Controls.Add(session.BtnConnect);
            page.Controls.Add(session.BtnStop);
            page.Controls.Add(session.Dgv);
            page.Controls.Add(session.RtbLogs);

            // Yalnızca dinamik eklenen sekmelerde "Sekmeyi Kapat" butonu bulunur.
            if (closable)
            {
                session.BtnCloseTab = new Button
                {
                    Text = "Sekmeyi Kapat", Location = new Point(326, 255), Size = new Size(120, 28),
                    UseVisualStyleBackColor = true
                };
                session.BtnCloseTab.Click += (s, e) => CloseSession(session);
                page.Controls.Add(session.BtnCloseTab);
            }

            InitializeComboBoxes(session);

            return session;
        }

        private void InitializeComboBoxes(TabSession session)
        {
            session.CmbFunctionCode.Items.Clear();
            foreach (var item in _functionCodeItems)
                session.CmbFunctionCode.Items.Add(item.Display);
            session.CmbFunctionCode.SelectedIndex = 2; // Varsayılan: Read Holding Registers (FC03)

            session.CmbDataType.Items.Clear();
            session.CmbDataType.Items.AddRange(new object[]
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
            session.CmbDataType.SelectedIndex = 0;

            session.CmbFunctionCode.SelectedIndexChanged += (s, e) => CmbFunctionCode_SelectedIndexChanged(session);
        }

        private ModbusFunctionCode GetSelectedFunctionCode(TabSession session)
        {
            int idx = session.CmbFunctionCode.SelectedIndex;
            return (idx >= 0 && idx < _functionCodeItems.Count)
                ? _functionCodeItems[idx].Code
                : ModbusFunctionCode.ReadHoldingRegisters;
        }

        private void CmbFunctionCode_SelectedIndexChanged(TabSession session)
        {
            var fc = GetSelectedFunctionCode(session);
            bool isBitBased = fc == ModbusFunctionCode.ReadCoils || fc == ModbusFunctionCode.ReadDiscreteInputs;
            session.CmbDataType.Enabled = !isBitBased;
        }

        // ---------------------------------------------------------
        // BAĞLAN / DURDUR (Sekme Bazlı)
        // ---------------------------------------------------------

        private async void BtnConnect_Click(TabSession session)
        {
            if (session.Client != null && session.Client.IsConnected)
            {
                LogMessage(session, "Zaten bağlı durumdasınız.", Color.Orange);
                return;
            }

            string ip = session.TxtIp.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                LogMessage(session, "IP adresi boş olamaz.", Color.Red);
                return;
            }

            if (!int.TryParse(session.TxtPort.Text.Trim(), out int port) || port <= 0 || port > 65535)
            {
                LogMessage(session, "Geçersiz port numarası.", Color.Red);
                return;
            }

            session.BtnConnect.Enabled = false;

            try
            {
                session.Client = new ModbusClient(ip, port) { ConnectTimeoutMs = 3000, IoTimeoutMs = 3000 };

                LogMessage(session, $"'{ip}:{port}' adresine bağlanılıyor...", Color.Gray);
                await session.Client.ConnectAsync();
                LogMessage(session, "Bağlantı başarılı.", Color.Green);

                // Yalnızca IP/Port/SlaveId kilitlenir. StartAddress, Quantity, DataType, FunctionCode
                // ve PollingInterval bağlantı canlıyken bile Hot-Reload ile serbest kalır.
                SetConnectionControlsEnabled(session, false);

                session.OldValues = null;
                session.OldBitValues = null;

                await StartPolling(session);
                session.BtnStop.Enabled = true;
            }
            catch (ModbusConnectionException ex) { LogMessage(session, $"BAĞLANTI HATASI: {ex.Message}", Color.Red); }
            catch (ModbusTimeoutException ex)    { LogMessage(session, $"BAĞLANTI ZAMAN AŞIMI: {ex.Message}", Color.Red); }
            catch (Exception ex)                 { LogMessage(session, $"Beklenmeyen hata: {ex.Message}", Color.Red); }
            finally { session.BtnConnect.Enabled = true; }
        }

        private async void BtnStop_Click(TabSession session)
        {
            await StopPolling(session);

            session.Client?.Disconnect();
            LogMessage(session, "Bağlantı kesildi, polling durduruldu.", Color.Orange);
            SetConnectionControlsEnabled(session, true);
            session.BtnStop.Enabled = false;
        }

        //// <summary>
        /// Bağlantı aktif olduğu sürece TÜM konfigürasyon kontrollerini kilitler.
        /// Bu sayede arka plan polling döngüsü, parametreleri her tick'te UI thread'inden
        /// okumak zorunda kalmaz; bağlantı kurulmadan hemen önce tek seferlik bir okuma yeterli olur.
        /// </summary>
        private void SetConnectionControlsEnabled(TabSession session, bool enabled)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => SetConnectionControlsEnabled(session, enabled)));
                return;
            }

            session.TxtIp.Enabled = enabled;
            session.TxtPort.Enabled = enabled;
            session.NumSlaveId.Enabled = enabled;
            session.NumStartAddress.Enabled = enabled;
            session.NumQuantity.Enabled = enabled;
            session.CmbFunctionCode.Enabled = enabled;
            session.CmbDataType.Enabled = enabled;
            session.NumPollingInterval.Enabled = enabled;
        }

        // ---------------------------------------------------------
        // POLLING DÖNGÜSÜ (PeriodicTimer, Hot-Reload destekli)
        // ---------------------------------------------------------

        private async Task StartPolling(TabSession session)
        {
            // Önceki görev varsa tamamen sonlanmasını bekleyip TEMİZ bir başlangıç yapıyoruz.
            await StopPolling(session);

            session.PollingCts = new CancellationTokenSource();
            var token = session.PollingCts.Token;
            session.PollingTask = Task.Run(() => PollingLoopAsync(session, token), token);
        }

        /// <summary>
        /// Arka plan polling görevini güvenli şekilde durdurur: önce iptal sinyali gönderir,
        /// ardından görevin GERÇEKTEN sona ermesini bekler (thread join), ve ancak o zaman
        /// CancellationTokenSource / PeriodicTimer gibi kaynakları imha eder. Bu sıralama,
        /// arka plan thread'inin imha edilmiş bir token veya kontrol üzerinde işlem yapmaya
        /// çalışıp çökmesini (ObjectDisposedException / InvalidOperationException) engeller.
        /// </summary>
        private async Task StopPolling(TabSession session)
        {
            session.PollingCts?.Cancel();

            if (session.PollingTask != null && !session.PollingTask.IsCompleted)
            {
                try
                {
                    // Arka plan görevi kendi içindeki catch(OperationCanceledException) ile
                    // döngüden çıkıp normal şekilde tamamlanır; yine de bir güvenlik payı olarak yakalıyoruz.
                    await session.PollingTask;
                }
                catch (OperationCanceledException)
                {
                    // Beklenen iptal senaryosu; sessizce geçiyoruz.
                }
            }

            // Arka plan thread'i artık kesin olarak durdu; kaynakları güvenle imha edebiliriz.
            session.PollingCts?.Dispose();
            session.PollingCts = null;
            session.PollingTask = null;

            session.Timer?.Dispose();
            session.Timer = null;
        }

        private async Task PollingLoopAsync(TabSession session, CancellationToken token)
{
    // Parametreler bağlantı kurulmadan önce zaten kilitlendiği için, döngüye girmeden hemen
    // önce SADECE BİR KEZ UI thread'inden okunuyor. Döngü boyunca bu sabit değer kullanılacak;
    // her tick'te tekrar Invoke ile UI'a gidilmiyor.
    PollingParameters parameters = GetPollingParametersSafe(session);

    session.CurrentIntervalMs = Math.Max(50, parameters.IntervalMs);
    session.Timer = new PeriodicTimer(TimeSpan.FromMilliseconds(session.CurrentIntervalMs));

    while (!token.IsCancellationRequested)
    {
        try
        {
            await ReadAndDisplayAsync(session, parameters);
            session.LastProtocolErrorCode = null;
            session.LastGeneralErrorMessage = null;
        }
        catch (ModbusProtocolException ex)
        {
            if (session.LastProtocolErrorCode == null || session.LastProtocolErrorCode.Value != ex.ExceptionCode)
            {
                LogMessage(session, $"PROTOKOL HATASI (Kod: {ex.ExceptionCode}): {ex.Message}", Color.Red);
                session.LastProtocolErrorCode = ex.ExceptionCode;
            }
            ClearGridSafe(session);
        }
        catch (ModbusTimeoutException ex)
        {
            LogMessage(session, $"ZAMAN AŞIMI: {ex.Message}", Color.Red);
            ClearGridSafe(session);

            bool reconnected = await TryReconnectAsync(session, token);
            if (!reconnected)
            {
                SetConnectionControlsEnabled(session, true);
                SetConnectionButtonsAfterDrop(session);
                break;
            }

            session.LastProtocolErrorCode = null;
            session.LastGeneralErrorMessage = null;
            session.OldValues = null;
            session.OldBitValues = null;
            continue;
        }
        catch (ModbusConnectionException ex)
        {
            LogMessage(session, $"BAĞLANTI HATASI: {ex.Message}", Color.Red);
            ClearGridSafe(session);

            bool reconnected = await TryReconnectAsync(session, token);
            if (!reconnected)
            {
                SetConnectionControlsEnabled(session, true);
                SetConnectionButtonsAfterDrop(session);
                break;
            }

            session.LastProtocolErrorCode = null;
            session.LastGeneralErrorMessage = null;
            session.OldValues = null;
            session.OldBitValues = null;
            continue;
        }
        catch (Exception ex)
        {
            if (session.LastGeneralErrorMessage == null || session.LastGeneralErrorMessage != ex.Message)
            {
                LogMessage(session, $"HATA: {ex.Message}", Color.Red);
                session.LastGeneralErrorMessage = ex.Message;
            }
            ClearGridSafe(session);
        }

        try
        {
            if (session.Timer == null) break;
            if (!await session.Timer.WaitForNextTickAsync(token)) break;
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }

    session.Timer?.Dispose();
    session.Timer = null;
}

        /// <summary>
        /// Bağlantı koptuğunda maksimum ReconnectMaxAttempts kez yeniden bağlanmayı dener.
        /// </summary>
        private async Task<bool> TryReconnectAsync(TabSession session, CancellationToken token)
        {
            if (session.Client == null) return false;

            for (int attempt = 1; attempt <= ReconnectMaxAttempts; attempt++)
            {
                if (token.IsCancellationRequested) return false;

                LogMessage(session, $"Yeniden bağlanılıyor... Deneme {attempt}/{ReconnectMaxAttempts}", Color.Yellow);

                try
                {
                    await Task.Delay(ReconnectDelayMs, token);
                    await session.Client.ConnectAsync();

                    LogMessage(session, "Yeniden bağlantı başarılı, polling devam ediyor.", Color.Green);
                    return true;
                }
                catch (TaskCanceledException) { return false; }
                catch (Exception ex) { LogMessage(session, $"Deneme {attempt} başarısız: {ex.Message}", Color.OrangeRed); }
            }

            LogMessage(session, $"{ReconnectMaxAttempts} deneme sonunda bağlantı kurulamadı. Lütfen manuel olarak yeniden bağlanın.", Color.Red);
            return false;
        }

        private void SetConnectionButtonsAfterDrop(TabSession session)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => SetConnectionButtonsAfterDrop(session)));
                return;
            }

            session.BtnConnect.Enabled = true;
            session.BtnStop.Enabled = false;
        }

        private void ClearGridSafe(TabSession session)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => ClearGridSafe(session)));
                return;
            }

            session.Dgv.Rows.Clear();
            session.OldValues = null;
            session.OldBitValues = null;

            // Durum senkronizasyonu: Fiziksel grid temizlendiği için hafızadaki çizim state'ini
            // de mutlak suretle sıfırlıyoruz. Aksi halde bir sonraki başarılı okumada
            // IsGridLayoutValid yanlışlıkla "layout aynı" sanıp in-place güncelleme moduna girer
            // ve artık var olmayan Rows[0]'a erişmeye çalışıp ArgumentOutOfRangeException fırlatır.
            session.RenderedStartAddress = 0;
            session.RenderedRowCount = 0;
            session.RenderedDataType = string.Empty;
        }

        private PollingParameters GetPollingParametersSafe(TabSession session)
        {
            if (this.InvokeRequired)
                return (PollingParameters)this.Invoke(new Func<PollingParameters>(() => ReadPollingParametersFromUi(session)));
            return ReadPollingParametersFromUi(session);
        }

        private PollingParameters ReadPollingParametersFromUi(TabSession session)
        {
            return new PollingParameters
            {
                SlaveId      = (byte)session.NumSlaveId.Value,
                StartAddress = (ushort)session.NumStartAddress.Value,
                DataType     = session.CmbDataType.SelectedItem?.ToString() ?? "Unsigned (16-bit)",
                FunctionCode = GetSelectedFunctionCode(session),
                UserQuantity = (int)session.NumQuantity.Value,
                IntervalMs   = (int)session.NumPollingInterval.Value
            };
        }

        // ---------------------------------------------------------
        // OKUMA + EKRANA YANSITMA
        // ---------------------------------------------------------

        private async Task ReadAndDisplayAsync(TabSession session, PollingParameters parameters)
        {
            bool isBitBased = parameters.FunctionCode == ModbusFunctionCode.ReadCoils ||
                               parameters.FunctionCode == ModbusFunctionCode.ReadDiscreteInputs;

            if (isBitBased) await ReadAndDisplayBitsAsync(session, parameters);
            else             await ReadAndDisplayRegistersAsync(session, parameters);
        }

        private async Task ReadAndDisplayBitsAsync(TabSession session, PollingParameters parameters)
        {
            if (parameters.UserQuantity > 2000)
                throw new ArgumentOutOfRangeException(nameof(parameters.UserQuantity), "Bit miktarı 2000'i aşıyor.");

            if (session.Client == null) return;

            bool[] bits = parameters.FunctionCode == ModbusFunctionCode.ReadCoils
                ? await session.Client.ReadCoilsAsync(parameters.SlaveId, parameters.StartAddress, (ushort)parameters.UserQuantity)
                : await session.Client.ReadDiscreteInputsAsync(parameters.SlaveId, parameters.StartAddress, (ushort)parameters.UserQuantity);

            session.LastProtocolErrorCode = null;

            bool hasChanged = HasBitValuesChanged(session.OldBitValues, bits);
            if (hasChanged)
            {
                session.OldBitValues = (bool[])bits.Clone();
                this.Invoke(new Action(() =>
                {
                    LogMessage(session, "Veri Değişti.", Color.Green);
                    DisplayBitsInGrid(session, bits, parameters.StartAddress);
                }));
            }
        }

        private async Task ReadAndDisplayRegistersAsync(TabSession session, PollingParameters parameters)
        {
            int registerSizePerItem = GetRegisterSizeForDataType(parameters.DataType);
            int totalQuantity = parameters.UserQuantity * registerSizePerItem;

            if (session.Client == null) return;

            ushort[] registers = parameters.FunctionCode == ModbusFunctionCode.ReadHoldingRegisters
                ? await session.Client.ReadHoldingRegistersAsync(parameters.SlaveId, parameters.StartAddress, (ushort)totalQuantity)
                : await session.Client.ReadInputRegistersAsync(parameters.SlaveId, parameters.StartAddress, (ushort)totalQuantity);

            session.LastProtocolErrorCode = null;

            bool hasChanged = HasValuesChanged(session.OldValues, registers);
            if (hasChanged)
            {
                session.OldValues = (ushort[])registers.Clone();
                this.Invoke(new Action(() =>
                {
                    LogMessage(session, "Veri Değişti.", Color.Green);
                    DisplayRegistersInGrid(session, registers, parameters.DataType, parameters.StartAddress, registerSizePerItem);
                }));
            }
        }

        private bool HasValuesChanged(ushort[]? oldValues, ushort[] newValues)
        {
            if (oldValues == null || oldValues.Length != newValues.Length) return true;
            for (int i = 0; i < oldValues.Length; i++) if (oldValues[i] != newValues[i]) return true;
            return false;
        }

        private bool HasBitValuesChanged(bool[]? oldValues, bool[] newValues)
        {
            if (oldValues == null || oldValues.Length != newValues.Length) return true;
            for (int i = 0; i < oldValues.Length; i++) if (oldValues[i] != newValues[i]) return true;
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

        private void DisplayBitsInGrid(TabSession session, bool[] bits, ushort startAddress)
        {
            const string BitsTypeMarker = "Bits";

            bool layoutValid = IsGridLayoutValid(session, bits.Length, startAddress, BitsTypeMarker);

            if (!layoutValid)
            {
                session.Dgv.Rows.Clear();
                for (int i = 0; i < bits.Length; i++)
                {
                    ushort itemAddress = (ushort)(startAddress + i);
                    session.Dgv.Rows.Add(itemAddress, bits[i] ? "1" : "0");
                }

                session.RenderedStartAddress = startAddress;
                session.RenderedRowCount = bits.Length;
                session.RenderedDataType = BitsTypeMarker;
            }
            else
            {
                for (int i = 0; i < bits.Length; i++)
                {
                    session.Dgv.Rows[i].Cells[1].Value = bits[i] ? "1" : "0";
                }
            }
        }
        
        /// <summary>
        /// Grid'in mevcut düzeninin, yeni okunan verinin düzeniyle uyuşup uyuşmadığını kontrol eder.
        /// String parçalama YAPMAZ; TabSession üzerinde saklanan son başarılı çizim durumunu
        /// (satır sayısı, başlangıç adresi, veri tipi) primitif karşılaştırmayla değerlendirir.
        /// </summary>
        private bool IsGridLayoutValid(TabSession session, int expectedRowCount, ushort startAddress, string dataType)
        {
            return session.RenderedRowCount == expectedRowCount &&
                   session.RenderedStartAddress == startAddress &&
                   session.RenderedDataType == dataType;
        }

        /// <summary>
        /// Tek bir register grubunun (1/2/4 register) seçili veri tipine göre görüntülenecek değerini hesaplar.
        /// Span tabanlı ModbusDataConverter metotları çağrılırken heap allocation oluşmaz.
        /// </summary>
        private object GetRegisterDisplayValue(ushort[] registers, string dataType, int i, int registerSizePerItem)
        {
            switch (dataType)
            {
                case "Unsigned (16-bit)": return registers[i];
                case "Signed (16-bit)":   return ModbusDataConverter.ToSigned(registers[i]);
                case "Hex":                return ModbusDataConverter.ToHex(registers[i]);
                case "Binary":             return ModbusDataConverter.ToBinary(registers[i]);

                case "Float (32-bit)":
                    return ModbusDataConverter.ToFloat(registers.AsSpan(i, registerSizePerItem), inverse: true);
                case "Float Inverse (32-bit)":
                    return ModbusDataConverter.ToFloat(registers.AsSpan(i, registerSizePerItem), inverse: false);

                case "Long (32-bit)":
                    return ModbusDataConverter.ToLong(registers.AsSpan(i, registerSizePerItem), inverse: true);
                case "Long Inverse (32-bit)":
                    return ModbusDataConverter.ToLong(registers.AsSpan(i, registerSizePerItem), inverse: false);

                case "Double (64-bit)":
                    return ModbusDataConverter.ToDouble(registers.AsSpan(i, registerSizePerItem), inverse: true);
                case "Double Inverse (64-bit)":
                    return ModbusDataConverter.ToDouble(registers.AsSpan(i, registerSizePerItem), inverse: false);

                default: return registers[i];
            }
        }

        private string GetRegisterAddressLabel(ushort itemAddress, int registerSizePerItem)
        {
            return registerSizePerItem switch
            {
                2 => $"{itemAddress}-{itemAddress + 1}",
                4 => $"{itemAddress}-{itemAddress + 3}",
                _ => itemAddress.ToString()
            };
        }

        private void DisplayRegistersInGrid(TabSession session, ushort[] registers, string dataType, ushort startAddress, int registerSizePerItem)
        {
            int expectedRowCount = registers.Length / registerSizePerItem;
            bool layoutValid = IsGridLayoutValid(session, expectedRowCount, startAddress, dataType);

            if (!layoutValid)
            {
                session.Dgv.Rows.Clear();

                for (int i = 0; i < registers.Length; i += registerSizePerItem)
                {
                    ushort itemAddress = (ushort)(startAddress + i);
                    string addressLabel = GetRegisterAddressLabel(itemAddress, registerSizePerItem);
                    object value = GetRegisterDisplayValue(registers, dataType, i, registerSizePerItem);
                    session.Dgv.Rows.Add(addressLabel, value);
                }

                session.RenderedStartAddress = startAddress;
                session.RenderedRowCount = expectedRowCount;
                session.RenderedDataType = dataType;
            }
            else
            {
                int rowIndex = 0;
                for (int i = 0; i < registers.Length; i += registerSizePerItem)
                {
                    object value = GetRegisterDisplayValue(registers, dataType, i, registerSizePerItem);
                    session.Dgv.Rows[rowIndex].Cells[1].Value = value;
                    rowIndex++;
                }
            }
        }

        

        // ---------------------------------------------------------
        // LOG YAZDIRMA (Sekmeye Özel Terminal)
        // ---------------------------------------------------------

        private void LogMessage(TabSession session, string message, Color color)
        {
            if (session.RtbLogs.InvokeRequired)
                session.RtbLogs.Invoke(new Action(() => AppendLog(session, message, color)));
            else
                AppendLog(session, message, color);
        }

        private void AppendLog(TabSession session, string message, Color color)
        {
            session.RtbLogs.SelectionStart = session.RtbLogs.TextLength;
            session.RtbLogs.SelectionLength = 0;
            session.RtbLogs.SelectionColor = color;
            session.RtbLogs.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            session.RtbLogs.SelectionColor = session.RtbLogs.ForeColor;
            session.RtbLogs.ScrollToCaret();
        }

        // ---------------------------------------------------------
        // WriteForm ENTEGRASYONU (Çift Tıklama)
        // ---------------------------------------------------------

        private void DgvRegisters_CellDoubleClick(TabSession session, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (session.Client == null || !session.Client.IsConnected) return;

            var parameters = ReadPollingParametersFromUi(session);

            bool isBitBased  = parameters.FunctionCode == ModbusFunctionCode.ReadCoils ||
                                parameters.FunctionCode == ModbusFunctionCode.ReadDiscreteInputs;
            int registerSize = isBitBased ? 1 : GetRegisterSizeForDataType(parameters.DataType);

            string addrCell  = session.Dgv.Rows[e.RowIndex].Cells[0].Value?.ToString() ?? "0";
            string firstPart = addrCell.Split('-')[0].Trim();
            if (!ushort.TryParse(firstPart, out ushort itemAddress)) return;

            ushort[]? slice    = null;
            bool[]?   bitSlice = null;
            int       rowIdx   = e.RowIndex;

            if (isBitBased)
            {
                if (session.OldBitValues != null && rowIdx < session.OldBitValues.Length)
                    bitSlice = new[] { session.OldBitValues[rowIdx] };
            }
            else
            {
                if (session.OldValues != null)
                {
                    int startIdx = rowIdx * registerSize;
                    if (startIdx + registerSize <= session.OldValues.Length)
                    {
                        slice = new ushort[registerSize];
                        Array.Copy(session.OldValues, startIdx, slice, 0, registerSize);
                    }
                }
            }

            using var writeForm = new WriteForm(
                session.Client, parameters.SlaveId, itemAddress, parameters.DataType,
                parameters.FunctionCode, slice, bitSlice);

            writeForm.OnSuccessLog = msg => LogMessage(session, msg, Color.Green);

            if (writeForm.ShowDialog(this) == DialogResult.OK)
            {
                session.OldValues = null;
                session.OldBitValues = null;
            }
        }

        // ---------------------------------------------------------
        // SEKME KAPATMA
        // ---------------------------------------------------------

        private async void CloseSession(TabSession session)
        {
            // Arka plan görevi TAMAMEN durana kadar bekliyoruz; page.Dispose() ancak ondan sonra çağrılıyor.
            await StopPolling(session);

            session.Client?.Disconnect();
            session.Client = null;

            tabControl.TabPages.Remove(session.Page);
            _sessions.Remove(session);
            session.Page.Dispose();
        }

        // ---------------------------------------------------------
        // FORM KAPANIRKEN TÜM SEKMELERİN TEMİZLİĞİ
        // ---------------------------------------------------------

        private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // 1. Aşama: Temizlik zaten tamamlandıysa formun gerçekten kapanmasına izin ver.
            if (_readyToClose) return;

            // 2. Aşama: Kapanmayı GEÇİCİ olarak iptal et; Windows'un formu erken yok etmesini engelle.
            e.Cancel = true;
            this.Enabled = false;

            try
            {
                foreach (var session in _sessions)
                {
                    await StopPolling(session);
                    session.Client?.Disconnect();
                }
            }
            catch (Exception)
            {
                // Temizlik sırasında oluşabilecek hataları yutuyoruz; form asılı kalmasın.
            }
            finally
            {
                // 3. Aşama: Temizlik kesin olarak bitti. Bayrağı kaldır ve formu tekrar kapat.
                _readyToClose = true;
                this.Close();
            }
        }

        // ---------------------------------------------------------
        // PARAMETRE TAŞIYICI STRUCT
        // ---------------------------------------------------------

        private struct PollingParameters
        {
            public byte SlaveId;
            public ushort StartAddress;
            public string DataType;
            public ModbusFunctionCode FunctionCode;
            public int UserQuantity;
            public int IntervalMs;
        }

        // ---------------------------------------------------------
        // TABSESSION: Her sekmenin bağımsız durumu ve kontrolleri
        // ---------------------------------------------------------

        private sealed class TabSession
        {
            public TabPage Page { get; }

            public ModbusClient? Client { get; set; }
            public CancellationTokenSource? PollingCts { get; set; }
            public Task? PollingTask { get; set; }
            public PeriodicTimer? Timer { get; set; }
            public int CurrentIntervalMs { get; set; }

            public ushort[]? OldValues { get; set; }
            public bool[]? OldBitValues { get; set; }

            public byte? LastProtocolErrorCode { get; set; }
            public string? LastGeneralErrorMessage { get; set; }

            public TextBox TxtIp { get; set; } = null!;
            public TextBox TxtPort { get; set; } = null!;
            public NumericUpDown NumSlaveId { get; set; } = null!;
            public NumericUpDown NumStartAddress { get; set; } = null!;
            public NumericUpDown NumQuantity { get; set; } = null!;
            public NumericUpDown NumPollingInterval { get; set; } = null!;
            public ComboBox CmbFunctionCode { get; set; } = null!;
            public ComboBox CmbDataType { get; set; } = null!;
            public Button BtnConnect { get; set; } = null!;
            public Button BtnStop { get; set; } = null!;
            public Button? BtnCloseTab { get; set; }
            public DataGridView Dgv { get; set; } = null!;
            public RichTextBox RtbLogs { get; set; } = null!;
            
            public ushort RenderedStartAddress { get; set; }
            public int RenderedRowCount { get; set; }
            public string RenderedDataType { get; set; } = string.Empty;

            public TabSession(TabPage page) { Page = page; }
        }
    }
}