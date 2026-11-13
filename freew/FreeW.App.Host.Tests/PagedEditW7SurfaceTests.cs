using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

using ModelParagraph = FreeW.Core.Model.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// W7: the paged (Print Layout page-by-page) editor as a real editing surface -- no data loss when
/// anything commits the hidden continuous editor, undo/redo on the shared DocumentCommandBus (one
/// unit per edit burst, across pages, restoring the exact prior document), the caret following text
/// as it re-flows between pages, Backspace/Delete across a page boundary, and document-level caret
/// navigation.
/// </summary>
public sealed class PagedEditW7SurfaceTests
{
    // ── commit / data loss ────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void CommitWithoutEdits_LeavesModelInstancesUntouched_AndPushesNoUndo()
    {
        var (panel, editor) = BuildTwoPagePanel();
        var before = editor.Model.Blocks.ToList();

        PaginatedCommitCoordinator.Commit(panel, editor);

        editor.Model.Blocks.Should().Equal(before, "an untouched page set must not be re-read into the model");
        editor.Commands.CanUndo.Should().BeFalse();
    }

    [StaFact]
    public void CommitToModelOnHiddenEditor_WhileAttached_CommitsThePages()
    {
        var (panel, editor) = BuildTwoPagePanel();
        panel.AttachAsActiveSurface();

        Append(panel.PageBoxes[1], " typed on page two");
        // Save, AutoSave, Print and every ribbon command call this on the (hidden) continuous editor.
        editor.CommitToModel();

        ModelTexts(editor).Should().Contain(t => t.EndsWith("typed on page two"),
            "committing the hidden editor must read the pages, not its own stale surface");
        panel.HasPendingBodyEdits.Should().BeFalse();
        panel.Detach();
    }

    [StaFact]
    public void TypingOnAPage_RaisesEditedBeforeTheModelChanges()
    {
        var (panel, editor) = BuildTwoPagePanel();
        var edited = 0;
        panel.Edited += () => edited++;

        Append(panel.PageBoxes[0], "!");

        edited.Should().BeGreaterThan(0, "the host marks the document dirty from this immediately");
        panel.HasPendingBodyEdits.Should().BeTrue();
        ModelTexts(editor)[0].Should().Be("Page 1 content", "the model only changes on commit");
    }

    [StaFact]
    public void LoadingAnotherDocument_DiscardsPendingPageEditsInsteadOfWritingThemIntoIt()
    {
        var (panel, editor) = BuildTwoPagePanel();
        panel.AttachAsActiveSurface();
        Append(panel.PageBoxes[0], " stale edit");

        var other = TextDocument.CreateEmpty();
        other.Blocks.Clear();
        other.Blocks.Add(new ModelParagraph("Other document"));
        editor.LoadModel(other);
        editor.CommitToModel();

        ModelTexts(editor).Should().Equal(["Other document"]);
        panel.SourceModel.Should().BeSameAs(other);
        panel.Detach();
    }

    [StaFact]
    public void Detach_ReleasesTheHiddenEditor()
    {
        var (panel, editor) = BuildTwoPagePanel();
        panel.AttachAsActiveSurface();
        editor.ExternalEditingSurface.Should().BeSameAs(panel);

        panel.Detach();

        editor.ExternalEditingSurface.Should().BeNull();
    }

    // ── undo / redo on the shared bus ─────────────────────────────────────────────────────────────

    [StaFact]
    public void EditsOnTwoPages_AreOneUndoUnit_AndUndoRestoresTheExactBlocks()
    {
        var (panel, editor) = BuildTwoPagePanel();
        var original = editor.Model.Blocks.ToList();

        Append(panel.PageBoxes[0], " A");
        Append(panel.PageBoxes[1], " B");
        panel.Repaginate();

        ModelTexts(editor).Should().Equal(["Page 1 content A", "Page 2 start B"]);
        editor.Commands.CanUndo.Should().BeTrue("page edits land on the continuous editor's history");

        panel.UndoCoordinator.Undo().Should().BeTrue();

        editor.Model.Blocks.Should().Equal(original,
            "one undo reverts the whole burst across both pages to the very same block instances");
        editor.Commands.CanUndo.Should().BeFalse();
        PageTexts(panel).Should().Equal(["Page 1 content", "Page 2 start"]);

        panel.UndoCoordinator.Redo().Should().BeTrue();
        ModelTexts(editor).Should().Equal(["Page 1 content A", "Page 2 start B"]);
    }

