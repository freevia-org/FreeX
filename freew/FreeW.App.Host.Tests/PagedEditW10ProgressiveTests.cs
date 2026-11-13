using System.Linq;
using System.Windows;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

using ModelParagraph = FreeW.Core.Model.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// W10: the progressive Page Edit surface -- open and re-flow lay out only the pages in view (the rest in
/// background slices), pages far from the viewport are virtualised, and neither ever loses, reorders or
/// changes a block: commits, undo and the final pagination match the whole, synchronous computation.
/// </summary>
public sealed class PagedEditW10ProgressiveTests
{
    [StaFact]
    public void ProgressiveOpen_ShowsTheFirstPagesFirst_AndCompletesToTheFullPagination()
    {
        var editor = Editor(LongDocument(pages: 60));
        var reference = PaginatedEditorPanel.Build(editor);

        var panel = PaginatedEditorPanel.Build(editor, progressive: true);

        panel.IsLayoutComplete.Should().BeFalse("a long document is paginated in the background");
        panel.PageBoxes.Count.Should().BeLessThan(reference.PageBoxes.Count);
        panel.PageBoxes.Count.Should().BeGreaterThanOrEqualTo(PaginatedEditorPanel.InitialPages);
        Ranges(panel).Should().Equal(Ranges(reference).Take(panel.PageBoxes.Count), "the pages shown are final");
        panel.TailStart.Should().Be(panel.PageBoxes[^1].ModelStart + panel.PageBoxes[^1].ModelBlocks!.Length);

        while (!panel.AdvanceBackgroundLayout(PaginatedEditorPanel.BackgroundSlice))
        {
        }

        panel.IsLayoutComplete.Should().BeTrue();
        Ranges(panel).Should().Equal(Ranges(reference), "background pagination ends exactly where the full computation does");
        PageTexts(panel).Should().Equal(PageTexts(reference));
        panel.PageBoxes.Select(b => b.FieldPageCount).Should().AllBeEquivalentTo(reference.PageBoxes.Count,
            "provisional page counts are corrected once the page count is known");
    }

    [StaFact]
    public void CommitDuringBackgroundPagination_KeepsTheBlocksNotYetOnAPage()
    {
        var editor = Editor(LongDocument(pages: 60));
        var panel = PaginatedEditorPanel.Build(editor, progressive: true);
        panel.AttachAsActiveSurface();
        var before = editor.Model.Blocks.ToList();
        var tail = panel.TailStart;
        tail.Should().BeGreaterThan(0);

        Append(panel.PageBoxes[0], " typed");
        panel.CommitPendingEdits();

        editor.Model.Blocks.Should().HaveCount(before.Count);
        for (var i = tail; i < before.Count; i++)
            editor.Model.Blocks[i].Should().BeSameAs(before[i], "blocks not yet paginated are kept as they are");
        ((ModelParagraph)editor.Model.Blocks[panel.PageBoxes[0].ModelBlocks!.Length - 1]).PlainText.Should().EndWith(" typed");

        panel.UndoCoordinator.Undo().Should().BeTrue();
        editor.Model.Blocks.Should().Equal(before, "undo restores the exact document");
        panel.FinishLayoutNow();
        panel.Detach();
    }

    [StaFact]
    public void WholeBodyCommitDuringBackgroundPagination_AppendsTheUnpaginatedBlocks()
    {
        var editor = Editor(LongDocument(pages: 60));
        var panel = PaginatedEditorPanel.Build(editor, progressive: true);
        panel.AttachAsActiveSurface();
        var before = editor.Model.Blocks.ToList();
        var tail = panel.TailStart;

        // A page with uncommitted edits the panel cannot place (an unknown edited box) forces the whole-body path.
        Append(panel.PageBoxes[0], "!");
        panel.OnWholeBodyCommitted(panel.TailStart); // forget the box ranges
        panel.MarkBodyPending();
        PaginatedCommitCoordinator.Commit(panel, editor);

        editor.Model.Blocks.Should().HaveCount(before.Count);
        for (var i = tail; i < before.Count; i++)
            editor.Model.Blocks[i].Should().BeSameAs(before[i]);
        panel.Detach();
    }

    [StaFact]
    public void EditDuringBackgroundPagination_RestartsIt_AndEndsAtTheFullPagination()
    {
        var editor = Editor(LongDocument(pages: 60));
        var panel = PaginatedEditorPanel.Build(editor, progressive: true);
        panel.AttachAsActiveSurface();
        panel.AdvanceBackgroundLayout(PaginatedEditorPanel.BackgroundSlice);

        Append(panel.PageBoxes[1], string.Concat(Enumerable.Repeat(" more words that wrap onto new lines", 30)));
        panel.Repaginate();
        panel.FinishLayoutNow();

        panel.IsLayoutComplete.Should().BeTrue();
        var reference = PaginatedEditorPanel.Build(editor);
        Ranges(panel).Should().Equal(Ranges(reference));
        PageTexts(panel).Should().Equal(PageTexts(reference));
        panel.Detach();
    }

