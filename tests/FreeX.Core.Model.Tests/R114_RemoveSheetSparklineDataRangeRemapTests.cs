using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R114: RemoveSheetCommand.Apply already remaps ChartModel.DataRange and PivotTableModel
/// .SourceRange (see R102_RemoveSheetPivotSourceRangeRemapTests) when the sheet they name is
/// deleted, but it never touched Sheet.Sparklines[*].DataRange / DateAxisRange -- GridRange fields
/// that, like ChartModel.DataRange, can point at a sheet different from the one hosting the
/// sparkline (Excel's Sparkline "Edit Data" dialog allows a cross-sheet source range; see
/// XlsxSparklineMapper.cs). Left unfixed, a surviving SparklineModel keeps a DataRange whose
/// Start/End.Sheet is a nonexistent SheetId once its data-source sheet is deleted --
/// XlsxSparklineMapper.Save's validSparklines filter requires ResolveSheetName(...) to resolve
/// that sheet, so the ENTIRE sparkline (not just the dangling reference) is silently dropped from
/// the next save. Real Excel instead keeps the sparkline in place with a stale/broken reference.
/// </summary>
public sealed class R114_RemoveSheetSparklineDataRangeRemapTests
{
    [Fact]
    public void R114_RemoveSheet_RemapsSparklineDataRangeOntoHostSheet()
    {
        // 'Data' feeds the sparkline's data range; the sparkline itself is hosted (anchored) on
        // 'Report'. Deleting 'Data' must NOT leave SparklineModel.DataRange pointing at the
        // now-nonexistent 'Data' SheetId -- it must be remapped onto the sparkline's own host
        // sheet, exactly like a chart's cross-sheet DataRange is remapped in the same command.
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(
            new CellAddress(data.Id, 1, 1),
            new CellAddress(data.Id, 1, 5));
        var sparkline = new SparklineModel
        {
            Kind = SparklineKind.Line,
            DataRange = dataRange,
            Location = new CellAddress(report.Id, 2, 1),
        };
        report.Sparklines.Add(sparkline);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        sparkline.DataRange.Start.Sheet.Should().Be(report.Id,
            because: "a surviving sparkline must not keep a DataRange naming a sheet id that no " +
                     "longer exists in the workbook -- it must be remapped onto its own host " +
                     "sheet, mirroring the chart DataRange remap in the same command");
        sparkline.DataRange.End.Sheet.Should().Be(report.Id);

        // Confirms this actually reaches the real bug: the workbook must still be able to resolve
        // the sparkline's DataRange.Start.Sheet -- this is exactly the lookup
        // XlsxSparklineMapper.Save's validSparklines filter (via ResolveSheetName) performs to
        // decide whether the sparkline survives the next XLSX save at all.
        wb.GetSheet(sparkline.DataRange.Start.Sheet).Should().NotBeNull();

        // The sparkline itself must still be present -- this is the "entire sparkline dropped",
        // not merely "reference dangling", failure mode the finding calls out.
        report.Sparklines.Should().ContainSingle();
    }

    [Fact]
    public void R114_RemoveSheet_RemapsSparklineDateAxisRangeOntoHostSheet()
    {
        // The group-level DateAxisRange is the same shape of field as DataRange and can also live
        // on a different sheet than the sparkline's host.
        var wb = new Workbook("test");
        var dates = wb.AddSheet("Dates");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);

        var dateAxisRange = new GridRange(
            new CellAddress(dates.Id, 1, 1),
            new CellAddress(dates.Id, 1, 5));
        var sparkline = new SparklineModel
        {
            Kind = SparklineKind.Line,
            DataRange = new GridRange(
                new CellAddress(report.Id, 3, 1),
                new CellAddress(report.Id, 3, 5)),
            Location = new CellAddress(report.Id, 4, 1),
            DateAxisRange = dateAxisRange,
        };
        report.Sparklines.Add(sparkline);

        var command = new RemoveSheetCommand(dates.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        sparkline.DateAxisRange.Should().NotBeNull();
        sparkline.DateAxisRange!.Value.Start.Sheet.Should().Be(report.Id,
            because: "a surviving sparkline's group-level DateAxisRange must not keep naming a " +
                     "deleted sheet either, mirroring the DataRange remap immediately above");
        wb.GetSheet(sparkline.DateAxisRange!.Value.Start.Sheet).Should().NotBeNull();
    }

    [Fact]
    public void R114_RemoveSheetRevert_RestoresSparklineDataRangeAndDateAxisRange()
    {
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(
            new CellAddress(data.Id, 1, 1),
            new CellAddress(data.Id, 1, 5));
        var dateAxisRange = new GridRange(
            new CellAddress(data.Id, 2, 1),
            new CellAddress(data.Id, 2, 5));
        var sparkline = new SparklineModel
        {
            Kind = SparklineKind.Line,
            DataRange = dataRange,
            Location = new CellAddress(report.Id, 2, 1),
            DateAxisRange = dateAxisRange,
        };
        report.Sparklines.Add(sparkline);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx);
        command.Revert(ctx);

        sparkline.DataRange.Should().Be(dataRange,
            because: "undoing the sheet delete must restore the sparkline's original cross-sheet " +
                     "DataRange exactly, mirroring the chart DataRange undo restore in the same " +
                     "command");
        sparkline.DateAxisRange.Should().Be(dateAxisRange,
            because: "undoing the sheet delete must also restore the sparkline's original " +
                     "DateAxisRange exactly");
    }

    [Fact]
    public void R114_RemoveSheet_LeavesUnrelatedSparklineDataRangeUntouched()
    {
        // No-regression sibling: a sparkline whose DataRange has nothing to do with the deleted
        // sheet (same-sheet source, on a sheet that isn't being deleted at all) must be left
        // completely alone by the new remap pass.
        var wb = new Workbook("test");
        var scratch = wb.AddSheet("Scratch");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(
            new CellAddress(report.Id, 1, 1),
            new CellAddress(report.Id, 1, 5));
        var sparkline = new SparklineModel
        {
            Kind = SparklineKind.Line,
            DataRange = dataRange,
            Location = new CellAddress(report.Id, 2, 1),
        };
        report.Sparklines.Add(sparkline);

        var command = new RemoveSheetCommand(scratch.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        sparkline.DataRange.Should().Be(dataRange,
            because: "a sparkline's own-sheet DataRange must not be touched when an unrelated " +
                     "sheet is deleted");
    }
}
