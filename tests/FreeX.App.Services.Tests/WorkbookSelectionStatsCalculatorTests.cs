using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSelectionStatsCalculatorTests
{
    [Fact]
    public void Calculate_SeparatesNonblankCountFromNumericalCount()
    {
        // Note: a DateTimeValue cell contributes its underlying serial value to
        // Sum/Average/Min/Max/NumericalCount, matching Excel's own SUM/AVERAGE
        // treatment of dates. This test previously asserted the date was excluded
        // from numeric aggregation (NumericalCount=2, Sum=4) — that was finding J18.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("counted")));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(BlankValue.Instance));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), Cell.FromValue(new NumberValue(3)));
        var dateValue = DateTimeValue.FromDateTime(new DateTime(2026, 6, 6));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), Cell.FromValue(dateValue));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 6), Cell.FromValue(new BoolValue(true)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 7), Cell.FromValue(ErrorValue.DivByZero));

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 7)));

        stats.Count.Should().Be(6);
        stats.NumericalCount.Should().Be(3);
        stats.Sum.Should().Be(4 + dateValue.Value);
        stats.Average.Should().Be((4 + dateValue.Value) / 3);
        stats.Min.Should().Be(1);
        stats.Max.Should().Be(dateValue.Value);
    }

    [Fact]
    public void Calculate_TextOnlySelectionStillReportsCountWithoutNumericStats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("text")));

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2)));

        stats.Count.Should().Be(1);
        stats.NumericalCount.Should().Be(0);
        stats.Sum.Should().Be(0);
        stats.Average.Should().BeNull();
        stats.Min.Should().BeNull();
        stats.Max.Should().BeNull();
    }

    [Fact]
    public void Calculate_FilteredRowsAreExcludedFromSelectionStats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("Header")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromValue(new TextValue("visible")));
        sheet.FilterHiddenRows.Add(2);

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 1)));

        stats.Count.Should().Be(3);
        stats.NumericalCount.Should().Be(1);
        stats.Sum.Should().Be(30);
        stats.Average.Should().Be(30);
        stats.Min.Should().Be(30);
        stats.Max.Should().Be(30);
    }

    [Fact]
    public void Calculate_ManuallyHiddenColumnsAreStillIncludedInSelectionStats()
    {
        // R61-render-status-bar-stats-6-1: Excel's status-bar AutoCalculate over a plain
        // rectangular selection still includes manually-hidden (Format > Hide Column) columns --
        // only AutoFilter-hidden rows are genuinely excluded (that's why SUBTOTAL(109,...) exists
        // as a separate mechanism). This test previously asserted the manually-hidden column 2
        // was excluded (Count=2/Sum=40); that was the bug.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(20)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(30)));
        sheet.HiddenCols.Add(2);

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3)));

        stats.Count.Should().Be(3);
        stats.NumericalCount.Should().Be(3);
        stats.Sum.Should().Be(60);
        stats.Average.Should().Be(20);
        stats.Min.Should().Be(10);
        stats.Max.Should().Be(30);
    }

    [Fact]
    public void Calculate_ManuallyHiddenAndGroupCollapsedRowsAreStillIncludedInSelectionStats()
    {
        // Sibling no-regression coverage: a manually-hidden row (Format > Hide Row) and an
        // outline-group-collapsed row must also remain INCLUDED in the selection stats, matching
        // Excel -- only Sheet.FilterHiddenRows (AutoFilter) should be excluded.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(100)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(20)));
        sheet.HiddenRows.Add(2);
        sheet.GroupHiddenRows.Add(3);

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1)));

        stats.Count.Should().Be(3);
        stats.NumericalCount.Should().Be(3);
        stats.Sum.Should().Be(130);
        stats.Average.Should().Be(130.0 / 3);
        stats.Min.Should().Be(10);
        stats.Max.Should().Be(100);
    }

    [Fact]
    public void Calculate_FormulaCellsUseTheirEffectiveCachedValues()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { FormulaText = "A2+1", Value = new NumberValue(5) });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new Cell { FormulaText = "\"text\"", Value = new TextValue("text") });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new Cell { FormulaText = "TRUE()", Value = new BoolValue(true) });
        sheet.SetCell(
            new CellAddress(sheet.Id, 1, 4),
            new Cell { FormulaText = "TODAY()", Value = DateTimeValue.FromDateTime(new DateTime(2026, 6, 6)) });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new Cell { FormulaText = "1/0", Value = ErrorValue.DivByZero });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 6), new Cell { FormulaText = "\"\"", Value = BlankValue.Instance });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 7), Cell.FromValue(new NumberValue(7)));

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 7)));

        // A4's cached formula result is a DateTimeValue (TODAY() -> serial 46179 for
        // 2026-06-06). Excel's SUM/AVERAGE/COUNT/MIN/MAX all include date-valued cells
        // in their numeric aggregation, so NumericalCount/Sum/Average/Min/Max here must
        // account for it alongside the two plain-number cells (A1=5, A7=7). This test
        // previously expected the date to be excluded (NumericalCount=2, Sum=12) — that
        // encoded the pre-fix bug.
        stats.Count.Should().Be(6);
        stats.NumericalCount.Should().Be(3);
        stats.Sum.Should().Be(5 + 46179 + 7);
        stats.Average.Should().Be((5 + 46179 + 7) / 3.0);
        stats.Min.Should().Be(5);
        stats.Max.Should().Be(46179);
    }

    [Fact]
    public void Calculate_SingleCellSelectionUsesDirectValueStats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var address = new CellAddress(sheet.Id, 5, 3);
        sheet.SetCell(address, Cell.FromValue(new NumberValue(42)));

        var stats = WorkbookSelectionStatsCalculator.Calculate(sheet, new GridRange(address, address));

        stats.Should().Be(new WorkbookSelectionStats(42, 1, 1, 42, 42, 42));
    }

    [Fact]
    public void Calculate_SelectionOutsideUsedRangeReturnsEmptyStats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= 1_000; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row)));

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 5),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 5)));

        stats.Should().Be(new WorkbookSelectionStats(0, 0, 0, null, null, null));
        stats.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Calculate_LargeSelections_UsesOnlyOccupiedCellsInsideRange()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 1_000_000, 1), Cell.FromValue(new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 1_000_000, 2), Cell.FromValue(new NumberValue(90)));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var stats = WorkbookSelectionStatsCalculator.Calculate(sheet, range);

        stats.Count.Should().Be(2);
        stats.NumericalCount.Should().Be(2);
        stats.Sum.Should().Be(40);
        stats.Average.Should().Be(20);
        stats.Min.Should().Be(10);
        stats.Max.Should().Be(30);
    }

    [Fact]
    public void Combine_MergesAggregateStats()
    {
        var combined = WorkbookSelectionStatsCalculator.Combine(
            new WorkbookSelectionStats(10, 2, 1, 10, 10, 10),
            new WorkbookSelectionStats(20, 3, 2, 10, 5, 15));

        combined.Should().Be(new WorkbookSelectionStats(30, 5, 3, 10, 5, 15));
    }

    [Fact]
    public void Cache_ReusesStatsWhenSheetRangeAndRevisionAreUnchanged()
    {
        var cache = new WorkbookSelectionStatsCache();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(7)));
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var first = cache.GetOrCalculate(sheet, range, revision: 4);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(8)));
        var second = cache.GetOrCalculate(sheet, range, revision: 4);
        var third = cache.GetOrCalculate(sheet, range, revision: 5);

        first.Should().Be(new WorkbookSelectionStats(7, 1, 1, 7, 7, 7));
        second.Should().Be(first);
        third.Should().Be(new WorkbookSelectionStats(8, 1, 1, 8, 8, 8));
    }

    [Fact]
    public void Cache_CombinesContainingRangeExpansion()
    {
        var cache = new WorkbookSelectionStatsCache();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(b1, new NumberValue(2));
        sheet.SetCell(a2, new NumberValue(3));
        sheet.SetCell(b2, new TextValue("counted"));

        var first = cache.GetOrCalculate(sheet, new GridRange(a1, a1), revision: 4);
        var expanded = cache.GetOrCalculate(sheet, new GridRange(a1, b2), revision: 4);

        first.Should().Be(new WorkbookSelectionStats(1, 1, 1, 1, 1, 1));
        expanded.Should().Be(new WorkbookSelectionStats(6, 4, 3, 2, 1, 3));
    }

    [Fact]
    public void Format_UsesWpfStatusLabelOrder()
    {
        using var _ = TestCultureScope.CurrentCulture("en-US");
        var stats = new WorkbookSelectionStats(
            Count: 4,
            NumericalCount: 3,
            Sum: 12,
            Average: 4,
            Min: 2,
            Max: 6);

        var text = WorkbookSelectionStatsFormatter.Format(stats);

        text.Should().Be("Average: 4   Count: 4   Numerical Count: 3   Sum: 12   Min: 2   Max: 6");
    }

    [Fact]
    public void Format_TextOnlySelectionShowsCountAndNumericalCount()
    {
        using var _ = TestCultureScope.CurrentCulture("en-US");
        var stats = new WorkbookSelectionStats(0, Count: 1, NumericalCount: 0, Average: null, Min: null, Max: null);

        WorkbookSelectionStatsFormatter.Format(stats)
            .Should().Be("Count: 1   Numerical Count: 0");
    }

    [Theory]
    [InlineData(12.5, "12.5")]
    [InlineData(12.0000000001, "12")]
    [InlineData(123456789.1234, "123456789.1")]
    public void FormatNumber_UsesCompactExcelLikeStatusText(double value, string expected)
    {
        using var _ = TestCultureScope.CurrentCulture("en-US");

        WorkbookSelectionStatsFormatter.FormatNumber(value).Should().Be(expected);
    }

    [Fact]
    public void WorkbookSession_SelectionStatsTracksSelectedRange()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(b1, new NumberValue(20));
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

        session.SelectRange(new GridRange(a1, b1));

        session.SelectionStats.Should().Be(new WorkbookSelectionStats(30, 2, 2, 15, 10, 20));
        session.SelectionStatsText.Should().Contain("Average: 15");
    }

    [Fact]
    public void WorkbookSession_SelectionStatsAggregatesGoToSpecialSelectedRanges()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var f1 = new CellAddress(sheet.Id, 1, 6);
        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(c1, new NumberValue(20));
        sheet.SetCell(d1, new TextValue("counted"));
        sheet.SetCell(f1, new NumberValue(-4));
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        session.SelectRange(new GridRange(a1, f1));

        var result = session.GoToSpecial(
            GoToSpecialKind.Constants,
            new GoToSpecialOptions(GoToSpecialValueTypes.Numbers | GoToSpecialValueTypes.Text));

        result.Success.Should().BeTrue();
        result.SelectedRanges.Should().Equal(
            new GridRange(a1, a1),
            new GridRange(c1, d1),
            new GridRange(f1, f1));
        AssertSelectionStats(
            session.SelectionStats,
            sum: 26,
            count: 4,
            numericalCount: 3,
            average: 26.0 / 3,
            min: -4,
            max: 20);
        session.SelectionStatsText.Should().Contain("Count: 4");
        session.SelectionStatsText.Should().Contain("Numerical Count: 3");
        session.SelectionStatsText.Should().Contain("Sum: 26");
    }

    [Fact]
    public void WorkbookSession_SelectionStatsDoesNotDoubleCountOverlappingSelectedRanges()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var firstRange = new GridRange(a1, b2);
        var secondRange = new GridRange(b1, c1);
        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(b1, new NumberValue(20));
        sheet.SetCell(c1, new NumberValue(-5));
        sheet.SetCell(a2, new NumberValue(30));
        sheet.SetCell(b2, new TextValue("counted"));
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

        session.SelectRanges(firstRange, [firstRange, secondRange]);

        session.SelectedRange.Should().Be(firstRange);
        session.SelectedRanges.Should().Equal(firstRange, secondRange);
        AssertSelectionStats(
            session.SelectionStats,
            sum: 55,
            count: 5,
            numericalCount: 4,
            average: 13.75,
            min: -5,
            max: 30);
    }

    [Fact]
    public void Calculate_LargeSelections_ScansSparseCellsWithoutCopyingUsedCellDictionary()
    {
        var calculatorSource = File.ReadAllText(
            RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSelectionStatsCalculator.cs"));
        var cacheSource = File.ReadAllText(
            RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSelectionStatsCache.cs"));

        calculatorSource.Should().Contain("range.Start == range.End");
        calculatorSource.Should().Contain("CalculateSingleCell(sheet.GetValue(range.Start.Row, range.Start.Col))");
        calculatorSource.Should().Contain(
            "sheet.GetUsedRange()",
            "status refreshes for selections outside the used range should avoid scanning occupied cells");
        calculatorSource.Should().NotContain(
            "GetUsedCells()",
            "status refreshes should not allocate a full used-cell dictionary");
        calculatorSource.Should().NotContain(
            ".Where(",
            "whole-column status calculations should avoid LINQ iterator chains in the hot path");
        calculatorSource.Should().NotContain(
            ".Select(",
            "whole-column status calculations should avoid LINQ iterator chains in the hot path");
        calculatorSource.Should().Contain(
            "sheet.CellCount < totalCells",
            "status calculations should choose the cheaper scan direction for sparse whole-column and dense bounded selections");
        calculatorSource.Should().Contain(
            "sheet.GetOccupiedCellMap()",
            "sparse status selections should enumerate occupied cell entries without constructing address objects");
        calculatorSource.Should().Contain(
            "sheet.GetValue(row, col)",
            "small status selections should clip to the used range and scan by primitive coordinates");
        cacheSource.Should().Contain(
            "GetOrCalculate",
            "the portable session should be able to reuse repeated and expanded selection stats");
        calculatorSource.Should().NotContain(
            "scanRange.AllCells()",
            "status-bar hot paths should avoid iterator and CellAddress allocation");
        calculatorSource.Should().NotContain(
            "sheet.EnumerateCells()",
            "status-bar hot paths should avoid address tuple allocation while scanning occupied cells");
    }

    private static void AssertSelectionStats(
        WorkbookSelectionStats stats,
        double sum,
        int count,
        int numericalCount,
        double average,
        double min,
        double max)
    {
        stats.Sum.Should().Be(sum);
        stats.Count.Should().Be(count);
        stats.NumericalCount.Should().Be(numericalCount);
        stats.Average.Should().NotBeNull();
        stats.Average!.Value.Should().BeApproximately(average, 1e-12);
        stats.Min.Should().Be(min);
        stats.Max.Should().Be(max);
    }
}
