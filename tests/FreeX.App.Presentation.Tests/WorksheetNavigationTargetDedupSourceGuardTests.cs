using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Guards the single owner of the keyboard "key -> target cell" decision
/// (<c>ExcelWorksheetNavigationPlanner.ResolveTarget</c>) and of the edit-commit cycle-or-fall-back
/// decision (<c>GridSelectionNavigationPlanner.PlanEditCommitMove</c>). Both shells must stay thin:
/// native key conversion, request construction, and applying the result.
/// </summary>
public sealed class WorksheetNavigationTargetDedupSourceGuardTests
{
    [Fact]
    public void BothHosts_CallTheSharedResolveTarget_AndKeepNoLocalPerKeyTargetSwitch()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var presentation = Read(repoRoot, "src", "FreeX.App.Presentation", "ExcelWorksheetNavigationPlanner.cs");
        var wpfAdapter = Read(repoRoot, "src", "FreeX.App.Host", "ExcelWorksheetNavigationPlanner.cs");
        var wpf = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.Selection.cs");
        var avalonia = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs");

        presentation.Should().Contain("public static ExcelWorksheetNavigationResolution? ResolveTarget(");
        wpfAdapter.Should().Contain("FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.ResolveTarget(");
        wpf.Should().Contain("ExcelWorksheetNavigationPlanner.ResolveTarget(");
        avalonia.Should().Contain("ExcelWorksheetNavigationPlanner.ResolveTarget(");

        // WPF: no hand-written Key.X => target arms, no direct use of the pieces ResolveTarget owns.
        wpf.Should().NotContain("Key.PageDown =>");
        wpf.Should().NotContain("Key.PageUp   =>");
        wpf.Should().NotContain("Key.Home     =>");
        wpf.Should().NotContain("Key.Enter =>");
        wpf.Should().NotContain("GetHomeNavigationTarget(");
        wpf.Should().NotContain("ExcelEditKeyPlanner.GetEnterTarget(");
        wpf.Should().NotContain("ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(");
        wpf.Should().NotContain("ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(");
        wpf.Should().NotContain("ExcelWorksheetNavigationPlanner.AdjustTargetPastMerge(");

        // Avalonia: same, and the hard-coded one-row Enter/Tab arms are gone.
        avalonia.Should().NotContain("ExcelWorksheetNavigationKey.PageUp =>");
        avalonia.Should().NotContain("ExcelWorksheetNavigationKey.PageDown =>");
        avalonia.Should().NotContain("ExcelWorksheetNavigationKey.Enter =>");
        avalonia.Should().NotContain("ExcelWorksheetNavigationKey.Tab =>");
        avalonia.Should().NotContain("ExcelWorksheetNavigationKey.Home =>");
        avalonia.Should().NotContain("ExcelWorksheetNavigationKey.Up =>");
        avalonia.Should().NotContain("OffsetAddress(");
        avalonia.Should().NotContain("ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(");
        avalonia.Should().NotContain("ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(");
        avalonia.Should().NotContain("ExcelWorksheetNavigationPlanner.GetHorizontalPageTarget(");
    }

    [Fact]
    public void BothHosts_RouteEditCommitMovesThroughPlanEditCommitMove()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var planner = Read(repoRoot, "src", "FreeX.App.Presentation", "GridInteraction", "GridSelectionNavigationPlanner.cs");
        var wpfEditing = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.Editing.cs");
        var avalonia = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs");

        planner.Should().Contain("public static GridSelectionEditCommitMove PlanEditCommitMove(");
        wpfEditing.Should().Contain("GridSelectionNavigationPlanner.PlanEditCommitMove(");
        wpfEditing.Should().NotContain("GridSelectionNavigationPlanner.PlanCycle(");
        avalonia.Should().Contain("GridSelectionNavigationPlanner.PlanEditCommitMove(");

        // Avalonia keeps exactly one direct PlanCycle call: the ready-mode Enter/Tab path in
        // NavigateActiveCell. The edit-commit copy must not return.
        CountOccurrences(avalonia, "GridSelectionNavigationPlanner.PlanCycle(").Should().Be(1);

        // The R139 rationale lives once, on the planner, not duplicated on each host.
        CountOccurrences(wpfEditing, "must CYCLE the active cell within a") .Should().Be(0);
        CountOccurrences(avalonia, "must CYCLE the active cell within a").Should().Be(0);
        CountOccurrences(planner, "must CYCLE the active cell within a").Should().Be(1);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));
}
