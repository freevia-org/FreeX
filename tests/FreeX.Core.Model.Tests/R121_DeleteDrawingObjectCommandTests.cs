using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R121 (round 111 backlog): FreeX had no way to delete a picture/text box/shape/chart -- every
/// existing <c>sheet.Pictures.Remove(...)</c>-style call site was the Revert of a same-session
/// Insert/Paste/Duplicate, never a user-facing delete of an EXISTING object.
/// <see cref="DeleteDrawingObjectCommand"/> is the new IWorkbookCommand that fills that gap, with undo
/// and the same per-object Locked + sheet-level "Edit objects" protection guard rounds 111/112/113
/// already added for move/resize/rotate/format.
/// </summary>
public sealed class R121_DeleteDrawingObjectCommandTests
{
    [Fact]
    public void DeletePicture_RemovesFromSheet_UndoRestoresSameInstance()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1],
            ContentType = "image/png",
            Name = "Picture 1"
        };
        sheet.Pictures.Add(picture);

        var command = new DeleteDrawingObjectCommand(sheet.Id, SelectionPaneObjectKind.Picture, picture.Id);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Pictures.Should().BeEmpty();

        command.Revert(ctx);

        sheet.Pictures.Should().ContainSingle().Which.Should().BeSameAs(picture);
    }

    [Fact]
    public void DeleteTextBox_RemovesFromSheet_UndoRestoresSameInstance()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Text = "Hello",
            Name = "TextBox 1"
        };
        sheet.TextBoxes.Add(textBox);

        var command = new DeleteDrawingObjectCommand(sheet.Id, SelectionPaneObjectKind.TextBox, textBox.Id);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.TextBoxes.Should().BeEmpty();

        command.Revert(ctx);
        sheet.TextBoxes.Should().ContainSingle().Which.Should().BeSameAs(textBox);
    }

    [Fact]
    public void DeleteShape_RemovesFromSheet_UndoRestoresSameInstance()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 3),
            Kind = DrawingShapeKind.Rectangle,
            Name = "Shape 1"
        };
        sheet.DrawingShapes.Add(shape);

        var command = new DeleteDrawingObjectCommand(sheet.Id, SelectionPaneObjectKind.Shape, shape.Id);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.DrawingShapes.Should().BeEmpty();

        command.Revert(ctx);
        sheet.DrawingShapes.Should().ContainSingle().Which.Should().BeSameAs(shape);
    }

    [Fact]
    public void DeleteChart_RemovesFromSheet_UndoRestoresSameInstance()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 0, 0), new CellAddress(sheet.Id, 1, 1)),
            Name = "Chart 1"
        };
        sheet.Charts.Add(chart);

        var command = new DeleteDrawingObjectCommand(sheet.Id, SelectionPaneObjectKind.Chart, chart.Id);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.Charts.Should().BeEmpty();

        command.Revert(ctx);
        sheet.Charts.Should().ContainSingle().Which.Should().BeSameAs(chart);
    }

    [Fact]
    public void DeletePicture_MissingObject_ReturnsFailureAndDoesNotThrow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var outcome = new DeleteDrawingObjectCommand(sheet.Id, SelectionPaneObjectKind.Picture, Guid.NewGuid()).Apply(ctx);

        outcome.Success.Should().BeFalse();
    }

    [Fact]
    public void DeletePicture_TombstonesNameOnApply_ClearsOnRevert()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1],
            ContentType = "image/png",
            Name = "Picture 1",
            IsSourceLoaded = true
        };
        sheet.Pictures.Add(picture);

        var command = new DeleteDrawingObjectCommand(sheet.Id, SelectionPaneObjectKind.Picture, picture.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.DeletedSourceDrawingObjectNames.Should().ContainSingle().Which.Should().Be("Picture 1");

        command.Revert(ctx);

        sheet.DeletedSourceDrawingObjectNames.Should().BeEmpty();
    }

    // --- Protection guard coverage (rounds 111/112/113 established this pattern for every other
    // drawing-object command; DeleteDrawingObjectCommand must honour it too) ---

    [Fact]
    public void DeletePicture_RejectsProtectedSheetWithoutEditObjectsPermission_WhenPictureLocked()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1],
            ContentType = "image/png",
            Locked = true
        };
        sheet.Pictures.Add(picture);

        var outcome = new DeleteDrawingObjectCommand(sheet.Id, SelectionPaneObjectKind.Picture, picture.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.Pictures.Should().ContainSingle("a locked picture must not be deleted while the sheet blocks Edit Objects");
    }

    [Fact]
    public void DeletePicture_AllowsProtectedSheetWithEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var ctx = new TestCommandContext(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1],
            ContentType = "image/png",
            Locked = true
        };
        sheet.Pictures.Add(picture);

        var outcome = new DeleteDrawingObjectCommand(sheet.Id, SelectionPaneObjectKind.Picture, picture.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Pictures.Should().BeEmpty();
    }

    [Fact]
    public void DeletePicture_AllowsProtectedSheetWhenPictureIsUnlocked()
    {
        // R111-model-drawing-object-lock-1-1 pattern: an author-unlocked object stays deletable even
        // while the sheet blocks "Edit objects", matching Excel's Format Object > Properties > Locked
        // checkbox.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1],
            ContentType = "image/png",
            Locked = false
        };
        sheet.Pictures.Add(picture);

        var outcome = new DeleteDrawingObjectCommand(sheet.Id, SelectionPaneObjectKind.Picture, picture.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Pictures.Should().BeEmpty();
    }

    [Fact]
    public void DeleteTextBox_RejectsProtectedSheetWithoutEditObjectsPermission_WhenLocked()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "Hello",
            Locked = true
        };
        sheet.TextBoxes.Add(textBox);

        var outcome = new DeleteDrawingObjectCommand(sheet.Id, SelectionPaneObjectKind.TextBox, textBox.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.TextBoxes.Should().ContainSingle();
    }

    [Fact]
    public void DeleteShape_RejectsProtectedSheetWithoutEditObjectsPermission_WhenLocked()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            Locked = true
        };
        sheet.DrawingShapes.Add(shape);

        var outcome = new DeleteDrawingObjectCommand(sheet.Id, SelectionPaneObjectKind.Shape, shape.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.DrawingShapes.Should().ContainSingle();
    }

    [Fact]
    public void DeleteChart_RejectsProtectedSheetWithoutEditObjectsPermission_WhenLocked()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 0, 0), new CellAddress(sheet.Id, 1, 1)),
            Locked = true
        };
        sheet.Charts.Add(chart);

        var outcome = new DeleteDrawingObjectCommand(sheet.Id, SelectionPaneObjectKind.Chart, chart.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.Charts.Should().ContainSingle();
    }
}
