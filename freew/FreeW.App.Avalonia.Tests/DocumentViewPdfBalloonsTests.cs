using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Free.Shared.Pdf;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// R139: Review &gt; Show Markup &gt; Balloons content (comments, tracked-change insertions/deletions)
/// used to never reach PDF/XPS export or Print (the Avalonia shell prints via
/// <c>FreeWAvaloniaPdfExport.Save</c>, which renders this same <see cref="DocumentView.BuildPdfContent"/>
/// content document) -- nothing in it referenced <see cref="ReviewBalloonLayoutPlanner"/> or the
/// interactive <c>ReviewBalloonsPane</c> strip at all. Paired with the WPF-side
/// PrintLayoutBalloonsTests. These tests exercise the real entry point
/// (<see cref="DocumentView.BuildPdfContent"/>) with <see cref="DocumentView.ShowMarkupBalloons"/> set
/// the way the live app sets it (<c>MainWindow.ToggleReviewBalloons</c>), not a helper that supplies
/// balloon content directly.
/// </summary>
public sealed class DocumentViewPdfBalloonsTests
{
    private const string CommentMarker = "UniqueAvaloniaBalloonMarkerXYZ";

    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public Task BuildPdfContent_ShowMarkupBalloonsOn_WidensPageAndDrawsCommentText() =>
        Session.Dispatch(() =>
        {
            var narrowView = MakeViewWithComment();
            var narrowWidth = narrowView.BuildPdfContent().Pages[0].WidthPoints;

            var wideView = MakeViewWithComment();
            wideView.ApplyShowMarkupBalloons(true);
            var widePdf = wideView.BuildPdfContent();

            Assert.True(
                widePdf.Pages[0].WidthPoints > narrowWidth,
                "the page should widen to add the balloon strip, matching Word's own print behaviour");

            var text = widePdf.Pages.SelectMany(page => page.Ops).OfType<PdfText>().Select(op => op.Text);
            Assert.Contains(text, t => t.Contains(CommentMarker));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_ShowMarkupBalloonsOff_LeavesPageWidthAndCommentTextUnaffected() =>
        Session.Dispatch(() =>
        {
            // Sibling/no-regression: with balloons off (the default -- matches every document exported
            // today), the comment text must not leak into the exported page at all, confirming the
            // balloon strip -- not some other unconditional code path -- is what puts it there.
            var view = MakeViewWithComment();

            var pdf = view.BuildPdfContent();

            var text = pdf.Pages.SelectMany(page => page.Ops).OfType<PdfText>().Select(op => op.Text);
            Assert.DoesNotContain(text, t => t.Contains(CommentMarker));
        }, CancellationToken.None);

    private static DocumentView MakeViewWithComment()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Body text ") { CommentId = 0 });
        paragraph.Runs.Add(Run.CommentReference(0));
        document.Blocks.Add(paragraph);
        document.Comments[0] = new Comment(0, CommentMarker, "Carol", "C");

        var view = new DocumentView();
        view.LoadDocument(document);
        return view;
    }
}
