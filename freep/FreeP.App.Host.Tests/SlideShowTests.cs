using System.Windows;
using System.IO;
using System.Reflection;
using System.Text;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.App.Recording;
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

    [Fact]
    public void ParagraphBuildPlanner_CreatesTextOnlyParagraphOverlays()
    {
        var slide = new Slide
        {
            AnimationBuildListXml =
                "<p:bldLst xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">" +
                "<p:bldP spid=\"7\" grpId=\"0\" build=\"p\" /></p:bldLst>"
        };
        var shape = new SlideShape
        {
            Id = 7,
            Name = "Paragraphs",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x44, 0x72, 0xC4))),
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph { Runs = { new Run { Text = "First" } } },
                    new Paragraph { Runs = { new Run { Text = "Second" } } },
                }
            }
        };

        SlideShowAnimationBuildPlanner.IsParagraphBuild(slide, shape.Id).Should().BeTrue();
        var overlays = SlideShowAnimationBuildPlanner.CreateParagraphShapes(shape);
        overlays.Should().HaveCount(2);
        overlays[0].TextBody!.Paragraphs.Should().ContainSingle();
        overlays[0].PlainText.Should().Be("First");
        overlays[1].PlainText.Should().Be("Second");
        overlays[0].Fill.Should().BeNull();
        overlays[0].Outline.Should().BeNull();
        overlays[0].Effects.Should().BeNull();
    }

    [Fact]
    public void ParagraphBuildPlanner_TogglesOneShapeAndPreservesOtherBuildEntries()
    {
        var slide = new Slide
        {
            AnimationBuildListXml =
                "<p:bldLst xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">" +
                "<p:bldP spid=\"7\" grpId=\"0\" build=\"p\" />" +
                "<p:bldP spid=\"9\" grpId=\"2\" build=\"all\" />" +
                "</p:bldLst>"
        };
        slide.Shapes.Add(new SlideShape
        {
            Id = 9,
            TextBody = new TextBody
            {
                Paragraphs = { new Paragraph { Runs = { new Run { Text = "Build me" } } } }
            }
        });

        SlideShowAnimationBuildPlanner.TrySetParagraphBuild(slide, 9, true, out var updated)
            .Should().BeTrue();
        updated.Should().Contain("spid=\"7\"");
        updated.Should().Contain("grpId=\"2\"");
        updated.Should().Contain("spid=\"9\"");
        updated.Should().Contain("build=\"p\"");
    }

    [Fact]
    public void ParagraphBuildPlanner_DisablingBuildRemovesOnlyTargetEntry()
    {
        var slide = new Slide
        {
            AnimationBuildListXml =
                "<p:bldLst xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">" +
                "<p:bldP spid=\"7\" grpId=\"0\" build=\"p\" />" +
                "<p:bldP spid=\"9\" grpId=\"0\" build=\"p\" />" +
                "</p:bldLst>"
        };

        SlideShowAnimationBuildPlanner.TrySetParagraphBuild(slide, 7, false, out var updated)
            .Should().BeTrue();
        updated.Should().NotContain("spid=\"7\"");
        updated.Should().Contain("spid=\"9\"");
    }

    [Fact]
    public void SetSlideAnimationBuildListCommand_ApplyRevert()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].AnimationBuildListXml = "<p:bldLst />";
        var command = new SetSlideAnimationBuildListCommand(0, "<p:bldLst><p:bldP /></p:bldLst>");

        command.Apply(presentation);
        presentation.Slides[0].AnimationBuildListXml.Should().Contain("bldP");

        command.Revert(presentation);
        presentation.Slides[0].AnimationBuildListXml.Should().Be("<p:bldLst />");
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
    public void Controller_AnimationStartIndex_SkipsEarlierAnimationSteps()
    {
        var slide = SlideWithAnimations(
            (AnimationTrigger.OnClick, AnimationPreset.Appear),
            (AnimationTrigger.OnClick, AnimationPreset.Fade),
            (AnimationTrigger.OnClick, AnimationPreset.FlyIn));
        var ctrl = new SlideShowController(new[] { slide }, 0, animationStartIndex: 1);

        ctrl.CurrentSteps.Should().HaveCount(2);
        ctrl.CurrentSteps[0].Animations.Should().ContainSingle()
            .Which.ShapeId.Should().Be(2u);
        ctrl.PendingStepIndex.Should().Be(0);

        var first = ctrl.Advance().Should().BeOfType<AdvanceResult.PlayStep>().Subject;
        first.Step.Animations.Should().ContainSingle()
            .Which.ShapeId.Should().Be(2u);
    }

    [Fact]
    public void Controller_AnimationStartIndex_StartsSelectedTriggerSequence()
    {
        var slide = SlideWithAnimations(
            (AnimationTrigger.OnClick, AnimationPreset.Appear));
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 20,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.OnClick,
            TriggerShapeId = 99u,
            DurationMs = 500,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 21,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.FlyIn,
            Trigger = AnimationTrigger.WithPrevious,
            TriggerShapeId = 99u,
            DurationMs = 500,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 22,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Zoom,
            Trigger = AnimationTrigger.OnClick,
            TriggerShapeId = 99u,
            DurationMs = 500,
        });

        var ctrl = new SlideShowController(new[] { slide }, 0, animationStartIndex: 1);

        ctrl.CurrentSteps.Should().HaveCount(2);
        ctrl.CurrentSteps[0].Animations.Select(animation => animation.ShapeId)
            .Should().Equal(20u, 21u);
        ctrl.Advance().Should().BeOfType<AdvanceResult.PlayStep>();
        ctrl.Advance().Should().BeOfType<AdvanceResult.PlayStep>()
            .Which.Step.Animations.Should().ContainSingle()
            .Which.ShapeId.Should().Be(22u);
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
    public void Controller_Advance_ReturnsToZoomParentBeforeNextSlide()
    {
        var pres = MakePresentation(3);
        var ctrl = new SlideShowController(pres.Slides, 0);

        ctrl.EnterZoomNavigation(2, returnToParent: true);
        ctrl.CurrentSlideIndex.Should().Be(2);

        var result = ctrl.Advance();

        result.Should().BeOfType<AdvanceResult.NavigateToSlide>()
            .Which.SlideIndex.Should().Be(0);
        ctrl.CurrentSlideIndex.Should().Be(0);
        ctrl.HasZoomReturnPath.Should().BeFalse();
    }

    [Fact]
    public void Controller_Back_ReturnsToZoomParentBeforePreviousSlide()
    {
        var pres = MakePresentation(3);
        var ctrl = new SlideShowController(pres.Slides, 0);

        ctrl.EnterZoomNavigation(2, returnToParent: true);

        var result = ctrl.Back();

        result.Should().BeOfType<BackResult.NavigateToSlide>()
            .Which.SlideIndex.Should().Be(0);
        ctrl.HasZoomReturnPath.Should().BeFalse();
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
    public void SlideShowWindow_AnimationRoute_StartsAtSelectedAnimation()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 1,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = 500,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 2,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = 500,
        });
        var route = SlideShowCustomShowPlanner
            .BuildFullPresentationRoute(pres)
            .WithAnimationStartIndex(1);
        var window = new SlideShowWindow(pres, route);
        try
        {
            window.Controller.CurrentSteps[0].Animations.Should().ContainSingle()
                .Which.ShapeId.Should().Be(2u);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_MorphByWord_ExecutesTokenOverlayRoute()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides.Add(new Slide());
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 10,
            Name = "Revenue",
            TextBody = MakeTextBody("Revenue Q1"),
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 914400,
        });
        pres.Slides[1].Shapes.Add(new SlideShape
        {
            Id = 99,
            Name = "Revenue",
            TextBody = MakeTextBody("Revenue Q2"),
            OffsetXEmu = 1828800,
            OffsetYEmu = 1828800,
            ExtentCxEmu = 5486400,
            ExtentCyEmu = 914400,
        });
        pres.Slides[1].Transition = new SlideTransition
        {
            Kind = TransitionKind.Morph,
            MorphOption = "byWord",
            DurationMs = 16,
        };

        var window = new SlideShowWindow(pres, 0);
        try
        {
            window.ExecuteAdvance();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_CustomPlaybackRoute_PlaysOrderedSlides()
    {
        var pres = MakePresentation("Intro", "Deep dive", "Appendix");
        var route = SlideShowCustomShowPlanner.BuildCustomShowRoute(
            pres,
            new SlideShowCustomSlideSequence(
                "Executive review",
                new[] { pres.Slides[2].Id, pres.Slides[0].Id }),
            startIndex: 0);

        var window = new SlideShowWindow(
            pres,
            route,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("WPF slideshow"));
        try
        {
            window.PlaybackRoute.CustomShowName.Should().Be("Executive review");
            window.Controller.CurrentSlide!.Title.Should().Be("Appendix");
            window.Controller.CurrentSlideIndex.Should().Be(0);
            window.CurrentPresentationSlideIndex.Should().Be(2);

            var state = window.CreatePresenterState(window.PresenterStartedAtUtc);
            state.HostState.SlideCount.Should().Be(2);
            state.HostState.StatusText.Should().Be("Slide 1 of 2");
            state.CurrentSlide!.Title.Should().Be("Appendix");
            state.NextSlide!.Title.Should().Be("Intro");

            var advance = window.ExecuteAdvance();
            advance.Should().BeOfType<AdvanceResult.NavigateToSlide>()
                .Which.Slide.Title.Should().Be("Intro");
            window.Controller.CurrentSlideIndex.Should().Be(1);
            window.CurrentPresentationSlideIndex.Should().Be(0);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_CustomPlaybackRoute_RecordTimings_MapToSourceSlides()
    {
        var pres = MakePresentation("Intro", "Deep dive", "Appendix");
        var route = SlideShowCustomShowPlanner.BuildCustomShowRoute(
            pres,
            new SlideShowCustomSlideSequence(
                "Executive review",
                new[] { pres.Slides[2].Id, pres.Slides[0].Id }),
            startIndex: 0);
        var started = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);

        var window = new SlideShowWindow(pres, route);
        try
        {
            window.ApplyPresenterToolIntent(
                timingIntent: SlideShowTimingIntent.RecordTimings,
                nowUtc: started);

            window.ExecuteAdvance(started.AddMilliseconds(2500));
            window.ExecuteAdvance(started.AddMilliseconds(6000));

            pres.Slides[2].Transition!.AdvanceAfterMs.Should().Be(2500);
            pres.Slides[0].Transition!.AdvanceAfterMs.Should().Be(3500);
            pres.Slides[1].Transition.Should().BeNull();
        }
        finally
        {
            if (!window.IsPresenterSessionClosed)
            {
                window.Close();
            }
        }
    }

    [StaFact]
    public void SlideShowWindow_CustomPlaybackRoute_PersistsInkWithRouteMetadata()
    {
        var pres = MakePresentation("Intro", "Deep dive", "Appendix");
        pres.Slides[2].Id = "appendix-slide";
        var route = SlideShowCustomShowPlanner.BuildCustomShowRoute(
            pres,
            new SlideShowCustomSlideSequence(
                "Executive review",
                new[] { pres.Slides[2].Id, pres.Slides[0].Id }),
            startIndex: 0);

        var window = new SlideShowWindow(pres, route);

        window.ApplyPresenterToolIntent(
            pointerMode: SlideShowPresenterPointerMode.Pen,
            inkRetentionDecision: SlideShowInkRetentionDecision.KeepInk);
        window.BeginPresenterInkStroke(10, 20);
        window.EndPresenterInkStroke(30, 40);
        window.Close();

        var ink = pres.Slides[2].Shapes.Single(shape => shape.Kind == SlideShapeKind.Ink);
        var inkXml = Encoding.UTF8.GetString(ink.PreservedObject!.Parts.Single().Value);
        inkXml.Should().Contain("freep:sourceSlideId=\"appendix-slide\"");
        inkXml.Should().Contain("freep:customShowName=\"Executive review\"");
        inkXml.Should().Contain("freep:playbackSlideCount=\"2\"");
        inkXml.Should().Contain("freep:sourceSlideOccurrenceIndex=\"0\"");
        pres.Slides[0].Shapes.Should().NotContain(shape => shape.Kind == SlideShapeKind.Ink);
    }

    [StaFact]
    public void SlideShowWindow_RecordingReviewPlan_ProjectsSharedSourceSlideEvidence()
    {
        var pres = MakePresentation("Intro", "Deep dive", "Appendix");
        var route = SlideShowCustomShowPlanner.BuildCustomShowRoute(
            pres,
            new SlideShowCustomSlideSequence(
                "Executive review",
                new[] { pres.Slides[2].Id, pres.Slides[0].Id }),
            startIndex: 0);
        var started = new DateTimeOffset(2026, 7, 4, 11, 0, 0, TimeSpan.Zero);

        var window = new SlideShowWindow(pres, route, CreateDeferredWpfCaptureBackend());
        try
        {
            window.ApplyPresenterToolIntent(
                SlideShowTimingIntent.RecordTimings,
                SlideShowRecordingMediaIntent.NarrationAndMedia,
                nowUtc: started);

            window.ExecuteAdvance(started.AddMilliseconds(2400));

            var review = window.RecordingReviewPlan;

            review.HostName.Should().Be("WPF slideshow");
            review.CompletedSegmentCount.Should().Be(1);
            review.DeferredMediaArtifactCount.Should().Be(2);
            review.CanApplyRecordedTimings.Should().BeFalse("the host already applied the recorded timing");
            review.Rows.Should().ContainSingle().Which.Should().Match<SlideShowRecordingReviewRow>(row =>
                row.SlideIndex == 2 &&
                row.SlideTitle == "Appendix" &&
                row.DurationMs == 2400 &&
                row.TimingStatus == SlideShowRecordingReviewTimingStatus.AlreadyApplied);
            review.Rows.Single().MediaArtifacts.Select(artifact => artifact.SuggestedFileName)
                .Should().Equal("slide-003-narration.m4a", "slide-003-camera.mp4");

            window.Close();
            pres.RecordingMediaArtifacts.Should().BeEmpty(
                "the deferred WPF capture adapter must not persist fake recording artifacts");
        }
        finally
        {
            if (!window.IsPresenterSessionClosed)
            {
                window.Close();
            }
        }
    }

    [StaFact]
    public void SlideShowWindow_RecordingCaptureAdapterReadiness_ExposesWpfContract()
    {
        var pres = MakePresentation("Intro");
        var window = new SlideShowWindow(pres, 0);
        try
        {
            var readiness = window.RecordingCaptureAdapterReadiness;

            readiness.HostName.Should().Be("WPF slideshow");
            readiness.AdapterName.Should().Be("WPF Windows recording capture adapter");
            readiness.Devices.Should().OnlyContain(device =>
                device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone ||
                device.Kind == SlideShowRecordingCaptureDeviceKind.Camera);
            var hasAvailableMicrophone = readiness.Devices.Any(device =>
                device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone && device.IsAvailable);
            var hasAvailableCamera = readiness.Devices.Any(device =>
                device.Kind == SlideShowRecordingCaptureDeviceKind.Camera && device.IsAvailable);
            readiness.CanCaptureNarration.Should().Be(hasAvailableMicrophone);
            readiness.CanCaptureCamera.Should().Be(hasAvailableCamera);
            readiness.ReadyStreams.Contains(SlideShowRecordingCaptureStreamKind.NarrationAudio)
                .Should().Be(hasAvailableMicrophone);
            readiness.ReadyStreams.Contains(SlideShowRecordingCaptureStreamKind.CameraVideo)
                .Should().Be(hasAvailableCamera);
            readiness.MissingStreams.Contains(SlideShowRecordingCaptureStreamKind.NarrationAudio)
                .Should().Be(!hasAvailableMicrophone);
            readiness.MissingStreams.Contains(SlideShowRecordingCaptureStreamKind.CameraVideo)
                .Should().Be(!hasAvailableCamera);
            readiness.StatusText.Should().NotContain("Recording capture adapter is not registered");
            window.RecordingExecutionState.HostCapabilities.EffectiveCaptureAdapterReadiness
                .Should().BeSameAs(readiness);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_RecordingCaptureBackend_UsesInjectedWpfAdapter()
    {
        var pres = MakePresentation("Intro", "Next");
        var backend = new SlideShowDeterministicRecordingCaptureBackend(
            "WPF deterministic capture adapter",
            "ppt/media/freep-recordings/wpf");
        var started = new DateTimeOffset(2026, 7, 6, 9, 0, 0, TimeSpan.Zero);

        var window = new SlideShowWindow(pres, startIndex: 0, backend);
        try
        {
            window.RecordingCaptureAdapterReadiness.HostName.Should()
                .Be("WPF deterministic capture adapter");

            var plan = window.ApplyPresenterToolIntent(
                SlideShowTimingIntent.RecordTimings,
                SlideShowRecordingMediaIntent.NarrationAndMedia,
                nowUtc: started);
            plan.Recording.NarrationCapture.IsAvailable.Should().BeTrue();
            plan.Recording.MediaCapture.IsAvailable.Should().BeTrue();
            plan.Recording.NarrationCapture.IsDeferred.Should().BeFalse();
            plan.Recording.MediaCapture.IsDeferred.Should().BeFalse();
            window.ExecuteAdvance(started.AddMilliseconds(1800));

            var review = window.RecordingReviewPlan;

            review.HostName.Should().Be("WPF deterministic capture adapter");
            review.CapturedMediaArtifactCount.Should().Be(2);
            review.DeferredMediaArtifactCount.Should().Be(0);
            review.PersistableMediaArtifactCount.Should().Be(2);
            review.Rows.Single().MediaArtifacts.Should().OnlyContain(artifact =>
                artifact.IsCaptured &&
                !artifact.IsDeferred &&
                artifact.IsPersistable &&
                artifact.PackagePath.StartsWith("ppt/media/freep-recordings/wpf/", StringComparison.Ordinal));

            var applied = window.ApplyRecordingReview();
            applied.MediaArtifactCount.Should().Be(2);
            applied.CaptionArtifactCount.Should().Be(2);
            pres.RecordingMediaArtifacts.Should().HaveCount(4);

            window.ApplyPresenterToolIntent(nowUtc: started.AddMilliseconds(1800));
            window.Close();

            var mediaArtifacts = pres.RecordingMediaArtifacts
                .Where(artifact => artifact.Kind is
                    PresentationRecordingMediaArtifactKind.NarrationAudio or
                    PresentationRecordingMediaArtifactKind.CameraVideo)
                .ToArray();
            mediaArtifacts.Should().HaveCount(4);
            mediaArtifacts.Select(artifact => artifact.PackagePath)
                .Should().Contain("ppt/media/freep-recordings/wpf/slide-001-narration.m4a");
            mediaArtifacts.Select(artifact => artifact.PackagePath)
                .Should().Contain("ppt/media/freep-recordings/wpf/slide-001-camera.mp4");
        }
        finally
        {
            if (!window.IsPresenterSessionClosed)
            {
                window.Close();
            }
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
    public void SlideShowWindow_CreatePresenterState_UsesSharedPlannerState()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Title = "Agenda";
        pres.Slides[0].Notes = MakeTextBody("speaker note");
        pres.Slides.Add(new Slide { Title = "Details" });

        var window = new SlideShowWindow(pres, 0);
        try
        {
            var displayIntent = new SlideShowPresenterDisplayIntent(
                IsFullScreenRequested: true,
                MonitorIndex: 2,
                MonitorName: "Confidence monitor");

            var state = window.CreatePresenterState(
                window.PresenterStartedAtUtc.AddSeconds(12),
                displayIntent);

            state.CurrentSlide!.SlideIndex.Should().Be(0);
            state.NextSlide!.Title.Should().Be("Details");
            state.NotesText.Should().Be("speaker note");
            state.Elapsed.Should().Be(TimeSpan.FromSeconds(12));
            state.DisplayIntent.Should().BeSameAs(displayIntent);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_ApplyPresenterToolIntent_UsesSharedPlannerState()
    {
        var pres = Presentation.CreateEmpty();
        var window = new SlideShowWindow(
            pres,
            0,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("WPF slideshow"));
        try
        {
            var plan = window.ApplyPresenterToolIntent(
                SlideShowTimingIntent.RecordTimings,
                SlideShowRecordingMediaIntent.NarrationAndMedia,
                SlideShowPresenterPointerMode.Pen,
                "#336699",
                5,
                SlideShowInkRetentionDecision.ClearInk);

            plan.Should().BeSameAs(window.PresenterToolPlan);
            plan.Recording.NarrationCapture.IsDeferred.Should().BeTrue();
            plan.Recording.MediaCapture.IsDeferred.Should().BeTrue();
            plan.PointerInk.PointerMode.Should().Be(SlideShowPresenterPointerMode.Pen);
            plan.PointerInk.InkState.ColorHex.Should().Be("#336699");
            plan.PointerInk.InkRetentionDecision.Should().Be(SlideShowInkRetentionDecision.ClearInk);
            window.PresenterWorkflowActions.Should().BeSameAs(plan.WorkflowActions);
            window.PresenterWorkflowActions.Should().Contain(action =>
                action.Kind == SlideShowPresenterWorkflowActionKind.RequestNarrationCapture &&
                action.IsDeferred);
            window.PresenterWorkflowActions.Should().Contain(action =>
                action.Kind == SlideShowPresenterWorkflowActionKind.ConfigureInkStroke);
            window.PresenterWorkflowActions.Should().Contain(action =>
                action.Kind == SlideShowPresenterWorkflowActionKind.ClearInkOnExit);
            window.PresenterCommandStates.Should().BeSameAs(plan.CommandStates);
            window.PresenterCommandStates.Where(command => command.IsChecked).Select(command => command.CommandId)
                .Should().Equal(
                    SlideShowPresenterToolPlanner.RecordTimingsCommandId,
                    SlideShowPresenterToolPlanner.NarrationAndMediaCommandId,
                    SlideShowPresenterToolPlanner.PenPointerCommandId,
                    SlideShowPresenterToolPlanner.ClearInkCommandId);
            window.RecordingExecutionState.IsSessionActive.Should().BeTrue();
            window.RecordingExecutionState.CurrentSlideIndex.Should().Be(0);
            window.RecordingExecutionState.IsNarrationCaptureActive.Should().BeFalse();
            window.RecordingExecutionState.IsCameraCaptureActive.Should().BeFalse();
            window.RecordingExecutionActions.Where(action => action.IsDeferred)
                .Select(action => action.Kind)
                .Should().Equal(
                    SlideShowRecordingExecutionActionKind.CaptureUnavailable,
                    SlideShowRecordingExecutionActionKind.CaptureUnavailable);
            window.RecordingExecutionActions.Where(action => action.IsDeferred)
                .Should().OnlyContain(action => action.StatusText.Contains("WPF slideshow"));

            var state = window.CreatePresenterState(window.PresenterStartedAtUtc.AddSeconds(3));
            state.ToolPlan.Should().BeSameAs(plan);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_SetPresenterMediaIntent_PreservesTimingAndPointerState()
    {
        var window = new SlideShowWindow(
            Presentation.CreateEmpty(),
            0,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("WPF slideshow"));
        try
        {
            window.ApplyPresenterToolIntent(
                SlideShowTimingIntent.RecordTimings,
                SlideShowRecordingMediaIntent.None,
                SlideShowPresenterPointerMode.Pen);

            var plan = window.SetPresenterMediaIntent(SlideShowRecordingMediaIntent.NarrationAndMedia);

            plan.Recording.TimingIntent.Should().Be(SlideShowTimingIntent.RecordTimings);
            plan.Recording.MediaIntent.Should().Be(SlideShowRecordingMediaIntent.NarrationAndMedia);
            plan.PointerInk.PointerMode.Should().Be(SlideShowPresenterPointerMode.Pen);
            plan.Recording.NarrationCapture.IsDeferred.Should().BeTrue();
            plan.Recording.MediaCapture.IsDeferred.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_RecordTimings_PersistsAdvanceAfterOnNavigationAndClose()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition { Kind = TransitionKind.Fade, DurationMs = 700 };
        pres.Slides.Add(new Slide { Title = "Second" });
        var started = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);
        var window = new SlideShowWindow(pres, 0);
        try
        {
            window.ApplyPresenterToolIntent(
                timingIntent: SlideShowTimingIntent.RecordTimings,
                nowUtc: started);

            var navigate = window.ExecuteAdvance(started.AddMilliseconds(2500));
            var close = window.ExecuteAdvance(started.AddMilliseconds(6000));

            navigate.Should().BeOfType<AdvanceResult.NavigateToSlide>();
            close.Should().BeOfType<AdvanceResult.AtEnd>();
            window.IsPresenterSessionClosed.Should().BeTrue();
            var firstTransition = pres.Slides[0].Transition;
            var secondTransition = pres.Slides[1].Transition;
            firstTransition.Should().NotBeNull();
            secondTransition.Should().NotBeNull();
            firstTransition!.Kind.Should().Be(TransitionKind.Fade);
            firstTransition.DurationMs.Should().Be(700);
            firstTransition.AdvanceAfterMs.Should().Be(2500);
            secondTransition!.AdvanceAfterMs.Should().Be(3500);
        }
        finally
        {
            if (!window.IsPresenterSessionClosed)
            {
                window.Close();
            }
        }
    }

    [StaFact]
    public void SlideShowWindow_RehearseTimings_TracksWithoutPersistingTransitionTiming()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides.Add(new Slide { Title = "Second" });
        var started = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);
        var window = new SlideShowWindow(pres, 0);
        try
        {
            window.ApplyPresenterToolIntent(
                timingIntent: SlideShowTimingIntent.RehearseTimings,
                nowUtc: started);

            window.ExecuteAdvance(started.AddMilliseconds(1800));

            window.TimingRecorderState.RecordedTimings.Should().ContainSingle();
            window.TimingRecorderState.RecordedTimings[0].AdvanceAfterMs.Should().Be(1800);
            window.TimingRecorderState.RecordedTimings[0].ShouldPersist.Should().BeFalse();
            pres.Slides[0].Transition.Should().BeNull();
        }
        finally
        {
            if (!window.IsPresenterSessionClosed)
            {
                window.Close();
            }
        }
    }

    [StaFact]
    public void SlideShowWindow_InkExecution_DelegatesStrokeLifecycleToSharedPlanner()
    {
        var pres = Presentation.CreateEmpty();
        var window = new SlideShowWindow(pres, 0);
        try
        {
            window.ApplyPresenterToolIntent(
                pointerMode: SlideShowPresenterPointerMode.Pen,
                inkColorHex: "#336699",
                inkThicknessDip: 5);

            var begin = window.BeginPresenterInkStroke(10, 20);
            var append = window.AppendPresenterInkStroke(30, 40);
            var end = window.EndPresenterInkStroke(50, 60);

            begin.IsHandled.Should().BeTrue();
            window.PresenterInkOverlayVisualCount.Should().Be(1);
            begin.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.BeginStroke);
            append.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.AppendStrokePoint);
            end.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.CommitStroke);
            window.PresenterInkOverlayVisualCount.Should().Be(1);
            window.InkExecutionState.CommittedStrokes.Should().ContainSingle();
            var stroke = window.InkExecutionState.CommittedStrokes.Single();
            stroke.PointerMode.Should().Be(SlideShowPresenterPointerMode.Pen);
            stroke.InkState.ColorHex.Should().Be("#336699");
            stroke.Points.Should().Equal(
                new SlideShowInkPoint(10, 20),
                new SlideShowInkPoint(30, 40),
                new SlideShowInkPoint(50, 60));
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_PresenterSessionSummary_CombinesRecordingAndInkEvidence()
    {
        var started = new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero);
        var pres = Presentation.CreateEmpty();
        pres.Slides.Add(new Slide { Title = "Second" });
        var window = new SlideShowWindow(pres, 0, CreateDeferredWpfCaptureBackend());
        try
        {
            window.ApplyPresenterToolIntent(
                SlideShowTimingIntent.RecordTimings,
                SlideShowRecordingMediaIntent.NarrationAndMedia,
                SlideShowPresenterPointerMode.Pen,
                "#336699",
                5,
                SlideShowInkRetentionDecision.KeepInk,
                started);
            window.BeginPresenterInkStroke(10, 20);
            window.EndPresenterInkStroke(30, 40);
            window.ExecuteAdvance(nowUtc: started.AddMilliseconds(1600));

            var summary = window.PresenterSessionSummary;

            summary.HostName.Should().Be("WPF slideshow");
            summary.Recording.CompletedSegmentCount.Should().Be(1);
            summary.Recording.TotalRecordedDurationMs.Should().Be(1600);
            summary.Recording.DeferredMediaArtifactCount.Should().Be(2);
            summary.Ink.GeneratedInkSlideCount.Should().Be(1);
            summary.Ink.GeneratedInkStrokeCount.Should().Be(1);
            summary.Ink.WillPersistInkOnExit.Should().BeTrue();
            summary.EvidenceLines.Should().Contain(line => line.Contains("WPF slideshow"));
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_InkClear_UsesSharedClearPlan()
    {
        var pres = Presentation.CreateEmpty();
        var window = new SlideShowWindow(pres, 0);
        try
        {
            window.ApplyPresenterToolIntent(pointerMode: SlideShowPresenterPointerMode.Highlighter);
            window.BeginPresenterInkStroke(10, 20);
            window.EndPresenterInkStroke(30, 40);

            var clear = window.ClearPresenterInkStrokes();

            clear.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.ClearInk);
            clear.Mutations.Single().AffectedStrokeCount.Should().Be(1);
            window.InkExecutionState.CommittedStrokes.Should().BeEmpty();
            window.PresenterInkOverlayVisualCount.Should().Be(0);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_InkUndo_UsesSharedUndoPlan()
    {
        var pres = Presentation.CreateEmpty();
        var window = new SlideShowWindow(pres, 0);
        try
        {
            window.ApplyPresenterToolIntent(pointerMode: SlideShowPresenterPointerMode.Pen);
            window.BeginPresenterInkStroke(10, 20);
            window.EndPresenterInkStroke(30, 40);
            window.BeginPresenterInkStroke(50, 60);
            window.EndPresenterInkStroke(70, 80);

            var undo = window.UndoLastPresenterInkStroke();

            undo.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.UndoLastStroke);
            undo.Mutations.Single().AffectedStrokeCount.Should().Be(1);
            window.InkExecutionState.CommittedStrokes.Should().ContainSingle();
            window.InkExecutionState.CommittedStrokes.Single().Points.Should().Equal(
                new SlideShowInkPoint(10, 20),
                new SlideShowInkPoint(30, 40));
            window.PresenterInkOverlayVisualCount.Should().Be(1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_NavigationCommitsActivePresenterInkThroughSharedPlanner()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides.Add(new Slide { Title = "Second" });
        var window = new SlideShowWindow(pres, 0);
        try
        {
            window.ApplyPresenterToolIntent(
                pointerMode: SlideShowPresenterPointerMode.Pen,
                inkColorHex: "#336699",
                inkThicknessDip: 5);

            window.BeginPresenterInkStroke(10, 20);
            window.AppendPresenterInkStroke(30, 40);
            window.ExecuteAdvance();

            window.Controller.CurrentSlideIndex.Should().Be(1);
            window.InkExecutionState.ActiveStroke.Should().BeNull();
            window.InkExecutionState.CommittedStrokes.Should().ContainSingle();
            var stroke = window.InkExecutionState.CommittedStrokes.Single();
            stroke.SlideIndex.Should().Be(0);
            stroke.Points.Should().Equal(
                new SlideShowInkPoint(10, 20),
                new SlideShowInkPoint(30, 40));
            window.PresenterInkOverlayVisualCount.Should().Be(0);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlideShowWindow_CloseWithKeepInk_PersistsInkThroughSharedPlanner()
    {
        var pres = Presentation.CreateEmpty();
        var window = new SlideShowWindow(pres, 0);
        try
        {
            window.ApplyPresenterToolIntent(
                pointerMode: SlideShowPresenterPointerMode.Pen,
                inkColorHex: "#336699",
                inkThicknessDip: 5,
                inkRetentionDecision: SlideShowInkRetentionDecision.KeepInk);
            window.BeginPresenterInkStroke(10, 20);
            window.EndPresenterInkStroke(30, 40);

            window.ExecuteAdvance();

            window.IsPresenterSessionClosed.Should().BeTrue();
            var ink = pres.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Ink);
            ink.PreservedObject.Should().NotBeNull();
            ink.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Ink);
            Encoding.UTF8.GetString(ink.PreservedObject.Parts.Values.Single())
                .Should().Contain("10,20 30,40");
        }
        finally
        {
            if (!window.IsPresenterSessionClosed)
            {
                window.Close();
            }
        }
    }

    [StaFact]
    public void SlideShowWindow_CloseWithClearInk_DoesNotPersistGeneratedInk()
    {
        var pres = Presentation.CreateEmpty();
        var window = new SlideShowWindow(pres, 0);
        try
        {
            window.ApplyPresenterToolIntent(
                pointerMode: SlideShowPresenterPointerMode.Pen,
                inkRetentionDecision: SlideShowInkRetentionDecision.ClearInk);
            window.BeginPresenterInkStroke(10, 20);
            window.EndPresenterInkStroke(30, 40);

            window.ExecuteAdvance();

            window.IsPresenterSessionClosed.Should().BeTrue();
            pres.Slides[0].Shapes.Should().NotContain(shape => shape.Kind == SlideShapeKind.Ink);
        }
        finally
        {
            if (!window.IsPresenterSessionClosed)
            {
                window.Close();
            }
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

    private static TextBody MakeTextBody(string text)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static Presentation MakePresentation(params string[] titles)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        foreach (var title in titles)
        {
            presentation.Slides.Add(new Slide { Title = title });
        }

        return presentation;
    }

    private static ISlideShowRecordingCaptureBackend CreateDeferredWpfCaptureBackend() =>
        new WindowsRecordingCaptureBackend(
            new WindowsRecordingHostMetadata(
                "WPF slideshow",
                "WPF Windows recording capture adapter",
                "ppt/media/freep-recordings/wpf"),
            new EmptyWindowsRecordingDeviceCatalog(),
            new DeferredWindowsRecordingCaptureEngine());

    private sealed class EmptyWindowsRecordingDeviceCatalog : IWindowsRecordingDeviceCatalog
    {
        public IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices() =>
            Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>();
    }

    private sealed class DeferredWindowsRecordingCaptureEngine : IWindowsRecordingCaptureEngine
    {
        public void BeginCapture(WindowsRecordingCaptureStartRequest request)
        {
        }

        public WindowsRecordingCaptureResult CompleteCapture(WindowsRecordingCaptureRequest request) =>
            WindowsRecordingCaptureResult.Deferred("test capture is intentionally deferred");
    }
}

/// <summary>
/// R132 REMEDIATION: PrepareAnimationOverlay must prefer explicit per-paragraph ranged
/// timing (p:txEl/p:pRg, surfaced as ShapeAnimation.ParagraphRangeStart/End) over the
/// pre-existing bldLst/bldP[@build='p'] marker path when a slide carries BOTH.
/// PowerPoint's "By 1st Level Paragraphs" entrance authors both together, so the
/// marker-only path (SlideShowAnimationBuildPlanner.IsParagraphBuild) must never win when
/// ranged timing data is present and covers every paragraph: that data is what actually
/// drives per-click playback identity (_paragraphRangeAnimElements, keyed by the
/// animation), whereas the naive marker-only split (_paragraphAnimElements, keyed by
/// shape) just spreads paragraphs uniformly with no animation identity at all. This test
/// exercises real SlideShowWindow construction/navigation (which calls
/// PrepareAnimationOverlay via DisplayCurrentSlide), not just the reader/planner in
/// isolation -- a reader-only assertion would pass even if playback never reached the
/// ranged code path.
/// </summary>
public sealed class ParagraphRangeOverlayPrecedenceTests
{
    [StaFact]
    public void PrepareAnimationOverlay_PrefersRangedTiming_OverBldLstMarker_WhenBothPresent()
    {
        // Two slides: slide 0 carries the animated shape under test; slide 1 is a plain
        // landing slide. PrepareAnimationOverlay only runs from DisplayCurrentSlide, which
        // the window's constructor wires to the Loaded event -- never raised in a headless
        // unit test that doesn't call Show(). Starting on slide 1 and then navigating back
        // to slide 0 with ExecuteBack() drives DisplayCurrentSlide through the same
        // NavigateToSlide path real playback uses, independent of Loaded.
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        pres.Slides.Add(new Slide { Title = "Landing" });

        const uint shapeId = 42;
        slide.Shapes.Add(new SlideShape
        {
            Id = shapeId,
            Name = "Bullets",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph { Runs = { new Run { Text = "First" } } },
                    new Paragraph { Runs = { new Run { Text = "Second" } } },
                }
            }
        });

        // PowerPoint's "By 1st Level Paragraphs" entrance emits BOTH markers together:
        // the pre-existing bldLst/bldP hint (drives the naive uniform-split path)...
        slide.AnimationBuildListXml =
            "<p:bldLst xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">" +
            $"<p:bldP spid=\"{shapeId}\" grpId=\"0\" build=\"p\" /></p:bldLst>";

        // ...and explicit p:txEl/p:pRg per-paragraph timing (the richer, ranged data),
        // one ShapeAnimation entry per paragraph, together covering the whole shape.
        var rangeAnim0 = new ShapeAnimation
        {
            ShapeId = shapeId,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.OnClick,
            ParagraphRangeStart = 0,
            ParagraphRangeEnd = 0,
        };
        var rangeAnim1 = new ShapeAnimation
        {
            ShapeId = shapeId,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.AfterPrevious,
            ParagraphRangeStart = 1,
            ParagraphRangeEnd = 1,
        };
        slide.Animations.Add(rangeAnim0);
        slide.Animations.Add(rangeAnim1);

        var window = new SlideShowWindow(pres, startIndex: 1);
        try
        {
            window.Controller.CurrentSlideIndex.Should().Be(1, "the window must start on the landing slide");

            var backResult = window.ExecuteBack();
            backResult.Should().BeOfType<BackResult.NavigateToSlide>(
                "stepping back from the landing slide must navigate to slide 0 and run DisplayCurrentSlide -> PrepareAnimationOverlay for it");
            window.Controller.CurrentSlideIndex.Should().Be(0, "the animated shape's slide must now be current");

            var rangeField = typeof(SlideShowWindow).GetField(
                "_paragraphRangeAnimElements", BindingFlags.NonPublic | BindingFlags.Instance);
            var naiveField = typeof(SlideShowWindow).GetField(
                "_paragraphAnimElements", BindingFlags.NonPublic | BindingFlags.Instance);
            rangeField.Should().NotBeNull("PrepareAnimationOverlay's ranged-overlay dictionary must still exist");
            naiveField.Should().NotBeNull("PrepareAnimationOverlay's naive per-paragraph dictionary must still exist");

            var rangedElements = (System.Collections.IDictionary)rangeField!.GetValue(window)!;
            var naiveElements = (System.Collections.IDictionary)naiveField!.GetValue(window)!;

            rangedElements.Count.Should().Be(2,
                "the explicit per-paragraph ranged timing must drive playback when both it and the bldLst marker are present on the same shape");
            rangedElements.Contains(rangeAnim0).Should().BeTrue("the first paragraph's ranged animation must have its own overlay element");
            rangedElements.Contains(rangeAnim1).Should().BeTrue("the second paragraph's ranged animation must have its own overlay element");

            naiveElements.Contains(shapeId).Should().BeFalse(
                "the naive bldLst-only split must NOT run once richer ranged timing already covers every paragraph of the shape");
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
        var mainWindow = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
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
        var mainWindow = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
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
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
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

// ─────────────────────────────────────────────────────────────────────────────
public sealed partial class SlideShowMainWindowCustomShowTests
{
    [StaFact]
    public void MainWindow_BuildSlideShowLaunchPlan_ExposesSharedCustomShowChoices()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var presentation = window.Editor.Presentation;
            presentation.Slides.Clear();
            presentation.Slides.Add(new Slide { Title = "Intro" });
            presentation.Slides.Add(new Slide { Title = "Deep dive" });
            presentation.Slides.Add(new Slide { Title = "Appendix" });

            var customShow = new PresentationCustomShow { Name = "Executive review" };
            customShow.SlideIds.Add(presentation.Slides[2].Id);
            customShow.SlideIds.Add(presentation.Slides[0].Id);
            presentation.CustomShows.Add(customShow);
            window.Editor.SelectSlide(1);

            var plan = window.BuildSlideShowLaunchPlan();

            plan.CurrentSlideIndex.Should().Be(1);
            plan.Choices.Select(choice => choice.ChoiceId).Should().Equal(
                SlideShowCustomShowPlanner.FullPresentationChoiceId,
                SlideShowCustomShowPlanner.FromCurrentSlideChoiceId,
                SlideShowCustomShowPlanner.CustomShowChoicePrefix + "0");
            plan.Choices[1].StartIndex.Should().Be(1);
            plan.Choices[2].Should().Match<SlideShowLaunchChoice>(choice =>
                choice.Kind == SlideShowLaunchChoiceKind.CustomShow &&
                choice.Label == "Executive review" &&
                choice.SlideCount == 2 &&
                choice.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_TryBuildCustomSlideShowRoute_SelectsStoredCustomShow()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var presentation = window.Editor.Presentation;
            presentation.Slides.Clear();
            presentation.Slides.Add(new Slide { Title = "Intro" });
            presentation.Slides.Add(new Slide { Title = "Deep dive" });
            presentation.Slides.Add(new Slide { Title = "Appendix" });

            var customShow = new PresentationCustomShow { Name = "Executive review" };
            customShow.SlideIds.Add(presentation.Slides[2].Id);
            customShow.SlideIds.Add(presentation.Slides[0].Id);
            presentation.CustomShows.Add(customShow);

            var found = window.TryBuildCustomSlideShowRoute(
                "executive REVIEW",
                startIndex: 0,
                out var route);

            found.Should().BeTrue();
            route.CustomShowName.Should().Be("Executive review");
            route.Slides.Select(slide => slide.Title).Should().Equal("Appendix", "Intro");
            route.SourceSlideIndices.Should().Equal(2, 0);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_CustomShowAuthoring_UsesSharedMutationRoute()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var presentation = window.Editor.Presentation;
            presentation.Slides.Clear();
            presentation.Slides.Add(new Slide { Title = "Intro" });
            presentation.Slides.Add(new Slide { Title = "Deep dive" });
            presentation.Slides.Add(new Slide { Title = "Appendix" });

            var create = window.CreateCustomShow(
                "  Executive review  ",
                new[] { presentation.Slides[2].Id, "missing-slide", presentation.Slides[0].Id });
            var rename = window.RenameCustomShow(create.CustomShowIndex, "Board review");
            var updateSlides = window.UpdateCustomShowSlides(
                create.CustomShowIndex,
                new[] { presentation.Slides[1].Id, presentation.Slides[2].Id });
            var moveSlide = window.MoveCustomShowSlide(
                create.CustomShowIndex,
                sourceSlideIndex: 0,
                sourceSlideId: presentation.Slides[1].Id,
                targetSlideIndex: 1);
            var plan = window.BuildCustomShowAuthoringPlan();

            create.Succeeded.Should().BeTrue();
            rename.Succeeded.Should().BeTrue();
            updateSlides.Succeeded.Should().BeTrue();
            moveSlide.Succeeded.Should().BeTrue();
            moveSlide.SelectedSlideIndex.Should().Be(1);
            presentation.CustomShows.Should().ContainSingle();
            presentation.CustomShows[0].Name.Should().Be("Board review");
            presentation.CustomShows[0].SlideIds.Should().Equal(presentation.Slides[2].Id, presentation.Slides[1].Id);
            plan.CustomShows.Should().ContainSingle().Which.Name.Should().Be("Board review");
            plan.AvailableSlides.Select(slide => slide.Title).Should().Equal("Intro", "Deep dive", "Appendix");

            var delete = window.DeleteCustomShow(create.CustomShowIndex);

            delete.Succeeded.Should().BeTrue();
            presentation.CustomShows.Should().BeEmpty();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void CustomShowDialog_RendersExistingShowsAndSlideRows()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        CustomShowDialog? dialog = null;
        try
        {
            var presentation = window.Editor.Presentation;
            presentation.Slides.Clear();
            presentation.Slides.Add(new Slide { Title = "Intro" });
            presentation.Slides.Add(new Slide { Title = "Deep dive" });

            var create = window.CreateCustomShow(
                "Executive review",
                new[] { presentation.Slides[0].Id, presentation.Slides[1].Id });
            create.Succeeded.Should().BeTrue();

            dialog = new CustomShowDialog(window);

            dialog.RenderedCustomShowCount.Should().Be(1);
            dialog.RenderedSlideOptionCount.Should().Be(2);
            dialog.RenderedCustomShowSlideCount.Should().Be(2);
            dialog.SelectedCustomShowSlideIndex.Should().Be(0);
            dialog.ValidationMessage.Should().BeEmpty();

            dialog.MoveSelectedCustomShowSlideDownForTests();

            presentation.CustomShows[0].SlideIds.Should().Equal(presentation.Slides[1].Id, presentation.Slides[0].Id);
            dialog.SelectedCustomShowSlideIndex.Should().Be(1);
            dialog.ValidationMessage.Should().BeEmpty();

            dialog.AddCustomShowSlideOccurrenceForTests(presentation.Slides[0].Id);

            presentation.CustomShows[0].SlideIds.Should().Equal(
                presentation.Slides[1].Id,
                presentation.Slides[0].Id,
                presentation.Slides[0].Id);
            dialog.SelectedCustomShowSlideIndex.Should().Be(2);

            dialog.RemoveSelectedCustomShowSlideForTests();

            presentation.CustomShows[0].SlideIds.Should().Equal(
                presentation.Slides[1].Id,
                presentation.Slides[0].Id);
            dialog.SelectedCustomShowSlideIndex.Should().Be(1);
        }
        finally
        {
            dialog?.Close();
            window.Close();
        }
    }

    [StaFact]
    public void CustomShowDialog_DragReorder_UsesSharedPlannerAndExistingMoveMutation()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        CustomShowDialog? dialog = null;
        try
        {
            var presentation = window.Editor.Presentation;
            presentation.Slides.Clear();
            presentation.Slides.Add(new Slide { Title = "Intro" });
            presentation.Slides.Add(new Slide { Title = "Deep dive" });
            presentation.Slides.Add(new Slide { Title = "Appendix" });

            var create = window.CreateCustomShow(
                "Executive review",
                new[]
                {
                    presentation.Slides[2].Id,
                    presentation.Slides[0].Id,
                    presentation.Slides[2].Id
                });
            create.Succeeded.Should().BeTrue();

            dialog = new CustomShowDialog(window);

            var plan = dialog.DragReorderCustomShowSlideForTests(
                sourceSlideIndex: 0,
                targetDropIndex: 3);

            plan.IsValid.Should().BeTrue();
            plan.ShouldApplyMutation.Should().BeTrue();
            plan.SourceSlideId.Should().Be(presentation.Slides[2].Id);
            plan.TargetDropIndex.Should().Be(3);
            plan.TargetSlideIndex.Should().Be(2);
            plan.SlideIds.Should().Equal(
                presentation.Slides[0].Id,
                presentation.Slides[2].Id,
                presentation.Slides[2].Id);
            presentation.CustomShows[0].SlideIds.Should().Equal(plan.SlideIds);
            dialog.SelectedCustomShowSlideIndex.Should().Be(2);
            dialog.ValidationMessage.Should().BeEmpty();
        }
        finally
        {
            dialog?.Close();
            window.Close();
        }
    }

    private static string FindRepoFile(params string[] relativeParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = directory.FullName;
            relativeParts.CopyTo(parts, 1);

            var candidate = Path.Combine(parts);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "Could not locate repository file.",
            Path.Combine(relativeParts));
    }
}

// Wave 16C: SlideShowMediaController tests
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Pure-logic tests for the media rect computation and the temp-file lifecycle.
/// No WPF display is required — we use a fake ITempMediaFileWriter and avoid
/// creating any MediaElement.
/// </summary>
public sealed class SlideShowMediaControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SlideShape MakeMediaShape(
        long offX = 0, long offY = 0,
        long cx   = 9144000, long cy = 6858000) =>
        new()
        {
            Id            = 1,
            Name          = "Video1",
            Kind          = SlideShapeKind.Media,
            OffsetXEmu    = offX,
            OffsetYEmu    = offY,
            ExtentCxEmu   = cx,
            ExtentCyEmu   = cy,
            Media         = new MediaInfo
            {
                IsVideo     = true,
                Bytes       = new byte[] { 0x00, 0x01, 0x02 },
                ContentType = "video/mp4",
            }
        };

    private static Slide SlideWithMedia(SlideShape shape)
    {
        var slide = new Slide();
        slide.Shapes.Add(shape);
        return slide;
    }

    private static byte[] CreateSeekableWav(TimeSpan duration)
    {
        const int sampleRate = 8000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var sampleCount = checked((int)(duration.TotalSeconds * sampleRate));
        var dataLength = sampleCount * channels * (bitsPerSample / 8);

        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
        return stream.ToArray();
    }

    private static void ConfigureSeekableMedia(SlideShape shape)
    {
        shape.Media = new MediaInfo
        {
            IsVideo = false,
            Bytes = CreateSeekableWav(TimeSpan.FromSeconds(5)),
            ContentType = "audio/wav",
        };
    }

    // ── ComputeMediaRect ──────────────────────────────────────────────────────

    [Fact]
    public void ComputeMediaRect_FullSlide_MatchesCanvasBounds()
    {
        // A shape that covers the full 10" x 7.5" slide (in EMU), display 960×540.
        var shape = MakeMediaShape(0, 0, cx: 9144000, cy: 6858000);

        // Slide DIP: 9144000/9525=960, 6858000/9525=720
        var r = SlideShowMediaController.ComputeMediaRect(
            shape, slideDipW: 960, slideDipH: 720, canvasW: 960, canvasH: 720);

        r.X.Should().BeApproximately(0, 0.5);
        r.Y.Should().BeApproximately(0, 0.5);
        r.Width.Should().BeApproximately(960, 0.5);
        r.Height.Should().BeApproximately(720, 0.5);
    }

    [Fact]
    public void ComputeMediaRect_QuarterSlide_TopLeft()
    {
        // Shape in top-left quarter: offset 0,0 size half-slide
        var shape = MakeMediaShape(0, 0, cx: 4572000, cy: 3429000);

        var r = SlideShowMediaController.ComputeMediaRect(
            shape, slideDipW: 960, slideDipH: 720, canvasW: 960, canvasH: 720);

        r.X.Should().BeApproximately(0, 0.5);
        r.Y.Should().BeApproximately(0, 0.5);
        r.Width.Should().BeApproximately(480, 1.0);
        r.Height.Should().BeApproximately(360, 1.0);
    }

    [Fact]
    public void ComputeMediaRect_LetterboxedCanvas_OffsetApplied()
    {
        // Canvas is wider than slide → horizontal letterbox bars
        // Slide DIP: 960×720, canvas: 1280×720
        // scale = min(1280/960, 720/720) = min(1.333, 1.0) = 1.0
        // offsetX = (1280 - 960*1.0)/2 = 160, offsetY = 0
        var shape = MakeMediaShape(0, 0, cx: 9144000, cy: 6858000); // full slide

        var r = SlideShowMediaController.ComputeMediaRect(
            shape, slideDipW: 960, slideDipH: 720, canvasW: 1280, canvasH: 720);

        r.X.Should().BeApproximately(160, 0.5);
        r.Y.Should().BeApproximately(0, 0.5);
        r.Width.Should().BeApproximately(960, 1.0);
        r.Height.Should().BeApproximately(720, 1.0);
    }

    [Fact]
    public void ComputeMediaRect_ZeroCanvas_DoesNotThrow()
    {
        var shape = MakeMediaShape();
        var act = () => SlideShowMediaController.ComputeMediaRect(shape, 960, 720, 0, 0);
        act.Should().NotThrow();
    }

    // ── TempMediaFileWriter.ContentTypeToExtension ────────────────────────────

    [Theory]
    [InlineData("video/mp4",        ".mp4")]
    [InlineData("video/x-ms-wmv",   ".wmv")]
    [InlineData("audio/mpeg",       ".mp3")]
    [InlineData("audio/x-ms-wma",   ".wma")]
    [InlineData("audio/wav",        ".wav")]
    [InlineData("application/octet-stream", ".bin")]
    public void ContentTypeToExtension_KnownTypes_ReturnExpectedExtension(
        string contentType, string expected)
    {
        TempMediaFileWriter.ContentTypeToExtension(contentType)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("audio/mpeg", ".mp3")]
    [InlineData("audio/x-wav", ".wav")]
    [InlineData("audio/flac", ".flac")]
    [InlineData("audio/x-m4a", ".m4a")]
    [InlineData("audio/unknown", ".mp3")]
    public void TransitionSoundContentTypeToExtension_KnownAndFallbackTypes_ReturnExpectedExtension(
        string contentType, string expected)
    {
        TransitionSoundTempFile.ContentTypeToExtension(contentType)
            .Should().Be(expected);
    }

    [Fact]
    public void TransitionSoundTempFile_WriteAndDelete_UsesOneOwnedFile()
    {
        var path = TransitionSoundTempFile.Write(new byte[] { 1, 2, 3 }, "audio/wav");

        try
        {
            path.Should().EndWith(".wav");
            File.Exists(path).Should().BeTrue();
            File.Exists(path[..^4] + ".tmp").Should().BeFalse();
        }
        finally
        {
            TransitionSoundTempFile.Delete(path);
        }

        File.Exists(path).Should().BeFalse();
    }

    // ── Fake file writer: lifecycle ───────────────────────────────────────────

    /// <summary>In-memory fake that records writes and deletes for lifecycle assertions.</summary>
    private sealed class FakeFileWriter : ITempMediaFileWriter
    {
        private int _nextId;
        public readonly List<string> Written  = new();
        public readonly List<string> Deleted  = new();

        public string Write(byte[] bytes, string contentType)
        {
            var path = $"fake_media_{_nextId++}.tmp";
            Written.Add(path);
            return path;
        }

        public void Delete(string path) => Deleted.Add(path);
    }

    [StaFact]
    public void EnterSlide_WithMediaShape_WritesFile()
    {
        var fakeWriter = new FakeFileWriter();
        var overlay    = new System.Windows.Controls.Canvas();
        var ctrl       = new SlideShowMediaController(overlay, fakeWriter);

        var shape = MakeMediaShape();
        var slide = SlideWithMedia(shape);

        ctrl.EnterSlide(slide, 960, 720, 960, 720);

        fakeWriter.Written.Should().HaveCount(1);
    }

    [StaFact]
    public void EnterSlide_WithGroupedMediaShape_WritesFileForNestedPlayer()
    {
        var fakeWriter = new FakeFileWriter();
        var overlay = new System.Windows.Controls.Canvas();
        var ctrl = new SlideShowMediaController(overlay, fakeWriter);
        var group = new SlideShape { Id = 20, Kind = SlideShapeKind.Group };
        group.Children.Add(MakeMediaShape());
        var slide = SlideWithMedia(group);

        ctrl.EnterSlide(slide, 960, 720, 960, 720);

        fakeWriter.Written.Should().HaveCount(1);
        ctrl.Teardown();
    }

    [StaFact]
    public void EnterSlide_WithCaptionTrack_CreatesAndTearsDownCaptionSurface()
    {
        var fakeWriter = new FakeFileWriter();
        var overlay = new System.Windows.Controls.Canvas();
        var ctrl = new SlideShowMediaController(overlay, fakeWriter);
        var slide = SlideWithMedia(MakeMediaShape());
        var track = new PresentationMediaTranscriptTrackDescriptor(
            SlideIndex: 0,
            ShapeId: 1,
            ShapeName: "Video1",
            TrackIndex: 0,
            Label: "English",
            Language: "en-US",
            Source: "captions.vtt",
            ContentType: "text/vtt",
            Status: PresentationMediaTranscriptTrackStatus.Available,
            StatusMessage: string.Empty,
            Cues: [new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Hello from WPF")]);

        ctrl.EnterSlide(slide, 960, 720, 960, 720, [track]);
        ctrl.RefreshCaptionsForTest(TimeSpan.FromMilliseconds(500));
        ctrl.CaptionTextForTest(1).Should().Be("Hello from WPF");
        overlay.Children.OfType<System.Windows.Controls.Border>()
            .Should().Contain(border => border.Visibility == System.Windows.Visibility.Visible);

        ctrl.RefreshCaptionsForTest(TimeSpan.FromSeconds(2));
        ctrl.CaptionTextForTest(1).Should().BeEmpty();
        overlay.Children.OfType<System.Windows.Controls.Border>()
            .Should().NotContain(border => border.Visibility == System.Windows.Visibility.Visible);

        ctrl.Teardown();
        overlay.Children.OfType<System.Windows.Controls.Border>()
            .Should().BeEmpty();
    }

    [StaFact]
    public void EnterSlide_WithPreferredCaptionTrack_UsesSelectedLanguage()
    {
        var overlay = new System.Windows.Controls.Canvas();
        var ctrl = new SlideShowMediaController(overlay, new FakeFileWriter());
        var slide = SlideWithMedia(MakeMediaShape());
        var tracks = new[]
        {
            new PresentationMediaTranscriptTrackDescriptor(
                0, 1, "Video1", 0, "English", "en-US", "english.vtt", "text/vtt",
                PresentationMediaTranscriptTrackStatus.Available, string.Empty,
                [new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "English caption")]),
            new PresentationMediaTranscriptTrackDescriptor(
                0, 1, "Video1", 1, "Spanish", "es-ES", "spanish.vtt", "text/vtt",
                PresentationMediaTranscriptTrackStatus.Available, string.Empty,
                [new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Subtitulo")])
        };

        ctrl.EnterSlide(slide, 960, 720, 960, 720, tracks, 1, 1);
        ctrl.RefreshCaptionsForTest(TimeSpan.FromMilliseconds(500));

        ctrl.CaptionTextForTest(1).Should().Be("Subtitulo");
        ctrl.Teardown();
    }

    [StaFact]
    public void ActiveWebVttCue_RendersBasicInlineEmphasis()
    {
        var overlay = new System.Windows.Controls.Canvas();
        var ctrl = new SlideShowMediaController(overlay, new FakeFileWriter());
        var slide = SlideWithMedia(MakeMediaShape());
        var track = new PresentationMediaTranscriptTrackDescriptor(
            SlideIndex: 0,
            ShapeId: 1,
            ShapeName: "Video1",
            TrackIndex: 0,
            Label: "English",
            Language: "en-US",
            Source: "captions.vtt",
            ContentType: "text/vtt",
            Status: PresentationMediaTranscriptTrackStatus.Available,
            StatusMessage: string.Empty,
            Cues:
            [
                new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Styled")
                {
                    Spans =
                    [
                        new("Bold", Bold: true)
                        {
                            ForegroundColorHex = "FFCC00",
                            BackgroundColorHex = "000000",
                            FontFamily = "Aptos",
                            FontSizePx = 24
                        },
                        new(" italic", Italic: true),
                        new(" underline", Underline: true)
                    ]
                }
            ]);

        ctrl.EnterSlide(slide, 960, 720, 960, 720, [track]);
        ctrl.RefreshCaptionsForTest(TimeSpan.FromMilliseconds(500));

        var text = overlay.Children.OfType<System.Windows.Controls.Border>().Single().Child
            .Should().BeOfType<System.Windows.Controls.TextBlock>().Subject;
        var runs = text.Inlines.OfType<System.Windows.Documents.Run>().ToArray();
        runs.Should().HaveCount(3);
        runs[0].FontWeight.Should().Be(System.Windows.FontWeights.Bold);
        runs[0].Foreground.Should().BeOfType<System.Windows.Media.SolidColorBrush>()
            .Which.Color.Should().Be(System.Windows.Media.Color.FromRgb(0xFF, 0xCC, 0x00));
        runs[0].Background.Should().BeOfType<System.Windows.Media.SolidColorBrush>()
            .Which.Color.Should().Be(System.Windows.Media.Colors.Black);
        runs[0].FontFamily.Source.Should().Be("Aptos");
        runs[0].FontSize.Should().Be(24);
        runs[1].FontStyle.Should().Be(System.Windows.FontStyles.Italic);
        runs[2].TextDecorations.Should().ContainSingle()
            .Which.Should().Be(System.Windows.TextDecorations.Underline[0]);
        ctrl.Teardown();
    }

    [StaFact]
    public void ActiveWebVttCue_UsesAuthoredCaptionPlacement()
    {
        var overlay = new System.Windows.Controls.Canvas();
        var ctrl = new SlideShowMediaController(overlay, new FakeFileWriter());
        var slide = SlideWithMedia(MakeMediaShape());
        var track = new PresentationMediaTranscriptTrackDescriptor(
            SlideIndex: 0,
            ShapeId: 1,
            ShapeName: "Video1",
            TrackIndex: 0,
            Label: "English",
            Language: "en-US",
            Source: "captions.vtt",
            ContentType: "text/vtt",
            Status: PresentationMediaTranscriptTrackStatus.Available,
            StatusMessage: string.Empty,
            Cues:
            [
                new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Positioned")
                {
                    PositionPercent = 25,
                    LinePercent = 30,
                    SizePercent = 50,
                    Alignment = PresentationMediaTranscriptCueAlignment.Start
                }
            ]);

        ctrl.EnterSlide(slide, 960, 720, 960, 720, [track]);
        ctrl.RefreshCaptionsForTest(TimeSpan.FromMilliseconds(500));

        var caption = overlay.Children.OfType<System.Windows.Controls.Border>().Single();
        System.Windows.Controls.Canvas.GetLeft(caption).Should().Be(240);
        System.Windows.Controls.Canvas.GetTop(caption).Should().Be(216);
        caption.Width.Should().Be(480);
        caption.Height.Should().Be(86);
    }

    [StaFact]
    public void ActiveWebVttVerticalCue_UsesWritingDirectionAndRotatesTextSurface()
    {
        var overlay = new System.Windows.Controls.Canvas();
        var ctrl = new SlideShowMediaController(overlay, new FakeFileWriter());
        var slide = SlideWithMedia(MakeMediaShape());
        var track = new PresentationMediaTranscriptTrackDescriptor(
            SlideIndex: 0,
            ShapeId: 1,
            ShapeName: "Video1",
            TrackIndex: 0,
            Label: "Japanese",
            Language: "ja-JP",
            Source: "captions.vtt",
            ContentType: "text/vtt",
            Status: PresentationMediaTranscriptTrackStatus.Available,
            StatusMessage: string.Empty,
            Cues:
            [
                new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Vertical")
                {
                    PositionPercent = 75,
                    SizePercent = 40,
                    WritingMode = PresentationMediaTranscriptCueWritingMode.VerticalRightToLeft
                }
            ]);

        ctrl.EnterSlide(slide, 960, 720, 960, 720, [track]);
        ctrl.RefreshCaptionsForTest(TimeSpan.FromMilliseconds(500));

        var caption = overlay.Children.OfType<System.Windows.Controls.Border>().Single();
        System.Windows.Controls.Canvas.GetLeft(caption).Should().Be(874);
        System.Windows.Controls.Canvas.GetTop(caption).Should().Be(396);
        caption.Width.Should().Be(86);
        caption.Height.Should().Be(288);
        var text = caption.Child.Should().BeOfType<System.Windows.Controls.TextBlock>().Subject;
        text.RenderTransform.Should().BeOfType<System.Windows.Media.RotateTransform>()
            .Which.Angle.Should().Be(90);
        text.Width.Should().Be(288);
        text.Height.Should().Be(86);
    }

    [StaFact]
    public void UpdateLayout_RepositionsCaptionOverlayAfterCanvasResize()
    {
        var fakeWriter = new FakeFileWriter();
        var overlay = new System.Windows.Controls.Canvas();
        var ctrl = new SlideShowMediaController(overlay, fakeWriter);
        var slide = SlideWithMedia(MakeMediaShape());
        var track = new PresentationMediaTranscriptTrackDescriptor(
            SlideIndex: 0,
            ShapeId: 1,
            ShapeName: "Video1",
            TrackIndex: 0,
            Label: "English",
            Language: "en-US",
            Source: "captions.vtt",
            ContentType: "text/vtt",
            Status: PresentationMediaTranscriptTrackStatus.Available,
            StatusMessage: string.Empty,
            Cues: [new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Resize me")]);

        ctrl.EnterSlide(slide, 960, 720, 960, 720, [track]);
        var caption = overlay.Children.OfType<System.Windows.Controls.Border>().Single();
        System.Windows.Controls.Canvas.GetLeft(caption).Should().Be(0);

        ctrl.UpdateLayout(slide, 1280, 720);

        System.Windows.Controls.Canvas.GetLeft(caption).Should().Be(160);
        caption.Width.Should().Be(960);

        ctrl.UpdateLayout(new Slide(), 1280, 720);
        overlay.Children.OfType<System.Windows.Controls.Border>()
            .Should().BeEmpty("a size event for a new slide must not leave old media overlays behind");
    }

    [StaFact]
    public void Teardown_AfterEnter_DeletesWrittenFiles()
    {
        var fakeWriter = new FakeFileWriter();
        var overlay    = new System.Windows.Controls.Canvas();
        var ctrl       = new SlideShowMediaController(overlay, fakeWriter);

        var shape = MakeMediaShape();
        var slide = SlideWithMedia(shape);

        ctrl.EnterSlide(slide, 960, 720, 960, 720);
        ctrl.Teardown();

        // Every written file must be deleted.
        fakeWriter.Deleted.Should().Contain(fakeWriter.Written);
    }

    [StaFact]
    public void Teardown_CalledTwice_DoesNotThrow()
    {
        var fakeWriter = new FakeFileWriter();
        var overlay    = new System.Windows.Controls.Canvas();
        var ctrl       = new SlideShowMediaController(overlay, fakeWriter);

        var shape = MakeMediaShape();
        var slide = SlideWithMedia(shape);

        ctrl.EnterSlide(slide, 960, 720, 960, 720);

        var act = () => { ctrl.Teardown(); ctrl.Teardown(); };
        act.Should().NotThrow();
    }

    [StaFact]
    public void EnterSlide_SecondCall_TearsDownFirst()
    {
        var fakeWriter = new FakeFileWriter();
        var overlay    = new System.Windows.Controls.Canvas();
        var ctrl       = new SlideShowMediaController(overlay, fakeWriter);

        var shape1 = MakeMediaShape();
        var slide1 = SlideWithMedia(shape1);
        ctrl.EnterSlide(slide1, 960, 720, 960, 720);

        var shape2 = MakeMediaShape();
        var slide2 = SlideWithMedia(shape2);
        ctrl.EnterSlide(slide2, 960, 720, 960, 720); // should teardown slide1 first

        // slide1's file deleted; slide2's written.
        fakeWriter.Written.Should().HaveCount(2);
        fakeWriter.Deleted.Should().Contain(fakeWriter.Written[0]); // first file was cleaned up
    }

    [StaFact]
    public void EnterSlide_NoMediaShapes_WritesNoFiles()
    {
        var fakeWriter = new FakeFileWriter();
        var overlay    = new System.Windows.Controls.Canvas();
        var ctrl       = new SlideShowMediaController(overlay, fakeWriter);

        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id   = 1,
            Kind = SlideShapeKind.AutoShape,
        });

        ctrl.EnterSlide(slide, 960, 720, 960, 720);

        fakeWriter.Written.Should().BeEmpty();
    }

    [StaFact]
    public void EnterSlide_LinkOnlyHttp_WritesNoFile()
    {
        var fakeWriter = new FakeFileWriter();
        var overlay    = new System.Windows.Controls.Canvas();
        var ctrl       = new SlideShowMediaController(overlay, fakeWriter);

        var shape = new SlideShape
        {
            Id   = 1,
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes   = Array.Empty<byte>(),   // link-only: no bytes
                LinkUrl = "https://example.com/video.mp4",
                ContentType = "video/mp4",
            }
        };
        var slide = SlideWithMedia(shape);

        ctrl.EnterSlide(slide, 960, 720, 960, 720);

        // No bytes → no temp file written; the link is used directly.
        fakeWriter.Written.Should().BeEmpty();
    }

    [StaFact]
    public void EnterSlide_LinkOnlyFileScheme_WritesNoFile()
    {
        var fakeWriter = new FakeFileWriter();
        var overlay    = new System.Windows.Controls.Canvas();
        var ctrl       = new SlideShowMediaController(overlay, fakeWriter);

        var shape = new SlideShape
        {
            Id   = 1,
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes   = Array.Empty<byte>(),
                LinkUrl = "file:///C:/unsafe.mp4",  // security-rejected scheme
                ContentType = "video/mp4",
            }
        };
        var slide = SlideWithMedia(shape);

        ctrl.EnterSlide(slide, 960, 720, 960, 720);

        fakeWriter.Written.Should().BeEmpty();
    }

    // ── Play/pause toggle state machine ──────────────────────────────────────

    [StaFact]
    public void TryHandleClick_OnMediaShapeRect_ReturnsTrue()
    {
        // We verify the hit-test returns true for a click inside the shape's rect.
        // The shape is at (0,0) 960×720 filling the full slide.
        var fakeWriter = new FakeFileWriter();
        var overlay    = new System.Windows.Controls.Canvas();
        var ctrl       = new SlideShowMediaController(overlay, fakeWriter);

        var shape = MakeMediaShape(0, 0, cx: 9144000, cy: 6858000);
        var slide = SlideWithMedia(shape);

        ctrl.EnterSlide(slide, 960, 720, 960, 720);

        // Click at centre of slide/canvas (480, 360) — inside the full-slide media rect.
        bool hit = ctrl.TryHandleClick(480, 360, slide, 960, 720);
        hit.Should().BeTrue();
    }

    [StaFact]
    public void TryHandleClick_UsesTopmostOverlappingMediaShape()
    {
        var fakeWriter = new FakeFileWriter();
        var overlay = new System.Windows.Controls.Canvas();
        var ctrl = new SlideShowMediaController(overlay, fakeWriter);
        var slide = new Slide();
        var bottomShape = MakeMediaShape(0, 0, cx: 9144000, cy: 6858000);
        bottomShape.Id = 10;
        var topShape = MakeMediaShape(0, 0, cx: 9144000, cy: 6858000);
        topShape.Id = 20;
        slide.Shapes.Add(bottomShape);
        slide.Shapes.Add(topShape);

        ctrl.EnterSlide(slide, 960, 720, 960, 720);

        ctrl.TryHandleClick(480, 360, slide, 960, 720).Should().BeTrue();
        ctrl.LastMediaClickShapeIdForTest.Should().Be(20);
    }

    [StaFact]
    public void TryHandleClick_OutsideMediaShapeRect_ReturnsFalse()
    {
        // Shape is in the top-left quarter only (0,0 to 480,360).
        var fakeWriter = new FakeFileWriter();
        var overlay    = new System.Windows.Controls.Canvas();
        var ctrl       = new SlideShowMediaController(overlay, fakeWriter);

        var shape = MakeMediaShape(0, 0, cx: 4572000, cy: 3429000); // half-slide
        var slide = SlideWithMedia(shape);

        ctrl.EnterSlide(slide, 960, 720, 960, 720);

        // Click at bottom-right corner (outside the half-slide media rect).
        bool hit = ctrl.TryHandleClick(900, 680, slide, 960, 720);
        hit.Should().BeFalse();
    }

    // ── SlideShowWindow headless construction with media shape ───────────────

    [StaFact]
    public void TrySetVolumeAndSeek_UseSharedMediaShapeIds()
    {
        var fakeWriter = new TempMediaFileWriter();
        var overlay = new System.Windows.Controls.Canvas();
        var ctrl = new SlideShowMediaController(overlay, fakeWriter);
        var shape = MakeMediaShape();
        shape.Id = 42;
        shape.Name = "Audio1";
        ConfigureSeekableMedia(shape);
        var slide = SlideWithMedia(shape);

        ctrl.EnterSlide(slide, 960, 720, 960, 720);

        ctrl.TrySetVolume(shape.Id, 35).Should().BeTrue();
        var element = overlay.Children.OfType<System.Windows.Controls.MediaElement>().Single();
        element.Volume.Should().BeApproximately(0.35, 0.0001);
        ctrl.TrySetVolume(999, 35).Should().BeFalse();

        ctrl.TrySeek(shape.Id, TimeSpan.FromSeconds(3)).Should().BeTrue();
        element.Position.Should().Be(TimeSpan.FromSeconds(3));
        ctrl.TrySeek(shape.Id, TimeSpan.FromSeconds(-1)).Should().BeFalse();
        ctrl.Teardown();
    }

    [StaFact]
    public void TrySeekToBookmark_UsesNamedMediaBookmark()
    {
        var overlay = new System.Windows.Controls.Canvas();
        var ctrl = new SlideShowMediaController(overlay, new TempMediaFileWriter());
        var shape = MakeMediaShape();
        ConfigureSeekableMedia(shape);
        shape.Media!.Bookmarks.Add(new MediaBookmarkInfo
        {
            Name = "Cue",
            TimeMilliseconds = 2000,
        });
        var slide = SlideWithMedia(shape);

        ctrl.EnterSlide(slide, 960, 720, 960, 720);

        ctrl.TrySeekToBookmark(shape.Id, " cue ").Should().BeTrue();
        var element = overlay.Children.OfType<System.Windows.Controls.MediaElement>().Single();
        element.Position.Should().Be(TimeSpan.FromSeconds(2));
        ctrl.TrySeekToBookmark(shape.Id, "missing").Should().BeFalse();
        ctrl.Teardown();
    }

    [StaFact]
    public void TrySetVolume_ClampsToSharedZeroToHundredRange()
    {
        var overlay = new System.Windows.Controls.Canvas();
        var ctrl = new SlideShowMediaController(overlay, new TempMediaFileWriter());
        ctrl.EnterSlide(SlideWithMedia(MakeMediaShape()), 960, 720, 960, 720);

        ctrl.TrySetVolume(1, 150).Should().BeTrue();
        var element = overlay.Children.OfType<System.Windows.Controls.MediaElement>().Single();
        element.Volume.Should().BeApproximately(1, 0.0001);

        ctrl.TrySetVolume(1, -25).Should().BeTrue();
        element.Volume.Should().BeApproximately(0, 0.0001);
        ctrl.Teardown();
    }

    [StaFact]
    public void SlideShowWindow_WithMediaShape_ConstructsWithoutThrowing()
    {
        var pres  = Presentation.CreateEmpty();
        var slide = pres.Slides[0];

        // Add a media shape (embedded bytes).
        slide.Shapes.Add(new SlideShape
        {
            Id          = 10,
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 3429000,
            Media       = new MediaInfo
            {
                IsVideo     = true,
                Bytes       = new byte[] { 0x00, 0x01, 0x02 },
                ContentType = "video/mp4",
            }
        });

        // Should not throw — MediaElement creation is guarded by try/catch when headless.
        SlideShowWindow? window = null;
        var act = () => { window = new SlideShowWindow(pres, 0); };
        act.Should().NotThrow();
        window?.Close();
    }
}
