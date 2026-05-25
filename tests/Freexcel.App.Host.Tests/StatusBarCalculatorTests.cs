using System.IO;
using System.Diagnostics;
using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class StatusBarCalculatorTests
{
    [Theory]
    [InlineData(12.5, "12.5")]
    [InlineData(12.0000000001, "12")]
    [InlineData(123456789.1234, "123456789.1")]
    public void FormatNumber_UsesCompactExcelLikeStatusText(double value, string expected)
    {
        StatusBarCalculator.FormatNumber(value).Should().Be(expected);
    }

    [Fact]
    public void GetReadyStatusText_ReportsValidationInputPromptForActiveCell()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(address, address),
            ShowInputMessage = true,
            PromptTitle = "Input",
            PromptMessage = "Use a number"
        });

        StatusBarCalculator.GetReadyStatusText(sheet, address).Should().Be("Input: Use a number");
    }

    [Fact]
    public void GetReadyStatusText_ReportsReadyWhenActiveCellHasNoError()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");

        StatusBarCalculator.GetReadyStatusText(sheet, new CellAddress(sheet.Id, 1, 1)).Should().Be("Ready");
    }

    [Fact]
    public void Calculate_LargeSelections_ScansSparseCellsWithoutCopyingUsedCellDictionary()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "StatusBarCalculator.cs"));

        source.Should().NotContain(
            "GetUsedCells()",
            "status-bar refreshes happen during navigation and should not allocate a full used-cell dictionary");
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

        var stats = StatusBarCalculator.Calculate(sheet, range);

        stats.Count.Should().Be(2);
        stats.Sum.Should().Be(40);
        stats.Average.Should().Be(20);
        stats.Min.Should().Be(10);
        stats.Max.Should().Be(30);
    }

    [Fact]
    public void Benchmark_RepeatedWholeColumnStatusCalculations()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= 100_000; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row)));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), Cell.FromValue(new NumberValue(row * 2)));
        }

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var cache = new StatusBarStatsCache();
        _ = cache.GetOrCreate(sheet, range, revision: 1, () => StatusBarCalculator.Calculate(sheet, range));

        var sw = Stopwatch.StartNew();
        StatusBarCalculator.Stats stats = null!;
        for (var i = 0; i < 25; i++)
            stats = cache.GetOrCreate(sheet, range, revision: 1, () => StatusBarCalculator.Calculate(sheet, range));
        sw.Stop();

        Console.WriteLine($"Repeated cached whole-column status refreshes: {sw.ElapsedMilliseconds}ms for 25 runs");
        stats.Count.Should().Be(100_000);
        stats.Sum.Should().Be(5_000_050_000d);
    }
}
