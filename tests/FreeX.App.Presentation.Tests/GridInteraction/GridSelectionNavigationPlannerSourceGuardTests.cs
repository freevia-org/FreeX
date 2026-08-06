using FluentAssertions;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class GridSelectionNavigationPlannerSourceGuardTests
{
    [Fact]
    public void SelectionNavigationPolicy_HasOnePresentationOwnerAndThinRenderers()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
        var plannerPath = Path.Combine(
            presentationRoot,
            "GridInteraction",
            "GridSelectionNavigationPlanner.cs");
        File.Exists(plannerPath).Should().BeTrue();

        var wpfSelection = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "MainWindow.Selection.cs"));
        wpfSelection.Should().Contain("GridSelectionNavigationPlanner.PlanCycle(");
        wpfSelection.Should().Contain("GridSelectionNavigationPlanner.UpdateDisjointSelectionAreas(");
        wpfSelection.Should().Contain("GridSelectionNavigationPlanner.FormatDragDimensionText(");
        wpfSelection.Should().Contain("StructuredTableSelectionPlanner.PlanWholeColumns(");
        wpfSelection.Should().Contain("StructuredTableSelectionPlanner.PlanWholeRows(");
        wpfSelection.Should().NotContain("private static CellAddress AdvanceActiveCellWithinRange(");
        wpfSelection.Should().NotContain("private static bool IsSingleMergedCellRange(");
        wpfSelection.Should().NotContain("private sealed class MutableSelectionRanges");
        wpfSelection.Should().NotContain("private static string FormatDragSelectionDimensionText(");
        wpfSelection.Should().NotContain("TryGetTableForSelection(");

        var avaloniaMain = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));
        avaloniaMain.Should().Contain("GridSelectionNavigationPlanner.PlanCycle(");
        avaloniaMain.Should().Contain("GridSelectionNavigationPlanner.AppendDisjointSelectionArea(");
        avaloniaMain.Should().Contain("GridSelectionNavigationPlanner.FormatDragDimensionText(");
        avaloniaMain.Should().Contain("StructuredTableSelectionPlanner.PlanWholeColumns(");
        avaloniaMain.Should().Contain("StructuredTableSelectionPlanner.PlanWholeRows(");
        avaloniaMain.Should().Contain("StructuredTableSelectionPlanner.TryResolveDataBodyRange(");
        avaloniaMain.Should().Contain("StructuredTableSelectionPlanner.ContainsTableName(");
        avaloniaMain.Should().NotContain("_session.SelectedRanges.Count <= 1");
        avaloniaMain.Should().NotContain("private static CellAddress AdvanceActiveCellWithinRange(");
        avaloniaMain.Should().NotContain("private static bool IsSingleMergedCellRange(");
        avaloniaMain.Should().NotContain("private static string FormatDragSelectionDimensionText(");
        avaloniaMain.Should().NotContain("TryFindStructuredTableDataBodyRange(");

        var wpfEditing = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "MainWindow.Editing.cs"));
        wpfEditing.Should().Contain("StructuredTableSelectionPlanner.TryResolveDataBodyRange(");
        wpfEditing.Should().Contain("StructuredTableSelectionPlanner.ContainsTableName(");
        wpfEditing.Should().NotContain("TryFindStructuredTableDataBodyRange(");

        var avaloniaHeaders = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.RowColumnVisibility.cs"));
        avaloniaHeaders.Should().Contain("GridSelectionNavigationPlanner.CreateWholeRowsRange(");
        avaloniaHeaders.Should().Contain("GridSelectionNavigationPlanner.CreateWholeColumnsRange(");
        avaloniaHeaders.Should().Contain("GridSelectionNavigationPlanner.AppendDisjointSelectionArea(");
        avaloniaHeaders.Should().NotContain("new List<GridRange>(_session.SelectedRanges) { newRange }");
    }
}
