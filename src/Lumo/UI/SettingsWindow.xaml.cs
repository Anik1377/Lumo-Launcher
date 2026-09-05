using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lumo.Core;
using Lumo.Native;
using Lumo.Services;
using Appearance = Lumo.Services.Appearance;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using RadioButton = System.Windows.Controls.RadioButton;

namespace Lumo.UI;

/// <summary>
/// Advanced settings &amp; customization window (v1.3, macOS System Settings style).
///
/// Appearance edits apply LIVE (theme, accent, glow border style/speed) so the user
/// sees the effect immediately on the launcher behind and in the preview strip.
/// Everything is persisted only on Save; Cancel restores the snapshot taken at open.
/// v1.3 — auto theme, UI-animations master switch, and animated page transitions.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly Settings _settings;      // the live instance shared with App/launcher
    private readonly Settings _snapshot;      // values at open — Cancel restores these
    private readonly Action _applyAppearance;
    private readonly Func<string> _applyHotkey;
    private readonly Action? _applyDeckHotkeys;   // v3.0 — deck numpad re-registration
    private readonly Action _rebuildIndex;
    private readonly ShortcutStore _shortcuts;   // v1.4
    private readonly Action? _recordMacro;       // v1.5
    private readonly Func<bool>? _recordingActive; // v1.5.1 — is a recording live right now?
    private readonly PluginStore? _plugins;      // v2.5 — Task 4.2 JSON plugins
    private readonly FirstPartyStore? _firstParty;   // v2.6.0-alpha.2 — first-party catalog + downloads
    private List<FirstPartyEntry>? _firstPartyCatalog;   // last fetched catalog (null until Browse)
    private readonly HashSet<string> _firstPartyBusy = new(StringComparer.OrdinalIgnoreCase);  // ids mid-install
    private readonly Func<ShortcutDef, string>? _shortcutHotkeyFeedback;   // v2.5 — Task 4.3
    private readonly UpdateService? _updates;    // v2.6 — Task 5.1 update check + staged download
    private readonly Action? _replayOnboarding;  // v2.6 — Task 5.3 replay the intro tour
    private readonly Action? _openDeck;          // v3.0.0-alpha.5 — "Open App Deck" (General page)
    private UpdateInfo? _updateInfo;             // newest release found this session (for the card)
    private string? _updateStagedPath;           // completed download, ready to open
    private CancellationTokenSource? _updateCts;  // cancels an in-flight download
    private int _initialPage = 0;

    private readonly List<(Border Box, string Hex)> _swatches = new();
    private bool _suppress;                   // guard while programmatically wiring controls
    private string _pendingHotkey = "";
    private bool _pendingStartWithWindows;
    // (_previewRotation removed in v2.0.1 — the rim comet lives on the launcher only;
    //  the settings preview card shows a static gradient of the style's palette)
    private bool _sourceReady;                // v1.8 — glass needs the HWND; set in OnSourceInitialized
    private CancellationTokenSource? _ollamaCts;   // v2.3.0-alpha.2 — cancels in-flight download/pull
    private bool _ollamaBusy;                 // v2.3.0-alpha.2 — one download/pull at a time (bounded)
    private readonly List<Button> _pullButtons = new();   // catalog Install buttons (rebuilt on each render)

    public SettingsWindow(Settings settings, Action applyAppearance, Func<string> applyHotkey, Action rebuildIndex,
                          ShortcutStore? shortcuts = null, Action? recordMacro = null,
                          Func<bool>? recordingActive = null, int initialPage = 0,
                          PluginStore? plugins = null,
                          Func<ShortcutDef, string>? shortcutHotkeyFeedback = null,
                          UpdateService? updates = null, Action? replayOnboarding = null,
                          Action? applyDeckHotkeys = null, Action? openDeck = null)
    {
        InitializeComponent();
        _settings = settings;
        _snapshot = settings.Clone();
        _applyAppearance = applyAppearance;
        _applyHotkey = applyHotkey;
        _rebuildIndex = rebuildIndex;
        _applyDeckHotkeys = applyDeckHotkeys;
        _shortcuts = shortcuts ?? new ShortcutStore();
        _recordMacro = recordMacro;
        _recordingActive = recordingActive;
        _plugins = plugins;
        if (_plugins is not null) _firstParty = new FirstPartyStore(_plugins);   // v2.6.0-alpha.2
        _shortcutHotkeyFeedback = shortcutHotkeyFeedback;
        _updates = updates;
        _replayOnboarding = replayOnboarding;
        _openDeck = openDeck;
        _initialPage = initialPage;

        BuildAccentSwatches();
        LoadFromSettings();
        ApplySelfTheme();
        UpdateRecordButton();   // v1.5.1 — reflect live recording state on open
        UpdatePreview();
        PlayEntrance();

        _shortcuts.Changed += () => Dispatcher.InvokeAsync(() => { try { LoadShortcutList(); } catch { } });
        LoadShortcutList();

        // v2.5 (Task 4.2) — live plugin list; the store raises Changed on every rescan
        if (_plugins is not null)
            _plugins.Changed += () => Dispatcher.InvokeAsync(() =>
            {
                try { LoadPluginList(); _firstPartyBusy.Clear(); LoadFirstPartyList(); } catch { }
            });
        LoadPluginList();

        LogPathText.Text = "Log: " + AppPaths.LogFile;
        string vs = "v" + AppVersion.Label;   // v2.4.0-alpha.7 — full label ("v2.4.0-alpha.7"), was truncated to v{major}.{minor}
        VersionText.Text = vs;
        AboutVersion.Text = vs;

        // v2.6 — data location (portable badge when the data folder follows the exe)
        DataPathText.Text = AppPaths.DataDir + (AppPaths.IsPortable ? "   (portable — travels with the exe)" : "");

        // v2.6 — Task 5.1 surface the update card state
        _updateInfo = _updates?.Latest;
        RefreshUpdateCard();

        // v3.0.0-alpha.5 — size Lumo's caches in the background (About → Storage & maintenance)
        _ = LoadCleanupListAsync();

        // open directly on the requested page (e.g. Shortcuts from the launcher)
        if (_initialPage > 0) SelectPage(_initialPage);
    }

    // ---------------------------------------------------------------- window chrome

    /// <summary>
    /// v2.0 — the settings window opens filling the whole work area (the "full screen
    /// app" feel, like the real Windows 11 Settings), while staying resizable and
    /// keeping the taskbar accessible.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _sourceReady = true;
        try
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left; Top = wa.Top;
            Width = wa.Width; Height = wa.Height;
        }
        catch { /* keep designed size */ }
        try { ApplySelfTheme(); }   // reapplies DWM chrome, then repaints
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OnSourceInitialized", ex); }
    }

    /// <summary>v2.0 — caption-bar minimize.</summary>
    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        try { WindowState = WindowState.Minimized; }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.Minimize", ex); }
    }

    /// <summary>Selects a sidebar page by index (checks the matching nav radio).</summary>
    public void SelectPage(int idx)
    {
        try
        {
            if (NavGeneral.Parent is StackPanel sp)
            {
                var rb = sp.Children.OfType<RadioButton>()
                    .FirstOrDefault(r => r.Tag?.ToString() == idx.ToString());
                if (rb is not null)
                {
                    rb.IsChecked = true;   // Checked → OnNavChanged → ShowPanel
                    return;
                }
            }
            ShowPanel(idx);
        }
        catch { try { ShowPanel(idx); } catch { } }
    }

    // ---------------------------------------------------------------- shortcuts (v1.4)

    private void LoadShortcutList()
    {
        var items = _shortcuts.Snapshot();
        ShortcutsList.ItemsSource = items;
        ShortcutsEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNewShortcut(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new ShortcutEditorWindow(_shortcuts, _settings, null, savedFeedback: _shortcutHotkeyFeedback) { Owner = this };
            dlg.ShowDialog();
            LoadShortcutList();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.NewShortcut", ex); }
    }

    /// <summary>v1.5 — record a macro from Settings: start the recorder, surface the launcher.
    /// v1.5.1 — the button reflects the live state; clicking while recording just
    /// surfaces the launcher (App.StartRecording no longer resets captures).</summary>
    private void OnRecordMacro(object sender, RoutedEventArgs e)
    {
        try
        {
            UpdateRecordButton();
            if (Owner is { } o) o.Activate();
            _recordMacro?.Invoke();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.RecordMacro", ex); }
    }

    /// <summary>v1.5.1 — keep the record button honest about the recorder state.</summary>
    private void UpdateRecordButton()
    {
        try
        {
            if (RecordMacroButton is null) return;
            bool live = _recordingActive?.Invoke() ?? false;
            RecordMacroButton.Content = live
                ? "⏹ Recording live — finish in the Lumo bar"
                : "⏺ Record macro";
            RecordMacroButton.ToolTip = live
                ? "A recording is running — open Lumo, launch a few things, then “Stop & save”"
                : "Capture what you launch in Lumo as macro steps";
        }
        catch { }
    }

    private void OnEditShortcut(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not ShortcutDef def) return;
            var live = _shortcuts.Find(def.Id);
            if (live is null) { LoadShortcutList(); return; }
            var dlg = new ShortcutEditorWindow(_shortcuts, _settings, live, savedFeedback: _shortcutHotkeyFeedback) { Owner = this };
            dlg.ShowDialog();
            LoadShortcutList();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.EditShortcut", ex); }
    }

    private void OnDeleteShortcut(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not ShortcutDef def) return;
            var answer = System.Windows.MessageBox.Show(
                $"Delete shortcut “{def.Name}” ?",
                "Lumo", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            _shortcuts.Remove(def.Id);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.DeleteShortcut", ex); }
    }

    // ---------------------------------------------------------------- plugins (v2.5 — DEV_PLAN Task 4.2)

    /// <summary>Bindable row for one installed plugin.</summary>
    public sealed class PluginRowVM
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Meta { get; init; } = "";
        public string Commands { get; init; } = "";
        public bool Enabled { get; set; }
    }

    private void LoadPluginList()
    {
        try
        {
            if (PluginsList is null) return;   // XAML not ready yet (ctor ordering)
            var defs = _plugins?.All() ?? new List<PluginDefinition>();
            PluginsList.ItemsSource = defs.Select(d => new PluginRowVM
            {
                Id = d.Id,
                Name = string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name,
                Meta = string.IsNullOrWhiteSpace(d.Author)
                    ? (string.IsNullOrWhiteSpace(d.Version) ? "" : "v" + d.Version)
                    : $"{d.Author}{(string.IsNullOrWhiteSpace(d.Version) ? "" : " · v" + d.Version)}",
                Commands = string.Join(",  ", d.Commands.Select(c => $"{c.Keyword} ({c.TypeName.ToLowerInvariant()})")),
                Enabled = _plugins!.IsEnabled(d.Id),
            }).ToList();
            PluginsEmpty.Visibility = defs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PluginsFolderText.Text = AppPaths.PluginsDir;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.LoadPluginList", ex); }
    }

    private void OnPluginToggle(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not PluginRowVM row) return;
            _plugins?.SetEnabled(row.Id, row.Enabled);   // row.Enabled is already the NEW checkbox state
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.PluginToggle", ex); }
    }

    private void OnOpenPluginsFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.PluginsDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OpenPluginsFolder", ex); }
    }

    private void OnRescanPlugins(object sender, RoutedEventArgs e)
    {
        try { _plugins?.Rescan(); LoadPluginList(); }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.RescanPlugins", ex); }
    }

    private void OnCopyStarterPlugin(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(Plugins.StarterJson);
            PluginActionStatus.Text = "Starter plugin.json copied — make a folder in the plugins dir, paste, save as plugin.json, then Rescan.";
            PluginActionStatus.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.CopyStarter", ex); }
    }

    // ------------------------------------------------- first-party plugins (v2.6.0-alpha.2)

    /// <summary>Bindable row for one catalog entry (Install button lives on the row).</summary>
    public sealed class FirstPartyRowVM
    {
        public string Name { get; init; } = "";
        public string Meta { get; init; } = "";
        public string Description { get; init; } = "";
        public string ButtonText { get; init; } = "Install";
        public bool Installable { get; init; } = true;
        public FirstPartyEntry Entry { get; init; } = new();
    }

    /// <summary>Fetches the official catalog from the Lumo repo (on demand — never at startup).</summary>
    private async void OnBrowseFirstParty(object sender, RoutedEventArgs e)
    {
        if (_firstParty is null || sender is not Button browse) return;
        try
        {
            browse.IsEnabled = false;
            ShowFirstPartyStatus("Fetching the catalog from GitHub…");
            var result = await _firstParty.FetchCatalogAsync();
            if (!result.Ok)
            {
                ShowFirstPartyStatus(result.Error ?? "could not fetch the catalog");
                return;
            }
            _firstPartyCatalog = result.Entries;
            LoadFirstPartyList();
            ShowFirstPartyStatus($"{result.Entries.Count} plugin(s) available — Install downloads the manifest into your plugins folder and activates it right away.");
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.BrowseFirstParty", ex); }
        finally { browse.IsEnabled = true; }
    }

    /// <summary>Installs one entry: download → validate → write → rescan. The row shows "Installing…" meanwhile.</summary>
    private async void OnInstallFirstParty(object sender, RoutedEventArgs e)
    {
        if (_firstParty is null || _firstPartyCatalog is null) return;
        if ((sender as FrameworkElement)?.DataContext is not FirstPartyRowVM row) return;
        if (sender is Button btn) btn.IsEnabled = false;
        try
        {
            _firstPartyBusy.Add(row.Entry.Id);
            LoadFirstPartyList();   // re-render with the busy state
            ShowFirstPartyStatus($"Installing {row.Entry.Name}…");
            var result = await _firstParty.InstallAsync(row.Entry);
            ShowFirstPartyStatus(result.Ok
                ? $"Installed {row.Entry.Name} — its keywords work immediately (type P/ or the keyword itself)."
                : $"{row.Entry.Name}: {result.Error}");
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.InstallFirstParty", ex); }
        finally
        {
            _firstPartyBusy.Remove(row.Entry.Id);
            LoadFirstPartyList();   // fresh install states
            LoadPluginList();       // the rescan inside InstallAsync may have changed the catalog
        }
    }

    /// <summary>Renders the cached catalog against the installed set; hides the list until a fetch happened.</summary>
    private void LoadFirstPartyList()
    {
        try
        {
            if (FirstPartyList is null) return;   // XAML not ready yet (ctor ordering)
            if (_firstPartyCatalog is null)
            {
                FirstPartyList.ItemsSource = Array.Empty<FirstPartyRowVM>();
                return;
            }
            FirstPartyList.ItemsSource = _firstPartyCatalog.Select(entry =>
            {
                var state = FirstParty.StateFor(entry, _firstParty?.FindInstalled(entry.Id));
                bool busy = _firstPartyBusy.Contains(entry.Id);
                return new FirstPartyRowVM
                {
                    Entry = entry,
                    Name = string.IsNullOrWhiteSpace(entry.Name) ? entry.Id : entry.Name,
                    Meta = string.Join(" · ", new[]
                    {
                        string.IsNullOrWhiteSpace(entry.Version) ? "" : "v" + entry.Version,
                        string.IsNullOrWhiteSpace(entry.Author) ? "" : "by " + entry.Author,
                        state == FirstPartyState.Missing ? "" : "installed",
                    }.Where(s => s.Length > 0)),
                    Description = entry.Description,
                    ButtonText = busy ? "Installing…" : FirstParty.ButtonLabel(state),
                    Installable = !busy,
                };
            }).ToList();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.LoadFirstPartyList", ex); }
    }

    private void ShowFirstPartyStatus(string text)
    {
        try
        {
            FirstPartyStatus.Text = text;
            FirstPartyStatus.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.FirstPartyStatus", ex); }
    }

    /// <summary>Copies the AI authoring prompt — paste into any AI chat, describe the plugin, get a plugin.json.</summary>
    private void OnCopyAiPrompt(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(Plugins.AiPrompt);
            ShowFirstPartyStatus("AI prompt copied — paste it into any AI chat, describe the plugin you want at the bottom, and save the answer as a plugin.json.");
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.CopyAiPrompt", ex); }
    }

    private void OnOpenPluginDocs(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = FirstParty.DocsUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OpenPluginDocs", ex); }
    }

    // ---------------------------------------------------------------- data location (v2.6 — Task 5.2) + tour (Task 5.3)

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.DataDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OpenDataFolder", ex); }
    }

    private void OnReplayTour(object sender, RoutedEventArgs e)
    {
        try { _replayOnboarding?.Invoke(); }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.ReplayTour", ex); }
    }

    // ---------------------------------------------------------------- updates (v2.6 — Task 5.1)

    /// <summary>Repaints the update card from _updateInfo / _updateStagedPath.
    /// Safe to call any time; every element exists after InitializeComponent.</summary>
    private void RefreshUpdateCard()
    {
        try
        {
            UpdatesToggle.IsChecked = _settings.UpdatesEnabled;

            if (_updateStagedPath is not null)
            {
                UpdateStatusText.Text = "Downloaded — extract the zip and replace your Lumo.exe with the one inside to finish installing.";
                DownloadUpdateButton.Visibility = Visibility.Collapsed;
                OpenStagedButton.Visibility = Visibility.Visible;
                UpdateProgress.Visibility = Visibility.Collapsed;
                return;
            }

            if (_updateInfo is { } info)
            {
                var size = info.ZipBytes > 0 ? $" · {info.ZipBytes / 1024.0 / 1024.0:0.0} MB" : "";
                UpdateStatusText.Text = $"Lumo {info.Version} is available (you are running {AppVersion.Label}{size}). " +
                                        "The zip is staged into your data folder; extract it over your current Lumo.exe.";
                DownloadUpdateButton.Visibility = Visibility.Visible;
                OpenStagedButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                UpdateStatusText.Text = _settings.UpdatesEnabled
                    ? "Automatic checks are on — Lumo checks GitHub about once a day."
                    : "Automatic checks are off — press “Check now” whenever you are curious.";
                DownloadUpdateButton.Visibility = Visibility.Collapsed;
                OpenStagedButton.Visibility = Visibility.Collapsed;
            }
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.RefreshUpdateCard", ex); }
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        if (_updates is null) { UpdateStatusText.Text = "Updates are not wired up in this build."; return; }
        try
        {
            CheckUpdatesButton.IsEnabled = false;
            UpdateStatusText.Text = "Checking GitHub Releases…";
            var found = await _updates.CheckNowAsync();
            _updateInfo = found;
            if (found is null) UpdateStatusText.Text = $"You are up to date (v{AppVersion.Label}) — or GitHub was unreachable; the log knows which.";
            RefreshUpdateCard();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Settings.CheckUpdates", ex);
            UpdateStatusText.Text = "The check failed — details are in the log.";
        }
        finally { CheckUpdatesButton.IsEnabled = true; }
    }

    private async void OnDownloadUpdate(object sender, RoutedEventArgs e)
    {
        if (_updates is null || _updateInfo is not { } info) return;
        try
        {
            DownloadUpdateButton.IsEnabled = false;
            UpdateProgress.Visibility = Visibility.Visible;
            UpdateProgress.Value = 0;
            UpdateStatusText.Text = $"Downloading Lumo {info.Version}…";
            _updateCts = new CancellationTokenSource();
            var path = await _updates.DownloadAsync(info, p => Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (p >= 0) UpdateProgress.Value = p;
                    if (p is > 0 and < 100) UpdateStatusText.Text = $"Downloading Lumo {info.Version}… {p:0}%";
                }
                catch { }
            }), _updateCts.Token);

            if (path is not null)
            {
                _updateStagedPath = path;
                UpdateStatusText.Text = "Downloaded — extract the zip and replace your Lumo.exe with the one inside to finish installing.";
                DownloadUpdateButton.Visibility = Visibility.Collapsed;
                OpenStagedButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateStatusText.Text = "The download failed — details are in the log. You can always grab the zip from the GitHub release page.";
            }
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Settings.DownloadUpdate", ex);
            UpdateStatusText.Text = "The download failed — details are in the log.";
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
        finally
        {
            DownloadUpdateButton.IsEnabled = _updateStagedPath is null;
            _updateCts?.Dispose();
            _updateCts = null;
        }
    }

    private void OnOpenStagedUpdate(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_updateStagedPath is not { } path || !File.Exists(path))
            {
                UpdateStatusText.Text = "The staged zip is gone (data folder cleaned?) — press “Check now” and download again.";
                OpenStagedButton.Visibility = Visibility.Collapsed;
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OpenStaged", ex); }
    }

    /// <summary>Window springs in — fade + gentle scale, a quiet Fluent touch.</summary>
    private void PlayEntrance()
    {
        try
        {
            if (!_settings.AnimationsEnabled) return;
            RootScale.ScaleX = RootScale.ScaleY = 0.98;
            Root.Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease };
            var scale = new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease };
            Root.BeginAnimation(OpacityProperty, fade);
            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.Entrance", ex); }
    }

    private void ApplySelfTheme()
    {
        try
        {
            var t = ThemeService.Apply(this, _settings);

            // v2.0 — DWM chrome only (rounded corners + dark-mode context); no acrylic.
            if (_sourceReady)
                GlassBackdrop.Apply(this, t.Dark);

            float r = GlassBackdrop.IsWin11 ? 8f : 0f;
            Root.Background = new SolidColorBrush(t.Panel);
            Root.CornerRadius = new CornerRadius(r);
            Root.BorderBrush = new SolidColorBrush(t.Border);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.ApplySelfTheme", ex); }
    }

    private static Color FromRgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    // ---------------------------------------------------------------- load values

    private void LoadFromSettings()
    {
        _suppress = true;
        try
        {
            _pendingStartWithWindows = StartupManager.IsEnabled();
            StartWithWindowsToggle.IsChecked = _pendingStartWithWindows;
            HideOnFocusLossToggle.IsChecked = _settings.HideOnFocusLoss;
            AnimationsToggle.IsChecked = _settings.AnimationsEnabled;
            UpdatesToggle.IsChecked = _settings.UpdatesEnabled;   // v2.6 — Task 5.1

            switch (_settings.WebEngine?.ToLowerInvariant())
            {
                case "bing": EngineBing.IsChecked = true; break;
                case "duckduckgo": EngineDdg.IsChecked = true; break;
                default: EngineGoogle.IsChecked = true; break;
            }

            BorderEffectToggle.IsChecked = _settings.BorderEffect;
            // v3.0 — the border style pills / comet speed / glow sliders are gone with
            // the comet engine; the rim is the edge shine now (BorderEffect only).
            DeckGlobalHotkeysToggle.IsChecked = _settings.DeckGlobalHotkeys;   // v3.0 — App Deck
            DeckGlobalHotkeysToggle.Click += (_, _) =>
            {
                if (_suppress) return;
                _settings.DeckGlobalHotkeys = DeckGlobalHotkeysToggle.IsChecked == true;
                FooterHint.Text = "Numpad hotkeys will apply on Save (or Cancel to undo)";
            };

            if (string.Equals(_settings.Theme, "light", StringComparison.OrdinalIgnoreCase))
                ThemeLight.IsChecked = true;
            else if (string.Equals(_settings.Theme, "auto", StringComparison.OrdinalIgnoreCase))
                ThemeAuto.IsChecked = true;
            else
                ThemeDark.IsChecked = true;

            AccentHexBox.Text = _settings.AccentColor;
            MarkSelectedSwatch(_settings.AccentColor);

            // v3.0 — theme gallery (presets + imported files, click to apply live)
            BuildThemeGallery();

            // v2.0.1 — advanced fine-tuning
            WidthSlider.Value = Math.Clamp(_settings.WindowWidth, 560, 900);
            if (string.Equals(_settings.CornerStyle, "square", StringComparison.OrdinalIgnoreCase))
                CornerSquare.IsChecked = true;
            else
                CornerRounded.IsChecked = true;
            if (string.Equals(_settings.RowDensity, "compact", StringComparison.OrdinalIgnoreCase))
                DensityCompact.IsChecked = true;
            else
                DensityComfortable.IsChecked = true;
            UpdateAdvLabels();

            MaxFilesSlider.Value = Math.Clamp(_settings.MaxIndexedFiles, 10_000, 300_000);
            MaxFilesLabel.Text = ((int)MaxFilesSlider.Value).ToString("N0");

            _pendingHotkey = string.IsNullOrWhiteSpace(_settings.Hotkey) ? "Alt+Space" : _settings.Hotkey;
            HotkeyDisplay.Text = _pendingHotkey;

            // v2.3 (DEV_PLAN Task 3.1) — AI answers: load + live-wire (Cancel restores
            // via RestoreFrom(_snapshot), same as every other live-edited setting).
            AiEnabledToggle.IsChecked = _settings.AiEnabled;
            if (AiProviders.IsAnthropic(_settings.AiStyle, _settings.AiEndpoint))
                AiAnthropic.IsChecked = true;
            else
                AiOllama.IsChecked = true;
            AiEndpointBox.Text = _settings.AiEndpoint;
            AiModelBox.Text = _settings.AiModel;
            AiKeyBox.Text = _settings.AiApiKey;
            AiEnabledToggle.Click += (_, _) => { if (!_suppress) _settings.AiEnabled = AiEnabledToggle.IsChecked == true; };
            AiOllama.Checked += (_, _) => { if (!_suppress) _settings.AiStyle = AiProviders.OllamaStyle; };
            AiAnthropic.Checked += (_, _) => { if (!_suppress) _settings.AiStyle = AiProviders.AnthropicStyle; };
            AiEndpointBox.TextChanged += (_, _) => { if (!_suppress) _settings.AiEndpoint = AiEndpointBox.Text.Trim(); };
            AiModelBox.TextChanged += (_, _) => { if (!_suppress) _settings.AiModel = AiModelBox.Text.Trim(); };
            AiKeyBox.TextChanged += (_, _) => { if (!_suppress) _settings.AiApiKey = AiKeyBox.Text.Trim(); };

            // v2.6.0-alpha.3 — voice typing: toggle lives with the AI settings; the status
            // line surfaces what dictation will actually use on this PC (live-edited like
            // every AI setting, Cancel restores via RestoreFrom(_snapshot)).
            // v2.6.0-alpha.5 — engine-aware: Whisper (downloaded on demand from the
            // chat's mic button) vs the built-in Windows recognizer fallback.
            VoiceEnabledToggle.IsChecked = _settings.VoiceEnabled;
            VoiceEnabledToggle.Click += (_, _) => { if (!_suppress) _settings.VoiceEnabled = VoiceEnabledToggle.IsChecked == true; };
            VoiceStatusText.Text = BuildVoiceStatus();

            // v2.4.0-alpha.6 — custom personas: their own store (personas.json, saved
            // immediately on edit — independent of this window's Save/Cancel cycle)
            RebuildPersonaList();

            // v2.3.0-alpha.2 — Local models (Ollama): probe in the background, render
            // when it lands. Closed cancels any in-flight download/pull.
            Closed += (_, _) => { try { _ollamaCts?.Cancel(); } catch { } };
            _ = ProbeAndRenderOllamaAsync();

            // event wiring for the pills (Checked handlers)
            StartWithWindowsToggle.Click += (_, _) =>
            {
                _pendingStartWithWindows = StartWithWindowsToggle.IsChecked == true;
            };
            HideOnFocusLossToggle.Click += (_, _) =>
            {
                _settings.HideOnFocusLoss = HideOnFocusLossToggle.IsChecked == true;
                FooterHint.Text = "Saved on next launch · press Save to keep it";
            };
            AnimationsToggle.Click += (_, _) =>
            {
                if (_suppress) return;
                _settings.AnimationsEnabled = AnimationsToggle.IsChecked == true;
                FooterHint.Text = _settings.AnimationsEnabled
                    ? "Animations on — press Save to keep them"
                    : "Reduced motion — press Save to keep it";
            };
            // v2.6 — Task 5.1: live-edited like the AI settings; Cancel restores via RestoreFrom(_snapshot).
            UpdatesToggle.Click += (_, _) =>
            {
                if (_suppress) return;
                _settings.UpdatesEnabled = UpdatesToggle.IsChecked == true;
                RefreshUpdateCard();
            };
            EngineGoogle.Checked += (_, _) => { if (!_suppress) _settings.WebEngine = "google"; };
            EngineBing.Checked += (_, _) => { if (!_suppress) _settings.WebEngine = "bing"; };
            EngineDdg.Checked += (_, _) => { if (!_suppress) _settings.WebEngine = "duckduckgo"; };

            BorderEffectToggle.Click += (_, _) => SyncLiveAppearance();
            ThemeDark.Checked += (_, _) => SyncLiveAppearance();
            ThemeLight.Checked += (_, _) => SyncLiveAppearance();
            ThemeAuto.Checked += (_, _) => SyncLiveAppearance();
            CornerRounded.Checked += (_, _) => SyncLiveAppearance();
            CornerSquare.Checked += (_, _) => SyncLiveAppearance();
            DensityComfortable.Checked += (_, _) => SyncLiveAppearance();
            DensityCompact.Checked += (_, _) => SyncLiveAppearance();

            MaxFilesSlider.ValueChanged += (_, _) =>
            {
                MaxFilesLabel.Text = ((int)MaxFilesSlider.Value).ToString("N0");
            };
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.Load", ex); }
        finally { _suppress = false; }
    }

    // ---------------------------------------------------------------- live appearance

    private void SyncLiveAppearance()
    {
        if (_suppress) return;
        try
        {
            _settings.Theme =
                ThemeLight.IsChecked == true ? "light" :
                ThemeAuto.IsChecked == true ? "auto" : "dark";
            _settings.BorderEffect = BorderEffectToggle.IsChecked == true;
            _settings.WindowWidth = Math.Clamp(WidthSlider.Value, 560, 900);
            _settings.CornerStyle = CornerSquare.IsChecked == true ? "square" : "rounded";
            _settings.RowDensity = DensityCompact.IsChecked == true ? "compact" : "comfortable";

            ApplySelfTheme();
            UpdatePreview();
            MarkSelectedSwatch(_settings.AccentColor);
            FooterHint.Text = "Changes applied live — press Save to keep them";
            _applyAppearance();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.SyncLive", ex); }
    }

    /// <summary>v2.0.1 — keeps the value chip next to the width slider honest.</summary>
    private void UpdateAdvLabels()
    {
        try
        {
            WidthValue.Text = $"{(int)Math.Clamp(WidthSlider.Value, 560, 900)} px";
        }
        catch { }
    }

    private void UpdatePreview()
    {
        try
        {
            if (_settings.BorderEffect)
            {
                // v3.0 — the edge shine itself: the same top-lit stroke the rim uses.
                bool dark = _settings.EffectiveDark();
                PreviewCard.BorderBrush = ThemeService.TopLitStroke(
                    dark ? FromRgb(0x26, 0x26, 0x2B) : FromRgb(0xE6, 0xE7, 0xEA), dark, 0x40);
            }
            else
            {
                bool dark = !string.Equals(_settings.Theme, "light", StringComparison.OrdinalIgnoreCase);
                PreviewCard.BorderBrush = new SolidColorBrush(dark ? FromRgb(0x33, 0x36, 0x4A) : FromRgb(0xE2, 0xE4, 0xEC));
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.Preview", ex); }
    }

    // ------------------------------------------------- v3.0 theme gallery + import/export

    /// <summary>One gallery entry: a preset or an imported theme file.</summary>
    private sealed record ThemeEntry(string Key, string DisplayName, ThemeSelect.ThemeSpec Spec, string? FileName);

    private void BuildThemeGallery()
    {
        try
        {
            var entries = new List<ThemeEntry>();
            foreach (var preset in ThemeSelect.Presets)
                entries.Add(new ThemeEntry(preset.Id, preset.Name,
                    new ThemeSelect.ThemeSpec(preset.Dark, preset.Accent, preset.Colors, preset.Id, preset.Name), null));

            try
            {
                Directory.CreateDirectory(AppPaths.ThemesDir);
                foreach (var file in Directory.GetFiles(AppPaths.ThemesDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var tf = ThemeFile.LoadFile(file);
                    if (tf is null) continue;
                    var name = Path.GetFileNameWithoutExtension(file);
                    entries.Add(new ThemeEntry("user:" + name, tf.Name,
                        new ThemeSelect.ThemeSpec(tf.Dark, tf.Accent, tf.Colors, "user:" + name, tf.Name), name));
                }
            }
            catch { /* the gallery still shows the presets */ }

            // Which entry is active right now? Custom file wins, then preset.
            string? activeKey = null;
            if (!string.IsNullOrWhiteSpace(_settings.CustomThemeFile))
                activeKey = "user:" + Path.GetFileNameWithoutExtension(_settings.CustomThemeFile);
            else if (ThemeSelect.FindPreset(_settings.ThemePreset) is { } p)
                activeKey = p.Id;

            ThemeGallery.Children.Clear();
            foreach (var entry in entries)
                ThemeGallery.Children.Add(BuildThemeCard(entry, entry.Key == activeKey));
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.ThemeGallery", ex); }
    }

    private UIElement BuildThemeCard(ThemeEntry entry, bool active)
    {
        var c = ThemeService.ResolveColors(entry.Spec);
        var brush = new SolidColorBrush(Color.FromRgb(c.Panel.R, c.Panel.G, c.Panel.B));
        brush.Freeze();
        var fieldBrush = new SolidColorBrush(c.Field); fieldBrush.Freeze();
        var titleBrush = new SolidColorBrush(c.Title); titleBrush.Freeze();
        var subBrush = new SolidColorBrush(c.Subtitle); subBrush.Freeze();
        var accentBrush = new SolidColorBrush(c.Accent); accentBrush.Freeze();
        var lineBrush = new SolidColorBrush(c.Border); lineBrush.Freeze();

        var card = new Border
        {
            Width = 132,
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(10),
            Background = brush,
            BorderThickness = new Thickness(active ? 2 : 1),
            BorderBrush = active ? accentBrush : lineBrush,
            Cursor = Cursors.Hand,
            Tag = entry,
            Padding = new Thickness(10, 9, 10, 8),
        };
        PressFeedback.SetIsEnabled(card, true);

        var stack = new StackPanel();
        stack.Children.Add(new StackPanel // fake launcher mini: title bar + field + accent dot
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = accentBrush },
                new Border { Width = 46, Height = 4, CornerRadius = new CornerRadius(2), Background = titleBrush, Margin = new Thickness(7, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center },
            },
        });
        stack.Children.Add(new Border
        {
            Height = 16, CornerRadius = new CornerRadius(5), Background = fieldBrush,
            Margin = new Thickness(0, 8, 0, 0),
            Child = new Border { Width = 30, Height = 3, CornerRadius = new CornerRadius(1.5), Background = subBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) },
        });
        stack.Children.Add(new TextBlock
        {
            Text = entry.DisplayName,
            FontSize = 11.5,
            FontWeight = FontWeight.FromOpenTypeWeight(600),
            Foreground = titleBrush,
            Margin = new Thickness(0, 8, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        card.Child = stack;
        card.MouseLeftButtonUp += (_, _) => ApplyThemeEntry(entry);
        return card;
    }

    private void ApplyThemeEntry(ThemeEntry entry)
    {
        try
        {
            _settings.CustomThemeFile = entry.FileName ?? "";
            _settings.ThemePreset = entry.FileName is null ? entry.Key : "";
            _suppress = true;
            try
            {
                // mirror the resolved mode into the legacy segmented control so the
                // radio row never contradicts the active theme
                if (entry.Spec.Dark) ThemeDark.IsChecked = true;
                else ThemeLight.IsChecked = true;
            }
            finally { _suppress = false; }

            ApplySelfTheme();
            UpdatePreview();
            _applyAppearance();
            _settings.Save();   // theme choice is instant-persist (like personas), not Save/Cancel
            BuildThemeGallery();
            FooterHint.Text = $"Theme '{entry.DisplayName}' applied";
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.ApplyTheme", ex); }
    }

    private void OnImportTheme(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import a Lumo theme",
                Filter = "Lumo theme (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(this) != true) return;

            var json = File.ReadAllText(dlg.FileName);
            if (!ThemeFile.TryParse(json, out var tf, out var error) || tf is null)
            {
                ThemeStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
                ThemeStatus.Text = $"Import failed — {error}";
                return;
            }

            Directory.CreateDirectory(AppPaths.ThemesDir);
            var name = Path.GetFileNameWithoutExtension(dlg.FileName);
            var target = Path.Combine(AppPaths.ThemesDir, ThemeFile.Slug(tf.Name) is { Length: > 0 } slug ? slug : ThemeFile.Slug(name) is { Length: > 0 } s2 ? s2 : "theme" ) + ".json";
            tf.Save(target);

            _settings.CustomThemeFile = Path.GetFileName(target);
            _settings.ThemePreset = "";
            ApplySelfTheme();
            UpdatePreview();
            _applyAppearance();
            _settings.Save();
            BuildThemeGallery();

            ThemeStatus.Foreground = (SolidColorBrush)Resources["SubtitleBrush"];
            ThemeStatus.Text = $"Imported '{tf.Name}' — applied and saved to {target}";
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Settings.ImportTheme", ex);
            ThemeStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
            ThemeStatus.Text = "Import failed — see the log for details.";
        }
    }

    private void OnExportTheme(object sender, RoutedEventArgs e)
    {
        try
        {
            var theme = ThemeService.ExportFile(_settings);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export the current theme",
                Filter = "Lumo theme (*.json)|*.json",
                FileName = ThemeFile.Slug(theme.Name) + ".json",
            };
            if (dlg.ShowDialog(this) != true) return;

            theme.Save(dlg.FileName);
            ThemeStatus.Foreground = (SolidColorBrush)Resources["SubtitleBrush"];
            ThemeStatus.Text = $"Exported '{theme.Name}' → {dlg.FileName}";
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Settings.ExportTheme", ex);
            ThemeStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
            ThemeStatus.Text = "Export failed — see the log for details.";
        }
    }

    // ---------------------------------------------------------------- accent swatches

    private void BuildAccentSwatches()
    {
        try
        {
            AccentRow.Children.Clear();
            _swatches.Clear();
            foreach (var hex in Appearance.AccentPresets)
            {
                var box = new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(14),
                    Margin = new Thickness(0, 0, 10, 10),
                    Cursor = Cursors.Hand,
                    Background = new SolidColorBrush(Appearance.ParseAccent(hex)),
                    Tag = hex,
                };
                box.MouseLeftButtonUp += OnSwatchClick;
                AccentRow.Children.Add(box);
                _swatches.Add((box, hex));
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.Swatches", ex); }
    }

    private void OnSwatchClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is Border { Tag: string hex })
            {
                _settings.AccentColor = hex;
                AccentHexBox.Text = hex;
                SyncLiveAppearance();
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.SwatchClick", ex); }
    }

    private void MarkSelectedSwatch(string hex)
    {
        try
        {
            bool dark = !string.Equals(_settings.Theme, "light", StringComparison.OrdinalIgnoreCase);
            var ring = new SolidColorBrush(dark ? Colors.White : FromRgb(0x1B, 0x1D, 0x27));
            foreach (var (box, h) in _swatches)
            {
                bool sel = h.Equals(hex, StringComparison.OrdinalIgnoreCase);
                box.BorderThickness = new Thickness(sel ? 2.5 : 0);
                box.BorderBrush = sel ? ring : Brushes.Transparent;
            }
        }
        catch { }
    }

    private void ApplyAccentHex()
    {
        try
        {
            var t = AccentHexBox.Text.Trim();
            if (!t.StartsWith("#")) t = "#" + t;
            var c = (Color)ColorConverter.ConvertFromString(t); // throws on garbage
            _ = c;
            _settings.AccentColor = t;
            MarkSelectedSwatch(t);
            SyncLiveAppearance();
        }
        catch
        {
            FooterHint.Text = "Invalid hex colour — use the form #RRGGBB";
        }
    }

    private void OnAccentHexKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Enter) { ApplyAccentHex(); e.Handled = true; }
        }
        catch { }
    }

    private void OnAccentHexLostFocus(object sender, RoutedEventArgs e)
    {
        try { ApplyAccentHex(); } catch { }
    }

    // ---------------------------------------------------------------- hotkey recorder

    private void OnHotkeyCaptureClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            HotkeyCapture.Focus();
            HotkeyDisplay.Text = "Press a combination…  (Esc clears)";
            HotkeyStatus.Text = "";
        }
        catch { }
    }

    private void OnHotkeyCaptureKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // pure modifier presses → just show progress
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                     or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                string mods = ModsToString(Keyboard.Modifiers);
                HotkeyDisplay.Text = mods.Length == 0 ? "Press a combination…" : mods + "…";
                return;
            }

            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                _pendingHotkey = "";
                HotkeyDisplay.Text = "(click here, then press a combination)";
                HotkeyStatus.Text = "";
                return;
            }

            string? main = DescribeMainKey(key);
            if (main is null)
            {
                HotkeyStatus.Text = "That key isn't supported — use letters, numbers, F1–F24, Space or `";
                return;
            }

            var mods2 = Keyboard.Modifiers;
            if ((mods2 & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
            {
                HotkeyStatus.Text = "Add Ctrl, Alt or Win — a bare key (or Shift+key) would hijack normal typing.";
                HotkeyDisplay.Text = (mods2 & ModifierKeys.Shift) != 0 ? "Shift+" + main : main;
                return;
            }

            _pendingHotkey = ModsToString(mods2) + main;
            HotkeyDisplay.Text = _pendingHotkey;
            HotkeyStatus.Text = "Press “Apply hotkey” to register it right now.";
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.HotkeyCapture", ex); }
    }

    private void OnApplyHotkey(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_pendingHotkey))
            {
                HotkeyStatus.Text = "Click the box and press a combination first.";
                return;
            }

            _settings.Hotkey = _pendingHotkey;
            string active = _applyHotkey();

            if (active == "(none)")
            {
                HotkeyStatus.Text = "Could not register any combination — all fallbacks are taken. Try another combo.";
            }
            else if (active.Equals(_pendingHotkey, StringComparison.OrdinalIgnoreCase))
            {
                HotkeyStatus.Text = $"Registered: {active} — it's live right now, press it!";
                FooterHint.Text = "Hotkey changed — press Save to remember it";
            }
            else
            {
                HotkeyStatus.Text = $"'{_pendingHotkey}' was unavailable — fallback active: {active}.";
                _pendingHotkey = active;
                _settings.Hotkey = active;
                HotkeyDisplay.Text = active;
                FooterHint.Text = "Hotkey changed — press Save to remember it";
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.ApplyHotkey", ex); }
    }

    private void OnResetHotkey(object sender, RoutedEventArgs e)
    {
        try
        {
            _pendingHotkey = "Alt+Space";
            HotkeyDisplay.Text = _pendingHotkey;
            OnApplyHotkey(sender, e);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.ResetHotkey", ex); }
    }

    private static string ModsToString(ModifierKeys m)
    {
        var s = "";
        if ((m & ModifierKeys.Control) != 0) s += "Ctrl+";
        if ((m & ModifierKeys.Alt) != 0) s += "Alt+";
        if ((m & ModifierKeys.Shift) != 0) s += "Shift+";
        if ((m & ModifierKeys.Windows) != 0) s += "Win+";
        return s;
    }

    private static string? DescribeMainKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return key.ToString().ToLowerInvariant();
        if (key is >= Key.D0 and <= Key.D9) return ((int)(key - Key.D0)).ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return ((int)(key - Key.NumPad0)).ToString();
        if (key is >= Key.F1 and <= Key.F24) return key.ToString();
        if (key == Key.Space) return "Space";
        if (key == Key.OemTilde) return "`";
        return null;
    }

    // ---------------------------------------------------------------- search & index

    private void OnRebuildIndex(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.MaxIndexedFiles = (int)Math.Clamp(MaxFilesSlider.Value, 10_000, 300_000);
            _rebuildIndex();
            IndexStatus.Text = $"Rebuilding with a {_settings.MaxIndexedFiles:N0}-file cap — watch the launcher status bar.";
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.Rebuild", ex); }
    }

    private void OnOpenLog(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.DataDir);
    private void OnOpenSettingsFolder(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.SettingsDir);

    private static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OpenFolder", ex); }
    }

    // ---------------------------------------------------------------- about

    private void OnOpenGitHub(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "https://github.com/Anik1377/Lumo-Launcher", UseShellExecute = true });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.GitHub", ex); }
    }

    private void OnCopyGitHub(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText("https://github.com/Anik1377/Lumo-Launcher");
            AboutStatus.Text = "Link copied to clipboard.";
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.CopyLink", ex); }
    }

    // ---------------------------------------------------------------- nav / lifecycle

    private void OnNavChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress || PanelGeneral is null) return;
        try
        {
            if (sender is RadioButton { Tag: string raw } && int.TryParse(raw, out int idx))
                ShowPanel(idx);
        }
        catch { }
    }

    private void ShowPanel(int idx)
    {
        PanelGeneral.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelAppearance.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelHotkey.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelSearch.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelShortcuts.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;
        PanelAI.Visibility = idx == 6 ? Visibility.Visible : Visibility.Collapsed;   // v2.3 — AI page
        PanelPlugins.Visibility = idx == 7 ? Visibility.Visible : Visibility.Collapsed;   // v2.5 — Plugins page
        PanelAbout.Visibility = idx == 5 ? Visibility.Visible : Visibility.Collapsed;

        // v1.3 — gentle page transition: the incoming panel fades + slides up a touch
        if (!_settings.AnimationsEnabled) return;
        try
        {
            if (idx switch
            {
                0 => PanelGeneral,
                1 => PanelAppearance,
                2 => PanelHotkey,
                3 => PanelSearch,
                4 => PanelShortcuts,
                6 => PanelAI,
                7 => PanelPlugins,
                _ => (ScrollViewer?)PanelAbout,
            } is not { } panel) return;

            var tt = new TranslateTransform(0, 10);
            panel.RenderTransform = tt;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(190);
            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, dur) { EasingFunction = ease });
            panel.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
        }
        catch { }
    }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
        catch { }
    }

    private void OnClose(object sender, RoutedEventArgs e) => SaveAndClose();
    private void OnSave(object sender, RoutedEventArgs e) => SaveAndClose();

    private void SaveAndClose()
    {
        try
        {
            _settings.MaxIndexedFiles = (int)Math.Clamp(MaxFilesSlider.Value, 10_000, 300_000);
            _settings.StartWithWindows = _pendingStartWithWindows;
            StartupManager.SetEnabled(_pendingStartWithWindows);
            _settings.Save();
            _applyDeckHotkeys?.Invoke();   // v3.0 — apply the deck hotkey toggle
            DiagnosticLogger.Log("Settings", "Saved");
            Close();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Settings.SaveAndClose", ex);
            try { Close(); } catch { }
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.RestoreFrom(_snapshot);
            _applyAppearance();
            _applyHotkey(); // put back the hotkey that was registered before the window opened
            _applyDeckHotkeys?.Invoke();   // v3.0 — undo any deck-hotkey toggle change
            Close();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Settings.Cancel", ex);
            try { Close(); } catch { }
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key != Key.Escape) return;
            if (HotkeyCapture.IsKeyboardFocused) return; // capture handler clears instead
            e.Handled = true;
            OnCancel(this, e);
        }
        catch { }
    }

    // ================================================================= v2.3.0-alpha.2 — Local models (Ollama)
    //
    // The one-click local AI setup: probe → install Ollama → start it → pull a
    // lightweight model with live progress. Every network/process operation runs
    // on a worker (Task.Run); only rendering touches the UI thread. One download
    // or pull at a time (_ollamaBusy) so nothing can stack; the window Closed
    // event cancels whatever is in flight via _ollamaCts.

    /// <summary>Background probe → dispatcher render. Fire-and-forget safe; never throws.</summary>
    private async Task ProbeAndRenderOllamaAsync()
    {
        try
        {
            string endpoint = _settings.AiEndpoint;
            await Task.Run(() => OllamaManager.RefreshStatusAsync(endpoint)).ConfigureAwait(true);
            await Dispatcher.InvokeAsync(() =>
            {
                try { RenderOllamaPanel(); }
                catch (Exception ex) { DiagnosticLogger.LogException("Settings.OllamaRender", ex); }
            });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OllamaProbe", ex); }
    }

    // --------------------------- v2.4.0-alpha.6 — custom personas (personas.json)

    private string? _editingPersonaId;   // null = creating a new persona

    /// <summary>
    /// v2.6.0-alpha.5 — the voice card's status line, engine-aware: what the chat's
    /// mic button will actually use, and the one-time Whisper download state.
    /// </summary>
    private string BuildVoiceStatus()
    {
        try
        {
            bool engineWhisper = !string.Equals(_settings.VoiceEngine, VoiceInputService.EngineWindows, StringComparison.OrdinalIgnoreCase);
            if (!engineWhisper)
            {
                int recognizers = 0;
                try { recognizers = VoiceInputService.Installed().Count; } catch { }
                if (!VoiceInputService.IsSupported)
                    return "Windows speech selected, but no recognizer is installed — add one under Windows Settings → Time & Language → Speech, or switch back to Whisper.";
                return recognizers <= 1
                    ? "Windows built-in speech (offline, follows the display language). For much better accuracy set \"VoiceEngine\": \"whisper\" in settings.json."
                    : $"Windows built-in speech · {recognizers} recognizers installed (offline). For much better accuracy set \"VoiceEngine\": \"whisper\" in settings.json.";
            }

            var model = VoiceWhisper.FromId(_settings.VoiceModel);
            if (!WhisperEngine.IsDownloaded(model))
            {
                return $"Whisper · {model.Name} ({model.SizeLabel}) — not downloaded yet. Click the mic in the AI chat to install it " +
                       "(one-time download; Windows speech stays available as the fallback).";
            }
            return $"Whisper · {model.Name} ({model.SizeLabel}) ready — offline transcription, nothing leaves the PC. " +
                   $"Switch engines with \"VoiceEngine\": \"windows\" in settings.json.";
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.VoiceStatus", ex); return "Voice input status unavailable."; }
    }

    private string _editingPersonaFace = "";   // v3.0 — face/color being edited
    private string _editingPersonaColor = "";

    private void RebuildPersonaList()
    {
        try
        {
            PersonaList.Items.Clear();
            var all = PersonaStore.Current.All;
            PersonaEmpty.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var p in all)
                PersonaList.Items.Add(BuildPersonaRow(p));
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.PersonaList", ex); }
    }

    private FrameworkElement BuildPersonaRow(ChatPersona p)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        // v3.0 — the row shows the persona's FACE (the glyph field is legacy now)
        var face = new PersonaFaceView
        {
            Width = 20,
            Height = 20,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FaceId = PersonaFaces.NormalizeId(p.Face),
            PersonaColor = PersonaFaces.NormalizeColor(p.Color),
        };
        var name = new TextBlock
        {
            Text = p.Name,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TitleBrush");
        nameRow.Children.Add(face);
        nameRow.Children.Add(name);
        text.Children.Add(nameRow);

        string preview = p.Prompt.Length > 96 ? p.Prompt[..96] + "…" : p.Prompt;
        var blurb = new TextBlock { Text = preview, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        blurb.SetResourceReference(TextBlock.ForegroundProperty, "SubtitleBrush");
        text.Children.Add(blurb);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var edit = new Button { Content = "Edit", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), Tag = p.Id };
        if (TryFindResource("GhostButton") is Style editStyle) edit.Style = editStyle;
        edit.Click += OnPersonaEdit;
        Grid.SetColumn(edit, 1);
        grid.Children.Add(edit);

        var del = new Button { Content = "Delete", Padding = new Thickness(12, 4, 12, 4), Tag = p.Id };
        if (TryFindResource("GhostButton") is Style delStyle) del.Style = delStyle;
        del.Click += OnPersonaDelete;
        Grid.SetColumn(del, 2);
        grid.Children.Add(del);
        return grid;
    }

    private void OnPersonaNew(object sender, RoutedEventArgs e)
    {
        try
        {
            _editingPersonaId = null;
            PersonaNameBox.Text = "";
            PersonaPromptBox.Text = "";
            _editingPersonaFace = PersonaFaces.DefaultFace;
            _editingPersonaColor = "";
            BuildPersonaFacePicker();
            BuildPersonaColorPicker();
            UpdatePersonaFacePreview();
            PersonaEditor.Visibility = Visibility.Visible;
            PersonaNameBox.Focus();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.PersonaNew", ex); }
    }

    private void OnPersonaEdit(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string id } || PersonaStore.Current.Find(id) is not { } p) return;
            _editingPersonaId = p.Id;
            PersonaNameBox.Text = p.Name;
            PersonaPromptBox.Text = p.Prompt;
            _editingPersonaFace = PersonaFaces.NormalizeId(p.Face) is { Length: > 0 } f ? f : PersonaFaces.DefaultFace;
            _editingPersonaColor = PersonaFaces.NormalizeColor(p.Color);
            BuildPersonaFacePicker();
            BuildPersonaColorPicker();
            UpdatePersonaFacePreview();
            PersonaEditor.Visibility = Visibility.Visible;
            PersonaNameBox.Focus();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.PersonaEdit", ex); }
    }

    private void OnPersonaDelete(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string id }) return;
            if (PersonaStore.Current.Delete(id))
            {
                if (string.Equals(_editingPersonaId, id, StringComparison.OrdinalIgnoreCase))
                    PersonaEditor.Visibility = Visibility.Collapsed;
                RebuildPersonaList();
                FooterHint.Text = "Chats that used this persona fall back to the Assistant";
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.PersonaDelete", ex); }
    }

    private void OnPersonaSave(object sender, RoutedEventArgs e)
    {
        try
        {
            string name = PersonaNameBox.Text.Trim();
            string prompt = PersonaPromptBox.Text.Trim();
            if (name.Length == 0 || prompt.Length == 0)
            {
                FooterHint.Text = "A persona needs at least a name and a system prompt";
                return;
            }
            // v3.0 — face/color ride along; the legacy glyph stays whatever it was
            string glyph = _editingPersonaId is { } existingId
                ? PersonaStore.Current.Find(existingId)?.Glyph ?? ""
                : "";
            bool ok = _editingPersonaId is { } id
                ? PersonaStore.Current.Update(id, name, glyph, prompt, "", _editingPersonaFace, _editingPersonaColor)
                : PersonaStore.Current.Add(name, glyph, prompt, "", _editingPersonaFace, _editingPersonaColor) is not null;
            if (!ok && _editingPersonaId is null)
            {
                FooterHint.Text = $"Persona list is full ({PersonaStore.MaxPersonas}) — delete one first";
                return;
            }
            PersonaEditor.Visibility = Visibility.Collapsed;
            RebuildPersonaList();
            FooterHint.Text = "Persona saved — pick it from the chat's persona chip";
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.PersonaSave", ex); }
    }

    // ------------------------------------------------- v3.0 — persona face picker

    private void BuildPersonaFacePicker()
    {
        PersonaFacePicker.Children.Clear();
        foreach (var f in PersonaFaces.All)
        {
            var id = f.Id;
            var card = new Border
            {
                Width = 44,
                Height = 44,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(11),
                Background = (System.Windows.Media.Brush)FindResource("FieldBrush"),
                BorderThickness = new Thickness(id == _editingPersonaFace ? 2 : 1),
                BorderBrush = id == _editingPersonaFace
                    ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                    : (System.Windows.Media.Brush)FindResource("BorderLineBrush"),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = f.Name,
                Child = new PersonaFaceView { FaceId = id, PersonaColor = _editingPersonaColor },
            };
            PressFeedback.SetIsEnabled(card, true);
            card.MouseLeftButtonUp += (_, _) =>
            {
                _editingPersonaFace = id;
                BuildPersonaFacePicker();
                UpdatePersonaFacePreview();
            };
            PersonaFacePicker.Children.Add(card);
        }
    }

    private void BuildPersonaColorPicker()
    {
        PersonaColorPicker.Children.Clear();
        var choices = new List<string> { "" };
        choices.AddRange(Appearance.AccentPresets.Where(h => !string.Equals(h, "#FF6363", StringComparison.OrdinalIgnoreCase)));
        foreach (var hex in choices)
        {
            var color = hex;
            System.Windows.Media.Color rgb;
            try { rgb = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex.Length == 0 ? "#808088" : hex); }
            catch { rgb = System.Windows.Media.Colors.Gray; }

            var swatch = new Border
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(15),
                Background = new System.Windows.Media.SolidColorBrush(rgb),
                BorderThickness = new Thickness(hex == _editingPersonaColor ? 3 : 1),
                BorderBrush = hex == _editingPersonaColor
                    ? (System.Windows.Media.Brush)FindResource("TitleBrush")
                    : (System.Windows.Media.Brush)FindResource("BorderLineBrush"),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = hex.Length == 0 ? "Follow the theme accent" : hex,
            };
            if (hex.Length == 0)
            {
                swatch.Child = new TextBlock
                {
                    Text = "A",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                swatch.Background = (System.Windows.Media.Brush)FindResource("SubtitleBrush");
            }
            PressFeedback.SetIsEnabled(swatch, true);
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                _editingPersonaColor = color;
                BuildPersonaColorPicker();
                BuildPersonaFacePicker();   // re-tint the face chips
                UpdatePersonaFacePreview();
            };
            PersonaColorPicker.Children.Add(swatch);
        }
    }

    private void UpdatePersonaFacePreview()
    {
        PersonaFacePreview.FaceId = _editingPersonaFace;
        PersonaFacePreview.PersonaColor = _editingPersonaColor;
    }

    private void OnPersonaCancel(object sender, RoutedEventArgs e)
    {
        try { PersonaEditor.Visibility = Visibility.Collapsed; }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.PersonaCancel", ex); }
    }

    private void OnOllamaRefresh(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_ollamaBusy) return;
            OllamaManager.Invalidate();
            _ = ProbeAndRenderOllamaAsync();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OllamaRefresh", ex); }
    }

    /// <summary>Repaints the whole card from the immutable OllamaManager.Current snapshot. UI thread only.</summary>
    private void RenderOllamaPanel()
    {
        var st = OllamaManager.Current;
        bool busy = _ollamaBusy;

        if (!st.Probed)
            OllamaStatusText.Text = "Checking…";
        else if (!st.Installed)
            OllamaStatusText.Text = "Ollama is not installed — one click below installs it (free, private, offline)";
        else if (!st.ServerUp)
            OllamaStatusText.Text = $"Ollama is installed but not answering on {st.Endpoint}";
        else
            OllamaStatusText.Text = $"Ollama is running · {st.Models.Count} model{(st.Models.Count == 1 ? "" : "s")} · {st.Endpoint}";

        OllamaInstallBlock.Visibility = st.Probed && !st.Installed ? Visibility.Visible : Visibility.Collapsed;
        OllamaInstallButton.IsEnabled = !busy;
        OllamaStartBlock.Visibility = st.Probed && st.Installed && !st.ServerUp ? Visibility.Visible : Visibility.Collapsed;
        OllamaStartButton.IsEnabled = !busy;
        OllamaRefreshButton.IsEnabled = !busy;

        // v3.0.0-alpha.5 — where Ollama itself lives (exe probe honours the saved location)
        string? exe = OllamaManager.ExePath;
        OllamaExePathText.Text = exe is not null
            ? $"ollama.exe: {exe}"
            : "ollama.exe not found yet — standard locations, PATH, or the saved location below are probed on every refresh.";
        OllamaInstallDirBrowse.IsEnabled = !busy;
        OllamaInstallDirReset.IsEnabled = !busy;
        OllamaInstallDirSave.IsEnabled = !busy;

        // v3.0.0-alpha.5 — where the models live (OLLAMA_MODELS) + their disk size
        bool showStorage = st.Probed && st.Installed;
        OllamaStorageBlock.Visibility = showStorage ? Visibility.Visible : Visibility.Collapsed;
        if (showStorage)
        {
            string modelsDir = OllamaManager.ModelsDir;
            long bytes = OllamaManager.FolderBytes(modelsDir);
            OllamaModelsPathText.Text = modelsDir + (bytes > 0 ? $"   ·   {FmtBytes(bytes)} on disk" : "   ·   empty");
            bool custom = OllamaManager.ModelsDirIsCustom;
            OllamaModelsPathText.Text += custom ? "   ·   custom location" : "";
            OllamaModelsMoveButton.IsEnabled = !busy;
            OllamaModelsOpenButton.IsEnabled = !busy;
            OllamaRestartButton.IsEnabled = !busy;
        }

        // installed models (server up only — the list comes from /api/tags)
        OllamaInstalledList.Items.Clear();
        bool showInstalled = st.ServerUp && st.Models.Count > 0;
        OllamaInstalledTitle.Visibility = showInstalled ? Visibility.Visible : Visibility.Collapsed;
        OllamaInstalledList.Visibility = showInstalled ? Visibility.Visible : Visibility.Collapsed;
        if (showInstalled)
            foreach (var m in st.Models)
                OllamaInstalledList.Items.Add(BuildInstalledRow(m));

        // curated lightweight catalog (install needs a live server)
        _pullButtons.Clear();
        OllamaCatalogList.Items.Clear();
        var installedNames = st.Models.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var m in OllamaManager.Catalog)
            OllamaCatalogList.Items.Add(BuildCatalogRow(m, installedNames.Contains(m.Id)));
        foreach (var b in _pullButtons)
            b.IsEnabled = !busy && st.ServerUp;
    }

    private FrameworkElement BuildInstalledRow(OllamaManager.ModelInfo m)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        bool active = m.Name.Equals(_settings.AiModel, StringComparison.OrdinalIgnoreCase);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = m.Name + (m.Bytes > 0 ? "   ·   " + FmtBytes(m.Bytes) : ""),
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TitleBrush");
        text.Children.Add(name);
        if (active)
        {
            var act = new TextBlock { Text = "active model", FontSize = 11 };
            act.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            text.Children.Add(act);
        }
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        if (!active)
        {
            var use = new Button { Content = "Use", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), Tag = m.Name };
            if (TryFindResource("GhostButton") is Style useStyle) use.Style = useStyle;
            use.Click += OnOllamaUse;
            Grid.SetColumn(use, 1);
            grid.Children.Add(use);
        }

        var del = new Button { Content = "Uninstall", Padding = new Thickness(12, 4, 12, 4) };
        if (TryFindResource("GhostButton") is Style delStyle) del.Style = delStyle;
        del.Tag = m.Name;
        del.Click += OnOllamaDelete;
        Grid.SetColumn(del, 2);
        grid.Children.Add(del);
        return grid;
    }

    private FrameworkElement BuildCatalogRow(OllamaManager.OllamaModel m, bool installed)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = $"{m.Id}   ·   ~{m.SizeGb:0.0} GB", FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TitleBrush");
        var blurb = new TextBlock { Text = m.Blurb, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        blurb.SetResourceReference(TextBlock.ForegroundProperty, "SubtitleBrush");
        text.Children.Add(name);
        text.Children.Add(blurb);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        if (installed)
        {
            var done = new TextBlock
            {
                Text = "installed",
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0),
            };
            done.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            Grid.SetColumn(done, 1);
            grid.Children.Add(done);
        }
        else
        {
            var pull = new Button { Content = "Install", Padding = new Thickness(14, 4, 14, 4), Tag = m.Id };
            if (TryFindResource("AccentButton") is Style pullStyle) pull.Style = pullStyle;
            pull.Click += OnOllamaPull;
            _pullButtons.Add(pull);
            Grid.SetColumn(pull, 1);
            grid.Children.Add(pull);
        }
        return grid;
    }

    private void SetOllamaBusy(bool busy, string? message)
    {
        try
        {
            OllamaInstallButton.IsEnabled = !busy;
            OllamaStartButton.IsEnabled = !busy;
            OllamaRefreshButton.IsEnabled = !busy;
            OllamaInstallDirBrowse.IsEnabled = !busy;      // v3.0.0-alpha.5 — location controls
            OllamaInstallDirSave.IsEnabled = !busy;
            OllamaInstallDirReset.IsEnabled = !busy;
            OllamaModelsMoveButton.IsEnabled = !busy;
            OllamaModelsOpenButton.IsEnabled = !busy;
            OllamaRestartButton.IsEnabled = !busy;
            foreach (var b in _pullButtons)
                b.IsEnabled = !busy;
            OllamaProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            OllamaProgressText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (message is not null) OllamaProgressText.Text = message;
            if (!busy)
            {
                OllamaProgress.Value = 0;
                OllamaProgress.IsIndeterminate = false;
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OllamaBusy", ex); }
    }

    private async void OnOllamaInstall(object sender, RoutedEventArgs e)
    {
        if (_ollamaBusy) return;
        _ollamaBusy = true;
        try
        {
            _ollamaCts?.Cancel();
            _ollamaCts?.Dispose();
            _ollamaCts = new CancellationTokenSource();
            var ct = _ollamaCts.Token;

            SetOllamaBusy(true, "Downloading Ollama from ollama.com…");
            string? path = await Task.Run(() => OllamaManager.DownloadInstallerAsync((done, total) =>
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (total > 0)
                        {
                            OllamaProgress.IsIndeterminate = false;
                            OllamaProgress.Value = 100.0 * done / total;
                        }
                        OllamaProgressText.Text = "Downloading Ollama — " + FmtBytes(done) + (total > 0 ? " / " + FmtBytes(total) : "");
                    }
                    catch { }
                }), ct)).ConfigureAwait(true);

            if (path is null)
            {
                _ollamaBusy = false;
                SetOllamaBusy(false, null);
                RenderOllamaPanel();
                OllamaStatusText.Text = "Download failed — check the log, or install manually from ollama.com";
                return;
            }

            OllamaProgress.IsIndeterminate = true;
            OllamaProgressText.Text = "Running the installer — this can take a minute or two…";
            // v3.0.0-alpha.5 — honour the user's install folder (Inno Setup /DIR) when set
            string? installDir = (_settings.OllamaInstallDir ?? "").Trim();
            bool ok = await Task.Run(() => OllamaManager.RunInstallerAsync(path, ct, string.IsNullOrWhiteSpace(installDir) ? null : installDir)).ConfigureAwait(true);

            OllamaProgressText.Text = "Installed — waiting for the local server…";
            await Task.Delay(2500).ConfigureAwait(true);   // give the service a beat to bind the port
            await Task.Run(() => OllamaManager.RefreshStatusAsync(_settings.AiEndpoint)).ConfigureAwait(true);
            _ollamaBusy = false;
            SetOllamaBusy(false, null);
            RenderOllamaPanel();
            if (OllamaManager.Current.ServerUp)
                OllamaStatusText.Text = "Ollama installed and running — pick a lightweight model below";
            else if (ok)
                OllamaStatusText.Text = "Ollama installed — if the server isn't up yet, press Start Ollama or Refresh";
            else
                OllamaStatusText.Text = "The installer exited unexpectedly — press Refresh, or install manually from ollama.com";
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Settings.OllamaInstall", ex);
            _ollamaBusy = false;
            SetOllamaBusy(false, null);
        }
        finally { _ollamaBusy = false; }
    }

    private async void OnOllamaStart(object sender, RoutedEventArgs e)
    {
        if (_ollamaBusy) return;
        _ollamaBusy = true;
        try
        {
            SetOllamaBusy(true, "Starting Ollama…");
            await Task.Run(() => OllamaManager.StartServer()).ConfigureAwait(true);
            await Task.Delay(2500).ConfigureAwait(true);   // the server needs a moment to bind the port
            await Task.Run(() => OllamaManager.RefreshStatusAsync(_settings.AiEndpoint)).ConfigureAwait(true);
            _ollamaBusy = false;
            SetOllamaBusy(false, null);
            RenderOllamaPanel();
            if (!OllamaManager.Current.ServerUp)
                OllamaStatusText.Text = "Ollama didn't respond yet — if it just started, press Refresh in a few seconds";
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Settings.OllamaStart", ex);
            _ollamaBusy = false;
            SetOllamaBusy(false, null);
        }
        finally { _ollamaBusy = false; }
    }

    private async void OnOllamaPull(object sender, RoutedEventArgs e)
    {
        if (_ollamaBusy) return;
        if (sender is not Button { Tag: string model }) return;
        _ollamaBusy = true;
        try
        {
            _ollamaCts?.Cancel();
            _ollamaCts?.Dispose();
            _ollamaCts = new CancellationTokenSource();
            var ct = _ollamaCts.Token;

            SetOllamaBusy(true, $"Pulling {model} — starting…");
            var final = await Task.Run(() => OllamaManager.PullAsync(_settings.AiEndpoint, model, p =>
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (p.TotalBytes > 0)
                        {
                            OllamaProgress.IsIndeterminate = false;
                            OllamaProgress.Value = p.Fraction * 100;
                            OllamaProgressText.Text = $"Pulling {model} — {p.Fraction * 100:F0}% · {FmtBytes(p.DoneBytes)} / {FmtBytes(p.TotalBytes)}";
                        }
                        else if (!string.IsNullOrEmpty(p.Status))
                        {
                            OllamaProgressText.Text = $"Pulling {model} — {p.Status}…";
                        }
                    }
                    catch { }
                }), ct)).ConfigureAwait(true);

            if (final.Ok)
            {
                await Task.Run(() => OllamaManager.RefreshStatusAsync(_settings.AiEndpoint)).ConfigureAwait(true);
                _ollamaBusy = false;
                SetOllamaBusy(false, null);
                RenderOllamaPanel();

                // adopt the fresh model when the current setting isn't on disk
                var installedNames = OllamaManager.Current.Models.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!installedNames.Contains(_settings.AiModel))
                {
                    _settings.AiModel = model;
                    AiModelBox.Text = model;
                }
                OllamaStatusText.Text = $"{model} ready — type ? in the launcher to ask. Active model: {_settings.AiModel}";
            }
            else
            {
                _ollamaBusy = false;
                SetOllamaBusy(false, null);
                RenderOllamaPanel();
                OllamaStatusText.Text = final.Error == "cancelled"
                    ? "Pull cancelled"
                    : $"Pull failed — {final.Error}";
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Settings.OllamaPull", ex);
            _ollamaBusy = false;
            SetOllamaBusy(false, null);
        }
        finally { _ollamaBusy = false; }
    }

    private void OnOllamaUse(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string model }) return;
            _settings.AiModel = model;
            AiModelBox.Text = model;
            RenderOllamaPanel();
            OllamaStatusText.Text = $"{model} is now the active model — ? in the launcher uses it";
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OllamaUse", ex); }
    }

    private async void OnOllamaDelete(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string model }) return;
            if (_ollamaBusy) return;
            var confirm = MessageBox.Show(this,
                $"Uninstall {model} and remove it from disk to free its space?", "Lumo",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _ollamaBusy = true;
            SetOllamaBusy(true, $"Deleting {model}…");
            var (ok, err) = await Task.Run(() => OllamaManager.DeleteModelAsync(_settings.AiEndpoint, model)).ConfigureAwait(true);
            await Task.Run(() => OllamaManager.RefreshStatusAsync(_settings.AiEndpoint)).ConfigureAwait(true);
            _ollamaBusy = false;
            SetOllamaBusy(false, null);
            RenderOllamaPanel();
            OllamaStatusText.Text = ok ? $"{model} deleted" : $"Delete failed — {err}";
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Settings.OllamaDelete", ex);
            _ollamaBusy = false;
            SetOllamaBusy(false, null);
        }
        finally { _ollamaBusy = false; }
    }

    // ------------------------------------------------- v3.0.0-alpha.5 — install & model locations

    /// <summary>Picks the folder Ollama is (or will be) installed in. .NET 8's OpenFolderDialog — no WinForms dependency.</summary>
    private string? _pendingOllamaInstallDir;

    private void OnOllamaInstallDirBrowse(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Where should Ollama live?",
                InitialDirectory = TryGetInitialDir(_settings.OllamaInstallDir),
            };
            if (dlg.ShowDialog(this) == true)
            {
                _pendingOllamaInstallDir = dlg.FolderName.TrimEnd(Path.DirectorySeparatorChar);
                OllamaInstallDirSave.Visibility = Visibility.Visible;   // explicit Save keeps it deliberate
                OllamaStatusText.Text = "Press \"Save location\" to keep " + _pendingOllamaInstallDir;
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OllamaInstallDirBrowse", ex); }
    }

    /// <summary>Persists the folder chosen in Browse (and immediately re-probes for ollama.exe).</summary>
    private void OnOllamaInstallDirSave(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = (_pendingOllamaInstallDir ?? "").Trim();
            if (dir.Length == 0) return;
            _settings.OllamaInstallDir = dir;
            OllamaManager.CustomInstallDir = dir;
            _settings.Save();
            _pendingOllamaInstallDir = null;
            OllamaInstallDirSave.Visibility = Visibility.Collapsed;
            OllamaManager.Invalidate();
            _ = ProbeAndRenderOllamaAsync();
            OllamaStatusText.Text = "Install location saved — Lumo will look for ollama.exe there first";
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OllamaInstallDirSave", ex); }
    }

    /// <summary>Back to the standard roots.</summary>
    private void OnOllamaInstallDirReset(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.OllamaInstallDir = "";
            OllamaManager.CustomInstallDir = "";
            _settings.Save();
            _pendingOllamaInstallDir = null;
            OllamaInstallDirSave.Visibility = Visibility.Collapsed;
            OllamaManager.Invalidate();
            _ = ProbeAndRenderOllamaAsync();
            OllamaStatusText.Text = "Install location reset — the standard roots and PATH are probed";
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OllamaInstallDirReset", ex); }
    }

    /// <summary>
    /// Moves Ollama's model storage: folder picker → OLLAMA_MODELS (user env) →
    /// server restart so it is live immediately. Existing models stay in the old
    /// folder (the description says so); a download later re-fills the new one.
    /// </summary>
    private async void OnOllamaModelsDirChange(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_ollamaBusy) return;
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Where should models be stored?",
                InitialDirectory = TryGetInitialDir(OllamaManager.ModelsDir),
            };
            if (dlg.ShowDialog(this) != true) return;

            string newDir = dlg.FolderName.TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(newDir, OllamaManager.ModelsDir, StringComparison.OrdinalIgnoreCase))
            {
                OllamaStatusText.Text = "That is already the model storage folder";
                return;
            }

            var confirm = MessageBox.Show(this,
                "Point Ollama at this folder for model storage?\n\n" + newDir +
                "\n\nLUMO sets the OLLAMA_MODELS environment variable and restarts Ollama so it takes effect now." +
                "\n\nModels already downloaded stay in the old folder — move them with File Explorer if you want to keep them.",
                "Lumo", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _ollamaBusy = true;
            SetOllamaBusy(true, "Moving model storage — restarting Ollama…");
            _ollamaCts?.Cancel();
            _ollamaCts?.Dispose();
            _ollamaCts = new CancellationTokenSource();
            var (ok, err) = await Task.Run(() => OllamaManager.SetModelsDirAsync(newDir, _ollamaCts.Token)).ConfigureAwait(true);
            await Task.Run(() => OllamaManager.RefreshStatusAsync(_settings.AiEndpoint)).ConfigureAwait(true);
            _ollamaBusy = false;
            SetOllamaBusy(false, null);
            RenderOllamaPanel();
            OllamaStatusText.Text = ok
                ? $"Model storage now {OllamaManager.ModelsDir} — new downloads land there"
                : $"Couldn't move model storage — {err}";
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Settings.OllamaModelsDir", ex);
            _ollamaBusy = false;
            SetOllamaBusy(false, null);
        }
        finally { _ollamaBusy = false; }
    }

    private void OnOllamaOpenModelsFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = OllamaManager.ModelsDir;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OllamaOpenModels", ex); }
    }

    private async void OnOllamaRestart(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_ollamaBusy) return;
            _ollamaBusy = true;
            SetOllamaBusy(true, "Restarting Ollama…");
            _ollamaCts?.Cancel();
            _ollamaCts?.Dispose();
            _ollamaCts = new CancellationTokenSource();
            bool up = await Task.Run(() => OllamaManager.RestartServerAsync(_ollamaCts.Token)).ConfigureAwait(true);
            await Task.Run(() => OllamaManager.RefreshStatusAsync(_settings.AiEndpoint)).ConfigureAwait(true);
            _ollamaBusy = false;
            SetOllamaBusy(false, null);
            RenderOllamaPanel();
            OllamaStatusText.Text = up
                ? "Ollama restarted and answering"
                : "Ollama didn't come back up — press Start Ollama, or check the log";
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Settings.OllamaRestart", ex);
            _ollamaBusy = false;
            SetOllamaBusy(false, null);
        }
        finally { _ollamaBusy = false; }
    }

    /// <summary>Safe seed folder for a picker: falls back to the user profile when the path doesn't exist yet.</summary>
    private static string? TryGetInitialDir(string? path)
    {
        try
        {
            string p = (path ?? "").Trim();
            if (p.Length > 0 && Directory.Exists(p)) return p;
            string? parent = Path.GetDirectoryName(p);
            return parent is not null && Directory.Exists(parent) ? parent : null;
        }
        catch { return null; }
    }

    // ------------------------------------------------- v3.0.0-alpha.5 — Storage & maintenance

    /// <summary>Background scan of Lumo's caches → dispatcher render. Fire-and-forget safe.</summary>
    private async Task LoadCleanupListAsync()
    {
        try
        {
            var items = await Task.Run(() => AppCleanup.Scan()).ConfigureAwait(true);
            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    bool busy = _cleanupBusy;
                    CleanupList.Items.Clear();
                    foreach (var item in items)
                        CleanupList.Items.Add(BuildCleanupRow(item));
                    if (!busy && CleanupStatus.Text.Length == 0)
                        CleanupStatus.Text = items.Any(i => i.Clearable)
                            ? "Clear a row to free its space — nothing user-created is ever touched."
                            : "Nothing to clear right now — every location is already empty.";
                }
                catch (Exception ex) { DiagnosticLogger.LogException("Settings.CleanupRender", ex); }
            });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.CleanupScan", ex); }
    }

    private FrameworkElement BuildCleanupRow(AppCleanup.CleanupItem item)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = item.Label + (item.Bytes > 0 ? $"   ·   {FmtBytes(item.Bytes)}" : "   ·   empty"),
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TitleBrush");
        var path = new TextBlock { Text = item.Path, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Path };
        path.SetResourceReference(TextBlock.ForegroundProperty, "SubtitleBrush");
        var hint = new TextBlock { Text = item.Hint, FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SubtitleBrush");
        text.Children.Add(name);
        text.Children.Add(path);
        text.Children.Add(hint);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        if (item.Clearable)
        {
            var clear = new Button
            {
                Content = "Clear",
                Padding = new Thickness(13, 4, 13, 4),
                Tag = item.Id,
                ToolTip = item.Hint,
            };
            if (TryFindResource("GhostButton") is Style s) clear.Style = s;
            clear.Click += OnCleanupClear;
            Grid.SetColumn(clear, 1);
            grid.Children.Add(clear);
        }
        return grid;
    }

    private bool _cleanupBusy;   // one clear at a time — sizes refresh after each

    private async void OnCleanupClear(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string id } || _cleanupBusy) return;
            var confirm = MessageBox.Show(this,
                id == "whisper"
                    ? "Delete the downloaded voice models? The next voice session re-downloads them."
                    : "Clear this location? It only holds re-creatable files.",
                "Lumo", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _cleanupBusy = true;
            var (ok, err, freed) = await Task.Run(() => AppCleanup.Clear(id)).ConfigureAwait(true);
            _cleanupBusy = false;
            CleanupStatus.Text = ok
                ? (freed > 0 ? $"Freed {FmtBytes(freed)}." : "Already clear.")
                : $"Couldn't clear — {err}";
            _ = LoadCleanupListAsync();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.CleanupClear", ex); }
    }

    private void OnCleanupRefresh(object sender, RoutedEventArgs e)
    {
        try { _ = LoadCleanupListAsync(); }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.CleanupRefresh", ex); }
    }

    // ------------------------------------------------- v3.0.0-alpha.5 — the App Deck (General page)

    private void OnOpenDeck(object sender, RoutedEventArgs e)
    {
        try { _openDeck?.Invoke(); }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.OpenDeck", ex); }
    }

    private static string FmtBytes(long b)
    {
        try
        {
            if (b >= 1_000_000_000) return $"{b / 1_000_000_000.0:0.0} GB";
            if (b >= 1_000_000) return $"{b / 1_000_000.0:0} MB";
            if (b >= 1_000) return $"{b / 1_000.0:0} KB";
            return $"{b} B";
        }
        catch { return "?"; }
    }
}
