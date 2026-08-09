using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class OptionsPropertiesDialogSessionOwnershipTests
{
    [Fact]
    public void AvaloniaOptionsDialogLeavesPortableLifetimeAndCommitDecisionsWithSession()
    {
        var source = File.ReadAllText(RepoFile("freew", "FreeW.App.Avalonia", "OptionsDialog.cs"));

        source.Should().Contain("new OptionsDialogSession(");
        source.Should().Contain("_session.InitialState");
        source.Should().Contain("_session.PlanEnabledState(");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().NotContain("OptionsDialogPlanner.BuildSurface(");
        source.Should().NotContain("OptionsDialogPlanner.TryParseAutoCorrectReplacements(");
        source.Should().NotContain("OptionsDialogWorkflowPlanner.TryBuildResult(");
        source.Should().NotContain("OptionsDialogWorkflowPlanner.PlanEnabledState(");
        source.Should().NotContain("SystemLanguageLabel(");
    }

    [Fact]
    public void AvaloniaPropertiesDialogLeavesCatalogProjectionAndCommitDecisionsWithSession()
    {
        var source = File.ReadAllText(RepoFile("freew", "FreeW.App.Avalonia", "PropertiesDialog.cs"));

        source.Should().Contain("new DocumentPropertiesDialogSession(");
        source.Should().Contain("_session.Surface.Fields");
        source.Should().Contain("_session.PlanCommit(");
        source.Should().Contain("CaptureInput()");
        source.Should().NotContain("DocumentPropertiesDialogValues.FromInput(");
        source.Should().NotContain("FormatDate(");
        source.Should().NotContain("properties.Title");
        source.Should().NotContain("\"Last saved by:\"");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
