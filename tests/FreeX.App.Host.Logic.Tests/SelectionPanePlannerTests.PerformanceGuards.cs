using FluentAssertions;
using FreeX.Core.Model;
using System.Diagnostics;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class SelectionPanePlannerTests
{
    [Fact]
    public void SelectionPaneDialog_PlannerAvoidsLinqScaffoldingInRepeatedStatePaths()
    {
        var source = DialogSourceTestSupport.ReadHostSources("SelectionPaneDialog.Planning.cs");

        var filterItems = SourceMethod(
            source,
            "public static IReadOnlyList<SelectionPaneDialogItemState> FilterItems",
            "public static SelectionPaneDialogReorderPlan? PlanMove");
        var createVisibilityChanges = SourceMethod(
            source,
            "public static IReadOnlyList<SelectionPaneVisibilityChange> CreateVisibilityChanges",
            "public static IReadOnlyList<SelectionPaneRenameChange> CreateRenameChanges");
        var createRenameChanges = SourceMethod(
            source,
            "public static IReadOnlyList<SelectionPaneRenameChange> CreateRenameChanges",
            "public static SelectionPaneDialogResult CreateResult");

        filterItems.Should().NotContain(".Where(");
        filterItems.Should().NotContain(".ToList(");
        createVisibilityChanges.Should().NotContain(".Where(");
        createVisibilityChanges.Should().NotContain(".Select(");
        createVisibilityChanges.Should().NotContain("states[item.Id]");
        createRenameChanges.Should().NotContain(".Where(");
        createRenameChanges.Should().NotContain(".Select(");
        createRenameChanges.Should().NotContain("names[item.Id]");
    }

    [BenchmarkFact]
    public void Benchmark_SelectionPaneDefaultFilter_AvoidsCopyAllocation()
    {
        const int itemCount = 10_000;
        var items = Enumerable.Range(0, itemCount)
            .Select(index => DialogState(SelectionPaneObjectKind.Picture, $"Picture {index}", isVisible: true))
            .ToArray();

        SelectionPaneDialogStatePlanner.FilterItems(items, "", "All").Should().BeSameAs(items);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 1_000; index++)
        {
            if (!ReferenceEquals(SelectionPaneDialogStatePlanner.FilterItems(items, "", "All"), items))
                throw new InvalidOperationException("Default Selection Pane filtering should return the source list.");
        }

        stopwatch.Stop();

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine(
            $"Selection pane default filter: {stopwatch.Elapsed.TotalMilliseconds:F2}ms, {allocated:N0} bytes for 1000 runs");

        allocated.Should().BeLessThan(200_000);
    }

    [Fact]
    public void SelectionPaneDialog_PlannerUsesIndexedLookupsForStateProjection()
    {
        var source = DialogSourceTestSupport.ReadHostSources("SelectionPaneDialog.Planning.cs");

        source.Should().Contain("private static IReadOnlyList<(Guid Id, bool IsVisible, string Name)> ToNamedCurrentStates");
        source.Should().Contain("TryGetValue(state.Id");
        source.Should().NotContain("originalItems.FirstOrDefault(item => item.Id == state.Id)");
        source.Should().NotContain("itemsById.ContainsKey(state.Id)");
    }

    [Fact]
    public void SelectionPaneDialog_PlannerConsolidatesDragReorderIndexLookups()
    {
        var hostSource = DialogSourceTestSupport.ReadHostSources("SelectionPaneDialog.Planning.cs");
        var serviceSource = WorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Services",
            "SelectionPanePlanner.cs");

        hostSource.Should().Contain("SharedSelectionPanePlanner.PlanDragReorder(");
        serviceSource.Should().Contain("private static (int DraggedIndex, int TargetIndex) FindDragIndexes");
        serviceSource.Should().Contain("var dragPlan = CreateDragMovePlan(items, draggedId, targetId, placement);");
        serviceSource.Should().NotContain("items.Select(item => (item.Kind, item.Id)).ToList()");
        serviceSource.Should().NotContain("var draggedIndex = FindIndex(items, draggedId);");
        serviceSource.Should().NotContain("var targetIndex = FindIndex(items, targetId);");
    }

    [Fact]
    public void SelectionPaneDialog_BuildItemsUsesPortablePlannerWithLocalizedText()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var hostPlannerPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "SelectionPanePlanner.cs");
        var dialogSource = DialogSourceTestSupport.ReadHostSources("SelectionPaneDialog.Planning.cs");
        var serviceSource = WorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Services",
            "SelectionPanePlanner.cs");

        File.Exists(hostPlannerPath)
            .Should()
            .BeFalse("the WPF Selection Pane should use the portable planner through the dialog edge");

        dialogSource.Should().Contain("SharedSelectionPanePlanner.BuildItems(sheet, CreateLocalizedPlannerText())");
        dialogSource.Should().Contain("UiText.Get(\"SelectionPane_DefaultChartName\")");
        dialogSource.Should().Contain("UiText.Get(\"SelectionPane_DefaultPictureName\")");
        dialogSource.Should().Contain("UiText.Get(\"SelectionPane_DefaultTextBoxName\")");
        dialogSource.Should().Contain("UiText.Get(\"SelectionPane_DefaultShapeNameFormat\")");
        dialogSource.Should().Contain("UiText.Get(\"SelectionPane_DefaultEllipseName\")");
        dialogSource.Should().Contain("UiText.Get(\"SelectionPane_DefaultLineName\")");
        dialogSource.Should().Contain("UiText.Get(\"SelectionPane_DefaultRectangleName\")");
        serviceSource.Should().Contain("SelectionPanePlannerText.Default");
        serviceSource.Should().NotContain("UiText.Get(");
    }
}
