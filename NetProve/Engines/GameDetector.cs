using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using NetProve.Core;
using NetProve.Models;

namespace NetProve.Engines
{
    /// <summary>
    /// Detects when a game from a supported launcher is running.
    /// Uses process name matching (no admin required) with an extensive game database.
    /// </summary>
    public sealed class GameDetector : IDisposable
    {
        // ── Known game process signatures → (name, platform) ─────────────────
        private static readonly Dictionary<string, (string Name, string Platform)> KnownGames =
            new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Riot Games ──
            ["VALORANT-Win64-Shipping"] = ("VALORANT", "Riot"),
            ["LeagueOfLegends"] = ("League of Legends", "Riot"),
            ["LoR"] = ("Legends of Runeterra", "Riot"),

            // ── Steam / Valve ──
            ["csgo"] = ("CS:GO", "Steam"),
            ["cs2"] = ("Counter-Strike 2", "Steam"),
            ["dota2"] = ("Dota 2", "Steam"),
            ["hl2"] = ("Half-Life 2", "Steam"),
            ["left4dead2"] = ("Left 4 Dead 2", "Steam"),
            ["tf_win64"] = ("Team Fortress 2", "Steam"),
            ["tf"] = ("Team Fortress 2", "Steam"),
            ["Portal2"] = ("Portal 2", "Steam"),
            ["GarrysMod"] = ("Garry's Mod", "Steam"),

            // ── Epic / Unreal ──
            ["FortniteClient-Win64-Shipping"] = ("Fortnite", "Epic"),
            ["RocketLeague"] = ("Rocket League", "Epic/Steam"),

            // ── Battle.net / Blizzard ──
            ["Overwatch"] = ("Overwatch 2", "Battle.net"),
            ["Warzone"] = ("Call of Duty: Warzone", "Battle.net"),
            ["ModernWarfare"] = ("Call of Duty: MW", "Battle.net"),
            ["BlackOpsColdWar"] = ("Call of Duty: BOCW", "Battle.net"),
            ["cod"] = ("Call of Duty", "Battle.net"),
            ["Diablo IV"] = ("Diablo IV", "Battle.net"),
            ["Hearthstone"] = ("Hearthstone", "Battle.net"),
            ["StarCraft II"] = ("StarCraft II", "Battle.net"),
            ["WowClassic"] = ("World of Warcraft", "Battle.net"),
            ["Wow"] = ("World of Warcraft", "Battle.net"),

            // ── EA ──
            ["FIFA24"] = ("EA FC 24", "EA"),
            ["FC25"] = ("EA FC 25", "EA"),
            ["bf2042"] = ("Battlefield 2042", "EA"),
            ["bfv"] = ("Battlefield V", "EA"),
            ["Madden24"] = ("Madden NFL 24", "EA"),
            ["ApexLegends"] = ("Apex Legends", "EA"),
            ["r5apex"] = ("Apex Legends", "EA"),
            ["NeedForSpeed"] = ("Need for Speed", "EA"),
            ["NeedForSpeedUnbound"] = ("Need for Speed Unbound", "EA"),
            ["TheSimsReturnees"] = ("The Sims 4", "EA"),
            ["TS4_x64"] = ("The Sims 4", "EA"),

            // ── Ubisoft ──
            ["RainbowSix"] = ("Rainbow Six Siege", "Ubisoft"),
            ["RainbowSix_BE"] = ("Rainbow Six Siege", "Ubisoft"),
            ["ACV"] = ("Assassin's Creed Valhalla", "Ubisoft"),
            ["ACOdyssey"] = ("Assassin's Creed Odyssey", "Ubisoft"),
            ["FarCry6"] = ("Far Cry 6", "Ubisoft"),
            ["TheDivision2"] = ("The Division 2", "Ubisoft"),
            ["GhostRecon"] = ("Ghost Recon Breakpoint", "Ubisoft"),

            // ── Rockstar ──
            ["GTA5"] = ("Grand Theft Auto V", "Rockstar"),
            ["GTAV"] = ("Grand Theft Auto V", "Rockstar"),
            ["PlayGTAV"] = ("Grand Theft Auto V", "Rockstar"),
            ["GTA6"] = ("Grand Theft Auto VI", "Rockstar"),
            ["RDR2"] = ("Red Dead Redemption 2", "Rockstar"),

