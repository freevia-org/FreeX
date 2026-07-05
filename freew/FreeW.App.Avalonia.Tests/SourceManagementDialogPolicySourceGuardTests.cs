using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class SourceManagementDialogPolicySourceGuardTests
{
    [Fact]
    public void ReferencesDialogs_DelegatesSourceAuthorPolicyToPresentationPlanner()
    {
        var source = ReadReferencesDialogsSource();

        source.Should().Contain("SourceManagementDialogPlanner.BuildSourceTypeChoices(");
        source.Should().Contain("SourceManagementDialogPlanner.BuildEntryFieldPlans(");
        source.Should().Contain("SourceManagementDialogPlanner.CreateEntry(");
        source.Should().Contain("_fields.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.Text),");
        source.Should().Contain("_initialEntry);");
    }

    [Fact]
    public void MainWindow_DelegatesCitationAddNewPolicyToPresentationPlanner()
    {
        var source = ReadMainWindowSource();

        source.Should().Contain("SourceManagementDialogPlanner.BuildInitialState(");
        source.Should().Contain("SourceManagementDialogPlanner.AddCitationSource(");
        source.Should().Contain("SourceManagementDialogPlanner.BuildResult(");
        source.Should().Contain("MasterSourceStore.Load()");
        source.Should().Contain("MasterSourceStore.Save(");
    }

    [Fact]
    public void ReferencesDialogs_DelegatesManageSourcesPolicyToPresentationPlanner()
    {
        var source = ReadReferencesDialogsSource();

        source.Should().Contain("SourceManagementDialogPlanner.BuildInitialState(");
        source.Should().Contain("SourceManagementDialogPlanner.AddMasterSource(");
        source.Should().Contain("SourceManagementDialogPlanner.EditMasterSource(");
        source.Should().Contain("SourceManagementDialogPlanner.DeleteMasterSource(");
        source.Should().Contain("SourceManagementDialogPlanner.CopyMasterToCurrent(");
        source.Should().Contain("SourceManagementDialogPlanner.CopyCurrentToMaster(");
        source.Should().Contain("SourceManagementDialogPlanner.AddCurrentSource(");
        source.Should().Contain("SourceManagementDialogPlanner.EditCurrentSource(");
        source.Should().Contain("SourceManagementDialogPlanner.DeleteCurrentSource(");
        source.Should().Contain("SourceManagementDialogPlanner.BuildResult(");
    }

    [Fact]
    public void ReferencesDialogs_DoesNotOwnSourceAuthorParsingPolicy()
    {
        var source = ReadReferencesDialogsSource();

        source.Should().NotContain(".Split(';')");
        source.Should().NotContain("PersonalAuthors =");
        source.Should().NotContain("CorporateAuthor =");
        source.Should().NotContain("SourceAuthorPerson.Create(");
    }

    private static string ReadReferencesDialogsSource()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "freew",
            "FreeW.App.Avalonia",
            "ReferencesDialogs.cs");
        return File.ReadAllText(path);
    }

    private static string ReadMainWindowSource()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "freew",
            "FreeW.App.Avalonia",
            "MainWindow.cs");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
