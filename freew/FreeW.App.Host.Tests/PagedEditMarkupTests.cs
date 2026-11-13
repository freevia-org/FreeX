using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Proofing;
using FreeW.Core.Model;
using Xunit;

using ModelParagraph = FreeW.Core.Model.Paragraph;
using ModelRun = FreeW.Core.Model.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// W11: the markup the continuous editor shows, on the pages: grammar underlines (also on a paragraph
/// continued from the page before), the grammar right-click menu acting through the shared command bus,
/// and comment / tracked-change balloons beside the page their anchor is on.
/// </summary>
public sealed class PagedEditMarkupTests
{
    private static readonly string Words = string.Join(" ", Enumerable.Range(0, 400).Select(i => $"word{i}"));

    [StaFact]
    public void GrammarUnderlines_AreDrawnOnThePageTheirTextIsOn()
    {
        var (panel, _) = Panel(
            new ModelParagraph("She ate a apple ,then left"),
            Break(new ModelParagraph("It was the the dog")));

        panel.RefreshPageMarkup();

        panel.PageBoxes.Should().HaveCount(2);
        panel.GrammarSquiggleTexts(panel.PageBoxes[0]).Should().Equal("a", "apple ,");
        panel.GrammarSquiggleTexts(panel.PageBoxes[1]).Should().Equal("the");
        panel.Detach();
    }

    [StaFact]
    public void GrammarUnderline_OnTheContinuationOfASplitParagraph_IsOnTheLaterPage()
    {
        var (panel, _) = Panel(new ModelParagraph("Opening."), new ModelParagraph(Words + " and the the end"));

        panel.RefreshPageMarkup();

        panel.PageBoxes.Count.Should().BeGreaterThan(1);
        panel.PageBoxes[^1].ContinuesPrevious.Should().BeTrue();
        panel.GrammarSquiggleTexts(panel.PageBoxes[^1]).Should().Equal("the");
        panel.PageBoxes.Take(panel.PageBoxes.Count - 1).SelectMany(panel.GrammarSquiggleTexts).Should().BeEmpty();
        panel.Detach();
    }

    [StaFact]
    public void GrammarMenu_OnAPage_OffersTheIssue_AndItsSuggestionFixesTheModelAsOneUndoableEdit()
    {
        var (panel, editor) = Panel(
            new ModelParagraph("Intro"),
            Break(new ModelParagraph("She ate a apple")));
        panel.RefreshPageMarkup();
        var page = panel.PageBoxes[1];
        var inside = PositionAfter(page, "She ate a".Length - 1);

        panel.PreparePageGrammarMenu(page, inside);

        var menu = panel.PageGrammarMenu;
        menu.Should().NotBeNull();
        page.Body.ContextMenu.Should().BeSameAs(menu);
        menu!.Items.OfType<MenuItem>().Where(item => item.Command is not null)
            .Should().OnlyContain(item => ReferenceEquals(item.CommandTarget, page.Body), "clipboard commands act on the page");
        var fix = menu.Items.OfType<MenuItem>().First(item => (string)item.Header == "an");

        fix.RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
        Flush();

        ((ModelParagraph)editor.Model.Blocks[1]).PlainText.Should().Be("She ate an apple");
        PageText(panel.PageBoxes[1]).Should().Be("She ate an apple", "the pages show the fix");
        editor.Undo();
        Flush();
        ((ModelParagraph)editor.Model.Blocks[1]).PlainText.Should().Be("She ate a apple");
        panel.Detach();
    }

    [StaFact]
    public void GrammarMenu_IgnoreOnce_RemovesThatUnderlineFromThePage()
    {
        var (panel, _) = Panel(new ModelParagraph("She ate a apple"), Break(new ModelParagraph("He ate a egg")));
        panel.RefreshPageMarkup();
        var page = panel.PageBoxes[0];
        panel.PreparePageGrammarMenu(page, PositionAfter(page, 8));
        var ignore = panel.PageGrammarMenu!.Items.OfType<MenuItem>().First(item => item.Tag as string == ProofingContextMenuPlanner.GrammarIgnoreOnce);

        ignore.RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
        panel.RefreshPageMarkup();

        panel.GrammarSquiggleTexts(panel.PageBoxes[0]).Should().BeEmpty();
        panel.GrammarSquiggleTexts(panel.PageBoxes[1]).Should().Equal(["a"], "only that occurrence is ignored");
        panel.Detach();
    }

    [StaFact]
    public void RightClickOutsideAnUnderline_KeepsTheNativeMenu()
    {
        var (panel, _) = Panel(new ModelParagraph("She ate a apple"));
        panel.RefreshPageMarkup();
        var page = panel.PageBoxes[0];

        panel.PreparePageGrammarMenu(page, PositionAfter(page, 1));

        panel.PageGrammarMenu.Should().BeNull();
        page.Body.ContextMenu.Should().BeNull();
        panel.Detach();
    }

    [StaFact]
    public void Balloons_ShowBesideThePageTheirCommentOrRevisionIsOn()
    {
        var first = new ModelParagraph();
        first.Runs.Add(new ModelRun("Commented text") { CommentId = 4 });
        first.Runs.Add(ModelRun.CommentReference(4));
        var second = Break(new ModelParagraph());
        second.Runs.Add(new ModelRun("Plain "));
        second.Runs.Add(new ModelRun("inserted") { Revision = RevisionKind.Inserted, RevisionAuthor = "Ann" });
        var (panel, editor) = Panel(first, second);
        editor.Model.Comments[4] = new Comment(4, "Check this.", "Casey", "C");

        panel.RefreshPageMarkup();
        panel.PageBalloons.Should().BeEmpty("balloons are off");

        editor.ShowMarkupBalloons = true;
        panel.RefreshPageMarkup();

        panel.PageBalloons.Select(balloon => (panel.PageBoxes.ToList().IndexOf(balloon.Box), balloon.Source.Kind, balloon.Source.Author))
            .Should().Equal((0, ReviewBalloonKind.Comment, "Casey"), (1, ReviewBalloonKind.Insertion, "Ann"));
        panel.PageBoxes.Should().OnlyContain(box => box.Margin.Right > 0, "each page leaves room for its balloon strip");

        editor.ShowMarkupBalloons = false;
        panel.RefreshPageMarkup();
        panel.PageBalloons.Should().BeEmpty();
        panel.PageBoxes.Should().OnlyContain(box => box.Margin.Right == 0);
        panel.Detach();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static ModelParagraph Break(ModelParagraph paragraph)
    {
        paragraph.Formatting = paragraph.Formatting with { PageBreakBefore = true };
        return paragraph;
    }

    private static (PaginatedEditorPanel Panel, DocumentView Editor) Panel(params FreeW.Core.Model.Block[] blocks)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Page.HeightPt = 360;
        foreach (var block in blocks)
            doc.Blocks.Add(block);
        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        var panel = PaginatedEditorPanel.Build(editor);
        panel.AttachAsActiveSurface();
        editor.RefreshFromModel();
        return (panel, editor);
    }

    private static TextPointer PositionAfter(PageBox box, int characters)
    {
        var pointer = box.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
        for (var i = 0; i < characters; i++)
            pointer = pointer.GetNextInsertionPosition(LogicalDirection.Forward)!;
        return pointer;
    }

    private static string PageText(PageBox box) =>
        new TextRange(box.Body.Document.ContentStart, box.Body.Document.ContentEnd).Text.TrimEnd('\r', '\n');

    private static void Flush() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
