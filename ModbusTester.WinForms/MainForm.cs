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
        private readonly List<TabSession> _sessions = new();
        private int _dynamicTabCounter = 1;
        private bool _readyToClose = false;

        private readonly List<(string Display, ModbusFunctionCode Code)> _functionCodeItems = new()
        {
            ("Read Coils (FC01)",             ModbusFunctionCode.ReadCoils),
            ("Read Discrete Inputs (FC02)",   ModbusFunctionCode.ReadDiscreteInputs),
            ("Read Holding Registers (FC03)", ModbusFunctionCode.ReadHoldingRegisters),
            ("Read Input Registers (FC04)",   ModbusFunctionCode.ReadInputRegisters),
        };

        private const int ReconnectMaxAttempts = 5;
        private const int ReconnectDelayMs     = 2000;

        private enum ConnectionPhase { Idle, Searching, Connected, DataError, Reconnecting }
        
        public MainForm()
        {
            InitializeComponent();
            CreateAndAddSession("Ana Cihaz", closable: false);
        }

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

        private TabSession BuildSession(string tabName, bool closable)
        {
            var page = new TabPage(tabName);
            var session = new TabSession(page);

            var lblIp = new Label { Text = "IP Adresi:", Location = new Point(12, 15), AutoSize = true };
            session.TxtIp = new TextBox { Location = new Point(130, 12), Size = new Size(150, 20), Text = "127.0.0.1" };

            var lblPort = new Label { Text = "Port:", Location = new Point(12, 45), AutoSize = true };
            session.TxtPort = new TextBox { Location = new Point(130, 42), Size = new Size(80, 20), Text = "502" };

            var lblSlaveId = new Label { Text = "Slave ID:", Location = new Point(12, 75), AutoSize = true };
            session.NumSlaveId = new NumericUpDown { Location = new Point(130, 72), Size = new Size(80, 20), Minimum = 1, Maximum = 255, Value = 1 };

            var lblStartAddress = new Label { Text = "Başlangıç Adresi:", Location = new Point(12, 105), AutoSize = true };
            session.NumStartAddress = new NumericUpDown { Location = new Point(130, 102), Size = new Size(80, 20), Minimum = 0, Maximum = 65535, Value = 0 };

            var lblFunctionCode = new Label { Text = "Fonksiyon Kodu:", Location = new Point(12, 135), AutoSize = true };
            session.CmbFunctionCode = new ComboBox { Location = new Point(130, 132), Size = new Size(220, 21), DropDownStyle = ComboBoxStyle.DropDownList };

            var lblDataType = new Label { Text = "Veri Tipi:", Location = new Point(12, 165), AutoSize = true };
            session.CmbDataType = new ComboBox { Location = new Point(130, 162), Size = new Size(220, 21), DropDownStyle = ComboBoxStyle.DropDownList };

            var lblQuantity = new Label { Text = "Adet:", Location = new Point(12, 195), AutoSize = true };
            session.NumQuantity = new NumericUpDown { Location = new Point(130, 192), Size = new Size(80, 20), Minimum = 1, Maximum = 500, Value = 1 };

            var lblPollingInterval = new Label { Text = "Sorgulama (ms):", Location = new Point(12, 225), AutoSize = true };
            session.NumPollingInterval = new NumericUpDown { Location = new Point(130, 222), Size = new Size(80, 20), Minimum = 50, Maximum = 60000, Value = 200 };

            session.BtnConnect = new Button { Text = "Bağlan", Location = new Point(130, 255), Size = new Size(90, 28), UseVisualStyleBackColor = true };
            session.BtnConnect.Click += (s, e) => BtnConnect_Click(session);

            session.BtnStop = new Button { Text = "Durdur", Location = new Point(228, 255), Size = new Size(90, 28), UseVisualStyleBackColor = true, Enabled = false };
            session.BtnStop.Click += (s, e) => BtnStop_Click(session);

            session.LblPhase = new Label
            {
                Text = "Bağlı Değil", Location = new Point(12, 288), Size = new Size(340, 20),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.Gray
            };

            var colAddress = new DataGridViewTextBoxColumn { HeaderText = "Adres", Name = "colAddress", ReadOnly = true };
            var colValue   = new DataGridViewTextBoxColumn { HeaderText = "Değer", Name = "colValue", ReadOnly = true };

            session.Dgv = new DataGridView
            {
                Location = new Point(12, 315),
                Size = new Size(440, 200),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Enabled = false
            };
            session.Dgv.Columns.AddRange(colAddress, colValue);
            session.Dgv.CellDoubleClick += (s, e) => DgvRegisters_CellDoubleClick(session, e);

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
            page.Controls.Add(session.LblPhase);
            page.Controls.Add(session.Dgv);
            page.Controls.Add(session.RtbLogs);

            if (closable)
            {
                session.BtnCloseTab = new Button
                {
                    Text = "Sekmeyi Kapat", Location = new Point(326, 255), Size = new Size(120, 28), UseVisualStyleBackColor = true
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
            session.CmbFunctionCode.SelectedIndex = 2;

            session.CmbDataType.Items.Clear();
            session.CmbDataType.Items.AddRange(new object[]
            {
                "Unsigned (16-bit)", "Signed (16-bit)", "Hex", "Binary",
                "Float (32-bit)", "Float Inverse (32-bit)",
                "Long (32-bit)", "Long Inverse (32-bit)",
                "Double (64-bit)", "Double Inverse (64-bit)"
            });
            session.CmbDataType.SelectedIndex = 0;

            session.CmbFunctionCode.SelectedIndexChanged += (s, e) => CmbFunctionCode_SelectedIndexChanged(session);
        }

        private ModbusFunctionCode GetSelectedFunctionCode(TabSession session)
        {
            int idx = session.CmbFunctionCode.SelectedIndex;
            return (idx >= 0 && idx < _functionCodeItems.Count) ? _functionCodeItems[idx].Code : ModbusFunctionCode.ReadHoldingRegisters;
        }

        private void CmbFunctionCode_SelectedIndexChanged(TabSession session)
        {
            var fc = GetSelectedFunctionCode(session);
            bool isBitBased = fc == ModbusFunctionCode.ReadCoils || fc == ModbusFunctionCode.ReadDiscreteInputs;
            if (session.CurrentPhase == ConnectionPhase.Idle)
                session.CmbDataType.Enabled = !isBitBased;
        }

        private void SetPhaseSafe(TabSession session, ConnectionPhase phase, string message)
        {
            if (this.InvokeRequired) { this.Invoke(new Action(() => SetPhaseSafe(session, phase, message))); return; }

            session.CurrentPhase = phase;
            session.LblPhase.Text = message;
            session.LblPhase.ForeColor = phase switch
            {
                ConnectionPhase.Searching    => Color.Orange,
                ConnectionPhase.Connected    => Color.Green,
                ConnectionPhase.DataError    => Color.DarkOrange, // Soket sağlam ama son istek reddedildi.
                ConnectionPhase.Reconnecting => Color.Red,
                _                            => Color.Gray
            };

            switch (phase)
            {
                case ConnectionPhase.Idle:
                    session.TxtIp.Enabled = true;
                    session.TxtPort.Enabled = true;
                    session.NumSlaveId.Enabled = true;
                    session.NumStartAddress.Enabled = true;
                    session.NumQuantity.Enabled = true;
                    session.NumPollingInterval.Enabled = true;
                    session.CmbFunctionCode.Enabled = true;
                    session.CmbDataType.Enabled = true;
                    session.BtnConnect.Enabled = true;
                    session.BtnStop.Enabled = false;
                    session.Dgv.Enabled = false;
                    break;

                case ConnectionPhase.Searching:
                    session.TxtIp.Enabled = true;
                    session.TxtPort.Enabled = true;
                    session.NumSlaveId.Enabled = false;
                    session.NumStartAddress.Enabled = false;
                    session.NumQuantity.Enabled = false;
                    session.NumPollingInterval.Enabled = false;
                    session.CmbFunctionCode.Enabled = false;
                    session.CmbDataType.Enabled = false;
                    session.BtnConnect.Enabled = false;
                    session.BtnStop.Enabled = true;
                    session.Dgv.Enabled = false;
                    break;

                case ConnectionPhase.Connected:
                    session.TxtIp.Enabled = false;
                    session.TxtPort.Enabled = false;
                    session.NumSlaveId.Enabled = false;
                    session.NumStartAddress.Enabled = false;
                    session.NumQuantity.Enabled = false;
                    session.CmbFunctionCode.Enabled = false;
                    session.CmbDataType.Enabled = false;
                    session.BtnConnect.Enabled = false;
                    session.NumPollingInterval.Enabled = true;
                    session.BtnStop.Enabled = true;
                    session.Dgv.Enabled = true;
                    break;

                case ConnectionPhase.DataError:
                    // Soket sağlam, kilitleme kuralları Connected ile AYNI (driver hâlâ çalışıyor,
                    // kullanıcı yapısal parametreleri değiştiremez); sadece görsel uyarı farklı.
                    // Dgv bilinçli olarak Enabled=true bırakılıyor ama içi boş (ClearGridSafe zaten
                    // temizledi) — operatör tablonun "aktif ama veri gelmiyor" olduğunu görüyor,
                    // "pasif/donmuş" ile karıştırmıyor.
                    session.TxtIp.Enabled = false;
                    session.TxtPort.Enabled = false;
                    session.NumSlaveId.Enabled = false;
                    session.NumStartAddress.Enabled = false;
                    session.NumQuantity.Enabled = false;
                    session.CmbFunctionCode.Enabled = false;
                    session.CmbDataType.Enabled = false;
                    session.BtnConnect.Enabled = false;
                    session.NumPollingInterval.Enabled = true;
                    session.BtnStop.Enabled = true;
                    session.Dgv.Enabled = true;
                    break;

                case ConnectionPhase.Reconnecting:
                    session.TxtIp.Enabled = false;
                    session.TxtPort.Enabled = false;
                    session.NumSlaveId.Enabled = false;
                    session.NumStartAddress.Enabled = false;
                    session.NumQuantity.Enabled = false;
                    session.CmbFunctionCode.Enabled = false;
                    session.CmbDataType.Enabled = false;
                    session.BtnConnect.Enabled = false;
                    session.NumPollingInterval.Enabled = true;
                    session.BtnStop.Enabled = true;
                    session.Dgv.Enabled = false;
                    break;
            }
        }

        private void BtnConnect_Click(TabSession session)
        {
            if (session.DriverTask != null && !session.DriverTask.IsCompleted)
            {
                LogMessage(session, "Sürücü zaten çalışıyor.", Color.Orange);
                return;
            }

            string ip = session.TxtIp.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip)) { LogMessage(session, "IP adresi boş olamaz.", Color.Red); return; }

            if (!int.TryParse(session.TxtPort.Text.Trim(), out int port) || port <= 0 || port > 65535)
            { LogMessage(session, "Geçersiz port numarası.", Color.Red); return; }

            session.CurrentIp = ip;
            session.CurrentPort = port;
            session.Client = new ModbusClient(ip, port) { ConnectTimeoutMs = 3000, IoTimeoutMs = 3000 };

            session.DriverCts = new CancellationTokenSource();
            var token = session.DriverCts.Token;

            SetPhaseSafe(session, ConnectionPhase.Searching, "Cihaz aranıyor...");
            session.DriverTask = Task.Run(() => RunDriverAsync(session, token), token);
        }

        private async void BtnStop_Click(TabSession session)
        {
            await StopDriverAsync(session);
            LogMessage(session, "Sürücü durduruldu.", Color.Orange);
        }

        private async Task StopDriverAsync(TabSession session)
        {
            session.DriverCts?.Cancel();

            if (session.DriverTask != null && !session.DriverTask.IsCompleted)
            {
                try { await session.DriverTask; }
                catch (OperationCanceledException) { }
            }

            session.DriverCts?.Dispose();
            session.DriverCts = null;
            session.DriverTask = null;

            session.Timer?.Dispose();
            session.Timer = null;

            session.Client?.Disconnect();

            SetPhaseSafe(session, ConnectionPhase.Idle, "Bağlı Değil");
            ClearGridSafe(session);
        }

        private async Task RunDriverAsync(TabSession session, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                bool connected = await EstablishConnectionAsync(session, token);
                if (!connected) break;

                PollingParameters initialParams = GetPollingParametersSafe(session);
                bool isBitBased = initialParams.FunctionCode == ModbusFunctionCode.ReadCoils ||
                                   initialParams.FunctionCode == ModbusFunctionCode.ReadDiscreteInputs;

                session.RegisterReadBuffer = isBitBased
                    ? null
                    : new ushort[initialParams.UserQuantity * GetRegisterSizeForDataType(initialParams.DataType)];

                session.OldValues = null;
                session.OldBitValues = null;
                session.LastProtocolErrorCode = null;
                session.LastGeneralErrorMessage = null;

                PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(50, initialParams.IntervalMs)));
                session.Timer = timer;

                try
                {
                    await InnerPollingLoopAsync(session, timer, token);
                }
                finally
                {
                    timer.Dispose();
                    session.Timer = null;
                }
            }

            SetPhaseSafe(session, ConnectionPhase.Idle, "Bağlı Değil");
        }

        private async Task<bool> EstablishConnectionAsync(TabSession session, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                (string liveIp, int livePort) = GetIpPortSafe(session);

                if (liveIp != session.CurrentIp || livePort != session.CurrentPort)
                {
                    session.Client?.Disconnect();
                    session.CurrentIp = liveIp;
                    session.CurrentPort = livePort;
                    session.Client = new ModbusClient(liveIp, livePort) { ConnectTimeoutMs = 3000, IoTimeoutMs = 3000 };
                }

                SetPhaseSafe(session, ConnectionPhase.Searching, $"Cihaz aranıyor: {session.CurrentIp}:{session.CurrentPort}...");

                try
                {
                    await session.Client!.ConnectAsync();
                    LogMessage(session, "Bağlantı başarılı.", Color.Green);
                    SetPhaseSafe(session, ConnectionPhase.Connected, "Veri Akışı Aktif");
                    return true;
                }
                catch (Exception ex)
                {
                    LogMessage(session, $"Bağlanılamadı: {ex.Message} — 5 sn sonra tekrar denenecek.", Color.Red);
                }

                try { await Task.Delay(5000, token); }
                catch (OperationCanceledException) { return false; }
            }
            return false;
        }

        private async Task InnerPollingLoopAsync(TabSession session, PeriodicTimer timer, CancellationToken token)
        {
            PollingParameters firstParams = GetPollingParametersSafe(session);
            int lastIntervalMs = firstParams.IntervalMs;

            while (!token.IsCancellationRequested)
            {
                PollingParameters parameters = GetPollingParametersSafe(session);

                if (parameters.Ip != session.CurrentIp || parameters.Port != session.CurrentPort)
                {
                    var temp = new ModbusClient(parameters.Ip, parameters.Port) { ConnectTimeoutMs = 3000, IoTimeoutMs = 3000 };
                    try
                    {
                        await temp.ConnectAsync();
                        session.Client?.Disconnect();
                        session.Client = temp;
                        session.CurrentIp = parameters.Ip;
                        session.CurrentPort = parameters.Port;
                        session.OldValues = null;
                    }
                    catch (Exception ex)
                    {
                        temp.Disconnect();
                        LogMessage(session, $"Beklenmeyen adres değişimi denemesi başarısız: {ex.Message}", Color.Red);
                    }
                }

                try
                {
                    await ReadAndDisplayAsync(session, parameters);
                    session.LastProtocolErrorCode = null;
                    session.LastGeneralErrorMessage = null;

                    // Son okuma başarılıydı; eğer faz zaten Connected DEĞİLSE (yani bir önceki tick'te
                    // DataError durumundaydıysak), şimdi otomatik olarak yeşile geri dönüyoruz.
                    // "if" koruması bilinçli: her başarılı tick'te (200ms'de bir) gereksiz Invoke/label
                    // repaint yapmamak için, yalnızca gerçek bir DURUM DEĞİŞİKLİĞİ olduğunda tetikleniyor.
                    if (session.CurrentPhase != ConnectionPhase.Connected)
                    {
                        SetPhaseSafe(session, ConnectionPhase.Connected, "Veri Akışı Aktif");
                    }
                }
                catch (ModbusProtocolException ex)
                {
                    if (session.LastProtocolErrorCode == null || session.LastProtocolErrorCode.Value != ex.ExceptionCode)
                    {
                        LogMessage(session, $"PROTOKOL HATASI (Kod: {ex.ExceptionCode}): {ex.Message}", Color.Red);
                        session.LastProtocolErrorCode = ex.ExceptionCode;
                    }
                    ClearGridSafe(session);

                    // KRİTİK DÜZELTME: Soket sağlam (Disconnect çağrılmadı, TryReconnectAsync tetiklenmiyor)
                    // ama operatöre "yalancı yeşil" göstermek yerine dürüstçe turuncu bir uyarı veriyoruz.
                    // Aynı "if" koruması burada da geçerli: hata her tekrarında (log throttling zaten var)
                    // tekrar tekrar aynı fazı set edip gereksiz repaint yapmıyoruz.
                    if (session.CurrentPhase != ConnectionPhase.DataError)
                    {
                        SetPhaseSafe(session, ConnectionPhase.DataError, $"Hatalı İstek (Kod: {ex.ExceptionCode}) — Adres/Adet ayarlarını kontrol edin");
                    }
                }
                catch (ModbusTimeoutException ex)
                {
                    if (token.IsCancellationRequested) break;

                    LogMessage(session, $"ZAMAN AŞIMI: {ex.Message}", Color.Red);
                    ClearGridSafe(session);

                    bool reconnected = await TryReconnectAsync(session, token);
                    if (!reconnected) return;

                    session.OldValues = null;
                    continue;
                }
                catch (ModbusConnectionException ex)
                {
                    if (token.IsCancellationRequested) break;

                    LogMessage(session, $"BAĞLANTI HATASI: {ex.Message}", Color.Red);
                    ClearGridSafe(session);

                    bool reconnected = await TryReconnectAsync(session, token);
                    if (!reconnected) return;

                    session.OldValues = null;
                    continue;
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;

                    if (session.LastGeneralErrorMessage == null || session.LastGeneralErrorMessage != ex.Message)
                    {
                        LogMessage(session, $"HATA: {ex.Message}", Color.Red);
                        session.LastGeneralErrorMessage = ex.Message;
                    }
                    ClearGridSafe(session);
                }

                if (parameters.IntervalMs != lastIntervalMs && parameters.IntervalMs > 0)
                {
                    timer.Dispose();
                    timer = new PeriodicTimer(TimeSpan.FromMilliseconds(parameters.IntervalMs));
                    session.Timer = timer;
                    lastIntervalMs = parameters.IntervalMs;
                }

                try
                {
                    if (!await timer.WaitForNextTickAsync(token)) break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<bool> TryReconnectAsync(TabSession session, CancellationToken token)
        {
            if (session.Client == null) return false;

            for (int attempt = 1; attempt <= ReconnectMaxAttempts; attempt++)
            {
                if (token.IsCancellationRequested) return false;

                SetPhaseSafe(session, ConnectionPhase.Reconnecting,
                    $"Bağlantı koptu, yeniden deneniyor ({attempt}/{ReconnectMaxAttempts})...");

                try
                {
                    await Task.Delay(ReconnectDelayMs, token);
                    await session.Client.ConnectAsync();

                    SetPhaseSafe(session, ConnectionPhase.Connected, "Veri Akışı Aktif");
                    LogMessage(session, "Yeniden bağlantı başarılı.", Color.Green);
                    return true;
                }
                catch (OperationCanceledException) { return false; }
                catch (Exception ex) { LogMessage(session, $"Deneme {attempt} başarısız: {ex.Message}", Color.OrangeRed); }
            }

            LogMessage(session, $"{ReconnectMaxAttempts} deneme tükendi, bağlantı fazına geri dönülüyor.", Color.Red);
            return false;
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
                Ip           = session.TxtIp.Text.Trim(),
                Port         = int.TryParse(session.TxtPort.Text.Trim(), out int p) ? p : session.CurrentPort,
                SlaveId      = (byte)session.NumSlaveId.Value,
                StartAddress = (ushort)session.NumStartAddress.Value,
                DataType     = session.CmbDataType.SelectedItem?.ToString() ?? "Unsigned (16-bit)",
                FunctionCode = GetSelectedFunctionCode(session),
                UserQuantity = (int)session.NumQuantity.Value,
                IntervalMs   = (int)session.NumPollingInterval.Value
            };
        }

        private (string Ip, int Port) GetIpPortSafe(TabSession session)
        {
            if (this.InvokeRequired)
                return ((string, int))this.Invoke(new Func<(string, int)>(() =>
                    (session.TxtIp.Text.Trim(), int.TryParse(session.TxtPort.Text.Trim(), out int p) ? p : session.CurrentPort)));
            return (session.TxtIp.Text.Trim(), int.TryParse(session.TxtPort.Text.Trim(), out int pp) ? pp : session.CurrentPort);
        }

        private async Task ReadAndDisplayAsync(TabSession session, PollingParameters parameters)
        {
            bool isBitBased = parameters.FunctionCode == ModbusFunctionCode.ReadCoils ||
                               parameters.FunctionCode == ModbusFunctionCode.ReadDiscreteInputs;

            if (isBitBased) await ReadAndDisplayBitsAsync(session, parameters);
            else             await ReadAndDisplayRegistersAsync(session, parameters);
        }

        private async Task ReadAndDisplayBitsAsync(TabSession session, PollingParameters parameters)
        {
            if (session.Client == null) return;

            bool[] bits = parameters.FunctionCode == ModbusFunctionCode.ReadCoils
                ? await session.Client.ReadCoilsAsync(parameters.SlaveId, parameters.StartAddress, (ushort)parameters.UserQuantity)
                : await session.Client.ReadDiscreteInputsAsync(parameters.SlaveId, parameters.StartAddress, (ushort)parameters.UserQuantity);

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
            if (session.Client == null || session.RegisterReadBuffer == null) return;

            ushort[] destination = session.RegisterReadBuffer;
            int registerSizePerItem = GetRegisterSizeForDataType(parameters.DataType);

            if (parameters.FunctionCode == ModbusFunctionCode.ReadHoldingRegisters)
                await session.Client.ReadHoldingRegistersAsync(parameters.SlaveId, parameters.StartAddress, destination);
            else
                await session.Client.ReadInputRegistersAsync(parameters.SlaveId, parameters.StartAddress, destination);

            bool hasChanged = HasValuesChanged(session.OldValues, destination);
            if (hasChanged)
            {
                session.OldValues = (ushort[])destination.Clone();
                this.Invoke(new Action(() =>
                {
                    LogMessage(session, "Veri Değişti.", Color.Green);
                    DisplayRegistersInGrid(session, destination, parameters.DataType, parameters.StartAddress, registerSizePerItem);
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
            return dataType switch
            {
                "Float (32-bit)" or "Float Inverse (32-bit)" or
                "Long (32-bit)"  or "Long Inverse (32-bit)"   => 2,
                "Double (64-bit)" or "Double Inverse (64-bit)" => 4,
                _ => 1
            };
        }

        private bool IsGridLayoutValid(TabSession session, int expectedRowCount, ushort startAddress, string dataType)
        {
            return session.RenderedRowCount == expectedRowCount &&
                   session.RenderedStartAddress == startAddress &&
                   session.RenderedDataType == dataType;
        }

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
                    session.Dgv.Rows[i].Cells[1].Value = bits[i] ? "1" : "0";
            }
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

        private void ClearGridSafe(TabSession session)
        {
            if (this.InvokeRequired) { this.Invoke(new Action(() => ClearGridSafe(session))); return; }

            session.Dgv.Rows.Clear();
            session.OldValues = null;
            session.OldBitValues = null;
            session.RenderedStartAddress = 0;
            session.RenderedRowCount = 0;
            session.RenderedDataType = string.Empty;
        }

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

        private async void CloseSession(TabSession session)
        {
            await StopDriverAsync(session);

            tabControl.TabPages.Remove(session.Page);
            _sessions.Remove(session);
            session.Page.Dispose();
        }

        private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (_readyToClose) return;

            e.Cancel = true;
            this.Enabled = false;

            try
            {
                foreach (var session in _sessions)
                {
                    await StopDriverAsync(session);
                }
            }
            catch (Exception) { }
            finally
            {
                _readyToClose = true;
                this.Close();
            }
        }

        private struct PollingParameters
        {
            public string Ip;
            public int Port;
            public byte SlaveId;
            public ushort StartAddress;
            public string DataType;
            public ModbusFunctionCode FunctionCode;
            public int UserQuantity;
            public int IntervalMs;
        }

        private sealed class TabSession
        {
            public TabPage Page { get; }

            public ModbusClient? Client { get; set; }
            public CancellationTokenSource? DriverCts { get; set; }
            public Task? DriverTask { get; set; }
            public PeriodicTimer? Timer { get; set; }

            public string CurrentIp { get; set; } = "127.0.0.1";
            public int CurrentPort { get; set; } = 502;
            public ConnectionPhase CurrentPhase { get; set; } = ConnectionPhase.Idle;

            public ushort[]? OldValues { get; set; }
            public bool[]? OldBitValues { get; set; }
            public ushort[]? RegisterReadBuffer { get; set; }

            public byte? LastProtocolErrorCode { get; set; }
            public string? LastGeneralErrorMessage { get; set; }

            public ushort RenderedStartAddress { get; set; }
            public int RenderedRowCount { get; set; }
            public string RenderedDataType { get; set; } = string.Empty;

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
            public Label LblPhase { get; set; } = null!;

            public TabSession(TabPage page) { Page = page; }
        }
    }
}