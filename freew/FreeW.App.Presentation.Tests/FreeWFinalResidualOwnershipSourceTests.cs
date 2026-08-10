namespace FreeW.App.Presentation.Tests;

public sealed class FreeWFinalResidualOwnershipSourceTests
{
    [Fact]
    public void Mail_merge_rule_dialogs_project_portable_sessions()
    {
        var wpf = Read("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = Read("freew", "FreeW.App.Avalonia", "MailMergeDialogs.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("new MailMergeRuleConditionDialogSession(");
            source.Should().Contain("session.SelectOperator(");
            source.Should().Contain("session.IsComparisonValueEnabled");
            source.Should().Contain("session.AcceptIf(");
            source.Should().Contain("session.AcceptCondition(");
            source.Should().Contain("new MailMergeRuleNameValueDialogSession()");
            source.Should().NotContain("MailMergeRuleDialogPlanner.GetConditionOperator(opCombo.SelectedIndex)");
            source.Should().NotContain("MailMergeRuleDialogPlanner.IsComparisonValueEnabled(op)");
        }
    }

    [Fact]
    public void Side_to_side_navigation_uses_shared_semantic_descriptors()
    {
        var wpf = Read("freew", "FreeW.App.Host", "MainWindow.cs");
        var avalonia = Read("freew", "FreeW.App.Avalonia", "MainWindow.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("FreeWApplicationFrameTextCatalog.PreviousPagePairSemantic");
            source.Should().Contain("FreeWApplicationFrameTextCatalog.NextPagePairSemantic");
            source.Should().Contain("FreeWApplicationFrameTextCatalog.PagePairStatusAutomationId");
            source.Should().NotContain("\"FreeW.SideToSidePagePairStatus\"");
        }
    }

    [Fact]
    public void Symbol_picker_renderers_use_shared_automation_identity()
    {
        var wpf = Read("freew", "FreeW.App.Host", "SymbolPickerDialog.cs");
        var avalonia = Read("freew", "FreeW.App.Avalonia", "SymbolPickerDialog.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("FreeWSymbolPickerDialogPlanner.BuildSemantic(glyph)");
            source.Should().Contain("FreeWSymbolPickerDialogPlanner.DialogAutomationId");
            source.Should().Contain("FreeWSymbolPickerDialogPlanner.CancelAutomationId");
            source.Should().NotContain("\"SymbolPickerCancelButton\"");
        }
    }

    [Fact]
    public void Building_blocks_renderers_project_the_portable_organizer_session()
    {
        var wpf = Read("freew", "FreeW.App.Host", "BuildingBlocksOrganizerDialog.cs");
        var avalonia = Read("freew", "FreeW.App.Avalonia", "FinalCommandParityDialogs.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("BuildingBlocksOrganizerPlanner.CreateSession(library)");
            source.Should().Contain("_session.SelectIndex(");
            source.Should().Contain("_session.AcceptSelection()");
            source.Should().Contain("_session.DeleteSelection()");
            source.Should().NotContain("_library.Snippets");
            source.Should().NotContain("_library.Remove(");
            source.Should().NotContain("BuildingBlocksOrganizerPlanner.FormatPreview(");
            source.Should().NotContain("new BuildingBlockListItem(");
        }
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.Find(parts));
}
