using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class SubtotalCommandTests
{
    [Fact]
    public void SubtotalCommand_InsertsGroupAndGrandTotalRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCtx(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(25));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));

        var command = new SubtotalCommand(sheet.Id, range, groupByColumnOffset: 0, subtotalColumnOffset: 1);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        sheet.GetCell(4, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B3)");
        sheet.GetValue(7, 1).Should().Be(new TextValue("West Total"));
        sheet.GetCell(7, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B5:B6)");
        sheet.GetValue(8, 1).Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(8, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B7)");

        command.Revert(context);

        sheet.GetValue(4, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(25));
        sheet.GetCell(6, 1).Should().BeNull();
    }

    [Fact]
    public void SubtotalCommand_RejectsRangesWithoutDataRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCtx(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));

        var outcome = new SubtotalCommand(sheet.Id, range, 0, 1).Apply(context);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("data row");
    }

    [Fact]
    public void SubtotalCommand_RejectsProtectedSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCtx(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.IsProtected = true;
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));

        var outcome = new SubtotalCommand(sheet.Id, range, 0, 1).Apply(context);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetValue(2, 1).Should().Be(new TextValue("East"));
    }

    [Fact]
    public void SubtotalCommand_WithPageBreakBetweenGroups_AddsBreakBeforeNextGroupAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCtx(workbook);
        sheet.RowPageBreaks.Add(20);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(25));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));

        var command = new SubtotalCommand(
            sheet.Id,
            range,
            groupByColumnOffset: 0,
            subtotalColumnOffset: 1,
            pageBreakBetweenGroups: true);

        command.Apply(context).Success.Should().BeTrue();

        sheet.RowPageBreaks.Should().Contain(5u);
        sheet.RowPageBreaks.Should().Contain(23u);

        command.Revert(context);

        sheet.RowPageBreaks.Should().Equal(20u);
    }

    [Fact]
    public void SubtotalCommand_WithPageBreakBetweenGroups_AddsBreakAfterEachSubtotalForThreeOrMoreGroups()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCtx(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        // Three groups: East (rows 2-4), West (rows 5-7), North (rows 8-10)
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(11));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new NumberValue(21));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 2), new NumberValue(22));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 9, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 9, 2), new NumberValue(31));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 2), new NumberValue(32));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 2));

        var command = new SubtotalCommand(
            sheet.Id,
            range,
            groupByColumnOffset: 0,
            subtotalColumnOffset: 1,
            pageBreakBetweenGroups: true);

        command.Apply(context).Success.Should().BeTrue();

        // After insertions: East Total at row 5, West Total at row 9, North Total at row 13.
        // North is the last group, so no break after it. The break should appear AFTER each
        // subtotal except the last, i.e. at rows 6 and 10.
        sheet.GetValue(5, 1).Should().Be(new TextValue("East Total"));
        sheet.GetValue(9, 1).Should().Be(new TextValue("West Total"));
        sheet.GetValue(13, 1).Should().Be(new TextValue("North Total"));
        sheet.RowPageBreaks.Should().Contain(6u);
        sheet.RowPageBreaks.Should().Contain(10u);
    }

    [Fact]
    public void SubtotalCommand_WithSummaryAboveData_InsertsTotalsBeforeGroupsAndGrandTotalAtTop()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCtx(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(25));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));

        var command = new SubtotalCommand(
            sheet.Id,
            range,
            groupByColumnOffset: 0,
            subtotalColumnOffset: 1,
            summaryBelowData: false);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetValue(2, 1).Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(2, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B3:B8)");
        sheet.GetValue(3, 1).Should().Be(new TextValue("East Total"));
        sheet.GetCell(3, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B4:B5)");
        sheet.GetValue(6, 1).Should().Be(new TextValue("West Total"));
        sheet.GetCell(6, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B7:B8)");

        command.Revert(context);

        sheet.GetValue(2, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(25));
        sheet.GetCell(6, 1).Should().BeNull();
    }

    [Fact]
    public void SubtotalCommand_WithSummaryAboveDataAndPageBreaks_BreaksBeforeLaterGroupTotals()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCtx(workbook);
        sheet.RowPageBreaks.Add(30);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 2), new NumberValue(35));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 7, 2));

        var command = new SubtotalCommand(
            sheet.Id,
            range,
            groupByColumnOffset: 0,
            subtotalColumnOffset: 1,
            pageBreakBetweenGroups: true,
            summaryBelowData: false);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetValue(3, 1).Should().Be(new TextValue("East Total"));
        sheet.GetValue(6, 1).Should().Be(new TextValue("West Total"));
        sheet.GetValue(9, 1).Should().Be(new TextValue("North Total"));
        sheet.RowPageBreaks.Should().Contain(6u);
        sheet.RowPageBreaks.Should().Contain(9u);
        sheet.RowPageBreaks.Should().Contain(34u);

        command.Revert(context);

        sheet.RowPageBreaks.Should().Equal(30u);
    }

    [Fact]
    public void SubtotalCommand_CanApplySubtotalToMultipleValueColumns()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCtx(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Cost"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(6));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(8));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));

        var command = new SubtotalCommand(
            sheet.Id,
            range,
            groupByColumnOffset: 0,
            subtotalColumnOffsets: [1u, 2u]);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        sheet.GetCell(4, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B3)");
        sheet.GetCell(4, 3)!.FormulaText.Should().Be("SUBTOTAL(9,C2:C3)");
        sheet.GetValue(6, 1).Should().Be(new TextValue("West Total"));
        sheet.GetCell(6, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B5:B5)");
        sheet.GetCell(6, 3)!.FormulaText.Should().Be("SUBTOTAL(9,C5:C5)");
        sheet.GetValue(7, 1).Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(7, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B6)");
        sheet.GetCell(7, 3)!.FormulaText.Should().Be("SUBTOTAL(9,C2:C6)");
    }

    [Fact]
    public void CompositeWorkbookCommand_AppliesSubtotalsAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var context = new SimpleCtx(workbook);
        SeedSubtotalRows(sheet1);
        SeedSubtotalRows(sheet2);
        var range1 = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 5, 2));
        var range2 = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 5, 2));
        var command = new CompositeWorkbookCommand(
            "Subtotal",
            [
                new SubtotalCommand(sheet1.Id, range1, groupByColumnOffset: 0, subtotalColumnOffset: 1),
                new SubtotalCommand(sheet2.Id, range2, groupByColumnOffset: 0, subtotalColumnOffset: 1)
            ]);

        command.Apply(context).Success.Should().BeTrue();

        sheet1.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        sheet1.GetCell(4, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B3)");
        sheet2.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        sheet2.GetCell(4, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B3)");

        command.Revert(context);

        sheet1.GetValue(4, 1).Should().Be(new TextValue("West"));
        sheet1.GetValue(5, 2).Should().Be(new NumberValue(25));
        sheet2.GetValue(4, 1).Should().Be(new TextValue("West"));
        sheet2.GetValue(5, 2).Should().Be(new NumberValue(25));
    }

    [Fact]
    public void RemoveSubtotalRowsCommand_RemovesSubtotalFormulaRowsAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCtx(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East Total"));
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 2), "SUBTOTAL(9,B2:B2)");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Grand Total"));
        sheet.SetFormula(new CellAddress(sheet.Id, 5, 2), "SUBTOTAL(9,B2:B4)");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));

        var command = new RemoveSubtotalRowsCommand(sheet.Id, range);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetValue(3, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(3, 2).Should().Be(new NumberValue(20));
        sheet.GetCell(4, 1).Should().BeNull();

        command.Revert(context);

        sheet.GetValue(3, 1).Should().Be(new TextValue("East Total"));
        sheet.GetCell(3, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B2)");
        sheet.GetValue(5, 1).Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(5, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B4)");
    }

    [Fact]
    public void RemoveSubtotalRowsCommand_RejectsProtectedSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCtx(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East Total"));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "SUBTOTAL(9,B1:B1)");
        sheet.IsProtected = true;
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));

        var outcome = new RemoveSubtotalRowsCommand(sheet.Id, range).Apply(context);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetValue(2, 1).Should().Be(new TextValue("East Total"));
    }

    [Fact]
    public void RemoveSubtotalRowsCommand_RemovesSparseSubtotalRowsOnceAcrossLargeRange()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCtx(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 5_000, 1), new TextValue("East Total"));
        sheet.SetFormula(new CellAddress(sheet.Id, 5_000, 2), "SUBTOTAL(9,B1:B4999)");
        sheet.SetFormula(new CellAddress(sheet.Id, 5_000, 3), "SUBTOTAL(9,C1:C4999)");
        sheet.SetCell(new CellAddress(sheet.Id, 5_001, 1), new TextValue("After East"));
        sheet.SetCell(new CellAddress(sheet.Id, 10_000, 1), new TextValue("West Total"));
        sheet.SetFormula(new CellAddress(sheet.Id, 10_000, 2), "SUBTOTAL(9,B5001:B9999)");
        sheet.SetCell(new CellAddress(sheet.Id, 10_001, 1), new TextValue("After West"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20_000, 10));

        var command = new RemoveSubtotalRowsCommand(sheet.Id, range);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetValue(5_000, 1).Should().Be(new TextValue("After East"));
        sheet.GetValue(9_999, 1).Should().Be(new TextValue("After West"));

        command.Revert(context);

        sheet.GetValue(5_000, 1).Should().Be(new TextValue("East Total"));
        sheet.GetCell(5_000, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B1:B4999)");
        sheet.GetCell(5_000, 3)!.FormulaText.Should().Be("SUBTOTAL(9,C1:C4999)");
        sheet.GetValue(10_000, 1).Should().Be(new TextValue("West Total"));
        sheet.GetCell(10_000, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B5001:B9999)");
    }

    [BenchmarkFact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_SubtotalRowFinderSparseFormulas_ReportsTimingAndAllocatedBytes()
    {
        const uint rows = 100_000;
        const uint cols = 12;
        const int steps = 5;

        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 500; row <= rows; row += 500)
            sheet.SetFormula(new CellAddress(sheet.Id, row, 3), "SUM(A1:A2)");
        for (uint row = 1_000; row <= rows; row += 1_000)
            sheet.SetFormula(new CellAddress(sheet.Id, row, 12), "SUBTOTAL(9,L1:L2)");

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, rows, cols));
        var find = typeof(SubtotalCommand).Assembly
            .GetType("FreeX.Core.Commands.SubtotalRowFinder")!
            .GetMethod("Find", BindingFlags.Public | BindingFlags.Static)!;

        var warmup = (List<uint>)find.Invoke(null, [sheet, sheet.Id, range])!;
        warmup.Count.Should().Be(100);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var timings = new double[steps];
        var total = Stopwatch.StartNew();
        var checksum = 0;

        for (var i = 0; i < steps; i++)
        {
            var step = Stopwatch.StartNew();
            var rowsFound = (List<uint>)find.Invoke(null, [sheet, sheet.Id, range])!;
            step.Stop();

            checksum += rowsFound.Count;
            timings[i] = step.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        checksum.Should().Be(steps * 100);
        Console.WriteLine(
            "PERF SUBTOTAL_ROW_FINDER_SPARSE_FORMULAS " +
            $"rows={rows} cols={cols} steps={steps} " +
            $"subtotal_rows=100 " +
            $"formula_cells={sheet.FormulaCellCount} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} " +
            $"p95_ms={timings.OrderBy(x => x).ElementAt((int)Math.Ceiling(steps * 0.95) - 1):F2} " +
            $"max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
    }

    private static void SeedSubtotalRows(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(25));
    }

    [BenchmarkFact]
    public void Benchmark_SubtotalPlanManyGroupsWithPageBreaks_ReportsTimingAndAllocatedBytes()
    {
        const int groups = 2_500;
        const int steps = 5;

        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        for (uint row = 2; row <= groups + 1; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Group {row - 1}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));
        }

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, (uint)groups + 1, 2));
        var build = typeof(SubtotalCommand).Assembly
            .GetType("FreeX.Core.Commands.SubtotalPlanBuilder")!
            .GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!;

        build.Invoke(null, [sheet, range, 0u, true, true]).Should().NotBeNull();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var timings = new double[steps];
        var total = Stopwatch.StartNew();
        var checksum = 0;

        for (var i = 0; i < steps; i++)
        {
            var step = Stopwatch.StartNew();
            var plan = build.Invoke(null, [sheet, range, 0u, true, true]);
            step.Stop();

            checksum += plan?.GetHashCode() ?? 0;
            timings[i] = step.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        checksum.Should().NotBe(0);
        Console.WriteLine(
            "PERF SUBTOTAL_PLAN_MANY_GROUPS_PAGEBREAKS " +
            $"groups={groups} steps={steps} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} " +
            $"p95_ms={timings.OrderBy(x => x).ElementAt((int)Math.Ceiling(steps * 0.95) - 1):F2} " +
            $"max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
