using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class AdvancedFilterDialogPlannerDedupSourceTests
{
    [Fact]
    public void AdvancedFilterDialogPlanner_HostFacadeIsRemovedAndDialogUsesPortablePlannerDirectly()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var hostPlannerPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "AdvancedFilterDialogPlanner.cs");
        var hostDefaultListPlannerPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "AdvancedFilterDefaultListRangePlanner.cs");
        var hostInputParserPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "AdvancedFilterInputParser.cs");
        var mainDataCommandsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");
        var dialogSource = DialogSourceTestSupport.ReadHostSources(
            "AdvancedFilterDialog.cs",
            "AdvancedFilterDialog.Planning.cs");
        var presentationSource = DialogSourceTestSupport.ReadPresentationSources("Filtering", "AdvancedFilterPlanner.cs");
        var servicesPlannerPath = Path.Combine(repoRoot, "src", "FreeX.App.Services", "AdvancedFilterPlanner.cs");
        var workbookSessionSource = DialogSourceTestSupport.ReadAppServicesSource("WorkbookSession.cs");

        File.Exists(hostPlannerPath)
            .Should().BeFalse("the WPF dialog should call the portable AdvancedFilterPlanner directly");
        File.Exists(hostDefaultListPlannerPath)
            .Should().BeFalse("default Advanced Filter list range selection should stay in the portable AdvancedFilterPlanner");
        File.Exists(hostInputParserPath)
            .Should().BeFalse("Advanced Filter input parsing should live in the shared presentation planner");

        mainDataCommandsSource.Should().Contain("AdvancedFilterPlanner.CreateDefaultListRange(sheet, selected)");
        mainDataCommandsSource.Should().Contain("AdvancedFilterPlanner.TryParseRange(");
        mainDataCommandsSource.Should().NotContain("AdvancedFilterDefaultListRangePlanner.");
        mainDataCommandsSource.Should().NotContain("AdvancedFilterInputParser.");

        dialogSource.Should().Contain(
            "using SharedAdvancedFilterPlanner = FreeX.App.Presentation.Filtering.AdvancedFilterPlanner;");
        dialogSource.Should().Contain("SharedAdvancedFilterPlanner.CreatePlan(");
        dialogSource.Should().Contain("SharedAdvancedFilterPlanner.CreateRangeSelectionRequest(");
        dialogSource.Should().Contain("SharedAdvancedFilterPlanner.FocusTargetForPlanError(error)");
        dialogSource.Should().Contain("FormatAdvancedFilterPlanError(");
        dialogSource.Should().Contain("UiText.Get(\"AdvancedFilter_EnterValidListRange\")");
        dialogSource.Should().Contain("UiText.Get(\"AdvancedFilter_EnterValidCriteriaRange\")");
        dialogSource.Should().Contain("UiText.Get(\"AdvancedFilter_EnterValidCopyToRange\")");
        dialogSource.Should().Contain("AdvancedFilterCommand.ListRangeTooLargeMessage");
        dialogSource.Should().Contain("AdvancedFilterCommand.CopyOutputTooLargeMessage");

        dialogSource.Should().NotContain("AdvancedFilterInputParser.TryParseRange");
        dialogSource.Should().NotContain("AdvancedFilterCommand.IsListRangeWithinSupportedBounds");
        dialogSource.Should().NotContain("AdvancedFilterCommand.IsCriteriaRangeWithinSupportedBounds");
        dialogSource.Should().NotContain("AdvancedFilterCommand.IsCopyOutputWithinSupportedBounds");

        presentationSource.Should().Contain("WorkbookRangeTextCodec.TryParse(");
        presentationSource.Should().Contain("WorkbookRangeTextCodec.TryParseOnCurrentSheet(");
        presentationSource.Should().Contain("CellReferenceInputParser.TryParseCell(");
        presentationSource.Should().Contain("AdvancedFilterCommand.IsListRangeWithinSupportedBounds");
        presentationSource.Should().Contain("AdvancedFilterCommand.IsCriteriaRangeWithinSupportedBounds");
        presentationSource.Should().Contain("AdvancedFilterCommand.MaxListColumns");
        presentationSource.Should().Contain("FocusTargetForPlanError(");
        presentationSource.Should().NotContain("UiText.Get(");
        presentationSource.Should().NotContain("WorkbookReferenceNavigator");

        File.Exists(servicesPlannerPath)
            .Should().BeFalse("Advanced Filter should have one canonical planner and type family");
        workbookSessionSource.Should().Contain("using FreeX.App.Presentation.Filtering;");
        workbookSessionSource.Should().Contain("ExecuteAdvancedFilterPlan(AdvancedFilterPlan plan)");
    }
}
