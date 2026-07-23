using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R80-meta-2: DuplicateSheetDrawingCloner's CloneDrawingShape object initializer omitted
/// DrawingShapeModel.Locked, so an author's explicit unlock of a shape (Locked = false, overriding
/// the true default so the shape stays movable even when the sheet is later protected with
/// 'Edit objects' blocked) was silently discarded by Duplicate Sheet -- the copy fell back to the
/// model default of Locked = true, contradicting the source shape. Verifies the explicit unlock now
/// survives Duplicate Sheet, plus a sibling no-regression case confirming a plain (default-locked)
/// shape still duplicates cleanly.
/// </summary>
public sealed class R80_DuplicateSheetDrawingClonerLockedFieldTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    // R80-meta-2 (the bug case): a shape the author explicitly unlocked must stay unlocked on the
    // Duplicate Sheet copy, not silently revert to the model's Locked = true default.
    [Fact]
    public void DuplicateSheet_ShapeExplicitlyUnlocked_PreservesUnlockedOnCopy()
    {
        var workbook = new Workbook("ShapeCloneLockedFalse");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "UnlockedRect",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            Width = 100,
            Height = 60,
            Locked = false
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedShape = workbook.Sheets[1].DrawingShapes.Should().ContainSingle().Subject;
        copiedShape.Locked.Should().BeFalse(
            "an author's explicit unlock must not be dropped by Duplicate Sheet");
    }

    // Sibling no-regression case: a plain shape left at the default Locked = true must still
    // duplicate cleanly, keeping the copy locked.
    [Fact]
    public void DuplicateSheet_ShapeWithDefaultLocked_LeavesFieldAtDefault()
    {
        var workbook = new Workbook("ShapeCloneLockedDefault");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "PlainRect",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            Width = 100,
            Height = 60
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedShape = workbook.Sheets[1].DrawingShapes.Should().ContainSingle().Subject;
        copiedShape.Locked.Should().BeTrue();
    }
}
