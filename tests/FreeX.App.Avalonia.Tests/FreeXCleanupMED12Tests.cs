using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Focused regression tests for FreeX cleanup batch MED12 (round-10 MED/LOW findings).
/// </summary>
public sealed class FreeXCleanupMED12Tests
{
    /// <summary>
    /// P53: PlanDataBar must carry the negative-axis position, the authored axis/border colors,
    /// and the negative-direction flag through into the render instruction, or the Avalonia shell
    /// silently drops the zero-crossing axis line, strokes the border with the fill color instead
    /// of the authored border color, and loses the negative-gradient direction relative to WPF.
    /// </summary>
    [Fact]
    public void PlanDataBar_CarriesAxisFractionColorsAndNegativeFlagThrough()
    {
        var bar = new ConditionalFormatDataBar(
            StartFraction: 0.1,
            EndFraction: 0.4,
            FillColor: new RgbColor(200, 50, 50),
            Gradient: true,
            Border: true,
            ShowValue: true,
            IsNegative: true,
            AxisFraction: 0.4,
            NegativeFillColor: null,
            AxisColor: new RgbColor(10, 20, 30),
            BorderColor: new RgbColor(40, 50, 60));

        var plan = ConditionalFormatCellRenderPlanner.PlanDataBar(bar);

        plan.Should().NotBeNull();
        plan!.Value.IsNegative.Should().BeTrue();
        plan.Value.AxisFraction.Should().Be(0.4);
        plan.Value.AxisColor.Should().Be(new PresentationRgb(10, 20, 30));
        plan.Value.BorderColor.Should().Be(new PresentationRgb(40, 50, 60));
    }

    /// <summary>
    /// P53: when the model supplies no explicit axis/border color (the common case), the render
    /// instruction must carry nulls through rather than silently substituting something — the
    /// renderer itself is responsible for the fallback (fill color for border, black for axis),
    /// mirroring WPF's GridView.ConditionalDataBars.cs fallback logic exactly.
    /// </summary>
    [Fact]
    public void PlanDataBar_WithoutAuthoredColors_LeavesAxisAndBorderColorNull()
    {
        var bar = new ConditionalFormatDataBar(
            StartFraction: 0d,
            EndFraction: 0.6,
            FillColor: new RgbColor(1, 2, 3),
            Gradient: false,
            Border: true,
            ShowValue: false);

        var plan = ConditionalFormatCellRenderPlanner.PlanDataBar(bar);

        plan.Should().NotBeNull();
        plan!.Value.IsNegative.Should().BeFalse();
        plan.Value.AxisFraction.Should().Be(0d);
        plan.Value.AxisColor.Should().BeNull();
        plan.Value.BorderColor.Should().BeNull();
    }
}