    [StaFact]
    public void HiddenEditorUndo_WhileAttached_UndoesThePagesThroughTheSameHistory()
    {
        var (panel, editor) = BuildTwoPagePanel();
        panel.AttachAsActiveSurface();

        Append(panel.PageBoxes[0], " Q");
        editor.CanUndo.Should().BeTrue("pending page typing is undoable from the ribbon/QAT");

        editor.Undo();

        ModelTexts(editor).Should().Equal(["Page 1 content", "Page 2 start"]);
        PageTexts(panel)[0].Should().Be("Page 1 content");
        editor.CanRedo.Should().BeTrue();
        panel.Detach();
    }

    [StaFact]
    public void UndoHistory_SurvivesLeavingThePagedView()
    {
        var (panel, editor) = BuildTwoPagePanel();
        panel.AttachAsActiveSurface();
        Append(panel.PageBoxes[0], " kept");
        panel.CommitPendingEdits();
        panel.Detach();
        editor.RefreshFromSharedDocument();

        editor.CanUndo.Should().BeTrue("the paged edit is on the shared bus, not a panel-private stack");
        editor.Undo();
        ModelTexts(editor).Should().Equal(["Page 1 content", "Page 2 start"]);
    }

    // ── reflow ────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void CaretFollowsItsParagraph_WhenTypingPushesItOntoTheNextPage()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        for (var i = 0; i < 60; i++)
            doc.Blocks.Add(new ModelParagraph($"Line {i}"));
        var (panel, editor) = BuildPanel(doc);
        panel.PageBoxes.Count.Should().BeGreaterThan(1);

