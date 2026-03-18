using System;

namespace NetProve.Models
{
    public sealed class NetworkMetrics
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;

        public double PingMs { get; init; }
        public double JitterMs { get; init; }
        public double PacketLossPercent { get; init; }
        public float DownloadBytesPerSec { get; init; }
        public float UploadBytesPerSec { get; init; }

        public float DownloadMbps => DownloadBytesPerSec * 8f / 1_000_000f;
        public float UploadMbps => UploadBytesPerSec * 8f / 1_000_000f;

        public bool IsStable => PingMs < 60 && JitterMs < 10 && PacketLossPercent < 0.5;
        public NetworkQuality Quality =>
            PacketLossPercent >= 3 || PingMs >= 150 ? NetworkQuality.Poor :
            PacketLossPercent >= 0.5 || PingMs >= 80 || JitterMs >= 25 ? NetworkQuality.Fair :
            PacketLossPercent >= 0.05 || PingMs >= 30 || JitterMs >= 8 ? NetworkQuality.Good :
            NetworkQuality.Excellent;
    }

    public enum NetworkQuality { Excellent, Good, Fair, Poor }

    public sealed class SpeedTestResult
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public double DownloadMbps { get; init; }
        public double UploadMbps { get; init; }
        public double PingMs { get; init; }
        public string Server { get; init; } = "";
        public bool Success { get; init; }
        public string Error { get; init; } = "";
    }
}
