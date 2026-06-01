using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using System.Diagnostics;

namespace FreeX.Core.Model.Tests;

public sealed class AdvancedFilterCommandTests
{
    [Fact]
    public void AdvancedFilter_InPlace_UsesCriteriaRowsAsOrAndColumnsAsAnd()
    {
        var (wb, sheet, ctx) = Setup();
        SeedList(sheet);
        Set(sheet, 1, 6, "Region");
        Set(sheet, 1, 7, "Sales");
        Set(sheet, 2, 6, "East");
        Set(sheet, 2, 7, ">100");
        Set(sheet, 3, 6, "West");
        Set(sheet, 3, 7, "<100");

        var command = new AdvancedFilterCommand(
            ListRange(sheet, 1, 1, 5, 3),
            CriteriaRange: ListRange(sheet, 1, 6, 3, 7),
            CopyTo: null,
            UniqueRecordsOnly: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 3u]);
        sheet.FilterHiddenRows.Should().NotContain([4u, 5u]);

        command.Revert(ctx);
        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void AdvancedFilter_RejectsListRangeOutsideWorkbook()
    {
        var (wb, sheet, ctx) = Setup();
        SeedList(sheet);
        Set(sheet, 1, 6, "Region");
        Set(sheet, 2, 6, "East");
        var staleSheetId = SheetId.New();

        var command = new AdvancedFilterCommand(
            new GridRange(new CellAddress(staleSheetId, 1, 1), new CellAddress(staleSheetId, 5, 3)),
            CriteriaRange: ListRange(sheet, 1, 6, 2, 6),
            CopyTo: null,
            UniqueRecordsOnly: false);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("list range");
        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void AdvancedFilter_RejectsCriteriaRangeOutsideWorkbook()
    {
        var (wb, sheet, ctx) = Setup();
        SeedList(sheet);
        var staleSheetId = SheetId.New();

        var command = new AdvancedFilterCommand(
            ListRange(sheet, 1, 1, 5, 3),
            CriteriaRange: new GridRange(new CellAddress(staleSheetId, 1, 1), new CellAddress(staleSheetId, 2, 1)),
            CopyTo: null,
            UniqueRecordsOnly: false);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("criteria range");
        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void AdvancedFilter_InPlace_RejectsProtectedSheetWithoutUseAutoFilterPermission()
    {
        var (wb, sheet, ctx) = Setup();
        SeedList(sheet);
        Set(sheet, 1, 6, "Region");
        Set(sheet, 2, 6, "East");
        sheet.IsProtected = true;

        var command = new AdvancedFilterCommand(
            ListRange(sheet, 1, 1, 5, 3),
            CriteriaRange: ListRange(sheet, 1, 6, 2, 6),
            CopyTo: null,
            UniqueRecordsOnly: false);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void AdvancedFilter_InPlace_AllowsProtectedSheetWithUseAutoFilterPermission()
    {
        var (wb, sheet, ctx) = Setup();
        SeedList(sheet);
        Set(sheet, 1, 6, "Region");
        Set(sheet, 2, 6, "East");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UseAutoFilter);

        var command = new AdvancedFilterCommand(
            ListRange(sheet, 1, 1, 5, 3),
            CriteriaRange: ListRange(sheet, 1, 6, 2, 6),
            CopyTo: null,
            UniqueRecordsOnly: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
    }

    [Fact]
    public void AdvancedFilter_CopyToLocation_CopiesHeadersAndMatchingRowsWithoutHidingSourceRows()
    {
        var (wb, sheet, ctx) = Setup();
        SeedList(sheet);
        Set(sheet, 1, 6, "Region");
        Set(sheet, 2, 6, "East");

        var command = new AdvancedFilterCommand(
            ListRange(sheet, 1, 1, 5, 3),
            CriteriaRange: ListRange(sheet, 1, 6, 2, 6),
            CopyTo: Addr(sheet, 8, 1),
            UniqueRecordsOnly: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEmpty();
        sheet.GetValue(8, 1).Should().Be(new TextValue("Region"));
        sheet.GetValue(8, 2).Should().Be(new TextValue("Sales"));
        sheet.GetValue(8, 3).Should().Be(new TextValue("Rep"));
        sheet.GetValue(9, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(9, 2).Should().Be(new NumberValue(90));
        sheet.GetValue(10, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(10, 2).Should().Be(new NumberValue(120));

        command.Revert(ctx);
        sheet.GetCell(8, 1).Should().BeNull();
        sheet.GetCell(9, 1).Should().BeNull();
        sheet.GetCell(10, 1).Should().BeNull();
    }

    [Fact]
    public void AdvancedFilter_CopyToLocation_AllowsProtectedSheetWhenDestinationCellsCanBeEdited()
    {
        var (wb, sheet, ctx) = Setup();
        SeedList(sheet);
        Set(sheet, 1, 6, "Region");
        Set(sheet, 2, 6, "East");
        sheet.AllowEditRanges.Add(ListRange(sheet, 8, 1, 10, 3));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UseAutoFilter);

        var command = new AdvancedFilterCommand(
            ListRange(sheet, 1, 1, 5, 3),
            CriteriaRange: ListRange(sheet, 1, 6, 2, 6),
            CopyTo: Addr(sheet, 8, 1),
            UniqueRecordsOnly: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(8, 1).Should().Be(new TextValue("Region"));
        sheet.GetValue(9, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(10, 1).Should().Be(new TextValue("East"));
    }

    [Fact]
    public void AdvancedFilter_CopyToLocation_RejectsProtectedSheetWhenDestinationCellsAreLocked()
    {
        var (wb, sheet, ctx) = Setup();
        SeedList(sheet);
        Set(sheet, 1, 6, "Region");
        Set(sheet, 2, 6, "East");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UseAutoFilter);

        var command = new AdvancedFilterCommand(
            ListRange(sheet, 1, 1, 5, 3),
            CriteriaRange: ListRange(sheet, 1, 6, 2, 6),
            CopyTo: Addr(sheet, 8, 1),
            UniqueRecordsOnly: false);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetCell(8, 1).Should().BeNull();
    }

    [Fact]
    public void AdvancedFilter_CopyToHeaderRange_CopiesOnlySelectedColumnsInHeaderOrder()
    {
        var (wb, sheet, ctx) = Setup();
        SeedList(sheet);
        Set(sheet, 1, 6, "Region");
        Set(sheet, 2, 6, "East");
        Set(sheet, 8, 1, "Rep");
        Set(sheet, 8, 2, "Region");

        var command = new AdvancedFilterCommand(
            ListRange(sheet, 1, 1, 5, 3),
            CriteriaRange: ListRange(sheet, 1, 6, 2, 6),
            CopyTo: Addr(sheet, 8, 1),
            UniqueRecordsOnly: false,
            CopyToRange: ListRange(sheet, 8, 1, 8, 2));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(8, 1).Should().Be(new TextValue("Rep"));
        sheet.GetValue(8, 2).Should().Be(new TextValue("Region"));
        sheet.GetValue(9, 1).Should().Be(new TextValue("Ana"));
        sheet.GetValue(9, 2).Should().Be(new TextValue("East"));
        sheet.GetValue(10, 1).Should().Be(new TextValue("Ana"));
        sheet.GetValue(10, 2).Should().Be(new TextValue("East"));
        sheet.GetCell(9, 3).Should().BeNull();

        command.Revert(ctx);
        sheet.GetValue(8, 1).Should().Be(new TextValue("Rep"));
        sheet.GetValue(8, 2).Should().Be(new TextValue("Region"));
        sheet.GetCell(9, 1).Should().BeNull();
    }

    [Fact]
    public void AdvancedFilter_AllowsCriteriaRangeOnAnotherSheet()
    {
        var (wb, sheet, ctx) = Setup();
        var criteriaSheet = wb.AddSheet("Criteria");
        SeedList(sheet);
        Set(criteriaSheet, 1, 1, "Region");
        Set(criteriaSheet, 2, 1, "East");

        var command = new AdvancedFilterCommand(
            ListRange(sheet, 1, 1, 5, 3),
            CriteriaRange: ListRange(criteriaSheet, 1, 1, 2, 1),
            CopyTo: null,
            UniqueRecordsOnly: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
    }

    [Fact]
    public void AdvancedFilter_CopyUnique_RemovesDuplicateOutputRows()
    {
        var (wb, sheet, ctx) = Setup();
        SeedList(sheet);
        Set(sheet, 6, 1, "East");
        Set(sheet, 6, 2, 120);
        Set(sheet, 6, 3, "Ana");
        Set(sheet, 1, 6, "Region");
        Set(sheet, 2, 6, "East");

        var command = new AdvancedFilterCommand(
            ListRange(sheet, 1, 1, 6, 3),
            CriteriaRange: ListRange(sheet, 1, 6, 2, 6),
            CopyTo: Addr(sheet, 8, 1),
            UniqueRecordsOnly: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(9, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(10, 1).Should().Be(new TextValue("East"));
        sheet.GetCell(11, 1).Should().BeNull();
    }

    [Fact]
    public void AdvancedFilter_CopyToLocation_ClearsStaleRowsFromPriorLargerOutput()
    {
        var (wb, sheet, ctx) = Setup();
        SeedList(sheet);
        Set(sheet, 1, 6, "Sales");
        Set(sheet, 2, 6, ">0");

        var first = new AdvancedFilterCommand(
            ListRange(sheet, 1, 1, 5, 3),
            CriteriaRange: ListRange(sheet, 1, 6, 2, 6),
            CopyTo: Addr(sheet, 8, 1),
            UniqueRecordsOnly: false);

        first.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(12, 1).Should().Be(new TextValue("West"));
        Set(sheet, 2, 6, ">120");

        var second = new AdvancedFilterCommand(
            ListRange(sheet, 1, 1, 5, 3),
            CriteriaRange: ListRange(sheet, 1, 6, 2, 6),
            CopyTo: Addr(sheet, 8, 1),
            UniqueRecordsOnly: false);

        second.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(8, 1).Should().Be(new TextValue("Region"));
        sheet.GetValue(9, 1).Should().Be(new TextValue("West"));
        sheet.GetCell(10, 1).Should().BeNull();
        sheet.GetCell(11, 1).Should().BeNull();
        sheet.GetCell(12, 1).Should().BeNull();

        second.Revert(ctx);
        sheet.GetValue(12, 1).Should().Be(new TextValue("West"));
    }

    [Fact]
    public void AdvancedFilter_RejectsCriteriaHeadersNotPresentInList()
    {
        var (wb, sheet, ctx) = Setup();
        SeedList(sheet);
        Set(sheet, 1, 6, "Missing");
        Set(sheet, 2, 6, "East");

        var command = new AdvancedFilterCommand(
            ListRange(sheet, 1, 1, 5, 3),
            CriteriaRange: ListRange(sheet, 1, 6, 2, 6),
            CopyTo: null,
            UniqueRecordsOnly: false);

        command.Apply(ctx).Success.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_AdvancedFilterCopyUniqueDenseRows_ReportsTimingAndAllocatedBytes()
    {
        const uint rows = 12000;
        const uint cols = 12;
        const int steps = 5;

        var (wb, sheet, ctx) = Setup();
        SeedDenseList(sheet, rows, cols);
        SeedDenseCriteria(sheet, rows + 4);

        var listRange = ListRange(sheet, 1, 1, rows + 1, cols);
        var criteriaRange = ListRange(sheet, rows + 4, 1, rows + 8, 3);
        var copyTo = Addr(sheet, rows + 12, 1);

        ApplyDenseAdvancedFilter(ctx, sheet, listRange, criteriaRange, copyTo)
            .Should()
            .Be(2240);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var timings = new double[steps];
        var total = Stopwatch.StartNew();
        var copiedRows = 0;

        for (var step = 0; step < steps; step++)
        {
            var stepWatch = Stopwatch.StartNew();
            copiedRows = ApplyDenseAdvancedFilter(ctx, sheet, listRange, criteriaRange, copyTo);
            stepWatch.Stop();
            timings[step] = stepWatch.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        copiedRows.Should().Be(2240);
        Console.WriteLine(
            "PERF ADVANCED_FILTER_COPY_UNIQUE_DENSE " +
            $"rows={rows} cols={cols} steps={steps} " +
            $"copied_rows={copiedRows} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} " +
            $"p95_ms={timings.OrderBy(x => x).ElementAt((int)Math.Ceiling(steps * 0.95) - 1):F2} " +
            $"max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes}");
    }

    private static (Workbook Wb, Sheet Sheet, ICommandContext Ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    private static void SeedList(Sheet sheet)
    {
        Set(sheet, 1, 1, "Region");
        Set(sheet, 1, 2, "Sales");
        Set(sheet, 1, 3, "Rep");
        Set(sheet, 2, 1, "East");
        Set(sheet, 2, 2, 90);
        Set(sheet, 2, 3, "Ana");
        Set(sheet, 3, 1, "West");
        Set(sheet, 3, 2, 130);
        Set(sheet, 3, 3, "Ben");
        Set(sheet, 4, 1, "East");
        Set(sheet, 4, 2, 120);
        Set(sheet, 4, 3, "Ana");
        Set(sheet, 5, 1, "West");
        Set(sheet, 5, 2, 80);
        Set(sheet, 5, 3, "Cy");
    }

    private static void SeedDenseList(Sheet sheet, uint rows, uint cols)
    {
        var headers = new[]
        {
            "Region",
            "Sales",
            "Rep",
            "Segment",
            "Product",
            "Quarter",
            "Year",
            "Status",
            "Score",
            "Channel",
            "Priority",
            "Notes"
        };

        for (uint col = 1; col <= cols; col++)
            Set(sheet, 1, col, headers[col - 1]);

        for (uint row = 2; row <= rows + 1; row++)
        {
            var item = row - 2;
            Set(sheet, row, 1, item % 3 == 0 ? "East" : item % 3 == 1 ? "West" : "South");
            Set(sheet, row, 2, 50 + item % 250);
            Set(sheet, row, 3, item % 2 == 0 ? "Ana" : "Ben");
            Set(sheet, row, 4, item % 4 == 0 ? "Enterprise" : "Retail");
            Set(sheet, row, 5, "SKU-" + (item % 500).ToString("000"));
            Set(sheet, row, 6, "Q" + (1 + item % 4).ToString());
            Set(sheet, row, 7, 2020 + item % 5);
            Set(sheet, row, 8, item % 6 == 0 ? "Closed" : "Open");
            Set(sheet, row, 9, item % 100);
            Set(sheet, row, 10, item % 2 == 0 ? "Online" : "Field");
            Set(sheet, row, 11, item % 5 == 0 ? "High" : "Normal");
            Set(sheet, row, 12, "Batch-" + (item % 2000).ToString("0000"));
        }
    }

    private static void SeedDenseCriteria(Sheet sheet, uint startRow)
    {
        Set(sheet, startRow, 1, "Region");
        Set(sheet, startRow, 2, "Sales");
        Set(sheet, startRow, 3, "Status");

        Set(sheet, startRow + 1, 1, "East");
        Set(sheet, startRow + 1, 2, ">125");
        Set(sheet, startRow + 1, 3, "Open");

        Set(sheet, startRow + 2, 1, "West");
        Set(sheet, startRow + 2, 2, ">180");

        Set(sheet, startRow + 3, 1, "South");
        Set(sheet, startRow + 3, 2, "<80");
        Set(sheet, startRow + 3, 3, "Open");

        Set(sheet, startRow + 4, 1, "East");
        Set(sheet, startRow + 4, 2, ">210");
    }

    private static int ApplyDenseAdvancedFilter(
        ICommandContext ctx,
        Sheet sheet,
        GridRange listRange,
        GridRange criteriaRange,
        CellAddress copyTo)
    {
        var command = new AdvancedFilterCommand(
            listRange,
            criteriaRange,
            copyTo,
            UniqueRecordsOnly: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetUsedRange().Should().NotBeNull();
        var usedRange = sheet.GetUsedRange()!.Value;
        var copiedRows = 0;
        for (var row = copyTo.Row + 1; row <= usedRange.End.Row; row++)
        {
            if (sheet.GetCell(row, copyTo.Col) is null)
                break;

            copiedRows++;
        }

        return copiedRows;
    }

    private static GridRange ListRange(Sheet sheet, uint r1, uint c1, uint r2, uint c2) => new(Addr(sheet, r1, c1), Addr(sheet, r2, c2));
    private static CellAddress Addr(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);
    private static void Set(Sheet sheet, uint row, uint col, string text) => sheet.SetCell(Addr(sheet, row, col), new TextValue(text));
    private static void Set(Sheet sheet, uint row, uint col, double number) => sheet.SetCell(Addr(sheet, row, col), new NumberValue(number));

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
