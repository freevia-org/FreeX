using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia;
using FreeW.Core.Model;
using Free.Shared.Pdf;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// freew-change-bars F1: <see cref="DocumentView.BuildPdfContent"/> (the shared content builder behind
/// PDF/XPS export and Linux/macOS printing) must apply the same Display-for-Review decisions
/// <see cref="ReviewDisplayPolicy.RevisionDecision"/> drives in the live Render() pass -- hidden revision
/// text must not leak into the export, and visible tracked changes must carry the same colour +
/// underline/strikethrough marking the editor shows in All Markup.
/// </summary>
public sealed class DocumentViewPdfReviewDisplayTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static TextDocument DocWithTrackedChanges()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Plain "));
        paragraph.Runs.Add(new Run("INSERTED", RunFormatting.Default)
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Alice",
        });
        paragraph.Runs.Add(new Run("DELETED", RunFormatting.Default)
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Alice",
        });
        document.Blocks.Add(paragraph);
        return document;
    }

    // ── No Markup must hide deleted text, not leak it ──────────────────────────────────────────

    [Fact]
    public Task BuildPdfContent_NoMarkup_OmitsDeletedTextFromTheExport() =>
        Session.Dispatch(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithTrackedChanges());
            view.ApplyDisplayForReview(ReviewDisplayMode.NoMarkup);

            var texts = view.BuildPdfContent().Pages
                .SelectMany(page => page.Ops)
                .OfType<PdfText>()
                .Select(op => op.Text)
                .ToArray();

            // The live editor hides deleted text in No Markup (see the RevisionDecision-gated filter in
            // Render()); a user picking No Markup before sharing a PDF must not find the "deleted" text
            // still printed as ordinary, permanent-looking content.
            string.Concat(texts).Should().NotContain("DELETED");
            string.Concat(texts).Should().Contain("INSERTED");
            string.Concat(texts).Should().Contain("Plain");
        }, CancellationToken.None);

    // ── All Markup must colour-code + decorate insertions/deletions, not just show them plain ──

    [Fact]
    public Task BuildPdfContent_AllMarkup_ColoursAndDecoratesTrackedChanges() =>
        Session.Dispatch(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithTrackedChanges());
            view.ApplyDisplayForReview(ReviewDisplayMode.AllMarkup);

            var pdf = view.BuildPdfContent();
            var ops = pdf.Pages.SelectMany(page => page.Ops).ToList();
            var textOps = ops.OfType<PdfText>().ToList();

            var plainOp = textOps.Single(op => op.Text == "Plain ");
            var insertedOp = textOps.Single(op => op.Text == "INSERTED");
            var deletedOp = textOps.Single(op => op.Text == "DELETED");

            // Both tracked runs must be visible in All Markup...
            // ...and coloured distinctly from ordinary text (Word's per-author revision colour).
            insertedOp.Color.Should().NotBe(plainOp.Color);
            deletedOp.Color.Should().NotBe(plainOp.Color);
            insertedOp.Color.Should().Be(deletedOp.Color, "both revisions share the same author");

            // Insertions are underlined, deletions struck through -- exported as PdfLine decorations
            // drawn in the same revision colour as the text (mirrors Render()'s DrawRevisionDecoration).
            ops.OfType<PdfLine>().Should().Contain(line => line.Color == insertedOp.Color);
        }, CancellationToken.None);

    // ── Sibling: plain text with no tracked changes is unaffected ──────────────────────────────

    [Fact]
    public Task BuildPdfContent_PlainDocumentWithoutRevisions_IsUnaffected() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Ordinary text, no revisions."));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.ApplyDisplayForReview(ReviewDisplayMode.AllMarkup);

            var texts = view.BuildPdfContent().Pages
                .SelectMany(page => page.Ops)
                .OfType<PdfText>()
                .Select(op => op.Text)
                .ToArray();

            string.Concat(texts).Should().Be("Ordinary text, no revisions.");
        }, CancellationToken.None);
}
