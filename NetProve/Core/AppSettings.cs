using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace NetProve.Core
{
    public sealed class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetProve", "settings.json");

        // ── Thresholds ────────────────────────────────────────────────────────
        public int RamPressureThresholdPercent { get; set; } = 85;
        public int CpuOverloadThresholdPercent { get; set; } = 90;
        public int DiskActivityThresholdPercent { get; set; } = 90;
        public int NetworkLatencyWarningMs { get; set; } = 80;
        public int NetworkLatencyCriticalMs { get; set; } = 150;
        public int PacketLossWarningPercent { get; set; } = 2;
        public int JitterWarningMs { get; set; } = 20;

        // ── Cache limits (MB) ─────────────────────────────────────────────────
        public long ChromeCacheLimitMb { get; set; } = 500;
        public long EdgeCacheLimitMb { get; set; } = 500;
        public long FirefoxCacheLimitMb { get; set; } = 500;
        public long OperaCacheLimitMb { get; set; } = 300;
        public long YandexCacheLimitMb { get; set; } = 300;

        // ── Process whitelist ─────────────────────────────────────────────────
        public HashSet<string> WhitelistedProcesses { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "svchost", "lsass", "wininit", "winlogon",
            "csrss", "smss", "services", "explorer", "dwm",
            "audiodg", "taskmgr", "SearchHost", "ShellExperienceHost",
            "StartMenuExperienceHost", "antimalware", "MsMpEng",
            "SecurityHealthService", "NetProve"
        };

        // ── Monitoring intervals (ms) ─────────────────────────────────────────
        public int SystemPollIntervalMs { get; set; } = 3000;
        public int NetworkPollIntervalMs { get; set; } = 5000;
        public int ProcessPollIntervalMs { get; set; } = 10000;

        // ── Preferences ───────────────────────────────────────────────────────
        public bool AutoStartGamingMode { get; set; } = true;
        public bool ShowLagWarnings { get; set; } = true;
        public bool AutoCleanCacheOnLimit { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool DarkTheme { get; set; } = true;
        public string Language { get; set; } = DetectSystemLanguage();

        private static string DetectSystemLanguage()
        {
            var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return culture switch
            {
                "tr" => "Turkish",
                "zh" => "Chinese",
                "ja" => "Japanese",
                "es" => "Spanish",
                "ru" => "Russian",
                _ => "English"
            };
        }
        public string PingTarget { get; set; } = "8.8.8.8";

        // ── Auto mode ───────────────────────────────────────────────────────────
        public bool AutoModeEnabled { get; set; } = false;

        // ── Network adapter ─────────────────────────────────────────────────────
        public bool NagleDisabled { get; set; } = false;
        public string PreferredDns { get; set; } = "";

        // ── Power plan ──────────────────────────────────────────────────────────
        public string OriginalPowerPlanGuid { get; set; } = "";

        // ── Static instance ───────────────────────────────────────────────────
        private static AppSettings? _instance;
        public static AppSettings Instance => _instance ??= Load();

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}
