using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class InsertDeleteCellsCommandTests
{
    [Fact]
    public void InsertCellsShiftRight_ShiftsCellsInSelectedRowsOnlyAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
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
        var ctx = new SimpleCtx(wb);
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
        var ctx = new SimpleCtx(wb);
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
        var ctx = new SimpleCtx(wb);
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
        var ctx = new SimpleCtx(wb);
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
        var ctx = new SimpleCtx(wb);
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
        var ctx = new SimpleCtx(wb);
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
        var ctx = new SimpleCtx(wb);
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

        return (workbook, sheet, new SimpleCtx(workbook));
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
