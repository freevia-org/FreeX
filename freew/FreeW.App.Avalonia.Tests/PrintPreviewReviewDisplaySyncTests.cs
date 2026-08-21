using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Free.Shared.Pdf;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// shared-print-preview F1: <see cref="PrintPreviewDialog"/> constructs its own preview
/// <see cref="DocumentView"/> that, before this fix, always defaulted to
/// <see cref="ReviewDisplayState.Default"/> (All Markup) regardless of what Review &gt; Display for
/// Review the live editor actually has selected. Since the Display-for-Review policy gates which
/// revision text is included in layout/line-breaking (and, identically, in
/// <see cref="DocumentView.BuildPdfContent"/> -- the same builder ExportPdfAsync/PrintAsync use against
/// the live editor), a preview that ignores the live editor's setting can show content and a page count
/// that disagree with what Create PDF / Print actually produce.
/// </summary>
public sealed class PrintPreviewReviewDisplaySyncTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static TextDocument DocWithTrackedDeletion()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Plain "));
        paragraph.Runs.Add(new Run("DELETED", RunFormatting.Default)
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Alice",
        });
        document.Blocks.Add(paragraph);
        return document;
    }

    private static DocumentView FindPreviewDocumentView(Control root)
    {
        if (root is DocumentView typedRoot
            && AutomationProperties.GetAutomationId(typedRoot) == "PrintPreviewDocumentView")
            return typedRoot;

        return root.GetLogicalDescendants()
            .OfType<DocumentView>()
            .First(control => AutomationProperties.GetAutomationId(control) == "PrintPreviewDocumentView");
    }

    // ── The defect: preview must honour the live editor's No Markup setting, not All Markup ────

    [Fact]
    public Task PrintPreviewDialog_LiveEditorInNoMarkup_HidesDeletedTextInPreview() =>
        Session.Dispatch(() =>
        {
            var liveEditorReviewState =
                ReviewDisplayState.Default.WithDisplayMode(ReviewDisplayMode.NoMarkup);

            var dialog = new PrintPreviewDialog(
                DocWithTrackedDeletion(),
                "Test.docx",
                reviewDisplayState: liveEditorReviewState);

            var preview = FindPreviewDocumentView(dialog);
            var texts = preview.BuildPdfContent().Pages
                .SelectMany(page => page.Ops)
                .OfType<PdfText>()
                .Select(op => op.Text)
                .ToArray();

            // The live editor is in No Markup (deleted text hidden from layout entirely, per
            // ReviewDisplayPolicy.RevisionDecision). ExportPdfAsync/PrintAsync render off that same live
            // editor instance, so the preview must agree instead of quietly showing All Markup content
            // and a page count computed from it.
            string.Concat(texts).Should().NotContain("DELETED");
            string.Concat(texts).Should().Contain("Plain");
        }, CancellationToken.None);

    // ── Sibling/no-regression: with no live state supplied, the previous All Markup default holds ──

    [Fact]
    public Task PrintPreviewDialog_NoReviewStateSupplied_KeepsAllMarkupDefault() =>
        Session.Dispatch(() =>
        {
            var dialog = new PrintPreviewDialog(DocWithTrackedDeletion(), "Test.docx");

            var preview = FindPreviewDocumentView(dialog);
            var texts = preview.BuildPdfContent().Pages
                .SelectMany(page => page.Ops)
                .OfType<PdfText>()
                .Select(op => op.Text)
                .ToArray();

            // Existing call sites (and existing tests) that construct PrintPreviewDialog without the new
            // optional parameter must keep behaving exactly as before: All Markup, deleted text visible.
            string.Concat(texts).Should().Contain("DELETED");
            string.Concat(texts).Should().Contain("Plain");
        }, CancellationToken.None);

    // ── Sibling: an editor already in All Markup must still show deleted text (no accidental flip) ──

    [Fact]
    public Task PrintPreviewDialog_LiveEditorInAllMarkup_StillShowsDeletedText() =>
        Session.Dispatch(() =>
        {
            var liveEditorReviewState =
                ReviewDisplayState.Default.WithDisplayMode(ReviewDisplayMode.AllMarkup);

            var dialog = new PrintPreviewDialog(
                DocWithTrackedDeletion(),
                "Test.docx",
                reviewDisplayState: liveEditorReviewState);

            var preview = FindPreviewDocumentView(dialog);
            var texts = preview.BuildPdfContent().Pages
                .SelectMany(page => page.Ops)
                .OfType<PdfText>()
                .Select(op => op.Text)
                .ToArray();

            string.Concat(texts).Should().Contain("DELETED");
            string.Concat(texts).Should().Contain("Plain");
        }, CancellationToken.None);
}
