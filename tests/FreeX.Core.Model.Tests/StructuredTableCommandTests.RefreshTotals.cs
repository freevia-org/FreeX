using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class StructuredTableCommandTests
{
    [Fact]
    public void RefreshStructuredTableTotalsCommand_MaterializesLabelsAndCommonFunctionsWithUndo()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        var table = new StructuredTableModel
        {
            Id = 3,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum"),
                new StructuredTableColumnModel(3, "Orders", TotalsRowFunction: "count")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new SimpleCtx(wb);
        var command = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(5, 1).Should().Be(new TextValue("Total"));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(45));
        sheet.GetValue(5, 3).Should().Be(new NumberValue(2));

        command.Revert(ctx);

        sheet.GetValue(5, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(5, 2).Should().Be(BlankValue.Instance);
        sheet.GetValue(5, 3).Should().Be(BlankValue.Instance);
    }

    [BenchmarkFact]
    public void Benchmark_RefreshStructuredTableTotalsWideTable_ReportsTimingAndAllocatedBytes()
    {
        const int rows = 5_000;
        const int valueColumns = 18;
        const int steps = 6;

        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var totalsRow = (uint)rows + 2;

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Label"));
        for (uint col = 2; col <= valueColumns + 1; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new TextValue($"Value {col - 1}"));

        for (uint row = 2; row < totalsRow; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Row {row - 1}"));
            for (uint col = 2; col <= valueColumns + 1; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row + col));
        }

        var table = new StructuredTableModel
        {
            Id = 42,
            Name = "LargeTotals",
            DisplayName = "LargeTotals",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, totalsRow, (uint)valueColumns + 1)),
            TotalsRowShown = true
        };

        table.Columns.Add(new StructuredTableColumnModel(1, "Label", TotalsRowLabel: "Total"));
        var functions = new[] { "sum", "average", "count", "countNums", "min", "max" };
        for (var index = 0; index < valueColumns; index++)
            table.Columns.Add(new StructuredTableColumnModel(index + 2, $"Value {index + 1}", TotalsRowFunction: functions[index % functions.Length]));

        sheet.StructuredTables.Add(table);
        var ctx = new SimpleCtx(wb);
        var warmup = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx);
        warmup.Success.Should().BeTrue();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var timings = new double[steps];
        var total = Stopwatch.StartNew();
        var checksum = 0d;

        for (var i = 0; i < steps; i++)
        {
            var command = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id);
            var step = Stopwatch.StartNew();
            var outcome = command.Apply(ctx);
            step.Stop();

            if (!outcome.Success)
                throw new InvalidOperationException(outcome.ErrorMessage);

            checksum += ((NumberValue)sheet.GetValue(totalsRow, 2)).Value;
            timings[i] = step.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        checksum.Should().BeGreaterThan(0);
        Console.WriteLine(
            "PERF STRUCTURED_TABLE_TOTALS_REFRESH " +
            $"rows={rows} value_columns={valueColumns} steps={steps} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} " +
            $"p95_ms={timings.OrderBy(x => x).ElementAt((int)Math.Ceiling(steps * 0.95) - 1):F2} " +
            $"max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
    }

    [Fact]
    public void RefreshStructuredTableTotalsCommand_RejectsProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        var table = new StructuredTableModel
        {
            Id = 3,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum")
            }
        };
        sheet.StructuredTables.Add(table);
        sheet.IsProtected = true;
        var ctx = new SimpleCtx(wb);

        var outcome = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetValue(5, 1).Should().Be(BlankValue.Instance);
    }
}
