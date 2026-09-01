using Lumo.Core;
using Lumo.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Lumo.UI;

/// <summary>
/// v3.0 — the App Deck surface: a 3×3 grid that mirrors the numpad, cards you can
/// click to launch, drop files onto, or edit in place. All state lives in
/// DeckStore (appdeck.json); this view is a projection of it.
///
/// v3.0.0-alpha.4 — two structural fixes:
///   · THEME TOKENS: the view is constructed BEFORE it joins the window tree, so
///     every theme brush must survive ORPHAN lookup. The old FindResource(key)
///     threw ResourceReferenceKeyNotFoundException (the tokens are painted into
///     the hub window's Resources, not Application's) — that exception made the
///     whole deck tab silently dead. All token access now goes through
///     Token(): TryFindResource first (live window values), then the
///     ThemeService-resolved palette, then a hard fallback. Never throws.
///   · NUMPAD MIRROR: cards fill in physical numpad order (7 8 9 / 4 5 6 /
///     1 2 3) so card position = key position — the muscle memory the header
///     always promised.
/// Also new: the tutorial page (overlay, auto-shown once via
/// Settings.DeckTutorialSeen, reopenable from "How it works").
/// </summary>
public partial class AppDeckView : UserControl
{
    private readonly Settings _settings;
    private int _editingIndex = -1;

    /// <summary>Fallback palette resolved from the active theme — used only when a
    /// token isn't reachable (i.e. while the view is still orphaned).</summary>
    private readonly ThemeService.Resolved _palette;

    /// <summary>Raised after the tutorial toggles global numpad hotkeys (already
    /// persisted) — the host re-registers the hotkeys.</summary>
    public event Action? GlobalHotkeysChanged;

    /// <summary>The tutorial's "Open settings…" row — the host routes it to Settings.</summary>
    public event Action? SettingsRequested;

    /// <summary>Card fill order: top row 7 8 9, middle 4 5 6, bottom 1 2 3 — the
    /// UniformGrid fills row-major, so the visual mirror of the keyboard.</summary>
    private static readonly int[] NumpadOrder = { 6, 7, 8, 3, 4, 5, 0, 1, 2 };

    public AppDeckView(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        _palette = ResolvePalette(settings);
        AllowDrop = true;
        Drop += OnDeckDrop;
        DragOver += OnDeckDragOver;
        BuildDeck();
        Loaded += (_, _) => MaybeAutoTutorial();
    }

    /// <summary>Rebuilds every card from the store (call after external changes).</summary>
    public void Refresh() => BuildDeck();

    // ------------------------------------------------------------ theme tokens

    private static ThemeService.Resolved ResolvePalette(Settings settings)
    {
        try { return ThemeService.ResolveColors(ThemeService.ResolveSpec(settings)); }
        catch { return ThemeService.ResolveColors(ThemeSelect.Resolve("", null, "dark", "", true)); }
    }

    /// <summary>
    /// The safe token lookup. Live window brushes win (they follow theme switches
    /// for everything that happens while attached); the resolved palette covers
    /// the orphan window; the color fallback is the last resort. This replaced the
    /// throwing FindResource that killed the deck tab in alpha.3.
    /// </summary>
    private Brush Token(string key, Color fallback)
    {
        try { if (TryFindResource(key) is Brush b) return b; }
        catch { /* resource tree not ready — fall through */ }
        var solid = new SolidColorBrush(fallback);
        solid.Freeze();
        return solid;
    }

    // ------------------------------------------------------------ the grid

    private void BuildDeck()
    {
        DeckGrid.Children.Clear();
        var slots = DeckStore.Current.Slots();
        foreach (int i in NumpadOrder)
            if (i < slots.Count)
                DeckGrid.Children.Add(BuildCard(slots[i]));
    }

