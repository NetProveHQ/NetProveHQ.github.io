using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NetProve.Core;
using NetProve.Models;

namespace NetProve.Monitors
{
    /// <summary>
    /// Polls CPU, RAM, and Disk metrics at a configurable interval.
    /// Uses Windows Performance Counters and GlobalMemoryStatusEx for accuracy.
    /// Idle overhead: &lt;0.3% CPU.
    /// </summary>
    public sealed class SystemMonitor : IDisposable
    {
        // ── Win32 memory API ─────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // ── Performance counters ─────────────────────────────────────────────
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _diskReadCounter;
        private PerformanceCounter? _diskWriteCounter;
        private PerformanceCounter? _diskTimeCounter;

        private CancellationTokenSource? _cts;
        private Task? _task;

        public SystemMetrics? Latest { get; private set; }
        public string CpuName { get; private set; } = "";

        public void Start(CancellationToken externalToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            InitCounters();
            _task = Task.Run(() => MonitorLoop(_cts.Token), _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _task?.Wait(2000); } catch { }
        }

        private void InitCounters()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // first read is always 0 – prime it
            }
            catch { _cpuCounter = null; }

            try
            {
                _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
                _diskTimeCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
                _diskReadCounter.NextValue();
                _diskWriteCounter.NextValue();
                _diskTimeCounter.NextValue();
            }
            catch
            {
                _diskReadCounter = null;
                _diskWriteCounter = null;
                _diskTimeCounter = null;
            }

            // WMI query is slow — run on background thread
            Task.Run(() => CpuName = GetCpuName());
        }

        private async Task MonitorLoop(CancellationToken ct)
        {
            var interval = AppSettings.Instance.SystemPollIntervalMs;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, ct);
                    var metrics = Sample();
                    Latest = metrics;
                    EventBus.Instance.Publish(new SystemMetricsUpdatedEvent { Metrics = metrics });
                }
                catch (OperationCanceledException) { break; }
                catch { /* swallow transient errors */ }
            }
        }

        private SystemMetrics Sample()
        {
            float cpu = 0f;
            try { cpu = Math.Clamp(_cpuCounter?.NextValue() ?? 0f, 0f, 100f); } catch { }

            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            GlobalMemoryStatusEx(ref mem);

            float diskRead = 0f, diskWrite = 0f, diskTime = 0f;
            try
            {
                diskRead = _diskReadCounter?.NextValue() ?? 0f;
                diskWrite = _diskWriteCounter?.NextValue() ?? 0f;
                diskTime = Math.Clamp(_diskTimeCounter?.NextValue() ?? 0f, 0f, 100f);
            }
            catch { }

            return new SystemMetrics
            {
                CpuUsagePercent = cpu,
                CpuCores = Environment.ProcessorCount,
                CpuName = CpuName,
                TotalRamBytes = (long)mem.ullTotalPhys,
                AvailableRamBytes = (long)mem.ullAvailPhys,
                DiskReadBytesPerSec = diskRead,
                DiskWriteBytesPerSec = diskWrite,
                DiskActivityPercent = diskTime
            };
        }

        private static string GetCpuName()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT Name FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                    return obj["Name"]?.ToString()?.Trim() ?? "";
            }
            catch { }
            return "Unknown CPU";
        }

        public void Dispose()
        {
            Stop();
            _cpuCounter?.Dispose();
            _diskReadCounter?.Dispose();
            _diskWriteCounter?.Dispose();
            _diskTimeCounter?.Dispose();
            _cts?.Dispose();
        }
    }
}
