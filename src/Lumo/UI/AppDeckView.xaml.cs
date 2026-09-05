using Lumo.Core;
using Lumo.Services;
using System.Diagnostics;
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
///   · THEME TOKENS: every token access goes through Token() (TryFindResource →
///     the resolved palette → a hard fallback). Never a throwing FindResource.
///   · NUMPAD MIRROR: cards fill in physical numpad order (7 8 9 / 4 5 6 /
///     1 2 3) so card position = key position.
/// Also: the tutorial page (Settings.DeckTutorialSeen).
///
/// v3.0.0-alpha.6 — the big usability round (user request):
///   · PAGES: a chip row above the grid (Main / Games / Studio / …), one-click
///     templates under "Add page", right-click a chip to rename/delete. Numpad
///     1–9 always fire the visible page; Ctrl+Tab cycles (AppDeckWindow).
///   · APP PICKER: clicking an empty card opens AppPickerWindow — every Start
///     Menu + Desktop app, live search, Enter to assign. "Browse…" survives
///     inside the editor for anything that isn't a Start Menu app.
///   · IMPORT/EXPORT: .lumodeck files via the header buttons (Core/DeckPages.
///     DeckLayout does the pure JSON; import merges, never overwrites).
///   · MORE: drag a card onto another to swap, duplicate slot, sort A→Z, clear
///     page, per-slot launch counters, run-as-admin + launch window state, a
///     right-click card menu (open file location / copy path…), multi-file
///     drop fills empty slots, and usage-based suggestions in the editor.
/// </summary>
public partial class AppDeckView : UserControl
{
    private readonly Settings _settings;
    private readonly UsageStore? _usage;
    private int _editingIndex = -1;
    private string _renamingPageId = "";   // the page whose chip opened the rename overlay

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

    /// <summary>The data format that identifies an in-flight card swap.</summary>
    private const string SlotDragFormat = "LumoDeckSlot";

    // v3.0.0-alpha.7 — press→drag→click decisions (pure, tested in DeckDragLatchTests).
    private readonly DeckDragLatch Drag = new();

    // drag-to-swap state (armed on press, fired past the system drag threshold)
    private bool _dragArmed;
    private int _dragIndex = -1;
    private Point _dragStart;

    public AppDeckView(Settings settings, UsageStore? usage = null)
    {
        InitializeComponent();
        _settings = settings;
        _usage = usage;
        _palette = ResolvePalette(settings);
        AllowDrop = true;
        Drop += OnDeckDrop;
        DragOver += OnDeckDragOver;
        BuildAll();
        Loaded += (_, _) => MaybeAutoTutorial();
    }

    /// <summary>Rebuilds chips + cards from the store (call after external changes).</summary>
    public void Refresh() => BuildAll();

    /// <summary>True while any overlay (editor / tutorial / rename) covers the deck —
    /// the host uses it to decide whether digit keys mean "launch" or "type".</summary>
    public bool IsOverlayOpen =>
        EditorOverlay.Visibility == Visibility.Visible ||
        TutorialOverlay.Visibility == Visibility.Visible ||
        RenameOverlay.Visibility == Visibility.Visible;

    private void BuildAll()
    {
        BuildChips();
        BuildDeck();
    }

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

    // ------------------------------------------------------------ page chips

    private void BuildChips()
    {
        PageChips.Children.Clear();
        var pages = DeckStore.Current.Pages();
        var activeId = DeckStore.Current.ActivePageId;
        var accent = Token("AccentBrush", _palette.Accent);

        foreach (var page in pages)
        {
            bool active = string.Equals(page.Id, activeId, StringComparison.Ordinal);
            int assigned = page.Slots.Count(s => s.IsAssigned);

            var chip = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(13, 6, 13, 7),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                Background = active ? Token("FieldBrush", _palette.Field) : Token("ChipBrush", _palette.Chip),
                BorderBrush = active ? accent : Token("BorderLineBrush", _palette.Border),
                BorderThickness = new Thickness(1),
                Tag = page.Id,
            };
            PressFeedback.SetIsEnabled(chip, true);

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (page.Icon.Length > 0)
            {
                row.Children.Add(new TextBlock
                {
                    Text = page.Icon,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 12,
                    Foreground = active ? accent : Token("SubtitleBrush", _palette.Subtitle),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            row.Children.Add(new TextBlock
            {
                Text = page.Name,
                FontSize = 12,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Medium,
                Margin = new Thickness(page.Icon.Length > 0 ? 7 : 0, 0, 0, 0),
                Foreground = active ? Token("TitleBrush", _palette.Title) : Token("SubtitleBrush", _palette.Subtitle),
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = assigned > 0 ? $"  {assigned}" : "",
                FontSize = 10.5,
                Foreground = Token("SubtitleBrush", _palette.Subtitle),
                Opacity = 0.8,
                VerticalAlignment = VerticalAlignment.Center,
            });
            chip.Child = row;

            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                SwitchToPage(page.Id, announce: true);
            };
            chip.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                OpenChipMenu(page, chip);
            };
            chip.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount != 2) return;
                e.Handled = true;
                OpenRename(page.Id, page.Name);
            };