            // ── PUBG ──
            ["TslGame"] = ("PUBG: Battlegrounds", "Steam"),
            ["PUBG-Win64-Shipping"] = ("PUBG: Battlegrounds", "Steam"),

            // ── Minecraft ──
            ["Minecraft.Windows"] = ("Minecraft (Bedrock)", "Microsoft"),
            ["javaw"] = ("Minecraft (Java)", "Java"), // Will verify with window title
            ["java"] = ("Minecraft (Java)", "Java"),

            // ── Other Popular Games ──
            ["Warframe.x64"] = ("Warframe", "Steam"),
            ["Warframe"] = ("Warframe", "Steam"),
            ["EscapeFromTarkov"] = ("Escape from Tarkov", "BSG"),
            ["pathofexile_x64"] = ("Path of Exile", "Steam"),
            ["PathOfExileSteam"] = ("Path of Exile", "Steam"),
            ["PathOfExile"] = ("Path of Exile", "Steam"),
            ["PathOfExile_x64Steam"] = ("Path of Exile", "Steam"),
            ["PathOfExileSteam_x64"] = ("Path of Exile", "Steam"),
            ["DeadByDaylight-Win64-Shipping"] = ("Dead by Daylight", "Steam"),
            ["Phasmophobia"] = ("Phasmophobia", "Steam"),
            ["eldenring"] = ("Elden Ring", "Steam"),
            ["start_protected_game"] = ("Elden Ring", "Steam"), // EAC launcher
            ["DarkSoulsIII"] = ("Dark Souls III", "Steam"),
            ["sekiro"] = ("Sekiro", "Steam"),
            ["Cyberpunk2077"] = ("Cyberpunk 2077", "Steam"),
            ["witcher3"] = ("The Witcher 3", "Steam"),
            ["Hogwarts Legacy"] = ("Hogwarts Legacy", "Steam"),
            ["HogwartsLegacy"] = ("Hogwarts Legacy", "Steam"),
            ["Palworld-Win64-Shipping"] = ("Palworld", "Steam"),
            ["GenshinImpact"] = ("Genshin Impact", "miHoYo"),
            ["YuanShen"] = ("Genshin Impact", "miHoYo"),
            ["StarRail"] = ("Honkai: Star Rail", "miHoYo"),
            ["ZenlessZoneZero"] = ("Zenless Zone Zero", "miHoYo"),
            ["NARAKA-Win64-Shipping"] = ("Naraka: Bladepoint", "Steam"),
            ["r2game"] = ("Rust", "Steam"),
            ["RustClient"] = ("Rust", "Steam"),
            ["rust"] = ("Rust", "Steam"),
            ["Among Us"] = ("Among Us", "Steam"),
            ["Terraria"] = ("Terraria", "Steam"),
            ["Stardew Valley"] = ("Stardew Valley", "Steam"),
            ["Subnautica"] = ("Subnautica", "Steam"),
            ["ARK"] = ("ARK: Survival", "Steam"),
            ["ArkAscended"] = ("ARK: Survival Ascended", "Steam"),
            ["ShooterGame"] = ("ARK: Survival Evolved", "Steam"),
            ["Baldur's Gate 3"] = ("Baldur's Gate 3", "Steam"),
            ["bg3"] = ("Baldur's Gate 3", "Steam"),
            ["bg3_dx11"] = ("Baldur's Gate 3", "Steam"),
            ["Lethal Company"] = ("Lethal Company", "Steam"),
            ["LethalCompany"] = ("Lethal Company", "Steam"),
            ["7DaysToDie"] = ("7 Days to Die", "Steam"),
            ["Cities2"] = ("Cities: Skylines II", "Steam"),
            ["EuroTruckSimulator2"] = ("Euro Truck Simulator 2", "Steam"),
            ["amtrucks"] = ("American Truck Simulator", "Steam"),
            ["aces"] = ("War Thunder", "Steam"),
            ["WorldOfTanks"] = ("World of Tanks", "Wargaming"),
            ["Roblox"] = ("Roblox", "Roblox"),
            ["RobloxPlayerBeta"] = ("Roblox", "Roblox"),
            ["FiveM"] = ("FiveM (GTA RP)", "FiveM"),
            ["FiveM_b2699_GTAProcess"] = ("FiveM (GTA RP)", "FiveM"),

