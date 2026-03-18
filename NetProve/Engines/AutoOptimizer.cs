using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetProve.Core;
using NetProve.Models;

namespace NetProve.Engines
{
    /// <summary>
    /// Intelligent automatic optimization engine.
    /// Monitors system + network metrics in real-time and takes proactive action.
    /// Handles: RAM pressure, CPU overload, network degradation, game optimization,
    /// background process management, DNS health, and adaptive responses.
    /// </summary>
    public sealed class AutoOptimizer
    {
        private bool _enabled;
        private readonly SemaphoreSlim _sysGuard = new(1, 1);
        private readonly SemaphoreSlim _netGuard = new(1, 1);

        // ── Cooldown tracking ─────────────────────────────────────────────────
        private DateTime _lastRamOptimize = DateTime.MinValue;
        private DateTime _lastDnsFlush = DateTime.MinValue;
        private DateTime _lastProcessThrottle = DateTime.MinValue;
        private DateTime _lastNetworkHeal = DateTime.MinValue;
        private DateTime _lastStandbyFlush = DateTime.MinValue;
        private DateTime _lastGamePrioritize = DateTime.MinValue;

        // ── State tracking ────────────────────────────────────────────────────
        private bool _tcpApplied;
        private bool _powerPlanSet;
        private bool _nagleDisabled;
        private bool _gamingActive;
        private int _gameProcessId;
        private int _consecutiveBadPings;
        private int _consecutiveHighPacketLoss;
        private int _consecutiveHighCpu;
        private int _consecutiveHighRam;

        // ── Thresholds & cooldowns ────────────────────────────────────────────
        private const int RamCooldownSec = 90;
        private const int StandbyFlushCooldownSec = 180;
        private const int ProcessCooldownSec = 45;
        private const int DnsFlushIntervalMin = 20;
        private const int NetworkHealCooldownSec = 120;
        private const int GamePrioritizeCooldownSec = 30;

        private const double PingWarningMs = 100;
        private const double PingCriticalMs = 180;
        private const double PacketLossWarningPct = 2;
        private const double PacketLossCriticalPct = 5;
        private const double JitterWarningMs = 25;
        private const float CpuWarningPct = 85;
        private const float RamWarningPct = 82;
        private const float RamCriticalPct = 92;

        // ── Consecutive check thresholds (avoid false positives) ──────────────
        private const int ConsecutiveThreshold = 3;

        public bool IsEnabled => _enabled;

        public void Enable()
        {
            if (_enabled) return;
            _enabled = true;

            // Reset all state
            _consecutiveBadPings = 0;
            _consecutiveHighPacketLoss = 0;
            _consecutiveHighCpu = 0;
            _consecutiveHighRam = 0;

            // Subscribe to ALL metric events
            EventBus.Instance.Subscribe<SystemMetricsUpdatedEvent>(OnSystemMetrics);
            EventBus.Instance.Subscribe<NetworkMetricsUpdatedEvent>(OnNetworkMetrics);
            EventBus.Instance.Subscribe<GameDetectedEvent>(OnGameDetected);
            EventBus.Instance.Subscribe<GameEndedEvent>(OnGameEnded);

            // Apply TCP optimizations immediately
            if (!_tcpApplied)
            {
                _ = Task.Run(async () =>
                {
                    await CoreEngine.Instance.NetworkOptimizer.ApplyTcpOptimizationsAsync();
                    _tcpApplied = true;
                });
            }

            // Initial DNS flush
            _ = Task.Run(async () =>
            {
                await CoreEngine.Instance.NetworkOptimizer.FlushDnsAsync();
                _lastDnsFlush = DateTime.Now;
            });

            EventBus.Instance.Publish(new AutoModeChangedEvent { Enabled = true });
        }

        public void Disable()
        {
            if (!_enabled) return;
            _enabled = false;

            EventBus.Instance.Unsubscribe<SystemMetricsUpdatedEvent>(OnSystemMetrics);
            EventBus.Instance.Unsubscribe<NetworkMetricsUpdatedEvent>(OnNetworkMetrics);
            EventBus.Instance.Unsubscribe<GameDetectedEvent>(OnGameDetected);
            EventBus.Instance.Unsubscribe<GameEndedEvent>(OnGameEnded);

            // Restore everything
            _ = Task.Run(async () =>
            {
                if (_powerPlanSet)
                {
                    await CoreEngine.Instance.PowerPlan.RestoreOriginalPlanAsync();
                    _powerPlanSet = false;
                }
                if (_nagleDisabled)
                {
                    await CoreEngine.Instance.AdapterOptimizer.EnableNagleAsync();
                    _nagleDisabled = false;
                }
                await CoreEngine.Instance.PowerPlan.RestoreVisualEffectsAsync();
                await CoreEngine.Instance.ProcessManager.RestoreProcessPrioritiesAsync();
            });

            EventBus.Instance.Publish(new AutoModeChangedEvent { Enabled = false });
        }

