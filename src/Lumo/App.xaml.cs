using System.Windows;
using Lumo.Core;
using Lumo.Services;
using Lumo.UI;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Lumo;

public partial class App : Application
{
    private Mutex? _mutex;
    private TrayController? _tray;
    private LauncherWindow? _window;
    private Settings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Last-resort exception capture: log everything, never crash on a UI hiccup.
        DispatcherUnhandledException += (_, args) =>
        {
            DiagnosticLogger.LogException("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                DiagnosticLogger.LogException("AppDomain.UnhandledException", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DiagnosticLogger.LogException("TaskScheduler.Unobserved", args.Exception);
            args.SetObserved();
        };

        DiagnosticLogger.Log("Startup", $"Lumo v1.1 starting (PID {Environment.ProcessId})");

        try
        {
            _mutex = SingleInstance.TryAcquireFirst();
            if (_mutex is null)
            {
                // Second launch (e.g. desktop shortcut clicked while running) → show existing window.
                SingleInstance.SignalExistingToShow();
                DiagnosticLogger.Log("Startup", "Second instance detected — signalled existing, exiting");
                Shutdown(0);
                return;
            }

            _settings = Settings.Load();
            _window = new LauncherWindow(_settings);
            MainWindow = _window;

            SingleInstance.StartShowServer(() =>
                Dispatcher.InvokeAsync(() => _window.ActivateLauncher()));

            _tray = new TrayController(
                _settings,
                openLauncher: () => Dispatcher.InvokeAsync(() => _window.ActivateLauncher()),
                exit: () => Dispatcher.InvokeAsync(() =>
                {
                    _window.PrepareForExit();
                    Shutdown(0);
                }));

            _tray.ThemeChanged += () => Dispatcher.InvokeAsync(() => _window.ApplyTheme());

            // FIX: the window is shown immediately at launch — starting Lumo from the
            // desktop shortcut now always opens the search window.
            _window.Show();
            _window.ActivateLauncher();

            DiagnosticLogger.Log("Startup", $"Startup complete. Hotkey: {_window.ActiveHotkeyDescription ?? "none"}");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Startup.FATAL", ex);
            MessageBox.Show(
                "Lumo failed to start.\n\n" +
                "Details were written to:\n" + AppPaths.LogFile + "\n\n" + ex.Message,
                "Lumo", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _tray?.Dispose(); } catch { }
        try { _window?.PrepareForExit(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        DiagnosticLogger.Log("Exit", $"Lumo exited (code {e.ApplicationExitCode})");
        base.OnExit(e);
    }
}
