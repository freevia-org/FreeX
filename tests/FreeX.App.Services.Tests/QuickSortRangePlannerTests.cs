using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class QuickSortRangePlannerTests
{
    [Fact]
    public void Create_ExpandsSingleCellCurrentRegionAndUsesActiveColumn()
    {
        var sheet = CreateSheetWithSalesList();
        var selectedCell = Address(sheet, 3, 2);

        var plan = QuickSortRangePlanner.Create(sheet, new GridRange(selectedCell, selectedCell), selectedCell);

        plan.Range.Should().Be(new GridRange(Address(sheet, 2, 1), Address(sheet, 4, 3)));
        plan.SortByColOffset.Should().Be(1);
    }

    [Fact]
    public void Create_PreservesExplicitSelectionAndUsesActiveColumnInsideRange()
    {
        var sheet = CreateSheetWithSalesList();
        var selectedRange = new GridRange(Address(sheet, 2, 1), Address(sheet, 4, 3));

        var plan = QuickSortRangePlanner.Create(sheet, selectedRange, Address(sheet, 2, 3));

        plan.Range.Should().Be(selectedRange);
        plan.SortByColOffset.Should().Be(2);
    }

    [Fact]
    public void Create_FallsBackToSelectedRangeStartWhenActiveCellIsOutsideSelection()
    {
        var sheet = CreateSheetWithSalesList();
        var selectedRange = new GridRange(Address(sheet, 2, 1), Address(sheet, 4, 3));

        var plan = QuickSortRangePlanner.Create(sheet, selectedRange, Address(sheet, 6, 2));

        plan.Range.Should().Be(selectedRange);
        plan.SortByColOffset.Should().Be(0);
    }

    [Fact]
    public void Create_DoesNotDropFirstRowWhenHeaderIsNotTextLike()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new NumberValue(10));
        sheet.SetCell(Address(sheet, 1, 2), new NumberValue(20));
        sheet.SetCell(Address(sheet, 2, 1), new NumberValue(30));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(40));
        var selectedCell = Address(sheet, 1, 2);

        var plan = QuickSortRangePlanner.Create(sheet, new GridRange(selectedCell, selectedCell), selectedCell);

        plan.Range.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 2, 2)));
        plan.SortByColOffset.Should().Be(1);
    }

    [Fact]
    public void ResolveCandidateRange_ExpandsSingleCellSelectionToCurrentRegion()
    {
        var sheet = CreateSheetWithSalesList();
        var selectedCell = Address(sheet, 3, 2);

        var range = QuickSortRangePlanner.ResolveCandidateRange(
            sheet,
            new GridRange(selectedCell, selectedCell),
            selectedCell);

        range.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3)));
    }

    [Fact]
    public void HasLikelyHeaderRow_DetectsTextHeadersWithBodyData()
    {
        var sheet = CreateSheetWithSalesList();

        QuickSortRangePlanner.HasLikelyHeaderRow(
                sheet,
                new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3)))
            .Should().BeTrue();
    }

    [Fact]
    public void HasLikelyHeaderRow_RejectsNumericFirstRows()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new NumberValue(10));
        sheet.SetCell(Address(sheet, 1, 2), new NumberValue(20));
        sheet.SetCell(Address(sheet, 2, 1), new NumberValue(30));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(40));

        QuickSortRangePlanner.HasLikelyHeaderRow(
                sheet,
                new GridRange(Address(sheet, 1, 1), Address(sheet, 2, 2)))
            .Should().BeFalse();
    }

    [Fact]
    public void HasLikelyHeaderRow_UsesStructuredTableHeaderMetadataForTextBody()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(Address(sheet, 1, 1), Address(sheet, 3, 2));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 3,
            Name = "People",
            DisplayName = "People",
            Range = range,
            HeaderRowCount = 1
        });
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("First"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Last"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("Ada"));
        sheet.SetCell(Address(sheet, 2, 2), new TextValue("Lovelace"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Grace"));
        sheet.SetCell(Address(sheet, 3, 2), new TextValue("Hopper"));

        QuickSortRangePlanner.HasLikelyHeaderRow(sheet, range).Should().BeTrue();
    }

    [Fact]
    public void SourceOwnership_DelegatesHeaderInterpretationWithoutCellScanning()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "QuickSortRangePlanner.cs"));

        source.Should().Contain("QuickAnalysisSelectionReader.HasHeaderRow(sheet, range)");
        source.Should().NotContain("for (var row =");
        source.Should().NotContain("for (var col =");
    }

    private static Sheet CreateSheetWithSalesList()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Name"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Score"));
        sheet.SetCell(Address(sheet, 1, 3), new TextValue("Team"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("Beth"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(4));
        sheet.SetCell(Address(sheet, 2, 3), new TextValue("West"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Ada"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(2));
        sheet.SetCell(Address(sheet, 3, 3), new TextValue("East"));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("Cy"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(3));
        sheet.SetCell(Address(sheet, 4, 3), new TextValue("North"));
        return sheet;
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);
}
