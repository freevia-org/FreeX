using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Page Edit is a WPF host surface, while Avalonia's normal PrintLayout is already a live,
/// multi-page editor. These tests prove the user-visible contract against that production surface
/// rather than asserting that Avalonia has a second host control.
/// </summary>
public sealed class PagedEditParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task PageEdit_toggle_uses_live_print_surface_and_restores_prior_view_and_selection()
    {
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var editor = window.Editor;
            editor.LoadDocument(BuildLongDocument());
            editor.ViewMode = DocumentViewMode.WebLayout;
            editor.Measure(new Size(816, 4000));
            editor.MoveCaretToBlockForTest(0, 8);
            editor.SetSelectionRangePublic(0, 1, 0, 8);
            var caretBefore = editor.CaretPosition;
            var selectedBefore = editor.SelectedText;
            var viewToggles = window.StatusViewControlsForTests;
            ((ToggleButton)viewToggles[1]).IsChecked.Should().BeFalse();
            ((ToggleButton)viewToggles[2]).IsChecked.Should().BeTrue();
            ((ToggleButton)viewToggles[4]).IsChecked.Should().BeFalse();

            window.TogglePagedEditViewForTests();
            editor.Measure(new Size(816, double.PositiveInfinity));

            window.IsPagedEditModeActiveForTests.Should().BeTrue();
            window.IsWorkspaceShowingLiveEditor.Should().BeTrue(
                "Avalonia keeps the existing live editor as the Page Edit surface");
            editor.ViewMode.Should().Be(DocumentViewMode.PrintLayout);
            editor.PageCount.Should().BeGreaterThan(1);
            editor.CaretPosition.Should().Be(caretBefore);
            editor.SelectedText.Should().Be(selectedBefore);
            ((ToggleButton)viewToggles[1]).IsChecked.Should().BeFalse();
            ((ToggleButton)viewToggles[2]).IsChecked.Should().BeFalse();
            ((ToggleButton)viewToggles[4]).IsChecked.Should().BeTrue();

            window.TogglePagedEditViewForTests();

            window.IsPagedEditModeActiveForTests.Should().BeFalse();
            editor.ViewMode.Should().Be(DocumentViewMode.WebLayout);
            editor.CaretPosition.Should().Be(caretBefore);
            editor.SelectedText.Should().Be(selectedBefore);
            ((ToggleButton)viewToggles[1]).IsChecked.Should().BeFalse();
            ((ToggleButton)viewToggles[2]).IsChecked.Should().BeTrue();
            ((ToggleButton)viewToggles[4]).IsChecked.Should().BeFalse();
        });

        ran.Should().BeTrue("the production Avalonia headless surface must be available for this gate");
    }

    [Fact]
    public async Task PageEdit_toggle_from_draft_restores_draft_and_checked_state()
    {
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var editor = window.Editor;
            editor.LoadDocument(BuildLongDocument());
            editor.ViewMode = DocumentViewMode.Draft;
            var viewToggles = window.StatusViewControlsForTests;

            ((ToggleButton)viewToggles[3]).IsChecked.Should().BeTrue();
            ((ToggleButton)viewToggles[4]).IsChecked.Should().BeFalse();

            window.TogglePagedEditViewForTests();
            editor.Measure(new Size(816, double.PositiveInfinity));

            editor.ViewMode.Should().Be(DocumentViewMode.PrintLayout);
            editor.PageCount.Should().BeGreaterThan(1);
            ((ToggleButton)viewToggles[3]).IsChecked.Should().BeFalse();
            ((ToggleButton)viewToggles[4]).IsChecked.Should().BeTrue();

            window.TogglePagedEditViewForTests();

            editor.ViewMode.Should().Be(DocumentViewMode.Draft);
            ((ToggleButton)viewToggles[3]).IsChecked.Should().BeTrue();
            ((ToggleButton)viewToggles[4]).IsChecked.Should().BeFalse();
        });

        ran.Should().BeTrue("the production Avalonia headless surface must be available for this gate");
    }

    [Fact]
    public async Task PrintLayout_page_edit_surface_reflows_real_model_and_supports_undo_redo()
    {
        var ran = await OnUiThread(() =>
        {
            var document = BuildLongDocument();
            var editor = new DocumentView();
            editor.LoadDocument(document);
            editor.ViewMode = DocumentViewMode.PrintLayout;
            editor.Measure(new Size(816, double.PositiveInfinity));

            var initialPages = editor.PageCount;
            initialPages.Should().BeGreaterThan(1);
            editor.MoveCaretToBlockForTest(document.Blocks.Count - 1,
                ((Paragraph)document.Blocks[^1]).PlainText.Length);
            editor.CaretPageIndex.Should().BeGreaterThan(0);

            var before = editor.PlainText;
            editor.InsertText(new string('x', 5000));
            editor.Measure(new Size(816, double.PositiveInfinity));
            editor.PageCount.Should().BeGreaterThanOrEqualTo(initialPages,
                "editing in Page Edit must reflow the live page surfaces");
            editor.PlainText.Should().Contain(before);
            editor.CanUndo.Should().BeTrue();

            editor.Undo();
            editor.PlainText.Should().Be(before);
            editor.Redo();
            editor.PlainText.Should().Be(before + new string('x', 5000));
        });

        ran.Should().BeTrue("the production Avalonia headless surface must be available for this gate");
    }

    [Fact]
    public async Task PrintLayout_page_edit_surface_preserves_header_footer_editing()
    {
        var ran = await OnUiThread(() =>
        {
            var document = BuildLongDocument();
            var editor = new DocumentView();
            editor.LoadDocument(document);
            editor.ViewMode = DocumentViewMode.PrintLayout;
            editor.Measure(new Size(816, double.PositiveInfinity));

            editor.PlaceCaretInHeaderFooter(footer: false);
            editor.HeaderFooterCaretInfo.Should().NotBeNull();
            editor.InsertText("Header from Page Edit");
            editor.PlaceCaretInHeaderFooter(footer: true);
            editor.InsertText("Footer from Page Edit");
            editor.ExitHeaderFooterCaret();
            editor.Measure(new Size(816, double.PositiveInfinity));

            document.FinalSectionHeadersFooters.Header!.Paragraphs[0].PlainText
                .Should().Contain("Header from Page Edit");
            document.FinalSectionHeadersFooters.Footer!.Paragraphs[0].PlainText
                .Should().Contain("Footer from Page Edit");
            editor.PageCount.Should().BeGreaterThan(1);
        });

        ran.Should().BeTrue("the production Avalonia headless surface must be available for this gate");
    }

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static TextDocument BuildLongDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        for (var i = 0; i < 80; i++)
        {
            document.Blocks.Add(new Paragraph(
                $"Page Edit paragraph {i + 1}: " +
                "This authored content is deliberately long enough to exercise real page " +
                "segmentation, caret routing, and re-pagination across the document surface. "));
        }

        return document;
    }
}
