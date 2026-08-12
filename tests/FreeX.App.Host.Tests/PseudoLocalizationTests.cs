using System.Globalization;
using FluentAssertions;
using Free.Shared.Localization;
using FreeX.App.Host;
using FreeX.App.Localization;
using static FreeX.App.Host.Tests.LocalizationResourceTestSupport;

namespace FreeX.App.Host.Tests;

public sealed partial class PseudoLocalizationTests
{
    private static readonly string[] HighRiskShellRibbonAndDialogKeys =
    [
        "Common_Ok",
        "Common_Cancel",
        "MainWindow_Header_Paste",
        "MainWindow_Content_Copy",
        "MainWindow_TooltipTitle_Paste",
        "MainWindow_TooltipDescription_PasteTheContentsOfTheClipboardCtrlV",
        "ClipboardFeedback_CopyMultipleSelectionUnsupported",
        "ClipboardFeedback_CutMultipleSelectionUnsupported",
        "ClipboardFeedback_ReadFailed",
        "PivotSort_AscendingByLabels",
        "PivotSort_DescendingByLabels",
        "PivotSort_AscendingByValues",
        "PivotSort_DescendingByValues",
        "PivotSort_ValueFieldRequired",
        "Options_ChooseDisplayLanguage",
        "Options_AppLanguageRestartMessage",
        "AdvancedFilter_AdvancedFilter",
        "AdvancedFilter_CopyToIsAvailableWhenCopyToAnotherLocationIsSelected",
        "FormatCells_FormatCells",
        "PageSetup_PageSetup",
        "FindReplace_FindWhat",
        "Sort_SortBy",
        "DataValidation_DataValidation",
        "TextToColumns_TextWizardStepOf3",
        "TextToColumns_DestinationLabel",
        "PrintPreview_TitleFormat",
    ];

    public static IEnumerable<object[]> HighRiskShellRibbonAndDialogKeyData() =>
        HighRiskShellRibbonAndDialogKeys.Select(key => new object[] { key });

    [Fact]
    public void Expand_PreservesCompositeFormatPlaceholdersAndAccessKeyCount()
    {
        const string neutral = "_Print Preview - {0:N2}";

        var pseudo = PseudoLocalization.Expand(neutral);

        pseudo.Should().Be("[[_PPrriinntt PPrreevviieeww - {0:N2}]]");
        AccessKeyCount(pseudo).Should().Be(AccessKeyCount(neutral));
        CompositePlaceholderTokens(pseudo)
            .Should()
            .BeEquivalentTo(CompositePlaceholderTokens(neutral));

        string.Format(CultureInfo.InvariantCulture, pseudo, 12.3)
            .Should()
            .Contain("12.30");
    }

    [Fact]
    public void PivotSortAndClipboardFeedbackResources_PseudoLocalizeWithoutLosingContracts()
    {
        string[] keys =
        [
            "ClipboardFeedback_CopyMultipleSelectionUnsupported",
            "ClipboardFeedback_CutMultipleSelectionUnsupported",
            "ClipboardFeedback_ReadFailed",
            "PivotSort_AscendingByLabels",
            "PivotSort_DescendingByLabels",
            "PivotSort_AscendingByValues",
            "PivotSort_DescendingByValues",
            "PivotSort_ValueFieldRequired",
        ];
        var neutralValues = ReadNeutralValues();

        foreach (var key in keys)
        {
            var neutral = neutralValues[key];
            var pseudo = PseudoLocalization.Expand(neutral);

            pseudo.Should().NotBe(neutral);
            pseudo.Length.Should().BeGreaterThan(neutral.Length);
            CompositePlaceholderTokens(pseudo).Should().BeEquivalentTo(CompositePlaceholderTokens(neutral));
        }

        using var cultureScope = TestCultureScope.CurrentCultureAndUICulture("qps-ploc");
        ClipboardFeedbackPlanner.MultiRangeSelectionUnsupported(isCut: false).Resolve(UiText.Get)
            .Should().Be(PseudoLocalization.Expand(neutralValues["ClipboardFeedback_CopyMultipleSelectionUnsupported"]));
        ClipboardFeedbackPlanner.MultiRangeSelectionUnsupported(isCut: true).Resolve(UiText.Get)
            .Should().Be(PseudoLocalization.Expand(neutralValues["ClipboardFeedback_CutMultipleSelectionUnsupported"]));
        ClipboardFeedbackPlanner.ReadFailed.Resolve(UiText.Get)
            .Should().Be(PseudoLocalization.Expand(neutralValues["ClipboardFeedback_ReadFailed"]));
    }

    [Theory]
    [MemberData(nameof(HighRiskShellRibbonAndDialogKeyData))]
    public void Expand_HighRiskShellRibbonAndDialogResources_PreservesLocalizationContracts(string key)
    {
        UiText.GetNeutralResourceKeys().Should().Contain(key);

        var neutralValues = ReadNeutralValues();
        var neutral = neutralValues[key];

        neutral.Should().NotBeNullOrEmpty();

        var pseudo = PseudoLocalization.Expand(neutral);

        pseudo.Should().NotBe(neutral);
        pseudo.Length.Should().BeGreaterThanOrEqualTo(
            neutral.Length + CountAsciiLettersOutsideCompositePlaceholders(neutral) + 4);
        AccessKeyCount(pseudo).Should().Be(AccessKeyCount(neutral));
        CompositePlaceholderTokens(pseudo)
            .Should()
            .BeEquivalentTo(CompositePlaceholderTokens(neutral));
    }

    private static Dictionary<string, string> ReadNeutralValues() =>
        ReadResxValues("Strings.resx");
}
