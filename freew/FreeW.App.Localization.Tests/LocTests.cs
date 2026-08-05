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
    public void SharedCatalog_FallsBackAcrossCulturesAndPreservesFormattingContracts()
    {
        WithUiCulture("en-US", () => Loc.Get("Ribbon_Command_Bold_Label")).Should().Be("Bold");
        WithUiCulture("en-US", () => Loc.Get("Ribbon_Command_Subscript_Label")).Should().Be("Subscript");
        WithUiCulture("en-US", () => Loc.Get("Ribbon_Command_Superscript_Label")).Should().Be("Superscript");
        WithUiCulture("en-US", () => Loc.Get("Options_AppLanguageSystemDefault"))
            .Should().Be("Use system default");
        WithUiCulture("fr-FR", () => Loc.Get("Options_AppLanguageSystemDefault"))
            .Should().Be("Utiliser la langue du systeme");
        WithUiCulture("en-US", () => Loc.Format("File_CommandFailedFormat", "Open", "Denied"))
            .Should().Be("Open failed: Denied");
        WithUiCulture("fr-FR", () => Loc.Get("Common_ConfirmTitle")).Should().Be("Confirmation");
        WithUiCulture("de-DE", () => Loc.Get("Backstage_GreetingMorning")).Should().Be("Good morning");
        WithUiCulture("en-US", () => Loc.Get("Shared_Catalog_Missing_Key"))
            .Should().Be("[[Shared_Catalog_Missing_Key]]");

        WithUiCulture(Loc.PseudoLocalizationCultureName, () => Loc.Get("Common_Cancel"))
            .Should().Contain("CCaanncceell");
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
            "Ribbon_Command_FormatPainter_Label",
            "Ribbon_Command_FormatPainter_KeyTip",
            "Ribbon_Command_PasteTextOnly_Label",
            "Ribbon_Command_PasteMergeFormatting_Label",
            "Ribbon_Command_PasteSpecial_Label",
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
            "Ribbon_Command_CharacterBorder_Label",
            "Ribbon_Command_CharacterShading_Label",
            "Ribbon_Command_ClearAllFormatting_Label",
            "Ribbon_Command_ClearFormattingCompact_Label",
            "Ribbon_Command_FontDialog_Label",
            "Ribbon_Palette_FontColor_Automatic_Label",
            "Ribbon_Palette_FontColor_Black_Label",
            "Ribbon_Palette_FontColor_DarkRed_Label",
            "Ribbon_Palette_FontColor_Red_Label",
            "Ribbon_Palette_FontColor_Orange_Label",
            "Ribbon_Palette_FontColor_Yellow_Label",
            "Ribbon_Palette_FontColor_Green_Label",
            "Ribbon_Palette_FontColor_Blue_Label",
            "Ribbon_Palette_FontColor_DarkBlue_Label",
            "Ribbon_Palette_FontColor_Purple_Label",
            "Ribbon_Palette_FontColor_White_Label",
            "Ribbon_Group_Paragraph_Label",
            "Ribbon_Group_Paragraph_KeyTip",
            "Ribbon_Command_Bullets_Label",
            "Ribbon_Command_Numbering_Label",
            "Ribbon_Command_MultilevelList_Label",
            "Ribbon_Command_MultilevelPromote_Label",
            "Ribbon_Command_MultilevelPromote_KeyTip",
            "Ribbon_Command_MultilevelDemote_Label",
            "Ribbon_Command_MultilevelDemote_KeyTip",
            "Ribbon_Command_MultilevelDefine_Label",
            "Ribbon_Command_MultilevelDefine_KeyTip",
            "Ribbon_Palette_MultilevelList_OutlineDecimal_Label",
            "Ribbon_Palette_MultilevelList_OutlineMixed_Label",
            "Ribbon_Palette_MultilevelList_OutlineHeadings_Label",
            "Ribbon_Group_Symbols_Label",
            "Ribbon_Group_Symbols_KeyTip",
            "Ribbon_Command_Symbol_Label",
            "Ribbon_Group_PageBackground_Label",
            "Ribbon_Group_PageBackground_KeyTip",
            "Ribbon_Command_Watermark_Label",
            "Ribbon_Command_PageColor_Label",
            "Ribbon_Command_PageBorders_Label",
            "Ribbon_Palette_PageColor_NoColor_Label",
            "Ribbon_Palette_PageColor_White_Label",
            "Ribbon_Palette_PageColor_LightGray_Label",
            "Ribbon_Palette_PageColor_Tan_Label",
            "Ribbon_Palette_PageColor_LightBlue_Label",
            "Ribbon_Palette_PageColor_LightGreen_Label",
            "Ribbon_Palette_PageColor_LightYellow_Label",
            "Ribbon_Palette_PageColor_Rose_Label",
            "Ribbon_Palette_Symbol_Euro_Label",
            "Ribbon_Palette_Symbol_Pound_Label",
            "Ribbon_Palette_Symbol_Yen_Label",
            "Ribbon_Palette_Symbol_Cent_Label",
            "Ribbon_Palette_Symbol_Copyright_Label",
            "Ribbon_Palette_Symbol_Registered_Label",
            "Ribbon_Palette_Symbol_Trademark_Label",
            "Ribbon_Palette_Symbol_Degree_Label",
            "Ribbon_Palette_Symbol_PlusMinus_Label",
            "Ribbon_Palette_Symbol_Multiplication_Label",
            "Ribbon_Palette_Symbol_Division_Label",
            "Ribbon_Palette_Symbol_NotEqual_Label",
            "Ribbon_Palette_Symbol_LessOrEqual_Label",
            "Ribbon_Palette_Symbol_GreaterOrEqual_Label",
            "Ribbon_Palette_Symbol_Bullet_Label",
            "Ribbon_Palette_Symbol_Ellipsis_Label",
            "Ribbon_Palette_Symbol_EmDash_Label",
            "Ribbon_Palette_Symbol_EnDash_Label",
            "Ribbon_Palette_Symbol_RightArrow_Label",
            "Ribbon_Palette_Symbol_LeftArrow_Label",
            "Ribbon_Dialog_PageColor_Title",
            "Ribbon_Dialog_PageColor_MoreColors_Label",
            "Ribbon_Dialog_PageColor_MoreColors_Title",
            "Ribbon_Dialog_PageColor_HexPrompt",
            "Ribbon_Dialog_PageColor_InvalidHexWarning",
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
    public void GetNeutralResourceKeys_CoversFileTextResources()
    {
        Loc.GetNeutralResourceKeys().Should().Contain([
            "File_OpenDocumentPickerTitle",
            "File_SaveDocumentPickerTitle",
            "File_DocumentFallbackDisplayName",
            "File_NewDocumentAction",
            "File_OpenDocumentAction",
            "File_OpenCommand",
            "File_SaveCommand",
            "File_InsertPictureCommand",
            "File_InsertPicturePickerTitle",
            "File_InsertTextCommand",
            "File_NewWindowCommand",
            "File_PdfFileTypeName",
            "File_PictureFileTypeName",
            "File_TextFromFileTypeName",
            "File_ExportPdfPickerTitle",
            "File_PdfExportCommand",
              "File_XpsFileTypeName",
              "File_ExportXpsPickerTitle",
              "File_XpsExportCommand",
              "File_XpsExportedStatusFormat",
            "File_CommandUnavailableFormat",
            "File_SelectedFileNotLocalPathFormat",
            "File_UnsupportedFileTypeFormat",
            "File_UnsupportedExtensionFormat",
            "File_CommandFailedFormat",
            "File_OpenedFormat",
            "File_SavedFormat",
            "File_InsertedFormat",
            "File_SaveAsTitleFormat",
            "File_PdfExportedStatusFormat",
            "File_PageSingular",
            "File_PagePlural"
        ]);

        WithUiCulture("en-US", () => Loc.Get("File_OpenDocumentPickerTitle")).Should().Be("Open document");
        WithUiCulture("en-US", () => Loc.Format("File_CommandFailedFormat", "Open", "Denied"))
            .Should().Be("Open failed: Denied");
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
