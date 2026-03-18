using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace NetProve.Themes
{
    public enum ThemeMode { Dark, Light }

    /// <summary>
    /// Centralized theme management.  Replaces brush resources in the
    /// window's ResourceDictionary so that DynamicResource bindings
    /// pick up the new values instantly — no restart required.
    /// </summary>
    public static class ThemeManager
    {
        public static ThemeMode Current { get; private set; } = ThemeMode.Dark;

        // ── Dark palette ──────────────────────────────────────────────────
        private static readonly Dictionary<string, Color> Dark = new()
        {
            ["BgMain"]        = C(0x0F, 0x17, 0x2A),
            ["BgSidebar"]     = C(0x1E, 0x29, 0x3B),
            ["BgCard"]        = C(0x1E, 0x29, 0x3B),
            ["BgCardHover"]   = C(0x27, 0x33, 0x47),
            ["Border"]        = C(0x33, 0x41, 0x55),
            ["Accent"]        = C(0x3B, 0x82, 0xF6),
            ["AccentGlow"]    = C(0x1D, 0x4E, 0xD8),
            ["Success"]       = C(0x10, 0xB9, 0x81),
            ["Warning"]       = C(0xF5, 0x9E, 0x0B),
            ["Danger"]        = C(0xEF, 0x44, 0x44),
            ["TextPrimary"]   = C(0xF1, 0xF5, 0xF9),
            ["TextSub"]       = C(0xBD, 0xC8, 0xD6),
            ["TextMuted"]     = C(0x6B, 0x7B, 0x8D),

            // Template-internal colors
            ["NavHoverBg"]    = C(0x27, 0x33, 0x47),
            ["NavActiveBg"]   = C(0x1D, 0x34, 0x61),
            ["SecBg"]         = C(0x27, 0x33, 0x47),
            ["SecHoverBg"]    = C(0x33, 0x41, 0x55),
            ["DisabledBg"]    = C(0x33, 0x41, 0x55),
            ["AccentHover"]   = C(0x25, 0x63, 0xEB),
            ["AccentPressed"] = C(0x1D, 0x4E, 0xD8),
            ["TrackBg"]       = C(0x2D, 0x30, 0x48),
            ["InsetBg"]       = C(0x0F, 0x17, 0x2A),
            ["AltRowBg"]      = C(0x1A, 0x22, 0x34),
            ["StatusGreenBg"] = C(0x1B, 0x3B, 0x2A),
            ["StatusBlueBg"]  = C(0x1D, 0x2A, 0x3B),
            ["BannerWarnBg"]  = C(0x2D, 0x1B, 0x0E),
            ["ChartCpuFill"]  = C(0x1A, 0x3A, 0x6B),
            ["ChartRamFill"]  = C(0x0D, 0x3B, 0x2C),
            ["ChartPingFill"] = C(0x2D, 0x1B, 0x5E),

            // Gradient ends for progress bar
            ["GradientStart"] = C(0x3B, 0x82, 0xF6),
            ["GradientEnd"]   = C(0x06, 0xB6, 0xD4),
        };

        // ── Light palette ─────────────────────────────────────────────────
        private static readonly Dictionary<string, Color> Light = new()
        {
            ["BgMain"]        = C(0xF1, 0xF5, 0xF9),
            ["BgSidebar"]     = C(0xFF, 0xFF, 0xFF),
            ["BgCard"]        = C(0xFF, 0xFF, 0xFF),
            ["BgCardHover"]   = C(0xF1, 0xF5, 0xF9),
            ["Border"]        = C(0xE2, 0xE8, 0xF0),
            ["Accent"]        = C(0x25, 0x63, 0xEB),
            ["AccentGlow"]    = C(0x1D, 0x4E, 0xD8),
            ["Success"]       = C(0x05, 0x96, 0x69),
            ["Warning"]       = C(0xD9, 0x77, 0x06),
            ["Danger"]        = C(0xDC, 0x26, 0x26),
            ["TextPrimary"]   = C(0x0F, 0x17, 0x2A),
            ["TextSub"]       = C(0x64, 0x74, 0x8B),
            ["TextMuted"]     = C(0xCB, 0xD5, 0xE1),

            ["NavHoverBg"]    = C(0xF1, 0xF5, 0xF9),
            ["NavActiveBg"]   = C(0xDB, 0xEA, 0xFE),
            ["SecBg"]         = C(0xF1, 0xF5, 0xF9),
            ["SecHoverBg"]    = C(0xE2, 0xE8, 0xF0),
            ["DisabledBg"]    = C(0xE2, 0xE8, 0xF0),
            ["AccentHover"]   = C(0x1D, 0x4E, 0xD8),
            ["AccentPressed"] = C(0x1E, 0x40, 0xAF),
            ["TrackBg"]       = C(0xE2, 0xE8, 0xF0),
            ["InsetBg"]       = C(0xF1, 0xF5, 0xF9),
            ["AltRowBg"]      = C(0xF8, 0xFA, 0xFC),
            ["StatusGreenBg"] = C(0xD1, 0xFA, 0xE5),
            ["StatusBlueBg"]  = C(0xDB, 0xEA, 0xFE),
            ["BannerWarnBg"]  = C(0xFE, 0xF3, 0xC7),
            ["ChartCpuFill"]  = C(0xDB, 0xEA, 0xFE),
            ["ChartRamFill"]  = C(0xD1, 0xFA, 0xE5),
            ["ChartPingFill"] = C(0xED, 0xE9, 0xFE),

            ["GradientStart"] = C(0x25, 0x63, 0xEB),
            ["GradientEnd"]   = C(0x06, 0x96, 0xB4),
        };

        /// <summary>
        /// Applies a theme by replacing all named SolidColorBrush resources
        /// in the given ResourceDictionary.  DynamicResource bindings will
        /// pick up the changes automatically.
        /// </summary>
        public static void Apply(ThemeMode mode, ResourceDictionary resources)
        {
            Current = mode;
            var palette = mode == ThemeMode.Dark ? Dark : Light;

            foreach (var kv in palette)
                resources[kv.Key] = new SolidColorBrush(kv.Value);
        }

        /// <summary>Toggles between dark and light and returns the new mode.</summary>
        public static ThemeMode Toggle(ResourceDictionary resources)
        {
            var next = Current == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
            Apply(next, resources);
            return next;
        }

        /// <summary>Returns the current theme's color for a given key.</summary>
        public static Color GetColor(string key)
        {
            var palette = Current == ThemeMode.Dark ? Dark : Light;
            return palette.TryGetValue(key, out var c) ? c : Colors.Magenta;
        }

        /// <summary>Returns a new SolidColorBrush for the given key in the current theme.</summary>
        public static SolidColorBrush GetBrush(string key) =>
            new SolidColorBrush(GetColor(key));

        private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    }
}
