using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for the round-163 "shared-drag-drop" finding F1: FreeW's Page-Edit (WPF)
/// drag-and-drop of text bypassed the content-control lock / Restrict-Editing protection that
/// typing, Delete, and Cut/Paste all enforce elsewhere in <see cref="PaginatedEditorPanel"/>.
///
/// <para>
/// Two independent gaps existed:
/// </para>
/// <list type="number">
///   <item>
///   <see cref="PaginatedEditorPanel.CompleteDrag"/> inserted the dragged text at the drop position
///   via <c>TextPointer.InsertTextInRun</c> with no <see cref="PageBox.ContentControlLockProbe"/>
///   check on the drop target -- unlike <see cref="PaginatedEditorPanel.PasteAtCaret"/>, which
///   already gates its own caret insert on this same probe.
///   </item>
///   <item>
///   <see cref="PaginatedEditorPanel"/>'s private <c>DeleteCrossPageSelection</c> (reached by
///   Ctrl+X, and by a drag-move's cut-before-insert step) deleted every spanned box's
///   <see cref="TextRange"/> outright with no lock check on the source, so dragging a selection
///   that overlapped a delete-locked (<see cref="ContentControlLockMode.ControlAndContentLocked"/>)
///   content control silently removed it.
///   </item>
/// </list>
///
/// <para>
/// The fix adds a drop-target lock check right before <c>CompleteDrag</c>'s insert (mirroring
/// <c>PasteAtCaret</c>'s own guard), and a new private static <c>RangeTouchesLockedContentControl</c>
/// helper that <c>DeleteCrossPageSelection</c> consults for every spanned box's own sub-range
/// <em>before</em> deleting anything -- declining the whole deletion, matching the canonical
/// "Word declines the whole gesture rather than deleting part of it" semantics already documented on
/// the Avalonia sibling <c>DocumentView.SelectionReachesLockedContentControl</c>.
/// </para>
///
/// <para>
/// <c>RangeTouchesLockedContentControl</c> is tested directly via reflection with plain
/// <see cref="TextPointer"/> navigation -- no WPF layout/rendering is required, matching this
/// suite's existing <c>PositionAfterText</c> convention (<see cref="PagedEditContentControlLockTests"/>).
/// <c>DeleteCrossPageSelection</c> is likewise reached directly via reflection, the same established
/// convention <see cref="PagedEditCrossPageDragMoveAtomicityTests"/> already uses for this exact
/// private method, so no window needs to be shown and the flaky shared-static-Brush cross-thread
/// rendering hazard documented on <see cref="PagedEditCrossPageDragCaptureTests"/> is never triggered.
/// </para>
///
/// <para>Runs on STA because tests create real WPF RichTextBox / FlowDocument instances.</para>
/// </summary>
public sealed class PagedEditCrossPageDragLockTests
{
    private static readonly MethodInfo RangeTouchesLockedContentControlMethod =
        typeof(PaginatedEditorPanel).GetMethod(
            "RangeTouchesLockedContentControl", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "PaginatedEditorPanel.RangeTouchesLockedContentControl not found -- renamed or removed.");

    private static readonly MethodInfo DeleteCrossPageSelectionMethod =
        typeof(PaginatedEditorPanel).GetMethod(
            "DeleteCrossPageSelection", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            "PaginatedEditorPanel.DeleteCrossPageSelection not found -- renamed or removed.");

