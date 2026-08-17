using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Belt-and-braces coverage for the docx writer: it must never emit a <c>w:commentRangeStart</c>,
/// <c>w:commentRangeEnd</c>, or <c>w:commentReference</c> for a comment id that has no matching entry in
/// <see cref="TextDocument.Comments"/> at write time — regardless of which paragraph store the stale
/// <see cref="Run.CommentId"/> lives in. A dangling reference is a package Word must repair on open.
///
/// <para>
/// The scenario below deliberately targets a paragraph store the model-layer cleanup
/// (<see cref="DocumentInspector.RemoveComments"/> / <see cref="DeleteCommentCommand"/>) does NOT walk — an
/// inline shape's text-box content — so this test exercises the writer's OWN guard in isolation, proving it
/// holds even when a model-layer bug (present or future) leaves a stale anchor behind. Sibling tests in
/// <c>FreeW.Core.Model.Tests</c> (<c>DocumentInspectorTests</c>, <c>CommentCommandTests</c>) cover the
/// model-layer cleanup itself for header/footer/footnote/endnote.
/// </para>
/// </summary>
public class CommentAnchorDanglingMarkupTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

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

    // A comment anchored inside an inline shape's text box (w:txbxContent), with the comment removed from
    // doc.Comments but the shape's covered run + reference run left carrying the stale CommentId — exactly
    // the inconsistent state a model-layer cleanup gap (or an undo/redo edge case) could produce.
    private static TextDocument BuildDocumentWithDanglingShapeTextCommentAnchor()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));

        var shape = new Shape { Kind = ShapeKind.Rectangle, WidthPt = 100, HeightPt = 50 };
        var shapeParagraph = new Paragraph();
        shapeParagraph.Runs.Add(new Run("Shape reviewed text") { CommentId = 42 });
        shapeParagraph.Runs.Add(Run.CommentReference(42));
        shape.TextParagraphs.Add(shapeParagraph);

        var hostParagraph = new Paragraph();
        hostParagraph.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(hostParagraph);

        // doc.Comments deliberately does NOT contain id 42 — simulating a comment already removed from
        // the side store (e.g. by DocumentInspector.RemoveComments, which does not walk shape text) while
        // the shape's runs still carry the now-orphaned mark.
        return doc;
    }

    [Fact]
    public void Write_NeverEmitsCommentMarkupForAnIdMissingFromDocumentComments()
    {
        var doc = BuildDocumentWithDanglingShapeTextCommentAnchor();
        doc.Comments.Should().BeEmpty();

        var bytes = WriteBytes(doc);
        var document = EntryXml(bytes, "word/document.xml");

        // No dangling reference to the removed comment survives anywhere in document.xml (which is where
        // an inline shape's txbxContent lives) — not a range start/end, not the reference marker.
        document.Descendants(W + "commentRangeStart").Should().BeEmpty();
        document.Descendants(W + "commentRangeEnd").Should().BeEmpty();
        document.Descendants(W + "commentReference").Should().BeEmpty();

        // The shape's visible text survives untouched — only the stale comment marks were dropped.
        document.Descendants(W + "t").Select(t => t.Value).Should().Contain("Shape reviewed text");

        // No comments part is emitted at all (there are zero comments), so nothing downstream could even
        // resolve a stray reference.
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        zip.GetEntry("word/comments.xml").Should().BeNull();
    }

    [Fact]
    public void Write_StillEmitsCommentMarkupWhenTheIdIsValid()
    {
        // Sibling no-regression check: a run whose CommentId DOES resolve in doc.Comments still round-trips
        // through the writer exactly as before — the guard only suppresses ids that are actually stale.
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Reviewed text") { CommentId = 1 });
        paragraph.Runs.Add(Run.CommentReference(1));
        doc.Blocks.Add(paragraph);
        doc.Comments[1] = new Comment(1, "note", "Ann", "A");

        var bytes = WriteBytes(doc);
        var document = EntryXml(bytes, "word/document.xml");

        document.Descendants(W + "commentRangeStart").Should().ContainSingle(e => e.Attribute(W + "id")!.Value == "1");
        document.Descendants(W + "commentReference").Should().ContainSingle(e => e.Attribute(W + "id")!.Value == "1");

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        zip.GetEntry("word/comments.xml").Should().NotBeNull();
    }
}
