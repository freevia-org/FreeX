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
}
