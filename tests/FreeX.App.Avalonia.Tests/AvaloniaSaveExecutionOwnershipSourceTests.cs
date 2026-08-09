using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaSaveExecutionOwnershipSourceTests
{
    [Fact]
    public void SaveRenderer_DelegatesPortableExecutionAndRetainsNativeFileAccessAndStatus()
    {
        var source = File.ReadAllText(RepoFile("MainWindow.cs"));

        source.Should().Contain("WorkbookSaveExecutionCoordinator.Begin(");
        source.Should().Contain("saveExecution.ExecuteAsync(");
        source.Should().NotContain("generationAtSaveStart");
        source.Should().NotContain("_session.TryMarkSavedIfNoEditsArrived(");
        source.Should().NotContain("catch (WorkbookExternallyModifiedException)");

        source.Should().Contain("ResolveExternallyModifiedFileOverwriteConfirm(path)");
        source.Should().Contain("_workbookFileAccessService.BeginAccessAsync(");
        source.Should().Contain("new WorkbookSaveExecutionPreparation(fileAccessIdentity, fileAccess)");
        source.Should().Contain("_saveService.SaveAsync(");
        source.Should().Contain("UpdateSaveButton()");
    }

    private static string RepoFile(string fileName) =>
        TestWorkspaceFileLocator.Find(RepoFile);
}
