using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ModbusTester.Core.Core;

namespace ModbusTester.Core.Core
{
    /// <summary>
    /// A single pooled physical connection shared by any number of TabSessions targeting the
    /// same IP:Port. Bundles the ModbusClient with a reference count and a per-connection
    /// transaction lock that serializes ALL socket access — polling reads, manual writes from
    /// WriteForm, and reconnect attempts — across every tab sharing it.
    /// </summary>
    public sealed class PooledConnection
    {
        public ModbusClient Client { get; }
        public SemaphoreSlim TransactionLock { get; } = new SemaphoreSlim(1, 1);
        public int RefCount;

        public PooledConnection(ModbusClient client)
        {
            Client = client;
        }
    }

    /// <summary>
    /// Static, thread-safe registry of pooled Modbus TCP connections, keyed by "IP:Port".
    /// Guarantees that no matter how many tabs target the same device, only ONE physical TCP
    /// socket is ever opened to it — respecting the low concurrent-connection limits typical
    /// of real PLCs and Modbus TCP gateways.
    /// </summary>
    public static class ModbusConnectionManager
    {
        private static readonly Dictionary<string, PooledConnection> _pool = new();

        // Guards ONLY the dictionary itself (fast, synchronous); never held across an await.
        private static readonly object _poolLock = new();

        public static string MakeKey(string ip, int port) => $"{ip}:{port}";

        /// <summary>
        /// Registers interest in the IP:Port connection (incrementing RefCount) and connects
        /// the underlying socket if this is the first tab requesting it. If the pool entry
        /// already exists, this call reuses it as-is without attempting a reconnect — restoring
        /// a dropped shared connection is the polling loop's responsibility (TryReconnectAsync),
        /// which uses the same TransactionLock to avoid concurrent reconnect attempts.
        /// </summary>
        public static async Task<PooledConnection> AcquireAsync(string ip, int port, int connectTimeoutMs, int ioTimeoutMs)
        {
            string key = MakeKey(ip, port);
            PooledConnection entry;
            bool isNew = false;

            lock (_poolLock)
            {
                if (!_pool.TryGetValue(key, out entry!))
                {
                    entry = new PooledConnection(new ModbusClient(ip, port)
                    {
                        ConnectTimeoutMs = connectTimeoutMs,
                        IoTimeoutMs = ioTimeoutMs
                    });
                    _pool[key] = entry;
                    isNew = true;
                }
                entry.RefCount++;
            }

            if (isNew)
            {
                // Only the tab that actually created the pool entry performs the initial
                // connect; every other tab arriving afterward reuses the same client.
                try
                {
                    await entry.Client.ConnectAsync();
                }
                catch
                {
                    // Roll back: this tab was the only one registered and the initial connect
                    // failed, so the orphaned entry is removed instead of leaking in the pool.
                    lock (_poolLock)
                    {
                        entry.RefCount--;
                        if (entry.RefCount <= 0) _pool.Remove(key);
                    }
                    throw;
                }
            }

            return entry;
        }

        /// <summary>
        /// Releases one reference to the IP:Port connection. Once the last tab using it
        /// releases, the physical socket is disconnected and the pool entry is removed.
        /// </summary>
        public static void Release(string ip, int port)
        {
            string key = MakeKey(ip, port);
            PooledConnection? toDispose = null;

            lock (_poolLock)
            {
                if (_pool.TryGetValue(key, out var entry))
                {
                    entry.RefCount--;
                    if (entry.RefCount <= 0)
                    {
                        _pool.Remove(key);
                        toDispose = entry;
                    }
                }
            }

            toDispose?.Client.Disconnect();
        }
    }
}