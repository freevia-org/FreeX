using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R16-structural-edit-shift-sweep-1/2/3 + R16-chart-datasource-editing-2: MoveRangeCommand
/// (Cut+Paste move) must relocate address-bearing artifacts that reference the moved cells —
/// a chart's plain DataRange, defined names (NamedRanges), and a cell's sparkline — to the
/// destination, not leave them pointing at the vacated source. Undo must restore each to the
/// original source location.
/// </summary>
public sealed class R16_move_range_Tests
{
    // ── R16-structural-edit-shift-sweep-1: chart.DataRange must move with the range ──────────

    [Fact]
    public void MoveRange_ChartDataRangeFullyInsideMovedRange_MovesToDestination_AndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        var sourceStart = new CellAddress(sheet.Id, 1, 1); // A1
        var sourceEnd = new CellAddress(sheet.Id, 3, 1);   // A3
        var sourceRange = new GridRange(sourceStart, sourceEnd);
        var destination = new CellAddress(sheet.Id, 1, 3); // C1

        sheet.SetCell(sourceStart, Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(sourceEnd, Cell.FromValue(new NumberValue(3)));

        var chart = new ChartModel { DataRange = sourceRange };
        sheet.Charts.Add(chart);

        var command = new MoveRangeCommand(sheet.Id, sourceRange, destination);
        command.Apply(context).Success.Should().BeTrue();

        chart.DataRange.Start.Row.Should().Be(1, "chart DataRange should follow the moved data to column C");
        chart.DataRange.Start.Col.Should().Be(3);
        chart.DataRange.End.Row.Should().Be(3);
        chart.DataRange.End.Col.Should().Be(3);

        command.Revert(context);

        chart.DataRange.Start.Row.Should().Be(1, "undo should restore the chart DataRange to column A");
        chart.DataRange.Start.Col.Should().Be(1);
        chart.DataRange.End.Row.Should().Be(3);
        chart.DataRange.End.Col.Should().Be(1);
    }

    // ── R16-chart-datasource-editing-2: a second, plain (verbatim-less) DataRange chart ─────

    [Fact]
    public void MoveRange_PlainDataRangeChart_HostedOnAnotherSheet_MovesToDestination_AndUndoRestores()
    {
        var workbook = new Workbook("test");
        var dataSheet = workbook.AddSheet("Data");
        var dashboardSheet = workbook.AddSheet("Dashboard");
        var context = new TestCommandContext(workbook);

        var sourceStart = new CellAddress(dataSheet.Id, 1, 1); // Data!A1
        var sourceEnd = new CellAddress(dataSheet.Id, 1, 2);   // Data!B1
        var sourceRange = new GridRange(sourceStart, sourceEnd);
        var destination = new CellAddress(dataSheet.Id, 5, 5); // Data!E5

        dataSheet.SetCell(sourceStart, Cell.FromValue(new NumberValue(10)));
        dataSheet.SetCell(sourceEnd, Cell.FromValue(new NumberValue(20)));

        // Chart hosted on a *different* sheet than the data it plots — a plain DataRange chart
        // with no VerbatimSeriesFormulas, so the formula-rewrite path never touches it.
        var chart = new ChartModel { DataRange = sourceRange };
        dashboardSheet.Charts.Add(chart);

        var command = new MoveRangeCommand(dataSheet.Id, sourceRange, destination);
        command.Apply(context).Success.Should().BeTrue();

        chart.DataRange.Start.Row.Should().Be(5, "cross-sheet chart DataRange should follow the moved data");
        chart.DataRange.Start.Col.Should().Be(5);
        chart.DataRange.End.Row.Should().Be(5);
        chart.DataRange.End.Col.Should().Be(6);
        chart.DataRange.Start.Sheet.Should().Be(dataSheet.Id);

        command.Revert(context);

        chart.DataRange.Start.Row.Should().Be(1, "undo should restore the cross-sheet chart DataRange");
        chart.DataRange.Start.Col.Should().Be(1);
        chart.DataRange.End.Col.Should().Be(2);
    }

    // ── R16-structural-edit-shift-sweep-1: defined names must move with the range ───────────

    [Fact]
    public void MoveRange_NamedRangeFullyInsideMovedRange_RetargetsToDestination_AndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        var sourceStart = new CellAddress(sheet.Id, 2, 2); // B2
        var sourceEnd = new CellAddress(sheet.Id, 4, 2);   // B4
        var sourceRange = new GridRange(sourceStart, sourceEnd);
        var destination = new CellAddress(sheet.Id, 2, 5); // E2

        sheet.SetCell(sourceStart, Cell.FromValue(new NumberValue(1)));
        workbook.DefineNamedRange("MyRange", sourceRange);

        var command = new MoveRangeCommand(sheet.Id, sourceRange, destination);
        command.Apply(context).Success.Should().BeTrue();

        workbook.TryGetNamedRange("MyRange", out var movedRange).Should().BeTrue();
        movedRange.Start.Row.Should().Be(2, "named range should follow the moved cells to column E");
        movedRange.Start.Col.Should().Be(5);
        movedRange.End.Row.Should().Be(4);
        movedRange.End.Col.Should().Be(5);

        command.Revert(context);

