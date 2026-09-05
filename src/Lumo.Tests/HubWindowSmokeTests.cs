#if LUMO_TESTS_WPF
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Lumo.Services;
using Lumo.UI;
using Xunit;

namespace Lumo.Tests;

/// <summary>
/// v3.0.0-alpha.4 — WPF SMOKE TESTS: the harness builds the REAL windows on a
/// real Windows runner (the CI job), exactly the way App.OpenAiChat and
/// App.OpenDeck do. This exists because "AI page doesn't open" (v3.0.0-alpha.3
/// user report) was invisible to the pure-core suite: a XamlParseException or a
/// resource lookup failure inside window construction is a RUNTIME event on
/// Windows only — it compiles clean everywhere and CI never opened a window.
///
/// v3.0.0-alpha.5 — the App Deck moved OUT of the hub into its own window, so
/// the probe covers three things now:
///   · App boot with the same merged dictionaries as App.xaml (WPF-UI ThemesDictionary
///     + ControlsDictionary) — the layer every window's implicit styles come from;
///   · the full AiChatWindow constructor (XAML parse, ThemeService paint, ChatStore,
///     PersonaStore, mascot, persona face) + Show() + the deck launch button — the
///     hub is the AI chat only now, and must never fail to construct without the deck;
///   · the standalone AppDeckWindow constructor + Show() — the deck hosts the same
///     orphaned-constructed AppDeckView the hub used to (regression: the orphan
///     FindResource("AccentBrush") ResourceReferenceKeyNotFoundException that made
///     the deck tab dead in alpha.3);
///   · the AppDeckView still constructs orphaned and renders all nine cards;
///   · clean Close() on both windows.
///
/// Any failure fails the test with the FULL exception chain (type + message + stack
/// per inner level), so the log alone is enough to debug.
/// </summary>
public class HubWindowSmokeTests
{
    private static readonly object StaLock = new();
    private static Thread? _staThread;      // ONE STA thread for the whole suite
    private static Dispatcher? _staDispatcher;

