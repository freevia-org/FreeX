using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// Excel coerces a DIRECT NUMERIC-text argument to AND/OR/XOR (a numeric string to a number, <>0 = TRUE);
// non-numeric text — including the words "TRUE"/"FALSE" — is #VALUE!. Text *inside a referenced range* is
// ignored (covered elsewhere). Regression for the fidelity-batch finding AND("1",1) => #VALUE! (Excel TRUE).
public class LogicalTextCoercionTests
{
    private readonly FormulaEvaluator _evaluator = new();

    private ScalarValue Eval(string formula)
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        return _evaluator.Evaluate(formula, sheet, wb);
    }

    [Theory]
    [InlineData("=AND(\"1\",1)", true)]
    [InlineData("=AND(\"1\",\"1\")", true)]
    [InlineData("=AND(\"0\",1)", false)]
    [InlineData("=AND(\"2.5\",1)", true)]
    public void And_CoercesNumericDirectText(string formula, bool expected) =>
        Eval(formula).Should().Be(new BoolValue(expected));

    [Theory]
    [InlineData("=OR(\"0\",0)", false)]
    [InlineData("=OR(\"1\",0)", true)]
    [InlineData("=OR(\"-3\",0)", true)]
    public void Or_CoercesNumericDirectText(string formula, bool expected) =>
        Eval(formula).Should().Be(new BoolValue(expected));

    // Regression for R31-formula-logical-lambda-1: XOR had its own un-mirrored special case
    // (`if (a is TextValue) return ErrorValue.Value;`) that skipped the AND/OR numeric-text
    // coercion path entirely, so XOR("1",1) wrongly errored instead of returning FALSE.
    [Theory]
    [InlineData("=XOR(\"1\",1)", false)]
    [InlineData("=XOR(\"0\",1)", true)]
    public void Xor_CoercesNumericDirectText(string formula, bool expected) =>
        Eval(formula).Should().Be(new BoolValue(expected));

    [Theory]
    [InlineData("=AND(\"abc\",1)")]
    [InlineData("=OR(\"abc\",0)")]
    [InlineData("=XOR(\"abc\",1)")]
    [InlineData("=AND(\"TRUE\",1)")]   // the word "TRUE" as text is not numeric -> #VALUE! (matches Excel)
    [InlineData("=OR(\"FALSE\",0)")]
    public void Logical_NonNumericText_IsValueError(string formula) =>
        Eval(formula).Should().Be(ErrorValue.Value);

    [Theory]
    [InlineData("=AND(1,1)", true)]
    [InlineData("=AND(0,1)", false)]
    [InlineData("=OR(0,0)", false)]
    [InlineData("=OR(1,0)", true)]
    public void Logical_NumericAndBoolean_Unaffected(string formula, bool expected) =>
        Eval(formula).Should().Be(new BoolValue(expected));

    [Fact]
    public void And_IgnoresTextInsideReferencedRange()
    {
        // A cell containing non-numeric text inside a range is ignored (not coerced, not an error).
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("abc"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(true));
        _evaluator.Evaluate("=AND(A1:A2)", sheet, wb).Should().Be(new BoolValue(true));
    }
}
