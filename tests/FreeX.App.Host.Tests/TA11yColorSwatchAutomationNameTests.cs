using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for J8: every Format Cells Font/Border/Fill/Pattern color-swatch button
/// (and the three "More ... Colors" pickers) must expose AutomationProperties.Name, reusing the
/// same localization key as its ToolTip, so screen readers announce something other than a blank
/// "Button" or the literal "..." when tabbing through the swatch grids.
/// </summary>
public sealed partial class TA11yColorSwatchAutomationNameTests
{
    [GeneratedRegex(
        "<Button\\b[\\s\\S]*?/>",
        RegexOptions.Compiled)]
    private static partial Regex ButtonElementPattern();

    private static readonly string[] SwatchClickHandlers =
    [
        "DlgFontColorSwatchButton_Click"
    ];

    [Fact]
    public void FormatCellsDialog_EveryColorSwatchButton_HasAutomationNameMatchingItsToolTipKey()
    {
        var xaml = DialogSourceTestSupport.ReadHostSourceFile("FormatCellsDialog.xaml");

        // Font swatches remain declarative. Fill and border palettes are validated below at their
        // typed dynamic construction sites.
        var swatchButtons = ButtonElementPattern().Matches(xaml)
            .Select(m => m.Value)
            .Where(button => SwatchClickHandlers.Any(handler =>
                button.Contains($"Click=\"{handler}\"", StringComparison.Ordinal)))
            .ToList();

        swatchButtons.Should().HaveCountGreaterThanOrEqualTo(8);

        foreach (var button in swatchButtons)
        {
            var toolTipMatch = Regex.Match(button, "ToolTip=\"\\{local:Loc Key=(?<key>[A-Za-z0-9_]+)\\}\"");
            toolTipMatch.Success.Should().BeTrue($"expected a ToolTip Loc key in: {button}");

            var expectedName = $"AutomationProperties.Name=\"{{local:Loc Key={toolTipMatch.Groups["key"].Value}}}\"";
            button.Should().Contain(
                expectedName,
                $"swatch button with ToolTip key '{toolTipMatch.Groups["key"].Value}' must expose an accessible name so screen readers don't announce it as blank or '...'");
        }
    }

    [Fact]
    public void FormatCellsDialog_DynamicBorderPalette_AssignsLocalizedAccessibleNames()
    {
        var source = DialogSourceTestSupport.ReadHostSourceFile("FormatCellsDialog.Border.cs");

        source.Should().Contain("foreach (var entry in FormatCellsBorderPalettePlanner.ColorEntries)");
        source.Should().Contain("var label = UiText.Get(entry.ResourceKey);");
        source.Should().Contain("AutomationProperties.SetName(button, label);");
        source.Should().Contain("ToolTip = label");
    }

    [Fact]
    public void FormatCellsDialog_MoreColorsPickerButtons_HaveNonDotAccessibleNames()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "FormatCellsDialog.Fill.cs",
            "FormatCellsDialog.Border.cs");

        source.Should().Contain("var label = UiText.Get(entry.ResourceKey);");
        source.Should().Contain("AutomationProperties.SetName(button, label);");
        source.Should().Contain("Content = entry.IsMore ? \"...\" : null");
        source.Should().Contain("button.Content = \"...\";");
    }
}
