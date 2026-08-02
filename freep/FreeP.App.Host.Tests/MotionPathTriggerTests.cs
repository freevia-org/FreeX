using System.IO;
using FluentAssertions;
using Free.Shared.Drawing;
using FreeP.App.Host;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 8A: Unit tests for motion-path animations + interactive trigger animations.
/// Tests cover:
///   • MotionPath model (segment evaluation, path sampling)
///   • Round-trip I/O (reader ↔ writer) for motion paths and trigger sequences
///   • SlideCloner copies motion/trigger fields
///   • SlideShowController.BuildSteps excludes trigger animations from main chain
///   • SlideShowController.FireTrigger returns the right group
///   • MotionPathEvaluator.Sample maps normalized coords correctly
/// </summary>
public sealed class MotionPathModelTests
{
    // ── MotionPathSegment.Evaluate ──────────────────────────────────────────────

    [Fact]
    public void Segment_MoveTo_Evaluate_ReturnsTarget()
    {
        var seg = MotionPathSegment.MoveTo(0.3, 0.4);
        var (x, y) = seg.Evaluate(0.5, 0, 0);
        x.Should().BeApproximately(0.3, 1e-9);
        y.Should().BeApproximately(0.4, 1e-9);
    }

    [Fact]
    public void Segment_LineTo_Evaluate_AtT0_ReturnsPrev()
    {
        var seg = MotionPathSegment.LineTo(1.0, 0.5);
        var (x, y) = seg.Evaluate(0.0, 0.0, 0.0);
        x.Should().BeApproximately(0.0, 1e-9, "t=0 should be at the start point");
        y.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void Segment_LineTo_Evaluate_AtT1_ReturnsEndpoint()
    {
        var seg = MotionPathSegment.LineTo(1.0, 0.5);
        var (x, y) = seg.Evaluate(1.0, 0.0, 0.0);
        x.Should().BeApproximately(1.0, 1e-9);
        y.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Segment_LineTo_Evaluate_AtT05_IsMidpoint()
    {
        var seg = MotionPathSegment.LineTo(1.0, 0.0);
        var (x, y) = seg.Evaluate(0.5, 0.0, 0.0);
        x.Should().BeApproximately(0.5, 1e-9);
        y.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void Segment_CubicTo_Evaluate_AtT0_ReturnsPrev()
    {
        var seg = MotionPathSegment.CubicTo(0.25, 0.0, 0.75, 0.0, 1.0, 0.0);
        var (x, y) = seg.Evaluate(0.0, 0.0, 0.0);
        x.Should().BeApproximately(0.0, 1e-9);
        y.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void Segment_CubicTo_Evaluate_AtT1_ReturnsEndpoint()
    {
        var seg = MotionPathSegment.CubicTo(0.1, 0.1, 0.9, 0.1, 0.5, 0.3);
        var (x, y) = seg.Evaluate(1.0, 0.0, 0.0);
        x.Should().BeApproximately(0.5, 1e-6);
        y.Should().BeApproximately(0.3, 1e-6);
    }

    // ── MotionPathEvaluator.Sample ──────────────────────────────────────────────

    [Fact]
    public void Evaluator_Sample_EmptyPath_ReturnsZero()
    {
        var mp = new MotionPath();
        var (dx, dy) = MotionPathEvaluator.Sample(mp, 0.5);
        dx.Should().Be(0);
        dy.Should().Be(0);
    }

    [Fact]
    public void Evaluator_Sample_LinearPath_AtT0_ReturnsZeroDisplacement()
    {
        // M 0 0 L 0.5 0.3 — start at (0,0), end at (0.5, 0.3)
        var mp = new MotionPath();
        mp.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        mp.Segments.Add(MotionPathSegment.LineTo(0.5, 0.3));

        var (dx, dy) = MotionPathEvaluator.Sample(mp, 0);
        dx.Should().BeApproximately(0, 1e-6, "at t=0 displacement should be zero");
        dy.Should().BeApproximately(0, 1e-6);
    }

    [Fact]
    public void Evaluator_Sample_LinearPath_AtT1_ReturnsFullDisplacement()
    {
        var mp = new MotionPath();
        mp.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        mp.Segments.Add(MotionPathSegment.LineTo(0.5, 0.3));

        var (dx, dy) = MotionPathEvaluator.Sample(mp, 1.0);
        dx.Should().BeApproximately(0.5, 1e-6);
        dy.Should().BeApproximately(0.3, 1e-6);
    }

    [Fact]
    public void Evaluator_Sample_TwoSegmentPath_AtT05_UsesArcLength()
    {
        // The second segment is longer, so halfway through the path is already
        // partway through that segment rather than at an equal-segment boundary.
        var mp = new MotionPath();
        mp.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        mp.Segments.Add(MotionPathSegment.LineTo(0.5, 0));
        mp.Segments.Add(MotionPathSegment.LineTo(1.0, 0.5));

        var (dx, dy) = MotionPathEvaluator.Sample(mp, 0.5);
        dx.Should().BeApproximately(0.573223, 1e-5);
        dy.Should().BeApproximately(0.073223, 1e-5);
    }

    [Fact]
    public void Evaluator_Sample_CubicAndLinePath_UsesCubicArcLength()
    {
        var mp = new MotionPath();
        mp.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        mp.Segments.Add(MotionPathSegment.CubicTo(0.025, 0, 0.075, 0, 0.1, 0));
        mp.Segments.Add(MotionPathSegment.LineTo(1, 0));

        var (dx, dy) = MotionPathEvaluator.Sample(mp, 0.5);
        dx.Should().BeApproximately(0.5, 1e-5);
        dy.Should().BeApproximately(0, 1e-5);
    }

    // ── MotionPath I/O round-trip ───────────────────────────────────────────────

    [Fact]
    public void RoundTrip_MotionPath_SegmentsPreserved()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 2, Name = "Mover", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu = 1143000, OffsetYEmu = 1143000,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
        });

        var motion = new MotionPath { Origin = "parent" };
        motion.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        motion.Segments.Add(MotionPathSegment.LineTo(0.5, 0.25));
        motion.Segments.Add(MotionPathSegment.Close());

        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId    = 2,
            Kind       = AnimationKind.Motion,
            Trigger    = AnimationTrigger.OnClick,
            DurationMs = 1200,
            Motion     = motion,
        });

