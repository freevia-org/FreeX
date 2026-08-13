using FluentAssertions;
using FreeX.App.Presentation.Tests;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisSourceGuardTests
{
    [Fact]
    public void QuickAnalysisPresentationPlanners_DoNotReferencePlatformUiAssemblies()
    {
        var directory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "QuickAnalysis");
        var sources = Directory.EnumerateFiles(directory, "*.cs")
            .Append(Path.Combine(
                RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "Services"),
                "QuickAnalysisShellSession.cs"));

        foreach (var file in sources)
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
        var sessionSource = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "Services",
            "QuickAnalysisShellSession.cs");
        var shellPlannerSource = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "QuickAnalysis",
            "QuickAnalysisShellPlanner.cs");
        var requestPlannerSource = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "QuickAnalysis",
            "QuickAnalysisShellRequestPlanner.cs");
        var selectionInterpreterSource = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "QuickAnalysis",
            "QuickAnalysisSelectionInterpreter.cs");
        var selectionReaderSource = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "QuickAnalysis",
            "QuickAnalysisSelectionReader.cs");
        var modelBuilderSource = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "QuickAnalysis",
            "QuickAnalysisModelBuilder.cs");
        var hostOperationSource = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "QuickAnalysis",
            "QuickAnalysisHostOperationPlanner.cs");
        var conditionalFormatCatalogSource = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "QuickAnalysis",
            "QuickAnalysisConditionalFormatCatalog.cs");
        var conditionalFormatPresetSource = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "QuickAnalysis",
            "QuickAnalysisConditionalFormatPresetPlanner.cs");
        var shellSources = string.Join(Environment.NewLine, hostSource, avaloniaSource);
        var iconFactorySources = string.Join(Environment.NewLine, hostIconFactorySource, avaloniaIconFactorySource);

        AssertShellUsesSharedQuickAnalysisSession(hostSource);
        AssertShellUsesSharedQuickAnalysisSession(avaloniaSource);
        avaloniaSource.Should().Contain("QuickAnalysisShellCapabilities.DialogBacked");
        avaloniaSource.Should().Contain("CreateQuickAnalysisOperationHandlers()");
        avaloniaSource.Should().Contain("_session.ExecuteQuickAnalysisTotal(operation)");
        avaloniaSource.Should().Contain("_session.ExecuteQuickAnalysisSparklines(operation)");
        avaloniaSource.Should().Contain("try");
        avaloniaSource.Should().Contain("catch (Exception exception)");
        avaloniaSource.Should().Contain("ShowEditIssue(exception.Message)");
        hostSource.Should().Contain("ShowCfDialog(dialogPlan.Title)");
        avaloniaSource.Should().Contain("dialogPlan.Seed");
        avaloniaSource.Should().Contain("CreateQuickAnalysisItemButton(flyout, item)");
        avaloniaSource.Should().Contain("ApplyQuickAnalysisItemAsync(item)");
        avaloniaSource.Replace("\r\n", "\n", StringComparison.Ordinal).Should().Contain(
            "var built = await ShowConditionalFormatRuleEditorAsync(dialogPlan.Seed);");
        shellSources.Should().NotContain("QuickAnalysisConditionalFormatDialogPlanner.Plan(");
        avaloniaSource.Should().Contain("ConditionalFormatCommandPlanner.PlanApplyRule(");
        avaloniaSource.Should().Contain("_session.GetCurrentGroupedEditSheetIds()");
        avaloniaSource.Should().Contain("ResolveConditionalFormatSelectionRanges(built.AppliesTo)");
        shellSources.Should().NotContain("QuickAnalysisSelectionReader.Describe(sheet, range)");
        shellSources.Should().NotContain("QuickAnalysisModelBuilder.Build(description).ToDisplayModel()");
        shellSources.Should().NotContain("QuickAnalysisPlanner.BuildDisplayModel(");
        shellSources.Should().NotContain("QuickAnalysisShellActionPlanner.Plan(item, QuickAnalysisShellCapabilities");
        shellSources.Should().NotContain("openPlan.Decision == QuickAnalysisShellOpenDecision");
        shellSources.Should().NotContain("QuickAnalysisGroup.Formatting =>");
        shellSources.Should().NotContain("QuickAnalysisCommandKind.");
        shellSources.Should().NotContain("QuickAnalysisSelectionReader.Describe(");
        shellSources.Should().NotContain("QuickAnalysisSparklinePlanner.BuildCommands(");
        shellSources.Should().NotContain("item.HoverPreview");
        shellSources.Should().NotContain("item.PreviewVisual");
        shellSources.Should().NotContain("QuickAnalysisHostOperationPlanner.Plan(item)");
        shellSources.Should().NotContain("switch (operation.Kind)");
        shellSources.Should().NotContain("QuickAnalysisHostOperationKind.");
        shellSources.Should().NotContain("QuickAnalysisHostOperationPlanner.TryBuildTotalFormulaEdits(");
        shellSources.Should().NotContain("QuickAnalysisHostOperationPlanner.TryBuildSparklineCommands(");
        shellSources.Should().NotContain("new EditCellsCommand(");
        shellSources.Should().NotContain("QuickAnalysisShellRequestPlanner.Build(");
        shellSources.Should().NotContain("QuickAnalysisShellOpenPlanner.Plan(request)");
        shellSources.Should().NotContain("StructuredTables");
        shellSources.Should().NotContain("MaximumAnalyzedCellCount");

        sessionSource.Should().Contain("QuickAnalysisShellRequestPlanner.Build(sheet, selection, capabilities)");
        sessionSource.Should().Contain("QuickAnalysisShellOpenPlanner.Plan(request)");
        sessionSource.Should().Contain("QuickAnalysisHostOperationPlanner.Plan(item)");
        sessionSource.Should().Contain("QuickAnalysisOperationExecutor.ExecuteAsync(operation, handlers)");
        sessionSource.Should().Contain("var preview = item.HoverPreview");
        hostOperationSource.Should().Contain("public sealed record QuickAnalysisConditionalFormatDialogPlan(");
        hostOperationSource.Should().Contain("QuickAnalysisConditionalFormatCommand Command");
        hostOperationSource.Should().Contain("QuickAnalysisConditionalFormatDialogSeed Seed");
        hostOperationSource.Should().Contain("QuickAnalysisConditionalFormatCatalog.ForCommand(command)");
        conditionalFormatPresetSource.Should().Contain("QuickAnalysisConditionalFormatCatalog.TryForCommand(command");
        conditionalFormatCatalogSource.Should().Contain("internal sealed record QuickAnalysisConditionalFormatDescriptor(");
        conditionalFormatCatalogSource.Should().Contain("QuickAnalysisFormatKind FormatKind");
        conditionalFormatCatalogSource.Should().Contain("ConditionalFormatPreset Preset");
        conditionalFormatCatalogSource.Should().Contain("QuickAnalysisConditionalFormatDialogSeed DialogSeed");
        conditionalFormatPresetSource.Should().NotContain("command switch");
        hostOperationSource.Should().NotContain("command switch");
        shellPlannerSource.Should().Contain("QuickAnalysisPreviewIconPlan PreviewIcon");
        shellPlannerSource.Should().NotContain("QuickAnalysisDisplayItem DisplayItem");
        requestPlannerSource.Should().Contain("QuickAnalysisSelectionInterpreter.Interpret(sheet, range)");
        requestPlannerSource.Should().NotContain("range.CellCount");
        selectionInterpreterSource.Should().Contain("MaximumAnalyzedCellCount");
        selectionInterpreterSource.Should().Contain("QuickAnalysisSelectionReader.Describe(sheet, selection)");
        selectionReaderSource.Should().Contain("StructuredTableSelectionPlanner.Describe(sheet, range)");
        modelBuilderSource.Should().Contain("selection.OverlapsStructuredTable");
        modelBuilderSource.Should().Contain("selection.CanWriteAdjacentColumn");

        hostSource.Should().Contain("QuickAnalysisMenuPlacementPlanner.BuildAnchor(");
        hostSource.Should().NotContain("FindLastVisibleRowInSelection");
        hostSource.Should().NotContain("FindLastVisibleColumnInSelection");
        hostSource.Should().NotContain("QuickAnalysisPlanner.BuildHoverPreview(");

        iconFactorySources.Should().Contain("QuickAnalysisPreviewIconRenderAdapter<Canvas,");
        iconFactorySources.Should().Contain(".Render(");
        iconFactorySources.Should().Contain("QuickAnalysisPreviewIconPlan plan");
        iconFactorySources.Should().NotContain("QuickAnalysisPreviewVisual visual");
        iconFactorySources.Should().NotContain("QuickAnalysisPreviewIconRenderPlanner.Render(");
        iconFactorySources.Should().NotContain("QuickAnalysisPreviewIconPlanner.Plan(visual)");
        iconFactorySources.Should().NotContain("foreach (var element in plan.Elements)");
        iconFactorySources.Should().NotContain("switch (element)");
        iconFactorySources.Should().NotContain("switch (visual.Kind)");
        iconFactorySources.Should().NotContain("QuickAnalysisPreviewVisualKind.");
        iconFactorySources.Should().NotContain("QuickAnalysisPreviewIconGlyph.");
    }

    private static void AssertShellUsesSharedQuickAnalysisSession(string source)
    {
        source.Should().Contain("private readonly QuickAnalysisShellSession _quickAnalysisSession = new();");
        source.Should().Contain("_quickAnalysisSession.PlanOpen(");
        source.Should().Contain("_quickAnalysisSession.ExecuteSelectionAsync(");
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryFileLocator.FindDirectory(parts[0]) }.Concat(parts[1..]).ToArray()));
}
