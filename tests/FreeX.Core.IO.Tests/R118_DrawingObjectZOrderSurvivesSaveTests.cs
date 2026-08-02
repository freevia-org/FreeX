using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R118-io-drawing-zorder-1 regression: <c>BringDrawingShapeForwardCommand</c>/
/// <c>SendDrawingShapeBackwardCommand</c> (<c>DrawingShapeZOrderCommands.cs</c>) and
/// <c>MoveSelectionPaneObjectCommand</c> (<c>SelectionPaneCommands.cs</c>) only ever mutate
/// <see cref="Sheet.DrawingObjectZOrder"/> -- neither clears the moved object's
/// <c>IsSourceLoaded</c> flag, which is the ordinary (unedited) state for most objects reloaded from
/// an .xlsx. <c>XlsxWorksheetDrawingObjectWriter</c>'s z-order-aware anchor loop only ever walks its
/// <c>!IsSourceLoaded</c>-filtered pictures/textBoxes/shapes lists, so a still-source-loaded object never
/// reaches it; its ORIGINAL anchor was instead copied verbatim, in original source document order, by
/// <c>XlsxWorksheetDrawingPartMerger.MergeDrawingPart</c> -- silently discarding a Bring Forward/Send
/// Backward/Selection Pane reorder on save. The fix teaches that merger to reorder the top-level anchors
/// it assembles to match the sheet's current z-order.
/// <para>
/// These are full <c>Save()</c> -&gt; command -&gt; <c>Save()</c> -&gt; <c>Load()</c> round-trips through the
/// REAL adapter and REAL commands (never a hand-authored drawing-part fixture, and never an assertion on
/// the in-memory model alone) -- exactly the scenario the defect describes: an ordinary, unedited picture
/// (or shape) whose z-order is changed via the real command, then saved and reopened.
/// </para>
/// </summary>
public sealed class R118_DrawingObjectZOrderSurvivesSaveTests
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    [Fact]
    public void BringPictureForward_OnTwoOrdinarySourceLoadedPictures_SurvivesSaveAndReload()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("ZOrderPictures");
        var sheet = workbook.AddSheet("Sheet1");
        AddPicture(sheet, "PicA", row: 2);
        AddPicture(sheet, "PicB", row: 6);

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        // Reload: both pictures come back IsSourceLoaded == true (the ordinary, unedited state for a
        // just-opened workbook), in their original document order [PicA, PicB].
        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        loadedSheet.Pictures.Select(p => p.Name).Should().Equal("PicA", "PicB");
        loadedSheet.Pictures.Should().OnlyContain(p => p.IsSourceLoaded,
            "a plain reloaded picture starts source-loaded -- the normal state this defect targets");
        var picA = loadedSheet.Pictures.Single(p => p.Name == "PicA");

        // The REAL command a Selection Pane / ribbon "Bring Forward" click dispatches.
        var command = new MoveSelectionPaneObjectCommand(loadedSheet.Id, SelectionPaneObjectKind.Picture, picA.Id, forward: true);
        command.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        picA.IsSourceLoaded.Should().BeTrue(
            "the command must not need to clear IsSourceLoaded for the fix to take effect");

        using var reorderedSave = new MemoryStream();
        adapter.Save(loaded, reorderedSave);

        // Saved drawing part: PicB's anchor must now precede PicA's (PicA was brought to the front /
        // end of document order).
        ReadPictureAnchorNamesInDocumentOrder(reorderedSave).Should().Equal(["PicB", "PicA"],
            "Bring Forward must be reflected in the saved anchor order even though neither picture was edited");

        // Reopen the saved file through the real adapter: the reorder must survive a full round trip,
        // not just be visible in the raw XML.
        reorderedSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(reorderedSave);
        reloaded.GetSheet("Sheet1")!.Pictures.Select(p => p.Name).Should().Equal(["PicB", "PicA"],
            "reopening the file must show the new stacking order, not revert to the original one");
    }

    [Fact]
    public void SendShapeBackward_OnTwoOrdinarySourceLoadedShapes_SurvivesSaveAndReload()
    {
        // Sibling coverage for the OTHER named entry point in the defect
        // (DrawingShapeZOrderCommands.cs's SendDrawingShapeBackwardCommand), built through the real
        // AddDrawingShapeCommand rather than constructing DrawingShapeModel by hand.
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("ZOrderShapes");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        new AddDrawingShapeCommand(sheet.Id, new CellAddress(sheet.Id, 2, 2), DrawingShapeKind.Rectangle, 100, 60)
            .Apply(ctx).Success.Should().BeTrue();
        new AddDrawingShapeCommand(sheet.Id, new CellAddress(sheet.Id, 8, 2), DrawingShapeKind.Ellipse, 100, 60)
            .Apply(ctx).Success.Should().BeTrue();
        sheet.DrawingShapes[0].Name = "ShapeA";
        sheet.DrawingShapes[1].Name = "ShapeB";

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        loadedSheet.DrawingShapes.Select(s => s.Name).Should().Equal("ShapeA", "ShapeB");
        loadedSheet.DrawingShapes.Should().OnlyContain(s => s.IsSourceLoaded);
        var shapeB = loadedSheet.DrawingShapes.Single(s => s.Name == "ShapeB");

        // The REAL command a ribbon/right-click "Send Backward" dispatches.
        var command = new SendDrawingShapeBackwardCommand(loadedSheet.Id, shapeB.Id);
        command.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        shapeB.IsSourceLoaded.Should().BeTrue();

        using var reorderedSave = new MemoryStream();
        adapter.Save(loaded, reorderedSave);

        reorderedSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(reorderedSave);
        reloaded.GetSheet("Sheet1")!.DrawingShapes.Select(s => s.Name).Should().Equal(["ShapeB", "ShapeA"],
            "Send Backward must be reflected in the saved anchor order even though neither shape was edited");
    }

    [Fact]
    public void UntouchedSourceLoadedPictures_KeepOriginalOrder_NoRegression()
    {
        // No-regression sibling: when no reorder command has ever run (sheet.DrawingObjectZOrder is
        // empty, the ordinary case for the vast majority of saves), the new reorder pass in
        // XlsxWorksheetDrawingPartMerger must be a complete no-op -- the original document order survives
        // exactly as it did before this fix.
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("ZOrderUntouched");
        var sheet = workbook.AddSheet("Sheet1");
        AddPicture(sheet, "PicA", row: 2);
        AddPicture(sheet, "PicB", row: 6);
        AddPicture(sheet, "PicC", row: 10);

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        loadedSheet.DrawingObjectZOrder.Should().BeEmpty();

        // An unrelated edit on one picture (touching Width) -- must not trigger any reordering.
        loadedSheet.Pictures.Single(p => p.Name == "PicB").Width = 321;

        using var resave = new MemoryStream();
        adapter.Save(loaded, resave);

        ReadPictureAnchorNamesInDocumentOrder(resave).Should().Equal("PicA", "PicB", "PicC");
    }

    private static void AddPicture(Sheet sheet, string name, uint row) =>
        sheet.Pictures.Add(new PictureModel
        {
            Name = name,
            Anchor = new CellAddress(sheet.Id, row, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64
        });

    private static List<string> ReadPictureAnchorNamesInDocumentOrder(MemoryStream packageStream)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/drawings/drawing1.xml")!;
        using var reader = new StreamReader(entry.Open());
        var drawingXml = XDocument.Parse(reader.ReadToEnd());

        return drawingXml.Root!.Elements()
            .Select(anchor => anchor.Descendants(SpreadsheetDrawingNs + "cNvPr").FirstOrDefault()?.Attribute("name")?.Value)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();
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
