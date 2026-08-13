using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class CrossRendererResidualPolicySourceGuardTests
{
    [Fact]
    public void DrawingRenderers_DelegateNudgeEligibilityAndDeltaPolicy()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var hostDrawing = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.Drawing.cs");
        var hostSelection = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.Selection.cs");
        var avaloniaDrawing = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.DrawingObjectInteraction.cs");
        var avaloniaMain = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs");

        hostDrawing.Should().Contain("DrawingObjectNudgePlanner.TryPlan(");
        avaloniaDrawing.Should().Contain("DrawingObjectNudgePlanner.TryPlan(");
        hostSelection.Should().Contain("TryPlanSelectedDrawingObjectNudge(");
        avaloniaMain.Should().Contain("TryPlanSelectedDrawingObjectNudge(");
        hostDrawing.Should().NotContain("DrawingObjectNudgeStep");
        hostDrawing.Should().NotContain("DrawingObjectFineNudgeStep");
        avaloniaDrawing.Should().NotContain("DrawingObjectNudgeStep");
        avaloniaDrawing.Should().NotContain("DrawingObjectFineNudgeStep");
    }

    [Fact]
    public void AutoFilterRenderers_DelegateChecklistAndPlacementPolicy()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var hostDialog = Read(repoRoot, "src", "FreeX.App.Host", "AutoFilterDialog.Controls.cs");
        var hostDropdown = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.EditingDropdowns.cs");
        var avaloniaPopup = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs");

        hostDialog.Should().Contain("AutoFilterMenuPlanner.PlanChecklistState(");
        avaloniaPopup.Should().Contain("AutoFilterMenuPlanner.PlanChecklistState(");
        hostDialog.Should().NotContain("var selectedCount =");
        hostDropdown.Should().Contain("AutoFilterPopupPlacementPlanner.FromPointer(");
        hostDropdown.Should().Contain("AutoFilterPopupPlacementPlanner.FromHeaderBounds(");
        hostDropdown.Should().NotContain("clickedPoint.Y + 18");
        avaloniaPopup.Should().Contain("AutoFilterPopupPlacementPlanner.PreferredEdge");
    }

    private static string Read(string repoRoot, params string[] path)
    {
        var segments = new string[path.Length + 1];
        segments[0] = repoRoot;
        path.CopyTo(segments, 1);
        return File.ReadAllText(Path.Combine(segments));
    }
}
