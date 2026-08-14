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
        var avaloniaCommandHost = ReadSource(
            "freew",
            "FreeW.App.Avalonia",
            "Editing",
            "AvaloniaFindReplaceCommandHost.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("new FindReplaceDialogSession(");
            source.Should().Contain("SyncSessionInput()");
            source.Should().Contain("private FindReplaceDialogInput ReadInput()");
            source.Should().Contain("_session.Execute(action, ReadInput())");
            source.Should().NotContain("_session.FindNext()");
            source.Should().NotContain("_session.ReplaceNext()");
            source.Should().NotContain("_session.ReplaceAll()");
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
        avaloniaCommandHost.Should().Contain("class AvaloniaFindReplaceCommandHost");
        avaloniaCommandHost.Should().Contain("editor.FindNext(request.Term, request.Options)");
        avaloniaCommandHost.Should().Contain("editor.ReplaceNext(request.Term, request.Replacement, request.Options)");
        avaloniaCommandHost.Should().Contain("editor.ReplaceAll(request.Term, request.Replacement, request.Options)");
        wpf.Should().NotContain("TextSearch.FindAll(");
        avaloniaCommandHost.Should().NotContain("TextSearch.FindAll(");

        var session = ReadSource("freew", "FreeW.App.Presentation", "Dialogs", "FindReplaceDialogSession.cs");
        session.Should().Contain("IFindReplaceDialogCommandHost");
        session.Should().Contain("public FindReplaceDialogState Execute(");
        session.Should().Contain("FindReplaceDialogPlanner.TryCreateSearchRequest(");
        session.Should().Contain("FindReplaceDialogPlanner.TryCreateReplaceRequest(");

        var planner = ReadSource("freew", "FreeW.App.Presentation", "Dialogs", "FindReplaceDialogPlanner.cs");
        planner.Should().Contain(".Any(match => match.Start == 0 && match.Length == text.Length)");
    }

    [Fact]
    public void AutosaveHosts_DelegateNeutralRecoveryWorkflowToPresentation()
    {
        var session = ReadSource("freew", "FreeW.App.Presentation", "Shell", "FreeWAutosaveSession.cs");
        var planner = ReadSource("freew", "FreeW.App.Presentation", "Shell", "AutosaveRecoveryPlanner.cs");
        var workflow = ReadSource("freew", "FreeW.App.Presentation", "Shell", "FreeWRecoveryWorkflow.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "AutosaveCoordinator.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "AutosaveAdapter.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("new FreeWAutosaveSession(");
            source.Should().Contain("_session.PlanRecoveries()");
            source.Should().Contain("FreeWRecoveryWorkflow.RunAsync(");
            source.Should().Contain("FreeWAutosaveSession.DefaultInterval");
            source.Should().NotContain("AutosaveSnapshotCoordinator");
            source.Should().NotContain("AutosaveRecoveryPlanner");
            source.Should().NotContain("AutosaveSnapshotStore");
            source.Should().NotContain("IAutosaveSnapshotSource");
            source.Should().NotContain("TimeSpan.FromSeconds(30)");
            source.Should().NotContain("DocxWriter");
        }

        var wpfStartupOffer = Between(wpf, "public bool OfferRecovery", "public bool RecoverUnsavedDocuments");
        var wpfManualOffer = wpf[wpf.IndexOf("public bool RecoverUnsavedDocuments", StringComparison.Ordinal)..];
        var avaloniaStartupOffer = Between(avalonia, "public async Task OfferRecoveryAsync", "public void Dispose");
        foreach (var nativeOffer in new[] { wpfStartupOffer, wpfManualOffer, avaloniaStartupOffer })
        {
            nativeOffer.Should().NotContain("for (var index = 0; index < recoveries.Count; index++)");
            nativeOffer.Should().NotContain("unsaved documents found");
        }

        session.Should().Contain("new AutosaveSnapshotCoordinator(");
        session.Should().Contain("AutosaveRecoveryPlanner.PlanAll(_store)");
        session.Should().Contain("AutosaveRecoveryPlanner.Complete(");
        planner.Should().Contain("SelectAllOrdered(store.ExcludeLiveOwned(store.EnumerateCandidates()))");
        workflow.Should().Contain("for (var index = 0; index < recoveries.Count; index++)");
        workflow.Should().Contain("var useCurrentWindow = !anyAccepted;");
        workflow.Should().Contain("FreeWRecoveryPromptMode.Manual");
        workflow.Should().Contain("unsaved documents found");
        session.Should().Contain("class SnapshotSource : IAutosaveSnapshotSource");
        session.Should().Contain("ExecuteWithDocument(document => DocxWriter.Write(document, snapshotPath))");
        session.Should().Contain("DocxReader.Read(snapshotPath)");

        wpf.Should().Contain("DialogMessageHelper.AskYesNo(");
        wpf.Should().Contain("_session.CompleteRecovery(");
        wpf.Should().Contain("_file.OpenSnapshot");
        wpf.Should().Contain("editor.CommitToModel()");
        avalonia.Should().Contain("RecoveryPromptDialog.ShowAsync(");
        avalonia.Should().Contain("Dispatcher.UIThread.InvokeAsync(");
        avalonia.Should().Contain("_session.CompleteDocumentRecovery(");
        avalonia.Should().Contain("_editor.LoadDocument(document)");
        avalonia.Should().NotContain("DocxReader");
        avalonia.Should().Contain("FreeWRecoveryRestoreExceptionPolicy.QuarantineCandidate");
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

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

}