            PageChips.Children.Add(chip);
        }
    }

    private ContextMenu MakeMenu()
    {
        var menu = new ContextMenu { Style = (Style)FindResource("DeckMenu") };
        return menu;
    }

    private MenuItem MakeMenuItem(string header, bool enabled, Action onClick)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = (Style)FindResource("DeckMenuItem"),
            IsEnabled = enabled,
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void OpenChipMenu(DeckPages.DeckPage page, FrameworkElement placement)
    {
        try
        {
            var pages = DeckStore.Current.Pages();
            var menu = MakeMenu();
            menu.Items.Add(MakeMenuItem("Rename page…", true, () => OpenRename(page.Id, page.Name)));
            menu.Items.Add(MakeMenuItem("Delete page", pages.Count > 1, () => DeletePage(page.Id)));
            ContextMenuService.SetPlacement(placement, System.Windows.Controls.Primitives.PlacementMode.Bottom);
            menu.PlacementTarget = placement;
            menu.IsOpen = true;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.ChipMenu", ex); }
    }

    private void SwitchToPage(string id, bool announce)
    {
        if (!DeckStore.Current.SwitchPage(id)) return;
        BuildAll();
        if (announce)
        {
            var page = DeckStore.Current.ActivePage();
            SetStatus($"Switched to {page.Name} — numpad 1–9 now launch this page.", good: true);
        }
    }

    /// <summary>Ctrl+Tab handler (the host window routes it here): dir +1 / −1 wraps.</summary>
    public void CyclePage(int dir)
    {
        try
        {
            var pages = DeckStore.Current.Pages();
            if (pages.Count < 2) return;
            string activeId = DeckStore.Current.ActivePageId;
            int current = 0;
            for (int i = 0; i < pages.Count; i++)
                if (string.Equals(pages[i].Id, activeId, StringComparison.Ordinal)) { current = i; break; }
            int next = (current + dir % pages.Count + pages.Count) % pages.Count;
            SwitchToPage(pages[next].Id, announce: true);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.CyclePage", ex); }
    }

    /// <summary>Ctrl+1…9 — jump straight to the Nth page (1-based). False when no such page.</summary>
    public bool JumpToPage(int oneBased)
    {
        try
        {
            var pages = DeckStore.Current.Pages();
            if (oneBased < 1 || oneBased > pages.Count) return false;
            SwitchToPage(pages[oneBased - 1].Id, announce: true);
            return true;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.JumpPage", ex); return false; }
    }

    private void OnAddPageClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var menu = MakeMenu();
            foreach (var template in DeckPages.Templates)
            {
                var t = template;
                var item = MakeMenuItem($"{t.Name} page", true, () => AddPage(t.Name, t.Icon));
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator { Style = (Style)FindResource("DeckMenuSeparator") });
            menu.Items.Add(MakeMenuItem("Blank page", true, () => AddPage("New page", "")));

            var btn = (Button)sender;
            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.AddPage", ex); }
    }

    private void AddPage(string name, string icon)
    {
        var page = DeckStore.Current.AddPage(name, icon);
        if (page is null)
        {
            SetStatus($"The deck holds {DeckPages.MaxPages} pages at most — delete one first.", good: false);
            return;
        }
        BuildAll();
        SetStatus($"Added the {page.Name} page — it's active now. Click an empty card to fill it.", good: true);
    }

    private void DeletePage(string id)
    {
        var pages = DeckStore.Current.Pages();
        var target = pages.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        if (target is null) return;
        int assigned = target.Slots.Count(s => s.IsAssigned);
        string detail = assigned > 0
            ? $"\"{target.Name}\" holds {assigned} assigned slot(s). They will be removed with the page."
            : $"\"{target.Name}\" is empty.";
        var owner = Window.GetWindow(this);
        var choice = MessageBox.Show(owner ?? (Window)Application.Current.MainWindow!,
            detail + "\n\nDelete this page?", "Delete page",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes) return;

        if (!DeckStore.Current.DeletePage(id))
        {
            SetStatus("The deck needs at least one page.", good: false);
            return;
        }
        BuildAll();
        SetStatus("Page deleted.", good: true);
    }

    // ------------------------------------------------------------ the rename overlay

    private void OpenRename(string pageId, string currentName)
    {
        _renamingPageId = pageId;
        RenameBox.Text = currentName;
        RenameOverlay.Visibility = Visibility.Visible;
        RenameOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        RenameBox.SelectAll();
        RenameBox.Focus();
    }

    private void OnRenameSave(object sender, RoutedEventArgs e) => CommitRename();

    private void OnRenameCancel(object sender, RoutedEventArgs e) => CloseRename();

    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; CommitRename(); }
        else if (e.Key == Key.Escape) { e.Handled = true; CloseRename(); }
    }

    private void CommitRename()
    {
        try
        {
            if (_renamingPageId.Length == 0) { CloseRename(); return; }
            string desired = RenameBox.Text;
            DeckStore.Current.RenamePage(_renamingPageId, desired);
            CloseRename();
            BuildAll();
            SetStatus($"Page renamed to {DeckPages.NormalizeName(desired)}.", good: true);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.Rename", ex); }
    }

    private void CloseRename()
    {
        _renamingPageId = "";
        RenameOverlay.Visibility = Visibility.Collapsed;
    }

    // ------------------------------------------------------------ header tools

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var menu = MakeMenu();
            menu.Items.Add(MakeMenuItem("Sort page A→Z", true, SortPageAZ));
            menu.Items.Add(MakeMenuItem("Clear page…", true, ClearPageConfirmed));
            menu.Items.Add(new Separator { Style = (Style)FindResource("DeckMenuSeparator") });
            menu.Items.Add(MakeMenuItem("Import layout…", true, OnImportRequested));
            menu.Items.Add(MakeMenuItem("Export layout…", true, OnExportRequested));

            var btn = (Button)sender;
            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.MoreMenu", ex); }
    }

    private void SortPageAZ()
    {
        try
        {
            int before = DeckStore.Current.AssignedCount;
            if (before == 0)
            {
                SetStatus("Nothing to sort — this page is empty.", good: false);
                return;
            }
            DeckStore.Current.SortPage();
            BuildAll();
            SetStatus($"Sorted {before} slot(s) A→Z — numpad keys follow the new order.", good: true);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.Sort", ex); }
    }

    private void ClearPageConfirmed()
    {
        try
        {
            int assigned = DeckStore.Current.AssignedCount;
            if (assigned == 0)
            {
                SetStatus("This page is already empty.", good: false);
                return;
            }
            var page = DeckStore.Current.ActivePage();
            var owner = Window.GetWindow(this);
            var choice = MessageBox.Show(owner ?? (Window)Application.Current.MainWindow!,
                $"Clear all {assigned} slot(s) on the \"{page.Name}\" page?", "Clear page",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (choice != MessageBoxResult.Yes) return;

            DeckStore.Current.ClearPage();
            BuildAll();
            SetStatus($"Cleared {assigned} slot(s).", good: true);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.ClearPage", ex); }
    }

    // ------------------------------------------------------------ import / export

    private void OnImportClick(object sender, RoutedEventArgs e) => OnImportRequested();
    private void OnExportClick(object sender, RoutedEventArgs e) => OnExportRequested();

    private void OnImportRequested()
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import a Lumo deck layout",
                Filter = "Lumo deck layout (*.lumodeck)|*.lumodeck|JSON (*.json)|*.json",
                CheckFileExists = true,
            };
            var owner = Window.GetWindow(this);
            if (dlg.ShowDialog(owner) != true) return;

            var info = new FileInfo(dlg.FileName);
            if (info.Length > 1_000_000)
            {
                SetStatus("That file is too large to be a deck layout.", good: false);
                return;
            }
            var imported = DeckLayout.Read(File.ReadAllText(dlg.FileName));
            if (imported.Count == 0)
            {
                SetStatus("Nothing to import — the file didn't contain any pages.", good: false);
                return;
            }
            int added = DeckStore.Current.ImportPages(imported);
            BuildAll();
            SetStatus(added < imported.Count
                ? $"Imported {added} page(s) — the deck was full, so some were skipped."
                : $"Imported {added} page(s). Nothing you had was overwritten.", good: true);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Deck.Import", ex);
            SetStatus("The import failed — see the log for details.", good: false);
        }
    }

    private void OnExportRequested()
    {
        try
        {
            var pages = DeckStore.Current.Pages();
            if (pages.Count == 0)
            {
                SetStatus("Nothing to export.", good: false);
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export your deck layout",
                Filter = "Lumo deck layout (*.lumodeck)|*.lumodeck",
                FileName = $"Lumo-deck-{DateTime.Now:yyyy-MM-dd}.lumodeck",
            };
            var owner = Window.GetWindow(this);
            if (dlg.ShowDialog(owner) != true) return;

            File.WriteAllText(dlg.FileName, DeckLayout.Write(pages));
            SetStatus($"Exported {pages.Count} page(s) → {System.IO.Path.GetFileName(dlg.FileName)}", good: true);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Deck.Export", ex);
            SetStatus("The export failed — see the log for details.", good: false);
        }
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
                : "click to pick an app · or drop a file",
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

        // v3.0.0-alpha.6 — press → drag-to-swap; release without a drag → click.
        // Mini-buttons swallow their own MouseDown (ButtonBase), so this never
        // fights the edit/clear buttons.
        // v3.0.0-alpha.7 — the click decision lives in a pure latch (DeckDragLatch)
        // and Press() runs for EVERY card, empty ones included: the old code only
        // cleared its drag flag on assigned cards, so after one drag-to-swap every
        // click-to-assign on an empty card was silently swallowed (the OLE drag
        // loop had consumed the release and the flag stayed set).
        card.MouseLeftButtonDown += (_, e) =>
        {
            Drag.Press();
            if (!assigned) return;
            _dragIndex = slot.Index;
            _dragStart = e.GetPosition(DeckGrid);
            _dragArmed = true;
            try { card.CaptureMouse(); } catch { }
        };
        card.MouseMove += (_, e) =>
        {
            if (!_dragArmed || Drag.InDrag) return;
            var pos = e.GetPosition(DeckGrid);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            Drag.DragStarted();
            try
            {
                var data = new DataObject(SlotDragFormat, slot.Index);
                DragDrop.DoDragDrop(card, data, DragDropEffects.Move);
            }
            catch (Exception ex) { DiagnosticLogger.LogException("Deck.CardDrag", ex); }
            finally { ResetDrag(card); Drag.DragFinished(); }
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            bool isClick = Drag.IsClick();
            ResetDrag(card);
            if (!isClick) return;
            if (assigned) LaunchSlot(slot.Index);
            else OpenPicker(slot.Index);
        };
        card.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenCardMenu(slot, card);
        };

        return card;
    }

    private void ResetDrag(Border card)
    {
        _dragArmed = false;
        _dragIndex = -1;
        try { if (card.IsMouseCaptured) card.ReleaseMouseCapture(); } catch { }
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

    // ------------------------------------------------------------ card context menu

    private void OpenCardMenu(DeckSlots.Slot slot, FrameworkElement placement)
    {
        try
        {
            var menu = MakeMenu();
            if (slot.IsAssigned)
            {
                menu.Items.Add(MakeMenuItem($"Open {slot.DisplayName}", true, () => LaunchSlot(slot.Index)));
                menu.Items.Add(new Separator { Style = (Style)FindResource("DeckMenuSeparator") });
                menu.Items.Add(MakeMenuItem("Edit slot…", true, () => OpenEditor(slot.Index)));
                menu.Items.Add(MakeMenuItem("Pick a different app…", true, () => OpenPicker(slot.Index)));
                menu.Items.Add(MakeMenuItem("Duplicate to empty slot", DeckStore.Current.Slots().Any(s => !s.IsAssigned),
                    () => DuplicateSlot(slot.Index)));
                menu.Items.Add(new Separator { Style = (Style)FindResource("DeckMenuSeparator") });
                menu.Items.Add(MakeMenuItem("Open file location", true, () => OpenFileLocation(slot.Target)));
                menu.Items.Add(MakeMenuItem("Copy target path", true, () => CopyTargetPath(slot)));
            }
            else
            {
                menu.Items.Add(MakeMenuItem("Pick an app…", true, () => OpenPicker(slot.Index)));
                menu.Items.Add(MakeMenuItem("Edit slot…", true, () => OpenEditor(slot.Index)));
            }
            if (slot.IsAssigned)
            {
                menu.Items.Add(new Separator { Style = (Style)FindResource("DeckMenuSeparator") });
                menu.Items.Add(MakeMenuItem("Clear slot", true, () => ClearSlot(slot.Index)));
            }

            menu.PlacementTarget = placement;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.CardMenu", ex); }
    }

    private void OpenFileLocation(string target)
    {
        try
        {
            string? locate = File.Exists(target) ? target : Directory.Exists(target) ? target : null;
            if (locate is null)
            {
                SetStatus($"Can't find {target} — it may have moved.", good: false);
                return;
            }
            if (Directory.Exists(locate))
            {
                Process.Start(new ProcessStartInfo { FileName = locate, UseShellExecute = true });
                return;
            }
            // select the file inside its folder
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{locate}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Deck.OpenLocation", ex);
            SetStatus("Couldn't open the file location.", good: false);
        }
    }

    private void CopyTargetPath(DeckSlots.Slot slot)
    {
        try
        {
            Clipboard.SetText(slot.Target);
            SetStatus("Target path copied to the clipboard.", good: true);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Deck.CopyPath", ex);
            SetStatus("Couldn't copy — the clipboard may be busy.", good: false);
        }
    }

    private void DuplicateSlot(int index)
    {
        try
        {
            int free = DeckStore.Current.DuplicateSlot(index);
            if (free < 0)
            {
                SetStatus("No empty slot to duplicate into — clear one first.", good: false);
                return;
            }
            BuildAll();
            SetStatus($"Duplicated → slot {free + 1}.", good: true);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.Duplicate", ex); }
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
        e.Effects = e.Data.GetDataPresent(SlotDragFormat) ? DragDropEffects.Move
            : e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.StringFormat)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDeckDrop(object sender, DragEventArgs e)
    {
        try
        {
            var pt = e.GetPosition(DeckGrid);

            // card-to-card: swap the two slots (drag-to-reorder)
            if (e.Data.GetDataPresent(SlotDragFormat))
            {
                if (e.Data.GetData(SlotDragFormat) is not int fromIndex) return;
                int target = -1;
                foreach (Border card in DeckGrid.Children)
                    if (IsPointOver(card, pt)) { target = (int)card.Tag; break; }
                if (target < 0 || target == fromIndex) return;
                if (DeckStore.Current.SwapSlots(fromIndex, target))
                {
                    BuildDeck();
                    SetStatus($"Swapped slots {fromIndex + 1} and {target + 1}.", good: true);
                }
                return;
            }

            // file drop: the first file lands on the card under the cursor, the
            // rest fill the next empty slots in order
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

            int dropTarget = -1;
            foreach (Border card in DeckGrid.Children)
                if (IsPointOver(card, pt)) { dropTarget = (int)card.Tag; break; }

            var slots = DeckStore.Current.Slots();
            var queue = new Queue<int>();
            if (dropTarget >= 0) queue.Enqueue(dropTarget);
            for (int i = 0; i < DeckSlots.Count && files.Length > queue.Count; i++)
                if (!slots[i].IsAssigned && (dropTarget < 0 || i != dropTarget)) queue.Enqueue(i);

            int used = 0;
            foreach (var file in files)
            {
                if (queue.Count == 0) break;
                int idx = queue.Dequeue();
                var slot = DeckSlots.Normalize(idx, System.IO.Path.GetFileNameWithoutExtension(file), file, "", "");
                if (slot is null) continue;
                DeckStore.Current.Assign(slot);
                used++;
                if (idx == dropTarget)
                    SetStatus($"Slot {idx + 1} ← {file}", good: true);
            }
            if (used > 1) SetStatus($"Assigned {used} files — one per empty slot.", good: true);
            BuildDeck();
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

    // ------------------------------------------------------------ the app picker

    /// <summary>
    /// Opens the installed-apps picker for a slot. Direct mode (clicking an empty
    /// card, or "Pick a different app…") assigns immediately; editor mode
    /// (OpenEditor → "Pick app…") fills the fields so the user can add arguments
    /// or a start-in folder before saving.
    /// v3.0.0-alpha.8 — never silent: every stage logs a breadcrumb, and ANY
    /// failure shows a real error dialog (the App.OpenDeck doctrine) instead of
    /// a status line a user can miss. If the picker ever fails to open on a
    /// user machine, the dialog + the log pinpoint exactly which stage died.
    /// </summary>
    private void OpenPicker(int index, bool fillEditorOnly = false)
    {
        DiagnosticLogger.Log("Deck.Picker", $"opening for slot {index + 1} (editor mode: {fillEditorOnly})");
        try
        {
            var slot = DeckStore.Current.Slot(index);
            var owner = Window.GetWindow(this);

            AppPickerWindow dlg;
            try
            {
                dlg = new AppPickerWindow(_settings, $"Slot {index + 1}", _usage)
                {
                    Owner = owner is { IsLoaded: true } ? owner : null,
                };
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogException("Deck.Picker.Construct", ex);
                ShowPickerError("The app picker window failed to build.", ex);
                return;
            }
            DiagnosticLogger.Log("Deck.Picker", "constructed — showing dialog");

            bool confirmed;
            try { confirmed = dlg.ShowDialog() == true; }
            catch (Exception ex)
            {
                DiagnosticLogger.LogException("Deck.Picker.Show", ex);
                ShowPickerError("The app picker window failed to open.", ex);
                return;
            }
            DiagnosticLogger.Log("Deck.Picker", $"dialog closed (confirmed: {confirmed}, path: '{dlg.PickedPath}')");
            if (!confirmed || dlg.PickedPath.Length == 0) return;

            if (fillEditorOnly && EditorOverlay.Visibility == Visibility.Visible)
            {
                EditorTarget.Text = dlg.PickedPath;
                if (EditorName.Text.Trim().Length == 0)
                    EditorName.Text = dlg.PickedName;
                PaintEditorIcon(dlg.PickedPath);
                SetStatus($"Picked {dlg.PickedName} — adjust and save.", good: true);
                return;
            }

            var normalized = DeckSlots.Normalize(index, dlg.PickedName, dlg.PickedPath, "", "");
            if (normalized is null) return;
            DeckStore.Current.Assign(normalized);
            BuildDeck();
            SetStatus($"Slot {index + 1} ← {dlg.PickedName}", good: true);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Deck.Picker", ex);
            ShowPickerError("The app picker failed.", ex);
        }
    }

    /// <summary>The picker's failures can no longer hide: the exception lands in the
    /// log AND in front of the user, so a report like "the picker doesn't open"
    /// always arrives with its reason attached.</summary>
    private static void ShowPickerError(string what, Exception ex)
    {
        try
        {
            System.Windows.MessageBox.Show(
                $"{what}\n\n{ex.GetType().Name}: {ex.Message}\n\nDetails were written to:\n{AppPaths.LogFile}",
                "Lumo — App Deck", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* never crash over the crash dialog */ }
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
        EditorAdmin.IsChecked = slot.Admin;
        SetWinChips(slot.WindowMode);

        EditorLaunches.Visibility = slot.Launches > 0 ? Visibility.Visible : Visibility.Collapsed;
        EditorLaunches.Text = slot.Launches == 1
            ? "Launched once"
            : $"Launched {slot.Launches} times";
        BuildSuggestions(slot.IsAssigned);

        PaintEditorIcon(slot.Target);

        EditorOverlay.Visibility = Visibility.Visible;
        EditorOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        EditorName.Focus();
    }

    private void SetWinChips(string mode)
    {
        EditorWinNormal.IsChecked = mode != "min" && mode != "max";
        EditorWinMin.IsChecked = mode == "min";
        EditorWinMax.IsChecked = mode == "max";
    }

    private string WinChipsMode() =>
        EditorWinMin.IsChecked == true ? "min" :
        EditorWinMax.IsChecked == true ? "max" : "";

    private void OnEditorAdminChanged(object sender, RoutedEventArgs e)
    {
        // state is read at save time; nothing to do here today
    }

    /// <summary>The label toggles the switch too — a bigger click target.</summary>
    private void OnEditorAdminLabel(object sender, MouseButtonEventArgs e)
    {
        try { EditorAdmin.IsChecked = EditorAdmin.IsChecked != true; } catch { }
    }

    private void OnWinModeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // exactly one chip stays lit; unchecking everything falls back to Normal
            if (sender == EditorWinMin) { EditorWinMin.IsChecked = true; EditorWinMax.IsChecked = false; EditorWinNormal.IsChecked = false; }
            else if (sender == EditorWinMax) { EditorWinMax.IsChecked = true; EditorWinMin.IsChecked = false; EditorWinNormal.IsChecked = false; }
            else if (sender == EditorWinNormal) { EditorWinNormal.IsChecked = true; EditorWinMin.IsChecked = false; EditorWinMax.IsChecked = false; }
            if (EditorWinMin.IsChecked != true && EditorWinMax.IsChecked != true && EditorWinNormal.IsChecked != true)
                EditorWinNormal.IsChecked = true;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Deck.WinChips", ex); }
    }

    /// <summary>"You open these a lot" — the top targets from the usage store that
    /// still exist on disk, as one-click chips (only for unassigned slots).</summary>
    private void BuildSuggestions(bool slotAssigned)
    {
        try
        {
            EditorSuggestions.Children.Clear();
            if (slotAssigned || _usage is null || _usage.Count == 0)
            {
                EditorSuggestionsHost.Visibility = Visibility.Collapsed;
                return;
            }

            var tops = _usage.Top(6, path => { try { return File.Exists(path) || Directory.Exists(path); } catch { return false; } });
            foreach (var (key, entry) in tops)
            {
                var target = key;
                var chip = new Button
                {
                    Style = (Style)FindResource("DeckGhostButton"),
                    Content = System.IO.Path.GetFileName(
                        target.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)),
                    FontSize = 11,
                    Padding = new Thickness(9, 4, 9, 5),
                    Margin = new Thickness(0, 0, 6, 6),
                    ToolTip = $"{target}  ·  opened {entry.Count}×",
                };
                if (chip.Content is string s && s.Length == 0) chip.Content = target;
                chip.Click += (_, _) =>
                {
                    try
                    {
                        EditorTarget.Text = target;
                        if (EditorName.Text.Trim().Length == 0)
                            EditorName.Text = System.IO.Path.GetFileNameWithoutExtension(target);
                        PaintEditorIcon(target);
                    }
                    catch (Exception ex) { DiagnosticLogger.LogException("Deck.SuggestionClick", ex); }
                };
                EditorSuggestions.Children.Add(chip);
            }
            EditorSuggestionsHost.Visibility = EditorSuggestions.Children.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Deck.Suggestions", ex);
            EditorSuggestionsHost.Visibility = Visibility.Collapsed;
        }
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
    /// now closed (rename closes first, then the tutorial, then the slot editor).</summary>
    public bool TryCloseEditor()
    {
        if (RenameOverlay.Visibility == Visibility.Visible)
        {
            CloseRename();
            return true;
        }
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
        var slot = DeckSlots.Normalize(_editingIndex, EditorName.Text, EditorTarget.Text, EditorArgs.Text, EditorWorkDir.Text,
            EditorAdmin.IsChecked == true, WinChipsMode(), DeckStore.Current.Slot(Math.Max(0, _editingIndex)).Launches);
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

    private void OnEditorPickApp(object sender, RoutedEventArgs e)
    {
        if (_editingIndex >= 0) OpenPicker(_editingIndex, fillEditorOnly: true);
    }

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
