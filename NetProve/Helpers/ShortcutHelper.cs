using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace NetProve.Helpers
{
    /// <summary>
    /// Creates shortcuts for the application: desktop, Start Menu, and Windows Startup.
    /// Uses COM IShellLink — no external dependencies.
    /// </summary>
    public static class ShortcutHelper
    {
        // ── COM interfaces for creating .lnk shortcuts ──────────────────────
        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLink
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        private static string ExePath => Environment.ProcessPath ?? "";
        private static string WorkingDir => Path.GetDirectoryName(ExePath) ?? "";
        private static string IconPath => Path.Combine(WorkingDir, "app.ico");

        // ── Shortcut paths ──────────────────────────────────────────────────
        private static string DesktopShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NetProve.lnk");

        private static string StartMenuShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "NetProve.lnk");

        private static string StartupShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "NetProve.lnk");

        // ── Desktop shortcut ────────────────────────────────────────────────
        public static bool CreateDesktopShortcutIfNeeded()
        {
            if (File.Exists(DesktopShortcut)) return false;
            return CreateShortcut(DesktopShortcut);
        }

        // ── Start Menu shortcut ─────────────────────────────────────────────
        public static bool IsStartMenuPinned => File.Exists(StartMenuShortcut);

        public static bool PinToStartMenu()
        {
            if (File.Exists(StartMenuShortcut)) return false;
            return CreateShortcut(StartMenuShortcut);
        }

        public static bool UnpinFromStartMenu()
        {
            try
            {
                if (File.Exists(StartMenuShortcut))
                    File.Delete(StartMenuShortcut);
                return true;
            }
            catch { return false; }
        }

        // ── Windows Startup ─────────────────────────────────────────────────
        public static bool IsStartupEnabled => File.Exists(StartupShortcut);

        public static bool EnableStartup()
        {
            if (File.Exists(StartupShortcut)) return false;
            return CreateShortcut(StartupShortcut);
        }

        public static bool DisableStartup()
        {
            try
            {
                if (File.Exists(StartupShortcut))
                    File.Delete(StartupShortcut);
                return true;
            }
            catch { return false; }
        }

        // ── Core shortcut creation ──────────────────────────────────────────
        private static bool CreateShortcut(string shortcutPath)
        {
            try
            {
                var exePath = ExePath;
                if (string.IsNullOrEmpty(exePath)) return false;

                // Ensure directory exists
                var dir = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var link = (IShellLink)new ShellLink();
                link.SetPath(exePath);
                link.SetDescription("NetProve — Gaming & Network Performance Optimizer");
                link.SetWorkingDirectory(WorkingDir);

                if (File.Exists(IconPath))
                    link.SetIconLocation(IconPath, 0);
                else
                    link.SetIconLocation(exePath, 0);

                var file = (IPersistFile)link;
                file.Save(shortcutPath, false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
