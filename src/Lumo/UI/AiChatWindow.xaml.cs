using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lumo.Core;
using Lumo.Native;
using Lumo.Services;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;

namespace Lumo.UI;

/// <summary>
/// v2.3.0-alpha.3 — the dedicated AI chat tab (opened via the AI/ prefix or the
/// "Open the AI chat" row). A conversation window in the prompt-kit design
/// language (prompt-kit.com), rebuilt natively in WPF: right-aligned accent
/// user bubbles, assistant replies with an avatar dot, markdown-lite rendering
/// with code blocks, a three-dot typing indicator, suggestion chips on the
/// empty state, a big rounded prompt input with a circular send button — and
/// TRUE token streaming for local Ollama (Anthropic arrives buffered and is
/// played with a typewriter reveal so both feel identical).
///
/// Constraints (DEV_PLAN agent rules): the request runs on a worker via
/// AiService.StreamChatAsync; the UI thread is touched ONLY by a 50 ms flush
/// timer (no per-token dispatcher storms, so a fast local model can't flood
/// the window); one generation at a time; every handler is try/catch +
/// DiagnosticLogger; nothing user-generated is ever logged (lengths only).
/// </summary>
public partial class AiChatWindow : Window
{
    private const int HistoryCap = 40;          // UI-side context turns (service trims to 16 when sending)

    private readonly Settings _settings;
    private readonly AiService _ai;
    private readonly List<AiProviders.AiTurn> _history = new();

    private bool _generating;
    private CancellationTokenSource? _genCts;
    private bool _sourceReady;
    private bool _atBottom = true;

    // ---- streaming plumbing -------------------------------------------------
    private readonly StringBuilder _streamBuf = new();
    private volatile bool _streamDirty;
    private DispatcherTimer? _flushTimer;
    private DispatcherTimer? _dotsTimer;
    private int _shownLen;                       // typewriter reveal cursor
    private bool _firstDeltaArrived;
    private TextBlock? _liveText;                // plain-text view while streaming
    private StackPanel? _liveHost;               // markdown host, filled on completion
    private StackPanel? _dotsPanel;              // the three-dot "thinking" row

    private static readonly string[] Chips =
    {
        "Explain quantum computing like I'm five",
        "Draft a polite follow-up email",
        "PowerShell: list files bigger than 1 GB",
        "Brainstorm 5 name ideas for a side project",
    };

    public AiChatWindow(Settings settings, AiService ai)
    {
        InitializeComponent();
        _settings = settings;
        _ai = ai;

        ApplySelfTheme();
        UpdateModelChip();
        UpdateBanner();
        UpdateEmptyState();
        BuildChips();

        Closed += (_, _) => { try { _genCts?.Cancel(); } catch { } };
        Loaded += (_, _) => PromptBox.Focus();
    }

