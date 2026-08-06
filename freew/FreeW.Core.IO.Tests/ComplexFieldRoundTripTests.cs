using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for generic Word fields (Insert &gt; Quick Parts &gt; Field), including the complex
/// <c>w:fldChar</c> sequence and unmodelled <c>w:fldSimple</c> storage carried by
/// <see cref="Run.ComplexField"/>. The instruction is preserved verbatim so any field code (PAGE,
/// NUMPAGES, DATE with a \@ picture, FILENAME, AUTHOR, or an unmodelled one) survives a save+reload, and
/// the cached result rides along on the run text.
/// </summary>
public class ComplexFieldRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XElement DocumentXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry).Root!;
    }

    private static TextDocument WithComplexField(string instruction, string result)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.ComplexFieldRun(instruction, result));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [Theory]
    [InlineData(" PAGE ", "1")]
    [InlineData(" NUMPAGES ", "3")]
    [InlineData(" DATE \\@ \"M/d/yyyy\" ", "6/19/2026")]
    [InlineData(" FILENAME ", "Report.docx")]
    [InlineData(" AUTHOR ", "Ada Lovelace")]
    public void ComplexField_SurvivesRoundTrip(string instruction, string cached)
    {
        var run = RoundTrip(WithComplexField(instruction, cached))
            .Blocks.OfType<Paragraph>().Single().Runs.Single();

        run.ComplexField.Should().NotBeNull();
        run.ComplexField!.Instruction.Should().Be(instruction);
        // The cached result rides along on the run text (fallback for field-unaware consumers).
        run.Text.Should().Be(cached);
    }

    [Fact]
    public void ComplexField_EmitsFldCharBeginSeparateEnd_WithInstrText()
    {
        var root = DocumentXml(WithComplexField(" PAGE ", "1"));

        // The complex field serialises as the begin/separate/end fldChar sequence, not a self-contained
        // w:fldSimple — this is what makes Alt+F9/F9 and arbitrary fields possible.
        var fldChars = root.Descendants(W + "fldChar")
            .Select(c => c.Attribute(W + "fldCharType")?.Value)
            .ToList();
        fldChars.Should().Equal("begin", "separate", "end");

        var instrText = root.Descendants(W + "instrText").Single();
        instrText.Value.Should().Be(" PAGE ");
        // The instruction keeps its surrounding whitespace via xml:space="preserve".
        instrText.Attribute(XNamespace.Xml + "space")!.Value.Should().Be("preserve");

        root.Descendants(W + "fldSimple").Should().BeEmpty();
    }

    [Fact]
    public void IndexMark_EmitsInstructionOnlyXeFieldAndReopensAsDurableOccurrence()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph
        {
            Runs = { new Run("Alpha"), DocumentIndex.MarkRun("Alpha topic") }
        });

        var root = DocumentXml(doc);
        root.Descendants(W + "fldChar")
            .Select(element => element.Attribute(W + "fldCharType")?.Value)
            .Should().Equal("begin", "end");
        root.Descendants(W + "instrText").Single().Value.Should().Be(" XE \"Alpha topic\" ");

        var reopened = RoundTrip(doc);
        var mark = reopened.Blocks.OfType<Paragraph>().Single().Runs.Single(run => run.ComplexField is not null);
        DocumentIndex.MarkedTerm(mark).Should().Be("Alpha topic");
        DocumentIndex.Build(reopened).Select(paragraph => paragraph.PlainText)
            .Should().Equal("A", "Alpha topic, 1");
    }

    [Fact]
    public void HierarchicalCrossReferenceIndexMark_RoundTripsExactXeSwitches()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Transport"),
                DocumentIndex.MarkRun(new IndexMark("Transportation", "Rail", "See Trains"))
            }
        });

        var root = DocumentXml(doc);
        root.Descendants(W + "fldChar")
            .Select(element => element.Attribute(W + "fldCharType")?.Value)
            .Should().Equal("begin", "end");
        root.Descendants(W + "instrText").Single().Value
            .Should().Be(" XE \"Transportation:Rail\" \\t \"See Trains\" ");

        var reopened = RoundTrip(doc);
        var run = reopened.Blocks.OfType<Paragraph>().Single().Runs.Single(candidate => candidate.ComplexField is not null);
        DocumentIndex.MarkedEntry(run).Should().Be(new IndexMark("Transportation", "Rail", "See Trains"));
        DocumentIndex.Build(reopened).Select(paragraph => paragraph.PlainText)
            .Should().Equal("T", "Transportation", "Rail. See Trains");
    }

    [Fact]
    public void IndexPageNumberFormatting_RoundTripsBoldAndItalicXeSwitches()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Alpha"),
                DocumentIndex.MarkRun(new IndexMark(
                    "Alpha",
                    BoldPageNumber: true,
                    ItalicPageNumber: true))
            }
        });

        DocumentXml(doc).Descendants(W + "instrText").Single().Value
            .Should().Be(" XE \"Alpha\" \\b \\i ");

        var reopened = RoundTrip(doc);
        var run = reopened.Blocks.OfType<Paragraph>().Single().Runs.Single(candidate => candidate.ComplexField is not null);
        DocumentIndex.MarkedEntry(run).Should().Be(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            ItalicPageNumber: true));
        var pageRun = DocumentIndex.Build(reopened)
            .Single(paragraph => paragraph.PlainText == "Alpha, 1")
            .Runs[^1];
        pageRun.Text.Should().Be("1");
        pageRun.Formatting.Bold.Should().BeTrue();
        pageRun.Formatting.Italic.Should().BeTrue();
    }

    [Fact]
    public void IndexBookmarkPageRange_RoundTripsExactXeSwitch()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Alpha"),
                DocumentIndex.MarkRun(new IndexMark("Alpha", BookmarkName: "TopicRange"))
            }
        });

        DocumentXml(doc).Descendants(W + "instrText").Single().Value
            .Should().Be(" XE \"Alpha\" \\r \"TopicRange\" ");

        var reopened = RoundTrip(doc);
        var run = reopened.Blocks.OfType<Paragraph>().Single().Runs.Single(candidate => candidate.ComplexField is not null);
        DocumentIndex.MarkedEntry(run).Should().Be(new IndexMark("Alpha", BookmarkName: "TopicRange"));
    }

    [Fact]
    public void AlternateIndexIdentifier_RoundTripsExactXeSwitch()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Alpha"),
                DocumentIndex.MarkRun(new IndexMark("Alpha", Identifier: "People"))
            }
        });

        DocumentXml(doc).Descendants(W + "instrText").Single().Value
            .Should().Be(" XE \"Alpha\" \\f \"People\" ");

        var reopened = RoundTrip(doc);
        var run = reopened.Blocks.OfType<Paragraph>().Single().Runs.Single(candidate => candidate.ComplexField is not null);
        DocumentIndex.MarkedEntry(run).Should().Be(new IndexMark("Alpha", Identifier: "People"));
        DocumentIndex.Build(reopened, identifier: "People").Select(paragraph => paragraph.PlainText)
            .Should().Equal("A", "Alpha, 1");
        DocumentIndex.Build(reopened).Should().BeEmpty();
    }

    [Fact]
    public void AlternateIndexGeneratedRegion_RoundTripsIdentifierSpecificStyles()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")) }
        });
        DocumentIndex.EnsureStyles(doc, "People");
        doc.Blocks.AddRange(DocumentIndex.Build(doc, identifier: "People"));

        var root = DocumentXml(doc);
        var regionXml = root.Descendants(W + "p")
            .Where(paragraph => paragraph.Element(W + "pPr")?.Element(W + "pStyle")?
                .Attribute(W + "val")?.Value is { } styleId
                && (styleId == DocumentIndex.HeadingStyleIdFor("People")
                    || styleId == DocumentIndex.EntryStyleIdFor("People")))
            .ToArray();
        regionXml.SelectMany(paragraph => paragraph.Descendants(W + "fldChar"))
            .Select(field => field.Attribute(W + "fldCharType")?.Value)
            .Should().Equal("begin", "separate", "end");
        regionXml.SelectMany(paragraph => paragraph.Descendants(W + "instrText"))
            .Should().ContainSingle()
            .Which.Value.Should().Be(" INDEX \\f \"People\" \\h \"A\" \\z \"1033\" ");

        var reopened = RoundTrip(doc);
        var region = reopened.Blocks.Where(block => DocumentIndex.IsIndexParagraph(block, "People"))
            .Cast<Paragraph>()
            .ToArray();

        region.Should().HaveCount(2);
        region.Select(paragraph => paragraph.PlainText).Should().Equal("A", "Ada, 1");
        region.Should().OnlyContain(block => !DocumentIndex.IsIndexParagraph(block, identifier: null));
        region[0].SpanningFieldStart!.Instruction
            .Should().Be(" INDEX \\f \"People\" \\h \"A\" \\z \"1033\" ");
        region.Should().OnlyContain(paragraph =>
            paragraph.SpanningFieldOwner != null
            && paragraph.SpanningFieldOwner.Instruction == " INDEX \\f \"People\" \\h \"A\" \\z \"1033\" ");
        region[0].EndsSpanningField.Should().BeFalse();
        region[1].SpanningFieldStart.Should().BeNull();
        region[1].EndsSpanningField.Should().BeTrue();
        reopened.Styles.Should().ContainKey(DocumentIndex.HeadingStyleIdFor("People"));
        reopened.Styles.Should().ContainKey(DocumentIndex.EntryStyleIdFor("People"));
    }

    [Fact]
    public void NativeMailMergeControlFields_EmitInstructionsAndRoundTripCachedLabels()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.ComplexFieldRun(
            $" {MailMerge.NextRecordInstruction} ",
            $"{MailMerge.FieldOpen}{MailMerge.NextRecordField}{MailMerge.FieldClose}"));
        paragraph.Runs.Add(Run.ComplexFieldRun(
            $" {MailMerge.MergeRecordNumberInstruction} ",
            $"{MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose}"));
        paragraph.Runs.Add(Run.ComplexFieldRun(
            $" {MailMerge.MergeSequenceNumberInstruction} ",
            $"{MailMerge.FieldOpen}{MailMerge.MergeSequenceNumberField}{MailMerge.FieldClose}"));
        doc.Blocks.Add(paragraph);

        DocumentXml(doc).Descendants(W + "instrText").Select(element => element.Value).Should().Equal(
            " NEXT ",
            " MERGEREC ",
            " MERGESEQ ");

        var fields = RoundTrip(doc).Blocks.OfType<Paragraph>().Single().Runs;
        fields.Select(run => run.ComplexField!.Keyword).Should().Equal(
            MailMerge.NextRecordInstruction,
            MailMerge.MergeRecordNumberInstruction,
            MailMerge.MergeSequenceNumberInstruction);
        fields.Select(run => run.Text).Should().Equal(
            $"{MailMerge.FieldOpen}{MailMerge.NextRecordField}{MailMerge.FieldClose}",
            $"{MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose}",
            $"{MailMerge.FieldOpen}{MailMerge.MergeSequenceNumberField}{MailMerge.FieldClose}");
    }

    [Fact]
    public void ComplexField_PreservesUnmodelledInstructionVerbatim()
    {
        // A field FreeW does not specifically model (here a MERGEFIELD) must still round-trip its raw
        // instruction rather than being flattened to its cached text and losing the field.
        var run = RoundTrip(WithComplexField(" MERGEFIELD FirstName \\* MERGEFORMAT ", "John"))
            .Blocks.OfType<Paragraph>().Single().Runs.Single();

        run.ComplexField.Should().NotBeNull();
        run.ComplexField!.Instruction.Should().Be(" MERGEFIELD FirstName \\* MERGEFORMAT ");
        run.ComplexField.Keyword.Should().Be("MERGEFIELD");
        run.Text.Should().Be("John");
    }

    [Fact]
    public void UnmodelledSimpleField_PreservesInstructionFlagsCachedTextAndStorageForm()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph());
        using var sourceStream = new MemoryStream();
        DocxWriter.Write(doc, sourceStream);

        using var authoredStream = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(sourceStream.ToArray()), ZipArchiveMode.Read))
        using (var authored = new ZipArchive(authoredStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var copy = authored.CreateEntry(entry.FullName);
                using var input = entry.Open();
                using var output = copy.Open();
                if (entry.FullName != "word/document.xml")
                {
                    input.CopyTo(output);
                    continue;
                }

                var document = new XElement(W + "document",
                    new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                    new XElement(W + "body",
                        new XElement(W + "p",
                            new XElement(W + "hyperlink",
                                new XAttribute(W + "anchor", "Target"),
                                new XElement(W + "fldSimple",
                                    new XAttribute(W + "instr", " DOCPROPERTY \"Company\" "),
                                    new XAttribute(W + "fldLock", "1"),
                                    new XAttribute(W + "dirty", "true"),
                                    new XElement(W + "r",
                                        new XElement(W + "rPr", new XElement(W + "b")),
                                        new XElement(W + "t", "Contoso")))),
                            new XElement(W + "sdt",
                                new XElement(W + "sdtPr",
                                    new XElement(W + "tag", new XAttribute(W + "val", "SimpleFieldControl"))),
                                new XElement(W + "sdtContent",
                                    new XElement(W + "fldSimple",
                                        new XAttribute(W + "instr", " CUSTOMEMPTY "),
                                        new XAttribute(W + "fldLock", "0"),
                                        new XAttribute(W + "dirty", "false"),
                                        new XElement(W + "r", new XElement(W + "t", string.Empty))))))));
                new XDocument(document).Save(output);
            }
        }

        authoredStream.Position = 0;
        var loaded = DocxReader.Read(authoredStream);
        var runs = loaded.Blocks.OfType<Paragraph>().Single().Runs;

        runs.Should().HaveCount(2);
        runs[0].Text.Should().Be("Contoso");
        runs[0].Formatting.Bold.Should().BeTrue();
        runs[0].ComplexField.Should().Be(new ComplexField(
            " DOCPROPERTY \"Company\" ",
            SimpleField: new SimpleFieldMetadata(IsLocked: true, IsDirty: true)));
        runs[0].HyperlinkAnchor.Should().Be("Target");
        runs[1].Text.Should().BeEmpty();
        runs[1].ComplexField.Should().Be(new ComplexField(
            " CUSTOMEMPTY ",
            SimpleField: new SimpleFieldMetadata()));
        runs[1].Control!.Tag.Should().Be("SimpleFieldControl");

        var savedFields = DocumentXml(loaded).Descendants(W + "fldSimple").ToList();
        savedFields.Should().HaveCount(2);
        savedFields[0].Attribute(W + "instr")!.Value.Should().Be(" DOCPROPERTY \"Company\" ");
        savedFields[0].Attribute(W + "fldLock")!.Value.Should().Be("1");
        savedFields[0].Attribute(W + "dirty")!.Value.Should().Be("1");
        savedFields[0].Descendants(W + "t").Single().Value.Should().Be("Contoso");
        savedFields[0].Ancestors(W + "hyperlink").Single().Attribute(W + "anchor")!.Value.Should().Be("Target");
        savedFields[1].Attribute(W + "instr")!.Value.Should().Be(" CUSTOMEMPTY ");
        savedFields[1].Attribute(W + "fldLock").Should().BeNull();
        savedFields[1].Attribute(W + "dirty").Should().BeNull();
        savedFields[1].Ancestors(W + "sdt").Should().ContainSingle();
        DocumentXml(loaded).Descendants(W + "fldChar").Should().BeEmpty();

        var reopened = RoundTrip(loaded).Blocks.OfType<Paragraph>().Single().Runs;
        reopened.Select(run => run.ComplexField!.Instruction).Should().Equal(
            " DOCPROPERTY \"Company\" ",
            " CUSTOMEMPTY ");
        reopened[0].ComplexField!.SimpleField.Should().Be(new SimpleFieldMetadata(true, true));
        reopened[0].HyperlinkAnchor.Should().Be("Target");
        reopened[1].ComplexField!.SimpleField.Should().Be(new SimpleFieldMetadata());
        reopened[1].Control!.Tag.Should().Be("SimpleFieldControl");
    }

    [Fact]
    public void CitationComplexField_SurvivesRoundTripWithSources()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.BibliographyStyle = CitationStyle.Ieee;
        doc.Sources.Add(new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" });
        doc.Sources.Add(new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" });
        doc.Blocks.Add(new Paragraph { Runs = { Run.ComplexFieldRun(" CITATION Tur1936 ", "[2]") } });

        var result = RoundTrip(doc);
        var run = result.Blocks.OfType<Paragraph>().Single().Runs.Single();

        run.ComplexField.Should().NotBeNull();
        run.ComplexField!.Instruction.Should().Be(" CITATION Tur1936 ");
        run.Text.Should().Be("[2]");
        result.Sources.Select(source => source.Tag).Should().Equal("Ada1843", "Tur1936");
        result.BibliographyStyle.Should().Be(CitationStyle.Ieee);
    }

    [Fact]
    public void ComplexField_PreservesRunFormatting()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.ComplexFieldRun(" AUTHOR ", "Ada",
            formatting: new RunFormatting { Bold = true, Italic = true }));
        doc.Blocks.Add(paragraph);

        var run = RoundTrip(doc).Blocks.OfType<Paragraph>().Single().Runs.Single();

        run.ComplexField.Should().NotBeNull();
        run.Formatting!.Bold.Should().BeTrue();
        run.Formatting.Italic.Should().BeTrue();
    }

    [Fact]
    public void ComplexField_ShowCode_IsNotSerialised()
    {
        // ShowCode is presentation-only state (the Alt+F9 toggle); it must not affect serialisation, so a
        // field reloaded always comes back with ShowCode off regardless of how it was displayed.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.ComplexFieldRun(" PAGE ", "1", showCode: true));
        doc.Blocks.Add(paragraph);

        var run = RoundTrip(doc).Blocks.OfType<Paragraph>().Single().Runs.Single();

        run.ComplexField!.ShowCode.Should().BeFalse();
        run.ComplexField.Instruction.Should().Be(" PAGE ");
    }

    [Fact]
    public void ComplexField_NestedField_CollapsesToOuterInstruction()
    {
        // Hand-author a nested complex field (an IF whose body is another field) to exercise the reader's
        // depth tracking: the whole span must collapse to a single ComplexField run, not leak the inner
        // begin/end or split into several runs.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph());
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);

        // Rebuild document.xml with a nested field by hand, then read it back.
        var bytes = stream.ToArray();
        using var outStream = new MemoryStream();
        using (var src = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        using (var dst = new ZipArchive(outStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in src.Entries)
            {
                var copy = dst.CreateEntry(entry.FullName);
                using var es = entry.Open();
                using var cs = copy.Open();
                if (entry.FullName == "word/document.xml")
                {
                    XNamespace w = W;
                    var body = new XElement(w + "document", new XAttribute(XNamespace.Xmlns + "w", w.NamespaceName),
                        new XElement(w + "body",
                            new XElement(w + "p",
                                FldChar(w, "begin"),
                                InstrText(w, " IF "),
                                FldChar(w, "begin"),
                                InstrText(w, " PAGE "),
                                FldChar(w, "separate"),
                                FldChar(w, "end"),
                                FldChar(w, "separate"),
                                TextRun(w, "yes"),
                                FldChar(w, "end"))));
                    new XDocument(body).Save(cs);
                }
                else
                {
                    es.CopyTo(cs);
                }
            }
        }

        outStream.Position = 0;
        var run = DocxReader.Read(outStream).Blocks.OfType<Paragraph>().Single().Runs.Single();
        run.ComplexField.Should().NotBeNull();
        // The instruction concatenates both nested instrText segments (outer IF + inner PAGE), and the
        // result is the cached "yes" after the outer separate.
        run.ComplexField!.Instruction.Should().Contain("IF");
        run.ComplexField.Instruction.Should().Contain("PAGE");
        run.Text.Should().Be("yes");
    }

    [Fact]
    public void NativeMultiParagraphToc_PreservesNestedPageReferencesAndKeepsSourceHeadingsOutsideField()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph());
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);

        using var authoredStream = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read))
        using (var authored = new ZipArchive(authoredStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var copy = authored.CreateEntry(entry.FullName);
                using var sourceEntry = entry.Open();
                using var authoredEntry = copy.Open();
                if (entry.FullName == "word/document.xml")
                {
                    var document = new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                        new XElement(W + "body",
                            TocResultParagraph("TOC1", "Chapter One", "_Toc1", startsOuterField: true),
                            TocResultParagraph("TOC2", "Section A", "_Toc2", startsOuterField: false),
                            new XElement(W + "p",
                                ParagraphStyle("Heading1"),
                                FldChar(W, "end"),
                                TextRun(W, "Chapter One")),
                            new XElement(W + "p", ParagraphStyle("Heading2"), TextRun(W, "Section A"))));
                    new XDocument(document).Save(authoredEntry);
                }
                else
                {
                    sourceEntry.CopyTo(authoredEntry);
                }
            }
        }

        authoredStream.Position = 0;
        var loaded = DocxReader.Read(authoredStream);
        var paragraphs = loaded.Blocks.OfType<Paragraph>().ToArray();

        paragraphs.Should().HaveCount(4);
        paragraphs[0].SpanningFieldStart!.Instruction.Should().Be(" TOC \\o \"1-3\" ");
        paragraphs.Take(2).All(paragraph => paragraph.SpanningFieldOwner?.Keyword == "TOC")
            .Should().BeTrue();
        paragraphs[0].EndsSpanningField.Should().BeFalse();
        paragraphs[1].EndsSpanningField.Should().BeTrue();
        paragraphs[2].SpanningFieldOwner.Should().BeNull();
        paragraphs[2].EndsSpanningField.Should().BeFalse();
        paragraphs[2].PlainText.Should().Be("Chapter One");
        paragraphs[3].PlainText.Should().Be("Section A");
        paragraphs.Take(2).SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.ComplexField is { Keyword: "PAGEREF" })
            .Should().HaveCount(2);
        paragraphs.Take(2).All(paragraph => TableOfContents.IsTocParagraph(paragraph))
            .Should().BeTrue();
        paragraphs.Skip(2).All(paragraph => !TableOfContents.IsTocParagraph(paragraph))
            .Should().BeTrue();

        var saved = DocumentXml(loaded);
        var savedParagraphs = saved.Descendants(W + "p").ToArray();
        savedParagraphs[0].Descendants(W + "instrText").Select(element => element.Value)
            .Should().Contain(" TOC \\o \"1-3\" ");
        savedParagraphs[0].Descendants(W + "fldChar").Select(FieldCharacterType)
            .Should().Equal("begin", "separate", "begin", "separate", "end");
        savedParagraphs[1].Descendants(W + "fldChar").Select(FieldCharacterType)
            .Should().Equal("begin", "separate", "end", "end");
        savedParagraphs[2].Descendants(W + "fldChar").Should().BeEmpty();
    }

    [Fact]
    public void GeneratedTableOfContents_EmitsOneNativeFieldAndReopensWithOwnedEntries()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Document Title") { StyleId = "Title" });
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Deep Six") { StyleId = "Heading6" });
        TableOfContents.EnsureStyles(doc);
        var generated = TableOfContents.Build(doc);
        for (var index = generated.Count - 1; index >= 0; index--)
            doc.Blocks.Insert(0, generated[index]);

        var root = DocumentXml(doc);
        var generatedXml = root.Descendants(W + "p").Take(generated.Count).ToArray();
        generatedXml[0].Descendants(W + "fldChar").Should().BeEmpty();
        generatedXml.SelectMany(paragraph => paragraph.Descendants(W + "instrText"))
            .Should().ContainSingle()
            .Which.Value.Should().Be(TableOfContents.NativeFieldInstruction);
        generatedXml.SelectMany(paragraph => paragraph.Descendants(W + "fldChar"))
            .Select(FieldCharacterType)
            .Should().Equal("begin", "separate", "end");

        var reopened = RoundTrip(doc);
        var toc = reopened.Blocks.Take(generated.Count).Cast<Paragraph>().ToArray();
        toc.Select(paragraph => paragraph.PlainText).Should().Equal(
            TableOfContents.HeadingText,
            "Document Title\t1",
            "Chapter One\t1",
            "Deep Six\t1");
        toc[0].SpanningFieldOwner.Should().BeNull();
        toc.Skip(1).All(paragraph =>
            paragraph.SpanningFieldOwner?.Instruction == TableOfContents.NativeFieldInstruction)
            .Should().BeTrue();
        toc[1].SpanningFieldStart!.Instruction.Should().Be(TableOfContents.NativeFieldInstruction);
        toc[^1].EndsSpanningField.Should().BeTrue();
    }

    [Fact]
    public void GeneratedTableOfFigures_EmitsNativeSequenceSourcesAndOneCaptionTableField()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, 1, "First diagram"));
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, 2, "Second diagram"));
        TableOfFigures.EnsureStyles(doc);
        var generated = TableOfFigures.Build(doc, CaptionLabel.Figure);
        for (var index = generated.Count - 1; index >= 0; index--)
            doc.Blocks.Insert(0, generated[index]);

        var root = DocumentXml(doc);
        var generatedXml = root.Descendants(W + "p").Take(generated.Count).ToArray();
        generatedXml[0].Descendants(W + "fldChar").Should().BeEmpty();
        generatedXml.SelectMany(paragraph => paragraph.Descendants(W + "instrText"))
            .Should().ContainSingle()
            .Which.Value.Should().Be(" TOC \\c \"Figure\" ");
        generatedXml.SelectMany(paragraph => paragraph.Descendants(W + "fldChar"))
            .Select(FieldCharacterType)
            .Should().Equal("begin", "separate", "end");
        root.Descendants(W + "instrText").Select(element => element.Value)
            .Should().ContainInOrder(
                " TOC \\c \"Figure\" ",
                " SEQ Figure \\* ARABIC ",
                " SEQ Figure \\* ARABIC ");

        var reopened = RoundTrip(doc);
        var table = reopened.Blocks.Take(generated.Count).Cast<Paragraph>().ToArray();
        table.Select(paragraph => paragraph.PlainText).Should().Equal(
            "Table of Figures",
            "Figure 1: First diagram\t1",
            "Figure 2: Second diagram\t1");
        table[0].SpanningFieldOwner.Should().BeNull();
        table.Skip(1).All(paragraph =>
            paragraph.SpanningFieldOwner?.Instruction == " TOC \\c \"Figure\" ")
            .Should().BeTrue();
        table[1].SpanningFieldStart!.Instruction.Should().Be(" TOC \\c \"Figure\" ");
        table[^1].EndsSpanningField.Should().BeTrue();
        table.Skip(1).All(paragraph => TableOfFigures.IsTableOfFiguresParagraph(paragraph))
            .Should().BeTrue();
        table.Skip(1).Any(paragraph => TableOfContents.IsTocParagraph(paragraph))
            .Should().BeFalse();
        reopened.Blocks.Skip(generated.Count).OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Count(run => run.ComplexField is { Keyword: "SEQ" })
            .Should().Be(2);
    }

    [Fact]
    public void ComplexField_InsideContentControl_PreservesFieldAndControl()
    {
        // Word can wrap arbitrary inline content in a structured document tag. The paragraph reader's
        // recursive content-control path must keep complex fields as fields instead of flattening the
        // fldChar/instrText sequence to only its cached result text.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph());
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);

        using var outStream = new MemoryStream();
        using (var src = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read))
        using (var dst = new ZipArchive(outStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in src.Entries)
            {
                var copy = dst.CreateEntry(entry.FullName);
                using var es = entry.Open();
                using var cs = copy.Open();
                if (entry.FullName == "word/document.xml")
                {
                    XNamespace w = W;
                    var body = new XElement(w + "document", new XAttribute(XNamespace.Xmlns + "w", w.NamespaceName),
                        new XElement(w + "body",
                            new XElement(w + "p",
                                new XElement(w + "sdt",
                                    new XElement(w + "sdtPr",
                                        new XElement(w + "tag", new XAttribute(w + "val", "FieldControl"))),
                                    new XElement(w + "sdtContent",
                                        FldChar(w, "begin"),
                                        InstrText(w, " PAGE "),
                                        FldChar(w, "separate"),
                                        TextRun(w, "7"),
                                        FldChar(w, "end"))))));
                    new XDocument(body).Save(cs);
                }
                else
                {
                    es.CopyTo(cs);
                }
            }
        }

        outStream.Position = 0;
        var run = DocxReader.Read(outStream).Blocks.OfType<Paragraph>().Single().Runs.Single();

        run.ComplexField.Should().NotBeNull();
        run.ComplexField!.Instruction.Should().Be(" PAGE ");
        run.Text.Should().Be("7");
        run.Control.Should().NotBeNull();
        run.Control!.Tag.Should().Be("FieldControl");
    }

    private static XElement FldChar(XNamespace w, string type) =>
        new(w + "r", new XElement(w + "fldChar", new XAttribute(w + "fldCharType", type)));

    private static XElement InstrText(XNamespace w, string instr) =>
        new(w + "r", new XElement(w + "instrText", new XAttribute(XNamespace.Xml + "space", "preserve"), instr));

    private static XElement TextRun(XNamespace w, string text) =>
        new(w + "r", new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));

    private static XElement TocResultParagraph(
        string styleId,
        string text,
        string bookmark,
        bool startsOuterField)
    {
        var paragraph = new XElement(W + "p", ParagraphStyle(styleId));
        if (startsOuterField)
        {
            paragraph.Add(
                FldChar(W, "begin"),
                InstrText(W, " TOC \\o \"1-3\" "),
                FldChar(W, "separate"));
        }

        paragraph.Add(
            TextRun(W, text),
            FldChar(W, "begin"),
            InstrText(W, $" PAGEREF {bookmark} \\h "),
            FldChar(W, "separate"),
            TextRun(W, "1"),
            FldChar(W, "end"));
        return paragraph;
    }

    private static XElement ParagraphStyle(string styleId) =>
        new(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", styleId)));

    private static string? FieldCharacterType(XElement element) =>
        element.Attribute(W + "fldCharType")?.Value;
}