    /// <summary>
    /// Boots the WPF Application ONCE, on a dedicated STA thread that stays alive
    /// (Dispatcher.Run) until the test process ends. The previous harness created
    /// the Application on each test's own STA thread — when that thread exited at
    /// the end of the first test, WPF shut the Application down with it, and every
    /// later LoadComponent died with "The Application object is being shut down"
    /// (v3.0.0-alpha.5 CI: exactly that, once a third test changed the execution
    /// order). Marshalling every body onto one long-lived dispatcher makes the
    /// order — and the timing — irrelevant.
    /// </summary>
    private static void EnsureSharedSta()
    {
        lock (StaLock)
        {
            if (_staThread is not null) return;
            var ready = new ManualResetEvent(false);
            _staThread = new Thread(() =>
            {
                try
                {
                    var app = new Application();   // one Application per AppDomain — created here, kept alive below
                    app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
                    app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                    _staDispatcher = Dispatcher.CurrentDispatcher;
                }
                finally { ready.Set(); }
                Dispatcher.Run();   // keeps the thread, the dispatcher AND the Application alive
            });
            _staThread.SetApartmentState(ApartmentState.STA);
            _staThread.IsBackground = true;   // never blocks test-process exit
            _staThread.Start();
            if (!ready.WaitOne(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("the shared STA dispatcher did not boot within 30 s");
        }
    }

    /// <summary>Runs body ON the shared STA dispatcher (windows must be created on
    /// their Application's thread) and waits for it. Nested PushFrame inside the
    /// body pumps layout/render/Loaded callbacks — WPF is idle without frames.</summary>
    private static Exception? RunOnSta(Action body)
    {
        EnsureSharedSta();
        Exception? caught = null;
        var op = _staDispatcher!.InvokeAsync(
            () => { try { body(); } catch (Exception ex) { caught = ex; } },
            DispatcherPriority.Normal);
        if (op.Wait(TimeSpan.FromSeconds(180)) != DispatcherOperationStatus.Completed && caught is null)
            caught = new TimeoutException("the shared STA dispatcher did not finish within 180 s");
        return caught;
    }

    private static void Pump(double ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(ms), DispatcherPriority.Background,
            (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static string Describe(Exception e)
    {
        var sb = new StringBuilder();
        for (int depth = 0; e is not null && depth < 8; depth++, e = e.InnerException!)
        {
            if (depth > 0) sb.AppendLine("   --- inner ---");
            sb.Append('[').Append(depth).Append("] ").Append(e.GetType().FullName)
              .Append(": ").AppendLine(e.Message);
            if (e.StackTrace is { } st) sb.AppendLine(st);
            if (e.InnerException is null) break;
        }
        return sb.ToString();
    }

    [Fact]
    public void HubWindow_constructs_shows_and_carries_the_deck_launch_button()
    {
        var failure = RunOnSta(() =>
        {
            EnsureSharedSta();
            SmoothScroll.MotionAllowed = () => false;    // deterministic: no motion under CI
            MascotView.MotionAllowed = () => false;

            // The exact constructor call App.OpenAiChat makes.
            var settings = new Settings();
            var win = new AiChatWindow(settings, new AiService());

            win.Show();
            Pump(400);   // Loaded handlers, layout, first render

            // v3.0.0-alpha.5 — the deck is no longer a tab; the rail must carry
            // the launch BUTTON that App wires to OpenDeck().
            var deckButton = (System.Windows.Controls.Button?)typeof(AiChatWindow)
                .GetField("RailDeckButton",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(win);
            Assert.NotNull(deckButton);

            // the hub must NOT still carry the old deck-tab plumbing
            Assert.Null(typeof(AiChatWindow).GetField("DeckHost",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));

            win.Close();
            Pump(120);
        });

        if (failure is not null)
            throw new Exception(
                "the AI hub window failed to construct/show on Windows — this is the same path App.OpenAiChat takes:\n" +
                Describe(failure), failure);
    }

    /// <summary>v3.0.0-alpha.5 — the App Deck window is built by App.OpenDeck(); its
    /// AppDeckView is constructed ORPHANED (before it joins the tree), so every
    /// theme token it asks for must survive orphan lookup.</summary>
    [Fact]
    public void AppDeckWindow_constructs_shows_and_hosts_the_deck_view()
    {
        var failure = RunOnSta(() =>
        {
            EnsureSharedSta();
            SmoothScroll.MotionAllowed = () => false;
            MascotView.MotionAllowed = () => false;

            var settings = new Settings();
            var win = new AppDeckWindow(settings);

            win.Show();
            Pump(400);

            var host = (System.Windows.Controls.ContentControl?)typeof(AppDeckWindow)
                .GetField("DeckHost",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(win);
            Assert.NotNull(host);
            Assert.IsType<AppDeckView>(host!.Content);

            win.Close();
            Pump(120);
        });

        if (failure is not null)
            throw new Exception("the standalone App Deck window failed to construct/show on Windows — " +
                                "this is the same path App.OpenDeck takes:\n" + Describe(failure), failure);
    }

    /// <summary>The App Deck view is built BEFORE it joins the window tree, so every
    /// theme token it asks for must survive orphan lookup (TryFindResource + the
    /// ThemeService-resolved palette — never a throwing FindResource).</summary>
    [Fact]
    public void AppDeckView_constructs_orphaned_and_renders_all_nine_cards()
    {
        var failure = RunOnSta(() =>
        {
            EnsureSharedSta();
            SmoothScroll.MotionAllowed = () => false;
            MascotView.MotionAllowed = () => false;

            var settings = new Settings();
            var view = new AppDeckView(settings);   // orphaned — no parent, exactly like AppDeckWindow builds it

            var grid = (System.Windows.Controls.Primitives.UniformGrid?)typeof(AppDeckView)
                .GetField("DeckGrid", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(view);
            Assert.NotNull(grid);
            Assert.Equal(9, grid!.Children.Count);
        });

        if (failure is not null)
            throw new Exception("the App Deck view failed to construct orphaned (theme tokens must not throw):\n" +
                                Describe(failure), failure);
    }
}
#endif
