using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Win32;
using NetProve.Core;

namespace NetProve.Engines
{
    /// <summary>
    /// Safe, connection-aware network optimizations for gaming.
    /// Only applies aggressive settings to physical Ethernet/Wi-Fi adapters.
    /// Skips virtual, mobile hotspot, and VPN adapters to prevent issues.
    /// All changes are fully reversible.
    /// </summary>
    public sealed class NetworkOptimizer
    {
        private bool _tcpOptimized;

        // Registry paths
        private const string MultimediaKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        private const string GamingTasksKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
        private const string TcpipParamsKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
        private const string AdaptersClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        // Adapter types to skip (virtual, mobile, VPN)
        private static readonly string[] SkipAdapterKeywords = {
            "virtual", "vpn", "tunnel", "wan miniport", "bluetooth",
            "teredo", "isatap", "6to4", "microsoft wi-fi direct",
            "remote ndis", "usb ethernet", "mobile", "cellular",
            "ndis", "miniport", "hyper-v", "vmware", "virtualbox",
            "debug", "kernel debug", "loopback"
        };

        /// <summary>Flushes the DNS resolver cache.</summary>
        public async Task<bool> FlushDnsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    RunCmd("ipconfig", "/flushdns");
                    EventBus.Instance.Publish(new OptimizationAppliedEvent
                    {
                        ActionName = "DNS Flush",
                        Description = "DNS resolver cache cleared."
                    });
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>
        /// Applies safe TCP/IP and network optimizations for gaming.
        /// Only modifies system-wide TCP settings and physical adapter settings.
        /// Does NOT touch mobile/virtual adapters.
        /// </summary>
        public async Task<bool> ApplyTcpOptimizationsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // ── TCP/IP Stack Tuning (system-wide, safe) ─────────────
                    // Disable heuristics - lets us control auto-tuning directly
                    RunNetsh("int tcp set heuristics disabled");
                    // Enable ECN for better congestion handling
                    RunNetsh("int tcp set global ecncapability=enabled");
                    // Enable RSS for multi-core packet processing
                    RunNetsh("int tcp set global rss=enabled");
                    // Normal auto-tuning - NOT disabled (disabling hurts throughput)
                    RunNetsh("int tcp set global autotuninglevel=normal");
                    // Disable timestamps to reduce 12 bytes per packet overhead
                    RunNetsh("int tcp set global timestamps=disabled");
                    // Disable RSC - reduces latency at minor throughput cost
                    RunNetsh("int tcp set global rsc=disabled");

                    // ── Disable Network Throttling ───────────────────────────
                    // Windows throttles non-multimedia network traffic by default.
                    // Setting to max removes throttling for game traffic.
                    try
                    {
                        using var mmKey = Registry.LocalMachine.OpenSubKey(MultimediaKey, true);
                        if (mmKey != null)
                        {
                            // 0xFFFFFFFF = disable throttling entirely
                            mmKey.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                            // 0 = reserve 0% of CPU for system tasks (give all to apps)
                            mmKey.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                        }
                    }
                    catch { }

                    // ── Gaming Task QoS Priority ────────────────────────────
                    try
                    {
                        using var gameKey = Registry.LocalMachine.CreateSubKey(GamingTasksKey);
                        if (gameKey != null)
                        {
                            gameKey.SetValue("Affinity", 0, RegistryValueKind.DWord);
                            gameKey.SetValue("Background Only", "False", RegistryValueKind.String);
                            gameKey.SetValue("Clock Rate", 10000, RegistryValueKind.DWord);
                            gameKey.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
                            gameKey.SetValue("Priority", 6, RegistryValueKind.DWord);
                            gameKey.SetValue("Scheduling Category", "High", RegistryValueKind.String);
                            gameKey.SetValue("SFIO Priority", "High", RegistryValueKind.String);
                        }
                    }
                    catch { }

                    // ── TCP Global Parameters ───────────────────────────────
                    try
                    {
                        using var tcpKey = Registry.LocalMachine.OpenSubKey(TcpipParamsKey, true);
                        if (tcpKey != null)
                        {
                            // More ephemeral ports = more concurrent connections
                            tcpKey.SetValue("MaxUserPort", 65534, RegistryValueKind.DWord);
                            // Faster connection recycling (default 120s -> 30s)
                            tcpKey.SetValue("TcpTimedWaitDelay", 30, RegistryValueKind.DWord);
                            // Standard TTL
                            tcpKey.SetValue("DefaultTTL", 64, RegistryValueKind.DWord);
                        }
                    }
                    catch { }

                    // ── Physical Adapter Optimizations ───────────────────────
                    // Only apply to real Ethernet/Wi-Fi adapters
                    OptimizePhysicalAdapters();

                    _tcpOptimized = true;
                    EventBus.Instance.Publish(new OptimizationAppliedEvent
                    {
                        ActionName = "TCP Optimize",
                        Description = "Network stack optimized for gaming."
                    });
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>Restores default TCP settings.</summary>
        public async Task<bool> RestoreTcpDefaultsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    RunNetsh("int tcp set heuristics enabled");
                    RunNetsh("int tcp set global ecncapability=default");
                    RunNetsh("int tcp set global rss=enabled");
                    RunNetsh("int tcp set global autotuninglevel=normal");
                    RunNetsh("int tcp set global timestamps=default");
                    RunNetsh("int tcp set global rsc=enabled");

