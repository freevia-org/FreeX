using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class SourceLoadedTextBoxRichTextTests
{
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void FullSave_UnchangedTextPreservesRunsFieldsBreaksAndFormatting()
    {
        using var input = CreateSource();
        RewriteBody(input, body => body.ReplaceNodes(
            new XElement(A + "bodyPr", new XAttribute("wrap", "none"), new XAttribute("lIns", "91440")),
            new XElement(A + "lstStyle"),
            new XElement(A + "p", new XElement(A + "pPr", new XAttribute("algn", "ctr")),
                new XElement(A + "r", new XElement(A + "rPr", new XAttribute("b", "1")), new XElement(A + "t", "Bold")),
                new XElement(A + "r", new XElement(A + "rPr", new XAttribute("i", "1")), new XElement(A + "t", " italic")),
                new XElement(A + "br", new XElement(A + "rPr", new XAttribute("sz", "1400"))),
                new XElement(A + "fld", new XAttribute("id", "{00000000-0000-0000-0000-000000000001}"), new XAttribute("type", "slidenum"),
                    new XElement(A + "rPr", new XAttribute("u", "sng")), new XElement(A + "t", "42")),
                new XElement(A + "endParaRPr", new XAttribute("lang", "en-US"))),
            new XElement(A + "p", new XElement(A + "r", new XElement(A + "t", "Second")))));
        var original = ReadBody(input);
        input.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(input);
        var sheet = workbook.Sheets.Single();
        var box = sheet.TextBoxes.Single();
        box.Text.Should().Be("Bold italic\n42\nSecond");
        box.Width += 12;
        box.AltText = "Changed alt text";
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Force full save", Anchor = new CellAddress(sheet.Id, 5, 1) });
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics!.Path.Should().Be(XlsxSavePath.FullSave);
        var after = ReadBody(saved);
        XNode.DeepEquals(original, after).Should().BeTrue("unchanged model text must not flatten the original XML");
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved).Sheets.Single().TextBoxes.Single();
        reloaded.Text.Should().Be("Bold italic\n42\nSecond");
        reloaded.AltText.Should().Be("Changed alt text");
    }

    [Fact]
    public void FullSave_EditedTextReplacesLeadingFieldAndBreaks()
    {
        using var input = CreateSource();
        RewriteBody(input, body => body.ReplaceNodes(new XElement(A + "bodyPr"), new XElement(A + "lstStyle"),
            new XElement(A + "p", new XElement(A + "fld", new XAttribute("id", "{00000000-0000-0000-0000-000000000001}"),
                new XAttribute("type", "slidenum"), new XElement(A + "rPr", new XAttribute("b", "1")), new XElement(A + "t", "42")),
                new XElement(A + "br"), new XElement(A + "r", new XElement(A + "t", "old")))));
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(input);
        var sheet = workbook.Sheets.Single();
        sheet.TextBoxes.Single().Text = "New\ntext";
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Force full save", Anchor = new CellAddress(sheet.Id, 5, 1) });
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics!.Path.Should().Be(XlsxSavePath.FullSave);
        var body = ReadBody(saved);
        body.Descendants(A + "fld").Should().BeEmpty();
        saved.Position = 0;
        new XlsxFileAdapter().Load(saved).Sheets.Single().TextBoxes.Single().Text.Should().Be("New\ntext");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FullSave_TextEditCreatesMissingBodyOrParagraph(bool missingBody)
    {
        using var input = CreateSource();
        RewriteBody(input, body => { if (missingBody) body.Remove(); else body.Elements(A + "p").Remove(); });
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(input);
        var sheet = workbook.Sheets.Single();
        sheet.TextBoxes.Single().Text = "Added";
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Force full save", Anchor = new CellAddress(sheet.Id, 5, 1) });
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics!.Path.Should().Be(XlsxSavePath.FullSave);
        saved.Position = 0;
        new XlsxFileAdapter().Load(saved).Sheets.Single().TextBoxes.Single().Text.Should().Be("Added");
    }

    [Theory]
    [InlineData("X")]
    [InlineData("X\nY\nZ")]
    public void FullSave_EditingBreakOnlyParagraph_RemovesOldBreaks(string text)
    {
        using var input = CreateSource();
        RewriteBody(input, body => body.ReplaceNodes(new XElement(A + "bodyPr"), new XElement(A + "lstStyle"),
            new XElement(A + "p", new XElement(A + "br"), new XElement(A + "endParaRPr", new XAttribute("lang", "en-US")))));
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(input);
        var sheet = workbook.Sheets.Single();
        sheet.TextBoxes.Single().Text.Should().Be("\n");
        sheet.TextBoxes.Single().Text = text;
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Force full save", Anchor = new CellAddress(sheet.Id, 5, 1) });
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics!.Path.Should().Be(XlsxSavePath.FullSave);
        ReadBody(saved).Descendants(A + "br").Should().BeEmpty();
        saved.Position = 0;
        new XlsxFileAdapter().Load(saved).Sheets.Single().TextBoxes.Single().Text.Should().Be(text);
    }

    private static MemoryStream CreateSource()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel { Name = "TextBox 1", Anchor = new CellAddress(sheet.Id, 1, 1), Text = "Original" });
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static void RewriteBody(MemoryStream stream, Action<XElement> edit)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.Entries.Single(item => item.FullName.StartsWith("xl/drawings/drawing", StringComparison.Ordinal) && item.FullName.EndsWith(".xml", StringComparison.Ordinal));
            XDocument document;
            using (var source = entry.Open()) document = XDocument.Load(source);
            edit(document.Descendants(Xdr + "txBody").Single());
            var path = entry.FullName;
            entry.Delete();
            using var destination = archive.CreateEntry(path).Open();
            document.Save(destination);
        }
        stream.Position = 0;
    }

    private static XElement ReadBody(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.Entries.Single(item => item.FullName.StartsWith("xl/drawings/drawing", StringComparison.Ordinal) && item.FullName.EndsWith(".xml", StringComparison.Ordinal));
        using var source = entry.Open();
        return new XElement(XDocument.Load(source).Descendants(Xdr + "txBody").Single());
    }
}