    private UIElement BuildCard(DeckSlots.Slot slot)
    {
        bool assigned = slot.IsAssigned;
        var accent = Token("AccentBrush", _palette.Accent);

        var card = new Border
        {
            Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = new CornerRadius(14),
            Background = assigned ? Token("FieldBrush", _palette.Field) : Token("ChipBrush", _palette.Chip),
            BorderBrush = Token("BorderLineBrush", _palette.Border),
            BorderThickness = new Thickness(1),
            MinHeight = 108,
            Cursor = Cursors.Hand,
            Tag = slot.Index,
            Padding = new Thickness(13, 11, 13, 11),
        };
        PressFeedback.SetIsEnabled(card, true);
        if (assigned) card.AllowDrop = true;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- icon tile / plus mark (row 0)
        object iconContent;
        if (assigned)
        {
            var icon = AppIcons.ForPath(slot.Target);
            iconContent = icon is not null
                ? (object)new Image { Source = icon, Width = 38, Height = 38, HorizontalAlignment = HorizontalAlignment.Left }
                : new TextBlock
                {
                    Text = FirstLetter(slot.DisplayName),
                    FontSize = 21,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = accent,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
        }
        else
        {
            iconContent = new TextBlock
            {
                Text = "\uE710",   // MDL2 plus
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 19,
                Foreground = Token("SubtitleBrush", _palette.Subtitle),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.75,
            };
        }

        var iconTile = new Border
        {
            Background = assigned ? Token("GlyphBoxBrush", _palette.GlyphBox) : Brushes.Transparent,
            CornerRadius = new CornerRadius(10),
            Width = 52, Height = 52,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = (UIElement)iconContent,
        };
        Grid.SetRow(iconTile, 0);
        layout.Children.Add(iconTile);

        // --- name row (row 1)
        var name = new TextBlock
        {
            Text = assigned ? slot.DisplayName : "Assign",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = assigned ? Token("TitleBrush", _palette.Title) : Token("SubtitleBrush", _palette.Subtitle),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(1, 10, 0, 0),
        };
        Grid.SetRow(name, 1);
        layout.Children.Add(name);

        // --- subtitle row (row 2): keycap hint / target path
        var sub = new TextBlock
        {
            Text = assigned
                ? (slot.Target.Length > 46 ? "…" + slot.Target[^46..] : slot.Target)
                : "click or drop a file here",
            FontSize = 10.5,
            Foreground = Token("SubtitleBrush", _palette.Subtitle),
            Opacity = 0.85,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(1, 3, 0, 0),
        };
        Grid.SetRow(sub, 2);
        layout.Children.Add(sub);

        // --- numpad badge (top-right)
        var badge = new Border
        {
            Background = Token("ChipBrush", _palette.Chip),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 3),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock
            {
                Text = (slot.Index + 1).ToString(),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Token("SubtitleBrush", _palette.Subtitle),
            },
        };
        layout.Children.Add(badge);

        // --- hover mini-actions (edit + clear), top-right under the badge
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = 0,
        };
        if (assigned)
        {
            actions.Children.Add(MiniButton("\uE70F", "Edit", () => OpenEditor(slot.Index)));      // pencil
            actions.Children.Add(MiniButton("\uE738", "Clear slot", () => ClearSlot(slot.Index))); // delete
        }
        var actionsHost = new Border { Child = actions };
        Grid.SetRow(actionsHost, 0);
        layout.Children.Add(actionsHost);

        card.Child = layout;

