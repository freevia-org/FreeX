using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

public sealed class FormulaParseRobustnessTests
{
    // An unparseable formula (e.g. a phone number stored as "+389 78 609-030") must evaluate to an
    // error value rather than throw out of Evaluate, so direct callers (formula bar, tools, parity
    // checks) behave like a recalc — which already maps a parse failure to #VALUE!.
    [Theory]
    [InlineData("=+389 78 609-030")]
    [InlineData("=1 2 3")]
    public void Evaluate_UnparseableFormula_ReturnsValueError_WithoutThrowing(string formula)
    {
        var sheet = new Sheet(SheetId.New(), "S");

        var evaluate = () => new FormulaEvaluator().Evaluate(formula, sheet);

        evaluate.Should().NotThrow();
        evaluate().Should().Be(ErrorValue.Value);
    }
}
