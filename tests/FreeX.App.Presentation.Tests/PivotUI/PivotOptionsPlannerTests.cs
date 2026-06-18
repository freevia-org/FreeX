using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotOptionsPlannerTests
{
    [Fact]
    public void ReportLayoutRoundTrip_FindsAndResolvesIndex()
    {
        var index = PivotOptionsPlanner.FindReportLayoutIndex(PivotReportLayout.Outline);
        PivotOptionsPlanner.ReportLayoutFromIndex(index).Should().Be(PivotReportLayout.Outline);

        PivotOptionsPlanner.ReportLayoutFromIndex(-1).Should().Be(PivotOptionsPlanner.ReportLayouts[0].Value);
        PivotOptionsPlanner.ReportLayoutFromIndex(99).Should().Be(PivotOptionsPlanner.ReportLayouts[^1].Value);
    }

    [Fact]
    public void SubtotalPlacementRoundTrip_FindsAndResolvesIndex()
    {
        var index = PivotOptionsPlanner.FindSubtotalPlacementIndex(PivotSubtotalPlacement.Top);
        PivotOptionsPlanner.SubtotalPlacementFromIndex(index).Should().Be(PivotSubtotalPlacement.Top);
    }

    [Fact]
    public void Capture_ReadsTotalsLayoutAndDisplayValues()
    {
        var pivot = new PivotTableModel
        {
            Name = "P",
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top,
            ReportLayout = PivotReportLayout.Outline,
            CompactRowLabelIndent = 3,
            RepeatItemLabels = false,
            BlankLineAfterItems = true,
            MergeAndCenterLabels = true,
        };
        pivot.ShowRowGrandTotals = true;
        pivot.ShowColumnGrandTotals = false;

        var values = PivotOptionsPlanner.Capture(pivot);

        values.ShowRowGrandTotals.Should().BeTrue();
        values.ShowColumnGrandTotals.Should().BeFalse();
        values.ShowSubtotals.Should().BeTrue();
        values.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Top);
        values.ReportLayout.Should().Be(PivotReportLayout.Outline);
        values.CompactRowLabelIndent.Should().Be(3);
        values.RepeatItemLabels.Should().BeFalse();
        values.BlankLineAfterItems.Should().BeTrue();
        values.MergeAndCenterLabels.Should().BeTrue();
    }

    [Theory]
    [InlineData("5", true, 5)]
    [InlineData("0", true, 0)]
    [InlineData("15", true, 15)]
    [InlineData("16", false, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("x", false, 0)]
    [InlineData("", false, 0)]
    public void TryParseCompactRowLabelIndent_ValidatesRange(string text, bool expectedOk, int expectedIndent)
    {
        var ok = PivotOptionsPlanner.TryParseCompactRowLabelIndent(text, out var indent, out var error);
        ok.Should().Be(expectedOk);
        if (expectedOk)
        {
            indent.Should().Be(expectedIndent);
            error.Should().BeNull();
        }
        else
        {
            error.Should().Be(PivotOptionsPlanner.CompactIndentRangeMessage);
        }
    }

    [Fact]
    public void CreateResult_BuildsValuesAndClampsIndent()
    {
        var result = PivotOptionsPlanner.CreateResult(
            showRowGrandTotals: false,
            showColumnGrandTotals: true,
            showSubtotals: true,
            subtotalPlacementIndex: PivotOptionsPlanner.FindSubtotalPlacementIndex(PivotSubtotalPlacement.Top),
            reportLayoutIndex: PivotOptionsPlanner.FindReportLayoutIndex(PivotReportLayout.Compact),
            compactRowLabelIndent: 99,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            mergeAndCenterLabels: true);

        result.ShowRowGrandTotals.Should().BeFalse();
        result.ShowColumnGrandTotals.Should().BeTrue();
        result.ShowSubtotals.Should().BeTrue();
        result.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Top);
        result.ReportLayout.Should().Be(PivotReportLayout.Compact);
        result.CompactRowLabelIndent.Should().Be(PivotOptionsPlanner.MaxCompactRowLabelIndent);
        result.RepeatItemLabels.Should().BeTrue();
        result.MergeAndCenterLabels.Should().BeTrue();
    }
}