        // hover: lift the stroke + reveal actions
        card.MouseEnter += (_, _) =>
        {
            card.BorderBrush = Token("SelStrokeBrush", _palette.SelStroke);
            if (assigned) AnimateOpacity(actions, 1.0, 110);
        };
        card.MouseLeave += (_, _) =>
        {
            card.BorderBrush = Token("BorderLineBrush", _palette.Border);
            if (assigned) AnimateOpacity(actions, 0.0, 160);
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (assigned) LaunchSlot(slot.Index);
            else OpenEditor(slot.Index);
        };
        card.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenEditor(slot.Index);
        };

        return card;
    }

    private Button MiniButton(string glyph, string tooltip, Action onClick)
    {
        var b = new Button
        {
            Content = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 11,
            Style = (Style)FindResource("DeckGhostButton"),
            Padding = new Thickness(7, 3, 7, 4),
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = tooltip,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    // ------------------------------------------------------------ actions

    /// <summary>Launches a slot; surfaces the outcome in the status line. Returns true
    /// when a launch attempt was made (the hub uses this to decide fallbacks).</summary>
    public bool LaunchSlot(int index)
    {
        string? error = DeckStore.Current.Launch(index, msg => SetStatus(msg, good: true));
        if (error is not null)
        {
            SetStatus(error, good: false);
            return false;
        }
        return true;
    }

    private void ClearSlot(int index)
    {
        DeckStore.Current.Clear(index);
        SetStatus($"Slot {index + 1} cleared", good: true);
        BuildDeck();
    }

    // ------------------------------------------------------------ drag & drop

    private void OnDeckDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.StringFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDeckDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            var pt = e.GetPosition(DeckGrid);

            // find the card under the drop point
            int target = -1;
            foreach (Border card in DeckGrid.Children)
            {
                if (IsPointOver(card, pt)) { target = (int)card.Tag; break; }
            }
            if (target < 0) return;

            var slot = DeckSlots.Normalize(target, System.IO.Path.GetFileNameWithoutExtension(files[0]), files[0], "", "");
            if (slot is null) return;
            DeckStore.Current.Assign(slot);
            BuildDeck();
            SetStatus($"Slot {target + 1} ← {files[0]}", good: true);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Deck.Drop", ex);
        }
    }

    private static bool IsPointOver(FrameworkElement element, Point deckPoint)
    {
        try
        {
            if (element.Parent is not UIElement parent) return false;
            var transform = element.TransformToVisual(parent);
            var rel = transform.Inverse!.Transform(deckPoint);
            return rel.X >= 0 && rel.Y >= 0 && rel.X <= element.ActualWidth && rel.Y <= element.ActualHeight;
        }
        catch { return false; }
    }

    // ------------------------------------------------------------ editor

    public void OpenEditor(int index)
    {
        var slot = DeckStore.Current.Slot(index);
        _editingIndex = index;
        EditorTitle.Text = slot.DisplayName;
        EditorKeycap.Text = $"Opens with numpad {index + 1} while Lumo is focused";
        EditorName.Text = slot.IsAssigned ? slot.Name : "";
        EditorTarget.Text = slot.Target;
        EditorArgs.Text = slot.Args;
        EditorWorkDir.Text = slot.WorkDir;
        PaintEditorIcon(slot.Target);

        EditorOverlay.Visibility = Visibility.Visible;
        EditorOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        EditorName.Focus();
    }

    private void PaintEditorIcon(string target)
    {
        EditorIconHost.Child = null;
        var icon = AppIcons.ForPath(target);
        if (icon is not null)
        {
            EditorIconHost.Child = new Image { Source = icon, Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        else
        {
            EditorIconHost.Child = new TextBlock
            {
                Text = "\uE713",   // gear placeholder
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = Token("SubtitleBrush", _palette.Subtitle),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
    }

    private void CloseEditor()
    {
        _editingIndex = -1;
        EditorOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>Esc hook from the host window: true when an overlay was open and is
    /// now closed (the tutorial closes first, then the slot editor).</summary>
    public bool TryCloseEditor()
    {
        if (TutorialOverlay.Visibility == Visibility.Visible)
        {
            CloseTutorial();
            return true;
        }
        if (EditorOverlay.Visibility != Visibility.Visible) return false;
        CloseEditor();
        return true;
    }

    private void OnEditorSave(object sender, RoutedEventArgs e)
    {
        var slot = DeckSlots.Normalize(_editingIndex, EditorName.Text, EditorTarget.Text, EditorArgs.Text, EditorWorkDir.Text);
        if (slot is null)
        {
            SetStatus("Nothing to save — pick a target first.", good: false);
            return;
        }
        DeckStore.Current.Assign(slot);
        CloseEditor();
        BuildDeck();
        SetStatus($"Slot {slot.Index + 1} saved — {slot.DisplayName}", good: true);
    }

    private void OnEditorClear(object sender, RoutedEventArgs e)
    {
        if (_editingIndex >= 0) ClearSlot(_editingIndex);
        CloseEditor();
    }

    private void OnEditorCancel(object sender, RoutedEventArgs e) => CloseEditor();

    private void OnEditorBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick an app, shortcut, file or script",
            Filter = "Programs and shortcuts (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() == true)
        {
            EditorTarget.Text = dlg.FileName;
            if (EditorName.Text.Length == 0)
                EditorName.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            PaintEditorIcon(dlg.FileName);
        }
    }

    // ------------------------------------------------------------ the tutorial

    /// <summary>First deck visit: show the tour once, then never again.</summary>
    private void MaybeAutoTutorial()
    {
        try
        {
            if (_settings.DeckTutorialSeen) return;
            _settings.DeckTutorialSeen = true;
            _settings.Save();
            ShowTutorial();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.TutorialAuto", ex); }
    }

    public void ShowTutorial()
    {
        try
        {
            TutorialGlobalToggle.IsChecked = _settings.DeckGlobalHotkeys;   // Click carries changes — setting it here is silent
            TutorialOverlay.Visibility = Visibility.Visible;
            if (_settings.AnimationsEnabled)
                TutorialOverlay.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.TutorialShow", ex); }
    }

    private void CloseTutorial()
    {
        try
        {
            TutorialOverlay.Visibility = Visibility.Collapsed;
            TutorialOverlay.BeginAnimation(OpacityProperty, null);
        }
        catch { /* cosmetic */ }
    }

    private void OnTutorialClose(object sender, RoutedEventArgs e) => CloseTutorial();

    private void OnTutorialGlobalToggle(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.DeckGlobalHotkeys = TutorialGlobalToggle.IsChecked == true;
            _settings.Save();
            GlobalHotkeysChanged?.Invoke();
            SetStatus(_settings.DeckGlobalHotkeys
                ? "Global numpad hotkeys are ON — slots 1–9 launch from anywhere."
                : "Global numpad hotkeys are off — slots launch while Lumo is open.",
                good: true);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.TutorialToggle", ex); }
    }

    private void OnTutorialSettings(object sender, RoutedEventArgs e)
    {
        try { SettingsRequested?.Invoke(); }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.TutorialSettings", ex); }
    }

    private void OnHowItWorksClick(object sender, RoutedEventArgs e) => ShowTutorial();

    // ------------------------------------------------------------ helpers

    private void SetStatus(string text, bool good)
    {
        DeckStatus.Text = text;
        DeckStatus.Foreground = good
            ? Token("SubtitleBrush", _palette.Subtitle)
            : Token("WarnBrush", Color.FromArgb(0x2A, 0xCA, 0x50, 0x10));
    }

    private static string FirstLetter(string name)
    {
        foreach (var c in name.Trim())
            if (!char.IsWhiteSpace(c)) return char.ToUpperInvariant(c).ToString();
        return "?";
    }

    private static void AnimateOpacity(DependencyObject target, double to, double ms)
    {
        if (target is UIElement el)
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)));
    }
}
