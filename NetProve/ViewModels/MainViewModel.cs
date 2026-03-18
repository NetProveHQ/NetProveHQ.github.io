using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NetProve.Core;
using NetProve.Engines;
using NetProve.Models;

namespace NetProve.ViewModels
{
    public sealed class MainViewModel : BaseViewModel
    {
        private readonly CoreEngine _engine = CoreEngine.Instance;
        private readonly Localization.LocalizationManager _loc = Localization.LocalizationManager.Instance;

        // ── Dashboard metrics ─────────────────────────────────────────────────
        private float _cpuUsage;
        private float _ramUsage;
        private float _diskUsage;
        private string _ramUsedText = "0 GB / 0 GB";
        private string _diskText = "0 MB/s";
        private double _ping;
        private double _jitter;
        private double _packetLoss;
        private string _downloadMbps = "0 Mbps";
        private string _uploadMbps = "0 Mbps";
        private string _networkQuality = "Unknown";
        private string _cpuName = "";

        public float CpuUsage { get => _cpuUsage; private set => SetField(ref _cpuUsage, value); }
        public float RamUsage { get => _ramUsage; private set => SetField(ref _ramUsage, value); }
        public float DiskUsage { get => _diskUsage; private set => SetField(ref _diskUsage, value); }
        public string RamUsedText { get => _ramUsedText; private set => SetField(ref _ramUsedText, value); }
        public string DiskText { get => _diskText; private set => SetField(ref _diskText, value); }
        public double Ping { get => _ping; private set => SetField(ref _ping, value); }
        public double Jitter { get => _jitter; private set => SetField(ref _jitter, value); }
        public double PacketLoss { get => _packetLoss; private set => SetField(ref _packetLoss, value); }
        public string DownloadMbps { get => _downloadMbps; private set => SetField(ref _downloadMbps, value); }
        public string UploadMbps { get => _uploadMbps; private set => SetField(ref _uploadMbps, value); }
        public string NetworkQuality { get => _networkQuality; private set => SetField(ref _networkQuality, value); }
        public string CpuName { get => _cpuName; private set => SetField(ref _cpuName, value); }

        // ── Mode state ────────────────────────────────────────────────────────
        private bool _gamingMode;
        private bool _streamingMode;
        private string _activeGame = "No game detected";
        private string _activePlatform = "";
        private bool _gameRunning;

        public bool GamingMode { get => _gamingMode; private set => SetField(ref _gamingMode, value); }
        public bool StreamingMode { get => _streamingMode; private set => SetField(ref _streamingMode, value); }
        public string ActiveGame { get => _activeGame; private set => SetField(ref _activeGame, value); }
        public string ActivePlatform { get => _activePlatform; private set => SetField(ref _activePlatform, value); }
        public bool GameRunning { get => _gameRunning; private set => SetField(ref _gameRunning, value); }

        // ── Lag / prediction ──────────────────────────────────────────────────
        private string _lagSummary = "Run analysis to detect lag causes.";
        private string _lagSeverityText = "–";
        private string _predictionText = "Monitoring…";
        private bool _lagWarningVisible;
        private LagAnalysisResult? _lastAnalysis;

        public string LagSummary { get => _lagSummary; private set => SetField(ref _lagSummary, value); }
        public string LagSeverityText { get => _lagSeverityText; private set => SetField(ref _lagSeverityText, value); }
        public string PredictionText { get => _predictionText; private set => SetField(ref _predictionText, value); }
        public bool LagWarningVisible { get => _lagWarningVisible; set => SetField(ref _lagWarningVisible, value); }
        public LagAnalysisResult? LastAnalysis { get => _lastAnalysis; private set => SetField(ref _lastAnalysis, value); }

        // ── Process list ──────────────────────────────────────────────────────
        private ObservableCollection<ProcessInfo> _processes = new();
        public ObservableCollection<ProcessInfo> Processes
        {
            get => _processes;
            private set => SetField(ref _processes, value);
        }

