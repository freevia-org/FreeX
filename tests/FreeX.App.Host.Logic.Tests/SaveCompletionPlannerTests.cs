using System.IO;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Unit tests for <see cref="SaveCompletionPlanner"/>.
/// These cover the post-save decision matrix without requiring a live window.
/// </summary>
public sealed class SaveCompletionPlannerTests
{
    // ── Same workbook, no edits during save ──────────────────────────────────

    [Fact]
    public void Plan_SameWorkbook_NoEdits_MarksSavedAndAppliesFileContext()
    {
        var plan = SaveCompletionPlanner.Plan(
            generationAtSaveStart: 3,
            generationNow: 3,
            sameWorkbook: true);

        plan.MarkSaved.Should().BeTrue();
        plan.ApplyFileContext.Should().BeTrue();
    }

    // ── Same workbook, edits arrived mid-save ────────────────────────────────

    [Fact]
    public void Plan_SameWorkbook_EditsDuringSerialize_DoesNotMarkSaved()
    {
        var plan = SaveCompletionPlanner.Plan(
            generationAtSaveStart: 5,
            generationNow: 6,
            sameWorkbook: true);

        plan.MarkSaved.Should().BeFalse(
            "edits arrived after save started — clearing dirty would silently discard them");
    }

    [Fact]
    public void Plan_SameWorkbook_EditsDuringSerialize_StillAppliesFileContext()
    {
        // The file was written to disk successfully; the path/name should be updated
        // even though we cannot mark the workbook as saved (it has pending edits).
        var plan = SaveCompletionPlanner.Plan(
            generationAtSaveStart: 1,
            generationNow: 4,
            sameWorkbook: true);

        plan.ApplyFileContext.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 100)]
    [InlineData(7, 8)]
    public void Plan_SameWorkbook_AnyGenerationAdvance_DoesNotMarkSaved(
        int generationAtStart,
        int generationNow)
    {
        var plan = SaveCompletionPlanner.Plan(generationAtStart, generationNow, sameWorkbook: true);

        plan.MarkSaved.Should().BeFalse();
        plan.ApplyFileContext.Should().BeTrue();
    }

    // ── Workbook replaced during save ────────────────────────────────────────

    [Fact]
    public void Plan_DifferentWorkbook_NoEdits_DoesNotMarkSavedOrApplyContext()
    {
        // The user opened a new file while save was running — the saved snapshot
        // belongs to the old workbook.  Nothing should be applied to the new one.
        var plan = SaveCompletionPlanner.Plan(
            generationAtSaveStart: 2,
            generationNow: 2,
            sameWorkbook: false);

        plan.MarkSaved.Should().BeFalse();
        plan.ApplyFileContext.Should().BeFalse();
    }

    [Fact]
    public void Plan_DifferentWorkbook_WithEdits_DoesNotMarkSavedOrApplyContext()
    {
        var plan = SaveCompletionPlanner.Plan(
            generationAtSaveStart: 0,
            generationNow: 3,
            sameWorkbook: false);

        plan.MarkSaved.Should().BeFalse();
        plan.ApplyFileContext.Should().BeFalse();
    }

    // ── Generation starts at zero (fresh clean workbook) ────────────────────

    [Fact]
    public void Plan_InitialGeneration_ZeroToZero_MarksSavedAndAppliesContext()
    {
        // A brand-new workbook that was never edited: generation stays at 0 throughout.
        var plan = SaveCompletionPlanner.Plan(
            generationAtSaveStart: 0,
            generationNow: 0,
            sameWorkbook: true);

        plan.MarkSaved.Should().BeTrue();
        plan.ApplyFileContext.Should().BeTrue();
    }

    // ── Record equality (sanity) ─────────────────────────────────────────────

    [Fact]
    public void Plan_WithPath_AttachesFileContextForCurrentWorkbook()
    {
        var path = Path.Combine(Path.GetTempPath(), "SavedWorkbook.fxl");

        var plan = SaveCompletionPlanner.Plan(
            generationAtSaveStart: 3,
            generationNow: 4,
            sameWorkbook: true,
            path);

        plan.MarkSaved.Should().BeFalse();
        plan.ApplyFileContext.Should().BeTrue();
        plan.FileContext.Should().NotBeNull();
        plan.FileContext!.Path.Should().Be(path);
        plan.FileContext.DisplayName.Should().Be("SavedWorkbook");
        plan.FileContext.RecentFileRegistration.FilePath.Should().Be(path);
    }

    [Fact]
    public void Plan_WithPath_SkipsFileContextWhenWorkbookWasReplaced()
    {
        var plan = SaveCompletionPlanner.Plan(
            generationAtSaveStart: 3,
            generationNow: 3,
            sameWorkbook: false,
            Path.Combine(Path.GetTempPath(), "StaleSave.fxl"));

        plan.MarkSaved.Should().BeFalse();
        plan.ApplyFileContext.Should().BeFalse();
        plan.FileContext.Should().BeNull();
    }

    [Fact]
    public void SaveCompletionPlan_RecordEquality_WorksByValue()
    {
        var a = new SaveCompletionPlan(MarkSaved: true, ApplyFileContext: true);
        var b = new SaveCompletionPlan(MarkSaved: true, ApplyFileContext: true);
        var c = new SaveCompletionPlan(MarkSaved: false, ApplyFileContext: true);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
