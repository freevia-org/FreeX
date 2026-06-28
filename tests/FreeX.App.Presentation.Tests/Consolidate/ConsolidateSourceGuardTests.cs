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
            source.Should().NotContain("Avalonia");
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
        source.Should().Contain("ConsolidateDialogPlanner.TryPlanApply(");
        source.Should().Contain("new ConsolidateCommand(");
        source.Should().NotContain("ConsolidateShellPlanner");
        source.Should().NotContain("new EditCellsCommand(sheetId, edits)");
    }
}
