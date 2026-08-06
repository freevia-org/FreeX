using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for cross-reference fields (Word's References &gt; Cross-reference). The field
/// serialises as a <c>w:fldSimple</c> whose <c>w:instr</c> carries a <c>REF</c>/<c>PAGEREF</c>/<c>NOTEREF</c>
/// instruction over a bookmark name, with optional <c>\w</c>/<c>\n</c>/<c>\p</c> and <c>\h</c>
/// switches, wrapping a run holding the cached resolved text. Legacy numeric NOTEREF operands remain
/// readable.
/// </summary>
public class CrossReferenceRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static string Instruction(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(entry);
        return xml.Descendants(W + "fldSimple").Single().Attribute(W + "instr")!.Value;
    }

    private static TextDocument WithCrossReference(CrossReferenceField field, string cached)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.CrossReferenceFieldRun(field, cached));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [Fact]
    public void RefField_WithHyperlink_SurvivesRoundTrip()
    {
        var field = new CrossReferenceField(CrossRefFieldKind.Ref, "_Ref1", CrossRefInsertAs.Text, Hyperlink: true);

        var result = RoundTrip(WithCrossReference(field, "Chapter One"));

        var run = result.Blocks.OfType<Paragraph>().Single().Runs.Single();
        run.CrossReference.Should().Be(field);
        // The cached resolved text is preserved as the run text (fallback for field-unaware consumers).
        run.Text.Should().Be("Chapter One");
    }

    [Fact]
    public void PageRefField_RoundTripsAsPageNumber()
    {
        var field = new CrossReferenceField(CrossRefFieldKind.PageRef, "_Ref2", CrossRefInsertAs.PageNumber, Hyperlink: false);

        var run = RoundTrip(WithCrossReference(field, "1")).Blocks.OfType<Paragraph>().Single().Runs.Single();

        run.CrossReference.Should().Be(field);
    }

    [Fact]
    public void LegacyNumericNoteRefField_RoundTripsOverNoteId()
    {
        var field = new CrossReferenceField(CrossRefFieldKind.NoteRef, "3", CrossRefInsertAs.Text, Hyperlink: true);

        var run = RoundTrip(WithCrossReference(field, "3")).Blocks.OfType<Paragraph>().Single().Runs.Single();

        run.CrossReference.Should().Be(field);
    }

    [Fact]
    public void NoteRefField_EmitsBookmarkAroundPhysicalMarkerAndReopensExactly()
    {
        var doc = new TextDocument();
        var target = new Paragraph();
        target.Runs.Add(new Run("Body"));
        target.Runs.Add(Run.FootnoteReference(3));
        target.BookmarkNames.Add("_RefNote");
        target.BookmarkBoundaries.Add(new BookmarkBoundary(
            "note", BookmarkBoundaryKind.Start, 1, "_RefNote"));
        target.BookmarkBoundaries.Add(new BookmarkBoundary(
            "note", BookmarkBoundaryKind.End, 2));
        doc.Blocks.Add(target);
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.CrossReferenceFieldRun(
                    new CrossReferenceField(CrossRefFieldKind.NoteRef, "_RefNote", CrossRefInsertAs.Text, true),
                    "1")
            }
        });
        doc.Footnotes[3] = new Footnote(3, "note");

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        var package = stream.ToArray();
        using (var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        using (var entry = zip.GetEntry("word/document.xml")!.Open())
        {
            var xml = XDocument.Load(entry);
            var bodyParagraph = xml.Descendants(W + "p").First();
            var children = bodyParagraph.Elements().ToList();
            var start = children.FindIndex(element => element.Name == W + "bookmarkStart");
            var marker = children.FindIndex(element => element.Descendants(W + "footnoteReference").Any());
            var end = children.FindIndex(element => element.Name == W + "bookmarkEnd");
            start.Should().BeLessThan(marker);
            marker.Should().BeLessThan(end);
            xml.Descendants(W + "fldSimple").Single().Attribute(W + "instr")!.Value
                .Should().Be(" NOTEREF _RefNote \\h ");
        }

        var reopened = DocxReader.Read(new MemoryStream(package));
        var reopenedTarget = (Paragraph)reopened.Blocks[0];
        reopenedTarget.BookmarkNames.Should().Contain("_RefNote");
        reopenedTarget.BookmarkBoundaries.Select(boundary => boundary.RunIndex).Should().Equal(1, 2);
        ((Paragraph)reopened.Blocks[1]).Runs.Single().CrossReference.Should().Be(
            new CrossReferenceField(CrossRefFieldKind.NoteRef, "_RefNote", CrossRefInsertAs.Text, true));
    }

    [Fact]
    public void NoteRefAboveBelow_EmitsPSwitch()
    {
        var field = new CrossReferenceField(
            CrossRefFieldKind.NoteRef, "_RefNote", CrossRefInsertAs.AboveBelow, Hyperlink: false);

        Instruction(WithCrossReference(field, "above"))
            .Should().Be(" NOTEREF _RefNote \\p ");
    }

    [Fact]
    public void CaptionVariantBookmarksAndPlainRefFieldsRoundTripWithExactRanges()
    {
        var doc = new TextDocument();
        var caption = Captions.BuildCaption(CaptionLabel.Figure, 1, "Sample caption text");
        caption.Runs[2] = RevisionEditPlanner.CloneRunWithText(caption.Runs[2], ": ");
        caption.Runs.Add(new Run("Sample caption text"));
        caption.BookmarkNames.AddRange(["_RefWhole", "_RefLabel", "_RefText"]);
        caption.BookmarkBoundaries.AddRange(
        [
            new BookmarkBoundary("whole", BookmarkBoundaryKind.Start, 0, "_RefWhole"),
            new BookmarkBoundary("whole", BookmarkBoundaryKind.End, 4),
            new BookmarkBoundary("label", BookmarkBoundaryKind.Start, 0, "_RefLabel"),
            new BookmarkBoundary("label", BookmarkBoundaryKind.End, 2),
            new BookmarkBoundary("text", BookmarkBoundaryKind.Start, 3, "_RefText"),
            new BookmarkBoundary("text", BookmarkBoundaryKind.End, 4)
        ]);
        doc.Blocks.Add(caption);
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.CrossReferenceFieldRun(
                    new CrossReferenceField(CrossRefFieldKind.Ref, "_RefWhole", CrossRefInsertAs.Text, true),
                    "Figure 1: Sample caption text"),
                Run.CrossReferenceFieldRun(
                    new CrossReferenceField(
                        CrossRefFieldKind.Ref, "_RefLabel", CrossRefInsertAs.CaptionLabelAndNumber, true),
                    "Figure 1"),
                Run.CrossReferenceFieldRun(
                    new CrossReferenceField(CrossRefFieldKind.Ref, "_RefText", CrossRefInsertAs.CaptionText, true),
                    "Sample caption text")
            }
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        var package = stream.ToArray();
        using (var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        using (var entry = zip.GetEntry("word/document.xml")!.Open())
        {
            var xml = XDocument.Load(entry);
            xml.Descendants(W + "fldSimple")
                .Select(field => field.Attribute(W + "instr")!.Value)
                .Should().Equal(
                    " REF _RefWhole \\h ",
                    " REF _RefLabel \\h ",
                    " REF _RefText \\h ");
            xml.Descendants(W + "bookmarkStart")
                .Select(start => start.Attribute(W + "name")!.Value)
                .Should().Equal("_RefWhole", "_RefLabel", "_RefText");
        }

        var reopened = DocxReader.Read(new MemoryStream(package));
        var reopenedCaption = (Paragraph)reopened.Blocks[0];
        var reopenedFields = ((Paragraph)reopened.Blocks[1]).Runs;
        reopenedFields.Select(run => run.CrossReference!.InsertAs)
            .Should().OnlyContain(insertAs => insertAs == CrossRefInsertAs.Text,
                "plain REF fields carry caption-variant semantics in their bookmark ranges, not a field switch");
        CrossReferences.ResolveField(
                reopened, reopenedFields[0].CrossReference!, reopenedFields[0].Text, sourceBlockIndex: 1)
            .Should().Be("Figure 1: Sample caption text");
        CrossReferences.ResolveField(
                reopened, reopenedFields[1].CrossReference!, reopenedFields[1].Text, sourceBlockIndex: 1)
            .Should().Be("Figure 1");
        CrossReferences.ResolveField(
                reopened, reopenedFields[2].CrossReference!, reopenedFields[2].Text, sourceBlockIndex: 1)
            .Should().Be("Sample caption text");
        reopenedCaption.BookmarkNames.Should().Contain(["_RefWhole", "_RefLabel", "_RefText"]);
    }

    [Fact]
    public void HeadingNumberField_RoundTripsWithWSwitch()
    {
        var field = new CrossReferenceField(CrossRefFieldKind.Ref, "_Ref4", CrossRefInsertAs.HeadingNumber, Hyperlink: false);

        var run = RoundTrip(WithCrossReference(field, "1.2")).Blocks.OfType<Paragraph>().Single().Runs.Single();

        run.CrossReference.Should().Be(field);
    }

    [Fact]
    public void AboveBelowField_RoundTripsWithPSwitch()
    {
        var field = new CrossReferenceField(CrossRefFieldKind.Ref, "_Ref5", CrossRefInsertAs.AboveBelow, Hyperlink: false);

        var run = RoundTrip(WithCrossReference(field, "above")).Blocks.OfType<Paragraph>().Single().Runs.Single();

        run.CrossReference.Should().Be(field);
    }

    [Fact]
    public void RefField_EmitsExpectedInstruction()
    {
        var field = new CrossReferenceField(CrossRefFieldKind.Ref, "_Ref9", CrossRefInsertAs.HeadingNumber, Hyperlink: true);

        var instr = Instruction(WithCrossReference(field, "1.2"));

        instr.Should().Contain("REF");
        instr.Should().Contain("_Ref9");
        instr.Should().Contain("\\w");
        instr.Should().Contain("\\h");
    }

    [Fact]
    public void PageRefField_EmitsPageRefKeyword()
    {
        var field = new CrossReferenceField(CrossRefFieldKind.PageRef, "_Ref10", CrossRefInsertAs.PageNumber, Hyperlink: false);

        Instruction(WithCrossReference(field, "1")).Should().Contain("PAGEREF").And.Contain("_Ref10");
    }
}
