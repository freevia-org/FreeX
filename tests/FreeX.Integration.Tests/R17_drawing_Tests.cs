using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Round-17 "drawing" bucket fix verification.
/// <para>
/// R17-drawing-hyperlink-name-1: <c>DuplicateSheetDrawingCloner.CloneDrawingShape</c>/
/// <c>CloneTextBox</c> copied <c>source.IsSourceLoaded</c> (true) onto the clone, but the
/// duplicate's brand-new sheet name has no matching source drawing part
/// (<c>XlsxFileAdapter.SavePostProcessing.GetSourceDrawingPathsBySheet</c> keys by sheet NAME),
/// and the writer skips every <c>IsSourceLoaded</c> object — so the cloned shape/text box was
/// silently dropped on save. Fixed to force <c>IsSourceLoaded = false</c> on both clones, mirroring
/// <c>ClonePicture</c>.
/// </para>
/// <para>
/// R17-drawing-hyperlink-name-2: <c>XlsxSourceDrawingGeometryRewriter</c>'s text-box loop only
/// rewrote anchor geometry, never patching the preserved <c>&lt;xdr:txBody&gt;</c>/<c>&lt;a:t&gt;</c>
/// runs — so an edited source-loaded text box's new text (<c>SetTextBoxTextCommand</c> mutates
/// <c>TextBoxModel.Text</c> without clearing <c>IsSourceLoaded</c>) was discarded on save. Fixed by
/// patching the preserved txBody's runs to the model's current text.
/// </para>
/// <para>
/// R17-drawing-hyperlink-name-3: alt text (<c>cNvPr@descr</c>) was patched for pictures only; the
/// shape and text-box loops never patched it, so an alt-text edit on a source-loaded shape/text box
/// was dropped. Fixed by patching <c>cNvPr@descr</c>/<c>@title</c> for shapes and text boxes too.
/// </para>
/// </summary>
public sealed class R17_drawing_Tests
{
    // ══════════════════════════════════════════════════════════════════════════
    // R17-drawing-hyperlink-name-1 — Duplicate Sheet must not drop a source-loaded
    // shape/text box on save (clone must round-trip as a freshly-authored object).
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DuplicateSheet_SourceLoadedShapeAndTextBox_SurviveSaveReload()
    {
        var adapter = new XlsxFileAdapter();

        var workbook1 = new Workbook("DuplicateDrawingRegression");
        var sheet1 = workbook1.AddSheet("Sheet1");
        sheet1.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Shape1",
            Anchor = new CellAddress(sheet1.Id, 2, 2),
            Width = 100,
            Height = 60
        });
        sheet1.TextBoxes.Add(new TextBoxModel
        {
            Name = "TextBox1",
            Anchor = new CellAddress(sheet1.Id, 4, 4),
            Text = "Hello box",
            Width = 120,
            Height = 40
        });

        using var firstSave = new MemoryStream();
        adapter.Save(workbook1, firstSave);

        // Reload so the shape/text box become source-loaded/preserved, exactly like opening a
        // real .xlsx.
        firstSave.Position = 0;
        var workbook2 = adapter.Load(firstSave);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook2, out var blockReason)
            .Should().BeTrue(blockReason);

        var reloadedSheet1 = workbook2.GetSheet("Sheet1")!;
        reloadedSheet1.DrawingShapes.Should().ContainSingle().Which.IsSourceLoaded.Should().BeTrue(
            "the shape came from the source package on reload");
        reloadedSheet1.TextBoxes.Should().ContainSingle().Which.IsSourceLoaded.Should().BeTrue(
            "the text box came from the source package on reload");

        var ctx = new TestCommandContext(workbook2);
        var duplicateCommand = new DuplicateSheetCommand(reloadedSheet1.Id);
        duplicateCommand.Apply(ctx).Success.Should().BeTrue();

        using var secondSave = new MemoryStream();
        adapter.Save(workbook2, secondSave);

        secondSave.Position = 0;
        var reloaded = adapter.Load(secondSave);
        var copySheet = reloaded.Sheets[1];

        copySheet.DrawingShapes.Should().ContainSingle(
            "a shape cloned from a source-loaded original must survive save as a freshly-authored " +
            "object (its new sheet name has no matching source drawing part), not be silently dropped");
        copySheet.TextBoxes.Should().ContainSingle(
            "a text box cloned from a source-loaded original must survive save the same way");
        copySheet.TextBoxes[0].Text.Should().Be("Hello box");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // R17-drawing-hyperlink-name-2 — an edited source-loaded text box's new text must
    // be patched into the preserved txBody, not discarded on save.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SourceLoadedTextBox_EditedText_SurvivesSaveReload()
    {
        var adapter = new XlsxFileAdapter();

        var workbook1 = new Workbook("TextBoxTextEditRegression");
        var sheet1 = workbook1.AddSheet("Sheet1");
        sheet1.TextBoxes.Add(new TextBoxModel
        {
            Name = "TextBox1",
            Anchor = new CellAddress(sheet1.Id, 2, 2),
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
        var reloadedTextBox = reloadedSheet1.TextBoxes.Should().ContainSingle().Subject;
        reloadedTextBox.IsSourceLoaded.Should().BeTrue("the text box came from the source package on reload");

        var ctx = new TestCommandContext(workbook2);
        var command = new SetTextBoxTextCommand(reloadedSheet1.Id, reloadedTextBox.Id, "Edited text");
        command.Apply(ctx).Success.Should().BeTrue();

        using var secondSave = new MemoryStream();
        adapter.Save(workbook2, secondSave);

        secondSave.Position = 0;
        var reloaded = adapter.Load(secondSave);
        var finalTextBox = reloaded.GetSheet("Sheet1")!.TextBoxes.Should().ContainSingle().Subject;

        finalTextBox.Text.Should().Be("Edited text",
            "an edited source-loaded text box's new text must survive save, not replay the original " +
            "preserved txBody text");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // R17-drawing-hyperlink-name-3 — an edited source-loaded shape's alt text must be
    // patched into the preserved cNvPr@descr, not discarded on save.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SourceLoadedShape_EditedAltText_SurvivesSaveReload()
    {
        var adapter = new XlsxFileAdapter();

        var workbook1 = new Workbook("ShapeAltTextEditRegression");
        var sheet1 = workbook1.AddSheet("Sheet1");
        sheet1.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Shape1",
            Anchor = new CellAddress(sheet1.Id, 2, 2),
            Width = 100,
            Height = 60,
            AltText = "Original alt text"
        });

        using var firstSave = new MemoryStream();
        adapter.Save(workbook1, firstSave);

        firstSave.Position = 0;
        var workbook2 = adapter.Load(firstSave);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook2, out var blockReason)
            .Should().BeTrue(blockReason);

        var reloadedSheet1 = workbook2.GetSheet("Sheet1")!;
        var reloadedShape = reloadedSheet1.DrawingShapes.Should().ContainSingle().Subject;
        reloadedShape.IsSourceLoaded.Should().BeTrue("the shape came from the source package on reload");

        var ctx = new TestCommandContext(workbook2);
        var command = new SetDrawingShapeAltTextCommand(reloadedSheet1.Id, reloadedShape.Id, "Edited alt text");
        command.Apply(ctx).Success.Should().BeTrue();

        using var secondSave = new MemoryStream();
        adapter.Save(workbook2, secondSave);

        secondSave.Position = 0;
        var reloaded = adapter.Load(secondSave);
        var finalShape = reloaded.GetSheet("Sheet1")!.DrawingShapes.Should().ContainSingle().Subject;

        finalShape.AltText.Should().Be("Edited alt text",
            "an edited source-loaded shape's alt text must survive save, not replay the original " +
            "preserved cNvPr descr attribute");
    }
}
