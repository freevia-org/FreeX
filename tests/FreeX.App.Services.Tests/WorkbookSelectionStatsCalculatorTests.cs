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
    public void Combine_KeepsAggregateErrorFromWhicheverSideHasOne()
    {
        var leftErrors = WorkbookSelectionStatsCalculator.Combine(
            new WorkbookSelectionStats(10, 1, 1, 10, 10, 10, AggregateErrorCode: "#DIV/0!"),
            new WorkbookSelectionStats(20, 1, 1, 20, 20, 20));
        var rightErrors = WorkbookSelectionStatsCalculator.Combine(
            new WorkbookSelectionStats(10, 1, 1, 10, 10, 10),
            new WorkbookSelectionStats(20, 1, 1, 20, 20, 20, AggregateErrorCode: "#N/A"));

        leftErrors.AggregateErrorCode.Should().Be("#DIV/0!");
        rightErrors.AggregateErrorCode.Should().Be("#N/A");
    }

    [Fact]
    public void Calculate_ErrorCellAmongNumbers_ReflectsErrorInAggregatesInsteadOfSilentlyExcludingIt()
    {
        // R67 backlog (status-bar-6-2): Excel's status bar propagates an error cell's error into
        // Sum/Average/Min/Max instead of quietly excluding it from the numbers -- a selection with
        // a #DIV/0! cell must not show a plausible-but-wrong Sum.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(ErrorValue.DivByZero));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(20)));

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3)));

        stats.HasAggregateError.Should().BeTrue();
        stats.AggregateErrorCode.Should().Be("#DIV/0!");
        // Count and Numerical Count still count normally -- Excel keeps counting.
        stats.Count.Should().Be(3);
        stats.NumericalCount.Should().Be(2);

        var text = WorkbookSelectionStatsFormatter.Format(stats);
        text.Should().Contain("Average: #DIV/0!");
        text.Should().Contain("Sum: #DIV/0!");
        text.Should().Contain("Min: #DIV/0!");
        text.Should().Contain("Max: #DIV/0!");
        text.Should().Contain("Count: 3");
        text.Should().Contain("Numerical Count: 2");
        text.Should().NotContain("Sum: 30", "a plausible-but-wrong Sum must not hide the error");
    }

    [Fact]
    public void Calculate_SingleErrorCellSelection_ReportsAggregateError()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var address = new CellAddress(sheet.Id, 5, 3);
        sheet.SetCell(address, Cell.FromValue(ErrorValue.NA));

        var stats = WorkbookSelectionStatsCalculator.Calculate(sheet, new GridRange(address, address));

        stats.HasAggregateError.Should().BeTrue();
        stats.AggregateErrorCode.Should().Be("#N/A");
        stats.Count.Should().Be(1);
        stats.NumericalCount.Should().Be(0);
    }

    [Fact]
    public void Calculate_ManuallyHiddenRowsWithNoErrors_HasNoAggregateErrorAndKeepsCorrectAggregates()
    {
        // No-regression companion to the error-propagation fix above: a selection with NO error
        // cell must still show the correct numeric aggregates, and R61's manually-hidden-row
        // inclusion must still hold (only FilterHiddenRows is excluded).
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(100)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(20)));
        sheet.HiddenRows.Add(2);

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1)));

        stats.HasAggregateError.Should().BeFalse();
        stats.AggregateErrorCode.Should().BeNull();
        stats.Count.Should().Be(3);
        stats.NumericalCount.Should().Be(3);
        stats.Sum.Should().Be(130);
        stats.Average.Should().Be(130.0 / 3);
        stats.Min.Should().Be(10);
        stats.Max.Should().Be(100);
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
    public void Cache_ExpandingSelectionUpward_ReportsFirstErrorInRowMajorOrder_NotWhicheverWasCachedFirst()
    {
        // R142 status-bar-F1: B1=#DIV/0!, B2=#REF!. Select B2 alone (caches "#REF!" for B2:B2),
        // then extend the selection upward to B1:B2 without an intervening revision bump -- the
        // exact "Shift+Up" / drag-up gesture that hits WorkbookSelectionStatsCache's incremental
        // expansion path. Excel scans B1 before B2 (row-major), so the status bar must report
        // "#DIV/0!" (B1's error) for B1:B2, not "#REF!" just because B2 happened to be cached
        // first. Before the fix, TryCalculateContainingExpansion's row-decrease branch passed the
        // OLD (B2) stats as Combine's left/winning argument, so it kept "#REF!" regardless of
        // which cell Excel would actually reach first.
        var cache = new WorkbookSelectionStatsCache();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(b1, Cell.FromValue(ErrorValue.DivByZero));
        sheet.SetCell(b2, Cell.FromValue(ErrorValue.Ref));

        var single = cache.GetOrCalculate(sheet, new GridRange(b2, b2), revision: 1);
        var expandedUpward = cache.GetOrCalculate(sheet, new GridRange(b1, b2), revision: 1);
        var fromScratch = WorkbookSelectionStatsCalculator.Calculate(sheet, new GridRange(b1, b2));

        single.AggregateErrorCode.Should().Be("#REF!");
        fromScratch.AggregateErrorCode.Should().Be("#DIV/0!", "B1 is scanned before B2 in row-major order");
        expandedUpward.AggregateErrorCode.Should().Be(
            "#DIV/0!",
            "extending the selection upward must agree with a from-scratch calculation over the same range");
        expandedUpward.Should().Be(fromScratch);
    }

    [Fact]
    public void Cache_ExpandingSelectionLeftward_ReportsFirstErrorInRowMajorOrder_NotWhicheverWasCachedFirst()
    {
        // Symmetric case for the column-start-decrease branch: A2=#DIV/0!, B2=#REF!. Select B2
        // alone, then extend left to A2:B2 (e.g. Shift+Left). Row-major order scans A2 before B2,
        // so the aggregate error must become "#DIV/0!".
        var cache = new WorkbookSelectionStatsCache();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a2, Cell.FromValue(ErrorValue.DivByZero));
        sheet.SetCell(b2, Cell.FromValue(ErrorValue.Ref));

        var single = cache.GetOrCalculate(sheet, new GridRange(b2, b2), revision: 1);
        var expandedLeftward = cache.GetOrCalculate(sheet, new GridRange(a2, b2), revision: 1);
        var fromScratch = WorkbookSelectionStatsCalculator.Calculate(sheet, new GridRange(a2, b2));

        single.AggregateErrorCode.Should().Be("#REF!");
        fromScratch.AggregateErrorCode.Should().Be("#DIV/0!", "A2 is scanned before B2 in row-major order");
        expandedLeftward.AggregateErrorCode.Should().Be(
            "#DIV/0!",
            "extending the selection leftward must agree with a from-scratch calculation over the same range");
        expandedLeftward.Should().Be(fromScratch);
    }

    [Fact]
    public void Cache_ExpandingSelectionDownwardAndRightward_StillAgreesWithFromScratch_NoRegression()
    {
        // No-regression companion: the End.Row/End.Col-increase branches (downward/rightward
        // expansion, the direction that was already correct) must still agree with a from-scratch
        // calculation after the fix, including when the newly-revealed error is NOT the one
        // encountered first.
        var cache = new WorkbookSelectionStatsCache();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, Cell.FromValue(ErrorValue.DivByZero));
        sheet.SetCell(b2, Cell.FromValue(ErrorValue.Ref));

        var single = cache.GetOrCalculate(sheet, new GridRange(a1, a1), revision: 7);
        var expandedDownRight = cache.GetOrCalculate(sheet, new GridRange(a1, b2), revision: 7);
        var fromScratch = WorkbookSelectionStatsCalculator.Calculate(sheet, new GridRange(a1, b2));

        single.AggregateErrorCode.Should().Be("#DIV/0!");
        fromScratch.AggregateErrorCode.Should().Be("#DIV/0!", "A1 is scanned before B2 in row-major order");
        expandedDownRight.AggregateErrorCode.Should().Be("#DIV/0!");
        expandedDownRight.Should().Be(fromScratch);
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
            "sheet.CellCount + sheet.SpillValueCount < totalCells",
            "status calculations should choose the cheaper scan direction for sparse whole-column and dense bounded selections, " +
            "and the estimate must include spill cells so a large spill doesn't make the sheet look sparser than it is");
        calculatorSource.Should().Contain(
            "sheet.EnumerateValueBearingCells()",
            "sparse status selections must union the primary cell dictionary with the dynamic-array spill overlay so spilled cells are counted");
        calculatorSource.Should().NotContain(
            "sheet.GetOccupiedCellMap()",
            "the sparse scan must not use the occupied-cell map alone, since that silently drops dynamic-array spill cells");
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

    // R111: WorkbookSelectionStatsCalculator's sparse scan (chosen whenever the sheet's populated
    // cell count is small relative to the selection size) used to iterate sheet.GetOccupiedCellMap(),
    // which only reflects the primary _cells dictionary. Dynamic-array formulas store every spilled
    // cell -- everything but the array's anchor -- in a separate overlay (_spillValues), so a large
    // spill inside an otherwise-sparse sheet had its overflow cells silently invisible to
    // Sum/Average/Count/Min/Max. Real Excel includes every spilled cell exactly like a normal value.
    [Fact]
    public void R111_Calculate_SingleRange_SparsePathIncludesDynamicArraySpillCells()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1);

        // Anchor lives in the primary cell dictionary, matching how a real spilling formula cell
        // is stored.
        sheet.SetCell(anchor, Cell.FromValue(new NumberValue(1)));

        // SetSpillRange skips slot [0,0] (the anchor) and writes every other cell only into the
        // spill overlay -- rows 2 and 3 below get 2 and 3 respectively.
        var spillCells = new ScalarValue[3, 1];
        spillCells[0, 0] = new NumberValue(1); // ignored (anchor slot)
        spillCells[1, 0] = new NumberValue(2);
        spillCells[2, 0] = new NumberValue(3);
        sheet.SetSpillRange(anchor, new RangeValue(spillCells));

        // A wide selection (1000 rows) against only 3 value-bearing cells (1 in _cells, 2 in
        // _spillValues) is exactly the "otherwise-sparse sheet" scenario from the finding: the
        // sparse scan path must be chosen and must still see the spilled cells.
        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1000, 1)));

        stats.Count.Should().Be(3);
        stats.NumericalCount.Should().Be(3);
        stats.Sum.Should().Be(6);
        stats.Min.Should().Be(1);
        stats.Max.Should().Be(3);
    }

    // Sibling of the test above covering the multi-range (Ctrl-click) selection overload, which
    // has the identical sparse-scan-over-GetOccupiedCellMap bug at its own call site.
    [Fact]
    public void R111_Calculate_MultiRange_SparsePathIncludesDynamicArraySpillCells()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, Cell.FromValue(new NumberValue(10)));

        var spillCells = new ScalarValue[3, 1];
        spillCells[0, 0] = new NumberValue(10); // ignored (anchor slot)
        spillCells[1, 0] = new NumberValue(20);
        spillCells[2, 0] = new NumberValue(30);
        sheet.SetSpillRange(anchor, new RangeValue(spillCells));

        var ranges = new List<GridRange>
        {
            new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1000, 1)),
            new(new CellAddress(sheet.Id, 1, 20), new CellAddress(sheet.Id, 1000, 20)),
        };

        var stats = WorkbookSelectionStatsCalculator.Calculate(sheet, ranges);

        stats.Count.Should().Be(3);
        stats.NumericalCount.Should().Be(3);
        stats.Sum.Should().Be(60);
        stats.Min.Should().Be(10);
        stats.Max.Should().Be(30);
    }

    // No-regression sibling: hidden-row filtering (a pre-existing, deliberately-tested sparse-path
    // behavior) must keep working once the sparse scan is switched to enumerate spill-aware
    // addresses instead of the raw occupied-cell map.
    [Fact]
    public void R111_Calculate_SparsePathStillExcludesFilterHiddenRowsWithSpillCellsPresent()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, Cell.FromValue(new NumberValue(1)));

        var spillCells = new ScalarValue[3, 1];
        spillCells[0, 0] = new NumberValue(1); // ignored (anchor slot)
        spillCells[1, 0] = new NumberValue(2); // row 2 -- will be filter-hidden
        spillCells[2, 0] = new NumberValue(3); // row 3 -- stays visible
        sheet.SetSpillRange(anchor, new RangeValue(spillCells));
        sheet.FilterHiddenRows.Add(2);

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1000, 1)));

        stats.Count.Should().Be(2);
        stats.NumericalCount.Should().Be(2);
        stats.Sum.Should().Be(4);
        stats.Min.Should().Be(1);
        stats.Max.Should().Be(3);
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
