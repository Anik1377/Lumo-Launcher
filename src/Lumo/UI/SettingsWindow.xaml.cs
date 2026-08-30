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
    private readonly Action _rebuildIndex;
    private readonly ShortcutStore _shortcuts;   // v1.4
    private readonly Action? _recordMacro;       // v1.5
    private readonly Func<bool>? _recordingActive; // v1.5.1 — is a recording live right now?
    private int _initialPage = 0;

    private readonly List<(Border Box, string Hex)> _swatches = new();
    private bool _suppress;                   // guard while programmatically wiring controls
    private string _pendingHotkey = "";
    private bool _pendingStartWithWindows;
    // (_previewRotation removed in v2.0.1 — the rim comet lives on the launcher only;
    //  the settings preview card shows a static gradient of the style's palette)
    private bool _sourceReady;                // v1.8 — glass needs the HWND; set in OnSourceInitialized

    public SettingsWindow(Settings settings, Action applyAppearance, Func<string> applyHotkey, Action rebuildIndex,
                          ShortcutStore? shortcuts = null, Action? recordMacro = null,
                          Func<bool>? recordingActive = null, int initialPage = 0)
    {
        InitializeComponent();
        _settings = settings;
        _snapshot = settings.Clone();
        _applyAppearance = applyAppearance;
        _applyHotkey = applyHotkey;
        _rebuildIndex = rebuildIndex;
        _shortcuts = shortcuts ?? new ShortcutStore();
        _recordMacro = recordMacro;
        _recordingActive = recordingActive;
        _initialPage = initialPage;

        BuildAccentSwatches();
        LoadFromSettings();
        ApplySelfTheme();
        UpdateRecordButton();   // v1.5.1 — reflect live recording state on open
        UpdatePreview();
        PlayEntrance();

        _shortcuts.Changed += () => Dispatcher.InvokeAsync(() => { try { LoadShortcutList(); } catch { } });
        LoadShortcutList();

        LogPathText.Text = "Log: " + AppPaths.LogFile;
        var ver = typeof(SettingsWindow).Assembly.GetName().Version;
        string vs = ver is null ? "v1.4" : $"v{ver.Major}.{ver.Minor}";
        VersionText.Text = vs;
        AboutVersion.Text = vs;

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
            var dlg = new ShortcutEditorWindow(_shortcuts, _settings, null) { Owner = this };
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
            var dlg = new ShortcutEditorWindow(_shortcuts, _settings, live) { Owner = this };
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
            bool dark = _settings.EffectiveDark();

            // v2.0 — DWM chrome only (rounded corners + dark-mode context); no acrylic.
            if (_sourceReady)
                GlassBackdrop.Apply(this, dark);

            var p = Appearance.PaletteFor(dark, _settings.AccentColor);

            // Windows 11 Fluent surface tokens — solid, no blur dependency.
            Color field   = dark ? FromRgb(0x2D, 0x2D, 0x2D) : FromRgb(0xFB, 0xFB, 0xFB);
            Color card    = dark ? FromRgb(0x2B, 0x2B, 0x2B) : Colors.White;
            Color sidebar = dark ? FromRgb(0x1C, 0x1C, 0x1C) : FromRgb(0xEC, 0xEC, 0xEC);
            Color segTrack = dark ? FromRgb(0x3B, 0x3B, 0x3B) : FromRgb(0xE6, 0xE6, 0xE6);
            Color segSel  = dark ? FromRgb(0x4A, 0x4A, 0x4A) : Colors.White;

            Resources["TitleBrush"] = new SolidColorBrush(p.Title);
            Resources["SubtitleBrush"] = new SolidColorBrush(p.Subtitle);
            Resources["HoverBrush"] = new SolidColorBrush(p.Hover);
            Resources["SelectedBrush"] = new SolidColorBrush(p.Selected);
            Resources["AccentBrush"] = new SolidColorBrush(p.Accent);
            Resources["SeparatorBrush"] = new SolidColorBrush(p.Separator);
            Resources["BorderLineBrush"] = new SolidColorBrush(p.Border);
            Resources["ChipBrush"] = new SolidColorBrush(dark
                ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0D, 0x00, 0x00, 0x00));
            Resources["FieldBrush"] = new SolidColorBrush(field);
            Resources["CardBrush"] = new SolidColorBrush(card);
            Resources["SidebarBrush"] = new SolidColorBrush(sidebar);
            Resources["SegTrackBrush"] = new SolidColorBrush(segTrack);
            Resources["SegSelBrush"] = new SolidColorBrush(segSel);

            float r = GlassBackdrop.IsWin11 ? 8f : 0f;
            Root.Background = new SolidColorBrush(p.Panel);
            Root.CornerRadius = new CornerRadius(r);
            Root.BorderBrush = new SolidColorBrush(p.Border);
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

            switch (_settings.WebEngine?.ToLowerInvariant())
            {
                case "bing": EngineBing.IsChecked = true; break;
                case "duckduckgo": EngineDdg.IsChecked = true; break;
                default: EngineGoogle.IsChecked = true; break;
            }

            BorderEffectToggle.IsChecked = _settings.BorderEffect;
            switch (_settings.BorderStyle?.ToLowerInvariant())
            {
                case "sunset": StyleSunset.IsChecked = true; break;
                case "ocean": StyleOcean.IsChecked = true; break;
                case "ember": StyleEmber.IsChecked = true; break;
                case "mint": StyleMint.IsChecked = true; break;
                case "solid": StyleSolid.IsChecked = true; break;
                default: StyleAurora.IsChecked = true; break;
            }

            double s = _settings.BorderSpeedSec;
            if (s <= 7.0) SpeedFast.IsChecked = true;        // v2.0.1 speeds: 6 / 9 / 14
            else if (s <= 11.0) SpeedNormal.IsChecked = true;
            else SpeedSlow.IsChecked = true;

            if (string.Equals(_settings.Theme, "light", StringComparison.OrdinalIgnoreCase))
                ThemeLight.IsChecked = true;
            else if (string.Equals(_settings.Theme, "auto", StringComparison.OrdinalIgnoreCase))
                ThemeAuto.IsChecked = true;
            else
                ThemeDark.IsChecked = true;

            AccentHexBox.Text = _settings.AccentColor;
            MarkSelectedSwatch(_settings.AccentColor);

            MaxFilesSlider.Value = Math.Clamp(_settings.MaxIndexedFiles, 10_000, 300_000);
            MaxFilesLabel.Text = ((int)MaxFilesSlider.Value).ToString("N0");

            _pendingHotkey = string.IsNullOrWhiteSpace(_settings.Hotkey) ? "Alt+Space" : _settings.Hotkey;
            HotkeyDisplay.Text = _pendingHotkey;

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
            EngineGoogle.Checked += (_, _) => { if (!_suppress) _settings.WebEngine = "google"; };
            EngineBing.Checked += (_, _) => { if (!_suppress) _settings.WebEngine = "bing"; };
            EngineDdg.Checked += (_, _) => { if (!_suppress) _settings.WebEngine = "duckduckgo"; };

            BorderEffectToggle.Click += (_, _) => SyncLiveAppearance();
            ThemeDark.Checked += (_, _) => SyncLiveAppearance();
            ThemeLight.Checked += (_, _) => SyncLiveAppearance();
            ThemeAuto.Checked += (_, _) => SyncLiveAppearance();
            StyleAurora.Checked += (_, _) => SyncLiveAppearance();
            StyleSunset.Checked += (_, _) => SyncLiveAppearance();
            StyleOcean.Checked += (_, _) => SyncLiveAppearance();
            StyleEmber.Checked += (_, _) => SyncLiveAppearance();
            StyleMint.Checked += (_, _) => SyncLiveAppearance();
            StyleSolid.Checked += (_, _) => SyncLiveAppearance();
            SpeedFast.Checked += (_, _) => SyncLiveAppearance();
            SpeedNormal.Checked += (_, _) => SyncLiveAppearance();
            SpeedSlow.Checked += (_, _) => SyncLiveAppearance();

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
            _settings.BorderStyle =
                StyleSunset.IsChecked == true ? "Sunset" :
                StyleOcean.IsChecked == true ? "Ocean" :
                StyleEmber.IsChecked == true ? "Ember" :
                StyleMint.IsChecked == true ? "Mint" :
                StyleSolid.IsChecked == true ? "Solid" : "Aurora";
            _settings.BorderSpeedSec =
                SpeedFast.IsChecked == true ? 6.0 :
                SpeedSlow.IsChecked == true ? 14.0 : 9.0;

            ApplySelfTheme();
            UpdatePreview();
            MarkSelectedSwatch(_settings.AccentColor);
            FooterHint.Text = "Changes applied live — press Save to keep them";
            _applyAppearance();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.SyncLive", ex); }
    }

    private void UpdatePreview()
    {
        try
        {
            if (_settings.BorderEffect)
            {
                // v2.0.1 — static palette gradient (head → body → deep body). The real
                // comet runs live on the launcher window behind the settings app.
                PreviewCard.BorderBrush = Appearance.BuildPreviewBrush(_settings.BorderStyle, _settings.AccentColor);
            }
            else
            {
                bool dark = !string.Equals(_settings.Theme, "light", StringComparison.OrdinalIgnoreCase);
                PreviewCard.BorderBrush = new SolidColorBrush(dark ? FromRgb(0x33, 0x36, 0x4A) : FromRgb(0xE2, 0xE4, 0xEC));
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.Preview", ex); }
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
}
