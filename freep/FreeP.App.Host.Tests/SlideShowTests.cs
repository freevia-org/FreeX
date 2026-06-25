using System.Windows;
using FreeP.App.Host;
using Xunit;
using FluentAssertions;
using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 4B: SlideShowController (pure logic) + SlideShowWindow construction tests.
/// </summary>
public sealed class SlideShowControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static Presentation MakePresentation(int slideCount)
    {
        var pres = Presentation.CreateEmpty();
        for (int i = 1; i < slideCount; i++)
            pres.Slides.Add(new Slide { Title = $"Slide {i + 1}" });
        return pres;
    }

    private static Slide SlideWithAnimations(params (AnimationTrigger trigger, AnimationPreset preset)[] anims)
    {
        var slide = new Slide();
        uint id = 1;
        foreach (var (trigger, preset) in anims)
        {
            slide.Shapes.Add(new SlideShape
            {
                Id   = id,
                Name = $"Shape{id}",
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                ExtentCxEmu = 914400,
                ExtentCyEmu = 914400,
            });
            slide.Animations.Add(new ShapeAnimation
            {
                ShapeId   = id++,
                Kind      = AnimationKind.Entrance,
                Preset    = preset,
                Trigger   = trigger,
                DurationMs = 500,
            });
        }
        return slide;
    }

    // ── BuildSteps: grouping rules ────────────────────────────────────────────────

    [Fact]
    public void BuildSteps_EmptyAnimations_ReturnsNoSteps()
    {
        var slide = new Slide();
        var steps = SlideShowController.BuildSteps(slide);
        steps.Should().BeEmpty();
    }

    [Fact]
    public void BuildSteps_AllOnClick_EachBecomesOwnStep()
    {
        var slide = SlideWithAnimations(
            (AnimationTrigger.OnClick, AnimationPreset.Appear),
            (AnimationTrigger.OnClick, AnimationPreset.Fade),
            (AnimationTrigger.OnClick, AnimationPreset.FlyIn));

        var steps = SlideShowController.BuildSteps(slide);

        steps.Should().HaveCount(3);
        steps[0].Animations.Should().ContainSingle();
        steps[1].Animations.Should().ContainSingle();
        steps[2].Animations.Should().ContainSingle();
    }

    [Fact]
    public void BuildSteps_WithPreviousJoinsCurrentStep()
    {
        var slide = SlideWithAnimations(
            (AnimationTrigger.OnClick,       AnimationPreset.Appear),
            (AnimationTrigger.WithPrevious,  AnimationPreset.Fade),
            (AnimationTrigger.AfterPrevious, AnimationPreset.FlyIn));

        var steps = SlideShowController.BuildSteps(slide);

        steps.Should().HaveCount(1, "all 3 animations are in one click-step");
        steps[0].Animations.Should().HaveCount(3);
    }

    [Fact]
    public void BuildSteps_MixedTriggers_CorrectGrouping()
    {
        // Click1: OnClick + WithPrevious → step 1
        // Click2: OnClick                → step 2
        // Click3: OnClick + AfterPrevious + WithPrevious → step 3
        var slide = new Slide();
        uint id = 1;
        void Add(AnimationTrigger t) =>
            slide.Animations.Add(new ShapeAnimation
            {
                ShapeId = id++, Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Appear, Trigger = t, DurationMs = 500
            });

        Add(AnimationTrigger.OnClick);       // step 1
        Add(AnimationTrigger.WithPrevious);  // step 1
        Add(AnimationTrigger.OnClick);       // step 2
        Add(AnimationTrigger.OnClick);       // step 3
        Add(AnimationTrigger.AfterPrevious); // step 3
        Add(AnimationTrigger.WithPrevious);  // step 3

        var steps = SlideShowController.BuildSteps(slide);

        steps.Should().HaveCount(3);
        steps[0].Animations.Should().HaveCount(2);
        steps[1].Animations.Should().HaveCount(1);
        steps[2].Animations.Should().HaveCount(3);
    }

    [Fact]
    public void BuildSteps_StartsWithWithPrevious_TreatedAsFirstStep()
    {
        // Edge case: first animation is WithPrevious (no OnClick before it).
        var slide = new Slide();
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 1, Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade, Trigger = AnimationTrigger.WithPrevious
        });

        var steps = SlideShowController.BuildSteps(slide);

        steps.Should().HaveCount(1);
        steps[0].Animations.Should().HaveCount(1);
    }

    // ── Controller navigation ─────────────────────────────────────────────────────

    [Fact]
    public void Controller_StartIndex_IsRespected()
    {
        var pres = MakePresentation(3);
        var ctrl = new SlideShowController(pres.Slides, 2);
        ctrl.CurrentSlideIndex.Should().Be(2);
    }

    [Fact]
    public void Controller_StartIndex_ClampedToValidRange()
    {
        var pres = MakePresentation(3);
        var ctrlHigh = new SlideShowController(pres.Slides, 99);
        ctrlHigh.CurrentSlideIndex.Should().Be(2);

        var ctrlLow = new SlideShowController(pres.Slides, -5);
        ctrlLow.CurrentSlideIndex.Should().Be(0);
    }

    [Fact]
    public void Controller_NoSlides_IndexIsMinusOne()
    {
        var empty = new SlideShowController(Array.Empty<Slide>(), 0);
        empty.CurrentSlideIndex.Should().Be(-1);
        empty.CurrentSlide.Should().BeNull();
    }

    [Fact]
    public void Controller_Advance_NoAnimations_NavigatesToNextSlide()
    {
        var pres = MakePresentation(3);
        var ctrl = new SlideShowController(pres.Slides, 0);

        var result = ctrl.Advance();

        result.Should().BeOfType<AdvanceResult.NavigateToSlide>();
        ctrl.CurrentSlideIndex.Should().Be(1);
    }

    [Fact]
    public void Controller_Advance_WithAnimations_PlaysStepsFirst()
    {
        var pres = MakePresentation(2);
        pres.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 1, Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear, Trigger = AnimationTrigger.OnClick,
            DurationMs = 500
        });
        pres.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 2, Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade, Trigger = AnimationTrigger.OnClick,
            DurationMs = 500
        });

        var ctrl = new SlideShowController(pres.Slides, 0);
        ctrl.StepCount.Should().Be(2);

        // First advance plays step 1
        var r1 = ctrl.Advance();
        r1.Should().BeOfType<AdvanceResult.PlayStep>();
        ctrl.PendingStepIndex.Should().Be(1);
        ctrl.CurrentSlideIndex.Should().Be(0);

        // Second advance plays step 2
        var r2 = ctrl.Advance();
        r2.Should().BeOfType<AdvanceResult.PlayStep>();
        ctrl.PendingStepIndex.Should().Be(2);
        ctrl.CurrentSlideIndex.Should().Be(0);

        // Third advance navigates to next slide
        var r3 = ctrl.Advance();
        r3.Should().BeOfType<AdvanceResult.NavigateToSlide>();
        ctrl.CurrentSlideIndex.Should().Be(1);
    }

    [Fact]
    public void Controller_Advance_AtLastSlideNoSteps_ReturnsAtEnd()
    {
        var pres = MakePresentation(1);
        var ctrl = new SlideShowController(pres.Slides, 0);

        var result = ctrl.Advance();

        result.Should().BeOfType<AdvanceResult.AtEnd>();
        ctrl.CurrentSlideIndex.Should().Be(0);
    }

    [Fact]
    public void Controller_Back_NavigatesToPreviousSlide()
    {
        var pres = MakePresentation(3);
        var ctrl = new SlideShowController(pres.Slides, 2);

        var result = ctrl.Back();

        result.Should().BeOfType<BackResult.NavigateToSlide>()
            .Which.SlideIndex.Should().Be(1);
        ctrl.CurrentSlideIndex.Should().Be(1);
    }

    [Fact]
    public void Controller_Back_ResetsAnimationState()
    {
        var pres = MakePresentation(2);
        pres.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 1, Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear, Trigger = AnimationTrigger.OnClick,
            DurationMs = 500
        });

        var ctrl = new SlideShowController(pres.Slides, 0);
        ctrl.Advance(); // play step 1 on slide 0
        ctrl.PendingStepIndex.Should().Be(1);

        // Navigate forward to slide 1 then back to slide 0.
        ctrl.Advance(); // navigate to slide 1
        ctrl.Back();    // navigate back to slide 0

        // Animation state should be reset.
        ctrl.PendingStepIndex.Should().Be(0);
        ctrl.CurrentSlideIndex.Should().Be(0);
        ctrl.HasPendingSteps.Should().BeTrue();
    }

    [Fact]
    public void Controller_Back_AtFirstSlide_ReturnsAtStart()
    {
        var pres = MakePresentation(2);
        var ctrl = new SlideShowController(pres.Slides, 0);

        var result = ctrl.Back();

        result.Should().BeOfType<BackResult.AtStart>();
        ctrl.CurrentSlideIndex.Should().Be(0);
    }

    [Fact]
    public void Controller_GoToSlide_ResetsStepIndex()
    {
        var pres = MakePresentation(3);
        pres.Slides[1].Animations.Add(new ShapeAnimation
        {
            ShapeId = 1, Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear, Trigger = AnimationTrigger.OnClick
        });

        var ctrl = new SlideShowController(pres.Slides, 0);

        ctrl.GoToSlide(1);

        ctrl.CurrentSlideIndex.Should().Be(1);
        ctrl.PendingStepIndex.Should().Be(0);
        ctrl.StepCount.Should().Be(1);
    }

    [Fact]
    public void Controller_IsAtEnd_TrueWhenLastSlideExhausted()
    {
        var pres = MakePresentation(1);
        var ctrl = new SlideShowController(pres.Slides, 0);

        ctrl.IsAtEnd.Should().BeTrue();
    }

    [Fact]
    public void Controller_HasPendingSteps_FalseWhenNoAnimations()
    {
        var pres = MakePresentation(2);
        var ctrl = new SlideShowController(pres.Slides, 0);

        ctrl.HasPendingSteps.Should().BeFalse();
    }
}

