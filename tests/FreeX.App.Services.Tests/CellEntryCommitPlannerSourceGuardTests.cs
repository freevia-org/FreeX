using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class CellEntryCommitPlannerSourceGuardTests
{
    [Fact]
    public void ProductCommitPaths_DelegateEntryPreparationToSharedPlanner()
    {
        var services = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Services", "WorkbookCellEditService.cs");
        var session = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Services", "WorkbookSession.cs");
        var wpf = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Host", "MainWindow.Editing.cs");
        var avalonia = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.cs");

        services.Should().Contain("CellEntryCommitPlanner.BuildSingle(");
        session.Should().Contain("CellEntryCommitPlanner.BuildSelection(");
        wpf.Should().Contain("_session.CommitCellText(");
        avalonia.Should().Contain("_session.CommitCellText(");
        wpf.Should().NotContain("CellEntryCommitPlanner.Build");
        avalonia.Should().NotContain("CellEntryCommitPlanner.Build");

        new[] { services, session, wpf, avalonia }
            .Should().OnlyContain(source => !source.Contains("CellEntryParser.CreateCell(", StringComparison.Ordinal));
    }
}
