using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lumo.Core;
using Lumo.Native;
using Lumo.Services;

namespace Lumo.UI;

/// <summary>
/// v3.0.0-alpha.6 — the App Deck app picker (user request: "clicking on a slot
/// should open a window with all the installed apps on the pc with a search
/// button, and give the user an easy way to select an app and assign it").
///
/// Lists every Start Menu + Desktop shortcut (the same bounded index the
/// launcher search uses — one private instance, scanned on open), live fuzzy
/// search over it, real shell icons, full keyboard flow: type → ↑↓ → Enter.
/// Double-click works too. "Browse for a file…" keeps the classic dialog for
/// anything that isn't a Start Menu app. The result lands in
/// <see cref="PickedPath"/>/<see cref="PickedName"/> when DialogResult=true.
/// </summary>
public partial class AppPickerWindow : Window
{
    /// <summary>One row the list can bind straight to (icon resolved once in code).</summary>
    public sealed record PickerRow(string Name, string Path, string DisplayPath, ImageSource? IconImage);

    private readonly Settings _settings;
    private readonly UsageStore? _usage;
    private readonly AppIndex _index = new();
    private System.Windows.Threading.DispatcherTimer? _loadTimer;
    private int _loadTicks;
    private List<PickerRow> _rows = new();
    private bool _browsing;   // suppress re-filter churn while the file dialog owns the window

    /// <summary>The assigned target ("" when cancelled).</summary>
    public string PickedPath { get; private set; } = "";

    /// <summary>The friendly name to prefill the slot with ("" when cancelled).</summary>
    public string PickedName { get; private set; } = "";

    private bool _sourceReady;

    public AppPickerWindow(Settings settings, string slotLabel, UsageStore? usage = null)
    {
        InitializeComponent();
        _settings = settings;
        _usage = usage;
        Title = string.IsNullOrWhiteSpace(slotLabel) ? "Choose an app" : $"Choose an app — {slotLabel}";
        CaptionSlot.Text = slotLabel;

        PickerSearch.TextChanged += OnSearchChanged;
        Closed += (_, _) =>
        {
            try
            {
                _loadTimer?.Stop();
                _loadTimer = null;
            }
            catch { }
        };
        Loaded += (_, _) =>
        {
            try { PickerSearch.Focus(); } catch { }
        };

        ApplySelfTheme();
        StartScan();
    }