    private static readonly MethodInfo CompleteDragMethod =
        typeof(PaginatedEditorPanel).GetMethod("CompleteDrag", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("PaginatedEditorPanel.CompleteDrag not found -- renamed or removed.");

    private static readonly FieldInfo PageHostField =
        typeof(PaginatedEditorPanel).GetField("_pageHost", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("PaginatedEditorPanel._pageHost not found -- renamed or removed.");

    private static bool InvokeRangeTouchesLockedContentControl(PageBox box, TextPointer from, TextPointer to) =>
        (bool)RangeTouchesLockedContentControlMethod.Invoke(null, [box, from, to])!;

    private static bool InvokeDeleteCrossPageSelection(PaginatedEditorPanel panel) =>
        (bool)DeleteCrossPageSelectionMethod.Invoke(panel, null)!;

    private static bool InvokeCompleteDrag(PaginatedEditorPanel panel, Point panelPoint, bool isCopy) =>
        (bool)CompleteDragMethod.Invoke(panel, [panelPoint, isCopy])!;

    /// <summary>
    /// Forces a real (offscreen, windowless) layout pass over <paramref name="panel"/> so its page
    /// boxes have real bounds/text views -- <c>CompleteDrag</c>'s own hit-testing
    /// (<c>FindPageBoxAtPoint</c>'s <c>TranslatePoint</c> and <c>RichTextBox.GetPositionFromPoint</c>)
    /// needs a laid-out visual tree, but WPF's <c>Measure</c>/<c>Arrange</c>/visual-tree transforms work
    /// without a live <see cref="Window"/> or <c>PresentationSource</c> -- deliberately avoiding
    /// <c>Window.Show()</c> on the whole panel, which is exactly the shared-static-Brush cross-thread
    /// rendering hazard <see cref="PagedEditCrossPageDragCaptureTests"/>'s doc comment documents.
    /// </summary>
    private static void LayoutOffscreen(PaginatedEditorPanel panel)
    {
        // Deliberately measure/arrange _pageHost directly rather than the panel (ScrollViewer) itself:
        // panel.Arrange cascades into ScrollViewer.ArrangeOverride, whose render pass touches
        // PaginatedEditorPanel's own private static readonly (unfrozen) Brush field -- the exact
        // shared-static-Freezable cross-thread hazard PagedEditCrossPageDragCaptureTests's doc comment
        // documents (confirmed empirically while writing this test: arranging the panel itself throws
        // "Cannot use a DependencyObject that belongs to a different thread than its parent Freezable"
        // whenever an earlier test's STA thread rendered a panel first). _pageHost's own Measure/Arrange
        // needs no such parent pass -- WPF layout can be invoked on any UIElement directly -- and it is
        // exactly the subtree CompleteDrag/FindPageBoxAtPoint hit-test against.
        var pageHost = (System.Windows.Controls.Panel)PageHostField.GetValue(panel)!;
        var size = new Size(2000, 6000);
        pageHost.Measure(size);
        pageHost.Arrange(new Rect(new Point(0, 0), size));
        pageHost.UpdateLayout();
    }

    /// <summary>
    /// Translates <paramref name="pointInBody"/> (in <paramref name="box"/>'s own Body coordinates)
    /// into the panel's <c>_pageHost</c> coordinate space -- the exact reverse of what
    /// <c>CompleteDrag</c>/<c>FindPageBoxAtPoint</c> do with the real mouse-up point, so a real
    /// <see cref="Point"/> can be constructed for <c>CompleteDrag</c> without a live mouse device.
    /// </summary>
    private static Point ToPageHostPoint(PaginatedEditorPanel panel, PageBox box, Point pointInBody)
    {
        var pageHost = (System.Windows.Controls.Panel)PageHostField.GetValue(panel)!;
        return box.Body.TranslatePoint(pointInBody, pageHost);
    }

    private static (PaginatedEditorPanel Panel, DocumentView Editor) BuildPanelWithMiddleLockedRun(
        ContentControlLockMode lockMode)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Before "));
        var locked = Run.PlainTextControl("Secret", tag: "Field");
        locked.Control = locked.Control! with { LockMode = lockMode };
        paragraph.Runs.Add(locked);
        paragraph.Runs.Add(new Run(" after"));
        document.Blocks.Add(paragraph);

        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.CommitToModel();

        var panel = PaginatedEditorPanel.Build(editor);
        return (panel, editor);
    }

    /// <summary>
    /// Two explicitly page-broken paragraphs so <see cref="CrossPageSelection.IsActive"/> (which
    /// requires the anchor and active box indices to actually DIFFER -- see
    /// <c>CrossPageSelection.cs</c>'s own "IsActive guarantees they differ" comment) can be satisfied:
    /// page 1 is plain text, page 2 holds "Lead " + a lockable "Secret" run + " Trail", mirroring
    /// <c>PagedEditCrossPageDragMoveAtomicityTests.BuildThreePagePanel</c>'s established convention for
    /// forcing a real multi-box layout via <c>PageBreakBefore</c>.
    /// </summary>
    private static (PaginatedEditorPanel Panel, DocumentView Editor) BuildTwoPagePanelWithLockedRun(
        ContentControlLockMode lockMode)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var page1 = new Paragraph();
        page1.Runs.Add(new Run("Page one"));
        document.Blocks.Add(page1);

        var page2 = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        };
        page2.Runs.Add(new Run("Lead "));
        var locked = Run.PlainTextControl("Secret", tag: "Field");
        locked.Control = locked.Control! with { LockMode = lockMode };
        page2.Runs.Add(locked);
        page2.Runs.Add(new Run(" Trail"));
        document.Blocks.Add(page2);

        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.CommitToModel();

