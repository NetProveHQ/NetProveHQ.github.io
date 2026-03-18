using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NetProve.Core;

namespace NetProve
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        private static bool _mutexOwned;
        private CancellationTokenSource? _pipeCts;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string mutexName = "Global\\NetProve_SingleInstance_8F3A";
            _mutex = new Mutex(true, mutexName, out bool createdNew);
            _mutexOwned = createdNew;

            if (!createdNew)
            {
                // Signal the running instance to show its window
                try
                {
                    using var client = new NamedPipeClientStream(".", "NetProve_ShowWindow", PipeDirection.Out);
                    client.Connect(2000);
                    using var writer = new StreamWriter(client);
                    writer.Write("SHOW");
                    writer.Flush();
                }
                catch { }
                // Dispose our non-owned handle and exit without touching the real owner's mutex
                _mutex.Dispose();
                _mutex = null;
                Shutdown();
                return;
            }

            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            // Start listening for second-instance signals
            _pipeCts = new CancellationTokenSource();
            StartPipeServer(_pipeCts.Token);

            // CoreEngine.Instance.Start() is called by MainWindow after ContentRendered
            // so the window is fully painted before background services begin.
        }

        private void StartPipeServer(CancellationToken ct)
        {
            Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream("NetProve_ShowWindow", PipeDirection.In);
                        await server.WaitForConnectionAsync(ct);
                        using var reader = new StreamReader(server);
                        var message = await reader.ReadToEndAsync(ct);
                        if (message == "SHOW")
                        {
                            Dispatcher.Invoke(() =>
                            {
                                var win = MainWindow;
                                if (win != null)
                                {
                                    if (win.IsVisible && win.WindowState != WindowState.Minimized)
                                    {
                                        // Window is already open and visible — flash taskbar to indicate
                                        win.Activate();
                                        win.Focus();
                                    }
                                    else
                                    {
                                        // Window is hidden or minimized — restore it
                                        win.Show();
                                        win.WindowState = WindowState.Normal;
                                        win.Activate();
                                        win.Focus();
                                    }
                                }
                            });
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, ct);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"UI Error:\n{e.Exception}", "NetProve Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            MessageBox.Show($"Background Error:\n{e.Exception?.InnerException?.Message ?? e.Exception?.Message}",
                "NetProve Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.SetObserved();
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Fatal Error:\n{(e.ExceptionObject as Exception)?.Message}",
                "NetProve Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _pipeCts?.Cancel();
            _pipeCts?.Dispose();

            // Only stop the engine / save settings when this is the primary instance
            if (_mutexOwned)
            {
                CoreEngine.Instance.Stop();
                AppSettings.Instance.Save();
                try { _mutex?.ReleaseMutex(); } catch { }
            }
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
