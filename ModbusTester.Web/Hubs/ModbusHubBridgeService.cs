using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using ModbusTester.Web;

namespace ModbusTester.Web.Hubs
{
    /// <summary>
    /// Bridges ModbusSessionManager's plain C# events onto SignalR group broadcasts. Kept as a
    /// separate hosted service (rather than folding SignalR calls into ModbusSessionManager
    /// itself) so the engine stays transport-agnostic — it knows nothing about Hubs, groups, or
    /// serialization.
    /// </summary>
    public sealed class ModbusHubBridgeService : IHostedService
    {
        private readonly ModbusSessionManager _sessionManager;
        private readonly IHubContext<ModbusHub> _hubContext;

        public ModbusHubBridgeService(ModbusSessionManager sessionManager, IHubContext<ModbusHub> hubContext)
        {
            _sessionManager = sessionManager;
            _hubContext = hubContext;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _sessionManager.OnPhaseChanged += HandlePhaseChanged;
            _sessionManager.OnDataReceived += HandleDataReceived;
            _sessionManager.OnTraffic += HandleTraffic;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _sessionManager.OnPhaseChanged -= HandlePhaseChanged;
            _sessionManager.OnDataReceived -= HandleDataReceived;
            _sessionManager.OnTraffic -= HandleTraffic;
            return Task.CompletedTask;
        }

        private void HandlePhaseChanged(string sessionId, ConnectionPhase phase, string message)
            => Fire(_hubContext.Clients.Group(sessionId).SendAsync("PhaseChanged", sessionId, phase.ToString(), message));

        private void HandleDataReceived(string sessionId, ModbusDataSnapshot snapshot)
            => Fire(_hubContext.Clients.Group(sessionId).SendAsync("DataReceived", sessionId, snapshot));

        private void HandleTraffic(string sessionId, byte[] frame, bool isTx)
            => Fire(_hubContext.Clients.Group(sessionId).SendAsync("Traffic", sessionId, frame, isTx));

        /// <summary>
        /// Fire-and-forget by design: the polling loop that raised the source event must never
        /// block on SignalR delivery to (potentially slow or disconnected) browser clients.
        /// Delivery failures are swallowed here rather than left as unobserved task exceptions.
        /// </summary>
        private static void Fire(Task deliveryTask) => _ = ObserveAsync(deliveryTask);

        private static async Task ObserveAsync(Task deliveryTask)
        {
            try { await deliveryTask; }
            catch { /* best-effort broadcast */ }
        }
    }
}