        workbook.TryGetNamedRange("MyRange", out var restoredRange).Should().BeTrue();
        restoredRange.Start.Row.Should().Be(2, "undo should restore the named range to column B");
        restoredRange.Start.Col.Should().Be(2);
        restoredRange.End.Row.Should().Be(4);
        restoredRange.End.Col.Should().Be(2);
    }

    [Fact]
    public void MoveRange_ScopedNamedRangeFullyInsideMovedRange_RetargetsToDestination_AndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        var sourceStart = new CellAddress(sheet.Id, 1, 1); // A1
        var sourceEnd = new CellAddress(sheet.Id, 1, 1);   // A1
        var sourceRange = new GridRange(sourceStart, sourceEnd);
        var destination = new CellAddress(sheet.Id, 6, 6); // F6

        sheet.SetCell(sourceStart, Cell.FromValue(new NumberValue(42)));
        workbook.DefineNamedRange("LocalRange", sourceRange, metadata: null, sheet.Id);

        var command = new MoveRangeCommand(sheet.Id, sourceRange, destination);
        command.Apply(context).Success.Should().BeTrue();

        workbook.ScopedNamedRanges.Should().ContainKey(("LocalRange", sheet.Id));
        var moved = workbook.ScopedNamedRanges[("LocalRange", sheet.Id)];
        moved.Start.Row.Should().Be(6, "sheet-scoped named range should follow the move");
        moved.Start.Col.Should().Be(6);

        command.Revert(context);

        workbook.ScopedNamedRanges.Should().ContainKey(("LocalRange", sheet.Id));
        var restored = workbook.ScopedNamedRanges[("LocalRange", sheet.Id)];
        restored.Start.Row.Should().Be(1, "undo should restore the sheet-scoped named range");
        restored.Start.Col.Should().Be(1);
    }

    // ── R16-structural-edit-shift-sweep-3: a moved cell's sparkline must move with it ───────

    [Fact]
    public void MoveRange_CellWithSparkline_SparklineLocationMovesToDestination_AndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        var source = new CellAddress(sheet.Id, 1, 1); // A1 hosts the sparkline
        var destination = new CellAddress(sheet.Id, 4, 4); // D4
        var feedRange = new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 1, 5)); // B1:E1 feeds the sparkline

        sheet.SetCell(source, Cell.FromValue(new NumberValue(1)));
        var sparkline = new SparklineModel
        {
            Location = source,
            DataRange = feedRange,
            Kind = SparklineKind.Column,
            ShowMarkers = true,
        };
        sheet.Sparklines.Add(sparkline);

        var command = new MoveRangeCommand(sheet.Id, new GridRange(source, source), destination);
        command.Apply(context).Success.Should().BeTrue();

        sheet.Sparklines.Should().ContainSingle("the sparkline must move, not duplicate");
        var movedSparkline = sheet.Sparklines[0];
        movedSparkline.Location.Should().Be(destination, "the sparkline must now be hosted at the destination cell");
        movedSparkline.Kind.Should().Be(SparklineKind.Column, "sparkline settings must be preserved across the move");
        movedSparkline.ShowMarkers.Should().BeTrue();
        sheet.Sparklines.Any(s => s.Location == source).Should()
            .BeFalse("the sparkline must not be left behind at the vacated source cell");

        command.Revert(context);

        sheet.Sparklines.Should().ContainSingle("undo must restore exactly one sparkline");
        sheet.Sparklines[0].Location.Should().Be(source, "undo should restore the sparkline to the source cell");
        sheet.Sparklines.Any(s => s.Location == destination).Should()
            .BeFalse("undo must not leave a stray sparkline at the destination");
    }

    [Fact]
    public void MoveRange_CellWithSparkline_MovedOntoExistingDestinationSparkline_ReplacesIt_AndUndoRestoresBoth()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        var source = new CellAddress(sheet.Id, 1, 1); // A1
        var destination = new CellAddress(sheet.Id, 2, 2); // B2 — already hosts a sparkline

        sheet.SetCell(source, Cell.FromValue(new NumberValue(1)));
        var movingSparkline = new SparklineModel { Location = source, Kind = SparklineKind.Line };
        var existingDestinationSparkline = new SparklineModel { Location = destination, Kind = SparklineKind.WinLoss };
        sheet.Sparklines.Add(movingSparkline);
        sheet.Sparklines.Add(existingDestinationSparkline);

        var command = new MoveRangeCommand(sheet.Id, new GridRange(source, source), destination);
        command.Apply(context).Success.Should().BeTrue();

        sheet.Sparklines.Should().ContainSingle("the pre-existing destination sparkline is overwritten by the move");
        sheet.Sparklines[0].Location.Should().Be(destination);
        sheet.Sparklines[0].Kind.Should().Be(SparklineKind.Line, "the moved sparkline's settings should win at the destination");

        command.Revert(context);

        sheet.Sparklines.Should().HaveCount(2, "undo restores both the source and the overwritten destination sparkline");
        sheet.Sparklines.Should().Contain(s => s.Location == source && s.Kind == SparklineKind.Line);
        sheet.Sparklines.Should().Contain(s => s.Location == destination && s.Kind == SparklineKind.WinLoss);
    }
}
