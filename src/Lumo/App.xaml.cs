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
    private AiChatWindow? _aiChatWindow;      // v2.3.0-alpha.3 — dedicated AI chat tab
    private Settings _settings = new();
    private ShortcutStore? _shortcuts;
    private MacroRecorder? _recorder;
    private ClipboardHistory? _clips;
    private UsageStore? _usage;                      // v2.1 — MRU ranking
    private Favourites? _favs;                       // v2.2 — pinned favourites

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

        DiagnosticLogger.Log("Startup", $"Lumo v2.1.0 starting (PID {Environment.ProcessId})");

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
            _recorder = new MacroRecorder();
            _clips = new ClipboardHistory();          // v1.6 — clipboard history (UI-thread timer)
            _usage = new UsageStore();                // v2.1 — launch frequency for MRU ranking
            _usage.Load();
            _favs = new Favourites();                 // v2.2 — pinned favourites
            _favs.Load();
            _window = new LauncherWindow(_settings, _shortcuts, _recorder, _clips, _usage, _favs);
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

            // v2.3.0-alpha.2 — deep-link to a settings page (AI setup row → page 6)
            _window.SettingsPageRequested += page =>
            {
                try { OpenSettings(initialPage: page); } catch (Exception ex) { DiagnosticLogger.LogException("App.SettingsPage", ex); }
            };

            // v2.3.0-alpha.3 — the dedicated AI chat window (AI/ prefix). Singleton:
            // a second AI/ just focuses the open window and forwards the new question.
            _window.AiChatRequested += query =>
            {
                try { OpenAiChat(query); } catch (Exception ex) { DiagnosticLogger.LogException("App.OpenAiChat", ex); }
            };

            // v1.5 — a finished recording opens the visual builder with the captured steps
            _window.RecordFinishRequested += (steps, name) =>
            {
                try
                {
                    if (_shortcuts is null) return;
                    var dlg = new ShortcutEditorWindow(_shortcuts, _settings, existing: null,
                                                       presetName: name, presetSteps: steps);
                    dlg.Owner = _window is { IsLoaded: true } ? _window : null;
                    dlg.ShowDialog();
                }
                catch (Exception ex) { DiagnosticLogger.LogException("App.RecordFinish", ex); }
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
                    try { _tray?.UpdateText($"Lumo v2.1.0 — press {active}"); } catch { }
                    return active;
                },
                rebuildIndex: () => { try { _window?.RebuildIndex(); } catch { } },
                shortcuts: _shortcuts,
                recordMacro: StartRecording,
                recordingActive: () => _recorder is { Active: true },
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

    /// <summary>v1.4/1.5 — open the shortcut editor (optionally pre-filling a name
    /// and/or the steps captured by the macro recorder).</summary>
    private void OpenShortcutEditor(string? presetName, List<MacroStep>? presetSteps = null, string? nameOverride = null)
    {
        try
        {
            if (_shortcuts is null) return;
            var dlg = new ShortcutEditorWindow(_shortcuts, _settings, existing: null,
                                               presetName: nameOverride ?? presetName, presetSteps: presetSteps);
            dlg.Owner = _window is { IsLoaded: true } ? _window : null;
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("App.OpenShortcutEditor", ex);
        }
    }

    /// <summary>
    /// v2.3.0-alpha.3 — open (or focus) the dedicated AI chat window. Singleton per
    /// app lifetime: a second AI/ activation just focuses it and forwards the new
    /// question. Reuses the launcher's AiService (one cache, one log stream).
    /// </summary>
    private void OpenAiChat(string? query)
    {
        try
        {
            if (_window is null) return;
            if (_aiChatWindow is { IsLoaded: true })
            {
                _aiChatWindow.Activate();
                if (!string.IsNullOrWhiteSpace(query)) _aiChatWindow.SendUserMessage(query!);
                return;
            }

            _aiChatWindow = new AiChatWindow(_settings, _window.Ai);
            // v2.3.0-alpha.4 — the chat's AI-off banner links straight to Settings → AI
            _aiChatWindow.SettingsRequested += () =>
            {
                try { OpenSettings(initialPage: 6); } catch (Exception ex) { DiagnosticLogger.LogException("App.AiChatSettings", ex); }
            };
            _aiChatWindow.Closed += (_, _) => _aiChatWindow = null;
            _aiChatWindow.Show();
            _aiChatWindow.Activate();
            if (!string.IsNullOrWhiteSpace(query)) _aiChatWindow.SendUserMessage(query!);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("App.OpenAiChat", ex);
        }
    }

    /// <summary>v1.5 — start a macro recording and surface the launcher to capture launches.
    /// v1.5.1: if a recording is already live, never restart it — that silently wiped
    /// everything captured so far; just surface the launcher instead.</summary>
    private void StartRecording()
    {
        try
        {
            if (_recorder is not { Active: true }) _recorder?.Start();
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _window?.ActivateLauncher();
                    if (_window is not null)
                    {
                        _window.RefreshStatusForRecording();
                    }
                }
                catch { }
            });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("App.StartRecording", ex); }
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
