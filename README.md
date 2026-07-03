# Industrial ModbusTCP Async Tester

A from-scratch, dependency-free **Modbus TCP Master** implementation and testing suite built in **C# / .NET 10**, designed for industrial reliability rather than a quick prototype. No third-party Modbus libraries (e.g. NModbus) are used — every byte of the protocol, from MBAP header packing to IEEE 754 word-swapping, is implemented manually.

The solution ships with **two independent front-ends** sharing the same core library:

- A **tabbed WinForms desktop tester** for interactive monitoring **and manual register writes**.
- A **headless, read-only console driver** designed to run 24/7 as a Windows Service or Linux/systemd daemon, with live `appsettings.json` hot-reload. The console driver is a pure **data acquisition / polling engine** — it never writes to the device.

Both front-ends are available as **pre-built, self-contained executables** — no .NET SDK or runtime installation required on the target machine. See [Production Deployment & Pre-built Executables](#production-deployment--pre-built-executables) below.

---

## Solution Structure

```
ModbusTester (Solution)
│
├── ModbusTester.Console          → Headless, read-only driver (Top-Level Statements, .NET 10)
│   ├── appsettings.json          → Live-reloadable runtime configuration
│   └── Program.cs                → Outer/Inner loop driver, PeriodicTimer-based polling
│
├── ModbusTester.Core             → Class Library (protocol + transport logic)
│   ├── Core/
│   │   └── ModbusClient.cs       → TCP transport, semaphore-guarded I/O, ArrayPool buffers
│   ├── Exceptions/
│   │   ├── ModbusConnectionException.cs
│   │   ├── ModbusProtocolException.cs
│   │   └── ModbusTimeoutException.cs
│   └── Protocol/
│       ├── ModbusDataConverter.cs   → Span-based, allocation-free type conversion
│       ├── ModbusFunctionCode.cs
│       ├── ModbusRequestBuilder.cs
│       ├── ModbusResponseParser.cs
│       └── ModbusTransactionManager.cs
│
└── ModbusTester.WinForms         → Tabbed desktop UI (read + write)
    ├── MainForm.cs / .Designer.cs
    ├── WriteForm.cs / .Designer.cs
    └── Program.cs
```

`ModbusTester.Console` and `ModbusTester.WinForms` both depend on `ModbusTester.Core`; `Core` has zero UI or console dependencies and can be reused in any .NET 10 project (including headless Linux/Raspberry Pi deployments).

> **Note on write support:** `ModbusTester.Core` implements the full read/write protocol surface (FC01–FC06, FC16). `ModbusTester.WinForms` exposes writes through a type-aware Write dialog (double-click any grid row). `ModbusTester.Console` is intentionally **read-only** — see [Console: 24/7 Read-Only Polling Daemon](#console-247-read-only-polling-daemon) for the rationale.

---

## Key Engineering Features

### Protocol Layer (built from raw bytes)
- Manual MBAP header packing/parsing (Transaction ID, Protocol ID, Length, Unit ID).
- Big-Endian ⇄ Little-Endian conversion handled explicitly via bit-shifting — no `Array.Reverse` chains.
- Support for **FC01/02/03/04** (read) and **FC05/06/16** (write) at the `ModbusTester.Core` library level, including automatic **request fragmentation** for reads exceeding the 125-register protocol limit.
- The FC05/06/16 write functions live entirely in `ModbusTester.Core` and are consumed **only by the WinForms UI's Write dialog**. The console driver never calls them — it is a strict read path by design (see below).
- Configurable **word/byte swap ("Inverse") modes** for Float, Long, and Double types, matching common PLC/SCADA simulator conventions (e.g. `mbslave`).

### Transport Resilience
- `SemaphoreSlim`-guarded socket access — guarantees only one transaction is in flight at a time, even under concurrent tab/session usage.
- `ArrayPool<byte>` rental for network buffers — zero per-transaction heap allocation on the read path.
- Guard clauses detect corrupted/out-of-range packet lengths and force a clean socket reset instead of risking buffer overruns.
- Strict exception taxonomy: `ModbusProtocolException` (device-level, socket stays alive) vs. `ModbusConnectionException`/`ModbusTimeoutException` (transport-level, triggers reconnect).

### Dual-Loop Driver Architecture (Console & WinForms)
Both front-ends implement the same **Outer/Inner Loop** resilience pattern:
- **Outer Loop** — patiently retries the initial/lost connection every 5 seconds, forever. The service never exits on a missing PLC or a dropped cable, no matter how long the outage lasts.
- **Inner Loop** — a `PeriodicTimer`-driven polling loop that reads registers, detects data changes, and falls back to the Outer Loop only after exhausting a bounded reconnect budget (5 attempts).
- Live IP/Port changes are validated via a **temporary-client-first** pattern: a new connection is proven successful *before* any persistent state is mutated, preventing a bad config value from permanently breaking the reconnect loop.

### Zero-Allocation Hot Path
- `ModbusResponseParser` writes directly into a caller-supplied `Span<ushort>` — no per-read array allocation.
- WinForms `DataGridView` rows are updated **in-place** (`Cells[i].Value = ...`) instead of `Clear()` + `Add()` on every tick, eliminating UI flicker and GC pressure during 24/7 operation.
- Layout-change detection uses cached primitive state (`RenderedRowCount`, `RenderedStartAddress`, `RenderedDataType`) instead of string parsing.

### WinForms: Tabbed, Phase-Locked UI (Read & Write)
- Multiple independent device sessions in a single window (`TabControl`), each with its own connection, buffer, and log panel.
- **Phase-based control locking** (`Idle → Searching → Connected → DataError → Reconnecting`) prevents structural parameters (DataType, Quantity, StartAddress) from being changed mid-poll, which would otherwise corrupt the read buffer or crash the grid.
- Two-stage graceful shutdown (`FormClosing` cancel → await background drivers → re-close) guarantees no background task touches a disposed control.
- Double-click any grid row to open a type-aware **Write dialog** (FC05/06/16) with input masking and range validation. **This is the only place in the solution where a write request is ever issued.**

### Console: 24/7 Read-Only Polling Daemon
`ModbusTester.Console` is purpose-built as a **headless data acquisition engine**, not a general-purpose Modbus client. It is architecturally incapable of writing to a device — `ModbusClient`'s write methods (`WriteSingleCoilAsync`, `WriteSingleRegisterAsync`, `WriteMultipleRegistersAsync`) are simply never referenced anywhere in `Program.cs`. This is a deliberate design boundary: a 24/7 unattended collector that could also mutate PLC state on a bad config value or a timing race is a much larger operational risk than a strictly read-only one.

- `appsettings.json` with `reloadOnChange: true` — SlaveId, StartAddress, Quantity, DataType, and PollingInterval can all be changed **without restarting the process**.
- Log throttling: repeated identical errors are logged once, not once per tick; a `[RECOVERY]` message confirms when the fault clears.
- Heartbeat logging every 100 cycles when data is stable, so a `tail`-ed log never looks "dead."
- If you need to write values, use the WinForms application — the console driver will not do it, by design.

---

## Production Deployment & Pre-built Executables

Both front-ends are distributed as **self-contained, single-file executables** — no .NET SDK or runtime installation is required on the target machine. The distribution package follows this layout:

```
Modbus/ (Distribution Root)
│
├── ModbusApp.exe             → Standalone, single-file WinForms desktop application
│
└── Console/                  → 24/7 background service folder
    ├── appsettings.json      → Live-editable runtime configuration
    ├── ModbusConsole-Win.exe → Standalone Windows console driver (Read-Only)
    └── ModbusConsole-Linux   → Standalone Linux binary driver (Read-Only, no extension)
```

> **Important:** `ModbusConsole-Win.exe` / `ModbusConsole-Linux` **must always sit in the same folder** as `appsettings.json`. This is enforced by design: the resilient connection guard in `Program.cs` will refuse to start if the configuration file cannot be located next to the executable, rather than silently falling back to hardcoded defaults in a production environment.

### Running the Pre-built WinForms App
Just double-click `ModbusApp.exe`. No installation, no dependencies. The phase-based control locking behavior (structural parameters lock once a connection is established) is identical to the source-built version — this is compiled application logic, not a development-only safeguard.

### Running the Pre-built Console Driver

**Windows:**
```powershell
cd Console
.\ModbusConsole-Win.exe
```

**Linux:**
```bash
cd Console
chmod +x ModbusConsole-Linux   # required once — grants execute permission
./ModbusConsole-Linux
```

Edit `appsettings.json` in the same folder at any time while the driver is running — changes to `Quantity`, `DataType`, `StartAddress`, or `PollingIntervalMs` take effect within one polling cycle, with no restart needed. Remember: this driver **reads only**; it is intended for continuous data collection/logging, not for issuing write commands.

---

## Getting Started (Building from Source)

### Prerequisites
- .NET 10 SDK
- A Modbus TCP slave to test against — e.g. [mbslave](https://sourceforge.net/projects/mbslave/) or [diagslave](http://www.modbusdriver.com/diagslave.html) for local testing.

### Running the WinForms Tester
```bash
cd ModbusTester.WinForms
dotnet run
```
Enter the target IP/Port, hit **Connect**, and the driver will search for the device, lock structural parameters once connected, and start streaming register values into the grid. Double-click a row to write a new value.

### Running the Console Driver
```bash
cd ModbusTester.Console
dotnet run
```
Edit `appsettings.json` while the driver is running to see live hot-reload in action. This process is read-only — it will poll and log data, but will never issue a write request.

### Configuration Reference (`appsettings.json`)

This is the actual configuration file shipped with the production build:

```json
{
  "ModbusSettings": {
    "TargetIp": "127.0.0.1",
    "TargetPort": 502,
    "SlaveId": 1,
    "StartAddress": 0,
    "Quantity": 5,
    "DataType": "Signed (16-bit)",
    "PollingIntervalMs": 500,
    "ConnectTimeoutMs": 3000,
    "IoTimeoutMs": 3000,
    "ReconnectMaxAttempts": 5,
    "ReconnectDelayMs": 2000,
    "_comment": "Supported types: 'Unsigned (16-bit)', 'Signed (16-bit)', 'Hex', 'Binary', 'Float (32-bit)', 'Float Inverse (32-bit)', 'Long (32-bit)', 'Long Inverse (32-bit)', 'Double (64-bit)', 'Double Inverse (64-bit)'"
  }
}
```

---

## Architecture Notes

- **Nullable Reference Types** are enabled solution-wide; the project compiles with **zero warnings**.
- All async local functions in `Program.cs` are intentionally non-static, since static local functions cannot capture outer-scope configuration variables in C# Top-Level Statements.
- The `Inverse` swap convention follows the `mbslave` simulator standard: `"Float (32-bit)"` uses `inverse: true`, while `"Float Inverse (32-bit)"` uses `inverse: false` (and equivalently for Long/Double).
- Write capability (FC05/06/16) is confined to `ModbusTester.Core` (library-level) and `ModbusTester.WinForms` (the only caller); this is a deliberate architectural boundary, not an oversight — see [Console: 24/7 Read-Only Polling Daemon](#console-247-read-only-polling-daemon).

## License

This project currently has no explicit license file. Add one (MIT, Apache-2.0, etc.) if you intend for others to reuse this code.
