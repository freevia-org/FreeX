using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

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
        TokenSet(pseudo, CompositeFormatPlaceholderPattern())
            .Should()
            .BeEquivalentTo(TokenSet(neutral, CompositeFormatPlaceholderPattern()));

        string.Format(CultureInfo.InvariantCulture, pseudo, 12.3)
            .Should()
            .Contain("12.30");
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
            neutral.Length + CountExpandableLettersOutsidePlaceholders(neutral) + 4);
        AccessKeyCount(pseudo).Should().Be(AccessKeyCount(neutral));
        TokenSet(pseudo, CompositeFormatPlaceholderPattern())
            .Should()
            .BeEquivalentTo(TokenSet(neutral, CompositeFormatPlaceholderPattern()));
    }

    private static Dictionary<string, string> ReadNeutralValues()
    {
        var path = WorkspaceFileLocator.Find("src", "FreeX.App.Host", "Resources", "Strings.resx");
        return XDocument.Load(path)
            .Descendants("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static HashSet<string> TokenSet(string value, Regex pattern) =>
        pattern.Matches(value)
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);

    private static int AccessKeyCount(string value) =>
        AccessKeyPattern().Matches(value).Count;

    private static int CountExpandableLettersOutsidePlaceholders(string value) =>
        CompositeFormatPlaceholderPattern()
            .Replace(value, string.Empty)
            .Count(IsAsciiLetter);

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    [GeneratedRegex(@"\{[^{}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex CompositeFormatPlaceholderPattern();

    [GeneratedRegex(@"(?<!_)_(?!_)", RegexOptions.CultureInvariant)]
    private static partial Regex AccessKeyPattern();
}
