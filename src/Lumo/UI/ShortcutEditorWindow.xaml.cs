using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Lumo.Core;
using Lumo.Services;
using Appearance = Lumo.Services.Appearance;
using Color = System.Windows.Media.Color;

namespace Lumo.UI;

/// <summary>
/// v1.5 — create / edit a shortcut, or build a macro in the visual builder.
/// Macro mode shows the captured actions as re-orderable cards (Apple Shortcuts
/// style): tap a card to edit it, reorder with the chevrons, remove with ✕,
/// add new actions from the type palette below. Save persists through the
/// shared ShortcutStore; ▶ Test run executes the current draft.
/// </summary>
public partial class ShortcutEditorWindow : Window
{
    /// <summary>Bindable wrapper for one macro action card.</summary>
    public sealed class StepVM : INotifyPropertyChanged
    {
        public MacroStep Step { get; }
        private bool _isSelected;

        public StepVM(MacroStep step) => Step = step;

        public string Glyph => Step.Glyph;
        public string TypeLabel => Step.TypeLabel;
        public string Describe => Step.Describe();

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ShortcutStore _store;
    private readonly Settings _settings;
    private readonly ShortcutDef _def;
    private readonly Func<ShortcutDef, string>? _savedFeedback;   // v2.5 — hotkey status after save
    private bool _suppress;
    private int _editIndex = -1;              // -1 → the editor panel adds a new step
    private string _pendingHotkey = "";       // v2.5 — Task 4.3 capture state

    private readonly ObservableCollection<StepVM> _steps = new();

    public ShortcutEditorWindow(ShortcutStore store, Settings settings, ShortcutDef? existing,
                                string? presetName = null, List<MacroStep>? presetSteps = null,
                                Func<ShortcutDef, string>? savedFeedback = null)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;
        _def = existing is null ? new ShortcutDef { Name = presetName ?? "" } : Clone(existing);
        _savedFeedback = savedFeedback;

        ApplySelfTheme();
        LoadDef(presetSteps);
        PlayEntrance();

