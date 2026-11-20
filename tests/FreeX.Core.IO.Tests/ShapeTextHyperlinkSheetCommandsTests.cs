using FluentAssertions;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class ShapeTextHyperlinkSheetCommandsTests
{
    private static DrawingShapeTextRunStyle Style(string target, string? mode = null) =>
        new(Hyperlink: new DrawingObjectHyperlink(target, mode, "tip"));

    private static DrawingShapeTextDocument CreateDocument() => new(
        [new DrawingShapeTextParagraph(
            [new(DrawingShapeTextInlineKind.Run, "Internal", style: Style("Data!A1")),
             new(DrawingShapeTextInlineKind.Run, "External", style: Style("https://example.org/Data!A1", "External")),
             new(DrawingShapeTextInlineKind.Run, "Other", style: Style("Other!A1"))],
            defaultRunStyle: Style("Data!A1"))],
        new DrawingShapeTextParagraphDefaults(RunStyle: Style("Data!A1")),
        [new DrawingShapeTextListLevel(0, new DrawingShapeTextParagraphDefaults(RunStyle: Style("Data!A1")))]);

    private static void AssertLinks(DrawingShapeTextDocument document, string expected)
    {
        document.DefaultParagraph!.RunStyle!.Hyperlink!.Target.Should().Be(expected);
        document.ListLevels.Single().Defaults.RunStyle!.Hyperlink!.Target.Should().Be(expected);
        document.Paragraphs.Single().DefaultRunStyle!.Hyperlink!.Target.Should().Be(expected);
        document.Paragraphs.Single().Inlines[0].Style!.Hyperlink!.Target.Should().Be(expected);
        document.Paragraphs.Single().Inlines[1].Style!.Hyperlink!.Target.Should().Be("https://example.org/Data!A1");
        document.Paragraphs.Single().Inlines[2].Style!.Hyperlink!.Target.Should().Be("Other!A1");
    }

    [Fact]
    public void RenameAndDelete_RewriteEveryTextLink_AndUndoRestoresOriginalDocument()
    {
        var workbook = new Workbook("test");
        var host = workbook.AddSheet("Host");
        var data = workbook.AddSheet("Data");
        var original = CreateDocument();
        var shape = new DrawingShapeModel { Anchor = new CellAddress(host.Id, 1, 1), ShapeTextDocument = original };
        host.DrawingShapes.Add(shape);
        var context = new TestCommandContext(workbook);
        var rename = new RenameSheetCommand(data.Id, "New Data");
        rename.Apply(context).Success.Should().BeTrue();
        AssertLinks(shape.ShapeTextDocument!, "'New Data'!A1");
        AssertLinks(original, "Data!A1");
        rename.Revert(context);
        shape.ShapeTextDocument.Should().BeSameAs(original);
        rename.Apply(context).Success.Should().BeTrue();
        AssertLinks(shape.ShapeTextDocument!, "'New Data'!A1");
        rename.Revert(context);
        var delete = new RemoveSheetCommand(data.Id);
        delete.Apply(context).Success.Should().BeTrue();
        AssertLinks(shape.ShapeTextDocument!, "#REF!");
        delete.Revert(context);
        shape.ShapeTextDocument.Should().BeSameAs(original);
        delete.Apply(context).Success.Should().BeTrue();
        AssertLinks(shape.ShapeTextDocument!, "#REF!");
        delete.Revert(context);
    }

    [Fact]
    public void Duplicate_RebasesOnlySameSheetTextLinks_AndKeepsOriginal()
    {
        var workbook = new Workbook("test");
        var data = workbook.AddSheet("Data");
        var original = CreateDocument();
        data.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(data.Id, 1, 1), ShapeTextDocument = original });
        var context = new TestCommandContext(workbook);
        var duplicate = new DuplicateSheetCommand(data.Id);
        duplicate.Apply(context).Success.Should().BeTrue();
        var copy = workbook.Sheets.Last();
        AssertLinks(copy.DrawingShapes.Single().ShapeTextDocument!, $"'{copy.Name}'!A1");
        AssertLinks(original, "Data!A1");
        duplicate.Revert(context);
        workbook.Sheets.Should().ContainSingle();
    }

    [Fact]
    public void SourceLoadedDuplicate_RebindsRichLinksInTheNewDrawingPart()
    {
        var workbook = new Workbook("test");
        var data = workbook.AddSheet("Data");
        data.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(data.Id, 1, 1), ShapeTextDocument = CreateDocument() });
        var xlsx = new XlsxFileAdapter();
        using var source = new MemoryStream();
        xlsx.Save(workbook, source);
        source.Position = 0;
        var loaded = xlsx.Load(source);
        var loadedSheet = loaded.Sheets.Single();
        new DuplicateSheetCommand(loadedSheet.Id).Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        var copyName = loaded.Sheets.Last().Name;
        using var saved = new MemoryStream();
        xlsx.Save(loaded, saved);
        saved.Position = 0;
        var reloaded = xlsx.Load(saved);
        AssertLinks(reloaded.Sheets.First().DrawingShapes.Single().ShapeTextDocument!, "Data!A1");
        AssertLinks(reloaded.Sheets.Last().DrawingShapes.Single().ShapeTextDocument!, $"'{copyName}'!A1");
    }

    [Fact]
    public void SourceLoadedDelete_RetainsRefErrorThroughXlsxAndUndo()
    {
        var workbook = new Workbook("test");
        var host = workbook.AddSheet("Host");
        workbook.AddSheet("Data");
        host.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(host.Id, 1, 1), ShapeTextDocument = CreateDocument() });
        var xlsx = new XlsxFileAdapter();
        using var source = new MemoryStream();
        xlsx.Save(workbook, source);
        source.Position = 0;
        var loaded = xlsx.Load(source);
        var loadedShape = loaded.Sheets.First().DrawingShapes.Single();
        var original = loadedShape.ShapeTextDocument;
        var delete = new RemoveSheetCommand(loaded.Sheets.Single(sheet => sheet.Name == "Data").Id);
        var context = new TestCommandContext(loaded);
        delete.Apply(context).Success.Should().BeTrue();
        using var saved = new MemoryStream();
        xlsx.Save(loaded, saved);
        saved.Position = 0;
        AssertLinks(xlsx.Load(saved).Sheets.First().DrawingShapes.Single().ShapeTextDocument!, "#REF!");
        delete.Revert(context);
        loadedShape.ShapeTextDocument.Should().BeSameAs(original);
    }

    [Fact]
    public void SourceLoadedRename_RebindsDefaultAndRunLinks_WhileRetainingOpaqueRelationship()
    {
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var workbook = new Workbook("test");
        var host = workbook.AddSheet("Host");
        var data = workbook.AddSheet("Data");
        host.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(host.Id, 1, 1), ShapeTextDocument = CreateDocument() });
        var xlsx = new XlsxFileAdapter();
        using var source = new MemoryStream();
        xlsx.Save(workbook, source);
        string sourceId;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/drawings/drawing1.xml")!;
            XDocument drawing;
            using (var input = entry.Open()) drawing = XDocument.Load(input);
            sourceId = drawing.Descendants(a + "hlinkClick").First().Attribute(r + "id")!.Value;
            drawing.Descendants(a + "lstStyle").Single().Add(new XElement(a + "extLst",
                new XElement(a + "ext", new XAttribute("uri", "urn:opaque-test"), new XAttribute(r + "id", sourceId))));
            entry.Delete();
            using var output = archive.CreateEntry("xl/drawings/drawing1.xml").Open();
            drawing.Save(output);
        }
        source.Position = 0;
        var loaded = xlsx.Load(source);
        var original = loaded.Sheets.First().DrawingShapes.Single().ShapeTextDocument!;
        var rename = new RenameSheetCommand(loaded.Sheets.Single(sheet => sheet.Name == data.Name).Id, "New Data");
        rename.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        AssertLinks(loaded.Sheets.First().DrawingShapes.Single().ShapeTextDocument!, "'New Data'!A1");
        using var saved = new MemoryStream();
        xlsx.Save(loaded, saved);
        saved.Position = 0;
        AssertLinks(xlsx.Load(saved).Sheets.First().DrawingShapes.Single().ShapeTextDocument!, "'New Data'!A1");
        saved.Position = 0;
        using var result = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        using var drawingStream = result.GetEntry("xl/drawings/drawing1.xml")!.Open();
        var savedDrawing = XDocument.Load(drawingStream);
        savedDrawing.Descendants(a + "ext").Single(element => (string?)element.Attribute("uri") == "urn:opaque-test")
            .Attribute(r + "id")!.Value.Should().Be(sourceId);
        using var relationshipStream = result.GetEntry("xl/drawings/_rels/drawing1.xml.rels")!.Open();
        var relationships = XDocument.Load(relationshipStream);
        XNamespace packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        relationships.Descendants(packageRelationships + "Relationship").Single(element => (string?)element.Attribute("Id") == sourceId)
            .Attribute("Target")!.Value.Should().Be("Data!A1");
        AssertLinks(original, "Data!A1");
    }

    [Fact]
    public void MissingSourceRelationship_RemainsOpaqueWhileKnownLinkIsRebound()
    {
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var body = XElement.Parse("""
            <xdr:txBody xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr><a:hlinkClick r:id="known"/></a:defRPr></a:pPr>
              <a:r><a:rPr><a:hlinkClick r:id="missing"/></a:rPr><a:t>Text</a:t></a:r></a:p>
            </xdr:txBody>
            """);
        var relationships = new Dictionary<string, (string Target, string? TargetMode)> { ["known"] = ("Data!A1", null) };
        var original = XlsxShapeTextDocumentCodec.Read(body, relationships)!;
        var rewritten = original.RewriteHyperlinks(link => link with { Target = "'New Data'!A1" });
        rewritten.Paragraphs.Single().Inlines.Single().Style!.Hyperlink.Should().BeNull();
        Action unsafeCopy = () => XlsxShapeTextDocumentCodec.Write(rewritten, false, _ => "newId");
        unsafeCopy.Should().Throw<InvalidOperationException>();
        var saved = XlsxShapeTextDocumentCodec.Write(rewritten, false, _ => "newId", preserveOpaqueRelationships: true);
        saved.Descendants(a + "hlinkClick").Select(element => (string?)element.Attribute(r + "id"))
            .Should().Equal("newId", "missing");
        original.Paragraphs.Single().DefaultRunStyle!.Hyperlink!.Target.Should().Be("Data!A1");
    }
}