        // Write → read
        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var anims = loaded.Slides[0].Animations;
        anims.Should().HaveCount(1);

        var a = anims[0];
        a.Kind.Should().Be(AnimationKind.Motion);
        a.ShapeId.Should().Be(2u);
        a.DurationMs.Should().Be(1200);
        a.Motion.Should().NotBeNull();

        var segs = a.Motion!.Segments;
        segs.Should().HaveCount(3);
        segs[0].Kind.Should().Be(MotionPathSegmentKind.Move);
        segs[0].X.Should().BeApproximately(0, 1e-4);
        segs[0].Y.Should().BeApproximately(0, 1e-4);
        segs[1].Kind.Should().Be(MotionPathSegmentKind.Line);
        segs[1].X.Should().BeApproximately(0.5, 1e-4);
        segs[1].Y.Should().BeApproximately(0.25, 1e-4);
        segs[2].Kind.Should().Be(MotionPathSegmentKind.Close);
    }

    [Fact]
    public void RoundTrip_MotionPath_CubicSegmentsPreserved()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 3, Name = "CurveMover", Kind = SlideShapeKind.AutoShape,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
        });

        var motion = new MotionPath { Origin = "parent" };
        motion.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        motion.Segments.Add(MotionPathSegment.CubicTo(0.1, -0.2, 0.4, -0.2, 0.5, 0));
        motion.Segments.Add(MotionPathSegment.LineTo(0.75, 0.1));

        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId    = 3,
            Kind       = AnimationKind.Motion,
            Trigger    = AnimationTrigger.OnClick,
            DurationMs = 800,
            Motion     = motion,
        });

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var a = loaded.Slides[0].Animations[0];
        a.Kind.Should().Be(AnimationKind.Motion);
        a.Motion.Should().NotBeNull();

        var segs = a.Motion!.Segments;
        segs.Should().HaveCount(3);
        segs[1].Kind.Should().Be(MotionPathSegmentKind.Cubic);
        segs[1].X1.Should().BeApproximately(0.1, 1e-4);
        segs[1].Y1.Should().BeApproximately(-0.2, 1e-4);
        segs[1].X2.Should().BeApproximately(0.4, 1e-4);
        segs[1].Y2.Should().BeApproximately(-0.2, 1e-4);
        segs[1].X.Should().BeApproximately(0.5, 1e-4);
        segs[1].Y.Should().BeApproximately(0, 1e-4);
        segs[2].Kind.Should().Be(MotionPathSegmentKind.Line);
        segs[2].X.Should().BeApproximately(0.75, 1e-4);
        segs[2].Y.Should().BeApproximately(0.1, 1e-4);
    }

    // ── Trigger animation I/O round-trip ────────────────────────────────────────

    [Fact]
    public void RoundTrip_TriggerAnimation_TriggerShapeIdPreserved()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];

        // Button (trigger shape) = Id 1 (already exists as the placeholder)
        slide.Shapes.Add(new SlideShape
        {
            Id = 10, Name = "Target", Kind = SlideShapeKind.AutoShape,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
        });

        // A trigger animation: clicking shape 1 makes shape 10 appear.
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId        = 10,
            Kind           = AnimationKind.Entrance,
            Preset         = AnimationPreset.Appear,
            Trigger        = AnimationTrigger.OnClick,
            DurationMs     = 500,
            TriggerShapeId = slide.Shapes[0].Id, // trigger = first shape (Id 1)
        });

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var anims = loaded.Slides[0].Animations;
        anims.Should().HaveCount(1);
        var a = anims[0];
        a.ShapeId.Should().Be(10u);
        a.TriggerShapeId.Should().NotBeNull("trigger shape id must survive round-trip");
        a.TriggerShapeId!.Value.Should().Be(slide.Shapes[0].Id);
    }

    [Fact]
    public void RoundTrip_MixedMainAndTriggerAnimations()
    {
        // A slide with both main-sequence and trigger animations.
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Add(new SlideShape { Id = 2, Name = "S2", Kind = SlideShapeKind.AutoShape,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400 });
        slide.Shapes.Add(new SlideShape { Id = 3, Name = "S3", Kind = SlideShapeKind.AutoShape,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400 });

        // Main sequence: shape 2 fades in on click
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId    = 2,
            Kind       = AnimationKind.Entrance,
            Preset     = AnimationPreset.Fade,
            Trigger    = AnimationTrigger.OnClick,
            DurationMs = 500,
        });

        // Trigger animation: clicking shape 1 shows shape 3
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId        = 3,
            Kind           = AnimationKind.Entrance,
            Preset         = AnimationPreset.Appear,
            Trigger        = AnimationTrigger.OnClick,
            DurationMs     = 200,
            TriggerShapeId = slide.Shapes[0].Id,
        });

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var anims = loaded.Slides[0].Animations;
        anims.Should().HaveCount(2);

        var mainAnim = anims.FirstOrDefault(a => a.TriggerShapeId is null);
        var trigAnim = anims.FirstOrDefault(a => a.TriggerShapeId is not null);

        mainAnim.Should().NotBeNull();
        mainAnim!.ShapeId.Should().Be(2u);
        mainAnim.Preset.Should().Be(AnimationPreset.Fade);

        trigAnim.Should().NotBeNull();
        trigAnim!.ShapeId.Should().Be(3u);
        trigAnim.TriggerShapeId.Should().Be(slide.Shapes[0].Id);
    }
}

