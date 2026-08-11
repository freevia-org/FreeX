using FluentAssertions;
using FreeX.App.Presentation.Tests;

namespace FreeX.App.Presentation.Tests.Consolidate;

public sealed class ConsolidateSourceGuardTests
{
    [Fact]
    public void ConsolidatePresentationPlanners_DoNotReferencePlatformUiAssemblies()
    {
        var directory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "Consolidate");

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
        {
            var source = File.ReadAllText(file);

            source.Should().NotContain("System.Windows");
            source.Should().NotContain("using Avalonia");
            source.Should().NotContain("FreeX.App.Host");
            source.Should().NotContain("FreeX.App.Avalonia");
        }
    }

    [Fact]
    public void AvaloniaConsolidateDialog_ConsumesSharedPresentationPlannersDirectly()
    {
        var repoRoot = RepositoryFileLocator.FindDirectory("src");
        var avaloniaRoot = Path.Combine(repoRoot, "FreeX.App.Avalonia");
        var source = File.ReadAllText(Path.Combine(avaloniaRoot, "MainWindow.Consolidate.cs"));

        File.Exists(Path.Combine(avaloniaRoot, "ConsolidateShellPlanner.cs"))
            .Should()
            .BeFalse("Avalonia should consume shared consolidate planners directly instead of keeping a pass-through facade");
        source.Should().Contain("ConsolidateDialogPlanner.FunctionChoices");
        source.Should().Contain("Width = ConsolidateDialogPlanner.CaptureWidth");
        source.Should().Contain("Height = ConsolidateDialogPlanner.CaptureHeight");
        source.Should().Contain("MinWidth = ConsolidateDialogPlanner.MinWidth");
        source.Should().Contain("MinHeight = ConsolidateDialogPlanner.ReferencesListHeight");
        source.Should().Contain("ConsolidateApplicationWorkflow.Plan(");
        source.Should().Contain("ConsolidateApplicationWorkflow.Execute(");
        source.Should().NotContain("new ConsolidateCommand(");
        source.Should().NotContain("ConsolidateShellPlanner");
        source.Should().NotContain("new EditCellsCommand(sheetId, edits)");
    }

    [Fact]
    public void WpfConsolidateDialog_ConsumesSharedPresentationPlannerDirectly()
    {
        var repoRoot = RepositoryFileLocator.FindDirectory("src");
        var hostRoot = Path.Combine(repoRoot, "FreeX.App.Host");
        var planningSource = File.ReadAllText(Path.Combine(hostRoot, "ConsolidateDialog.Planning.cs"));

        File.Exists(Path.Combine(hostRoot, "ConsolidateDialogPlanner.cs"))
            .Should()
            .BeFalse("WPF should consume the shared consolidate planner directly instead of keeping a pass-through facade");
        planningSource.Should().Contain(
            "SharedConsolidateDialogPlanner = FreeX.App.Presentation.Consolidate.ConsolidateDialogPlanner");
        var dialogSource = File.ReadAllText(Path.Combine(hostRoot, "ConsolidateDialog.cs"));

        dialogSource.Should().Contain("Width = ConsolidateDialogPlanner.WpfWindowWidth");
        dialogSource.Should().Contain("Height = ConsolidateDialogPlanner.ReferencesListHeight");
        planningSource.Should().Contain("SharedConsolidateDialogPlanner.TryAddReference(");
        planningSource.Should().Contain("SharedConsolidateDialogPlanner.TryParse(");
        planningSource.Should().Contain("ConsolidateDialogIssue");
        planningSource.Should().Contain("SharedConsolidateDialogPlanner.DescribeIssue(");
        planningSource.Should().NotContain("UiText.Get(\"Consolidate_EnterValidDestinationCell\")");

        var commandSource = File.ReadAllText(Path.Combine(hostRoot, "MainWindow.DataCommands.cs"));
        commandSource.Should().Contain("ConsolidateApplicationWorkflow.Plan(");
        commandSource.Should().Contain("ConsolidateApplicationWorkflow.Execute(");
        commandSource.Should().NotContain("new ConsolidateCommand(");
    }
}