        // Caret at the end of the last paragraph on page 1; the paragraph above it then grows by
        // several lines (text typed there), pushing the caret's paragraph onto page 2.
        var firstPage = panel.PageBoxes[0];
        var lastParagraph = (System.Windows.Documents.Paragraph)firstPage.Body.Document.Blocks.LastBlock;
        var previousParagraph = (System.Windows.Documents.Paragraph)lastParagraph.PreviousBlock;
        var lastText = new TextRange(lastParagraph.ContentStart, lastParagraph.ContentEnd).Text;
        previousParagraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)
            .InsertTextInRun(string.Concat(Enumerable.Repeat(" grown words to wrap", 40)));
        firstPage.Body.Focus();
        firstPage.Body.CaretPosition = lastParagraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);

        panel.Repaginate();

        var active = panel.ActiveBox;
        active.Should().NotBeNull();
        active!.PageNumber.Should().BeGreaterThan(1, "the caret's paragraph no longer fits on page 1");
        var caretParagraph = active.Body.CaretPosition.Paragraph;
        caretParagraph.Should().NotBeNull();
        new TextRange(caretParagraph!.ContentStart, caretParagraph.ContentEnd).Text
            .Should().Be(lastText, "the caret stays in the paragraph it was in, at its end");
        editor.Model.Blocks.Should().HaveCount(60);
    }

    [StaFact]
    public void BurstThatKeepsPageHeights_CommitsWithoutRebuildingPages_ButGrowthRepaginates()
    {
        var (panel, editor) = BuildTwoPagePanel();
        Layout(panel);
        panel.CaptureContentBottoms();
        var boxesBefore = panel.PageBoxes.ToList();

        // One character inside the first line: no page gets taller.
        var first = panel.PageBoxes[0];
        first.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!
            .GetPositionAtOffset(2)!.InsertTextInRun("x");
        Layout(panel);
        panel.CommitAndRepaginateIfLayoutChanged();

        panel.PageBoxes.Should().Equal(boxesBefore, "an edit that moves nothing between pages skips re-pagination");
        ModelTexts(editor)[0].Should().Contain("x", "the burst is still committed");
        editor.Commands.CanUndo.Should().BeTrue();

        // Now a new line of text: page 1 gets taller, so the pages are re-laid out.
        Append(first, string.Concat(Enumerable.Repeat(" grow", 60)));
        Layout(panel);
        panel.CommitAndRepaginateIfLayoutChanged();

        panel.PageBoxes[0].Should().NotBeSameAs(first, "a height change re-runs pagination");
        ModelTexts(editor)[0].Should().EndWith("grow");
    }

    [StaFact]
    public void BurstOnAPageWithoutMeasuredLayout_AlwaysRepaginates()
    {
        var (panel, _) = BuildTwoPagePanel();
        var first = panel.PageBoxes[0];
        Append(first, "y");

        panel.CommitAndRepaginateIfLayoutChanged();

        panel.PageBoxes[0].Should().NotBeSameAs(first, "unknown heights must never skip re-pagination");
    }

    // ── across the page boundary ──────────────────────────────────────────────────────────────────

    [StaFact]
    public void BackspaceAtTopOfPage_JoinsWithThePreviousPagesLastParagraph_AsOneUndoUnit()
    {
        var (panel, editor) = BuildTwoPagePanel();
        var original = editor.Model.Blocks.ToList();

        panel.MergeAcrossBoundary(panel.PageBoxes[0], panel.PageBoxes[1]).Should().BeTrue();
        panel.Repaginate();

        ModelTexts(editor).Should().Equal(["Page 1 contentPage 2 start"]);
        panel.PageBoxes.Should().HaveCount(1, "the joined paragraph keeps page 1's formatting (no page break)");

        panel.UndoCoordinator.Undo().Should().BeTrue();
        editor.Model.Blocks.Should().Equal(original);
        panel.PageBoxes.Should().HaveCount(2);
    }

    [StaFact]
    public void MergeAcrossBoundary_RefusesAMissingOrNonParagraphEdge()
    {
        var (panel, editor) = BuildTwoPagePanel();

        panel.MergeAcrossBoundary(panel.PageBoxes[1], null).Should().BeFalse("there is no page after the last");

        // A non-paragraph edge (here: the lower page's first block is a list) is left to native keys.
        var lower = panel.PageBoxes[1];
        var paragraph = lower.Body.Document.Blocks.FirstBlock;
        var list = new List();
        lower.Body.Document.Blocks.InsertBefore(paragraph, list);
        list.ListItems.Add(new ListItem(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("item"))));

        panel.MergeAcrossBoundary(panel.PageBoxes[0], lower).Should().BeFalse();
        PageTexts(panel)[0].Should().Be("Page 1 content", "nothing was moved");
        editor.Model.Blocks.Should().HaveCount(2);
    }

    [StaFact]
    public void DocumentNavigation_MovesBetweenPages()
    {
        var (panel, _) = BuildTwoPagePanel();
        var first = panel.PageBoxes[0];
        var second = panel.PageBoxes[1];

        panel.OnDocumentNavigationRequested(first, PageNavigationTarget.DocumentEnd, extendSelection: false);
        panel.ActiveBox.Should().BeSameAs(second);
        second.Body.CaretPosition.CompareTo(
            second.Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)).Should().Be(0);

        panel.OnDocumentNavigationRequested(second, PageNavigationTarget.DocumentStart, extendSelection: false);
        panel.ActiveBox.Should().BeSameAs(first);

        panel.OnDocumentNavigationRequested(first, PageNavigationTarget.NextPage, extendSelection: false);
        panel.ActiveBox.Should().BeSameAs(second);

        panel.OnDocumentNavigationRequested(second, PageNavigationTarget.PreviousPage, extendSelection: true);
        panel.CrossPageSelection.IsActive.Should().BeTrue("Shift+Page Up extends the selection across the page");
    }

    [StaFact]
    public void SelectAllPages_SpansEveryPage()
    {
        var (panel, _) = BuildTwoPagePanel();

        panel.SelectAllPages();

        panel.CrossPageSelection.IsActive.Should().BeTrue();
        var text = panel.CrossPageSelection.GetSelectedText(panel.PageBoxes);
        text.Should().Contain("Page 1 content").And.Contain("Page 2 start");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────

    private static void Layout(PaginatedEditorPanel panel)
    {
        panel.Measure(new System.Windows.Size(1200, 4000));
        panel.Arrange(new System.Windows.Rect(0, 0, 1200, 4000));
        panel.UpdateLayout();
    }

    private static void Append(PageBox box, string text)
    {
        box.Body.Focus();
        var end = box.Body.Document.Blocks.LastBlock.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        end.InsertTextInRun(text);
    }

    private static List<string> ModelTexts(DocumentView editor) =>
        editor.Model.Blocks.OfType<ModelParagraph>().Select(p => p.PlainText.TrimEnd('\r', '\n')).ToList();

    private static List<string> PageTexts(PaginatedEditorPanel panel) =>
        panel.PageBoxes
            .Select(b => new TextRange(b.Body.Document.ContentStart, b.Body.Document.ContentEnd).Text.TrimEnd('\r', '\n'))
            .ToList();

    private static (PaginatedEditorPanel panel, DocumentView editor) BuildTwoPagePanel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new ModelParagraph("Page 1 content"));
        doc.Blocks.Add(new ModelParagraph("Page 2 start")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });
        var built = BuildPanel(doc);
        built.panel.PageBoxes.Should().HaveCount(2);
        return built;
    }

    private static (PaginatedEditorPanel panel, DocumentView editor) BuildPanel(TextDocument doc)
    {
        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        return (PaginatedEditorPanel.Build(editor), editor);
    }
}
