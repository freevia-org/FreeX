using System.Globalization;
using FluentAssertions;
using Free.Shared.Localization;
using Xunit;

namespace FreeW.App.Localization.Tests;

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
            () => Loc.Format("Backstage_Recent_OpenRecentFileAutomationName", "Roadmap.docx"));

        pseudo.Should().Contain("Roadmap.docx");
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
            "Ribbon_Tab_Home_Label",
            "Ribbon_Tab_Home_KeyTip",
            "Ribbon_Group_Clipboard_Label",
            "Ribbon_Group_Clipboard_KeyTip",
            "Ribbon_Command_Paste_Label",
            "Ribbon_Command_Paste_KeyTip",
            "Ribbon_Command_Cut_Label",
            "Ribbon_Command_Cut_KeyTip",
            "Ribbon_Command_Copy_Label",
            "Ribbon_Command_Copy_KeyTip",
            "Ribbon_Group_Font_Label",
            "Ribbon_Group_Font_KeyTip",
            "Ribbon_Command_FontFamily_Label",
            "Ribbon_Command_FontSize_Label",
            "Ribbon_Command_Bold_Label",
            "Ribbon_Command_Bold_KeyTip",
            "Ribbon_Command_Italic_Label",
            "Ribbon_Command_Italic_KeyTip",
            "Ribbon_Command_Underline_Label",
            "Ribbon_Command_Underline_KeyTip",
            "Ribbon_Command_Strikethrough_Label",
            "Ribbon_Command_GrowFont_Label",
            "Ribbon_Command_GrowFontCompact_Label",
            "Ribbon_Command_ShrinkFont_Label",
            "Ribbon_Command_ShrinkFontCompact_Label",
            "Ribbon_Command_Subscript_Label",
            "Ribbon_Command_SubscriptCompact_Label",
            "Ribbon_Command_Superscript_Label",
            "Ribbon_Command_SuperscriptCompact_Label",
            "Ribbon_Command_ChangeCase_Label",
            "Ribbon_Command_ChangeCaseCompact_Label",
            "Ribbon_Command_SmallCaps_Label",
            "Ribbon_Command_AllCaps_Label",
            "Ribbon_Command_TextHighlightColor_Label",
            "Ribbon_Command_HighlightCompact_Label",
            "Ribbon_Command_FontColor_Label",
            "Ribbon_Command_FontColorDropdown_Label",
            "Ribbon_Command_ClearAllFormatting_Label",
            "Ribbon_Command_ClearFormattingCompact_Label",
            "Ribbon_Command_FontDialog_Label",
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
    public void SharedHelpers_ExposeCatalogContracts()
    {
        Loc.PseudoLocalizationCultureName.Should().Be(LocalizedTextCatalog.PseudoLocalizationCultureName);
        Loc.IsPseudoLocalizationCulture("QPS-PLOC").Should().BeTrue();
        Loc.CreateAutomationName("_Open _File").Should().Be("Open File");
        Loc.CreateMissingText("Missing_Key").Should().Be("[[Missing_Key]]");
    }
}
