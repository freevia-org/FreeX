using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 153 fix, shared-drag-drop F4: <c>DocumentView.OnPointerPressed</c>'s body-text branch used to
/// unconditionally collapse the current selection to a fresh anchor at the click point -- even when the
/// click landed INSIDE the existing selection, which should instead behave like a drag-to-move/copy
/// gesture (the WPF host has exactly this via <c>PaginatedEditorPanel.OnBodyMouseDown</c> /
/// <c>IsPointInsideCrossPageSelection</c>). A press-and-drag inside a selection therefore destroyed the
/// selection the user was trying to reposition, instead of moving or copying it.
///
/// Fixed by <c>TryArmBodyTextDrag</c> (called from <c>OnPointerPressed</c>), <c>UpdateBodyTextDrag</c>
/// (from <c>OnPointerMoved</c>) and <c>CommitBodyTextDrag</c> (from <c>OnPointerReleased</c>): a press
/// inside the selection now arms a pending drag instead of collapsing it. A genuine drag past the
/// platform threshold moves (or, with Ctrl held, copies) the text to the drop point for a same-
/// paragraph, untracked, unlocked selection; a release that never exceeded the threshold still collapses
/// to the press point exactly like an ordinary click always has.
/// </summary>
public sealed class DocumentViewBodyTextDragMoveTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task OnUiThread(Action action) => Session.Dispatch(action, CancellationToken.None);

    private const string Text = "AAAA BBBB CCCC";

    private static DocumentView BuildView(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));

        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static string SoleParagraphText(DocumentView view) =>
        view.Document.Blocks.Cast<Paragraph>().Single().PlainText;

    /// <summary>
    /// A point guaranteed to hit-test back to <paramref name="offset"/>: a quarter of the way into that
    /// offset's own glyph, safely away from either neighbouring boundary. Moves (and so collapses) the
    /// caret/selection as a side effect -- call this before arranging the selection under test, not
    /// after.
    /// </summary>
    private static Point GlyphPoint(DocumentView view, int block, int offset)
    {
        view.MoveCaretToBlockForTest(block, offset);
        var start = view.CaretRectForTest!.Value;
        view.MoveCaretToBlockForTest(block, offset + 1);
        var end = view.CaretRectForTest!.Value;
        return new Point(start.X + (end.X - start.X) * 0.25, start.Y + start.Height / 2);
    }

    /// <summary>A point at the caret position right after the paragraph's last character.</summary>
    private static Point EndOfParagraphPoint(DocumentView view, int block, int length)
    {
        view.MoveCaretToBlockForTest(block, length);
        var rect = view.CaretRectForTest!.Value;
        return new Point(rect.X, rect.Y + rect.Height / 2);
    }

    // ==== Core fix: a press inside the selection arms a drag instead of collapsing it ==============

    [Fact]
    public async Task PressInsideExistingSelection_ArmsDragInsteadOfCollapsingIt()
    {
        bool armed = false;
        string? selectedAfterPress = null;

        await OnUiThread(() =>
        {
            var view = BuildView(Text);
            var start = Text.IndexOf("BBBB", StringComparison.Ordinal);
            var end = start + "BBBB".Length;
            var pressPoint = GlyphPoint(view, 0, start + 1); // inside "BBBB"

            view.SetBodySelectionForTest(0, start, 0, end);

            armed = view.TryArmBodyTextDragForTest(pressPoint);
            selectedAfterPress = view.SelectedText;
        });

        armed.Should().BeTrue("a press landing inside the current selection must arm a pending drag, not collapse it");
        selectedAfterPress.Should().Be("BBBB", "the selection must still be intact immediately after the press -- arming a drag must not touch it");
    }

    [Fact]
    public async Task DragPastThresholdAndDropOutsideSelection_MovesTheSelectedText()
    {
        string? result = null;
        bool becameActive = false;

        await OnUiThread(() =>
        {
            var view = BuildView(Text);
            var start = Text.IndexOf("BBBB", StringComparison.Ordinal);
            var end = start + "BBBB".Length;
            var pressPoint = GlyphPoint(view, 0, start + 1); // inside "BBBB"
            var dropPoint = EndOfParagraphPoint(view, 0, Text.Length); // well past "CCCC"

            view.SetBodySelectionForTest(0, start, 0, end);

            view.TryArmBodyTextDragForTest(pressPoint).Should().BeTrue();
            view.UpdateBodyTextDragForTest(new Point(dropPoint.X, pressPoint.Y));
            becameActive = view.BodyTextDragActiveForTest;

            view.CommitBodyTextDragForTest(dropPoint);
            result = SoleParagraphText(view);
        });

        becameActive.Should().BeTrue("moving well past the platform drag threshold must activate the drag");
        result.Should().Be("AAAA  CCCCBBBB", "BBBB must be removed from its old spot and reinserted at the drop point");
    }

    [Fact]
    public async Task CtrlHeldOnDrop_CopiesInsteadOfMoving()
    {
        string? result = null;

        await OnUiThread(() =>
        {
            var view = BuildView(Text);
            var start = Text.IndexOf("BBBB", StringComparison.Ordinal);
            var end = start + "BBBB".Length;
            var pressPoint = GlyphPoint(view, 0, start + 1);
            var dropPoint = EndOfParagraphPoint(view, 0, Text.Length);

            view.SetBodySelectionForTest(0, start, 0, end);

            view.TryArmBodyTextDragForTest(pressPoint).Should().BeTrue();
            view.UpdateBodyTextDragForTest(new Point(dropPoint.X, pressPoint.Y), ctrlHeld: true);

            view.CommitBodyTextDragForTest(dropPoint, ctrlHeld: true);
            result = SoleParagraphText(view);
        });

        result.Should().Be("AAAA BBBB CCCCBBBB", "Ctrl held at drop must copy the text, leaving the original selection's text in place");
    }

    [Fact]
    public async Task DropInsideSourceSelection_IsANoOpThatRestoresTheSelection()
    {
        string? result = null;
        string? selectedAfterRelease = null;

        await OnUiThread(() =>
        {
            var view = BuildView(Text);
            var start = Text.IndexOf("BBBB", StringComparison.Ordinal);
            var end = start + "BBBB".Length;
            var pressPoint = GlyphPoint(view, 0, start + 1); // offset start+1, inside "BBBB"
            var dropInsidePoint = GlyphPoint(view, 0, start + 2); // still inside "BBBB"

            view.SetBodySelectionForTest(0, start, 0, end);

            view.TryArmBodyTextDragForTest(pressPoint).Should().BeTrue();
            view.UpdateBodyTextDragForTest(new Point(dropInsidePoint.X, pressPoint.Y + 20));
            view.CommitBodyTextDragForTest(new Point(dropInsidePoint.X, pressPoint.Y));

            result = SoleParagraphText(view);
            selectedAfterRelease = view.SelectedText;
        });

        result.Should().Be(Text, "dropping back inside the source selection must not change the document");
        selectedAfterRelease.Should().Be("BBBB", "a no-op drop must leave the original selection exactly as it was");
    }

    // ==== Sibling/adjacent cases: unaffected by the fix =============================================

    [Fact]
    public async Task PressOutsideExistingSelection_StillCollapsesImmediately()
    {
        bool armed = true;
        string? selectedAfterPress = null;
        (int Block, int Offset)? caretAfterPress = null;

        await OnUiThread(() =>
        {
            var view = BuildView(Text);
            var start = Text.IndexOf("BBBB", StringComparison.Ordinal);
            var end = start + "BBBB".Length;
            var outsidePoint = GlyphPoint(view, 0, 1); // inside "AAAA", well outside the selection

            view.SetBodySelectionForTest(0, start, 0, end);

            armed = view.TryArmBodyTextDragForTest(outsidePoint);
            selectedAfterPress = view.SelectedText;
            caretAfterPress = view.CaretPositionForTest;
        });

        armed.Should().BeFalse("a press outside the selection must behave like an ordinary click, not arm a drag");
        selectedAfterPress.Should().BeEmpty("an ordinary click outside the selection still collapses it immediately, exactly as before this fix");
        caretAfterPress!.Value.Offset.Should().Be(1);
    }

    [Fact]
    public async Task DragBelowThreshold_StillCollapsesSelectionToThePressPointOnRelease()
    {
        string? selectedAfterRelease = null;
        (int Block, int Offset)? caretAfterRelease = null;
        bool becameActive = true;
        var pressOffset = Text.IndexOf("BBBB", StringComparison.Ordinal) + 1;

        await OnUiThread(() =>
        {
            var view = BuildView(Text);
            var start = Text.IndexOf("BBBB", StringComparison.Ordinal);
            var end = start + "BBBB".Length;
            var pressPoint = GlyphPoint(view, 0, pressOffset); // inside "BBBB"

            view.SetBodySelectionForTest(0, start, 0, end);

            view.TryArmBodyTextDragForTest(pressPoint).Should().BeTrue();
            // Barely move -- stays under the drag threshold, so this must still resolve as a click.
            view.UpdateBodyTextDragForTest(new Point(pressPoint.X + 1, pressPoint.Y));
            becameActive = view.BodyTextDragActiveForTest;

            view.CommitBodyTextDragForTest(pressPoint);
            selectedAfterRelease = view.SelectedText;
            caretAfterRelease = view.CaretPositionForTest;
        });

        becameActive.Should().BeFalse("a sub-threshold move must not activate the drag");
        selectedAfterRelease.Should().BeEmpty("a click-in-place (no real drag) must still collapse the selection, matching how a plain click has always behaved");
        caretAfterRelease!.Value.Offset.Should().Be(pressOffset);
    }
}
