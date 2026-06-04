using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class StructuredTableCommandTests
{
    [Fact]
    public void ApplyStructuredTableFiltersCommand_HidesRowsThatDoNotMatchTableFilterColumns()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["North"]));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(1, ["Open"], IncludeBlank: true));
        sheet.StructuredTables.Add(table);
        sheet.FilterHiddenRows.Add(20u);
        var ctx = new TestCommandContext(wb);
        var command = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain([3u, 4u]);
        sheet.FilterHiddenRows.Should().NotContain([1u, 2u, 5u]);
        sheet.FilterHiddenRows.Should().Contain(20u);

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo([20u]);
    }

    [Fact]
    public void ApplyStructuredTableFiltersCommand_UsesZeroBasedFilterColumnIdsLoadedFromXlsx()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["North"]));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(1, ["Open"], IncludeBlank: true));
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        var outcome = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain([3u, 4u]);
        sheet.FilterHiddenRows.Should().NotContain([1u, 2u, 5u]);
    }

    [Fact]
    public void ApplyStructuredTableFiltersCommand_MatchesSingleValueFiltersCaseInsensitively()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["north"]));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(1, ["OPEN"], IncludeBlank: true));
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        var outcome = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u]);
    }

    [Fact]
    public void ApplyStructuredTableFiltersCommand_ClearsRowsInTableWhenNoFilterColumnsRemain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        sheet.StructuredTables.Add(table);
        sheet.FilterHiddenRows.UnionWith([2u, 3u, 20u]);
        var ctx = new TestCommandContext(wb);

        var outcome = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().NotContain([2u, 3u]);
        sheet.FilterHiddenRows.Should().Contain(20u);
    }

    [Fact]
    public void ApplyStructuredTableFiltersCommand_RejectsUnknownFilterColumnWithoutChangingHiddenRows()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(99, ["North"]));
        sheet.StructuredTables.Add(table);
        sheet.FilterHiddenRows.UnionWith([2u, 20u]);
        var ctx = new TestCommandContext(wb);

        var outcome = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 20u]);
    }

    [BenchmarkFact]
    public void Benchmark_ApplyStructuredTableFiltersDenseRows_ReportsTimingAndAllocatedBytes()
    {
        const int rows = 30_000;
        const int steps = 6;

        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Channel"));

        for (uint row = 2; row <= rows + 1; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(row % 3 == 0 ? "North" : row % 3 == 1 ? "South" : "West"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(row % 4 == 0 ? "Open" : "Closed"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new TextValue(row % 5 == 0 ? "Retail" : "Online"));
        }

        var table = new StructuredTableModel
        {
            Id = 55,
            Name = "DenseFilter",
            DisplayName = "DenseFilter",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, (uint)rows + 1, 3)),
            HasAutoFilter = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Status"),
                new StructuredTableColumnModel(3, "Channel")
            },
            FilterColumns =
            {
                new StructuredTableFilterColumnModel(0, ["North", "West"]),
                new StructuredTableFilterColumnModel(1, ["Open"]),
                new StructuredTableFilterColumnModel(2, ["Online"])
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var warmup = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id).Apply(ctx);
        warmup.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Count.Should().BeGreaterThan(0);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var timings = new double[steps];
        var total = Stopwatch.StartNew();
        var checksum = 0;

        for (var i = 0; i < steps; i++)
        {
            var command = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id);
            var step = Stopwatch.StartNew();
            var outcome = command.Apply(ctx);
            step.Stop();

            if (!outcome.Success)
                throw new InvalidOperationException(outcome.ErrorMessage);

            checksum += sheet.FilterHiddenRows.Count;
            timings[i] = step.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        checksum.Should().BeGreaterThan(0);
        Console.WriteLine(
            "PERF STRUCTURED_TABLE_FILTER_DENSE " +
            $"rows={rows} steps={steps} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} " +
            $"p95_ms={timings.OrderBy(x => x).ElementAt((int)Math.Ceiling(steps * 0.95) - 1):F2} " +
            $"max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
    }
}
