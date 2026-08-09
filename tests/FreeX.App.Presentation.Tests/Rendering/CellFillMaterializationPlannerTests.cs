using FluentAssertions;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Rendering;

public sealed class CellFillMaterializationPlannerTests
{
    [Fact]
    public void LinearGradient_NormalizesAngleStopsAndSpread()
    {
        var gradient = new CellGradientFill
        {
            Degree = 450,
            Stops =
            [
                new CellGradientStop(1.2, new CellColor(0, 0, 255)),
                new CellGradientStop(-0.2, new CellColor(255, 0, 0)),
                new CellGradientStop(0.5, new CellColor(255, 255, 0)),
            ],
        };

        var plan = CellFillMaterializationPlanner.PlanGradient(
            gradient,
            EmptyCellGradientBehavior.Materialize)!;

        plan.Kind.Should().Be(CellFillBackgroundKind.LinearGradient);
        plan.NormalizedDegree.Should().Be(90);
        plan.Start.X.Should().BeApproximately(0.5, 0.000000001);
        plan.Start.Y.Should().BeApproximately(0, 0.000000001);
        plan.End.X.Should().BeApproximately(0.5, 0.000000001);
        plan.End.Y.Should().BeApproximately(1, 0.000000001);
        plan.Spread.Should().Be(CellGradientSpreadMode.Pad);
        plan.Stops.Select(stop => stop.Offset).Should().Equal(0, 0.5, 1);
    }

    [Fact]
    public void PathGradient_ProducesNormalizedRadialGeometry()
    {
        var gradient = new CellGradientFill
        {
            Type = CellGradientFillType.Path,
            Left = 0.2,
            Right = 0.4,
            Top = 0.1,
            Bottom = 0.3,
            Stops = [new CellGradientStop(0, CellColor.White)],
        };

        var plan = CellFillMaterializationPlanner.PlanGradient(
            gradient,
            EmptyCellGradientBehavior.Materialize)!;

        plan.Kind.Should().Be(CellFillBackgroundKind.RadialGradient);
        plan.Center.X.Should().BeApproximately(0.4, 0.000000001);
        plan.Center.Y.Should().BeApproximately(0.4, 0.000000001);
        plan.Origin.Should().Be(plan.Center);
        plan.RadiusX.Should().BeApproximately(0.6, 0.000000001);
        plan.RadiusY.Should().BeApproximately(0.6, 0.000000001);
    }

    [Fact]
    public void EmptyGradientBehavior_PreservesRendererFallbackDifference()
    {
        var style = new CellStyle
        {
            FillColor = new CellColor(20, 30, 40),
            GradientFill = new CellGradientFill { Stops = [] },
        };

        var wpf = CellFillMaterializationPlanner.Plan(
            style,
            WorkbookTheme.Office,
            CellFillMaterializationProfile.Wpf,
            CellFillFallbackKind.White);
        var avalonia = CellFillMaterializationPlanner.Plan(
            style,
            WorkbookTheme.Office,
            CellFillMaterializationProfile.Avalonia,
            CellFillFallbackKind.White);

        wpf.BackgroundKind.Should().Be(CellFillBackgroundKind.LinearGradient);
        wpf.Gradient.Should().NotBeNull();
        avalonia.BackgroundKind.Should().Be(CellFillBackgroundKind.Solid);
        avalonia.SolidColor.Should().Be(style.FillColor);
    }

    [Fact]
    public void PatternOverlayBehavior_IsExplicitForGradientCells()
    {
        var style = new CellStyle
        {
            FillPatternStyle = CellFillPatternStyle.LightGrid,
            FillPatternColor = new CellColor(7, 8, 9),
            GradientFill = new CellGradientFill
            {
                Stops = [new CellGradientStop(0, CellColor.White)],
            },
        };

        var wpf = CellFillMaterializationPlanner.Plan(
            style,
            WorkbookTheme.Office,
            CellFillMaterializationProfile.Wpf,
            CellFillFallbackKind.Transparent);
        var avalonia = CellFillMaterializationPlanner.Plan(
            style,
            WorkbookTheme.Office,
            CellFillMaterializationProfile.Avalonia,
            CellFillFallbackKind.Transparent);

        wpf.Pattern.Kind.Should().Be(CellFillPatternPlanKind.None);
        avalonia.Pattern.Kind.Should().Be(CellFillPatternPlanKind.Hatch);
        avalonia.PatternColor.Should().Be(style.FillPatternColor);
    }

    [Theory]
    [InlineData(CellFillFallbackKind.Transparent, CellFillBackgroundKind.Transparent)]
    [InlineData(CellFillFallbackKind.White, CellFillBackgroundKind.WhiteFallback)]
    public void UnfilledCell_UsesRequestedRendererFallback(
        CellFillFallbackKind fallback,
        CellFillBackgroundKind expected)
    {
        var plan = CellFillMaterializationPlanner.Plan(
            null,
            WorkbookTheme.Office,
            CellFillMaterializationProfile.Wpf,
            fallback);

        plan.BackgroundKind.Should().Be(expected);
        plan.HasDeclaredSurface.Should().BeFalse();
        plan.HasExplicitPrimaryFill.Should().BeFalse();
    }
}