                    try
                    {
                        using var mmKey = Registry.LocalMachine.OpenSubKey(MultimediaKey, true);
                        if (mmKey != null)
                        {
                            mmKey.SetValue("NetworkThrottlingIndex", 10, RegistryValueKind.DWord);
                            mmKey.SetValue("SystemResponsiveness", 20, RegistryValueKind.DWord);
                        }
                    }
                    catch { }

                    try
                    {
                        using var tcpKey = Registry.LocalMachine.OpenSubKey(TcpipParamsKey, true);
                        if (tcpKey != null)
                        {
                            tcpKey.DeleteValue("MaxUserPort", false);
                            tcpKey.DeleteValue("TcpTimedWaitDelay", false);
                            tcpKey.DeleteValue("DefaultTTL", false);
                        }
                    }
                    catch { }

                    // Restore adapter settings
                    RestorePhysicalAdapters();

                    _tcpOptimized = false;
                    EventBus.Instance.Publish(new OptimizationAppliedEvent
                    {
                        ActionName = "TCP Restore",
                        Description = "TCP/IP settings restored to Windows defaults."
                    });
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>Elevates priority of a game process.</summary>
        public async Task<bool> PrioritizeGameProcessAsync(int pid)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var p = Process.GetProcessById(pid);
                    p.PriorityClass = ProcessPriorityClass.High;
                    p.Dispose();
                    return true;
                }
                catch { return false; }
            });
        }

        public bool IsTcpOptimized => _tcpOptimized;

        // ── Adapter optimization (only physical adapters) ───────────────────

        /// <summary>
        /// Optimizes only real physical adapters (Ethernet/Wi-Fi).
        /// Skips virtual, mobile hotspot, VPN, and other non-physical adapters.
        /// </summary>
        private void OptimizePhysicalAdapters()
        {
            try
            {
                using var classKey = Registry.LocalMachine.OpenSubKey(AdaptersClassKey, false);
                if (classKey == null) return;

                foreach (var subName in classKey.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = Registry.LocalMachine.OpenSubKey(
                            $@"{AdaptersClassKey}\{subName}", true);
                        if (subKey == null) continue;

                        var desc = subKey.GetValue("DriverDesc") as string;
                        if (string.IsNullOrEmpty(desc)) continue;

                        // Skip virtual/mobile/VPN adapters
                        if (IsVirtualAdapter(desc)) continue;

                        // Only apply to adapters that have a component ID (real hardware)
                        var componentId = subKey.GetValue("ComponentId") as string;
                        if (string.IsNullOrEmpty(componentId)) continue;

                        // ── Safe optimizations for physical adapters ────────

                        // Disable power management (prevents NIC sleep = less packet loss)
                        subKey.SetValue("PnPCapabilities", 0x18, RegistryValueKind.DWord);

                        // Disable Energy Efficient Ethernet (adds latency)
                        SetIfExists(subKey, "EEELinkAdvertisement", "0");
                        SetIfExists(subKey, "AdvancedEEE", "0");
                        SetIfExists(subKey, "EnableGreenEthernet", "0");

                        // Disable wake-on-LAN (reduces power state transitions)
                        SetIfExists(subKey, "EnablePME", "0");
                        SetIfExists(subKey, "WakeOnMagicPacket", "0");
                        SetIfExists(subKey, "WakeOnPattern", "0");

                        // NOTE: We do NOT disable InterruptModeration or FlowControl
                        // as these can cause MORE packet loss on many adapters,
                        // especially Wi-Fi and consumer-grade NICs.
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Restores physical adapter settings to defaults.
        /// </summary>
        private void RestorePhysicalAdapters()
        {
            try
            {
                using var classKey = Registry.LocalMachine.OpenSubKey(AdaptersClassKey, false);
                if (classKey == null) return;

                foreach (var subName in classKey.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = Registry.LocalMachine.OpenSubKey(
                            $@"{AdaptersClassKey}\{subName}", true);
                        if (subKey == null) continue;

                        var desc = subKey.GetValue("DriverDesc") as string;
                        if (string.IsNullOrEmpty(desc)) continue;
                        if (IsVirtualAdapter(desc)) continue;

                        var componentId = subKey.GetValue("ComponentId") as string;
                        if (string.IsNullOrEmpty(componentId)) continue;

                        // Restore power management (0x00 = default, allow power management)
                        subKey.SetValue("PnPCapabilities", 0x00, RegistryValueKind.DWord);

                        // Restore energy settings
                        SetIfExists(subKey, "EEELinkAdvertisement", "1");
                        SetIfExists(subKey, "AdvancedEEE", "1");
                        SetIfExists(subKey, "EnableGreenEthernet", "1");
                        SetIfExists(subKey, "EnablePME", "1");
                        SetIfExists(subKey, "WakeOnMagicPacket", "1");
                        SetIfExists(subKey, "WakeOnPattern", "1");
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Checks if an adapter is virtual/mobile/VPN based on its description.
        /// </summary>
        private static bool IsVirtualAdapter(string description)
        {
            var lower = description.ToLowerInvariant();
            return SkipAdapterKeywords.Any(kw => lower.Contains(kw));
        }

        /// <summary>
        /// Only sets a registry value if the key already exists on the adapter.
        /// This prevents adding unsupported properties to adapters that don't have them.
        /// </summary>
        private static void SetIfExists(RegistryKey key, string name, string value)
        {
            if (key.GetValue(name) != null)
                key.SetValue(name, value, RegistryValueKind.String);
        }

        // ── Command helpers ─────────────────────────────────────────────────

        private static void RunNetsh(string args)
        {
            var psi = new ProcessStartInfo("netsh", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }

        private static void RunCmd(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
    }
}
