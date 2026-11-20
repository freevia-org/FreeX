using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

using ModelParagraph = FreeW.Core.Model.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// W9: a page edit commits only the edited page's blocks (every other model block keeps its instance),
/// the hidden source editor refreshes only those blocks, re-flow keeps the boxes of unchanged pages,
/// and pages are sharded by render unit (a multi-paragraph list is one block on one page).
/// </summary>
public sealed class PagedEditW9IncrementalTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [StaFact]
    public void PageEdit_CommitsOnlyThatPagesBlocks_AndUndoRestoresTheExactDocument()
    {
        var (panel, editor) = BuildPanel(LongDocument(pages: 6));
        panel.AttachAsActiveSurface();
        editor.RefreshFromModel(); // records the hidden editor's units
        var before = editor.Model.Blocks.ToList();
        var page = panel.PageBoxes[2];
        var pageStart = page.ModelStart;
        var pageCount = page.ModelBlocks!.Length;

        Append(page, " typed");
        PaginatedCommitCoordinator.Commit(panel, editor);

        var after = editor.Model.Blocks;
        after.Should().HaveCount(before.Count);
        for (var i = 0; i < before.Count; i++)
        {
            if (i >= pageStart && i < pageStart + pageCount)
                continue;
            after[i].Should().BeSameAs(before[i], $"block {i} is not on the edited page");
        }
        ((ModelParagraph)after[pageStart + pageCount - 1]).PlainText.Should().EndWith(" typed");
        editor.IncrementalBodyRefreshCount.Should().Be(1, "the hidden editor refreshes only the replaced blocks");
        HiddenText(editor).Should().Be(FreshRenderText(editor.Model));

        editor.Undo();
        editor.Model.Blocks.Should().Equal(before, "undo restores the exact block instances");
        panel.Detach();
    }

    /// <summary>
    /// W13: a document with inline manual page breaks no longer re-renders the whole hidden editor on a
    /// commit: only the replaced blocks are refreshed, and the hidden editor still equals a fresh render.
    /// </summary>
    [StaFact]
    public void InlineBreakDocument_PageEdit_RefreshesTheHiddenEditorInPlace()
    {
        var (panel, editor) = BuildPanel(PagedEditW7PerformanceProbe.InlineBreakDocument(paragraphs: 40));
        panel.AttachAsActiveSurface();
        editor.RefreshFromModel();
        var before = editor.Model.Blocks.ToList();
        var fullRenders = editor.FullBodyRenderCount;

        Append(panel.PageBoxes[3], " typed");
        PaginatedCommitCoordinator.Commit(panel, editor);

        editor.IncrementalBodyRefreshCount.Should().Be(1, "only the replaced blocks are rendered again (refused at {0})", editor.LastBodyRefreshRefusal);
        editor.FullBodyRenderCount.Should().Be(fullRenders);
        HiddenText(editor).Should().Be(FreshRenderText(editor.Model));
        editor.Model.Blocks.Count(block => block is ModelParagraph p && p.Runs.Any(run => run.IsPageBreak))
            .Should().Be(before.Count(block => block is ModelParagraph p && p.Runs.Any(run => run.IsPageBreak)), "no break is lost");

        panel.Repaginate();
        PageTexts(panel).Should().Equal(PageTexts(PaginatedEditorPanel.Build(editor)));
        editor.Undo();
        editor.Model.Blocks.Should().Equal(before, "undo restores the exact block instances");
        panel.Detach();
    }

    /// <summary>
    /// W14: when every page ends in a paragraph split at an inline break, an edit commits only the pieces it
    /// shares with its neighbours -- a few blocks, not the chain of every page -- and the result is exactly
    /// what a whole read gives (the page texts of a fresh build, the hidden editor of a fresh render).
    /// </summary>
    [StaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void BreakOnEveryPage_PageEdit_CommitsOnlyTheNeighbourhood(bool atEnd)
    {
        var (panel, editor) = BuildPanel(PagedEditW7PerformanceProbe.InlineBreakDocument(paragraphs: 90, breakEvery: 3));
        panel.AttachAsActiveSurface();
        editor.RefreshFromModel();
        panel.PageBoxes.Count(box => box.ContinuesPrevious).Should().BeGreaterThan(20, "every page continues a split paragraph");
        var before = editor.Model.Blocks.ToList();
        var box = panel.PageBoxes[panel.PageBoxes.Count / 2];

        if (atEnd)
        {
            Append(box, " typed");
        }
        else
        {
            box.Body.Focus();
            box.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!.InsertTextInRun("lead ");
        }
        PaginatedCommitCoordinator.Commit(panel, editor);

        editor.Model.Blocks.Should().HaveCount(before.Count);
        var changed = Enumerable.Range(0, before.Count).Where(i => !ReferenceEquals(editor.Model.Blocks[i], before[i])).ToList();
        output.WriteLine($"changed blocks: {string.Join(",", changed)}");
        changed.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(5, "only the edited page's blocks (with the ones it shares with its neighbours) are replaced");
        editor.Model.Blocks.OfType<ModelParagraph>().Count(p => p.PlainText.Contains(atEnd ? " typed" : "lead "))
            .Should().Be(1);
        editor.Model.Blocks.Count(block => block is ModelParagraph p && p.Runs.Any(run => run.IsPageBreak))
            .Should().Be(before.Count(block => block is ModelParagraph p && p.Runs.Any(run => run.IsPageBreak)), "no break is lost");
        HiddenText(editor).Should().Be(FreshRenderText(editor.Model));

        panel.Repaginate();
        PageTexts(panel).Should().Equal(PageTexts(PaginatedEditorPanel.Build(editor)));
        editor.Undo();
        editor.Model.Blocks.Should().Equal(before, "undo restores the exact block instances");
        panel.Detach();
    }

    /// <summary>
    /// W14: an edit that adds a page shifts every later page; when the layout finishes by itself the pages past
    /// the sync target are left to the background pagination instead of all being built before the keystroke
    /// returns -- and the result, once finished, is exactly a fresh build.
    /// </summary>
    [StaFact]
    public void PageAddingEdit_HoldsBackLaterPages_AndFinishesAsAFreshBuild()
    {
        var editor = new DocumentView();
        editor.LoadModel(PagedEditW7PerformanceProbe.InlineBreakDocument(paragraphs: 400, breakEvery: 12));
        editor.CommitToModel();
        var panel = PaginatedEditorPanel.Build(editor, progressive: true);
        panel.Measure(new System.Windows.Size(1200, 900));
        panel.Arrange(new System.Windows.Rect(0, 0, 1200, 900));
        panel.FinishLayoutNow();
        panel.AttachAsActiveSurface();
        var pagesBefore = panel.PageBoxes.Count;
        pagesBefore.Should().BeGreaterThan(40);

        var box = panel.PageBoxes[2];
        Append(box, string.Concat(Enumerable.Repeat(" more words that wrap onto new lines", 60)));
        panel.Repaginate();

        output.WriteLine($"after the edit: {panel.PageBoxes.Count} boxes shown of {pagesBefore}, layout complete: {panel.IsLayoutComplete}");
        panel.IsLayoutComplete.Should().BeFalse("the pages the shift moved are built in the background");
        panel.FinishLayoutNow();
        panel.IsLayoutComplete.Should().BeTrue();
        panel.PageBoxes.Count.Should().BeGreaterThan(pagesBefore);
        PageTexts(panel).Should().Equal(PageTexts(PaginatedEditorPanel.Build(editor)));
        panel.Detach();
    }

    [StaFact]
    public void Reflow_KeepsUnchangedPageBoxes_AndMatchesAFreshBuild()
    {
        var (panel, editor) = BuildPanel(LongDocument(pages: 6));
        panel.AttachAsActiveSurface();
        Append(panel.PageBoxes[3], "s");
        panel.Repaginate();
        var firstBox = panel.PageBoxes[0];

        // Typing that does not move any block keeps every other box.
        Append(panel.PageBoxes[3], "s");
        panel.Repaginate();
        output.WriteLine($"kept {panel.LastRebuildKeptBoxes} of {panel.PageBoxes.Count}");

        panel.LastRebuildKeptBoxes.Should().BeGreaterThan(0);
        panel.PageBoxes[0].Should().BeSameAs(firstBox, "page 1 did not change");
        PageTexts(panel).Should().Equal(PageTexts(PaginatedEditorPanel.Build(editor)),
            "an incremental re-flow shows exactly what a fresh build shows");
        panel.Detach();
    }

    [StaFact]
    public void GrowingEdit_ReflowsLaterPages_AndMatchesAFreshBuild()
    {
        var (panel, editor) = BuildPanel(LongDocument(pages: 6));
        panel.AttachAsActiveSurface();

        Append(panel.PageBoxes[1], string.Concat(Enumerable.Repeat(" more words that wrap onto new lines", 40)));
        panel.Repaginate();

        PageTexts(panel).Should().Equal(PageTexts(PaginatedEditorPanel.Build(editor)));
        panel.LayoutSession.LastStatistics.Incremental.Should().BeTrue();
        panel.Detach();
    }

    [StaFact]
    public void MultiParagraphList_IsShardedAsOneUnit_SoLaterBlocksLandOnTheirOwnPages()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        for (var i = 0; i < 3; i++)
            doc.Blocks.Add(new ModelParagraph($"item {i}") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet } });
        doc.Blocks.Add(new ModelParagraph("second page") { Formatting = ParagraphFormatting.Default with { PageBreakBefore = true } });

        var (panel, _) = BuildPanel(doc);

        panel.PageBoxes.Should().HaveCount(2);
        PageTexts(panel)[1].Should().Be("second page");
        panel.PageBoxes[1].ModelStart.Should().Be(3);
    }

    private static TextDocument LongDocument(int pages)
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

    private static string HiddenText(DocumentView editor) =>
        new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;

    private static string FreshRenderText(TextDocument model)
    {
        var fresh = new DocumentView();
        fresh.LoadModel(model);
        return HiddenText(fresh);
    }

    private static (PaginatedEditorPanel panel, DocumentView editor) BuildPanel(TextDocument doc)
    {
        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        return (PaginatedEditorPanel.Build(editor), editor);
    }
}
