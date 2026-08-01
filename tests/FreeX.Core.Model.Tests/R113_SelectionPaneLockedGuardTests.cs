using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R113-model-drawing-object-lock-1-1: R111/R112 layered the per-object <c>Locked</c> override on top
/// of the sheet-level "Edit objects" protection guard for the Chart/Picture/DrawingShape/TextBox
/// command families -- see <see cref="R111_DrawingObjectLockedGuardTests"/> and
/// <see cref="R112_DrawingObjectLockedGuardSiblingTests"/>. Four commands were left behind because
/// they route through <c>SelectionPaneObjectAccess</c>, whose <c>SelectionPaneObjectRef</c> had no way
/// to reach the underlying model's <c>Locked</c> flag: SetSelectionPaneObjectVisibilityCommand,
/// MoveSelectionPaneObjectCommand, RenameSelectionPaneObjectCommand and
/// SetDrawingObjectRotationCommand. Each of them operates on ONE already-resolved object, so an
/// author-unlocked object was incorrectly rejected for show/hide, z-order, rename and rotate while
/// its sheet blocked "Edit objects" -- Excel's Format Object &gt; Properties &gt; Locked checkbox
/// governs all manipulations of an object.
///
/// Each command gets an "unlocked object on a protected sheet is allowed" case plus a "locked
/// (default) object on the same protected sheet is still rejected" no-regression case, mirroring the
/// R111/R112 pattern.
/// </summary>
public sealed class R113_SelectionPaneLockedGuardTests
{
    private static GridRange ChartRange(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));

    // ── SetSelectionPaneObjectVisibilityCommand ──────────────────────────

    [Fact]
    public void SetSelectionPaneObjectVisibilityCommand_UnlockedShapeOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            IsVisible = true,
            Locked = false
        };
        sheet.DrawingShapes.Add(shape);

        var outcome = new SetSelectionPaneObjectVisibilityCommand(
            sheet.Id,
            SelectionPaneObjectKind.Shape,
            shape.Id,
            isVisible: false).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked shape must stay hideable from the selection pane even while the sheet blocks Edit objects, matching Excel");
        shape.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void SetSelectionPaneObjectVisibilityCommand_LockedShapeOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            IsVisible = true
            // Locked defaults to true.
        };
        sheet.DrawingShapes.Add(shape);

        var outcome = new SetSelectionPaneObjectVisibilityCommand(
            sheet.Id,
            SelectionPaneObjectKind.Shape,
            shape.Id,
            isVisible: false).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        shape.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void SetSelectionPaneObjectVisibilityCommand_UnlockedPictureOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            IsVisible = true,
            Locked = false
        };
        sheet.Pictures.Add(picture);

        var outcome = new SetSelectionPaneObjectVisibilityCommand(
            sheet.Id,
            SelectionPaneObjectKind.Picture,
            picture.Id,
            isVisible: false).Apply(ctx);

        outcome.Success.Should().BeTrue();
        picture.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void SetSelectionPaneObjectVisibilityCommand_UnlockedTextBoxOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            IsVisible = true,
            Locked = false
        };
        sheet.TextBoxes.Add(textBox);

        var outcome = new SetSelectionPaneObjectVisibilityCommand(
            sheet.Id,
            SelectionPaneObjectKind.TextBox,
            textBox.Id,
            isVisible: false).Apply(ctx);

        outcome.Success.Should().BeTrue();
        textBox.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void SetSelectionPaneObjectVisibilityCommand_UnlockedChartOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = ChartRange(sheet),
            IsVisible = true,
            Locked = false
        };
        sheet.Charts.Add(chart);

        var outcome = new SetSelectionPaneObjectVisibilityCommand(
            sheet.Id,
            SelectionPaneObjectKind.Chart,
            chart.Id,
            isVisible: false).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void SetSelectionPaneObjectVisibilityCommand_LockedChartOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = ChartRange(sheet),
            IsVisible = true
            // Locked defaults to true.
        };
        sheet.Charts.Add(chart);

        var outcome = new SetSelectionPaneObjectVisibilityCommand(
            sheet.Id,
            SelectionPaneObjectKind.Chart,
            chart.Id,
            isVisible: false).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        chart.IsVisible.Should().BeTrue();
    }

    // ── MoveSelectionPaneObjectCommand ───────────────────────────────────

    [Fact]
    public void MoveSelectionPaneObjectCommand_UnlockedPictureOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var back = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        var front = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 2) };
        sheet.Pictures.Add(back);
        sheet.Pictures.Add(front);
        sheet.IsProtected = true;

        var outcome = new MoveSelectionPaneObjectCommand(
            sheet.Id,
            SelectionPaneObjectKind.Picture,
            back.Id,
            forward: true).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked picture must stay re-orderable from the selection pane even while the sheet blocks Edit objects, matching Excel");
        sheet.DrawingObjectZOrder.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, front.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, back.Id));
    }

    [Fact]
    public void MoveSelectionPaneObjectCommand_LockedPictureOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Locked defaults to true on both pictures.
        var back = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var front = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 2) };
        sheet.Pictures.Add(back);
        sheet.Pictures.Add(front);
        sheet.IsProtected = true;

        var outcome = new MoveSelectionPaneObjectCommand(
            sheet.Id,
            SelectionPaneObjectKind.Picture,
            back.Id,
            forward: true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.DrawingObjectZOrder.Should().BeEmpty();
    }

    [Fact]
    public void MoveSelectionPaneObjectCommand_UnlockedShapeOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var back = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        var front = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 2) };
        sheet.DrawingShapes.Add(back);
        sheet.TextBoxes.Add(front);
        sheet.IsProtected = true;

        var outcome = new MoveSelectionPaneObjectCommand(
            sheet.Id,
            SelectionPaneObjectKind.Shape,
            back.Id,
            forward: true).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.DrawingObjectZOrder.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, front.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, back.Id));
    }

    [Fact]
    public void MoveSelectionPaneObjectCommand_UnlockedChartOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = ChartRange(sheet),
            Locked = false
        };
        // Normalization orders shapes, then pictures, then text boxes, then charts -- so the chart
        // starts on top and must be sent backward to actually move.
        var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 2) };
        sheet.Charts.Add(chart);
        sheet.Pictures.Add(picture);
        sheet.IsProtected = true;

        var outcome = new MoveSelectionPaneObjectCommand(
            sheet.Id,
            SelectionPaneObjectKind.Chart,
            chart.Id,
            forward: false).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.DrawingObjectZOrder.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Chart, chart.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id));
    }

    // ── RenameSelectionPaneObjectCommand ─────────────────────────────────

    [Fact]
    public void RenameSelectionPaneObjectCommand_UnlockedShapeOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Name = "Shape 1",
            Locked = false
        };
        sheet.DrawingShapes.Add(shape);

        var outcome = new RenameSelectionPaneObjectCommand(
            sheet.Id,
            SelectionPaneObjectKind.Shape,
            shape.Id,
            "Renamed").Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked shape must stay renameable from the selection pane even while the sheet blocks Edit objects, matching Excel");
        shape.Name.Should().Be("Renamed");
    }

    [Fact]
    public void RenameSelectionPaneObjectCommand_LockedShapeOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Name = "Shape 1"
            // Locked defaults to true.
        };
        sheet.DrawingShapes.Add(shape);

        var outcome = new RenameSelectionPaneObjectCommand(
            sheet.Id,
            SelectionPaneObjectKind.Shape,
            shape.Id,
            "Renamed").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        shape.Name.Should().Be("Shape 1");
    }

    [Fact]
    public void RenameSelectionPaneObjectCommand_UnlockedChartOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = ChartRange(sheet),
            Name = "Chart 1",
            Locked = false
        };
        sheet.Charts.Add(chart);

        var outcome = new RenameSelectionPaneObjectCommand(
            sheet.Id,
            SelectionPaneObjectKind.Chart,
            chart.Id,
            "Renamed").Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.Name.Should().Be("Renamed");
    }

    [Fact]
    public void RenameSelectionPaneObjectCommand_LockedChartOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = ChartRange(sheet),
            Name = "Chart 1"
            // Locked defaults to true.
        };
        sheet.Charts.Add(chart);

        var outcome = new RenameSelectionPaneObjectCommand(
            sheet.Id,
            SelectionPaneObjectKind.Chart,
            chart.Id,
            "Renamed").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        chart.Name.Should().Be("Chart 1");
    }

    // ── SetDrawingObjectRotationCommand ──────────────────────────────────

    [Fact]
    public void SetDrawingObjectRotationCommand_UnlockedPictureOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        sheet.Pictures.Add(picture);

        var outcome = new SetDrawingObjectRotationCommand(
            sheet.Id,
            SelectionPaneObjectKind.Picture,
            picture.Id,
            45).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked picture must stay rotatable on-canvas even while the sheet blocks Edit objects, matching Excel");
        picture.RotationDegrees.Should().Be(45);
    }

    [Fact]
    public void SetDrawingObjectRotationCommand_LockedPictureOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1) }; // Locked defaults to true
        sheet.Pictures.Add(picture);

        var outcome = new SetDrawingObjectRotationCommand(
            sheet.Id,
            SelectionPaneObjectKind.Picture,
            picture.Id,
            45).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        picture.RotationDegrees.Should().Be(0);
    }

    [Fact]
    public void SetDrawingObjectRotationCommand_UnlockedShapeOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        sheet.DrawingShapes.Add(shape);

        var outcome = new SetDrawingObjectRotationCommand(
            sheet.Id,
            SelectionPaneObjectKind.Shape,
            shape.Id,
            45).Apply(ctx);

        outcome.Success.Should().BeTrue();
        shape.RotationDegrees.Should().Be(45);
    }

    [Fact]
    public void SetDrawingObjectRotationCommand_LockedShapeOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) }; // Locked defaults to true
        sheet.DrawingShapes.Add(shape);

        var outcome = new SetDrawingObjectRotationCommand(
            sheet.Id,
            SelectionPaneObjectKind.Shape,
            shape.Id,
            45).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        shape.RotationDegrees.Should().Be(0);
    }

    [Fact]
    public void SetDrawingObjectRotationCommand_UnlockedTextBoxOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        sheet.TextBoxes.Add(textBox);

        var outcome = new SetDrawingObjectRotationCommand(
            sheet.Id,
            SelectionPaneObjectKind.TextBox,
            textBox.Id,
            45).Apply(ctx);

        outcome.Success.Should().BeTrue();
        textBox.RotationDegrees.Should().Be(45);
    }

    [Fact]
    public void SetDrawingObjectRotationCommand_LockedTextBoxOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1) }; // Locked defaults to true
        sheet.TextBoxes.Add(textBox);

        var outcome = new SetDrawingObjectRotationCommand(
            sheet.Id,
            SelectionPaneObjectKind.TextBox,
            textBox.Id,
            45).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        textBox.RotationDegrees.Should().Be(0);
    }
}
