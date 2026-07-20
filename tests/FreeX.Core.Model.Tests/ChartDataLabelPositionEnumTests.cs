using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R51-io-chart-datalabel-3-1: <see cref="ChartDataLabelPosition"/> previously had no
/// Left/Right/Top/Bottom members, so every IO/reader/writer/renderer site that needs to
/// represent OOXML c:dLblPos val="l"/"r"/"t"/"b" (used by Line, 3-D Line, Scatter and Bubble
/// charts) had no choice but to collapse those positions to BestFit/Center. These tests pin the
/// model-level fix: the enum must expose all four directional members, distinct from every
/// pre-existing member and from each other.
/// </summary>
public sealed class ChartDataLabelPositionEnumTests
{
    [Fact]
    public void ChartDataLabelPosition_HasLeftRightTopBottomMembers()
    {
        var names = Enum.GetNames<ChartDataLabelPosition>();

        names.Should().Contain(new[] { "Left", "Right", "Top", "Bottom" });

        // Each directional member must be distinct from every other member (not silently
        // aliased onto BestFit/Center/etc., which is exactly the bug: a real Excel "Right"
        // position round-tripping as "Center").
        var distinctValues = new HashSet<ChartDataLabelPosition>(
            (ChartDataLabelPosition[])Enum.GetValues(typeof(ChartDataLabelPosition)));
        distinctValues.Count.Should().Be(names.Length);
    }

    [Fact]
    public void ChartDataLabelPosition_PreservesPreExistingMembers()
    {
        // Sibling no-regression test: the original five members (and their string names, which
        // XML readers/writers and any name-based (de)serialization depend on) must still exist
        // unchanged after adding the four directional members.
        var names = Enum.GetNames<ChartDataLabelPosition>();

        names.Should().Contain(new[]
        {
            "BestFit", "Center", "InsideEnd", "OutsideEnd", "InsideBase"
        });

        // Ordinal values of the pre-existing members must be unchanged (new members appended
        // at the end) so any code persisting the enum's underlying int value is unaffected.
        ((int)ChartDataLabelPosition.BestFit).Should().Be(0);
        ((int)ChartDataLabelPosition.Center).Should().Be(1);
        ((int)ChartDataLabelPosition.InsideEnd).Should().Be(2);
        ((int)ChartDataLabelPosition.OutsideEnd).Should().Be(3);
        ((int)ChartDataLabelPosition.InsideBase).Should().Be(4);
    }
}
