using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lumo.Core;
using Lumo.Services;
using Appearance = Lumo.Services.Appearance;
using Color = System.Windows.Media.Color;

namespace Lumo.UI;

/// <summary>
/// First-run onboarding tour (v2.6 — DEV_PLAN Task 5.3): three quiet steps —
/// the hotkey, the prefixes, where data lives + how updates work — ending in a
/// "Get started". Shown once by App on the very first launch (Settings.FirstRunDone)
/// and replayable from Settings → About. Escape / Skip / the window's ✕ all count
/// as "seen" — the tour must never trap a user.
/// </summary>
public partial class OnboardingWindow : Window
{
    private readonly Settings _settings;
    private int _step;

    /// <summary>Raised when the tour ends for any reason (finished, skipped, closed).</summary>
    public event Action? Finished;

    public OnboardingWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;

        ApplySelfTheme();

        HotkeyChip.Text = string.IsNullOrWhiteSpace(settings.Hotkey) ? "Alt+Space" : settings.Hotkey;
        DataLine.Text = AppPaths.IsPortable
            ? "Portable mode — your data folder travels with the exe:"
            : "Data folder (settings, shortcuts, chats, plugins):";
        DataSubLine.Text = AppPaths.DataDir;

        ShowStep(0);
        PlayEntrance();
    }

    // ---------------------------------------------------------------- theming

    private void ApplySelfTheme()
    {
        try
        {
            // v3.0 — the shared ThemeService ladder (Onboarding used to carry its own
            // field/border constants that drifted from the family; now it can't).
            var t = ThemeService.Apply(this, _settings);
            Root.Background = new SolidColorBrush(t.Panel);
            Root.BorderBrush = new SolidColorBrush(t.Border);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Onboarding.Theme", ex); }
    }

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
        catch (Exception ex) { DiagnosticLogger.LogException("Onboarding.Entrance", ex); }
    }

    // ---------------------------------------------------------------- steps

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, 2);
        Step0.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        Step1.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;

        (Dot0.Fill, Dot1.Fill, Dot2.Fill) = _step switch
        {
            0 => ((Brush)FindResource("AccentBrush"), (Brush)FindResource("BorderLineBrush"), (Brush)FindResource("BorderLineBrush")),
            1 => ((Brush)FindResource("BorderLineBrush"), (Brush)FindResource("AccentBrush"), (Brush)FindResource("BorderLineBrush")),
            _ => ((Brush)FindResource("BorderLineBrush"), (Brush)FindResource("BorderLineBrush"), (Brush)FindResource("AccentBrush")),
        };

        BackButton.IsEnabled = _step > 0;
        NextButton.Content = _step == 2 ? "Get started" : "Next";
        SkipButton.Visibility = _step == 2 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnBack(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_step < 2) { ShowStep(_step + 1); return; }
        Close();   // finished — Closed raises Finished
    }

    private void OnSkip(object sender, RoutedEventArgs e) => Close();

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            else if (e.Key == Key.Enter) { e.Handled = true; OnNext(sender, e); }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Onboarding.Key", ex); }
    }

    protected override void OnClosed(EventArgs e)
    {
        try { Finished?.Invoke(); } catch (Exception ex) { DiagnosticLogger.LogException("Onboarding.Finished", ex); }
        base.OnClosed(e);
    }
}
