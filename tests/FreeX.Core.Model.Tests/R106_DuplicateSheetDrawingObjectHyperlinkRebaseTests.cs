using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R106-drawing-object-hyperlink-duplicate-rebase: a drawing object's (text box/shape/picture/
/// chart) internal ('Place in This Document') <see cref="DrawingObjectHyperlink"/> target is a
/// sheet-qualified reference stored verbatim (e.g. "Sheet1!A1"), the exact same shape
/// <c>Sheet.Clone</c> already rebases for a CELL hyperlink so a self-referencing link keeps
/// pointing at the DUPLICATE sheet instead of jumping back to the source sheet. Before this fix,
/// <c>DuplicateSheetDrawingCloner.CloneTextBox</c>/<c>CloneDrawingShape</c>/<c>ClonePicture</c>/
/// <c>CloneChart</c> copied <c>Hyperlink</c> verbatim with no equivalent rewrite, so a shape's own
/// same-sheet hyperlink on a duplicated sheet silently kept jumping back to the ORIGINAL sheet —
/// unlike the equivalent cell hyperlink right next to it. Verifies each of the four drawing-object
/// kinds now rebases the same way, plus sibling no-regression cases for a cross-sheet target
/// (must NOT be rewritten) and an external hyperlink (must NOT be touched at all).
/// </summary>
public sealed class R106_DuplicateSheetDrawingObjectHyperlinkRebaseTests
{
    [Fact]
    public void DuplicateSheet_TextBoxSameSheetHyperlink_RebasesOntoCopy()
    {
        var workbook = new Workbook("TextBoxHyperlinkRebase");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "Go to A1",
            Hyperlink = new DrawingObjectHyperlink("Sheet1!A1")
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        copy.Name.Should().Be("Sheet1 (2)");
        var copiedTextBox = copy.TextBoxes.Should().ContainSingle().Subject;
        copiedTextBox.Hyperlink.Should().NotBeNull();
        copiedTextBox.Hyperlink!.Target.Should().Be("'Sheet1 (2)'!A1",
            because: "a text box's own same-sheet hyperlink must follow the duplicate, matching the equivalent cell hyperlink");
    }

    [Fact]
    public void DuplicateSheet_ShapeSameSheetHyperlink_RebasesOntoCopy()
    {
        var workbook = new Workbook("ShapeHyperlinkRebase");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            Hyperlink = new DrawingObjectHyperlink("Sheet1!B2")
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        var copiedShape = copy.DrawingShapes.Should().ContainSingle().Subject;
        copiedShape.Hyperlink.Should().NotBeNull();
        copiedShape.Hyperlink!.Target.Should().Be("'Sheet1 (2)'!B2",
            because: "a shape's own same-sheet hyperlink must follow the duplicate, matching the equivalent cell hyperlink");
    }

    [Fact]
    public void DuplicateSheet_PictureSameSheetHyperlink_RebasesOntoCopy()
    {
        var workbook = new Workbook("PictureHyperlinkRebase");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
            Hyperlink = new DrawingObjectHyperlink("Sheet1!C3")
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        var copiedPicture = copy.Pictures.Should().ContainSingle().Subject;
        copiedPicture.Hyperlink.Should().NotBeNull();
        copiedPicture.Hyperlink!.Target.Should().Be("'Sheet1 (2)'!C3",
            because: "a picture's own same-sheet hyperlink must follow the duplicate, matching the equivalent cell hyperlink");
    }

    [Fact]
    public void DuplicateSheet_ChartSameSheetHyperlink_RebasesOntoCopy()
    {
        var workbook = new Workbook("ChartHyperlinkRebase");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            Hyperlink = new DrawingObjectHyperlink("Sheet1!D4")
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.Hyperlink.Should().NotBeNull();
        copiedChart.Hyperlink!.Target.Should().Be("'Sheet1 (2)'!D4",
            because: "a chart's own same-sheet hyperlink must follow the duplicate, matching the equivalent cell hyperlink");
    }

    // Sibling no-regression case: a hyperlink qualified with a DIFFERENT sheet's name must keep
    // pointing at that other sheet, not follow the duplicate (matches the identical cell-hyperlink
    // and ConditionalFormat/DataValidation sibling cases already covered elsewhere).
    [Fact]
    public void DuplicateSheet_ShapeCrossSheetHyperlink_StaysOnOriginalSheet()
    {
        var workbook = new Workbook("ShapeHyperlinkCrossSheet");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.AddSheet("Other");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            Hyperlink = new DrawingObjectHyperlink("Other!A1")
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        copy.Name.Should().Be("Sheet1 (2)");
        var copiedShape = copy.DrawingShapes.Should().ContainSingle().Subject;
        copiedShape.Hyperlink!.Target.Should().Be("Other!A1",
            because: "a hyperlink qualified with a DIFFERENT sheet's name must not be remapped onto the duplicate");
    }

    // Sibling no-regression case: an external ("Existing File or Web Page") hyperlink must be left
    // completely untouched by the rebase, including when its Target text happens to look like it
    // could contain a sheet-name-shaped substring.
    [Fact]
    public void DuplicateSheet_TextBoxExternalHyperlink_IsNeverRewritten()
    {
        var workbook = new Workbook("TextBoxHyperlinkExternal");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "Visit",
            Hyperlink = new DrawingObjectHyperlink("https://example.com/Sheet1!A1", TargetMode: "External")
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedTextBox = workbook.Sheets[1].TextBoxes.Should().ContainSingle().Subject;
        copiedTextBox.Hyperlink!.Target.Should().Be("https://example.com/Sheet1!A1",
            because: "an external hyperlink target must never be treated as a same-sheet-qualified reference");
        copiedTextBox.Hyperlink!.TargetMode.Should().Be("External");
    }
}
