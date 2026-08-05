using System.IO;
using FluentAssertions;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class FreeWBehaviorSourceGuardTests
{
    [Fact]
    public void ChartHosts_ConsumeSharedSignedAxisPlan()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        wpf.Should().Contain("ChartValueAxisPlan ValueAxis");
        wpf.Should().Contain("settings.ValueAxis");
        wpf.Should().Contain("axis.ValueFraction");
        avalonia.Should().Contain("ChartValueAxisPlan ValueAxis");
        avalonia.Should().Contain("cd.ValueAxis");
        avalonia.Should().Contain("axis.ValueFraction");
        wpf.Should().NotContain("ChartMax");
        avalonia.Should().NotContain("ComputeAxisRange");
    }

    [Fact]
    public void WpfOptionsDialog_ConsumesSharedSurfaceSpecification()
    {
        var source = ReadSource("freew", "FreeW.App.Host", "OptionsDialog.cs");

        source.Should().Contain("new OptionsDialogSession(");
        source.Should().Contain("_surface = _session.Surface");
        source.Should().Contain("_surface.General");
        source.Should().Contain("_surface.AutoFormat");
        source.Should().Contain("_surface.AutoCorrect");
        source.Should().NotContain("OptionsDialogPlanner.BuildSurface(");
        source.Should().NotContain("AddRow(grid, 0, \"Recent files\"");
        source.Should().NotContain("AddRow(grid, 2, \"Default save format\"");
    }

    [Fact]
    public void FindReplaceHosts_DelegateWorkflowToSharedSessionAndKeepNativeTraversalLocal()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "FindReplaceDialog.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "FindReplaceDialog.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("new FindReplaceDialogSession(");
            source.Should().Contain("SyncSessionInput()");
            source.Should().Contain("_session.FindNext()");
            source.Should().Contain("_session.ReplaceNext()");
            source.Should().Contain("_session.ReplaceAll()");
            source.Should().NotContain("FindReplaceDialogPlanner.TryCreateSearchRequest(");
            source.Should().NotContain("FindReplaceDialogPlanner.TryCreateReplaceRequest(");
            source.Should().NotContain("FindReplaceDialogPlanner.BuildFindStatus(");
            source.Should().NotContain("FindReplaceDialogPlanner.BuildReplaceStatus(");
            source.Should().NotContain("FindReplaceDialogPlanner.BuildReplaceAllStatus(");
        }

        wpf.Should().Contain("class WpfFindReplaceCommandHost");
        wpf.Should().Contain("FindReplaceDialogPlanner.FindAll(");
        wpf.Should().Contain("FindReplaceDialogPlanner.MatchesExactly(");
        wpf.Should().Contain("TextPointer from");
        wpf.Should().Contain("restrictToSelection");
        avalonia.Should().Contain("class AvaloniaFindReplaceCommandHost");
        avalonia.Should().Contain("editor.FindNext(request.Term, request.Options)");
        avalonia.Should().Contain("editor.ReplaceNext(request.Term, request.Replacement, request.Options)");
        avalonia.Should().Contain("editor.ReplaceAll(request.Term, request.Replacement, request.Options)");
        wpf.Should().NotContain("TextSearch.FindAll(");
        avalonia.Should().NotContain("TextSearch.FindAll(");

        var session = ReadSource("freew", "FreeW.App.Presentation", "Dialogs", "FindReplaceDialogSession.cs");
        session.Should().Contain("IFindReplaceDialogCommandHost");
        session.Should().Contain("FindReplaceDialogPlanner.TryCreateSearchRequest(");
        session.Should().Contain("FindReplaceDialogPlanner.TryCreateReplaceRequest(");

        var planner = ReadSource("freew", "FreeW.App.Presentation", "Dialogs", "FindReplaceDialogPlanner.cs");
        planner.Should().Contain(".Any(match => match.Start == 0 && match.Length == text.Length)");
    }

    [Fact]
    public void AutosaveHosts_DelegateNeutralRecoveryWorkflowToPresentation()
    {
        var planner = ReadSource("freew", "FreeW.App.Presentation", "Shell", "AutosaveRecoveryPlanner.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "AutosaveCoordinator.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "AutosaveAdapter.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("AutosaveRecoveryPlanner.PlanLatest(_store)");
            source.Should().Contain("AutosaveRecoveryPlanner.Complete(");
            source.Should().NotContain("_store.EnumerateCandidates()");
            source.Should().NotContain("AutosaveRecoveryPlanner.SelectLatest(");
            source.Should().NotContain("AutosaveRecoveryPlanner.DisplayName(");
            source.Should().NotContain("AutosaveRecoveryPlanner.ResolveDisposition(");
            source.Should().NotContain("ApplyRecoveryDisposition");
            source.Should().NotContain("AutosaveSnapshotStore.DeleteCandidate(");
            source.Should().NotContain("AutosaveSnapshotStore.QuarantineCandidate(");
        }

        planner.Should().Contain("store.EnumerateCandidates()");
        planner.Should().Contain("AutosaveSnapshotStore.DeleteCandidate(candidate)");
        planner.Should().Contain("AutosaveSnapshotStore.QuarantineCandidate(candidate)");

        wpf.Should().Contain("DialogMessageHelper.AskYesNo(");
        wpf.Should().Contain("_file.OpenSnapshot(");
        avalonia.Should().Contain("RecoveryPromptDialog.ShowAsync(");
        avalonia.Should().Contain("Dispatcher.UIThread.InvokeAsync(");
    }

    [Fact]
    public void CustomDictionaryPersistence_IsNeutralAndRegistrationRemainsWpfLocal()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        File.Exists(Path.Combine(root, "freew", "FreeW.App.Presentation", "Proofing", "CustomDictionaryStore.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(root, "freew", "FreeW.App.Host", "CustomDictionaryStore.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(root, "freew", "FreeW.App.Avalonia", "CustomDictionaryStore.cs"))
            .Should().BeFalse();

        ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs")
            .Should().Contain("customDictionary.EnsurePersisted()");
        ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs")
            .Should().Contain("RegisterCustomDictionary");
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx") }.Concat(parts).ToArray()));

}
