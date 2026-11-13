using System.Linq;
using System.Windows.Documents;
using System.Windows.Threading;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Proofing;
using FreeW.Core.Model;
using Xunit;

using ModelParagraph = FreeW.Core.Model.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// W10: commands written against the (hidden) continuous editor act on the page the user sees. The page
/// selection is mirrored into the hidden editor before a command reads it; a selection a command makes
/// there (Find Next, Go To, the spelling walk-through) is shown on the page; model edits re-flow the pages
/// and undo exactly; header/footer edits on a page are undoable.
/// </summary>
public sealed class PagedEditW10CommandBridgeTests
{
    [StaFact]
    public void PageSelection_IsMirroredIntoTheHiddenEditor_BeforeACommand()
    {
        var (panel, editor) = AttachedPanel();
        var page = panel.PageBoxes[2];
        SelectWord(page, "p2.1");

        panel.PrepareForCommand();

        editor.Selection.Text.Should().Be("p2.1");
        panel.Detach();
    }

    [StaFact]
    public void PendingPageEdits_AreCommittedBeforeTheSelectionIsMirrored()
    {
        var (panel, editor) = AttachedPanel();
        var page = panel.PageBoxes[1];
        var end = page.Body.Document.Blocks.LastBlock.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        end.InsertTextInRun(" fresh");
        SelectWord(page, "fresh");

        panel.PrepareForCommand();

        panel.HasPendingBodyEdits.Should().BeFalse();
        editor.Selection.Text.Should().Be("fresh", "the hidden editor holds the typed text before it is selected");
        panel.Detach();
    }

    [StaFact]
    public void FindNextOrGoToInTheHiddenEditor_SelectsOnThePage()
    {
        var (panel, editor) = AttachedPanel();
        var target = HiddenRangeOf(editor, "p3.2");

        // What Find Next does: select the match in the editor it searches.
        editor.Selection.Select(target.Start, target.End);

        panel.PageBoxes[3].Body.Selection.Text.Should().Be("p3.2");
        panel.ActiveBox.Should().BeSameAs(panel.PageBoxes[3]);

        // Go To: the caret moves to a heading/block in the hidden editor.
        editor.BringBlockIntoView(panel.PageBoxes[4].ModelStart);
        panel.ActiveBox.Should().BeSameAs(panel.PageBoxes[4]);
        new TextRange(panel.PageBoxes[4].Body.Document.ContentStart, panel.PageBoxes[4].Body.CaretPosition).Text
            .Should().BeEmpty("the caret lands at the start of the target block");
        panel.Detach();
    }

    [StaFact]
    public void Replace_OnThePageSelection_ReflowsThePages_AndUndoesExactly()
    {
        var (panel, editor) = AttachedPanel();
        var before = editor.Model.Blocks.ToList();
        SelectWord(panel.PageBoxes[2], "p2.1");
        panel.PrepareForCommand();

        editor.ReplaceSelectionForFind("REPLACED");
        Pump();

        PageText(panel, 2).Should().Contain("REPLACED").And.NotContain("p2.1");
        panel.UndoCoordinator.Undo().Should().BeTrue();
        editor.Model.Blocks.Should().Equal(before, "undo restores the exact block instances");
        PageText(panel, 2).Should().Contain("p2.1");
        panel.Detach();
    }

    [StaFact]
    public void RibbonFormatting_AppliesToThePageSelection()
    {
        var (panel, editor) = AttachedPanel();
        var before = editor.Model.Blocks.ToList();
        SelectWord(panel.PageBoxes[1], "p1.3");
        panel.PrepareForCommand();

        // Home > Bold / Font: the model-level path reads the (mirrored) selection.
        editor.TrySetSelectedRunFormatting(f => f.Bold == true, f => f with { Bold = true }).Should().BeTrue();
        Pump();

        var paragraph = editor.Model.Blocks.OfType<ModelParagraph>().Single(p => p.PlainText.StartsWith("p1.3"));
        paragraph.Runs.Should().Contain(run => run.Text == "p1.3" && run.Formatting.Bold == true);
        panel.PageBoxes[1].Body.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(p => p.Inlines.OfType<System.Windows.Documents.Run>())
            .Should().Contain(run => run.Text == "p1.3" && run.FontWeight == System.Windows.FontWeights.Bold,
                "the page shows the formatted text");

        // Styles act on the paragraphs of the page selection.
        SelectWord(panel.PageBoxes[1], "p1.0");
        panel.PrepareForCommand();
        editor.SetParagraphStyle("Heading1");
        Pump();
        editor.Model.Blocks.OfType<ModelParagraph>().Single(p => p.PlainText.StartsWith("p1.0")).StyleId.Should().Be("Heading1");

        // A native command with no model path (formatting the caret's next text) targets the page.
        editor.CommandSelection.Should().BeSameAs(panel.ActiveBox!.Body.Selection);

        panel.UndoCoordinator.Undo().Should().BeTrue();
        panel.UndoCoordinator.Undo().Should().BeTrue();
        editor.Model.Blocks.Should().Equal(before);
        panel.Detach();
    }

