using FluentAssertions;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class OutlineDashPresetMappingTests
{
    [Fact]
    public void OutlineDash_MapsToSamePresetByName()
    {
        foreach (var dash in Enum.GetValues<OutlineDash>())
            dash.ToPresetDash().ToString().Should().Be(dash.ToString());
    }

    [Fact]
    public void OutlineDash_LongDash_UsesEcmaEightThree()
    {
        DrawingMlPresetDashPattern.Get(OutlineDash.LongDash.ToPresetDash()).Should().Equal(8, 3);
        DrawingMlPresetDashPattern.Get(OutlineDash.Solid.ToPresetDash()).Should().BeNull();
    }
}
