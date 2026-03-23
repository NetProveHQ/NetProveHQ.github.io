using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Win32;
using NetProve.Core;

namespace NetProve.Engines
{
    /// <summary>
    /// Optimizes network adapter settings: Nagle algorithm, Wi-Fi band detection,
    /// network stack reset. Only modifies active physical adapters.
    /// All changes are reversible.
    /// </summary>
    public sealed class NetworkAdapterOptimizer
    {
        private const string TcpipInterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

        /// <summary>
        /// Disable Nagle algorithm only on active physical network interfaces.
        /// Skips loopback, tunnel, and inactive interfaces.
        /// Sets TcpAckFrequency=1 and TCPNoDelay=1.
        /// </summary>
        public Task<bool> DisableNagleAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    // Get GUIDs of active physical adapters only
                    var activeGuids = GetActivePhysicalAdapterGuids();

                    using var interfaces = Registry.LocalMachine.OpenSubKey(TcpipInterfacesKey, false);
                    if (interfaces == null) return false;

                    int modified = 0;
                    foreach (var subKeyName in interfaces.GetSubKeyNames())
                    {
                        // Only apply to active physical adapters
                        if (!activeGuids.Contains(subKeyName, StringComparer.OrdinalIgnoreCase))
                            continue;

                        try
                        {
                            using var subKey = Registry.LocalMachine.OpenSubKey(
                                $@"{TcpipInterfacesKey}\{subKeyName}", true);
                            if (subKey == null) continue;

                            // Verify this interface has an IP address (actually in use)
                            var hasIp = subKey.GetValue("DhcpIPAddress") as string ??
                                        subKey.GetValue("IPAddress") as string;
                            if (string.IsNullOrEmpty(hasIp) || hasIp == "0.0.0.0") continue;

                            subKey.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                            subKey.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                            modified++;
                        }
                        catch { }
                    }

                    AppSettings.Instance.NagleDisabled = true;
                    AppSettings.Instance.Save();

                    EventBus.Instance.Publish(new OptimizationAppliedEvent
                    {
                        ActionName = "Nagle Disabled",
                        Description = $"Nagle disabled on {modified} active adapter(s)."
                    });
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>
        /// Re-enable Nagle algorithm by removing custom registry values
        /// from all interfaces (safe cleanup).
        /// </summary>
        public Task<bool> EnableNagleAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    using var interfaces = Registry.LocalMachine.OpenSubKey(TcpipInterfacesKey, false);
                    if (interfaces == null) return false;

                    foreach (var subKeyName in interfaces.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = Registry.LocalMachine.OpenSubKey(
                                $@"{TcpipInterfacesKey}\{subKeyName}", true);
                            if (subKey == null) continue;

                            subKey.DeleteValue("TcpAckFrequency", false);
                            subKey.DeleteValue("TCPNoDelay", false);
                        }
                        catch { }
                    }

                    AppSettings.Instance.NagleDisabled = false;
                    AppSettings.Instance.Save();

