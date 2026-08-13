using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using FluentAssertions;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-10 fix group COMMENTS-AV regression guards:
///
///   P2 — Avalonia "Delete Note" and "Delete Comment" both ran the broad ClearCommentsCommand
///        (via DeleteActiveCellComment/_session.ClearSelectedRangeComments), destroying a cell's
///        WHOLE threaded conversation (every reply) whenever the user only meant to delete the
///        legacy note, and vice versa. WPF keeps these separate: Delete Note -> DeleteCommentCommand
///        (note only), Delete Comment -> DeleteThreadedCommentCommand (thread only). The fix splits
///        the Avalonia handler into DeleteActiveCellNote / DeleteActiveCellThreadedComment, each
///        targeting only its own kind.
///
///   P4 — the Avalonia grid never consulted DisplayCell.HasComment/CommentDisplay at all, so cells
///        carrying a threaded comment or legacy note rendered identically to a plain cell (no corner
///        indicator, no hover card) — a total parity gap vs WPF's GridView.Rendering.cs
///        DrawCommentIndicator + GridView.CommentPreview.cs. The fix threads CommentDisplay through
///        CreateCell/CreateInteractiveCellBorder/CreateCellBorder to paint a small corner triangle
///        (color keyed by CellCommentDisplayKind) and set a ToolTip with the comment's title/body.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class FreeXReview10CommentsAvTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── P2: Delete Note must remove only the legacy note, keeping the threaded conversation ──────

    [Fact]
    public async Task DeleteActiveCellNote_RemovesOnlyTheNote_KeepsThreadedCommentWithReplies()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var address = new CellAddress(sheet.Id, 3, 2);
            sheet.Comments[address] = "Legacy note text";
            var thread = new ThreadedComment("Root comment") with
            {
                Replies = [new CommentReply("A reply that must survive")],
            };
            sheet.ThreadedComments[address] = thread;

            window.Session.SelectCell(address);

            window.DeleteActiveCellNoteForTest();

            sheet.Comments.ContainsKey(address).Should().BeFalse(
                "Delete Note must remove the legacy note");
            sheet.ThreadedComments.TryGetValue(address, out var survivingThread).Should().BeTrue(
                "Delete Note must NOT touch the coexisting threaded comment");
            survivingThread!.Replies.Should().ContainSingle(r => r.Text == "A reply that must survive",
                "every reply in the thread must survive a Delete Note action");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── P2: Delete Comment must remove only the threaded comment, keeping the legacy note ────────

    [Fact]
    public async Task DeleteActiveCellThreadedComment_RemovesOnlyTheThread_KeepsLegacyNote()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var address = new CellAddress(sheet.Id, 4, 2);
            sheet.Comments[address] = "Legacy note that must survive";
            var thread = new ThreadedComment("Root comment") with
            {
                Replies = [new CommentReply("Reply one"), new CommentReply("Reply two")],
            };
            sheet.ThreadedComments[address] = thread;

            window.Session.SelectCell(address);

            window.DeleteActiveCellThreadedCommentForTest();

            sheet.ThreadedComments.ContainsKey(address).Should().BeFalse(
                "Delete Comment must remove the whole threaded conversation (root + replies)");
            sheet.Comments.TryGetValue(address, out var survivingNote).Should().BeTrue(
                "Delete Comment must NOT touch the coexisting legacy note");
            survivingNote.Should().Be("Legacy note that must survive");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── P2: undo must restore exactly what the targeted-delete removed (command Apply/Revert) ────

    [Fact]
    public async Task DeleteActiveCellThreadedComment_UsesUndoableCommand_RevertRestoresThread()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var address = new CellAddress(sheet.Id, 5, 2);
            var thread = new ThreadedComment("Root") with { Replies = [new CommentReply("Reply")] };
            sheet.ThreadedComments[address] = thread;
            window.Session.SelectCell(address);

            var command = new DeleteThreadedCommentCommand(sheet.Id, address);
            var ctx = new TestCommandContext(window.Session.Workbook);
            var outcome = command.Apply(ctx);
            outcome.Success.Should().BeTrue();
            sheet.ThreadedComments.ContainsKey(address).Should().BeFalse();

            command.Revert(ctx);
            sheet.ThreadedComments.TryGetValue(address, out var restored).Should().BeTrue(
                "Revert must restore the deleted threaded comment, including its replies");
            restored!.Replies.Should().ContainSingle(r => r.Text == "Reply");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── P4: a cell with a threaded comment must render a corner indicator + hover-card tooltip ────

    [Fact]
    public async Task CreateCell_RendersCommentIndicatorAndTooltip_ForCellWithThreadedComment()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var address = new CellAddress(sheet.Id, 2, 2);
            sheet.SetCell(address, new TextValue("Has a comment"));
            sheet.ThreadedComments[address] = new ThreadedComment("Reviewer note") with
            {
                Replies = [new CommentReply("Follow-up")],
            };
            ForceViewportRefresh(window);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;
            var border = FindCellsCoveringSlot(grid, headerOffset + 1, headerOffset + 1).Single();

            var tip = global::Avalonia.Controls.ToolTip.GetTip(border);
            tip.Should().NotBeNull("a cell with a threaded comment must carry a hover-card tooltip");
            tip!.ToString().Should().Contain("Reviewer note");

            HasCommentIndicatorShape(border).Should().BeTrue(
                "a cell with a threaded comment must render the small corner indicator triangle");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CreateCell_NoCommentIndicator_ForPlainCell()
    {
        // Guards against a regression in the opposite direction: a cell with no comment/note at all
        // must not spuriously grow an indicator or tooltip.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var address = new CellAddress(sheet.Id, 2, 2);
            sheet.SetCell(address, new TextValue("Plain cell"));
            ForceViewportRefresh(window);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;
            var border = FindCellsCoveringSlot(grid, headerOffset + 1, headerOffset + 1).Single();

            global::Avalonia.Controls.ToolTip.GetTip(border).Should().BeNull(
                "a plain cell without any comment/note must not carry a hover-card tooltip");
            HasCommentIndicatorShape(border).Should().BeFalse(
                "a plain cell without any comment/note must not render the corner indicator");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Shared helpers ─────────────────────────────────────────────────────────────────────────

    private const double InitialViewportHeightForTests = 880;
    private const double InitialViewportWidthForTests = 1440;

    private static void ForceViewportRefresh(MainWindow window) =>
        window.Session.UpdateViewportSize(InitialViewportHeightForTests + 1, InitialViewportWidthForTests);

    private static Grid FindInnerGrid(Control built)
    {
        if (built is Grid { Background: not null } ownGrid)
            return ownGrid;

        if (built is Grid composite)
            return composite.Children.OfType<Grid>().First(g => g.Background is not null);

        return (Grid)built;
    }

    private static IEnumerable<Border> FindCellsCoveringSlot(Grid grid, int row, int col) =>
        grid.Children.OfType<Border>().Where(b =>
        {
            var br = Grid.GetRow(b);
            var bc = Grid.GetColumn(b);
            var rowSpan = Grid.GetRowSpan(b);
            var colSpan = Grid.GetColumnSpan(b);
            return row >= br && row < br + rowSpan && col >= bc && col < bc + colSpan;
        });

    // The comment corner indicator is rendered as an Avalonia.Controls.Shapes.Path with a small
    // (<=7*zoom-ish) square bounding box, filled solid (no stroke) — distinguishing it from other
    // shape-based adornments (e.g. the autofill handle, which is a Rectangle, not a Path).
    private static bool HasCommentIndicatorShape(Border border) =>
        FindDescendants(border).OfType<global::Avalonia.Controls.Shapes.Path>()
            .Any(p => p.Width > 0 && p.Width == p.Height && p.Width <= 10);

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        if (root is Border { Child: { } child })
        {
            yield return child;
            foreach (var descendant in FindDescendants(child))
                yield return descendant;
        }
        else if (root is Panel panel)
        {
            foreach (var c in panel.Children)
            {
                yield return c;
                foreach (var descendant in FindDescendants(c))
                    yield return descendant;
            }
        }
    }

    /// <summary>Minimal ICommandContext for directly exercising Apply/Revert on a command.</summary>
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
