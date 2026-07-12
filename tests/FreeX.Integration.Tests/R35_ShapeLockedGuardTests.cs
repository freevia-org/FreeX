using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R35-deferred-shape-locked-1: drawing shapes now carry a per-object <see cref="DrawingShapeModel.Locked"/>
/// flag (default <see langword="true"/>, matching Excel's default-locked shape / OOXML
/// <c>&lt;a:spLocks&gt;</c>), and <see cref="RepositionShapeCommand"/> layers that flag on top of the
/// sheet-level "Edit objects" protection permission check: an author-unlocked shape
/// (<see cref="DrawingShapeModel.Locked"/> == <see langword="false"/>) stays movable on a protected sheet
/// with "Edit objects" blocked, while a (default) locked shape on the same sheet is still rejected --
/// matching Excel's Format Shape &gt; Properties &gt; Locked checkbox behavior.
///
/// Note: reading/writing the OOXML per-shape lock attribute on load/save is deferred follow-up work --
/// this covers the in-memory model + command-guard enforcement only.
/// </summary>
public sealed class R35_ShapeLockedGuardTests
{
    [Fact]
    public void RepositionShapeCommand_UnlockedShapeOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        // "Edit objects" is NOT in ProtectionPermissions -- sheet protection blocks object edits by default.
        var ctx = new TestCommandContext(wb);

        var originalAnchor = new CellAddress(sheet.Id, 2, 2);
        var shape = new DrawingShapeModel
        {
            Anchor = originalAnchor,
            Locked = false // author explicitly unlocked this one shape
        };
        sheet.DrawingShapes.Add(shape);

        var newAnchor = new CellAddress(sheet.Id, 5, 5);
        var outcome = new RepositionShapeCommand(sheet.Id, shape.Id, newAnchor).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked shape must stay movable even while the sheet blocks Edit objects, matching Excel");
        shape.Anchor.Should().Be(newAnchor);
    }

    [Fact]
    public void RepositionShapeCommand_LockedShapeOnProtectedSheet_IsRejected()
    {
        // Sibling no-regression case: a (default-locked) shape on the same kind of protected sheet
        // must still be rejected, exactly like before this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var originalAnchor = new CellAddress(sheet.Id, 2, 2);
        var shape = new DrawingShapeModel
        {
            Anchor = originalAnchor
            // Locked defaults to true.
        };
        sheet.DrawingShapes.Add(shape);

        var newAnchor = new CellAddress(sheet.Id, 5, 5);
        var outcome = new RepositionShapeCommand(sheet.Id, shape.Id, newAnchor).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        shape.Anchor.Should().Be(originalAnchor, "the rejected move must leave the locked shape in place");
    }

    [Fact]
    public void RepositionShapeCommand_LockedShapeOnProtectedSheetWithEditObjectsAllowed_Succeeds()
    {
        // Sibling no-regression case: the existing sheet-level "Edit objects" permission still
        // unconditionally allows moving even a (default) locked shape, unchanged from before this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var ctx = new TestCommandContext(wb);

        var originalAnchor = new CellAddress(sheet.Id, 2, 2);
        var shape = new DrawingShapeModel { Anchor = originalAnchor };
        sheet.DrawingShapes.Add(shape);

        var newAnchor = new CellAddress(sheet.Id, 5, 5);
        var outcome = new RepositionShapeCommand(sheet.Id, shape.Id, newAnchor).Apply(ctx);

        outcome.Success.Should().BeTrue();
        shape.Anchor.Should().Be(newAnchor);
    }

    [Fact]
    public void RepositionShapeCommand_LockedShapeOnUnprotectedSheet_Succeeds()
    {
        // Sibling no-regression case: an unprotected sheet must keep working exactly as before --
        // even a default-locked shape moves freely when the sheet itself isn't protected.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var originalAnchor = new CellAddress(sheet.Id, 2, 2);
        var shape = new DrawingShapeModel { Anchor = originalAnchor };
        sheet.DrawingShapes.Add(shape);

        var newAnchor = new CellAddress(sheet.Id, 5, 5);
        var outcome = new RepositionShapeCommand(sheet.Id, shape.Id, newAnchor).Apply(ctx);

        outcome.Success.Should().BeTrue();
        shape.Anchor.Should().Be(newAnchor);
    }
}
