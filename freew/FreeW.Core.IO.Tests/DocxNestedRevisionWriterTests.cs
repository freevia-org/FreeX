using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Xunit.Abstractions;

namespace FreeW.Core.IO.Tests;

public sealed class DocxNestedRevisionWriterTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string InnerAuthor = "Inner reviewer";
    private const string OuterAuthor = "Outer reviewer";
    private const string InnerDate = "2026-01-02T11:30:00Z";
    private const string OuterDate = "2026-01-01T10:00:00Z";

    public DocxNestedRevisionWriterTests(ITestOutputHelper output)
    {
        foreach (var assembly in new[] { typeof(DocxWriter).Assembly, typeof(Run).Assembly })
        {
            output.WriteLine("Loaded {0}: {1}; SHA256={2}; version={3}", assembly.GetName().Name,
                assembly.Location, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(),
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        }
    }

    [Theory]
    [InlineData("plain", RevisionKind.Inserted)]
    [InlineData("plain", RevisionKind.Deleted)]
    [InlineData("external", RevisionKind.Inserted)]
    [InlineData("external", RevisionKind.Deleted)]
    [InlineData("internal", RevisionKind.Inserted)]
    [InlineData("internal", RevisionKind.Deleted)]
    [InlineData("control", RevisionKind.Inserted)]
    [InlineData("control", RevisionKind.Deleted)]
    public void SaveAndReopenPreservesBothRevisionAuthors(string context, RevisionKind innerKind)
    {
        var outer = Outer(innerKind);
        var run = Marked("nested", innerKind, outer);
        ConfigureContext(run, context);
        var document = Document(run);
        if (context == "internal") document.Paragraphs.Single().BookmarkNames.Add("Target");

        using var output = Save(document);
        AssertSchema(output);
        var xml = ReadDocumentXml(output);
        var textRun = xml.Descendants(W + "r").Single(element => element.Elements()
            .Any(child => (child.Name == W + "t" || child.Name == W + "delText") && child.Value == "nested"));
        var wrappers = textRun.Ancestors().Where(IsRevisionWrapper).ToArray();
        wrappers.Should().HaveCount(2);
        AssertMark(wrappers[0], innerKind, InnerAuthor, InnerDate);
        AssertMark(wrappers[1], outer.Kind, OuterAuthor, OuterDate);
        textRun.Element(W + (innerKind == RevisionKind.Deleted ? "delText" : "t"))!.Value.Should().Be("nested");
        wrappers.Select(element => (string?)element.Attribute(W + "id")).Should().OnlyHaveUniqueItems();

        output.Position = 0;
        var reopened = DocxReader.Read(output).Paragraphs.Single().Runs.Single(item => item.Text == "nested");
        reopened.Revision.Should().Be(innerKind);
        reopened.RevisionAuthor.Should().Be(InnerAuthor);
        reopened.RevisionDateXml.Should().Be(InnerDate);
        reopened.NestedRevision.Should().Be(outer);
        reopened.HyperlinkUrl.Should().Be(run.HyperlinkUrl);
        reopened.HyperlinkAnchor.Should().Be(run.HyperlinkAnchor);
        (reopened.Control?.Kind).Should().Be(run.Control?.Kind);

        document.Paragraphs.Single().Runs.Should().ContainSingle().Which.Should().BeSameAs(run);
        run.NestedRevision.Should().BeSameAs(outer);
        run.Revision.Should().Be(innerKind);
        run.Text.Should().Be("nested");
    }

    [Theory]
    [InlineData("plain", RevisionKind.Inserted)]
    [InlineData("plain", RevisionKind.Deleted)]
    [InlineData("external", RevisionKind.Inserted)]
    [InlineData("external", RevisionKind.Deleted)]
    [InlineData("control", RevisionKind.Inserted)]
    [InlineData("control", RevisionKind.Deleted)]
    public void EqualNestedIdentitiesCoalesceWithoutDuplicatingWrappers(string context, RevisionKind innerKind)
    {
        var first = Marked("a", innerKind, Outer(innerKind));
        var second = Marked("b", innerKind, Outer(innerKind));
        ConfigureContext(first, context);
        ConfigureContext(second, context);
        if (context == "control") second.Control = first.Control;

        using var output = Save(Document(first, second));
        var wrappers = ReadDocumentXml(output).Descendants().Where(IsRevisionWrapper).ToArray();
        wrappers.Should().HaveCount(2);
        AssertMark(wrappers[0], Outer(innerKind).Kind, OuterAuthor, OuterDate);
        AssertMark(wrappers[1], innerKind, InnerAuthor, InnerDate);
        string.Concat(wrappers[1].Descendants(W + (innerKind == RevisionKind.Deleted ? "delText" : "t"))
            .Select(element => element.Value)).Should().Be("ab");
    }

    [Theory]
    [InlineData("plain", "author")]
    [InlineData("plain", "date")]
    [InlineData("plain", "absent")]
    [InlineData("external", "author")]
    [InlineData("external", "date")]
    [InlineData("external", "absent")]
    [InlineData("control", "author")]
    [InlineData("control", "date")]
    [InlineData("control", "absent")]
    public void DifferentOuterIdentitiesAreNotMergedIntoOneInnerRevision(string context, string difference)
    {
        var firstOuter = Outer(RevisionKind.Deleted);
        var secondOuter = difference switch
        {
            "author" => firstOuter with { Author = "Other outer reviewer" },
            "date" => firstOuter with { DateXml = "2026-01-03T12:00:00Z" },
            _ => null,
        };
        var first = Marked("a", RevisionKind.Deleted, firstOuter);
        var second = Marked("b", RevisionKind.Deleted, secondOuter);
        ConfigureContext(first, context);
        ConfigureContext(second, context);
        if (context == "control") second.Control = first.Control;

        using var output = Save(Document(first, second));
        var xml = ReadDocumentXml(output);
        foreach (var (text, expectedOuter) in new[] { ("a", firstOuter), ("b", secondOuter) })
        {
            var run = xml.Descendants(W + "r").Single(element => element.Element(W + "delText")?.Value == text);
            var wrappers = run.Ancestors().Where(IsRevisionWrapper).ToArray();
            wrappers.Should().HaveCount(expectedOuter is null ? 1 : 2);
            AssertMark(wrappers[0], RevisionKind.Deleted, InnerAuthor, InnerDate);
            if (expectedOuter is not null)
                AssertMark(wrappers[1], expectedOuter.Kind, expectedOuter.Author, expectedOuter.DateXml);
        }
        output.Position = 0;
        var reopened = DocxReader.Read(output).Paragraphs.Single().Runs.Where(run => run.Text.Length > 0).ToArray();
        reopened.Select(run => run.NestedRevision).Should().Equal(firstOuter, secondOuter);
    }

    [Theory]
    [InlineData("plain", RevisionKind.Inserted)]
    [InlineData("plain", RevisionKind.Deleted)]
    [InlineData("external", RevisionKind.Inserted)]
    [InlineData("external", RevisionKind.Deleted)]
    [InlineData("internal", RevisionKind.Inserted)]
    [InlineData("internal", RevisionKind.Deleted)]
    public void OrdinaryRevisionUsesOneWrapperWithRequiredEmptyAuthorAndNoAbsentDate(string context, RevisionKind kind)
    {
        var run = new Run("ordinary") { Revision = kind };
        ConfigureContext(run, context);
        var document = Document(run);
        if (context == "internal") document.Paragraphs.Single().BookmarkNames.Add("Target");
        using var output = Save(document);
        AssertSchema(output);
        var wrapper = ReadDocumentXml(output).Descendants().Where(IsRevisionWrapper).Should().ContainSingle().Which;
        AssertMark(wrapper, kind, string.Empty, null);
        wrapper.Parent!.Name.Should().Be(W + (context == "plain" ? "p" : "hyperlink"));
        output.Position = 0;
        DocxReader.Read(output).Paragraphs.Single().Runs.Single().NestedRevision.Should().BeNull();
        run.RevisionAuthor.Should().BeNull();
        run.RevisionDateXml.Should().BeNull();
    }

    [Theory]
    [InlineData(RevisionKind.Inserted)]
    [InlineData(RevisionKind.Deleted)]
    public void BothNestedMarksHaveRequiredAuthorEvenWhenAuthorshipIsUnknown(RevisionKind innerKind)
    {
        var run = new Run("unknown")
        {
            Revision = innerKind,
            NestedRevision = new NestedRevisionInfo(Outer(innerKind).Kind, null, null),
        };
        using var output = Save(Document(run));
        AssertSchema(output);
        var wrappers = ReadDocumentXml(output).Descendants().Where(IsRevisionWrapper).ToArray();
        wrappers.Should().HaveCount(2);
        AssertMark(wrappers[0], run.NestedRevision.Kind, string.Empty, null);
        AssertMark(wrappers[1], innerKind, string.Empty, null);
        run.RevisionAuthor.Should().BeNull();
        run.NestedRevision.Author.Should().BeNull();
    }

    [Fact]
    public void OneContentControlCanContainNestedOrdinaryAndNestedRunsInOrder()
    {
        var control = new ContentControl(ContentControlKind.PlainText);
        var first = Marked("a", RevisionKind.Deleted, Outer(RevisionKind.Deleted));
        var middle = new Run("b");
        var last = Marked("c", RevisionKind.Inserted, Outer(RevisionKind.Inserted));
        foreach (var run in new[] { first, middle, last }) run.Control = control;

        using var output = Save(Document(first, middle, last));
        AssertSchema(output);
        var content = ReadDocumentXml(output).Descendants(W + "sdtContent").Should().ContainSingle().Which;
        string.Concat(content.Descendants().Where(element => element.Name == W + "t" || element.Name == W + "delText")
            .Select(element => element.Value)).Should().Be("abc");
        content.Elements().Select(element => element.Name).Should().Equal(W + "ins", W + "r", W + "del");
        output.Position = 0;
        var runs = DocxReader.Read(output).Paragraphs.Single().Runs.Where(run => run.Text.Length > 0).ToArray();
        runs.Select(run => run.Text).Should().Equal("a", "b", "c");
        runs.Select(run => run.NestedRevision).Should().Equal(first.NestedRevision, null, last.NestedRevision);
        runs.Should().OnlyContain(run => run.Control != null);
        runs[1].Control.Should().BeSameAs(runs[0].Control);
        runs[2].Control.Should().BeSameAs(runs[0].Control);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("header")]
    [InlineData("footer")]
    [InlineData("footnote")]
    [InlineData("endnote")]
    public void FreshOuterIdsCannotCollideWithCurrentOrLaterMovesInThePart(string story)
    {
        var first = MoveParagraph(1, nested: true);
        var later = MoveParagraph(2, nested: false);
        var document = Document(new Run("anchor"));
        var partName = "word/document.xml";
        switch (story)
        {
            case "body":
                document.Blocks.Clear(); document.Blocks.Add(first); document.Blocks.Add(later);
                break;
            case "header":
                document.Header = new HeaderFooter();
                document.Header.Paragraphs.AddRange([first, later]);
                partName = "word/header1.xml";
                break;
            case "footer":
                document.Footer = new HeaderFooter();
                document.Footer.Paragraphs.AddRange([first, later]);
                partName = "word/footer1.xml";
                break;
            case "footnote":
                document.Footnotes[1] = new Footnote(1) { Content = { first } };
                document.Footnotes[2] = new Footnote(2) { Content = { later } };
                document.Paragraphs.Single().Runs.AddRange([Run.FootnoteReference(1), Run.FootnoteReference(2)]);
                partName = "word/footnotes.xml";
                break;
            case "endnote":
                document.Endnotes[1] = new Endnote(1) { Content = { first } };
                document.Endnotes[2] = new Endnote(2) { Content = { later } };
                document.Paragraphs.Single().Runs.AddRange([Run.EndnoteReference(1), Run.EndnoteReference(2)]);
                partName = "word/endnotes.xml";
                break;
        }

        using var output = Save(document);
        AssertMoveIds(ReadDocumentXml(output, partName));
        output.Position = 0;
        var reopened = DocxReader.Read(output);
        IEnumerable<Paragraph> paragraphs = story switch
        {
            "header" => reopened.Header!.Paragraphs,
            "footer" => reopened.Footer!.Paragraphs,
            "footnote" => reopened.Footnotes.Values.SelectMany(note => note.Content),
            "endnote" => reopened.Endnotes.Values.SelectMany(note => note.Content),
            _ => reopened.Paragraphs,
        };
        var moves = paragraphs.SelectMany(paragraph => paragraph.Runs).Where(run => run.MoveRevisionId != null).ToArray();
        moves.Should().HaveCount(4);
        foreach (var run in moves)
        {
            run.RevisionAuthor.Should().Be(InnerAuthor);
            run.RevisionDateXml.Should().Be(InnerDate);
            run.Text.Should().Be("move" + run.MoveRevisionId);
            run.NestedRevision.Should().Be(run.MoveRevisionId == 1 ? Outer(run.Revision) : null);
        }
        moves.GroupBy(run => run.MoveRevisionId).Select(group => group.Select(run => run.Revision).ToArray())
            .Should().OnlyContain(pair => pair.Length == 2 && pair.Contains(RevisionKind.Inserted) && pair.Contains(RevisionKind.Deleted));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveInsideNestedTableIsReservedAlongsideOtherPartMoves(bool inHeader)
    {
        var first = MoveParagraph(1, nested: true);
        var later = MoveParagraph(2, nested: false);
        var table = Table.Create(1, 1);
        table.Formatting = table.Formatting with { HeaderRow = false };
        var cell = table.Rows[0].Cells[0];
        cell.Paragraphs.Clear(); cell.Paragraphs.Add(first);
        var nestedTable = Table.Create(1, 1);
        nestedTable.Formatting = nestedTable.Formatting with { HeaderRow = false };
        nestedTable.Rows[0].Cells[0].Paragraphs.Clear();
        nestedTable.Rows[0].Cells[0].Paragraphs.Add(later);
        cell.NestedTables.Add(nestedTable);
        var document = Document(new Run("anchor"));
        if (inHeader)
        {
            document.Header = new HeaderFooter { Table = table };
            document.Header.Paragraphs.Add(first);
        }
        else
        {
            document.Blocks.Clear(); document.Blocks.Add(table);
        }

        using var output = Save(document);
        AssertMoveIds(ReadDocumentXml(output, inHeader ? "word/header1.xml" : "word/document.xml"));
    }

    [Theory]
    [InlineData(RevisionKind.Inserted)]
    [InlineData(RevisionKind.Deleted)]
    public void HeaderRowBoldCopyRetainsOuterRevisionWithoutChangingSource(RevisionKind kind)
    {
        var outer = Outer(kind);
        var run = Marked("header", kind, outer);
        var paragraph = new Paragraph(); paragraph.Runs.Add(run);
        var table = Table.Create(1, 1);
        table.Formatting = table.Formatting with { HeaderRow = true };
        table.Rows[0].Cells[0].Paragraphs.Clear();
        table.Rows[0].Cells[0].Paragraphs.Add(paragraph);
        var document = Document(); document.Blocks.Clear(); document.Blocks.Add(table);

        using var output = Save(document);
        AssertSchema(output);
        var element = ReadDocumentXml(output).Descendants(W + "r")
            .Single(item => item.Element(W + (kind == RevisionKind.Deleted ? "delText" : "t"))?.Value == "header");
        element.Element(W + "rPr")!.Element(W + "b").Should().NotBeNull();
        var wrappers = element.Ancestors().Where(IsRevisionWrapper).ToArray();
        wrappers.Should().HaveCount(2);
        AssertMark(wrappers[0], kind, InnerAuthor, InnerDate);
        AssertMark(wrappers[1], outer.Kind, OuterAuthor, OuterDate);
        paragraph.Runs.Should().ContainSingle().Which.Should().BeSameAs(run);
        run.Formatting.Bold.Should().BeFalse();
        run.NestedRevision.Should().BeSameAs(outer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveIdsInsideNestedDrawingGroupsAreReservedBeforeEarlierRevisions(bool inHeader)
    {
        var textBox = new Shape(ShapeKind.TextBox, 120, 80);
        textBox.TextParagraphs.Add(MoveParagraph(1, nested: false));
        var innerGroup = new DrawingGroup();
        innerGroup.Children.Add(textBox);
        innerGroup.Children.Add(new Shape(ShapeKind.Rectangle, 20, 20));
        var outerGroup = new DrawingGroup();
        outerGroup.Children.Add(innerGroup);
        outerGroup.Children.Add(new Shape(ShapeKind.Rectangle, 20, 20));
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Marked("earlier", RevisionKind.Deleted, Outer(RevisionKind.Deleted)));
        paragraph.Runs.Add(Run.FromDrawingGroup(outerGroup));
        var document = Document();
        if (inHeader)
        {
            document.Header = new HeaderFooter();
            document.Header.Paragraphs.Add(paragraph);
        }
        else
        {
            document.Blocks.Clear(); document.Blocks.Add(paragraph);
        }

        using var output = Save(document);
        var xml = ReadDocumentXml(output, inHeader ? "word/header1.xml" : "word/document.xml");
        var generated = xml.Descendants().Where(IsRevisionWrapper)
            .Select(element => (int)element.Attribute(W + "id")!).ToArray();
        generated.Should().HaveCount(2);
        generated.Should().OnlyContain(id => id != 1);
        generated.Should().OnlyHaveUniqueItems();
        xml.Descendants().Where(element => element.Name == W + "moveFrom" || element.Name == W + "moveTo")
            .Select(element => (int)element.Attribute(W + "id")!).Should().Equal(1, 1);
    }

    private static Paragraph MoveParagraph(int id, bool nested)
    {
        var paragraph = new Paragraph();
        foreach (var kind in new[] { RevisionKind.Deleted, RevisionKind.Inserted })
        {
            var run = Marked("move" + id, kind, nested ? Outer(kind) : null);
            run.MoveRevisionId = id;
            paragraph.Runs.Add(run);
        }
        return paragraph;
    }

    private static void AssertMoveIds(XDocument xml)
    {
        var allocated = xml.Descendants().Where(IsRevisionWrapper)
            .Select(element => (int)element.Attribute(W + "id")!).ToArray();
        allocated.Should().HaveCount(2);
        allocated.Should().OnlyContain(id => id != 1 && id != 2);
        allocated.Should().OnlyHaveUniqueItems();
        var moved = xml.Descendants().Where(element => element.Name == W + "moveFrom" || element.Name == W + "moveTo")
            .Select(element => (int)element.Attribute(W + "id")!).ToArray();
        moved.OrderBy(id => id).Should().Equal(1, 1, 2, 2);
    }

    private static NestedRevisionInfo Outer(RevisionKind inner) =>
        new(inner == RevisionKind.Deleted ? RevisionKind.Inserted : RevisionKind.Deleted, OuterAuthor, OuterDate);

    private static Run Marked(string text, RevisionKind kind, NestedRevisionInfo? outer) => new(text)
    {
        Revision = kind,
        RevisionAuthor = InnerAuthor,
        RevisionDateXml = InnerDate,
        NestedRevision = outer,
    };

    private static void ConfigureContext(Run run, string context)
    {
        if (context == "external") run.HyperlinkUrl = "https://example.invalid/review";
        if (context == "internal") run.HyperlinkAnchor = "Target";
        if (context == "control") run.Control = new ContentControl(ContentControlKind.PlainText);
    }

    private static TextDocument Document(params Run[] runs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange(runs);
        document.Blocks.Add(paragraph);
        return document;
    }

    private static MemoryStream Save(TextDocument document)
    {
        var output = new MemoryStream();
        DocxWriter.Write(document, output);
        output.Position = 0;
        return output;
    }

    private static XDocument ReadDocumentXml(Stream input, string partName = "word/document.xml")
    {
        input.Position = 0;
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        using var xml = archive.GetEntry(partName)!.Open();
        return XDocument.Load(xml);
    }

    private static void AssertSchema(MemoryStream input)
    {
        using var copy = new MemoryStream(input.ToArray());
        using var package = WordprocessingDocument.Open(copy, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2013).Validate(package).ToArray();
        errors.Select(error => $"{error.Path?.XPath}: {error.Description}").Should().BeEmpty();
    }

    private static bool IsRevisionWrapper(XElement element) => element.Name == W + "ins" || element.Name == W + "del";

    private static void AssertMark(XElement element, RevisionKind kind, string? author, string? date)
    {
        element.Name.Should().Be(W + (kind == RevisionKind.Deleted ? "del" : "ins"));
        ((string?)element.Attribute(W + "author")).Should().Be(author);
        ((string?)element.Attribute(W + "date")).Should().Be(date);
        ((string?)element.Attribute(W + "id")).Should().NotBeNullOrEmpty();
    }
}