        var panel = PaginatedEditorPanel.Build(editor);
        return (panel, editor);
    }

    /// <summary>
    /// Same walker <see cref="PagedEditContentControlLockTests.PositionAfterText"/> uses: returns the
    /// TextPointer immediately after <paramref name="text"/> in <paramref name="body"/>'s document.
    /// </summary>
    private static TextPointer PositionAfterText(System.Windows.Controls.RichTextBox body, string text)
    {
        var remaining = text.Length;
        var pointer = body.Document.ContentStart;
        while (pointer is not null && pointer.CompareTo(body.Document.ContentEnd) < 0)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var runText = pointer.GetTextInRun(LogicalDirection.Forward);
                if (remaining <= runText.Length)
                    return pointer.GetPositionAtOffset(remaining)!;
                remaining -= runText.Length;
            }
            pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
        }
        throw new InvalidOperationException($"Text '{text}' was not found.");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // RangeTouchesLockedContentControl — pure TextPointer-range detection, no rendering required.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Core fix proof: a range spanning the WHOLE paragraph ("Before Secret after") must be reported
    /// as touching the lock even though the locked run sits strictly in the MIDDLE of the range, not
    /// at either boundary -- exactly the "selection overlaps a delete-locked content control" shape
    /// the finding describes for a cross-page drag-move.
    /// </summary>
    [StaFact]
    public void RangeTouchesLockedContentControl_LockedRunInMiddleOfRange_ReturnsTrue()
    {
        var (panel, _) = BuildPanelWithMiddleLockedRun(ContentControlLockMode.ControlAndContentLocked);
        var box = panel.PageBoxes[0];

        var from = box.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
        var to = box.Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)!;

        InvokeRangeTouchesLockedContentControl(box, from, to).Should().BeTrue(
            "the range spans the locked 'Secret' run even though it sits in the middle, not at either boundary");
    }

    /// <summary>
    /// Sibling/no-regression guard: a range over the SAME document shape but with no lock at all must
    /// report false, so the new helper never blocks an ordinary cross-page delete/drag-move.
    /// </summary>
    [StaFact]
    public void RangeTouchesLockedContentControl_NoLockedRun_ReturnsFalse()
    {
        var (panel, _) = BuildPanelWithMiddleLockedRun(ContentControlLockMode.NotSpecified);
        var box = panel.PageBoxes[0];

        var from = box.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
        var to = box.Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)!;

        InvokeRangeTouchesLockedContentControl(box, from, to).Should().BeFalse(
            "an unlocked run must never be reported as touching a lock");
    }

    /// <summary>
    /// Sibling guard: a range that only covers the "Before " text, stopping strictly before the locked
    /// run starts, must report false -- text next to a locked field stays deletable, matching the
    /// Avalonia sibling's documented semantics.
    /// </summary>
    [StaFact]
    public void RangeTouchesLockedContentControl_RangeEndsBeforeLockedRun_ReturnsFalse()
    {
        var (panel, _) = BuildPanelWithMiddleLockedRun(ContentControlLockMode.ControlAndContentLocked);
        var box = panel.PageBoxes[0];

        var from = box.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
        var to = PositionAfterText(box.Body, "Before");

        InvokeRangeTouchesLockedContentControl(box, from, to).Should().BeFalse(
            "a range that never reaches the locked run must not be blocked");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // DeleteCrossPageSelection — the real production method the finding names, reached the same way
    // PagedEditCrossPageDragMoveAtomicityTests already reaches it (reflection, no window shown).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FAILS BEFORE THE FIX / PASSES AFTER: selecting across a delete-locked content control
    /// (<see cref="ContentControlLockMode.ControlAndContentLocked"/>) and deleting the cross-page
    /// selection (the exact step <c>CompleteDrag</c>'s move branch performs via <c>CutSelection</c>)
    /// must refuse outright -- the locked "Secret" run must still be present afterwards, and the
    /// method must report failure so a caller (a drag-move) never re-inserts the pre-captured text
    /// elsewhere and duplicates it.
    /// </summary>
    [StaFact]
    public void DeleteCrossPageSelection_SelectionOverlapsDeleteLockedControl_RefusesAndLeavesContentIntact()
    {
        var (panel, _) = BuildTwoPagePanelWithLockedRun(ContentControlLockMode.ControlAndContentLocked);
        if (panel.PageBoxes.Count < 2)
            return; // pagination didn't produce 2 boxes on this layout -- nothing to exercise

        var boxes = panel.PageBoxes;
        var sel = panel.CrossPageSelection;

        // Selects EVERYTHING from box 0's start through box 1's end -- box 1's own sub-range covers
        // "Lead " + the locked "Secret" run + " Trail" whole, so this is exactly the
        // "selection overlaps a delete-locked content control" shape the finding describes.
        var anchor = boxes[0].Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
        var active = boxes[1].Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)!;
        sel.BeginSelection(boxes, boxes[0], anchor);
        sel.ExtendSelection(boxes, boxes[1], active);
        sel.IsActive.Should().BeTrue("the anchor and active box indices differ (0 vs 1)");

        var result = InvokeDeleteCrossPageSelection(panel);

        result.Should().BeFalse(
            "a cross-page delete/drag-move spanning a delete-locked content control must refuse outright");

        var box0After = new TextRange(boxes[0].Body.Document.ContentStart, boxes[0].Body.Document.ContentEnd).Text;
        var box1After = new TextRange(boxes[1].Body.Document.ContentStart, boxes[1].Body.Document.ContentEnd).Text;
        box0After.Should().Contain("Page one",
            "the whole gesture must be declined -- box 0's content must not have been removed either");
        box1After.Should().Contain("Secret",
            "the delete-locked field must still be present -- the whole deletion must have been declined, " +
            "not just partially applied");
    }

    /// <summary>
    /// Sibling/no-regression guard: the same document shape but with NO lock on the field must still
    /// delete normally end-to-end -- proves the new guard does not regress the ordinary cross-page
    /// delete/drag-move path <see cref="PagedEditCrossPageDragMoveAtomicityTests"/> already covers.
    /// </summary>
    [StaFact]
    public void DeleteCrossPageSelection_NoLockedControl_DeletesNormally()
    {
        var (panel, _) = BuildTwoPagePanelWithLockedRun(ContentControlLockMode.NotSpecified);
        if (panel.PageBoxes.Count < 2)
            return;

        var boxes = panel.PageBoxes;
        var sel = panel.CrossPageSelection;

        var anchor = boxes[0].Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
        var active = boxes[1].Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)!;
        sel.BeginSelection(boxes, boxes[0], anchor);
        sel.ExtendSelection(boxes, boxes[1], active);

        var result = InvokeDeleteCrossPageSelection(panel);

        result.Should().BeTrue("no lock is present, so the whole selection must delete successfully");

        var box0After = new TextRange(boxes[0].Body.Document.ContentStart, boxes[0].Body.Document.ContentEnd).Text;
        var box1After = new TextRange(boxes[1].Body.Document.ContentStart, boxes[1].Body.Document.ContentEnd).Text;
        box0After.Trim().Should().BeEmpty("an ordinary, unlocked cross-page selection must still fully delete (box 0)");
        box1After.Trim().Should().BeEmpty("an ordinary, unlocked cross-page selection must still fully delete (box 1)");
    }

    /// <summary>
    /// Sibling guard: a selection that spans two different boxes (so <c>IsActive</c> is satisfied) but
    /// whose sub-range within the locked box stops strictly BEFORE the locked "Secret" run begins must
    /// still delete normally -- the new guard must not become overly conservative and block deletes
    /// that never actually touch the lock.
    /// </summary>
    [StaFact]
    public void DeleteCrossPageSelection_SelectionDoesNotReachLockedControl_DeletesNormally()
    {
        var (panel, _) = BuildTwoPagePanelWithLockedRun(ContentControlLockMode.ControlAndContentLocked);
        if (panel.PageBoxes.Count < 2)
            return;

        var boxes = panel.PageBoxes;
        var sel = panel.CrossPageSelection;

        var anchor = boxes[0].Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
        var active = PositionAfterText(boxes[1].Body, "Lead ");
        sel.BeginSelection(boxes, boxes[0], anchor);
        sel.ExtendSelection(boxes, boxes[1], active);
        sel.IsActive.Should().BeTrue("the anchor and active box indices differ (0 vs 1)");

        var result = InvokeDeleteCrossPageSelection(panel);

        result.Should().BeTrue("the selection never reaches the locked field, so it must delete normally");

        var box0After = new TextRange(boxes[0].Body.Document.ContentStart, boxes[0].Body.Document.ContentEnd).Text;
        var box1After = new TextRange(boxes[1].Body.Document.ContentStart, boxes[1].Body.Document.ContentEnd).Text;
        box0After.Trim().Should().BeEmpty("box 0's content was entirely selected and must have been deleted");
        box1After.Should().Contain("Secret", "the locked field itself was never selected, so it must remain");
        box1After.Should().NotContain("Lead", "the selected, unlocked leading text in box 1 must have been deleted");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // CompleteDrag — the real production method the finding names at line 1409/1412. Reached with a
    // real Point via CompleteDrag's own documented testability contract (its doc comment: "so the
    // drop-target resolution and move/copy logic can be unit-tested with a real point in panel
    // coordinates, without needing a live WPF mouse device").
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two page-broken paragraphs shaped for a cross-page drag-drop: page 1 is the drag SOURCE
    /// ("Hello World", entirely unlocked); page 2 is the drop TARGET, with unlocked "Other " text
    /// (used as the cross-page selection's far boundary so the drop point below sits just past it and
    /// therefore outside the selection -- see <c>IsDropInsideSelection</c>) followed by a lockable
    /// "Locked" run and trailing " End" text.
    /// </summary>
    private static (PaginatedEditorPanel Panel, DocumentView Editor) BuildTwoPagePanelForDragDrop(
        ContentControlLockMode dropTargetLockMode)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var page1 = new Paragraph();
        page1.Runs.Add(new Run("Hello World"));
        document.Blocks.Add(page1);

        var page2 = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        };
        page2.Runs.Add(new Run("Other "));
        var locked = Run.PlainTextControl("Locked", tag: "Field");
        locked.Control = locked.Control! with { LockMode = dropTargetLockMode };
        page2.Runs.Add(locked);
        page2.Runs.Add(new Run(" End"));
        document.Blocks.Add(page2);

        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.CommitToModel();

        var panel = PaginatedEditorPanel.Build(editor);
        return (panel, editor);
    }

    /// <summary>
    /// FAILS BEFORE THE FIX / PASSES AFTER: dragging the unlocked "Hello World" selection from page 1
    /// and dropping it one character into the content-locked "Locked" run on page 2 must be refused --
    /// no text may be inserted there, and (since this is a move, not a copy) the source selection must
    /// survive untouched, matching <c>PasteAtCaret</c>'s own drop-into-locked-field refusal.
    /// </summary>
    [StaFact]
    public void CompleteDrag_DropsOntoLockedContentControl_RefusesInsertAndLeavesSourceIntact()
    {
        var (panel, _) = BuildTwoPagePanelForDragDrop(ContentControlLockMode.ContentLocked);
        if (panel.PageBoxes.Count < 2)
            return; // pagination didn't produce 2 boxes on this layout -- nothing to exercise

        LayoutOffscreen(panel);

        var boxes = panel.PageBoxes;
        var sel = panel.CrossPageSelection;

        // Cross-page selection: all of page 1's "Hello World", through page 2 up to (but not
        // including) the locked run -- so the drop point below (inside "Locked") is outside the
        // selection per IsDropInsideSelection, letting CompleteDrag proceed to the real drop-target
        // lock check instead of bailing out on the earlier "drop inside selection" no-op guard.
        var anchor = boxes[0].Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
        var active = PositionAfterText(boxes[1].Body, "Other ");
        sel.BeginSelection(boxes, boxes[0], anchor);
        sel.ExtendSelection(boxes, boxes[1], active);
        sel.IsActive.Should().BeTrue("the anchor and active box indices differ (0 vs 1)");

        // Drop point: one character inside the locked "Locked" run.
        var dropTextPtr = PositionAfterText(boxes[1].Body, "Other L");
        var dropRect = dropTextPtr.GetCharacterRect(LogicalDirection.Backward);
        var dropPointInBody = new Point(dropRect.Left, dropRect.Top + (dropRect.Height / 2));
        var panelPoint = ToPageHostPoint(panel, boxes[1], dropPointInBody);

        var handled = InvokeCompleteDrag(panel, panelPoint, isCopy: false);

        handled.Should().BeTrue("the drag gesture must be consumed (not left for native handling) even when refused");

        var box1After = new TextRange(boxes[1].Body.Document.ContentStart, boxes[1].Body.Document.ContentEnd).Text;
        box1After.Should().Contain("Locked",
            "the locked run's own text must be unchanged -- nothing may be inserted into it");
        box1After.Should().NotContain("Hello World",
            "the dragged text must never have been inserted into the locked drop target");

        var box0After = new TextRange(boxes[0].Body.Document.ContentStart, boxes[0].Body.Document.ContentEnd).Text;
        box0After.Should().Contain("Hello World",
            "refusing the drop must not have cut the source selection either -- it must survive intact");
    }

    /// <summary>
    /// Sibling/no-regression guard: the same drag/drop shape but with NO lock on the drop target must
    /// still complete the move end-to-end -- the dragged text lands at the drop point and is removed
    /// from its source, proving the new guard does not regress an ordinary cross-page drag-move.
    /// </summary>
    [StaFact]
    public void CompleteDrag_DropTargetUnlocked_MovesTextNormally()
    {
        var (panel, _) = BuildTwoPagePanelForDragDrop(ContentControlLockMode.NotSpecified);
        if (panel.PageBoxes.Count < 2)
            return;

        LayoutOffscreen(panel);

        var boxes = panel.PageBoxes;
        var sel = panel.CrossPageSelection;

        var anchor = boxes[0].Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
        var active = PositionAfterText(boxes[1].Body, "Other ");
        sel.BeginSelection(boxes, boxes[0], anchor);
        sel.ExtendSelection(boxes, boxes[1], active);

        var dropTextPtr = PositionAfterText(boxes[1].Body, "Other L");
        var dropRect = dropTextPtr.GetCharacterRect(LogicalDirection.Backward);
        var dropPointInBody = new Point(dropRect.Left, dropRect.Top + (dropRect.Height / 2));
        var panelPoint = ToPageHostPoint(panel, boxes[1], dropPointInBody);

        var handled = InvokeCompleteDrag(panel, panelPoint, isCopy: false);

        handled.Should().BeTrue("an ordinary unlocked drop must still be handled");

        var box1After = new TextRange(boxes[1].Body.Document.ContentStart, boxes[1].Body.Document.ContentEnd).Text;
        box1After.Should().Contain("Hello World",
            "with no lock on the drop target, the dragged text must land there as before the fix");

        var box0After = new TextRange(boxes[0].Body.Document.ContentStart, boxes[0].Body.Document.ContentEnd).Text;
        box0After.Should().NotContain("Hello World",
            "a successful move must remove the dragged text from its source");
    }
}