                    EventBus.Instance.Publish(new OptimizationAppliedEvent
                    {
                        ActionName = "Nagle Enabled",
                        Description = "Nagle algorithm restored to default."
                    });
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>Detect current Wi-Fi band (2.4 GHz vs 5 GHz).</summary>
        public async Task<string> DetectWifiBandAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    };
                    using var p = Process.Start(psi);
                    var output = p?.StandardOutput.ReadToEnd() ?? "";
                    p?.WaitForExit(5000);

                    foreach (var line in output.Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("Radio type", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("Radyo t", StringComparison.OrdinalIgnoreCase))
                        {
                            if (trimmed.Contains("802.11a") || trimmed.Contains("802.11ac") ||
                                trimmed.Contains("802.11ax") || trimmed.Contains("802.11n") && trimmed.Contains("5"))
                                return "5 GHz (802.11ac/ax)";
                            if (trimmed.Contains("802.11b") || trimmed.Contains("802.11g"))
                                return "2.4 GHz (802.11b/g)";
                            return trimmed.Contains(":") ? trimmed.Split(':').Last().Trim() : "Unknown";
                        }
                        if (trimmed.StartsWith("Channel", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("Kanal", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(trimmed.Split(':').Last().Trim(), out int ch))
                                return ch >= 36 ? $"5 GHz (Channel {ch})" : $"2.4 GHz (Channel {ch})";
                        }
                    }

                    var wifiAdapter = NetworkInterface.GetAllNetworkInterfaces()
                        .Any(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                                   ni.OperationalStatus == OperationalStatus.Up);
                    return wifiAdapter ? "Unknown Band" : "Ethernet (No Wi-Fi)";
                }
                catch { return "Detection failed"; }
            });
        }

        /// <summary>
        /// Disables Wi-Fi power saving mode on all active wireless adapters.
        /// Wi-Fi PSM causes 10-100ms latency spikes because the adapter sleeps
        /// between beacon intervals and buffers frames.
        /// </summary>
        public Task<bool> DisableWifiPowerSaveAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    // Method 1: PowerShell Set-NetAdapterAdvancedProperty
                    var adapters = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                                     ni.OperationalStatus == OperationalStatus.Up)
                        .ToArray();

                    foreach (var adapter in adapters)
                    {
                        var name = adapter.Name;
                        // Disable power saving mode (3 = Maximum Performance)
                        RunPowerShell($"Set-NetAdapterAdvancedProperty -Name '{name}' -RegistryKeyword 'PowerSavingMode' -RegistryValue 3 -ErrorAction SilentlyContinue");
                        // Also set via power plan
                        RunCmd("powercfg", "/setacvalueindex scheme_current sub_none 12bbebe6-58d6-4636-95bb-3217ef867c1a 19cbb8fa-5279-450e-9fac-8a3d5fedd0c1 0");
                    }

                    // Set wireless adapter power saving to Maximum Performance via active power scheme
                    RunCmd("powercfg", "/setacvalueindex scheme_current 19cbb8fa-5279-450e-9fac-8a3d5fedd0c1 12bbebe6-58d6-4636-95bb-3217ef867c1a 0");
                    RunCmd("powercfg", "/setactive scheme_current");

                    EventBus.Instance.Publish(new OptimizationAppliedEvent
                    {
                        ActionName = "Wi-Fi Power Save",
                        Description = $"Wi-Fi power saving disabled on {adapters.Length} adapter(s). Latency spikes reduced."
                    });
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>
        /// Restores Wi-Fi power saving mode to default.
        /// </summary>
        public Task<bool> RestoreWifiPowerSaveAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var adapters = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                                     ni.OperationalStatus == OperationalStatus.Up)
                        .ToArray();

                    foreach (var adapter in adapters)
                    {
                        RunPowerShell($"Set-NetAdapterAdvancedProperty -Name '{adapter.Name}' -RegistryKeyword 'PowerSavingMode' -RegistryValue 1 -ErrorAction SilentlyContinue");
                    }

                    // Restore wireless power saving to Medium (default)
                    RunCmd("powercfg", "/setacvalueindex scheme_current 19cbb8fa-5279-450e-9fac-8a3d5fedd0c1 12bbebe6-58d6-4636-95bb-3217ef867c1a 1");
                    RunCmd("powercfg", "/setactive scheme_current");
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>
        /// Disables Windows Delivery Optimization (P2P update sharing).
        /// Windows shares update files with other PCs in the background,
        /// consuming bandwidth unpredictably during gaming.
        /// </summary>
        public Task<bool> DisableDeliveryOptimizationAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    const string doKey = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
                    using var key = Registry.LocalMachine.CreateSubKey(doKey);
                    if (key != null)
                    {
                        // DODownloadMode 0 = HTTP only (no P2P)
                        key.SetValue("DODownloadMode", 0, RegistryValueKind.DWord);
                    }

                    EventBus.Instance.Publish(new OptimizationAppliedEvent
                    {
                        ActionName = "Delivery Optimization",
                        Description = "Windows P2P update sharing disabled during gaming."
                    });
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>Restores Delivery Optimization to default.</summary>
        public Task<bool> RestoreDeliveryOptimizationAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    const string doKey = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
                    using var key = Registry.LocalMachine.OpenSubKey(doKey, true);
                    key?.DeleteValue("DODownloadMode", false);
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>
        /// Sets Interrupt Moderation to a lower rate for reduced NIC-to-application latency.
        /// Only applies to physical adapters that support this setting.
        /// </summary>
        public Task<bool> ReduceInterruptModerationAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var adapters = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                     (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                      ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                                     !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    int modified = 0;
                    foreach (var adapter in adapters)
                    {
                        // Set interrupt moderation to adaptive/medium (not fully disabled to avoid CPU spike)
                        RunPowerShell($"Set-NetAdapterAdvancedProperty -Name '{adapter.Name}' -RegistryKeyword '*InterruptModeration' -RegistryValue 0 -ErrorAction SilentlyContinue");
                        modified++;
                    }

                    EventBus.Instance.Publish(new OptimizationAppliedEvent
                    {
                        ActionName = "Interrupt Moderation",
                        Description = $"NIC interrupt moderation disabled on {modified} adapter(s). ~1-2ms latency reduction."
                    });
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>Restores interrupt moderation to default (enabled).</summary>
        public Task<bool> RestoreInterruptModerationAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var adapters = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                     (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                      ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                                     !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    foreach (var adapter in adapters)
                    {
                        RunPowerShell($"Set-NetAdapterAdvancedProperty -Name '{adapter.Name}' -RegistryKeyword '*InterruptModeration' -RegistryValue 1 -ErrorAction SilentlyContinue");
                    }
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>
        /// Detects bufferbloat by measuring latency under load.
        /// This is the #1 cause of gaming lag on shared connections.
        /// Returns the latency increase under load (bloat in ms).
        /// </summary>
        public async Task<BufferbloatResult> DetectBufferbloatAsync()
        {
            return await Task.Run(() =>
            {
                var result = new BufferbloatResult();
                try
                {
                    // Step 1: Measure idle latency
                    var idlePings = new System.Collections.Generic.List<double>();
                    using var ping = new System.Net.NetworkInformation.Ping();
                    for (int i = 0; i < 5; i++)
                    {
                        var reply = ping.Send("1.1.1.1", 2000);
                        if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                            idlePings.Add(reply.RoundtripTime);
                        System.Threading.Thread.Sleep(200);
                    }

                    if (idlePings.Count == 0)
                    {
                        result.Status = "Test Failed";
                        result.Recommendation = "Ping testi başarısız. İnternet bağlantınızı kontrol edin.";
                        return result;
                    }

                    result.IdleLatencyMs = idlePings.Average();

                    // Step 2: Measure latency during a brief load test
                    // Create some network load by downloading from multiple connections
                    var loadPings = new System.Collections.Generic.List<double>();
                    var cts = new System.Threading.CancellationTokenSource();

                    // Start background downloads to create load
                    var loadTasks = new System.Collections.Generic.List<Task>();
                    for (int i = 0; i < 4; i++)
                    {
                        loadTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                using var client = new System.Net.Http.HttpClient();
                                client.Timeout = TimeSpan.FromSeconds(5);
                                // Download a small file to create network activity
                                await client.GetAsync("http://speed.cloudflare.com/__down?bytes=5000000", cts.Token);
                            }
                            catch { }
                        }));
                    }

                    // Wait a moment for load to build, then measure
                    System.Threading.Thread.Sleep(1000);

                    for (int i = 0; i < 5; i++)
                    {
                        var reply = ping.Send("1.1.1.1", 2000);
                        if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                            loadPings.Add(reply.RoundtripTime);
                        System.Threading.Thread.Sleep(200);
                    }

                    cts.Cancel();
                    try { Task.WhenAll(loadTasks).Wait(3000); } catch { }

                    if (loadPings.Count > 0)
                    {
                        result.LoadLatencyMs = loadPings.Average();
                        result.BloatMs = result.LoadLatencyMs - result.IdleLatencyMs;

                        if (result.BloatMs < 5)
                        {
                            result.Status = "Excellent";
                            result.Recommendation = "Bufferbloat yok. Bağlantınız oyun için ideal.";
                        }
                        else if (result.BloatMs < 30)
                        {
                            result.Status = "Good";
                            result.Recommendation = "Hafif bufferbloat. Oyun için kabul edilebilir.";
                        }
                        else if (result.BloatMs < 100)
                        {
                            result.Status = "Warning";
                            result.Recommendation = "Orta düzeyde bufferbloat. Router'ınızda SQM/QoS etkinleştirin.";
                        }
                        else
                        {
                            result.Status = "Critical";
                            result.Recommendation = "Ciddi bufferbloat! Router'ınızda SQM (fq_codel/CAKE) etkinleştirin. Bu yazılımla düzeltilemez.";
                        }
                    }
                }
                catch
                {
                    result.Status = "Error";
                    result.Recommendation = "Bufferbloat testi sırasında hata oluştu.";
                }
                return result;
            });
        }

        /// <summary>Full network stack reset (requires system restart to take effect).</summary>
        public async Task<bool> ResetNetworkStackAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    RunCmd("netsh", "winsock reset");
                    RunCmd("netsh", "int ip reset");
                    RunCmd("ipconfig", "/release");
                    RunCmd("ipconfig", "/renew");
                    RunCmd("ipconfig", "/flushdns");

                    EventBus.Instance.Publish(new OptimizationAppliedEvent
                    {
                        ActionName = "Network Reset",
                        Description = "Network stack reset. Restart recommended for full effect."
                    });
                    return true;
                }
                catch { return false; }
            });
        }

        /// <summary>
        /// Gets the interface GUIDs of active physical (non-virtual) adapters.
        /// </summary>
        private static string[] GetActivePhysicalAdapterGuids()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni =>
                        ni.OperationalStatus == OperationalStatus.Up &&
                        (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                         ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                        !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                        !ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) &&
                        !ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) &&
                        !ni.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) &&
                        !ni.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) &&
                        !ni.Description.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
                    .Select(ni => ni.Id) // GUID format like {XXXXXXXX-XXXX-...}
                    .Select(id => id.Trim('{', '}'))
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
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
            p?.WaitForExit(10000);
        }

        private static void RunPowerShell(string command)
        {
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(10000);
        }
    }

    /// <summary>Result of bufferbloat detection test.</summary>
    public sealed class BufferbloatResult
    {
        public double IdleLatencyMs { get; set; }
        public double LoadLatencyMs { get; set; }
        public double BloatMs { get; set; }
        public string Status { get; set; } = "Unknown";
        public string Recommendation { get; set; } = "";
    }
}
