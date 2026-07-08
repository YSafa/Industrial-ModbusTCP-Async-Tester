using Microsoft.AspNetCore.Mvc;
using ModbusTester.Core.Protocol;

namespace ModbusTester.Web.Controllers
{
    public sealed class CreateSessionRequest
    {
        public string? SessionId { get; set; }
        public required string Ip { get; set; }
        public int Port { get; set; } = 502;
        public byte SlaveId { get; set; }
        public ushort StartAddress { get; set; }
        public string DataType { get; set; } = "Unsigned (16-bit)";
        public ModbusFunctionCode FunctionCode { get; set; } = ModbusFunctionCode.ReadHoldingRegisters;
        public int Quantity { get; set; } = 1;
        public int IntervalMs { get; set; } = 1000;
    }

    public sealed class UpdateParametersRequest
    {
        public required string Ip { get; set; }
        public int Port { get; set; } = 502;
        public byte SlaveId { get; set; }
        public ushort StartAddress { get; set; }
        public string DataType { get; set; } = "Unsigned (16-bit)";
        public ModbusFunctionCode FunctionCode { get; set; } = ModbusFunctionCode.ReadHoldingRegisters;
        public int Quantity { get; set; } = 1;
        public int IntervalMs { get; set; } = 1000;
    }

    public sealed class WriteCoilRequest
    {
        public byte SlaveId { get; set; }
        public ushort Address { get; set; }
        public bool Value { get; set; }
    }

    public sealed class WriteRegisterRequest
    {
        public byte SlaveId { get; set; }
        public ushort Address { get; set; }
        public ushort Value { get; set; }
    }

    public sealed class WriteMultipleRegistersRequest
    {
        public byte SlaveId { get; set; }
        public ushort StartAddress { get; set; }
        public required ushort[] Values { get; set; }
    }

    public sealed class SessionSummary
    {
        public required string SessionId { get; set; }
        public required string Phase { get; set; }
        public required string Ip { get; set; }
        public required int Port { get; set; }
    }

    /// <summary>
    /// Top-down flow: REST endpoints for the commands a user issues (create/connect/stop a
    /// session, write a value) — the counterpart to ModbusHub's bottom-up event stream. Every
    /// endpoint is a thin, ephemeral call into the singleton ModbusSessionManager; no connection
    /// state lives on the controller.
    /// </summary>
    [ApiController]
    [Route("api/sessions")]
    public sealed class SessionsController : ControllerBase
    {
        private readonly ModbusSessionManager _sessionManager;

        public SessionsController(ModbusSessionManager sessionManager)
        {
            _sessionManager = sessionManager;
        }

        [HttpGet("{sessionId}")]
        public ActionResult<SessionSummary> Get(string sessionId)
        {
            if (!_sessionManager.TryGetSession(sessionId, out var session) || session == null)
                return NotFound();

            return new SessionSummary
            {
                SessionId = session.SessionId,
                Phase = session.CurrentPhase.ToString(),
                Ip = session.CurrentIp,
                Port = session.CurrentPort
            };
        }

        /// <summary>Creates a session and immediately starts its driver loop (connects to the target).</summary>
        [HttpPost]
        public async Task<ActionResult<SessionSummary>> Create([FromBody] CreateSessionRequest request)
        {
            string sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? Guid.NewGuid().ToString() : request.SessionId;

            var parameters = new PollingParameters
            {
                Ip = request.Ip,
                Port = request.Port,
                SlaveId = request.SlaveId,
                StartAddress = request.StartAddress,
                DataType = request.DataType,
                FunctionCode = request.FunctionCode,
                UserQuantity = request.Quantity,
                IntervalMs = request.IntervalMs
            };

            try
            {
                await _sessionManager.StartSessionAsync(sessionId, parameters);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }

            return CreatedAtAction(nameof(Get), new { sessionId }, new SessionSummary
            {
                SessionId = sessionId,
                Phase = ConnectionPhase.Idle.ToString(),
                Ip = request.Ip,
                Port = request.Port
            });
        }

        /// <summary>Re-points a running session at a new target/read parameters (also used to "Connect" after editing IP/Port).</summary>
        [HttpPut("{sessionId}/parameters")]
        public IActionResult UpdateParameters(string sessionId, [FromBody] UpdateParametersRequest request)
        {
            var parameters = new PollingParameters
            {
                Ip = request.Ip,
                Port = request.Port,
                SlaveId = request.SlaveId,
                StartAddress = request.StartAddress,
                DataType = request.DataType,
                FunctionCode = request.FunctionCode,
                UserQuantity = request.Quantity,
                IntervalMs = request.IntervalMs
            };

            return _sessionManager.UpdateParameters(sessionId, parameters) ? NoContent() : NotFound();
        }

        [HttpPost("{sessionId}/stop")]
        public async Task<IActionResult> Stop(string sessionId)
        {
            if (!_sessionManager.TryGetSession(sessionId, out _))
                return NotFound();

            await _sessionManager.StopSessionAsync(sessionId);
            return NoContent();
        }

        [HttpPost("{sessionId}/write/coil")]
        public async Task<IActionResult> WriteCoil(string sessionId, [FromBody] WriteCoilRequest request)
        {
            if (!_sessionManager.TryGetSession(sessionId, out _))
                return NotFound();

            try
            {
                await _sessionManager.WriteSingleCoilAsync(sessionId, request.SlaveId, request.Address, request.Value);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
        }

        [HttpPost("{sessionId}/write/register")]
        public async Task<IActionResult> WriteRegister(string sessionId, [FromBody] WriteRegisterRequest request)
        {
            if (!_sessionManager.TryGetSession(sessionId, out _))
                return NotFound();

            try
            {
                await _sessionManager.WriteSingleRegisterAsync(sessionId, request.SlaveId, request.Address, request.Value);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
        }

        [HttpPost("{sessionId}/write/registers")]
        public async Task<IActionResult> WriteRegisters(string sessionId, [FromBody] WriteMultipleRegistersRequest request)
        {
            if (!_sessionManager.TryGetSession(sessionId, out _))
                return NotFound();

            try
            {
                await _sessionManager.WriteMultipleRegistersAsync(sessionId, request.SlaveId, request.StartAddress, request.Values);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
        }
    }
}
