using System.Reflection;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for the r141 "shared-drag-drop" finding
/// <c>freew-crosspage-drag-duplicate-on-partial-delete</c>: a cross-page drag-MOVE must be
/// atomic -- either the full spanned selection is removed from its source boxes, or the
/// pre-captured selection text must never be re-inserted at the drop point.
///
/// <para>
/// Before the fix, <c>PaginatedEditorPanel.DeleteCrossPageSelection</c> caught each spanned
/// box's delete failure individually and kept going, and <c>OnBodyMouseUp</c> always inserted
/// the pre-captured selection text at the drop target regardless of whether every box's delete
/// actually succeeded. When one spanned box's <c>TextRange.Text = string.Empty</c> mutation
/// throws while the earlier read (<see cref="CrossPageSelection.GetSelectedText"/>) of that same
/// box succeeded, that box's original content survives *and* the same text gets re-inserted
/// elsewhere -- a silent duplication.
/// </para>
///
/// <para>
/// These tests reach the same <c>CutSelection</c> + conditional-insert sequence that
/// <c>OnBodyMouseUp</c> performs for a real mouse-driven cross-page drag-move (see the identical,
/// already-established testing convention in <c>PagedEditW18SelPolishTests</c>, whose own doc
/// comment explains why exercising this infrastructure directly is the accepted proxy for the
/// mouse gesture in this headless WPF test environment). <c>CutSelection</c> is invoked through
/// reflection so the same test source compiles unchanged whether the method returns
/// <c>void</c> (pre-fix) or <c>bool</c> (post-fix) -- a <c>void</c> result is treated as "always
/// proceed to insert", reproducing the pre-fix caller behaviour exactly.
/// </para>
///
/// <para>Runs on STA because tests create real WPF RichTextBox / FlowDocument instances.</para>
/// </summary>
public sealed class PagedEditCrossPageDragMoveAtomicityTests
{
    /// <summary>
    /// Simulates the real-world race the source comments describe ("position may be invalid
    /// after earlier deletions"): box 2's live FlowDocument is swapped out for a fresh instance
    /// with identical text between the selection read and the cut, so the cross-page selection's
    /// stored end pointer becomes a genuine cross-TextContainer pointer relative to the box's
    /// current document -- <c>TextRange.Text = string.Empty</c> throws
    /// <see cref="System.ArgumentException"/> ("TextPointer is not in the TextTree associated
    /// with this object") for that box while its content, textually, is still exactly what
    /// <see cref="CrossPageSelection.GetSelectedText"/> already read.
    /// </summary>
    [StaFact]
    public void DragMove_BoxDeleteFailsAfterReadSucceeds_DoesNotDuplicateContent()
    {
        var (panel, _) = BuildThreePagePanel();
        if (panel.PageBoxes.Count < 3)
            return; // pagination didn't produce 3 boxes on this layout -- nothing to exercise

        var sel   = panel.CrossPageSelection;
        var boxes = panel.PageBoxes;

        SelectAllAcrossBoxes(sel, boxes, 0, 2);

        var selectedText = sel.GetSelectedText(boxes);
        selectedText.Should().Contain("Page 1 content");
        selectedText.Should().Contain("Page 2 middle");
        selectedText.Should().Contain("Page 3 end");

        // Race: box 2's FlowDocument instance is replaced (same text, different TextContainer)
        // AFTER the read above but BEFORE the cut below.
        boxes[2].Body.Document = new FlowDocument(
            new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Page 3 end")));

        // The exact sequence OnBodyMouseUp performs for a move (isCopy == false): call
        // CutSelection, and only insert the pre-captured text at the drop point when the cut
        // fully succeeded.
        var cutMethod = typeof(PaginatedEditorPanel).GetMethod(
            "CutSelection", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!;
        var rawResult = cutMethod.Invoke(panel, null);
        bool cutOk = rawResult is bool b ? b : true; // void (pre-fix) => always proceed, like the old caller did

        if (cutOk)
        {
            var dropPtr = boxes[0].Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)
                ?? boxes[0].Body.Document.ContentEnd;
            try { dropPtr.InsertTextInRun(selectedText); }
            catch
            {
                var r = new TextRange(dropPtr, dropPtr);
                r.Text = selectedText;
            }
        }

        // Box 2's delete must have failed (its FlowDocument was swapped from under the stored
        // pointer), so its original content is still there.
        var box2TextAfter = new TextRange(
            boxes[2].Body.Document.ContentStart, boxes[2].Body.Document.ContentEnd).Text;
        box2TextAfter.Should().Contain("Page 3 end",
            "box 2's delete threw, so its content was never actually removed");

        // The fix's contract: a failed per-box delete must suppress the re-insertion of the
        // pre-captured selection text, or "Page 3 end" would appear both still in box 2 AND
        // freshly inserted into box 0 -- a silent duplication.
        var box0TextAfter = new TextRange(
            boxes[0].Body.Document.ContentStart, boxes[0].Body.Document.ContentEnd).Text;
        box0TextAfter.Should().NotContain("Page 3 end",
            "the drag-move must not re-insert the captured selection text when one of its " +
            "spanned boxes failed to delete -- doing so duplicates that box's surviving content");
    }

    /// <summary>
    /// Sibling/neighbour-behaviour guard: when every spanned box's delete succeeds normally (no
    /// injected failure), the drag-move must still work exactly as before -- content removed from
    /// every source box and present, once, at the drop target. Proves the atomicity fix does not
    /// regress the ordinary success path.
    /// </summary>
    [StaFact]
    public void DragMove_AllBoxesDeleteSucceed_ContentMovedOnceNoDuplication()
    {
        var (panel, _) = BuildThreePagePanel();
        if (panel.PageBoxes.Count < 3)
            return;

        var sel   = panel.CrossPageSelection;
        var boxes = panel.PageBoxes;

        SelectAllAcrossBoxes(sel, boxes, 0, 2);

        var selectedText = sel.GetSelectedText(boxes);
        selectedText.Should().Contain("Page 3 end");

        var cutMethod = typeof(PaginatedEditorPanel).GetMethod(
            "CutSelection", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!;
        var rawResult = cutMethod.Invoke(panel, null);
        bool cutOk = rawResult is bool b ? b : true;

        cutOk.Should().BeTrue("no failure was injected -- every spanned box's delete must succeed");

        if (cutOk)
        {
            var dropPtr = boxes[0].Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)
                ?? boxes[0].Body.Document.ContentEnd;
            try { dropPtr.InsertTextInRun(selectedText); }
            catch
            {
                var r = new TextRange(dropPtr, dropPtr);
                r.Text = selectedText;
            }
        }

        // All three source boxes were fully spanned and deleted -- box 1 and box 2 must now be
        // empty of their original text (moved away), and box 0 (the drop target) must contain
        // the moved text exactly once.
        var box1TextAfter = new TextRange(
            boxes[1].Body.Document.ContentStart, boxes[1].Body.Document.ContentEnd).Text;
        box1TextAfter.Should().NotContain("Page 2 middle",
            "box 1's content was successfully deleted as part of the move");

        var box2TextAfter = new TextRange(
            boxes[2].Body.Document.ContentStart, boxes[2].Body.Document.ContentEnd).Text;
        box2TextAfter.Should().NotContain("Page 3 end",
            "box 2's content was successfully deleted as part of the move");

        var box0TextAfter = new TextRange(
            boxes[0].Body.Document.ContentStart, boxes[0].Body.Document.ContentEnd).Text;
        box0TextAfter.Should().Contain("Page 3 end",
            "the moved text must land at the drop target");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static (PaginatedEditorPanel panel, DocumentView editor) BuildThreePagePanel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Page 1 content"));
        doc.Blocks.Add(new Paragraph("Page 2 middle")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });
        doc.Blocks.Add(new Paragraph("Page 3 end")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var panel = PaginatedEditorPanel.Build(editor);
        return (panel, editor);
    }

    private static void SelectAllAcrossBoxes(
        CrossPageSelection sel,
        IReadOnlyList<PageBox> boxes,
        int startBoxIdx,
        int endBoxIdx)
    {
        var anchorPtr = boxes[startBoxIdx].Body.Document.ContentStart
            .GetInsertionPosition(LogicalDirection.Forward);
        var activePtr = boxes[endBoxIdx].Body.Document.ContentEnd
            .GetInsertionPosition(LogicalDirection.Backward);

        if (anchorPtr is null || activePtr is null)
            return;

        sel.BeginSelection(boxes, boxes[startBoxIdx], anchorPtr);
        sel.ExtendSelection(boxes, boxes[endBoxIdx], activePtr);
    }
}
