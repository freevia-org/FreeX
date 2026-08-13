using System.IO;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookViewportScrollPlannerDedupTests
{
    [Fact]
    public void CalculateViewportOrigin_AllowsHostStartupFallbackWithoutSheet()
    {
        WorkbookViewportScrollPlanner.CalculateViewportOrigin(
                sheet: null,
                verticalScrollValue: 0,
                horizontalScrollValue: 0)
            .Should().Be((1u, 1u));
    }

    [Fact]
    public void CalculateScrollValueToRevealCell_PlansForwardKeyboardRevealWithFrozenRows()
    {
        WorkbookViewportScrollPlanner.CalculateScrollValueToRevealCell(
                targetIndex: 19,
                firstVisibleIndex: 9,
                lastVisibleIndex: 13,
                absoluteLimit: CellAddress.MaxRow,
                visibleSpan: 5)
            .Should().Be(15);
    }

    [Fact]
    public void CalculateWheelScroll_UsesNormalizedTouchpadDeltaInSharedPlanner()
    {
        var notches = WorkbookViewportScrollPlanner.NormalizeWheelNotches(-30);

        WorkbookViewportScrollPlanner.CalculateWheelScroll(
                currentValue: 1,
                currentMaximum: 40,
                wheelNotches: notches,
                stepPerNotch: 3,
                visibleSpan: 40,
                absoluteLimit: CellAddress.MaxRow)
            .Should().Be((40d, 4d));
    }

    [Fact]
    public void WorkbookViewportScrollPlanner_IsOwnedByServicesAndCalledDirectlyByHost()
    {
        var plannerSource = WorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Services",
            "WorkbookViewportScrollPlanner.cs");
        var hostSource = DialogSourceTestSupport.ReadHostSources(
            "MainWindow.Viewport.cs",
            "ViewportScrollbarUpdater.cs");
        var hostFacadePath = Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(),
            "src",
            "FreeX.App.Host",
            "ViewportScrollCalculator.cs");

        plannerSource.Should().Contain("public static WorkbookViewportCellRevealPlan PlanCellReveal");
        plannerSource.Should().Contain("public static uint CalculateScrollValueToRevealCell");
        plannerSource.Should().Contain("public static (double Maximum, double Value) CalculateWheelScroll");
        plannerSource.Should().Contain("public static (double Maximum, double Value) CalculateDragAutoScroll");
        hostSource.Should().Contain("WorkbookViewportScrollPlanner.CalculateViewportOrigin");
        hostSource.Should().Contain("WorkbookViewportScrollPlanner.CalculateWheelScroll");
        File.Exists(hostFacadePath).Should().BeFalse();
    }
}
