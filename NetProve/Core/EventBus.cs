using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NetProve.Core
{
    /// <summary>
    /// Lightweight event-driven message bus for inter-module communication.
    /// Keeps modules decoupled and ensures minimal overhead.
    /// </summary>
    public sealed class EventBus
    {
        private static readonly Lazy<EventBus> _instance = new(() => new EventBus());
        public static EventBus Instance => _instance.Value;

        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
        private readonly object _lock = new();

        private EventBus() { }

        public void Subscribe<T>(Action<T> handler) where T : class
        {
            var type = typeof(T);
            lock (_lock)
            {
                if (!_handlers.TryGetValue(type, out var list))
                {
                    list = new List<Delegate>();
                    _handlers[type] = list;
                }
                list.Add(handler);
            }
        }

        public void Unsubscribe<T>(Action<T> handler) where T : class
        {
            var type = typeof(T);
            lock (_lock)
            {
                if (_handlers.TryGetValue(type, out var list))
                    list.Remove(handler);
            }
        }

        public void Publish<T>(T message) where T : class
        {
            var type = typeof(T);
            List<Delegate>? handlers = null;
            lock (_lock)
            {
                if (_handlers.TryGetValue(type, out var list))
                    handlers = new List<Delegate>(list);
            }
            if (handlers == null) return;
            foreach (var h in handlers)
            {
                try { ((Action<T>)h)(message); }
                catch { /* Isolated handler errors don't crash the bus */ }
            }
        }

        public Task PublishAsync<T>(T message) where T : class =>
            Task.Run(() => Publish(message));
    }

    // ─── Event message types ────────────────────────────────────────────────
    public sealed class SystemMetricsUpdatedEvent
    {
        public Models.SystemMetrics Metrics { get; init; } = null!;
    }
    public sealed class NetworkMetricsUpdatedEvent
    {
        public Models.NetworkMetrics Metrics { get; init; } = null!;
    }
    public sealed class GameDetectedEvent
    {
        public string GameName { get; init; } = "";
        public string Platform { get; init; } = "";
        public int ProcessId { get; init; }
    }
    public sealed class GameEndedEvent
    {
        public string GameName { get; init; } = "";
        public DateTime SessionEnd { get; init; }
    }
    public sealed class LagWarningEvent
    {
        public string Cause { get; init; } = "";
        public string Detail { get; init; } = "";
        public Models.LagSeverity Severity { get; init; }
    }
    public sealed class OptimizationAppliedEvent
    {
        public string ActionName { get; init; } = "";
        public string Description { get; init; } = "";
    }
    public sealed class ModeChangedEvent
    {
        public AppMode Mode { get; init; }
        public bool IsActive { get; init; }
    }
    public enum AppMode { Normal, Gaming, Streaming }

    public sealed class AutoModeChangedEvent
    {
        public bool Enabled { get; init; }
    }

    /// <summary>Fired when lag prediction stabilizes — dismiss the warning banner.</summary>
    public sealed class LagWarningDismissEvent { }
}