/// <summary>
/// Tests for SlideShowController trigger-animation separation.
/// </summary>
public sealed class MotionPathControllerTests
{
    // ── BuildSteps excludes trigger animations ──────────────────────────────────

    [Fact]
    public void BuildSteps_ExcludesTriggerAnimations_FromMainChain()
    {
        var slide = new Slide();
        // Main sequence animation
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 1, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick, DurationMs = 500,
        });
        // Trigger animation (should NOT appear in main steps)
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 2, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.OnClick, DurationMs = 300,
            TriggerShapeId = 1u, // clicking shape 1 fires this
        });

        var steps = SlideShowController.BuildSteps(slide);

        steps.Should().HaveCount(1, "only the main-sequence animation should form a step");
        steps[0].Animations.Should().HaveCount(1);
        steps[0].Animations[0].ShapeId.Should().Be(1u);
    }

    [Fact]
    public void BuildSteps_OnlyTriggerAnimations_ReturnsNoMainSteps()
    {
        var slide = new Slide();
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 2, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick, DurationMs = 500, TriggerShapeId = 1u,
        });

        var steps = SlideShowController.BuildSteps(slide);
        steps.Should().BeEmpty("all animations are triggered — main chain is empty");
    }

    [Fact]
    public void BuildSteps_MixedAnimations_MainStepsUnaffected()
    {
        var slide = new Slide();
        // Two main-sequence animations
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 1, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick, DurationMs = 500,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 2, Kind = AnimationKind.Entrance, Preset = AnimationPreset.FlyIn,
            Trigger = AnimationTrigger.OnClick, DurationMs = 500,
        });
        // Two trigger animations
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 3, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.OnClick, DurationMs = 300, TriggerShapeId = 1u,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 4, Kind = AnimationKind.Emphasis, Preset = AnimationPreset.Spin,
            Trigger = AnimationTrigger.WithPrevious, DurationMs = 300, TriggerShapeId = 1u,
        });

        var steps = SlideShowController.BuildSteps(slide);

        steps.Should().HaveCount(2, "only main-sequence animations form click-steps");
        steps[0].Animations[0].ShapeId.Should().Be(1u);
        steps[1].Animations[0].ShapeId.Should().Be(2u);
    }

    // ── FireTrigger returns the right group ──────────────────────────────────────

    [Fact]
    public void FireTrigger_ReturnsAnimationsForCorrectShape()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];

        // Trigger animations for shape 1
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 10, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick, DurationMs = 500, TriggerShapeId = 1u,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 11, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.WithPrevious, DurationMs = 300, TriggerShapeId = 1u,
        });

        // Trigger animations for shape 2 (different button)
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 20, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Zoom,
            Trigger = AnimationTrigger.OnClick, DurationMs = 700, TriggerShapeId = 2u,
        });

        var ctrl = new SlideShowController(pres.Slides, 0);

        // Fire trigger for shape 1
        var steps1 = ctrl.FireTrigger(1u);
        steps1.Should().HaveCount(1, "one click group for shape-1 trigger");
        steps1[0].Animations.Should().HaveCount(2, "OnClick + WithPrevious in same step");
        steps1[0].Animations[0].ShapeId.Should().Be(10u);
        steps1[0].Animations[1].ShapeId.Should().Be(11u);

        // Fire trigger for shape 2
        var steps2 = ctrl.FireTrigger(2u);
        steps2.Should().HaveCount(1);
        steps2[0].Animations.Should().HaveCount(1);
        steps2[0].Animations[0].ShapeId.Should().Be(20u);
    }

    [Fact]
    public void FireTrigger_UnknownShapeId_ReturnsEmpty()
    {
        var pres = Presentation.CreateEmpty();
        var ctrl = new SlideShowController(pres.Slides, 0);

        var steps = ctrl.FireTrigger(999u);
        steps.Should().BeEmpty();
    }

    [Fact]
    public void FireTrigger_NoTriggerAnimations_ReturnsEmpty()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 1, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick, DurationMs = 500, // no TriggerShapeId
        });

        var ctrl = new SlideShowController(pres.Slides, 0);
        var steps = ctrl.FireTrigger(1u);
        steps.Should().BeEmpty("shape 1 has no trigger animations registered for it");
    }

    // ── Controller.Advance does not consume triggered animations ─────────────────

    [Fact]
    public void Controller_Advance_TriggerAnimations_DoNotBlockMainAdvance()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];

        // Only trigger animations — no main sequence animations
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 2, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick, DurationMs = 300, TriggerShapeId = 1u,
        });

        pres.Slides.Add(new Slide { Title = "Slide 2" });

        var ctrl = new SlideShowController(pres.Slides, 0);

        // No main steps: first advance should navigate to slide 1 (not try to play trigger anim).
        ctrl.StepCount.Should().Be(0);
        var result = ctrl.Advance();
        result.Should().BeOfType<AdvanceResult.NavigateToSlide>()
            .Which.SlideIndex.Should().Be(1);
    }

    // ── Motion animation in BuildSteps ──────────────────────────────────────────

    [Fact]
    public void BuildSteps_MotionAnimation_IncludedInMainChain()
    {
        var slide = new Slide();
        var mp = new MotionPath();
        mp.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        mp.Segments.Add(MotionPathSegment.LineTo(0.5, 0));

        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId    = 1,
            Kind       = AnimationKind.Motion,
            Trigger    = AnimationTrigger.OnClick,
            DurationMs = 1000,
            Motion     = mp,
            // No TriggerShapeId → main sequence
        });

        var steps = SlideShowController.BuildSteps(slide);
        steps.Should().HaveCount(1);
        steps[0].Animations[0].Kind.Should().Be(AnimationKind.Motion);
    }

    // ── SlideCloner preserves motion + trigger fields ────────────────────────────

    [Fact]
    public void Cloner_MotionAnimation_Preserved()
    {
        var slide = new Slide();
        var mp = new MotionPath { Origin = "parent", PtsTypes = "F" };
        mp.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        mp.Segments.Add(MotionPathSegment.LineTo(0.3, 0.1));
        mp.Segments.Add(MotionPathSegment.CubicTo(0.4, 0.0, 0.6, 0.0, 0.7, 0.2));

        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId    = 1,
            Kind       = AnimationKind.Motion,
            Trigger    = AnimationTrigger.OnClick,
            DurationMs = 1500,
            Motion     = mp,
        });

        var clone = SlideCloner.CloneSlide(slide);

        clone.Animations.Should().HaveCount(1);
        var ca = clone.Animations[0];
        ca.Kind.Should().Be(AnimationKind.Motion);
        ca.Motion.Should().NotBeNull();
        ca.Motion!.Origin.Should().Be("parent");
        ca.Motion.PtsTypes.Should().Be("F");
        ca.Motion.Segments.Should().HaveCount(3);
        ca.Motion.Segments[2].Kind.Should().Be(MotionPathSegmentKind.Cubic);
        ca.Motion.Segments[2].X1.Should().BeApproximately(0.4, 1e-9);
        ca.Motion.Segments[2].X.Should().BeApproximately(0.7, 1e-9);

        // Mutating the original should NOT affect the clone (independent copy).
        mp.Segments.Add(MotionPathSegment.Close());
        ca.Motion.Segments.Should().HaveCount(3, "clone is independent of original");
    }

    [Fact]
    public void Cloner_TriggerAnimation_TriggerShapeIdPreserved()
    {
        var slide = new Slide();
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId        = 5,
            Kind           = AnimationKind.Entrance,
            Preset         = AnimationPreset.Appear,
            Trigger        = AnimationTrigger.OnClick,
            DurationMs     = 300,
            TriggerShapeId = 99u,
        });

        var clone = SlideCloner.CloneSlide(slide);

        clone.Animations.Should().HaveCount(1);
        clone.Animations[0].TriggerShapeId.Should().Be(99u);
    }
}

