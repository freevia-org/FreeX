namespace FreeW.App.Presentation.Tests;

public sealed class ChartInsertionOwnershipSourceTests
{
    [Fact]
    public void NativeDocumentViewsDelegateChartDefaultsToThePortableCoordinator()
    {
        var coordinator = ReadSource(
            "freew", "FreeW.App.Presentation", "Editing", "DocumentObjectEditingCoordinator.cs");
        var catalog = ReadSource(
            "freew", "FreeW.App.Presentation", "Editing", "ChartDataPresetCatalog.cs");
        var dialog = ReadSource(
            "freew", "FreeW.App.Presentation", "Dialogs", "ChartDialogPlanners.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var avaloniaRibbon = ReadSource(
            "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");
        var editorProfile = ReadSource(
            "freew", "FreeW.App.Presentation", "Ribbon", "FreeWRibbonEditorExecutionProfile.cs");

        coordinator.Should().Contain("public static Chart PlanChartInsertion(Chart? chart = null)");
        coordinator.Should().Contain("ChartDataPresetCatalog.CreateDefaultInsertion()");
        catalog.Should().Contain("public static class ChartDataPresetCatalog");
        catalog.Should().Contain("public static bool TryCreateNamedReplacement(");
        dialog.Should().Contain("ChartDataPresetCatalog.CreateDefaultInsertion()");
        dialog.Should().NotContain("private static readonly string[] DefaultCategories");

        wpf.Should().Contain("DocumentObjectEditingCoordinator.PlanChartInsertion(chart)");
        avalonia.Should().Contain("DocumentObjectEditingCoordinator.PlanChartInsertion(chart)");
        editorProfile.Should().Contain("ChartDataPresetCatalog.TryCreateNamedReplacement(");
        avaloniaRibbon.Should().NotContain("ChartDataPresetCatalog.TryCreateNamedReplacement(");
        avaloniaRibbon.Should().NotContain("TryBuildChartDataPreset");
        avaloniaRibbon.Should().NotContain("Quarterly Sales");
        avaloniaRibbon.Should().NotContain("Monthly Revenue");
        wpf.Should().NotContain("public void InsertChart(Chart chart)");
        avalonia.Should().NotContain("chart ?? Chart.Create(");
        avalonia.Should().NotContain("[\"Q1\", \"Q2\", \"Q3\", \"Q4\"]");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
