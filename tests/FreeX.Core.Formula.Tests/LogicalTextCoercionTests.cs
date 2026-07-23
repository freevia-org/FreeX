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

    // R65-formula-logical-6-1: NOT routed through ToBool (no numeric-text case) instead of
    // TryDirectLogicalBool (the helper AND/OR/XOR use), so NOT("1")/NOT("0") wrongly errored.
    [Theory]
    [InlineData("=NOT(\"1\")", false)]
    [InlineData("=NOT(\"0\")", true)]
    public void Not_CoercesNumericDirectText(string formula, bool expected) =>
        Eval(formula).Should().Be(new BoolValue(expected));

    [Fact]
    public void Not_CellWithNumericText_Coerces()
    {
        // A1 holds the numeric-text "0" (not a direct literal) -- must coerce the same way.
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("0"));
        _evaluator.Evaluate("=NOT(A1)", sheet, wb).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Not_BlankCell_CoercesToFalseThenNegates()
    {
        // Regression guard: TryDirectLogicalBool has no BlankValue case (AND/OR/XOR handle
        // blank cells via ReferencedScalarValue instead), so NotScalar must special-case blank
        // explicitly rather than falling through to #VALUE!. NOT(<blank>) = NOT(FALSE) = TRUE.
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=NOT(A1)", sheet, wb).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Not_NonNumericText_IsValueError() =>
        Eval("=NOT(\"abc\")").Should().Be(ErrorValue.Value);

    [Theory]
    [InlineData("=NOT(TRUE)", false)]
    [InlineData("=NOT(FALSE)", true)]
    [InlineData("=NOT(1)", false)]
    [InlineData("=NOT(0)", true)]
    public void Not_BooleanAndNumeric_Unaffected(string formula, bool expected) =>
        Eval(formula).Should().Be(new BoolValue(expected));

    // R80-formula-logical-lambda-5-1: a trailing/embedded comma leaves a direct argument
    // omitted, which the parser turns into an OmittedArgumentNode that evaluates to a raw
    // BlankValue (not wrapped in ReferencedScalarValue, since it isn't a cell reference).
    // TryDirectLogicalBool previously had no BlankValue case, so this fell to `default: return
    // false`, which AND/OR/XOR's callers interpreted as "cannot coerce" -> #VALUE!. Excel treats
    // an omitted direct logical argument as FALSE, same as a blank cell reference.
    [Theory]
    [InlineData("=AND(TRUE,)", false)]
    [InlineData("=OR(FALSE,)", false)]
    [InlineData("=XOR(,)", false)]
    public void Logical_DirectOmittedArgument_CoercesToFalse(string formula, bool expected) =>
        Eval(formula).Should().Be(new BoolValue(expected));

    // No-regression sibling: an omitted argument must still only contribute FALSE, not flip an
    // otherwise-true result, and a determining FALSE earlier in the list still short-circuits.
    [Theory]
    [InlineData("=AND(TRUE,,TRUE)", false)]
    [InlineData("=OR(TRUE,)", true)]
    [InlineData("=XOR(TRUE,)", true)]
    [InlineData("=XOR(TRUE,,TRUE)", false)]
    public void Logical_DirectOmittedArgument_DoesNotOverrideOtherArgs(string formula, bool expected) =>
        Eval(formula).Should().Be(new BoolValue(expected));
}
