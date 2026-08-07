using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using Free.Shared.Ribbon;
using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// The Avalonia shell wired Review-tab Comments/Notes command handlers (New/Delete/Previous/Next
/// Comment or Note, Convert to Comments) but never supplied a matching ExtraCommandStates entry, so
/// every one of these registered as a plain <see cref="ActionRibbonCommand"/> instead of an
/// <see cref="IRibbonStatefulCommand"/> -- unlike the WPF host's RefreshReviewCommentNoteCommandStates
/// (MainWindow.ReviewCommands.cs), they stayed permanently enabled no matter the selection or whether
/// the sheet had any comments/notes at all. These tests drive the real registered ribbon commands (via
/// <see cref="MainWindow.RibbonCommandRegistryForTest"/>) to prove the fix's GetReview*RibbonState
/// wiring reports the live sheet/selection state, mirroring the R80 Table Design toggle-state tests.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R127_ReviewCommentNoteRibbonStateAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── Fail-before: Delete Comment must grey out with no threaded comment on the active cell ─────
    [Fact]
    public async Task DeleteComment_NoThreadedCommentOnSheet_RibbonCommandReportsDisabled()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var registry = window.RibbonCommandRegistryForTest!;

                // Failing before the fix: no ExtraCommandStates entry existed for "Delete Comment", so
                // it registered as a plain ActionRibbonCommand (not IRibbonStatefulCommand) and this
                // cast failed.
                var deleteComment = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Delete Comment"));
                deleteComment.GetState().IsEnabled.Should().BeFalse(
                    "the active cell has no threaded comment to delete");
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    // ── No-regression sibling: once the active cell actually has a threaded comment, Delete Comment
    // (and the Previous/Next Comment navigators, gated on the sheet as a whole) must report enabled. ──
    [Fact]
    public async Task DeleteComment_ActiveCellHasThreadedComment_RibbonCommandReportsEnabled()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var cell = new CellAddress(sheet.Id, 1, 1);
                sheet.ThreadedComments[cell] = new ThreadedComment("Hello");
                window.Session.SelectCell(cell);

                var registry = window.RibbonCommandRegistryForTest!;

                var deleteComment = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Delete Comment"));
                deleteComment.GetState().IsEnabled.Should().BeTrue(
                    "the active cell now has a threaded comment");

                var nextComment = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Next Comment"));
                nextComment.GetState().IsEnabled.Should().BeTrue(
                    "the sheet now has at least one threaded comment");

                var previousComment = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Previous Comment"));
                previousComment.GetState().IsEnabled.Should().BeTrue(
                    "the sheet now has at least one threaded comment");
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    // ── Previous/Next Comment gate on the sheet having ANY threaded comment, not just the active cell ──
    [Fact]
    public async Task NavigateComment_NoThreadedCommentsOnSheet_RibbonCommandsReportDisabled()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var registry = window.RibbonCommandRegistryForTest!;

                var nextComment = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Next Comment"));
                nextComment.GetState().IsEnabled.Should().BeFalse("the sheet has no threaded comments");

                var previousComment = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Previous Comment"));
                previousComment.GetState().IsEnabled.Should().BeFalse("the sheet has no threaded comments");
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    // ── Notes family: Edit/Delete Note gate on the active cell's own note; Previous/Next Note and
    // Convert to Comments gate on the sheet having any notes at all. ────────────────────────────────
    [Fact]
    public async Task EditAndDeleteNote_NoNoteOnActiveCell_RibbonCommandsReportDisabled()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var registry = window.RibbonCommandRegistryForTest!;

                var editNote = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Edit Note"));
                editNote.GetState().IsEnabled.Should().BeFalse("the active cell has no note");

                var deleteNote = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Delete Note"));
                deleteNote.GetState().IsEnabled.Should().BeFalse("the active cell has no note");

                var nextNote = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Next Note"));
                nextNote.GetState().IsEnabled.Should().BeFalse("the sheet has no notes");

                var previousNote = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Previous Note"));
                previousNote.GetState().IsEnabled.Should().BeFalse("the sheet has no notes");

                var convert = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Convert to Comments"));
                convert.GetState().IsEnabled.Should().BeFalse("the sheet has no notes to convert");
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditAndDeleteNote_ActiveCellHasNote_RibbonCommandsReportEnabled()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var cell = new CellAddress(sheet.Id, 1, 1);
                sheet.Comments[cell] = "A legacy note";
                window.Session.SelectCell(cell);

                var registry = window.RibbonCommandRegistryForTest!;

                var editNote = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Edit Note"));
                editNote.GetState().IsEnabled.Should().BeTrue("the active cell now has a note");

                var deleteNote = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Delete Note"));
                deleteNote.GetState().IsEnabled.Should().BeTrue("the active cell now has a note");

                var nextNote = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Next Note"));
                nextNote.GetState().IsEnabled.Should().BeTrue("the sheet now has at least one note");

                var convert = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Convert to Comments"));
                convert.GetState().IsEnabled.Should().BeTrue("the sheet now has at least one note to convert");
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static IRibbonCommand GetCommand(IRibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue(
            $"'{id}' must be a registered ribbon command");
        return command!;
    }
}