    // ---------------------------------------------------------------- theme

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _sourceReady = true;
        try { ApplySelfTheme(); } catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Theme", ex); }
    }

    /// <summary>Same Fluent token set as SettingsWindow — the chat belongs to the launcher family.</summary>
    private void ApplySelfTheme()
    {
        try
        {
            bool dark = _settings.EffectiveDark();
            if (_sourceReady)
                GlassBackdrop.Apply(this, dark);

            var p = Appearance.PaletteFor(dark, _settings.AccentColor);
            Resources["TitleBrush"] = new SolidColorBrush(p.Title);
            Resources["SubtitleBrush"] = new SolidColorBrush(p.Subtitle);
            Resources["HoverBrush"] = new SolidColorBrush(p.Hover);
            Resources["AccentBrush"] = new SolidColorBrush(p.Accent);
            Resources["SeparatorBrush"] = new SolidColorBrush(p.Separator);
            Resources["BorderLineBrush"] = new SolidColorBrush(p.Border);
            Resources["PanelBrush"] = new SolidColorBrush(p.Panel);
            Resources["FieldBrush"] = new SolidColorBrush(p.Field);
            Resources["CardBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Colors.White);
            Resources["SidebarBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Color.FromRgb(0xEC, 0xEC, 0xEC));
            Resources["ChipBrush"] = new SolidColorBrush(dark
                ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0D, 0x00, 0x00, 0x00));
            Resources["UserBubbleBrush"] = new SolidColorBrush(Appearance.Tint(p.Accent, 0x2E));   // accent @ 18%
            Resources["AvatarBrush"] = new SolidColorBrush(Appearance.Tint(p.Accent, 0x2A));
            Resources["CodeBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Color.FromRgb(0xF9, 0xF9, 0xF9));
            Resources["WarnBrush"] = new SolidColorBrush(Color.FromArgb(0x2A, 0xCA, 0x50, 0x10));
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.ApplyTheme", ex); }
    }

    // ---------------------------------------------------------------- chrome

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        try { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
        catch { }
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        try { WindowState = WindowState.Minimized; }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Minimize", ex); }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        try { Close(); } catch { }
    }

    // ---------------------------------------------------------------- empty state / chips

    private void BuildChips()
    {
        try
        {
            ChipHost.Children.Clear();
            foreach (var chip in Chips)
            {
                var b = new Button { Style = (Style)FindResource("ChipButton"), Content = MakeChipText(chip) };
                b.Click += (_, _) => SendUserMessage(chip);
                ChipHost.Children.Add(b);
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Chips", ex); }
    }

    private static TextBlock MakeChipText(string s) => new()
    {
        Text = s,
        FontSize = 12,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private void UpdateEmptyState()
    {
        try
        {
            EmptyState.Visibility = MessagesHost.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            bool anthropic = AiProviders.IsAnthropic(_settings.AiStyle, _settings.AiEndpoint);
            EmptySub.Text = _settings.AiEnabled
                ? (anthropic
                    ? "Answers come from the Anthropic API — they leave this PC (key stays local)"
                    : $"Private & offline — answers are generated locally by {_settings.AiModel}")
                : "AI is currently off — the banner above shows how to enable it";
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.EmptyState", ex); }
    }

    private void UpdateModelChip()
    {
        try
        {
            bool anthropic = AiProviders.IsAnthropic(_settings.AiStyle, _settings.AiEndpoint);
            ModelChipText.Text = $"{_settings.AiModel} · {(anthropic ? "Anthropic" : "local")}";
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Chip", ex); }
    }

    private void UpdateBanner()
    {
        try { AiOffBanner.Visibility = _settings.AiEnabled ? Visibility.Collapsed : Visibility.Visible; }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Banner", ex); }
    }

    // ---------------------------------------------------------------- input

    private void OnPromptKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                SendFromInput();
            }
            // Shift+Enter falls through — the TextBox inserts the newline
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.PromptKey", ex); }
    }

    private void OnPromptTextChanged(object sender, TextChangedEventArgs e)
    {
        try { SendButton.IsEnabled = !string.IsNullOrWhiteSpace(PromptBox.Text); }
        catch { }
    }

    private void OnSendClick(object sender, RoutedEventArgs e) => SendFromInput();

    private void SendFromInput()
    {
        try
        {
            string t = PromptBox.Text.Trim();
            if (t.Length == 0 || _generating) return;
            PromptBox.Clear();
            SendUserMessage(t);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.SendInput", ex); }
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        try { _genCts?.Cancel(); }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Stop", ex); }
    }

    private void OnNewChat(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_generating) _genCts?.Cancel();
            _history.Clear();
            MessagesHost.Children.Clear();
            _streamBuf.Clear();
            _shownLen = 0;
            _generating = false;
            SendButton.Visibility = Visibility.Visible;
            StopButton.Visibility = Visibility.Collapsed;
            UpdateEmptyState();
            PromptBox.Focus();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.NewChat", ex); }
    }

    // ---------------------------------------------------------------- sending / streaming

    /// <summary>Entry point used by both the input box and the launcher's AI/query deep link.</summary>
    public void SendUserMessage(string text)
    {
        try
        {
            text = (text ?? "").Trim();
            if (text.Length == 0 || _generating) return;
            if (!_settings.AiEnabled) { UpdateBanner(); }   // the request will fail with the same message — surface it visibly

            AppendUserBubble(text);
            UpdateEmptyState();

            // assistant placeholder: avatar + plain-text live line + typing dots
            var (host, live) = AppendAssistantBubble();
            _liveHost = host;
            _liveText = live;
            StartTypingDots(host);

            _streamBuf.Clear();
            _streamDirty = false;
            _shownLen = 0;
            _firstDeltaArrived = false;
            _generating = true;
            SendButton.Visibility = Visibility.Collapsed;
            StopButton.Visibility = Visibility.Visible;

            _genCts = new CancellationTokenSource();
            var ct = _genCts.Token;
            string prompt = text;

            _flushTimer ??= CreateFlushTimer();
            _throttle.Restart();
            _flushTimer.Start();

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _ai.StreamChatAsync(_settings, _history, prompt,
                        delta => { lock (_streamBuf) { _streamBuf.Append(delta); _streamDirty = true; } },
                        ct).ConfigureAwait(true);
                    await Dispatcher.InvokeAsync(() => FinishTurn(result));
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogException("AiChat.Turn", ex);
                    await Dispatcher.InvokeAsync(() =>
                        FinishTurn(AiService.AiStreamResult.Fail(ex.Message)));
                }
            });

            PromptBox.Focus();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Send", ex); }
    }

    private readonly Stopwatch _throttle = Stopwatch.StartNew();

    /// <summary>50 ms UI flush: typewriter reveal of the accumulated stream text. One tick, one repaint.</summary>
    private DispatcherTimer CreateFlushTimer()
    {
        var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        t.Tick += (_, _) =>
        {
            try
            {
                if (!_streamDirty) return;
                _streamDirty = false;

                string partial;
                lock (_streamBuf) { partial = _streamBuf.ToString(); }
                if (partial.Length == 0) return;

                if (!_firstDeltaArrived)
                {
                    _firstDeltaArrived = true;
                    StopTypingDots();
                }

                // typewriter reveal — Ollama deltas are naturally small so this stays
                // caught up (true streaming); Anthropic arrives at once and plays back
                int gap = partial.Length - _shownLen;
                int step = gap > 400 ? 48 : gap > 120 ? 20 : 9;
                _shownLen = Math.Min(partial.Length, _shownLen + step);

                if (_liveText is { } live)
                {
                    live.Text = partial[.._shownLen];
                    ScrollIfAtBottom();
                }
            }
            catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Flush", ex); }
        };
        return t;
    }

    /// <summary>Ends a turn: finalize markdown, record history, restore the send button. UI thread.</summary>
    private void FinishTurn(AiService.AiStreamResult result)
    {
        try
        {
            _flushTimer?.Stop();
            StopTypingDots();

            string typed;
            lock (_streamBuf) { typed = _streamBuf.ToString().Trim(); }

            if (result.Ok)
            {
                string full = typed.Length > 0 ? typed : result.Text;
                _history.Add(new AiProviders.AiTurn("user", _pendingUserPrompt));
                _history.Add(new AiProviders.AiTurn("assistant", full));
                if (_liveHost is { } host)
                {
                    if (_liveText is { } lt) lt.Visibility = Visibility.Collapsed;
                    RenderMarkdownInto(host, full);
                }
            }
            else if (result.Cancelled)
            {
                string partial = typed.Length > 0 ? typed : result.Text;
                _history.Add(new AiProviders.AiTurn("user", _pendingUserPrompt));
                if (partial.Length > 0 && _liveHost is { } h)
                {
                    if (_liveText is { } lt) lt.Visibility = Visibility.Collapsed;
                    RenderMarkdownInto(h, partial);
                    AddFootnote(h, "stopped");
                }
                else
                {
                    RemoveLastAssistantPlaceholder();
                    AppendErrorBubble("Generation stopped.");
                }
            }
            else
            {
                if (typed.Length > 0 && _liveHost is { } h2)
                {
                    if (_liveText is { } lt2) lt2.Visibility = Visibility.Collapsed;
                    RenderMarkdownInto(h2, typed);
                }
                else
                {
                    RemoveLastAssistantPlaceholder();
                }
                AppendErrorBubble(result.Error);
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Finish", ex); }
        finally
        {
            _generating = false;
            _liveHost = null;
            _liveText = null;
            _dotsPanel = null;
            SendButton.Visibility = Visibility.Visible;
            StopButton.Visibility = Visibility.Collapsed;
            SendButton.IsEnabled = !string.IsNullOrWhiteSpace(PromptBox.Text);
            UpdateEmptyState();
            ScrollIfAtBottom();
        }
    }

    /// <summary>The prompt whose answer is streaming (for history pairing). UI-thread only.</summary>
    private string _pendingUserPrompt = "";

    // ---------------------------------------------------------------- bubbles

    private void AppendUserBubble(string text)
    {
        try
        {
            _pendingUserPrompt = text;   // the next assistant completion pairs with this
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var bubble = new Border
            {
                Background = (Brush)Resources["UserBubbleBrush"],
                CornerRadius = new CornerRadius(16, 16, 4, 16),   // prompt-kit: sharp tail corner toward the sender
                Padding = new Thickness(13, 9, 13, 9),
                MaxWidth = 540,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var tb = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13.5,
                LineHeight = 19.5,
                Foreground = (Brush)Resources["TitleBrush"],
            };
            bubble.Child = tb;
            Grid.SetColumn(bubble, 1);
            grid.Children.Add(bubble);
            MessagesHost.Children.Add(grid);
            ScrollIfAtBottom();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.UserBubble", ex); }
    }

    /// <summary>Assistant placeholder: avatar dot + plain live text + (caller adds) typing dots.</summary>
    private (StackPanel Host, TextBlock Live) AppendAssistantBubble()
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var avatar = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = (Brush)Resources["AvatarBrush"],
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 10, 0),
        };
        avatar.Child = new TextBlock
        {
            Text = "?",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Resources["AccentBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(avatar, 0);

        var host = new StackPanel();
        var live = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.5,
            LineHeight = 20,
            Foreground = (Brush)Resources["TitleBrush"],
        };
        host.Children.Add(live);
        Grid.SetColumn(host, 1);

        grid.Children.Add(avatar);
        grid.Children.Add(host);
        MessagesHost.Children.Add(grid);
        ScrollIfAtBottom();
        return (host, live);
    }

    /// <summary>Removes the last assistant placeholder grid (used when a turn dies before any text).</summary>
    private void RemoveLastAssistantPlaceholder()
    {
        try
        {
            // walk up from the live host to its wrapping grid and drop it
            if (_liveHost?.Parent is Grid g && MessagesHost.Children.Contains(g))
                MessagesHost.Children.Remove(g);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.RemovePlaceholder", ex); }
    }

    private void AppendErrorBubble(string text)
    {
        try
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var avatar = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
                Background = (Brush)Resources["WarnBrush"],
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0),
            };
            avatar.Child = new TextBlock
            {
                Text = "!", FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = (Brush)Resources["TitleBrush"],
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(avatar, 0);

            var card = new Border
            {
                Background = (Brush)Resources["WarnBrush"],
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 8, 12, 8),
                MaxWidth = 560,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            card.Child = new TextBlock
            {
                Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5,
                Foreground = (Brush)Resources["TitleBrush"],
            };
            Grid.SetColumn(card, 1);

            grid.Children.Add(avatar);
            grid.Children.Add(card);
            MessagesHost.Children.Add(grid);
            ScrollIfAtBottom();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.ErrorBubble", ex); }
    }

    // ---------------------------------------------------------------- typing dots

    private void StartTypingDots(StackPanel host)
    {
        try
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 4, 0, 2) };
            var dots = new List<System.Windows.Shapes.Ellipse>();
            for (int i = 0; i < 3; i++)
            {
                var d = new System.Windows.Shapes.Ellipse
                {
                    Width = 7, Height = 7, Margin = new Thickness(0, 0, 5, 0),
                    Fill = (Brush)Resources["SubtitleBrush"],
                    Opacity = 0.35,
                };
                dots.Add(d);
                panel.Children.Add(d);
            }
            host.Children.Add(panel);
            _dotsPanel = panel;

            if (_dotsTimer is not { }) _dotsTimer = MakeDotsTimer();
            _dotsTimer.Tag = dots;
            if (_settings.AnimationsEnabled) _dotsTimer.Start();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.DotsStart", ex); }
    }

    private DispatcherTimer MakeDotsTimer()
    {
        var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        int phase = 0;
        t.Tick += (_, _) =>
        {
            try
            {
                if (t.Tag is not List<System.Windows.Shapes.Ellipse> dots) return;
                phase = (phase + 1) % 3;
                for (int i = 0; i < dots.Count; i++)
                    dots[i].Opacity = i == phase ? 1.0 : 0.35;
            }
            catch { /* dots are cosmetic */ }
        };
        return t;
    }

    private void StopTypingDots()
    {
        try
        {
            _dotsTimer?.Stop();
            if (_dotsPanel is { } p && p.Parent is StackPanel host)
                host.Children.Remove(p);
            _dotsPanel = null;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.DotsStop", ex); }
    }

    // ---------------------------------------------------------------- markdown rendering

    /// <summary>Turns markdown-lite blocks into visual children of the bubble host.</summary>
    private void RenderMarkdownInto(StackPanel host, string markdown)
    {
        try
        {
            foreach (var block in MarkdownLite.Parse(markdown))
            {
                switch (block)
                {
                    case MarkdownLite.Heading h:
                        host.Children.Add(new TextBlock
                        {
                            Text = h.Text,
                            FontSize = h.Level == 1 ? 17 : h.Level == 2 ? 15.5 : 14.5,
                            FontWeight = FontWeights.SemiBold,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = (Brush)Resources["TitleBrush"],
                            Margin = new Thickness(0, 10, 0, 4),
                        });
                        break;

                    case MarkdownLite.Bullet b:
                        var row = new Grid { Margin = new Thickness(2, 0, 0, 3) };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        var marker = new TextBlock
                        {
                            Text = "•  ",
                            Foreground = (Brush)Resources["AccentBrush"],
                            FontSize = 13.5,
                            VerticalAlignment = VerticalAlignment.Top,
                        };
                        var content = new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 13.5,
                            LineHeight = 20,
                            Foreground = (Brush)Resources["TitleBrush"],
                        };
                        AddInlineRuns(content, b.Text);
                        Grid.SetColumn(marker, 0);
                        Grid.SetColumn(content, 1);
                        row.Children.Add(marker);
                        row.Children.Add(content);
                        host.Children.Add(row);
                        break;

                    case MarkdownLite.CodeBlock c:
                        host.Children.Add(BuildCodeBlock(c));
                        break;

                    case MarkdownLite.Para p2:
                        var para = new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 13.5,
                            LineHeight = 20,
                            Foreground = (Brush)Resources["TitleBrush"],
                            Margin = new Thickness(0, 0, 0, 8),
                        };
                        AddInlineRuns(para, p2.Text);
                        host.Children.Add(para);
                        break;
                }
            }

            // per-answer footer: copy the whole answer (prompt-kit's message action)
            host.Children.Add(BuildCopyFooterRow(markdown));
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Render", ex); }
    }

    private System.Windows.Controls.Border BuildCodeBlock(MarkdownLite.CodeBlock c)
    {
        var outer = new System.Windows.Controls.Border
        {
            Background = (Brush)Resources["CodeBrush"],
            BorderBrush = (Brush)Resources["BorderLineBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 10),
            Margin = new Thickness(0, 4, 0, 10),
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var stack = new StackPanel();

        // header: language label + copy
        var header = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lang = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(c.Lang) ? "code" : c.Lang,
            FontSize = 10.5,
            Foreground = (Brush)Resources["SubtitleBrush"],
        };
        var copy = new Button { Style = (Style)FindResource("CaptionButton"), Content = "Copy", Tag = c.Text };
        copy.Click += OnCopyTagClick;
        Grid.SetColumn(lang, 0);
        Grid.SetColumn(copy, 1);
        header.Children.Add(lang);
        header.Children.Add(copy);
        stack.Children.Add(header);

        // code body — horizontally scrollable, monospace, verbatim
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var code = new TextBlock
        {
            Text = c.Text,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            Foreground = (Brush)Resources["TitleBrush"],
        };
        scroll.Content = code;
        stack.Children.Add(scroll);

        outer.Child = stack;
        return outer;
    }

    private FrameworkElement BuildCopyFooterRow(string fullText)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 0, 2) };
        var copy = new Button { Style = (Style)FindResource("CaptionButton"), Content = "Copy answer", Tag = fullText };
        copy.Click += OnCopyTagClick;
        row.Children.Add(copy);
        return row;
    }

    /// <summary>Fills a TextBlock with bold / inline-code runs via MarkdownLite.Inline.</summary>
    private static void AddInlineRuns(TextBlock target, string text)
    {
        try
        {
            foreach (var run in MarkdownLite.Inline(text))
            {
                var r = new System.Windows.Documents.Run(run.Text);
                if (run.Bold) r.FontWeight = FontWeights.SemiBold;
                if (run.Code)
                {
                    r.FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New");
                    r.FontSize = 12.5;
                    r.Background = TryGetBrush(target, "CodeBrush");
                }
                target.Inlines.Add(r);
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Inline", ex); target.Text = text; }
    }

    /// <summary>Walks up to the Window for a resource — inline elements need it for theme brushes.</summary>
    private static Brush? TryGetBrush(FrameworkElement element, string key)
        => element.TryFindResource(key) is Brush b ? b : null;

    private void AddFootnote(StackPanel host, string note)
    {
        try
        {
            host.Children.Add(new TextBlock
            {
                Text = $"· {note}",
                FontSize = 11,
                Foreground = (Brush)Resources["SubtitleBrush"],
                Margin = new Thickness(0, 2, 0, 2),
            });
        }
        catch { }
    }

    // ---------------------------------------------------------------- copy

    private void OnCopyTagClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string text } btn) return;
            if (text.Length == 0) return;
            Clipboard.SetText(text);
            string original = btn.Content as string ?? "Copy";
            btn.Content = "Copied";
            var revert = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            revert.Tick += (_, _) =>
            {
                try { btn.Content = original; }
                catch { }
                finally { try { revert.Stop(); } catch { } }
            };
            revert.Start();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Copy", ex); }
    }

    // ---------------------------------------------------------------- scrolling

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        try { _atBottom = e.ExtentHeight - (e.VerticalOffset + e.ViewportHeight) < 48; }
        catch { }
    }

    private void ScrollIfAtBottom()
    {
        try { if (_atBottom) ChatScroll.ScrollToEnd(); }
        catch { }
    }
}
