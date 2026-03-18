using System;
using System.Threading;
using System.Threading.Tasks;
using NetProve.Monitors;
using NetProve.Managers;
using NetProve.Engines;

namespace NetProve.Core
{
    /// <summary>
    /// Central orchestrator. Starts/stops all subsystems and coordinates
    /// event-driven inter-module communication.
    /// </summary>
    public sealed class CoreEngine : IDisposable
    {
        private static readonly Lazy<CoreEngine> _instance = new(() => new CoreEngine());
        public static CoreEngine Instance => _instance.Value;

        // ── Sub-systems ───────────────────────────────────────────────────────
        public SystemMonitor SystemMonitor { get; } = new();
        public NetworkAnalyzer NetworkAnalyzer { get; } = new();
        public RAMManager RAMManager { get; } = new();
        public ProcessManager ProcessManager { get; } = new();
        public CacheManager CacheManager { get; } = new();
        public GameDetector GameDetector { get; } = new();
        public LagAnalysisEngine LagAnalysis { get; } = new();
        public LagPredictionEngine LagPrediction { get; } = new();
        public OptimizationEngine Optimization { get; } = new();
        public NetworkOptimizer NetworkOptimizer { get; } = new();
        public SpeedTestEngine SpeedTest { get; } = new();
        public PerformanceReportEngine Reports { get; } = new();

        // ── New sub-systems ─────────────────────────────────────────────────────
        public AutoOptimizer AutoOptimizer { get; } = new();
        public PowerPlanManager PowerPlan { get; } = new();
        public DnsBenchmark DnsBenchmark { get; } = new();
        public NetworkAdapterOptimizer AdapterOptimizer { get; } = new();

        private CancellationTokenSource? _cts;
        private bool _running;

        private CoreEngine()
        {
            // Wire game detection → auto gaming mode
            EventBus.Instance.Subscribe<GameDetectedEvent>(e =>
            {
                if (AppSettings.Instance.AutoStartGamingMode)
                    SetMode(AppMode.Gaming, true);
            });
            EventBus.Instance.Subscribe<GameEndedEvent>(e =>
            {
                SetMode(AppMode.Gaming, false);
            });
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            SystemMonitor.Start(token);
            NetworkAnalyzer.Start(token);
            GameDetector.Start(token);
            LagPrediction.Start(token);

            // Start auto optimizer if enabled
            if (AppSettings.Instance.AutoModeEnabled)
                AutoOptimizer.Enable();
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _cts?.Cancel();
            SystemMonitor.Stop();
            NetworkAnalyzer.Stop();
            GameDetector.Stop();
            LagPrediction.Stop();
            AutoOptimizer.Disable();
        }

        public void SetMode(AppMode mode, bool active)
        {
            switch (mode)
            {
                case AppMode.Gaming:
                    if (active) Optimization.EnableGamingMode();
                    else Optimization.DisableGamingMode();
                    break;
                case AppMode.Streaming:
                    if (active) Optimization.EnableStreamingMode();
                    else Optimization.DisableStreamingMode();
                    break;
            }
            EventBus.Instance.Publish(new ModeChangedEvent { Mode = mode, IsActive = active });
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            SystemMonitor.Dispose();
            NetworkAnalyzer.Dispose();
            GameDetector.Dispose();
            LagPrediction.Dispose();
            ProcessManager.Dispose();
        }
    }
}
