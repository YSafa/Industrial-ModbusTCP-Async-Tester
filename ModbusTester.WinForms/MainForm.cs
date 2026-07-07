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
        private const int TimeoutBackoffMs = 3000;
        private const int ProbeTimeoutMs = 300;
        private const int RequiredConsecutiveSuccesses = 3;

        private enum ConnectionPhase { Idle, Searching, Connected, DataError, Reconnecting }

        public MainForm()
        {
            InitializeComponent();
            CreateAndAddSession("Main Device", closable: false);
        }

        // ---------------------------------------------------------
        // ADD TAB
        // ---------------------------------------------------------

        private void BtnAddTab_Click(object? sender, EventArgs e)
        {
            string tabName = Interaction.InputBox(
                "Enter a name for the new tab:", "Tab Name", $"Device {_dynamicTabCounter + 1}");
            if (string.IsNullOrWhiteSpace(tabName)) return;

            _dynamicTabCounter++;
            CreateAndAddSession(tabName.Trim(), closable: true);
        }

        private void CreateAndAddSession(string tabName, bool closable)
        {
            TabSession session = BuildSession(tabName, closable);

            // If any existing tab already has an active connection, pre-fill the new tab's
            // IP/Port with the same values — most multi-slave setups target the same gateway/PLC.
            var activeSession = _sessions.Find(s => s.Connection != null && s.Connection.Client.IsConnected);
            if (activeSession != null)
            {
                session.TxtIp.Text = activeSession.CurrentIp;
                session.TxtPort.Text = activeSession.CurrentPort.ToString();
            }

            _sessions.Add(session);
            tabControl.TabPages.Add(session.Page);
            tabControl.SelectedTab = session.Page;
        }

        // ---------------------------------------------------------
        // PER-TAB UI CONSTRUCTION
        // ---------------------------------------------------------

        private TabSession BuildSession(string tabName, bool closable)
        {
            var page = new TabPage(tabName);
            var session = new TabSession(page);

            var lblIp = new Label { Text = "IP Address:", Location = new Point(12, 15), AutoSize = true };
            session.TxtIp = new TextBox { Location = new Point(130, 12), Size = new Size(150, 20), Text = "127.0.0.1" };

            var lblPort = new Label { Text = "Port:", Location = new Point(12, 45), AutoSize = true };
            session.TxtPort = new TextBox { Location = new Point(130, 42), Size = new Size(80, 20), Text = "502" };

            var lblSlaveId = new Label { Text = "Slave ID:", Location = new Point(12, 75), AutoSize = true };
            session.NumSlaveId = new NumericUpDown { Location = new Point(130, 72), Size = new Size(80, 20), Minimum = 1, Maximum = 255, Value = 1 };

            var lblStartAddress = new Label { Text = "Start Address:", Location = new Point(12, 105), AutoSize = true };
            session.NumStartAddress = new NumericUpDown { Location = new Point(130, 102), Size = new Size(80, 20), Minimum = 0, Maximum = 65535, Value = 0 };

            var lblFunctionCode = new Label { Text = "Function Code:", Location = new Point(12, 135), AutoSize = true };
            session.CmbFunctionCode = new ComboBox { Location = new Point(130, 132), Size = new Size(220, 21), DropDownStyle = ComboBoxStyle.DropDownList };

            var lblDataType = new Label { Text = "Data Type:", Location = new Point(12, 165), AutoSize = true };
            session.CmbDataType = new ComboBox { Location = new Point(130, 162), Size = new Size(220, 21), DropDownStyle = ComboBoxStyle.DropDownList };

            var lblQuantity = new Label { Text = "Quantity:", Location = new Point(12, 195), AutoSize = true };
            session.NumQuantity = new NumericUpDown { Location = new Point(130, 192), Size = new Size(80, 20), Minimum = 1, Maximum = 500, Value = 1 };

            var lblPollingInterval = new Label { Text = "Polling (ms):", Location = new Point(12, 225), AutoSize = true };
            session.NumPollingInterval = new NumericUpDown { Location = new Point(130, 222), Size = new Size(80, 20), Minimum = 50, Maximum = 60000, Value = 200 };

            session.BtnConnect = new Button { Text = "Connect", Location = new Point(130, 255), Size = new Size(90, 28), UseVisualStyleBackColor = true };
            session.BtnConnect.Click += (s, e) => BtnConnect_Click(session);

            session.BtnStop = new Button { Text = "Stop", Location = new Point(228, 255), Size = new Size(90, 28), UseVisualStyleBackColor = true, Enabled = false };
            session.BtnStop.Click += (s, e) => BtnStop_Click(session);

            session.LblPhase = new Label
            {
                Text = "Not Connected", Location = new Point(12, 288), Size = new Size(340, 20),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.Gray
            };

            var colAddress = new DataGridViewTextBoxColumn
            {
                HeaderText = "Address",
                Name = "colAddress",
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable // Header click no longer triggers sorting.
            };
            var colValue = new DataGridViewTextBoxColumn
            {
                HeaderText = "Value",
                Name = "colValue",
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            
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
                    Text = "Close Tab", Location = new Point(326, 255), Size = new Size(120, 28), UseVisualStyleBackColor = true
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
            session.CmbFunctionCode.SelectedIndex = 2; // Default: Read Holding Registers (FC03)

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
            // Only meaningful while the driver is NOT running (Idle); when Connected/Searching
            // this control is already locked so the user cannot reach it.
            var fc = GetSelectedFunctionCode(session);
            bool isBitBased = fc == ModbusFunctionCode.ReadCoils || fc == ModbusFunctionCode.ReadDiscreteInputs;
            if (session.CurrentPhase == ConnectionPhase.Idle)
                session.CmbDataType.Enabled = !isBitBased;
        }

        // ---------------------------------------------------------
        // PHASE-BASED CONTROL LOCKING
        // ---------------------------------------------------------

        /// <summary>
        /// Updates both the status label and the Enabled state of the tab's controls based on
        /// the driver's current phase (Idle/Searching/Connected/DataError/Reconnecting). This
        /// eliminates the "Zombie UI" risk while also preventing structural parameters
        /// (DataType, Quantity, StartAddress, etc.) from being changed mid-poll, which would
        /// otherwise corrupt the read buffer/grid layout.
        /// </summary>
        private void SetPhaseSafe(TabSession session, ConnectionPhase phase, string message)
        {
            if (this.InvokeRequired) { this.Invoke(new Action(() => SetPhaseSafe(session, phase, message))); return; }

            session.CurrentPhase = phase;
            session.LblPhase.Text = message;
            session.LblPhase.ForeColor = phase switch
            {
                ConnectionPhase.Searching    => Color.Orange,
                ConnectionPhase.Connected    => Color.Green,
                ConnectionPhase.DataError    => Color.DarkOrange, // Socket is fine, last request was rejected.
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
                    // Let the user fix a wrong IP/Port; everything else stays locked.
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
                    session.Dgv.Enabled = false; // stale data should never look "fresh".
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
                    session.NumPollingInterval.Enabled = true; // speed can be adjusted live.
                    session.BtnStop.Enabled = true;
                    session.Dgv.Enabled = true;
                    break;

                case ConnectionPhase.DataError:
                    // Socket is alive; locking rules are the SAME as Connected (the driver is
                    // still running). Only the status color/message differs. The grid is left
                    // Enabled (already cleared by ClearGridSafe) so the operator can see it's
                    // "active but not receiving data", not "paused".
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

        // ---------------------------------------------------------
        // CONNECT / STOP
        // ---------------------------------------------------------

        private void BtnConnect_Click(TabSession session)
        {
            if (session.DriverTask != null && !session.DriverTask.IsCompleted)
            {
                LogMessage(session, "Driver is already running.", Color.Orange);
                return;
            }

            string ip = session.TxtIp.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip)) { LogMessage(session, "IP address cannot be empty.", Color.Red); return; }

            if (!int.TryParse(session.TxtPort.Text.Trim(), out int port) || port <= 0 || port > 65535)
            { LogMessage(session, "Invalid port number.", Color.Red); return; }

            // The physical connection is now acquired lazily inside EstablishConnectionAsync via the
            // shared ModbusConnectionManager pool — no ModbusClient is constructed directly here.
            session.DriverCts = new CancellationTokenSource();
            var token = session.DriverCts.Token;

            SetPhaseSafe(session, ConnectionPhase.Searching, "Searching for device...");
            session.DriverTask = Task.Run(() => RunDriverAsync(session, token), token);
        }

        private async void BtnStop_Click(TabSession session)
        {
            await StopDriverAsync(session);
            LogMessage(session, "Driver stopped.", Color.Orange);
        }

        /// <summary>
        /// Guarantees the cancel -> await(join) -> dispose sequence. Shared by BtnStop_Click,
        /// CloseSession, and MainForm_FormClosing.
        /// </summary>
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

            // Release this tab's reference to the shared pooled connection instead of disconnecting
            // it directly — the physical socket only closes once every tab using it has released.
            if (session.Connection != null)
            {
                ModbusConnectionManager.Release(session.CurrentIp, session.CurrentPort);
                session.Connection = null;
            }

            SetPhaseSafe(session, ConnectionPhase.Idle, "Not Connected");
            ClearGridSafe(session); // Explicit Stop still clears the grid, unlike automatic error paths.
        }

        // ---------------------------------------------------------
        // DUAL-LOOP ARCHITECTURE (Outer / Inner Loop)
        // ---------------------------------------------------------

        private async Task RunDriverAsync(TabSession session, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                bool connected = await EstablishConnectionAsync(session, token);
                if (!connected) break;

                PollingParameters initialParams = GetPollingParametersSafe(session);
                bool isBitBased = initialParams.FunctionCode == ModbusFunctionCode.ReadCoils ||
                                  initialParams.FunctionCode == ModbusFunctionCode.ReadDiscreteInputs;

                // REGISTER-CENTRIC: Quantity now means "raw 16-bit registers to read from the wire",
                // matching ModScan/Modbus Poll conventions. No multiplication by registerSizePerItem —
                // if the user enters 10 and selects Long (32-bit), 10 registers are read and 5 rows
                // (Long values) are displayed, not 20 registers.
                session.RegisterReadBuffer = isBitBased
                    ? null
                    : new ushort[initialParams.UserQuantity];

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

            SetPhaseSafe(session, ConnectionPhase.Idle, "Not Connected");
        }

        private async Task<bool> EstablishConnectionAsync(TabSession session, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                (string liveIp, int livePort) = GetIpPortSafe(session);

                // REFCOUNT BALANCE: this tab may still hold a pool reference from the previous
                // inner-loop run (e.g. it returned here after exhausting reconnect attempts, with
                // the SAME target). AcquireAsync below increments RefCount unconditionally, so any
                // reference already held MUST be released first — otherwise every trip through the
                // outer loop leaks one RefCount and the shared socket can never close on Stop.
                if (session.Connection != null)
                {
                    ModbusConnectionManager.Release(session.CurrentIp, session.CurrentPort);
                    session.Connection = null;
                }

                session.CurrentIp = liveIp;
                session.CurrentPort = livePort;

                SetPhaseSafe(session, ConnectionPhase.Searching, $"Searching for device: {session.CurrentIp}:{session.CurrentPort}...");

                try
                {
                    session.Connection = await ModbusConnectionManager.AcquireAsync(liveIp, livePort, 3000, 3000);
                    LogMessage(session, "Connected successfully.", Color.Green);
                    SetPhaseSafe(session, ConnectionPhase.Connected, "Data Stream Active");
                    return true;
                }
                catch (Exception ex)
                {
                    LogMessage(session, $"Could not connect: {ex.Message} — retrying in 5 sec.", Color.Red);
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

                // Target change guard: IP/Port controls are locked while Connected, but there is
                // a brief window (Searching -> Connected transition, phase update marshalled via
                // Invoke) where the user can still edit them. If the live UI target no longer
                // matches the connection this loop is polling, break back to the outer loop —
                // EstablishConnectionAsync releases the old pooled connection and acquires the
                // new target. Never keep polling a device the user has navigated away from.
                if (parameters.Ip != session.CurrentIp || parameters.Port != session.CurrentPort)
                {
                    LogMessage(session, $"Target changed to {parameters.Ip}:{parameters.Port} — re-establishing connection.", Color.Orange);
                    return;
                }

                // BACKOFF GUARD: if this tab is currently in a timeout backoff window, skip the
                // actual read entirely this tick — never touch the shared TransactionLock. This is
                // the key fix for the "noisy neighbor" problem: without it, a consistently-timing-out
                // slave would grab the shared lock every single tick for the full IoTimeoutMs
                // duration, throttling every other tab sharing the same physical socket down to its
                // own retry cadence. By skipping ticks during backoff, the lock is left free for
                // healthy tabs almost all the time.
                bool inBackoff = session.TimeoutBackoffUntilUtc.HasValue && DateTime.UtcNow < session.TimeoutBackoffUntilUtc.Value;

                if (!inBackoff)
                {
                    try
                    {
                        await ReadAndDisplayAsync(session, parameters);
                        session.ConsecutiveSuccessCount++;

                        // Only clear the error/backoff state (and switch the phase back to green) after a
                        // few consecutive clean reads — not on the very first one. A single lucky response
                        // from a flaky device shouldn't reset the "we were failing" memory, otherwise the
                        // next failure gets logged as if it were brand new, producing exactly the noisy
                        // "error every ~5s" pattern seen with an intermittently-responding slave.
                        if (session.ConsecutiveSuccessCount >= RequiredConsecutiveSuccesses)
                        {
                            session.LastProtocolErrorCode = null;
                            session.LastGeneralErrorMessage = null;
                            session.TimeoutBackoffUntilUtc = null;

                            if (session.CurrentPhase != ConnectionPhase.Connected)
                            {
                                SetPhaseSafe(session, ConnectionPhase.Connected, "Data Stream Active");
                            }
                        }
                    }
                    catch (ModbusProtocolException ex)
                    {
                        session.ConsecutiveSuccessCount = 0;

                        if (session.LastProtocolErrorCode == null || session.LastProtocolErrorCode.Value != ex.ExceptionCode)
                        {
                            LogMessage(session, $"PROTOCOL ERROR (Code: {ex.ExceptionCode}): {ex.Message}", Color.Red);
                            session.LastProtocolErrorCode = ex.ExceptionCode;
                        }

                        if (session.CurrentPhase != ConnectionPhase.DataError)
                        {
                            SetPhaseSafe(session, ConnectionPhase.DataError, $"Invalid Request (Code: {ex.ExceptionCode}) — check Address/Quantity settings");
                        }
                    }
                    catch (ModbusTimeoutException ex)
                    {
                        if (token.IsCancellationRequested) break;

                        session.ConsecutiveSuccessCount = 0;

                        string errorMessage = $"TIMEOUT (Slave ID: {parameters.SlaveId}): {ex.Message}";
                        if (session.LastGeneralErrorMessage != errorMessage)
                        {
                            LogMessage(session, errorMessage, Color.Red);
                            session.LastGeneralErrorMessage = errorMessage;
                        }

                        if (session.Connection != null && session.Connection.Client.IsConnected)
                        {
                            if (session.CurrentPhase != ConnectionPhase.DataError)
                            {
                                SetPhaseSafe(session, ConnectionPhase.DataError, $"Device Timeout — Check Slave ID {parameters.SlaveId} status");
                            }

                            session.OldValues = null;
                            session.OldBitValues = null;
                            session.TimeoutBackoffUntilUtc = DateTime.UtcNow.AddMilliseconds(TimeoutBackoffMs);
                        }
                        else
                        {
                            bool reconnected = await TryReconnectAsync(session, token);
                            if (!reconnected) return;
                            session.OldValues = null;
                            session.LastGeneralErrorMessage = null;
                            session.TimeoutBackoffUntilUtc = null;
                        }
                        continue;
                    }
                    catch (ModbusConnectionException ex)
                    {
                        if (token.IsCancellationRequested) break;

                        session.ConsecutiveSuccessCount = 0;
                        LogMessage(session, $"CONNECTION ERROR: {ex.Message}", Color.Red);

                        bool reconnected = await TryReconnectAsync(session, token);
                        if (!reconnected) return;

                        session.OldValues = null;
                        continue;
                    }
                    catch (Exception ex)
                    {
                        if (token.IsCancellationRequested) break;

                        session.ConsecutiveSuccessCount = 0;

                        if (session.LastGeneralErrorMessage == null || session.LastGeneralErrorMessage != ex.Message)
                        {
                            LogMessage(session, $"ERROR: {ex.Message}", Color.Red);
                            session.LastGeneralErrorMessage = ex.Message;
                        }
                    }
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
            var connection = session.Connection;
            if (connection == null) return false;

            SetPhaseSafe(session, ConnectionPhase.Reconnecting, "Connection lost, preparing to reconnect...");

            for (int attempt = 1; attempt <= ReconnectMaxAttempts; attempt++)
            {
                if (token.IsCancellationRequested) return false;

                // CRITICAL FIX: the wait happens OUTSIDE the lock. Other tabs sharing this same
                // connection remain free to read/write while this tab waits between attempts.
                if (attempt > 1)
                {
                    try { await Task.Delay(ReconnectDelayMs, token); }
                    catch (OperationCanceledException) { return false; }
                }

                await connection.TransactionLock.WaitAsync(token);
                try
                {
                    // Double-checked locking: another tab sharing this connection may have already
                    // restored it while we were waiting for the lock.
                    if (connection.Client.IsConnected)
                    {
                        SetPhaseSafe(session, ConnectionPhase.Connected, "Data Stream Active");
                        LogMessage(session, "Connection already restored by another tab sharing this device.", Color.Green);
                        return true;
                    }

                    SetPhaseSafe(session, ConnectionPhase.Reconnecting, $"Reconnecting ({attempt}/{ReconnectMaxAttempts})...");
                    await connection.Client.ConnectAsync();

                    SetPhaseSafe(session, ConnectionPhase.Connected, "Data Stream Active");
                    LogMessage(session, "Reconnected successfully.", Color.Green);
                    return true;
                }
                catch (OperationCanceledException) { return false; }
                catch (Exception ex)
                {
                    LogMessage(session, $"Attempt {attempt} failed: {ex.Message}", Color.OrangeRed);
                }
                finally
                {
                    connection.TransactionLock.Release();
                }
            }

            LogMessage(session, $"{ReconnectMaxAttempts} attempts exhausted, returning to connection phase.", Color.Red);
            return false;
        }

        // ---------------------------------------------------------
        // THREAD-SAFE BATCH UI READ
        // ---------------------------------------------------------

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

        // ---------------------------------------------------------
        // READ + DISPLAY
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
            var connection = session.Connection;
            if (connection == null) return;

            // If this tab is currently recovering from a prior timeout (TimeoutBackoffUntilUtc is
            // set, whether the window is still active or has just expired), use a short probe
            // timeout instead of the full IoTimeoutMs. This keeps the shared TransactionLock held
            // for only ~300ms per retry instead of 3000ms, so other tabs sharing the same physical
            // socket barely notice this slave's continued failures. Once the slave proves itself
            // healthy again (3 consecutive successes), this reverts to the normal, full timeout.
            int? probeTimeout = session.TimeoutBackoffUntilUtc.HasValue ? ProbeTimeoutMs : (int?)null;

            bool[] bits;

            await connection.TransactionLock.WaitAsync();
            try
            {
                bits = parameters.FunctionCode == ModbusFunctionCode.ReadCoils
                    ? await connection.Client.ReadCoilsAsync(parameters.SlaveId, parameters.StartAddress, (ushort)parameters.UserQuantity, probeTimeout)
                    : await connection.Client.ReadDiscreteInputsAsync(parameters.SlaveId, parameters.StartAddress, (ushort)parameters.UserQuantity, probeTimeout);
            }
            finally
            {
                connection.TransactionLock.Release();
            }

            bool hasChanged = HasBitValuesChanged(session.OldBitValues, bits);
            if (hasChanged)
            {
                session.OldBitValues = (bool[])bits.Clone();
                this.Invoke(new Action(() =>
                {
                    LogMessage(session, "Data changed.", Color.Green);
                    DisplayBitsInGrid(session, bits, parameters.StartAddress);
                }));
            }
        }

        private async Task ReadAndDisplayRegistersAsync(TabSession session, PollingParameters parameters)
        {
            var connection = session.Connection;
            if (connection == null || session.RegisterReadBuffer == null) return;

            ushort[] destination = session.RegisterReadBuffer;
            int registerSizePerItem = GetRegisterSizeForDataType(parameters.DataType);

            int? probeTimeout = session.TimeoutBackoffUntilUtc.HasValue ? ProbeTimeoutMs : (int?)null;

            await connection.TransactionLock.WaitAsync();
            try
            {
                if (parameters.FunctionCode == ModbusFunctionCode.ReadHoldingRegisters)
                    await connection.Client.ReadHoldingRegistersAsync(parameters.SlaveId, parameters.StartAddress, destination, probeTimeout);
                else
                    await connection.Client.ReadInputRegistersAsync(parameters.SlaveId, parameters.StartAddress, destination, probeTimeout);
            }
            finally
            {
                connection.TransactionLock.Release();
            }

            bool hasChanged = HasValuesChanged(session.OldValues, destination);
            if (hasChanged)
            {
                session.OldValues = (ushort[])destination.Clone();
                this.Invoke(new Action(() =>
                {
                    LogMessage(session, "Data changed.", Color.Green);
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

        // ---------------------------------------------------------
        // GRID (ALLOCATION-FREE, IN-PLACE UPDATES)
        // ---------------------------------------------------------

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
            // Row count is derived by integer division; if Quantity isn't an exact multiple of
            // registerSizePerItem (e.g. 5 registers with Long=2), the remainder register is
            // silently dropped from the grid — it doesn't form a complete value.
            int expectedRowCount = registers.Length / registerSizePerItem;
            bool layoutValid = IsGridLayoutValid(session, expectedRowCount, startAddress, dataType);

            if (!layoutValid)
            {
                session.Dgv.Rows.Clear();

                // Tail guard: stop before the last group if it doesn't have enough registers
                // remaining to form a full value (only relevant when Quantity isn't a clean
                // multiple of registerSizePerItem).
                for (int i = 0; i + registerSizePerItem <= registers.Length; i += registerSizePerItem)
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
                for (int i = 0; i + registerSizePerItem <= registers.Length; i += registerSizePerItem)
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

        // ---------------------------------------------------------
        // LOGGING
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
        // WriteForm INTEGRATION
        // ---------------------------------------------------------

        private void DgvRegisters_CellDoubleClick(TabSession session, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (session.Connection == null || !session.Connection.Client.IsConnected) return;

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
                session.Connection.Client, session.Connection.TransactionLock,
                parameters.SlaveId, itemAddress, parameters.DataType,
                parameters.FunctionCode, slice, bitSlice);

            writeForm.OnSuccessLog = msg => LogMessage(session, msg, Color.Green);

            if (writeForm.ShowDialog(this) == DialogResult.OK)
            {
                session.OldValues = null;
                session.OldBitValues = null;
            }
        }

        // ---------------------------------------------------------
        // TAB CLOSING
        // ---------------------------------------------------------

        private async void CloseSession(TabSession session)
        {
            await StopDriverAsync(session);

            tabControl.TabPages.Remove(session.Page);
            _sessions.Remove(session);
            session.Page.Dispose();
        }

        // ---------------------------------------------------------
        // TWO-STAGE FORM SHUTDOWN
        // ---------------------------------------------------------

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

        // ---------------------------------------------------------
        // PARAMETER CARRIER STRUCT
        // ---------------------------------------------------------

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

        // ---------------------------------------------------------
        // TABSESSION
        // ---------------------------------------------------------

        private sealed class TabSession
        {
            public TabPage Page { get; }

            public PooledConnection? Connection { get; set; }
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
            public DateTime? TimeoutBackoffUntilUtc { get; set; }

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

            public int ConsecutiveSuccessCount { get; set; }
            
            public TabSession(TabPage page) { Page = page; }
        }
    }
}