        // ── Cache info ────────────────────────────────────────────────────────
        private ObservableCollection<CacheInfo> _caches = new();
        public ObservableCollection<CacheInfo> Caches
        {
            get => _caches;
            private set => SetField(ref _caches, value);
        }

        // ── Reports ───────────────────────────────────────────────────────────
        private ObservableCollection<PerformanceReport> _reports = new();
        public ObservableCollection<PerformanceReport> Reports
        {
            get => _reports;
            private set => SetField(ref _reports, value);
        }

        // ── Status / log ──────────────────────────────────────────────────────
        private ObservableCollection<string> _activityLog = new();
        public ObservableCollection<string> ActivityLog => _activityLog;

        private string _statusText = "Monitoring active";
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }

        // null = no result, true = success, false = failure
        private bool? _lastResult;
        public bool? LastResult { get => _lastResult; set => SetField(ref _lastResult, value); }
        private string _lastResultText = "";
        public string LastResultText { get => _lastResultText; set => SetField(ref _lastResultText, value); }

        // ── Speed test ────────────────────────────────────────────────────────
        private string _speedTestStatus = "Click 'Run Speed Test' to begin.";
        private int _speedTestProgress;
        private bool _speedTestRunning;
        private string _speedTestDownload = "–";
        private string _speedTestUpload = "–";
        private string _speedTestPing = "–";
        private bool _speedTestComplete;

        public string SpeedTestStatus { get => _speedTestStatus; set => SetField(ref _speedTestStatus, value); }
        public int SpeedTestProgress { get => _speedTestProgress; set => SetField(ref _speedTestProgress, value); }
        public bool SpeedTestRunning { get => _speedTestRunning; set => SetField(ref _speedTestRunning, value); }
        public string SpeedTestDownload { get => _speedTestDownload; set => SetField(ref _speedTestDownload, value); }
        public string SpeedTestUpload { get => _speedTestUpload; set => SetField(ref _speedTestUpload, value); }
        public string SpeedTestPing { get => _speedTestPing; set => SetField(ref _speedTestPing, value); }
        public bool SpeedTestComplete { get => _speedTestComplete; set => SetField(ref _speedTestComplete, value); }

        // ── Chart data ────────────────────────────────────────────────────────
        private ObservableCollection<double> _pingHistory = new();
        public ObservableCollection<double> PingHistory => _pingHistory;

        private ObservableCollection<double> _cpuHistory = new();
        public ObservableCollection<double> CpuHistory => _cpuHistory;

        private ObservableCollection<double> _ramHistory = new();
        public ObservableCollection<double> RamHistory => _ramHistory;

        // ── Network optimizer ─────────────────────────────────────────────────
        private bool _tcpOptimized;
        private string _tcpOptimizeText = "Apply TCP Optimizations";
        public bool TcpOptimized { get => _tcpOptimized; set => SetField(ref _tcpOptimized, value); }
        public string TcpOptimizeText { get => _tcpOptimizeText; set => SetField(ref _tcpOptimizeText, value); }

        // ── Auto mode ───────────────────────────────────────────────────────────
        private bool _autoMode;
        public bool AutoMode { get => _autoMode; set => SetField(ref _autoMode, value); }

        // ── DNS Benchmark ───────────────────────────────────────────────────────
        private ObservableCollection<DnsBenchmarkResult> _dnsResults = new();
        public ObservableCollection<DnsBenchmarkResult> DnsResults => _dnsResults;
        private bool _dnsBenchmarkRunning;
        public bool DnsBenchmarkRunning { get => _dnsBenchmarkRunning; set => SetField(ref _dnsBenchmarkRunning, value); }
        private string _wifiBandInfo = "";
        public string WifiBandInfo { get => _wifiBandInfo; set => SetField(ref _wifiBandInfo, value); }

        // ── Nagle ───────────────────────────────────────────────────────────────
        private bool _nagleDisabled;
        public bool NagleDisabled { get => _nagleDisabled; set => SetField(ref _nagleDisabled, value); }

