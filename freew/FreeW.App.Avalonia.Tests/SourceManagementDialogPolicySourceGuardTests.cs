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
        source.Should().Contain("SourceManagementDialogPlanner.ProjectPrimaryAuthorEditorState(");
        source.Should().Contain("SourceManagementDialogPlanner.NormalizePrimaryAuthorEditorState(");
        source.Should().Contain("SourceManagementDialogPlanner.ApplyPrimaryAuthorEditorState(");
        source.Should().Contain("_fields.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.Text),");
        source.Should().Contain("_entryBaseline);");
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
        source.Should().Contain("SourceManagementDialogPlanner.BuildSourceConflictMessage(");
        source.Should().Contain("SourceManagementDialogPlanner.BuildSourceConflictResolutionChoices(");
        source.Should().Contain("SourceManagementDialogPlanner.ResolveSourceConflict(");
        source.Should().Contain("SourceManagementDialogPlanner.AddCurrentSource(");
        source.Should().Contain("SourceManagementDialogPlanner.EditCurrentSource(");
        source.Should().Contain("SourceManagementDialogPlanner.DeleteCurrentSource(");
        source.Should().Contain("SourceManagementDialogPlanner.BuildResult(");
        source.Should().Contain("_masterList.DoubleTapped += (_, _) => _ = EditMasterAsync();");
        source.Should().Contain("_currentList.DoubleTapped += (_, _) => _ = EditCurrentAsync();");
    }

    [Fact]
    public void ReferencesDialogs_DoesNotOwnSourceAuthorOrConflictPolicy()
    {
        var source = ReadReferencesDialogsSource();

        source.Should().NotContain(".Split(';')");
        source.Should().NotContain("PersonalAuthors =");
        source.Should().NotContain("CorporateAuthor =");
        source.Should().NotContain("SourceAuthorPerson.Create(");
        source.Should().NotContain("SourcePayloadEquals(");
        source.Should().NotContain("SourcePeopleEqual(");
        source.Should().NotContain("SourceValueEquals(");
        source.Should().NotContain("SourceManagementTagIdentity");
        source.Should().NotContain("FindSourceIndexByTag(");
    }

    [Fact]
    public void ReferencesDialogs_PreservesWpfManageSourcesSizingAndCopyControlAuthority()
    {
        var source = ReadReferencesDialogsSource();

        source.Should().Contain("SizeToContent = SizeToContent.WidthAndHeight;");
        source.Should().NotContain("Width = 620;");
        source.Should().Contain("Button(\"Copy →\", () => _ = CopyMasterToCurrentAsync())");
        source.Should().Contain("ApplyButton(button, DialogChromeStyle, minWidth: 72");
    }

    private static string ReadReferencesDialogsSource()
    {
        var path = Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.App.Avalonia",
            "ReferencesDialogs.cs");
        return File.ReadAllText(path);
    }

    private static string ReadMainWindowSource()
    {
        var path = Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.App.Avalonia",
            "MainWindow.cs");
        return File.ReadAllText(path);
    }

}