/// <summary>
/// U2 regression: trigger-target shapes must NOT be hidden at slide entry.
/// PrepareAnimationOverlay previously added every Entrance/Motion-animated shape to
/// _entranceShapeIds regardless of whether the animation is interactive (TriggerShapeId != null).
/// The fix filters out trigger animations so trigger-target shapes remain visible.
/// These tests exercise the controller/model seam that drives PrepareAnimationOverlay.
/// </summary>
public sealed class TriggerTargetVisibilityTests
{
    private static Slide MakeSlide(
        uint mainShapeId, uint triggerButtonId, uint triggerTargetId)
    {
        var slide = new Slide();

        // Main-sequence entrance: shape should be hidden until clicked.
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId        = mainShapeId,
            Kind           = AnimationKind.Entrance,
            Preset         = AnimationPreset.Appear,
            Trigger        = AnimationTrigger.OnClick,
            DurationMs     = 500,
        });

        // Interactive trigger: clicking triggerButtonId reveals triggerTargetId.
        // The TARGET shape must be VISIBLE at slide entry (not hidden).
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId        = triggerTargetId,
            Kind           = AnimationKind.Entrance,
            Preset         = AnimationPreset.Fade,
            Trigger        = AnimationTrigger.OnClick,
            DurationMs     = 300,
            TriggerShapeId = triggerButtonId,
        });

        return slide;
    }

    [Fact]
    public void TriggerTargetShape_IsNotInMainSteps_SoNotHiddenAtEntry()
    {
        // The main-sequence BuildSteps must NOT include the trigger-target shape —
        // that is the seam which drives what PrepareAnimationOverlay hides.
        var slide = MakeSlide(mainShapeId: 1u, triggerButtonId: 2u, triggerTargetId: 3u);

        var steps = SlideShowController.BuildSteps(slide);

        // Only the main-sequence shape forms a step.
        steps.Should().HaveCount(1);
        steps[0].Animations.Should().HaveCount(1);
        steps[0].Animations[0].ShapeId.Should().Be(1u,
            "trigger-target shape must NOT appear in the main advance chain");
    }

    [Fact]
    public void TriggerOnlySlide_MainSteps_AreEmpty_NothingHiddenAtEntry()
    {
        // A slide whose ONLY animations are interactive triggers: no shapes should be
        // hidden at slide entry (all main steps are empty).
        var slide = new Slide();
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId        = 5,
            Kind           = AnimationKind.Entrance,
            Preset         = AnimationPreset.Appear,
            Trigger        = AnimationTrigger.OnClick,
            DurationMs     = 400,
            TriggerShapeId = 1u,
        });

        var steps = SlideShowController.BuildSteps(slide);
        steps.Should().BeEmpty("no main-sequence animations → nothing is hidden at entry");
    }

    [Fact]
    public void MotionTrigger_TargetShape_NotHiddenAtEntry()
    {
        // A Motion animation that is an interactive trigger: the mover shape must be
        // visible at slide entry (it moves when clicked, not disappears).
        var slide = new Slide();
        var mp = new MotionPath();
        mp.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        mp.Segments.Add(MotionPathSegment.LineTo(0.3, 0));

        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId        = 7,
            Kind           = AnimationKind.Motion,
            Trigger        = AnimationTrigger.OnClick,
            DurationMs     = 800,
            Motion         = mp,
            TriggerShapeId = 2u,  // interactive trigger
        });

        var steps = SlideShowController.BuildSteps(slide);
        steps.Should().BeEmpty("motion trigger animation is not in the main advance chain");
    }

    [Fact]
    public void MixedSlide_EntranceShapeIds_OnlyContainsMainSequenceShapes()
    {
        // Simulate the filter that PrepareAnimationOverlay applies:
        // only Entrance/Motion animations with TriggerShapeId == null count.
        var slide = MakeSlide(mainShapeId: 10u, triggerButtonId: 20u, triggerTargetId: 30u);

        var hiddenAtEntry = slide.Animations
            .Where(a => (a.Kind == AnimationKind.Entrance || a.Kind == AnimationKind.Motion)
                        && a.TriggerShapeId == null)
            .Select(a => a.ShapeId)
            .Distinct()
            .ToList();

        hiddenAtEntry.Should().ContainSingle()
            .Which.Should().Be(10u, "only the main-sequence shape is hidden at entry");
        hiddenAtEntry.Should().NotContain(30u,
            "trigger-target shape must remain visible at slide entry");
    }
}

