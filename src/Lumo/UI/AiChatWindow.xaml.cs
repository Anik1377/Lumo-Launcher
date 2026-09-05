using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    // v2.4.0-alpha.5 — persisted multi-chat history + system-prompt personas
    private readonly ChatStore _store;
    private ChatSession? _session;              // active conversation (null = fresh unsaved chat)
    private bool _sidebarOpen;
    private bool _restoring;                    // suppress per-message motion during a bulk transcript restore

    private bool _generating;
    private CancellationTokenSource? _genCts;
    private bool _sourceReady;
    private bool _atBottom = true;

    // ---- v2.6.0-alpha.4 — offline voice typing, record → transcribe → show:
    //      the clip is captured in full, recognized as ONE batch, and only then
    //      shown — _voiceBase is the text the transcription will join onto.
    private readonly VoiceInputService _voice = new();
    private string _voiceBase = "";
    private System.Windows.Threading.DispatcherTimer? _voiceErrTimer;
    private System.Windows.Media.Animation.Storyboard? _micPulse;

    // ---- v2.6.0-alpha.5 — the recording overlay (waveform + prominent stop cap):
    private System.Windows.Threading.DispatcherTimer? _voiceClock;
    private DateTime _voiceClockStart;
    private DateTime? _voicePauseStarted;
    private TimeSpan _voicePausedTotal;
    private bool _voiceSetupOpen;                                   // the Whisper setup card is up
    private string? _voiceEngineOverride;                           // v2.6.0-alpha.6 — session-only engine fallback from the setup card (never persisted)
    private VoiceWhisper.WhisperModel? _setupModel;                 // the model the card offers
    private CancellationTokenSource? _voiceDlCts;                   // active model download

    // ---- v2.6.0-alpha.5 — prompt-kit Reasoning block (streaming live view):
    private System.Windows.Controls.Border? _thinkCard;
    private System.Windows.Controls.TextBlock? _thinkBody;
    private System.Windows.Controls.TextBlock? _thinkHeader;
    private System.Windows.Controls.TextBlock? _thinkChevron;
    private bool _thinkOpen;

    // ---- v2.6.0-alpha.5 — prompt-kit ImageAttachment (one image per turn):
    private AiProviders.ImagePayload? _pendingImage;
    private AiProviders.ImagePayload? _turnImage;                   // the image whose answer is streaming

    /// <summary>v2.3.0-alpha.4 — the banner's "Open AI settings" link asks App to open Settings → AI (page 6).</summary>
    public event Action? SettingsRequested;

    // ---- streaming plumbing -------------------------------------------------
    private readonly StringBuilder _streamBuf = new();
    private volatile bool _streamDirty;
    private DispatcherTimer? _flushTimer;
    private int _shownLen;                       // typewriter reveal cursor
    private bool _firstDeltaArrived;
    private TextBlock? _liveText;                // plain-text view while streaming
    private StackPanel? _liveHost;               // markdown host, filled on completion
    private StackPanel? _dotsPanel;              // the three-dot "thinking" row

    private static readonly (string Glyph, string Text)[] Chips =
    {
        ("\uE945", "Explain quantum computing like I'm five"),      // bolt
        ("\uE715", "Draft a polite follow-up email"),               // mail
        ("\uE943", "PowerShell: list files bigger than 1 GB"),      // code
        ("\uE8A5", "Brainstorm 5 name ideas for a side project"),   // document
    };

    public AiChatWindow(Settings settings, AiService ai)
    {
        InitializeComponent();
        _settings = settings;
        _ai = ai;
        _store = ChatStore.Load();   // v2.4.0-alpha.5 — chats survive restarts

        ApplySelfTheme();
        UpdateModelChip();
        UpdatePersonaChip();
        UpdateBanner();
        UpdateEmptyState();
        BuildChips();

        Closed += (_, _) =>
        {
            try { _genCts?.Cancel(); } catch { }
            try { _voiceDlCts?.Cancel(); } catch { }
            try { _voice.Dispose(); } catch { }   // v2.6.0-alpha.4 — never leave a recording or transcription dangling after the window dies
        };
        Loaded += (_, _) =>
        {
            PromptBox.Focus();
            StartOrbBreathing();
            UpdateMicState();
        };

        // v2.6.0-alpha.4 — capture and recognition run off the UI thread; the service
        // marshals every event onto its session dispatcher and InvokeAsync re-queues
        // to the window thread, so voice can never touch a control from a worker.
        _voice.CaptureStarted += () => Dispatcher.InvokeAsync(OnVoiceStarted);
        _voice.TranscribingStarted += () => Dispatcher.InvokeAsync(OnVoiceTranscribing);
        _voice.Final += t => Dispatcher.InvokeAsync(() => OnVoiceFinal(t));
        _voice.Failed += m => Dispatcher.InvokeAsync(() => OnVoiceFailed(m));
        // v2.6.0-alpha.5 — 10 Hz mic loudness → the waveform; a missing whisper
        // model opens the one-time setup card instead of recording.
        _voice.Level += l => Dispatcher.InvokeAsync(() => Waveform.Push(l));
        _voice.ModelNeeded += id => Dispatcher.InvokeAsync(() => OnVoiceModelNeeded(id));
    }

    // ---------------------------------------------------------------- polish motion

    /// <summary>
    /// v2.3.0-alpha.4 — the empty-state halo breathes: a slow sine opacity wave that
    /// gives the welcome screen life without demanding attention. Runs only when the
    /// user has not disabled animations; the orb is collapsed once chatting starts,
    /// so the cost disappears with it.
    /// </summary>
    private void StartOrbBreathing()
    {
        try
        {
            if (!_settings.AnimationsEnabled) return;
            // v3.0 — glow fix: the halo breathes a QUIET whisper now (0.22→0.42, was
            // 0.55→0.95) so the orb reads as presence, not a lighthouse.
            var wave = new System.Windows.Media.Animation.DoubleAnimation(0.22, 0.42, TimeSpan.FromMilliseconds(2600))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut,
                },
            };
            OrbGlow.BeginAnimation(OpacityProperty, wave);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.OrbBreath", ex); }
    }

    /// <summary>
    /// v2.3.0-alpha.4 — message entrance: a short fade + 7 px rise, the fluid-response
    /// micro-motion that keeps the transcript from popping in dead still. One animation
    /// per message; skipped entirely when animations are disabled.
    /// </summary>
    private void AnimateIn(FrameworkElement fe, double rise = 7)
    {
        try
        {
            if (_restoring || !_settings.AnimationsEnabled) return;
            var tt = new System.Windows.Media.TranslateTransform(0, rise);
            fe.RenderTransform = tt;
            var ease = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            };
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = ease,
            };
            var lift = new System.Windows.Media.Animation.DoubleAnimation(rise, 0, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = ease,
            };
            fe.BeginAnimation(OpacityProperty, fade);
            tt.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, lift);
        }
        catch { /* motion is cosmetic */ }
    }

    // ---------------------------------------------------------------- theme

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _sourceReady = true;
        try { ApplySelfTheme(); } catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Theme", ex); }
    }

    /// <summary>Same Fluent token set as SettingsWindow — the chat belongs to the launcher family.
    /// v3.0 — the brush set comes from the shared ThemeService; this method keeps only
    /// the chat's own extras (DWM chrome, corner sync, scrim).</summary>
    private void ApplySelfTheme()
    {
        try
        {
            var t = ThemeService.Apply(this, _settings);
            if (_sourceReady)
                GlassBackdrop.Apply(this, t.Dark);

            _focusRingBrush = new SolidColorBrush(Appearance.Tint(t.Accent, 0x66));

            // the card edge IS the window edge now — sync the corner radius to what DWM
            // actually rounds (Win11 8 px, Win10 square), so the shadow hugs the card.
            bool rounded = !string.Equals(_settings.CornerStyle, "square", StringComparison.OrdinalIgnoreCase);
            float r = GlassBackdrop.IsWin11 && rounded ? 8f : 0f;
            _chromeRadius = r;   // v2.4.0-alpha.6 — remembered by the fullscreen toggle
            if (!_fullscreen)    // don't fight the squared fullscreen state on a live theme switch
            {
                RootCard.CornerRadius = new CornerRadius(r);
                CaptionBar.CornerRadius = new CornerRadius(r, r, 0, 0);
                SidebarPanel.CornerRadius = new CornerRadius(0, r, r, 0);
            }
            SidebarScrim.Background = new SolidColorBrush(t.Dark
                ? Color.FromArgb(0x73, 0x00, 0x00, 0x00)
                : Color.FromArgb(0x3D, 0x00, 0x00, 0x00));
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.ApplyTheme", ex); }
    }

    private SolidColorBrush? _focusRingBrush;

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

    // ------------------------------------- v2.4.0-alpha.6 — fullscreen / window

    private bool _fullscreen;
    private float _chromeRadius = 8f;   // the DWM-synced corner radius ApplySelfTheme computes

    /// <summary>Caption button + F11: borderless fullscreen (covers the taskbar) ↔ windowed.</summary>
    private void OnFullscreenClick(object sender, RoutedEventArgs e)
    {
        try { SetFullscreen(!_fullscreen); }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Fullscreen", ex); }
    }

    private void SetFullscreen(bool on)
    {
        _fullscreen = on;
        FullscreenButton.Content = on ? "\uE8A7" : "\uE740";   // back-to-window ↔ fullscreen glyph
        FullscreenButton.ToolTip = on ? "Exit fullscreen (F11)" : "Fullscreen (F11)";
        WindowState = on ? WindowState.Maximized : WindowState.Normal;   // WindowStyle=None → true fullscreen

        // square the card against the screen edges while fullscreen, restore after
        float r = on ? 0f : _chromeRadius;
        RootCard.CornerRadius = new CornerRadius(r);
        CaptionBar.CornerRadius = new CornerRadius(r, r, 0, 0);
        SidebarPanel.CornerRadius = new CornerRadius(0, r, r, 0);
    }

    private void OnOpenAiSettings(object sender, RoutedEventArgs e)
    {
        try { SettingsRequested?.Invoke(); }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.OpenSettings", ex); }
    }

    // ------------------------------------- v3.0.0-alpha.5 — the App Deck launch button
    //
    // The deck used to be a TAB inside this window (RailDeck radio + DeckHost
    // overlay + SwitchTab). The user asked for it to be independent, so it is
    // its own window now (AppDeckWindow): the rail button just raises
    // DeckLaunchRequested and App.OpenDeck() shows (or focuses) that window.

    /// <summary>The rail's deck button was pressed — App opens the standalone App Deck window.</summary>
    public event Action? DeckLaunchRequested;

    private void OnRailDeckClick(object sender, RoutedEventArgs e)
    {
        try { DeckLaunchRequested?.Invoke(); }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.DeckLaunch", ex); }
    }

    private void OnRailSettingsClick(object sender, RoutedEventArgs e)
    {
        try { SettingsRequested?.Invoke(); }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.RailSettings", ex); }
    }

    // ---------------------------------------------------------------- empty state / chips

    private void BuildChips()
    {
        try
        {
            ChipHost.Children.Clear();
            foreach (var (glyph, chip) in Chips)
            {
                // prompt-kit suggestion rows carry a small icon ahead of the text
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 11,
                    Foreground = (Brush)Resources["AccentBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 7, 0),
                });
                panel.Children.Add(MakeChipText(chip));
                var b = new Button { Style = (Style)FindResource("ChipButton"), Content = panel };
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
            // v3.0 — the mascot wears the active persona's face and color
            var _personaForMascot = ResolvePersona(_session?.Persona ?? _settings.AiPersona);
            Mascot.FaceId = PersonaFaces.NormalizeId(_personaForMascot.Face);
            Mascot.PersonaColor = PersonaFaces.NormalizeColor(_personaForMascot.Color);
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
            // live status dot: emerald for private local models, accent for the API
            ModelDot.Fill = anthropic
                ? (System.Windows.Media.Brush)Resources["AccentBrush"]
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81));
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
                SendFromInput();   // v2.6.0-alpha.4 — while recording, Enter finishes the clip and shows the text; a second Enter sends it
            }
            // Shift+Enter falls through — the TextBox inserts the newline
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.PromptKey", ex); }
    }

    /// <summary>
    /// v2.3.0-alpha.4 — Esc closes the chat from anywhere in the window (the hint
    /// chips promise it, so it must be true). v2.4.0-alpha.5 — Esc closes the
    /// history sidebar first when it is open, and Ctrl+N starts a new chat.
    /// v2.6.0-alpha.5 — Esc also backs out of the Whisper setup card (cancelling a
    /// running download), and Enter finishes a recording even though the prompt
    /// box is collapsed while the overlay is up.
    /// </summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            // v3.0.0-alpha.5 — the deck tab is gone (the deck is its own window);
            // numpad keys here are free again for chat use.
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (_voice.IsListening) { _voice.Cancel(); OnVoiceStopped(); return; }   // v2.6.0-alpha.4 — first Esc cancels voice (clip or pending text), not the window
                if (_voiceSetupOpen)                                                     // v2.6.0-alpha.5 — back out of the setup card / model download
                {
                    try { _voiceDlCts?.Cancel(); } catch { }
                    HideVoiceOverlay();
                    PromptBox.Focus();
                    return;
                }
                if (_sidebarOpen) { CloseSidebar(); return; }
                // v2.4.0-alpha.6 — while a generation is running, Esc stops it first
                // (the hint line says "esc stop / hide"); a second Esc then hides.
                if (_generating) { _genCts?.Cancel(); return; }
                Close();
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && _voice.IsRecording)
            {
                e.Handled = true;
                StopVoice();   // v2.6.0-alpha.5 — Enter finishes the clip while the overlay holds the input row
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && !_voice.IsListening)
            {
                // v2.6.0-alpha.5 — pasting a screenshot attaches it (prompt-kit
                // ImageAttachment) instead of letting the TextBox swallow it
                if (TryAttachFromClipboard()) e.Handled = true;
            }
            else if (e.Key == Key.F11)
            {
                e.Handled = true;
                SetFullscreen(!_fullscreen);   // v2.4.0-alpha.6 — fullscreen toggle
            }
            else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                OnNewChat(sender, e);
            }
            else if (e.Key == Key.M && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;                                  // v2.6.0-alpha.3 — voice typing toggle
                ToggleVoice();
            }
        }
        catch { }
    }

    private void OnPromptTextChanged(object sender, TextChangedEventArgs e)
    {
        try { UpdateSendEnabled(); }
        catch { }
    }

    /// <summary>v2.6.0-alpha.5 — send is live when there is text OR an attached image.</summary>
    private void UpdateSendEnabled()
    {
        SendButton.IsEnabled = !string.IsNullOrWhiteSpace(PromptBox.Text) || _pendingImage is not null;
    }

    /// <summary>
    /// v2.3.0-alpha.4 — prompt-kit focus treatment: the input's hairline stroke lifts
    /// to an accent ring while the prompt holds focus, then settles back. The one
    /// place the chrome actively answers the user's pointer.
    /// </summary>
    private void OnPromptFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        try
        {
            PromptShell.BorderBrush = PromptBox.IsKeyboardFocusWithin || _voice.IsListening
                ? _focusRingBrush ?? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("BorderLineBrush");
        }
        catch { }
    }

    // ---------------------------------------------------------------- voice typing (v2.6.0-alpha.5 — record → transcribe → show, with the live overlay)

    /// <summary>
    /// Mic visibility: voice enabled AND the configured engine is usable. Whisper
    /// needs no SAPI recognizers (the model download is its own setup path), so
    /// the mic shows even on machines Windows speech never supported. The attach
    /// button rides along whenever AI can answer (vision models consume images).
    /// </summary>
    private void UpdateMicState()
    {
        try
        {
            bool engineWhisper = !string.Equals(_settings.VoiceEngine, VoiceInputService.EngineWindows, StringComparison.OrdinalIgnoreCase);
            bool usable = _settings.VoiceEnabled && (engineWhisper || VoiceInputService.IsSupported);
            MicButton.Visibility = usable ? Visibility.Visible : Visibility.Collapsed;
            AttachButton.Visibility = _settings.AiEnabled ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.MicState", ex); }
    }

    private void OnMicClick(object sender, RoutedEventArgs e) => ToggleVoice();

    private void ToggleVoice()
    {
        if (_voice.IsRecording) StopVoice();        // second press: finish the clip → transcribe
        else if (_voice.IsListening) return;        // transcribing — the text is about to appear
        else StartVoice();
    }

    private void StartVoice()
    {
        try
        {
            if (_voice.IsListening || _voiceSetupOpen || _generating || !_settings.VoiceEnabled) return;
            _voiceBase = PromptBox.Text;   // transcription joins whatever is already typed
            // v2.6.0-alpha.6 — the setup card's mic/"Use Windows speech" is a SESSION
            // fallback: it must not permanently demote Whisper in settings.json (a
            // failed download used to strand the user on the weaker engine forever).
            bool engineWhisper = _voiceEngineOverride is null &&
                                 !string.Equals(_settings.VoiceEngine, VoiceInputService.EngineWindows, StringComparison.OrdinalIgnoreCase);
            string model = string.IsNullOrWhiteSpace(_settings.VoiceModel)
                ? VoiceWhisper.DefaultModelId : _settings.VoiceModel.Trim();
            _voice.Start(_settings.VoiceLanguage,
                engineWhisper ? VoiceInputService.EngineWhisper : VoiceInputService.EngineWindows,
                model);   // a missing whisper model raises ModelNeeded → the setup card
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.VoiceStart", ex); }
    }

    /// <summary>Finish the clip and let the batch recognizer run — no partials, the text shows when it is done.</summary>
    private void StopVoice()
    {
        try { _voice.Stop(); }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.VoiceStop", ex); }
    }

    private void OnVoiceStarted()
    {
        Mascot.Mood = MascotView.Moods.Listening;   // v3.0 — the mascot leans in while you speak
        if (!_voice.IsRecording) return;   // stale queued event after an instant cancel — drop
        try
        {
            MicButton.Tag = "listening";
            MicButton.Content = "\uE71A";   // mic cap becomes a stop cap: click again to finish
            MicButton.ToolTip = "Finish & transcribe (Ctrl+M or Enter) · Esc cancels";
            ShowVoiceOverlay();
            StartMicPulse();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.VoiceStarted", ex); }
    }

    /// <summary>Clip captured, recognition running — freeze the waveform, only cancel stays alive.</summary>
    private void OnVoiceTranscribing()
    {
        if (!_voice.IsTranscribing) return;   // stale queued event — drop
        try
        {
            MicButton.Tag = "transcribing";
            MicButton.IsEnabled = false;      // nothing to click while the batch runs
            MicButton.ToolTip = "Transcribing…";

            VoiceStatusText.Text = "Transcribing…";
            VoiceDot.Fill = (Brush)Resources["AccentBrush"];
            VoiceTimerText.Visibility = Visibility.Collapsed;
            Waveform.Freeze();
            VoiceStopButton.Tag = "";         // heartbeat scale off
            VoiceStopButton.IsEnabled = false;
            VoiceStopButton.Opacity = 0.45;
            VoicePauseButton.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.VoiceTranscribing", ex); }
    }

    /// <summary>
    /// v2.6.0-alpha.4 — the SHOW step: the whole clip transcribed, the text lands
    /// in the prompt once, caret pinned to the end. Nothing was written to the box
    /// during recording or recognition, so this is the first thing the user sees.
    /// </summary>
    private void OnVoiceFinal(string text)
    {
        if (_voice.IsListening) return;   // stale queued event after a cancel — never repopulate a cleared box
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _voiceBase = VoiceText.Compose(_voiceBase, text);
            PromptBox.Text = _voiceBase;
            PromptBox.CaretIndex = _voiceBase.Length;
            PromptBox.Focus();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.VoiceFinal", ex); }
        OnVoiceStopped();
    }

    /// <summary>Restore the idle voice UI — after show, after failure, and after Esc cancels.</summary>
    private void OnVoiceStopped()
    {
        Mascot.Mood = MascotView.Moods.Idle;   // v3.0
        try
        {
            MicButton.Tag = "";
            MicButton.Content = "\uE720";   // back to the mic glyph
            MicButton.IsEnabled = true;
            MicButton.ToolTip = "Voice input (Ctrl+M)";
            HideVoiceOverlay();
            StopMicPulse();
            if (PromptBox.IsKeyboardFocusWithin)
                PromptShell.BorderBrush = (Brush)FindResource("BorderLineBrush");
        }
        catch { }
    }

    /// <summary>Mic on a machine that can't dictate: one quiet placeholder-sized message, auto-clears; tooltip keeps the full reason.</summary>
    private void OnVoiceFailed(string message)
    {
        try
        {
            OnVoiceStopped();
            MicButton.ToolTip = message;
            if (PromptBox.Text.Length == 0)
                PromptPlaceholder.Text = message.Length <= 90 ? message : message[..87] + "…";
            _voiceErrTimer?.Stop();
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += (_, _) =>
            {
                try { if (!_voice.IsListening) PromptPlaceholder.Text = "Ask anything…"; t.Stop(); }
                catch { }
            };
            t.Start();
            _voiceErrTimer = t;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.VoiceFailed", ex); }
    }

    /// <summary>Breathing opacity on the mic while listening — same doctrine as the orb: skip when animations are off.</summary>
    private void StartMicPulse()
    {
        try
        {
            if (!_settings.AnimationsEnabled) return;
            var wave = new System.Windows.Media.Animation.DoubleAnimation(1, 0.5, TimeSpan.FromMilliseconds(1100))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
                }
            };
            System.Windows.Media.Animation.Storyboard.SetTarget(wave, MicButton);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(wave, new PropertyPath("Opacity"));
            var sb = new System.Windows.Media.Animation.Storyboard();
            sb.Children.Add(wave);
            sb.Begin(MicButton, true);
            _micPulse = sb;
        }
        catch { }
    }

    private void StopMicPulse()
    {
        try { _micPulse?.Stop(MicButton); MicButton.Opacity = 1; } catch { }
        _micPulse = null;
    }

    // ------------------------------------------- v2.6.0-alpha.5 — the voice overlay

    /// <summary>
    /// The overlay takes the prompt row's place while a session is live: waveform
    /// proof-of-life, elapsed clock, pause / PROMINENT red stop / cancel caps.
    /// PromptShell collapses so exactly one of the two shows.
    /// </summary>
    private void ShowVoiceOverlay()
    {
        _voiceSetupOpen = false;
        PromptShell.Visibility = Visibility.Collapsed;
        VoiceOverlay.Visibility = Visibility.Visible;
        VoiceSetupPanel.Visibility = Visibility.Collapsed;
        VoiceLivePanel.Visibility = Visibility.Visible;

        VoiceDot.Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));   // recording red
        VoiceStatusText.Text = "Listening…";
        VoiceTimerText.Visibility = Visibility.Visible;
        VoiceTimerText.Text = "0:00";
        VoiceStopButton.Tag = "recording";      // heartbeat + glow on the PROMINENT cap
        VoiceStopButton.IsEnabled = true;
        VoiceStopButton.Opacity = 1;
        VoicePauseButton.Visibility = Visibility.Visible;
        VoicePauseButton.Content = "\uE769";    // pause glyph
        VoicePauseButton.ToolTip = "Pause recording";

        Waveform.Reset();
        Waveform.BarColor = Color.FromRgb(0xE5, 0x48, 0x4D);
        Waveform.Start();
        StartVoiceClock();
    }

    /// <summary>Back to the prompt row; the overlay (and its clock) fully stops.</summary>
    private void HideVoiceOverlay()
    {
        _voiceSetupOpen = false;
        VoiceOverlay.Visibility = Visibility.Collapsed;
        VoiceLivePanel.Visibility = Visibility.Collapsed;
        VoiceSetupPanel.Visibility = Visibility.Collapsed;
        VoiceDownloadPanel.Visibility = Visibility.Collapsed;
        PromptShell.Visibility = Visibility.Visible;
        Waveform.Reset();
        StopVoiceClock();
    }

    private void OnVoiceStopClick(object sender, RoutedEventArgs e) => StopVoice();

    /// <summary>v2.6.0-alpha.6 — back out of the setup card: cancel any running download and restore the prompt row.</summary>
    private void OnVoiceSetupCancelClick(object sender, RoutedEventArgs e)
    {
        try
        {
            try { _voiceDlCts?.Cancel(); } catch { }
            _setupModel = null;
            HideVoiceOverlay();
            UpdateMicState();
            PromptBox.Focus();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.VoiceSetupCancel", ex); }
    }

    private void OnVoiceCancelClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _voice.Cancel();
            OnVoiceStopped();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.VoiceCancel", ex); }
    }

    // ------------------------------------------- pause / resume

    private void OnVoicePauseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_voice.IsPaused)
            {
                _voice.Resume();
                if (_voicePauseStarted is { } at) { _voicePausedTotal += DateTime.UtcNow - at; _voicePauseStarted = null; }
                VoiceStatusText.Text = "Listening…";
                VoicePauseButton.Content = "\uE769";
                VoicePauseButton.ToolTip = "Pause recording";
                VoiceDot.Opacity = 1;
            }
            else
            {
                _voice.Pause();
                _voicePauseStarted = DateTime.UtcNow;
                VoiceStatusText.Text = "Paused — click to resume";
                VoicePauseButton.Content = "\uE768";   // play
                VoicePauseButton.ToolTip = "Resume recording";
                VoiceDot.Opacity = 0.35;
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.VoicePause", ex); }
    }

    // ------------------------------------------- elapsed clock (paused time excluded)

    private void StartVoiceClock()
    {
        _voiceClockStart = DateTime.UtcNow;
        _voicePauseStarted = null;
        _voicePausedTotal = TimeSpan.Zero;
        _voiceClock ??= CreateVoiceClock();
        _voiceClock.Start();
    }

    private void StopVoiceClock()
    {
        try { _voiceClock?.Stop(); } catch { }
    }

    private DispatcherTimer CreateVoiceClock()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        t.Tick += (_, _) =>
        {
            try
            {
                if (!_voice.IsRecording) return;
                var end = _voicePauseStarted ?? DateTime.UtcNow;
                var elapsed = end - _voiceClockStart - _voicePausedTotal;
                if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
                VoiceTimerText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
            }
            catch { }
        };
        return t;
    }

    // ------------------------------------------- v2.6.0-alpha.5 — the Whisper setup card

    /// <summary>
    /// Raised BEFORE recording when the engine is whisper but the model isn't
    /// installed: the overlay becomes a one-time setup card — what the download
    /// is, how big, and a one-click path. "Use Windows speech" records right
    /// away with the SAPI fallback instead.
    /// </summary>
    private void OnVoiceModelNeeded(string modelId)
    {
        try
        {
            if (_voice.IsListening || _voiceDlCts is not null) return;
            _setupModel = VoiceWhisper.FromId(modelId);
            var m = _setupModel;

            _voiceSetupOpen = true;
            PromptShell.Visibility = Visibility.Collapsed;
            VoiceOverlay.Visibility = Visibility.Visible;
            VoiceLivePanel.Visibility = Visibility.Collapsed;
            VoiceSetupPanel.Visibility = Visibility.Visible;
            VoiceDownloadPanel.Visibility = Visibility.Collapsed;

            VoiceSetupDesc.Text = $"Whisper runs fully offline and is far more accurate than Windows speech. " +
                                  $"One-time download: {m.Name} · {m.SizeLabel} — then the mic just works, no connection needed. " +
                                  $"Broken downloads resume where they stopped.";
            VoiceDownloadButton.Content = new TextBlock { Text = $"Download · {m.SizeLabel}" };
            VoiceDownloadButton.IsEnabled = true;
            bool sapi = VoiceInputService.IsSupported;
            VoiceWindowsButton.IsEnabled = sapi;
            VoiceWindowsButton.ToolTip = sapi
                ? "Skip the download — record with the built-in Windows recognizer"
                : "Windows speech is not installed on this PC";
            VoiceSetupMicButton.Visibility = sapi ? Visibility.Visible : Visibility.Collapsed;
            VoiceSetupMicButton.IsEnabled = sapi;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.VoiceSetup", ex); }
    }

    private void OnVoiceDownloadClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_setupModel is not { } m || _voiceDlCts is not null) return;
            _voiceDlCts = new CancellationTokenSource();
            VoiceDownloadPanel.Visibility = Visibility.Visible;
            VoiceDownloadBar.Value = 0;
            VoiceDownloadText.Text = "Starting…";
            VoiceDownloadButton.IsEnabled = false;
            VoiceWindowsButton.IsEnabled = false;
            VoiceSetupMicButton.IsEnabled = false;

            var progress = new Progress<double>(v =>
            {
                VoiceDownloadBar.Value = Math.Clamp(v, 0, 1);
                VoiceDownloadText.Text = v < 0.995
                    ? $"Downloading… {v:P0} of {m.SizeLabel}"
                    : "Finishing…";
            });
            var ct = _voiceDlCts.Token;

            _ = Task.Run(async () =>
            {
                string? err = await WhisperEngine.DownloadAsync(m, progress, ct).ConfigureAwait(true);
                await Dispatcher.InvokeAsync(() => OnVoiceDownloadDone(m, err));
            });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.VoiceDownload", ex); }
    }

    private void OnVoiceDownloadDone(VoiceWhisper.WhisperModel m, string? error)
    {
        try
        {
            _voiceDlCts = null;
            if (error is null)
            {
                _settings.VoiceEngine = VoiceInputService.EngineWhisper;
                _settings.VoiceModel = m.Id;
                _settings.Save();
                HideVoiceOverlay();
                UpdateMicState();
                StartVoice();   // the download is the setup — recording starts immediately
                return;
            }
            if (error == "cancelled")
            {
                HideVoiceOverlay();
                return;
            }
            // the card stays up with the reason + the resume promise; the mic,
            // download retry and the way back are all still one tap away
            VoiceDownloadText.Text = error + " — the next attempt resumes where it stopped. You can also record right away with the mic button.";
            VoiceDownloadButton.IsEnabled = true;
            bool sapi = VoiceInputService.IsSupported;
            VoiceWindowsButton.IsEnabled = sapi;
            VoiceSetupMicButton.IsEnabled = sapi;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.VoiceDownloadDone", ex); }
    }

    private void OnVoiceUseWindowsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // v2.6.0-alpha.6 — session-only fallback: record with Windows speech NOW
            // without uninstalling Whisper as the default engine (that stays a
            // settings.json decision: "VoiceEngine": "windows").
            _voiceEngineOverride = VoiceInputService.EngineWindows;
            _setupModel = null;
            HideVoiceOverlay();
            UpdateMicState();
            StartVoice();   // SAPI fallback — records right away
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.VoiceUseWindows", ex); }
    }

    private void OnSendClick(object sender, RoutedEventArgs e) => SendFromInput();

    private void SendFromInput()
    {
        try
        {
            // v2.6.0-alpha.4 — record → transcribe → show: while recording, the first
            // Enter / send click finishes the clip and shows the text instead of sending;
            // while transcribing there is nothing to send yet, wait for it.
            if (_voice.IsRecording) { StopVoice(); return; }
            if (_voice.IsTranscribing || _voiceSetupOpen) return;
            string t = PromptBox.Text.Trim();
            if ((t.Length == 0 && _pendingImage is null) || _generating) return;
            PromptBox.Clear();
            var img = _pendingImage;
            ClearPendingImage();
            SendUserMessage(t, img);   // v2.6.0-alpha.5 — an attached image rides along
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.SendInput", ex); }
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        try { _genCts?.Cancel(); }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Stop", ex); }
    }

    /// <summary>
    /// v2.4.0-alpha.5 — starts a fresh chat. The previous conversation (if it had
    /// any content) stays saved in the store and remains reachable in the sidebar;
    /// an already-empty chat just refocuses the input instead of stacking ghosts.
    /// </summary>
    private void OnNewChat(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_generating) _genCts?.Cancel();
            if (_session is null)
            {
                // already a fresh, unsaved chat — just refocus
                CloseSidebar();
                PromptBox.Focus();
                return;
            }
            _session = null;
            ResetConversationUi();
            UpdatePersonaChip();
            CloseSidebar();
            PromptBox.Focus();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.NewChat", ex); }
    }

    /// <summary>Common teardown: clears transcript, stream state and the busy flags.</summary>
    private void ResetConversationUi()
    {
        _history.Clear();
        MessagesHost.Children.Clear();
        _streamBuf.Clear();
        _shownLen = 0;
        _generating = false;
        _lastAssistantGrid = null;
        _liveHost = null;
        _liveText = null;
        _dotsPanel = null;
        SendButton.Visibility = Visibility.Visible;
        StopButton.Visibility = Visibility.Collapsed;
        SendButton.IsEnabled = !string.IsNullOrWhiteSpace(PromptBox.Text);
        UpdateEmptyState();
    }

    // ------------------------------------------- v2.4.0-alpha.5/6 — personas

    /// <summary>v2.4.0-alpha.6 — custom personas resolve FIRST, then the built-in registry.</summary>
    private ChatPersona ResolvePersona(string? id) =>
        ChatPersonas.ResolveWith(id, PersonaStore.Current.All);

    /// <summary>MDL2 glyphs live in the Private Use Area — anything else is emoji/text.</summary>
    private static bool IsMdl2Glyph(string g) =>
        g.Length > 0 && g[0] >= '\uE000' && g[0] <= '\uF8FF';

    private void UpdatePersonaChip()
    {
        try
        {
            var persona = ResolvePersona(_session?.Persona ?? _settings.AiPersona);
            // v3.0 — the chip shows the persona's FACE; color "" follows the theme accent
            PersonaFaceGlyph.FaceId = PersonaFaces.NormalizeId(persona.Face);
            PersonaFaceGlyph.PersonaColor = PersonaFaces.NormalizeColor(persona.Color);
            PersonaChipText.Text = persona.Name;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.PersonaChip", ex); }
    }

    /// <summary>
    /// Persona flyout: one system prompt per chat, check-marked when active.
    /// v2.4.0-alpha.6 — user-defined personas (Settings → AI) list under their own
    /// caption, and a "Manage personas…" row jumps straight to the editor.
    /// </summary>
    private void OnPersonaChipClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string current = _session?.Persona ?? _settings.AiPersona;
            var menu = new ContextMenu();
            menu.Items.Add(CaptionItem("PERSONA · SYSTEM PROMPT"));
            foreach (var p in ChatPersonas.All)
            {
                bool active = string.Equals(p.Id, current, StringComparison.OrdinalIgnoreCase);
                var item = new MenuItem
                {
                    Header = (active ? "\u2713  " : "") + $"{p.Name}  \u00b7  {p.Blurb}",
                    FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                    Tag = p.Id,
                    Icon = FaceIcon(p),   // v3.0 — the persona's own face in the flyout
                };
                item.Click += OnPersonaMenuItemClick;
                menu.Items.Add(item);
            }

            var custom = PersonaStore.Current.All;
            if (custom.Count > 0)
            {
                menu.Items.Add(CaptionItem("YOUR PERSONAS"));
                foreach (var p in custom)
                {
                    bool active = string.Equals(p.Id, current, StringComparison.OrdinalIgnoreCase);
                    var item = new MenuItem
                    {
                        Header = (active ? "\u2713  " : "") + $"{p.Name}  \u00b7  {p.Blurb}",
                        FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                        Tag = p.Id,
                        Icon = FaceIcon(p),   // v3.0
                    };
                    item.Click += OnPersonaMenuItemClick;
                    menu.Items.Add(item);
                }
            }

            menu.Items.Add(new Separator());
            var manage = new MenuItem { Header = "Manage personas\u2026" };
            manage.Click += (_, _) =>
            {
                try { SettingsRequested?.Invoke(); }
                catch (Exception ex) { DiagnosticLogger.LogException("AiChat.ManagePersonas", ex); }
            };
            menu.Items.Add(manage);

            menu.PlacementTarget = PersonaChip;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.PersonaPick", ex); }
    }

    /// <summary>v3.0 — a tiny face for a persona flyout row (MenuItem.Icon takes any UIElement).</summary>
    private static System.Windows.Controls.Border FaceIcon(ChatPersona p) => new()
    {
        Width = 20,
        Height = 20,
        Child = new PersonaFaceView
        {
            FaceId = PersonaFaces.NormalizeId(p.Face),
            PersonaColor = PersonaFaces.NormalizeColor(p.Color),
        },
    };

    private void OnPersonaMenuItemClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem { Tag: string id } || id.Length == 0) return;
            _settings.AiPersona = id;      // new chats inherit the choice
            _settings.Save();
            if (_session is { } s) { s.Persona = id; _store.Upsert(s); }   // this chat switches live
            UpdatePersonaChip();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.PersonaSwitch", ex); }
    }

    // ------------------------------------------- v2.4.0-alpha.5 — history sidebar

    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_sidebarOpen) CloseSidebar();
            else OpenSidebar();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.HistoryToggle", ex); }
    }

    private void OnSidebarScrimClick(object sender, MouseButtonEventArgs e)
    {
        try { CloseSidebar(); }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.SidebarScrim", ex); }
    }

    private void OpenSidebar()
    {
        RebuildSidebar();
        SidebarOverlay.Visibility = Visibility.Visible;
        _sidebarOpen = true;

        // Raycast slide-over motion: the panel eases in from the left while the
        // scrim fades up. Skipped entirely when the user disabled animations.
        if (_settings.AnimationsEnabled)
        {
            try
            {
                var tt = new System.Windows.Media.TranslateTransform(-36, 0);
                SidebarPanel.RenderTransform = tt;
                var ease = new System.Windows.Media.Animation.CubicEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                var slide = new System.Windows.Media.Animation.DoubleAnimation(-36, 0, TimeSpan.FromMilliseconds(190))
                { EasingFunction = ease };
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140));
                tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
                SidebarScrim.BeginAnimation(OpacityProperty, fade);
            }
            catch { /* motion is cosmetic */ }
        }
    }

    private void CloseSidebar()
    {
        if (!_sidebarOpen) return;
        _sidebarOpen = false;
        SidebarOverlay.Visibility = Visibility.Collapsed;
        SidebarScrim.Opacity = 1;
    }

    private void RebuildSidebar()
    {
        try
        {
            SidebarHost.Children.Clear();
            var sessions = _store.Sessions;   // pinned first, then newest first
            bool anyPinned = sessions.Any(s => s.Pinned);
            bool captionPinned = false, captionRecent = false;
            foreach (var s in sessions)
            {
                // v2.4.0-alpha.6 — PINNED / RECENT section captions (Raycast pattern)
                if (anyPinned && s.Pinned && !captionPinned)
                {
                    SidebarHost.Children.Add(MakeSidebarCaption("PINNED"));
                    captionPinned = true;
                }
                if (anyPinned && !s.Pinned && !captionRecent)
                {
                    SidebarHost.Children.Add(MakeSidebarCaption("RECENT"));
                    captionRecent = true;
                }
                SidebarHost.Children.Add(BuildSessionRow(s));
            }
            SidebarFooter.Text = sessions.Count == 0
                ? "No chats yet — say something first"
                : $"{sessions.Count} chat{(sessions.Count == 1 ? "" : "s")} · stored on this PC";
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.SidebarBuild", ex); }
    }

    private static TextBlock MakeSidebarCaption(string text) => new()
    {
        Text = text,
        FontSize = 9.5,
        FontWeight = FontWeights.SemiBold,
        Opacity = 0.6,
        Margin = new Thickness(11, 8, 11, 3),
    };

    /// <summary>
    /// One session row in the launcher's raised-card language: quiet fill, the
    /// active chat wears the SelStroke ring, and hover fades in the curation
    /// actions — rename (inline), pin/unpin, delete. Pinned chats wear a small
    /// accent pin ahead of the title so they read pinned even at a glance.
    /// v2.4.0-alpha.6 — pinning + inline rename.
    /// </summary>
    private FrameworkElement BuildSessionRow(ChatSession s)
    {
        bool active = string.Equals(s.Id, _session?.Id, StringComparison.Ordinal);
        var border = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(11, 8, 5, 8),
            Margin = new Thickness(0, 1, 0, 1),
            Background = active ? (Brush)Resources["SelectedBrush"] : System.Windows.Media.Brushes.Transparent,
            BorderBrush = active ? (Brush)Resources["SelStrokeBrush"] : System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Tag = s.Id,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        if (s.Pinned)
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = "\uE840",                      // filled pin
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 9,
                Foreground = (Brush)Resources["AccentBrush"],
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            });
        }
        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(s.Title) ? "New chat" : s.Title,
            FontSize = 12.5,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)Resources["TitleBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleRow.Children.Add(title);

        // rename swaps this host's content for an inline edit box (and back)
        var titleHost = new ContentControl { Content = titleRow, IsTabStop = false, Focusable = false };
        stack.Children.Add(titleHost);

        int turns = s.Messages.Count(m => m.Role == "user");
        stack.Children.Add(new TextBlock
        {
            Text = $"{RelTime(s.UpdatedAt)} · {turns} turn{(turns == 1 ? "" : "s")}",
            FontSize = 10.5,
            Opacity = 0.85,
            Foreground = (Brush)Resources["SubtitleBrush"],
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(stack, 0);

        // curation actions, faded in on hover (pin stays visible while pinned)
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var ren = SidebarActionButton("\uE70F", "Rename");
        ren.Click += (_, _) =>
        {
            try { StartInlineRename(s, titleHost); }
            catch (Exception ex) { DiagnosticLogger.LogException("AiChat.SessionRename", ex); }
        };
        actions.Children.Add(ren);

        var pin = SidebarActionButton(s.Pinned ? "\uE77A" : "\uE718", s.Pinned ? "Unpin" : "Pin");
        if (s.Pinned) pin.Opacity = 0.85;
        pin.Tag = s.Id;
        pin.Click += OnPinSessionClick;
        actions.Children.Add(pin);

        var del = SidebarActionButton("\uE74D", "Delete chat");
        del.Tag = s.Id;
        del.Click += OnDeleteSessionClick;
        actions.Children.Add(del);
        Grid.SetColumn(actions, 1);

        grid.Children.Add(stack);
        grid.Children.Add(actions);
        border.Child = grid;

        border.MouseLeftButtonDown += OnSessionRowClick;
        border.MouseEnter += (_, _) =>
        {
            if (!active) border.Background = (Brush)Resources["HoverBrush"];
            foreach (var b in actions.Children.OfType<Button>())
                if (!(ReferenceEquals(b, pin) && s.Pinned)) b.Opacity = 0.9;
        };
        border.MouseLeave += (_, _) =>
        {
            if (!active) border.Background = System.Windows.Media.Brushes.Transparent;
            foreach (var b in actions.Children.OfType<Button>())
                if (!(ReferenceEquals(b, pin) && s.Pinned)) b.Opacity = 0;
        };
        return border;
    }

    private static Button SidebarActionButton(string glyph, string tooltip) => new()
    {
        Style = null,   // templated below — the caption style's FontSize setter would fight the glyph
        Content = glyph,
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        FontSize = 10,
        Opacity = 0,
        ToolTip = tooltip,
        Cursor = Cursors.Hand,
        Focusable = false,
        Padding = new Thickness(5, 3, 5, 3),
        VerticalAlignment = VerticalAlignment.Center,
        Background = System.Windows.Media.Brushes.Transparent,
        BorderThickness = new Thickness(0),
    };

    /// <summary>Pin/unpin — pinned chats float to the PINNED section (store sorts pinned-first).</summary>
    private void OnPinSessionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            e.Handled = true;
            if (sender is not Button { Tag: string id }) return;
            if (_store.Find(id) is not { } s) return;
            s.Pinned = !s.Pinned;
            _store.Upsert(s);
            RebuildSidebar();   // sidebar stays open for further curation
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.SessionPin", ex); }
    }

    /// <summary>
    /// v2.4.0-alpha.6 — inline rename: the row's title swaps for a borderless edit
    /// box. Enter or focus-loss commits (trimmed, ≤80 chars), Esc cancels; clicks
    /// inside the box never bubble to the row's switch handler.
    /// </summary>
    private void StartInlineRename(ChatSession s, ContentControl titleHost)
    {
        var box = new TextBox
        {
            Text = s.Title,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (Brush)Resources["SelStrokeBrush"],
            Foreground = (Brush)Resources["TitleBrush"],
            CaretBrush = (Brush)Resources["AccentBrush"],
            Padding = new Thickness(0, 0, 0, 1),
            MinWidth = 120,
            MaxLength = 120,
        };

        bool done = false;
        void Commit()
        {
            if (done) return;
            done = true;
            try
            {
                string t = box.Text.Trim();
                if (t.Length > 0 && !string.Equals(t, s.Title, StringComparison.Ordinal))
                {
                    s.Title = t.Length > 80 ? t[..80].TrimEnd() + "…" : t;
                    _store.Upsert(s);   // rename also refreshes UpdatedAt — recently curated floats up
                }
            }
            catch (Exception ex) { DiagnosticLogger.LogException("AiChat.RenameCommit", ex); }
            RebuildSidebar();
        }
        void Cancel()
        {
            if (done) return;
            done = true;
            RebuildSidebar();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Commit(); }
            else if (e.Key == Key.Escape) { e.Handled = true; Cancel(); }
        };
        box.LostFocus += (_, _) => Commit();
        // clicks inside the editor must not bubble to the row's switch handler
        // (the TextBox class-handles the click first — caret still lands — and
        // marking the bubbled event handled keeps the Border from acting on it)
        box.MouseLeftButtonDown += (_, e2) => e2.Handled = true;

        titleHost.Content = box;
        box.Focus();
        box.SelectAll();
    }

    private void OnSessionRowClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is not Border { Tag: string id }) return;
            SwitchSession(id);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.SessionClick", ex); }
    }

    private void OnDeleteSessionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            e.Handled = true;
            if (sender is not Button { Tag: string id }) return;
            _store.Delete(id);
            if (string.Equals(_session?.Id, id, StringComparison.Ordinal))
            {
                _session = null;
                ResetConversationUi();
                UpdatePersonaChip();
            }
            RebuildSidebar();   // sidebar stays open for further curation
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.SessionDelete", ex); }
    }

    /// <summary>Loads a stored conversation into the transcript (sidebar click / restore).</summary>
    private void SwitchSession(string id)
    {
        try
        {
            var s = _store.Find(id);
            if (s is null) return;
            CloseSidebar();
            if (_generating) _genCts?.Cancel();
            _session = s;
            ResetConversationUi();

            _restoring = true;
            try
            {
                // only the newest restored answer earns a Regenerate action —
                // older rows would pop the wrong turn out of the context
                int lastAssistantIdx = -1;
                for (int i = s.Messages.Count - 1; i >= 0; i--)
                {
                    if (s.Messages[i].Role == "assistant") { lastAssistantIdx = i; break; }
                }

                for (int i = 0; i < s.Messages.Count; i++)
                {
                    var m = s.Messages[i];
                    if (m.Role == "user")
                        AppendUserBubble(m.Content);
                    else if (m.Role == "assistant")
                    {
                        var (host, _) = AppendAssistantBubble();
                        // v2.6.0-alpha.5 — reasoning models persist raw <think> text;
                        // restored transcripts render it as the collapsible Reasoning
                        // block ahead of the visible answer.
                        var parts = ThinkSplit.Split(m.Content);
                        if (parts.HasReasoning) AddReasoningBlock(host, parts.Reasoning);
                        string stats = m.At.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
                        RenderMarkdownInto(host, parts.Answer, stats, allowRegenerate: i == lastAssistantIdx);
                    }
                }
            }
            finally { _restoring = false; }

            RebuildHistoryFromSession();
            UpdatePersonaChip();
            UpdateEmptyState();   // transcript may be non-empty — drop the welcome screen
            _atBottom = true;
            ChatScroll.ScrollToEnd();
            PromptBox.Focus();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.SessionSwitch", ex); }
    }

    /// <summary>
    /// Rebuilds the in-memory API context from the stored transcript, merging
    /// consecutive same-role messages (providers dislike back-to-back turns).
    /// </summary>
    private void RebuildHistoryFromSession()
    {
        _history.Clear();
        if (_session is null) return;
        foreach (var m in _session.Messages)
        {
            if (m.Role is not ("user" or "assistant")) continue;
            // v2.6.0-alpha.5 — reasoning text stays out of the API context: the
            // transcript keeps it for display, the request keeps only the answer.
            string content = m.Role == "assistant" ? ThinkSplit.Split(m.Content).Answer : m.Content;
            if (_history.Count > 0 && _history[^1].Role == m.Role)
                _history[^1] = new AiProviders.AiTurn(m.Role, _history[^1].Content + "\n\n" + content);
            else
                _history.Add(new AiProviders.AiTurn(m.Role, content));
        }
        if (_history.Count > HistoryCap)
            _history.RemoveRange(0, _history.Count - HistoryCap);
    }

    /// <summary>Compact relative age for sidebar rows: now · 5m · 2h · 3d · date.</summary>
    private static string RelTime(DateTime utc)
    {
        TimeSpan d = DateTime.UtcNow - utc.ToUniversalTime();
        if (d.TotalMinutes < 1) return "now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays}d";
        return utc.ToLocalTime().ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------- v2.4.0-alpha.4 — model picker

    private bool _pickerBusy;   // one probe at a time — a double-click can't stack requests

    /// <summary>
    /// The caption chip is a live model switcher: Ollama installs get a fresh
    /// /api/tags probe (served from the cached snapshot when it is still warm);
    /// Anthropic setups get the standard model ids plus a Settings shortcut.
    /// Picking a row rewrites settings.AiModel, PERSISTS it, and refreshes the chip.
    /// </summary>
    private void OnModelChipClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_pickerBusy) return;
            bool anthropic = AiProviders.IsAnthropic(_settings.AiStyle, _settings.AiEndpoint);
            if (anthropic)
            {
                OpenModelMenu(BuildAnthropicMenu());
                return;
            }

            var status = OllamaManager.Current;
            if (status is { Probed: true, ServerUp: true } && status.Models.Count > 0 && !status.Stale)
            {
                OpenModelMenu(BuildOllamaMenu(status.Models));
                return;
            }

            // cold or stale snapshot — probe on a worker, then open on the UI thread
            _pickerBusy = true;
            string endpoint = _settings.AiEndpoint;
            _ = Task.Run(async () =>
            {
                try
                {
                    var fresh = await OllamaManager.RefreshStatusAsync(endpoint).ConfigureAwait(true);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _pickerBusy = false;
                        if (IsLoaded)
                            OpenModelMenu(fresh.ServerUp && fresh.Models.Count > 0
                                ? BuildOllamaMenu(fresh.Models)
                                : BuildOfflineMenu());
                    });
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogException("AiChat.ModelProbe", ex);
                    await Dispatcher.InvokeAsync(() => { _pickerBusy = false; if (IsLoaded) OpenModelMenu(BuildOfflineMenu()); });
                }
            });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.ModelPick", ex); }
    }

    private ContextMenu BuildOllamaMenu(IReadOnlyList<OllamaManager.ModelInfo> models)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CaptionItem($"LOCAL MODELS · {models.Count}"));
        foreach (var m in models.OrderByDescending(x => x.Bytes))
        {
            bool active = string.Equals(m.Name, _settings.AiModel, StringComparison.OrdinalIgnoreCase);
            var item = new MenuItem
            {
                Header = (active ? "\u2713  " : "") + $"{m.Name}  \u00b7  {FormatGb(m.Bytes)}",
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                Tag = m.Name,
            };
            item.Click += OnModelMenuItemClick;
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(SettingsItem());
        return menu;
    }

    private ContextMenu BuildAnthropicMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CaptionItem("ANTHROPIC API"));
        foreach (string id in new[] { "claude-sonnet-4-5", "claude-haiku-4-5" })
        {
            bool active = string.Equals(id, _settings.AiModel, StringComparison.OrdinalIgnoreCase);
            var item = new MenuItem
            {
                Header = (active ? "\u2713  " : "") + id,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                Tag = id,
            };
            item.Click += OnModelMenuItemClick;
            menu.Items.Add(item);
        }
        if (_settings.AiModel.Length > 0 &&
            _settings.AiModel != "claude-sonnet-4-5" && _settings.AiModel != "claude-haiku-4-5")
            menu.Items.Add(CaptionItem($"custom · {_settings.AiModel}"));
        menu.Items.Add(new Separator());
        menu.Items.Add(SettingsItem());
        return menu;
    }

    private ContextMenu BuildOfflineMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CaptionItem("OLLAMA IS OFFLINE"));
        menu.Items.Add(CaptionItem("start it (or pull a model) from Settings → AI"));
        menu.Items.Add(new Separator());
        menu.Items.Add(SettingsItem());
        return menu;
    }

    private MenuItem CaptionItem(string text) => new()
    {
        Header = text,
        IsEnabled = false,
        FontSize = 10.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)Resources["SubtitleBrush"],
    };

    private MenuItem SettingsItem()
    {
        var item = new MenuItem { Header = "Manage models in Settings\u2026" };
        item.Click += (_, _) =>
        {
            try { SettingsRequested?.Invoke(); }
            catch (Exception ex) { DiagnosticLogger.LogException("AiChat.ModelSettings", ex); }
        };
        return item;
    }

    private void OnModelMenuItemClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem { Tag: string model } || model.Length == 0) return;
            _settings.AiModel = model;
            _settings.Save();          // v2.4.0-alpha.4 — the switch persists across restarts
            UpdateModelChip();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.ModelSwitch", ex); }
    }

    private void OpenModelMenu(ContextMenu menu)
    {
        try
        {
            menu.PlacementTarget = ModelChip;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.MenuOpen", ex); }
    }

    private static string FormatGb(long bytes) => bytes <= 0 ? "?" : $"{bytes / 1_000_000_000.0:0.#} GB";

    // ---------------------------------------------------------------- sending / streaming

    /// <summary>Entry point used by both the input box and the launcher's AI/query deep link.</summary>
    public void SendUserMessage(string text, AiProviders.ImagePayload? image = null)
    {
        try
        {
            text = (text ?? "").Trim();
            if (image is not null && text.Length == 0)
                text = "Describe this image.";   // vision turn with no caption — give the model something to answer
            if (text.Length == 0 || _generating) return;
            if (!_settings.AiEnabled) { UpdateBanner(); }   // the request will fail with the same message — surface it visibly

            EnsureSession(text);      // v2.4.0-alpha.5 — first message mints the session (title + persona)
            AppendUserBubble(text, image);
            UpdateEmptyState();
            BeginAssistantTurn(text, image);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Send", ex); }
    }

    /// <summary>
    /// v2.4.0-alpha.5 — guarantees an active session exists before a turn runs: the
    /// first user message mints one (title derived, persona inherited from settings)
    /// and the user turn is persisted immediately, so a crash mid-answer keeps text.
    /// </summary>
    private void EnsureSession(string firstUserMessage)
    {
        try
        {
            if (_session is not null) return;
            _session = new ChatSession
            {
                Title = ChatSession.DeriveTitle(firstUserMessage),
                Persona = _settings.AiPersona,
                CreatedAt = DateTime.UtcNow,
            };
            _session.Messages.Add(new ChatMessage("user", firstUserMessage, DateTime.UtcNow));
            _store.Upsert(_session);
            UpdatePersonaChip();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.EnsureSession", ex); }
    }

    /// <summary>
    /// v2.4.0-alpha.4 — the second half of a turn, split out so REGENERATE can
    /// re-run it for the previous prompt without re-appending the user bubble:
    /// builds the assistant placeholder, then starts the streamed request.
    /// </summary>
    private void BeginAssistantTurn(string prompt, AiProviders.ImagePayload? image = null)
    {
        Mascot.Mood = MascotView.Moods.Thinking;   // v3.0 — eyes wander while the reply streams
        try
        {
            // assistant placeholder: avatar + plain-text live line + typing dots
            var (host, live) = AppendAssistantBubble();
            _liveHost = host;
            _liveText = live;
            _turnImage = image;   // v2.6.0-alpha.5 — the image whose answer is streaming
            StartTypingDots(host);

            _streamBuf.Clear();
            _streamDirty = false;
            _shownLen = 0;
            _firstDeltaArrived = false;
            _generating = true;
            _turnStart.Restart();
            SendButton.Visibility = Visibility.Collapsed;
            StopButton.Visibility = Visibility.Visible;

            _genCts = new CancellationTokenSource();
            var ct = _genCts.Token;

            // v2.4.0-alpha.5 — the session's persona rides along as the system prompt
            // (v2.4.0-alpha.6 — custom personas resolve through PersonaStore first)
            string systemPrompt = ResolvePersona(_session?.Persona ?? _settings.AiPersona).Prompt;

            _flushTimer ??= CreateFlushTimer();
            _throttle.Restart();
            _flushTimer.Start();

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _ai.StreamChatAsync(_settings, _history, prompt,
                        delta => { lock (_streamBuf) { _streamBuf.Append(delta); _streamDirty = true; } },
                        ct, systemPrompt, image).ConfigureAwait(true);
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
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.BeginTurn", ex); }
    }

    private readonly Stopwatch _throttle = Stopwatch.StartNew();
    private readonly Stopwatch _turnStart = Stopwatch.StartNew();   // v2.4.0-alpha.4 — per-answer wall time

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

                // v2.6.0-alpha.5 — reasoning models inline their chain of thought in
                // <think>…</think>; the live view splits it into the collapsible
                // Reasoning panel and only types the visible answer.
                var parts = ThinkSplit.Split(partial);
                UpdateThinkPanel(partial, parts);

                string answer = parts.Answer;
                int gap = answer.Length - _shownLen;
                int step = gap > 400 ? 48 : gap > 120 ? 20 : 9;
                _shownLen = Math.Min(answer.Length, _shownLen + step);

                if (_liveText is { } live)
                {
                    live.Text = answer[.._shownLen];
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
        Mascot.Mood = MascotView.Moods.Idle;   // v3.0
        try
        {
            _flushTimer?.Stop();
            StopTypingDots();

            string typed;
            lock (_streamBuf) { typed = _streamBuf.ToString().Trim(); }

            if (result.Ok)
            {
                string full = typed.Length > 0 ? typed : result.Text;
                var parts = ThinkSplit.Split(full);   // v2.6.0-alpha.5 — keep reasoning out of the API history
                _history.Add(new AiProviders.AiTurn("user", _pendingUserPrompt, _turnImage));
                _history.Add(new AiProviders.AiTurn("assistant", parts.Answer));
                PersistAssistant(full, "");           // the raw text (think tags included) is what persists
                if (_liveHost is { } host)
                {
                    if (_liveText is { } lt) lt.Visibility = Visibility.Collapsed;
                    TeardownThinkCard();
                    // v2.4.0-alpha.4 — quiet per-answer stats: time · wall time · answer length
                    string stats = $"{DateTime.Now.ToString("t", CultureInfo.CurrentCulture)} \u00b7 " +
                                   $"{_turnStart.Elapsed.TotalSeconds:0.0}s \u00b7 {full.Length:N0} chars";
                    if (parts.HasReasoning) AddReasoningBlock(host, parts.Reasoning);
                    RenderMarkdownInto(host, parts.Answer, stats);
                }
            }
            else if (result.Cancelled)
            {
                string partial = typed.Length > 0 ? typed : result.Text;
                _history.Add(new AiProviders.AiTurn("user", _pendingUserPrompt, _turnImage));
                if (partial.Length > 0 && _liveHost is { } h)
                {
                    if (_liveText is { } lt) lt.Visibility = Visibility.Collapsed;
                    var stopped = ThinkSplit.Split(partial);
                    TeardownThinkCard();
                    if (stopped.HasReasoning) AddReasoningBlock(h, stopped.Reasoning);
                    RenderMarkdownInto(h, stopped.Answer);
                    AddFootnote(h, "stopped");
                    PersistAssistant(partial, " (stopped)");
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
                    var failed = ThinkSplit.Split(typed);
                    TeardownThinkCard();
                    if (failed.HasReasoning) AddReasoningBlock(h2, failed.Reasoning);
                    RenderMarkdownInto(h2, failed.Answer);
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
            _turnImage = null;
            _thinkCard = null;
            _thinkBody = null;
            _thinkHeader = null;
            _thinkChevron = null;
            SendButton.Visibility = Visibility.Visible;
            StopButton.Visibility = Visibility.Collapsed;
            UpdateSendEnabled();
            UpdateEmptyState();
            ScrollIfAtBottom();
        }
    }

    /// <summary>The prompt whose answer is streaming (for history pairing). UI-thread only.</summary>
    private string _pendingUserPrompt = "";

    /// <summary>
    /// v2.4.0-alpha.5 — appends the finished answer to the session transcript and
    /// persists it. <paramref name="suffix"/> marks a stop mid-stream so the restored
    /// transcript stays honest. Errors leave the user turn saved but no answer.
    /// </summary>
    private void PersistAssistant(string text, string suffix)
    {
        try
        {
            if (_session is not { } s || text.Length == 0) return;
            s.Messages.Add(new ChatMessage("assistant", text + suffix, DateTime.UtcNow));
            _store.Upsert(s);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Persist", ex); }
    }

    // ---------------------------------------------------------------- bubbles

    private void AppendUserBubble(string text, AiProviders.ImagePayload? image = null)
    {
        try
        {
            _pendingUserPrompt = text;   // the next assistant completion pairs with this
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // v2.6.0-alpha.8 — the ChatGPT bubble: a quiet elevated pill with a
            // UNIFORM radius (no sharp tail) and generous padding; the gray fill
            // separates the turn without shouting.
            var bubble = new Border
            {
                Background = (Brush)Resources["UserBubbleBrush"],
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(14, 9, 14, 10),
                MaxWidth = 580,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            // v2.6.0-alpha.5 — prompt-kit ImageAttachment: a rounded thumbnail rides
            // INSIDE the bubble, above the caption text (when there is one).
            StackPanel content = new() { Orientation = Orientation.Vertical };
            if (image is not null)
            {
                var thumb = MakeImageThumb(image, 132);
                thumb.Margin = new Thickness(0, 0, 0, text.Length > 0 ? 8 : 0);
                content.Children.Add(thumb);
            }
            if (text.Length > 0)
            {
                content.Children.Add(new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    LineHeight = 20,
                    Foreground = (Brush)Resources["UserBubbleTextBrush"],
                });
            }
            bubble.Child = content;
            Grid.SetColumn(bubble, 1);
            Grid.SetRow(bubble, 0);
            grid.Children.Add(bubble);

            // v2.6.0-alpha.5 — prompt-kit ChatBubbleTimestamp: a quiet clock under
            // the bubble, the color of a footnote.
            var stamp = new TextBlock
            {
                Text = DateTime.Now.ToString("t", CultureInfo.CurrentCulture),
                FontSize = 10,
                Opacity = 0.45,
                Foreground = (Brush)Resources["SubtitleBrush"],
                Margin = new Thickness(0, 3, 6, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            Grid.SetColumn(stamp, 1);
            Grid.SetRow(stamp, 1);
            grid.Children.Add(stamp);

            MessagesHost.Children.Add(grid);
            AnimateIn(grid);
            ScrollIfAtBottom();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.UserBubble", ex); }
    }

    /// <summary>v2.6.0-alpha.5 — decodes an attached image into a rounded, height-capped thumbnail.</summary>
    private static System.Windows.Controls.Image MakeImageThumb(AiProviders.ImagePayload image, double maxHeight)
    {
        var bmp = new BitmapImage();
        try
        {
            var bytes = Convert.FromBase64String(image.Base64);
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
        }
        catch { /* a broken image still shows the bubble text */ }

        return new System.Windows.Controls.Image
        {
            Source = bmp,
            MaxHeight = maxHeight,
            MaxWidth = 320,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    /// <summary>Assistant placeholder: gradient sparkle avatar + plain live text + (caller adds) typing dots.</summary>
    private (StackPanel Host, TextBlock Live) AppendAssistantBubble()
    {
        // v2.6.0-alpha.8 — ChatGPT rhythm: a 26 px avatar and a GENEROUS bottom
        // gap (22 px) so each turn reads as its own block on the page.
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 22) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // v2.3.0-alpha.4 — prompt-kit avatar: a gradient disc with a white sparkle
        // mark. v3.0 — the disc is gone: the avatar is the ACTIVE PERSONA'S FACE
        // (its own character + color), so every persona reads at a glance.
        var persona = ResolvePersona(_session?.Persona ?? _settings.AiPersona);
        var avatar = new PersonaFaceView
        {
            Width = 26,
            Height = 26,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 12, 0),
            FaceId = PersonaFaces.NormalizeId(persona.Face),
            PersonaColor = PersonaFaces.NormalizeColor(persona.Color),
        };
        Grid.SetColumn(avatar, 0);

        var host = new StackPanel();
        var live = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 21.5,
            Foreground = (Brush)Resources["TitleBrush"],
        };
        host.Children.Add(live);
        Grid.SetColumn(host, 1);

        grid.Children.Add(avatar);
        grid.Children.Add(host);
        MessagesHost.Children.Add(grid);
        AnimateIn(grid);
        ScrollIfAtBottom();
        _lastAssistantGrid = grid;   // v2.4.0-alpha.4 — regenerate swaps exactly this row
        return (host, live);
    }

    /// <summary>The wrapper grid of the most recent assistant bubble (regenerate target).</summary>
    private FrameworkElement? _lastAssistantGrid;

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
            // v2.3.0-alpha.4 — errors are an inline warning card with a small badge,
            // not a full avatar row: they read as a note about the answer, not a speaker.
            var grid = new Grid { Margin = new Thickness(38, 2, 0, 12) };   // clears the 28 px avatar column

            var card = new Border
            {
                Background = (Brush)Resources["WarnBrush"],
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0xCA, 0x50, 0x10)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 8, 12, 8),
                MaxWidth = 560,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var badge = new Border
            {
                Width = 16, Height = 16,
                CornerRadius = new CornerRadius(8),   // circle
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCA, 0x50, 0x10)),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 9, 0),
            };
            badge.Child = new TextBlock
            {
                Text = "!", FontSize = 10.5, FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(badge);
            row.Children.Add(new TextBlock
            {
                Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5,
                Foreground = (Brush)Resources["TitleBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            card.Child = row;
            grid.Children.Add(card);
            MessagesHost.Children.Add(grid);
            AnimateIn(grid);
            ScrollIfAtBottom();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.ErrorBubble", ex); }
    }

    // ---------------------------------------------------------------- typing dots

    private void StartTypingDots(StackPanel host)
    {
        try
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 5, 0, 2) };
            for (int i = 0; i < 3; i++)
            {
                var d = new System.Windows.Shapes.Ellipse
                {
                    Width = 6.5, Height = 6.5, Margin = new Thickness(0, 0, 5.5, 0),
                    Fill = (Brush)Resources["SubtitleBrush"],
                    Opacity = 0.35,
                };
                // v2.3.0-alpha.4 — a smooth staggered sine pulse (was: timer-stepped
                // opacity flips, which read as three LEDs blinking, not "thinking").
                if (_settings.AnimationsEnabled)
                {
                    var pulse = new System.Windows.Media.Animation.DoubleAnimation(0.3, 1.0, TimeSpan.FromMilliseconds(560))
                    {
                        AutoReverse = true,
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                        BeginTime = TimeSpan.FromMilliseconds(i * 170),
                        EasingFunction = new System.Windows.Media.Animation.SineEase
                        {
                            EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut,
                        },
                    };
                    d.BeginAnimation(OpacityProperty, pulse);
                }
                panel.Children.Add(d);
            }
            host.Children.Add(panel);
            _dotsPanel = panel;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.DotsStart", ex); }
    }

    private void StopTypingDots()
    {
        try
        {
            if (_dotsPanel is { } p)
            {
                foreach (var child in p.Children.OfType<System.Windows.Shapes.Ellipse>())
                    child.BeginAnimation(OpacityProperty, null);   // kill the pulse before teardown
                if (p.Parent is Panel host)
                    host.Children.Remove(p);
            }
            _dotsPanel = null;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.DotsStop", ex); }
    }

    // ------------------------------------------- v2.6.0-alpha.5 — prompt-kit Reasoning block

    /// <summary>
    /// The live reasoning panel while a reasoning model streams: a collapsible
    /// card above the answer that auto-EXPANDS while the model is inside
    /// &lt;think&gt;…&lt;/think&gt; and auto-collapses the moment the visible
    /// answer starts (prompt-kit's Reasoning, which stays open while thinking
    /// and folds away when the reply lands).
    /// </summary>
    private void UpdateThinkPanel(string raw, ThinkSplit.Parts parts)
    {
        try
        {
            if (!parts.HasReasoning)
            {
                TeardownThinkCard();
                return;
            }

            if (_thinkCard is null)
            {
                var (card, body, header, chevron) = BuildThinkCard();
                _thinkCard = card;
                _thinkBody = body;
                _thinkHeader = header;
                _thinkChevron = chevron;
                if (_liveHost is { } host && host.Children.Count > 0)
                    host.Children.Insert(0, card);   // the panel sits above the live answer line
                else if (_liveHost is { } h2)
                    h2.Children.Add(card);
            }

            // bound the layout: a 4k-token chain of thought must not grow the
            // window forever — show the tail (the part closest to the answer)
            _thinkBody!.Text = parts.Reasoning.Length > 3500
                ? "\u2026" + parts.Reasoning[^3500..]
                : parts.Reasoning;

            bool thinkingNow = ThinkSplit.IsThinking(raw);
            _thinkHeader!.Text = thinkingNow ? "Reasoning\u2026" : "Reasoning";
            SetThinkOpen(thinkingNow, animate: false);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.ThinkPanel", ex); }
    }

    /// <summary>Removes the live reasoning card from its host (turn finished — the final block replaces it).</summary>
    private void TeardownThinkCard()
    {
        try
        {
            if (_thinkCard?.Parent is Panel p)
                p.Children.Remove(_thinkCard);
            _thinkCard = null;
            _thinkBody = null;
            _thinkHeader = null;
            _thinkChevron = null;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.ThinkTeardown", ex); }
    }

    /// <summary>Builds the collapsible reasoning card: header button + chevron + muted body.</summary>
    private (Border Card, TextBlock Body, TextBlock Header, TextBlock Chevron) BuildThinkCard()
    {
        var header = new TextBlock
        {
            Text = "Reasoning\u2026",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["SubtitleBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        var chevron = new TextBlock
        {
            Text = "\uE70E",   // chevron down (open)
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 9.5,
            Foreground = (Brush)Resources["SubtitleBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        var headerRow = new Button
        {
            Style = null,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = false,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = MakeLeftRightRow(header, chevron),
        };
        headerRow.Click += (_, _) => SetThinkOpen(!_thinkOpen, animate: true);

        var body = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            FontStyle = FontStyles.Italic,
            LineHeight = 17,
            Foreground = (Brush)Resources["SubtitleBrush"],
        };
        var bodyHost = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 150,
            Margin = new Thickness(0, 7, 0, 0),
        };

        var stack = new StackPanel();
        stack.Children.Add(headerRow);
        stack.Children.Add(bodyHost);

        var card = new Border
        {
            Background = (Brush)Resources["CodeBrush"],
            BorderBrush = (Brush)Resources["BorderLineBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(11, 8, 11, 10),
            Margin = new Thickness(0, 2, 0, 8),
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = stack,
        };
        return (card, body, header, chevron);
    }

    /// <summary>Shows/hides the reasoning body; the chevron follows (down = open, right = folded).</summary>
    private void SetThinkOpen(bool open, bool animate)
    {
        _thinkOpen = open;
        try
        {
            if (_thinkBody?.Parent is ScrollViewer sv)
                sv.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (_thinkChevron is { } ch)
                ch.Text = open ? "\uE70E" : "\uE70D";
        }
        catch { }
    }

    /// <summary>The static reasoning block for a FINISHED answer (history + completed turns).</summary>
    private void AddReasoningBlock(StackPanel host, string reasoning)
    {
        try
        {
            var (card, body, header, chevron) = BuildThinkCard();
            bool longThink = reasoning.Length > 400;
            body.Text = reasoning.Length > 6000 ? "\u2026" + reasoning[^6000..] : reasoning;
            header.Text = $"Reasoning \u00b7 {(reasoning.Length / 5.0):0} words";
            chevron.Text = "\uE70D";   // folded by default once the answer is in
            if (body.Parent is ScrollViewer sv) sv.Visibility = Visibility.Collapsed;
            if (longThink) { /* already folded — the header words count is the hook */ }
            host.Children.Insert(0, card);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.ReasoningBlock", ex); }
    }

    /// <summary>One row with a left-aligned and a right-aligned child (the think header's layout).</summary>
    private static Grid MakeLeftRightRow(System.Windows.UIElement left, System.Windows.UIElement right)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        g.Children.Add(left);
        g.Children.Add(right);
        return g;
    }

    // ------------------------------------------- v2.6.0-alpha.5 — prompt-kit ImageAttachment

    private void OnAttachClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_generating || _voice.IsListening) return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Attach an image",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.gif|All files|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(this) is not true) return;
            var img = AiProviders.ImagePayload.Create(File.ReadAllBytes(dlg.FileName), GuessMediaType(dlg.FileName));
            if (img is null)
            {
                PromptPlaceholder.Text = "That image is too large (over 4 MB) or not a supported format.";
                return;
            }
            SetPendingImage(img);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Attach", ex); }
    }

    /// <summary>Ctrl+V with a screenshot on the clipboard — attach it instead of pasting text.</summary>
    private bool TryAttachFromClipboard()
    {
        try
        {
            if (_generating || !PromptBox.IsKeyboardFocusWithin) return false;
            if (!System.Windows.Clipboard.ContainsImage()) return false;
            var src = System.Windows.Clipboard.GetImage();
            if (src is null) return false;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            var img = AiProviders.ImagePayload.Create(ms.ToArray(), "image/png");
            if (img is null) return false;
            SetPendingImage(img);
            return true;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.AttachPaste", ex); return false; }
    }

    private static string GuessMediaType(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/png",
        };
    }

    /// <summary>Shows the attachment chip above the prompt (thumbnail + remove cap + size caption).</summary>
    private void SetPendingImage(AiProviders.ImagePayload img)
    {
        try
        {
            _pendingImage = img;
            AttachHost.Children.Clear();

            var chip = new Grid { Margin = new Thickness(0, 0, 8, 8) };
            var thumb = MakeImageThumb(img, 56);
            var thumbHost = new Border
            {
                Child = thumb,
                Width = 88,
                Height = 56,
                CornerRadius = new CornerRadius(9),
                Background = (Brush)Resources["CodeBrush"],
                BorderBrush = (Brush)Resources["BorderLineBrush"],
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
            };
            chip.Children.Add(thumbHost);

            // the remove cap, pinned to the chip's top-right corner
            var remove = new Button
            {
                Style = null,
                Content = "\uE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 8,
                Width = 18,
                Height = 18,
                Cursor = Cursors.Hand,
                Focusable = false,
                Background = (Brush)Resources["UserBubbleBrush"],
                Foreground = (Brush)Resources["UserBubbleTextBrush"],
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0),
                ToolTip = "Remove attachment",
            };
            remove.Click += (_, _) => ClearPendingImage();
            chip.Children.Add(remove);

            AttachHost.Children.Add(chip);
            AttachHost.Visibility = Visibility.Visible;
            UpdateSendEnabled();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.AttachSet", ex); }
    }

    private void ClearPendingImage()
    {
        try
        {
            _pendingImage = null;
            AttachHost.Children.Clear();
            AttachHost.Visibility = Visibility.Collapsed;
            UpdateSendEnabled();
        }
        catch { }
    }

    // ---------------------------------------------------------------- markdown rendering

    /// <summary>Turns markdown-lite blocks into visual children of the bubble host.
    /// <paramref name="stats"/> (v2.4.0-alpha.4) rides along to the actions row;
    /// <paramref name="allowRegenerate"/> (v2.4.0-alpha.5) is false for restored
    /// history rows — regenerating only makes sense on the newest answer.</summary>
    private void RenderMarkdownInto(StackPanel host, string markdown, string? stats = null, bool allowRegenerate = true)
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
                            FontSize = 14,
                            VerticalAlignment = VerticalAlignment.Top,
                        };
                        var content = new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 14,
                            LineHeight = 21.5,
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
                            FontSize = 14,
                            LineHeight = 21.5,
                            Foreground = (Brush)Resources["TitleBrush"],
                            Margin = new Thickness(0, 0, 0, 10),
                        };
                        AddInlineRuns(para, p2.Text);
                        host.Children.Add(para);
                        break;
                }
            }

            // per-answer footer: copy the whole answer + regenerate (prompt-kit's
            // message actions), plus the quiet timing/length stats
            host.Children.Add(BuildAnswerActionsRow(host, markdown, stats, allowRegenerate));
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
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 8, 12, 10),
            Margin = new Thickness(0, 4, 0, 10),
            MaxWidth = 680,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var stack = new StackPanel();

        // header: language label + line count + copy (prompt-kit CodeBlock)
        int lines = c.Text.Count(ch => ch == '\n') + (c.Text.Length > 0 && !c.Text.EndsWith('\n') ? 1 : 0);
        var header = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lang = new TextBlock
        {
            Text = (string.IsNullOrWhiteSpace(c.Lang) ? "code" : c.Lang) + (lines > 3 ? $" \u00b7 {lines} lines" : ""),
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

    /// <summary>
    /// v2.4.0-alpha.4 — the answer actions row: Copy answer · Regenerate, then the
    /// quiet stats caption ("2.4s · 812 chars"). Regenerate drops the trailing
    /// assistant turn (history + UI) and re-runs the last prompt.
    /// </summary>
    private FrameworkElement BuildAnswerActionsRow(StackPanel host, string fullText, string? stats, bool allowRegenerate = true)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 0, 2) };
        var copy = new Button { Style = (Style)FindResource("CaptionButton"), Content = "Copy answer", Tag = fullText };
        copy.Click += OnCopyTagClick;
        row.Children.Add(copy);

        if (allowRegenerate)
        {
            var regen = new Button { Style = (Style)FindResource("CaptionButton"), Content = "Regenerate" };
            regen.Click += (_, _) => OnRegenerateClick();
            row.Children.Add(regen);
        }

        if (!string.IsNullOrEmpty(stats))
            row.Children.Add(new TextBlock
            {
                Text = stats,
                FontSize = 10.5,
                Opacity = 0.75,
                Foreground = (Brush)Resources["SubtitleBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 1, 0, 0),
            });
        return row;
    }

    /// <summary>
    /// v2.4.0-alpha.4 — regenerate: pop the trailing assistant turn (history + UI)
    /// and re-run the last user prompt through the same streaming pipeline. The user
    /// bubble stays exactly where it is; only the answer is replaced.
    /// </summary>
    private void OnRegenerateClick()
    {
        try
        {
            if (_generating) return;
            string? lastUser = null;
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (_history[i].Role == "assistant") { _history.RemoveAt(i); continue; }
                if (_history[i].Role == "user") { lastUser = _history[i].Content; _history.RemoveAt(i); }
                break;
            }
            if (string.IsNullOrWhiteSpace(lastUser)) return;

            // v2.4.0-alpha.5 — the stored transcript pops its trailing answer too
            if (_session is { } s && s.Messages.Count > 0 && s.Messages[^1].Role == "assistant")
            {
                s.Messages.RemoveAt(s.Messages.Count - 1);
                _store.Upsert(s);
            }

            if (_lastAssistantGrid is { } g && MessagesHost.Children.Contains(g))
                MessagesHost.Children.Remove(g);
            _lastAssistantGrid = null;

            _pendingUserPrompt = lastUser;
            UpdateEmptyState();
            BeginAssistantTurn(lastUser);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("AiChat.Regenerate", ex); }
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
