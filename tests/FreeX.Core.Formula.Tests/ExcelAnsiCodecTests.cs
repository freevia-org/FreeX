using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class ExcelAnsiCodecTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Theory]
    [InlineData(0, '\0')]
    [InlineData(65, 'A')]
    [InlineData(128, '\u20AC')]
    [InlineData(159, '\u0178')]
    [InlineData(255, '\u00FF')]
    public void Decode_UsesExcelWindowsAnsiMapping(int code, char expected)
    {
        ExcelAnsiCodec.Decode(code).Should().Be(expected);
    }

    [Theory]
    [InlineData('A', 65)]
    [InlineData('\u20AC', 128)]
    [InlineData('\u0178', 159)]
    [InlineData('\u00FF', 255)]
    [InlineData('\u0100', 63)]
    [InlineData('\u754C', 63)]
    public void Encode_UsesExcelWindowsAnsiMappingAndReplacement(char value, int expected)
    {
        ExcelAnsiCodec.Encode(value).Should().Be(expected);
    }

    [Fact]
    public void CharAndCode_RetainFormulaSpecificDomainAndCoercionRules()
    {
        var sheet = new Sheet(SheetId.New(), "S");

        _evaluator.Evaluate("=CHAR(0)", sheet).Should().Be(ErrorValue.Value);
        _evaluator.Evaluate("=CHAR(255.9)", sheet).Should().Be(new TextValue("\u00FF"));
        _evaluator.Evaluate("=CHAR(256)", sheet).Should().Be(ErrorValue.Value);
        _evaluator.Evaluate("=CODE(\"\u20ACsuffix\")", sheet).Should().Be(new NumberValue(128));
        _evaluator.Evaluate("=CODE(\"\u0100\")", sheet).Should().Be(new NumberValue(63));
    }

    [Fact]
    public void FormulaOwnsExcelAnsiCodec_AndAccessibilityDelegatesFormulaEvaluation()
    {
        var formulaSource = TestWorkspaceFileLocator.ReadAllText(
            "src", "FreeX.Core.Formula", "BuiltInFunctions.TextAdvanced.cs");
        var accessibilitySource = TestWorkspaceFileLocator.ReadAllText(
            "src", "FreeX.Core.Commands", "AccessibilityCheckerService.Contrast.cs");

        formulaSource.Should().Contain("ExcelAnsiCodec.Decode(code)");
        formulaSource.Should().Contain("ExcelAnsiCodec.Encode(text[0])");
        accessibilitySource.Should().Contain("ConditionalFormatEvaluationSession");
        accessibilitySource.Should().NotContain("ExcelAnsiCodec");
        formulaSource.Should().NotContain("ExcelAnsiCodeToChar");
    }
}