            // ── Additional Steam games (common actual process names) ──
            ["Witcher3"] = ("The Witcher 3", "Steam"),
            ["TheWitcher3"] = ("The Witcher 3", "Steam"),
            ["HollowKnight"] = ("Hollow Knight", "Steam"),
            ["hollow_knight"] = ("Hollow Knight", "Steam"),
            ["Celeste"] = ("Celeste", "Steam"),
            ["Hades"] = ("Hades", "Steam"),
            ["Hades2"] = ("Hades II", "Steam"),
            ["ori"] = ("Ori", "Steam"),
            ["Cuphead"] = ("Cuphead", "Steam"),
            ["DeepRockGalactic"] = ("Deep Rock Galactic", "Steam"),
            ["FSD-Win64-Shipping"] = ("Deep Rock Galactic", "Steam"),
            ["MonsterHunterWorld"] = ("Monster Hunter: World", "Steam"),
            ["MonsterHunterRise"] = ("Monster Hunter Rise", "Steam"),
            ["DyingLight2"] = ("Dying Light 2", "Steam"),
            ["DyingLightGame"] = ("Dying Light", "Steam"),
            ["Satisfactory"] = ("Satisfactory", "Steam"),
            ["FactoryGame-Win64-Shipping"] = ("Satisfactory", "Steam"),
            ["Valheim"] = ("Valheim", "Steam"),
            ["valheim"] = ("Valheim", "Steam"),
            ["SoT"] = ("Sea of Thieves", "Steam"),
            ["SeaOfThieves"] = ("Sea of Thieves", "Steam"),
            ["Remnant2-Win64-Shipping"] = ("Remnant 2", "Steam"),
            ["DaysGone"] = ("Days Gone", "Steam"),
            ["BendGame-Win64-Shipping"] = ("Days Gone", "Steam"),
            ["HorizonZeroDawn"] = ("Horizon Zero Dawn", "Steam"),
            ["HorizonForbiddenWest"] = ("Horizon Forbidden West", "Steam"),
            ["GodOfWar"] = ("God of War", "Steam"),
            ["GoW"] = ("God of War", "Steam"),
            ["SpiderMan"] = ("Spider-Man", "Steam"),
            ["Spider-Man"] = ("Spider-Man", "Steam"),
            ["TheForest"] = ("The Forest", "Steam"),
            ["SonsOfTheForest"] = ("Sons of the Forest", "Steam"),
            ["SonsOfTheForestDedicatedServer"] = ("Sons of the Forest", "Steam"),
            ["RaftGame-Win64-Shipping"] = ("Raft", "Steam"),
            ["Raft"] = ("Raft", "Steam"),
            ["Unturned"] = ("Unturned", "Steam"),
            ["Scum"] = ("SCUM", "Steam"),
            ["DontStarve"] = ("Don't Starve", "Steam"),
            ["dontstarve_steam"] = ("Don't Starve", "Steam"),
            ["ProjectZomboid64"] = ("Project Zomboid", "Steam"),
            ["PlateUp"] = ("PlateUp!", "Steam"),
            ["TheLongDark"] = ("The Long Dark", "Steam"),
            ["SCP Secret Laboratory"] = ("SCP: Secret Laboratory", "Steam"),
            ["SCPSL"] = ("SCP: Secret Laboratory", "Steam"),
            ["ContentWarning"] = ("Content Warning", "Steam"),
            ["ScheduleI"] = ("Schedule I", "Steam"),
        };

        // ── Known launcher process names (used for parent-based detection) ──────
        private static readonly HashSet<string> LauncherProcesses =
            new(StringComparer.OrdinalIgnoreCase)
        {
            "steam", "steamwebhelper",
            "EpicGamesLauncher",
            "RiotClientServices", "RiotClientUx",
            "Battle.net", "Agent",
            "EADesktop", "Origin",
            "UbisoftConnect", "upc",
        };

        // ── Directories that indicate a game installation ──────────────────────
        private static readonly string[] GameDirKeywords =
        {
            "steamapps", "steam", "epic games", "riot games",
            "battle.net", "ubisoft", "ea games", "origin",
            "rockstar games", "program files"
        };

