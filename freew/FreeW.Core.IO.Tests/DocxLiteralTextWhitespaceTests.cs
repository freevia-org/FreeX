using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class DocxLiteralTextWhitespaceTests
{
    private const string Marker = "LiteralTextMarker";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static IEnumerable<object[]> LiteralLineEndings()
    {
        foreach (var story in new[] { "body", "table", "header", "footer", "shape" })
        foreach (var kind in new[] { "plain", "deleted", "simpleField", "complexField" })
        foreach (var preserve in new[] { false, true })
        foreach (var lineEnding in new[] { "\n", "\r\n", "\r" })
            yield return [story, kind, preserve, lineEnding];
    }

    [Theory]
    [MemberData(nameof(LiteralLineEndings))]
    public void PhysicalXmlLineEndingsAreSpacesRatherThanWrappingBreaks(
        string story, string kind, bool preserve, string lineEnding)
    {
        var paragraph = Read(story, kind, preserve, "Alpha" + lineEnding + "Beta");

        paragraph.PlainText.Should().Be("Alpha Beta");
        if (kind == "deleted")
            paragraph.Runs.Single().Revision.Should().Be(RevisionKind.Deleted);
        if (kind == "complexField")
            paragraph.Runs.Single().ComplexField!.Instruction.Should().Be(" DOCVARIABLE Example ");
    }

    public static IEnumerable<object[]> StoryKinds()
    {
        foreach (var story in new[] { "body", "table", "header", "footer", "shape" })
        foreach (var kind in new[] { "plain", "deleted", "simpleField", "complexField" })
            yield return [story, kind];
    }

    [Theory]
    [MemberData(nameof(StoryKinds))]
    public void ExplicitBreakElementsKeepTheirPositionsAndConsecutiveBreaks(string story, string kind)
    {
        var tag = kind == "deleted" ? "delText" : "t";
        var text = $"Alpha</w:{tag}><w:br/><w:cr/><w:{tag} xml:space=\"preserve\">Beta";

        Read(story, kind, preserve: true, text).PlainText.Should().Be("Alpha\n\nBeta");
    }

    [Theory]
    [MemberData(nameof(StoryKinds))]
    public void PreservedLeadingTrailingAndConsecutiveSpacesAreNotCollapsed(string story, string kind)
    {
        Read(story, kind, preserve: true, "  Alpha \n Beta  ").PlainText.Should().Be("  Alpha   Beta  ");
    }

    [Theory]
    [MemberData(nameof(StoryKinds))]
    public void EscapedCarriageReturnIsNotGivenThePhysicalXmlLineEndingPolicy(string story, string kind)
    {
        // XML's physical CR/CRLF normalization does not apply to a character reference.
        // Its Word display behavior has not been established by the reference fixture.
        Read(story, kind, preserve: true, "Alpha&#13;Beta").PlainText.Should().Be("Alpha\rBeta");
    }

    private static Paragraph Read(string story, string kind, bool preserve, string replacementXml)
    {
        var run = kind switch
        {
            "simpleField" => Run.DateField(Marker),
            "complexField" => Run.ComplexFieldRun(" DOCVARIABLE Example ", Marker),
            _ => new Run(Marker)
        };
        if (kind == "deleted") run.Revision = RevisionKind.Deleted;
        var paragraph = new Paragraph(); paragraph.Runs.Add(run);
        var document = TextDocument.CreateEmpty(); document.Blocks.Clear();
        switch (story)
        {
            case "table":
                var cell = new TableCell(); cell.Paragraphs.Clear(); cell.Paragraphs.Add(paragraph);
                var row = new TableRow(); row.Cells.Add(cell);
                var table = new Table(); table.Rows.Add(row); document.Blocks.Add(table);
                break;
            case "header":
            case "footer":
                document.Blocks.Add(new Paragraph("Body control"));
                var headerFooter = new HeaderFooter(); headerFooter.Paragraphs.Add(paragraph);
                if (story == "header") document.FinalSectionHeadersFooters.Header = headerFooter;
                else document.FinalSectionHeadersFooters.Footer = headerFooter;
                break;
            case "shape":
                var shape = new Shape(ShapeKind.TextBox, 180, 72); shape.TextParagraphs.Add(paragraph);
                var owner = new Paragraph(); owner.Runs.Add(Run.FromShape(shape)); document.Blocks.Add(owner);
                break;
            default:
                document.Blocks.Add(paragraph);
                break;
        }

        using var package = new MemoryStream(); DocxWriter.Write(document, package);
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var part = story switch { "header" => "word/header1.xml", "footer" => "word/footer1.xml", _ => "word/document.xml" };
            var entry = archive.GetEntry(part)!;
            XDocument xml;
            using (var stream = entry.Open()) xml = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            var literal = xml.Descendants().Single(element => (element.Name == W + "t" || element.Name == W + "delText") && element.Value == Marker);
            literal.SetAttributeValue(XNamespace.Xml + "space", preserve ? "preserve" : null);
            // Inject after serialization so physical CR and CRLF remain physical XML line endings,
            // rather than being escaped as character references by the XML writer.
            var content = xml.ToString(SaveOptions.DisableFormatting).Replace(Marker, replacementXml, StringComparison.Ordinal);
            entry.Delete(); entry = archive.CreateEntry(part);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)); writer.Write(content);
        }
        package.Position = 0;
        var imported = DocxReader.Read(package);
        return story switch
        {
            "table" => imported.Blocks.OfType<Table>().Single().Rows.Single().Cells.Single().Paragraphs.Single(),
            "header" => imported.FinalSectionHeadersFooters.Header!.Paragraphs.Single(),
            "footer" => imported.FinalSectionHeadersFooters.Footer!.Paragraphs.Single(),
            "shape" => imported.Blocks.OfType<Paragraph>().Single().Runs.Single(run => run.Shape is not null).Shape!.TextParagraphs.Single(),
            _ => imported.Blocks.OfType<Paragraph>().Single()
        };
    }
}
