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
    public void SharedCatalog_FallsBackAcrossCulturesAndPreservesFormattingContracts()
    {
        WithUiCulture("en-US", () => Loc.Get("Ribbon_Command_Bold_Label")).Should().Be("Bold");
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
            "Ribbon_Command_Layout_Label",
            "Ribbon_Command_Layout_KeyTip",
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
            "Ribbon_Group_Font_Label",
            "Ribbon_Group_Font_KeyTip",
            "Ribbon_Command_FontFamily_Label",
            "Ribbon_Command_Bold_Label",
            "Ribbon_Command_Bold_KeyTip",
            "Ribbon_Command_Italic_Label",
            "Ribbon_Command_Italic_KeyTip",
            "Ribbon_Command_Underline_Label",
            "Ribbon_Command_Underline_KeyTip",
            "Ribbon_Group_Edit_Label",
            "Ribbon_Group_Edit_KeyTip",
            "Ribbon_Command_Undo_Label",
            "Ribbon_Command_Undo_KeyTip",
            "Ribbon_Command_Redo_Label",
            "Ribbon_Command_Redo_KeyTip",
            "Ribbon_Group_Editing_Label",
            "Ribbon_Group_Editing_KeyTip",
            "Ribbon_Command_Find_Label",
            "Ribbon_Command_Find_KeyTip",
            "Ribbon_Command_Replace_Label",
            "Ribbon_Command_Replace_KeyTip",
            "Ribbon_Group_SlideShow_Label",
            "Ribbon_Group_SlideShow_WpfKeyTip",
            "Ribbon_Group_SlideShow_AvaloniaKeyTip",
            "Ribbon_Command_SlideShowFromBeginning_Label",
            "Ribbon_Command_SlideShowFromBeginning_KeyTip",
            "Ribbon_Command_SlideShowFromCurrentSlide_Label",
            "Ribbon_Command_SlideShowFromCurrentSlide_KeyTip",
            "Ribbon_Command_SlideShowCustomShows_Label",
            "Ribbon_Command_SlideShowCustomShows_KeyTip"
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
            "Ribbon_Command_HeaderFooter_Label",
            "Ribbon_Command_HeaderFooter_KeyTip",
            "Ribbon_Command_DateTime_Label",
            "Ribbon_Command_DateTime_KeyTip",
            "Ribbon_Command_SlideNumber_Label",
            "Ribbon_Command_SlideNumber_KeyTip",
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
    public void GetNeutralResourceKeys_CoversRibbonWpfOnlyResidualFoundation()
    {
        Loc.GetNeutralResourceKeys().Should().Contain([
            "Ribbon_Group_Arrange_Label",
            "Ribbon_Group_Arrange_KeyTip",
            "Ribbon_Command_ArrangeGroup_Label",
            "Ribbon_Command_ArrangeGroup_KeyTip",
            "Ribbon_Command_ArrangeUngroup_Label",
            "Ribbon_Command_ArrangeUngroup_KeyTip",
            "Ribbon_Command_ArrangeBringToFront_Label",
            "Ribbon_Command_ArrangeBringToFront_KeyTip",
            "Ribbon_Command_ArrangeBringForward_Label",
            "Ribbon_Command_ArrangeBringForward_KeyTip",
            "Ribbon_Command_ArrangeSendBackward_Label",
            "Ribbon_Command_ArrangeSendBackward_KeyTip",
            "Ribbon_Command_ArrangeSendToBack_Label",
            "Ribbon_Command_ArrangeSendToBack_KeyTip",
            "Ribbon_Command_ArrangeAlignLeft_Label",
            "Ribbon_Command_ArrangeAlignLeft_KeyTip",
            "Ribbon_Command_ArrangeAlignCenterHorizontal_Label",
            "Ribbon_Command_ArrangeAlignCenterHorizontal_KeyTip",
            "Ribbon_Command_ArrangeAlignRight_Label",
            "Ribbon_Command_ArrangeAlignRight_KeyTip",
            "Ribbon_Command_ArrangeAlignTop_Label",
            "Ribbon_Command_ArrangeAlignTop_KeyTip",
            "Ribbon_Command_ArrangeAlignMiddle_Label",
            "Ribbon_Command_ArrangeAlignMiddle_KeyTip",
            "Ribbon_Command_ArrangeAlignBottom_Label",
            "Ribbon_Command_ArrangeAlignBottom_KeyTip",
            "Ribbon_Command_ArrangeDistributeHorizontal_Label",
            "Ribbon_Command_ArrangeDistributeHorizontal_KeyTip",
            "Ribbon_Command_ArrangeDistributeVertical_Label",
            "Ribbon_Command_ArrangeDistributeVertical_KeyTip",
            "Ribbon_Tab_Design_Label",
            "Ribbon_Tab_Design_KeyTip",
            "Ribbon_Group_Themes_Label",
            "Ribbon_Group_Themes_KeyTip",
            "Ribbon_Command_ThemeOffice_Label",
            "Ribbon_Command_ThemeOffice_KeyTip",
            "Ribbon_Command_ThemeBerlin_Label",
            "Ribbon_Command_ThemeBerlin_KeyTip",
            "Ribbon_Command_ThemeFacet_Label",
            "Ribbon_Command_ThemeFacet_KeyTip",
            "Ribbon_Command_ThemeIon_Label",
            "Ribbon_Command_ThemeIon_KeyTip",
            "Ribbon_Command_ThemeSlice_Label",
            "Ribbon_Command_ThemeSlice_KeyTip",
            "Ribbon_Group_Customize_Label",
            "Ribbon_Group_Customize_KeyTip",
            "Ribbon_Command_SlideSizeWidescreen_Label",
            "Ribbon_Command_SlideSizeWidescreen_KeyTip",
            "Ribbon_Command_SlideSizeStandard_Label",
            "Ribbon_Command_SlideSizeStandard_KeyTip",
            "Ribbon_Command_SlideSizeCustom_Label",
            "Ribbon_Command_SlideSizeCustom_KeyTip",
            "Ribbon_Tab_Transitions_Label",
            "Ribbon_Tab_Transitions_KeyTip",
            "Ribbon_Group_TransitionGallery_Label",
            "Ribbon_Group_TransitionGallery_KeyTip",
            "Ribbon_Command_TransitionNone_Label",
            "Ribbon_Command_TransitionNone_KeyTip",
            "Ribbon_Command_TransitionFade_Label",
            "Ribbon_Command_TransitionFade_KeyTip",
            "Ribbon_Command_TransitionPush_Label",
            "Ribbon_Command_TransitionPush_KeyTip",
            "Ribbon_Command_TransitionWipe_Label",
            "Ribbon_Command_TransitionWipe_KeyTip",
            "Ribbon_Command_TransitionSplit_Label",
            "Ribbon_Command_TransitionSplit_KeyTip",
            "Ribbon_Command_TransitionCut_Label",
            "Ribbon_Command_TransitionCut_KeyTip",
            "Ribbon_Command_TransitionCover_Label",
            "Ribbon_Command_TransitionCover_KeyTip",
            "Ribbon_Command_TransitionUncover_Label",
            "Ribbon_Command_TransitionUncover_KeyTip",
            "Ribbon_Command_TransitionBlinds_Label",
            "Ribbon_Command_TransitionBlinds_KeyTip",
            "Ribbon_Command_TransitionDissolve_Label",
            "Ribbon_Command_TransitionDissolve_KeyTip",
            "Ribbon_Command_TransitionZoom_Label",
            "Ribbon_Command_TransitionZoom_KeyTip",
            "Ribbon_Command_TransitionWheel_Label",
            "Ribbon_Command_TransitionWheel_KeyTip",
            "Ribbon_Group_TransitionTiming_Label",
            "Ribbon_Group_TransitionTiming_KeyTip",
            "Ribbon_Command_TransitionDuration_Label",
            "Ribbon_Command_TransitionAdvanceOnClick_Label",
            "Ribbon_Command_TransitionAdvanceOnClick_KeyTip",
            "Ribbon_Command_TransitionAdvanceAfter_Label",
            "Ribbon_Command_TransitionApplyAll_Label",
            "Ribbon_Command_TransitionApplyAll_KeyTip",
            "Ribbon_Option_TransitionAdvanceAfterNone_Label",
            "Ribbon_Tab_Animations_Label",
            "Ribbon_Tab_Animations_KeyTip",
            "Ribbon_Group_AnimationEffects_Label",
            "Ribbon_Group_AnimationEffects_KeyTip",
            "Ribbon_Command_AnimationEntranceAppear_Label",
            "Ribbon_Command_AnimationEntranceAppear_KeyTip",
            "Ribbon_Command_AnimationEntranceFade_Label",
            "Ribbon_Command_AnimationEntranceFade_KeyTip",
            "Ribbon_Command_AnimationEntranceFlyIn_Label",
            "Ribbon_Command_AnimationEntranceFlyIn_KeyTip",
            "Ribbon_Command_AnimationEntranceWipe_Label",
            "Ribbon_Command_AnimationEntranceWipe_KeyTip",
            "Ribbon_Command_AnimationEntranceZoom_Label",
            "Ribbon_Command_AnimationEntranceZoom_KeyTip",
            "Ribbon_Command_AnimationEntranceSplit_Label",
            "Ribbon_Command_AnimationEntranceSplit_KeyTip",
            "Ribbon_Command_AnimationEmphasisPulse_Label",
            "Ribbon_Command_AnimationEmphasisPulse_KeyTip",
            "Ribbon_Command_AnimationEmphasisSpin_Label",
            "Ribbon_Command_AnimationEmphasisSpin_KeyTip",
            "Ribbon_Command_AnimationEmphasisGrowShrink_Label",
            "Ribbon_Command_AnimationEmphasisGrowShrink_KeyTip",
            "Ribbon_Command_AnimationExitDisappear_Label",
            "Ribbon_Command_AnimationExitDisappear_KeyTip",
            "Ribbon_Command_AnimationExitFadeOut_Label",
            "Ribbon_Command_AnimationExitFadeOut_KeyTip",
            "Ribbon_Command_AnimationExitFlyOut_Label",
            "Ribbon_Command_AnimationExitFlyOut_KeyTip",
            "Ribbon_Command_AnimationNone_Label",
            "Ribbon_Command_AnimationNone_KeyTip",
            "Ribbon_Group_AnimationTiming_Label",
            "Ribbon_Group_AnimationTiming_KeyTip",
            "Ribbon_Command_AnimationTrigger_Label",
            "Ribbon_Command_AnimationDuration_Label",
            "Ribbon_Command_AnimationDelay_Label",
            "Ribbon_Command_AnimationMoveEarlier_Label",
            "Ribbon_Command_AnimationMoveEarlier_KeyTip",
            "Ribbon_Command_AnimationMoveLater_Label",
            "Ribbon_Command_AnimationMoveLater_KeyTip",
            "Ribbon_Group_AdvancedAnimation_Label",
            "Ribbon_Group_AdvancedAnimation_KeyTip",
            "Ribbon_Command_AnimationPane_Label",
            "Ribbon_Command_AnimationPane_KeyTip",
            "Ribbon_Option_AnimationTriggerOnClick_Label",
            "Ribbon_Option_AnimationTriggerWithPrevious_Label",
            "Ribbon_Option_AnimationTriggerAfterPrevious_Label"
        ]);
    }

    [Fact]
    public void GetNeutralResourceKeys_CoversFileTextResources()
    {
        Loc.GetNeutralResourceKeys().Should().Contain([
            "File_OpenPresentationPickerTitle",
            "File_SavePresentationPickerTitle",
            "File_PresentationFallbackDisplayName",
            "File_NewPresentationAction",
            "File_OpenPresentationAction",
            "File_OpenCommand",
            "File_SaveCommand",
            "File_InsertPictureCommand",
            "File_InsertPicturePickerTitle",
            "File_PictureFileTypeName",
            "File_CommandUnavailableFormat",
            "File_SelectedFileNotLocalPathFormat",
            "File_UnsupportedFileTypeFormat",
            "File_UnsupportedExtensionFormat",
            "File_CommandFailedFormat",
            "File_OpenedFormat",
            "File_SavedFormat",
            "File_InsertedFormat",
            "File_SaveAsTitleFormat"
        ]);

        WithUiCulture("en-US", () => Loc.Get("File_OpenPresentationPickerTitle"))
            .Should().Be("Open Presentation");
        WithUiCulture("en-US", () => Loc.Format("File_CommandFailedFormat", "Save", "Denied"))
            .Should().Be("Save failed: Denied");
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
