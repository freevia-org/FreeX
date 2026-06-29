using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FormatCellsDialogPolicySourceGuardTests
{
    [Fact]
    public void FormatCellsDialog_DelegatesAcceptancePolicyToServicesPlanner()
    {
        var source = ReadDialogSources();

        source.Should().Contain("using FreeX.App.Services;");
        source.Should().Contain("FormatCellsDialogPlanner.TryCreateResult(");
        source.Should().Contain("CreatePlannerInput()");
        source.Should().Contain("ShowPlannerValidation(validation!)");
        source.Should().Contain("new FormatCellsDialogNumberInput(");
        source.Should().Contain("new FormatCellsDialogFontInput(");
        source.Should().Contain("new FormatCellsDialogFillInput(");
        source.Should().Contain("new FormatCellsDialogAlignmentInput(");
        source.Should().Contain("new FormatCellsDialogBorderInput(");
        source.Should().Contain("new FormatCellsDialogProtectionInput(");
    }

    [Fact]
    public void FormatCellsDialog_DoesNotOwnResultConstructionValidationOrColorParsing()
    {
        var source = ReadDialogSources();

        source.Should().NotContain("new StyleDiff(");
        source.Should().NotContain("ValidateNumberInputs");
        source.Should().NotContain("ValidateBorderInputs");
        source.Should().NotContain("TryParseRequiredColor");
        source.Should().NotContain("TryParseOptionalColor");
        source.Should().NotContain("FormatCellsInputParser.TryParseFontSize");
        source.Should().NotContain("FormatCellsInputParser.TryParseIndentLevel");
        source.Should().NotContain("FormatCellsInputParser.IsSupportedCustomNumberFormat");
        source.Should().NotContain("int.TryParse(NumberDecimalPlacesBox");
    }

    [Fact]
    public void FormatCellsDialog_DelegatesFontFillAndBorderChoicesToPlanner()
    {
        var source = ReadDialogSources();

        source.Should().Contain("FormatCellsDialogPlanner.FontStyleLabel(");
        source.Should().Contain("FormatCellsDialogPlanner.IsFontStyleBold(");
        source.Should().Contain("FormatCellsDialogPlanner.CreateFillPatternDisplayChoices(UiText.Get)");
        source.Should().Contain("FormatCellsDialogPlanner.ResolveFillPatternStyle(");
        source.Should().Contain("FormatCellsDialogPlanner.GetFillPatternResourceKey(");
        source.Should().Contain("FormatCellsDialogPlanner.NextBorderSideStyle(");
        source.Should().Contain("FormatCellsDialogPlanner.CreateSelectedBorderLine(");
    }

    private static string ReadDialogSources() =>
        DialogSourceTestSupport.ReadHostSources(
            "FormatCellsDialog.xaml.cs",
            "FormatCellsDialog.Number.cs",
            "FormatCellsDialog.Font.cs",
            "FormatCellsDialog.Fill.cs",
            "FormatCellsDialog.Border.cs");
}
