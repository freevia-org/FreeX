using System.Globalization;
using FluentAssertions;
using Free.Shared.Localization;
using Xunit;

namespace FreeP.App.Localization.Tests;

public sealed class LocTests
{
    private static T WithUiCulture<T>(string cultureName, Func<T> action)
    {
        var originalUi = CultureInfo.CurrentUICulture;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Get_NeutralCulture_ReturnsEnglishShellStrings()
    {
        WithUiCulture("en-US", () => Loc.Get("Common_Cancel")).Should().Be("_Cancel");
        WithUiCulture("en-US", () => LocalizedUiText.ErrorTitle).Should().Be("Error");
    }

    [Fact]
    public void Get_FrenchCulture_ReturnsTranslationAndFallsBackToNeutral()
    {
        WithUiCulture("fr-FR", () => Loc.Get("Common_Cancel")).Should().Be("_Annuler");
        WithUiCulture("fr-FR", () => Loc.Get("Common_Ok")).Should().Be("_OK");
    }

    [Fact]
    public void Get_PseudoCulture_ExpandsNeutralText()
    {
        var pseudo = WithUiCulture(Loc.PseudoLocalizationCultureName, () => Loc.Get("Common_Cancel"));

        pseudo.Should().StartWith("[[").And.EndWith("]]");
        pseudo.Should().Contain("CCaanncceell");
    }

    [Fact]
    public void Format_PreservesCompositePlaceholders()
    {
        var pseudo = WithUiCulture(
            Loc.PseudoLocalizationCultureName,
            () => Loc.Format("Backstage_Recent_OpenRecentFileAutomationName", "Roadmap.pptx"));

        pseudo.Should().Contain("Roadmap.pptx");
        pseudo.Should().StartWith("[[").And.EndWith("]]");
    }

    [Fact]
    public void GetNeutralResourceKeys_CoversSharedShellAndBackstageFoundation()
    {
        Loc.GetNeutralResourceKeys().Should().Contain([
            "Common_Ok",
            "Common_Cancel",
            "Common_ErrorTitle",
            "Common_WarningTitle",
            "Common_InformationTitle",
            "Common_ConfirmTitle",
            "Options_AppLanguageSystemDefault",
            "Options_AppLanguageEnglishUnitedStates",
            "Backstage_GreetingMorning",
            "Backstage_GreetingAfternoon",
            "Backstage_GreetingEvening",
            "Backstage_Recent_OpenRecentFileAutomationName",
            "Backstage_Recent_OpenPinnedFileAutomationName",
            "Backstage_Recent_RemoveAutomationHelpText",
            "Backstage_Recent_LastOpenedTodayAt"
        ]);
    }

    [Fact]
    public void GetNeutralResourceKeys_CoversRibbonHomeShellFoundation()
    {
        Loc.GetNeutralResourceKeys().Should().Contain([
            "Ribbon_Tab_Home_Label",
            "Ribbon_Tab_Home_KeyTip",
            "Ribbon_Group_File_Label",
            "Ribbon_Group_File_KeyTip",
            "Ribbon_Command_FileNew_Label",
            "Ribbon_Command_FileNew_KeyTip",
            "Ribbon_Command_FileOpen_Label",
            "Ribbon_Command_FileOpen_KeyTip",
            "Ribbon_Command_FileSave_Label",
            "Ribbon_Command_FileSave_KeyTip",
            "Ribbon_Command_FileSaveAs_Label",
            "Ribbon_Command_FileSaveAs_KeyTip",
            "Ribbon_Group_Slides_Label",
            "Ribbon_Group_Slides_KeyTip",
            "Ribbon_Command_NewSlide_Label",
            "Ribbon_Command_NewSlide_KeyTip",
            "Ribbon_Command_NewSlide_AvaloniaKeyTip",
            "Ribbon_Command_DuplicateSlide_Label",
            "Ribbon_Command_DuplicateSlide_KeyTip",
            "Ribbon_Command_DeleteSlide_Label",
            "Ribbon_Command_DeleteSlide_KeyTip",
            "Ribbon_Group_Edit_Label",
            "Ribbon_Group_Edit_KeyTip",
            "Ribbon_Command_Undo_Label",
            "Ribbon_Command_Undo_KeyTip",
            "Ribbon_Command_Redo_Label",
            "Ribbon_Command_Redo_KeyTip",
            "Ribbon_Group_SlideShow_Label",
            "Ribbon_Group_SlideShow_WpfKeyTip",
            "Ribbon_Group_SlideShow_AvaloniaKeyTip",
            "Ribbon_Command_SlideShowFromBeginning_Label",
            "Ribbon_Command_SlideShowFromBeginning_KeyTip",
            "Ribbon_Command_SlideShowFromCurrentSlide_Label",
            "Ribbon_Command_SlideShowFromCurrentSlide_KeyTip"
        ]);
    }

    [Fact]
    public void GetNeutralResourceKeys_CoversRibbonInsertFoundation()
    {
        Loc.GetNeutralResourceKeys().Should().Contain([
            "Ribbon_Tab_Insert_Label",
            "Ribbon_Tab_Insert_KeyTip",
            "Ribbon_Group_Text_Label",
            "Ribbon_Group_Text_KeyTip",
            "Ribbon_Command_TextBox_Label",
            "Ribbon_Command_TextBox_KeyTip",
            "Ribbon_Group_Tables_Label",
            "Ribbon_Group_Tables_KeyTip",
            "Ribbon_Command_InsertTable3x3_Label",
            "Ribbon_Command_InsertTable3x3_KeyTip",
            "Ribbon_Command_InsertTable2x2_Label",
            "Ribbon_Command_InsertTable2x2_KeyTip",
            "Ribbon_Command_InsertTable4x4_Label",
            "Ribbon_Command_InsertTable4x4_KeyTip",
            "Ribbon_Group_Charts_Label",
            "Ribbon_Group_Charts_KeyTip",
            "Ribbon_Command_InsertChartColumn_Label",
            "Ribbon_Command_InsertChartColumn_KeyTip",
            "Ribbon_Command_InsertChartBar_Label",
            "Ribbon_Command_InsertChartBar_KeyTip",
            "Ribbon_Command_InsertChartLine_Label",
            "Ribbon_Command_InsertChartLine_KeyTip",
            "Ribbon_Command_InsertChartPie_Label",
            "Ribbon_Command_InsertChartPie_KeyTip",
            "Ribbon_Command_ChartEditData_Label",
            "Ribbon_Command_ChartEditData_KeyTip",
            "Ribbon_Group_Links_Label",
            "Ribbon_Group_Links_KeyTip",
            "Ribbon_Command_InsertLink_Label",
            "Ribbon_Command_InsertLink_KeyTip",
            "Ribbon_Command_RemoveLink_Label",
            "Ribbon_Command_RemoveLink_KeyTip",
            "Ribbon_Group_Illustrations_Label",
            "Ribbon_Group_Illustrations_KeyTip",
            "Ribbon_Command_Picture_Label",
            "Ribbon_Command_Picture_KeyTip",
            "Ribbon_Command_ShapeRectangle_Label",
            "Ribbon_Command_ShapeRectangle_KeyTip",
            "Ribbon_Command_ShapeEllipse_Label",
            "Ribbon_Command_ShapeEllipse_KeyTip"
        ]);
    }

    [Fact]
    public void SharedHelpers_ExposeCatalogContracts()
    {
        Loc.PseudoLocalizationCultureName.Should().Be(LocalizedTextCatalog.PseudoLocalizationCultureName);
        Loc.IsPseudoLocalizationCulture("QPS-PLOC").Should().BeTrue();
        Loc.CreateAutomationName("_Open _File").Should().Be("Open File");
        Loc.CreateMissingText("Missing_Key").Should().Be("[[Missing_Key]]");
    }
}
