using System;

namespace NetProve.Models
{
    public sealed class NetworkDevice
    {
        public string IpAddress { get; init; } = "";
        public string MacAddress { get; init; } = "";
        public string HostName { get; init; } = "";
        public string DeviceType { get; init; } = "Unknown";
        public string Vendor { get; init; } = "";
        public bool IsCurrentDevice { get; init; }
        public bool IsGateway { get; init; }
        public DateTime LastSeen { get; init; } = DateTime.Now;

        // Ping response time (ms) - indicates if device is active
        public double PingMs { get; init; } = -1;

        // Estimated bandwidth usage description
        public string BandwidthUsage { get; init; } = "";

        // Tag label for display (e.g. "This Device", "Gateway")
        public string Tag { get; init; } = "";
    }
}
