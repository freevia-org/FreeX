using Avalonia.Headless;
using System.Threading;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class ReviewChangeNavigationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static TextDocument BuildDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        for (var i = 0; i < 3; i++)
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run($"change {i}")
            {
                Revision = RevisionKind.Inserted,
                RevisionAuthor = "Tester",
            });
            document.Blocks.Add(paragraph);
        }
        return document;
    }

    [Fact]
    public async Task NavigateToRevision_moves_caret_and_requests_scroll_without_mutating_model()
    {
        await Session.Dispatch(() =>
        {
            var document = BuildDocument();
            var view = new DocumentView();
            view.LoadDocument(document);
            var entry = RevisionList.Enumerate(document)[1];
            var before = document.PlainText;
            var scrollRequests = 0;
            view.ScrollToCaretRequested += () => scrollRequests++;

            view.NavigateToRevision(entry);

            view.CaretPositionForTest.Should().Be((1, 0));
            scrollRequests.Should().Be(1);
            document.PlainText.Should().Be(before);
            RevisionList.Enumerate(document).Should().HaveCount(3);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NavigateToRevision_uses_containing_table_top_level_owner()
    {
        await Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph { Runs = { new Run("before") } });
            var table = new Table();
            var row = new TableRow();
            var cell = new TableCell();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("table change")
            {
                Revision = RevisionKind.Inserted,
                RevisionAuthor = "Tester",
            });
            cell.Paragraphs.Add(paragraph);
            row.Cells.Add(cell);
            table.Rows.Add(row);
            document.Blocks.Add(table);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.NavigateToRevision(RevisionList.Enumerate(document).Single());

            view.CaretPositionForTest.Should().Be((1, 0));
            view.CellCaretInfo.Should().BeNull("WPF navigates to the containing top-level table block");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReviewingPane_step_wraps_and_refresh_preserves_valid_selection()
    {
        await Session.Dispatch(() =>
        {
            var document = BuildDocument();
            var view = new DocumentView();
            view.LoadDocument(document);
            var pane = new ReviewingPane(view);

            pane.StepRevision(-1).Should().BeTrue();
            pane.SelectedRevisionIndexForTest.Should().Be(2);
            view.CaretPositionForTest.Should().Be((2, 0));

            pane.Refresh();
            pane.SelectedRevisionIndexForTest.Should().Be(2);
            pane.StepRevision(1).Should().BeTrue();
            pane.SelectedRevisionIndexForTest.Should().Be(0);
            view.CaretPositionForTest.Should().Be((0, 0));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReviewingPane_refresh_preserves_selection_slot_after_revision_mutation()
    {
        await Session.Dispatch(() =>
        {
            var document = BuildDocument();
            var view = new DocumentView();
            view.LoadDocument(document);
            var pane = new ReviewingPane(view);

            pane.StepRevision(-1).Should().BeTrue();
            var last = pane.SelectedRevisionForTest;
            last.Should().NotBeNull();
            RevisionList.Accept(document, last!).Should().BeTrue();
            view.InvalidateAfterExternalMutation();
            pane.Refresh();

            pane.RevisionItemCount.Should().Be(2);
            pane.SelectedRevisionIndexForTest.Should().Be(1, "the WPF slot clamps to the new last item");
            pane.SelectedRevisionForTest!.Text.Should().Be("change 1");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Production_MainWindow_step_opens_hidden_pane_and_navigates()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.LoadDocument(BuildDocument());

            window.ReviewingPane.IsVisible.Should().BeFalse();
            window.StepRevision(1).Should().BeTrue();

            window.ReviewingPane.IsVisible.Should().BeTrue();
            window.ReviewingPane.SelectedRevisionIndexForTest.Should().Be(1,
                "opening the hidden pane selects index 0 before Next advances once");
            window.Editor.CaretPositionForTest.Should().Be((1, 0));
        }, CancellationToken.None);
    }
}
