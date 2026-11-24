using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class DocxSoftLineBreakSerializationTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static IEnumerable<object[]> StoryCases()
    {
        foreach (var story in new[] { "body", "table", "header" })
        foreach (var kind in new[] { "plain", "simpleField", "complexField" })
        foreach (var separator in new[] { "\n", "\r\n", "\r" })
            yield return [story, kind, separator];
    }

    [Theory]
    [MemberData(nameof(StoryCases))]
    public void TextAndCachedResultsEmitWrappingBreaksInEveryStory(string story, string kind, string separator)
    {
        var text = "  first\t" + separator + "\tsecond  ";
        var run = kind switch
        {
            "simpleField" => Run.DateField(text),
            "complexField" => Run.ComplexFieldRun(" DOCVARIABLE Example ", text),
            _ => new Run(text)
        };
        var document = InStory(story, run);

        var bytes = Write(document);
        var paragraph = StoryXml(bytes, story);
        var content = paragraph.Descendants().Where(element =>
            element.Name == W + "t" || element.Name == W + "br").ToList();
        content.Select(element => element.Name == W + "br" ? "break" : element.Value)
            .Should().Equal("  first\t", "break", "\tsecond  ");
        paragraph.Descendants(W + "br").Single().Attribute(W + "type").Should().BeNull();
        paragraph.Descendants(W + "t").Should().OnlyContain(element =>
            !element.Value.Contains('\r') && !element.Value.Contains('\n')
            && element.Attribute(XNamespace.Xml + "space")!.Value == "preserve");

        var reopened = StoryParagraph(DocxReader.Read(new MemoryStream(bytes)), story);
        reopened.PlainText.Should().Be("  first\t\n\tsecond  ");
        if (kind == "complexField")
            reopened.Runs.Single().ComplexField!.Instruction.Should().Be(" DOCVARIABLE Example ");
        else if (kind == "simpleField")
            reopened.Runs.Single().FieldKind.Should().Be(RunFieldKind.Date);
    }

    [Theory]
    [InlineData("\n", RevisionKind.Inserted)]
    [InlineData("\r\n", RevisionKind.Inserted)]
    [InlineData("\r", RevisionKind.Inserted)]
    [InlineData("\n", RevisionKind.Deleted)]
    [InlineData("\r\n", RevisionKind.Deleted)]
    [InlineData("\r", RevisionKind.Deleted)]
    public void NestedCachedResultsKeepFieldOrderAndTrackedMarkup(string separator, RevisionKind revision)
    {
        var prefix = " lead" + separator + " ";
        var nestedText = "inner" + separator + "two";
        const string suffix = "\t tail ";
        var run = Run.ComplexFieldRun(
            " TOC ",
            prefix + nestedText + suffix,
            nestedFields:
            [
                new NestedComplexField(new ComplexField(" PAGEREF Example "), nestedText,
                    NestedComplexFieldPlacement.Result, prefix.Length, nestedText.Length)
            ]);
        run.Revision = revision;
        run.RevisionAuthor = "Reviewer";
        run.RevisionDateXml = "2026-01-01T00:00:00Z";

        var bytes = Write(InStory("body", run));
        var paragraph = StoryXml(bytes, "body");
        var wrapperName = revision == RevisionKind.Deleted ? "del" : "ins";
        var textName = revision == RevisionKind.Deleted ? "delText" : "t";
        var wrapper = paragraph.Element(W + wrapperName)!;
        wrapper.Should().NotBeNull();
        wrapper.Attribute(W + "author")!.Value.Should().Be("Reviewer");
        paragraph.Elements(W + "r").Should().BeEmpty("the entire field stays in its revision wrapper");
        wrapper.Elements(W + "r").SelectMany(element => element.Elements())
            .Where(element => element.Name != W + "rPr")
            .Select(element => element.Name == W + "fldChar"
                ? element.Attribute(W + "fldCharType")!.Value
                : element.Name == W + "br" ? "break" : element.Value)
            .Should().Equal("begin", " TOC ", "separate", " lead", "break", " ",
                "begin", " PAGEREF Example ", "separate", "inner", "break", "two", "end", "\t tail ", "end");
        wrapper.Descendants(W + textName).Should().HaveCount(5).And.OnlyContain(element =>
            !element.Value.Contains('\r') && !element.Value.Contains('\n'));
        wrapper.Descendants(W + (revision == RevisionKind.Deleted ? "t" : "delText")).Should().BeEmpty();

        var reopened = StoryParagraph(DocxReader.Read(new MemoryStream(bytes)), "body").Runs.Single();
        reopened.Text.Should().Be(" lead\n inner\ntwo\t tail ");
        reopened.Revision.Should().Be(revision);
        reopened.RevisionAuthor.Should().Be("Reviewer");
        var nested = reopened.ComplexField!.NestedFields.Should().ContainSingle().Subject;
        nested.Offset.Should().Be(" lead\n ".Length);
        nested.Length.Should().Be("inner\ntwo".Length);
        nested.CachedResult.Should().Be("inner\ntwo");
        nested.Field.Instruction.Should().Be(" PAGEREF Example ");
    }

    [Fact]
    public void BreaksKeepLeadingTrailingAndConsecutivePositions()
    {
        var text = "\r\n\rfirst" + Hyphenator.SoftHyphen + "\nlast" + Hyphenator.NoBreakHyphen + "\r";
        var bytes = Write(InStory("body", Run.ComplexFieldRun(" DOCVARIABLE Example ", text)));
        var content = StoryXml(bytes, "body").Descendants(W + "r")
            .SelectMany(run => run.Elements())
            .Where(element => element.Name != W + "rPr" && element.Name != W + "fldChar" && element.Name != W + "instrText");

        content.Select(element => element.Name == W + "t" ? element.Value : element.Name.LocalName)
            .Should().Equal("br", "br", "first", "softHyphen", "br", "last", "noBreakHyphen", "br");
        StoryParagraph(DocxReader.Read(new MemoryStream(bytes)), "body").PlainText
            .Should().Be("\n\nfirst" + Hyphenator.SoftHyphen + "\nlast" + Hyphenator.NoBreakHyphen + "\n");
    }

    private static TextDocument InStory(string story, Run run)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(run);
        if (story == "table")
        {
            var cell = new TableCell();
            cell.Paragraphs.Clear();
            cell.Paragraphs.Add(paragraph);
            var row = new TableRow();
            row.Cells.Add(cell);
            var table = new Table();
            table.Rows.Add(row);
            document.Blocks.Add(table);
        }
        else if (story == "header")
        {
            document.Blocks.Add(new Paragraph("Body"));
            document.FinalSectionHeadersFooters.Header = new HeaderFooter();
            document.FinalSectionHeadersFooters.Header.Paragraphs.Add(paragraph);
        }
        else
        {
            document.Blocks.Add(paragraph);
        }
        return document;
    }

    private static Paragraph StoryParagraph(TextDocument document, string story) => story switch
    {
        "table" => document.Blocks.OfType<Table>().Single().Rows.Single().Cells.Single().Paragraphs.Single(),
        "header" => document.FinalSectionHeadersFooters.Header!.Paragraphs.Single(),
        _ => document.Blocks.OfType<Paragraph>().Single()
    };

    private static XElement StoryXml(byte[] bytes, string story)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var stream = archive.GetEntry(story == "header" ? "word/header1.xml" : "word/document.xml")!.Open();
        var xml = XDocument.Load(stream);
        return story == "table" ? xml.Descendants(W + "tc").Single().Element(W + "p")! : xml.Descendants(W + "p").Single();
    }

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }
}
