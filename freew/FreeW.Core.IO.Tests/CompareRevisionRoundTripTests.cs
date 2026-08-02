using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;
using Xunit;
using FluentAssertions;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// IO-layer round-trip tests for the legal-blackline output of <see cref="DocumentCompare"/> and
/// <see cref="DocumentCombine"/>: verify that the tracked insertions and deletions those engines produce
/// are serialised as proper OOXML (<c>w:ins</c>/<c>w:del</c> wrappers with <c>w:author</c>,
/// <c>w:date</c>, and <c>w:delText</c>) and read back through <see cref="DocxReader"/> with every field
/// intact, so the result can be Accepted/Rejected via the reviewing infrastructure.
/// </summary>
public class CompareRevisionRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Author = "Eve Editor";
    private const string DateXml = "2026-06-19T10:00:00Z";

    private static TextDocument DocWith(params string[] paragraphs)
    {
        var doc = new TextDocument();
        foreach (var text in paragraphs)
            doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    // Round-trip a document through DocxWriter → stream → DocxReader.
    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    // Write to a MemoryStream and return the raw word/document.xml text.
    private static string DocumentXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        using var reader = new StreamReader(entry);
        return reader.ReadToEnd();
    }

    // Write to a MemoryStream and return the parsed word/document.xml XDocument.
    private static XDocument DocumentXDoc(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    // -----------------------------------------------------------------------
    // XML shape: w:ins / w:del wrappers
    // -----------------------------------------------------------------------

    [Fact]
    public void Compare_WordReplacement_Emits_WIns_And_WDel_Elements()
    {
        var original = DocWith("the quick brown fox");
        var revised  = DocWith("the quick red fox");

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var xml = DocumentXml(compared);

        // Deletion wrapper must appear for "brown".
        xml.Should().Contain("<w:del");
        xml.Should().Contain("w:delText");

        // Insertion wrapper must appear for "red".
        xml.Should().Contain("<w:ins");
    }

    [Fact]
    public void Compare_Deletion_Carries_WAuthor_And_WDate_On_WDel()
    {
        var original = DocWith("the quick brown fox");
        var revised  = DocWith("the quick red fox");

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var xdoc = DocumentXDoc(compared);

        var delElements = xdoc.Descendants(W + "del").ToList();
        delElements.Should().NotBeEmpty();

        var del = delElements.First();
        del.Attribute(W + "author")!.Value.Should().Be(Author);
        del.Attribute(W + "date")!.Value.Should().Be(DateXml);
        del.Attribute(W + "id").Should().NotBeNull(); // unique id required by schema
    }

    [Fact]
    public void Compare_Insertion_Carries_WAuthor_And_WDate_On_WIns()
    {
        var original = DocWith("the quick brown fox");
        var revised  = DocWith("the quick red fox");

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var xdoc = DocumentXDoc(compared);

        var insElements = xdoc.Descendants(W + "ins").ToList();
        insElements.Should().NotBeEmpty();

        var ins = insElements.First();
        ins.Attribute(W + "author")!.Value.Should().Be(Author);
        ins.Attribute(W + "date")!.Value.Should().Be(DateXml);
        ins.Attribute(W + "id").Should().NotBeNull();
    }

    [Fact]
    public void Compare_NoChanges_Emits_Neither_WIns_Nor_WDel()
    {
        var original = DocWith("identical text");
        var revised  = DocWith("identical text");

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var xml = DocumentXml(compared);

        xml.Should().NotContain("<w:ins");
        xml.Should().NotContain("<w:del");
    }

    [Fact]
    public void DeletedText_Uses_WDelText_Not_WT()
    {
        // Word requires that text inside a w:del uses w:delText, not w:t, so the deleted content renders
        // as struck-through (not as live text) in Word's markup view.
        var original = DocWith("keep me and delete me too");
        var revised  = DocWith("keep me");

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var xdoc = DocumentXDoc(compared);

        var delElements = xdoc.Descendants(W + "del").ToList();
        delElements.Should().NotBeEmpty();

        // Every w:r inside a w:del must carry its text as w:delText (not w:t).
        foreach (var del in delElements)
        {
            del.Descendants(W + "delText").Should().NotBeEmpty();
            del.Descendants(W + "t").Should().BeEmpty();
        }
    }

    // -----------------------------------------------------------------------
    // Round-trip: the model read back matches what Compare produced
    // -----------------------------------------------------------------------

    [Fact]
    public void Compare_WordReplacement_RoundTrips_InsertionAndDeletion()
    {
        var original = DocWith("the quick brown fox");
        var revised  = DocWith("the quick red fox");

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var reloaded = RoundTrip(compared);

        var paragraph = reloaded.Paragraphs.Single();

        // Both marked runs survive the round-trip.
        var deleted  = paragraph.Runs.Where(r => r.Revision == RevisionKind.Deleted).ToList();
        var inserted = paragraph.Runs.Where(r => r.Revision == RevisionKind.Inserted).ToList();

        deleted.Should().ContainSingle(r => r.Text.Trim() == "brown");
        inserted.Should().ContainSingle(r => r.Text.Trim() == "red");

        // Author and date survive.
        deleted.Should().OnlyContain(r => r.RevisionAuthor == Author && r.RevisionDateXml == DateXml);
        inserted.Should().OnlyContain(r => r.RevisionAuthor == Author && r.RevisionDateXml == DateXml);

        // Unchanged words carry no revision mark.
        paragraph.Runs.Where(r => r.Text.Trim() is "the" or "quick" or "fox")
            .Should().OnlyContain(r => r.Revision == RevisionKind.None);
    }

    [Fact]
    public void Compare_WholeNewParagraph_RoundTrips_AsInsertedRuns()
    {
        var original = DocWith("First", "Third");
        var revised  = DocWith("First", "Second", "Third");

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var reloaded = RoundTrip(compared);

        // The inserted paragraph survives: all its runs are marked Inserted.
        var paragraphs = reloaded.Paragraphs.ToList();
        paragraphs.Should().HaveCount(3);
        paragraphs[1].Runs.Should().NotBeEmpty();
        paragraphs[1].Runs.Should().OnlyContain(r => r.Revision == RevisionKind.Inserted);
        paragraphs[1].Runs.Should().OnlyContain(r => r.RevisionAuthor == Author);
    }

    [Fact]
    public void Compare_DeletedParagraph_RoundTrips_AndAcceptDropsIt()
    {
        var original = DocWith("Keep", "Drop this", "Tail");
        var revised  = DocWith("Keep", "Tail");

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var reloaded = RoundTrip(compared);

        // The deleted paragraph is kept in the model (struck-through), in its original position.
        var paragraphs = reloaded.Paragraphs.ToList();
        paragraphs.Should().HaveCount(3);
        paragraphs[1].Runs.Should().OnlyContain(r => r.Revision == RevisionKind.Deleted);
        paragraphs[1].Runs.Should().OnlyContain(r => r.RevisionAuthor == Author && r.RevisionDateXml == DateXml);

        // Accepting drops the deleted text (runs removed), leaving "Keep" and "Tail" with content.
        TrackChanges.AcceptAll(reloaded);
        reloaded.Paragraphs.Where(p => p.PlainText.Length > 0)
            .Select(p => p.PlainText)
            .Should().Equal("Keep", "Tail");
    }

    [Fact]
    public void Compare_RoundTripped_CanBeAccepted_ToYieldRevisedText()
    {
        // The legal-blackline's promise: accepting all tracked changes after round-trip yields exactly
        // the revised document's text.
        var original = DocWith("one two three");
        var revised  = DocWith("one four three");

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var reloaded = RoundTrip(compared);

        TrackChanges.AcceptAll(reloaded);
        reloaded.Paragraphs.Single().PlainText.Should().Be("one four three");
    }

    [Fact]
    public void Compare_DoNotTrackMoves_EmitsOrdinaryRevisionsAndPreservesSetting()
    {
        var original = DocWith("Alpha", "Bravo", "Charlie");
        var revised = DocWith("Bravo", "Alpha", "Charlie");
        revised.DoNotTrackMoves = true;

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var documentXml = DocumentXDoc(compared);
        var reloaded = RoundTrip(compared);

        documentXml.Descendants(W + "moveFrom").Should().BeEmpty();
        documentXml.Descendants(W + "moveTo").Should().BeEmpty();
        documentXml.Descendants(W + "del").Should().NotBeEmpty();
        documentXml.Descendants(W + "ins").Should().NotBeEmpty();
        reloaded.DoNotTrackMoves.Should().BeTrue();
        reloaded.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().NotContain(run => run.MoveRevisionId != null);
    }

    [Fact]
    public void Compare_RoundTripped_CanBeRejected_ToYieldOriginalText()
    {
        var original = DocWith("one two three");
        var revised  = DocWith("one four three");

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var reloaded = RoundTrip(compared);

        TrackChanges.RejectAll(reloaded);
        reloaded.Paragraphs.Single().PlainText.Should().Be("one two three");
    }

    [Fact]
    public void Compare_FormatOnlyChange_RoundTrips_AsRPrChange_AndCanBeRejected()
    {
        var original = new TextDocument();
        var originalParagraph = new Paragraph();
        originalParagraph.Runs.Add(new Run("format me"));
        original.Blocks.Add(originalParagraph);

        var revised = new TextDocument();
        var revisedParagraph = new Paragraph();
        revisedParagraph.Runs.Add(new Run("format me", new RunFormatting { Bold = true }));
        revised.Blocks.Add(revisedParagraph);

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var xdoc = DocumentXDoc(compared);
        var formatChange = xdoc.Descendants(W + "rPrChange").Should().ContainSingle().Subject;
        formatChange.Attribute(W + "author")!.Value.Should().Be(Author);
        formatChange.Attribute(W + "date")!.Value.Should().Be(DateXml);

        var reloaded = RoundTrip(compared);
        var run = reloaded.Paragraphs.Single().Runs.Single();
        run.Formatting.Bold.Should().BeTrue();
        run.FormatRevision.Should().Be(new FormatRevision(RunFormatting.Default, Author, DateXml));

        TrackChanges.RejectAll(reloaded);
        reloaded.Paragraphs.Single().Runs.Single().Formatting.Bold.Should().BeFalse();
    }

    [Fact]
    public void Compare_UniqueParagraphMove_RoundTrips_AsPairedMoveWrappers()
    {
        var original = DocWith("Alpha", "Bravo", "Charlie");
        var revised = DocWith("Bravo", "Alpha", "Charlie");

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var xdoc = DocumentXDoc(compared);
        var moveFrom = xdoc.Descendants(W + "moveFrom").Should().ContainSingle().Subject;
        var moveTo = xdoc.Descendants(W + "moveTo").Should().ContainSingle().Subject;
        moveFrom.Attribute(W + "id")!.Value.Should().Be(moveTo.Attribute(W + "id")!.Value);
        moveFrom.Attribute(W + "author")!.Value.Should().Be(Author);
        moveTo.Attribute(W + "date")!.Value.Should().Be(DateXml);

        var reloaded = RoundTrip(compared);
        var moved = reloaded.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.MoveRevisionId != null)
            .ToList();
        moved.Should().HaveCount(2);
        moved.Select(run => run.MoveRevisionId).Distinct().Should().ContainSingle();
        moved.Should().ContainSingle(run => run.Text == "Alpha" && run.Revision == RevisionKind.Deleted);
        moved.Should().ContainSingle(run => run.Text == "Alpha" && run.Revision == RevisionKind.Inserted);
    }

    [Fact]
    public void Compare_RetainedCommentAnchor_RoundTripsWithItsCommentThread()
    {
        var original = DocWith("Annotated text");
        var revised = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Annotated text") { CommentId = 5 });
        paragraph.Runs.Add(Run.CommentReference(5));
        revised.Blocks.Add(paragraph);

        var comment = new Comment(5, "Please verify", "Alice", "A") { Resolved = true };
        comment.AddReply(6, "Verified", "Bob", "B");
        revised.Comments[5] = comment;

        var reloaded = RoundTrip(DocumentCompare.Compare(original, revised, Author, DateXml));

        reloaded.Paragraphs.Single().Runs.Should().Contain(run => run.CommentId == 5 && run.IsCommentReference);
        reloaded.Comments.Should().ContainKey(5);
        reloaded.Comments[5].PlainText.Should().Be("Please verify");
        reloaded.Comments[5].Resolved.Should().BeTrue();
        reloaded.Comments[5].Replies.Should().ContainSingle(reply =>
            reply.Id == 6 && reply.PlainText == "Verified" && reply.Author == "Bob");
    }

    [Fact]
    public void Compare_DeletedCommentAnchor_RoundTripsWithOriginalThread()
    {
        var original = DocWith("Keep", "Doomed", "Tail");
        var doomed = original.Paragraphs.ElementAt(1);
        doomed.Runs[0].CommentId = 5;
        doomed.Runs.Add(Run.CommentReference(5));
        var comment = new Comment(5, "Remove this note", "Alice", "A") { Resolved = true };
        comment.AddReply(6, "Acknowledged", "Bob", "B");
        original.Comments[5] = comment;

        var compared = DocumentCompare.Compare(original, DocWith("Keep", "Tail"), Author, DateXml);
        var xdoc = DocumentXDoc(compared);
        var reloaded = RoundTrip(compared);

        xdoc.Descendants(W + "commentRangeStart").Should().ContainSingle(marker => marker.Attribute(W + "id")!.Value == "5");
        reloaded.Paragraphs.ElementAt(1).Runs.Should().Contain(run =>
            run.Revision == RevisionKind.Deleted && run.CommentId == 5 && run.IsCommentReference);
        reloaded.Comments[5].PlainText.Should().Be("Remove this note");
        reloaded.Comments[5].Replies.Should().ContainSingle(reply => reply.Id == 6 && reply.PlainText == "Acknowledged");
    }

    [Fact]
    public void Compare_RevisionList_After_RoundTrip_EnumeratesAllEntries()
    {
        // "hello world" → "hello earth": one deletion ("world") and one insertion ("earth").
        var original = DocWith("hello world");
        var revised  = DocWith("hello earth");

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var reloaded = RoundTrip(compared);

        var entries = RevisionList.Enumerate(reloaded);
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.Kind == RevisionEntryKind.Deletion && e.Text.Trim() == "world");
        entries.Should().Contain(e => e.Kind == RevisionEntryKind.Insertion && e.Text.Trim() == "earth");
        entries.Should().OnlyContain(e => e.Author == Author && e.DateXml == DateXml);
    }

    [Fact]
    public void Compare_SingleRevision_Accept_Via_RevisionList_After_RoundTrip()
    {
        // Verify that single-revision Accept (the Reviewing Pane's per-change action) works on the
        // reloaded document — an end-to-end test of the Compare → write → read → RevisionList.Accept path.
        var original = DocWith("cat dog bird");
        var revised  = DocWith("cat fish bird");

        var compared = DocumentCompare.Compare(original, revised, Author, DateXml);
        var reloaded = RoundTrip(compared);

        var entries = RevisionList.Enumerate(reloaded);
        entries.Should().HaveCount(2); // deletion("dog") + insertion("fish")

        // Accept only the deletion.
        var deletion = entries.First(e => e.Kind == RevisionEntryKind.Deletion);
        RevisionList.Accept(reloaded, deletion).Should().BeTrue();

        // Now only the insertion remains.
        var remaining = RevisionList.Enumerate(reloaded);
        remaining.Should().ContainSingle(e => e.Kind == RevisionEntryKind.Insertion);

        // "dog" is gone; "fish" is still pending as an insertion.
        reloaded.Paragraphs.Single().PlainText.Should().NotContain("dog");
        reloaded.Paragraphs.Single().PlainText.Should().Contain("fish");
    }

    // -----------------------------------------------------------------------
    // Combine round-trip
    // -----------------------------------------------------------------------

    [Fact]
    public void Combine_TwoAuthors_RoundTrips_WithBothAuthorsPreserved()
    {
        var original = DocWith("one two three four");
        var revisedA = DocWith("one ALICE three four");
        var revisedB = DocWith("one two BOB four");

        var combined = DocumentCombine.Combine(original, revisedA, "Alice", revisedB, "Bob", DateXml);
        var reloaded = RoundTrip(combined);

        // Both authors' revisions survive the round-trip.
        var allRuns = reloaded.Paragraphs.SelectMany(p => p.Runs)
            .Where(r => r.Revision != RevisionKind.None)
            .ToList();

        allRuns.Should().NotBeEmpty();
        allRuns.Should().Contain(r => r.RevisionAuthor == "Alice");
        allRuns.Should().Contain(r => r.RevisionAuthor == "Bob");
    }

    [Fact]
    public void Combine_RoundTripped_XML_Carries_BothAuthorNames()
    {
        var original = DocWith("kept changed1 changed2");
        var revisedA = DocWith("kept AliceWord changed2");
        var revisedB = DocWith("kept changed1 BobWord");

        var combined = DocumentCombine.Combine(original, revisedA, "Alice", revisedB, "Bob", DateXml);
        var xml = DocumentXml(combined);

        xml.Should().Contain("w:author=\"Alice\"");
        xml.Should().Contain("w:author=\"Bob\"");
    }
}
