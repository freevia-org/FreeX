using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// round-177-drawing-anchor-cell-persist. The sibling defect found while fixing
/// round-176-drawing-hyperlink-persist (see R175_ShiftedDrawingHyperlinkSurvivesPatchSaveTests for that
/// one): the same verbatim-preservation path also discarded a source-loaded drawing object's shifted
/// ANCHOR CELL. <c>XlsxSourceDrawingGeometryRewriter</c> rewrote the preserved anchor's sub-cell offsets
/// and its size/to-marker, but never the from-marker's <c>xdr:col</c>/<c>xdr:row</c> -- so a row/column
/// insert or delete moved the object in the model (RowColumnShiftHelpers assigns a shifted
/// <c>Anchor</c>) and the saved file replayed the pre-shift cell. The original probe showed it plainly:
/// a shape anchored at row 1 moved to row 2 in the model, and the saved <c>xdr:row</c> was still 0.
///
/// <para>The from-marker now follows the model's <c>Anchor</c>. For a twoCellAnchor that MOVED without
/// being resized, the to-marker is translated by the same delta: the resize skip-gate (R94) correctly
/// declines to recompute it, but leaving it where it was would stretch the object from its new top-left
/// to its old bottom-right.</para>
/// </summary>
public sealed class R177_ShiftedDrawingAnchorCellSurvivesSaveTests
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void InsertRow_ShiftedAnchorCellOfEveryDrawingObjectKind_SurvivesSaveAndReload()
    {
        var adapter = new XlsxFileAdapter();
        var loaded = SaveAndReload(adapter, BuildWorkbook());
        var sheet = loaded.GetSheetAt(0);
        AssertAnchorRows(sheet, shapeRow: 8, textBoxRow: 12, pictureRow: 16, "the fixture round-trips before any edit");

        sheet.DrawingShapes[0].IsSourceLoaded.Should().BeTrue(
            "the defect only exists for objects the writer skips as source-loaded");
        sheet.TextBoxes[0].IsSourceLoaded.Should().BeTrue();
        sheet.Pictures[0].IsSourceLoaded.Should().BeTrue();

        new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 3)
            .Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        AssertAnchorRows(sheet, 11, 15, 19, "the insert moved every object down three rows in the model");

        AssertAnchorRows(
            SaveAndReload(adapter, loaded).GetSheetAt(0),
            11, 15, 19,
            "the shifted anchor cell must reach the saved file, not just the in-memory model");
    }

    [Fact]
    public void DeleteRow_ShiftedAnchorCellOfEveryDrawingObjectKind_SurvivesSaveAndReload()
    {
        var adapter = new XlsxFileAdapter();
        var loaded = SaveAndReload(adapter, BuildWorkbook());
        var sheet = loaded.GetSheetAt(0);

        new DeleteRowsCommand(sheet.Id, startRow: 1, count: 2)
            .Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        AssertAnchorRows(sheet, 6, 10, 14, "the delete moved every object up two rows in the model");

        AssertAnchorRows(
            SaveAndReload(adapter, loaded).GetSheetAt(0),
            6, 10, 14,
            "a delete-driven move must reach the saved file too");
    }

    [Fact]
    public void InsertColumn_ShiftedAnchorColumn_SurvivesSaveAndReload()
    {
        var adapter = new XlsxFileAdapter();
        var loaded = SaveAndReload(adapter, BuildWorkbook());
        var sheet = loaded.GetSheetAt(0);
        sheet.DrawingShapes[0].Anchor.Col.Should().Be(2);

        new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1)
            .Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        sheet.DrawingShapes[0].Anchor.Col.Should().Be(3, "the insert moved the shape one column right");

        SaveAndReload(adapter, loaded).GetSheetAt(0).DrawingShapes[0].Anchor.Col.Should().Be(
            3, "the shifted anchor column must reach the saved file");
    }

    [Fact]
    public void AMovedObject_KeepsItsSize()
    {
        var adapter = new XlsxFileAdapter();
        var loaded = SaveAndReload(adapter, BuildWorkbook());
        var sheet = loaded.GetSheetAt(0);
        var (width, height) = (sheet.DrawingShapes[0].Width, sheet.DrawingShapes[0].Height);

        new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 3)
            .Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved).GetSheetAt(0);

        reloaded.DrawingShapes[0].Width.Should().BeApproximately(
            width, 1.0, "a move must not resize the object -- the to-marker follows the from-marker");
        reloaded.DrawingShapes[0].Height.Should().BeApproximately(height, 1.0);

        AssertSchemaValid(saved, "a rewritten from/to marker pair must stay schema-legal");
    }

    [Fact]
    public void AnUnmovedObject_RoundTripsUnchanged()
    {
        var adapter = new XlsxFileAdapter();
        var loaded = SaveAndReload(adapter, BuildWorkbook());

        AssertAnchorRows(
            SaveAndReload(adapter, loaded).GetSheetAt(0),
            8, 12, 16,
            "a plain re-save must leave every preserved anchor exactly where it was");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static Workbook BuildWorkbook()
    {
        var workbook = new Workbook("AnchorShift");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("keep the sheet non-empty"));
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Rectangle 1",
            Anchor = new CellAddress(sheet.Id, 8, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 200,
            Height = 100,
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Name = "TextBox 1",
            Anchor = new CellAddress(sheet.Id, 12, 2),
            Text = "hello",
        });
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Picture 1",
            Anchor = new CellAddress(sheet.Id, 16, 2),
            Kind = PictureKind.Image,
            ImageBytes = PngBytes,
            ContentType = "image/png",
        });
        return workbook;
    }

    private static void AssertAnchorRows(Sheet sheet, uint shapeRow, uint textBoxRow, uint pictureRow, string because)
    {
        sheet.DrawingShapes.Should().ContainSingle(because).Which.Anchor.Row.Should().Be(shapeRow, because);
        sheet.TextBoxes.Should().ContainSingle(because).Which.Anchor.Row.Should().Be(textBoxRow, because);
        sheet.Pictures.Should().ContainSingle(because).Which.Anchor.Row.Should().Be(pictureRow, because);
    }

    private static void AssertSchemaValid(MemoryStream saved, string because)
    {
        saved.Position = 0;
        using var document = SpreadsheetDocument.Open(saved, false);
        new OpenXmlValidator(FileFormatVersions.Microsoft365)
            .Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Path?.XPath}: {error.Description}")
            .Should().BeEmpty(because);
    }

    private static Workbook SaveAndReload(XlsxFileAdapter adapter, Workbook workbook)
    {
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        return adapter.Load(saved);
    }
}
