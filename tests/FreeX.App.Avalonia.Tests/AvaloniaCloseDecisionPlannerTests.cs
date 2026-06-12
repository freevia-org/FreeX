using FluentAssertions;
using FreeX.App.Avalonia;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for <see cref="AvaloniaCloseDecisionPlanner"/>.
/// Covers the full matrix of confirmation outcomes × dirty state.
/// </summary>
public sealed class AvaloniaCloseDecisionPlannerTests
{
    // ── Cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Cancel_DirtyWorkbook_StaysOpen()
    {
        var action = AvaloniaCloseDecisionPlanner.Decide(
            AvaloniaCloseConfirmation.Cancel, isDirtyNow: true);

        action.Should().Be(AvaloniaCloseAction.StayOpen);
    }

    [Fact]
    public void Decide_Cancel_CleanWorkbook_StaysOpen()
    {
        var action = AvaloniaCloseDecisionPlanner.Decide(
            AvaloniaCloseConfirmation.Cancel, isDirtyNow: false);

        action.Should().Be(AvaloniaCloseAction.StayOpen,
            "Cancel always aborts the close regardless of dirty state");
    }

    // ── Discard ──────────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Discard_DirtyWorkbook_Closes()
    {
        // Critical: must close even though isDirtyNow=true.
        // The Discard path intentionally discards changes — re-checking dirty would be the
        // class of bug that existed in the WPF host (Discard couldn't close dirty files).
        var action = AvaloniaCloseDecisionPlanner.Decide(
            AvaloniaCloseConfirmation.Discard, isDirtyNow: true);

        action.Should().Be(AvaloniaCloseAction.Close,
            "Discard must close unconditionally — dirty flag is deliberately NOT re-checked");
    }

    [Fact]
    public void Decide_Discard_CleanWorkbook_Closes()
    {
        var action = AvaloniaCloseDecisionPlanner.Decide(
            AvaloniaCloseConfirmation.Discard, isDirtyNow: false);

        action.Should().Be(AvaloniaCloseAction.Close);
    }

    // ── Continue (save-and-close path) ───────────────────────────────────────

    [Fact]
    public void Decide_Continue_WorkbookClean_Closes()
    {
        // Save completed cleanly — no mid-save edits arrived.  Safe to close.
        var action = AvaloniaCloseDecisionPlanner.Decide(
            AvaloniaCloseConfirmation.Continue, isDirtyNow: false);

        action.Should().Be(AvaloniaCloseAction.Close);
    }

    [Fact]
    public void Decide_Continue_WorkbookStillDirty_StaysOpen()
    {
        // New edits arrived while the async save was running.  The save completed,
        // but the workbook has unsaved changes — keep the window open.
        var action = AvaloniaCloseDecisionPlanner.Decide(
            AvaloniaCloseConfirmation.Continue, isDirtyNow: true);

        action.Should().Be(AvaloniaCloseAction.StayOpen,
            "mid-save edits make it unsafe to close even though the user clicked Save");
    }

    // ── Enum completeness / unknown values ───────────────────────────────────

    [Theory]
    [InlineData((AvaloniaCloseConfirmation)99, true)]
    [InlineData((AvaloniaCloseConfirmation)99, false)]
    public void Decide_UnknownConfirmation_StaysOpen(
        AvaloniaCloseConfirmation unknown, bool isDirtyNow)
    {
        // Unknown discriminant values default to StayOpen (safe side).
        var action = AvaloniaCloseDecisionPlanner.Decide(unknown, isDirtyNow);

        action.Should().Be(AvaloniaCloseAction.StayOpen,
            "unrecognised confirmation values must not accidentally close the window");
    }

    // ── Symmetry: dirty flag is irrelevant for Cancel/Discard ────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decide_Cancel_AlwaysStaysOpen_RegardlessOfDirtyState(bool isDirty)
    {
        AvaloniaCloseDecisionPlanner.Decide(AvaloniaCloseConfirmation.Cancel, isDirty)
            .Should().Be(AvaloniaCloseAction.StayOpen);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decide_Discard_AlwaysCloses_RegardlessOfDirtyState(bool isDirty)
    {
        AvaloniaCloseDecisionPlanner.Decide(AvaloniaCloseConfirmation.Discard, isDirty)
            .Should().Be(AvaloniaCloseAction.Close);
    }
}
