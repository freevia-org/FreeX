using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R92-consumer-wiring-sweep-2: R91-io-clipboard-image-formats-5-1 wired Ctrl+C/Ctrl+V
/// object-duplicate for Chart/Shape via <see cref="DuplicateDrawingObjectCommand"/>, but left its
/// own two sibling <see cref="SelectionPaneObjectKind"/> members -- Picture and TextBox -- falling
/// to the `default => "Copying this object type is not supported yet."` branch in Apply(), so
/// selecting an inserted picture or text box and pressing Ctrl+C/Ctrl+V silently never duplicated
/// it (MainWindow.ClipboardCommands.TryCopySelectedDrawingObject also never armed the object
/// clipboard for these two kinds in the first place). These tests exercise the real command
/// (DuplicateDrawingObjectCommand.Apply/Revert), not a hand-built model, the same way
/// R91_DuplicateDrawingObjectCommandTests does for Chart/Shape.
/// </summary>
public sealed class R92_DuplicateDrawingObjectCommandPictureTextBoxTests
{
    [Fact]
    public void ApplyPicture_DuplicatesPictureOntoSameSheetAndUndoRemovesIt()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var anchor = new CellAddress(sheet.Id, 2, 2);
        var imageBytes = new byte[] { 1, 2, 3, 4 };
        var insertPicture = new InsertPictureCommand(sheet.Id, anchor, imageBytes, "image/png");
        insertPicture.Apply(ctx).Success.Should().BeTrue();
        var originalPicture = sheet.Pictures[0];

        var command = new DuplicateDrawingObjectCommand(sheet.Id, sheet.Id, SelectionPaneObjectKind.Picture, originalPicture.Id);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue("duplicating a selected Picture must be supported, exactly like Chart/Shape");
        sheet.Pictures.Should().HaveCount(2);
        var duplicate = sheet.Pictures.Single(p => p.Id != originalPicture.Id);
        command.NewObjectId.Should().Be(duplicate.Id);
        duplicate.ContentType.Should().Be(originalPicture.ContentType);
        duplicate.ImageBytes.Should().Equal(originalPicture.ImageBytes);
        duplicate.Width.Should().Be(originalPicture.Width);
        duplicate.Height.Should().Be(originalPicture.Height);
        // Real Excel offsets a same-sheet object paste slightly so it doesn't land exactly on top of
        // the source and look like nothing happened -- matching the Chart/Shape offset behavior.
        duplicate.AnchorOffsetX.Should().Be(originalPicture.AnchorOffsetX + 12);
        duplicate.AnchorOffsetY.Should().Be(originalPicture.AnchorOffsetY + 12);

        command.Revert(ctx);

        sheet.Pictures.Should().ContainSingle();
        sheet.Pictures[0].Id.Should().Be(originalPicture.Id);
    }

    [Fact]
    public void ApplyTextBox_DuplicatesTextBoxOntoSameSheetAndUndoRemovesIt()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var anchor = new CellAddress(sheet.Id, 3, 3);
        var addTextBox = new AddTextBoxCommand(sheet.Id, anchor, "Hello");
        addTextBox.Apply(ctx).Success.Should().BeTrue();
        var originalTextBox = sheet.TextBoxes[0];

        var command = new DuplicateDrawingObjectCommand(sheet.Id, sheet.Id, SelectionPaneObjectKind.TextBox, originalTextBox.Id);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue("duplicating a selected TextBox must be supported, exactly like Chart/Shape");
        sheet.TextBoxes.Should().HaveCount(2);
        var duplicate = sheet.TextBoxes.Single(t => t.Id != originalTextBox.Id);
        command.NewObjectId.Should().Be(duplicate.Id);
        duplicate.Text.Should().Be(originalTextBox.Text);
        duplicate.Width.Should().Be(originalTextBox.Width);
        duplicate.Height.Should().Be(originalTextBox.Height);
        duplicate.AnchorOffsetX.Should().Be(originalTextBox.AnchorOffsetX + 12);
        duplicate.AnchorOffsetY.Should().Be(originalTextBox.AnchorOffsetY + 12);

        command.Revert(ctx);

        sheet.TextBoxes.Should().ContainSingle();
        sheet.TextBoxes[0].Id.Should().Be(originalTextBox.Id);
    }

    /// <summary>No-regression sibling: Chart duplication (the original R91 path) must keep working
    /// unchanged after adding the Picture/TextBox cases to the same switch.</summary>
    [Fact]
    public void ApplyChart_StillDuplicatesChartOntoSameSheetAndUndoRemovesIt()
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
        command.NewObjectId.Should().Be(sheet.Charts.Single(c => c.Id != originalChart.Id).Id);

        command.Revert(ctx);
        sheet.Charts.Should().ContainSingle();
    }

    [Fact]
    public void ApplyPicture_SourcePictureMissing_FailsWithoutThrowing()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var command = new DuplicateDrawingObjectCommand(sheet.Id, sheet.Id, SelectionPaneObjectKind.Picture, Guid.NewGuid());
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.Pictures.Should().BeEmpty();
    }
}