    // ---------------------------------------------------------------- theme

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _sourceReady = true;
        try { ApplySelfTheme(); } catch (Exception ex) { DiagnosticLogger.LogException("AppPicker.Theme", ex); }
    }

    /// <summary>The same Fluent token set as the deck — same family, same rules.</summary>
    private void ApplySelfTheme()
    {
        try
        {
            var t = ThemeService.Apply(this, _settings);
            if (_sourceReady) GlassBackdrop.Apply(this, t.Dark);

            bool rounded = !string.Equals(_settings.CornerStyle, "square", StringComparison.OrdinalIgnoreCase);
            float r = GlassBackdrop.IsWin11 && rounded ? 8f : 0f;
            RootCard.CornerRadius = new CornerRadius(r);
            CaptionBar.CornerRadius = new CornerRadius(r, r, 0, 0);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AppPicker.ApplyTheme", ex); }
    }

    // ---------------------------------------------------------------- the index

    /// <summary>
    /// Scans Start Menu + Desktop on a background thread (typically well under a
    /// second) and rebuilds the list as soon as it lands — the picker is usable
    /// the instant it opens, and the rows appear without the user doing anything.
    /// </summary>
    private void StartScan()
    {
        try
        {
            _index.BeginIndexInBackground();
            _loadTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _loadTicks = 0;
            _loadTimer.Tick += (_, _) =>
            {
                try
                {
                    Rebuild();
                    // keep polling while the scan warms up (48 × 250 ms = 12 s cap)
                    if (_index.Entries.Count > 0 || ++_loadTicks > 48)
                    {
                        _loadTimer?.Stop();
                        if (_index.Entries.Count == 0) Rebuild();   // final pass — may show "none found"
                    }
                }
                catch (Exception ex) { DiagnosticLogger.LogException("AppPicker.LoadTick", ex); }
            };
            _loadTimer.Start();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AppPicker.Scan", ex); }
    }

    // ---------------------------------------------------------------- filtering

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            SearchHint.Visibility = PickerSearch.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            Rebuild();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AppPicker.Search", ex); }
    }

    private void Rebuild()
    {
        if (_browsing) return;
        var entries = _index.Entries;
        string query = PickerSearch.Text;

        var picked = AppPicker.Filter(entries, query, key => _usage?.Get(key));
        _rows = picked.Select(e => new PickerRow(
            e.Name, e.Path, e.Path,
            AppIcons.ForPath(e.Path))).ToList();
        PickerList.ItemsSource = _rows;

        // status line under the search box
        if (entries.Count == 0)
        {
            PickerCount.Text = _loadTimer is { IsEnabled: true }
                ? "Looking for installed apps…"
                : "No apps found on this PC";
        }
        else if (query.Trim().Length == 0)
        {
            PickerCount.Text = _rows.Count < entries.Count
                ? $"{entries.Count} apps — showing the {PickerRowLimitFor(entries.Count)} you open most; type to search"
                : $"{entries.Count} apps — most-used first; type to search";
        }
        else
        {
            PickerCount.Text = $"{_rows.Count} match(es)";
        }

        // empty states
        bool empty = _rows.Count == 0;
        PickerEmpty.Text = entries.Count == 0
            ? "No apps were found in the Start Menu or on the Desktop.\nUse \"Browse for a file…\" below to pick one manually."
            : $"Nothing matches \"{query.Trim()}\".\nTry a shorter search, or use \"Browse for a file…\".";
        PickerEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        if (PickerList.Items.Count > 0 && PickerList.SelectedIndex < 0)
            PickerList.SelectedIndex = 0;   // Enter should always have a target
    }

    private static string PickerRowLimitFor(int total) =>
        total > AppPicker.BrowseLimit ? $"{AppPicker.BrowseLimit}" : total.ToString();

    // ---------------------------------------------------------------- actions

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // a selection made by the mouse should not be clobbered by the auto-select
        // in Rebuild — nothing to do here today, kept as the hook for row feedback.
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => ConfirmSelection();

    private void ConfirmSelection()
    {
        try
        {
            if (PickerList.SelectedItem is PickerRow row)
            {
                PickedPath = row.Path;
                PickedName = row.Name;
                DialogResult = true;
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AppPicker.Confirm", ex); }
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        try
        {
            _browsing = true;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pick an app, shortcut, file or script",
                Filter = "Programs and shortcuts (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(this) == true)
            {
                PickedPath = dlg.FileName;
                PickedName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                DialogResult = true;
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AppPicker.Browse", ex); }
        finally { _browsing = false; }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
    }

    // ---------------------------------------------------------------- chrome + keys

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        try { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
        catch { }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
                return;
            }
            if (e.Key is Key.Enter or Key.Return && PickerList.SelectedItem is PickerRow)
            {
                e.Handled = true;
                ConfirmSelection();
                return;
            }
            // ↑↓ land on the list even while the search box keeps keyboard focus
            if (e.Key is Key.Up or Key.Down)
            {
                e.Handled = true;
                int delta = e.Key == Key.Up ? -1 : 1;
                int target = Math.Clamp((PickerList.SelectedIndex < 0 ? 0 : PickerList.SelectedIndex) + delta,
                    0, Math.Max(0, PickerList.Items.Count - 1));
                if (PickerList.Items.Count > 0)
                {
                    PickerList.SelectedIndex = target;
                    try { PickerList.ScrollIntoView(PickerList.SelectedItem); } catch { }
                }
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AppPicker.Key", ex); }
    }
}
