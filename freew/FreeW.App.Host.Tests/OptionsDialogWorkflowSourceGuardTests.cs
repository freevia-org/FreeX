using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class OptionsDialogWorkflowSourceGuardTests
{
    [Fact]
    public void DesktopOptionsDialogs_DelegateCommitAndEnabledStatePolicyToPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "OptionsDialog.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "OptionsDialog.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("OptionsDialogWorkflowPlanner.TryBuildResult(");
            source.Should().Contain("OptionsDialogWorkflowPlanner.PlanEnabledState(");
            source.Should().Contain("new OptionsDialogInput(");
            source.Should().NotContain("OptionsDialogPlanner.TryParseRecentFilesCap(");
            source.Should().NotContain("OptionsDialogPlanner.BuildResult(");
            source.Should().NotContain("new AutoFormatOptions");
            source.Should().NotContain("new AutoCorrectOptions");
        }
    }
}
