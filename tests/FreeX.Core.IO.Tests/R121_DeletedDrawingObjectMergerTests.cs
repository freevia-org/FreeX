using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R121 (round 111 backlog, linked pair): deleting a picture/text box/shape/chart that was ever loaded
/// from the opened .xlsx must not reappear on the next save+reload. Before this fix,
/// <c>XlsxWorksheetDrawingObjectWriter.GetRewrittenSourceObjectNames</c> only reported names of objects
/// STILL PRESENT in <c>sheet.Pictures</c>/<c>TextBoxes</c>/<c>DrawingShapes</c> whose <c>IsSourceLoaded</c>
/// flag had been cleared by an edit -- an object removed OUTRIGHT (via the new
/// <see cref="DeleteDrawingObjectCommand"/>) appeared in neither the kept nor the superseded set, so
/// <c>XlsxWorksheetDrawingPartMerger.MergeDrawingPart</c> copied its original anchor (and, for a picture,
/// its image relationship) straight back in from the untouched source package on the very next full save.
/// <para>
/// ROUND-TRIP FIXTURE RULE: every fixture here is built by saving a workbook with the REAL
/// <see cref="XlsxFileAdapter"/> writer and reloading it, so the object under test is genuinely
/// <c>IsSourceLoaded == true</c> before it is deleted -- never a hand-authored drawing XML fragment.
/// </para>
/// </summary>
public sealed class R121_DeletedDrawingObjectMergerTests
{
    [Fact]
    public void DeleteSourceLoadedPicture_SaveAndReload_DoesNotReappear()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("DeletePicture");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var insert = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 2, 2), CreatePngBytes(), "image/png");
        insert.Apply(ctx).Success.Should().BeTrue();

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var picture = loadedSheet.Pictures.Should().ContainSingle().Which;
        picture.IsSourceLoaded.Should().BeTrue("a plain reloaded picture starts source-loaded");

        var deleteCommand = new DeleteDrawingObjectCommand(loadedSheet.Id, SelectionPaneObjectKind.Picture, picture.Id);
        deleteCommand.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        loadedSheet.Pictures.Should().BeEmpty("the delete command must remove the picture from the live model");

        using var deletedSave = new MemoryStream();
        adapter.Save(loaded, deletedSave);

        deletedSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(deletedSave);
        reloaded.GetSheet("Sheet1")!.Pictures.Should().BeEmpty(
            "a deleted source-loaded picture must not be merged back in from the untouched source package");
    }

    [Fact]
    public void DeleteSourceLoadedTextBox_SaveAndReload_DoesNotReappear()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("DeleteTextBox");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var insert = new AddTextBoxCommand(sheet.Id, new CellAddress(sheet.Id, 1, 1), "Warning");
        insert.Apply(ctx).Success.Should().BeTrue();

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var textBox = loadedSheet.TextBoxes.Should().ContainSingle().Which;
        textBox.IsSourceLoaded.Should().BeTrue();

        var deleteCommand = new DeleteDrawingObjectCommand(loadedSheet.Id, SelectionPaneObjectKind.TextBox, textBox.Id);
        deleteCommand.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        loadedSheet.TextBoxes.Should().BeEmpty();

        using var deletedSave = new MemoryStream();
        adapter.Save(loaded, deletedSave);

        deletedSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(deletedSave);
        reloaded.GetSheet("Sheet1")!.TextBoxes.Should().BeEmpty(
            "a deleted source-loaded text box must not be merged back in from the untouched source package");
    }

    [Fact]
    public void DeleteSourceLoadedShape_SaveAndReload_DoesNotReappear()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("DeleteShape");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var insert = new AddDrawingShapeCommand(sheet.Id, new CellAddress(sheet.Id, 3, 3), DrawingShapeKind.Rectangle, 100, 60);
        insert.Apply(ctx).Success.Should().BeTrue();

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var shape = loadedSheet.DrawingShapes.Should().ContainSingle().Which;
        shape.IsSourceLoaded.Should().BeTrue();

        var deleteCommand = new DeleteDrawingObjectCommand(loadedSheet.Id, SelectionPaneObjectKind.Shape, shape.Id);
        deleteCommand.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        loadedSheet.DrawingShapes.Should().BeEmpty();

        using var deletedSave = new MemoryStream();
        adapter.Save(loaded, deletedSave);

        deletedSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(deletedSave);
        reloaded.GetSheet("Sheet1")!.DrawingShapes.Should().BeEmpty(
            "a deleted source-loaded shape must not be merged back in from the untouched source package");
    }

    [Fact]
    public void DeleteChart_SaveAndReload_DoesNotReappear()
    {
        // Charts have no IsSourceLoaded flag (XlsxWorksheetChartWriter always fully rewrites every
        // supported-type chart still present in sheet.Charts), but the merger's supersede check reads
        // a source anchor's cNvPr@name generically -- so a deleted chart's original graphicFrame is
        // exactly as vulnerable to resurrection as a picture's anchor is.
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("DeleteChart");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 0, 0), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 0, 1), new TextValue("Series"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 0), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 0), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        var ctx = new TestCommandContext(workbook);
        var dataRange = new GridRange(new CellAddress(sheet.Id, 0, 0), new CellAddress(sheet.Id, 2, 1));
        var insert = new AddChartCommand(sheet.Id, dataRange, ChartType.Column, "Chart 1");
        insert.Apply(ctx).Success.Should().BeTrue();

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var chart = loadedSheet.Charts.Should().ContainSingle().Which;

        var deleteCommand = new DeleteDrawingObjectCommand(loadedSheet.Id, SelectionPaneObjectKind.Chart, chart.Id);
        deleteCommand.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        loadedSheet.Charts.Should().BeEmpty();

        using var deletedSave = new MemoryStream();
        adapter.Save(loaded, deletedSave);

        deletedSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(deletedSave);
        reloaded.GetSheet("Sheet1")!.Charts.Should().BeEmpty(
            "a deleted chart must not be merged back in from the untouched source package");
    }

    [Fact]
    public void UndoDelete_RestoresPictureAndItsSaveRoundTrip()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("UndoDeletePicture");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var insert = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 2, 2), CreatePngBytes(), "image/png");
        insert.Apply(ctx).Success.Should().BeTrue();

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var picture = loadedSheet.Pictures.Should().ContainSingle().Which;
        var loadedCtx = new TestCommandContext(loaded);

        var deleteCommand = new DeleteDrawingObjectCommand(loadedSheet.Id, SelectionPaneObjectKind.Picture, picture.Id);
        deleteCommand.Apply(loadedCtx).Success.Should().BeTrue();
        loadedSheet.Pictures.Should().BeEmpty();
        loadedSheet.DeletedSourceDrawingObjectNames.Should().Contain(picture.Name!);

        deleteCommand.Revert(loadedCtx);
        loadedSheet.Pictures.Should().ContainSingle().Which.Id.Should().Be(picture.Id);
        loadedSheet.DeletedSourceDrawingObjectNames.Should().NotContain(picture.Name!);

        using var restoredSave = new MemoryStream();
        adapter.Save(loaded, restoredSave);

        restoredSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(restoredSave);
        reloaded.GetSheet("Sheet1")!.Pictures.Should().ContainSingle(
            "undoing the delete must restore the picture such that it still round-trips on the next save");
    }

    private static byte[] CreatePngBytes()
    {
        // Minimal valid 1x1 transparent PNG.
        return
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
    }
}
