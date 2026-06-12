using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Unit tests for <see cref="WorkbookDocumentState"/>.
/// Covers dirty-flag transitions, generation monotonicity, mark-saved variants,
/// file-path transitions, suppress-close-prompt, and multi-instance independence.
/// </summary>
public sealed class WorkbookDocumentStateTests
{
    // ── Initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsClean()
    {
        var state = new WorkbookDocumentState();

        state.IsDirty.Should().BeFalse();
        state.DirtyGeneration.Should().Be(0);
        state.CurrentFilePath.Should().BeNull();
        state.SuppressClosePrompt.Should().BeFalse();
    }

    // ── MarkDirty ─────────────────────────────────────────────────────────────

    [Fact]
    public void MarkDirty_SetsDirtyFlag()
    {
        var state = new WorkbookDocumentState();

        state.MarkDirty();

        state.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void MarkDirty_IncrementsDirtyGeneration()
    {
        var state = new WorkbookDocumentState();

        state.MarkDirty();

        state.DirtyGeneration.Should().Be(1);
    }

    [Fact]
    public void MarkDirty_CalledMultipleTimes_GenerationIsMonotonicallyIncreasing()
    {
        var state = new WorkbookDocumentState();

        state.MarkDirty();
        state.MarkDirty();
        state.MarkDirty();

        state.DirtyGeneration.Should().Be(3);
    }

    [Fact]
    public void MarkDirty_ClearsSuppressClosePrompt()
    {
        var state = new WorkbookDocumentState();
        state.SuppressClosePrompt = true;

        state.MarkDirty();

        state.SuppressClosePrompt.Should().BeFalse(
            "re-dirtying after a suppressed close must re-arm the prompt");
    }

    // ── MarkSaved ─────────────────────────────────────────────────────────────

    [Fact]
    public void MarkSaved_ClearsDirtyFlag()
    {
        var state = new WorkbookDocumentState();
        state.MarkDirty();

        state.MarkSaved();

        state.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void MarkSaved_DoesNotResetGeneration()
    {
        var state = new WorkbookDocumentState();
        state.MarkDirty();
        state.MarkDirty();
        var generationBeforeSave = state.DirtyGeneration;

        state.MarkSaved();

        state.DirtyGeneration.Should().Be(generationBeforeSave,
            "generation is not reset on save — it only ever increases");
    }

    [Fact]
    public void MarkSaved_DoesNotChangeCurrentFilePath()
    {
        var state = new WorkbookDocumentState();
        state.SetCurrentFilePath(@"C:\docs\book.xlsx");
        state.MarkDirty();

        state.MarkSaved();

        state.CurrentFilePath.Should().Be(@"C:\docs\book.xlsx");
    }

    // ── MarkSavedWithPath ─────────────────────────────────────────────────────

    [Fact]
    public void MarkSavedWithPath_ClearsDirtyAndSetsPath()
    {
        var state = new WorkbookDocumentState();
        state.MarkDirty();

        state.MarkSavedWithPath(@"C:\work\report.xlsx");

        state.IsDirty.Should().BeFalse();
        state.CurrentFilePath.Should().Be(@"C:\work\report.xlsx");
    }

    [Fact]
    public void MarkSavedWithPath_NullPath_Throws()
    {
        var state = new WorkbookDocumentState();
        var act = () => state.MarkSavedWithPath(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MarkSavedWithPath_EmptyPath_Throws()
    {
        var state = new WorkbookDocumentState();
        var act = () => state.MarkSavedWithPath("");

        act.Should().Throw<ArgumentException>();
    }

    // ── Generation-checked saved transition (mirrors SaveCompletionPlanner use) ──

    [Fact]
    public void GenerationCheck_NoEditsArrivedDuringSave_GenerationIsStable()
    {
        var state = new WorkbookDocumentState();
        state.MarkDirty();
        var generationAtSaveStart = state.DirtyGeneration;

        // Simulate no edits arriving during the async save window.
        var noEditsArrived = state.DirtyGeneration == generationAtSaveStart;
        noEditsArrived.Should().BeTrue();
    }

    [Fact]
    public void GenerationCheck_EditArrivedDuringSave_GenerationAdvanced()
    {
        var state = new WorkbookDocumentState();
        state.MarkDirty();
        var generationAtSaveStart = state.DirtyGeneration;

        // Simulate an edit arriving while async serialization was in flight.
        state.MarkDirty();

        var editsArrived = state.DirtyGeneration != generationAtSaveStart;
        editsArrived.Should().BeTrue();
    }

    // ── File path transitions ─────────────────────────────────────────────────

    [Fact]
    public void SetCurrentFilePath_UpdatesPath()
    {
        var state = new WorkbookDocumentState();

        state.SetCurrentFilePath(@"C:\work\sheet.xlsx");

        state.CurrentFilePath.Should().Be(@"C:\work\sheet.xlsx");
    }

    [Fact]
    public void SetCurrentFilePath_Null_ClearsPath()
    {
        var state = new WorkbookDocumentState();
        state.SetCurrentFilePath(@"C:\work\sheet.xlsx");

        state.SetCurrentFilePath(null);

        state.CurrentFilePath.Should().BeNull();
    }

    [Fact]
    public void ClearCurrentFilePath_ResetsToNull()
    {
        var state = new WorkbookDocumentState();
        state.SetCurrentFilePath(@"C:\work\sheet.xlsx");

        state.ClearCurrentFilePath();

        state.CurrentFilePath.Should().BeNull();
    }

    [Fact]
    public void SetCurrentFilePath_DoesNotChangeDirtyFlag()
    {
        var state = new WorkbookDocumentState();

        state.SetCurrentFilePath(@"C:\work\sheet.xlsx");

        state.IsDirty.Should().BeFalse();
    }

    // ── SuppressClosePrompt ──────────────────────────────────────────────────

    [Fact]
    public void SuppressClosePrompt_DefaultsFalse()
    {
        var state = new WorkbookDocumentState();
        state.SuppressClosePrompt.Should().BeFalse();
    }

    [Fact]
    public void SuppressClosePrompt_CanBeSetDirectly()
    {
        var state = new WorkbookDocumentState();
        state.SuppressClosePrompt = true;

        state.SuppressClosePrompt.Should().BeTrue();
    }

    // ── Multi-instance independence ───────────────────────────────────────────

    [Fact]
    public void TwoInstances_HaveIndependentState()
    {
        var stateA = new WorkbookDocumentState();
        var stateB = new WorkbookDocumentState();

        stateA.MarkDirty();
        stateA.MarkDirty();
        stateB.MarkDirty();
        stateA.SetCurrentFilePath(@"C:\a.xlsx");

        stateA.IsDirty.Should().BeTrue();
        stateA.DirtyGeneration.Should().Be(2);
        stateA.CurrentFilePath.Should().Be(@"C:\a.xlsx");

        stateB.IsDirty.Should().BeTrue();
        stateB.DirtyGeneration.Should().Be(1, "stateB has its own counter");
        stateB.CurrentFilePath.Should().BeNull("stateB path was never set");
    }

    [Fact]
    public void TwoInstances_MarkSavedOnOneDoesNotAffectOther()
    {
        var stateA = new WorkbookDocumentState();
        var stateB = new WorkbookDocumentState();
        stateA.MarkDirty();
        stateB.MarkDirty();

        stateA.MarkSaved();

        stateA.IsDirty.Should().BeFalse();
        stateB.IsDirty.Should().BeTrue("stateB was not saved");
    }

    // ── Round-trip: dirty → save → dirty again ────────────────────────────────

    [Fact]
    public void DirtySaveAndDirtyAgain_GenerationContinuesFromWhereItLeft()
    {
        var state = new WorkbookDocumentState();

        state.MarkDirty(); // generation = 1
        state.MarkSaved();
        state.MarkDirty(); // generation = 2

        state.IsDirty.Should().BeTrue();
        state.DirtyGeneration.Should().Be(2);
    }

    // ── SavedUndoDepth initial state ──────────────────────────────────────────

    [Fact]
    public void InitialState_SavedUndoDepthIsMinusOne()
    {
        var state = new WorkbookDocumentState();

        state.SavedUndoDepth.Should().Be(-1,
            "no save-point has been recorded on a fresh state");
    }

    // ── MarkSaved resets SavedUndoDepth ────────────────────────────────────────

    [Fact]
    public void MarkSaved_ResetsSavedUndoDepthToMinusOne()
    {
        var state = new WorkbookDocumentState();
        state.MarkSavedAtUndoDepth(5);

        state.MarkSaved();

        state.SavedUndoDepth.Should().Be(-1,
            "MarkSaved without a depth means no save-point is known");
    }

    // ── MarkSavedAtUndoDepth ─────────────────────────────────────────────────

    [Fact]
    public void MarkSavedAtUndoDepth_ClearsDirtyAndRecordsDepth()
    {
        var state = new WorkbookDocumentState();
        state.MarkDirty();

        state.MarkSavedAtUndoDepth(3);

        state.IsDirty.Should().BeFalse();
        state.SavedUndoDepth.Should().Be(3);
    }

    [Fact]
    public void MarkSavedAtUndoDepth_DepthZero_IsValid()
    {
        var state = new WorkbookDocumentState();
        state.MarkDirty();

        state.MarkSavedAtUndoDepth(0);

        state.IsDirty.Should().BeFalse();
        state.SavedUndoDepth.Should().Be(0);
    }

    // ── TryMarkCleanIfAtSavePoint ─────────────────────────────────────────────

    [Fact]
    public void TryMarkCleanIfAtSavePoint_MatchingDepth_ClearsAndReturnsTrue()
    {
        var state = new WorkbookDocumentState();
        state.MarkSavedAtUndoDepth(2);
        state.MarkDirty();  // dirtied again after save (simulates an edit)

        var result = state.TryMarkCleanIfAtSavePoint(currentUndoDepth: 2);

        result.Should().BeTrue();
        state.IsDirty.Should().BeFalse(
            "undo has returned the stack to the save-point depth — workbook is effectively saved");
    }

    [Fact]
    public void TryMarkCleanIfAtSavePoint_NonMatchingDepth_ReturnsFalse()
    {
        var state = new WorkbookDocumentState();
        state.MarkSavedAtUndoDepth(2);
        state.MarkDirty();

        var result = state.TryMarkCleanIfAtSavePoint(currentUndoDepth: 1);

        result.Should().BeFalse("undo depth does not match the save point");
        state.IsDirty.Should().BeTrue("dirty flag unchanged");
    }

    [Fact]
    public void TryMarkCleanIfAtSavePoint_NoSavePointRecorded_ReturnsFalse()
    {
        var state = new WorkbookDocumentState();
        state.MarkDirty();

        // SavedUndoDepth == -1 means "no save point"; depth -1 should never match.
        var result = state.TryMarkCleanIfAtSavePoint(currentUndoDepth: -1);

        result.Should().BeFalse("a depth of -1 must never be treated as a save point");
        state.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void TryMarkCleanIfAtSavePoint_AlreadyClean_ReturnsTrueAndStaysClean()
    {
        var state = new WorkbookDocumentState();
        state.MarkSavedAtUndoDepth(3);
        // No MarkDirty — workbook is already clean.

        var result = state.TryMarkCleanIfAtSavePoint(currentUndoDepth: 3);

        // Returns true because the depths match — the caller should NOT call MarkWorkbookDirty().
        result.Should().BeTrue("depth matches the save point, so the clean rule applies");
        state.IsDirty.Should().BeFalse("still clean");
    }

    // ── MarkSavedWithPath resets SavedUndoDepth ────────────────────────────────

    [Fact]
    public void MarkSavedWithPath_ResetsSavedUndoDepthToMinusOne()
    {
        var state = new WorkbookDocumentState();
        state.MarkSavedAtUndoDepth(4);

        state.MarkSavedWithPath(@"C:\work\book.xlsx");

        state.SavedUndoDepth.Should().Be(-1,
            "MarkSavedWithPath does not know the undo depth; callers that need save-point" +
            " tracking should use MarkSavedAtUndoDepth");
    }

    // ── Save-point round-trip: save → edit → undo back ────────────────────────

    [Fact]
    public void SavePointRoundTrip_UndoBackToSavePoint_MarksClean()
    {
        var state = new WorkbookDocumentState();

        // Simulate: user has made 3 commands (undoDepth = 3), then saves.
        state.MarkSavedAtUndoDepth(3);

        // User makes another edit → dirty.
        state.MarkDirty();
        state.IsDirty.Should().BeTrue();

        // User undoes the edit → undoDepth goes back to 3.
        var clearedByUndoToSavePoint = state.TryMarkCleanIfAtSavePoint(currentUndoDepth: 3);

        clearedByUndoToSavePoint.Should().BeTrue();
        state.IsDirty.Should().BeFalse();
    }
}
