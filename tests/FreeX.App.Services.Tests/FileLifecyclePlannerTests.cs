using FluentAssertions;
using Free.Shared.IO;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Pure unit coverage for the shared <see cref="FileLifecyclePlanner"/> — the neutral file-lifecycle
/// ceremony (dirty-gate, Save-vs-Save-As resolution, recent registration) extracted in P2 and adopted
/// by FreeW. No WPF, no dialogs, no I/O.
/// </summary>
public sealed class FileLifecyclePlannerTests
{
    // ── Dirty-gate (New / Open / Close) ──────────────────────────────────────

    [Fact]
    public void PlanDirtyGate_CleanDocument_ProceedsWithoutPrompt()
    {
        FileLifecyclePlanner.PlanDirtyGate(isDirty: false)
            .Should().Be(DirtyGateIntent.ProceedWithoutPrompt);
    }

    [Fact]
    public void PlanDirtyGate_DirtyDocument_PromptsToSave()
    {
        FileLifecyclePlanner.PlanDirtyGate(isDirty: true)
            .Should().Be(DirtyGateIntent.PromptSaveChanges);
    }

    [Theory]
    [InlineData(SaveChangesPrompt.Save, DirtyGateAction.SaveThenProceed)]
    [InlineData(SaveChangesPrompt.DontSave, DirtyGateAction.ProceedDiscardingChanges)]
    [InlineData(SaveChangesPrompt.Cancel, DirtyGateAction.Cancel)]
    public void ResolveDirtyGate_MapsEachPromptAnswerToAction(
        SaveChangesPrompt answer,
        DirtyGateAction expected)
    {
        FileLifecyclePlanner.ResolveDirtyGate(answer).Should().Be(expected);
    }

    [Fact]
    public void ResolveDirtyGate_Cancel_AbortsTheDestructiveAction()
    {
        // The Cancel path must never proceed — guards the New/Open/Close "Cancel aborts" requirement.
        FileLifecyclePlanner.ResolveDirtyGate(SaveChangesPrompt.Cancel)
            .Should().Be(DirtyGateAction.Cancel);
    }

    // ── Save vs Save-As resolution ───────────────────────────────────────────

    [Fact]
    public void PlanSave_DirtyWithExistingPath_UsesExistingPath()
    {
        FileLifecyclePlanner.PlanSave(isDirty: true, currentFilePath: @"C:\Docs\Letter.docx")
            .Should().Be(FileSaveIntent.UseExistingPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PlanSave_NeverSavedDocument_FallsThroughToSaveAs(string? currentFilePath)
    {
        // No usable path → always prompt Save-As, even though dirty.
        FileLifecyclePlanner.PlanSave(isDirty: true, currentFilePath)
            .Should().Be(FileSaveIntent.PromptSaveAs);
    }

    [Fact]
    public void PlanSave_CleanNeverSavedDocument_StillPromptsSaveAs()
    {
        FileLifecyclePlanner.PlanSave(isDirty: false, currentFilePath: null)
            .Should().Be(FileSaveIntent.PromptSaveAs);
    }

    [Fact]
    public void PlanSave_CleanWithExistingPath_IsNoOp()
    {
        FileLifecyclePlanner.PlanSave(isDirty: false, currentFilePath: @"C:\Docs\Letter.docx")
            .Should().Be(FileSaveIntent.NothingToDo);
    }

    // ── Recent-files registration ────────────────────────────────────────────

    [Fact]
    public void PlanRecentRegistration_NormalSave_Registers()
    {
        FileLifecyclePlanner.PlanRecentRegistration(@"C:\Docs\Letter.docx", suppressRecentFiles: false)
            .Should().Be(RecentFileRegistration.Register);
    }

    [Fact]
    public void PlanRecentRegistration_Suppressed_Skips()
    {
        // Recovery snapshots / transient template paths must not pollute the MRU.
        FileLifecyclePlanner.PlanRecentRegistration(@"C:\Temp\recovery.docx", suppressRecentFiles: true)
            .Should().Be(RecentFileRegistration.Skip);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PlanRecentRegistration_BlankPath_Skips(string? path)
    {
        FileLifecyclePlanner.PlanRecentRegistration(path, suppressRecentFiles: false)
            .Should().Be(RecentFileRegistration.Skip);
    }

    // ── End-to-end ceremony: the host's Save-before-close flow ───────────────

    [Fact]
    public void Ceremony_DirtyDocumentWithPath_SaveAnswer_ResolvesToSaveExistingThenProceed()
    {
        // Simulate the host wiring: a dirty document with a path, user clicks Save on the close gate.
        const string path = @"C:\Docs\Letter.docx";

        FileLifecyclePlanner.PlanDirtyGate(isDirty: true).Should().Be(DirtyGateIntent.PromptSaveChanges);

        var action = FileLifecyclePlanner.ResolveDirtyGate(SaveChangesPrompt.Save);
        action.Should().Be(DirtyGateAction.SaveThenProceed);

        // The save the host now runs resolves to writing to the existing path (no Save-As dialog).
        FileLifecyclePlanner.PlanSave(isDirty: true, path).Should().Be(FileSaveIntent.UseExistingPath);
    }

    [Fact]
    public void Ceremony_DirtyUntitledDocument_SaveAnswer_ResolvesToSaveAs()
    {
        FileLifecyclePlanner.PlanDirtyGate(isDirty: true).Should().Be(DirtyGateIntent.PromptSaveChanges);
        FileLifecyclePlanner.ResolveDirtyGate(SaveChangesPrompt.Save).Should().Be(DirtyGateAction.SaveThenProceed);
        FileLifecyclePlanner.PlanSave(isDirty: true, currentFilePath: null).Should().Be(FileSaveIntent.PromptSaveAs);
    }
}

/// <summary>Coverage for the neutral dialog request/result records used by the host seam.</summary>
public sealed class FileDialogResultTests
{
    [Fact]
    public void Cancelled_HasNoPathAndIsNotChosen()
    {
        FileDialogResult.Cancelled.Path.Should().BeNull();
        FileDialogResult.Cancelled.Chosen.Should().BeFalse();
    }

    [Fact]
    public void Chosen_TrueOnlyForNonBlankPath()
    {
        new FileDialogResult(@"C:\Docs\Letter.docx").Chosen.Should().BeTrue();
        new FileDialogResult("   ").Chosen.Should().BeFalse();
        new FileDialogResult(null).Chosen.Should().BeFalse();
    }
}
