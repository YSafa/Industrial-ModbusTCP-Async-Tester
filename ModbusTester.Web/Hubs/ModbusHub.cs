using Microsoft.AspNetCore.SignalR;

namespace ModbusTester.Web.Hubs
{
    /// <summary>
    /// Bottom-up flow: browsers join a group per session so server-pushed events (phase changes,
    /// data reads, raw wire traffic) are scoped to the tab actually watching that session, instead
    /// of broadcasting every session's telemetry to every connected client. The hub itself pushes
    /// nothing — ModbusHubBridgeService drives Clients.Group(sessionId).SendAsync(...) from the
    /// ModbusSessionManager's events.
    /// </summary>
    public sealed class ModbusHub : Hub
    {
        public Task JoinSession(string sessionId) => Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

        public Task LeaveSession(string sessionId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
    }
}
