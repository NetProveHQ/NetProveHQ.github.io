using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NetProve.Models;

namespace NetProve.Engines
{
    /// <summary>
    /// Scans the local network for connected devices using ARP table + ping sweep.
    /// Optimized for speed: single ping round, parallel name resolution, short timeouts.
    /// </summary>
    public sealed class NetworkScanner
    {
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref int macAddrLen);

        private volatile bool _scanning;
        public bool IsScanning => _scanning;

        // Cache previous scan results so devices don't disappear between scans
        private readonly Dictionary<string, CachedDevice> _deviceCache = new();
        private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(10);

        private sealed class CachedDevice
        {
            public string Ip { get; set; } = "";
            public string Mac { get; set; } = "";
            public string HostName { get; set; } = "";
            public string Vendor { get; set; } = "";
            public string DeviceType { get; set; } = "";
            public bool IsRandomMac { get; set; }
            public DateTime LastSeen { get; set; }
            public double LastPingMs { get; set; } = -1;
        }

        public async Task<List<NetworkDevice>> ScanAsync(CancellationToken ct = default)
        {
            if (_scanning) return new List<NetworkDevice>();
            _scanning = true;

            try
            {
                var devices = new List<NetworkDevice>();
                var (localIp, gatewayIp, subnetMask) = GetLocalNetworkInfo();

                if (string.IsNullOrEmpty(localIp)) return devices;

                var ips = GetSubnetIps(localIp, subnetMask, gatewayIp);

                // Phase 1: Single fast ping sweep + SendARP in parallel
                var pingResults = new Dictionary<string, double>();
                var semaphore = new SemaphoreSlim(64); // limit concurrent pings

                var tasks = ips.Select(async ip =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        var ms = await PingHostAsync(ip, ct);
                        if (ms >= 0)
                        {
                            lock (pingResults) { pingResults[ip] = ms; }
                        }
                        else
                        {
                            // Try SendARP for devices that block ICMP
                            try
                            {
                                var ipAddr = IPAddress.Parse(ip);
                                var ipUint = BitConverter.ToUInt32(ipAddr.GetAddressBytes(), 0);
                                var mac = new byte[6];
                                int macLen = 6;
                                if (SendARP(ipUint, 0, mac, ref macLen) == 0)
                                    lock (pingResults) { pingResults.TryAdd(ip, -0.5); }
                            }
                            catch { }
                        }
                    }
                    finally { semaphore.Release(); }
                }).ToArray();

                await Task.WhenAll(tasks);

                // Phase 2: Read ARP table (captures devices found by ping/SendARP)
                var arpEntries = GetArpTable();
                var now = DateTime.Now;

                // Phase 3: Resolve names in parallel for all discovered devices
                var nameResolveTasks = new Dictionary<string, Task<string>>();
                foreach (var entry in arpEntries)
                {
                    ct.ThrowIfCancellationRequested();
                    // Only resolve if not cached or cached name is empty
                    if (_deviceCache.TryGetValue(entry.Mac, out var cached) &&
                        !string.IsNullOrEmpty(cached.HostName) && cached.HostName != "—")
                    {
                        nameResolveTasks[entry.Mac] = Task.FromResult(cached.HostName);
                    }
                    else
                    {
                        nameResolveTasks[entry.Mac] = ResolveDeviceNameAsync(entry.Ip);
                    }
                }

                // Wait for all name resolutions (with overall timeout)
                try
                {
                    await Task.WhenAll(nameResolveTasks.Values).WaitAsync(TimeSpan.FromSeconds(5), ct);
                }
                catch { /* timeout, cancellation, or individual resolution failures — all safe to ignore */ }

