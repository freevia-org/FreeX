using FluentAssertions;
using FreeX.App.Presentation.Tests;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisSourceGuardTests
{
    [Fact]
    public void QuickAnalysisPresentationPlanners_DoNotReferencePlatformUiAssemblies()
    {
        var directory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "QuickAnalysis");

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
    public void QuickAnalysisRendererShells_StayAtNativeControlBoundary()
    {
        var hostSource = ReadSource("src", "FreeX.App.Host", "MainWindow.QuickAnalysis.cs");
        var avaloniaSource = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.QuickAnalysis.cs");
        var hostIconFactorySource = ReadSource("src", "FreeX.App.Host", "QuickAnalysisPreviewIconFactory.cs");
        var avaloniaIconFactorySource = ReadSource("src", "FreeX.App.Avalonia", "QuickAnalysisPreviewIconFactory.cs");
        var shellSources = string.Join(Environment.NewLine, hostSource, avaloniaSource);
        var iconFactorySources = string.Join(Environment.NewLine, hostIconFactorySource, avaloniaIconFactorySource);

        AssertShellUsesSharedQuickAnalysisPlanning(hostSource);
        AssertShellUsesSharedQuickAnalysisPlanning(avaloniaSource);
        avaloniaSource.Should().Contain("QuickAnalysisShellCapabilities.DialogBacked");
        avaloniaSource.Should().Contain("QuickAnalysisHostOperationKind.OpenConditionalFormatDialog");
        avaloniaSource.Should().Contain("QuickAnalysisHostOperationKind.ClearConditionalFormatting");
        avaloniaSource.Should().Contain("QuickAnalysisHostOperationKind.OpenChartPicker");
        avaloniaSource.Should().Contain("QuickAnalysisHostOperationKind.InsertPercentTotalFormula");
        avaloniaSource.Should().Contain("QuickAnalysisHostOperationKind.InsertRunningTotalFormula");
        avaloniaSource.Should().Contain("QuickAnalysisHostOperationKind.CreatePivotTable");
        avaloniaSource.Should().Contain("try");
        avaloniaSource.Should().Contain("catch (Exception exception)");
        avaloniaSource.Should().Contain("ShowEditIssue(exception.Message)");
        avaloniaSource.Should().Contain("QuickAnalysisConditionalFormatDialogPlanner.Plan(command)");
        avaloniaSource.Should().Contain("ShowConditionalFormatRuleEditorAsync(seed)");
        shellSources.Should().NotContain("QuickAnalysisSelectionReader.Describe(sheet, range)");
        shellSources.Should().NotContain("QuickAnalysisModelBuilder.Build(description).ToDisplayModel()");
        shellSources.Should().NotContain("QuickAnalysisPlanner.BuildDisplayModel(");
        shellSources.Should().NotContain("QuickAnalysisShellActionPlanner.Plan(item, QuickAnalysisShellCapabilities");
        shellSources.Should().NotContain("openPlan.Decision == QuickAnalysisShellOpenDecision");
        shellSources.Should().NotContain("QuickAnalysisGroup.Formatting =>");
        shellSources.Should().NotContain("QuickAnalysisCommandKind.");
        shellSources.Should().NotContain("QuickAnalysisSelectionReader.Describe(");
        shellSources.Should().NotContain("QuickAnalysisSparklinePlanner.BuildCommands(");

        hostSource.Should().Contain("QuickAnalysisMenuPlacementPlanner.BuildAnchor(");
        hostSource.Should().NotContain("FindLastVisibleRowInSelection");
        hostSource.Should().NotContain("FindLastVisibleColumnInSelection");
        hostSource.Should().NotContain("QuickAnalysisPlanner.BuildHoverPreview(");

        iconFactorySources.Should().Contain("QuickAnalysisPreviewIconRenderPlanner.Render(visual, renderer)");
        iconFactorySources.Should().NotContain("QuickAnalysisPreviewIconPlanner.Plan(visual)");
        iconFactorySources.Should().NotContain("foreach (var element in plan.Elements)");
        iconFactorySources.Should().NotContain("switch (element)");
        iconFactorySources.Should().NotContain("switch (visual.Kind)");
        iconFactorySources.Should().NotContain("QuickAnalysisPreviewVisualKind.");
        iconFactorySources.Should().NotContain("QuickAnalysisPreviewIconGlyph.");
    }

    private static void AssertShellUsesSharedQuickAnalysisPlanning(string source)
    {
        source.Should().Contain("QuickAnalysisShellRequestPlanner.Build(");
        source.Should().Contain("QuickAnalysisShellOpenPlanner.Plan(request)");
        source.Should().Contain("QuickAnalysisHostOperationPlanner.Plan(item)");
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryFileLocator.FindDirectory(parts[0]) }.Concat(parts[1..]).ToArray()));
}
