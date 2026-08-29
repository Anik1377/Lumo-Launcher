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
    private SettingsWindow? _settingsWindow;
    private Settings _settings = new();
    private ShortcutStore? _shortcuts;

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

        DiagnosticLogger.Log("Startup", $"Lumo v1.4.1 starting (PID {Environment.ProcessId})");

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
            _shortcuts = new ShortcutStore();
            _window = new LauncherWindow(_settings, _shortcuts);
            MainWindow = _window;

            _window.SettingsRequested += () =>
            {
                try { OpenSettings(); } catch (Exception ex) { DiagnosticLogger.LogException("App.OpenSettings", ex); }
            };

            // v1.4 — shortcut creation & management from the launcher's /sc rows
            _window.ShortcutEditorRequested += preset =>
            {
                try { OpenShortcutEditor(preset); } catch (Exception ex) { DiagnosticLogger.LogException("App.ShortcutEditor", ex); }
            };
            _window.ManageShortcutsRequested += () =>
            {
                try { OpenSettings(initialPage: 4); } catch (Exception ex) { DiagnosticLogger.LogException("App.ManageShortcuts", ex); }
            };

            SingleInstance.StartShowServer(() =>
                Dispatcher.InvokeAsync(() => _window.ActivateLauncher()));

            _tray = new TrayController(
                _settings,
                openLauncher: () => Dispatcher.InvokeAsync(() => _window.ActivateLauncher()),
                openSettings: () => Dispatcher.InvokeAsync(() => OpenSettings()),
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

    /// <summary>Open (or focus) the settings window — singleton per app lifetime.</summary>
    private void OpenSettings(int initialPage = 0)
    {
        try
        {
            if (_settingsWindow is { IsLoaded: true })
            {
                _settingsWindow.Activate();
                if (initialPage > 0) _settingsWindow.SelectPage(initialPage);
                return;
            }

            _settingsWindow = new SettingsWindow(
                _settings,
                applyAppearance: () => { try { _window?.RefreshAppearance(); } catch { } },
                applyHotkey: () =>
                {
                    string active = _window?.ReapplyHotkey() ?? "(none)";
                    try { _tray?.UpdateText($"Lumo v1.4.1 — press {active}"); } catch { }
                    return active;
                },
                rebuildIndex: () => { try { _window?.RebuildIndex(); } catch { } },
                shortcuts: _shortcuts,
                initialPage: initialPage);

            _settingsWindow.Topmost = true; // stay above other apps while customizing
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("App.OpenSettings", ex);
        }
    }

    /// <summary>v1.4 — open the shortcut editor (optionally pre-filling a name).</summary>
    private void OpenShortcutEditor(string? presetName)
    {
        try
        {
            if (_shortcuts is null) return;
            var dlg = new ShortcutEditorWindow(_shortcuts, _settings, existing: null, presetName);
            dlg.Owner = _window is { IsLoaded: true } ? _window : null;
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("App.OpenShortcutEditor", ex);
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
