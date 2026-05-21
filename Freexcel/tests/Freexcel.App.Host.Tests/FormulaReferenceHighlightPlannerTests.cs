using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class FormulaReferenceHighlightPlannerTests
{
    private static readonly SheetId CurrentSheet = SheetId.New();
    private static readonly SheetId OtherSheet = SheetId.New();

    [Fact]
    public void GetHighlights_ReturnsColoredSpansAndRangesForFormulaReferences()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=SUM(A1:B2,C3)",
            CurrentSheet,
            ResolveSheet);

        highlights.Should().HaveCount(2);
        highlights[0].TextStart.Should().Be(5);
        highlights[0].TextLength.Should().Be(5);
        highlights[0].Text.Should().Be("A1:B2");
        highlights[0].PaletteIndex.Should().Be(0);
        highlights[0].Range.Should().Be(Range("A1", "B2"));

        highlights[1].TextStart.Should().Be(11);
        highlights[1].TextLength.Should().Be(2);
        highlights[1].Text.Should().Be("C3");
        highlights[1].PaletteIndex.Should().Be(1);
        highlights[1].Range.Should().Be(Range("C3", "C3"));
    }

    [Fact]
    public void GetHighlights_HandlesAbsoluteReferences()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=$A$1+$B2+C$3",
            CurrentSheet,
            ResolveSheet);

        highlights.Select(h => h.Text).Should().Equal("$A$1", "$B2", "C$3");
        highlights.Select(h => h.Range!.Value).Should().Equal(
            Range("A1", "A1"),
            Range("B2", "B2"),
            Range("C3", "C3"));
    }

    [Fact]
    public void GetHighlights_ColorsSheetQualifiedReferencesAndMapsKnownSheets()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=SUM(Sheet2!B4,'Sheet 2'!C5)",
            CurrentSheet,
            ResolveSheet);

        highlights.Should().HaveCount(2);
        highlights[0].Text.Should().Be("Sheet2!B4");
        highlights[0].SheetName.Should().Be("Sheet2");
        highlights[0].Range.Should().Be(new GridRange(
            new CellAddress(OtherSheet, 4, 2),
            new CellAddress(OtherSheet, 4, 2)));

        highlights[1].Text.Should().Be("'Sheet 2'!C5");
        highlights[1].SheetName.Should().Be("Sheet 2");
        highlights[1].Range.Should().Be(new GridRange(
            new CellAddress(OtherSheet, 5, 3),
            new CellAddress(OtherSheet, 5, 3)));
    }

    [Fact]
    public void GetHighlights_SkipsReferencesInsideStringLiterals()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=IF(A1=\"B2\",C3,\"D4:E5\")",
            CurrentSheet,
            ResolveSheet);

        highlights.Select(h => h.Text).Should().Equal("A1", "C3");
    }

    [Fact]
    public void GetHighlights_IgnoresTextThatIsNotAFormula()
    {
        FormulaReferenceHighlightPlanner.GetHighlights(
                "A1:B2",
                CurrentSheet,
                ResolveSheet)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void GetHighlights_IgnoresInvalidCellReferences()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=SUM(XFE1,A1048577,A1)",
            CurrentSheet,
            ResolveSheet);

        highlights.Select(h => h.Text).Should().Equal("A1");
    }

    private static SheetId? ResolveSheet(string sheetName) =>
        sheetName is "Sheet2" or "Sheet 2" ? OtherSheet : null;

    private static GridRange Range(string start, string end) =>
        new(CellAddress.Parse(start, CurrentSheet), CellAddress.Parse(end, CurrentSheet));
}
