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

    [Fact]
    public void Planner_UsesStablePowerPointSoftEdgePresets()
    {
        var subtle = ShapeEffectAuthoringPlanner.ResolveSoftEdge(ShapeSoftEdgePreset.Subtle);
        var strong = ShapeEffectAuthoringPlanner.ResolveSoftEdge(ShapeSoftEdgePreset.Strong);

        subtle.Enabled.Should().BeTrue();
        subtle.RadiusEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(4));
        strong.Enabled.Should().BeTrue();
        strong.RadiusEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(8));
    }

    [Fact]
    public void SetShapeSoftEdgeCommand_PreservesOtherEffectsAndSupportsUndo()
    {
        var presentation = Presentation.CreateEmpty();
        var shape = new SlideShape
        {
            Id = 10,
            Kind = SlideShapeKind.AutoShape,
            Effects = new ShapeEffects
            {
                HasGlow = true,
                GlowAlpha = 0xA0,
                HasOuterShadow = true,
                OuterShadowAlpha = 0x80,
            },
        };
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetShapeSoftEdgeCommand(
            0,
            shape.Id,
            ShapeEffectAuthoringPlanner.ResolveSoftEdge(ShapeSoftEdgePreset.Strong)));

        shape.Effects.Should().NotBeNull();
        shape.Effects!.HasSoftEdge.Should().BeTrue();
        shape.Effects.SoftEdgeRadEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(8));
        shape.Effects.HasGlow.Should().BeTrue();
        shape.Effects.HasOuterShadow.Should().BeTrue();

        bus.Undo();

        shape.Effects.Should().NotBeNull();
        shape.Effects!.HasSoftEdge.Should().BeFalse();
        shape.Effects.HasGlow.Should().BeTrue();
        shape.Effects.HasOuterShadow.Should().BeTrue();
    }

    [Fact]
    public void Planner_UsesStablePowerPointBevelPresets()
    {
        var subtle = ShapeEffectAuthoringPlanner.ResolveBevel(ShapeBevelPreset.Subtle);
        var strong = ShapeEffectAuthoringPlanner.ResolveBevel(ShapeBevelPreset.Strong);

        subtle.Enabled.Should().BeTrue();
        subtle.PresetName.Should().Be("circle");
        subtle.WidthEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(1));
        subtle.HeightEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(1));
        strong.Enabled.Should().BeTrue();
        strong.PresetName.Should().Be("relaxedInset");
        strong.WidthEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(3));
        strong.HeightEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(3));
    }

    [Fact]
    public void SetShapeBevelCommand_PreservesOtherEffectsAndSupportsUndo()
    {
        var presentation = Presentation.CreateEmpty();
        var shape = new SlideShape
        {
            Id = 11,
            Kind = SlideShapeKind.AutoShape,
            Effects = new ShapeEffects
            {
                HasGlow = true,
                GlowRadiusEmu = 321,
                BevelTop = new BevelInfo { PresetName = "cross" },
            },
        };
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetShapeBevelCommand(
            0,
            shape.Id,
            ShapeEffectAuthoringPlanner.ResolveBevel(ShapeBevelPreset.Strong)));

        shape.Effects.Should().NotBeNull();
        shape.Effects!.BevelTop.Should().NotBeNull();
        shape.Effects.BevelTop!.PresetName.Should().Be("relaxedInset");
        shape.Effects.BevelBottom.Should().NotBeNull();
        shape.Effects.BevelBottom!.WidthEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(3));
        shape.Effects.HasGlow.Should().BeTrue();

        bus.Undo();

        shape.Effects.Should().NotBeNull();
        shape.Effects!.BevelTop!.PresetName.Should().Be("cross");
        shape.Effects.BevelBottom.Should().BeNull();
        shape.Effects.HasGlow.Should().BeTrue();
    }

    [Fact]
    public void Planner_UsesStablePowerPointShape3dPresets()
    {
        var subtle = ShapeEffectAuthoringPlanner.ResolveShape3d(Shape3dPreset.Subtle);
        var strong = ShapeEffectAuthoringPlanner.ResolveShape3d(Shape3dPreset.Strong);

        subtle.Enabled.Should().BeTrue();
        subtle.CameraPreset.Should().Be("orthographicFront");
        subtle.LightRig.Should().Be("flat");
        subtle.ExtrusionHeightEmu.Should().Be(0);
        subtle.PrstMaterial.Should().Be("matte");
        strong.Enabled.Should().BeTrue();
        strong.CameraPreset.Should().Be("perspectiveRelaxed");
        strong.LightRig.Should().Be("threePt");
        strong.ExtrusionHeightEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(3));
    }

    [Fact]
    public void SetShape3dCommand_PreservesOtherEffectsAndSupportsUndo()
    {
        var presentation = Presentation.CreateEmpty();
        var shape = new SlideShape
        {
            Id = 12,
            Kind = SlideShapeKind.AutoShape,
            Effects = new ShapeEffects
            {
                HasGlow = true,
                GlowRadiusEmu = 321,
                BevelTop = new BevelInfo { PresetName = "circle" },
            },
        };
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetShape3dCommand(
            0,
            shape.Id,
            ShapeEffectAuthoringPlanner.ResolveShape3d(Shape3dPreset.Strong)));

        shape.Effects.Should().NotBeNull();
        shape.Effects!.Scene3d.Should().NotBeNull();
        shape.Effects.Scene3d!.CameraPreset.Should().Be("perspectiveRelaxed");
        shape.Effects.ExtrusionHeightEmu.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(3));
        shape.Effects.PrstMaterial.Should().Be("matte");
        shape.Effects.BevelTop.Should().NotBeNull();
        shape.Effects.HasGlow.Should().BeTrue();

        bus.Undo();

        shape.Effects.Should().NotBeNull();
        shape.Effects!.Scene3d.Should().BeNull();
        shape.Effects.ExtrusionHeightEmu.Should().Be(0);
        shape.Effects.BevelTop!.PresetName.Should().Be("circle");
        shape.Effects.HasGlow.Should().BeTrue();
    }
}
