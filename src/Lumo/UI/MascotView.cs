using Lumo.Core;
using Lumo.Services;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lumo.UI;

/// <summary>
/// v3.0 — the Lumo mascot: an animated persona face. Same body/eyes/mouth
/// vocabulary as <see cref="PersonaFaceView"/>, brought to life on a ~30 fps
/// timer that only runs while the element is visible:
///   · Idle      — a gentle bob, squash-and-stretch, and a blink every few seconds
///   · Listening — a slight eager lean, wider eyes (voice session live)
///   · Thinking  — the eyes wander off while a reply streams
///   · Speaking  — the mouth opens and closes with a talk cadence
/// Colors follow the persona (FaceId + PersonaColor); the timer dies with the
/// visibility and the master motion gate.
/// </summary>
public class MascotView : PersonaFaceView
{
    public enum Moods { Idle, Listening, Thinking, Speaking }

    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(Moods), typeof(MascotView),
        new FrameworkPropertyMetadata(Moods.Idle, FrameworkPropertyMetadataOptions.AffectsRender));

    public Moods Mood { get => (Moods)GetValue(MoodProperty); set => SetValue(MoodProperty, value); }

    /// <summary>The host injects the animations gate (Settings.AnimationsEnabled).</summary>
    public static Func<bool>? MotionAllowed { get; set; }

    private static bool MotionOk() => MotionAllowed?.Invoke() ?? true;

    private readonly DispatcherTimer _timer;
    private double _t;                       // seconds since mascot shown
    private double _nextBlink = 2.2;
    private readonly Random _rand = new();

    public MascotView()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue && MotionOk()) Start();
            else Stop();
        };
        Loaded += (_, _) => { if (IsVisible && MotionOk()) Start(); };
        Unloaded += (_, _) => Stop();
    }

    private void Start()
    {
        if (!_timer.IsEnabled)
        {
            _nextBlink = _t + 1.6 + _rand.NextDouble() * 2.4;
            _timer.Start();
        }
    }

    private void Stop() => _timer.Stop();

    private void Tick()
    {
        if (!MotionOk()) { Stop(); return; }
        _t += 0.033;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 4 || double.IsNaN(size)) return;

        var face = PersonaFaces.Resolve(FaceId);
        var tint = ResolveTint();

        double scale = size / 24.0;
        double ox = (ActualWidth - size) / 2.0, oy = (ActualHeight - size) / 2.0;

        // ----- animation state -------------------------------------------------
        double bob = Math.Sin(_t * 2 * Math.PI / 2.8) * 0.55;                    // ~2.8 s cycle
        double squash = 1 + Math.Sin(_t * 2 * Math.PI / 2.8) * 0.014;            // subtle breathe
        double lean = Mood == Moods.Listening ? Math.Sin(_t * 2 * Math.PI / 1.6) * 1.6 : 0;

        double blink = 1.0;
        if (_t >= _nextBlink)
        {
            double p = (_t - _nextBlink) / 0.17;                                 // 170 ms blink
            if (p >= 1) { _nextBlink = _t + 2.2 + _rand.NextDouble() * 3.2; }
            else blink = Math.Max(0.07, Math.Abs(Math.Cos(Math.PI * p)));
        }

        double open = Mood == Moods.Speaking ? 0.35 + 0.65 * Math.Abs(Math.Sin(_t * 2 * Math.PI / 0.55)) : 0;
        double gazeX = Mood == Moods.Thinking ? Math.Sin(_t * 2 * Math.PI / 2.2) * 0.9 : 0;
        double gazeY = Mood == Moods.Thinking ? -0.9 : 0;
        double eyeWide = Mood == Moods.Listening ? 1.12 : 1.0;

        // ----- compose ----------------------------------------------------------
        dc.PushTransform(new TranslateTransform(ox, oy));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.PushTransform(new TranslateTransform(0, bob));                        // the bob
        dc.PushTransform(new RotateTransform(lean, 12, 21));                     // eager lean
        dc.PushTransform(new ScaleTransform(2 - squash, squash, 12, 22));        // squash around the feet

        var body = Geo(face.Body);
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.15, 1) };
        fill.GradientStops.Add(new GradientStop(ThemeService.Lift(tint, 0.28), 0.0));
        fill.GradientStops.Add(new GradientStop(tint, 1.0));
        fill.Freeze();
        dc.DrawGeometry(fill, null, body);

        var ink = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x28));
        ink.Freeze();
        var pen = new Pen(ink, 1.9)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();

        // eyes: blink folds them vertically around the eye line; gaze nudges them
        dc.PushTransform(new TranslateTransform(gazeX, gazeY));
        dc.PushTransform(new ScaleTransform(eyeWide, 1.0, 12, 11.4));
        dc.PushTransform(new ScaleTransform(1.0, blink, 12, 11.4));
        dc.DrawGeometry(ink, null, Geo(EyesPath(face.Eyes)));
        dc.Pop();
        dc.Pop();
        dc.Pop();

        // mouth: static styles; speaking swaps in an animated "o"
        if (Mood == Moods.Speaking)
        {
            double ry = 0.9 + 1.4 * open;
            dc.DrawEllipse(ink, null, new Point(12, 15.2 + ry * 0.35), 1.35 + 0.35 * open, ry);
        }
        else
        {
            DrawMouth(dc, face.Mouth, ink);
        }

        // blush cheeks
        var blush = new SolidColorBrush(Color.FromArgb(0x5A, 0xFF, 0xFF, 0xFF));
        blush.Freeze();
        dc.DrawRoundedRectangle(blush, null, new Rect(4.6, 13.4, 2.8, 1.4), 0.7, 0.7);
        dc.DrawRoundedRectangle(blush, null, new Rect(16.6, 13.4, 2.8, 1.4), 0.7, 0.7);

        dc.Pop();   // squash
        dc.Pop();   // lean
        dc.Pop();   // bob
        dc.Pop();   // scale
        dc.Pop();   // translate
    }
}
