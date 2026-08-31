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
/// r175 remediation. Round 175 taught the row/column shift to rewrite a drawing object's
/// "Place in This Document" hyperlink, which made <c>DrawingObjectHyperlink</c> mutable for the
/// first time from a command that does not force a full save. R101's patch-safety guard fired,
/// because the drawing fingerprint that decides whether a save may reuse the source package's
/// drawing parts verbatim did not compare the field -- so the shifted target could be discarded.
/// The fingerprint now covers it (see WriteDrawingObjectHyperlinkFingerprint).
///
/// <para>round-176-drawing-hyperlink-persist: writing this test first revealed that the shifted
/// target still did not survive a save-and-reload even with the fingerprint fixed -- the model held
/// Sheet1!$A$6 after the insert but a reloaded workbook read Sheet1!$A$5. The cause was one level
/// down, in the .xlsx writer rather than in the patch-safety decision: a picture/text box/shape
/// loaded from an .xlsx keeps <c>IsSourceLoaded</c> set, and <c>XlsxWorksheetDrawingObjectWriter</c>
/// gates every object it emits behind <c>!IsSourceLoaded</c>, so such an object's anchor -- its
/// <c>a:hlinkClick</c> and the drawing-rels entry behind it included -- was carried forward VERBATIM
/// from the source package on every save, replaying the pre-shift target. (A CHART was never
/// affected: charts have no <c>IsSourceLoaded</c> flag and are always re-emitted from the model.)
/// <c>XlsxSourceDrawingHyperlinkRewriter</c> now rewrites that preserved <c>hlinkClick</c> from the
/// live model, exactly as <c>XlsxSourceDrawingGeometryRewriter</c> (F15) already did for the
/// preserved anchor's geometry. The save-and-reload assertions below pin that end to end.</para>
///
/// <para>Writing THOSE revealed one more layer, hit by an edit that changes a hyperlink and nothing
/// else (Insert/Remove Hyperlink on a drawing object -- never the row/column shift, which always
/// moves cells too): <c>DrawingObjectHyperlink</c> is not part of the .fxl DTOs the whole-model
/// fingerprint is built from, so such a save looked like "model unchanged" and took the source-COPY
/// path, replaying the original package bytes wholesale before any writer ran.
/// <c>WriteDrawingObjectHyperlinkModelFingerprint</c> now feeds the field into that fingerprint, the
/// same remedy <c>WriteLegacyCommentAuthorFingerprint</c>/<c>WriteShownCommentsFingerprint</c>
/// already apply to their own fingerprint-invisible fields.</para>
/// </summary>
public sealed class R175_ShiftedDrawingHyperlinkSurvivesPatchSaveTests
{
    // 1x1 transparent PNG -- the smallest raster that makes a PictureModel a "supported" picture.
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void InsertRow_ShiftsAShapeHyperlinkTargetInTheModel()
    {
        var workbook = new Workbook("HyperlinkShift");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("target"));
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Shape 1",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 100,
            Height = 50,
            Hyperlink = new DrawingObjectHyperlink("Sheet1!$A$5", TargetMode: null, Tooltip: "jump"),
        });

        var context = new TestCommandContext(workbook);
        new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1).Apply(context).Success.Should().BeTrue();

        sheet.DrawingShapes[0].Hyperlink!.Target.Should().Be(
            "Sheet1!$A$6",
            "inserting a row above the target must carry the hyperlink with the rows it points at");
        sheet.DrawingShapes[0].Hyperlink!.Tooltip.Should().Be(
            "jump",
            "only the address changes -- the rest of the hyperlink is carried through unchanged");
    }

    [Fact]
    public void UndoingTheInsert_RestoresTheOriginalHyperlinkTarget()
    {
        var workbook = new Workbook("HyperlinkShift");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Shape 1",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 100,
            Height = 50,
            Hyperlink = new DrawingObjectHyperlink("Sheet1!$A$5"),
        });

        var context = new TestCommandContext(workbook);
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        command.Apply(context).Success.Should().BeTrue();
        command.Revert(context);

        sheet.DrawingShapes[0].Hyperlink!.Target.Should().Be(
            "Sheet1!$A$5",
            "undo must put the hyperlink back exactly, not leave it shifted");
    }

    // ── The shifted target must reach the SAVED FILE, for all four drawing-object kinds. ──────────

    [Fact]
    public void InsertRow_ShiftedHyperlinkOfEveryDrawingObjectKind_SurvivesSaveAndReload()
    {
        var adapter = new XlsxFileAdapter();
        var loaded = SaveAndReload(adapter, BuildWorkbookWithAllFourKinds());
        var dashboard = loaded.GetSheetAt(1);
        AssertAllFourTargets(dashboard, "Dashboard!$A$20", "the fixture round-trips before any edit");

        // Every object on the sheet is source-loaded now -- the exact state in which the writer skips
        // it and its anchor is carried forward verbatim from the source package.
        dashboard.DrawingShapes[0].IsSourceLoaded.Should().BeTrue();
        dashboard.TextBoxes[0].IsSourceLoaded.Should().BeTrue();
        dashboard.Pictures[0].IsSourceLoaded.Should().BeTrue();

        new InsertRowsCommand(dashboard.Id, beforeRow: 1, count: 1)
            .Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        AssertAllFourTargets(dashboard, "Dashboard!$A$21", "the shift rewrote the in-memory model");

        var reloaded = SaveAndReload(adapter, loaded);
        AssertAllFourTargets(
            reloaded.GetSheetAt(1),
            "Dashboard!$A$21",
            "the shifted target must reach the saved file, not just the in-memory model");
    }

    [Fact]
    public void UndoingTheInsert_RestoresTheOriginalTargetInTheSavedFile()
    {
        var adapter = new XlsxFileAdapter();
        var loaded = SaveAndReload(adapter, BuildWorkbookWithAllFourKinds());
        var dashboard = loaded.GetSheetAt(1);

        var context = new TestCommandContext(loaded);
        var command = new InsertRowsCommand(dashboard.Id, beforeRow: 1, count: 1);
        command.Apply(context).Success.Should().BeTrue();
        command.Revert(context);

        AssertAllFourTargets(
            SaveAndReload(adapter, loaded).GetSheetAt(1),
            "Dashboard!$A$20",
            "an undone shift must leave the saved file pointing where it originally did");
    }

    [Fact]
    public void DeleteRow_ShiftedHyperlinkOfEveryDrawingObjectKind_SurvivesSaveAndReload()
    {
        var adapter = new XlsxFileAdapter();
        var loaded = SaveAndReload(adapter, BuildWorkbookWithAllFourKinds());
        var dashboard = loaded.GetSheetAt(1);

        new DeleteRowsCommand(dashboard.Id, startRow: 1, count: 1)
            .Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        AssertAllFourTargets(dashboard, "Dashboard!$A$19", "the shift rewrote the in-memory model");

        AssertAllFourTargets(
            SaveAndReload(adapter, loaded).GetSheetAt(1),
            "Dashboard!$A$19",
            "a delete-driven shift must reach the saved file too, not just an insert-driven one");
    }

    // ── The same preserved-anchor rewrite, for the other two ways the model's value can change. ───

    [Fact]
    public void RemovingASourceLoadedObjectHyperlink_RemovesItFromTheSavedFile()
    {
        var adapter = new XlsxFileAdapter();
        var loaded = SaveAndReload(adapter, BuildWorkbookWithAllFourKinds());
        var dashboard = loaded.GetSheetAt(1);
        dashboard.DrawingShapes[0].Hyperlink = null;
        dashboard.TextBoxes[0].Hyperlink = null;
        dashboard.Pictures[0].Hyperlink = null;

        var reloaded = SaveAndReload(adapter, loaded).GetSheetAt(1);
        reloaded.DrawingShapes[0].Hyperlink.Should().BeNull(
            "a hyperlink cleared on the model must not be replayed from the preserved anchor");
        reloaded.TextBoxes[0].Hyperlink.Should().BeNull();
        reloaded.Pictures[0].Hyperlink.Should().BeNull();
    }

    [Fact]
    public void AddingAHyperlinkToASourceLoadedObject_ReachesTheSavedFile()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = BuildWorkbookWithAllFourKinds();
        foreach (var shape in workbook.GetSheetAt(1).DrawingShapes)
            shape.Hyperlink = null;

        var loaded = SaveAndReload(adapter, workbook);
        var dashboard = loaded.GetSheetAt(1);
        dashboard.DrawingShapes[0].Hyperlink.Should().BeNull("the fixture starts with no shape hyperlink");
        dashboard.DrawingShapes[0].IsSourceLoaded.Should().BeTrue();
        dashboard.DrawingShapes[0].Hyperlink = new DrawingObjectHyperlink("Dashboard!$A$20");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        adapter.Load(saved).GetSheetAt(1).DrawingShapes[0].Hyperlink!.Target.Should().Be(
            "Dashboard!$A$20",
            "a hyperlink added to a source-loaded object needs a relationship of its own in the " +
            "preserved drawing part");

        AssertSchemaValid(saved, "an hlinkClick inserted into a preserved anchor must be schema-legal");
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

    [Fact]
    public void AnUneditedHyperlink_RoundTripsUnchanged()
    {
        var adapter = new XlsxFileAdapter();
        var loaded = SaveAndReload(adapter, BuildWorkbookWithAllFourKinds());

        AssertAllFourTargets(
            SaveAndReload(adapter, loaded).GetSheetAt(1),
            "Dashboard!$A$20",
            "a plain re-save must leave every preserved hyperlink exactly as it was");
        loaded.GetSheetAt(1).DrawingShapes[0].Hyperlink!.Tooltip.Should().Be(
            "jump", "the tooltip rides along with the target");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A chart, a shape, a text box and a picture on one sheet, each pointing at the same
    /// "Place in This Document" target, so a single row shift moves all four at once.
    /// </summary>
    private static Workbook BuildWorkbookWithAllFourKinds()
    {
        var workbook = new Workbook("HyperlinkShift");
        var data = workbook.AddSheet("Data");
        for (uint row = 1; row <= 4; row++)
        {
            data.SetCell(new CellAddress(data.Id, row, 1), new TextValue("r" + row));
            data.SetCell(new CellAddress(data.Id, row, 2), new NumberValue(row));
        }

        var dashboard = workbook.AddSheet("Dashboard");
        dashboard.Charts.Add(new ChartModel
        {
            Name = "Chart 1",
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 4, 2)),
            Hyperlink = new DrawingObjectHyperlink("Dashboard!$A$20"),
        });
        dashboard.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Rectangle 1",
            Anchor = new CellAddress(dashboard.Id, 8, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 200,
            Height = 100,
            Hyperlink = new DrawingObjectHyperlink("Dashboard!$A$20", TargetMode: null, Tooltip: "jump"),
        });
        dashboard.TextBoxes.Add(new TextBoxModel
        {
            Name = "TextBox 1",
            Anchor = new CellAddress(dashboard.Id, 12, 2),
            Text = "hello",
            Hyperlink = new DrawingObjectHyperlink("Dashboard!$A$20"),
        });
        dashboard.Pictures.Add(new PictureModel
        {
            Name = "Picture 1",
            Anchor = new CellAddress(dashboard.Id, 16, 2),
            Kind = PictureKind.Image,
            ImageBytes = PngBytes,
            ContentType = "image/png",
            Hyperlink = new DrawingObjectHyperlink("Dashboard!$A$20"),
        });
        return workbook;
    }

    private static void AssertAllFourTargets(Sheet sheet, string expectedTarget, string because)
    {
        sheet.Charts.Should().ContainSingle(because).Which
            .Hyperlink!.Target.Should().Be(expectedTarget, because);
        sheet.DrawingShapes.Should().ContainSingle(because).Which
            .Hyperlink!.Target.Should().Be(expectedTarget, because);
        sheet.TextBoxes.Should().ContainSingle(because).Which
            .Hyperlink!.Target.Should().Be(expectedTarget, because);
        sheet.Pictures.Should().ContainSingle(because).Which
            .Hyperlink!.Target.Should().Be(expectedTarget, because);
    }

    private static Workbook SaveAndReload(XlsxFileAdapter adapter, Workbook workbook)
    {
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        return adapter.Load(saved);
    }
}
