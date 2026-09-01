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
/// v3.0.0-alpha.4 — WPF SMOKE TESTS: the harness builds the REAL hub window on a
/// real Windows runner (the CI job), exactly the way App.OpenAiChat does, and
/// switches both hub tabs. This exists because "AI page doesn't open" (v3.0.0-alpha.3
/// user report) was invisible to the pure-core suite: a XamlParseException or a
/// resource lookup failure inside AiChatWindow/AppDeckView construction is a RUNTIME
/// event on Windows only — it compiles clean everywhere and CI never opened a window.
///
/// What the probe covers, in one pass:
///   · App boot with the same merged dictionaries as App.xaml (WPF-UI ThemesDictionary
///     + ControlsDictionary) — the layer every window's implicit styles come from;
///   · the full AiChatWindow constructor (XAML parse, ThemeService paint, ChatStore,
///     PersonaStore, mascot, persona face);
///   · Show() — OnSourceInitialized → GlassBackdrop/DWM chrome;
///   · SwitchTab(true) — the App Deck view construction (regression: the orphan
///     FindResource("AccentBrush") ResourceReferenceKeyNotFoundException that made the
///     deck tab dead in alpha.3);
///   · SwitchTab(false) — back to the AI page;
///   · a clean Close().
///
/// Any failure fails the test with the FULL exception chain (type + message + stack
/// per inner level), so the log alone is enough to debug.
/// </summary>
public class HubWindowSmokeTests
{
    private static Application? _app;

    /// <summary>Boots the WPF Application once per process with App.xaml's layers.</summary>
    private static void BootApp()
    {
        if (_app is not null) return;
        var app = new Application();   // must be created on an STA thread — RunOnSta guarantees it
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        _app = app;
    }

    /// <summary>Runs body on a dedicated STA thread and pumps the dispatcher for
    /// layout/render/Loaded callbacks to actually fire (WPF is idle without frames).</summary>
    private static Exception? RunOnSta(Action body)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(180)) && caught is null)
            caught = new TimeoutException("the STA WPF thread did not finish within 180 s");
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
    public void HubWindow_constructs_shows_and_switches_both_tabs()
    {
        var failure = RunOnSta(() =>
        {
            BootApp();
            SmoothScroll.MotionAllowed = () => false;    // deterministic: no motion under CI
            MascotView.MotionAllowed = () => false;

            // The exact constructor call App.OpenAiChat makes.
            var settings = new Settings();
            var win = new AiChatWindow(settings, new AiService());

            win.Show();
            Pump(400);   // Loaded handlers, layout, first render

            // Switch to the App Deck tab the way the nav rail does (SwitchTab is
            // private; reflection keeps the production surface untouched).
            var switchTab = typeof(AiChatWindow).GetMethod("SwitchTab",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(switchTab);
            var deckHost = (System.Windows.Controls.Border?)typeof(AiChatWindow)
                .GetField("DeckHost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(win);
            Assert.NotNull(deckHost);

            switchTab!.Invoke(win, new object[] { true });
            Pump(400);
            Assert.True(deckHost!.Child is AppDeckView,
                $"DeckHost.Child should be AppDeckView but was {deckHost.Child?.GetType().FullName ?? "null"}");
            Assert.Equal(Visibility.Visible, deckHost.Visibility);

            // Back to the AI page — the report's headline path.
            switchTab.Invoke(win, new object[] { false });
            Pump(150);
            Assert.Equal(Visibility.Collapsed, deckHost.Visibility);

            win.Close();
            Pump(120);
        });

        if (failure is not null)
            throw new Exception(
                "the AI hub window failed to construct/show/switch tabs on Windows — " +
                "this is the same path App.OpenAiChat takes:\n" + Describe(failure), failure);
    }

    /// <summary>The App Deck view is built BEFORE it joins the window tree, so every
    /// theme token it asks for must survive orphan lookup (TryFindResource + the
    /// ThemeService-resolved palette — never a throwing FindResource).</summary>
    [Fact]
    public void AppDeckView_constructs_orphaned_and_renders_all_nine_cards()
    {
        var failure = RunOnSta(() =>
        {
            BootApp();
            SmoothScroll.MotionAllowed = () => false;
            MascotView.MotionAllowed = () => false;

            var settings = new Settings();
            var view = new AppDeckView(settings);   // orphaned — no parent, exactly like SwitchTab builds it

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
