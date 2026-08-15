using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Panes;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWSurfaceTextCatalogTests
{
    [Fact]
    public void SurfaceCatalogs_ResolveThroughTheProvidedLocalizationProvider()
    {
        static string Localize(string key) => $"localized:{key}";

        NavigationPaneTextCatalog.Resolve(Localize).Title.Should().Be("localized:Navigation_Title");
        SmartArtDialogPlanner.ResolveText(Localize).InsertTitle.Should().Be("localized:SmartArt_Dialog_Insert_Title");
        TableTextConversionDialogPlanner.ResolveText(Localize).PromptLabel.Should().Be("localized:TableConversion_Prompt_Label");
        SourceManagementDialogPlanner.ResolveText(Localize).ManageSourcesTitle.Should().Be("localized:SourceManagement_Manage_Title");
        BackstageInfoSafetyPanePlanner.ResolveText(Localize).MarkedAsFinalStatus.Should().Be("localized:Backstage_Safety_MarkedAsFinal_Status");
        DesignDialogTextCatalog.Resolve(Localize).EffectsTitle.Should().Be("localized:Design_Effects_Title");
        var backstage = FreeWBackstagePaneTextCatalog.BuildTextSpec(Localize);
        backstage.RecentEmptyText.Should().Be("localized:FreeW_Backstage_Recent_EmptyText");
        backstage.Info.Heading.Should().Be("localized:FreeW_Backstage_Info_Heading");
        backstage.Info.CoreProperties.AuthorLabel.Should().Be("localized:FreeW_Backstage_Info_AuthorLabel");
        backstage.OptionsSummary.DataFolderLabel.Should().Be(
            "localized:FreeW_Backstage_OptionsSummary_DataFolderLabel");
    }

    [Fact]
    public void SurfaceCatalogs_ExposeUniqueNonEmptyResourceKeys()
    {
        var keys = MailMergeRuleDialogPlanner.RequiredResourceKeys
            .Concat(NavigationPaneTextCatalog.RequiredResourceKeys)
            .Concat(SmartArtDialogPlanner.RequiredResourceKeys)
            .Concat(TableTextConversionDialogPlanner.RequiredResourceKeys)
            .Append(TableFormulaDialogPlanner.CursorOutsideTableResourceKey)
            .Append(TablePropertiesDialogPlanner.CursorOutsideTableResourceKey)
            .Concat(FreeWBackstagePaneTextCatalog.RequiredResourceKeys)
            .Concat(SourceManagementDialogPlanner.RequiredResourceKeys)
            .Concat(BackstageInfoSafetyPanePlanner.RequiredResourceKeys)
            .Concat(DesignDialogTextCatalog.RequiredResourceKeys)
            .ToArray();

        keys.Should().OnlyContain(key => !string.IsNullOrWhiteSpace(key));
        keys.Should().OnlyHaveUniqueItems();
    }
}
