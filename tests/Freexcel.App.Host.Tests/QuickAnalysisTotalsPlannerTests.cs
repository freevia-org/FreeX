using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class QuickAnalysisTotalsPlannerTests
{
    [Fact]
    public void BuildPercentTotalEdits_CreatesAdjacentPercentOfSelectionFormulas()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 4, 4));

        var edits = QuickAnalysisTotalsPlanner.BuildPercentTotalEdits(range);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheetId, 2, 5),
            new CellAddress(sheetId, 3, 5),
            new CellAddress(sheetId, 4, 5));
        edits[0].NewCell.FormulaText.Should().Be("SUM(B2:D2)/SUM($B$2:$D$4)");
        edits[2].NewCell.FormulaText.Should().Be("SUM(B4:D4)/SUM($B$2:$D$4)");
    }

    [Theory]
    [InlineData("SUM", "SUM(B2:D2)", "SUM(B4:D4)")]
    [InlineData("AVERAGE", "AVERAGE(B2:D2)", "AVERAGE(B4:D4)")]
    [InlineData("COUNT", "COUNT(B2:D2)", "COUNT(B4:D4)")]
    [InlineData("MAX", "MAX(B2:D2)", "MAX(B4:D4)")]
    [InlineData("MIN", "MIN(B2:D2)", "MIN(B4:D4)")]
    public void BuildAggregateEdits_CreatesAdjacentRowAggregateFormulas(
        string functionName,
        string firstFormula,
        string lastFormula)
    {
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 4, 4));

        var edits = QuickAnalysisTotalsPlanner.BuildAggregateEdits(range, functionName);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheetId, 2, 5),
            new CellAddress(sheetId, 3, 5),
            new CellAddress(sheetId, 4, 5));
        edits[0].NewCell.FormulaText.Should().Be(firstFormula);
        edits[2].NewCell.FormulaText.Should().Be(lastFormula);
    }

    [Fact]
    public void BuildRunningTotalEdits_CreatesAdjacentCumulativeSelectionFormulas()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 4, 4));

        var edits = QuickAnalysisTotalsPlanner.BuildRunningTotalEdits(range);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheetId, 2, 5),
            new CellAddress(sheetId, 3, 5),
            new CellAddress(sheetId, 4, 5));
        edits[0].NewCell.FormulaText.Should().Be("SUM($B$2:$D$2)");
        edits[2].NewCell.FormulaText.Should().Be("SUM($B$2:$D$4)");
    }
}