        // ── Minecraft Java detection (javaw can be anything) ──────────────────
        private static bool IsMinecraftJava(Process p)
        {
            try
            {
                if (p.ProcessName.Equals("javaw", StringComparison.OrdinalIgnoreCase) ||
                    p.ProcessName.Equals("java", StringComparison.OrdinalIgnoreCase))
                {
                    var title = p.MainWindowTitle;
                    return !string.IsNullOrEmpty(title) &&
                           title.Contains("Minecraft", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return false;
        }

        private GameSession? _currentSession;
        private CancellationTokenSource? _cts;
        private Task? _task;

        public GameSession? CurrentSession => _currentSession;
        public bool IsGameRunning => _currentSession?.IsActive == true;

        public void Start(CancellationToken externalToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _task = Task.Run(() => DetectLoop(_cts.Token), _cts.Token);
        }

        public void Stop() { _cts?.Cancel(); try { _task?.Wait(2000); } catch { } }

        private async Task DetectLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(8000, ct); // Check every 8 seconds (process scan is expensive)
                    ScanProcesses();
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        private void ScanProcesses()
        {
            var procs = Process.GetProcesses();
            bool foundGame = false;
            string gameName = "";
            string platform = "";
            int gamePid = 0;

            // First pass: check if current session process is still alive
            if (_currentSession?.IsActive == true)
            {
                try
                {
                    var alive = Process.GetProcessById(_currentSession.ProcessId);
                    if (!alive.HasExited)
                    {
                        foundGame = true;
                        gameName = _currentSession.GameName;
                        platform = _currentSession.Platform;
                        gamePid = _currentSession.ProcessId;
                    }
                    alive.Dispose();
                }
                catch
                {
                    // Process no longer exists
                }
            }

            // Second pass: scan all processes for games
            if (!foundGame)
            {
                // Candidates from heuristic detection (lower priority)
                string heuristicName = "";
                string heuristicPlatform = "";
                int heuristicPid = 0;

                foreach (var p in procs)
                {
                    try
                    {
                        // Skip known non-game processes
                        if (IsSystemProcess(p.ProcessName)) continue;

                        // 1. Exact match by process name
                        if (KnownGames.TryGetValue(p.ProcessName, out var info))
                        {
                            if (p.ProcessName.Equals("javaw", StringComparison.OrdinalIgnoreCase) ||
                                p.ProcessName.Equals("java", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!IsMinecraftJava(p)) continue;
                            }

                            foundGame = true;
                            gameName = info.Name;
                            platform = info.Platform;
                            gamePid = p.Id;
                            break;
                        }

                        // 2. Heuristic: process name matches game-like patterns
                        if (heuristicPid == 0 && IsLikelyGameByName(p.ProcessName))
                        {
                            heuristicName = CleanGameName(p.ProcessName);
                            heuristicPlatform = DetectPlatformFromPath(p);
                            heuristicPid = p.Id;
                        }

                        // 3. Check if process runs from a game directory
                        if (heuristicPid == 0 && IsGameByPath(p))
                        {
                            heuristicName = CleanGameName(p.ProcessName);
                            heuristicPlatform = DetectPlatformFromPath(p);
                            heuristicPid = p.Id;
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }

                // Use heuristic match if no exact match was found
                if (!foundGame && heuristicPid != 0)
                {
                    foundGame = true;
                    gameName = heuristicName;
                    platform = heuristicPlatform;
                    gamePid = heuristicPid;
                }
            }

            if (foundGame && (_currentSession == null || !_currentSession.IsActive))
            {
                _currentSession = new GameSession
                {
                    GameName = gameName,
                    Platform = platform,
                    ProcessId = gamePid
                };
                EventBus.Instance.Publish(new GameDetectedEvent
                {
                    GameName = gameName,
                    Platform = platform,
                    ProcessId = gamePid
                });
            }
            else if (!foundGame && _currentSession?.IsActive == true)
            {
                _currentSession.EndTime = DateTime.Now;
                EventBus.Instance.Publish(new GameEndedEvent
                {
                    GameName = _currentSession.GameName,
                    SessionEnd = _currentSession.EndTime.Value
                });
            }
        }

        /// <summary>
        /// Quick filter to skip processes that are definitely not games.
        /// </summary>
        private static bool IsSystemProcess(string name)
        {
            return name.Length <= 2 || name.Equals("svchost", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Idle", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("conhost", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("csrss", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("dwm", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("taskhostw", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Windows", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("msedge", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("chrome", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("firefox", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Code", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("RuntimeBroker", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("backgroundTaskHost", StringComparison.OrdinalIgnoreCase) ||
                   LauncherProcesses.Contains(name);
        }

        /// <summary>
        /// Heuristic: detects game-like process names without needing admin access.
        /// </summary>
        private static bool IsLikelyGameByName(string name)
        {
            // Common Unreal Engine game suffixes
            if (name.EndsWith("-Win64-Shipping", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.EndsWith("-Win64-Test", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.EndsWith("-Win64", StringComparison.OrdinalIgnoreCase) && name.Length > 8) return true;
            if (name.EndsWith("_BE", StringComparison.OrdinalIgnoreCase) && name.Length > 5) return true;
            // Unity games
            if (name.EndsWith("-Win64-Shipping", StringComparison.OrdinalIgnoreCase)) return true;
            // Common game executable patterns
            if (name.Contains("Game", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("GameBar", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("GameInput", StringComparison.OrdinalIgnoreCase) &&
                name.Length > 6) return true;
            return false;
        }

        /// <summary>
        /// Detects games by checking if process runs from a known game directory.
        /// </summary>
        private static bool IsGameByPath(Process p)
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (string.IsNullOrEmpty(path)) return false;
                var lower = path.ToLowerInvariant();

                // Must be in a game directory
                bool inGameDir = false;
                foreach (var keyword in GameDirKeywords)
                {
                    if (lower.Contains(keyword))
                    {
                        inGameDir = true;
                        break;
                    }
                }
                if (!inGameDir) return false;

                // Must NOT be a launcher itself
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (LauncherProcesses.Contains(fileName)) return false;
                if (fileName.Contains("setup", StringComparison.OrdinalIgnoreCase)) return false;
                if (fileName.Contains("install", StringComparison.OrdinalIgnoreCase)) return false;
                if (fileName.Contains("update", StringComparison.OrdinalIgnoreCase)) return false;
                if (fileName.Contains("crash", StringComparison.OrdinalIgnoreCase)) return false;
                if (fileName.Contains("helper", StringComparison.OrdinalIgnoreCase)) return false;
                if (fileName.Contains("launcher", StringComparison.OrdinalIgnoreCase)) return false;
                if (fileName.Contains("service", StringComparison.OrdinalIgnoreCase)) return false;
                if (fileName.Contains("unins", StringComparison.OrdinalIgnoreCase)) return false;
                if (fileName.Contains("redist", StringComparison.OrdinalIgnoreCase)) return false;

                // Check if process has a visible window (games usually do)
                if (p.MainWindowHandle != IntPtr.Zero)
                    return true;
            }
            catch { } // MainModule throws if access denied - skip
            return false;
        }

        /// <summary>
        /// Detect platform from process path.
        /// </summary>
        private static string DetectPlatformFromPath(Process p)
        {
            try
            {
                var path = p.MainModule?.FileName?.ToLowerInvariant() ?? "";
                if (path.Contains("steamapps")) return "Steam";
                if (path.Contains("epic games")) return "Epic";
                if (path.Contains("riot games")) return "Riot";
                if (path.Contains("battle.net")) return "Battle.net";
                if (path.Contains("ubisoft")) return "Ubisoft";
                if (path.Contains("ea games") || path.Contains("origin")) return "EA";
                if (path.Contains("rockstar")) return "Rockstar";
            }
            catch { }
            return "Unknown";
        }

        /// <summary>
        /// Extracts a clean game name from a process name.
        /// </summary>
        private static string CleanGameName(string processName)
        {
            var name = processName;
            // Remove common suffixes
            foreach (var suffix in new[] { "-Win64-Shipping", "-Win64-Test", "_BE", "_x64", "-Win64" })
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^suffix.Length];
                    break;
                }
            }
            // Add spaces before capitals: "DeadByDaylight" → "Dead By Daylight"
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                    result.Append(' ');
                result.Append(name[i]);
            }
            return result.ToString();
        }

        public void Dispose() { Stop(); _cts?.Dispose(); }
    }
}