        // ── System metrics handler ────────────────────────────────────────────
        private void OnSystemMetrics(SystemMetricsUpdatedEvent e)
        {
            if (!_enabled) return;
            if (!_sysGuard.Wait(0)) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var m = e.Metrics;
                    var now = DateTime.Now;

                    // ── RAM Management ─────────────────────────────────────
                    if (m.RamUsagePercent >= RamCriticalPct)
                    {
                        _consecutiveHighRam++;
                        if (_consecutiveHighRam >= 2 &&
                            (now - _lastRamOptimize).TotalSeconds > RamCooldownSec)
                        {
                            _lastRamOptimize = now;
                            await CoreEngine.Instance.RAMManager.OptimizeAsync();

                            // Critical RAM: also flush standby list
                            if ((now - _lastStandbyFlush).TotalSeconds > StandbyFlushCooldownSec)
                            {
                                _lastStandbyFlush = now;
                                await CoreEngine.Instance.RAMManager.FlushStandbyListAsync();
                            }

                            // Critical RAM during gaming: throttle background processes
                            if (_gamingActive &&
                                (now - _lastProcessThrottle).TotalSeconds > ProcessCooldownSec)
                            {
                                _lastProcessThrottle = now;
                                await CoreEngine.Instance.ProcessManager.ThrottleBackgroundProcessesAsync();
                            }
                        }
                    }
                    else if (m.RamUsagePercent >= RamWarningPct)
                    {
                        _consecutiveHighRam++;
                        if (_consecutiveHighRam >= ConsecutiveThreshold &&
                            (now - _lastRamOptimize).TotalSeconds > RamCooldownSec)
                        {
                            _lastRamOptimize = now;
                            _consecutiveHighRam = 0;
                            await CoreEngine.Instance.RAMManager.OptimizeAsync();
                        }
                    }
                    else
                    {
                        _consecutiveHighRam = 0;
                    }

                    // ── CPU Management ─────────────────────────────────────
                    if (m.CpuUsagePercent >= CpuWarningPct)
                    {
                        _consecutiveHighCpu++;
                        if (_consecutiveHighCpu >= ConsecutiveThreshold &&
                            (now - _lastProcessThrottle).TotalSeconds > ProcessCooldownSec)
                        {
                            _lastProcessThrottle = now;
                            _consecutiveHighCpu = 0;
                            await CoreEngine.Instance.ProcessManager.ThrottleBackgroundProcessesAsync();
                        }
                    }
                    else
                    {
                        _consecutiveHighCpu = 0;
                    }

                    // ── Game process priority maintenance ──────────────────
                    if (_gamingActive && _gameProcessId > 0 &&
                        (now - _lastGamePrioritize).TotalSeconds > GamePrioritizeCooldownSec)
                    {
                        _lastGamePrioritize = now;
                        try
                        {
                            var gameProc = Process.GetProcessById(_gameProcessId);
                            if (gameProc.PriorityClass != ProcessPriorityClass.High)
                                gameProc.PriorityClass = ProcessPriorityClass.High;
                            gameProc.Dispose();
                        }
                        catch { /* Game may have ended */ }
                    }

                    // ── Periodic DNS flush ─────────────────────────────────
                    if ((now - _lastDnsFlush).TotalMinutes > DnsFlushIntervalMin)
                    {
                        _lastDnsFlush = now;
                        await CoreEngine.Instance.NetworkOptimizer.FlushDnsAsync();
                    }
                }
                catch { }
                finally { _sysGuard.Release(); }
            });
        }

        // ── Network metrics handler ───────────────────────────────────────────
        private void OnNetworkMetrics(NetworkMetricsUpdatedEvent e)
        {
            if (!_enabled) return;
            if (!_netGuard.Wait(0)) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var m = e.Metrics;
                    var now = DateTime.Now;

                    // ── Packet Loss Response ──────────────────────────────
                    if (m.PacketLossPercent >= PacketLossWarningPct)
                    {
                        _consecutiveHighPacketLoss++;

                        if (_consecutiveHighPacketLoss >= ConsecutiveThreshold &&
                            (now - _lastNetworkHeal).TotalSeconds > NetworkHealCooldownSec)
                        {
                            _lastNetworkHeal = now;
                            _consecutiveHighPacketLoss = 0;

                            // Flush DNS
                            await CoreEngine.Instance.NetworkOptimizer.FlushDnsAsync();
                            _lastDnsFlush = now;

                            // Re-apply TCP optimizations
                            await CoreEngine.Instance.NetworkOptimizer.ApplyTcpOptimizationsAsync();

                            // If critical packet loss, additional measures
                            if (m.PacketLossPercent >= PacketLossCriticalPct)
                            {
                                // Ensure Nagle is disabled
                                if (!_nagleDisabled)
                                {
                                    await CoreEngine.Instance.AdapterOptimizer.DisableNagleAsync();
                                    _nagleDisabled = true;
                                }

                                // Throttle background processes to free bandwidth
                                if ((now - _lastProcessThrottle).TotalSeconds > ProcessCooldownSec)
                                {
                                    _lastProcessThrottle = now;
                                    await CoreEngine.Instance.ProcessManager.ThrottleBackgroundProcessesAsync();
                                }
                            }
                        }
                    }
                    else
                    {
                        _consecutiveHighPacketLoss = 0;
                    }

                    // ── High Ping Response ────────────────────────────────
                    if (m.PingMs >= PingWarningMs)
                    {
                        _consecutiveBadPings++;

                        if (_consecutiveBadPings >= ConsecutiveThreshold &&
                            (now - _lastNetworkHeal).TotalSeconds > NetworkHealCooldownSec)
                        {
                            _lastNetworkHeal = now;
                            _consecutiveBadPings = 0;

                            // DNS flush can help with routing issues
                            await CoreEngine.Instance.NetworkOptimizer.FlushDnsAsync();
                            _lastDnsFlush = now;

                            // Critical ping: take stronger action
                            if (m.PingMs >= PingCriticalMs)
                            {
                                // Ensure TCP optimizations are applied
                                if (!_tcpApplied)
                                {
                                    await CoreEngine.Instance.NetworkOptimizer.ApplyTcpOptimizationsAsync();
                                    _tcpApplied = true;
                                }

                                // Throttle bandwidth-hungry background processes
                                if ((now - _lastProcessThrottle).TotalSeconds > ProcessCooldownSec)
                                {
                                    _lastProcessThrottle = now;
                                    await CoreEngine.Instance.ProcessManager.ThrottleBackgroundProcessesAsync();
                                }
                            }
                        }
                    }
                    else
                    {
                        _consecutiveBadPings = 0;
                    }

                    // ── High Jitter Response ─────────────────────────────
                    if (m.JitterMs >= JitterWarningMs && _gamingActive)
                    {
                        // High jitter during gaming: ensure all optimizations are on
                        if (!_nagleDisabled)
                        {
                            await CoreEngine.Instance.AdapterOptimizer.DisableNagleAsync();
                            _nagleDisabled = true;
                        }
                    }
                }
                catch { }
                finally { _netGuard.Release(); }
            });
        }

        // ── Game lifecycle handlers ───────────────────────────────────────────
        private void OnGameDetected(GameDetectedEvent e)
        {
            if (!_enabled) return;
            _gamingActive = true;
            _gameProcessId = e.ProcessId;

            _ = Task.Run(async () =>
            {
                try
                {
                    // ── Full gaming optimization suite ─────────────────────

                    // 1. High Performance power plan
                    if (!_powerPlanSet)
                    {
                        await CoreEngine.Instance.PowerPlan.SetHighPerformanceAsync();
                        _powerPlanSet = true;
                    }

                    // 2. Disable Nagle for lowest latency
                    if (!_nagleDisabled)
                    {
                        await CoreEngine.Instance.AdapterOptimizer.DisableNagleAsync();
                        _nagleDisabled = true;
                    }

                    // 3. Re-apply TCP optimizations (in case they were reset)
                    if (!_tcpApplied)
                    {
                        await CoreEngine.Instance.NetworkOptimizer.ApplyTcpOptimizationsAsync();
                        _tcpApplied = true;
                    }

                    // 4. Reduce visual effects to free CPU/GPU
                    await CoreEngine.Instance.PowerPlan.ReduceVisualEffectsAsync();

                    // 5. Flush DNS for fresh routing
                    await CoreEngine.Instance.NetworkOptimizer.FlushDnsAsync();
                    _lastDnsFlush = DateTime.Now;

                    // 6. Clear standby RAM before gaming
                    await CoreEngine.Instance.RAMManager.OptimizeAsync();
                    _lastRamOptimize = DateTime.Now;
                    await CoreEngine.Instance.RAMManager.FlushStandbyListAsync();
                    _lastStandbyFlush = DateTime.Now;

                    // 7. Throttle background processes
                    await CoreEngine.Instance.ProcessManager.ThrottleBackgroundProcessesAsync();
                    _lastProcessThrottle = DateTime.Now;

                    // 8. Set game process to High priority
                    try
                    {
                        await CoreEngine.Instance.NetworkOptimizer.PrioritizeGameProcessAsync(e.ProcessId);
                        _lastGamePrioritize = DateTime.Now;
                    }
                    catch { }
                }
                catch { }
            });
        }

        private void OnGameEnded(GameEndedEvent e)
        {
            if (!_enabled) return;
            _gamingActive = false;
            _gameProcessId = 0;

            _ = Task.Run(async () =>
            {
                try
                {
                    // Restore power plan
                    if (_powerPlanSet)
                    {
                        await CoreEngine.Instance.PowerPlan.RestoreOriginalPlanAsync();
                        _powerPlanSet = false;
                    }

                    // Restore Nagle (not needed outside gaming)
                    if (_nagleDisabled)
                    {
                        await CoreEngine.Instance.AdapterOptimizer.EnableNagleAsync();
                        _nagleDisabled = false;
                    }

                    // Restore visual effects
                    await CoreEngine.Instance.PowerPlan.RestoreVisualEffectsAsync();

                    // Restore process priorities
                    await CoreEngine.Instance.ProcessManager.RestoreProcessPrioritiesAsync();
                }
                catch { }
            });
        }
    }
}