/// <summary>
/// U3 regression: interactive trigger sequences must advance ONE step per click,
/// not fire all steps simultaneously.  Tests cover SlideShowController.AdvanceTrigger,
/// which mirrors the PendingStepIndex pattern for per-trigger cursors.
/// </summary>
public sealed class TriggerStepCursorTests
{
    private static SlideShowController MakeControllerWithMultiStepTrigger(
        uint triggerButtonId, out int stepCount)
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];

        // Three OnClick steps for the same trigger button (each step = one click).
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 10, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick, DurationMs = 300, TriggerShapeId = triggerButtonId,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 11, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.OnClick, DurationMs = 300, TriggerShapeId = triggerButtonId,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 12, Kind = AnimationKind.Entrance, Preset = AnimationPreset.FlyIn,
            Trigger = AnimationTrigger.OnClick, DurationMs = 300, TriggerShapeId = triggerButtonId,
        });

        stepCount = 3;
        return new SlideShowController(pres.Slides, 0);
    }

    [Fact]
    public void AdvanceTrigger_FirstClick_ReturnsFirstStep()
    {
        var ctrl = MakeControllerWithMultiStepTrigger(1u, out _);

        var step = ctrl.AdvanceTrigger(1u);

        step.Should().NotBeNull("first click must return the first step");
        step!.Animations.Should().HaveCount(1);
        step.Animations[0].ShapeId.Should().Be(10u);
    }

    [Fact]
    public void AdvanceTrigger_SecondClick_ReturnsSecondStep()
    {
        var ctrl = MakeControllerWithMultiStepTrigger(1u, out _);

        ctrl.AdvanceTrigger(1u);                  // click 1
        var step = ctrl.AdvanceTrigger(1u);       // click 2

        step.Should().NotBeNull();
        step!.Animations[0].ShapeId.Should().Be(11u);
    }

    [Fact]
    public void AdvanceTrigger_ThirdClick_ReturnsThirdStep()
    {
        var ctrl = MakeControllerWithMultiStepTrigger(1u, out _);

        ctrl.AdvanceTrigger(1u);
        ctrl.AdvanceTrigger(1u);
        var step = ctrl.AdvanceTrigger(1u);       // click 3

        step.Should().NotBeNull();
        step!.Animations[0].ShapeId.Should().Be(12u);
    }

    [Fact]
    public void AdvanceTrigger_BeyondLastStep_ReturnsNull()
    {
        var ctrl = MakeControllerWithMultiStepTrigger(1u, out _);

        ctrl.AdvanceTrigger(1u);
        ctrl.AdvanceTrigger(1u);
        ctrl.AdvanceTrigger(1u);
        var step = ctrl.AdvanceTrigger(1u);       // click 4 — exhausted

        step.Should().BeNull("subsequent clicks after all steps done must be silent no-ops");
    }

    [Fact]
    public void AdvanceTrigger_UnknownTrigger_ReturnsNull()
    {
        var ctrl = MakeControllerWithMultiStepTrigger(1u, out _);

        var step = ctrl.AdvanceTrigger(999u);

        step.Should().BeNull("unknown trigger shape returns null, no crash");
    }

    [Fact]
    public void AdvanceTrigger_CursorsArePerTrigger_Independent()
    {
        // Two different trigger shapes, each with its own independent cursor.
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];

        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 20, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick, DurationMs = 300, TriggerShapeId = 1u,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 21, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.OnClick, DurationMs = 300, TriggerShapeId = 1u,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 30, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Zoom,
            Trigger = AnimationTrigger.OnClick, DurationMs = 300, TriggerShapeId = 2u,
        });

        var ctrl = new SlideShowController(pres.Slides, 0);

        // Advance trigger 1 once.
        var t1step1 = ctrl.AdvanceTrigger(1u);
        t1step1!.Animations[0].ShapeId.Should().Be(20u);

        // Advance trigger 2 once — independent from trigger 1's cursor.
        var t2step1 = ctrl.AdvanceTrigger(2u);
        t2step1!.Animations[0].ShapeId.Should().Be(30u,
            "trigger 2's cursor is independent of trigger 1");

        // Advance trigger 1 again — should give its second step, not trigger 2's.
        var t1step2 = ctrl.AdvanceTrigger(1u);
        t1step2!.Animations[0].ShapeId.Should().Be(21u);
    }

    [Fact]
    public void AdvanceTrigger_CursorResets_OnSlideChange()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides.Add(new Slide { Title = "S2" });
        var slide = pres.Slides[0];
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 5, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick, DurationMs = 300, TriggerShapeId = 1u,
        });

        var ctrl = new SlideShowController(pres.Slides, 0);

        ctrl.AdvanceTrigger(1u);   // exhausts the single step

        // Navigate away and back.
        ctrl.GoToSlide(1);
        ctrl.GoToSlide(0);

        // Cursor must be reset — first click plays the step again.
        var step = ctrl.AdvanceTrigger(1u);
        step.Should().NotBeNull("cursor resets when slide is re-entered");
        step!.Animations[0].ShapeId.Should().Be(5u);
    }

    [Fact]
    public void FireTrigger_StillReturnsAllSteps_Unchanged()
    {
        // FireTrigger is a pure query — AdvanceTrigger should not affect it.
        var ctrl = MakeControllerWithMultiStepTrigger(1u, out _);

        ctrl.AdvanceTrigger(1u);   // advance cursor past step 0
        ctrl.AdvanceTrigger(1u);   // advance cursor past step 1

        var allSteps = ctrl.FireTrigger(1u);

        allSteps.Should().HaveCount(3,
            "FireTrigger returns all steps regardless of cursor position");
    }
}

