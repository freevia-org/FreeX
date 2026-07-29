using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R91-io-clipboard-image-formats-5-1: before this fix, Ctrl+C/Ctrl+V on a selected chart or shape
/// never duplicated the object at all -- FreeX.App.Host.MainWindow.ClipboardCommands.ExecuteCopy
/// only ever read SheetGrid.SelectedRange, so it silently copied whatever cell sat under the
/// object's anchor instead. <see cref="DuplicateDrawingObjectCommand"/> is the new command backing
/// the Ctrl+V side of that fix (real product logic, not a hand-built model): it duplicates a chart
/// or shape by Id from a source sheet onto a destination sheet, reusing
/// <see cref="DuplicateSheetDrawingCloner"/>'s existing per-object clone helpers (the same ones
/// Duplicate Sheet already uses) rather than a second hand-rolled property list.
/// </summary>
public sealed class R91_DuplicateDrawingObjectCommandTests
{
    [Fact]
    public void ApplyChart_DuplicatesChartOntoSameSheetAndUndoRemovesIt()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 4));
        var addChart = new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales", left: 10, top: 10);
        addChart.Apply(ctx).Success.Should().BeTrue();
        var originalChart = sheet.Charts[0];

        var command = new DuplicateDrawingObjectCommand(sheet.Id, sheet.Id, SelectionPaneObjectKind.Chart, originalChart.Id);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Charts.Should().HaveCount(2);
        var duplicate = sheet.Charts.Single(c => c.Id != originalChart.Id);
        command.NewObjectId.Should().Be(duplicate.Id);
        duplicate.Type.Should().Be(originalChart.Type);
        duplicate.Title.Should().Be(originalChart.Title);
        duplicate.DataRange.Should().Be(originalChart.DataRange);
        // Real Excel offsets a same-sheet object paste slightly so it doesn't land exactly on top of
        // the source and look like nothing happened.
        duplicate.Left.Should().Be(originalChart.Left + 12);
        duplicate.Top.Should().Be(originalChart.Top + 12);

        command.Revert(ctx);

        sheet.Charts.Should().ContainSingle();
        sheet.Charts[0].Id.Should().Be(originalChart.Id);
    }

    [Fact]
    public void ApplyShape_DuplicatesShapeOntoSameSheetAndUndoRemovesIt()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var anchor = new CellAddress(sheet.Id, 2, 2);
        var addShape = new AddDrawingShapeCommand(sheet.Id, anchor, DrawingShapeKind.Rectangle);
        addShape.Apply(ctx).Success.Should().BeTrue();
        var originalShape = sheet.DrawingShapes[0];

        var command = new DuplicateDrawingObjectCommand(sheet.Id, sheet.Id, SelectionPaneObjectKind.Shape, originalShape.Id);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.DrawingShapes.Should().HaveCount(2);
        var duplicate = sheet.DrawingShapes.Single(s => s.Id != originalShape.Id);
        command.NewObjectId.Should().Be(duplicate.Id);
        duplicate.Kind.Should().Be(originalShape.Kind);
        duplicate.Anchor.Should().Be(originalShape.Anchor);
        duplicate.Width.Should().Be(originalShape.Width);
        duplicate.Height.Should().Be(originalShape.Height);

        command.Revert(ctx);

        sheet.DrawingShapes.Should().ContainSingle();
        sheet.DrawingShapes[0].Id.Should().Be(originalShape.Id);
    }

    [Fact]
    public void ApplyChart_ToDifferentSheet_DuplicatesOntoDestinationSheetOnly()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 3, 4));
        new AddChartCommand(sheet1.Id, range, ChartType.Column, "Sales").Apply(ctx).Success.Should().BeTrue();
        var originalChart = sheet1.Charts[0];

        var command = new DuplicateDrawingObjectCommand(sheet1.Id, sheet2.Id, SelectionPaneObjectKind.Chart, originalChart.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet1.Charts.Should().ContainSingle();
        sheet2.Charts.Should().ContainSingle();
        command.NewObjectId.Should().Be(sheet2.Charts[0].Id);
    }

    [Fact]
    public void Apply_SourceObjectMissing_FailsWithoutThrowing()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var command = new DuplicateDrawingObjectCommand(sheet.Id, sheet.Id, SelectionPaneObjectKind.Chart, Guid.NewGuid());
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.Charts.Should().BeEmpty();
    }
}
