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

    [Theory]
    [MemberData(nameof(HighRiskShellRibbonAndDialogKeyData))]
    public void Expand_HighRiskShellRibbonAndDialogResources_PreservesLocalizationContracts(string key)
    {
        UiText.GetNeutralResourceKeys().Should().Contain(key);

        var neutral = UiText.GetNeutral(key);

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