/// <summary>
/// Wave 8A: Fixture round-trip — reads 10-motionpath.pptx (hand-built) and verifies
/// that motion-path animations survive a reader → writer → reader cycle.
/// </summary>
public sealed class MotionPathFixtureTests
{
    private static readonly string CorpusPath =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "tools", "FreeP.RenderCompare", "corpus");

    [Fact]
    public void Fixture_10_MotionPath_RoundTrips()
    {
        var fixturePath = Path.GetFullPath(Path.Combine(CorpusPath, "10-motionpath.pptx"));
        if (!File.Exists(fixturePath))
        {
            // Fixture might not exist if running in a path without the corpus.
            // Skip gracefully — the model-level round-trip tests above still validate the feature.
            return;
        }

        var pres = PptxPackageReader.Read(fixturePath);
        pres.Slides.Should().NotBeEmpty();

        // The fixture has at least one motion-path animation.
        var hasMotion = pres.Slides.Any(s =>
            s.Animations.Any(a => a.Kind == AnimationKind.Motion));
        hasMotion.Should().BeTrue("10-motionpath.pptx must contain at least one motion animation");

        // Round-trip: write → read again.
        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var reloaded = PptxPackageReader.Read(ms);

        var orig    = pres.Slides[0].Animations;
        var reloaded0 = reloaded.Slides[0].Animations;
        reloaded0.Should().HaveCount(orig.Count, "animation count must survive round-trip");

        var origMotion = orig.FirstOrDefault(a => a.Kind == AnimationKind.Motion);
        var rtMotion   = reloaded0.FirstOrDefault(a => a.Kind == AnimationKind.Motion);

        rtMotion.Should().NotBeNull();
        rtMotion!.Motion.Should().NotBeNull();
        rtMotion.Motion!.Segments.Should().HaveCount(origMotion!.Motion!.Segments.Count,
            "segment count must survive round-trip");
    }
}
