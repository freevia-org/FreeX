using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R36-meta-2: mirrors <see cref="R35_ShapeLockedGuardTests"/> for
/// <see cref="ResizeDrawingShapeCommand"/> (and its sibling <see cref="RotateDrawingShapeCommand"/>).
///
/// The r35 fix switched <see cref="RepositionShapeCommand"/> (move) to the shape-aware guard
/// <see cref="DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(Sheet, DrawingShapeModel)"/>, which only
/// blocks when <see cref="DrawingShapeModel.Locked"/> is <see langword="true"/>. But
/// <see cref="ResizeDrawingShapeCommand.Apply"/> (and <see cref="RotateDrawingShapeCommand.Apply"/>) still
/// called the sheet-only overload BEFORE looking up the shape, so resizing/rotating an author-UNLOCKED
/// shape on a protected sheet with "Edit objects" blocked was wrongly rejected. Excel's Locked checkbox
/// gates move, resize, AND rotate uniformly.
/// </summary>
public sealed class R36_ShapeResizeLockedGuardTests
{
    [Fact]
    public void ResizeDrawingShapeCommand_UnlockedShapeOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        // "Edit objects" is NOT in ProtectionPermissions -- sheet protection blocks object edits by default.
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 100,
            Height = 50,
            Locked = false // author explicitly unlocked this one shape
        };
        sheet.DrawingShapes.Add(shape);

        var outcome = new ResizeDrawingShapeCommand(sheet.Id, shape.Id, 200, 80).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked shape must stay resizable even while the sheet blocks Edit objects, matching Excel");
        shape.Width.Should().Be(200);
        shape.Height.Should().Be(80);
    }

    [Fact]
    public void ResizeDrawingShapeCommand_LockedShapeOnProtectedSheet_IsRejected()
    {
        // Sibling no-regression case: a (default-locked) shape on the same kind of protected sheet
        // must still be rejected, exactly like before this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 100,
            Height = 50
            // Locked defaults to true.
        };
        sheet.DrawingShapes.Add(shape);

        var outcome = new ResizeDrawingShapeCommand(sheet.Id, shape.Id, 200, 80).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        shape.Width.Should().Be(100, "the rejected resize must leave the locked shape's size unchanged");
        shape.Height.Should().Be(50);
    }

    [Fact]
    public void ResizeDrawingShapeCommand_LockedShapeOnProtectedSheetWithEditObjectsAllowed_Succeeds()
    {
        // Sibling no-regression case: the existing sheet-level "Edit objects" permission still
        // unconditionally allows resizing even a (default) locked shape, unchanged from before this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 100,
            Height = 50
        };
        sheet.DrawingShapes.Add(shape);

        var outcome = new ResizeDrawingShapeCommand(sheet.Id, shape.Id, 200, 80).Apply(ctx);

        outcome.Success.Should().BeTrue();
        shape.Width.Should().Be(200);
        shape.Height.Should().Be(80);
    }

    [Fact]
    public void ResizeDrawingShapeCommand_LockedShapeOnUnprotectedSheet_Succeeds()
    {
        // Sibling no-regression case: an unprotected sheet must keep working exactly as before --
        // even a default-locked shape resizes freely when the sheet itself isn't protected.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 100,
            Height = 50
        };
        sheet.DrawingShapes.Add(shape);

        var outcome = new ResizeDrawingShapeCommand(sheet.Id, shape.Id, 200, 80).Apply(ctx);

        outcome.Success.Should().BeTrue();
        shape.Width.Should().Be(200);
        shape.Height.Should().Be(80);
    }

    [Fact]
    public void ResizeDrawingShapeCommand_ShapeNotFoundOnProtectedSheet_ReturnsNotFoundWithoutThrowing()
    {
        // Sibling no-regression case: reordering the guard to look up the shape first must not
        // throw an NRE when the shape doesn't exist -- it should return the normal not-found outcome.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var outcome = new ResizeDrawingShapeCommand(sheet.Id, Guid.NewGuid(), 200, 80).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("not");
    }

    [Fact]
    public void RotateDrawingShapeCommand_UnlockedShapeOnProtectedSheet_IsAllowed()
    {
        // Same guard-ordering bug affects RotateDrawingShapeCommand.Apply -- Excel's Locked
        // checkbox gates rotation the same way it gates move and resize.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Locked = false
        };
        sheet.DrawingShapes.Add(shape);

        var outcome = new RotateDrawingShapeCommand(sheet.Id, shape.Id, 45).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked shape must stay rotatable even while the sheet blocks Edit objects, matching Excel");
        shape.RotationDegrees.Should().Be(45);
    }

    [Fact]
    public void RotateDrawingShapeCommand_LockedShapeOnProtectedSheet_IsRejected()
    {
        // Sibling no-regression case for Rotate.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2)
            // Locked defaults to true.
        };
        sheet.DrawingShapes.Add(shape);

        var outcome = new RotateDrawingShapeCommand(sheet.Id, shape.Id, 45).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        shape.RotationDegrees.Should().Be(0, "the rejected rotate must leave the locked shape unchanged");
    }
}
