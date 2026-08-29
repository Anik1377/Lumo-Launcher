using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Lumo.Services;
using Appearance = Lumo.Services.Appearance;
using Color = System.Windows.Media.Color;

namespace Lumo.UI;

/// <summary>
/// v1.4 — create / edit a user shortcut or macro. Small, focused dialog that
/// matches the settings window's Apple-clean look and self-themes from the
/// live palette. Save persists through the shared ShortcutStore.
/// </summary>
public partial class ShortcutEditorWindow : Window
{
    private readonly ShortcutStore _store;
    private readonly Settings _settings;
    private readonly ShortcutDef _def;
    private bool _suppress;

    public ShortcutEditorWindow(ShortcutStore store, Settings settings, ShortcutDef? existing, string? presetName = null)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;
        _def = existing is null ? new ShortcutDef { Name = presetName ?? "" } : Clone(existing);

        ApplySelfTheme();
        LoadDef();
        PlayEntrance();

        Dispatcher.BeginInvoke(() => { try { NameBox.Focus(); NameBox.CaretIndex = NameBox.Text.Length; } catch { } },
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private static ShortcutDef Clone(ShortcutDef s) => new()
    {
        Id = s.Id, Name = s.Name, Type = s.Type, Target = s.Target,
        Steps = new List<string>(s.Steps), Keywords = s.Keywords,
    };

    // ---------------------------------------------------------------- theming

    private void ApplySelfTheme()
    {
        try
        {
            bool dark = _settings.EffectiveDark();
            var p = Appearance.PaletteFor(dark, _settings.AccentColor);
            Color field = dark ? FromRgb(0x2C, 0x2C, 0x2E) : FromRgb(0xF5, 0xF5, 0xF7);
            Color segTrack = dark ? FromRgb(0x2C, 0x2C, 0x2E) : FromRgb(0xE9, 0xE9, 0xEB);
            Color segSel = dark ? FromRgb(0x48, 0x48, 0x4A) : Colors.White;

            Resources["TitleBrush"] = new SolidColorBrush(p.Title);
            Resources["SubtitleBrush"] = new SolidColorBrush(p.Subtitle);
            Resources["HoverBrush"] = new SolidColorBrush(p.Hover);
            Resources["AccentBrush"] = new SolidColorBrush(p.Accent);
            Resources["BorderLineBrush"] = new SolidColorBrush(p.Border);
            Resources["FieldBrush"] = new SolidColorBrush(field);
            Resources["SegTrackBrush"] = new SolidColorBrush(segTrack);
            Resources["SegSelBrush"] = new SolidColorBrush(segSel);

            Root.Background = new SolidColorBrush(p.Panel);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ShortcutEditor.Theme", ex); }
    }

    private static Color FromRgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    /// <summary>Fade + gentle scale-in, matching the settings window.</summary>
    private void PlayEntrance()
    {
        try
        {
            if (!_settings.AnimationsEnabled) return;
            RootScale.ScaleX = RootScale.ScaleY = 0.96;
            Root.Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
        }
        catch { }
    }

    // ---------------------------------------------------------------- load / save

    private void LoadDef()
    {
        _suppress = true;
        try
        {
            NameBox.Text = _def.Name;
            KeywordsBox.Text = _def.Keywords;
            HeaderText.Text = string.IsNullOrEmpty(_def.Target) && _def.Steps.Count == 0 && string.IsNullOrEmpty(_def.Name)
                ? "New shortcut" : "Edit shortcut";

            switch (_def.Type.ToLowerInvariant())
            {
                case "file": TypeFile.IsChecked = true; break;
                case "folder": TypeFolder.IsChecked = true; break;
                case "macro": TypeMacro.IsChecked = true; break;
                default: TypeUrl.IsChecked = true; break;
            }
            TargetBox.Text = _def.Target;
            StepsBox.Text = string.Join(Environment.NewLine, _def.Steps);
            SyncTypePanels();
        }
        finally { _suppress = false; }
    }

    private string SelectedType =>
        TypeFile.IsChecked == true ? "file" :
        TypeFolder.IsChecked == true ? "folder" :
        TypeMacro.IsChecked == true ? "macro" : "url";

    private void SyncTypePanels()
    {
        string t = SelectedType;
        bool macro = t == "macro";
        MacroPanel.Visibility = macro ? Visibility.Visible : Visibility.Collapsed;
        TargetPanel.Visibility = macro ? Visibility.Collapsed : Visibility.Visible;
        BrowseButton.Visibility = t is "file" or "folder" ? Visibility.Visible : Visibility.Collapsed;

        TargetLabel.Text = t switch
        {
            "file" => "FILE PATH",
            "folder" => "FOLDER PATH",
            _ => "URL",
        };
        TargetHint.Text = t switch
        {
            "file" => "e.g.  C:\\Users\\me\\Documents\\report.docx  (%ENV% supported)",
            "folder" => "e.g.  D:\\Projects  (%USERPROFILE%\\Desktop works too)",
            _ => "e.g.  github.com  or  https://mail.google.com",
        };
    }

    private void OnTypeChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        try { SyncTypePanels(); ErrorText.Visibility = Visibility.Collapsed; } catch { }
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SelectedType == "folder")
            {
                var dlg = new OpenFolderDialog { Title = "Pick a folder for this shortcut" };
                if (dlg.ShowDialog(this) == true)
                    TargetBox.Text = dlg.FolderName;
            }
            else
            {
                var dlg = new OpenFileDialog { Title = "Pick a file for this shortcut", CheckFileExists = true };
                if (dlg.ShowDialog(this) == true)
                    TargetBox.Text = dlg.FileName;
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ShortcutEditor.Browse", ex); }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => SaveAndClose();
    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
    private void OnCloseClick(object sender, MouseButtonEventArgs e) => Close();

    private void SaveAndClose()
    {
        try
        {
            string name = NameBox.Text.Trim();
            if (name.Length == 0) { ShowError("Give the shortcut a name — you'll type it after /sc."); return; }
            if (name.Contains(' ') && name.Split(' ').Length > 2)
            {
                ShowError("Keep the name to one or two words — it's easier to type after /sc.");
                return;
            }

            string type = SelectedType;
            if (type == "macro")
            {
                var steps = StepsBox.Text.Split('\n')
                    .Select(s => s.Trim().TrimEnd('\r'))
                    .Where(s => s.Length > 0).Take(12).ToList();
                if (steps.Count == 0)
                {
                    ShowError("A macro needs at least one step — one URL or path per line.");
                    return;
                }
                _def.Steps = steps;
                _def.Target = steps.Count > 0 ? steps[0] : "";
            }
            else
            {
                string target = TargetBox.Text.Trim();
                if (target.Length == 0)
                {
                    ShowError("Add a " + (type == "url" ? "URL" : "path") + " to open.");
                    return;
                }
                _def.Target = target;
                _def.Steps = new List<string>();
            }

            _def.Name = name;
            _def.Type = type;
            _def.Keywords = KeywordsBox.Text.Trim();

            _store.AddOrUpdate(_def);
            DiagnosticLogger.Log("Shortcuts", $"Saved '{_def.Name}' ({_def.Type})");
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("ShortcutEditor.Save", ex);
            ShowError("Couldn't save — details were written to the log.");
        }
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                SaveAndClose();
            }
        }
        catch { }
    }
}
