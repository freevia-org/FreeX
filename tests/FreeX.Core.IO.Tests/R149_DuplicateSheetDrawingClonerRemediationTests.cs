using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R149-remediation (audit gap found in the r149 fix wave's own scope review): the r149 fix taught
/// <c>DuplicateSheetDrawingCloner.ClonePicture</c> to carry <see cref="PictureModel.IsDecorative"/>
/// forward, but its two siblings -- <see cref="DuplicateSheetDrawingCloner.CloneDrawingShape"/> and
/// <see cref="DuplicateSheetDrawingCloner.CloneTextBox"/> -- kept dropping the analogous
/// <see cref="DrawingShapeModel.IsDecorative"/>/<see cref="TextBoxModel.IsDecorative"/> flags, so
/// Duplicate Sheet re-flagged a duplicated decorative shape/text box as missing alt text -- the
/// exact defect the r149 fix was written to remove, just on the two object kinds it didn't touch.
/// <para>
/// Auditing the same three cloners for the identical omission pattern (a per-object field added by
/// an earlier round, carried by one cloner but not its siblings) turned up three more: r111 added
/// <c>Locked</c> to <see cref="ChartModel"/>/<see cref="PictureModel"/>/<see cref="TextBoxModel"/>/
/// <see cref="DrawingShapeModel"/> alike (mirrored by <c>R80_DuplicateSheetDrawingClonerLockedFieldTests</c>
/// for the shape case only), but only <c>CloneDrawingShape</c> ever copied it -- <c>CloneChart</c>,
/// <c>ClonePicture</c>, and <c>CloneTextBox</c> all silently discarded it. And <c>CloneTextBox</c>
/// separately omitted <see cref="TextBoxModel.OutlineHasNoFill"/>, which <c>CloneDrawingShape</c>
/// already carries for the shape's own <c>OutlineHasNoFill</c>.
/// </para>
/// </summary>
public sealed class R149_DuplicateSheetDrawingClonerRemediationTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static Sheet CreateChartSheet(Workbook workbook, out GridRange range)
    {
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        return sheet;
    }

    // ---------------------------------------------------------------- IsDecorative (the named gap)

    // The bug case: a shape explicitly marked decorative must stay decorative on the Duplicate Sheet
    // copy, mirroring R91_DuplicateSheetDrawingClonerDecorativePictureTests for pictures.
    [Fact]
    public void DuplicateSheet_DecorativeShape_PreservesIsDecorativeOnCopy()
    {
        var workbook = new Workbook("ShapeCloneDecorative");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "DecorativeRect",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            Width = 100,
            Height = 60,
            IsDecorative = true
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedShape = workbook.Sheets[1].DrawingShapes.Should().ContainSingle().Subject;
        copiedShape.IsDecorative.Should().BeTrue(
            "the 'Mark as decorative' flag must not be dropped by Duplicate Sheet");
    }

    // Sibling no-regression case: a plain (non-decorative) shape must still duplicate cleanly.
    [Fact]
    public void DuplicateSheet_NonDecorativeShape_LeavesIsDecorativeAtDefault()
    {
        var workbook = new Workbook("ShapeCloneNonDecorative");
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

        workbook.Sheets[1].DrawingShapes.Should().ContainSingle().Subject.IsDecorative.Should().BeFalse();
    }

    // The bug case: a text box explicitly marked decorative must stay decorative on the copy.
    [Fact]
    public void DuplicateSheet_DecorativeTextBox_PreservesIsDecorativeOnCopy()
    {
        var workbook = new Workbook("TextBoxCloneDecorative");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Name = "DecorativeTextBox",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 100,
            Height = 60,
            IsDecorative = true
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedTextBox = workbook.Sheets[1].TextBoxes.Should().ContainSingle().Subject;
        copiedTextBox.IsDecorative.Should().BeTrue(
            "the 'Mark as decorative' flag must not be dropped by Duplicate Sheet");
    }

    // Sibling no-regression case: a plain (non-decorative) text box must still duplicate cleanly.
    [Fact]
    public void DuplicateSheet_NonDecorativeTextBox_LeavesIsDecorativeAtDefault()
    {
        var workbook = new Workbook("TextBoxCloneNonDecorative");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Name = "PlainTextBox",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 100,
            Height = 60
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        workbook.Sheets[1].TextBoxes.Should().ContainSingle().Subject.IsDecorative.Should().BeFalse();
    }

    // ---------------------------------------------------------------- Locked / OutlineHasNoFill
    // (found while enumerating the three cloners' other fields, same omission pattern)

    // The bug case: an explicitly-unlocked text box (mirrors R80's shape case) and one with its line
    // explicitly suppressed must keep both on the Duplicate Sheet copy.
    [Fact]
    public void DuplicateSheet_TextBoxExplicitlyUnlockedAndNoLine_PreservesBothOnCopy()
    {
        var workbook = new Workbook("TextBoxCloneLockedAndNoFill");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Name = "UnlockedNoLineTextBox",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 100,
            Height = 60,
            Locked = false,
            OutlineHasNoFill = true
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedTextBox = workbook.Sheets[1].TextBoxes.Should().ContainSingle().Subject;
        copiedTextBox.Locked.Should().BeFalse(
            "an author's explicit unlock must not be dropped by Duplicate Sheet");
        copiedTextBox.OutlineHasNoFill.Should().BeTrue(
            "an explicit 'No Line' must not be dropped by Duplicate Sheet");
    }

    // Sibling no-regression case: a plain text box left at the defaults must still duplicate cleanly.
    [Fact]
    public void DuplicateSheet_TextBoxWithDefaultLockedAndFill_LeavesFieldsAtDefault()
    {
        var workbook = new Workbook("TextBoxCloneLockedDefault");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Name = "PlainTextBox",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 100,
            Height = 60
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedTextBox = workbook.Sheets[1].TextBoxes.Should().ContainSingle().Subject;
        copiedTextBox.Locked.Should().BeTrue();
        copiedTextBox.OutlineHasNoFill.Should().BeFalse();
    }

    // The bug case: an explicitly-unlocked picture must stay unlocked on the copy.
    [Fact]
    public void DuplicateSheet_PictureExplicitlyUnlocked_PreservesUnlockedOnCopy()
    {
        var workbook = new Workbook("PictureCloneLockedFalse");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "UnlockedPicture",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
            Width = 100,
            Height = 20,
            Locked = false
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedPicture = workbook.Sheets[1].Pictures.Should().ContainSingle().Subject;
        copiedPicture.Locked.Should().BeFalse(
            "an author's explicit unlock must not be dropped by Duplicate Sheet");
    }

    // Sibling no-regression case: a plain (default-locked) picture must still duplicate cleanly.
    [Fact]
    public void DuplicateSheet_PictureWithDefaultLocked_LeavesFieldAtDefault()
    {
        var workbook = new Workbook("PictureCloneLockedDefault");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "PlainPicture",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [4, 5, 6],
            ContentType = "image/png",
            Width = 100,
            Height = 20
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        workbook.Sheets[1].Pictures.Should().ContainSingle().Subject.Locked.Should().BeTrue();
    }

    // The bug case: an explicitly-unlocked chart must stay unlocked on the copy.
    [Fact]
    public void DuplicateSheet_ChartExplicitlyUnlocked_PreservesUnlockedOnCopy()
    {
        var workbook = new Workbook("ChartCloneLockedFalse");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            Locked = false
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.Locked.Should().BeFalse(
            "an author's explicit unlock must not be dropped by Duplicate Sheet");
    }

    // Sibling no-regression case: a plain (default-locked) chart must still duplicate cleanly.
    [Fact]
    public void DuplicateSheet_ChartWithDefaultLocked_LeavesFieldAtDefault()
    {
        var workbook = new Workbook("ChartCloneLockedDefault");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        workbook.Sheets[1].Charts.Should().ContainSingle().Subject.Locked.Should().BeTrue();
    }
}
