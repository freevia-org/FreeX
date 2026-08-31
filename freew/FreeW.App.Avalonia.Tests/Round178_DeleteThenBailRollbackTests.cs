using System.Threading.Tasks;

using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 178. Several editing paths open an undo group, call DeleteSelection(), then bail out when
/// the post-deletion state turns out to be uneditable -- and bailed with AbortUndoGroup, which by
/// its own contract discards the group WITHOUT reverting what was already applied. The selected
/// text was therefore permanently deleted, the typed character never arrived, and Ctrl+Z could not
/// bring the text back because no undo entry was ever pushed for the deletion.
///
/// The reachable case: a selection spanning from one paragraph into another that holds a LOCKED
/// run-level content control positioned entirely AFTER the selection end. The lock is not touched,
/// so the cross-paragraph merge is permitted; but the merged paragraph then carries the locked run,
/// so the editability re-check immediately after fails.
///
/// Sibling guards in InsertParagraphBreak and InsertFieldRunAtActiveCaret already rolled back here
/// and carry the comment explaining why; these two sites were missed.
/// </summary>
public sealed class Round178_DeleteThenBailRollbackTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.StartNew(typeof(global::Avalonia.Application));

    private static Task<bool> OnUiThread(System.Action action) => HeadlessUiThread.Run(action);

    [Fact]
    public async Task TypingOverASelectionThatCannotBeReplaced_LeavesTheDocumentUndoable()
    {
        string? textAfterEdit = null;
        string? textAfterUndo = null;

        var ran = await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph("alpha"));

            var second = new Paragraph();
            second.Runs.Add(new Run("beta"));
            second.Runs.Add(new Run("LOCKED")
            {
                Control = new ContentControl(
                    ContentControlKind.PlainText,
                    LockMode: ContentControlLockMode.ControlAndContentLocked),
            });
            document.Blocks.Add(second);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(800, 2000));

            // Select from inside "alpha" into "beta" -- the locked run sits entirely after the
            // selection end, so the merge is allowed but the merged paragraph is not replaceable.
            view.SetSelectionRangePublic(0, 1, 1, 2);
            view.InsertText("X");

            textAfterEdit = view.Document.PlainText;
            view.Undo();
            textAfterUndo = view.Document.PlainText;
        });
        if (!ran) return;

        // Whatever the edit does or declines to do, it must not leave an unundoable deletion.
        if (textAfterEdit != "alpha\nbetaLOCKED")
        {
            textAfterUndo.Should().Be(
                "alpha\nbetaLOCKED",
                "one Ctrl+Z must restore the pre-gesture text -- abandoning the undo group left the " +
                "deletion applied with nothing on the stack to revert it");
        }
    }
}
