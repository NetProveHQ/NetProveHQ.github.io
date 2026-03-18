using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NetProve.Core;
using NetProve.Localization;
using NetProve.Models;

namespace NetProve.Engines
{
    /// <summary>
    /// Generates post-session performance reports from collected GameSession data.
    /// Persists reports to disk for historical review.
    /// </summary>
    public sealed class PerformanceReportEngine
    {
        private static readonly string ReportsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetProve", "Reports");

        private readonly List<PerformanceReport> _history = new();
        private readonly LocalizationManager _loc = LocalizationManager.Instance;

        public PerformanceReportEngine()
        {
            Directory.CreateDirectory(ReportsDir);
            LoadHistory();

            EventBus.Instance.Subscribe<GameEndedEvent>(_ =>
            {
                var session = CoreEngine.Instance.GameDetector.CurrentSession;
                if (session != null) GenerateReport(session);
            });
        }

        public PerformanceReport GenerateReport(GameSession session)
        {
            var ping = session.PingSamples;
            var jitter = session.JitterSamples;
            var pl = session.PacketLossSamples;
            var cpu = session.CpuSamples;
            var ram = session.RamSamples;

            double avgPing = ping.Count > 0 ? ping.Average() : 0;
            double minPing = ping.Count > 0 ? ping.Min() : 0;
            double maxPing = ping.Count > 0 ? ping.Max() : 0;
            double avgJitter = jitter.Count > 0 ? jitter.Average() : 0;
            double avgPl = pl.Count > 0 ? pl.Average() : 0;
            float avgCpu = cpu.Count > 0 ? cpu.Average() : 0;
            float avgRam = ram.Count > 0 ? ram.Average() : 0;
            float peakCpu = cpu.Count > 0 ? cpu.Max() : 0;
            float peakRam = ram.Count > 0 ? ram.Max() : 0;

            // Determine quality rating with realistic gaming thresholds
            string rating, emoji;
            double score = 100;

            // Packet loss (very impactful for gaming)
            if (avgPl >= 3) score -= 40;
            else if (avgPl >= 1) score -= 25;
            else if (avgPl >= 0.1) score -= 10;

            // Ping (gaming-sensitive thresholds)
            if (avgPing >= 120) score -= 35;
            else if (avgPing >= 70) score -= 20;
            else if (avgPing >= 40) score -= 10;
            else if (avgPing >= 20) score -= 3;

            // Jitter
            if (avgJitter >= 25) score -= 25;
            else if (avgJitter >= 12) score -= 15;
            else if (avgJitter >= 5) score -= 5;

            // Lag spikes
            if (session.LagSpikeCount > 10) score -= 20;
            else if (session.LagSpikeCount > 3) score -= 10;
            else if (session.LagSpikeCount > 0) score -= 5;

            // CPU/RAM pressure during session
            if (peakCpu >= 95) score -= 5;
            if (peakRam >= 95) score -= 5;

            if (score >= 90) { rating = _loc["RatingExcellent"]; emoji = "★★★★★"; }
            else if (score >= 75) { rating = _loc["RatingGood"]; emoji = "★★★★☆"; }
            else if (score >= 55) { rating = _loc["RatingFair"]; emoji = "★★★☆☆"; }
            else if (score >= 35) { rating = _loc["RatingPoor"]; emoji = "★★☆☆☆"; }
            else { rating = _loc["RatingVeryPoor"]; emoji = "★☆☆☆☆"; }

            // Build suggestions (localized)
            var suggestions = new List<string>();
            if (avgPl >= 0.5)
                suggestions.Add(_loc["SugPacketLoss"]);
            if (avgPing >= 50)
                suggestions.Add(_loc["SugHighPing"]);
            if (avgJitter >= 10)
                suggestions.Add(_loc["SugJitter"]);
            if (session.LagSpikeCount > 5)
                suggestions.Add(_loc["SugLagSpikes"]);
            if (peakCpu >= 90)
                suggestions.Add(_loc["SugCpuPeak"]);
            if (peakRam >= 90)
                suggestions.Add(_loc["SugRamPeak"]);
            if (suggestions.Count == 0)
                suggestions.Add(_loc["SugGreatSession"]);

            var report = new PerformanceReport
            {
                GameName = session.GameName,
                Platform = session.Platform,
                SessionStart = session.StartTime,
                SessionEnd = session.EndTime ?? DateTime.Now,
                Duration = session.Duration,
                AvgPingMs = Math.Round(avgPing, 1),
                MinPingMs = Math.Round(minPing, 1),
                MaxPingMs = Math.Round(maxPing, 1),
                AvgJitterMs = Math.Round(avgJitter, 1),
                AvgPacketLossPercent = Math.Round(avgPl, 2),
                AvgCpuPercent = MathF.Round(avgCpu, 1),
                AvgRamPercent = MathF.Round(avgRam, 1),
                PeakCpuPercent = MathF.Round(peakCpu, 1),
                PeakRamPercent = MathF.Round(peakRam, 1),
                LagSpikeCount = session.LagSpikeCount,
                BottlenecksDetected = session.DetectedBottlenecks.Distinct().ToList(),
                Suggestions = suggestions.ToArray(),
                OverallRating = rating,
                RatingEmoji = emoji
            };

            SaveReport(report);
            _history.Insert(0, report);
            return report;
        }

        public IReadOnlyList<PerformanceReport> GetHistory() => _history.AsReadOnly();

        private void SaveReport(PerformanceReport report)
        {
            try
            {
                var name = $"report_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var path = Path.Combine(ReportsDir, name);
                var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        private void LoadHistory()
        {
            try
            {
                foreach (var f in Directory.GetFiles(ReportsDir, "*.json")
                    .OrderByDescending(f => f).Take(20))
                {
                    try
                    {
                        var json = File.ReadAllText(f);
                        var r = JsonSerializer.Deserialize<PerformanceReport>(json);
                        if (r != null) _history.Add(r);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
