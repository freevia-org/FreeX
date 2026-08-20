using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-DRAGMOVE pointer-capture regression. The body-text drag-move gesture
/// (<c>TryArmBodyTextDrag</c>/<c>UpdateBodyTextDrag</c>/<c>CommitBodyTextDrag</c>, driven from
/// <c>OnPointerPressed</c>/<c>OnPointerMoved</c>/<c>OnPointerReleased</c>) used to arm itself without
/// taking pointer capture -- unlike every other drag <c>DocumentView</c> implements. Avalonia routes a
/// release to whatever sits under the pointer when nothing is captured, so releasing the button outside
/// the view -- the ordinary way a drag-to-move ends when the user overshoots -- never delivered
/// <c>PointerReleased</c> here: the drag neither committed its move nor restored the selection it
/// started from, and <c>_bodyDragPending</c>/<c>_bodyDragActive</c> stayed set -- with
/// <c>OnPointerMoved</c>'s drag branch claiming every later move -- until some future press happened to
/// clear them. (The WPF host had the identical gap in <c>PaginatedEditorPanel</c>'s cross-page
/// drag -- round 153 finding "shared-drag-drop F3" -- fixed there by capturing the mouse in
/// <c>BeginActiveDrag</c> and releasing it in <c>EndActiveDrag</c>.)
///
/// The fix captures the pointer when the drag is armed and hands it back when the gesture completes, so
/// the release always comes back to this control; <c>OnPointerCaptureLost</c> remains the backstop for a
/// platform/window teardown that revokes capture without a matching release.
///
/// These tests drive the real protected pointer handlers through a shown headless window, so the
/// capture calls under test are the ones the running app makes. Coverage for the ORIGINAL drag-move
/// behaviour (F4: a press inside the selection arms a drag instead of collapsing it) lives in
/// <see cref="DocumentViewBodyTextDragMoveTests"/>.
/// </summary>
public sealed class DocumentViewBodyTextDragMoveCaptureTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private const string Text = "AAAA BBBB CCCC";

    /// <summary>
    /// Runs <paramref name="body"/> on the UI thread against a <see cref="DocumentView"/> hosted in a
    /// shown window. The window matters: pointer event args resolve their position through the visual
    /// root, so an unrooted control would see every press at (0,0).
    /// </summary>
    private static Task OnHostedView(Action<Window, DocumentView> body) =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph(Text));

            var view = new DocumentView();
            view.LoadDocument(document);
            var window = new Window { Width = 900, Height = 700, Content = view };
            window.Show();
            try
            {
                body(window, view);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

    private static string SoleParagraphText(DocumentView view) =>
        view.Document.Blocks.Cast<Paragraph>().Single().PlainText;

    private static int SelectionStart => Text.IndexOf("BBBB", StringComparison.Ordinal);

    /// <summary>Selects "BBBB" and returns a press point a quarter of the way into one of its glyphs.</summary>
    private static Point SelectBbbbAndGetInsidePoint(DocumentView view)
    {
        var point = GlyphPoint(view, 0, SelectionStart + 1);
        view.SetBodySelectionForTest(0, SelectionStart, 0, SelectionStart + "BBBB".Length);
        return point;
    }

    /// <summary>A point a quarter of the way into <paramref name="offset"/>'s own glyph, safely away
    /// from either neighbouring boundary. Moves the caret as a side effect -- call it before arranging
    /// the selection under test, not after.</summary>
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

    // ── Real pointer-event plumbing ─────────────────────────────────────────────────────────────────
    // Events are routed the way Avalonia's pointer device routes them: to the captured element if there
    // is one, otherwise to whatever the point hit-tests to (and nowhere near this view when the point is
    // outside the window). That routing rule IS the bug under test -- without capture, an out-of-bounds
    // release simply never reaches DocumentView.

    private static Pointer NewPointer() => new(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);

    private static Point ToRoot(Window window, DocumentView view, Point point) =>
        view.TranslatePoint(point, window) ?? point;

    /// <summary>
    /// The element this pointer event would be delivered to: the captured element if there is one,
    /// otherwise the view only while the point is still inside its bounds. Null means the platform
    /// delivered the event somewhere else entirely -- the view hears nothing.
    /// </summary>
    private static DocumentView? RouteTarget(DocumentView view, IPointer pointer, Point point) =>
        pointer.Captured as DocumentView ?? (new Rect(view.Bounds.Size).Contains(point) ? view : null);

    private static void Press(Window window, DocumentView view, IPointer pointer, Point point)
    {
        if (RouteTarget(view, pointer, point) is not { } target)
            return;
        target.RaiseEvent(new PointerPressedEventArgs(
            target, pointer, window, ToRoot(window, view, point), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));
    }

    private static void Move(Window window, DocumentView view, IPointer pointer, Point point)
    {
        if (RouteTarget(view, pointer, point) is not { } target)
            return;
        target.RaiseEvent(new PointerEventArgs(
            InputElement.PointerMovedEvent, target, pointer, window, ToRoot(window, view, point), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None));
    }

    private static void Release(Window window, DocumentView view, IPointer pointer, Point point)
    {
        if (RouteTarget(view, pointer, point) is not { } target)
            return;
        target.RaiseEvent(new PointerReleasedEventArgs(
            target, pointer, window, ToRoot(window, view, point), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left));
    }

    /// <summary>Far outside the view's bounds -- where an overshooting drag-to-move typically ends.</summary>
    private static readonly Point OutsidePoint = new(-120, -80);

    // ==== The regression: an out-of-bounds release must not strand the gesture ======================

    [Fact]
    public async Task PressInsideSelection_CapturesThePointerSoAnOutsideReleaseStillReachesTheView()
    {
        object? capturedAfterPress = null;

        await OnHostedView((window, view) =>
        {
            var pressPoint = SelectBbbbAndGetInsidePoint(view);

            var pointer = NewPointer();
            Press(window, view, pointer, pressPoint);

            view.BodyTextDragPendingForTest.Should().BeTrue("the press landed inside the selection, so a drag is armed");
            capturedAfterPress = pointer.Captured;
        });

        capturedAfterPress.Should().BeOfType<DocumentView>(
            "arming the drag must capture the pointer -- uncaptured, a release outside the view is routed elsewhere and the armed state is never cleared");
    }

    [Fact]
    public async Task ReleaseOutsideTheViewBounds_ClearsTheDragStateAndReleasesCapture()
    {
        bool pending = true, active = true;
        object? capturedAfterRelease = new object();
        string? text = null;

        await OnHostedView((window, view) =>
        {
            var pressPoint = SelectBbbbAndGetInsidePoint(view);

            var pointer = NewPointer();
            Press(window, view, pointer, pressPoint);
            Move(window, view, pointer, new Point(pressPoint.X + 60, pressPoint.Y));
            view.BodyTextDragActiveForTest.Should().BeTrue("moving well past the drag threshold activates the drag");

            // Overshoot: the button comes up well outside the control. Capture is what makes this
            // release arrive here at all -- the point is negative in the view's own coordinates.
            Release(window, view, pointer, OutsidePoint);

            pending = view.BodyTextDragPendingForTest;
            active = view.BodyTextDragActiveForTest;
            capturedAfterRelease = pointer.Captured;
            text = SoleParagraphText(view);
        });

        pending.Should().BeFalse("a release outside the view must end the gesture, not leave it armed forever");
        active.Should().BeFalse("a release outside the view must end the gesture, not leave it active forever");
        capturedAfterRelease.Should().BeNull("the completed gesture must hand the pointer back");
        text.Should().Be("BBBBAAAA  CCCC",
            "the out-of-bounds drop resolves to the nearest in-document position (above/left of the text ⇒ offset 0), so the move completes there -- the point of the fix is that the gesture RESOLVES rather than being stranded half-applied");
    }

    [Fact]
    public async Task AfterAnOutsideRelease_AnOrdinaryClickDragSelectionStillWorks()
    {
        bool pendingAfterSecondGesture = true, activeAfterSecondGesture = true;
        string? selectedAfterSecondDrag = null;

        await OnHostedView((window, view) =>
        {
            // GlyphPoint moves (and so collapses) the caret -- take every point before selecting.
            var aStart = GlyphPoint(view, 0, 0); // inside "AAAA", outside the selection under test
            var aEnd = GlyphPoint(view, 0, 3);
            var pressPoint = SelectBbbbAndGetInsidePoint(view);

            var first = NewPointer();
            Press(window, view, first, pressPoint);
            Move(window, view, first, new Point(pressPoint.X + 60, pressPoint.Y));
            Release(window, view, first, OutsidePoint);

            var second = NewPointer();
            Press(window, view, second, aStart);
            Move(window, view, second, aEnd);
            Release(window, view, second, aEnd);

            selectedAfterSecondDrag = view.SelectedText;
            pendingAfterSecondGesture = view.BodyTextDragPendingForTest;
            activeAfterSecondGesture = view.BodyTextDragActiveForTest;
        });

        selectedAfterSecondDrag.Should().NotBeNullOrEmpty(
            "a plain click-drag after the overshot drag must still select text");
        pendingAfterSecondGesture.Should().BeFalse("the second gesture ended in bounds, so nothing stays armed");
        activeAfterSecondGesture.Should().BeFalse("the second gesture ended in bounds, so nothing stays active");
    }

    [Fact]
    public async Task PointerCaptureLost_AbandonsTheDragAndRestoresTheOriginalSelection()
    {
        bool pending = true, active = true;
        string? selectedAfterLoss = null;
        string? text = null;

        await OnHostedView((window, view) =>
        {
            var pressPoint = SelectBbbbAndGetInsidePoint(view);

            var pointer = NewPointer();
            Press(window, view, pointer, pressPoint);
            Move(window, view, pointer, new Point(pressPoint.X + 60, pressPoint.Y));

            // A window/platform teardown revokes capture with no matching release.
            view.RaiseEvent(new PointerCaptureLostEventArgs(view, pointer));

            pending = view.BodyTextDragPendingForTest;
            active = view.BodyTextDragActiveForTest;
            selectedAfterLoss = view.SelectedText;
            text = SoleParagraphText(view);
        });

        pending.Should().BeFalse("losing capture must abandon the armed drag, not strand it");
        active.Should().BeFalse("losing capture must abandon the active drag, not strand it");
        selectedAfterLoss.Should().Be("BBBB", "an abandoned drag restores the selection it started from");
        text.Should().Be(Text, "an abandoned drag must not half-apply the move");
    }

    // ==== No-regression: the ordinary in-bounds gesture still moves the text ========================

    [Fact]
    public async Task OrdinaryInBoundsDragMove_StillMovesTheTextAndReleasesCapture()
    {
        string? text = null;
        object? capturedAfterRelease = new object();

        await OnHostedView((window, view) =>
        {
            var dropPoint = EndOfParagraphPoint(view, 0, Text.Length); // well past "CCCC"
            var pressPoint = SelectBbbbAndGetInsidePoint(view);

            var pointer = NewPointer();
            Press(window, view, pointer, pressPoint);
            Move(window, view, pointer, new Point(dropPoint.X, pressPoint.Y));
            Release(window, view, pointer, dropPoint);

            text = SoleParagraphText(view);
            capturedAfterRelease = pointer.Captured;
        });

        text.Should().Be("AAAA  CCCCBBBB", "taking capture must not change what an ordinary in-bounds drag-move does");
        capturedAfterRelease.Should().BeNull("the completed gesture must hand the pointer back");
    }
}
