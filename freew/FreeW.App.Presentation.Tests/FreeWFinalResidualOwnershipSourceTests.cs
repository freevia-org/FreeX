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

    [Fact]
    public void Final_freew_compatibility_results_are_retired()
    {
        var avaloniaFont = Read("freew", "FreeW.App.Avalonia", "FontDialog.cs");
        var avaloniaParagraph = Read("freew", "FreeW.App.Avalonia", "ParagraphDialog.cs");
        var avaloniaPageSetup = Read("freew", "FreeW.App.Avalonia", "PageSetupDialog.cs");
        var avaloniaMail = Read("freew", "FreeW.App.Avalonia", "Ribbon", "MailMergeEngine.cs");
        var wpfEditor = Read("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var wpfCommands = Read("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");

        avaloniaFont.Should().NotContain("FontDialogResult");
        avaloniaParagraph.Should().NotContain("ParagraphDialogResult");
        avaloniaPageSetup.Should().NotContain("PageSetupDialogOutcome");
        avaloniaMail.Should().NotContain("MailMergeFinishBuildResult");
        wpfEditor.Should().NotContain("TogglePrintLayout()");
        wpfEditor.Should().NotContain("BuildWatermarkBrush(string");
        wpfCommands.Should().NotContain("ManageSourcesResult");
    }

    [Fact]
    public void Page_layout_ribbon_routes_keep_single_page_setting_ownership()
    {
        var wpfCommands = Read("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avaloniaCommands = Read("freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");
        var sharedWorkflow = Read("freew", "FreeW.App.Presentation", "Ribbon", "PageLayoutRibbonWorkflow.cs");

        wpfCommands.Should().Contain("PageLayoutRibbonWorkflow.Register(");
        avaloniaCommands.Should().Contain("PageLayoutRibbonWorkflow.Register(");
        sharedWorkflow.Should().Contain("BindColumnPreset(FreeWRibbonCommandAction.ColumnsOne");
        sharedWorkflow.Should().Contain("BindLineNumberMode(FreeWRibbonCommandAction.LineNumbersNone");
        sharedWorkflow.Should().Contain("FreeWRibbonCommandAction.HyphenationNone");
        sharedWorkflow.Should().Contain("FreeWRibbonCommandAction.DifferentFirstPage");
        wpfCommands.Should().NotContain("class ColumnsPresetCommand");
        wpfCommands.Should().NotContain("class LineNumberModeCommand");
        wpfCommands.Should().NotContain("class HyphenationCommand");
        wpfCommands.Should().NotContain("class HyphenationModeCommand");
        wpfCommands.Should().NotContain("class DifferentFirstPageCommand");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.Find(parts));
}
