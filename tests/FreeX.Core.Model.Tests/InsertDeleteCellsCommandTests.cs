using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class InsertDeleteCellsCommandTests
{
    [Fact]
    public void InsertCellsShiftRight_ShiftsCellsInSelectedRowsOnlyAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("B2"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        var command = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().BeOfType<BlankValue>();
        sheet.GetValue(1, 2).Should().Be(new TextValue("A1"));
        sheet.GetValue(1, 3).Should().Be(new TextValue("B1"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("B2"));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("B1"));
        sheet.GetCell(1, 3).Should().BeNull();
    }

    [Fact]
    public void InsertCellsShiftDown_ShiftsCellsInSelectedColumnsOnlyAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B1"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        var command = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().BeOfType<BlankValue>();
        sheet.GetValue(2, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("A2"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("B1"));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A2"));
        sheet.GetCell(3, 1).Should().BeNull();
    }

    [Fact]
    public void InsertCellsCommand_RejectsInvalidShiftDirection()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        var outcome = new InsertCellsCommand(sheet.Id, range, (InsertCellsShiftDirection)99).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetCell(2, 1).Should().BeNull();
    }

    [Fact]
    public void InsertCellsCommand_RejectsProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.IsProtected = true;
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        var outcome = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetCell(1, 2).Should().BeNull();
    }

    [Fact]
    public void DeleteCellsShiftLeft_ShiftsCellsInSelectedRowsOnlyAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("C1"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 2));

        var command = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("C1"));
        sheet.GetCell(1, 3).Should().BeNull();

        command.Revert(ctx);

        sheet.GetValue(1, 2).Should().Be(new TextValue("B1"));
        sheet.GetValue(1, 3).Should().Be(new TextValue("C1"));
    }

    [Fact]
    public void DeleteCellsShiftUp_ShiftsCellsInSelectedColumnsOnlyAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("A3"));
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));

        var command = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Up);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A3"));
        sheet.GetCell(3, 1).Should().BeNull();

        command.Revert(ctx);

        sheet.GetValue(2, 1).Should().Be(new TextValue("A2"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("A3"));
    }

    [Fact]
    public void DeleteCellsCommand_RejectsInvalidShiftDirection()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A2"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        var outcome = new DeleteCellsCommand(sheet.Id, range, (DeleteCellsShiftDirection)99).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A2"));
    }

    [Fact]
    public void DeleteCellsCommand_RejectsProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B1"));
        sheet.IsProtected = true;
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        var outcome = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("B1"));
    }

    [BenchmarkFact]
    public void Benchmark_InsertCellsShiftRightWithDenseMovedCells_ReportsTiming()
    {
        const int iterations = 3;
        var (workbook, sheet, ctx) = SetupDenseShiftWorkbook();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, DenseCellShiftRows, 2));

        var warmup = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.CellCount.Should().Be(DenseCellShiftRows * DenseCellShiftColumns);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);
            var step = Stopwatch.StartNew();
            command.Apply(ctx).Success.Should().BeTrue();
            command.Revert(ctx);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        workbook.SheetCount.Should().Be(1);
        sheet.CellCount.Should().Be(DenseCellShiftRows * DenseCellShiftColumns);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1001));
        sheet.GetValue(DenseCellShiftRows, DenseCellShiftColumns).Should().Be(new NumberValue(DenseCellShiftRows * 1000 + DenseCellShiftColumns));
        Console.WriteLine(
            "PERF INSERT_CELLS_SHIFT_RIGHT_DENSE " +
            $"rows={DenseCellShiftRows} cols={DenseCellShiftColumns} " +
            $"moved_cells={DenseCellShiftRows * (DenseCellShiftColumns - 1)} steps={iterations} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [BenchmarkFact]
    public void Benchmark_InsertCellsShiftRightSingleRow_ReportsTiming()
    {
        const int iterations = 5;
        var (workbook, sheet, ctx) = SetupDenseShiftWorkbook();
        var range = new GridRange(
            new CellAddress(sheet.Id, 200, 2),
            new CellAddress(sheet.Id, 200, 2));

        var warmup = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.CellCount.Should().Be(DenseCellShiftRows * DenseCellShiftColumns);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);
            var step = Stopwatch.StartNew();
            command.Apply(ctx).Success.Should().BeTrue();
            command.Revert(ctx);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        workbook.SheetCount.Should().Be(1);
        sheet.CellCount.Should().Be(DenseCellShiftRows * DenseCellShiftColumns);
        sheet.GetValue(200, 1).Should().Be(new NumberValue(200001));
        sheet.GetValue(200, DenseCellShiftColumns).Should().Be(new NumberValue(200000 + DenseCellShiftColumns));
        Console.WriteLine(
            "PERF INSERT_CELLS_SHIFT_RIGHT_SINGLE_ROW " +
            $"rows={DenseCellShiftRows} cols={DenseCellShiftColumns} moved_cells={DenseCellShiftColumns - 1} " +
            $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    private const int DenseCellShiftRows = 400;
    private const int DenseCellShiftColumns = 80;

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) SetupDenseShiftWorkbook()
    {
        var workbook = new Workbook("dense cell shift perf");
        var sheet = workbook.AddSheet("Sheet1");

        for (uint row = 1; row <= DenseCellShiftRows; row++)
        {
            for (uint col = 1; col <= DenseCellShiftColumns; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * 1000 + col));
        }

        return (workbook, sheet, new TestCommandContext(workbook));
    }

    // ── Formula rewrite tests ─────────────────────────────────────────────────

    [Fact]
    public void InsertCellsShiftDown_RewritesFormulaInBandColumn()
    {
        // B5 has =A5. Insert cells shift-down at A1:A1 → A5 moves to A6.
        // B5's formula =A5 is inside band column A (col=1), so it should rewrite to =A6.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(99));  // A5
        var b5 = new Cell { Value = new NumberValue(0) };
        b5.FormulaText = "A5";
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), b5);  // B5 has =A5

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // A5 moved to A6
        sheet.GetValue(6, 1).Should().Be(new NumberValue(99));
        sheet.GetCell(5, 1).Should().BeNull();

        // B5's formula should have been rewritten: A5 → A6
        var b5After = sheet.GetCell(5, 2)!;
        b5After.FormulaText.Should().Be("A6");

        // Undo restores formulas and cells
        cmd.Revert(ctx);
        sheet.GetValue(5, 1).Should().Be(new NumberValue(99));
        sheet.GetCell(6, 1).Should().BeNull();
        sheet.GetCell(5, 2)!.FormulaText.Should().Be("A5");
    }

    [Fact]
    public void InsertCellsShiftDown_FormulaOutsideBandColumnUntouched()
    {
        // C5 has =B5. Insert cells shift-down at A1:A1 only affects column A band.
        // B5 and C5 are outside the band columns, so =B5 stays unchanged.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var c5 = new Cell { Value = new NumberValue(0) };
        c5.FormulaText = "B5";
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), c5);  // C5 has =B5

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // C5's formula should remain =B5 (B is outside band column A)
        sheet.GetCell(5, 3)!.FormulaText.Should().Be("B5");
    }

    [Fact]
    public void InsertCellsShiftDown_FormulaOutsideBandReferencingBandCellRewrites()
    {
        // C1 (outside band col A) has =A5. Insert at A1 shifts A5→A6.
        // =A5 references a cell inside the band column, so it should rewrite to =A6.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var c1 = new Cell { Value = new NumberValue(0) };
        c1.FormulaText = "A5";
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), c1);  // C1 has =A5

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(1, 3)!.FormulaText.Should().Be("A6");
    }

    [Fact]
    public void DeleteCellsShiftUp_ReferenceToCellInDeletedRangeBecomesRefError()
    {
        // B1 has =A2. Delete A2 shift-up → A2 is removed. =A2 should become =#REF!.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var b1 = new Cell { Value = new NumberValue(0) };
        b1.FormulaText = "A2";
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), b1);  // B1 has =A2

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var cmd = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(1, 2)!.FormulaText.Should().Be("#REF!");

        // Undo restores original formula
        cmd.Revert(ctx);
        sheet.GetCell(1, 2)!.FormulaText.Should().Be("A2");
    }

    [Fact]
    public void DeleteCellsShiftUp_ReferenceBelowDeletedRangeShiftsUp()
    {
        // B1 has =A3. Delete A2 shift-up → A3 slides to A2. =A3 should become =A2.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var b1 = new Cell { Value = new NumberValue(0) };
        b1.FormulaText = "A3";
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), b1);  // B1 has =A3

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var cmd = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(1, 2)!.FormulaText.Should().Be("A2");
    }

    [Fact]
    public void DeleteCellsShiftUp_ReferenceOutsideBandColumnUntouched()
    {
        // B1 has =B3. Delete A2 shift-up only affects column A; B3 is outside band.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var b1 = new Cell { Value = new NumberValue(0) };
        b1.FormulaText = "B3";
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), b1);

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var cmd = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // B3 is outside band col A; formula unchanged
        sheet.GetCell(1, 2)!.FormulaText.Should().Be("B3");
    }

    [Fact]
    public void InsertCellsShiftRight_RewritesFormulaInBandRow()
    {
        // Insert cells shift-right at A2:A2 (band: row 2, cols 1..MaxCol).
        // A2 has value 42; E2 (col 5) has formula =B2.
        // After insert: A2→blank, B2←original A2 (42), and all cols ≥1 in row 2 shift right by 1.
        // E2 moves to F2 and its formula =B2 (col 2 ≥ 1, in band) rewrites to =C2.
        // A formula in row 3 (=A2) is outside the band row [2..2] and stays unchanged.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));  // A2

        var e2 = new Cell { Value = new NumberValue(0) };
        e2.FormulaText = "B2";
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), e2);  // E2 has =B2

        // Row-3 formula referencing row-2 cell (outside band row) should still rewrite
        // because the referenced cell (A2, band row 2) shifted
        var a3 = new Cell { Value = new NumberValue(0) };
        a3.FormulaText = "A2";
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), a3);  // A3 has =A2

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // A2 now blank; B2 has original A2 value
        sheet.GetCell(2, 1).Should().BeNull();
        sheet.GetValue(2, 2).Should().Be(new NumberValue(42));

        // E2 moved to F2 (col 6), its formula =B2 → =C2 (B in band shifted to C)
        sheet.GetCell(2, 5).Should().BeNull();
        sheet.GetCell(2, 6)!.FormulaText.Should().Be("C2");

        // A3's formula =A2: A2 is in band (row 2, col A ≥ col 1) and was shifted to B2
        sheet.GetCell(3, 1)!.FormulaText.Should().Be("B2");

        cmd.Revert(ctx);
        // After undo: back to original
        sheet.GetValue(2, 1).Should().Be(new NumberValue(42));
        sheet.GetCell(2, 6).Should().BeNull();
        sheet.GetCell(2, 5)!.FormulaText.Should().Be("B2");
        sheet.GetCell(3, 1)!.FormulaText.Should().Be("A2");
    }

    [Fact]
    public void InsertCellsShiftRight_FormulaInDifferentRowUntouched()
    {
        // Insert shift-right at A2:A2 (band row 2 only).
        // Row 3 formula =A3: row 3 is outside band [2..2], untouched.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var c3 = new Cell { Value = new NumberValue(0) };
        c3.FormulaText = "A3";
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), c3);  // C3 has =A3

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // Row 3 is outside the band; formula unchanged
        sheet.GetCell(3, 3)!.FormulaText.Should().Be("A3");
    }

    // ── Merge guard tests ─────────────────────────────────────────────────────

    [Fact]
    public void InsertCellsShiftDown_PartialMergeOverlapRejectsOperation()
    {
        // A merge spanning A1:B1 (across row boundary of band col A).
        // Insert shift-down at A2:A2 (band: col A, rows 2+).
        // Merge A1:B1 is partially inside band col A but also in band col B (not in band).
        // Actually let's use: merge A1:A3 spans rows 1..3, band col A rows 2+.
        // The merge straddles the band start row (row 2): start.Row < bandStartRow but end.Row >= bandStartRow.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Merge A1:A3 spans band rows (col A, rows 2+) and before-band row (row 1)
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var outcome = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("merged");
        // Model unchanged
        sheet.MergedRegions.Should().HaveCount(1);
        sheet.GetCell(1, 1).Should().BeNull();
    }

    [Fact]
    public void InsertCellsShiftDown_MergeFullyInsideBandMovesWithShift()
    {
        // Merge A3:A4 is fully inside band (col A, rows 3+), should NOT block.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1)));

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var outcome = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down).Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Merge should have shifted down by 1
        sheet.MergedRegions.Should().HaveCount(1);
        sheet.MergedRegions[0].Start.Row.Should().Be(4u);
        sheet.MergedRegions[0].End.Row.Should().Be(5u);
    }

    [Fact]
    public void DeleteCellsShiftUp_PartialMergeOverlapRejectsOperation()
    {
        // Merge A2:A3 where delete is A2:A2. The merge is partially deleted.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1)));

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var outcome = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Up).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("merged");
    }

    [Fact]
    public void InsertCellsShiftRight_PartialMergeOverlapRejectsOperation()
    {
        // Merge A1:A3 (column A, rows 1-3). Insert shift-right at A2:A2 (band rows 2..2).
        // Merge spans rows 1-3, band is rows 2..2 only, so merge straddles band boundary.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var outcome = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("merged");
    }

    [Fact]
    public void DeleteCellsShiftLeft_PartialMergeOverlapRejectsOperation()
    {
        // Merge A2:C2 where delete is B2:C2. Merge has col A which is outside the band (band rows 2..2, deleted cols B..C).
        // Actually the band rows are the same, but the merge straddles the col edge of the deleted range.
        // Delete B2:B2 shift-left. Merge B2:C2 is partially in deleted range [B2:B2] and partially in shifted [C2+].
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 2, 3)));

        var range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 2));
        var outcome = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("merged");
    }

    [Fact]
    public void DeleteCellsShiftLeft_StartEdgeStraddleMergeShrinksNotDropped()
    {
        // Merge B2:C2. Delete only C2 shift-left: the merge STARTS before the deleted range
        // (col B) and ENDS inside it (col C). Column B survives, so the merge must shrink to
        // B2:B2 rather than being silently dropped.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 2, 3)));

        var range = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 2, 3));
        var outcome = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.MergedRegions.Should().ContainSingle();
        sheet.MergedRegions[0].Start.Row.Should().Be(2u);
        sheet.MergedRegions[0].End.Row.Should().Be(2u);
        sheet.MergedRegions[0].Start.Col.Should().Be(2u);
        sheet.MergedRegions[0].End.Col.Should().Be(2u);
    }

    [Fact]
    public void DeleteCellsShiftUp_StartEdgeStraddleMergeShrinksNotDropped()
    {
        // Merge A2:A3. Delete only A3 shift-up: the merge STARTS before the deleted range
        // (row 2) and ENDS inside it (row 3). Row 2 survives, so the merge must shrink to
        // A2:A2 rather than being silently dropped.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1)));

        var range = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1));
        var outcome = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Up).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.MergedRegions.Should().ContainSingle();
        sheet.MergedRegions[0].Start.Row.Should().Be(2u);
        sheet.MergedRegions[0].End.Row.Should().Be(2u);
        sheet.MergedRegions[0].Start.Col.Should().Be(1u);
        sheet.MergedRegions[0].End.Col.Should().Be(1u);
    }

    // ── Comments/hyperlinks move with cells ───────────────────────────────────

    [Fact]
    public void InsertCellsShiftDown_CommentMovesWithCell()
    {
        // Comment at A2. Insert shift-down at A1:A1 → comment should move to A3.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.Comments[a2] = "my comment";

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.Comments.Should().NotContainKey(a2);
        sheet.Comments[new CellAddress(sheet.Id, 3, 1)].Should().Be("my comment");

        cmd.Revert(ctx);
        sheet.Comments[a2].Should().Be("my comment");
        sheet.Comments.Should().NotContainKey(new CellAddress(sheet.Id, 3, 1));
    }

    [Fact]
    public void InsertCellsShiftRight_HyperlinkMovesWithCell()
    {
        // Hyperlink at B2. Insert shift-right at A2:A2 (band row 2) → hyperlink moves to C2.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.Hyperlinks[b2] = "https://example.com";

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.Hyperlinks.Should().NotContainKey(b2);
        sheet.Hyperlinks[new CellAddress(sheet.Id, 2, 3)].Should().Be("https://example.com");

        cmd.Revert(ctx);
        sheet.Hyperlinks[b2].Should().Be("https://example.com");
    }

    [Fact]
    public void DeleteCellsShiftUp_CommentOnDeletedCellRemoved_CommentBelowMoves()
    {
        // Comment at A2 (deleted). Comment at A4 (below, shifts to A3).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a4 = new CellAddress(sheet.Id, 4, 1);
        sheet.Comments[a2] = "deleted comment";
        sheet.Comments[a4] = "shifted comment";

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var cmd = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.Comments.Should().NotContainKey(a2);
        sheet.Comments.Should().NotContainKey(a4);
        sheet.Comments[new CellAddress(sheet.Id, 3, 1)].Should().Be("shifted comment");

        cmd.Revert(ctx);
        sheet.Comments[a2].Should().Be("deleted comment");
        sheet.Comments[a4].Should().Be("shifted comment");
        sheet.Comments.Should().NotContainKey(new CellAddress(sheet.Id, 3, 1));
    }

    [Fact]
    public void DeleteCellsShiftLeft_CommentOutsideBandRowUntouched()
    {
        // Comment at A5 (band row is 2..2). Delete shift-left at B2:B2 should not move A5's comment.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var a5 = new CellAddress(sheet.Id, 5, 1);
        sheet.Comments[a5] = "outside band";

        var range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 2));
        var cmd = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // A5's comment should be untouched
        sheet.Comments[a5].Should().Be("outside band");
    }

    // ── Range reference endpoint behavior across band edge ────────────────────

    [Fact]
    public void FormulaRewriter_InsertCellsShiftDown_RangeStartInsideBandShifts()
    {
        // A1:A5 where insert is at col A, rows 3+. Start A1 is above band, end A5 is inside band.
        // Per Excel: endpoints inside the band shift, endpoints outside don't.
        var op = new InsertCellsShiftDownOp("Sheet1", 3, CellAddress.MaxRow, 1, 1, 3, 1);
        var result = FormulaRewriter.Rewrite("A1:A5", op, "Sheet1");
        // A1 (row 1) is above band start row 3 → stays A1
        // A5 (row 5) is inside band col A, row >= 3 → shifts to A6
        result.Should().Be("A1:A6");
    }

    [Fact]
    public void FormulaRewriter_InsertCellsShiftRight_RangeShiftedInBandRow()
    {
        // Insert shift-right at col 2, band rows 1..1. B1:C1 reference.
        // Both B1 (col 2) and C1 (col 3) are at/right of insert col 2, in band row 1.
        var op = new InsertCellsShiftRightOp("Sheet1", 1, 1, 1, CellAddress.MaxCol, 2, 1);
        var result = FormulaRewriter.Rewrite("B1:C1", op, "Sheet1");
        result.Should().Be("C1:D1");
    }

    [Fact]
    public void FormulaRewriter_DeleteCellsShiftUp_RangeStartEndpointDeletedShrinksRange()
    {
        // Delete A2:A2 shift-up. Formula =A2:A3 has its START (A2) in the deleted band but its
        // END (A3) survives, so Excel SHRINKS the range (old A3 slides up to A2) rather than
        // collapsing it to #REF! — only a fully-deleted range becomes #REF!.
        var op = new DeleteCellsShiftUpOp("Sheet1", 2, 2, CellAddress.MaxRow, 1, 1, 1);
        var result = FormulaRewriter.Rewrite("A2:A3", op, "Sheet1");
        result.Should().Be("A2:A2");
    }

    [Fact]
    public void FormulaRewriter_DeleteCellsShiftLeft_RangeStartEndpointDeletedShrinksRange()
    {
        // Delete B1:B1 shift-left. Formula =B1:C1 has its START (B1) in the deleted band but its
        // END (C1) survives, so Excel SHRINKS the range (old C1 slides left to B1) rather than
        // collapsing it to #REF! — only a fully-deleted range becomes #REF!.
        var op = new DeleteCellsShiftLeftOp("Sheet1", 1, 1, 2, 2, CellAddress.MaxCol, 1);
        var result = FormulaRewriter.Rewrite("B1:C1", op, "Sheet1");
        result.Should().Be("B1:B1");
    }

    // ── CF / DV rule range adjustment on Insert/Delete Cells ─────────────────

    [Fact]
    public void InsertCellsShiftDown_DvRuleFullyInsideBand_MovesDown_AndUndoRestores()
    {
        // Band = column A (col 1). DV rule A5:A8, insert 1 row before A5.
        // Rule should move to A6:A9; DV lookup at A6 should find the rule; A5 should find nothing.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 1)),
            Type = DvType.List,
            Formula1 = "Yes,No"
        };
        sheet.DataValidations.Add(dvRule);

        // Insert 1 row at A5 (shift down) — band is column A only.
        var insertRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1));
        var cmd = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        dvRule.AppliesTo.Start.Row.Should().Be(6, "rule should have moved down by 1");
        dvRule.AppliesTo.End.Row.Should().Be(9);
        dvRule.AppliesTo.Start.Col.Should().Be(1);
        dvRule.AppliesTo.End.Col.Should().Be(1);

        // DV lookup via the cached service path.
        DataValidationService.GetApplicable(sheet, new CellAddress(sheet.Id, 6, 1))
            .Should().ContainSingle("rule now covers A6");
        DataValidationService.GetApplicable(sheet, new CellAddress(sheet.Id, 5, 1))
            .Should().BeEmpty("row 5 is the newly inserted blank row — no rule");

        cmd.Revert(ctx);

        dvRule.AppliesTo.Start.Row.Should().Be(5, "rule should be restored to A5:A8 on undo");
        dvRule.AppliesTo.End.Row.Should().Be(8);
        DataValidationService.GetApplicable(sheet, new CellAddress(sheet.Id, 5, 1))
            .Should().ContainSingle("rule restored to original range");
    }

    [Fact]
    public void InsertCellsShiftDown_DvRuleOutsideBandColumn_Unchanged()
    {
        // Band = column A (col 1). DV rule covers column B (col 2). Should not move.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 2), new CellAddress(sheet.Id, 8, 2)),
            Type = DvType.List,
            Formula1 = "A,B"
        };
        sheet.DataValidations.Add(dvRule);

        var insertRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1));
        new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Down).Apply(ctx).Success.Should().BeTrue();

        dvRule.AppliesTo.Start.Row.Should().Be(5, "rule in a different column is unchanged");
        dvRule.AppliesTo.End.Row.Should().Be(8);
    }

    [Fact]
    public void InsertCellsShiftDown_DvRulePartiallyOverlappingBand_Unchanged()
    {
        // Band = column A only. DV rule A5:B8 spans both A and B — partial col overlap with band.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
            Type = DvType.List,
            Formula1 = "X,Y"
        };
        sheet.DataValidations.Add(dvRule);

        var insertRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1));
        new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Down).Apply(ctx).Success.Should().BeTrue();

        dvRule.AppliesTo.Start.Row.Should().Be(5, "partial-overlap rule is left unchanged");
        dvRule.AppliesTo.End.Row.Should().Be(8);
        dvRule.AppliesTo.Start.Col.Should().Be(1);
        dvRule.AppliesTo.End.Col.Should().Be(2);
    }

    [Fact]
    public void InsertCellsShiftDown_CfRuleFullyInsideBand_MovesDown()
    {
        // CF rule A3:A6 with insert before A3 in column A band. Rule should shift to A4:A7.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var cfRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 6, 1)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.ConditionalFormats.Add(cfRule);

        var insertRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1));
        var cmd = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        cfRule.AppliesTo.Start.Row.Should().Be(4, "CF rule should shift down by 1");
        cfRule.AppliesTo.End.Row.Should().Be(7);

        cmd.Revert(ctx);
        cfRule.AppliesTo.Start.Row.Should().Be(3, "CF rule restored to original position on undo");
        cfRule.AppliesTo.End.Row.Should().Be(6);
    }

    [Fact]
    public void InsertCellsShiftRight_DvRuleFullyInsideBand_MovesRight_AndUndoRestores()
    {
        // Band = row 2 (rows 2..2). DV rule B2:D2. Insert 1 col before col 2.
        // Rule should move to C2:E2.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 4)),
            Type = DvType.List,
            Formula1 = "Red,Green,Blue"
        };
        sheet.DataValidations.Add(dvRule);

        var insertRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 2));
        var cmd = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Right);
        cmd.Apply(ctx).Success.Should().BeTrue();

        dvRule.AppliesTo.Start.Col.Should().Be(3, "rule should have moved right by 1");
        dvRule.AppliesTo.End.Col.Should().Be(5);
        dvRule.AppliesTo.Start.Row.Should().Be(2);

        DataValidationService.GetApplicable(sheet, new CellAddress(sheet.Id, 2, 3))
            .Should().ContainSingle("rule now covers C2");
        DataValidationService.GetApplicable(sheet, new CellAddress(sheet.Id, 2, 2))
            .Should().BeEmpty("col 2 is the new blank col");

        cmd.Revert(ctx);

        dvRule.AppliesTo.Start.Col.Should().Be(2, "rule restored on undo");
        dvRule.AppliesTo.End.Col.Should().Be(4);
    }

    [Fact]
    public void DeleteCellsShiftUp_DvRuleBelowDeletedRange_MovesUp_AndUndoRestores()
    {
        // Delete row 3 in column A. DV rule A5:A8 (fully below deleted row, in band col A).
        // Rule should shift up to A4:A7.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 1)),
            Type = DvType.List,
            Formula1 = "Yes,No"
        };
        sheet.DataValidations.Add(dvRule);

        var deleteRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1));
        var cmd = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        dvRule.AppliesTo.Start.Row.Should().Be(4, "rule should have moved up by 1");
        dvRule.AppliesTo.End.Row.Should().Be(7);

        DataValidationService.GetApplicable(sheet, new CellAddress(sheet.Id, 4, 1))
            .Should().ContainSingle("rule now starts at A4");
        DataValidationService.GetApplicable(sheet, new CellAddress(sheet.Id, 8, 1))
            .Should().BeEmpty("A8 no longer in rule after shift");

        cmd.Revert(ctx);

        dvRule.AppliesTo.Start.Row.Should().Be(5, "rule restored to A5:A8 on undo");
        dvRule.AppliesTo.End.Row.Should().Be(8);
        DataValidationService.GetApplicable(sheet, new CellAddress(sheet.Id, 5, 1))
            .Should().ContainSingle("rule restored");
    }

    [Fact]
    public void DeleteCellsShiftUp_DvRuleEntirelyInDeletedRange_IsRemoved_AndUndoRestores()
    {
        // Delete rows 5..6 in column A. DV rule A5:A6 entirely within the deleted range.
        // Rule should be removed; undo should restore it.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 6, 1)),
            Type = DvType.List,
            Formula1 = "X,Y"
        };
        sheet.DataValidations.Add(dvRule);

        var deleteRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 6, 1));
        var cmd = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().BeEmpty("rule was entirely within the deleted rows");

        cmd.Revert(ctx);

        sheet.DataValidations.Should().ContainSingle("rule restored on undo");
        sheet.DataValidations[0].AppliesTo.Start.Row.Should().Be(5, "rule AppliesTo restored");
        sheet.DataValidations[0].AppliesTo.End.Row.Should().Be(6);
    }

    [Fact]
    public void DeleteCellsShiftUp_DvRuleOutsideBandColumn_Unchanged()
    {
        // Delete row 3 in column A. DV rule in column B. Should not move.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 2), new CellAddress(sheet.Id, 8, 2)),
            Type = DvType.List,
            Formula1 = "A,B"
        };
        sheet.DataValidations.Add(dvRule);

        var deleteRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1));
        new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Up).Apply(ctx).Success.Should().BeTrue();

        dvRule.AppliesTo.Start.Row.Should().Be(5, "rule outside the band column is not affected");
        dvRule.AppliesTo.End.Row.Should().Be(8);
    }

    [Fact]
    public void DeleteCellsShiftUp_CfRuleBelowDeletedRange_MovesUp()
    {
        // Delete row 2 in column A. CF rule A4:A6 should shift to A3:A5.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var cfRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 4, 1), new CellAddress(sheet.Id, 6, 1)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.ConditionalFormats.Add(cfRule);

        var deleteRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var cmd = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        cfRule.AppliesTo.Start.Row.Should().Be(3, "CF rule should shift up by 1");
        cfRule.AppliesTo.End.Row.Should().Be(5);

        cmd.Revert(ctx);
        cfRule.AppliesTo.Start.Row.Should().Be(4, "CF rule restored on undo");
        cfRule.AppliesTo.End.Row.Should().Be(6);
    }

    [Fact]
    public void DeleteCellsShiftLeft_DvRuleRightOfDeletedRange_MovesLeft_AndUndoRestores()
    {
        // Delete col B (col 2) in row 3. DV rule D3:F3 (fully right, in band row 3).
        // Rule should shift left to C3:E3.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 3, 4), new CellAddress(sheet.Id, 3, 6)),
            Type = DvType.List,
            Formula1 = "P,Q,R"
        };
        sheet.DataValidations.Add(dvRule);

        var deleteRange = new GridRange(new CellAddress(sheet.Id, 3, 2), new CellAddress(sheet.Id, 3, 2));
        var cmd = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();

        dvRule.AppliesTo.Start.Col.Should().Be(3, "rule should move left by 1");
        dvRule.AppliesTo.End.Col.Should().Be(5);
        dvRule.AppliesTo.Start.Row.Should().Be(3);

        DataValidationService.GetApplicable(sheet, new CellAddress(sheet.Id, 3, 3))
            .Should().ContainSingle("rule now starts at C3");

        cmd.Revert(ctx);

        dvRule.AppliesTo.Start.Col.Should().Be(4, "rule restored on undo");
        dvRule.AppliesTo.End.Col.Should().Be(6);
        DataValidationService.GetApplicable(sheet, new CellAddress(sheet.Id, 3, 4))
            .Should().ContainSingle("rule restored to D3:F3");
    }

    [Fact]
    public void DeleteCellsShiftLeft_DvRuleEntirelyInDeletedCols_IsRemoved_AndUndoRestores()
    {
        // Delete cols B..C in row 3. DV rule B3:C3 entirely within the deleted range.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 3, 2), new CellAddress(sheet.Id, 3, 3)),
            Type = DvType.List,
            Formula1 = "M,N"
        };
        sheet.DataValidations.Add(dvRule);

        var deleteRange = new GridRange(new CellAddress(sheet.Id, 3, 2), new CellAddress(sheet.Id, 3, 3));
        var cmd = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().BeEmpty("rule was entirely within the deleted cols");

        cmd.Revert(ctx);

        sheet.DataValidations.Should().ContainSingle("rule restored on undo");
        sheet.DataValidations[0].AppliesTo.Start.Col.Should().Be(2, "AppliesTo restored");
        sheet.DataValidations[0].AppliesTo.End.Col.Should().Be(3);
    }

    [Fact]
    public void InsertCellsShiftDown_DvRuleAboveInsertPoint_Unchanged()
    {
        // Band = column A. DV rule A1:A4, insert before A5. Rule is above insert point → unchanged.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            Type = DvType.List,
            Formula1 = "Yes,No"
        };
        sheet.DataValidations.Add(dvRule);

        var insertRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1));
        new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Down).Apply(ctx).Success.Should().BeTrue();

        dvRule.AppliesTo.Start.Row.Should().Be(1, "rule above insert point is unchanged");
        dvRule.AppliesTo.End.Row.Should().Be(4);
    }

    // ── Revert ordering / undo-redo-undo convergence ──────────────────────────

    [Fact]
    public void InsertCellsShiftDown_UndoRedoUndo_ModelConvergesWithFormulas()
    {
        // Regression guard for the RestoreFormulas-before-Snapshot.Restore ordering in Revert.
        // The formula snapshot is keyed by shifted (post-Apply) addresses; if Snapshot.Restore ran
        // first the shifted-address lookup would find nothing and formulas would be lost.
        // This test verifies that Apply→Undo→Redo→Undo leaves the model identical to the start.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // A1 = 10, A2 has =A1+1, B1 = 99
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        var a2 = new Cell { Value = new NumberValue(11) };
        a2.FormulaText = "A1+1";
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), a2);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(99));

        // Snapshot initial state
        var initialA1 = sheet.GetValue(1, 1);
        var initialA2Formula = sheet.GetCell(2, 1)!.FormulaText;
        var initialA2Value = sheet.GetValue(2, 1);
        var initialB1 = sheet.GetValue(1, 2);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);

        // ── Apply ──
        cmd.Apply(ctx).Success.Should().BeTrue();
        // A2 moved to A3, formula A1+1 should rewrite to A2+1 (A1 stays, was above insert at row 1)
        // Actually: insert at row 1 shifts everything at row>=1 down. A1→A2, A2→A3.
        // Wait: the insert is AT row 1 in column A, shifting down. A1 moves to A2, A2 moves to A3.
        // Formula in A2 (=A1+1) moves to A3. The reference A1 is at row 1 >= insertBeforeRow 1 in band col A,
        // so it gets rewritten to A2+1.
        sheet.GetCell(1, 1).Should().BeNull("row 1 is empty after insert");
        sheet.GetValue(2, 1).Should().Be(new NumberValue(10), "A1 value moved to A2");
        sheet.GetCell(3, 1).Should().NotBeNull("formula cell moved to A3");
        sheet.GetCell(3, 1)!.FormulaText.Should().Be("A2+1", "formula reference rewritten after insert");
        sheet.GetValue(1, 2).Should().Be(new NumberValue(99), "B1 untouched (outside band)");

        // ── Undo ──
        cmd.Revert(ctx);
        sheet.GetValue(1, 1).Should().Be(initialA1, "A1 restored after undo");
        sheet.GetCell(2, 1).Should().NotBeNull("A2 formula cell restored after undo");
        sheet.GetCell(2, 1)!.FormulaText.Should().Be(initialA2Formula, "formula text restored after undo");
        sheet.GetCell(3, 1).Should().BeNull("A3 empty after undo");
        sheet.GetValue(1, 2).Should().Be(initialB1, "B1 unchanged after undo");

        // ── Redo ──
        cmd.Apply(ctx).Success.Should().BeTrue("redo must succeed");
        sheet.GetCell(1, 1).Should().BeNull("row 1 empty again after redo");
        sheet.GetValue(2, 1).Should().Be(new NumberValue(10), "A2 has original A1 value after redo");
        sheet.GetCell(3, 1)!.FormulaText.Should().Be("A2+1", "formula rewritten again after redo");

        // ── Undo again ──
        cmd.Revert(ctx);
        sheet.GetValue(1, 1).Should().Be(initialA1, "A1 restored after second undo");
        sheet.GetCell(2, 1)!.FormulaText.Should().Be(initialA2Formula,
            "formula text must be restored correctly after second undo — validates Revert ordering");
        sheet.GetCell(3, 1).Should().BeNull("A3 empty after second undo");
        sheet.GetValue(1, 2).Should().Be(initialB1, "B1 unchanged after second undo");
    }

    // ── AdditionalRanges delete gap regression tests ──────────────────────────

    [Fact]
    public void DeleteCellsShiftUp_DvAdditionalRangeInDeletedBand_RemovedEvenWhenPrimaryUnchanged()
    {
        // Regression: AdjustRulesDeleteShiftUp only called AdjustAdditionalRanges when the primary
        // AppliesTo was translated.  If primary had partial overlap (→ unchanged), additional ranges
        // fully inside the deleted band were silently left dangling.
        //
        // Setup: primary AppliesTo spans rows 2..8 in col A (partial overlap with delete band rows 4..5)
        //        → primary left unchanged.  AdditionalRange covers rows 4..5 in col A (fully deleted).
        //        Expected: additional range removed; primary unchanged.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 8, 1)),
            Type = DvType.List,
            Formula1 = "A,B"
        };
        dvRule.AdditionalRanges.Add(
            new GridRange(new CellAddress(sheet.Id, 4, 1), new CellAddress(sheet.Id, 5, 1)));
        sheet.DataValidations.Add(dvRule);

        // Delete rows 4..5 in col A band.  Primary A2:A8 has partial overlap → unchanged.
        var deleteRange = new GridRange(new CellAddress(sheet.Id, 4, 1), new CellAddress(sheet.Id, 5, 1));
        var cmd = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // Primary was partially overlapping → left unchanged
        dvRule.AppliesTo.Start.Row.Should().Be(2, "primary partial-overlap range unchanged");
        dvRule.AppliesTo.End.Row.Should().Be(8);

        // AdditionalRange fully inside deleted band → must be removed
        dvRule.AdditionalRanges.Should().BeEmpty(
            "additional range fully inside deleted band must be removed even when primary is unchanged");

        // Undo restores both
        cmd.Revert(ctx);
        dvRule.AppliesTo.Start.Row.Should().Be(2, "primary restored after undo");
        dvRule.AppliesTo.End.Row.Should().Be(8);
        dvRule.AdditionalRanges.Should().ContainSingle("additional range restored after undo");
        dvRule.AdditionalRanges[0].Start.Row.Should().Be(4, "additional range AppliesTo restored");
        dvRule.AdditionalRanges[0].End.Row.Should().Be(5);
    }

    [Fact]
    public void DeleteCellsShiftLeft_DvAdditionalRangeRightOfDeleted_TranslatedEvenWhenPrimaryUnchanged()
    {
        // Symmetric left-direction test: additional range fully to the right of deleted cols
        // must translate left even when primary has partial col overlap (→ unchanged).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 6)),
            Type = DvType.List,
            Formula1 = "X,Y"
        };
        // Additional range fully to the right of deleted cols 3..4
        dvRule.AdditionalRanges.Add(
            new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 2, 6)));
        sheet.DataValidations.Add(dvRule);

        // Delete cols 3..4 in row 2 band.  Primary B2:F2 partial overlap (cols 3..4 inside, col 1..2 and 5..6 outside).
        var deleteRange = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 2, 4));
        var cmd = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // Primary partial overlap → unchanged
        dvRule.AppliesTo.Start.Col.Should().Be(1, "primary partial-overlap range col start unchanged");
        dvRule.AppliesTo.End.Col.Should().Be(6, "primary partial-overlap range col end unchanged");

        // Additional range E2:F2 (cols 5..6) is fully to the right of deleted cols 3..4 → shifts left by 2
        dvRule.AdditionalRanges.Should().ContainSingle("additional range should still exist (translated)");
        dvRule.AdditionalRanges[0].Start.Col.Should().Be(3, "additional range col 5 → col 3 after left shift by 2");
        dvRule.AdditionalRanges[0].End.Col.Should().Be(4, "additional range col 6 → col 4 after left shift by 2");

        // Undo restores
        cmd.Revert(ctx);
        dvRule.AdditionalRanges.Should().ContainSingle("additional range restored after undo");
        dvRule.AdditionalRanges[0].Start.Col.Should().Be(5, "additional range start col restored after undo");
        dvRule.AdditionalRanges[0].End.Col.Should().Be(6, "additional range end col restored after undo");
    }

    // ── X2 regression: CF/DV formula-text rewrites on band-scoped Insert/Delete ──

    [Fact]
    public void InsertCellsShiftDown_RewritesCfFormulaTextAndUndoRestores()
    {
        // CF rule with FormulaText '=A1>0' over A1:A1; insert one cell at A1 shift-down.
        // After insert, A1>0 ref that was at row 1 is now pushed to row 2 → FormulaText should become '=A2>0'.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var cfRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>0"
        };
        sheet.ConditionalFormats.Add(cfRule);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Type = DvType.Custom,
            Formula1 = "A1<>\"\"",
            AlertStyle = DvAlertStyle.Stop
        };
        sheet.DataValidations.Add(dvRule);

        // Insert one cell at A1 shifting down (col band A:A, insert before row 1).
        var insertRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var cmd = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // CF FormulaText should have shifted from A1 to A2.
        cfRule.FormulaText.Should().Be("A2>0", "insert-down shifts the row-1 ref to row 2");
        // DV Formula1 should also have shifted.
        dvRule.Formula1.Should().Be("A2<>\"\"", "insert-down shifts the row-1 ref to row 2");

        // Undo must restore originals.
        cmd.Revert(ctx);
        cfRule.FormulaText.Should().Be("A1>0", "undo restores CF formula");
        dvRule.Formula1.Should().Be("A1<>\"\"", "undo restores DV formula");
    }

    [Fact]
    public void InsertCellsShiftRight_RewritesCfFormulaTextAndUndoRestores()
    {
        // CF rule with FormulaText 'A1>0' over A1:A1; insert one cell at A1 shift-right.
        // After insert, the col-1 ref should shift to col-2 → FormulaText becomes 'B1>0'.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var cfRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>0"
        };
        sheet.ConditionalFormats.Add(cfRule);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Type = DvType.Custom,
            Formula1 = "A1<>\"\"",
            AlertStyle = DvAlertStyle.Stop
        };
        sheet.DataValidations.Add(dvRule);

        var insertRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var cmd = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Right);
        cmd.Apply(ctx).Success.Should().BeTrue();

        cfRule.FormulaText.Should().Be("B1>0", "insert-right shifts the col-1 ref to col 2");
        dvRule.Formula1.Should().Be("B1<>\"\"", "insert-right shifts the col-1 ref to col 2");

        cmd.Revert(ctx);
        cfRule.FormulaText.Should().Be("A1>0", "undo restores CF formula after insert-right");
        dvRule.Formula1.Should().Be("A1<>\"\"", "undo restores DV formula after insert-right");
    }

    [Fact]
    public void DeleteCellsShiftUp_RewritesCfFormulaTextAndUndoRestores()
    {
        // CF rule with FormulaText 'A2>0'; delete row 1 in col-A band (shift-up).
        // After delete, A2>0 should shift to A1>0.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var cfRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1)),
            RuleType = CfRuleType.Formula,
            FormulaText = "A2>0"
        };
        sheet.ConditionalFormats.Add(cfRule);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1)),
            Type = DvType.Custom,
            Formula1 = "A2<>\"\"",
            AlertStyle = DvAlertStyle.Stop
        };
        sheet.DataValidations.Add(dvRule);

        // Delete cell A1, shift up → cells below move up.
        var deleteRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var cmd = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        cfRule.FormulaText.Should().Be("A1>0", "delete-up shifts A2 ref to A1");
        dvRule.Formula1.Should().Be("A1<>\"\"", "delete-up shifts A2 ref to A1");

        cmd.Revert(ctx);
        cfRule.FormulaText.Should().Be("A2>0", "undo restores CF formula after delete-up");
        dvRule.Formula1.Should().Be("A2<>\"\"", "undo restores DV formula after delete-up");
    }
}