                // Phase 4: Build device list
                foreach (var entry in arpEntries)
                {
                    ct.ThrowIfCancellationRequested();

                    bool isLocal = entry.Ip == localIp;
                    bool isGw = entry.Ip == gatewayIp;
                    double pingMs = pingResults.TryGetValue(entry.Ip, out var pm) ? pm : -1;

                    string hostName = "";
                    if (nameResolveTasks.TryGetValue(entry.Mac, out var nameTask) && nameTask.IsCompletedSuccessfully)
                        hostName = nameTask.Result;

                    string vendor = GetVendorFromMac(entry.Mac);
                    bool isRandomMac = IsRandomizedMac(entry.Mac);
                    string deviceType = GuessDeviceType(vendor, hostName, isGw, isRandomMac);
                    string tag = isGw ? "🌐" : isLocal ? "⭐" : "";
                    bool isOnline = pingMs >= 0 || pingMs == -0.5;

                    var prevCached = _deviceCache.GetValueOrDefault(entry.Mac);

                    // Update cache
                    _deviceCache[entry.Mac] = new CachedDevice
                    {
                        Ip = entry.Ip,
                        Mac = entry.Mac,
                        HostName = hostName,
                        Vendor = vendor,
                        DeviceType = deviceType,
                        IsRandomMac = isRandomMac,
                        LastSeen = now,
                        LastPingMs = pingMs >= 0 ? pingMs : (prevCached?.LastPingMs ?? -1),
                    };

                    string displayPing = pingMs > 0 ? $"{Math.Round(pingMs, 1)}ms" :
                                         isOnline ? "<1ms" : "";

                    devices.Add(new NetworkDevice
                    {
                        IpAddress = entry.Ip,
                        MacAddress = entry.Mac,
                        HostName = string.IsNullOrEmpty(hostName) ? "—" : hostName,
                        DeviceType = deviceType,
                        Vendor = string.IsNullOrEmpty(vendor) ? (isRandomMac ? "Private MAC" : "—") : vendor,
                        IsCurrentDevice = isLocal,
                        IsGateway = isGw,
                        PingMs = pingMs >= 0 ? Math.Round(pingMs, 1) : (prevCached?.LastPingMs ?? -1),
                        BandwidthUsage = isLocal ? EstimateLocalBandwidthUsage() :
                                         isOnline ? $"🟢 Online ({displayPing})" : "⚫ Offline",
                        Tag = tag,
                    });
                }

                // Phase 5: Add cached devices that weren't found in this scan
                foreach (var kv in _deviceCache)
                {
                    if (now - kv.Value.LastSeen > CacheExpiry) continue;
                    if (devices.Any(d => d.MacAddress == kv.Key)) continue;
                    // Also skip if last seen is from this scan (already added above)
                    if (kv.Value.LastSeen == now) continue;

                    var c = kv.Value;
                    devices.Add(new NetworkDevice
                    {
                        IpAddress = !string.IsNullOrEmpty(c.Ip) ? c.Ip : "—",
                        MacAddress = kv.Key,
                        HostName = string.IsNullOrEmpty(c.HostName) ? "—" : c.HostName,
                        DeviceType = c.DeviceType,
                        Vendor = string.IsNullOrEmpty(c.Vendor) ? "—" : c.Vendor,
                        PingMs = -1,
                        BandwidthUsage = "💤 Recently seen",
                        Tag = "",
                    });
                }

                // Phase 6: Add current device if not in ARP
                if (!devices.Any(d => d.IsCurrentDevice))
                {
                    var localMac = GetLocalMac();
                    devices.Insert(0, new NetworkDevice
                    {
                        IpAddress = localIp,
                        MacAddress = localMac,
                        HostName = Environment.MachineName,
                        DeviceType = "💻 PC",
                        Vendor = GetVendorFromMac(localMac),
                        IsCurrentDevice = true,
                        PingMs = 0,
                        BandwidthUsage = EstimateLocalBandwidthUsage(),
                        Tag = "⭐",
                    });
                }

                // Remove expired cache entries
                var expired = _deviceCache.Where(kv => now - kv.Value.LastSeen > CacheExpiry)
                    .Select(kv => kv.Key).ToList();
                foreach (var key in expired) _deviceCache.Remove(key);

