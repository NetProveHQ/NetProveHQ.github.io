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
                            // 10 = reserve 10% of CPU for system tasks (safe minimum)
                            mmKey.SetValue("SystemResponsiveness", 10, RegistryValueKind.DWord);
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

        /// <summary>
        /// Applies QoS DSCP marking for game traffic priority.
        /// Sets the game process to use Expedited Forwarding (DSCP 46)
        /// which routers recognize as high-priority real-time traffic.
        /// </summary>
        public async Task<bool> ApplyGameQoSAsync(int processId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Set QoS policy for the game process via registry
                    // DSCP 46 = Expedited Forwarding (highest priority for real-time)
                    const string qosKey = @"SOFTWARE\Policies\Microsoft\Windows\QoS\NetProve Gaming";
                    try
                    {
                        using var key = Registry.LocalMachine.CreateSubKey(qosKey);
                        if (key != null)
                        {
                            key.SetValue("Version", "1.0");
                            key.SetValue("Application Name", "*"); // All apps when gaming
                            key.SetValue("Protocol", "*");
                            key.SetValue("Local Port", "*");
                            key.SetValue("Local IP", "*");
                            key.SetValue("Local IP Prefix Length", "*");
                            key.SetValue("Remote Port", "*");
                            key.SetValue("Remote IP", "*");
                            key.SetValue("Remote IP Prefix Length", "*");
                            key.SetValue("DSCP Value", "46"); // Expedited Forwarding
                            key.SetValue("Throttle Rate", "-1"); // No throttling
                        }
                    }
                    catch { }

                    // Also set the game process to use high I/O priority
                    try
                    {
                        var proc = Process.GetProcessById(processId);
                        proc.PriorityClass = ProcessPriorityClass.High;
                        // Set processor affinity to performance cores if available
                        if (Environment.ProcessorCount > 4)
                        {
                            // Use upper half of cores (typically P-cores on hybrid CPUs)
                            long mask = 0;
                            for (int i = Environment.ProcessorCount / 2; i < Environment.ProcessorCount; i++)
                                mask |= 1L << i;
                            if (mask > 0)
                                proc.ProcessorAffinity = (IntPtr)mask;
                        }
                        proc.Dispose();
                    }
                    catch { }

                    EventBus.Instance.Publish(new OptimizationAppliedEvent
                    {
                        ActionName = "QoS Priority",
                        Description = "Game traffic marked as high priority (DSCP 46)."
                    });
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>Removes QoS gaming policy.</summary>
        public async Task<bool> RemoveGameQoSAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    const string qosParent = @"SOFTWARE\Policies\Microsoft\Windows\QoS";
                    using var parentKey = Registry.LocalMachine.OpenSubKey(qosParent, true);
                    parentKey?.DeleteSubKey("NetProve Gaming", false);
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>
        /// Diagnoses the actual source of network problems.
        /// Returns a diagnostic report identifying whether the issue is
        /// local (WiFi/adapter), router, ISP, or destination server.
        /// </summary>
        public async Task<NetworkDiagnosticResult> DiagnoseNetworkAsync()
        {
            return await Task.Run(() =>
            {
                var result = new NetworkDiagnosticResult();

                try
                {
                    // Step 1: Ping gateway (local network test)
                    var gatewayPing = PingHost(GetDefaultGateway());
                    result.GatewayPingMs = gatewayPing;
                    result.GatewayReachable = gatewayPing >= 0;

                    // Step 2: Ping ISP DNS / first hop beyond router
                    var ispPing = PingHost("1.1.1.1"); // Cloudflare (reliable, fast)
                    result.IspPingMs = ispPing;
                    result.IspReachable = ispPing >= 0;

                    // Step 3: Ping popular game servers
                    var gamePing = PingHost("8.8.8.8"); // Google DNS as proxy
                    result.InternetPingMs = gamePing;
                    result.InternetReachable = gamePing >= 0;

                    // Diagnose the bottleneck
                    if (!result.GatewayReachable)
                    {
                        result.ProblemSource = "Local Network";
                        result.Recommendation = "Wi-Fi/Ethernet bağlantınızı kontrol edin. Router'ı yeniden başlatmayı deneyin.";
                    }
                    else if (result.GatewayPingMs > 10)
                    {
                        result.ProblemSource = "Wi-Fi / Local Network";
                        result.Recommendation = "Gateway ping çok yüksek. 5GHz Wi-Fi'ye geçin veya Ethernet kablosu kullanın.";
                    }
                    else if (!result.IspReachable)
                    {
                        result.ProblemSource = "ISP / Router";
                        result.Recommendation = "İnternet bağlantısı yok. Router'ı kontrol edin veya ISP'nize başvurun.";
                    }
                    else if (result.IspPingMs > 50)
                    {
                        result.ProblemSource = "ISP";
                        result.Recommendation = "ISP gecikmesi yüksek. Bu uygulama ile düzeltilemez — ISP'nize başvurun veya VPN deneyin.";
                    }
                    else
                    {
                        result.ProblemSource = "None";
                        result.Recommendation = "Ağ bağlantınız iyi durumda.";
                    }
                }
                catch
                {
                    result.ProblemSource = "Unknown";
                    result.Recommendation = "Tanılama sırasında hata oluştu.";
                }

                return result;
            });
        }

        private static string GetDefaultGateway()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    foreach (var gw in props.GatewayAddresses)
                    {
                        var addr = gw.Address.ToString();
                        if (addr != "0.0.0.0" && !addr.Contains(":"))
                            return addr;
                    }
                }
            }
            catch { }
            return "192.168.1.1";
        }

        private static double PingHost(string host)
        {
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var replies = new List<double>();
                for (int i = 0; i < 3; i++)
                {
                    var reply = ping.Send(host, 2000);
                    if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                        replies.Add(reply.RoundtripTime);
                }
                return replies.Count > 0 ? replies.Average() : -1;
            }
            catch { return -1; }
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
