using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for Mark Citation (TA) fields — the hidden marks that record legal citations for a
/// Table of Authorities (Word's References &gt; Mark Citation). The mark serialises as an instruction-only
/// complex field whose <c>w:instrText</c> carries the TA instruction
/// (<c> TA \l "long" \s "short" \c N </c>); the reader recovers the <see cref="Citation"/>.
/// </summary>
public class TableOfAuthoritiesRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace B = "http://schemas.openxmlformats.org/officeDocument/2006/bibliography";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static byte[] WriteDocx(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument ReadDocx(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return DocxReader.Read(stream);
    }

    private static XDocument EntryXml(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(entryPath)!.Open();
        return XDocument.Load(entry);
    }

    private static TextDocument CitationDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Brown v. Board of Education"));
        paragraph.Runs.Add(Run.CitationMark(new Citation(
            "Brown v. Board of Education, 347 U.S. 483 (1954)", CitationCategory.Cases, "Brown")));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [Fact]
    public void CitationMark_SurvivesRoundTrip()
    {
        var result = RoundTrip(CitationDocument());

        var citationRun = result.Blocks.OfType<Paragraph>().Single().Runs.Single(r => r.Citation is not null);
        citationRun.Citation!.LongCitation.Should().Be("Brown v. Board of Education, 347 U.S. 483 (1954)");
        citationRun.Citation.ShortCitation.Should().Be("Brown");
        citationRun.Citation.Category.Should().Be(CitationCategory.Cases);
        // The mark carries no visible text (Word's hidden TA field).
        citationRun.Text.Should().BeEmpty();
    }

    [Fact]
    public void CitationMark_EmitsWordCompatibleComplexTaFieldWithSwitches()
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(CitationDocument(), stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(entry);

        var instr = xml.Descendants(W + "instrText").Single().Value;
        xml.Descendants(W + "fldSimple").Should().BeEmpty();
        xml.Descendants(W + "fldChar")
            .Select(fieldChar => fieldChar.Attribute(W + "fldCharType")?.Value)
            .Should().Equal("begin", "end");
        instr.Should().Contain("TA");
        instr.Should().Contain("\\l \"Brown v. Board of Education, 347 U.S. 483 (1954)\"");
        instr.Should().Contain("\\s \"Brown\"");
        instr.Should().Contain("\\c 1");
    }

    [Fact]
    public void CitationMark_WithoutShortForm_OmitsShortSwitch()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.CitationMark(new Citation("17 U.S.C. § 107", CitationCategory.Statutes)));
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var instr = XDocument.Load(entry).Descendants(W + "instrText").Single().Value;

        instr.Should().NotContain("\\s");
        instr.Should().Contain("\\c 2");

        // And the category survives the read back.
        var result = RoundTrip(doc);
        var run = result.Blocks.OfType<Paragraph>().Single().Runs.Single(r => r.Citation is not null);
        run.Citation!.Category.Should().Be(CitationCategory.Statutes);
        run.Citation.ShortCitation.Should().BeEmpty();
    }

    [Fact]
    public void TableOfAuthorities_BuildsFromBodyCitationMarksAfterReopen()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(Run.CitationMark(new Citation("Roe v. Wade, 410 U.S. 113 (1973)", CitationCategory.Cases)));
        p.Runs.Add(Run.CitationMark(new Citation("42 U.S.C. § 1983", CitationCategory.Statutes)));
        doc.Blocks.Add(p);

        // Reopen: the side-store is empty, so the table must be built from the body TA marks.
        var reopened = RoundTrip(doc);
        reopened.Citations.Should().BeEmpty();

        var table = TableOfAuthorities.Build(reopened).Select(x => x.PlainText).ToList();
        table.Should().Equal(
            TableOfAuthorities.HeadingText,
            "Cases",
            "Roe v. Wade, 410 U.S. 113 (1973)",
            "Statutes",
            "42 U.S.C. § 1983");
    }

    [Fact]
    public void TableOfAuthorities_BuildsFromDirectAndNestedTableMarksAfterReopen()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var outer = Table.Create(1, 1);
        outer.Rows[0].Cells[0].Paragraphs[0].Runs.Add(
            Run.CitationMark(new Citation("Direct Table Case", CitationCategory.Cases)));
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0].Runs.Add(
            Run.CitationMark(new Citation("Nested Table Case", CitationCategory.Cases)));
        outer.Rows[0].Cells[0].NestedTables.Add(nested);
        doc.Blocks.Add(outer);

        var reopened = RoundTrip(doc);

        TableOfAuthorities.CollectCitations(reopened)
            .Select(citation => citation.LongCitation)
            .Should().Equal("Nested Table Case", "Direct Table Case");
        TableOfAuthorities.Build(reopened)
            .Where(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("Direct Table Case", "Nested Table Case");
    }

    [Fact]
    public void TableOfAuthorities_ShortCitationAliasesAggregateAfterReopen()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var full = new Paragraph();
        full.Runs.Add(Run.CitationMark(new Citation(
            "Brown v. Board of Education, 347 U.S. 483 (1954)",
            CitationCategory.Cases,
            "Brown")));
        doc.Blocks.Add(full);
        doc.Blocks.Add(DocumentOps.CreatePageBreak());
        var shortForm = new Paragraph();
        shortForm.Runs.Add(Run.CitationMark(new Citation("Brown", CitationCategory.Cases)));
        doc.Blocks.Add(shortForm);

        var reopened = RoundTrip(doc);
        reopened.Citations.Should().BeEmpty();

        var entry = TableOfAuthorities.Build(reopened)
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Brown v. Board of Education, 347 U.S. 483 (1954)\t1, 2");
    }

    [Fact]
    public void TableOfAuthoritiesEntryTabLeader_SurvivesRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(Run.CitationMark(new Citation("Legal Services Corp. v. Velazquez", CitationCategory.Cases)));
        doc.Blocks.Add(p);
        doc.Blocks.AddRange(TableOfAuthorities.Build(doc, new ToaOptions { TabLeader = ToaTabLeader.Dashes }));

        var reopened = RoundTrip(doc);

        reopened.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId)
            .Formatting.TabStops.Should().Equal(
                new TabStop(
                    TableOfAuthorities.DefaultEntryRightTabStopPt,
                    TabStopAlignment.Right,
                    TabLeader.Dashes));
    }

    [Fact]
    public void GeneratedTableOfAuthorities_EmitsOneNativeOwnerAndRetainsOptionsAfterReopen()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var mark = new Paragraph();
        mark.Runs.Add(Run.CitationMark(new Citation("17 U.S.C. 107", CitationCategory.Statutes)));
        doc.Blocks.Add(mark);
        var options = new ToaOptions
        {
            CategoryFilter = CitationCategory.Statutes,
            UsePassim = true,
            TabLeader = ToaTabLeader.Dashes
        };
        var generated = TableOfAuthorities.Build(doc, options);
        for (var index = generated.Count - 1; index >= 0; index--)
            doc.Blocks.Insert(0, generated[index]);

        var docx = WriteDocx(doc);
        var generatedXml = EntryXml(docx, "word/document.xml")
            .Descendants(W + "p")
            .Take(generated.Count)
            .ToArray();
        generatedXml[0].Descendants(W + "fldChar").Should().BeEmpty();
        generatedXml.SelectMany(paragraph => paragraph.Descendants(W + "instrText"))
            .Should().ContainSingle()
            .Which.Value.Should().Be(" TOA \\h \\c \"2\" \\p \\f ");
        generatedXml.SelectMany(paragraph => paragraph.Descendants(W + "fldChar"))
            .Select(field => field.Attribute(W + "fldCharType")!.Value)
            .Should().Equal("begin", "separate", "end");

        var reopened = ReadDocx(docx);
        var result = reopened.Blocks.Take(generated.Count).Cast<Paragraph>().ToArray();
        result[0].SpanningFieldOwner.Should().BeNull();
        result.Skip(1).Should().OnlyContain(paragraph =>
            paragraph.SpanningFieldOwner != null
            && paragraph.SpanningFieldOwner.Instruction == " TOA \\h \\c \"2\" \\p \\f ");
        result[1].SpanningFieldStart!.Instruction.Should().Be(" TOA \\h \\c \"2\" \\p \\f ");
        result[^1].EndsSpanningField.Should().BeTrue();
        TableOfAuthorities.ExistingOptions(reopened)!.CategoryFilter.Should().Be(CitationCategory.Statutes);
        TableOfAuthorities.ExistingOptions(reopened)!.UsePassim.Should().BeTrue();
    }

    [Fact]
    public void EmptyFilteredTableOfAuthorities_RoundTripsNativeEmptyResult()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var generated = TableOfAuthorities.Build(
            doc,
            new ToaOptions { CategoryFilter = CitationCategory.Statutes });
        doc.Blocks.AddRange(generated);

        var docx = WriteDocx(doc);
        var generatedXml = EntryXml(docx, "word/document.xml")
            .Descendants(W + "p")
            .Take(generated.Count)
            .ToArray();
        generatedXml[0].Descendants(W + "fldChar").Should().BeEmpty();
        generatedXml[1].Descendants(W + "instrText").Should().ContainSingle()
            .Which.Value.Should().Be(" TOA \\h \\c \"2\" \\f ");
        generatedXml[1].Descendants(W + "fldChar")
            .Select(field => field.Attribute(W + "fldCharType")!.Value)
            .Should().Equal("begin", "separate", "end");

        var reopened = ReadDocx(docx);
        var result = reopened.Blocks.OfType<Paragraph>().Last();
        result.PlainText.Should().Be(TableOfAuthorities.EmptyResultText);
        result.Runs.Should().ContainSingle();
        result.Runs[0].ComplexField!.Instruction.Should().Be(" TOA \\h \\c \"2\" \\f ");
        TableOfAuthorities.IsTableOfAuthoritiesParagraph(result).Should().BeTrue();
    }

    [Fact]
    public void ReferencesHeavyFieldsFixture_RetainsSourcesFieldsAndToaPageNumbersThroughDocx()
    {
        var doc = ReferencesHeavyFieldsDocument();

        var docx = WriteDocx(doc);
        var documentXml = EntryXml(docx, "word/document.xml");
        var sourcesXml = EntryXml(docx, "word/bibliography/sources.xml");
        var currentSourcesXml = EntryXml(docx, "customXml/item1.xml");
        var currentSourcesPropsXml = EntryXml(docx, "customXml/itemProps1.xml");

        var fieldInstructions = documentXml.Descendants(W + "instrText").Select(e => e.Value).ToList();
        fieldInstructions.Should().Contain(instruction => instruction.Contains("CITATION Knuth1997"));
        fieldInstructions.Should().Contain(instruction => instruction.Contains("CITATION Doe2024"));
        fieldInstructions.Should().Contain(instruction => instruction.Contains("CITATION W3C2025"));
        fieldInstructions.Should().Contain(instruction => instruction.Contains("BIBLIOGRAPHY"));
        fieldInstructions.Should().Contain(instruction => instruction.Contains("TOA"));

        var citationControls = documentXml.Descendants(W + "sdt")
            .Where(control => control.Element(W + "sdtPr")?.Element(W + "citation") is not null)
            .ToList();
        citationControls.Should().HaveCount(5);
        foreach (var citationControl in citationControls)
        {
            var content = citationControl.Element(W + "sdtContent");
            content.Should().NotBeNull();
            content!.Descendants(W + "fldChar")
                .Any(fieldChar => fieldChar.Attribute(W + "fldCharType")?.Value == "begin")
                .Should().BeTrue();
        }

        var taInstructions = documentXml.Descendants(W + "instrText")
            .Select(field => field.Value)
            .Where(instruction => instruction.Contains("TA"))
            .ToList();
        taInstructions.Should().Contain(instruction =>
            instruction.Contains("\\l \"Example v. FreeW, 123 F.4th 456 (2026)\"")
            && instruction.Contains("\\c 1"));
        taInstructions.Should().Contain(instruction =>
            instruction.Contains("\\l \"Free Software Evidence Act, 42 U.S.C. 2026\"")
            && instruction.Contains("\\c 2"));

        var sourceTags = sourcesXml.Root!.Elements(B + "Source")
            .Select(source => source.Element(B + "Tag")!.Value)
            .ToList();
        sourceTags.Should().Equal("Knuth1997", "Doe2024", "W3C2025");
        sourcesXml.Root!.Attribute("SelectedStyle")!.Value.Should().Be("IEEE");
        currentSourcesXml.Root!.Name.Should().Be(B + "Sources");
        currentSourcesXml.Root!.Attribute("SelectedStyle")!.Value.Should().Be("\\IEEE.XSL");
        currentSourcesXml.Root!.Attribute("StyleName")!.Value.Should().Be("IEEE");
        currentSourcesXml.Root!.Attribute("URI")!.Value
            .Should().Be("http://schemas.openxmlformats.org/bibliographicStyle/IEEE");
        currentSourcesXml.Root!.Elements(B + "Source")
            .Select(source => source.Element(B + "Tag")!.Value)
            .Should().Equal("Knuth1997", "Doe2024", "W3C2025");
        currentSourcesPropsXml.Root!.Name.LocalName.Should().Be("dataStoreItem");

        var reopened = ReadDocx(docx);
        reopened.BibliographyStyle.Should().Be(CitationStyle.Ieee);
        reopened.Sources.Select(source => source.Tag).Should().Equal("Knuth1997", "Doe2024", "W3C2025");
        reopened.Sources.Select(source => source.Type).Should().Equal(
            SourceType.Book,
            SourceType.JournalArticle,
            SourceType.WebSite);

        var reopenedParagraphs = reopened.Blocks.OfType<Paragraph>().ToList();
        reopenedParagraphs.Single(paragraph => paragraph.Runs.Count == 0)
            .Formatting.PageBreakBefore.Should().BeTrue();
        reopenedParagraphs.Where(paragraph =>
                paragraph.Runs.Any(run => run.ComplexField is { Keyword: "CITATION" }))
            .Should().OnlyContain(paragraph => !paragraph.Formatting.PageBreakBefore);

        var rewritten = WriteDocx(reopened);
        EntryXml(rewritten, "customXml/item1.xml").Root!.Name.Should().Be(B + "Sources");

        var complexFields = reopened.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.ComplexField is not null)
            .ToList();
        complexFields.Select(run => run.ComplexField!.Keyword)
            .Should().Contain(["CITATION", "BIBLIOGRAPHY", "TOA"]);
        complexFields.Count(run => run.ComplexField!.Keyword == "CITATION").Should().Be(5);
        complexFields.Should().Contain(run =>
            run.ComplexField!.Keyword == "BIBLIOGRAPHY"
            && run.Text == "References");
        complexFields.Should().Contain(run =>
            run.ComplexField!.Keyword == "TOA"
            && run.Text == "Cases\t1, 2");

        var generatedToaEntries = reopened.Blocks.OfType<Paragraph>()
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Where(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId)
            .Select(paragraph => paragraph.PlainText)
            .ToList();
        generatedToaEntries.Should().Contain([
            "Example v. FreeW, 123 F.4th 456 (2026)\t1, 2",
            "Free Software Evidence Act, 42 U.S.C. 2026\t1"
        ]);

        var rebuiltToaEntries = TableOfAuthorities.Build(reopened, new ToaOptions { TabLeader = ToaTabLeader.Dots })
            .Where(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId)
            .Select(paragraph => paragraph.PlainText)
            .ToList();
        // The serialized break follows the first authority mark, so the regenerated TOA preserves Word's
        // physical page references rather than moving the whole preceding citation block to page 2.
        rebuiltToaEntries.Should().Contain([
            "Example v. FreeW, 123 F.4th 456 (2026)\t1, 2",
            "Free Software Evidence Act, 42 U.S.C. 2026\t1"
        ]);
    }

    private static TextDocument ReferencesHeavyFieldsDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.BibliographyStyle = CitationStyle.Ieee;

        var book = new Source
        {
            Tag = "Knuth1997",
            Type = SourceType.Book,
            Author = "Knuth, Donald",
            Title = "The Art of Computer Programming",
            Publisher = "Addison-Wesley",
            Year = "1997"
        };
        var article = new Source
        {
            Tag = "Doe2024",
            Type = SourceType.JournalArticle,
            Author = "Jane Q. Doe; Alex Smith",
            Title = "Evidence-first document rendering",
            Journal = "Journal of Document Systems",
            Volume = "42",
            Issue = "2",
            Pages = "12-20",
            Year = "2024"
        };
        var web = new Source
        {
            Tag = "W3C2025",
            Type = SourceType.WebSite,
            Author = "World Wide Web Consortium",
            Title = "Digital publishing accessibility notes",
            Url = "https://www.w3.org/",
            Accessed = "2026-07-04",
            Year = "2025"
        };
        doc.Sources.AddRange([book, article, web]);

        doc.Blocks.Add(CitationParagraph(doc, "Numeric citations: ", [book, article, web]));
        doc.Blocks.Add(CitationParagraph(doc, "Repeated citations: ", [article, book]));

        var caseCitation = new Citation("Example v. FreeW, 123 F.4th 456 (2026)", CitationCategory.Cases, "Example");
        var statuteCitation = new Citation("Free Software Evidence Act, 42 U.S.C. 2026", CitationCategory.Statutes, "FSEA");
        var firstPageAuthorities = new Paragraph();
        firstPageAuthorities.Runs.Add(new Run("Marked authorities on page one."));
        firstPageAuthorities.Runs.Add(Run.CitationMark(caseCitation));
        firstPageAuthorities.Runs.Add(Run.CitationMark(statuteCitation));
        doc.Blocks.Add(firstPageAuthorities);
        doc.Blocks.Add(DocumentOps.CreatePageBreak());

        var secondPageAuthority = new Paragraph();
        secondPageAuthority.Runs.Add(new Run("Second-page repeated authority mark."));
        secondPageAuthority.Runs.Add(Run.CitationMark(caseCitation));
        doc.Blocks.Add(secondPageAuthority);

        doc.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" BIBLIOGRAPHY \\l 1033 ", "References") }
        });
        doc.Blocks.AddRange(Citations.BuildBibliography(doc, CitationStyle.Ieee));
        doc.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" TOA \\h \\c \"1\" ", "Cases\t1, 2") }
        });
        doc.Blocks.AddRange(TableOfAuthorities.Build(doc, new ToaOptions { TabLeader = ToaTabLeader.Dots }));

        return doc;
    }

    private static Paragraph CitationParagraph(TextDocument document, string prefix, IReadOnlyList<Source> sources)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(prefix));
        foreach (var source in sources)
        {
            if (Citations.TryCreateCitationFieldRun(document, source, document.BibliographyStyle, out var run))
                paragraph.Runs.Add(run);
        }

        return paragraph;
    }
}