    [StaFact]
    public void SpellingWalkThrough_SelectsTheIssueOnThePage_AndAppliesTheCorrection()
    {
        var (panel, editor) = AttachedPanel();
        var block = panel.PageBoxes[3].ModelStart + 1;
        var paragraph = (ModelParagraph)editor.Model.Blocks[block];
        var offset = paragraph.PlainText.IndexOf("layout", StringComparison.Ordinal);
        var issue = new ProofingReviewIssue(
            ProofingReviewIssueKind.Spelling, block, offset, 6, "layout",
            [new ProofingReviewSuggestion("LAYOUT", offset, 6)]);

        editor.SelectProofingIssue(issue).Should().BeTrue();
        panel.PageBoxes[3].Body.Selection.Text.Should().Be("layout");

        editor.ApplyProofingReplacements([new ProofingReviewReplacement(block, offset, 6, "LAYOUT")]).Should().BeTrue();
        Pump();
        ((ModelParagraph)editor.Model.Blocks[block]).PlainText.Should().Contain("LAYOUT");
        PageText(panel, 3).Should().Contain("LAYOUT");
        panel.Detach();
    }

    [StaFact]
    public void HeaderEditOnAPage_IsUndoable()
    {
        var doc = Document(pages: 3);
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter();
        doc.FinalSectionHeadersFooters.Header.Paragraphs.Add(new ModelParagraph("Original header"));
        var (panel, editor) = AttachedPanel(doc);
        var original = editor.Model.FinalSectionHeadersFooters.Header;
        var header = panel.PageBoxes[1].HeaderSubEditor!;

        var end = header.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        end.InsertTextInRun(" edited");
        panel.CommitPendingEdits();

        editor.Model.FinalSectionHeadersFooters.Header!.Paragraphs.Single().PlainText.Should().Be("Original header edited");
        editor.Commands.CanUndo.Should().BeTrue("the header edit is on the shared command history");

        panel.UndoCoordinator.Undo().Should().BeTrue();
        editor.Model.FinalSectionHeadersFooters.Header.Should().BeSameAs(original, "undo restores the exact header story");
        panel.Detach();
    }

    private static (PaginatedEditorPanel Panel, DocumentView Editor) AttachedPanel(TextDocument? doc = null)
    {
        var editor = new DocumentView();
        editor.LoadModel(doc ?? Document(pages: 6));
        editor.CommitToModel();
        var panel = PaginatedEditorPanel.Build(editor);
        panel.AttachAsActiveSurface();
        return (panel, editor);
    }

    private static TextDocument Document(int pages)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Page.HeightPt = 400;
        var words = string.Join(" ", Enumerable.Repeat("layout", 40));
        for (var page = 0; page < pages; page++)
        {
            for (var p = 0; p < 4; p++)
            {
                doc.Blocks.Add(new ModelParagraph($"p{page}.{p} {words}")
                {
                    Formatting = ParagraphFormatting.Default with { PageBreakBefore = page > 0 && p == 0 }
                });
            }
        }
        return doc;
    }

    private static void SelectWord(PageBox box, string word)
    {
        var range = RangeOf(box.Body.Document.ContentStart, box.Body.Document.ContentEnd, word);
        box.Body.Focus();
        box.Body.Selection.Select(range.Start, range.End);
    }

    private static TextRange HiddenRangeOf(DocumentView editor, string word) =>
        RangeOf(editor.Document.ContentStart, editor.Document.ContentEnd, word);

    private static TextRange RangeOf(TextPointer from, TextPointer to, string word)
    {
        for (var position = from; position is not null && position.CompareTo(to) < 0;
             position = position.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (position.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
                continue;
            var text = position.GetTextInRun(LogicalDirection.Forward);
            var index = text.IndexOf(word, StringComparison.Ordinal);
            if (index >= 0)
                return new TextRange(position.GetPositionAtOffset(index)!, position.GetPositionAtOffset(index + word.Length)!);
        }
        throw new InvalidOperationException($"'{word}' not found");
    }

    private static string PageText(PaginatedEditorPanel panel, int page) =>
        new TextRange(panel.PageBoxes[page].Body.Document.ContentStart, panel.PageBoxes[page].Body.Document.ContentEnd).Text;

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
