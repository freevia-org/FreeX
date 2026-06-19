using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for modern (threaded) comments: replies (w15:paraIdParent) and the resolved/done
/// flag (w15:done) emitted into word/commentsExtended.xml survive write → read, and the new part is wired
/// through [Content_Types].xml + document.xml.rels with the w14:paraId stamps on the comment paragraphs.
/// </summary>
public class ThreadedCommentsRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    private static readonly XNamespace W15 = "http://schemas.microsoft.com/office/word/2012/wordml";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string CommentsExtendedContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtended+xml";
    private const string CommentsExtendedRelType =
        "http://schemas.microsoft.com/office/2011/relationships/commentsExtended";

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument ReadDoc(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return DocxReader.Read(stream);
    }

    private static byte[] EntryBytes(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(entryPath)!.Open();
        using var buffer = new MemoryStream();
        entry.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static XDocument EntryXml(byte[] docx, string entryPath) =>
        XDocument.Load(new MemoryStream(EntryBytes(docx, entryPath)));

    private static bool HasEntry(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        return zip.GetEntry(entryPath) is not null;
    }

    /// <summary>A document with one anchored, RESOLVED comment carrying two replies, plus a second open comment.</summary>
    private static TextDocument BuildThreadedDocument()
    {
        var doc = new TextDocument();

        // Body paragraph the first comment (id 0) brackets, with its anchor reference run.
        var para = new Paragraph();
        para.Runs.Add(new Run("Reviewed text") { CommentId = 0 });
        para.Runs.Add(Run.CommentReference(0));
        doc.Blocks.Add(para);

        var parent = new Comment(0, "Please clarify", "Alice", "A")
        {
            DateXml = "2026-01-01T00:00:00Z",
            Resolved = true,
        };
        parent.AddReply(1, "Clarified above", "Bob", "B").DateXml = "2026-01-02T00:00:00Z";
        parent.AddReply(2, "Thanks", "Alice", "A").DateXml = "2026-01-03T00:00:00Z";
        doc.Comments[0] = parent;

        // A second, open (unresolved), reply-free comment to prove resolved/threaded state is per-thread.
        var para2 = new Paragraph();
        para2.Runs.Add(new Run("More text") { CommentId = 3 });
        para2.Runs.Add(Run.CommentReference(3));
        doc.Blocks.Add(para2);
        doc.Comments[3] = new Comment(3, "Looks good", "Cara", "C");

        return doc;
    }

    [Fact]
    public void RepliesAndResolved_SurviveWriteThenRead()
    {
        var read = ReadDoc(WriteBytes(BuildThreadedDocument()));

        // Only the two TOP-LEVEL comments are keyed in the document; replies live inside their parent.
        read.Comments.Keys.Should().BeEquivalentTo([0, 3]);

        var parent = read.Comments[0];
        parent.PlainText.Should().Be("Please clarify");
        parent.Resolved.Should().BeTrue();
        parent.Replies.Should().HaveCount(2);
        parent.Replies.Select(r => r.Id).Should().ContainInOrder(1, 2);
        parent.Replies.Select(r => r.PlainText).Should().ContainInOrder("Clarified above", "Thanks");
        parent.Replies.Select(r => r.Author).Should().ContainInOrder("Bob", "Alice");

        var second = read.Comments[3];
        second.PlainText.Should().Be("Looks good");
        second.Resolved.Should().BeFalse();
        second.Replies.Should().BeEmpty();
    }

    [Fact]
    public void CommentsExtendedPart_IsWiredThroughContentTypesAndRels()
    {
        var bytes = WriteBytes(BuildThreadedDocument());

        HasEntry(bytes, "word/commentsExtended.xml").Should().BeTrue();

        // Content-type Override for the new part.
        var overrides = EntryXml(bytes, "[Content_Types].xml").Root!.Elements(Ct + "Override")
            .ToDictionary(o => o.Attribute("PartName")!.Value, o => o.Attribute("ContentType")!.Value);
        overrides.Should().ContainKey("/word/commentsExtended.xml");
        overrides["/word/commentsExtended.xml"].Should().Be(CommentsExtendedContentType);

        // Document relationship to the new part.
        var rels = EntryXml(bytes, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship").ToList();
        var ext = rels.SingleOrDefault(r => r.Attribute("Type")!.Value == CommentsExtendedRelType);
        ext.Should().NotBeNull();
        ext!.Attribute("Target")!.Value.Should().Be("commentsExtended.xml");
    }

    [Fact]
    public void CommentsExtended_ThreadsRepliesAndMarksResolved()
    {
        var bytes = WriteBytes(BuildThreadedDocument());

        // Each comment's last paragraph carries a w14:paraId; build id → paraId so we can resolve threading.
        var commentParaIds = EntryXml(bytes, "word/comments.xml").Root!.Elements(W + "comment")
            .ToDictionary(
                c => int.Parse(c.Attribute(W + "id")!.Value),
                c => c.Elements(W + "p").Last().Attribute(W14 + "paraId")!.Value);
        commentParaIds.Should().HaveCount(4); // parent + two replies + the open comment

        var exEntries = EntryXml(bytes, "word/commentsExtended.xml").Root!.Elements(W15 + "commentEx")
            .ToDictionary(
                e => e.Attribute(W15 + "paraId")!.Value,
                e => (Parent: e.Attribute(W15 + "paraIdParent")?.Value, Done: e.Attribute(W15 + "done")?.Value));

        // Parent (id 0) is a thread root marked done="1".
        var parentParaId = commentParaIds[0];
        exEntries[parentParaId].Parent.Should().BeNull();
        exEntries[parentParaId].Done.Should().Be("1");

        // Both replies point at the parent's paraId and inherit the resolved flag.
        foreach (var replyId in new[] { 1, 2 })
        {
            var entry = exEntries[commentParaIds[replyId]];
            entry.Parent.Should().Be(parentParaId);
            entry.Done.Should().Be("1");
        }

        // The open comment (id 3) is a root with no done flag.
        var openParaId = commentParaIds[3];
        exEntries[openParaId].Parent.Should().BeNull();
        exEntries[openParaId].Done.Should().BeNull();
    }

    [Fact]
    public void ThreadedComments_SurviveASecondRoundTrip()
    {
        var once = ReadDoc(WriteBytes(BuildThreadedDocument()));
        var twice = ReadDoc(WriteBytes(once));

        twice.Comments[0].Resolved.Should().BeTrue();
        twice.Comments[0].Replies.Select(r => r.PlainText).Should().ContainInOrder("Clarified above", "Thanks");
        twice.Comments[3].Replies.Should().BeEmpty();
        twice.Comments[3].Resolved.Should().BeFalse();
    }
}