/// <summary>
/// Wave 4B: SlideShowWindow STA construction tests.
/// These verify that the window can be created without throwing and that
/// it is in a sane initial state. We do NOT call Show() to avoid
/// blocking the test runner.
/// </summary>
public sealed class SlideShowWindowTests
{
    [StaFact]
    public void SlideShowWindow_ConstructsWithNoSlides_DoesNotThrow()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides.Clear();

        var window = new SlideShowWindow(pres, 0);
        try
        {
            window.Should().NotBeNull();
            window.Controller.CurrentSlideIndex.Should().Be(-1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_ConstructsWithSlides_AtRequestedIndex()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides.Add(new Slide { Title = "Slide 2" });
        pres.Slides.Add(new Slide { Title = "Slide 3" });

        var window = new SlideShowWindow(pres, startIndex: 2);
        try
        {
            window.Controller.CurrentSlideIndex.Should().Be(2);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_Advance_PastLastSlide_DoesNotThrow()
    {
        var pres = Presentation.CreateEmpty();
        var window = new SlideShowWindow(pres, 0);
        try
        {
            // Advancing past the last (and only) slide returns AtEnd without crashing.
            // The window would normally close itself; here we just verify it doesn't throw.
            window.Controller.Advance().Should().BeOfType<AdvanceResult.AtEnd>();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_ExecuteBack_AtFirstSlide_ReturnsAtStart()
    {
        var pres = Presentation.CreateEmpty();
        var window = new SlideShowWindow(pres, 0);
        try
        {
            var result = window.ExecuteBack();
            result.Should().BeOfType<BackResult.AtStart>();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_IsFullscreenBorderless()
    {
        var pres = Presentation.CreateEmpty();
        var window = new SlideShowWindow(pres, 0);
        try
        {
            window.WindowStyle.Should().Be(WindowStyle.None);
            window.WindowState.Should().Be(WindowState.Maximized);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_MultipleSlides_AdvanceNavigatesCorrectly()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides.Add(new Slide { Title = "S2" });
        pres.Slides.Add(new Slide { Title = "S3" });

        // Start at slide 0 (no animations → each advance goes to next slide)
        var window = new SlideShowWindow(pres, 0);
        try
        {
            window.Controller.CurrentSlideIndex.Should().Be(0);

            // Advance to slide 1
            var r1 = window.Controller.Advance();
            r1.Should().BeOfType<AdvanceResult.NavigateToSlide>()
                .Which.SlideIndex.Should().Be(1);

            // Advance to slide 2
            var r2 = window.Controller.Advance();
            r2.Should().BeOfType<AdvanceResult.NavigateToSlide>()
                .Which.SlideIndex.Should().Be(2);

            // Advance past end
            var r3 = window.Controller.Advance();
            r3.Should().BeOfType<AdvanceResult.AtEnd>();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_AnimationSteps_PlayBeforeSlideAdvance()
    {
        var pres = Presentation.CreateEmpty();
        var slide0 = pres.Slides[0];

        // Add 2 OnClick animations to slide 0
        slide0.Shapes.Add(new SlideShape
        {
            Id = 2, Name = "S2", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        slide0.Animations.Add(new ShapeAnimation
        {
            ShapeId = slide0.Shapes[0].Id, Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear, Trigger = AnimationTrigger.OnClick, DurationMs = 100
        });
        slide0.Animations.Add(new ShapeAnimation
        {
            ShapeId = 2, Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade, Trigger = AnimationTrigger.OnClick, DurationMs = 100
        });

        pres.Slides.Add(new Slide { Title = "Slide 2" });

        var window = new SlideShowWindow(pres, 0);
        try
        {
            window.Controller.StepCount.Should().Be(2);
            window.Controller.HasPendingSteps.Should().BeTrue();

            // First advance: play step 1 (still on slide 0)
            var r1 = window.Controller.Advance();
            r1.Should().BeOfType<AdvanceResult.PlayStep>();
            window.Controller.CurrentSlideIndex.Should().Be(0);

            // Second advance: play step 2 (still on slide 0)
            var r2 = window.Controller.Advance();
            r2.Should().BeOfType<AdvanceResult.PlayStep>();
            window.Controller.CurrentSlideIndex.Should().Be(0);

            // Third advance: navigate to slide 1
            var r3 = window.Controller.Advance();
            r3.Should().BeOfType<AdvanceResult.NavigateToSlide>()
                .Which.SlideIndex.Should().Be(1);
        }
        finally
        {
            window.Close();
        }
    }
}

/// <summary>
/// Wave 4B: MainWindow.StartSlideShow wiring tests.
/// Verifies that the internal StartSlideShow method exists and is accessible,
/// and that SlideShowWindow can be constructed from a MainWindow's presentation.
/// We do NOT call Show() (which would open a live window in a headless test runner);
/// instead we verify construction of SlideShowWindow directly with the same
/// presentation the MainWindow would pass.
/// </summary>
public sealed class SlideShowMainWindowTests
{
    [StaFact]
    public void MainWindow_HasStartSlideShow_Method()
    {
        // Verify the internal method exists (it will be called by the ribbon in 4C).
        var method = typeof(MainWindow).GetMethod(
            "StartSlideShow",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
            null,
            new[] { typeof(bool) },
            null);
        method.Should().NotBeNull("StartSlideShow(bool fromStart) must be discoverable by 4C ribbon wiring");
    }

    [StaFact]
    public void MainWindow_Presentation_CanConstructSlideShowWindow_FromStart()
    {
        // Verify SlideShowWindow can be constructed with the main window's presentation at index 0.
        var mainWindow = new MainWindow();
        try
        {
            var slideShow = new SlideShowWindow(mainWindow.Editor.Presentation, startIndex: 0);
            try
            {
                slideShow.Controller.CurrentSlideIndex.Should().Be(0);
            }
            finally
            {
                slideShow.Close();
            }
        }
        finally
        {
            mainWindow.Close();
        }
    }

    [StaFact]
    public void MainWindow_Presentation_CanConstructSlideShowWindow_FromCurrent()
    {
        var mainWindow = new MainWindow();
        try
        {
            mainWindow.Editor.InsertSlide();  // now on slide 1
            int currentIdx = mainWindow.Editor.CurrentSlideIndex;

            var slideShow = new SlideShowWindow(mainWindow.Editor.Presentation, startIndex: currentIdx);
            try
            {
                slideShow.Controller.CurrentSlideIndex.Should().Be(currentIdx);
            }
            finally
            {
                slideShow.Close();
            }
        }
        finally
        {
            mainWindow.Close();
        }
    }

    [StaFact]
    public void MainWindow_StartSlideShow_EmptyPresentation_DoesNotThrow()
    {
        var window = new MainWindow();
        try
        {
            // Clear all slides
            while (window.Editor.Presentation.Slides.Count > 0)
                window.Editor.DeleteCurrentSlide();

            // StartSlideShow with 0 slides should silently do nothing (early return guard).
            // We can call it safely because the guard fires before Show() is reached.
            var act = () => window.StartSlideShow(fromStart: true);
            act.Should().NotThrow();
        }
        finally
        {
            window.Close();
        }
    }
}
