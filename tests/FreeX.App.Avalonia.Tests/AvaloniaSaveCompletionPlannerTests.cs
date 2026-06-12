using FluentAssertions;
using FreeX.App.Avalonia;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for <see cref="AvaloniaSaveCompletionPlanner"/>.
/// Covers the post-save decision matrix without requiring a live Avalonia window.
/// </summary>
public sealed class AvaloniaSaveCompletionPlannerTests
{
    // ── Same workbook, no edits during save ──────────────────────────────────

    [Fact]
    public void Plan_SameWorkbook_NoEdits_MarksSavedAndAppliesFileContext()
    {
        var plan = AvaloniaSaveCompletionPlanner.Plan(
            generationAtSaveStart: 3,
            generationNow: 3,
            sameWorkbook: true);

        plan.MarkSaved.Should().BeTrue();
        plan.ApplyFileContext.Should().BeTrue();
    }

    [Fact]
    public void Plan_SameWorkbook_ZeroGeneration_NoEdits_MarksSaved()
    {
        var plan = AvaloniaSaveCompletionPlanner.Plan(
            generationAtSaveStart: 0,
            generationNow: 0,
            sameWorkbook: true);

        plan.MarkSaved.Should().BeTrue();
        plan.ApplyFileContext.Should().BeTrue();
    }

    // ── Same workbook, edits arrived mid-save ────────────────────────────────

    [Fact]
    public void Plan_SameWorkbook_EditsDuringSerialize_DoesNotMarkSaved()
    {
        var plan = AvaloniaSaveCompletionPlanner.Plan(
            generationAtSaveStart: 5,
            generationNow: 6,
            sameWorkbook: true);

        plan.MarkSaved.Should().BeFalse(
            "edits arrived after save started — clearing dirty would silently discard them");
    }

    [Fact]
    public void Plan_SameWorkbook_EditsDuringSerialize_StillAppliesFileContext()
    {
        // The file was written to disk; the path/name should be updated even though
        // we cannot mark the workbook as saved (it has pending edits).
        var plan = AvaloniaSaveCompletionPlanner.Plan(
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
        var plan = AvaloniaSaveCompletionPlanner.Plan(generationAtStart, generationNow, sameWorkbook: true);

        plan.MarkSaved.Should().BeFalse();
        plan.ApplyFileContext.Should().BeTrue();
    }

    // ── Workbook replaced during save ────────────────────────────────────────

    [Fact]
    public void Plan_DifferentWorkbook_NoEdits_DoesNotMarkSavedOrApplyContext()
    {
        var plan = AvaloniaSaveCompletionPlanner.Plan(
            generationAtSaveStart: 2,
            generationNow: 2,
            sameWorkbook: false);

        plan.MarkSaved.Should().BeFalse();
        plan.ApplyFileContext.Should().BeFalse();
    }

    [Fact]
    public void Plan_DifferentWorkbook_WithEdits_DoesNotMarkSavedOrApplyContext()
    {
        var plan = AvaloniaSaveCompletionPlanner.Plan(
            generationAtSaveStart: 0,
            generationNow: 3,
            sameWorkbook: false);

        plan.MarkSaved.Should().BeFalse();
        plan.ApplyFileContext.Should().BeFalse();
    }

    // ── Record equality ──────────────────────────────────────────────────────

    [Fact]
    public void AvaloniaSaveCompletionPlan_RecordEquality_WorksByValue()
    {
        var a = new AvaloniaSaveCompletionPlan(MarkSaved: true, ApplyFileContext: true);
        var b = new AvaloniaSaveCompletionPlan(MarkSaved: true, ApplyFileContext: true);
        var c = new AvaloniaSaveCompletionPlan(MarkSaved: false, ApplyFileContext: true);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
