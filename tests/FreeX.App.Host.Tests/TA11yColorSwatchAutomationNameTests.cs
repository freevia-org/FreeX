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
        "DlgFontColorSwatchButton_Click",
        "DlgBorderLineColorSwatchButton_Click",
        "DlgFillSwatchButton_Click",
        "DlgFillPatternSwatchButton_Click"
    ];

    private static readonly string[] MoreColorsClickHandlers =
    [
        "DlgBorderLineColorPickerButton_Click",
        "DlgFillColorPickerButton_Click",
        "DlgFillPatternColorPickerButton_Click"
    ];

    [Fact]
    public void FormatCellsDialog_EveryColorSwatchButton_HasAutomationNameMatchingItsToolTipKey()
    {
        var xaml = DialogSourceTestSupport.ReadHostSourceFile("FormatCellsDialog.xaml");

        // Plain color swatches (Font/Border/Fill/Pattern) plus the three dot-content "More ...
        // Colors" pickers -- excludes the separate, already-labeled "Pick"/"Pick2" text buttons
        // that share the same Click handlers but already have visible Content.
        var swatchButtons = ButtonElementPattern().Matches(xaml)
            .Select(m => m.Value)
            .Where(button =>
                SwatchClickHandlers.Any(handler => button.Contains($"Click=\"{handler}\"", StringComparison.Ordinal)) ||
                (MoreColorsClickHandlers.Any(handler => button.Contains($"Click=\"{handler}\"", StringComparison.Ordinal)) &&
                 button.Contains("Content=\"...\"", StringComparison.Ordinal)))
            .ToList();

        // Sanity check: this must actually exercise the ~54 swatch/picker buttons described by
        // the finding (Font 8 + Border 7 + Fill 29 + Pattern 7, plus the 3 "More ... Colors"
        // pickers), not silently match zero elements if the XAML shape changes.
        swatchButtons.Should().HaveCountGreaterThanOrEqualTo(54);

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
    public void FormatCellsDialog_MoreColorsPickerButtons_HaveNonDotAccessibleNames()
    {
        var xaml = DialogSourceTestSupport.ReadHostSourceFile("FormatCellsDialog.xaml");

        foreach (var clickHandler in MoreColorsClickHandlers)
        {
            var button = ButtonElementPattern().Matches(xaml)
                .Select(m => m.Value)
                .Single(b => b.Contains($"Click=\"{clickHandler}\"", StringComparison.Ordinal)
                    && b.Contains("Content=\"...\"", StringComparison.Ordinal));

            button.Should().MatchRegex("AutomationProperties\\.Name=\"\\{local:Loc Key=[A-Za-z0-9_]+\\}\"");
        }
    }
}
