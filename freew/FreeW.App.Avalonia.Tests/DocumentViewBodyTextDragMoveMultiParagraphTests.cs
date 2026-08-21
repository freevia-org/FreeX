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
/// r160, shared-drag-drop-content F1: <c>TryCommitBodyTextDragMove</c> used to refuse ANY selection that
/// crossed a paragraph boundary outright (<c>if (source.Start.Block != source.End.Block) return false;</c>),
/// even though <c>TryArmBodyTextDrag</c>/<c>UpdateBodyTextDrag</c> happily arm and track a multi-paragraph
/// drag (cursor and all) with no such check -- so the drag looked live the whole time and then silently
/// restored the original selection at drop, with no error. The WPF host gets ordinary within-page
/// drag-drop for free from native RichTextBox, which is not limited to one paragraph.
/// </summary>
public sealed class DocumentViewBodyTextDragMoveMultiParagraphTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task OnUiThread(Action action) => Session.Dispatch(action, CancellationToken.None);

    private static DocumentView BuildThreeParagraphView()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("AAAA BBBB"));
        document.Blocks.Add(new Paragraph("CCCC DDDD"));
        document.Blocks.Add(new Paragraph("EEEE FFFF"));

        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    /// <summary>A point guaranteed to hit-test back to (block, offset); moves the caret as a side effect.</summary>
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

    private static string[] ParagraphTexts(DocumentView view) =>
        view.Document.Paragraphs.Select(p => p.PlainText).ToArray();

    [Fact]
    public async Task DraggingASelectionThatSpansTwoParagraphs_MovesItToTheDropParagraph()
    {
        string[]? result = null;
        bool becameActive = false;

        await OnUiThread(() =>
        {
            var view = BuildThreeParagraphView();
            // Select "BBBB" (end of paragraph 0) through "CCCC" (start of paragraph 1) -- exactly the
            // finding's own example gesture.
            var pressPoint = GlyphPoint(view, 0, 6); // inside "BBBB"
            var dropPoint = GlyphPoint(view, 2, 5); // inside paragraph 2, just before "FFFF"

            view.SetBodySelectionForTest(0, 5, 1, 4);
            view.TryArmBodyTextDragForTest(pressPoint).Should().BeTrue(
                "arming must not itself be limited to one paragraph -- that was never the bug");

            view.UpdateBodyTextDragForTest(dropPoint);
            becameActive = view.BodyTextDragActiveForTest;

            view.CommitBodyTextDragForTest(dropPoint);
            result = ParagraphTexts(view);
        });

        becameActive.Should().BeTrue();
        result.Should().Equal(
            new[] { "AAAA  DDDD", "EEEE BBBB", "CCCCFFFF" },
            "the cross-paragraph text must be removed from its old spot (paragraphs 0/1 merge around the gap) "
            + "and re-inserted at the drop point, itself splitting paragraph 2 around the paragraph break the "
            + "dragged text carried");
    }

    [Fact]
    public async Task CtrlHeldDragOfATwoParagraphSelection_CopiesInsteadOfMoving()
    {
        string[]? result = null;

        await OnUiThread(() =>
        {
            var view = BuildThreeParagraphView();
            var pressPoint = GlyphPoint(view, 0, 6);
            var dropPoint = GlyphPoint(view, 2, 5);

            view.SetBodySelectionForTest(0, 5, 1, 4);
            view.TryArmBodyTextDragForTest(pressPoint).Should().BeTrue();
            view.UpdateBodyTextDragForTest(dropPoint, ctrlHeld: true);
            view.CommitBodyTextDragForTest(dropPoint, ctrlHeld: true);

            result = ParagraphTexts(view);
        });

        result.Should().Equal(
            new[] { "AAAA BBBB", "CCCC DDDD", "EEEE BBBB", "CCCCFFFF" },
            "Ctrl held must copy the cross-paragraph text, leaving the original two paragraphs untouched "
            + "and splitting the drop paragraph the same way a move would");
    }

    [Fact]
    public async Task DroppingInsideAMultiParagraphSourceSelection_IsANoOpThatRestoresTheSelection()
    {
        string[]? result = null;
        string? selectedAfterRelease = null;

        await OnUiThread(() =>
        {
            var view = BuildThreeParagraphView();
            var pressPoint = GlyphPoint(view, 0, 6); // inside "BBBB"
            var dropInsidePoint = GlyphPoint(view, 1, 2); // inside "CCCC", still inside the source range

            view.SetBodySelectionForTest(0, 5, 1, 4);
            view.TryArmBodyTextDragForTest(pressPoint).Should().BeTrue();
            view.UpdateBodyTextDragForTest(new Point(dropInsidePoint.X, dropInsidePoint.Y + 40));
            view.CommitBodyTextDragForTest(dropInsidePoint);

            result = ParagraphTexts(view);
            selectedAfterRelease = view.SelectedText;
        });

        result.Should().Equal(new[] { "AAAA BBBB", "CCCC DDDD", "EEEE FFFF" },
            "dropping back inside the multi-paragraph source selection must not change the document");
        selectedAfterRelease.Should().Be("BBBB\nCCCC", "a no-op drop must leave the original selection exactly as it was");
    }

    // ── Sibling / no-regression: a single-paragraph drag is completely unaffected ────────────────────

    [Fact]
    public async Task DraggingASingleParagraphSelection_StillMovesWithinThatParagraph()
    {
        string? result = null;

        await OnUiThread(() =>
        {
            var view = BuildThreeParagraphView();
            var pressPoint = GlyphPoint(view, 1, 1); // inside "CCCC" (paragraph 1)
            var dropPoint = EndOfParagraphPoint(view, 1, 9); // end of paragraph 1, well past "DDDD"

            view.SetBodySelectionForTest(1, 0, 1, 4); // whole "CCCC", same paragraph both ends
            view.TryArmBodyTextDragForTest(pressPoint).Should().BeTrue();
            view.UpdateBodyTextDragForTest(dropPoint);
            view.CommitBodyTextDragForTest(dropPoint);

            result = view.Document.Paragraphs.ElementAt(1).PlainText;
        });

        result.Should().Be(" DDDDCCCC", "an ordinary same-paragraph drag-move is completely unaffected by this fix");
    }
}
