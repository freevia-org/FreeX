using System.IO;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for G16: <see cref="XlsxSourceDrawingGeometryRewriter"/> must match XML anchors only
/// against SOURCE-LOADED model objects, in their original load order — never against the full model list
/// (which can contain NEW, non-source-loaded objects appended after load). Before the fix, appending a new
/// picture/text box/shape to a sheet that already had a source-loaded one of the same kind caused the
/// rewriter to walk <c>sheet.Pictures</c>/<c>TextBoxes</c>/<c>DrawingShapes</c> in full-list order against
/// XML anchors in document order, silently swapping resized geometry between the new and the original
/// object on save.
/// </summary>
public sealed class XlsxSourceDrawingGeometryRewriterSameSheetOrderTests
{
    [Fact]
    public void ResizingSourcePicture_AfterAddingNewPictureOnSameSheet_DoesNotSwapGeometryBetweenThem()
    {
        var adapter = new XlsxFileAdapter();

        // ── Step 1: build a workbook with one picture on Sheet1 and save it ─────────────
        var workbook1 = new Workbook("GeometrySwapRegression");
        var sheet1 = workbook1.AddSheet("Sheet1");
        sheet1.Pictures.Add(new PictureModel
        {
            Name = "OriginalPicture",
            Anchor = new CellAddress(sheet1.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64,
            AltText = "Original picture"
        });

        using var firstSave = new MemoryStream();
        adapter.Save(workbook1, firstSave);

        // ── Step 2: reload so the picture becomes source-loaded/preserved ───────────────
        firstSave.Position = 0;
        var workbook2 = adapter.Load(firstSave);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook2, out var blockReason)
            .Should().BeTrue(blockReason);

        var reloadedSheet1 = workbook2.GetSheet("Sheet1")!;
        var originalPicture = reloadedSheet1.Pictures.Should().ContainSingle().Subject;
        originalPicture.IsSourceLoaded.Should().BeTrue("the picture came from the source package on reload");

        // ── Step 3: resize the ORIGINAL (source-loaded) picture ──────────────────────────
        originalPicture.Width = 200;
        originalPicture.Height = 150;

        // ── Step 4: append a NEW (non-source-loaded) picture to the SAME sheet ───────────
        reloadedSheet1.Pictures.Add(new PictureModel
        {
            Name = "NewPicture",
            Anchor = new CellAddress(reloadedSheet1.Id, 6, 6),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 300,
            Height = 50,
            AltText = "New picture"
        });

        // ── Step 5: save again and reload ────────────────────────────────────────────────
        using var secondSave = new MemoryStream();
        adapter.Save(workbook2, secondSave);

        secondSave.Position = 0;
        var reloaded = adapter.Load(secondSave);
        var finalSheet1 = reloaded.GetSheet("Sheet1")!;
        finalSheet1.Pictures.Should().HaveCount(2, "both the original and the newly added picture must survive the save");

        var finalOriginal = finalSheet1.Pictures.Should()
            .ContainSingle(picture => picture.Name == "OriginalPicture",
                "the original picture must still be identifiable by name after the round-trip").Subject;
        var finalNew = finalSheet1.Pictures.Should()
            .ContainSingle(picture => picture.Name == "NewPicture",
                "the newly added picture must still be identifiable by name after the round-trip").Subject;

        // Without the fix, the rewriter matches XML-index-0 (the new picture's anchor, written first
        // by XlsxWorksheetDrawingObjectWriter) against sheet.Pictures[0] (the original, source-loaded
        // picture) and overwrites the NEW picture's geometry with the ORIGINAL's resized 200x150 values,
        // while the true source anchor (XML-index-1) is matched against sheet.Pictures[1] (the new
        // picture, not source-loaded) and skipped — so the new picture would come back as 200x150 and
        // the original would come back unresized (still its pre-resize 96x64 default from the source).
        finalOriginal.Width.Should().Be(200, "the original picture's resize must be preserved on its own anchor");
        finalOriginal.Height.Should().Be(150, "the original picture's resize must be preserved on its own anchor");
        finalNew.Width.Should().Be(300, "the new picture's own width must not be overwritten by the original's geometry");
        finalNew.Height.Should().Be(50, "the new picture's own height must not be overwritten by the original's geometry");
    }

    [Fact]
    public void ResizingSourceTextBox_AfterAddingNewTextBoxOnSameSheet_DoesNotSwapGeometryBetweenThem()
    {
        var adapter = new XlsxFileAdapter();

        var workbook1 = new Workbook("TextBoxGeometrySwapRegression");
        var sheet1 = workbook1.AddSheet("Sheet1");
        sheet1.TextBoxes.Add(new TextBoxModel
        {
            Name = "OriginalTextBox",
            Anchor = new CellAddress(sheet1.Id, 1, 1),
            Text = "Original text",
            Width = 120,
            Height = 40
        });

        using var firstSave = new MemoryStream();
        adapter.Save(workbook1, firstSave);

        firstSave.Position = 0;
        var workbook2 = adapter.Load(firstSave);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook2, out var blockReason)
            .Should().BeTrue(blockReason);

        var reloadedSheet1 = workbook2.GetSheet("Sheet1")!;
        var originalTextBox = reloadedSheet1.TextBoxes.Should().ContainSingle().Subject;
        originalTextBox.IsSourceLoaded.Should().BeTrue("the text box came from the source package on reload");

        originalTextBox.Width = 250;
        originalTextBox.Height = 90;

        reloadedSheet1.TextBoxes.Add(new TextBoxModel
        {
            Name = "NewTextBox",
            Anchor = new CellAddress(reloadedSheet1.Id, 8, 8),
            Text = "New text",
            Width = 60,
            Height = 20
        });

        using var secondSave = new MemoryStream();
        adapter.Save(workbook2, secondSave);

        secondSave.Position = 0;
        var reloaded = adapter.Load(secondSave);
        var finalSheet1 = reloaded.GetSheet("Sheet1")!;
        finalSheet1.TextBoxes.Should().HaveCount(2);

        var finalOriginal = finalSheet1.TextBoxes.Should()
            .ContainSingle(textBox => textBox.Name == "OriginalTextBox").Subject;
        var finalNew = finalSheet1.TextBoxes.Should()
            .ContainSingle(textBox => textBox.Name == "NewTextBox").Subject;

        finalOriginal.Width.Should().Be(250, "the original text box's resize must be preserved on its own anchor");
        finalOriginal.Height.Should().Be(90, "the original text box's resize must be preserved on its own anchor");
        finalNew.Width.Should().Be(60, "the new text box's own width must not be overwritten by the original's geometry");
        finalNew.Height.Should().Be(20, "the new text box's own height must not be overwritten by the original's geometry");
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];
}
