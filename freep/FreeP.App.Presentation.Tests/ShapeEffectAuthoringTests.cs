using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ShapeEffectAuthoringTests
{
    [Fact]
    public void Planner_UsesStablePowerPointShadowPresets()
    {
        var subtle = ShapeEffectAuthoringPlanner.Resolve(ShapeShadowPreset.Subtle);
        var offset = ShapeEffectAuthoringPlanner.Resolve(ShapeShadowPreset.Offset);

        subtle.Enabled.Should().BeTrue();
        subtle.Color.Should().Be(SrgbColor.Black);
        subtle.Alpha.Should().Be(0x55);
        subtle.BlurRadEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(4));
        subtle.DistEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(2));
        subtle.DirDeg.Should().Be(45);
        offset.Alpha.Should().Be(0x80);
        offset.BlurRadEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(6));
        offset.DistEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(4));
    }

    [Fact]
    public void SetShapeShadowCommand_PreservesOtherEffectsAndSupportsUndo()
    {
        var presentation = Presentation.CreateEmpty();
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            Effects = new ShapeEffects
            {
                HasGlow = true,
                GlowColor = new SrgbColor(0x11, 0x22, 0x33),
                GlowRadiusEmu = 1234,
            },
        };
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetShapeShadowCommand(
            0,
            shape.Id,
            ShapeEffectAuthoringPlanner.Resolve(ShapeShadowPreset.Offset)));

        shape.Effects.Should().NotBeNull();
        shape.Effects!.HasOuterShadow.Should().BeTrue();
        shape.Effects.OuterShadowAlpha.Should().Be(0x80);
        shape.Effects.HasGlow.Should().BeTrue();
        shape.Effects.GlowColor.Should().Be(new SrgbColor(0x11, 0x22, 0x33));
        shape.Effects.GlowRadiusEmu.Should().Be(1234);

        bus.Undo();

        shape.Effects.Should().NotBeNull();
        shape.Effects!.HasOuterShadow.Should().BeFalse();
        shape.Effects.HasGlow.Should().BeTrue();
        shape.Effects.GlowRadiusEmu.Should().Be(1234);
    }

    [Fact]
    public void SetShapeShadowCommand_NoneRemovesOnlyOuterShadow()
    {
        var presentation = Presentation.CreateEmpty();
        var shape = new SlideShape
        {
            Id = 8,
            Kind = SlideShapeKind.AutoShape,
            Effects = new ShapeEffects
            {
                HasOuterShadow = true,
                OuterShadowAlpha = 0x80,
                HasSoftEdge = true,
                SoftEdgeRadEmu = 4321,
            },
        };
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetShapeShadowCommand(0, shape.Id, ShapeShadowValues.None));

        shape.Effects.Should().NotBeNull();
        shape.Effects!.HasOuterShadow.Should().BeFalse();
        shape.Effects.HasSoftEdge.Should().BeTrue();
        shape.Effects.SoftEdgeRadEmu.Should().Be(4321);
    }

    [Fact]
    public void Planner_UsesStablePowerPointGlowPresets()
    {
        var subtle = ShapeEffectAuthoringPlanner.ResolveGlow(ShapeGlowPreset.Subtle);
        var strong = ShapeEffectAuthoringPlanner.ResolveGlow(ShapeGlowPreset.Strong);

        subtle.Enabled.Should().BeTrue();
        subtle.Color.Should().Be(new SrgbColor(0xFF, 0xC0, 0x00));
        subtle.Alpha.Should().Be(0x66);
        subtle.RadiusEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(4));
        strong.Alpha.Should().Be(0xA0);
        strong.RadiusEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(8));
    }

    [Fact]
    public void SetShapeGlowCommand_PreservesOtherEffectsAndSupportsUndo()
    {
        var presentation = Presentation.CreateEmpty();
        var shape = new SlideShape
        {
            Id = 9,
            Kind = SlideShapeKind.AutoShape,
            Effects = new ShapeEffects
            {
                HasOuterShadow = true,
                OuterShadowAlpha = 0x80,
                HasSoftEdge = true,
                SoftEdgeRadEmu = 321,
            },
        };
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetShapeGlowCommand(
            0,
            shape.Id,
            ShapeEffectAuthoringPlanner.ResolveGlow(ShapeGlowPreset.Strong)));

        shape.Effects.Should().NotBeNull();
        shape.Effects!.HasGlow.Should().BeTrue();
        shape.Effects.GlowAlpha.Should().Be(0xA0);
        shape.Effects.GlowRadiusEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(8));
        shape.Effects.HasOuterShadow.Should().BeTrue();
        shape.Effects.HasSoftEdge.Should().BeTrue();

        bus.Undo();

        shape.Effects.Should().NotBeNull();
        shape.Effects!.HasGlow.Should().BeFalse();
        shape.Effects.HasOuterShadow.Should().BeTrue();
        shape.Effects.HasSoftEdge.Should().BeTrue();
        shape.Effects.SoftEdgeRadEmu.Should().Be(321);
    }
}
