using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// R52-render-formula-bar-ref-3-2: whole-column ("A:A") and whole-row ("3:3") references typed
/// directly into a formula must get a colored reference-highlight box, same as any other
/// reference and matching real Excel. TryReadCell alone can never parse these (it requires a
/// row immediately after the column letters, or vice versa), so TryReadReference must special-
/// case the whole-column/whole-row shape.
/// </summary>
public sealed class FormulaReferenceHighlightPlannerWholeColumnRowTests
{
    private static readonly SheetId CurrentSheet = SheetId.New();

    [Fact]
    public void GetHighlights_HighlightsWholeColumnReference()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=SUM(A:A)",
            CurrentSheet,
            resolveSheetId: null);

        highlights.Should().HaveCount(1);
        highlights[0].Text.Should().Be("A:A");
        highlights[0].TextStart.Should().Be(5);
        highlights[0].TextLength.Should().Be(3);
        highlights[0].Range.Should().Be(new GridRange(
            new CellAddress(CurrentSheet, 1, 1),
            new CellAddress(CurrentSheet, CellAddress.MaxRow, 1)));
    }

    [Fact]
    public void GetHighlights_HighlightsAbsoluteWholeColumnReference()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=SUM($B:$B)",
            CurrentSheet,
            resolveSheetId: null);

        highlights.Should().HaveCount(1);
        highlights[0].Text.Should().Be("$B:$B");
        highlights[0].Range.Should().Be(new GridRange(
            new CellAddress(CurrentSheet, 1, 2),
            new CellAddress(CurrentSheet, CellAddress.MaxRow, 2)));
    }

    [Fact]
    public void GetHighlights_HighlightsWholeRowReference()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=SUM(3:3)",
            CurrentSheet,
            resolveSheetId: null);

        highlights.Should().HaveCount(1);
        highlights[0].Text.Should().Be("3:3");
        highlights[0].Range.Should().Be(new GridRange(
            new CellAddress(CurrentSheet, 3, 1),
            new CellAddress(CurrentSheet, 3, CellAddress.MaxCol)));
    }

    /// <summary>Sibling no-regression: an ordinary A1:B2 cell range must still highlight normally.</summary>
    [Fact]
    public void GetHighlights_StillHighlightsOrdinaryCellRange()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=SUM(A1:B2)",
            CurrentSheet,
            resolveSheetId: null);

        highlights.Should().HaveCount(1);
        highlights[0].Text.Should().Be("A1:B2");
        highlights[0].Range.Should().Be(new GridRange(
            new CellAddress(CurrentSheet, 1, 1),
            new CellAddress(CurrentSheet, 2, 2)));
    }
}
