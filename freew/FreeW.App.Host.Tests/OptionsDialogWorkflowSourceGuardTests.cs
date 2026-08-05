using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class OptionsDialogWorkflowSourceGuardTests
{
    [Fact]
    public void DesktopOptionsDialogs_DelegateLifetimeCommitAndEnabledStatePolicyToSharedSession()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "OptionsDialog.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "OptionsDialog.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("new OptionsDialogSession(");
            source.Should().Contain("_session.PlanAcceptance(");
            source.Should().Contain("_session.PlanEnabledState(");
            source.Should().Contain("_session.InitialState");
            source.Should().Contain("new OptionsDialogInput(");
            source.Should().NotContain("OptionsDialogPlanner.BuildSurface(");
            source.Should().NotContain("OptionsDialogWorkflowPlanner.TryBuildResult(");
            source.Should().NotContain("OptionsDialogWorkflowPlanner.PlanEnabledState(");
            source.Should().NotContain("OptionsDialogPlanner.TryParseAutoCorrectReplacements(");
            source.Should().NotContain("OptionsDialogPlanner.TryParseRecentFilesCap(");
            source.Should().NotContain("OptionsDialogPlanner.BuildResult(");
            source.Should().NotContain("new AutoFormatOptions");
            source.Should().NotContain("new AutoCorrectOptions");
        }
    }
}
