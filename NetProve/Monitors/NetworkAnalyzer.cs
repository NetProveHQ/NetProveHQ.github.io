using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using NetProve.Core;
using NetProve.Models;

namespace NetProve.Monitors
{
    /// <summary>
    /// Continuously measures ping, jitter, and packet-loss to a configurable
    /// target. Also tracks NIC throughput using PerformanceCounters.
    /// </summary>
    public sealed class NetworkAnalyzer : IDisposable
    {
        private const int PingBatchSize = 3;       // pings per measurement cycle (reduced for lower CPU)
        private const int PingTimeoutMs = 1500;

        private PerformanceCounter? _dlCounter;
        private PerformanceCounter? _ulCounter;

        private CancellationTokenSource? _cts;
        private Task? _task;

        public NetworkMetrics? Latest { get; private set; }

        // Rolling history for jitter/loss calculation
        private readonly Queue<double> _pingHistory = new();
        private const int HistorySize = 30;

        public void Start(CancellationToken externalToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            InitNicCounters();
            _task = Task.Run(() => MonitorLoop(_cts.Token), _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _task?.Wait(3000); } catch { }
        }

        private void InitNicCounters()
        {
            try
            {
                // Find the first active NIC category instance
                var cat = new PerformanceCounterCategory("Network Interface");
                var instances = cat.GetInstanceNames();
                // Prefer non-loopback instances
                var nic = instances.FirstOrDefault(n =>
                    !n.Contains("Loopback", StringComparison.OrdinalIgnoreCase) &&
                    !n.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                    !n.Contains("Pseudo", StringComparison.OrdinalIgnoreCase))
                    ?? instances.FirstOrDefault();

                if (nic != null)
                {
                    _dlCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", nic);
                    _ulCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", nic);
                    _dlCounter.NextValue();
                    _ulCounter.NextValue();
                }
            }
            catch { }
        }

        private async Task MonitorLoop(CancellationToken ct)
        {
            var interval = AppSettings.Instance.NetworkPollIntervalMs;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var metrics = await MeasureAsync(ct);
                    Latest = metrics;
                    EventBus.Instance.Publish(new NetworkMetricsUpdatedEvent { Metrics = metrics });
                    await Task.Delay(interval, ct);
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(interval, ct).ConfigureAwait(false); }
            }
        }

        private async Task<NetworkMetrics> MeasureAsync(CancellationToken ct)
        {
            var target = AppSettings.Instance.PingTarget;
            var pings = new List<double>();
            int lost = 0;

            for (int i = 0; i < PingBatchSize; i++)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(target, PingTimeoutMs);
                    if (reply.Status == IPStatus.Success)
                        pings.Add(reply.RoundtripTime);
                    else
                        lost++;
                }
                catch { lost++; }
                if (i < PingBatchSize - 1)
                    await Task.Delay(80, ct);
            }

            double avgPing = pings.Count > 0 ? pings.Average() : 0;
            double jitter = pings.Count > 1 ? CalculateJitter(pings) : 0;
            double packetLoss = (double)lost / PingBatchSize * 100.0;

            // Track history
            _pingHistory.Enqueue(avgPing);
            while (_pingHistory.Count > HistorySize) _pingHistory.Dequeue();

            float dl = 0f, ul = 0f;
            try
            {
                dl = _dlCounter?.NextValue() ?? 0f;
                ul = _ulCounter?.NextValue() ?? 0f;
            }
            catch { }

            return new NetworkMetrics
            {
                PingMs = avgPing,
                JitterMs = jitter,
                PacketLossPercent = packetLoss,
                DownloadBytesPerSec = dl,
                UploadBytesPerSec = ul
            };
        }

        private static double CalculateJitter(List<double> pings)
        {
            // RFC 3550 jitter: mean absolute deviation between consecutive samples
            double sum = 0;
            for (int i = 1; i < pings.Count; i++)
                sum += Math.Abs(pings[i] - pings[i - 1]);
            return sum / (pings.Count - 1);
        }

        /// <summary>Returns ping rolling history for chart display.</summary>
        public double[] GetPingHistory() => _pingHistory.ToArray();

        public void Dispose()
        {
            Stop();
            _dlCounter?.Dispose();
            _ulCounter?.Dispose();
            _cts?.Dispose();
        }
    }
}