        // ── Commands ──────────────────────────────────────────────────────────
        public ICommand OptimizeRamCommand { get; }
        public ICommand ToggleGamingModeCommand { get; }
        public ICommand ToggleStreamingModeCommand { get; }
        public ICommand RunLagAnalysisCommand { get; }
        public ICommand RunFullOptimizationCommand { get; }
        public ICommand RunSpeedTestCommand { get; }
        public ICommand FlushDnsCommand { get; }
        public ICommand ToggleTcpOptimizeCommand { get; }
        public ICommand ScanCachesCommand { get; }
        public ICommand ClearCacheCommand { get; }
        public ICommand RefreshProcessesCommand { get; }
        public ICommand ToggleAutoModeCommand { get; }
        public ICommand RunDnsBenchmarkCommand { get; }
        public ICommand ApplyDnsCommand { get; }
        public ICommand RestoreDnsCommand { get; }
        public ICommand ToggleNagleCommand { get; }
        public ICommand ResetNetworkStackCommand { get; }
        public ICommand DetectWifiBandCommand { get; }

        private CancellationTokenSource? _speedTestCts;

        public MainViewModel()
        {
            OptimizeRamCommand = new RelayCommand(async _ => await OptimizeRamAsync());
            ToggleGamingModeCommand = new RelayCommand(_ => ToggleGamingMode());
            ToggleStreamingModeCommand = new RelayCommand(_ => ToggleStreamingMode());
            RunLagAnalysisCommand = new RelayCommand(async _ => await RunLagAnalysisAsync());
            RunFullOptimizationCommand = new RelayCommand(async _ => await RunFullOptimizationAsync());
            RunSpeedTestCommand = new RelayCommand(async _ => await RunSpeedTestAsync(), _ => !SpeedTestRunning);
            FlushDnsCommand = new RelayCommand(async _ => await FlushDnsAsync());
            ToggleTcpOptimizeCommand = new RelayCommand(async _ => await ToggleTcpOptimizeAsync());
            ScanCachesCommand = new RelayCommand(async _ => await ScanCachesAsync());
            ClearCacheCommand = new RelayCommand(async p => await ClearCacheAsync(p));
            RefreshProcessesCommand = new RelayCommand(async _ => await RefreshProcessesAsync());
            ToggleAutoModeCommand = new RelayCommand(_ => ToggleAutoMode());
            RunDnsBenchmarkCommand = new RelayCommand(async _ => await RunDnsBenchmarkAsync());
            ApplyDnsCommand = new RelayCommand(async p => await ApplyDnsAsync(p));
            RestoreDnsCommand = new RelayCommand(async _ => await RestoreDnsAsync());
            ToggleNagleCommand = new RelayCommand(async _ => await ToggleNagleAsync());
            ResetNetworkStackCommand = new RelayCommand(async _ => await ResetNetworkStackAsync());
            DetectWifiBandCommand = new RelayCommand(async _ => await DetectWifiBandAsync());

            // Load initial auto mode state
            AutoMode = AppSettings.Instance.AutoModeEnabled;
            NagleDisabled = AppSettings.Instance.NagleDisabled;

            SubscribeToEvents();
            LoadInitialData();
        }

        private void SubscribeToEvents()
        {
            EventBus.Instance.Subscribe<SystemMetricsUpdatedEvent>(OnSystemMetrics);
            EventBus.Instance.Subscribe<NetworkMetricsUpdatedEvent>(OnNetworkMetrics);
            EventBus.Instance.Subscribe<GameDetectedEvent>(OnGameDetected);
            EventBus.Instance.Subscribe<GameEndedEvent>(OnGameEnded);
            EventBus.Instance.Subscribe<LagWarningEvent>(OnLagWarning);
            EventBus.Instance.Subscribe<OptimizationAppliedEvent>(OnOptimizationApplied);
            EventBus.Instance.Subscribe<ModeChangedEvent>(OnModeChanged);
        }

