using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Unit tests for <see cref="WindowCloseDecisionPlanner"/>.
/// Covers all (confirmation, isDirtyNow) combinations for the close-flow decision matrix.
/// </summary>
public sealed class WindowCloseDecisionPlannerTests
{
    // ── Cancel always stays open ──────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decide_Cancel_AlwaysStaysOpen(bool isDirtyNow)
    {
        var action = WindowCloseDecisionPlanner.Decide(SaveChangesConfirmation.Cancel, isDirtyNow);

        action.Should().Be(WindowCloseAction.StayOpen,
            "Cancel means the user explicitly said 'don't close'");
    }

    // ── DiscardWithoutSaving always closes ─────────────────────────────────────

    [Fact]
    public void Decide_Discard_WhenDirty_Closes()
    {
        // The P1 bug: before fix, the dirty re-check fired here and kept the window open.
        var action = WindowCloseDecisionPlanner.Decide(SaveChangesConfirmation.DiscardWithoutSaving, isDirtyNow: true);

        action.Should().Be(WindowCloseAction.Close,
            "Discard means 'close without saving' — the dirty flag state is irrelevant");
    }

    [Fact]
    public void Decide_Discard_WhenClean_Closes()
    {
        var action = WindowCloseDecisionPlanner.Decide(SaveChangesConfirmation.DiscardWithoutSaving, isDirtyNow: false);

        action.Should().Be(WindowCloseAction.Close);
    }

    // ── Continue (save ran) ──────────────────────────────────────────────────

    [Fact]
    public void Decide_Continue_WhenCleanAfterSave_Closes()
    {
        // The save completed cleanly with no edits arriving mid-save.
        var action = WindowCloseDecisionPlanner.Decide(SaveChangesConfirmation.Continue, isDirtyNow: false);

        action.Should().Be(WindowCloseAction.Close,
            "save succeeded and no new edits arrived — safe to close");
    }

    [Fact]
    public void Decide_Continue_WhenDirtyAfterSave_StaysOpen()
    {
        // Edits arrived while the async save was in flight — the window stays open
        // so the user is not surprised by data loss on the unsaved changes.
        var action = WindowCloseDecisionPlanner.Decide(SaveChangesConfirmation.Continue, isDirtyNow: true);

        action.Should().Be(WindowCloseAction.StayOpen,
            "new edits arrived during the async save — the window must stay open to prompt again");
    }

    // ── Full matrix (using int casts to avoid accessibility mismatch in [InlineData]) ──

    [Theory]
    // (confirmation-as-int, isDirtyNow, expectedAction-as-int)
    // SaveChangesConfirmation: Cancel=0, Continue=1, DiscardWithoutSaving=2
    // WindowCloseAction: Close=0, StayOpen=1
    [InlineData(0, false, 1)]  // Cancel + clean    → StayOpen
    [InlineData(0, true,  1)]  // Cancel + dirty    → StayOpen
    [InlineData(2, false, 0)]  // Discard + clean   → Close
    [InlineData(2, true,  0)]  // Discard + dirty   → Close
    [InlineData(1, false, 0)]  // Continue + clean  → Close
    [InlineData(1, true,  1)]  // Continue + dirty  → StayOpen
    public void Decide_FullMatrix(int confirmationInt, bool isDirtyNow, int expectedActionInt)
    {
        var confirmation = (SaveChangesConfirmation)confirmationInt;
        var expectedAction = (WindowCloseAction)expectedActionInt;

        var action = WindowCloseDecisionPlanner.Decide(confirmation, isDirtyNow);

        action.Should().Be(expectedAction,
            $"confirmation={confirmation}, isDirtyNow={isDirtyNow}");
    }
}
