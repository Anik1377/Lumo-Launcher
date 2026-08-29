using System.Windows.Forms;
using Lumo.Core;
using WinForms = System.Windows.Forms;

namespace Lumo.Services;

/// <summary>
/// Tray icon controller.
///
/// v1.1: single left-click opens the window; right-click menu offers Open, theme toggle,
/// the settings folder and Exit.
/// v1.2: added a “Settings…” item that opens the full customization window.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Settings _settings;
    private readonly Action _openLauncher;
    private readonly Action? _openSettings;
    private readonly Action _exit;

    public TrayController(Settings settings, Action openLauncher, Action? openSettings, Action exit)
    {
        _settings = settings;
        _openLauncher = openLauncher;
        _openSettings = openSettings;
        _exit = exit;

        _icon = new NotifyIcon
        {
            Icon = IconFactory.CreateAppIcon(32),
            Text = $"Lumo v1.4 — press {settings.Hotkey}",
            Visible = true,
        };

        // Single left-click opens the launcher (the known-good path is preserved too:
        // a double click simply raises two MouseClicks which is idempotent "show").
        _icon.MouseClick += OnMouseClick;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Lumo", null, (_, _) => Safe(_openLauncher));
        menu.Items.Add("Settings…", null, (_, _) => { if (_openSettings is not null) Safe(_openSettings); });
        menu.Items.Add(new ToolStripSeparator());

        var themeItem = new ToolStripMenuItem("Toggle light/dark theme");
        themeItem.Click += (_, _) => Safe(() =>
        {
            _settings.Theme = _settings.Theme == "dark" ? "light" : "dark";
            _settings.Save();
            ThemeChanged?.Invoke();
        });
        menu.Items.Add(themeItem);

        menu.Items.Add("Open settings folder", null, (_, _) => Safe(OpenSettingsFolder));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Lumo", null, (_, _) => Safe(_exit));

        _icon.ContextMenuStrip = menu;
    }

    /// <summary>Raised when the user toggles the theme from the tray menu.</summary>
    public event Action? ThemeChanged;

    /// <summary>Update the tray tooltip (e.g. after the hotkey changed in Settings).</summary>
    public void UpdateText(string text)
    {
        try { _icon.Text = text.Length > 63 ? text[..63] : text; } catch { }
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) Safe(_openLauncher);
    }

    private static void OpenSettingsFolder()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.SettingsDir,
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Tray.OpenSettings", ex); }
    }

    private static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { DiagnosticLogger.LogException("Tray", ex); }
    }

    public void Dispose()
    {
        try
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        catch { }
    }
}
