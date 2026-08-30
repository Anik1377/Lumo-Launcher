using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Lumo.UI;

/// <summary>
/// Apple "Designing Fluid Interfaces" (WWDC 2018) §1 — Response: feedback lives on
/// the press, and it is instant. Highlight the moment the pointer goes DOWN, never
/// waiting for the release/click.
///
/// Attach to any FrameworkElement:
///     ui:PressFeedback.IsEnabled="True"
/// The element shrinks to 98.5 % the instant the pointer presses it (a 60 ms
/// ease-out, starting from the CURRENT value — never a hard jump), and springs
/// back over 120 ms on release / leave. The scale rides a ScaleTransform added to
/// the element's RenderTransform on demand, so it composes with any existing
/// translate/scale transforms (result rows use one for the staggered entrance).
/// </summary>
public static class PressFeedback
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(PressFeedback),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool v) => d.SetValue(IsEnabledProperty, v);

    /// <summary>Pressed scale — a 1.5 % dip: perceptible acknowledgment, not a bounce.</summary>
    private const double PressedScale = 0.985;

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if ((bool)e.NewValue)
        {
            // Preview* — act before anything else can consume the press.
            fe.PreviewMouseLeftButtonDown += OnDown;
            fe.PreviewMouseLeftButtonUp += OnUp;
            fe.MouseLeave += OnLeave;
        }
        else
        {
            fe.PreviewMouseLeftButtonDown -= OnDown;
            fe.PreviewMouseLeftButtonUp -= OnUp;
            fe.MouseLeave -= OnLeave;
        }
    }

    private static ScaleTransform ScaleOf(FrameworkElement fe)
    {
        // Already carrying a scale (previous press) — reuse it.
        if (fe.RenderTransform is TransformGroup g && !g.IsFrozen)
        {
            foreach (var t in g.Children)
                if (t is ScaleTransform s) return s;
            var added = new ScaleTransform(1, 1);
            g.Children.Add(added);
            return added;
        }
        if (fe.RenderTransform is ScaleTransform st && !st.IsFrozen) return st;

        // Wrap whatever transform is there (translate from the stagger, none, …)
        // into a group and append our scale. Never mutate frozen instances.
        var group = new TransformGroup();
        if (fe.RenderTransform is Transform existing && !existing.IsFrozen)
            group.Children.Add(existing);
        var scale = new ScaleTransform(1, 1);
        group.Children.Add(scale);
        fe.RenderTransform = group;
        return scale;
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        try
        {
            var s = ScaleOf(fe);
            // No From= — animate from the presentation value (§3 Interruptibility).
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var shrink = new DoubleAnimation(PressedScale, TimeSpan.FromMilliseconds(60)) { EasingFunction = ease };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, shrink.Clone());
        }
        catch { /* transform ownership edge cases must never break the click */ }
    }

    private static void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe) Restore(fe);
    }

    private static void OnLeave(object sender, MouseEventArgs e)
    {
        // Left while holding → release the pressed state; if the press continues
        // elsewhere the next Down re-applies it.
        if (sender is FrameworkElement fe && e.LeftButton == MouseButtonState.Pressed)
            Restore(fe);
    }

    private static void Restore(FrameworkElement fe)
    {
        try
        {
            if (fe.RenderTransform is not Transform { } t) return;
            ScaleTransform? s = t switch
            {
                ScaleTransform st2 => st2,
                TransformGroup tg => tg.Children.OfType<ScaleTransform>().FirstOrDefault(),
                _ => null,
            };
            if (s is null) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var grow = new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, grow.Clone());
        }
        catch { }
    }
}
