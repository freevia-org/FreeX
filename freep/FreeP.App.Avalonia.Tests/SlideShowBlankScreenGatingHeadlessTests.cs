using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// Round 134 fix (Avalonia twin of <c>FreeP.App.Host.Tests.SlideShowBlankScreenGatingTests</c>):
/// with the audience screen blanked (B/W), keyboard and pointer input must not silently
/// advance the deck, fire an animation, or follow a hyperlink underneath the blank overlay
/// — only the B/W toggle and Escape may act. Also covers the sibling fix: an explicit
/// hyperlink to a HIDDEN slide must still navigate (revealed, like the H key), even though
/// normal Advance continues to skip hidden slides.
/// </summary>
public sealed class SlideShowBlankScreenGatingHeadlessTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    static SlideShowBlankScreenGatingHeadlessTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static Presentation MakePresentation(int slideCount)
    {
        var pres = Presentation.CreateEmpty();
        for (var i = 1; i < slideCount; i++)
            pres.Slides.Add(new Slide { Title = $"Slide {i + 1}" });
        return pres;
    }

    // ── (a) HIGH: blanked screen must gate navigation/activation input ────────────

    [Fact]
    public async Task AvaloniaHost_BlankScreen_ArrowKeyDoesNotAdvanceTheDeck()
    {
        SlideShowWindow? window = null;
        var ran = await OnUiThread(() =>
        {
            var presentation = MakePresentation(3);
            window = new SlideShowWindow(presentation, 0);
            window.Show();

            window.SetScreenMode(SlideShowScreenMode.Black);
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        });

        if (!ran) return;
        window!.Controller.CurrentSlideIndex.Should().Be(0);
        window.ScreenMode.Should().Be(SlideShowScreenMode.Black);
    }

    [Fact]
    public async Task AvaloniaHost_BlankScreen_ClickDoesNotAdvanceTheDeck()
    {
        SlideShowWindow? window = null;
        var ran = await OnUiThread(() =>
        {
            var presentation = MakePresentation(3);
            window = new SlideShowWindow(presentation, 0);
            window.Show();

            window.SetScreenMode(SlideShowScreenMode.White);
            window.MouseDown(new Point(10, 10), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(10, 10), MouseButton.Left, RawInputModifiers.None);
        });

        if (!ran) return;
        window!.Controller.CurrentSlideIndex.Should().Be(0);
    }

    [Fact]
    public async Task AvaloniaHost_BlankScreen_BKeyStillTogglesScreenModeBackToNormal()
    {
        SlideShowWindow? window = null;
        var ran = await OnUiThread(() =>
        {
            var presentation = MakePresentation(3);
            window = new SlideShowWindow(presentation, 0);
            window.Show();

            window.SetScreenMode(SlideShowScreenMode.Black);
            window.KeyPress(Key.B, RawInputModifiers.None, PhysicalKey.B, null);
        });

        if (!ran) return;
        window!.ScreenMode.Should().Be(SlideShowScreenMode.Normal);
    }

    [Fact]
    public async Task AvaloniaHost_BlankScreen_EscapeStillClosesTheShow()
    {
        SlideShowWindow? window = null;
        var closed = false;
        var ran = await OnUiThread(() =>
        {
            var presentation = MakePresentation(3);
            window = new SlideShowWindow(presentation, 0);
            window.Closed += (_, _) => closed = true;
            window.Show();

            window.SetScreenMode(SlideShowScreenMode.Black);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        });

        if (!ran) return;
        closed.Should().BeTrue();
    }

    // ── Sibling no-regression: unblanked playback keeps responding ────────────────

    [Fact]
    public async Task AvaloniaHost_NormalScreen_ArrowKeyStillAdvances()
    {
        SlideShowWindow? window = null;
        var ran = await OnUiThread(() =>
        {
            var presentation = MakePresentation(3);
            window = new SlideShowWindow(presentation, 0);
            window.Show();

            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        });

        if (!ran) return;
        window!.Controller.CurrentSlideIndex.Should().Be(1);
    }

    // ── (b) MED: hyperlink to a hidden slide must still navigate ──────────────────

    [Fact]
    public async Task AvaloniaHost_HyperlinkToHiddenSlide_RevealsItWithoutMovingThePlaybackIndex()
    {
        SlideShowWindow? window = null;
        Presentation? presentation = null;
        var ran = await OnUiThread(() =>
        {
            presentation = MakePresentation(3);
            presentation.Slides[1].IsHidden = true;
            window = new SlideShowWindow(presentation, 0);

            var hyperlink = new Hyperlink { TargetSlideId = presentation.Slides[1].Id };
            window.ActivateHyperlink(hyperlink);
        });

        if (!ran) return;
        window!.Controller.CurrentSlideIndex.Should().Be(0);
        window.RevealedHiddenSlideForTest.Should().BeSameAs(presentation!.Slides[1]);
    }

    [Fact]
    public async Task AvaloniaHost_HyperlinkToVisibleSlide_StillNavigatesNormally()
    {
        // Sibling no-regression: the ordinary (non-hidden) hyperlink jump path must be untouched.
        SlideShowWindow? window = null;
        var ran = await OnUiThread(() =>
        {
            var presentation = MakePresentation(3);
            window = new SlideShowWindow(presentation, 0);

            var hyperlink = new Hyperlink { TargetSlideId = presentation.Slides[2].Id };
            window.ActivateHyperlink(hyperlink);
        });

        if (!ran) return;
        window!.Controller.CurrentSlideIndex.Should().Be(2);
        window.RevealedHiddenSlideForTest.Should().BeNull();
    }
}
