using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lumo.UI;

/// <summary>
/// v2.6.0-alpha.5 — the live microphone waveform for the recording overlay (the
/// "is it actually recording?" proof). A scrolling mirror of vertical bars: every
/// ~100 ms a new sample slides in on the right carrying the latest 0..1 mic
/// loudness (fed by WaveRecorder.LevelAvailable → the voice service's Level
/// event), older bars slide left and fade, and the newest bar eases toward its
/// target between samples so the motion reads fluid at 30 fps instead of
/// stepping at 10 Hz.
///
/// Everything is drawn in OnRender — no per-bar visuals, no bindings, one
/// InvalidateVisual per tick — and the timer runs ONLY while the overlay is
/// open, so an idle chat window pays nothing. Freeze() freezes the last frame
/// (that's the "Transcribing…" look); Start() resumes scrolling. The bar color
/// is a Color (not a Brush) on purpose: the alpha falloff is computed per bar,
/// which needs raw channels a gradient brush can't expose.
/// </summary>
public sealed class VoiceWaveform : FrameworkElement
{
    private const int BarCount = 56;
    private const int ScrollTicks = 3;          // 33 ms ticks per scroll ≈ 10 Hz scroll rate

    private readonly double[] _bars = new double[BarCount];
    private double _target;                     // latest pushed level
    private double _peak;                       // auto-gain reference (decays slowly)
    private int _tick;
    private DispatcherTimer? _timer;

    public static readonly DependencyProperty BarColorProperty = DependencyProperty.Register(
        nameof(BarColor), typeof(Color), typeof(VoiceWaveform),
        new FrameworkPropertyMetadata(Color.FromRgb(0x0A, 0x84, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The bar color — the window assigns the theme's accent (or the recording red).</summary>
    public Color BarColor
    {
        get => (Color)GetValue(BarColorProperty);
        set => SetValue(BarColorProperty, value);
    }

    public VoiceWaveform()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    /// <summary>Begins scrolling (bars keep their history when resuming after a freeze).</summary>
    public void Start()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>Stops the motion but keeps the last frame on screen.</summary>
    public void Freeze()
    {
        try { _timer?.Stop(); } catch { }
        _timer = null;
    }

    /// <summary>Stops and clears — bars fall back to the flat idle line.</summary>
    public void Reset()
    {
        Freeze();
        Array.Clear(_bars);
        _target = 0;
        _peak = 0;
        InvalidateVisual();
    }

    /// <summary>
    /// Feeds the next mic loudness reading (0..1, already normalized by VoiceAudio.RmsToLevel).
    /// v2.6.0-alpha.6 — the reading passes through auto-gain (a decaying peak
    /// reference) so quiet microphones still produce visible motion; silence
    /// stays flat.
    /// </summary>
    public void Push(double level)
    {
        _target = Core.VoiceAudio.AutoGain(Math.Clamp(level, 0, 1), ref _peak);
    }

    private void Tick()
    {
        if (++_tick % ScrollTicks == 0)
        {
            Array.Copy(_bars, 1, _bars, 0, BarCount - 1);   // slide left
            _bars[^1] = _target;
        }
        else
        {
            // ease the incoming bar toward its target between samples — the
            // 10 Hz capture becomes a smooth 30 fps ramp
            _bars[^1] += (_target - _bars[^1]) * 0.45;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double half = h / 2.0;
        double slot = w / BarCount;
        double barW = Math.Max(1.5, slot - 2.0);
        double maxHalf = h * 0.52;   // v2.6.0-alpha.6 — slightly taller bars now that auto-gain normalizes input
        Color c = BarColor;

        for (int i = 0; i < BarCount; i++)
        {
            double level = _bars[i];
            double barH = Math.Max(2.0, level * maxHalf);
            double x = i * slot + (slot - barW) / 2;

            // newest (right) bars burn brightest, history fades out — quadratic
            // so the tail stays quiet and the head pops
            double age = BarCount > 1 ? (double)i / (BarCount - 1) : 1;
            byte alpha = (byte)Math.Min(255, 46 + 200 * age * age);
            var fill = new SolidColorBrush(Color.FromArgb((byte)Math.Min(255, alpha + 30), c.R, c.G, c.B));
            fill.Freeze();

            var rect = new Rect(x, half - barH, barW, barH * 2);
            dc.DrawRoundedRectangle(fill, null, rect, barW / 2, barW / 2);
        }

        // the resting hairline the bars breathe around
        var line = new Pen(new SolidColorBrush(Color.FromArgb(38, 128, 128, 128)), 1);
        line.Freeze();
        dc.DrawLine(line, new Point(0, half), new Point(w, half));
    }
}
