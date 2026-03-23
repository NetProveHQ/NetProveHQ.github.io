using System;
using System.Threading;
using System.Threading.Tasks;
using NetProve.Core;
using NetProve.Managers;

namespace NetProve.Engines
{
    /// <summary>
    /// Intelligent rule-based optimization controller.
    /// Triggers optimizations only when metrics cross configured thresholds.
    /// All optimizations are reversible.
    /// </summary>
    public sealed class OptimizationEngine
    {
        private bool _gamingModeActive;
        private bool _streamingModeActive;

        // Debounce flags to avoid rapid re-triggering
        private DateTime _lastRamOptimize = DateTime.MinValue;
        private DateTime _lastProcessThrottle = DateTime.MinValue;
        private const int RamCooldownSec = 120;
        private const int ProcessCooldownSec = 60;

        public bool GamingModeActive => _gamingModeActive;
        public bool StreamingModeActive => _streamingModeActive;

        public OptimizationEngine()
        {
            // Auto-trigger on threshold events
            EventBus.Instance.Subscribe<SystemMetricsUpdatedEvent>(OnSystemMetrics);
        }

        private void OnSystemMetrics(SystemMetricsUpdatedEvent e)
        {
            var m = e.Metrics;
            var s = AppSettings.Instance;

            bool anyModeActive = _gamingModeActive || _streamingModeActive;

            // Auto RAM trim when under pressure + cooldown respected
            if (m.RamUsagePercent >= s.RamPressureThresholdPercent &&
                (DateTime.Now - _lastRamOptimize).TotalSeconds > RamCooldownSec)
            {
                _lastRamOptimize = DateTime.Now;
                _ = Task.Run(async () =>
                {
                    await CoreEngine.Instance.RAMManager.OptimizeAsync();
                });
            }

            // Auto throttle background processes in active modes
            if (anyModeActive &&
                m.CpuUsagePercent >= s.CpuOverloadThresholdPercent &&
                (DateTime.Now - _lastProcessThrottle).TotalSeconds > ProcessCooldownSec)
            {
                _lastProcessThrottle = DateTime.Now;
                _ = Task.Run(async () =>
                {
                    await CoreEngine.Instance.ProcessManager.ThrottleBackgroundProcessesAsync();
                });
            }
        }

        public void EnableGamingMode()
        {
            if (_gamingModeActive) return;
            _gamingModeActive = true;

            _ = Task.Run(async () =>
            {
                // Throttle background processes
                await CoreEngine.Instance.ProcessManager.ThrottleBackgroundProcessesAsync();

                // Flush DNS for fresh routing
                await CoreEngine.Instance.NetworkOptimizer.FlushDnsAsync();

                // Prioritize game process if known
                var session = CoreEngine.Instance.GameDetector.CurrentSession;
                if (session != null && session.ProcessId > 0)
                    await CoreEngine.Instance.NetworkOptimizer.PrioritizeGameProcessAsync(session.ProcessId);

                // Enhanced gaming optimizations
                await CoreEngine.Instance.PowerPlan.SetHighPerformanceAsync();
                await CoreEngine.Instance.AdapterOptimizer.DisableNagleAsync();
                await CoreEngine.Instance.PowerPlan.ReduceVisualEffectsAsync();

                // Wi-Fi power saving disable (prevents 10-100ms spikes)
                await CoreEngine.Instance.AdapterOptimizer.DisableWifiPowerSaveAsync();

                // Disable Delivery Optimization (stops P2P update bandwidth theft)
                await CoreEngine.Instance.AdapterOptimizer.DisableDeliveryOptimizationAsync();

                // Reduce interrupt moderation for lower NIC latency
                await CoreEngine.Instance.AdapterOptimizer.ReduceInterruptModerationAsync();

                // Apply QoS for game traffic priority
                if (session != null && session.ProcessId > 0)
                    await CoreEngine.Instance.NetworkOptimizer.ApplyGameQoSAsync(session.ProcessId);

                EventBus.Instance.Publish(new OptimizationAppliedEvent
                {
                    ActionName = "Gaming Mode ON",
                    Description = "Gaming mode activated. Wi-Fi PSM, power plan, QoS, interrupt moderation optimized."
                });
            });
        }

        public void DisableGamingMode()
        {
            if (!_gamingModeActive) return;
            _gamingModeActive = false;

            _ = Task.Run(async () =>
            {
                await CoreEngine.Instance.ProcessManager.RestoreProcessPrioritiesAsync();
                await CoreEngine.Instance.PowerPlan.RestoreOriginalPlanAsync();
                await CoreEngine.Instance.AdapterOptimizer.EnableNagleAsync();
                await CoreEngine.Instance.PowerPlan.RestoreVisualEffectsAsync();
                await CoreEngine.Instance.NetworkOptimizer.RemoveGameQoSAsync();
                await CoreEngine.Instance.AdapterOptimizer.RestoreWifiPowerSaveAsync();
                await CoreEngine.Instance.AdapterOptimizer.RestoreDeliveryOptimizationAsync();
                await CoreEngine.Instance.AdapterOptimizer.RestoreInterruptModerationAsync();
                EventBus.Instance.Publish(new OptimizationAppliedEvent
                {
                    ActionName = "Gaming Mode OFF",
                    Description = "Gaming mode deactivated. All settings restored."
                });
            });
        }

        public void EnableStreamingMode()
        {
            if (_streamingModeActive) return;
            _streamingModeActive = true;

            _ = Task.Run(async () =>
            {
                await CoreEngine.Instance.ProcessManager.ThrottleBackgroundProcessesAsync();
                await CoreEngine.Instance.NetworkOptimizer.FlushDnsAsync();

                EventBus.Instance.Publish(new OptimizationAppliedEvent
                {
                    ActionName = "Streaming Mode ON",
                    Description = "Streaming mode activated. Background processes throttled."
                });
            });
        }

        public void DisableStreamingMode()
        {
            if (!_streamingModeActive) return;
            _streamingModeActive = false;

            _ = Task.Run(async () =>
            {
                await CoreEngine.Instance.ProcessManager.RestoreProcessPrioritiesAsync();
                EventBus.Instance.Publish(new OptimizationAppliedEvent
                {
                    ActionName = "Streaming Mode OFF",
                    Description = "Streaming mode deactivated. Process priorities restored."
                });
            });
        }

        /// <summary>Run all safe optimizations immediately.</summary>
        public async Task RunFullOptimizationAsync()
        {
            await CoreEngine.Instance.RAMManager.OptimizeAsync();
            await CoreEngine.Instance.ProcessManager.ThrottleBackgroundProcessesAsync();
            await CoreEngine.Instance.NetworkOptimizer.FlushDnsAsync();
            await CoreEngine.Instance.NetworkOptimizer.ApplyTcpOptimizationsAsync();
            await CoreEngine.Instance.RAMManager.FlushStandbyListAsync();
        }
    }
}
