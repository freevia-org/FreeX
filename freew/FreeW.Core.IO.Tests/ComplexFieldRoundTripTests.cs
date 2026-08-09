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
    [InlineData(" SECTION \\* ROMAN ", "II")]
    [InlineData(" SECTIONPAGES ", "7")]
    [InlineData(" DATE \\@ \"M/d/yyyy\" ", "6/19/2026")]
    [InlineData(" FILENAME ", "Report.docx")]
    [InlineData(" AUTHOR ", "Ada Lovelace")]
    [InlineData(" SEQ Figure \\r 14 \\* ROMAN ", "XIV")]
    [InlineData(" SEQ Figure \\r 14 \\* roman ", "xiv")]
    [InlineData(" SEQ Figure \\r 27 \\* ALPHABETIC ", "AA")]
    [InlineData(" SEQ Figure \\r 27 \\* alphabetic ", "aa")]
    [InlineData(" SEQ Figure \\r 14 \\* MERGEFORMAT \\* ROMAN ", "XIV")]
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
        paragraph.Runs.Add(Run.ComplexFieldRun(
            MailMerge.AddressBlockInstruction,
            $"{MailMerge.FieldOpen}AddressBlock{MailMerge.FieldClose}"));
        paragraph.Runs.Add(Run.ComplexFieldRun(
            MailMerge.GreetingLineInstruction,
            $"{MailMerge.FieldOpen}GreetingLine{MailMerge.FieldClose}"));
        doc.Blocks.Add(paragraph);

        DocumentXml(doc).Descendants(W + "instrText").Select(element => element.Value).Should().Equal(
            " NEXT ",
            " MERGEREC ",
            " MERGESEQ ",
            " ADDRESSBLOCK \\* MERGEFORMAT ",
            " GREETINGLINE \\f \"<<_BEFORE_ Dear >><<_TITLE0_ >><<_LAST0_>><<_AFTER_ ,>>\" \\e \"Dear Sir or Madam,\" \\l 1033 \\* MERGEFORMAT ");

        var fields = RoundTrip(doc).Blocks.OfType<Paragraph>().Single().Runs;
        fields.Select(run => run.ComplexField!.Keyword).Should().Equal(
            MailMerge.NextRecordInstruction,
            MailMerge.MergeRecordNumberInstruction,
            MailMerge.MergeSequenceNumberInstruction,
            "ADDRESSBLOCK",
            "GREETINGLINE");
        fields.Select(run => run.Text).Should().Equal(
            $"{MailMerge.FieldOpen}{MailMerge.NextRecordField}{MailMerge.FieldClose}",
            $"{MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose}",
            $"{MailMerge.FieldOpen}{MailMerge.MergeSequenceNumberField}{MailMerge.FieldClose}",
            $"{MailMerge.FieldOpen}AddressBlock{MailMerge.FieldClose}",
            $"{MailMerge.FieldOpen}GreetingLine{MailMerge.FieldClose}");
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
    public void ComplexField_PreservesOuterBeginLockAndDirtyAcrossReaderPaths()
    {
        var loaded = ReadAuthoredDocument(
            new XElement(W + "p",
                FieldCharacterRun("begin", isLocked: true, isDirty: true),
                InstrText(W, " STYLEREF 1 "),
                FldChar(W, "separate"),
                TextRun(W, "Locked direct"),
                FldChar(W, "end")),
            new XElement(W + "p",
                new XElement(W + "sdt",
                    new XElement(W + "sdtPr",
                        new XElement(W + "tag", new XAttribute(W + "val", "LockedField"))),
                    new XElement(W + "sdtContent",
                        FieldCharacterRun("begin", isLocked: true, isDirty: true),
                        InstrText(W, " DOCPROPERTY Title "),
                        FldChar(W, "separate"),
                        TextRun(W, "Locked control"),
                        FldChar(W, "end")))));

        var fields = loaded.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.ComplexField is not null)
            .ToArray();
        fields.Should().HaveCount(2);
        fields.Should().OnlyContain(run =>
            run.ComplexField!.Sequence == new ComplexFieldSequenceMetadata(true, true));
        fields.Should().OnlyContain(run => run.ComplexField!.IsLocked && run.ComplexField.IsDirty);

        var xml = DocumentXml(loaded);
        var beginCharacters = xml.Descendants(W + "fldChar")
            .Where(field => field.Attribute(W + "fldCharType")?.Value == "begin")
            .ToArray();
        beginCharacters.Should().HaveCount(2);
        foreach (var field in beginCharacters)
        {
            field.Attribute(W + "fldLock")!.Value.Should().Be("1");
            field.Attribute(W + "dirty")!.Value.Should().Be("1");
        }
        foreach (var field in xml.Descendants(W + "fldChar")
                     .Where(field => field.Attribute(W + "fldCharType")?.Value != "begin"))
        {
            field.Attribute(W + "fldLock").Should().BeNull();
            field.Attribute(W + "dirty").Should().BeNull();
        }

        var reopened = RoundTrip(loaded).Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.ComplexField is not null)
            .ToArray();
        reopened.Should().HaveCount(2);
        reopened.Should().OnlyContain(run =>
                run.ComplexField!.Sequence == new ComplexFieldSequenceMetadata(true, true));
        reopened[1].Control!.Tag.Should().Be("LockedField");
    }

    [Fact]
    public void SpanningComplexField_PreservesOuterBeginLockAndDirty()
    {
        var loaded = ReadAuthoredDocument(
            new XElement(W + "p",
                FieldCharacterRun("begin", isLocked: true, isDirty: true),
                InstrText(W, " TOC \\o \"1-3\" "),
                FldChar(W, "separate"),
                TextRun(W, "Heading")),
            new XElement(W + "p",
                TextRun(W, "More headings"),
                FldChar(W, "end")));

        var paragraphs = loaded.Blocks.OfType<Paragraph>().ToArray();
        paragraphs[0].SpanningFieldStart!.Sequence
            .Should().Be(new ComplexFieldSequenceMetadata(true, true));
        paragraphs[0].SpanningFieldStart!.IsLocked.Should().BeTrue();

        var savedBegin = DocumentXml(loaded).Descendants(W + "fldChar")
            .Single(field => field.Attribute(W + "fldCharType")?.Value == "begin");
        savedBegin.Attribute(W + "fldLock")!.Value.Should().Be("1");
        savedBegin.Attribute(W + "dirty")!.Value.Should().Be("1");

        var reopened = RoundTrip(loaded).Blocks.OfType<Paragraph>().ToArray();
        reopened[0].SpanningFieldStart!.Sequence
            .Should().Be(new ComplexFieldSequenceMetadata(true, true));
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
    public void ComplexField_NestedField_PreservesInnerOwnershipAcrossRoundTrip()
    {
        // Hand-author an IF whose first operand is a nested PAGE field. The editor still exposes one
        // visible outer run, but the model and package must retain the inner field as an independently
        // updateable sequence rather than flattening PAGE into the IF instruction.
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
                                TextRun(w, "7"),
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
        var imported = DocxReader.Read(outStream);
        var run = imported.Blocks.OfType<Paragraph>().Single().Runs.Single();
        run.ComplexField.Should().NotBeNull();
        run.ComplexField!.Instruction.Should().Be(" IF 7");
        run.Text.Should().Be("yes");
        var nested = run.ComplexField.NestedFields.Should().ContainSingle().Subject;
        nested.Field.Instruction.Should().Be(" PAGE ");
        nested.CachedResult.Should().Be("7");
        nested.Placement.Should().Be(NestedComplexFieldPlacement.Instruction);
        nested.Offset.Should().Be(4);
        nested.Length.Should().Be(1);

        using var saved = new MemoryStream();
        DocxWriter.Write(imported, saved);
        saved.Position = 0;
        using (var package = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        using (var xmlStream = package.GetEntry("word/document.xml")!.Open())
        {
            var xml = XDocument.Load(xmlStream);
            xml.Descendants(W + "fldChar")
                .Count(element => element.Attribute(W + "fldCharType")?.Value == "begin")
                .Should().Be(2);
            xml.Descendants(W + "fldChar")
                .Count(element => element.Attribute(W + "fldCharType")?.Value == "end")
                .Should().Be(2);
            xml.Descendants(W + "instrText").Select(element => element.Value)
                .Should().ContainInOrder(" IF ", " PAGE ");
        }

        saved.Position = 0;
        var reopened = DocxReader.Read(saved).Blocks.OfType<Paragraph>().Single().Runs.Single();
        reopened.ComplexField!.Instruction.Should().Be(" IF 7");
        reopened.ComplexField.NestedFields.Should().ContainSingle()
            .Which.Field.Instruction.Should().Be(" PAGE ");
    }

    [Fact]
    public void ComplexField_NestedResultField_PreservesPlacementAndSequenceMetadata()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(
                    " TOC ",
                    "Chapter\t3",
                    nestedFields:
                    [
                        new NestedComplexField(
                            new ComplexField(
                                " PAGEREF chapter ",
                                Sequence: new ComplexFieldSequenceMetadata(IsDirty: true)),
                            "3",
                            NestedComplexFieldPlacement.Result,
                            Offset: 8,
                            Length: 1)
                    ])
            }
        });

        var xml = DocumentXml(doc);
        xml.Descendants(W + "fldChar")
            .Count(element => element.Attribute(W + "fldCharType")?.Value == "begin")
            .Should().Be(2);
        xml.Descendants(W + "fldChar")
            .Single(element => element.Attribute(W + "fldCharType")?.Value == "begin"
                && element.Attribute(W + "dirty") is not null)
            .Attribute(W + "dirty")!.Value.Should().Be("1");

        var reopened = RoundTrip(doc).Blocks.OfType<Paragraph>().Single().Runs.Single();
        reopened.Text.Should().Be("Chapter\t3");
        var nested = reopened.ComplexField!.NestedFields.Should().ContainSingle().Subject;
        nested.Placement.Should().Be(NestedComplexFieldPlacement.Result);
        nested.Offset.Should().Be(8);
        nested.Length.Should().Be(1);
        nested.Field.Instruction.Should().Be(" PAGEREF chapter ");
        nested.Field.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void ComplexField_NestedFieldFollowedByMoreOuterInstruction_KeepsOuterInstructionAndResultUncorrupted()
    {
        // This covers ONE of the three field-accumulator sites in DocxReader: ReadParagraph's plain-run
        // branch (a w:r child directly on the paragraph). ComplexField_NestedFieldInHyperlinkBranch_...
        // and ComplexField_NestedFieldInTrackedInsertion_... below exercise the other two (the
        // w:hyperlink branch, and the AddParagraphRuns helper used for w:ins/w:del/w:sdt/etc.) with the
        // identical corruption shape -- each site has its own copy of the separate-tracking state
        // machine, so a regression in only one of them would be invisible to the other two tests.
        //
        // Unlike ComplexField_NestedField_PreservesInnerOwnershipAcrossRoundTrip (whose inner field's "separate"
        // is immediately followed by "end", so the outer field never has to accumulate MORE instruction
        // text once the inner field closes), this hand-authors the shape that actually triggers the bug:
        // an outer field (e.g. IF) with an inner field (e.g. PAGE) embedded in its instruction, followed
        // by MORE outer instruction text ("EXTRA_TAIL") AFTER the inner field's own end fldChar and
        // BEFORE the outer field's own separate. A shared (non-nesting-aware) "past separate" flag gets
        // latched true by the inner field's separate and never resets, so that trailing outer instruction
        // text is misrouted into the outer RESULT instead of the outer INSTRUCTION, corrupting both.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph());
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);

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
                                FldChar(w, "begin"),        // outer (IF) begin
                                InstrText(w, " IF "),       // outer instruction, part 1
                                FldChar(w, "begin"),        // inner (PAGE) begin
                                InstrText(w, " PAGE "),     // inner instruction (flat, still pre-separate)
                                FldChar(w, "separate"),     // inner separate: latches the shared flag when buggy
                                FldChar(w, "end"),          // inner end: no cached text for the inner field
                                InstrText(w, " EXTRA_TAIL "), // outer instruction, part 2 (AFTER inner closed)
                                FldChar(w, "separate"),     // outer's OWN separate
                                TextRun(w, "yes"),          // outer's cached result
                                FldChar(w, "end"))));       // outer end: collapses
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
        // The trailing instruction fragment must land in the INSTRUCTION, not be swallowed into the result.
        run.ComplexField!.Instruction.Should().Contain("IF");
        run.ComplexField.Instruction.Should().Contain("EXTRA_TAIL");
        run.ComplexField.NestedFields.Should().ContainSingle()
            .Which.Field.Instruction.Should().Be(" PAGE ");
        // The cached RESULT must be exactly the outer field's own post-separate text, not polluted with
        // the pre-separate "EXTRA_TAIL" instruction fragment that a latched flag would misroute into it.
        run.Text.Should().Be("yes");
    }

    [Fact]
    public void ComplexField_NestedFieldInHyperlinkBranch_KeepsOuterInstructionAndResultUncorrupted()
    {
        // Same corruption shape as the plain-run-branch test above, but the nested field's begin/
        // separate/end sequence -- and the outer field's trailing instruction fragment after it -- sit
        // inside a w:hyperlink. TOC/INDEX/HYPERLINK fields always wrap their post-separate result (and
        // any PAGEREF field nested in it) in a w:hyperlink, and ReadParagraph handles that with its OWN
        // copy of the separate-tracking logic (a second loop, over the hyperlink's child runs). A
        // regression that reverts only THAT copy back to a flat, non-nesting-aware "past separate" flag
        // would still pass the plain-run-branch test, since that test never routes anything through a
        // w:hyperlink.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph());
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);

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
                                FldChar(w, "begin"),          // outer (TOC) begin
                                InstrText(w, " TOC "),        // outer instruction, part 1
                                new XElement(w + "hyperlink",
                                    FldChar(w, "begin"),          // inner (PAGEREF) begin -- inside the hyperlink
                                    InstrText(w, " PAGEREF "),    // inner instruction (no cached text of its own)
                                    FldChar(w, "separate"),       // inner separate: latches the shared flag when buggy
                                    FldChar(w, "end"),             // inner end: closes the nested field
                                    InstrText(w, " EXTRA_TAIL ")), // outer instruction, part 2 -- still inside the
                                                                    // hyperlink, AFTER the inner field closed
                                FldChar(w, "separate"),        // outer's OWN separate (back on the plain-run branch)
                                TextRun(w, "yes"),              // outer's cached result
                                FldChar(w, "end"))));            // outer end: collapses
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
        // The trailing instruction fragment must land in the INSTRUCTION, not be swallowed into the result.
        run.ComplexField!.Instruction.Should().Contain("TOC");
        run.ComplexField.Instruction.Should().Contain("EXTRA_TAIL");
        run.ComplexField.NestedFields.Should().ContainSingle()
            .Which.Field.Instruction.Should().Be(" PAGEREF ");
        // The cached RESULT must be exactly the outer field's own post-separate text.
        run.Text.Should().Be("yes");
    }

    [Fact]
    public void ComplexField_NestedFieldInTrackedInsertion_KeepsOuterInstructionAndResultUncorrupted()
    {
        // Same corruption shape again, but the entire field sequence sits inside a w:ins (a tracked
        // insertion), which routes through AddParagraphRuns -- a THIRD, separate copy of the field
        // accumulator and separate-tracking logic (also used for w:del/w:sdt/w:smartTag/w:customXml/
        // w:dir/w:bdo content). A regression that reverts only THIS copy would still pass both of the
        // ReadParagraph-branch tests above, since neither of them goes through AddParagraphRuns.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph());
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);

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
                                new XElement(w + "ins",
                                    new XAttribute(w + "id", "1"),
                                    new XAttribute(w + "author", "Test"),
                                    new XAttribute(w + "date", "2026-01-01T00:00:00Z"),
                                    FldChar(w, "begin"),          // outer (IF) begin
                                    InstrText(w, " IF "),         // outer instruction, part 1
                                    FldChar(w, "begin"),          // inner (PAGE) begin
                                    InstrText(w, " PAGE "),       // inner instruction (no cached text of its own)
                                    FldChar(w, "separate"),       // inner separate: latches the shared flag when buggy
                                    FldChar(w, "end"),              // inner end: closes the nested field
                                    InstrText(w, " EXTRA_TAIL "), // outer instruction, part 2 (AFTER inner closed)
                                    FldChar(w, "separate"),       // outer's OWN separate
                                    TextRun(w, "yes"),              // outer's cached result
                                    FldChar(w, "end")))));          // outer end: collapses
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
        run.ComplexField!.Instruction.Should().Contain("IF");
        run.ComplexField.Instruction.Should().Contain("EXTRA_TAIL");
        run.ComplexField.NestedFields.Should().ContainSingle()
            .Which.Field.Instruction.Should().Be(" PAGE ");
        run.Text.Should().Be("yes");
        run.Revision.Should().Be(RevisionKind.Inserted);
    }

    [Fact]
    public void ComplexField_SimpleNonNestedField_InstructionAndResultRouteCorrectly()
    {
        // Sibling/no-regression coverage: an ordinary (non-nested) field must still route its
        // pre-separate instruction and post-separate cached result correctly after the nesting-aware
        // separate tracking change — distinctive, non-default values on both sides so the assertion
        // cannot pass by coincidence.
        var run = RoundTrip(WithComplexField(" AUTHOR \\* Upper ", "ADA LOVELACE"))
            .Blocks.OfType<Paragraph>().Single().Runs.Single();

        run.ComplexField.Should().NotBeNull();
        run.ComplexField!.Instruction.Should().Be(" AUTHOR \\* Upper ");
        run.Text.Should().Be("ADA LOVELACE");
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

    private static XElement FieldCharacterRun(string type, bool isLocked, bool isDirty)
    {
        var character = new XElement(W + "fldChar", new XAttribute(W + "fldCharType", type));
        if (isLocked)
            character.Add(new XAttribute(W + "fldLock", "1"));
        if (isDirty)
            character.Add(new XAttribute(W + "dirty", "true"));
        return new XElement(W + "r", character);
    }

    private static TextDocument ReadAuthoredDocument(params XElement[] paragraphs)
    {
        using var sourceStream = new MemoryStream();
        DocxWriter.Write(TextDocument.CreateEmpty(), sourceStream);
        using var authoredStream = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(sourceStream.ToArray()), ZipArchiveMode.Read))
        using (var authored = new ZipArchive(authoredStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var copy = authored.CreateEntry(entry.FullName);
                using var input = entry.Open();
                using var output = copy.Open();
                if (entry.FullName == "word/document.xml")
                {
                    new XDocument(
                        new XElement(W + "document",
                            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                            new XElement(W + "body", paragraphs)))
                        .Save(output);
                }
                else
                {
                    input.CopyTo(output);
                }
            }
        }

        authoredStream.Position = 0;
        return DocxReader.Read(authoredStream);
    }

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
