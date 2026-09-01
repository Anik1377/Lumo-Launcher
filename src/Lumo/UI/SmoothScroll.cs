using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lumo.UI;

/// <summary>
/// v3.0 — inertia-style smooth wheel scrolling for any ScrollViewer.
///
/// WPF's default wheel step teleports the viewport in hard 48 px jumps. This
/// attached behavior intercepts PreviewMouseWheel, accumulates the deltas into a
/// target offset and glides there with exponential smoothing on the composition
/// clock — the way every modern surface scrolls. Keyboard, drag and programmatic
/// scrolling are untouched, so selection and focus behave exactly as before.
///
/// Usage: <c>behaviors:SmoothScroll.Enabled="True"</c> on a ScrollViewer.
/// Honors the master motion gate (Settings.AnimationsEnabled + the OS "animate
/// controls" preference) — when motion is off the wheel keeps its native step.
/// </summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SmoothScroll),
        new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

    /// <summary>
    /// The master motion gate, injected by the app (a Func so nothing here needs a
    /// Settings reference). Null = assume motion is allowed.
    /// </summary>
    public static Func<bool>? MotionAllowed { get; set; }

    private static bool MotionOk() => MotionAllowed?.Invoke() ?? true;

    /// <summary>Per-viewer glide state (target offset + live clock subscription).</summary>
    private sealed class Glide
    {
        public double Target;
        public DateTime LastTick = DateTime.UtcNow;
        public bool Subscribed;
    }

    private static readonly DependencyProperty GlideProperty = DependencyProperty.RegisterAttached(
        "Glide", typeof(Glide), typeof(SmoothScroll), new PropertyMetadata(null));

    // Smoothing factor per second — higher = snappier. ~12 reaches 95 % of a step in
    // roughly a quarter second, which reads as "gliding, not lagging".
    private const double GainPerSecond = 13.0;
    private const double SnapEpsilon = 0.4;

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;

        if ((bool)e.NewValue)
            sv.PreviewMouseWheel += OnPreviewMouseWheel;
        else
        {
            sv.PreviewMouseWheel -= OnPreviewMouseWheel;
            if (sv.GetValue(GlideProperty) is Glide g) Unsubscribe(sv, g);
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        if (sv.ScrollableHeight <= 0) return;

        if (!MotionOk()) return;   // unhandled → the native hard step

        e.Handled = true;

        var glide = (Glide?)sv.GetValue(GlideProperty);
        if (glide is null)
        {
            glide = new Glide { Target = sv.VerticalOffset };
            sv.SetValue(GlideProperty, glide);
        }

        // Accumulate onto the current TARGET (not the live offset) so a fast flick
        // compounds into one long glide instead of restarting mid-flight.
        glide.Target = Math.Clamp(glide.Target - e.Delta * 0.62, 0, sv.ScrollableHeight);
        glide.LastTick = DateTime.UtcNow;

        if (!glide.Subscribed)
        {
            glide.Subscribed = true;
            _active.Add((sv, glide));
            if (_active.Count == 1) CompositionTarget.Rendering += OnFrame;
        }
    }

    private static void OnFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        for (int i = _active.Count - 1; i >= 0; i--)   // backwards: entries may finish mid-loop
        {
            var (sv, glide) = _active[i];
            double dt = Math.Clamp((now - glide.LastTick).TotalSeconds, 0, 0.1);
            glide.LastTick = now;

            double current = sv.VerticalOffset;
            double diff = glide.Target - current;
            if (Math.Abs(diff) <= SnapEpsilon)
            {
                sv.ScrollToVerticalOffset(glide.Target);
                glide.Subscribed = false;
                _active.RemoveAt(i);
                continue;
            }

            double step = diff * Math.Min(1.0, GainPerSecond * dt);
            sv.ScrollToVerticalOffset(current + step);
        }
        if (_active.Count == 0) CompositionTarget.Rendering -= OnFrame;
    }

    // The Rendering event is global, so active glides are tracked in one static list.
    // (Viewer count is tiny — the settings panels, the chat log, the deck.)
    private static readonly List<(ScrollViewer Viewer, Glide State)> _active = new();

    private static void Unsubscribe(ScrollViewer sv, Glide glide)
    {
        glide.Subscribed = false;
        _active.RemoveAll(p => p.Viewer == sv && p.State == glide);
        if (_active.Count == 0) CompositionTarget.Rendering -= OnFrame;
    }
}
