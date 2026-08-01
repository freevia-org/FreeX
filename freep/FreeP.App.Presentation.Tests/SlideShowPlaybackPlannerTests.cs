using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowPlaybackPlannerTests
{
    [Fact]
    public void PlanTransition_NormalizesRendererActionDurationAndDirection()
    {
        var cut = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Cut,
            DurationMs = 1
        });

        cut.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.ShowInstant);
        cut.DurationMs.Should().Be(SlideShowPlaybackPlanner.MinTransitionDurationMs);

        var push = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Push,
            Direction = TransitionDirection.Right,
            DurationMs = 325
        });

        push.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Push);
        push.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Push);
        push.DurationMs.Should().Be(325);
        push.IncomingOffsetX.Should().Be(-1);
        push.IncomingOffsetY.Should().Be(0);

        var cover = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Cover,
            Direction = TransitionDirection.Right,
            DurationMs = 325
        });

        cover.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Cover);
        cover.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Cover);

        var morph = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Morph,
            DurationMs = 750
        });

        morph.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Morph);
        morph.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Morph);
        morph.DurationMs.Should().Be(750);

        var cube = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Cube,
            Direction = TransitionDirection.Left,
            DurationMs = 640
        });

        cube.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Cube);
        cube.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Cube);
        cube.IncomingOffsetX.Should().Be(1);
        cube.IncomingOffsetY.Should().Be(0);
        cube.DurationMs.Should().Be(640);

        var flip = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Flip,
            Direction = TransitionDirection.Up
        });

        flip.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Flip);
        flip.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Flip);
        flip.IncomingOffsetX.Should().Be(0);
        flip.IncomingOffsetY.Should().Be(1);

        var rotate = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Rotate,
            Direction = TransitionDirection.Right
        });

        rotate.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Rotate);
        rotate.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Rotate);

        var honeycomb = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Honeycomb,
            Direction = TransitionDirection.Right,
            DurationMs = 400
        });

        honeycomb.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Honeycomb);
        honeycomb.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Honeycomb);
        honeycomb.DurationMs.Should().Be(400);

        var orbit = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Orbit,
            Direction = TransitionDirection.Left
        });

        orbit.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Orbit);
        orbit.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Orbit);

        var flythrough = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Flythrough,
            Direction = TransitionDirection.Right,
            DurationMs = 640
        });

        flythrough.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Flythrough);
        flythrough.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Flythrough);
        flythrough.DurationMs.Should().Be(640);

        var glitter = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Glitter,
            DurationMs = 640
        });

        glitter.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Glitter);
        glitter.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Glitter);

        var ripple = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Ripple,
            Direction = TransitionDirection.Right,
            DurationMs = 640
        });

        ripple.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Ripple);
        ripple.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Ripple);

        var wind = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Wind,
            Direction = TransitionDirection.Right
        });

        wind.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Wind);
        wind.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Wind);

        var curtains = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Curtains,
            Direction = TransitionDirection.Right
        });

        curtains.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Curtains);
        curtains.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Curtains);

        var shred = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Shred,
            Direction = TransitionDirection.Down
        });

        shred.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Shred);
        shred.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Shred);

        var peelOff = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.PeelOff,
            Direction = TransitionDirection.Left
        });

        peelOff.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.PageCurl);
        peelOff.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.PageCurl);

        var drape = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Drape,
            Direction = TransitionDirection.Down
        });

        drape.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Drape);
        drape.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Drape);

        var airplane = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Airplane,
            Direction = TransitionDirection.Right
        });

        airplane.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Flythrough);
        airplane.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Flythrough);

        var origami = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Origami,
            Direction = TransitionDirection.Down
        });

        origami.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.PageCurl);
        origami.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.PageCurl);

        var vortex = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Vortex,
            Direction = TransitionDirection.Left
        });

        vortex.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Vortex);
        vortex.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Vortex);

        var pageCurl = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.PageCurlSingle,
            Direction = TransitionDirection.Right
        });

        pageCurl.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.PageCurl);
        pageCurl.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.PageCurl);

        var doubleCurl = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.PageCurlDouble,
            Direction = TransitionDirection.Down
        });

        doubleCurl.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.PageCurl);
        doubleCurl.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.PageCurl);
    }

    [Theory]
    [InlineData(TransitionKind.Flip, TransitionDirection.Right, true, 0.02, 0, 0)]
    [InlineData(TransitionKind.Cube, TransitionDirection.Left, true, 0.08, -90, 0.12)]
    [InlineData(TransitionKind.Rotate, TransitionDirection.Down, false, 0.82, 90, 0.04)]
    [InlineData(TransitionKind.Switch, TransitionDirection.Right, true, 0.86, 90, 0.18)]
    [InlineData(TransitionKind.Orbit, TransitionDirection.Left, true, 0.64, -180, 0.25)]
    [InlineData(TransitionKind.Ferris, TransitionDirection.Down, false, 0.72, 75, 0.18)]
    [InlineData(TransitionKind.Flythrough, TransitionDirection.Right, true, 0.48, 0, 0.30)]
    [InlineData(TransitionKind.Airplane, TransitionDirection.Right, true, 0.48, 0, 0.30)]
    public void PerspectivePlanner_MapsAxisDirectionAndProjection(
        TransitionKind kind,
        TransitionDirection direction,
        bool horizontal,
        double startScale,
        double rotation,
        double travel)
    {
        var plan = SlideShowPerspectiveTransitionPlanner.Plan(new SlideTransition
        {
            Kind = kind,
            Direction = direction
        });

        plan.HorizontalAxis.Should().Be(horizontal);
        plan.StartScale.Should().Be(startScale);
        plan.StartRotationDegrees.Should().Be(rotation);
        plan.TravelFactor.Should().Be(travel);
    }

    [Fact]
    public void HoneycombPlanner_BuildsDeterministicHexagonalRevealCells()
    {
        var plan = SlideShowHoneycombTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Honeycomb,
            Direction = TransitionDirection.Right
        });

        plan.HorizontalAxis.Should().BeTrue();
        plan.Reverse.Should().BeTrue();

        var closed = SlideShowHoneycombTransitionPlanner.BuildPolygons(960, 540, 0, plan);
        var partial = SlideShowHoneycombTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);
        var open = SlideShowHoneycombTransitionPlanner.BuildPolygons(960, 540, 1, plan);
        var repeat = SlideShowHoneycombTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);

        closed.Should().BeEmpty();
        partial.Should().NotBeEmpty();
        open.Count.Should().BeGreaterThanOrEqualTo(partial.Count);
        partial.Should().HaveSameCount(repeat);
        partial.All(cell => cell.Points.Count == 6).Should().BeTrue();
        partial.SelectMany(cell => cell.Points)
            .Should().BeEquivalentTo(repeat.SelectMany(cell => cell.Points));
    }

    [Fact]
    public void GlitterPlanner_BuildsDeterministicSparkleCellReveal()
    {
        var plan = SlideShowGlitterTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Glitter
        });

        var closed = SlideShowGlitterTransitionPlanner.BuildPolygons(960, 540, 0, plan);
        var partial = SlideShowGlitterTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);
        var open = SlideShowGlitterTransitionPlanner.BuildPolygons(960, 540, 1, plan);
        var repeat = SlideShowGlitterTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);

        closed.Should().BeEmpty();
        partial.Should().NotBeEmpty();
        partial.Should().OnlyContain(cell => cell.Points.Count == 8);
        open.Should().HaveCount(1);
        open[0].Points.Should().HaveCount(4);
        partial.Should().BeEquivalentTo(repeat);
    }

    [Fact]
    public void RipplePlanner_BuildsDeterministicWavefront()
    {
        var plan = SlideShowRippleTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Ripple,
            Direction = TransitionDirection.Right
        });

        var closed = SlideShowRippleTransitionPlanner.BuildPolygons(960, 540, 0, plan);
        var partial = SlideShowRippleTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);
        var open = SlideShowRippleTransitionPlanner.BuildPolygons(960, 540, 1, plan);
        var repeat = SlideShowRippleTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);

        closed.Should().BeEmpty();
        partial.Should().HaveCount(1);
        partial[0].Points.Should().HaveCount(plan.SegmentCount);
        open.Should().HaveCount(1);
        open[0].Points.Should().HaveCount(4);
        partial.Should().BeEquivalentTo(repeat);
    }

    [Fact]
    public void WindPlanner_BuildsDeterministicStaggeredBands()
    {
        var plan = SlideShowWindTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Wind,
            Direction = TransitionDirection.Right
        });

        plan.HorizontalAxis.Should().BeTrue();
        plan.Reverse.Should().BeFalse();

        var closed = SlideShowWindTransitionPlanner.BuildPolygons(960, 540, 0, plan);
        var partial = SlideShowWindTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);
        var open = SlideShowWindTransitionPlanner.BuildPolygons(960, 540, 1, plan);
        var repeat = SlideShowWindTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);

        closed.Should().BeEmpty();
        partial.Should().NotBeEmpty();
        partial.Should().OnlyContain(band => band.Points.Count == 4);
        open.Should().HaveCount(1);
        open[0].Points.Should().HaveCount(4);
        partial.Should().BeEquivalentTo(repeat);
    }

    [Fact]
    public void CurtainsPlanner_BuildsDeterministicCenterOutPanels()
    {
        var plan = SlideShowCurtainsTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Curtains,
            Direction = TransitionDirection.Right
        });

        plan.HorizontalAxis.Should().BeTrue();
        plan.Reverse.Should().BeFalse();

        var closed = SlideShowCurtainsTransitionPlanner.BuildPolygons(960, 540, 0, plan);
        var partial = SlideShowCurtainsTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);
        var open = SlideShowCurtainsTransitionPlanner.BuildPolygons(960, 540, 1, plan);
        var repeat = SlideShowCurtainsTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);

        closed.Should().BeEmpty();
        partial.Should().NotBeEmpty();
        partial.Should().OnlyContain(panel => panel.Points.Count == 4);
        open.Should().HaveCount(1);
        open[0].Points.Should().HaveCount(4);
        partial.Should().BeEquivalentTo(repeat);
    }

    [Fact]
    public void ShredPlanner_BuildsDeterministicTornFragments()
    {
        var plan = SlideShowShredTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Shred,
            Direction = TransitionDirection.Right
        });

        plan.HorizontalAxis.Should().BeTrue();
        plan.Reverse.Should().BeFalse();

        var closed = SlideShowShredTransitionPlanner.BuildPolygons(960, 540, 0, plan);
        var partial = SlideShowShredTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);
        var open = SlideShowShredTransitionPlanner.BuildPolygons(960, 540, 1, plan);
        var repeat = SlideShowShredTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);

        closed.Should().BeEmpty();
        partial.Should().NotBeEmpty();
        partial.Should().OnlyContain(fragment => fragment.Points.Count == 4);
        open.Should().HaveCount(1);
        open[0].Points.Should().HaveCount(4);
        partial.Should().BeEquivalentTo(repeat);
    }

    [Fact]
    public void DrapePlanner_BuildsDeterministicWavyFront()
    {
        var plan = SlideShowDrapeTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Drape,
            Direction = TransitionDirection.Right
        });

        plan.HorizontalAxis.Should().BeTrue();
        plan.Reverse.Should().BeFalse();

        var closed = SlideShowDrapeTransitionPlanner.BuildPolygons(960, 540, 0, plan);
        var partial = SlideShowDrapeTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);
        var open = SlideShowDrapeTransitionPlanner.BuildPolygons(960, 540, 1, plan);
        var repeat = SlideShowDrapeTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);

        closed.Should().BeEmpty();
        partial.Should().NotBeEmpty();
        partial.Should().OnlyContain(segment => segment.Points.Count == 4);
        open.Should().HaveCount(1);
        open[0].Points.Should().HaveCount(4);
        partial.Should().BeEquivalentTo(repeat);
    }

    [Fact]
    public void VortexPlanner_BuildsDeterministicSpiralSectors()
    {
        var plan = SlideShowVortexTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Vortex,
            Direction = TransitionDirection.Left
        });

        plan.Reverse.Should().BeTrue();

        var closed = SlideShowVortexTransitionPlanner.BuildPolygons(960, 540, 0, plan);
        var partial = SlideShowVortexTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);
        var open = SlideShowVortexTransitionPlanner.BuildPolygons(960, 540, 1, plan);
        var repeat = SlideShowVortexTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);

        closed.Should().BeEmpty();
        partial.Should().HaveCount(plan.SectorCount + 1);
        partial.Should().OnlyContain(sector => sector.Points.Count == 4);
        open.Should().HaveCount(1);
        open[0].Points.Should().HaveCount(4);
        partial.Should().BeEquivalentTo(repeat);
    }

    [Fact]
    public void WarpPlanner_BuildsDeterministicElasticFront()
    {
        var plan = SlideShowWarpTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Warp,
            Direction = TransitionDirection.Right
        });

        plan.HorizontalAxis.Should().BeTrue();
        plan.Reverse.Should().BeFalse();

        var closed = SlideShowWarpTransitionPlanner.BuildPolygons(960, 540, 0, plan);
        var partial = SlideShowWarpTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);
        var open = SlideShowWarpTransitionPlanner.BuildPolygons(960, 540, 1, plan);
        var repeat = SlideShowWarpTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);

        closed.Should().BeEmpty();
        partial.Should().HaveCount(plan.SegmentCount);
        partial.Should().OnlyContain(segment => segment.Points.Count == 4);
        open.Should().HaveCount(1);
        open[0].Points.Should().HaveCount(4);
        partial.Should().BeEquivalentTo(repeat);
    }

    [Fact]
    public void FracturePlanner_BuildsDeterministicCenterFirstShards()
    {
        var plan = SlideShowFractureTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Fracture,
            Direction = TransitionDirection.Right
        });

        plan.Reverse.Should().BeFalse();

        var closed = SlideShowFractureTransitionPlanner.BuildPolygons(960, 540, 0, plan);
        var partial = SlideShowFractureTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);
        var open = SlideShowFractureTransitionPlanner.BuildPolygons(960, 540, 1, plan);
        var repeat = SlideShowFractureTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);

        closed.Should().BeEmpty();
        partial.Should().NotBeEmpty();
        partial.Should().OnlyContain(shard => shard.Points.Count == 4);
        open.Should().HaveCount(1);
        open[0].Points.Should().HaveCount(4);
        partial.Should().BeEquivalentTo(repeat);
    }

    [Fact]
    public void CrushPlanner_BuildsDeterministicCenteredAperture()
    {
        var rightPlan = SlideShowCrushTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Crush,
            Direction = TransitionDirection.Right
        });
        var leftPlan = SlideShowCrushTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Crush,
            Direction = TransitionDirection.Left
        });

        rightPlan.HorizontalAxis.Should().BeTrue();
        rightPlan.Reverse.Should().BeFalse();
        leftPlan.Reverse.Should().BeTrue();

        var closed = SlideShowCrushTransitionPlanner.BuildPolygons(960, 540, 0, rightPlan);
        var partial = SlideShowCrushTransitionPlanner.BuildPolygons(960, 540, 0.5, rightPlan);
        var open = SlideShowCrushTransitionPlanner.BuildPolygons(960, 540, 1, rightPlan);
        var repeat = SlideShowCrushTransitionPlanner.BuildPolygons(960, 540, 0.5, rightPlan);
        var reversed = SlideShowCrushTransitionPlanner.BuildPolygons(960, 540, 0.5, leftPlan);

        closed.Should().BeEmpty();
        partial.Should().ContainSingle();
        partial[0].Points.Should().HaveCount(4);
        open.Should().ContainSingle();
        open[0].Points.Should().HaveCount(4);
        partial.Should().BeEquivalentTo(repeat);
        reversed.Should().NotBeEquivalentTo(partial);
    }

    [Fact]
    public void PrismPlanner_BuildsDeterministicAngledFacets()
    {
        var rightPlan = SlideShowPrismTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Prism,
            Direction = TransitionDirection.Right
        });
        var leftPlan = SlideShowPrismTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Prism,
            Direction = TransitionDirection.Left
        });

        rightPlan.HorizontalAxis.Should().BeTrue();
        rightPlan.Reverse.Should().BeFalse();
        leftPlan.Reverse.Should().BeTrue();

        var closed = SlideShowPrismTransitionPlanner.BuildPolygons(960, 540, 0, rightPlan);
        var partial = SlideShowPrismTransitionPlanner.BuildPolygons(960, 540, 0.6, rightPlan);
        var open = SlideShowPrismTransitionPlanner.BuildPolygons(960, 540, 1, rightPlan);
        var repeat = SlideShowPrismTransitionPlanner.BuildPolygons(960, 540, 0.6, rightPlan);
        var reversed = SlideShowPrismTransitionPlanner.BuildPolygons(960, 540, 0.6, leftPlan);

        closed.Should().BeEmpty();
        partial.Should().NotBeEmpty();
        partial.Should().OnlyContain(facet => facet.Points.Count == 4);
        open.Should().ContainSingle();
        open[0].Points.Should().HaveCount(4);
        partial.Should().BeEquivalentTo(repeat);
        reversed.Should().NotBeEquivalentTo(partial);
    }

    [Fact]
    public void PrestigePlanner_BuildsDeterministicExpandingDiamond()
    {
        var rightPlan = SlideShowPrestigeTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Prestige,
            Direction = TransitionDirection.Right
        });
        var leftPlan = SlideShowPrestigeTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Prestige,
            Direction = TransitionDirection.Left
        });

        rightPlan.Reverse.Should().BeFalse();
        leftPlan.Reverse.Should().BeTrue();

        var closed = SlideShowPrestigeTransitionPlanner.BuildPolygons(960, 540, 0, rightPlan);
        var partial = SlideShowPrestigeTransitionPlanner.BuildPolygons(960, 540, 0.5, rightPlan);
        var open = SlideShowPrestigeTransitionPlanner.BuildPolygons(960, 540, 1, rightPlan);
        var repeat = SlideShowPrestigeTransitionPlanner.BuildPolygons(960, 540, 0.5, rightPlan);
        var reversed = SlideShowPrestigeTransitionPlanner.BuildPolygons(960, 540, 0.5, leftPlan);

        closed.Should().BeEmpty();
        partial.Should().ContainSingle();
        partial[0].Points.Should().HaveCount(4);
        open.Should().ContainSingle();
        open[0].Points.Should().HaveCount(4);
        partial.Should().BeEquivalentTo(repeat);
        reversed.Should().NotBeEquivalentTo(partial);
    }

    [Fact]
    public void PageCurlPlanner_FoldsOutgoingPageToEmptyClip()
    {
        var plan = SlideShowPageCurlTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.PageCurlSingle,
            Direction = TransitionDirection.Right
        });

        plan.HorizontalAxis.Should().BeTrue();
        plan.CurlFromEnd.Should().BeTrue();

        var closed = SlideShowPageCurlTransitionPlanner.BuildPolygons(960, 540, 0, plan);
        var partial = SlideShowPageCurlTransitionPlanner.BuildPolygons(960, 540, 0.5, plan);
        var open = SlideShowPageCurlTransitionPlanner.BuildPolygons(960, 540, 1, plan);

        closed.Should().HaveCount(1);
        closed[0].Points.Should().HaveCount(4);
        partial.Should().HaveCount(1);
        partial[0].Points.Should().HaveCount(5);
        open.Should().BeEmpty();

        var doublePlan = SlideShowPageCurlTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.PageCurlDouble,
            Direction = TransitionDirection.Down
        });
        var doublePartial = SlideShowPageCurlTransitionPlanner.BuildPolygons(
            960, 540, 0.5, doublePlan);

        doublePlan.DoubleFold.Should().BeTrue();
        doublePartial.Should().HaveCount(2);
        doublePartial.Should().OnlyContain(polygon => polygon.Points.Count == 5);

        var origamiPlan = SlideShowPageCurlTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Origami,
            Direction = TransitionDirection.Down
        });

        origamiPlan.DoubleFold.Should().BeTrue();
    }

    [Fact]
    public void MorphPlanner_PrefersStableIdsThenUniqueNames()
    {
        var source = new Slide();
        source.Shapes.Add(new SlideShape { Id = 10, Name = "Title", ExtentCxEmu = 1, ExtentCyEmu = 1 });
        source.Shapes.Add(new SlideShape { Id = 11, Name = "Body", ExtentCxEmu = 1, ExtentCyEmu = 1 });

        var target = new Slide();
        target.Shapes.Add(new SlideShape { Id = 10, Name = "Renamed title", ExtentCxEmu = 1, ExtentCyEmu = 1 });
        target.Shapes.Add(new SlideShape { Id = 99, Name = "Body", ExtentCxEmu = 1, ExtentCyEmu = 1 });

        var plan = SlideShowMorphPlanner.Plan(
            new SlideTransition { Kind = TransitionKind.Morph, MorphOption = "byObject" },
            source,
            target);

        plan.Matches.Should().HaveCount(2);
        plan.Matches[0].MatchKey.Should().Be("id:10");
        plan.Matches[1].MatchKey.Should().Be("name:body");
        plan.UnmatchedSourceCount.Should().Be(0);
        plan.UnmatchedTargetCount.Should().Be(0);
    }

    [Fact]
    public void MorphPlanner_LeavesAmbiguousNamesUnmatched()
    {
        var source = new Slide();
        source.Shapes.Add(new SlideShape { Id = 1, Name = "Card", ExtentCxEmu = 1, ExtentCyEmu = 1 });
        source.Shapes.Add(new SlideShape { Id = 2, Name = "Card", ExtentCxEmu = 1, ExtentCyEmu = 1 });
        var target = new Slide();
        target.Shapes.Add(new SlideShape { Id = 99, Name = "Card", ExtentCxEmu = 1, ExtentCyEmu = 1 });

        var plan = SlideShowMorphPlanner.Plan(
            new SlideTransition { Kind = TransitionKind.Morph }, source, target);

        plan.Matches.Should().BeEmpty();
        plan.UnmatchedSourceCount.Should().Be(2);
        plan.UnmatchedTargetCount.Should().Be(1);
    }

    [Fact]
    public void MorphPlanner_ByWordMatchesUniqueTextOverlapAfterIdentityPasses()
    {
        var source = new Slide();
        source.Shapes.Add(new SlideShape
        {
            Id = 10,
            Name = string.Empty,
            Text = "Revenue Q1",
            ExtentCxEmu = 1,
            ExtentCyEmu = 1
        });
        source.Shapes.Add(new SlideShape
        {
            Id = 11,
            Name = string.Empty,
            Text = "Expenses Q1",
            ExtentCxEmu = 1,
            ExtentCyEmu = 1
        });

        var target = new Slide();
        target.Shapes.Add(new SlideShape
        {
            Id = 99,
            Name = string.Empty,
            Text = "Revenue Q2",
            ExtentCxEmu = 1,
            ExtentCyEmu = 1
        });

        var plan = SlideShowMorphPlanner.Plan(
            new SlideTransition { Kind = TransitionKind.Morph, MorphOption = "byWord" },
            source,
            target);

        plan.Matches.Should().ContainSingle();
        plan.Matches[0].Source.Id.Should().Be(10);
        plan.Matches[0].Target.Id.Should().Be(99);
        plan.Matches[0].MatchKey.Should().Be("byWord:text:1");
        plan.Matches[0].Tokens.Should().ContainSingle();
        plan.Matches[0].Tokens[0].SourceText.Should().Be("Revenue");
        plan.Matches[0].Tokens[0].TargetText.Should().Be("Revenue");
        plan.Matches[0].Tokens[0].SourceStart.Should().Be(0);
        plan.Matches[0].Tokens[0].TargetStart.Should().Be(0);
        plan.UnmatchedSourceCount.Should().Be(1);
        plan.UnmatchedTargetCount.Should().Be(0);
    }

    [Fact]
    public void MorphPlanner_ByCharExposesOrderedTokenCorrespondence()
    {
        var source = new Slide();
        source.Shapes.Add(new SlideShape { Id = 10, Text = "ABCD", ExtentCxEmu = 1, ExtentCyEmu = 1 });
        var target = new Slide();
        target.Shapes.Add(new SlideShape { Id = 99, Text = "ABXCD", ExtentCxEmu = 1, ExtentCyEmu = 1 });

        var plan = SlideShowMorphPlanner.Plan(
            new SlideTransition { Kind = TransitionKind.Morph, MorphOption = "byChar" },
            source,
            target);

        plan.Matches.Should().ContainSingle();
        plan.Matches[0].Tokens.Select(token => token.SourceText)
            .Should().Equal("A", "B", "C", "D");
        plan.Matches[0].Tokens.Select(token => token.TargetText)
            .Should().Equal("A", "B", "C", "D");
        plan.Matches[0].Tokens.Select(token => token.TargetStart)
            .Should().Equal(0, 1, 3, 4);
    }

    [Fact]
    public void MorphPlanner_CreateTokenShapePreservesTargetGeometryAndFormats()
    {
        var shape = new SlideShape
        {
            Id = 7,
            OffsetXEmu = 100,
            OffsetYEmu = 200,
            ExtentCxEmu = 300,
            ExtentCyEmu = 400,
            TextBody = new TextBody()
        };
        shape.TextBody.Paragraphs.Add(new Paragraph
        {
            Runs = { new Run { Text = "Revenue Q2", Bold = true } }
        });

        var token = SlideShowMorphPlanner.CreateTokenShape(shape, 0, 7);

        token.OffsetXEmu.Should().Be(100);
        token.ExtentCyEmu.Should().Be(400);
        token.PlainText.Should().Be("Revenue");
        token.TextBody!.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
    }

    [Fact]
    public void MorphPlanner_ByCharLeavesTiedTextCandidatesUnmatched()
    {
        var source = new Slide();
        source.Shapes.Add(new SlideShape { Id = 10, Text = "ABCD", ExtentCxEmu = 1, ExtentCyEmu = 1 });
        source.Shapes.Add(new SlideShape { Id = 11, Text = "ABCE", ExtentCxEmu = 1, ExtentCyEmu = 1 });

        var target = new Slide();
        target.Shapes.Add(new SlideShape { Id = 99, Text = "ABCF", ExtentCxEmu = 1, ExtentCyEmu = 1 });

        var plan = SlideShowMorphPlanner.Plan(
            new SlideTransition { Kind = TransitionKind.Morph, MorphOption = "byChar" },
            source,
            target);

        plan.Matches.Should().BeEmpty();
        plan.UnmatchedSourceCount.Should().Be(2);
        plan.UnmatchedTargetCount.Should().Be(1);
    }

    [Fact]
    public void PlanTransition_DissolveUsesDedicatedAction()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Dissolve,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Dissolve);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Dissolve);
        plan.DurationMs.Should().Be(420);
    }

    [Fact]
    public void PlanTransition_FlashUsesDedicatedAction()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Flash,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Flash);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Flash);
        plan.DurationMs.Should().Be(420);
    }

    [Fact]
    public void PlanTransition_FlyUsesSerializedPushPlayback()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Fly,
            Direction = TransitionDirection.Right,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Push);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Push);
        plan.IncomingOffsetX.Should().Be(-1);
        plan.IncomingOffsetY.Should().Be(0);
        plan.DurationMs.Should().Be(420);
    }

    [Fact]
    public void PlanTransition_PanUsesDedicatedActionAndDirection()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Pan,
            Direction = TransitionDirection.Right,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Pan);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Pan);
        plan.IncomingOffsetX.Should().Be(-1);
        plan.IncomingOffsetY.Should().Be(0);
        SlideShowPlaybackPlanner.PanStartScale.Should().BeApproximately(1.12, 0.0001);
    }

    [Fact]
    public void PlanTransition_GalleryUsesTwoSurfaceActionAndDirection()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Gallery,
            Direction = TransitionDirection.Left,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Gallery);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Gallery);
        plan.IncomingOffsetX.Should().Be(1);
        plan.IncomingOffsetY.Should().Be(0);
        SlideShowPlaybackPlanner.GalleryStartScale.Should().BeApproximately(0.78, 0.0001);
        SlideShowPlaybackPlanner.GalleryOutgoingEndScale.Should().BeApproximately(0.88, 0.0001);
        SlideShowPlaybackPlanner.GalleryTravelFactor.Should().BeApproximately(0.55, 0.0001);
    }

    [Fact]
    public void PlanTransition_ConveyorUsesBeltActionAndDirection()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Conveyor,
            Direction = TransitionDirection.Down,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Conveyor);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Conveyor);
        plan.IncomingOffsetX.Should().Be(0);
        plan.IncomingOffsetY.Should().Be(-1);
        SlideShowPlaybackPlanner.ConveyorStartScale.Should().BeApproximately(0.90, 0.0001);
        SlideShowPlaybackPlanner.ConveyorOutgoingEndScale.Should().BeApproximately(0.90, 0.0001);
        SlideShowPlaybackPlanner.ConveyorTravelFactor.Should().BeApproximately(1.0, 0.0001);
        SlideShowPlaybackPlanner.ConveyorCrossAxisFactor.Should().BeApproximately(0.08, 0.0001);
        SlideShowPlaybackPlanner.ConveyorTiltDegrees.Should().BeApproximately(3.0, 0.0001);
    }

    [Fact]
    public void PlanTransition_WindowUsesCenteredApertureAction()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Window,
            Direction = TransitionDirection.Up,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Window);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Window);
        plan.IncomingOffsetX.Should().Be(0);
        plan.IncomingOffsetY.Should().Be(1);
        SlideShowPlaybackPlanner.WindowStartScale.Should().BeApproximately(0.92, 0.0001);
        SlideShowPlaybackPlanner.WindowInitialOpenFactor.Should().BeApproximately(0.18, 0.0001);
    }

    [Theory]
    [InlineData(TransitionDirection.In, true)]
    [InlineData(TransitionDirection.Out, false)]
    [InlineData(null, true)]
    public void PlanTransition_BoxUsesDedicatedActionAndDirection(
        TransitionDirection? direction,
        bool expandsFromCenter)
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Box,
            Direction = direction,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Box);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Box);
        plan.BoxExpandsFromCenter.Should().Be(expandsFromCenter);
    }

    [Theory]
    [InlineData(TransitionDirection.Right, -1, 0)]
    [InlineData(TransitionDirection.Left, 1, 0)]
    [InlineData(TransitionDirection.Down, 0, -1)]
    [InlineData(TransitionDirection.Up, 0, 1)]
    public void PlanTransition_RevealUsesDirectionalClipAction(
        TransitionDirection direction,
        double expectedOffsetX,
        double expectedOffsetY)
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Reveal,
            Direction = direction
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Reveal);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Reveal);
        plan.IncomingOffsetX.Should().Be(expectedOffsetX);
        plan.IncomingOffsetY.Should().Be(expectedOffsetY);
    }

    [Fact]
    public void PlanTransition_WipeUsesDirectionalRevealClipAction()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Wipe,
            Direction = TransitionDirection.Left,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Reveal);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Reveal);
        plan.IncomingOffsetX.Should().Be(1);
        plan.IncomingOffsetY.Should().Be(0);
        plan.DurationMs.Should().Be(420);
    }

    [Fact]
    public void PlanTransition_UncoverUsesOutgoingClipAction()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Uncover,
            Direction = TransitionDirection.Right,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Uncover);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Uncover);
        plan.IncomingOffsetX.Should().Be(-1);
        plan.IncomingOffsetY.Should().Be(0);
        plan.DurationMs.Should().Be(420);
    }

    [Fact]
    public void PlanTransition_DoorsUsesVerticalCenterOpening()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Doors,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Split);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Split);
        plan.SplitHorizontal.Should().BeTrue();
        plan.SplitOut.Should().BeTrue();
    }

    [Fact]
    public void PlanTransition_SplitUsesDedicatedActionAndPreservesGeometry()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Split,
            Direction = TransitionDirection.Vertical,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Split);
        plan.DurationMs.Should().Be(420);
        plan.SplitHorizontal.Should().BeFalse();
        plan.SplitOut.Should().BeTrue();
    }

    [Fact]
    public void PlanTransition_BlindsUsesDedicatedAction()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Blinds,
            Direction = TransitionDirection.Vertical,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Blinds);
        plan.BlindsHorizontal.Should().BeFalse();
    }

    [Theory]
    [InlineData(TransitionDirection.Horizontal, true)]
    [InlineData(TransitionDirection.Vertical, false)]
    public void PlanTransition_CombUsesSharedBlindsActionAndAxis(
        TransitionDirection direction,
        bool expectedHorizontal)
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Comb,
            Direction = direction,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Blinds);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Blinds);
        plan.BlindsHorizontal.Should().Be(expectedHorizontal);
        plan.DurationMs.Should().Be(420);
    }

    [Fact]
    public void PlanTransition_RandomBarsUsesDedicatedAction()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.RandomBar,
            Direction = TransitionDirection.Vertical,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.RandomBars);
        plan.RandomBarsHorizontal.Should().BeFalse();
    }

    [Theory]
    [InlineData(TransitionKind.Wheel, false)]
    [InlineData(TransitionKind.WheelReverse, true)]
    public void PlanTransition_WheelUsesDedicatedActionAndPreservesSpokes(
        TransitionKind kind,
        bool expectedReverse)
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = kind,
            WheelSpokeCount = 8,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Wheel);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Wheel);
        plan.WheelSpokeCount.Should().Be(8);
        plan.WheelReverse.Should().Be(expectedReverse);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(TransitionDirection.In, true)]
    [InlineData(TransitionDirection.Out, false)]
    public void PlanTransition_ZoomUsesDedicatedActionAndDirection(
        TransitionDirection? direction,
        bool expectedZoomIn)
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Zoom,
            Direction = direction,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Zoom);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Zoom);
        plan.ZoomIn.Should().Be(expectedZoomIn);
        plan.DurationMs.Should().Be(420);
    }

    [Theory]
    [InlineData(TransitionDirection.LeftDown, true)]
    [InlineData(TransitionDirection.RightUp, true)]
    [InlineData(TransitionDirection.LeftUp, false)]
    [InlineData(TransitionDirection.RightDown, false)]
    public void PlanTransition_StripsUsesDedicatedActionAndDirectionSlope(
        TransitionDirection direction,
        bool expectedSlopeDown)
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Strips,
            Direction = direction,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Strips);
        plan.DurationMs.Should().Be(420);
        plan.StripsSlopeDown.Should().Be(expectedSlopeDown);
    }

    [Fact]
    public void PlanAnimationStep_UsesControllerEntryStartDelays()
    {
        var slide = new Slide();
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 1,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick,
            DelayMs = 25,
            DurationMs = 100
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 2,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.WithPrevious,
            DelayMs = 40,
            DurationMs = 100
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 3,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.FlyIn,
            Trigger = AnimationTrigger.AfterPrevious,
            DelayMs = 30,
            DurationMs = 200
        });

        var step = SlideShowController.BuildSteps(slide).Single();
        var plans = SlideShowPlaybackPlanner.PlanAnimationStep(step);

        plans.Select(p => p.DelayMs).Should().Equal(25, 40, 155);
        plans.Select(p => p.EffectKind).Should().Equal(
            SlideShowShapeAnimationEffectKind.Appear,
            SlideShowShapeAnimationEffectKind.Fade,
            SlideShowShapeAnimationEffectKind.FlyIn);
    }

    [Fact]
    public void PlanShapeAnimation_NormalizesEffectIntentAndRevealTiming()
    {
        var exitFade = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 1,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Fade,
                DurationMs = 1
            },
            startDelayMs: -10);

        exitFade.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Fade);
        exitFade.DurationMs.Should().Be(SlideShowPlaybackPlanner.MinShapeAnimationDurationMs);
        exitFade.DelayMs.Should().Be(0);
        exitFade.FromOpacity.Should().Be(1);
        exitFade.ToOpacity.Should().Be(0);
        exitFade.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var flyIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 2,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.FlyIn,
                Direction = AnimationDirection.FromTopRight,
                DurationMs = 120
            },
            startDelayMs: 75);

        flyIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.FlyIn);
        flyIn.OffsetXFactor.Should().Be(1);
        flyIn.OffsetYFactor.Should().Be(-1);
        flyIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var verticalWipe = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 3,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Wipe,
                Direction = AnimationDirection.Vertical
            },
            startDelayMs: 0);

        verticalWipe.WipeHorizontal.Should().BeFalse();
    }

    [Theory]
    [InlineData(AnimationDirection.HorizontalIn, true, false)]
    [InlineData(AnimationDirection.HorizontalOut, true, true)]
    [InlineData(AnimationDirection.VerticalIn, false, false)]
    [InlineData(AnimationDirection.VerticalOut, false, true)]
    public void PlanShapeAnimation_MapsSplitDirectionToSharedAxisAndMovement(
        AnimationDirection direction,
        bool expectedHorizontal,
        bool expectedFromCenter)
    {
        var plan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 4,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Split,
                Direction = direction,
                DurationMs = 300,
            },
            startDelayMs: 0);

        plan.SplitHorizontal.Should().Be(expectedHorizontal);
        plan.SplitFromCenter.Should().Be(expectedFromCenter);
        var frame = SlideShowPlaybackFramePlanner.PlanFrame(plan, 150, 960, 540);
        frame.ClipKind.Should().Be(SlideShowAnimationClipKind.Split);
        frame.ClipHorizontal.Should().Be(expectedHorizontal);
        frame.ClipFromCenter.Should().Be(expectedFromCenter);
    }

    [Fact]
    public void PlanShapeAnimation_PreservesRepeatAndAutoReverseTiming()
    {
        var plan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 7,
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.Pulse,
                RepeatCount = 3,
                AutoReverse = true,
                DurationMs = 240
            },
            startDelayMs: 15);

        plan.RepeatCount.Should().Be(3);
        plan.RepeatIndefinitely.Should().BeFalse();
        plan.AutoReverse.Should().BeTrue();
        plan.DelayMs.Should().Be(15);
    }

    [Fact]
    public void PlanFrame_ProjectsFiniteRepeatAndAutoReversePasses()
    {
        var plan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 17,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Fade,
                DurationMs = 100,
                RepeatCount = 2,
                AutoReverse = true
            },
            startDelayMs: 25);

        var before = SlideShowPlaybackFramePlanner.PlanFrame(plan, 24, 960, 540);
        var firstPass = SlideShowPlaybackFramePlanner.PlanFrame(plan, 75, 960, 540);
        var reversePass = SlideShowPlaybackFramePlanner.PlanFrame(plan, 175, 960, 540);
        var complete = SlideShowPlaybackFramePlanner.PlanFrame(plan, 225, 960, 540);

        before.IsBeforeStart.Should().BeTrue();
        before.Progress.Should().Be(0);
        firstPass.Progress.Should().BeApproximately(0.5, 0.0001);
        reversePass.Progress.Should().BeApproximately(0.5, 0.0001);
        complete.IsComplete.Should().BeTrue();
        complete.Progress.Should().Be(0);
    }

    [Fact]
    public void PlanAnimationStepCheckpoints_IncludeFiniteRepeatDuration()
    {
        var step = new AnimationStep(new[]
        {
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 18,
                    Kind = AnimationKind.Emphasis,
                    Preset = AnimationPreset.Pulse,
                    DurationMs = 200,
                    RepeatCount = 3
                },
                StartDelayMs: 50)
        });

        var checkpoints = SlideShowPlaybackFramePlanner.PlanAnimationStepCheckpoints(step, 960, 540);

        checkpoints.Select(checkpoint => checkpoint.ElapsedMs)
            .Should().Equal(0, 325, 650);
        checkpoints[^1].Frames.Single().IsComplete.Should().BeTrue();
    }

    [Fact]
    public void PlanShapeAnimation_MapsAdvancedImportedEffects()
    {
        var split = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 4,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Split,
                Direction = AnimationDirection.Vertical,
                DurationMs = 300
            },
            startDelayMs: 20);

        split.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Split);
        split.WipeHorizontal.Should().BeFalse();
        split.DurationMs.Should().Be(300);
        split.DelayMs.Should().Be(20);
        split.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var randomBars = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 5,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.RandomBars,
                Direction = AnimationDirection.Horizontal
            },
            startDelayMs: 0);

        randomBars.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.RandomBars);
        randomBars.WipeHorizontal.Should().BeTrue();
        randomBars.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var horizontalBlinds = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 6,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Blinds,
                Direction = AnimationDirection.Horizontal,
                DurationMs = 275
            },
            startDelayMs: 5);

        horizontalBlinds.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Blinds);
        horizontalBlinds.BlindsHorizontal.Should().BeTrue();
        horizontalBlinds.BlindsBandCount.Should().Be(SlideShowPlaybackPlanner.BlindsBandCount);
        horizontalBlinds.DurationMs.Should().Be(275);
        horizontalBlinds.DelayMs.Should().Be(5);
        horizontalBlinds.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var verticalBlinds = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 6,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Blinds,
                Direction = AnimationDirection.Vertical
            },
            startDelayMs: 0);

        verticalBlinds.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Blinds);
        verticalBlinds.BlindsHorizontal.Should().BeFalse();
        verticalBlinds.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var boxIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 7,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Box,
                Direction = AnimationDirection.In,
                DurationMs = 260
            },
            startDelayMs: 35);

        boxIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Box);
        boxIn.BoxExpandsFromCenter.Should().BeTrue();
        boxIn.DurationMs.Should().Be(260);
        boxIn.DelayMs.Should().Be(35);
        boxIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var boxOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 8,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Box,
                Direction = AnimationDirection.Out
            },
            startDelayMs: 0);

        boxOut.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Box);
        boxOut.BoxExpandsFromCenter.Should().BeFalse();
        boxOut.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var horizontalCheckerboard = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 9,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Checkerboard,
                Direction = AnimationDirection.Horizontal,
                DurationMs = 325
            },
            startDelayMs: 15);

        horizontalCheckerboard.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Checkerboard);
        horizontalCheckerboard.CheckerboardHorizontal.Should().BeTrue();
        horizontalCheckerboard.CheckerboardRowCount.Should().Be(SlideShowPlaybackPlanner.CheckerboardRowCount);
        horizontalCheckerboard.CheckerboardColumnCount.Should().Be(SlideShowPlaybackPlanner.CheckerboardColumnCount);
        horizontalCheckerboard.DurationMs.Should().Be(325);
        horizontalCheckerboard.DelayMs.Should().Be(15);
        horizontalCheckerboard.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var verticalCheckerboard = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 10,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Checkerboard,
                Direction = AnimationDirection.Vertical
            },
            startDelayMs: 0);

        verticalCheckerboard.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Checkerboard);
        verticalCheckerboard.CheckerboardHorizontal.Should().BeFalse();
        verticalCheckerboard.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var circleIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 11,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Circle,
                Direction = AnimationDirection.In,
                DurationMs = 285
            },
            startDelayMs: 25);

        circleIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Circle);
        circleIn.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Circle);
        circleIn.GeometricMaskExpandsFromCenter.Should().BeTrue();
        circleIn.DurationMs.Should().Be(285);
        circleIn.DelayMs.Should().Be(25);
        circleIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var circleOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 12,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Circle,
                Direction = AnimationDirection.Out
            },
            startDelayMs: 0);

        circleOut.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Circle);
        circleOut.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Circle);
        circleOut.GeometricMaskExpandsFromCenter.Should().BeFalse();
        circleOut.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var diamondIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 13,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Diamond,
                Direction = AnimationDirection.In,
                DurationMs = 285
            },
            startDelayMs: 25);

        diamondIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Diamond);
        diamondIn.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Diamond);
        diamondIn.GeometricMaskExpandsFromCenter.Should().BeTrue();
        diamondIn.DurationMs.Should().Be(285);
        diamondIn.DelayMs.Should().Be(25);
        diamondIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var diamondOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 14,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Diamond,
                Direction = AnimationDirection.Out
            },
            startDelayMs: 0);

        diamondOut.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Diamond);
        diamondOut.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Diamond);
        diamondOut.GeometricMaskExpandsFromCenter.Should().BeFalse();
        diamondOut.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var plusIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 15,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Plus,
                Direction = AnimationDirection.In,
                DurationMs = 285
            },
            startDelayMs: 25);

        plusIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Plus);
        plusIn.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Plus);
        plusIn.GeometricMaskExpandsFromCenter.Should().BeTrue();
        plusIn.DurationMs.Should().Be(285);
        plusIn.DelayMs.Should().Be(25);
        plusIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var plusOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 16,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Plus,
                Direction = AnimationDirection.Out
            },
            startDelayMs: 0);

        plusOut.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Plus);
        plusOut.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Plus);
        plusOut.GeometricMaskExpandsFromCenter.Should().BeFalse();
        plusOut.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var stripsLeftDown = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 17,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Strips,
                Direction = AnimationDirection.LeftDown,
                DurationMs = 315
            },
            startDelayMs: 30);

        stripsLeftDown.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Strips);
        stripsLeftDown.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Strips);
        stripsLeftDown.GeometricMaskExpandsFromCenter.Should().BeTrue();
        stripsLeftDown.GeometricMaskStripCount.Should().Be(SlideShowPlaybackPlanner.StripsBandCount);
        stripsLeftDown.GeometricMaskStripsSlopeDown.Should().BeTrue();
        stripsLeftDown.DurationMs.Should().Be(315);
        stripsLeftDown.DelayMs.Should().Be(30);
        stripsLeftDown.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var stripsRightDownExit = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 18,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Strips,
                Direction = AnimationDirection.RightDown
            },
            startDelayMs: 0);

        stripsRightDownExit.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Strips);
        stripsRightDownExit.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Strips);
        stripsRightDownExit.GeometricMaskExpandsFromCenter.Should().BeFalse();
        stripsRightDownExit.GeometricMaskStripsSlopeDown.Should().BeFalse();
        stripsRightDownExit.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var wedgeIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 19,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Wedge,
                Direction = AnimationDirection.In,
                DurationMs = 285
            },
            startDelayMs: 25);

        wedgeIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Wedge);
        wedgeIn.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Wedge);
        wedgeIn.GeometricMaskExpandsFromCenter.Should().BeTrue();
        wedgeIn.DurationMs.Should().Be(285);
        wedgeIn.DelayMs.Should().Be(25);
        wedgeIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var wedgeOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 20,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Wedge,
                Direction = AnimationDirection.Out
            },
            startDelayMs: 0);

        wedgeOut.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Wedge);
        wedgeOut.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Wedge);
        wedgeOut.GeometricMaskExpandsFromCenter.Should().BeFalse();
        wedgeOut.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var wheelIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 21,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Wheel,
                Direction = AnimationDirection.In,
                WheelSpokeCount = 8,
                DurationMs = 285
            },
            startDelayMs: 25);

        wheelIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Wheel);
        wheelIn.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Wheel);
        wheelIn.GeometricMaskExpandsFromCenter.Should().BeTrue();
        wheelIn.GeometricMaskSpokeCount.Should().Be(8);
        wheelIn.DurationMs.Should().Be(285);
        wheelIn.DelayMs.Should().Be(25);
        wheelIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var wheelOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 22,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Wheel,
                Direction = AnimationDirection.Out,
                WheelSpokeCount = 0
            },
            startDelayMs: 0);

        wheelOut.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Wheel);
        wheelOut.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Wheel);
        wheelOut.GeometricMaskExpandsFromCenter.Should().BeFalse();
        wheelOut.GeometricMaskSpokeCount.Should().Be(SlideShowPlaybackPlanner.WheelSpokeCount);
        wheelOut.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var dissolve = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 23,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Dissolve,
                DurationMs = 240
            },
            startDelayMs: 5);

        dissolve.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Dissolve);
        dissolve.DurationMs.Should().Be(240);
        dissolve.DelayMs.Should().Be(5);
        dissolve.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var flashExit = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 24,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Flash
            },
            startDelayMs: 0);

        flashExit.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Flash);
        flashExit.FromOpacity.Should().Be(1);
        flashExit.ToOpacity.Should().Be(0);
        flashExit.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var spiral = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 25,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Spiral,
                DurationMs = 300
            },
            startDelayMs: 15);

        spiral.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Spiral);
        spiral.RotationDegrees.Should().Be(360);
        spiral.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var spiralOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 251,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Spiral,
                Direction = AnimationDirection.Out
            },
            startDelayMs: 0);

        spiralOut.RotationDegrees.Should().Be(-360);

        var swivelExit = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 26,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Swivel
            },
            startDelayMs: 0);

        swivelExit.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Swivel);
        swivelExit.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var swivelOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 261,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Swivel,
                Direction = AnimationDirection.Out
            },
            startDelayMs: 0);

        swivelOut.RotationDegrees.Should().Be(-360);

        var bounce = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 27,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Bounce,
                Direction = AnimationDirection.FromBottom
            },
            startDelayMs: 0);

        bounce.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Bounce);
        bounce.OffsetXFactor.Should().Be(0);
        bounce.OffsetYFactor.Should().Be(1);
        bounce.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var floatIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 28,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Float,
                Direction = AnimationDirection.FromTop
            },
            startDelayMs: 0);

        floatIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Float);
        floatIn.OffsetYFactor.Should().Be(-1);

        var swoop = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 29,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Swoop,
                Direction = AnimationDirection.FromBottomRight
            },
            startDelayMs: 0);

        swoop.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Swoop);
        swoop.OffsetXFactor.Should().Be(1);
        swoop.OffsetYFactor.Should().Be(1);

        var boomerangExit = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 30,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Boomerang,
                Direction = AnimationDirection.FromLeft
            },
            startDelayMs: 0);

        boomerangExit.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Boomerang);
        boomerangExit.OffsetXFactor.Should().Be(-1);
        boomerangExit.FromOpacity.Should().Be(1);
        boomerangExit.ToOpacity.Should().Be(0);
        boomerangExit.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var peekFromLeft = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 31,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Peek,
                Direction = AnimationDirection.FromLeft,
                DurationMs = 275
            },
            startDelayMs: 35);

        peekFromLeft.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Peek);
        peekFromLeft.OffsetXFactor.Should().Be(-1);
        peekFromLeft.OffsetYFactor.Should().Be(0);
        peekFromLeft.DurationMs.Should().Be(275);
        peekFromLeft.DelayMs.Should().Be(35);
        peekFromLeft.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var peekExitFromBottom = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 32,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Peek,
                Direction = AnimationDirection.FromBottom
            },
            startDelayMs: 0);

        peekExitFromBottom.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Peek);
        peekExitFromBottom.OffsetXFactor.Should().Be(0);
        peekExitFromBottom.OffsetYFactor.Should().Be(1);
        peekExitFromBottom.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var crawlFromRight = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 33,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Crawl,
                Direction = AnimationDirection.FromRight,
                DurationMs = 310
            },
            startDelayMs: 45);

        crawlFromRight.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Crawl);
        crawlFromRight.OffsetXFactor.Should().Be(1);
        crawlFromRight.OffsetYFactor.Should().Be(0);
        crawlFromRight.DurationMs.Should().Be(310);
        crawlFromRight.DelayMs.Should().Be(45);
        crawlFromRight.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var crawlExitFromTop = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 34,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Crawl,
                Direction = AnimationDirection.FromTop
            },
            startDelayMs: 0);

        crawlExitFromTop.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Crawl);
        crawlExitFromTop.OffsetXFactor.Should().Be(0);
        crawlExitFromTop.OffsetYFactor.Should().Be(-1);
        crawlExitFromTop.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var growShrink = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 35,
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.Grow,
                DurationMs = 450
            },
            startDelayMs: 10);

        growShrink.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.GrowShrink);
        growShrink.FromScale.Should().Be(1);
        growShrink.ToScale.Should().Be(1);
        growShrink.PeakScale.Should().BeGreaterThan(1);
        growShrink.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);
    }

    [Theory]
    [InlineData(AnimationPreset.Grow, 0.25, 0.25)]
    [InlineData(AnimationPreset.Grow, 1.5, 1.5)]
    [InlineData(AnimationPreset.Shrink, 0.5, 0.5)]
    [InlineData(AnimationPreset.Shrink, 4.0, 4.0)]
    [InlineData(AnimationPreset.Grow, 1.35, 1.35)]
    public void PlanShapeAnimation_UsesSharedGrowShrinkAmountScale(
        AnimationPreset preset,
        double scale,
        double expectedPeakScale)
    {
        var plan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 35,
                Kind = AnimationKind.Emphasis,
                Preset = preset,
                ScaleBehavior = AnimationScaleBehavior.FromTo(scale),
            },
            startDelayMs: 0);

        plan.PeakScale.Should().Be(expectedPeakScale);
        var frame = SlideShowPlaybackFramePlanner.PlanFrame(plan, 250, 960, 540);
        frame.Scale.Should().Be(expectedPeakScale);
    }

    [Fact]
    public void PlanShapeAnimation_ProjectsAsymmetricGrowShrinkScaleAxesThroughFrames()
    {
        var plan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 36,
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.Grow,
                DurationMs = 400,
                ScaleBehavior = new AnimationScaleBehavior
                {
                    FromX = "100000",
                    FromY = "100000",
                    ToX = "150000",
                    ToY = "75000",
                },
            },
            startDelayMs: 20);

        plan.FromScaleX.Should().Be(1);
        plan.FromScaleY.Should().Be(1);
        plan.ToScaleX.Should().Be(1);
        plan.ToScaleY.Should().Be(1);
        plan.PeakScaleX.Should().Be(1.5);
        plan.PeakScaleY.Should().Be(0.75);

        var quarterFrame = SlideShowPlaybackFramePlanner.PlanFrame(plan, 120, 960, 540);
        quarterFrame.ScaleX.Should().BeApproximately(1.25, 0.0001);
        quarterFrame.ScaleY.Should().BeApproximately(0.875, 0.0001);
        quarterFrame.Scale.Should().Be(quarterFrame.ScaleX);
        quarterFrame.FromScaleX.Should().Be(plan.FromScaleX);
        quarterFrame.FromScaleY.Should().Be(plan.FromScaleY);
        quarterFrame.ToScaleX.Should().Be(plan.ToScaleX);
        quarterFrame.ToScaleY.Should().Be(plan.ToScaleY);
        quarterFrame.PeakScaleX.Should().Be(plan.PeakScaleX);
        quarterFrame.PeakScaleY.Should().Be(plan.PeakScaleY);

        var peakFrame = SlideShowPlaybackFramePlanner.PlanFrame(plan, 220, 960, 540);
        peakFrame.ScaleX.Should().Be(plan.PeakScaleX);
        peakFrame.ScaleY.Should().Be(plan.PeakScaleY);
        peakFrame.EvidenceSummary.Should().Contain("scale-x 1.5");
        peakFrame.EvidenceSummary.Should().Contain("scale-y 0.75");
    }

    [Fact]
    public void PlanShapeAnimation_PreSamplesMotionPathKeyframes()
    {
        var path = new MotionPath();
        path.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        path.Segments.Add(MotionPathSegment.LineTo(0.5, 0.25));

        var plan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 9,
                Kind = AnimationKind.Motion,
                Motion = path,
                DurationMs = 250
            },
            startDelayMs: 15);

        plan.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.MotionPath);
        plan.DelayMs.Should().Be(15);
        plan.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);
        plan.MotionKeyFrames.Should().HaveCount(SlideShowPlaybackPlanner.MotionPathFrameCount + 1);
        plan.MotionKeyFrames[0].Should().Be(new SlideShowMotionPathKeyFrame(0, 0, 0));
        plan.MotionKeyFrames[^1].OffsetXFactor.Should().BeApproximately(0.5, 0.0001);
        plan.MotionKeyFrames[^1].OffsetYFactor.Should().BeApproximately(0.25, 0.0001);
    }

    [Fact]
    public void ReverseMotionPathKeyFrames_ReversesProgressAndPreservesOffsets()
    {
        var reversed = SlideShowPlaybackPlanner.ReverseMotionPathKeyFrames(
        [
            new SlideShowMotionPathKeyFrame(0, 0, 0),
            new SlideShowMotionPathKeyFrame(0.5, 0.2, 0.1),
            new SlideShowMotionPathKeyFrame(1, 0.5, 0.25),
        ]);

        reversed.Should().Equal(
            new SlideShowMotionPathKeyFrame(0, 0.5, 0.25),
            new SlideShowMotionPathKeyFrame(0.5, 0.2, 0.1),
            new SlideShowMotionPathKeyFrame(1, 0, 0));
    }

    [Fact]
    public void PlanShapeAnimation_PreservesImportedEmphasisPresetFamilies()
    {
        var expected = new Dictionary<AnimationPreset, SlideShowShapeAnimationEffectKind>
        {
            [AnimationPreset.Teeter] = SlideShowShapeAnimationEffectKind.Teeter,
            [AnimationPreset.Blink] = SlideShowShapeAnimationEffectKind.Blink,
            [AnimationPreset.ColorPulse] = SlideShowShapeAnimationEffectKind.ColorPulse,
            [AnimationPreset.ChangeColor] = SlideShowShapeAnimationEffectKind.ChangeColor,
            [AnimationPreset.GrowWithColor] = SlideShowShapeAnimationEffectKind.GrowWithColor,
            [AnimationPreset.Wave] = SlideShowShapeAnimationEffectKind.Wave,
            [AnimationPreset.Shimmer] = SlideShowShapeAnimationEffectKind.Shimmer,
            [AnimationPreset.Bold] = SlideShowShapeAnimationEffectKind.Bold,
            [AnimationPreset.Underline] = SlideShowShapeAnimationEffectKind.Underline
        };

        foreach (var (preset, effectKind) in expected)
        {
            var plan = SlideShowPlaybackPlanner.PlanShapeAnimation(
                new ShapeAnimation
                {
                    ShapeId = 70,
                    Kind = AnimationKind.Emphasis,
                    Preset = preset,
                    DurationMs = 600
                },
                startDelayMs: 25);

            plan.EffectKind.Should().Be(effectKind);
            plan.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);
        }
    }

    [Fact]
    public void PlanFrame_ProjectsImportedEmphasisTracks()
    {
        var blinkPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 71,
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.Blink,
                DurationMs = 400
            },
            startDelayMs: 0);
        var blinkFrame = SlideShowPlaybackFramePlanner.PlanFrame(blinkPlan, 100, 960, 540);
        blinkFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.Opacity);
        blinkFrame.Opacity.Should().BeApproximately(0.15, 0.0001);

        var teeterPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 72,
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.Teeter,
                DurationMs = 400
            },
            startDelayMs: 0);
        var teeterFrame = SlideShowPlaybackFramePlanner.PlanFrame(teeterPlan, 150, 960, 540);
        teeterFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.Rotate);
        teeterFrame.RotationDegrees.Should().BeApproximately(-10, 0.0001);

        var swivelPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 74,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Swivel,
                DurationMs = 400
            },
            startDelayMs: 0);
        var swivelEdgeFrame = SlideShowPlaybackFramePlanner.PlanFrame(swivelPlan, 100, 960, 540);
        swivelEdgeFrame.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Swivel);
        swivelEdgeFrame.RotationDegrees.Should().BeApproximately(90, 0.0001);
        swivelEdgeFrame.HorizontalScale.Should().BeApproximately(0.04, 0.0001);
        var swivelFaceFrame = SlideShowPlaybackFramePlanner.PlanFrame(swivelPlan, 200, 960, 540);
        swivelFaceFrame.HorizontalScale.Should().BeApproximately(1, 0.0001);

        var colorPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 73,
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.GrowWithColor,
                DurationMs = 400
            },
            startDelayMs: 0);
        var colorFrame = SlideShowPlaybackFramePlanner.PlanFrame(colorPlan, 100, 960, 540);
        colorFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.Emphasis);
        colorFrame.Scale.Should().BeGreaterThan(1);
        colorFrame.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.GrowWithColor);
    }

    [Fact]
    public void PlanFallbackAnimation_OnlyPlansEmphasisFlash()
    {
        var flash = SlideShowPlaybackPlanner.PlanFallbackAnimation(
            new ShapeAnimation
            {
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.Pulse,
                DurationMs = 25
            },
            startDelayMs: -1);

        flash.Should().NotBeNull();
        flash!.DurationMs.Should().Be(SlideShowPlaybackPlanner.MinFallbackAnimationDurationMs);
        flash.DelayMs.Should().Be(0);
        flash.FromOpacity.Should().Be(1);
        flash.FlashOpacity.Should().Be(0.5);

        SlideShowPlaybackPlanner.PlanFallbackAnimation(
            new ShapeAnimation { Kind = AnimationKind.Entrance },
            startDelayMs: 0).Should().BeNull();
    }

    [Theory]
    [InlineData(AnimationKind.Entrance, true, false)]
    [InlineData(AnimationKind.Motion, true, false)]
    [InlineData(AnimationKind.Exit, false, true)]
    [InlineData(AnimationKind.Emphasis, false, false)]
    public void PlanFallbackVisibility_PreservesShapeStepSemantics(
        AnimationKind kind,
        bool suppressAtStart,
        bool suppressAtCompletion)
    {
        var plan = SlideShowPlaybackPlanner.PlanFallbackVisibility(
            new ShapeAnimation { ShapeId = 7, Kind = kind });

        plan.SuppressAtStart.Should().Be(suppressAtStart);
        plan.SuppressAtCompletion.Should().Be(suppressAtCompletion);
    }

    [Fact]
    public void PlanFrame_ProjectsTranslateAndMotionPathEvidenceInSlideCoordinates()
    {
        var flyInPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 41,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.FlyIn,
                Direction = AnimationDirection.FromRight,
                DurationMs = 400
            },
            startDelayMs: 0);

        var flyInFrame = SlideShowPlaybackFramePlanner.PlanFrame(
            flyInPlan,
            elapsedMs: 200,
            slideWidthDip: 960,
            slideHeightDip: 540);

        flyInFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.Translate);
        flyInFrame.Progress.Should().Be(0.5);
        flyInFrame.TranslateXFactor.Should().Be(0.5);
        flyInFrame.TranslateXDip.Should().Be(480);
        flyInFrame.TranslateYDip.Should().Be(0);
        flyInFrame.EvidenceSummary.Should().Contain("FlyIn Translate");

        var path = new MotionPath();
        path.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        path.Segments.Add(MotionPathSegment.LineTo(0.5, 0.25));
        var motionPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 42,
                Kind = AnimationKind.Motion,
                Motion = path,
                DurationMs = 1000
            },
            startDelayMs: 0);

        var motionFrame = SlideShowPlaybackFramePlanner.PlanFrame(
            motionPlan,
            elapsedMs: 500,
            slideWidthDip: 960,
            slideHeightDip: 540);

        motionFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.MotionPath);
        motionFrame.TranslateXFactor.Should().BeApproximately(0.25, 0.0001);
        motionFrame.TranslateYFactor.Should().BeApproximately(0.125, 0.0001);
        motionFrame.TranslateXDip.Should().BeApproximately(240, 0.0001);
        motionFrame.TranslateYDip.Should().BeApproximately(67.5, 0.0001);
    }

    [Fact]
    public void PlanFrame_ProjectsAdvancedClipAndScaleVisualEvidence()
    {
        var wheelPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 51,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Wheel,
                Direction = AnimationDirection.In,
                WheelSpokeCount = 8,
                DurationMs = 300
            },
            startDelayMs: 0);

        var wheelFrame = SlideShowPlaybackFramePlanner.PlanFrame(
            wheelPlan,
            elapsedMs: 150,
            slideWidthDip: 960,
            slideHeightDip: 540);

        wheelFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.Clip);
        wheelFrame.ClipKind.Should().Be(SlideShowAnimationClipKind.Wheel);
        wheelFrame.ClipProgress.Should().Be(0.5);
        wheelFrame.ClipSpokeCount.Should().Be(8);
        wheelFrame.EvidenceSummary.Should().Contain("clip Wheel 0.5");

        var growPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 52,
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.Grow,
                DurationMs = 400
            },
            startDelayMs: 0);

        var growFrame = SlideShowPlaybackFramePlanner.PlanFrame(
            growPlan,
            elapsedMs: 200,
            slideWidthDip: 960,
            slideHeightDip: 540);

        growFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.Scale);
        growFrame.Scale.Should().Be(growPlan.PeakScale);
        growFrame.Opacity.Should().Be(1);
        growFrame.EvidenceSummary.Should().Contain("GrowShrink Scale");
    }

    [Fact]
    public void PlanAnimationStepFrames_UsesControllerDelaysForSharedHostEvidence()
    {
        var step = new AnimationStep(
        [
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 61,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.Appear,
                    DurationMs = 100
                },
                StartDelayMs: 0),
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 62,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.Fade,
                    DurationMs = 200
                },
                StartDelayMs: 150)
        ]);

        var frames = SlideShowPlaybackFramePlanner.PlanAnimationStepFrames(
            step,
            elapsedMs: 100,
            slideWidthDip: 960,
            slideHeightDip: 540);

        frames.Should().HaveCount(2);
        frames[0].ShapeId.Should().Be(61);
        frames[0].IsComplete.Should().BeTrue();
        frames[1].ShapeId.Should().Be(62);
        frames[1].IsBeforeStart.Should().BeTrue();
        frames[1].Progress.Should().Be(0);
        frames[1].Opacity.Should().Be(0);
    }

    [Fact]
    public void PlanAnimationStepCheckpoints_ProjectsStepLevelPlaybackEvidence()
    {
        var step = new AnimationStep(
        [
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 71,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.Fade,
                    DurationMs = 200
                },
                StartDelayMs: 0),
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 72,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.FlyIn,
                    Direction = AnimationDirection.FromBottom,
                    DurationMs = 300
                },
                StartDelayMs: 400)
        ]);

        var checkpoints = SlideShowPlaybackFramePlanner.PlanAnimationStepCheckpoints(
            step,
            slideWidthDip: 960,
            slideHeightDip: 540);

        checkpoints.Select(checkpoint => checkpoint.Checkpoint)
            .Should()
            .Equal("start", "midpoint", "complete");
        checkpoints.Select(checkpoint => checkpoint.ElapsedMs)
            .Should()
            .Equal(0, 350, 700);
        checkpoints.Should().OnlyContain(checkpoint => checkpoint.Frames.Count == 2);

        checkpoints[0].Frames[0].IsBeforeStart.Should().BeFalse();
        checkpoints[0].Frames[1].IsBeforeStart.Should().BeTrue();
        checkpoints[1].Frames[0].IsComplete.Should().BeTrue();
        checkpoints[1].Frames[1].IsBeforeStart.Should().BeTrue();
        checkpoints[2].Frames.Should().OnlyContain(frame => frame.IsComplete);
        checkpoints[2].Frames[1].TranslateYFactor.Should().Be(0);
        checkpoints[2].EvidenceSummary.Should().Be("complete at 700ms: 2 frame(s); 0 active; 2 complete");
    }

    [Fact]
    public void BuildAnimationStepPlaybackReadinessPlan_ProjectsSharedNoComHostRows()
    {
        var step = new AnimationStep(
        [
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 81,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.Wheel,
                    Direction = AnimationDirection.In,
                    WheelSpokeCount = 6,
                    DurationMs = 300
                },
                StartDelayMs: 0),
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 82,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.FlyIn,
                    Direction = AnimationDirection.FromRight,
                    DurationMs = 250
                },
                StartDelayMs: 175)
        ]);

        var readiness = SlideShowPlaybackFramePlanner.BuildAnimationStepPlaybackReadinessPlan(
            step,
            slideIndex: 2,
            stepIndex: 4,
            slideWidthDip: 960,
            slideHeightDip: 540,
            scenarioId: "Deck A/Playback");

        readiness.ScenarioId.Should().Be("deck-a-playback");
        readiness.SlideIndex.Should().Be(2);
        readiness.StepIndex.Should().Be(4);
        readiness.AnimationEntryCount.Should().Be(2);
        readiness.CheckpointCount.Should().Be(3);
        readiness.DelayedEntryCount.Should().Be(1);
        readiness.TrackKinds.Should().Equal(
            SlideShowAnimationVisualTrackKind.Clip,
            SlideShowAnimationVisualTrackKind.Translate);
        readiness.ClipKinds.Should().Equal(SlideShowAnimationClipKind.Wheel);
        readiness.HasSharedHostParity.Should().BeTrue();
        readiness.HostRows.Select(row => row.Host)
            .Should()
            .Equal(SlideShowPlaybackReadinessHost.Wpf, SlideShowPlaybackReadinessHost.Avalonia);
        readiness.HostRows.Should().OnlyContain(row => row.RequiresPowerPointCom == false);
        readiness.HostRows.Should().OnlyContain(row => row.EvidenceId.StartsWith(
            "deck-a-playback-slide-3-step-5-",
            StringComparison.Ordinal));
        readiness.EvidenceLines.Should().Contain("Shared host rows: WPF/Avalonia; PowerPoint COM required: false");
    }
}
