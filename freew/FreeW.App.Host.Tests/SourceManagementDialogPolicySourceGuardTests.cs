using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class SourceManagementDialogPolicySourceGuardTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesSourceManagementPolicyToPresentationPlanner()
    {
        var source = ReadHostRibbonSource();

        source.Should().Contain("using FreeW.App.Presentation.Ribbon;");
        source.Should().Contain("SourceManagementDialogPlanner.AddCitationSource(");
        source.Should().Contain("SourceManagementDialogPlanner.BuildPickerItems(");
        source.Should().Contain("SourceManagementDialogPlanner.BuildSourceTypeChoices(");
        source.Should().Contain("SourceManagementDialogPlanner.BuildEntryFieldPlans(");
        source.Should().Contain("SourceManagementDialogPlanner.CreateEntry(");
        source.Should().Contain("new SourceManagementAuthorEditorSession(entry)");
        source.Should().Contain("session.AddPersonalAuthorRow(");
        source.Should().Contain("session.RemoveFinalPersonalAuthorRow(");
        source.Should().Contain("session.SelectMode(");
        source.Should().Contain("session.Accept(");
        source.Should().Contain("plan.PersonalAuthorFieldsEnabled");
        source.Should().Contain("plan.CorporateAuthorFieldEnabled");
        source.Should().Contain("SourceManagementDialogPlanner.ApplyPrimaryAuthorEditorState(");
        source.Should().Contain("fields.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.Text),");
        source.Should().Contain("entry);");
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
    }

    [Fact]
    public void FreeWRibbonCommands_DoesNotOwnSourceManagementPolicy()
    {
        var source = ReadHostRibbonSource();

        source.Should().NotContain("private sealed record SourcePick");
        source.Should().NotContain("private sealed record SourceEntry");
        source.Should().NotContain("private static bool HasSourceData(");
        source.Should().NotContain("private static Source BuildSource(");
        source.Should().NotContain("private static Source CloneSource(");
        source.Should().NotContain("private static string DescribeSource(Source");
        source.Should().NotContain(".Split(';')");
        source.Should().NotContain("PersonalAuthors =");
        source.Should().NotContain("CorporateAuthor =");
        source.Should().NotContain("SourceAuthorPerson.Create(");
        source.Should().NotContain("SourcePayloadEquals(");
        source.Should().NotContain("SourcePeopleEqual(");
        source.Should().NotContain("SourceValueEquals(");
        source.Should().NotContain("SourceManagementTagIdentity");
        source.Should().NotContain("FindSourceIndexByTag(");
        source.Should().NotContain("workingDoc");
        source.Should().NotContain("workingMaster");
        source.Should().NotContain("new SourceRecord");
        source.Should().NotContain("entry.Author.Length == 0 && entry.Title.Length == 0 && entry.Year.Length == 0");
        source.Should().NotContain("Any(s => s.Tag == src.Tag)");
        source.Should().NotContain("rowControls.Count <= 1");
        source.Should().NotContain("new SourceManagementAuthorEditorState(");
        source.Should().NotContain("SourceManagementDialogPlanner.NormalizePrimaryAuthorEditorState(");
    }

    [Fact]
    public void FreeWRibbonCommands_PreservesWpfDoubleClickEditInteraction()
    {
        var source = ReadHostRibbonSource();

        source.Should().Contain("masterList.MouseDoubleClick += (_, _) => EditMasterSource();");
        source.Should().Contain("docList.MouseDoubleClick += (_, _) => EditDocSource();");
    }

    [Fact]
    public void FreeWRibbonCommands_DefinesManageSourcesSizingAndCopyControlAuthority()
    {
        var source = ReadHostRibbonSource();

        source.Should().Contain("SizeToContent = SizeToContent.WidthAndHeight,");
        source.Should().Contain("Content = text.CopyToCurrentButtonLabel, MinWidth = 72");
        source.Should().Contain("Content = text.CopyToMasterButtonLabel, MinWidth = 72");
        source.Should().Contain("MinWidth = 220,");
        source.Should().Contain("MinHeight = 180,");
    }

    private static string ReadHostRibbonSource()
    {
        var path = Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs");
        return File.ReadAllText(path);
    }

}
