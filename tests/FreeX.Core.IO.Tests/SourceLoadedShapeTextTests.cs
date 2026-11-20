using FluentAssertions;
using Free.Shared.Drawing;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO.Tests;

public sealed class SourceLoadedShapeTextTests
{
    [Fact]
    public void EmptyRichDocument_WritesRequiredParagraph()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(sheet.Id, 1, 1),
            ShapeTextDocument = new DrawingShapeTextDocument(Array.Empty<DrawingShapeTextParagraph>()) });
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var drawing = archive.Entries.Single(e => e.FullName == "xl/drawings/drawing1.xml").Open();
        var xml = XDocument.Load(drawing);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        xml.Descendants(xdr + "txBody").Single().Elements(a + "p").Should().ContainSingle();
    }

    [Fact]
    public void AuthoredRichHyperlinkAndThemeColor_SurviveNativeAndXlsx()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var style = new DrawingShapeTextRunStyle(ThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
            Hyperlink: new DrawingObjectHyperlink("https://example.org/", "External", "tip"));
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(sheet.Id, 1, 1),
            ShapeTextDocument = new DrawingShapeTextDocument(new[] { new DrawingShapeTextParagraph(new[] { new DrawingShapeTextInline(DrawingShapeTextInlineKind.Run, "link", style: style) }) }) });
        using var native = new MemoryStream();
        var nativeAdapter = new NativeJsonAdapter();
        nativeAdapter.Save(workbook, native);
        native.Position = 0;
        using var result = new MemoryStream();
        var xlsx = new XlsxFileAdapter();
        xlsx.Save(nativeAdapter.Load(native), result);
        result.Position = 0;
        var actual = xlsx.Load(result).Sheets.Single().DrawingShapes.Single().ShapeTextDocument!.Paragraphs.Single().Inlines.Single().Style!;
        actual.Hyperlink.Should().Be(style.Hyperlink);
        actual.ThemeColor.Should().Be(style.ThemeColor);
    }

    [Fact]
    public void LinePresetInShapeElement_RetainsTextThroughEditsAndRegeneration()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Line shape", Anchor = new CellAddress(sheet.Id, 1, 1), ShapeText = "Before" });
        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        using (var zip = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("xl/drawings/drawing1.xml")!;
            XDocument drawing;
            using (var input = entry.Open()) drawing = XDocument.Load(input);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            drawing.Descendants(a + "prstGeom").Single().SetAttributeValue("prst", "line");
            entry.Delete();
            using var output = zip.CreateEntry("xl/drawings/drawing1.xml").Open();
            drawing.Save(output);
        }
        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.Sheets.Single();
        var shape = loadedSheet.DrawingShapes.Single();
        shape.Kind.Should().Be(DrawingShapeKind.Line);
        shape.ShapeText.Should().Be("Before");
        new SetDrawingShapeTextCommand(loadedSheet.Id, shape.Id, "After").Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        shape.IsSourceLoaded = false;
        using var native = new MemoryStream();
        var nativeAdapter = new NativeJsonAdapter();
        nativeAdapter.Save(loaded, native);
        native.Position = 0;
        using var saved = new MemoryStream();
        adapter.Save(nativeAdapter.Load(native), saved);
        saved.Position = 0;
        adapter.Load(saved).Sheets.Single().DrawingShapes.Single().ShapeText.Should().Be("After");
    }

    [Fact]
    public void ConnectorElementWithNonLinePreset_RetainsConnectorIdentity()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Connector", Kind = DrawingShapeKind.Line, Anchor = new CellAddress(sheet.Id, 1, 1) });
        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        using (var zip = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("xl/drawings/drawing1.xml")!;
            XDocument drawing;
            using (var input = entry.Open()) drawing = XDocument.Load(input);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            drawing.Descendants(a + "prstGeom").Single().SetAttributeValue("prst", "rect");
            entry.Delete();
            using var output = zip.CreateEntry("xl/drawings/drawing1.xml").Open();
            drawing.Save(output);
        }
        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.Sheets.Single();
        var shape = loadedSheet.DrawingShapes.Single();
        shape.Kind.Should().Be(DrawingShapeKind.Rectangle);
        var context = new TestCommandContext(loaded);
        new SetDrawingShapeTextCommand(loadedSheet.Id, shape.Id, "No").Apply(context).Success.Should().BeFalse();
        new SetDrawingShapeTextInsetsCommand(loadedSheet.Id, shape.Id, new DrawingShapeTextInsets(0, null, null, null)).Apply(context).Success.Should().BeFalse();
        new DuplicateSheetCommand(loadedSheet.Id).Apply(context).Success.Should().BeTrue();
        shape.IsSourceLoaded = false;
        using var native = new MemoryStream();
        var nativeAdapter = new NativeJsonAdapter();
        nativeAdapter.Save(loaded, native);
        native.Position = 0;
        using var saved = new MemoryStream();
        adapter.Save(nativeAdapter.Load(native), saved);
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        XNamespace xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var drawings = archive.Entries.Where(entry => entry.FullName.StartsWith("xl/drawings/drawing", StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", StringComparison.Ordinal)).ToList();
        drawings.Should().HaveCount(2);
        foreach (var drawing in drawings)
        {
            using var input = drawing.Open();
            var xml = XDocument.Load(input);
            xml.Descendants(xdr + "cxnSp").Should().ContainSingle();
            xml.Descendants(xdr + "sp").Should().BeEmpty();
        }
    }

    [Fact]
    public void RichHyperlink_SurvivesNativeRoundtripAndDrawingRegeneration()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(sheet.Id, 1, 1),
            ShapeText = "linked", Hyperlink = new DrawingObjectHyperlink("https://example.org/", "External", null) });
        var xlsx = new XlsxFileAdapter();
        using var source = new MemoryStream();
        xlsx.Save(workbook, source);
        using (var zip = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("xl/drawings/drawing1.xml")!;
            XDocument drawing;
            using (var input = entry.Open()) drawing = XDocument.Load(input);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var click = drawing.Descendants(a + "hlinkClick").Single();
            var run = drawing.Descendants(a + "r").First();
            var firstClick = new XElement(click);
            firstClick.SetAttributeValue("tooltip", "first");
            run.Element(a + "rPr")!.Add(firstClick);
            var secondRun = new XElement(run);
            secondRun.Descendants(a + "hlinkClick").Single().SetAttributeValue("tooltip", "second");
            run.AddAfterSelf(secondRun);
            entry.Delete();
            using var output = zip.CreateEntry("xl/drawings/drawing1.xml").Open();
            drawing.Save(output);
        }
        source.Position = 0;
        var loaded = xlsx.Load(source);
        var loadedSheet = loaded.Sheets.Single();
        var loadedShape = loadedSheet.DrawingShapes.Single();
        loadedShape.ShapeTextDocument!.Paragraphs.Single().Inlines.First().Style!.Hyperlink!.Target.Should().Be("https://example.org/");
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 8, 1), new TextValue("cell edit"));
        using (var cellSave = new MemoryStream()) xlsx.Save(loaded, cellSave);
        xlsx.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, "the existing drawing guard conservatively rejects external relationships");
        loadedSheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Force full save", Anchor = new CellAddress(loadedSheet.Id, 3, 1) });
        loadedShape.ShapeTextInsets = new DrawingShapeTextInsets(9525, null, null, null);
        using (var first = new MemoryStream()) xlsx.Save(loaded, first);
        var edit = new SetDrawingShapeTextCommand(loadedSheet.Id, loadedShape.Id, "edited");
        var context = new TestCommandContext(loaded);
        edit.Apply(context).Success.Should().BeTrue();
        using (var edited = new MemoryStream()) xlsx.Save(loaded, edited);
        edit.Revert(context);
        using (var undone = new MemoryStream())
        {
            xlsx.Save(loaded, undone);
            undone.Position = 0;
            var restoredShape = xlsx.Load(undone).Sheets.Single().DrawingShapes.Single(shape => shape.Name == "Shape");
            restoredShape.ShapeTextInsets!.Left.Should().Be(9525);
            restoredShape.ShapeTextDocument!.Paragraphs.Single().Inlines.First().Style!.Hyperlink!.Target.Should().Be("https://example.org/");
        }
        using var native = new MemoryStream();
        var nativeAdapter = new NativeJsonAdapter();
        nativeAdapter.Save(loaded, native);
        native.Position = 0;
        var restored = nativeAdapter.Load(native);
        using var result = new MemoryStream();
        xlsx.Save(restored, result);
        result.Position = 0;
        var runs = xlsx.Load(result).Sheets.Single().DrawingShapes.Single(shape => shape.Name == "Shape").ShapeTextDocument!.Paragraphs.Single().Inlines;
        var link = runs.First().Style!.Hyperlink;
        link!.Target.Should().Be("https://example.org/");
        runs.Select(run => run.Style!.Hyperlink!.Tooltip).Should().Equal("first", "second");

        new DuplicateSheetCommand(loadedSheet.Id).Apply(context).Success.Should().BeTrue();
        using var duplicated = new MemoryStream();
        xlsx.Save(loaded, duplicated);
        duplicated.Position = 0;
        var copiedSheet = xlsx.Load(duplicated).Sheets.Single(sheet => sheet.Name != loadedSheet.Name);
        copiedSheet.DrawingShapes.Single(shape => shape.Name == "Shape").ShapeTextDocument!.Paragraphs.Single().Inlines.First().Style!.Hyperlink!.Target.Should().Be("https://example.org/");

        using var otherSource = new MemoryStream();
        otherSource.Write(source.ToArray());
        using (var zip = new ZipArchive(otherSource, ZipArchiveMode.Update, leaveOpen: true))
        {
            var relEntry = zip.GetEntry("xl/drawings/_rels/drawing1.xml.rels")!;
            XDocument rels;
            using (var input = relEntry.Open()) rels = XDocument.Load(input);
            rels.Root!.Elements().Single().SetAttributeValue("Target", "https://other.example/");
            relEntry.Delete();
            using var output = zip.CreateEntry("xl/drawings/_rels/drawing1.xml.rels").Open();
            rels.Save(output);
        }
        otherSource.Position = 0;
        loadedShape.ShapeTextDocument = xlsx.Load(otherSource).Sheets.Single().DrawingShapes.Single().ShapeTextDocument;
        using var retargeted = new MemoryStream();
        xlsx.Save(loaded, retargeted);
        retargeted.Position = 0;
        xlsx.Load(retargeted).Sheets.Single(sheet => sheet.Name == loadedSheet.Name).DrawingShapes.Single(shape => shape.Name == "Shape")
            .ShapeTextDocument!.Paragraphs.Single().Inlines.First().Style!.Hyperlink!.Target.Should().Be("https://other.example/");

        using var opaqueSource = new MemoryStream();
        opaqueSource.Write(source.ToArray());
        using (var zip = new ZipArchive(opaqueSource, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("xl/drawings/drawing1.xml")!;
            XDocument drawing;
            using (var input = entry.Open()) drawing = XDocument.Load(input);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace custom = "urn:freex:test";
            var id = drawing.Descendants(a + "hlinkClick").First().Attribute(r + "id")!.Value;
            drawing.Descendants(a + "rPr").First().Add(new XElement(custom + "opaque", new XAttribute(r + "embed", id)));
            entry.Delete();
            using var output = zip.CreateEntry("xl/drawings/drawing1.xml").Open();
            drawing.Save(output);
        }
        opaqueSource.Position = 0;
        var opaqueWorkbook = xlsx.Load(opaqueSource);
        var opaqueSheet = opaqueWorkbook.Sheets.Single();
        var opaqueShape = opaqueSheet.DrawingShapes.Single();
        using (var untouched = new MemoryStream(new byte[] { 1, 2, 3 }, writable: true))
        {
            Action nativeSave = () => new NativeJsonAdapter().Save(opaqueWorkbook, untouched);
            nativeSave.Should().Throw<InvalidOperationException>().WithMessage("*opaque shape text relationships*");
            untouched.ToArray().Should().Equal(1, 2, 3);
        }
        opaqueSheet.SetCell(new CellAddress(opaqueSheet.Id, 8, 1), new TextValue("cell edit"));
        using (var cellSave = new MemoryStream()) xlsx.Save(opaqueWorkbook, cellSave);
        xlsx.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, "the existing drawing guard conservatively rejects external relationships");
        opaqueSheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Force full save", Anchor = new CellAddress(opaqueSheet.Id, 3, 1) });
        using (var first = new MemoryStream()) xlsx.Save(opaqueWorkbook, first);
        using (var second = new MemoryStream()) xlsx.Save(opaqueWorkbook, second);
        loadedShape.ShapeTextDocument = opaqueShape.ShapeTextDocument;
        Action transfer = () => { using var output = new MemoryStream(); xlsx.Save(loaded, output); };
        transfer.Should().Throw<InvalidOperationException>().WithMessage("*relationships*cannot be safely regenerated*");

        using var unrelatedSource = new MemoryStream();
        unrelatedSource.Write(source.ToArray());
        using (var zip = new ZipArchive(unrelatedSource, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("xl/drawings/_rels/drawing1.xml.rels")!;
            XDocument rels;
            using (var input = entry.Open()) rels = XDocument.Load(input);
            XNamespace package = "http://schemas.openxmlformats.org/package/2006/relationships";
            rels.Root!.Add(new XElement(package + "Relationship", new XAttribute("Id", "rIdOther"),
                new XAttribute("Type", "urn:freex:test:external"), new XAttribute("Target", "https://other.example/"),
                new XAttribute("TargetMode", "External")));
            entry.Delete();
            using var output = zip.CreateEntry("xl/drawings/_rels/drawing1.xml.rels").Open();
            rels.Save(output);
        }
        unrelatedSource.Position = 0;
        var unrelated = xlsx.Load(unrelatedSource);
        var unrelatedSheet = unrelated.Sheets.Single();
        unrelatedSheet.SetCell(new CellAddress(unrelatedSheet.Id, 8, 1), new TextValue("cell edit"));
        using (var output = new MemoryStream()) xlsx.Save(unrelated, output);
        xlsx.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
    }
    [Fact]
    public void TextEditSaveUndoAndRedo_SurviveSourcePackageFastPaths()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(sheet.Id, 1, 1), ShapeText = "Before" });
        var adapter = new XlsxFileAdapter();
        using var original = new MemoryStream();
        adapter.Save(workbook, original);
        original.Position = 0;
        var loaded = adapter.Load(original);
        var loadedSheet = loaded.Sheets.Single();
        var shape = loadedSheet.DrawingShapes.Single();
        shape.IsSourceLoaded.Should().BeTrue();
        var context = new TestCommandContext(loaded);
        var command = new SetDrawingShapeTextCommand(loadedSheet.Id, shape.Id, "After\nEdit");
        command.Apply(context).Success.Should().BeTrue();
        SaveAndRead(loaded, adapter).Should().Be("After\nEdit");
        command.Revert(context);
        SaveAndRead(loaded, adapter).Should().Be("Before");
        command.Apply(context).Success.Should().BeTrue();
        SaveAndRead(loaded, adapter).Should().Be("After\nEdit");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Insets_PreserveAbsentAndExplicitZeroThroughSourceAndRegeneration(bool regenerate)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(sheet.Id, 1, 1),
            ShapeText = "Label", ShapeTextInsets = new DrawingShapeTextInsets(0, 19050, null, 38100) });
        var adapter = new XlsxFileAdapter();
        using var original = new MemoryStream();
        adapter.Save(workbook, original);
        original.Position = 0;
        var loaded = adapter.Load(original);
        var shape = loaded.Sheets.Single().DrawingShapes.Single();
        shape.ShapeTextInsets.Should().Be(new DrawingShapeTextInsets(0, 19050, null, 38100));
        var context = new TestCommandContext(loaded);
        var command = new SetDrawingShapeTextInsetsCommand(loaded.Sheets.Single().Id, shape.Id, new DrawingShapeTextInsets(9525, null, 0, 38100));
        command.Apply(context).Success.Should().BeTrue();
        command.Revert(context);
        shape.ShapeTextInsets.Should().Be(new DrawingShapeTextInsets(0, 19050, null, 38100));
        command.Apply(context).Success.Should().BeTrue();
        if (regenerate) shape.IsSourceLoaded = false;
        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        var result = new XlsxFileAdapter().Load(saved).Sheets.Single().DrawingShapes.Single();
        result.ShapeTextInsets.Should().Be(new DrawingShapeTextInsets(9525, null, 0, 38100));
        using var native = new MemoryStream();
        var nativeAdapter = new NativeJsonAdapter();
        nativeAdapter.Save(loaded, native);
        native.Position = 0;
        nativeAdapter.Load(native).Sheets.Single().DrawingShapes.Single().ShapeTextInsets.Should().Be(result.ShapeTextInsets);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConnectorWithTextOrInsets_IsRejectedInsteadOfSilentlyLosingData(bool sourceLoaded, bool insets)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Connector", Kind = DrawingShapeKind.Line, Anchor = new CellAddress(sheet.Id, 1, 1) });
        var adapter = new XlsxFileAdapter();
        if (sourceLoaded)
        {
            using var source = new MemoryStream();
            adapter.Save(workbook, source);
            source.Position = 0;
            workbook = adapter.Load(source);
            sheet = workbook.Sheets.Single();
        }
        var shape = sheet.DrawingShapes.Single();
        if (insets) shape.ShapeTextInsets = new DrawingShapeTextInsets(0, null, null, null);
        else shape.ShapeText = "not supported";
        Action save = () => { using var output = new MemoryStream(); adapter.Save(workbook, output); };
        save.Should().Throw<InvalidOperationException>().WithMessage("*Connector shapes cannot contain text*");
    }

    private static string? SaveAndRead(Workbook workbook, XlsxFileAdapter adapter)
    {
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream).Sheets.Single().DrawingShapes.Single().ShapeText;
    }
}
