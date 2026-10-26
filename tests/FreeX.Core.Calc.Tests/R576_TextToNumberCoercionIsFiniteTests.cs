using FluentAssertions;

using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// r576: Excel's text-to-number coercion decides what <c>="1"+1</c> and <c>=VALUE(x)</c> mean, and
/// <c>ExcelTextNumberParser.TryParseNumericStrict</c> performs it with three unguarded
/// <c>double.TryParse</c> returns. Since .NET accepts the literals "NaN" and "Infinity" and
/// overflows a long digit run, a formula over TEXT could produce a non-finite result -- which
/// lands in a cell.
///
/// <para>That is the model invariant r562 disproved and r563/r569 defended at the other writers:
/// a cell value must be finite. This is the FORMULA door, the one r562's correction predicted
/// would exist somewhere and no round had checked.</para>
///
/// <para>Excel agrees: its coercion does not recognise those words, so <c>="Infinity"*1</c> is
/// #VALUE! rather than a number. Rejecting them is alignment as well as invariant.</para>
/// </summary>
public sealed class R576_TextToNumberCoercionIsFiniteTests
{
    private static ScalarValue Evaluate(string formulaText)
    {
        var workbook = new Workbook("Coercion");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new Cell { FormulaText = formulaText });

        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator())
            .RecalculateAllFormulas(workbook);

        return sheet.GetCell(address)?.Value ?? BlankValue.Instance;
    }

    [Theory]
    [InlineData("=\"Infinity\"*1")]
    [InlineData("=\"NaN\"*1")]
    [InlineData("=\"1e400\"*1")]
    [InlineData("=\"-Infinity\"+0")]
    public void Coercing_text_never_yields_a_non_finite_cell_value(string formula)
    {
        var value = Evaluate(formula);

        (value is not NumberValue number || double.IsFinite(number.Value))
            .Should().BeTrue("the cell became " + value);
    }

    [Theory]
    [InlineData("=VALUE(\"Infinity\")")]
    [InlineData("=VALUE(\"NaN\")")]
    public void Value_of_a_non_numeric_literal_never_yields_a_non_finite_cell(string formula)
    {
        var value = Evaluate(formula);

        (value is not NumberValue number || double.IsFinite(number.Value))
            .Should().BeTrue("the cell became " + value);
    }

    [Fact]
    public void Ordinary_text_coercion_still_works()
    {
        // Non-vacuity: a guard that rejected every coercion would satisfy the assertions above
        // while breaking ="1"+1, which Excel evaluates to 2.
        Evaluate("=\"1\"+1").Should().BeOfType<NumberValue>().Which.Value.Should().Be(2);
        Evaluate("=\"2.5\"*2").Should().BeOfType<NumberValue>().Which.Value.Should().Be(5);
        Evaluate("=VALUE(\"42\")").Should().BeOfType<NumberValue>().Which.Value.Should().Be(42);
    }

    [Fact]
    public void Ordinary_arithmetic_overflow_is_unaffected()
    {
        // Guards the boundary of the change: arithmetic that genuinely overflows is Excel's
        // #NUM!, decided by the evaluator, not by the text parser this round touches.
        var value = Evaluate("=1E308*10");

        (value is not NumberValue number || double.IsFinite(number.Value))
            .Should().BeTrue("the cell became " + value);
    }

    [Fact]
    public void An_overflowing_literal_still_reaches_the_cell_as_infinity()
    {
        // Recorded as a KNOWN GAP rather than fixed, so it is visible instead of silent.
        //
        // VALUE("1E309") still yields Infinity, and this test pins that so the state of the code
        // is written down. It is not fixed because
        // AccessibilityCheckerServiceTests.FindIssues_UsesCanonicalFormulaConditionalFormatValue
        // FunctionOperandCoercionAndErrorSemantics deliberately expects VALUE("1E309")>0 to be
        // TRUE, in a test whose whole subject is this function's coercion and error semantics.
        //
        // Excel most likely answers #NUM! there -- its own range stops near 9.99E307 -- but Excel
        // COM is not registered on this machine, and overturning a deliberately pinned expectation
        // on an unverified reading is exactly what r516 had to retract. What settles it: evaluate
        // =VALUE("1E309") in real Excel. If it is #NUM!, both this test and the accessibility
        // expectation change together.
        var value = Evaluate("=VALUE(\"1E309\")");

        value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(double.PositiveInfinity);
    }
}
