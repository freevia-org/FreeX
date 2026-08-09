using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// R124: DocxReader.MarkDuplicateDrawingIdentities annotates the pristine (unread) w:document.xml tree's
/// duplicate wp:docPr's parent element BEFORE body content is walked. AddBodyBlock then routed every
/// top-level body paragraph through PrepareSpanningFieldParagraph, which used to unconditionally clone the
/// paragraph via `new XElement(source)` -- even when the paragraph carried no field code at all. XElement's
/// copy constructor does not carry annotations across a clone, so ReadSmartArt (which runs against the
/// cloned paragraph) never saw the duplicate-id marker, and SmartArt.IsWordSuppressedByDuplicateDrawingId
/// came back false for every top-level-body duplicate-docPr-id diagram -- silently re-enabling a diagram
/// Word itself does not render. FreeW.Core.IO.Tests.SmartArtRoundTripTests already covers the full
/// preserved-payload contract for this scenario; this test isolates the specific defect (annotation loss
/// across the field-paragraph clone) with a minimal repro plus a same-id-collision variant.
/// </summary>
public class R124_SmartArtDuplicateDrawingIdSurvivesParagraphCloneTests
{
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument EntryXml(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(entryPath)!.Open();
        return XDocument.Load(entry);
    }

    private static byte[] WithDuplicateSecondDocPrId(byte[] sourceBytes)
    {
        using var sourceStream = new MemoryStream(sourceBytes);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read);
        var documentXml = EntryXml(sourceBytes, "word/document.xml");
        var docPrs = documentXml.Descendants(Wp + "docPr").ToList();
        docPrs.Should().HaveCount(2);
        docPrs[1].SetAttributeValue("id", docPrs[0].Attribute("id")!.Value);

        using var output = new MemoryStream();
        using (var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var target = destination.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var targetStream = target.Open();
                if (entry.FullName == "word/document.xml")
                {
                    documentXml.Save(targetStream);
                    continue;
                }

                using var sourceEntryStream = entry.Open();
                sourceEntryStream.CopyTo(targetStream);
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// FAIL-BEFORE / PASS-AFTER: a plain top-level body paragraph (no field codes at all -- the common
    /// case that AddBodyBlock still routes through PrepareSpanningFieldParagraph) whose SmartArt reuses an
    /// earlier drawing's wp:docPr id must come back marked as Word-suppressed.
    /// </summary>
    [Fact]
    public void Body_paragraph_smartart_reusing_chart_docPr_id_is_marked_suppressed()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(Chart.Create(ChartKind.Column, ["Q1"], [1.0])));
        paragraph.Runs.Add(Run.FromSmartArt(SmartArt.Create(SmartArtKind.List, ["Plan", "Build"])));
        document.Blocks.Add(paragraph);
        var source = WithDuplicateSecondDocPrId(WriteBytes(document));

        var read = DocxReader.Read(new MemoryStream(source));
        var smartArtRun = read.Paragraphs.Single().Runs.Single(run => run.SmartArt is not null);

        smartArtRun.SmartArt!.IsWordSuppressedByDuplicateDrawingId.Should().BeTrue(
            "Word suppresses a diagram whose wp:docPr id collides with an earlier drawing in the same part, " +
            "even when the paragraph carrying it has no field code and therefore takes the -- previously " +
            "annotation-dropping -- fast clone path in PrepareSpanningFieldParagraph");
    }

    /// <summary>
    /// Two SmartArt diagrams, both in plain (fieldless) top-level body paragraphs, sharing a docPr id: only
    /// the SECOND (the one that did not claim the id first) is suppressed -- first-claim-wins, matching
    /// Word's own behaviour. Guards against a broad fix that marks every same-id occurrence rather than
    /// only the later collisions.
    /// </summary>
    [Fact]
    public void First_occurrence_of_a_colliding_docPr_id_stays_unsuppressed()
    {
        var document = new TextDocument();
        var firstParagraph = new Paragraph();
        firstParagraph.Runs.Add(Run.FromSmartArt(SmartArt.Create(SmartArtKind.List, ["One", "Two"])));
        document.Blocks.Add(firstParagraph);
        var secondParagraph = new Paragraph();
        secondParagraph.Runs.Add(Run.FromSmartArt(SmartArt.Create(SmartArtKind.List, ["Three", "Four"])));
        document.Blocks.Add(secondParagraph);

        var source = WithDuplicateSecondDocPrId(WriteBytes(document));

        var read = DocxReader.Read(new MemoryStream(source));
        var runs = read.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs).Where(r => r.SmartArt is not null).ToList();
        runs.Should().HaveCount(2);

        runs[0].SmartArt!.IsWordSuppressedByDuplicateDrawingId.Should().BeFalse(
            "the first drawing to claim a docPr id keeps rendering in Word");
        runs[1].SmartArt!.IsWordSuppressedByDuplicateDrawingId.Should().BeTrue(
            "the later drawing that reuses an already-claimed docPr id is the one Word suppresses");
    }

    /// <summary>
    /// NO-REGRESSION SIBLING: PrepareSpanningFieldParagraph now only clones a paragraph when a mutation is
    /// actually required (an unmatched field-begin, or a field continuing from the previous paragraph).
    /// A paragraph that genuinely carries a complex field (Insert &gt; Quick Parts &gt; Field) must still take
    /// that mutating path and have its w:fldChar begin/separate/end sequence stripped into a
    /// Run.ComplexField exactly as before -- this guards the behaviour the refactor must not touch.
    /// </summary>
    [Fact]
    public void Complex_field_paragraph_still_takes_the_mutating_clone_path()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.ComplexFieldRun(" PAGE ", "1"));
        document.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);

        var run = read.Blocks.OfType<Paragraph>().Single().Runs.Single();
        run.ComplexField.Should().NotBeNull();
        run.ComplexField!.Instruction.Should().Be(" PAGE ");
        run.Text.Should().Be("1");
    }
}
