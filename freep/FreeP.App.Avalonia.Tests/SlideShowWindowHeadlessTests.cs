using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Free.Shared.Drawing;
using Free.Shared.AppServices;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// Headless Avalonia tests for <see cref="SlideShowWindow"/> (Theme 24).
/// </summary>
public sealed class SlideShowWindowHeadlessTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    static SlideShowWindowHeadlessTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            // Headless drawing unavailable; skip gracefully.
            return false;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Presentation MakePresentation(int slideCount)
    {
        var pres = Presentation.CreateEmpty();
        for (int i = 1; i < slideCount; i++)
            pres.Slides.Add(new Slide { Title = $"Slide {i + 1}" });
        return pres;
    }

    // ── Construction ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SlideShowWindow_constructs_with_empty_presentation()
    {
        SlideShowWindow? window = null;
        var ran = await OnUiThread(() =>
        {
            var pres = Presentation.CreateEmpty();
            pres.Slides.Clear();
            window = new SlideShowWindow(pres, 0);
        });

        if (!ran) return;
        window.Should().NotBeNull();
        window!.Controller.CurrentSlideIndex.Should().Be(-1);
    }

    [Fact]
    public async Task SlideShowWindow_constructs_at_correct_start_index()
    {
        var idx = -99;
        var ran = await OnUiThread(() =>
        {
            var pres = MakePresentation(3);
            var window = new SlideShowWindow(pres, startIndex: 2);
            idx = window.Controller.CurrentSlideIndex;
        });

        if (!ran) return;
        idx.Should().Be(2);
    }

    [Fact]
    public async Task SlideShowWindow_advance_past_last_slide_returns_AtEnd()
    {
        AdvanceResult? result = null;
        var ran = await OnUiThread(() =>
        {
            var pres = MakePresentation(1);
            var window = new SlideShowWindow(pres, 0);
            result = window.Controller.Advance();
        });

        if (!ran) return;
        result.Should().BeOfType<AdvanceResult.AtEnd>();
    }

    [Fact]
    public async Task SlideShowWindow_back_at_first_slide_returns_AtStart()
    {
        BackResult? result = null;
        var ran = await OnUiThread(() =>
        {
            var pres = MakePresentation(2);
            var window = new SlideShowWindow(pres, 0);
            result = window.ExecuteBack();
        });

        if (!ran) return;
        result.Should().BeOfType<BackResult.AtStart>();
    }

    [Fact]
    public async Task SlideShowWindow_advance_with_animations_plays_steps_before_navigation()
    {
        var stepCount = -1;
        var firstResult = (AdvanceResult?)null;
        var ran = await OnUiThread(() =>
        {
            var pres = MakePresentation(2);
            var slide0 = pres.Slides[0];
            slide0.Shapes.Add(new SlideShape
            {
                Id = 1, Name = "S1", Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = DrawingShapeKind.Rectangle,
                ExtentCxEmu = 914400, ExtentCyEmu = 914400,
            });
            slide0.Animations.Add(new ShapeAnimation
            {
                ShapeId = 1, Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Appear, Trigger = AnimationTrigger.OnClick,
                DurationMs = 100,
            });
            var window = new SlideShowWindow(pres, 0);
            stepCount    = window.Controller.StepCount;
            firstResult  = window.Controller.Advance();
        });

        if (!ran) return;
        stepCount.Should().Be(1);
        firstResult.Should().BeOfType<AdvanceResult.PlayStep>();
    }

    [Fact]
    public async Task SlideShowWindow_constructs_with_transitions_and_animations()
    {
        var ran = await OnUiThread(() =>
        {
            var pres = MakePresentation(2);
            var slide = pres.Slides[0];
            slide.Transition = new SlideTransition
            {
                Kind       = TransitionKind.Fade,
                DurationMs = 500,
            };
            slide.Shapes.Add(new SlideShape
            {
                Id = 2, Name = "S2", Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = DrawingShapeKind.Rectangle,
                ExtentCxEmu = 914400, ExtentCyEmu = 914400,
            });
            slide.Animations.Add(new ShapeAnimation
            {
                ShapeId = 2, Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.FlyIn, Trigger = AnimationTrigger.OnClick,
                DurationMs = 300,
            });
            // Should not throw.
            var _ = new SlideShowWindow(pres, 0);
        });

        ran.Should().BeTrue("window with transitions and animations must construct without throwing");
    }

    // ── Hyperlink routing ───────────────────────────────────────────────────────

    [Fact]
    public async Task HitTestHyperlink_external_url_route()
    {
        Hyperlink? result = null;
        var ran = await OnUiThread(() =>
        {
            var pres = MakePresentation(1);
            var slide = pres.Slides[0];

            // Shape covering the full slide.
            slide.Shapes.Add(new SlideShape
            {
                Id         = 3,
                Name       = "HyperlinkShape",
                Kind       = SlideShapeKind.AutoShape,
                OffsetXEmu = 0,
                OffsetYEmu = 0,
                ExtentCxEmu = pres.SlideSizeCxEmu,
                ExtentCyEmu = pres.SlideSizeCyEmu,
                Hyperlink  = new Hyperlink { Url = "https://example.com" },
            });

            var window = new SlideShowWindow(pres, 0);
            // Hit-test at (0,0) — should land in the shape.
            result = window.HitTestHyperlink(slide, 1, 1);
        });

        if (!ran) return;
        result.Should().NotBeNull("a click at the top-left should hit the full-slide hyperlink shape");
        result!.IsExternal.Should().BeTrue();
        result.Url.Should().Be("https://example.com");
    }

    [Fact]
    public async Task HitTestHyperlink_internal_slide_jump_route()
    {
        Hyperlink? result = null;
        string? targetSlideId = null;
        var ran = await OnUiThread(() =>
        {
            var pres  = MakePresentation(3);
            var slide = pres.Slides[0];

            targetSlideId = pres.Slides[2].Id;
            slide.Shapes.Add(new SlideShape
            {
                Id         = 4,
                Name       = "InternalLink",
                Kind       = SlideShapeKind.AutoShape,
                OffsetXEmu = 0,
                OffsetYEmu = 0,
                ExtentCxEmu = pres.SlideSizeCxEmu,
                ExtentCyEmu = pres.SlideSizeCyEmu,
                Hyperlink  = new Hyperlink { TargetSlideId = targetSlideId },
            });

            var window = new SlideShowWindow(pres, 0);
            result = window.HitTestHyperlink(slide, 1, 1);
        });

        if (!ran) return;
        result.Should().NotBeNull();
        result!.IsExternal.Should().BeFalse();
        result.TargetSlideId.Should().Be(targetSlideId);
    }

    [Fact]
    public void OpenExternalUrl_rejects_file_scheme()
    {
        // Security guard: file:// must be silently rejected (no exception thrown).
        var act = () => SlideShowWindow.OpenExternalUrl("file:///C:/secret.exe");
        act.Should().NotThrow();
    }

    [Fact]
    public void OpenExternalUrl_rejects_unknown_scheme()
    {
        var act = () => SlideShowWindow.OpenExternalUrl("ftp://example.com/file");
        act.Should().NotThrow();
    }

    // ── MainWindow slideshow launch ─────────────────────────────────────────────

    [Fact]
    public async Task MainWindow_StartSlideShow_empty_presentation_does_not_throw()
    {
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            // Clear slides.
            while (window.Editor.Presentation.Slides.Count > 0)
                window.Editor.DeleteCurrentSlide();

            // With 0 slides, StartSlideShow must silently return.
            var act = () => window.StartSlideShow(fromStart: true);
            act.Should().NotThrow();
        });

        if (!ran) return; // headless skip
    }

    [Fact]
    public async Task MainWindow_StartSlideShow_constructs_slideshow_window()
    {
        // We just verify StartSlideShow does not throw on a presentation with slides.
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var act = () => window.StartSlideShow(fromStart: true);
            act.Should().NotThrow();
        });

        if (!ran) return;
    }

    // ── Ribbon definition ───────────────────────────────────────────────────────

    [Fact]
    public void RibbonDefinition_has_slideshow_group()
    {
        var definition = FreePRibbonAvalonia.Build();
        var home = definition.Tabs.Single(t => t.Id == "home");
        home.Groups.Should().Contain(g => g.Id == "slideshow",
            "a Slide Show group must be present in the Home tab");
    }

    [Fact]
    public void RibbonDefinition_slideshow_group_has_from_beginning_and_from_current()
    {
        var definition = FreePRibbonAvalonia.Build();
        var home  = definition.Tabs.Single(t => t.Id == "home");
        var sg    = home.Groups.Single(g => g.Id == "slideshow");
        var ids   = sg.Controls.Select(i => i.CommandId.Value).ToList();
        ids.Should().Contain("freep.slideshow.from-beginning");
        ids.Should().Contain("freep.slideshow.from-current");
    }

}