                return devices
                    .OrderByDescending(d => d.IsGateway)
                    .ThenByDescending(d => d.IsCurrentDevice)
                    .ThenByDescending(d => d.PingMs >= 0) // online first
                    .ThenBy(d => d.IpAddress)
                    .ToList();
            }
            finally
            {
                _scanning = false;
            }
        }

        private static bool IsRandomizedMac(string mac)
        {
            if (string.IsNullOrEmpty(mac) || mac.Length < 2) return false;
            try
            {
                var firstByte = Convert.ToByte(mac[..2], 16);
                return (firstByte & 0x02) != 0;
            }
            catch { return false; }
        }

        private static string EstimateLocalBandwidthUsage()
        {
            try
            {
                var iface = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                          (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                           ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                                          !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase));
                if (iface == null) return "—";

                var stats1 = iface.GetIPv4Statistics();
                var sent1 = stats1.BytesSent;
                var recv1 = stats1.BytesReceived;
                Thread.Sleep(300); // shorter sample
                var stats2 = iface.GetIPv4Statistics();
                var sent2 = stats2.BytesSent;
                var recv2 = stats2.BytesReceived;

                var dlRate = (recv2 - recv1) * (1000.0 / 300); // bytes/sec
                var ulRate = (sent2 - sent1) * (1000.0 / 300);
                var totalMbps = (dlRate + ulRate) * 8.0 / 1_000_000.0;

                if (totalMbps < 0.1) return "🟢 ~0 Mbps";
                if (totalMbps < 1) return $"🟢 {totalMbps:F1} Mbps";
                if (totalMbps < 10) return $"🟡 {totalMbps:F1} Mbps";
                return $"🔴 {totalMbps:F1} Mbps";
            }
            catch { return "—"; }
        }

        private static (string localIp, string gatewayIp, string subnetMask) GetLocalNetworkInfo()
        {
            try
            {
                var iface = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                  ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                                 !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (iface == null) return ("", "", "");

                var props = iface.GetIPProperties();
                var ipv4 = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                var gw = props.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

                return (
                    ipv4?.Address.ToString() ?? "",
                    gw?.Address.ToString() ?? "",
                    ipv4?.IPv4Mask.ToString() ?? "255.255.255.0"
                );
            }
            catch { return ("", "", ""); }
        }

        private static string GetLocalMac()
        {
            try
            {
                var iface = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                          (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                           ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));
                if (iface == null) return "";
                var bytes = iface.GetPhysicalAddress().GetAddressBytes();
                return string.Join(":", bytes.Select(b => b.ToString("X2")));
            }
            catch { return ""; }
        }

        private static List<string> GetSubnetIps(string localIp, string subnetMask, string gatewayIp)
        {
            var ips = new List<string>();
            try
            {
                var ipBytes = IPAddress.Parse(localIp).GetAddressBytes();
                var maskBytes = IPAddress.Parse(subnetMask).GetAddressBytes();

                var networkBytes = new byte[4];
                var broadcastBytes = new byte[4];
                for (int i = 0; i < 4; i++)
                {
                    networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
                    broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                }

                var start = BitConverter.ToUInt32(networkBytes.Reverse().ToArray(), 0) + 1;
                var end = BitConverter.ToUInt32(broadcastBytes.Reverse().ToArray(), 0);

                var count = Math.Min(end - start, 254u);
                for (uint i = 0; i < count; i++)
                {
                    var addrBytes = BitConverter.GetBytes(start + i).Reverse().ToArray();
                    ips.Add(new IPAddress(addrBytes).ToString());
                }
            }
            catch { }
            return ips;
        }

        private static async Task<double> PingHostAsync(string ip, CancellationToken ct)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 200); // 200ms timeout (was 300)
                return reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
            }
            catch { return -1; }
        }

        private static List<(string Ip, string Mac)> GetArpTable()
        {
            var entries = new List<(string, string)>();
            try
            {
                var psi = new ProcessStartInfo("arp", "-a")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return entries;

                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);

                foreach (var line in output.Split('\n'))
                {
                    var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 && IPAddress.TryParse(parts[0], out _))
                    {
                        var mac = parts[1].Replace('-', ':').ToUpperInvariant();
                        if (mac == "FF:FF:FF:FF:FF:FF") continue;
                        if (mac.StartsWith("01:00:5E")) continue;
                        entries.Add((parts[0], mac));
                    }
                }
            }
            catch { }
            return entries;
        }

        /// <summary>
        /// Tries DNS and NetBIOS in parallel with short timeouts.
        /// </summary>
        private static async Task<string> ResolveDeviceNameAsync(string ip)
        {
            try
            {
                var dnsTask = Task.Run(() => ResolveDns(ip));
                var netbiosTask = Task.Run(() => ResolveNetBIOS(ip));

                // Wait max 2 seconds for name resolution
                try
                {
                    await Task.WhenAll(dnsTask, netbiosTask).WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch { }

                var dns = dnsTask.IsCompletedSuccessfully ? dnsTask.Result : "";
                var netbios = netbiosTask.IsCompletedSuccessfully ? netbiosTask.Result : "";

                if (!string.IsNullOrEmpty(netbios) && netbios != ip)
                    return netbios;
                if (!string.IsNullOrEmpty(dns) && dns != ip &&
                    !dns.StartsWith("192.") && !dns.StartsWith("10.") && !dns.StartsWith("172."))
                    return dns;
            }
            catch { }
            return "";
        }

        private static string ResolveDns(string ip)
        {
            try
            {
                var entry = Dns.GetHostEntry(ip);
                return entry.HostName ?? "";
            }
            catch { return ""; }
        }

        private static string ResolveNetBIOS(string ip)
        {
            try
            {
                var psi = new ProcessStartInfo("nbtstat", $"-A {ip}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return "";

                if (!proc.WaitForExit(1500)) // 1.5s timeout (was 3s)
                {
                    try { proc.Kill(); } catch { }
                    return "";
                }

                var output = proc.StandardOutput.ReadToEnd();
                foreach (var line in output.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Contains("<00>") && trimmed.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = trimmed.Split('<')[0].Trim();
                        if (!string.IsNullOrEmpty(name) && name.Length > 1 &&
                            !name.StartsWith("__", StringComparison.Ordinal))
                            return name;
                    }
                }
            }
            catch { }
            return "";
        }

        private static string GuessDeviceType(string vendor, string hostName, bool isGateway, bool isRandomMac)
        {
            if (isGateway) return "🌐 Router/Gateway";

            var v = vendor.ToLowerInvariant();
            var h = hostName.ToLowerInvariant();

            if (isRandomMac)
            {
                if (h.Contains("ipad") || h.Contains("tablet")) return "📱 Tablet";
                return "📱 Mobile Device";
            }

            if (v.Contains("apple") || h.Contains("iphone") || h.Contains("ipad"))
                return h.Contains("ipad") ? "📱 Tablet (Apple)" : "📱 Phone (Apple)";
            if (v.Contains("samsung") || h.Contains("galaxy") || h.Contains("samsung"))
                return "📱 Phone (Samsung)";
            if (v.Contains("xiaomi") || v.Contains("redmi") || h.Contains("xiaomi") || h.Contains("redmi"))
                return "📱 Phone (Xiaomi)";
            if (v.Contains("huawei") || h.Contains("huawei"))
                return "📱 Phone (Huawei)";
            if (v.Contains("oppo") || h.Contains("oppo"))
                return "📱 Phone (OPPO)";
            if (v.Contains("oneplus") || h.Contains("oneplus"))
                return "📱 Phone (OnePlus)";
            if (v.Contains("google") && (h.Contains("pixel") || v.Contains("pixel")))
                return "📱 Phone (Google)";

            if (v.Contains("lg electr") || h.Contains("lgtv") || h.Contains("lg-tv"))
                return "📺 Smart TV (LG)";
            if (h.Contains("samsung") && (h.Contains("tv") || h.Contains("tizen")))
                return "📺 Smart TV (Samsung)";
            if (v.Contains("roku") || h.Contains("roku"))
                return "📺 Roku";
            if (v.Contains("amazon") || h.Contains("fire") || h.Contains("kindle"))
                return "📺 Fire TV / Alexa";
            if (h.Contains("chromecast") || (v.Contains("google") && !h.Contains("pixel")))
                return "📺 Chromecast";
            if (v.Contains("sony") && (h.Contains("bravia") || h.Contains("tv")))
                return "📺 Smart TV (Sony)";

            if (v.Contains("sony") || h.Contains("playstation") || h.Contains("ps5") || h.Contains("ps4"))
                return "🎮 PlayStation";
            if (v.Contains("microsoft") && (h.Contains("xbox") || v.Contains("xbox")))
                return "🎮 Xbox";
            if (v.Contains("nintendo") || h.Contains("switch"))
                return "🎮 Nintendo Switch";

            if (v.Contains("espressif") || v.Contains("tuya") || h.Contains("tasmota"))
                return "💡 Smart Home Device";
            if (h.Contains("echo") || h.Contains("alexa"))
                return "🔊 Smart Speaker";
            if (h.Contains("nest") || h.Contains("thermostat"))
                return "🏠 Smart Home (Nest)";

            if (h.Contains("desktop") || h.Contains("-pc") || h.Contains("workstation"))
                return "💻 PC";
            if (h.Contains("laptop") || h.Contains("notebook"))
                return "💻 Laptop";
            if (v.Contains("intel") || v.Contains("realtek") || v.Contains("qualcomm"))
                return "💻 Computer";

            if (v.Contains("tp-link") || v.Contains("d-link") || v.Contains("netgear") ||
                v.Contains("asus") || v.Contains("linksys") || v.Contains("zyxel") ||
                v.Contains("mikrotik") || v.Contains("ubiquiti"))
                return "📡 Network Device";

            if (v.Contains("hp") && (h.Contains("print") || v.Contains("print")) ||
                v.Contains("epson") || v.Contains("canon") || v.Contains("brother") ||
                h.Contains("printer"))
                return "🖨️ Printer";

            if (v.Contains("mobile") || v.Contains("wireless") || h.Contains("android"))
                return "📱 Mobile Device";

            if (v.Contains("luxshare") || v.Contains("foxconn") || v.Contains("pegatron") ||
                v.Contains("quanta") || v.Contains("wistron"))
                return "📱 Apple Device";

            if (v.Contains("zte") || v.Contains("huawei") || v.Contains("sagemcom") ||
                v.Contains("arris") || v.Contains("technicolor"))
                return "📡 Modem/Router";

            if (v.Contains("vivo") || v.Contains("motorola"))
                return "📱 Phone";

            if (v.Contains("mediatek"))
                return "📱 Mobile Device";

            if (v.Contains("dell") || v.Contains("lenovo") || v.Contains("hp") || v.Contains("acer"))
                return "💻 Computer";

            if (v.Contains("vmware") || v.Contains("virtual"))
                return "🖥️ Virtual Machine";

            if (v.Contains("raspberry"))
                return "🔧 Raspberry Pi";

            if (v.Contains("fitbit"))
                return "⌚ Wearable";

            if (!string.IsNullOrEmpty(v) && v != "—")
                return "📟 Device";

            return "❓ Unknown";
        }

        private static string GetVendorFromMac(string mac)
        {
            if (string.IsNullOrEmpty(mac) || mac.Length < 8) return "";
            var oui = mac[..8].ToUpperInvariant();

            if (_ouiDatabase.TryGetValue(oui, out var vendor))
                return vendor;

            return "";
        }

        private static readonly Dictionary<string, string> _ouiDatabase =
            new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Apple ──
            ["00:1C:B3"] = "Apple", ["3C:15:C2"] = "Apple", ["AC:DE:48"] = "Apple",
            ["A4:83:E7"] = "Apple", ["F0:18:98"] = "Apple", ["14:7D:DA"] = "Apple",
            ["78:7B:8A"] = "Apple", ["D0:03:4B"] = "Apple", ["E0:B5:2D"] = "Apple",
            ["A8:66:7F"] = "Apple", ["B8:09:8A"] = "Apple", ["DC:A9:04"] = "Apple",
            ["38:F9:D3"] = "Apple", ["F4:5C:89"] = "Apple", ["20:78:F0"] = "Apple",
            ["6C:96:CF"] = "Apple", ["F0:D1:A9"] = "Apple", ["A0:99:9B"] = "Apple",
            ["C8:69:CD"] = "Apple", ["F8:E9:4E"] = "Apple", ["00:CD:FE"] = "Apple",
            ["5C:F7:E6"] = "Apple", ["E4:E4:AB"] = "Apple", ["A4:D1:8C"] = "Apple",
            ["BC:52:B7"] = "Apple", ["AC:BC:32"] = "Apple", ["70:56:81"] = "Apple",
            ["04:F1:28"] = "Apple", ["34:C0:59"] = "Apple", ["CC:08:E0"] = "Apple",
            ["08:66:98"] = "Apple", ["28:6A:BA"] = "Apple", ["40:98:AD"] = "Apple",
            ["3C:18:A0"] = "Apple (Luxshare)",

            // ── Samsung ──
            ["00:21:19"] = "Samsung", ["34:23:BA"] = "Samsung", ["8C:F5:A3"] = "Samsung",
            ["B4:79:A7"] = "Samsung", ["C4:73:1E"] = "Samsung", ["E4:7C:F9"] = "Samsung",
            ["50:01:BB"] = "Samsung", ["A0:CC:2B"] = "Samsung", ["F8:04:2E"] = "Samsung",
            ["00:26:37"] = "Samsung", ["78:47:1D"] = "Samsung", ["00:1E:E2"] = "Samsung",
            ["D0:22:BE"] = "Samsung", ["C0:BD:D1"] = "Samsung", ["14:49:E0"] = "Samsung",
            ["EC:1F:72"] = "Samsung", ["94:35:0A"] = "Samsung", ["CC:3A:61"] = "Samsung",
            ["10:D5:42"] = "Samsung", ["A8:81:95"] = "Samsung", ["2C:AE:2B"] = "Samsung",
            ["F4:7B:09"] = "Samsung", ["84:38:35"] = "Samsung", ["00:F4:6F"] = "Samsung",
            ["5C:3C:27"] = "Samsung", ["8C:77:12"] = "Samsung", ["4C:BC:48"] = "Samsung",

            // ── Xiaomi / Redmi ──
            ["28:6C:07"] = "Xiaomi", ["64:CC:2E"] = "Xiaomi", ["8C:DE:F9"] = "Xiaomi",
            ["9C:99:A0"] = "Xiaomi", ["78:11:DC"] = "Xiaomi", ["50:64:2B"] = "Xiaomi",
            ["7C:1C:68"] = "Xiaomi", ["AC:C1:EE"] = "Xiaomi", ["34:CE:00"] = "Xiaomi",
            ["74:23:44"] = "Xiaomi", ["F8:A4:5F"] = "Xiaomi", ["58:44:98"] = "Xiaomi",
            ["FC:64:BA"] = "Xiaomi", ["0C:1D:AF"] = "Xiaomi", ["4C:63:71"] = "Xiaomi",

            // ── Huawei / Honor ──
            ["00:E0:FC"] = "Huawei", ["48:46:FB"] = "Huawei", ["20:A6:80"] = "Huawei",
            ["CC:A2:23"] = "Huawei", ["70:8A:09"] = "Huawei", ["D4:6E:5C"] = "Huawei",
            ["E0:19:1D"] = "Huawei", ["88:28:B3"] = "Huawei", ["5C:C3:07"] = "Huawei",
            ["C8:D1:5E"] = "Huawei", ["04:BD:70"] = "Huawei", ["24:09:95"] = "Huawei",
            ["00:9A:CD"] = "Huawei", ["FC:48:EF"] = "Huawei", ["AC:E2:15"] = "Huawei",

            // ── OPPO / OnePlus / Realme ──
            ["2C:5B:E1"] = "OPPO", ["A4:3B:FA"] = "OPPO", ["CC:2D:83"] = "OPPO",
            ["E8:61:7E"] = "OPPO", ["94:D9:B3"] = "OnePlus", ["C0:EE:FB"] = "OnePlus",

            // ── Intel ──
            ["00:1E:64"] = "Intel", ["3C:97:0E"] = "Intel", ["68:05:CA"] = "Intel",
            ["A4:C4:94"] = "Intel", ["B4:96:91"] = "Intel", ["48:51:B7"] = "Intel",
            ["00:1B:21"] = "Intel", ["F8:94:C2"] = "Intel", ["80:86:F2"] = "Intel",
            ["A0:36:9F"] = "Intel", ["B4:69:21"] = "Intel", ["8C:8D:28"] = "Intel",
            ["E8:B1:FC"] = "Intel", ["94:65:9C"] = "Intel", ["C8:5B:76"] = "Intel",
            ["9C:2E:A1"] = "Intel", ["DC:71:96"] = "Intel", ["3C:F0:11"] = "Intel",

            // ── Realtek ──
            ["00:E0:4C"] = "Realtek", ["52:54:00"] = "Realtek", ["00:04:25"] = "Realtek",

            // ── Qualcomm / Atheros ──
            ["00:03:7F"] = "Qualcomm", ["00:0A:F5"] = "Qualcomm",

            // ── TP-Link ──
            ["50:C7:BF"] = "TP-Link", ["C0:25:E9"] = "TP-Link", ["14:EB:B6"] = "TP-Link",
            ["60:32:B1"] = "TP-Link", ["98:DA:C4"] = "TP-Link", ["B0:BE:76"] = "TP-Link",
            ["EC:08:6B"] = "TP-Link", ["C0:06:C3"] = "TP-Link", ["30:B5:C2"] = "TP-Link",
            ["AC:84:C6"] = "TP-Link", ["F4:F2:6D"] = "TP-Link", ["54:AF:97"] = "TP-Link",
            ["74:DA:88"] = "TP-Link", ["A0:F3:C1"] = "TP-Link",

            // ── D-Link ──
            ["00:05:5D"] = "D-Link", ["00:17:9A"] = "D-Link", ["1C:7E:E5"] = "D-Link",
            ["28:10:7B"] = "D-Link", ["84:C9:B2"] = "D-Link", ["B8:A3:86"] = "D-Link",

            // ── ASUS ──
            ["00:1A:92"] = "ASUS", ["2C:56:DC"] = "ASUS", ["50:46:5D"] = "ASUS",
            ["90:E6:BA"] = "ASUS", ["AC:9E:17"] = "ASUS", ["04:D9:F5"] = "ASUS",
            ["00:1D:60"] = "ASUS", ["D8:50:E6"] = "ASUS", ["74:D0:2B"] = "ASUS",
            ["1C:87:2C"] = "ASUS", ["B0:6E:BF"] = "ASUS", ["AC:F2:3C"] = "ASUS",
            ["04:92:26"] = "ASUS", ["F4:6D:04"] = "ASUS", ["E0:3F:49"] = "ASUS",

            // ── Netgear ──
            ["00:09:5B"] = "Netgear", ["00:14:6C"] = "Netgear", ["20:E5:2A"] = "Netgear",
            ["6C:B0:CE"] = "Netgear", ["C4:04:15"] = "Netgear", ["E4:F4:C6"] = "Netgear",

            // ── Linksys ──
            ["00:1A:70"] = "Linksys", ["00:18:F8"] = "Linksys", ["C0:56:27"] = "Linksys",

            // ── Sony ──
            ["00:04:1F"] = "Sony", ["00:13:A9"] = "Sony", ["00:1D:0D"] = "Sony",
            ["FC:0F:E6"] = "Sony", ["00:24:8D"] = "Sony", ["BC:60:A7"] = "Sony",
            ["78:C8:81"] = "Sony", ["2C:CC:44"] = "Sony", ["A8:E3:EE"] = "Sony",

            // ── Microsoft ──
            ["00:50:F2"] = "Microsoft", ["28:18:78"] = "Microsoft", ["7C:ED:8D"] = "Microsoft",
            ["DC:B4:C4"] = "Microsoft", ["C8:3F:26"] = "Microsoft", ["58:82:A8"] = "Microsoft",

            // ── Nintendo ──
            ["00:09:BF"] = "Nintendo", ["00:17:AB"] = "Nintendo", ["00:1F:32"] = "Nintendo",
            ["00:22:AA"] = "Nintendo", ["58:BD:A3"] = "Nintendo", ["00:1C:BE"] = "Nintendo",
            ["98:B6:E9"] = "Nintendo", ["7C:BB:8A"] = "Nintendo", ["E8:4E:CE"] = "Nintendo",

            // ── Google ──
            ["3C:5A:B4"] = "Google", ["54:60:09"] = "Google", ["F4:F5:D8"] = "Google",
            ["A4:77:33"] = "Google", ["48:D6:D5"] = "Google", ["F8:8F:CA"] = "Google",
            ["30:FD:38"] = "Google",

            // ── LG Electronics ──
            ["00:1E:75"] = "LG", ["10:68:3F"] = "LG", ["64:99:68"] = "LG",
            ["CC:FA:00"] = "LG", ["00:22:A9"] = "LG", ["A8:16:B2"] = "LG",
            ["B4:B5:B6"] = "LG", ["58:A2:B5"] = "LG",

            // ── Amazon ──
            ["F0:D2:F1"] = "Amazon", ["74:75:48"] = "Amazon", ["FC:65:DE"] = "Amazon",
            ["A0:02:DC"] = "Amazon", ["44:65:0D"] = "Amazon", ["84:D6:D0"] = "Amazon",

            // ── HP ──
            ["00:1A:4B"] = "HP", ["00:17:A4"] = "HP", ["10:60:4B"] = "HP",
            ["30:E1:71"] = "HP", ["3C:D9:2B"] = "HP", ["94:57:A5"] = "HP",

            // ── Dell ──
            ["00:14:22"] = "Dell", ["00:1E:4F"] = "Dell", ["B8:CA:3A"] = "Dell",
            ["F8:BC:12"] = "Dell", ["18:03:73"] = "Dell", ["E4:F0:04"] = "Dell",

            // ── Lenovo ──
            ["00:06:1B"] = "Lenovo", ["28:D2:44"] = "Lenovo", ["EC:B1:D7"] = "Lenovo",
            ["50:7B:9D"] = "Lenovo", ["94:E6:F7"] = "Lenovo", ["00:0D:F0"] = "Lenovo",

            // ── Roku ──
            ["DC:3A:5E"] = "Roku", ["B8:3E:59"] = "Roku", ["CC:6D:A0"] = "Roku",

            // ── Epson ──
            ["00:00:48"] = "Epson", ["00:1B:A9"] = "Epson", ["AC:18:26"] = "Epson",

            // ── Canon ──
            ["00:1E:8F"] = "Canon", ["18:0C:AC"] = "Canon", ["C4:36:55"] = "Canon",

            // ── Brother ──
            ["00:1B:A9"] = "Brother", ["00:80:77"] = "Brother",

            // ── ZTE ──
            ["00:19:C6"] = "ZTE", ["64:13:6C"] = "ZTE", ["E0:D3:62"] = "ZTE",
            ["00:1E:73"] = "ZTE", ["00:25:12"] = "ZTE", ["48:28:2F"] = "ZTE",

            // ── Espressif (IoT) ──
            ["24:0A:C4"] = "Espressif", ["30:AE:A4"] = "Espressif",
            ["AC:67:B2"] = "Espressif", ["A4:CF:12"] = "Espressif",

            // ── Tuya (Smart Home) ──
            ["D8:1F:12"] = "Tuya", ["10:D5:61"] = "Tuya",

            // ── Raspberry Pi ──
            ["B8:27:EB"] = "Raspberry Pi", ["DC:A6:32"] = "Raspberry Pi",
            ["E4:5F:01"] = "Raspberry Pi",

            // ── Ubiquiti ──
            ["00:27:22"] = "Ubiquiti", ["04:18:D6"] = "Ubiquiti",
            ["24:5A:4C"] = "Ubiquiti", ["44:D9:E7"] = "Ubiquiti",

            // ── MikroTik ──
            ["00:0C:42"] = "MikroTik", ["4C:5E:0C"] = "MikroTik",
            ["D4:01:C3"] = "MikroTik", ["E4:8D:8C"] = "MikroTik",

            // ── Motorola ──
            ["00:04:56"] = "Motorola", ["00:0C:E5"] = "Motorola",
            ["68:C4:4D"] = "Motorola", ["E8:B4:C8"] = "Motorola",

            // ── VMware ──
            ["00:50:56"] = "VMware", ["00:0C:29"] = "VMware",

            // ── Aruba / HPE ──
            ["00:0B:86"] = "Aruba", ["24:DE:C6"] = "Aruba",

            // ── Cisco ──
            ["00:1B:D4"] = "Cisco", ["00:1E:F7"] = "Cisco", ["00:26:0B"] = "Cisco",
            ["58:AC:78"] = "Cisco", ["3C:08:F6"] = "Cisco",

            // ── Zyxel ──
            ["00:13:49"] = "Zyxel", ["B0:B2:DC"] = "Zyxel", ["E4:F3:E8"] = "Zyxel",

            // ── Vivo ──
            ["C4:A5:DF"] = "Vivo", ["3C:91:57"] = "Vivo",

            // ── Fitbit ──
            ["D0:03:DF"] = "Fitbit",

            // ── MediaTek ──
            ["00:0C:E7"] = "MediaTek",
        };
    }
}
