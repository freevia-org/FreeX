using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R91-print-twin-two-tier-synthetic-sweep-2: DuplicateSheetDrawingCloner's ClonePicture object
/// initializer copies every other PictureModel field (Title, AltText, crop, flip, etc.) but omitted
/// the r90 <see cref="PictureModel.IsDecorative"/> "Mark as decorative" flag, so a duplicated
/// decorative picture silently reverted to the default (not decorative) and falsely failed
/// AccessibilityCheckerService's missing-alt-text rule even though real Excel preserves the
/// decorative marking across Duplicate Sheet / Move-or-Copy. Verifies the field now survives
/// Duplicate Sheet, plus a sibling no-regression case confirming a plain (non-decorative) picture
/// still duplicates cleanly. Exercised through the real command entry point: DuplicateSheetCommand.
/// </summary>
public sealed class R91_DuplicateSheetDrawingClonerDecorativePictureTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    // The bug case: a picture explicitly marked decorative must stay decorative on the copy.
    [Fact]
    public void DuplicateSheet_DecorativePicture_PreservesIsDecorativeOnCopy()
    {
        var workbook = new Workbook("PictureCloneDecorative");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Divider",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
            Width = 100,
            Height = 20,
            IsDecorative = true
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedPicture = workbook.Sheets[1].Pictures.Should().ContainSingle().Subject;
        copiedPicture.IsDecorative.Should().BeTrue(
            "the 'Mark as decorative' flag must not be dropped by Duplicate Sheet");
    }

    // Sibling no-regression case: a plain (non-decorative) picture must still duplicate cleanly,
    // leaving IsDecorative at its default (false).
    [Fact]
    public void DuplicateSheet_NonDecorativePicture_LeavesIsDecorativeAtDefault()
    {
        var workbook = new Workbook("PictureCloneNonDecorative");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Photo",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [4, 5, 6],
            ContentType = "image/png",
            Width = 100,
            Height = 20
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedPicture = workbook.Sheets[1].Pictures.Should().ContainSingle().Subject;
        copiedPicture.IsDecorative.Should().BeFalse();
    }
}
