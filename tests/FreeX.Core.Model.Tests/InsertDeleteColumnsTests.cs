using System.Diagnostics;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public class InsertDeleteColumnsTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    [Fact]
    public void InsertColumn_ShiftsCellsRight()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(100));

        new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1).Apply(ctx);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(100));
        sheet.GetCell(1, 3).Should().BeNull();
    }

    [Fact]
    public void InsertColumnRevert_RestoresOriginalState()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(100));

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetValue(1, 3).Should().Be(new NumberValue(100));
        sheet.GetCell(1, 4).Should().BeNull();
    }

    [Fact]
    public void InsertColumn_ShiftsCustomColumnWidthsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColumnWidths[3] = 15;
        sheet.ColumnWidths[5] = 25;

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ColumnWidths.Should().NotContainKey(3);
        sheet.ColumnWidths.Should().NotContainKey(4);
        sheet.ColumnWidths[5].Should().Be(15);
        sheet.ColumnWidths[7].Should().Be(25);

        cmd.Revert(ctx);

        sheet.ColumnWidths[3].Should().Be(15);
        sheet.ColumnWidths[5].Should().Be(25);
        sheet.ColumnWidths.Should().NotContainKey(7);
    }

    [Fact]
    public void InsertColumn_ShiftsCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 2, 3);
        var shifted = new CellAddress(sheet.Id, 2, 5);
        sheet.Comments[original] = "Check this";

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.Comments.Should().NotContainKey(original);
        sheet.Comments[shifted].Should().Be("Check this");

        cmd.Revert(ctx);

        sheet.Comments[original].Should().Be("Check this");
        sheet.Comments.Should().NotContainKey(shifted);
    }

    [Fact]
    public void InsertColumn_ShiftsThreadedCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 2, 3);
        var shifted = new CellAddress(sheet.Id, 2, 5);
        sheet.ThreadedComments[original] = new ThreadedComment("Check this", "Quinn");

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ThreadedComments.Should().NotContainKey(original);
        sheet.ThreadedComments[shifted].Should().Be(new ThreadedComment("Check this", "Quinn"));

        cmd.Revert(ctx);

        sheet.ThreadedComments[original].Should().Be(new ThreadedComment("Check this", "Quinn"));
        sheet.ThreadedComments.Should().NotContainKey(shifted);
    }

    [Fact]
    public void InsertColumn_ShiftsRuleRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var validation = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 6)),
            Type = DvType.List,
            Formula1 = "A,B"
        };
        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 2, 6)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.DataValidations.Add(validation);
        sheet.ConditionalFormats.Add(format);

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        validation.AppliesTo.Start.Col.Should().Be(7);
        validation.AppliesTo.End.Col.Should().Be(8);
        format.AppliesTo.Start.Col.Should().Be(7);
        format.AppliesTo.End.Col.Should().Be(8);

        cmd.Revert(ctx);

        validation.AppliesTo.Start.Col.Should().Be(5);
        validation.AppliesTo.End.Col.Should().Be(6);
        format.AppliesTo.Start.Col.Should().Be(5);
        format.AppliesTo.End.Col.Should().Be(6);
    }

    [Fact]
    public void InsertColumn_ShiftsNamedRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        wb.DefineNamedRange("Sales", new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 1, 6)));

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        wb.NamedRanges["Sales"].Start.Col.Should().Be(7);
        wb.NamedRanges["Sales"].End.Col.Should().Be(8);

        cmd.Revert(ctx);

        wb.NamedRanges["Sales"].Start.Col.Should().Be(5);
        wb.NamedRanges["Sales"].End.Col.Should().Be(6);
    }

    [Fact]
    public void InsertColumn_ShiftsPrintAreaAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 3, 6));

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.PrintArea!.Value.Start.Col.Should().Be(7);
        sheet.PrintArea.Value.End.Col.Should().Be(8);

        cmd.Revert(ctx);

        sheet.PrintArea!.Value.Start.Col.Should().Be(5);
        sheet.PrintArea.Value.End.Col.Should().Be(6);
    }

    [Fact]
    public void InsertColumn_ShiftsColumnPageBreaksAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColumnPageBreaks.Add(3);
        sheet.ColumnPageBreaks.Add(8);

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ColumnPageBreaks.Should().Equal(5u, 10u);

        cmd.Revert(ctx);

        sheet.ColumnPageBreaks.Should().Equal(3u, 8u);
    }

    [Fact]
    public void DeleteColumn_RemovesAndShiftsLeft()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(30));

        new DeleteColumnsCommand(sheet.Id, startCol: 2, count: 1).Apply(ctx);

        sheet.GetValue(1, 2).Should().Be(new NumberValue(30));
        sheet.GetCell(1, 3).Should().BeNull();
    }

    [Fact]
    public void DeleteColumnRevert_RestoresCells()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(30));

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 2, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetValue(1, 2).Should().Be(new NumberValue(20));
        sheet.GetValue(1, 3).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void DeleteColumn_ShiftsCustomColumnWidthsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColumnWidths[2] = 12;
        sheet.ColumnWidths[4] = 24;
        sheet.ColumnWidths[6] = 36;

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ColumnWidths[2].Should().Be(12);
        sheet.ColumnWidths[4].Should().Be(36);
        sheet.ColumnWidths.Should().NotContainKey(3);
        sheet.ColumnWidths.Should().NotContainKey(6);

        cmd.Revert(ctx);

        sheet.ColumnWidths[2].Should().Be(12);
        sheet.ColumnWidths[4].Should().Be(24);
        sheet.ColumnWidths[6].Should().Be(36);
    }

    [Fact]
    public void DeleteColumn_ShiftsHiddenColumnsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.HiddenCols.Add(2);
        sheet.HiddenCols.Add(4);
        sheet.HiddenCols.Add(6);

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.HiddenCols.Should().BeEquivalentTo(new[] { 2u, 4u });

        cmd.Revert(ctx);

        sheet.HiddenCols.Should().BeEquivalentTo(new[] { 2u, 4u, 6u });
    }

    [Fact]
    public void DeleteColumn_ShiftsCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var deleted = new CellAddress(sheet.Id, 2, 3);
        var originalRight = new CellAddress(sheet.Id, 2, 6);
        var shiftedRight = new CellAddress(sheet.Id, 2, 4);
        sheet.Comments[deleted] = "Remove with column";
        sheet.Comments[originalRight] = "Move left";

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.Comments.Should().NotContainKey(deleted);
        sheet.Comments.Should().NotContainKey(originalRight);
        sheet.Comments[shiftedRight].Should().Be("Move left");

        cmd.Revert(ctx);

        sheet.Comments[deleted].Should().Be("Remove with column");
        sheet.Comments[originalRight].Should().Be("Move left");
        sheet.Comments.Should().NotContainKey(shiftedRight);
    }

    [Fact]
    public void DeleteColumn_ShiftsThreadedCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var deleted = new CellAddress(sheet.Id, 2, 3);
        var originalRight = new CellAddress(sheet.Id, 2, 6);
        var shiftedRight = new CellAddress(sheet.Id, 2, 4);
        sheet.ThreadedComments[deleted] = new ThreadedComment("Remove with column", "Quinn");
        sheet.ThreadedComments[originalRight] = new ThreadedComment("Move left", "Codex");

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ThreadedComments.Should().NotContainKey(deleted);
        sheet.ThreadedComments.Should().NotContainKey(originalRight);
        sheet.ThreadedComments[shiftedRight].Should().Be(new ThreadedComment("Move left", "Codex"));

        cmd.Revert(ctx);

        sheet.ThreadedComments[deleted].Should().Be(new ThreadedComment("Remove with column", "Quinn"));
        sheet.ThreadedComments[originalRight].Should().Be(new ThreadedComment("Move left", "Codex"));
        sheet.ThreadedComments.Should().NotContainKey(shiftedRight);
    }

    [Fact]
    public void DeleteColumn_ShiftsRuleRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var validation = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 6), new CellAddress(sheet.Id, 1, 7)),
            Type = DvType.List,
            Formula1 = "A,B"
        };
        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 6), new CellAddress(sheet.Id, 2, 7)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.DataValidations.Add(validation);
        sheet.ConditionalFormats.Add(format);

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        validation.AppliesTo.Start.Col.Should().Be(4);
        validation.AppliesTo.End.Col.Should().Be(5);
        format.AppliesTo.Start.Col.Should().Be(4);
        format.AppliesTo.End.Col.Should().Be(5);

        cmd.Revert(ctx);

        validation.AppliesTo.Start.Col.Should().Be(6);
        validation.AppliesTo.End.Col.Should().Be(7);
        format.AppliesTo.Start.Col.Should().Be(6);
        format.AppliesTo.End.Col.Should().Be(7);
    }

    [Fact]
    public void DeleteColumn_ShiftsNamedRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        wb.DefineNamedRange("Sales", new GridRange(
            new CellAddress(sheet.Id, 1, 6),
            new CellAddress(sheet.Id, 1, 7)));

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        wb.NamedRanges["Sales"].Start.Col.Should().Be(4);
        wb.NamedRanges["Sales"].End.Col.Should().Be(5);

        cmd.Revert(ctx);

        wb.NamedRanges["Sales"].Start.Col.Should().Be(6);
        wb.NamedRanges["Sales"].End.Col.Should().Be(7);
    }

    [Fact]
    public void DeleteColumn_ShiftsPrintAreaAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 6),
            new CellAddress(sheet.Id, 3, 7));

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.PrintArea!.Value.Start.Col.Should().Be(4);
        sheet.PrintArea.Value.End.Col.Should().Be(5);

        cmd.Revert(ctx);

        sheet.PrintArea!.Value.Start.Col.Should().Be(6);
        sheet.PrintArea.Value.End.Col.Should().Be(7);
    }

    [Fact]
    public void DeleteColumn_ShiftsColumnPageBreaksAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColumnPageBreaks.Add(2);
        sheet.ColumnPageBreaks.Add(4);
        sheet.ColumnPageBreaks.Add(8);

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ColumnPageBreaks.Should().Equal(2u, 6u);

        cmd.Revert(ctx);

        sheet.ColumnPageBreaks.Should().Equal(2u, 4u, 8u);
    }

    [Fact]
    public void InsertColumn_InsideMergedRegionExpandsRegion()
    {
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, 2, 5)));

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 4, count: 2);
        cmd.Apply(ctx);

        sheet.MergedRegions[0].Start.Col.Should().Be(3);
        sheet.MergedRegions[0].End.Col.Should().Be(7);

        cmd.Revert(ctx);

        sheet.MergedRegions[0].Start.Col.Should().Be(3);
        sheet.MergedRegions[0].End.Col.Should().Be(5);
    }

    [Fact]
    public void DeleteColumn_ShiftsMergedRegionsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 6),
            new CellAddress(sheet.Id, 2, 7)));

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 4),
            new CellAddress(sheet.Id, 2, 5)));

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 6),
            new CellAddress(sheet.Id, 2, 7)));
    }

    [Fact]
    public void InsertColumns_WhenDataWouldBePushedPastMaxCol_ReturnsFailed()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, CellAddress.MaxCol), new NumberValue(1));

        var result = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1).Apply(ctx);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pushed past the last column");
    }

    [Fact]
    public void Benchmark_InsertColumnsWithDenseMovedCells_ReportsTiming()
    {
        const int iterations = 3;
        var (workbook, sheet, ctx) = SetupDenseShiftWorkbook();

        var warmup = new InsertColumnsCommand(sheet.Id, beforeCol: DenseShiftBeforeColumn);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.CellCount.Should().Be(DenseShiftRows * DenseShiftColumns);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new InsertColumnsCommand(sheet.Id, beforeCol: DenseShiftBeforeColumn);
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
        sheet.CellCount.Should().Be(DenseShiftRows * DenseShiftColumns);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1001));
        sheet.GetValue(DenseShiftRows, DenseShiftColumns).Should().Be(new NumberValue(DenseShiftRows * 1000 + DenseShiftColumns));
        Console.WriteLine(
            "PERF INSERT_COLUMNS_DENSE_SHIFT " +
            $"rows={DenseShiftRows} cols={DenseShiftColumns} before_col={DenseShiftBeforeColumn} " +
            $"moved_cells={DenseShiftRows * (DenseShiftColumns - DenseShiftBeforeColumn + 1)} steps={iterations} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_DeleteColumnsWithDenseMovedCells_ReportsTiming()
    {
        const int iterations = 3;
        var (workbook, sheet, ctx) = SetupDenseShiftWorkbook();

        var warmup = new DeleteColumnsCommand(sheet.Id, startCol: DenseShiftBeforeColumn);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.CellCount.Should().Be(DenseShiftRows * DenseShiftColumns);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new DeleteColumnsCommand(sheet.Id, startCol: DenseShiftBeforeColumn);
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
        sheet.CellCount.Should().Be(DenseShiftRows * DenseShiftColumns);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1001));
        sheet.GetValue(DenseShiftRows, DenseShiftColumns).Should().Be(new NumberValue(DenseShiftRows * 1000 + DenseShiftColumns));
        Console.WriteLine(
            "PERF DELETE_COLUMNS_DENSE_SHIFT " +
            $"rows={DenseShiftRows} cols={DenseShiftColumns} start_col={DenseShiftBeforeColumn} " +
            $"moved_cells={DenseShiftRows * (DenseShiftColumns - DenseShiftBeforeColumn)} steps={iterations} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_InsertColumnsWithDenseColumnMetadata_ReportsTiming()
    {
        const int iterations = 3;
        var (workbook, sheet, ctx) = SetupDenseColumnMetadataWorkbook();

        var warmup = new InsertColumnsCommand(sheet.Id, beforeCol: DenseMetadataStartColumn);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.ColumnWidths.Should().HaveCount(DenseMetadataColumns);
        sheet.Comments.Should().HaveCount(DenseMetadataColumns);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new InsertColumnsCommand(sheet.Id, beforeCol: DenseMetadataStartColumn);
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
        sheet.ColumnWidths.Should().HaveCount(DenseMetadataColumns);
        sheet.Comments.Should().ContainKey(new CellAddress(sheet.Id, 1, DenseMetadataColumns));
        sheet.ThreadedComments.Should().ContainKey(new CellAddress(sheet.Id, 2, DenseMetadataColumns));
        sheet.Hyperlinks.Should().ContainKey(new CellAddress(sheet.Id, 3, DenseMetadataColumns));
        sheet.HyperlinkMetadata.Should().ContainKey(new CellAddress(sheet.Id, 3, DenseMetadataColumns));
        Console.WriteLine(
            "PERF INSERT_COLUMNS_METADATA_SHIFT " +
            $"cols={DenseMetadataColumns} before_col={DenseMetadataStartColumn} steps={iterations} " +
            $"metadata_entries={DenseMetadataColumns * 6} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_DeleteColumnsWithDenseColumnMetadata_ReportsTiming()
    {
        const int iterations = 3;
        var (workbook, sheet, ctx) = SetupDenseColumnMetadataWorkbook();

        var warmup = new DeleteColumnsCommand(sheet.Id, startCol: DenseMetadataStartColumn);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.ColumnWidths.Should().HaveCount(DenseMetadataColumns);
        sheet.Comments.Should().HaveCount(DenseMetadataColumns);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new DeleteColumnsCommand(sheet.Id, startCol: DenseMetadataStartColumn);
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
        sheet.ColumnWidths.Should().HaveCount(DenseMetadataColumns);
        sheet.Comments.Should().ContainKey(new CellAddress(sheet.Id, 1, DenseMetadataColumns));
        sheet.ThreadedComments.Should().ContainKey(new CellAddress(sheet.Id, 2, DenseMetadataColumns));
        sheet.Hyperlinks.Should().ContainKey(new CellAddress(sheet.Id, 3, DenseMetadataColumns));
        sheet.HyperlinkMetadata.Should().ContainKey(new CellAddress(sheet.Id, 3, DenseMetadataColumns));
        Console.WriteLine(
            "PERF DELETE_COLUMNS_METADATA_SHIFT " +
            $"cols={DenseMetadataColumns} start_col={DenseMetadataStartColumn} steps={iterations} " +
            $"metadata_entries={DenseMetadataColumns * 6} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void ColumnCommands_UseCompactMetadataSnapshotsForUndo()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src",
            "FreeX.Core.Commands",
            "InsertDeleteColumnsCommand.cs"));

        source.Should().Contain("private List<KeyValuePair<uint, double>>? _columnWidthSnapshot;");
        source.Should().Contain("CaptureDictionary(sheet.ColumnWidths)");
        source.Should().Contain("CaptureDictionary(sheet.Comments)");
        source.Should().Contain("CaptureSet(sheet.HiddenCols)");
        source.Should().Contain("CaptureSortedSet(sheet.ColumnPageBreaks)");
        source.Should().NotContain("new Dictionary<uint, double>(sheet.ColumnWidths)");
        source.Should().NotContain("new Dictionary<CellAddress, string>(sheet.Comments)");
        source.Should().NotContain("[.. sheet.HiddenCols]");
        source.Should().NotContain("sheet.ColumnPageBreaks.ToList()");
    }

    private const int DenseShiftRows = 500;
    private const int DenseShiftColumns = 80;
    private const uint DenseShiftBeforeColumn = 2;
    private const int DenseMetadataColumns = 6_000;
    private const uint DenseMetadataStartColumn = 2;

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) SetupDenseShiftWorkbook()
    {
        var workbook = new Workbook("dense column shift perf");
        var sheet = workbook.AddSheet("Sheet1");

        for (uint row = 1; row <= DenseShiftRows; row++)
        {
            for (uint col = 1; col <= DenseShiftColumns; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * 1000 + col));
        }

        return (workbook, sheet, new SimpleCtx(workbook));
    }

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) SetupDenseColumnMetadataWorkbook()
    {
        var workbook = new Workbook("dense column metadata shift perf");
        var sheet = workbook.AddSheet("Sheet1");

        for (uint col = 1; col <= DenseMetadataColumns; col++)
        {
            sheet.ColumnWidths[col] = 9 + col % 11;
            sheet.HiddenCols.Add(col);
            sheet.ColumnPageBreaks.Add(col);

            sheet.Comments[new CellAddress(sheet.Id, 1, col)] = $"comment {col}";
            sheet.ThreadedComments[new CellAddress(sheet.Id, 2, col)] = new ThreadedComment($"thread {col}", "FreeX");
            var hyperlinkAddress = new CellAddress(sheet.Id, 3, col);
            sheet.Hyperlinks[hyperlinkAddress] = $"https://example.com/{col}";
            sheet.HyperlinkMetadata[hyperlinkAddress] = new HyperlinkMetadata(ScreenTip: $"Open column {col}");
        }

        return (workbook, sheet, new SimpleCtx(workbook));
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find workspace file {Path.Combine(parts)}");
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