    [StaFact]
    public void Virtualisation_RealisesOnlyPagesNearTheViewport_AndKeepsEveryPagesText()
    {
        var editor = Editor(LongDocument(pages: 60));
        var panel = PaginatedEditorPanel.Build(editor, progressive: true);
        panel.FinishLayoutNow();
        Layout(panel);
        panel.UpdateRealization();
        Layout(panel);

        var realized = panel.PageBoxes.Count(b => b.IsRealized);
        realized.Should().BeLessThan(panel.PageBoxes.Count / 2, "pages far from the viewport show placeholders");
        panel.PageBoxes[0].IsRealized.Should().BeTrue();
        var far = panel.PageBoxes[^1];
        far.IsRealized.Should().BeFalse();
        far.HeaderSubEditor.Should().BeNull("a virtualised page has no header/footer sub-editors");
        far.ActualHeight.Should().BeGreaterThan(0, "a placeholder keeps the page's height");

        // Every page still has its text, and the caret can go to a virtualised page.
        PageTexts(panel).Should().Equal(PageTexts(PaginatedEditorPanel.Build(editor)));
        panel.RestoreCaret(new PagedCaretPosition(BlockIndexOf(panel, far), 0));
        far.IsRealized.Should().BeTrue("placing the caret on a page realises it");

        // Scrolling realises the pages that come into view and drops the ones far behind.
        panel.ScrollToVerticalOffset(panel.ExtentHeight / 2);
        Layout(panel);
        panel.UpdateRealization();
        Layout(panel);
        panel.PageBoxes[panel.PageBoxes.Count / 2].IsRealized.Should().BeTrue();
        panel.PageBoxes[0].IsRealized.Should().BeFalse();
    }

    [StaFact]
    public void FirstReflowAfterOpen_KeepsUnchangedPages_AndLeavesHeaderFooterSlotsAlone()
    {
        var editor = Editor(ShortDocument(pages: 6));
        var panel = PaginatedEditorPanel.Build(editor);
        panel.AttachAsActiveSurface();
        var hf = editor.Model.FinalSectionHeadersFooters;
        var header = hf.Header;
        var footer = hf.Footer;
        var boxes = panel.PageBoxes.ToList();

        Append(panel.PageBoxes[3], "s");
        panel.Repaginate();

        hf.Header.Should().BeSameAs(header, "an unedited header slot is not rewritten");
        hf.Footer.Should().BeSameAs(footer);
        panel.LastRebuildKeptBoxes.Should().Be(boxes.Count, "no page's blocks or facts changed");
        panel.PageBoxes.Should().Equal(boxes);
        panel.Detach();
    }

    private static int BlockIndexOf(PaginatedEditorPanel panel, PageBox target)
    {
        var index = 0;
        foreach (var box in panel.PageBoxes)
        {
            if (ReferenceEquals(box, target))
                return index;
            index += box.Body.Document.Blocks.Count;
        }
        return index;
    }

    private static List<(int Start, int Count)> Ranges(PaginatedEditorPanel panel) =>
        panel.PageBoxes.Select(b => (b.ModelStart, b.ModelBlocks?.Length ?? -1)).ToList();

    private static void Layout(PaginatedEditorPanel panel)
    {
        var size = new Size(1000, 800);
        panel.Measure(size);
        panel.Arrange(new Rect(size));
        panel.UpdateLayout();
    }

    private static DocumentView Editor(TextDocument doc)
    {
        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        return editor;
    }

    private static TextDocument LongDocument(int pages)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Page.HeightPt = 400;
        var words = string.Join(" ", Enumerable.Repeat("layout", 30));
        for (var page = 0; page < pages; page++)
        {
            for (var p = 0; p < 4; p++)
                doc.Blocks.Add(new ModelParagraph($"p{page}.{p} {words}"));
        }
        return doc;
    }

    private static TextDocument ShortDocument(int pages)
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

    private static void Append(PageBox box, string text)
    {
        box.Body.Focus();
        var end = box.Body.Document.Blocks.LastBlock.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        end.InsertTextInRun(text);
    }

    private static List<string> PageTexts(PaginatedEditorPanel panel) =>
        panel.PageBoxes
            .Select(b => new TextRange(b.Body.Document.ContentStart, b.Body.Document.ContentEnd).Text.TrimEnd('\r', '\n'))
            .ToList();
}