        private void LoadInitialData()
        {
            // Delay initial loading to avoid slow startup
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(3000); // Let UI render first
                    LoadReports();
                    // Don't scan caches/processes at startup - they load on page visit
                }
                catch (Exception ex)
                {
                    AddLog($"Init error: {ex.Message}");
                }
            });
        }

        // ── Event handlers ───────────────────────────────────────────────────

        private void OnSystemMetrics(SystemMetricsUpdatedEvent e)
        {
            var m = e.Metrics;
            CpuUsage = m.CpuUsagePercent;
            RamUsage = m.RamUsagePercent;
            DiskUsage = m.DiskActivityPercent;
            RamUsedText = $"{m.UsedRamGb:F1} GB / {m.TotalRamGb:F1} GB";
            DiskText = $"{m.DiskTotalMbPerSec:F1} MB/s";
            CpuName = m.CpuName;

            // Update chart histories (keep last 60 samples)
            UpdateHistory(_cpuHistory, m.CpuUsagePercent);
            UpdateHistory(_ramHistory, m.RamUsagePercent);

            // Collect session data if game is running
            var session = _engine.GameDetector.CurrentSession;
            if (session?.IsActive == true)
            {
                session.CpuSamples.Add(m.CpuUsagePercent);
                session.RamSamples.Add(m.RamUsagePercent);
            }

            // Auto lag prediction update
            var pred = _engine.LagPrediction.Latest;
            if (pred != null)
            {
                PredictionText = pred.PredictedLag
                    ? $"⚠ {pred.Reason} ({pred.Confidence:F0}% confidence)"
                    : "All metrics stable";
            }
        }

        private void OnNetworkMetrics(NetworkMetricsUpdatedEvent e)
        {
            var m = e.Metrics;
            Ping = Math.Round(m.PingMs, 0);
            Jitter = Math.Round(m.JitterMs, 1);
            PacketLoss = Math.Round(m.PacketLossPercent, 1);
            DownloadMbps = $"{m.DownloadMbps:F1} Mbps";
            UploadMbps = $"{m.UploadMbps:F1} Mbps";
            NetworkQuality = m.Quality.ToString();
            UpdateHistory(_pingHistory, m.PingMs);

            // Collect session data
            var session = _engine.GameDetector.CurrentSession;
            if (session?.IsActive == true)
            {
                session.PingSamples.Add(m.PingMs);
                session.JitterSamples.Add(m.JitterMs);
                session.PacketLossSamples.Add(m.PacketLossPercent);
                if (m.PingMs > AppSettings.Instance.NetworkLatencyCriticalMs)
                    session.LagSpikeCount++;
            }
        }

        private void OnGameDetected(GameDetectedEvent e)
        {
            ActiveGame = e.GameName;
            ActivePlatform = e.Platform;
            GameRunning = true;
            AddLog($"Game detected: {e.GameName} ({e.Platform})");
        }

        private void OnGameEnded(GameEndedEvent e)
        {
            ActiveGame = "No game detected";
            ActivePlatform = "";
            GameRunning = false;
            AddLog($"Game session ended: {e.GameName}");
            LoadReports();
        }

        private void OnLagWarning(LagWarningEvent e)
        {
            LagWarningVisible = true;
            PredictionText = $"⚠ {e.Detail}";
            AddLog($"Lag warning: {e.Cause} – {e.Detail}");
        }

        private void OnOptimizationApplied(OptimizationAppliedEvent e)
        {
            AddLog($"✓ {e.ActionName}: {e.Description}");
            StatusText = e.ActionName;
        }

        private void OnModeChanged(ModeChangedEvent e)
        {
            if (e.Mode == AppMode.Gaming) GamingMode = e.IsActive;
            else if (e.Mode == AppMode.Streaming) StreamingMode = e.IsActive;
        }

        // ── Commands impl ────────────────────────────────────────────────────

        private async Task OptimizeRamAsync()
        {
            IsBusy = true;
            StatusText = _loc["OptimizingRam"];
            AddLog(_loc["OptimizingRam"]);
            try
            {
                var freed = await _engine.RAMManager.OptimizeAsync();
                var msg = string.Format(_loc["RamOptComplete"], $"{freed / 1_048_576f:F0}");
                AddLog(msg);
                StatusText = msg;
                ShowResult(true, msg);
            }
            catch (Exception ex)
            {
                ShowResult(false, $"{_loc["RamOptFailed"]}: {ex.Message}");
            }
            finally { IsBusy = false; }
        }

        private void ToggleGamingMode()
        {
            if (_gamingMode) _engine.SetMode(AppMode.Gaming, false);
            else _engine.SetMode(AppMode.Gaming, true);
        }

        private void ToggleStreamingMode()
        {
            if (_streamingMode) _engine.SetMode(AppMode.Streaming, false);
            else _engine.SetMode(AppMode.Streaming, true);
        }

        private async Task RunLagAnalysisAsync()
        {
            IsBusy = true;
            StatusText = _loc["AnalyzingLag"];
            AddLog(_loc["AnalyzingLag"]);
            try
            {
                var result = await _engine.LagAnalysis.AnalyzeAsync();
                LastAnalysis = result;
                LagSummary = result.Summary;
                LagSeverityText = result.Severity.ToString();
                AddLog($"{_loc["LagAnalysisComplete"]}: {result.Summary}");
                ShowResult(true, _loc["LagAnalysisComplete"]);
            }
            catch (Exception ex) { ShowResult(false, $"{_loc["AnalysisFailed"]}: {ex.Message}"); }
            finally { IsBusy = false; }
        }

        private async Task RunFullOptimizationAsync()
        {
            IsBusy = true;
            StatusText = _loc["RunningFullOpt"];
            AddLog(_loc["RunningFullOpt"]);
            try
            {
                await _engine.Optimization.RunFullOptimizationAsync();
                ShowResult(true, _loc["FullOptComplete"]);
            }
            catch (Exception ex)
            {
                ShowResult(false, $"{_loc["OptFailed"]}: {ex.Message}");
            }
            finally { IsBusy = false; }
        }

        private async Task RunSpeedTestAsync()
        {
            _speedTestCts = new CancellationTokenSource();
            SpeedTestRunning = true;
            SpeedTestComplete = false;
            SpeedTestDownload = "–";
            SpeedTestUpload = "–";
            SpeedTestPing = "–";

            try
            {
                var progress = new Progress<SpeedTestProgress>(p =>
                {
                    SpeedTestStatus = p.Stage;
                    SpeedTestProgress = p.Percent;
                    if (p.CurrentMbps > 0)
                        SpeedTestDownload = $"{p.CurrentMbps:F1} Mbps";
                });

                var result = await _engine.SpeedTest.RunAsync(progress, _speedTestCts.Token);

                if (result.Success)
                {
                    SpeedTestDownload = $"{result.DownloadMbps:F2} Mbps";
                    SpeedTestUpload = $"{result.UploadMbps:F2} Mbps";
                    SpeedTestPing = $"{result.PingMs:F0} ms";
                    SpeedTestStatus = _loc["SpeedTestComplete"];
                    SpeedTestComplete = true;
                    AddLog($"Speed test: ↓{result.DownloadMbps:F1} Mbps  ↑{result.UploadMbps:F1} Mbps  Ping:{result.PingMs:F0}ms");
                }
                else
                {
                    SpeedTestStatus = _loc["SpeedTestFailed"];
                    AddLog(_loc["SpeedTestFailed"]);
                }
            }
            catch (OperationCanceledException)
            {
                SpeedTestStatus = _loc["SpeedTestCancelled"];
            }
            catch (Exception ex)
            {
                SpeedTestStatus = $"Error: {ex.Message}";
            }
            finally
            {
                SpeedTestRunning = false;
                SpeedTestProgress = 0;
            }
        }

        private async Task FlushDnsAsync()
        {
            IsBusy = true;
            StatusText = _loc["FlushingDns"];
            AddLog(_loc["FlushingDns"]);
            try
            {
                bool ok = await _engine.NetworkOptimizer.FlushDnsAsync();
                if (ok)
                    ShowResult(true, _loc["DnsFlushOk"]);
                else
                    ShowResult(false, _loc["DnsFlushFail"]);
            }
            catch (Exception ex)
            {
                ShowResult(false, $"{_loc["DnsFlushFail"]}: {ex.Message}");
            }
            finally { IsBusy = false; }
        }

        private async Task ToggleTcpOptimizeAsync()
        {
            IsBusy = true;
            try
            {
                if (_tcpOptimized)
                {
                    StatusText = _loc["RestoringTcp"];
                    await _engine.NetworkOptimizer.RestoreTcpDefaultsAsync();
                    TcpOptimized = false;
                    TcpOptimizeText = _loc["ApplyTCP"];
                    ShowResult(true, _loc["TcpRestored"]);
                }
                else
                {
                    StatusText = _loc["ApplyingTcp"];
                    await _engine.NetworkOptimizer.ApplyTcpOptimizationsAsync();
                    TcpOptimized = true;
                    TcpOptimizeText = _loc["RestoreTCP"];
                    ShowResult(true, _loc["TcpApplied"]);
                }
            }
            catch (Exception ex)
            {
                ShowResult(false, $"{_loc["TcpFailed"]}: {ex.Message}");
            }
            finally { IsBusy = false; }
        }

        private async Task ScanCachesAsync()
        {
            IsBusy = true;
            StatusText = _loc["ScanningCaches"];
            try
            {
                var list = await _engine.CacheManager.ScanAllAsync();
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    Caches.Clear();
                    foreach (var c in list) Caches.Add(c);
                });
                ShowResult(true, string.Format(_loc["CacheScanDone"], list.Count));
            }
            catch (Exception ex)
            {
                ShowResult(false, $"Cache scan error: {ex.Message}");
            }
            finally { IsBusy = false; }
        }

        private async Task ClearCacheAsync(object? param)
        {
            if (param is BrowserType browser)
            {
                IsBusy = true;
                StatusText = string.Format(_loc["ClearingCache"], browser);
                try
                {
                    AddLog(string.Format(_loc["ClearingCache"], browser));
                    long freed = await _engine.CacheManager.ClearCacheAsync(browser);
                    ShowResult(true, string.Format(_loc["CacheClearDone"], browser, $"{freed / 1_048_576f:F0}"));
                    await ScanCachesAsync();
                }
                catch (Exception ex)
                {
                    ShowResult(false, $"Cache clear error: {ex.Message}");
                }
                finally { IsBusy = false; }
            }
        }

        private async Task RefreshProcessesAsync()
        {
            IsBusy = true;
            try
            {
                var snap = await Task.Run(() => _engine.ProcessManager.GetSnapshot());
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    Processes.Clear();
                    foreach (var p in snap.Take(100)) Processes.Add(p);
                });
            }
            catch (Exception ex)
            {
                AddLog($"Process refresh error: {ex.Message}");
            }
            finally { IsBusy = false; }
        }

        // ── New commands impl ─────────────────────────────────────────────────

        private void ToggleAutoMode()
        {
            AutoMode = !AutoMode;
            if (AutoMode)
                _engine.AutoOptimizer.Enable();
            else
                _engine.AutoOptimizer.Disable();

            AppSettings.Instance.AutoModeEnabled = AutoMode;
            AppSettings.Instance.Save();
            ShowResult(true, AutoMode ? _loc["AutoModeOn"] : _loc["AutoModeOff"]);
        }

        private async Task RunDnsBenchmarkAsync()
        {
            DnsBenchmarkRunning = true;
            IsBusy = true;
            StatusText = _loc["RunningDnsBench"];
            try
            {
                var results = await _engine.DnsBenchmark.BenchmarkAsync();
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    _dnsResults.Clear();
                    foreach (var r in results) _dnsResults.Add(r);
                });
                if (results.Count > 0)
                    ShowResult(true, string.Format(_loc["DnsBenchDone"], results[0].Name, $"{results[0].AverageMs:F0}"));
            }
            catch (Exception ex) { ShowResult(false, $"DNS benchmark failed: {ex.Message}"); }
            finally { DnsBenchmarkRunning = false; IsBusy = false; }
        }

        private async Task ApplyDnsAsync(object? param)
        {
            if (param is DnsBenchmarkResult dns)
            {
                IsBusy = true;
                StatusText = string.Format(_loc["ApplyingDns"], dns.Name);
                try
                {
                    bool ok = await _engine.DnsBenchmark.ApplyDnsAsync(dns.PrimaryIp, dns.SecondaryIp);
                    ShowResult(ok, ok ? string.Format(_loc["DnsApplied"], dns.Name, dns.PrimaryIp) : _loc["DnsApplyFail"]);
                }
                catch (Exception ex) { ShowResult(false, $"DNS apply error: {ex.Message}"); }
                finally { IsBusy = false; }
            }
        }

        private async Task RestoreDnsAsync()
        {
            IsBusy = true;
            StatusText = _loc["RestoringDns"];
            try
            {
                bool ok = await _engine.DnsBenchmark.RestoreAutoDnsAsync();
                ShowResult(ok, ok ? _loc["DnsRestored"] : _loc["DnsRestoreFail"]);
            }
            catch (Exception ex) { ShowResult(false, $"DNS restore error: {ex.Message}"); }
            finally { IsBusy = false; }
        }

        private async Task ToggleNagleAsync()
        {
            IsBusy = true;
            try
            {
                if (NagleDisabled)
                {
                    StatusText = _loc["EnablingNagle"];
                    await _engine.AdapterOptimizer.EnableNagleAsync();
                    NagleDisabled = false;
                    ShowResult(true, _loc["NagleOnResult"]);
                }
                else
                {
                    StatusText = _loc["DisablingNagle"];
                    await _engine.AdapterOptimizer.DisableNagleAsync();
                    NagleDisabled = true;
                    ShowResult(true, _loc["NagleOffResult"]);
                }
            }
            catch (Exception ex) { ShowResult(false, $"Nagle toggle error: {ex.Message}"); }
            finally { IsBusy = false; }
        }

        private async Task ResetNetworkStackAsync()
        {
            IsBusy = true;
            StatusText = _loc["ResettingNetwork"];
            try
            {
                bool ok = await _engine.AdapterOptimizer.ResetNetworkStackAsync();
                ShowResult(ok, ok ? _loc["NetworkResetOk"] : _loc["NetworkResetFail"]);
            }
            catch (Exception ex) { ShowResult(false, $"Network reset error: {ex.Message}"); }
            finally { IsBusy = false; }
        }

        private async Task DetectWifiBandAsync()
        {
            try
            {
                WifiBandInfo = await _engine.AdapterOptimizer.DetectWifiBandAsync();
            }
            catch { WifiBandInfo = _loc["DetectionFailed"]; }
        }

        private void LoadReports()
        {
            var reps = _engine.Reports.GetHistory();
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Reports.Clear();
                foreach (var r in reps) Reports.Add(r);
            });
        }

        private void AddLog(string msg)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _activityLog.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
                while (_activityLog.Count > 200) _activityLog.RemoveAt(_activityLog.Count - 1);
            });
        }

        private void ShowResult(bool success, string message)
        {
            LastResultText = message;
            LastResult = success;
            AddLog(message);
            StatusText = message;
        }

        private static void UpdateHistory(ObservableCollection<double> col, double value)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (col.Count >= 60)
                    col.RemoveAt(0);
                col.Add(value);
            });
        }
    }

    // ── Simple ICommand implementation ───────────────────────────────────────
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? p) => _canExecute?.Invoke(p) ?? true;
        public void Execute(object? p) => _execute(p);
    }
}
