using System.IO;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R112: XlsxHeaderFooterPicturePackageWriter.Save numbered each "xl/drawings/freexHeaderFooterN.vml"
/// part it (re)writes from a save-local counter that only advanced for sheets it was ABOUT to rewrite
/// (skipping every sheet in <c>sheetsToPreserve</c> before incrementing -- see the writer's own doc
/// comments). A preserved sheet's legacyDrawingHF relationship still points at whatever N it was given on
/// an EARLIER save; when a DIFFERENT sheet's freshly restarted counter landed on that same N, the writer
/// unconditionally deleted/recreated that exact path with the OTHER sheet's picture, and the later
/// source-package preservation pass (XlsxWorksheetVmlReferencePreserver, further down the save pipeline)
/// re-wired the preserved sheet's own relationship at that now-overwritten path -- silently swapping one
/// sheet's header/footer picture for another's on a completely ordinary "edit one sheet, leave another
/// untouched" round trip.
///
/// These fixtures go through the REAL product entry point end to end: an in-memory <see cref="Workbook"/>
/// is saved via <see cref="XlsxFileAdapter"/>, reloaded (establishing the SourcePackage the second save
/// patches against), edited, and saved again -- exactly the sequence a user hits by opening a workbook
/// with header/footer pictures on more than one sheet, changing only one of them, and saving. No XML is
/// hand-authored: every fixture byte comes from FreeX's own writer via this same Save/Load round trip, so
/// a writer/reader mismatch could never hide behind a hand-built fixture.
/// </summary>
public sealed class R112_HeaderFooterPictureVmlPathCollisionTests
{
    // Six distinct 1-byte-payload "PNGs" -- content only has to be byte-distinguishable (readers never
    // sniff/decode it; the content type comes from the declared MIME string), so each sheet/version gets
    // its own final payload byte to make post-reload ImageBytes comparisons unambiguous.
    private static byte[] PngWithPayload(byte payloadByte) =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, payloadByte,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44,
        0xAE, 0x42, 0x60, 0x82
    ];

    private static byte[] SheetAOriginalPng() => PngWithPayload(0xA1);
    private static byte[] SheetAUpdatedPng() => PngWithPayload(0xA2);
    private static byte[] SheetBOriginalPng() => PngWithPayload(0xB1);
    private static byte[] SheetBUpdatedPng() => PngWithPayload(0xB2);
    private static byte[] SheetCOriginalPng() => PngWithPayload(0xC1);
    private static byte[] SheetCUpdatedPng() => PngWithPayload(0xC2);

    private static Sheet AddSheetWithHeaderPicture(Workbook workbook, string name, byte[] pngBytes, string fileName)
    {
        var sheet = workbook.AddSheet(name);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(name));
        sheet.PageHeader = new WorksheetHeaderFooter("&[Picture]", "", "");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture(pngBytes, "image/png", fileName, 96, 32),
            null,
            null);
        return sheet;
    }

    [Fact]
    public void SecondSave_WithOneSheetPreservedAndAnotherEdited_DoesNotOverwritePreservedSheetsPicture()
    {
        var workbook = new Workbook("R112HeaderFooterVmlCollision");
        AddSheetWithHeaderPicture(workbook, "SheetA", SheetAOriginalPng(), "sheetA-logo.png");
        AddSheetWithHeaderPicture(workbook, "SheetB", SheetBOriginalPng(), "sheetB-logo.png");

        var adapter = new XlsxFileAdapter();
        using var firstSave = new MemoryStream();
        adapter.Save(workbook, firstSave);

        firstSave.Position = 0;
        var reloaded = adapter.Load(firstSave);

        // Leave SheetA's header picture completely untouched; only edit SheetB's.
        var reloadedSheetB = reloaded.GetSheetAt(1);
        reloadedSheetB.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture(SheetBUpdatedPng(), "image/png", "sheetB-logo-v2.png", 96, 32),
            null,
            null);
        reloadedSheetB.SetCell(new CellAddress(reloadedSheetB.Id, 2, 2), new NumberValue(99));

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);

        // FreeX's own "freexHeaderFooterN.vml" naming is never patch-safe (the patch-save eligibility
        // guard only allows the "vmlDrawingN.vml" name real Excel uses -- see
        // XlsxFileAdapter.TryAddPatchSafeHeaderFooterVmlDrawingPaths), so any FreeX round trip of a
        // header/footer picture always takes the FULL save path this defect lives on. Asserting it here
        // documents that this fixture actually exercises the buggy code path rather than short-circuiting
        // through source-copy or cell-patch.
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        secondSave.Position = 0;
        var reloadedAgain = new XlsxFileAdapter().Load(secondSave);
        var finalSheetA = reloadedAgain.GetSheetAt(0);
        var finalSheetB = reloadedAgain.GetSheetAt(1);

        finalSheetA.PageHeaderPictures.Left.Should().NotBeNull();
        finalSheetA.PageHeaderPictures.Left!.ImageBytes.Should().Equal(SheetAOriginalPng());

        finalSheetB.PageHeaderPictures.Left.Should().NotBeNull();
        finalSheetB.PageHeaderPictures.Left!.ImageBytes.Should().Equal(SheetBUpdatedPng());
    }

    /// <summary>
    /// No-regression sibling: a MIDDLE sheet is preserved while BOTH its neighbors are edited on the
    /// same save, so the allocator must skip a reserved index that sits between two freshly assigned
    /// ones (not just avoid colliding with the very first slot). Covers the fix generalizing beyond the
    /// minimal two-sheet repro above.
    /// </summary>
    [Fact]
    public void SecondSave_WithMiddleSheetPreservedAndBothNeighborsEdited_KeepsAllThreePicturesDistinct()
    {
        var workbook = new Workbook("R112HeaderFooterVmlCollisionThreeSheets");
        AddSheetWithHeaderPicture(workbook, "SheetA", SheetAOriginalPng(), "sheetA-logo.png");
        AddSheetWithHeaderPicture(workbook, "SheetB", SheetBOriginalPng(), "sheetB-logo.png");
        AddSheetWithHeaderPicture(workbook, "SheetC", SheetCOriginalPng(), "sheetC-logo.png");

        var adapter = new XlsxFileAdapter();
        using var firstSave = new MemoryStream();
        adapter.Save(workbook, firstSave);

        firstSave.Position = 0;
        var reloaded = adapter.Load(firstSave);

        // SheetB (the middle sheet) is left untouched; SheetA and SheetC are both edited.
        var reloadedSheetA = reloaded.GetSheetAt(0);
        reloadedSheetA.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture(SheetAUpdatedPng(), "image/png", "sheetA-logo-v2.png", 96, 32),
            null,
            null);
        var reloadedSheetC = reloaded.GetSheetAt(2);
        reloadedSheetC.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture(SheetCUpdatedPng(), "image/png", "sheetC-logo-v2.png", 96, 32),
            null,
            null);

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        secondSave.Position = 0;
        var reloadedAgain = new XlsxFileAdapter().Load(secondSave);

        reloadedAgain.GetSheetAt(0).PageHeaderPictures.Left!.ImageBytes.Should().Equal(SheetAUpdatedPng());
        reloadedAgain.GetSheetAt(1).PageHeaderPictures.Left!.ImageBytes.Should().Equal(SheetBOriginalPng());
        reloadedAgain.GetSheetAt(2).PageHeaderPictures.Left!.ImageBytes.Should().Equal(SheetCUpdatedPng());
    }
}
