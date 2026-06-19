using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the shared <see cref="WindowCloseDecisionPlanner"/> as consumed by the
/// cross-platform port's close flow.
/// Covers the full matrix of confirmation outcomes × dirty state.
/// </summary>
public sealed class AvaloniaCloseDecisionPlannerTests
{
    // ── Cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Cancel_DirtyWorkbook_StaysOpen()
    {
        var action = WindowCloseDecisionPlanner.Decide(
            SaveChangesConfirmation.Cancel, isDirtyNow: true);

        action.Should().Be(WindowCloseAction.StayOpen);
    }

    [Fact]
    public void Decide_Cancel_CleanWorkbook_StaysOpen()
    {
        var action = WindowCloseDecisionPlanner.Decide(
            SaveChangesConfirmation.Cancel, isDirtyNow: false);

        action.Should().Be(WindowCloseAction.StayOpen,
            "Cancel always aborts the close regardless of dirty state");
    }

    // ── Discard ──────────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Discard_DirtyWorkbook_Closes()
    {
        // Critical: must close even though isDirtyNow=true.
        // The Discard path intentionally discards changes — re-checking dirty would be the
        // class of bug that existed in the WPF host (Discard couldn't close dirty files).
        var action = WindowCloseDecisionPlanner.Decide(
            SaveChangesConfirmation.DiscardWithoutSaving, isDirtyNow: true);

        action.Should().Be(WindowCloseAction.Close,
            "Discard must close unconditionally — dirty flag is deliberately NOT re-checked");
    }

    [Fact]
    public void Decide_Discard_CleanWorkbook_Closes()
    {
        var action = WindowCloseDecisionPlanner.Decide(
            SaveChangesConfirmation.DiscardWithoutSaving, isDirtyNow: false);

        action.Should().Be(WindowCloseAction.Close);
    }

    // ── Continue (save-and-close path) ───────────────────────────────────────

    [Fact]
    public void Decide_Continue_WorkbookClean_Closes()
    {
        // Save completed cleanly — no mid-save edits arrived.  Safe to close.
        var action = WindowCloseDecisionPlanner.Decide(
            SaveChangesConfirmation.Continue, isDirtyNow: false);

        action.Should().Be(WindowCloseAction.Close);
    }

    [Fact]
    public void Decide_Continue_WorkbookStillDirty_StaysOpen()
    {
        // New edits arrived while the async save was running.  The save completed,
        // but the workbook has unsaved changes — keep the window open.
        var action = WindowCloseDecisionPlanner.Decide(
            SaveChangesConfirmation.Continue, isDirtyNow: true);

        action.Should().Be(WindowCloseAction.StayOpen,
            "mid-save edits make it unsafe to close even though the user clicked Save");
    }

    // ── Enum completeness / unknown values ───────────────────────────────────

    [Theory]
    [InlineData((SaveChangesConfirmation)99, true)]
    [InlineData((SaveChangesConfirmation)99, false)]
    public void Decide_UnknownConfirmation_StaysOpen(
        SaveChangesConfirmation unknown, bool isDirtyNow)
    {
        // Unknown discriminant values default to StayOpen (safe side).
        var action = WindowCloseDecisionPlanner.Decide(unknown, isDirtyNow);

        action.Should().Be(WindowCloseAction.StayOpen,
            "unrecognised confirmation values must not accidentally close the window");
    }

    // ── Symmetry: dirty flag is irrelevant for Cancel/Discard ────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decide_Cancel_AlwaysStaysOpen_RegardlessOfDirtyState(bool isDirty)
    {
        WindowCloseDecisionPlanner.Decide(SaveChangesConfirmation.Cancel, isDirty)
            .Should().Be(WindowCloseAction.StayOpen);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decide_Discard_AlwaysCloses_RegardlessOfDirtyState(bool isDirty)
    {
        WindowCloseDecisionPlanner.Decide(SaveChangesConfirmation.DiscardWithoutSaving, isDirty)
            .Should().Be(WindowCloseAction.Close);
    }
}
