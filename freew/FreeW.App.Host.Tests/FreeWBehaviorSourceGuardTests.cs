using System.IO;
using FluentAssertions;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class FreeWBehaviorSourceGuardTests
{
    [Fact]
    public void ChartHosts_ConsumeSharedChartScene()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        wpf.Should().Contain("ChartSmartArtVisualPlanner.BuildChartScene(chart, widthPx, heightPx)");
        avalonia.Should().Contain("ChartSmartArtVisualPlanner.BuildChartScene(chart, rect.Width, rect.Height)");
        wpf.Should().Contain("ChartSmartArtVisualPlanner.BuildChartElementCommandState(chart)");
        avalonia.Should().Contain("ChartSmartArtVisualPlanner.BuildChartElementCommandState(chart)");
        avalonia.Should().NotContain("ComputeAxisRange");
        wpf.Should().NotContain("ComputeAxisRange");
    }

    [Fact]
    public void WpfOptionsDialog_ConsumesSharedSurfaceSpecification()
    {
        var source = ReadSource("freew", "FreeW.App.Host", "OptionsDialog.cs");

        source.Should().Contain("OptionsDialogPlanner.BuildSurface(");
        source.Should().Contain("_surface.General");
        source.Should().Contain("_surface.AutoFormat");
        source.Should().Contain("_surface.AutoCorrect");
        source.Should().NotContain("AddRow(grid, 0, \"Recent files\"");
        source.Should().NotContain("AddRow(grid, 2, \"Default save format\"");
    }

    [Fact]
    public void FindReplaceHosts_DelegateExecutionToSharedOptionAwarePlanner()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "FindReplaceDialog.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "FindReplaceDialog.cs");

        wpf.Should().Contain("FindReplaceDialogPlanner.FindAll(");
        wpf.Should().Contain("FindReplaceDialogPlanner.MatchesExactly(");
        wpf.Should().Contain("CurrentOptions");
        wpf.Should().Contain("var found = SelectFrom(start, searchRequest)");
        wpf.Should().Contain("FindReplaceDialogPlanner.BuildReplaceStatus(request!, found)");
        wpf.Should().NotContain("BuildReplaceStatus(request!, replaced)");
        avalonia.Should().Contain("_editor.FindNext(request.Term, request.Options)");
        avalonia.Should().Contain("_editor.ReplaceNext(request!.Term, request.Replacement, request.Options)");
        avalonia.Should().Contain("_editor.ReplaceAll(request!.Term, request.Replacement, request.Options)");
        wpf.Should().NotContain("TextSearch.FindAll(");
        avalonia.Should().NotContain("TextSearch.FindAll(");

        var planner = ReadSource("freew", "FreeW.App.Presentation", "Dialogs", "FindReplaceDialogPlanner.cs");
        planner.Should().Contain(".Any(match => match.Start == 0 && match.Length == text.Length)");
    }

    [Fact]
    public void AutosaveHosts_UseSharedDispositionPolicy()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "AutosaveCoordinator.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "AutosaveAdapter.cs");

        wpf.Should().Contain("AutosaveRecoveryPlanner.SelectLatest(");
        wpf.Should().Contain("AutosaveRecoveryPlanner.ResolveDisposition(");
        avalonia.Should().Contain("AutosaveRecoveryPlanner.SelectLatest(");
        avalonia.Should().Contain("AutosaveRecoveryPlanner.ResolveDisposition(");
        avalonia.Should().NotContain("private static AutosaveRecoveryCandidate? SelectLatest");
        avalonia.Should().NotContain("CandidateDisplayName");
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
