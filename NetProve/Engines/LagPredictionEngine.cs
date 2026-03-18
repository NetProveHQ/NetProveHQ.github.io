using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetProve.Core;
using NetProve.Localization;
using NetProve.Models;

namespace NetProve.Engines
{
    /// <summary>
    /// Early-warning system that detects deteriorating trends and predicts
    /// potential lag before it manifests.  Uses a sliding window of recent
    /// metric samples and computes trend slopes.
    /// </summary>
    public sealed class LagPredictionEngine : IDisposable
    {
        private const int WindowSize = 15; // number of samples kept

        private readonly Queue<SystemMetrics> _sysHistory = new();
        private readonly Queue<NetworkMetrics> _netHistory = new();

        private CancellationTokenSource? _cts;
        private Task? _task;

        public LagPrediction? Latest { get; private set; }

        public void Start(CancellationToken externalToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);

            // Subscribe to the metric streams
            EventBus.Instance.Subscribe<SystemMetricsUpdatedEvent>(OnSystem);
            EventBus.Instance.Subscribe<NetworkMetricsUpdatedEvent>(OnNetwork);

            _task = Task.Run(() => PredictionLoop(_cts.Token), _cts.Token);
        }

        public void Stop()
        {
            EventBus.Instance.Unsubscribe<SystemMetricsUpdatedEvent>(OnSystem);
            EventBus.Instance.Unsubscribe<NetworkMetricsUpdatedEvent>(OnNetwork);
            _cts?.Cancel();
            try { _task?.Wait(2000); } catch { }
        }

        private void OnSystem(SystemMetricsUpdatedEvent e)
        {
            lock (_sysHistory)
            {
                _sysHistory.Enqueue(e.Metrics);
                while (_sysHistory.Count > WindowSize) _sysHistory.Dequeue();
            }
        }

        private void OnNetwork(NetworkMetricsUpdatedEvent e)
        {
            lock (_netHistory)
            {
                _netHistory.Enqueue(e.Metrics);
                while (_netHistory.Count > WindowSize) _netHistory.Dequeue();
            }
        }

        private async Task PredictionLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(10000, ct); // Check every 10 seconds instead of 5
                    var pred = Predict();
                    Latest = pred;

                    if (AppSettings.Instance.ShowLagWarnings)
                    {
                        if (pred.PredictedLag)
                        {
                            var loc = LocalizationManager.Instance;
                            EventBus.Instance.Publish(new LagWarningEvent
                            {
                                Cause = pred.Reason,
                                Detail = string.Format(loc["LagPredictedIn"],
                                    pred.EstimatedSecondsUntilLag, pred.Confidence.ToString("F0")),
                                Severity = pred.PredictedSeverity
                            });
                        }
                        else
                        {
                            // Metrics stabilized — dismiss the warning
                            EventBus.Instance.Publish(new LagWarningDismissEvent());
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        private LagPrediction Predict()
        {
            SystemMetrics[] sysSnap;
            NetworkMetrics[] netSnap;

            lock (_sysHistory) sysSnap = _sysHistory.ToArray();
            lock (_netHistory) netSnap = _netHistory.ToArray();

            if (sysSnap.Length < 5 || netSnap.Length < 5)
                return new LagPrediction { PredictedLag = false };

            var loc = LocalizationManager.Instance;
            var reasons = new List<string>();
            float maxConfidence = 0;

            // ── CPU trend ─────────────────────────────────────────────────
            var cpuValues = sysSnap.Select(s => (double)s.CpuUsagePercent).ToArray();
            double cpuSlope = ComputeSlope(cpuValues);
            double latestCpu = cpuValues.Last();
            if (cpuSlope > 3.0 && latestCpu > 60)
            {
                double secsToOverload = latestCpu < 90 ? (90 - latestCpu) / cpuSlope : 5;
                reasons.Add(string.Format(loc["CpuRising"], $"{cpuSlope:+0.0}%", $"{latestCpu:F0}"));
                maxConfidence = Math.Max(maxConfidence, Math.Min(90f, (float)(cpuSlope * 10 + latestCpu / 2)));
            }

            // ── RAM trend ─────────────────────────────────────────────────
            var ramValues = sysSnap.Select(s => (double)s.RamUsagePercent).ToArray();
            double ramSlope = ComputeSlope(ramValues);
            double latestRam = ramValues.Last();
            if (ramSlope > 1.5 && latestRam > 75)
            {
                reasons.Add(string.Format(loc["RamClimbing"], $"{latestRam:F0}", $"{ramSlope:F1}"));
                maxConfidence = Math.Max(maxConfidence, Math.Min(85f, (float)(ramSlope * 15 + latestRam / 3)));
            }

            // ── Ping trend ────────────────────────────────────────────────
            var pingValues = netSnap.Select(n => n.PingMs).ToArray();
            double pingSlope = ComputeSlope(pingValues);
            double latestPing = pingValues.Last();
            if (pingSlope > 2.0 && latestPing > 50)
            {
                reasons.Add(string.Format(loc["LatencyIncreasing"], $"{latestPing:F0}", $"{pingSlope:F1}"));
                maxConfidence = Math.Max(maxConfidence, Math.Min(95f, (float)(pingSlope * 8 + latestPing / 3)));
            }

            // ── Jitter spikes ─────────────────────────────────────────────
            var jitterValues = netSnap.Select(n => n.JitterMs).ToArray();
            double jitterMax = jitterValues.Max();
            double jitterAvg = jitterValues.Average();
            if (jitterMax > 30 && jitterMax > jitterAvg * 2.5)
            {
                reasons.Add(string.Format(loc["JitterSpikes"], $"{jitterMax:F0}", $"{jitterAvg:F0}"));
                maxConfidence = Math.Max(maxConfidence, Math.Min(80f, (float)jitterMax));
            }

            // ── Packet loss trend ─────────────────────────────────────────
            var plValues = netSnap.Select(n => n.PacketLossPercent).ToArray();
            double plRecent = plValues.TakeLast(3).Average();
            if (plRecent >= 1.0)
            {
                reasons.Add(string.Format(loc["PacketLossDetected"], $"{plRecent:F1}"));
                maxConfidence = Math.Max(maxConfidence, Math.Min(90f, (float)plRecent * 20f));
            }

            bool willLag = maxConfidence >= 40f;
            int eta = willLag ? Math.Max(3, (int)(60 / Math.Max(1, maxConfidence / 20))) : 0;

            LagSeverity severity = maxConfidence >= 75 ? LagSeverity.High :
                                   maxConfidence >= 50 ? LagSeverity.Medium :
                                   maxConfidence >= 30 ? LagSeverity.Low :
                                   LagSeverity.None;

            return new LagPrediction
            {
                PredictedLag = willLag,
                PredictedSeverity = severity,
                EstimatedSecondsUntilLag = eta,
                Reason = reasons.Count > 0 ? string.Join("; ", reasons) : loc["AllMetricsStable"],
                Confidence = maxConfidence
            };
        }

        /// <summary>Least-squares slope of a data series (units/index).</summary>
        private static double ComputeSlope(double[] values)
        {
            if (values.Length < 2) return 0;
            int n = values.Length;
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            for (int i = 0; i < n; i++)
            {
                sumX += i; sumY += values[i];
                sumXY += i * values[i];
                sumX2 += i * i;
            }
            double denom = n * sumX2 - sumX * sumX;
            return denom == 0 ? 0 : (n * sumXY - sumX * sumY) / denom;
        }

        public void Dispose() { Stop(); _cts?.Dispose(); }
    }
}
