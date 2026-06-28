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
        var dialogSource = DialogSourceTestSupport.ReadHostSources(
            "AdvancedFilterDialog.cs",
            "AdvancedFilterDialog.Planning.cs");
        var servicesSource = DialogSourceTestSupport.ReadAppServicesSource("AdvancedFilterPlanner.cs");

        File.Exists(hostPlannerPath)
            .Should().BeFalse("the WPF dialog should call the portable AdvancedFilterPlanner directly");

        dialogSource.Should().Contain(
            "using ServicesAdvancedFilterPlanner = FreeX.App.Services.AdvancedFilterPlanner;");
        dialogSource.Should().Contain("ServicesAdvancedFilterPlanner.CreatePlan(");
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

        servicesSource.Should().Contain("AdvancedFilterPlanError");
        servicesSource.Should().Contain("AdvancedFilterCommand.IsListRangeWithinSupportedBounds");
        servicesSource.Should().Contain("AdvancedFilterCommand.IsCriteriaRangeWithinSupportedBounds");
        servicesSource.Should().Contain("AdvancedFilterCommand.MaxListColumns");
        servicesSource.Should().NotContain("UiText.Get(");
    }
}