        Dispatcher.BeginInvoke(() => { try { NameBox.Focus(); NameBox.CaretIndex = NameBox.Text.Length; } catch { } },
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private static ShortcutDef Clone(ShortcutDef s) => new()
    {
        Id = s.Id, Name = s.Name, Type = s.Type, Target = s.Target,
        Steps = new List<string>(s.Steps), Keywords = s.Keywords, Hotkey = s.Hotkey,
    };

    // ---------------------------------------------------------------- theming

    private void ApplySelfTheme()
    {
        try
        {
            // v3.0 — the shared ThemeService ladder (the old local field values sat a
            // step off the family ladder; now every surface shares one source).
            var t = ThemeService.Apply(this, _settings);
            Root.Background = new SolidColorBrush(t.Panel);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ShortcutEditor.Theme", ex); }
    }

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

    private bool IsMacroType => TypeMacro.IsChecked == true;

    private void LoadDef(List<MacroStep>? presetSteps = null)
    {
        _suppress = true;
        try
        {
            NameBox.Text = _def.Name;
            KeywordsBox.Text = _def.Keywords;
            _pendingHotkey = _def.Hotkey ?? "";   // v2.5 — Task 4.3
            HotkeyDisplay.Text = _pendingHotkey.Length == 0
                ? "(click here, then press a combination)"
                : _pendingHotkey;
            bool fresh = string.IsNullOrEmpty(_def.Target) && _def.Steps.Count == 0 && string.IsNullOrEmpty(_def.Name);
            HeaderText.Text = fresh ? "New shortcut" : "Edit shortcut";

            switch (_def.Type.ToLowerInvariant())
            {
                case "file": TypeFile.IsChecked = true; break;
                case "folder": TypeFolder.IsChecked = true; break;
                case "macro": TypeMacro.IsChecked = true; break;
                case "snippet": TypeSnippet.IsChecked = true; break;
                default: TypeUrl.IsChecked = true; break;
            }
            TargetBox.Text = _def.Target;

            var parsed = presetSteps ?? _def.Steps.Select(MacroStep.Parse).ToList();
            _steps.Clear();
            foreach (var s in parsed) _steps.Add(new StepVM(s));
            StepsList.ItemsSource = _steps;
            SyncStepsEmpty();

            SyncTypePanels();
        }
        finally { _suppress = false; }
    }

    private string SelectedType =>
        TypeFile.IsChecked == true ? "file" :
        TypeFolder.IsChecked == true ? "folder" :
        TypeMacro.IsChecked == true ? "macro" :
        TypeSnippet.IsChecked == true ? "snippet" : "url";

    private void SyncTypePanels()
    {
        string t = SelectedType;
        bool macro = t == "macro";
        bool snippet = t == "snippet";          // v1.6
        MacroPanel.Visibility = macro ? Visibility.Visible : Visibility.Collapsed;
        TargetPanel.Visibility = macro ? Visibility.Collapsed : Visibility.Visible;
        BrowseButton.Visibility = t is "file" or "folder" ? Visibility.Visible : Visibility.Collapsed;

        // v1.6 — snippets hold multi-line text
        TargetBox.AcceptsReturn = snippet;
        TargetBox.MinHeight = snippet ? 88 : 36;
        TargetBox.VerticalScrollBarVisibility = snippet ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        TargetBox.VerticalContentAlignment = snippet ? VerticalAlignment.Top : VerticalAlignment.Center;

        SubtitleText.Text = snippet
            ? "Run it with  /sc name  or  !name  — Enter copies the text, Ctrl+V pastes it"
            : "Run it any time by typing  /sc name  in Lumo";

        TargetLabel.Text = t switch
        {
            "file" => "FILE PATH",
            "folder" => "FOLDER PATH",
            "snippet" => "SNIPPET TEXT",
            _ => "URL",
        };
        TargetHint.Text = t switch
        {
            "file" => "e.g.  C:\\Users\\me\\Documents\\report.docx  (%ENV% supported)",
            "folder" => "e.g.  D:\\Projects  (%USERPROFILE%\\Desktop works too)",
            "snippet" => "Text that gets copied to the clipboard — e-mail drafts, addresses, replies. Multi-line allowed.",
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

    // ---------------------------------------------------------------- steps list

    private void SyncStepsEmpty()
    {
        StepsEmpty.Visibility = _steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<MacroStep> CurrentSteps() => _steps.Select(vm => vm.Step).ToList();

    private void SyncStepSelection()
    {
        for (int i = 0; i < _steps.Count; i++) _steps[i].IsSelected = i == _editIndex;
    }

    private void OnStepCardClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not StepVM vm) return;
            int i = _steps.IndexOf(vm);
            if (i < 0) return;
            BeginEditStep(i);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ShortcutEditor.StepClick", ex); }
    }

    private void OnStepUp(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not StepVM vm) return;
            int i = _steps.IndexOf(vm);
            if (i > 0)
            {
                _steps.Move(i, i - 1);
                if (_editIndex == i) _editIndex = i - 1; else if (_editIndex == i - 1) _editIndex = i;
                SyncStepSelection();
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ShortcutEditor.StepUp", ex); }
    }

    private void OnStepDown(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not StepVM vm) return;
            int i = _steps.IndexOf(vm);
            if (i >= 0 && i < _steps.Count - 1)
            {
                _steps.Move(i, i + 1);
                if (_editIndex == i) _editIndex = i + 1; else if (_editIndex == i + 1) _editIndex = i;
                SyncStepSelection();
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ShortcutEditor.StepDown", ex); }
    }

    private void OnStepDelete(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not StepVM vm) return;
            int i = _steps.IndexOf(vm);
            if (i < 0) return;
            _steps.RemoveAt(i);
            if (_editIndex == i) CloseStepEditor();
            else if (_editIndex > i) _editIndex--;
            SyncStepSelection();
            SyncStepsEmpty();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ShortcutEditor.StepDelete", ex); }
    }

    // ---------------------------------------------------------------- step editor panel

    private void OnAddActionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (StepEditorPanel.Visibility == Visibility.Visible && _editIndex < 0)
            {
                StepEditorPanel.Visibility = Visibility.Collapsed;   // toggle closed
                return;
            }
            BeginAddStep();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ShortcutEditor.AddAction", ex); }
    }

    private void BeginAddStep()
    {
        _editIndex = -1;
        SyncStepSelection();
        SetStepType("app");
        StepArgBox.Text = "";
        AddStepButton.Content = "Add step";
        CancelEditButton.Visibility = Visibility.Collapsed;
        StepEditorPanel.Visibility = Visibility.Visible;
        SyncStepArgHint();
        Dispatcher.BeginInvoke(() => { try { StepArgBox.Focus(); } catch { } }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void BeginEditStep(int index)
    {
        _editIndex = index;
        SyncStepSelection();
        var s = _steps[index].Step;
        SetStepType(s.Type);
        StepArgBox.Text = s.Arg;
        AddStepButton.Content = "Update step";
        CancelEditButton.Visibility = Visibility.Visible;
        StepEditorPanel.Visibility = Visibility.Visible;
        SyncStepArgHint();
        Dispatcher.BeginInvoke(() => { try { StepArgBox.Focus(); StepArgBox.CaretIndex = StepArgBox.Text.Length; } catch { } },
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CloseStepEditor()
    {
        _editIndex = -1;
        SyncStepSelection();
        StepEditorPanel.Visibility = Visibility.Collapsed;
    }

    private void OnCancelEditClick(object sender, RoutedEventArgs e)
    {
        try { CloseStepEditor(); } catch (Exception ex) { DiagnosticLogger.LogException("ShortcutEditor.CancelEdit", ex); }
    }

    private string SelectedStepType =>
        StepUrl.IsChecked == true ? "url" :
        StepFile.IsChecked == true ? "file" :
        StepFolder.IsChecked == true ? "folder" :
        StepWait.IsChecked == true ? "wait" :
        StepClip.IsChecked == true ? "clip" : "app";

    private void SetStepType(string type)
    {
        switch (type)
        {
            case "url": StepUrl.IsChecked = true; break;
            case "file": StepFile.IsChecked = true; break;
            case "folder": StepFolder.IsChecked = true; break;
            case "wait": StepWait.IsChecked = true; break;
            case "clip": StepClip.IsChecked = true; break;
            case "auto": StepUrl.IsChecked = true; break;   // recorded file/folder steps → editable as auto-ish URL box
            default: StepApp.IsChecked = true; break;
        }
        SyncStepArgHint();
    }

    private void OnStepTypeChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        try { SyncStepArgHint(); } catch { }
    }

    private void SyncStepArgHint()
    {
        string t = SelectedStepType;
        StepBrowseButton.Visibility = t is "app" or "file" or "folder" ? Visibility.Visible : Visibility.Collapsed;
        StepArgHint.Text = t switch
        {
            "app" => "Path to the program or its Start-Menu shortcut (.lnk). %ENV% supported.",
            "file" => "e.g.  C:\\Users\\me\\Documents\\report.docx",
            "folder" => "e.g.  D:\\Projects  (%USERPROFILE%\\Desktop works too)",
            "url" => "e.g.  github.com  or  https://mail.google.com",
            "wait" => "Milliseconds to pause before the next action (100–60000).",
            "clip" => "Text that will be copied to the clipboard.",
            _ => "",
        };
    }

    private void OnStepBrowseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string t = SelectedStepType;
            if (t == "folder")
            {
                var dlg = new OpenFolderDialog { Title = "Pick a folder" };
                if (dlg.ShowDialog(this) == true) StepArgBox.Text = dlg.FolderName;
            }
            else
            {
                var dlg = new OpenFileDialog { Title = "Pick a file", CheckFileExists = true };
                if (dlg.ShowDialog(this) == true) StepArgBox.Text = dlg.FileName;
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ShortcutEditor.StepBrowse", ex); }
    }

    private void OnAddStepConfirmClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string type = SelectedStepType;
            string arg = StepArgBox.Text.Trim();
            if (type == "wait" && (!int.TryParse(arg, out int ms) || ms < 100 || ms > 60_000))
            {
                ShowError("Wait needs a number of milliseconds between 100 and 60000.");
                return;
            }
            if (type != "wait" && arg.Length == 0)
            {
                ShowError("Give this action a value first.");
                return;
            }

            var step = new MacroStep(type, arg);
            if (_editIndex >= 0)
            {
                _steps[_editIndex] = new StepVM(step);
                CloseStepEditor();
            }
            else
            {
                _steps.Add(new StepVM(step));
                StepArgBox.Text = "";             // stay open for quick serial entry
                StepArgBox.Focus();
            }
            SyncStepsEmpty();
            HideMessages();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ShortcutEditor.AddStep", ex); }
    }

    private void OnTestRunClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var steps = CurrentSteps();
            string? err = SearchEngine.TestRunSteps(steps);
            if (err is not null) { ShowError(err); return; }
            RunInfoText.Text = $"▶ Running {steps.Count} step{(steps.Count == 1 ? "" : "s")}…";
            RunInfoText.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ShortcutEditor.TestRun", ex); }
    }

    // ---------------------------------------------------------------- hotkey capture (v2.5 — Task 4.3)

    private void OnHotkeyCaptureClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            HotkeyCapture.Focus();
            HotkeyDisplay.Text = "Press a combination…  (Esc clears)";
            HotkeyHint.Text = "Use Ctrl/Alt/Win + a letter, number or F-key.";
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
                return;
            }

            string? main = DescribeMainKey(key);
            if (main is null)
            {
                HotkeyHint.Text = "That key isn't supported — use letters, numbers, F1–F24, Space or `";
                return;
            }

            var mods2 = Keyboard.Modifiers;
            if ((mods2 & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
            {
                HotkeyHint.Text = "Add Ctrl, Alt or Win — a bare key would hijack normal typing.";
                return;
            }

            _pendingHotkey = ModsToString(mods2) + main;
            HotkeyDisplay.Text = _pendingHotkey;
            HotkeyHint.Text = "Saved with the shortcut — it registers the moment you save.";
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ShortcutEditor.HotkeyCapture", ex); }
    }

    private static string ModsToString(ModifierKeys mods)
    {
        string s = "";
        if (mods.HasFlag(ModifierKeys.Control)) s += "Ctrl+";
        if (mods.HasFlag(ModifierKeys.Alt)) s += "Alt+";
        if (mods.HasFlag(ModifierKeys.Shift)) s += "Shift+";
        if (mods.HasFlag(ModifierKeys.Windows)) s += "Win+";
        return s;
    }

    /// <summary>Same key set HotkeyService.TryParseCombo accepts (letters, digits, F1–F24, Space, `) —
    /// the capture must never produce a combo the registrar would reject.</summary>
    private static string? DescribeMainKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Space => "Space",
        Key.OemTilde => "`",
        _ => null,
    };

    // ---------------------------------------------------------------- save / close

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
                var steps = CurrentSteps();
                if (steps.Count == 0)
                {
                    ShowError("Add at least one action — or record steps from the launcher.");
                    return;
                }
                string? invalid = MacroProgram.Validate(steps);
                if (invalid is not null) { ShowError(invalid); return; }

                _def.Steps = steps.Select(s => s.Encode()).ToList();
                _def.Target = steps[0].Arg;
            }
            else if (type == "snippet")   // v1.6 — multi-line text, copy on Enter
            {
                string text = TargetBox.Text.TrimEnd();
                if (text.Trim().Length == 0)
                {
                    ShowError("Give the snippet some text to copy.");
                    return;
                }
                _def.Target = text;
                _def.Steps = new List<string>();
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
            _def.Hotkey = _pendingHotkey;   // v2.5 — Task 4.3

            _store.AddOrUpdate(_def);
            DiagnosticLogger.Log("Shortcuts", $"Saved '{_def.Name}' ({_def.Type}, {_def.Steps.Count} steps, hotkey '{_def.Hotkey}')");

            // v2.5 — surface the live/not-live hotkey state right in the editor
            string feedback = _savedFeedback?.Invoke(_def) ?? "";
            if (feedback.Length > 0)
            {
                RunInfoText.Text = feedback;
                RunInfoText.Visibility = Visibility.Visible;
                DialogResult = true;
                return;   // keep the window open so the status is actually readable
            }

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

    private void HideMessages()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        RunInfoText.Visibility = Visibility.Collapsed;
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
