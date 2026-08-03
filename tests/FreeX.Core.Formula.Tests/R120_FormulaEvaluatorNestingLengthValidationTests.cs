using FluentAssertions;
using FreeX.Core.Formula;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R120-formula-entry-nesting-length-validation: unit-level coverage of
/// <see cref="FormulaEvaluator.ValidateFunctionNestingDepth"/> and
/// <see cref="FormulaEvaluator.ValidateFormulaEntryLength"/>, the new choke points
/// <see cref="FreeX.App.Services.CellEntryParser.CreateCell"/> calls right after parsing (and, for
/// the length check, before parsing) so a formula exceeding Excel's documented 64-level
/// function-nesting limit or 8,192-character length limit is rejected at formula-entry time
/// instead of being silently accepted just because it stays under the parser's much larger
/// internal DoS-guard caps (<see cref="FormulaSafetyLimits.MaxParseNesting"/> = 256,
/// <see cref="FormulaSafetyLimits.MaxParseDepth"/> = 512). See
/// R120_CellEntryFormulaNestingLengthValidationTests (FreeX.App.Services.Tests) for coverage
/// through the real product entry point.
/// </summary>
public sealed class R120_FormulaEvaluatorNestingLengthValidationTests
{
    private static string BuildNestedIfFormula(int nestingLevels)
    {
        // Builds IF(IF(IF(...,1,1),1,1),1,1) with exactly `nestingLevels` nested IF() calls.
        var formula = "1";
        for (var i = 0; i < nestingLevels; i++)
            formula = $"IF({formula},1,1)";
        return formula;
    }

    [Fact]
    public void ValidateFunctionNestingDepth_100NestedIfCalls_Throws()
    {
        // 100 > Excel's documented 64-level function-nesting limit, and 100 < the parser's own
        // internal MaxParseNesting DoS guard (256) -- so this formula parses successfully but
        // must still be rejected by the new Excel-parity check.
        var ast = FormulaEvaluator.ParseFormula(BuildNestedIfFormula(100));

        var act = () => FormulaEvaluator.ValidateFunctionNestingDepth(ast);

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void ValidateFunctionNestingDepth_ExactlySixtyFour_DoesNotThrow()
    {
        var ast = FormulaEvaluator.ParseFormula(BuildNestedIfFormula(64));

        var act = () => FormulaEvaluator.ValidateFunctionNestingDepth(ast);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateFunctionNestingDepth_SixtyFivePlusOne_Throws()
    {
        var ast = FormulaEvaluator.ParseFormula(BuildNestedIfFormula(65));

        var act = () => FormulaEvaluator.ValidateFunctionNestingDepth(ast);

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void ValidateFunctionNestingDepth_ManySiblingCallsNotNested_DoesNotThrow()
    {
        // No-regression sibling: 100 function calls that are SIBLINGS (arguments of one SUM), not
        // nested inside each other, must not trip the nesting check -- only actual
        // function-in-function nesting counts, matching Excel (which lets you SUM() together as
        // many non-nested calls as its 255-argument syntax limit allows).
        var args = string.Join(",", Enumerable.Range(1, 100).Select(n => $"ABS({-n})"));
        var ast = FormulaEvaluator.ParseFormula($"SUM({args})");

        var act = () => FormulaEvaluator.ValidateFunctionNestingDepth(ast);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateFunctionNestingDepth_NestingInsideBinaryOperand_Throws()
    {
        // The over-nested call sits inside a binary-operator operand, exercising the BinaryOpNode
        // pass-through branch specifically (mirrors ValidateBuiltInFunctionArity's own binary-
        // operand coverage).
        var ast = FormulaEvaluator.ParseFormula($"1+{BuildNestedIfFormula(100)}");

        var act = () => FormulaEvaluator.ValidateFunctionNestingDepth(ast);

        act.Should().Throw<FormulaParseException>();
    }

    [Theory]
    [InlineData("IF(A1>0,1,2)")]
    [InlineData("SUM(A1:A10)")]
    [InlineData("1+2*3")]
    public void ValidateFunctionNestingDepth_OrdinaryFormulas_DoesNotThrow(string formula)
    {
        var ast = FormulaEvaluator.ParseFormula(formula);

        var act = () => FormulaEvaluator.ValidateFunctionNestingDepth(ast);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateFormulaEntryLength_ExceedsMax_Throws()
    {
        var enteredText = "=" + string.Join("+", Enumerable.Repeat("1", 5000));
        enteredText.Length.Should().BeGreaterThan(FormulaEvaluator.MaxFormulaEntryLength);

        var act = () => FormulaEvaluator.ValidateFormulaEntryLength(enteredText);

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void ValidateFormulaEntryLength_AtMax_DoesNotThrow()
    {
        var enteredText = "=" + new string('1', FormulaEvaluator.MaxFormulaEntryLength - 1);
        enteredText.Length.Should().Be(FormulaEvaluator.MaxFormulaEntryLength);

        var act = () => FormulaEvaluator.ValidateFormulaEntryLength(enteredText);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateFormulaEntryLength_OrdinaryShortFormula_DoesNotThrow()
    {
        var act = () => FormulaEvaluator.ValidateFormulaEntryLength("=IF(A1>0,1,2)");

        act.Should().NotThrow();
    }
}
