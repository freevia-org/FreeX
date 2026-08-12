namespace FreeP.App.Compositor.Tests;

public sealed class DeadMemberOwnershipSourceTests
{
    [Fact]
    public void Superseded_planner_parser_command_and_model_members_stay_retired()
    {
        Read("freep", "FreeP.App.Presentation", "ChartRenderPlanner.cs")
            .Should().NotContain("BuildMajorGridLinePlans(")
            .And.NotContain("BuildSecondaryValueAxisLabelPlans(");
        Read("freep", "FreeP.Core.IO", "PptxChartReader.cs")
            .Should().NotContain("private static void ReadScatterChart(");
        Read("freep", "FreeP.Core.IO", "PptxPackageWriter.cs")
            .Should().NotContain("private static XElement CnvPr(")
            .And.NotContain("private static string GetShapeId(");
        Read("freep", "FreeP.App.Presentation", "PresentationExportPlanner.cs")
            .Should().NotContain("PresentationDeferredExportPlan")
            .And.NotContain("BuildDeferredExportPlan(");
        Read("freep", "FreeP.App.Presentation", "PresentationFileCommandSession.cs")
            .Should().NotContain("BuildVideoExportHandoffPlan(");
        Read("freep", "FreeP.Core.Model", "PresentationCommands.cs")
            .Should().NotContain("class AddSlideCommand");
        Read("freep", "FreeP.Core.Model", "Slide.cs")
            .Should().NotContain("bool HasBevel");

        var smartArt = Read("freep", "FreeP.App.Presentation", "SmartArtAuthoringPlanner.cs");
        smartArt.Should().NotContain("Simple = SimpleFill")
            .And.NotContain("Moderate = ModerateEffect")
            .And.NotContain("Intense = IntenseEffect");
    }

    [Fact]
    public void Declaration_only_editing_session_shortcuts_stay_retired()
    {
        var source = Read("freep", "FreeP.App.Presentation", "EditingSession.cs");
        var retiredMembers = new[]
        {
            "AddSmartArtAssistant(",
            "SetShowMediaControls(",
            "SetSelectedSummaryZoomTileProperties(",
            "SetSelectedSummaryZoomTileCoverImage(",
            "ResetSelectedSummaryZoomTileCoverImage(",
            "SetSelectedSummaryZoomTileLayout(",
            "SetSelectedSummaryZoomTargets(",
            "AddSectionAtCurrentSlide(",
            "SetSelectedSlideZoomTarget(",
            "SetSelectedSectionZoomTarget(",
            "SetSelectedOutline(",
            "MergeSelectedCells(",
        };

        foreach (var member in retiredMembers)
            source.Should().NotContain(member);
    }

    private static string Read(params string[] pathParts) =>
        TestWorkspaceFileLocator.ReadAllText(pathParts);
}
