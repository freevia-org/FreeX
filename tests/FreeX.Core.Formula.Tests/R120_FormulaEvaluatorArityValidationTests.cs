using FluentAssertions;
using FreeX.Core.Formula;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R120-formula-entry-arity-validation: unit-level coverage of
/// <see cref="FormulaEvaluator.ValidateBuiltInFunctionArity"/>, the new choke point
/// <see cref="FreeX.App.Services.CellEntryParser.CreateCell"/> calls right after parsing so a
/// well-known built-in function invoked with too few/too many arguments is rejected at
/// formula-entry time instead of only ever surfacing later as a #VALUE! during recalculation.
/// See R120_CellEntryFormulaArityValidationTests (FreeX.App.Services.Tests) for coverage through
/// the real product entry point.
/// </summary>
public sealed class R120_FormulaEvaluatorArityValidationTests
{
    [Theory]
    [InlineData("IF(A1>0)")]           // IF requires 2 or 3; 1 supplied.
    [InlineData("IFERROR(A1)")]        // IFERROR requires exactly 2; 1 supplied.
    [InlineData("CHOOSE(1)")]          // CHOOSE requires at least 2; 1 supplied.
    public void ValidateBuiltInFunctionArity_TooFewArguments_Throws(string formula)
    {
        var ast = FormulaEvaluator.ParseFormula(formula);

        var act = () => FormulaEvaluator.ValidateBuiltInFunctionArity(ast);

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void ValidateBuiltInFunctionArity_TooManyArguments_Throws()
    {
        // LEFT allows at most 2 arguments (text, [num_chars]).
        var ast = FormulaEvaluator.ParseFormula("LEFT(\"x\",1,2,3)");

        var act = () => FormulaEvaluator.ValidateBuiltInFunctionArity(ast);

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void ValidateBuiltInFunctionArity_BadArityNestedInsideAnotherCall_Throws()
    {
        // The malformed call is an argument of SUM, not the top-level node -- the walker must
        // recurse into argument lists, not just check the outermost FunctionCallNode.
        var ast = FormulaEvaluator.ParseFormula("SUM(IF(A1>0),1)");

        var act = () => FormulaEvaluator.ValidateBuiltInFunctionArity(ast);

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void ValidateBuiltInFunctionArity_BadArityInsideBinaryOperand_Throws()
    {
        // The malformed call sits inside a binary-operator operand, exercising the BinaryOpNode
        // recursion branch specifically.
        var ast = FormulaEvaluator.ParseFormula("1+IF(A1>0)");

        var act = () => FormulaEvaluator.ValidateBuiltInFunctionArity(ast);

        act.Should().Throw<FormulaParseException>();
    }

    [Theory]
    [InlineData("IF(A1>0,1,2)")]
    [InlineData("IF(A1>0,1)")]
    [InlineData("LEFT(\"x\",1)")]
    public void ValidateBuiltInFunctionArity_ValidArity_DoesNotThrow(string formula)
    {
        var ast = FormulaEvaluator.ParseFormula(formula);

        var act = () => FormulaEvaluator.ValidateBuiltInFunctionArity(ast);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateBuiltInFunctionArity_SumWithManyArguments_DoesNotThrow()
    {
        // No-regression sibling: aggregate functions are genuinely variadic up to Excel's
        // 255-argument syntax limit (already SUM's registered MaxArgs) -- 40 arguments is well
        // within that limit and must still be accepted. See R126_AggregateFunctionArgumentCapTests
        // for coverage of the 255/256-argument boundary itself (R126-aggregate-arg-cap).
        var manyArgs = string.Join(",", Enumerable.Range(1, 40));
        var ast = FormulaEvaluator.ParseFormula($"SUM({manyArgs})");

        var act = () => FormulaEvaluator.ValidateBuiltInFunctionArity(ast);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateBuiltInFunctionArity_LambdaSpecialForm_DoesNotThrow()
    {
        // No-regression sibling: LET/LAMBDA/SINGLE/ANCHORARRAY are AST-aware special forms never
        // registered in BuiltInFunctions, so the validator must leave them alone entirely rather
        // than misreading them as unknown/invalid-arity built-ins.
        var ast = FormulaEvaluator.ParseFormula("LET(x,5,x*2)");

        var act = () => FormulaEvaluator.ValidateBuiltInFunctionArity(ast);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateBuiltInFunctionArity_UnknownName_DoesNotThrow()
    {
        // No-regression sibling: a not-yet-resolvable name (e.g. a Name-Manager custom LAMBDA
        // function, or a genuine #NAME? typo) has no statically known registered arity, so it
        // must be left alone here -- exactly like EvaluateFunction's own BuiltInFunctions.TryGet
        // carve-out for names outside the registry.
        var ast = FormulaEvaluator.ParseFormula("MyCustomFunction(1)");

        var act = () => FormulaEvaluator.ValidateBuiltInFunctionArity(ast);

        act.Should().NotThrow();
    }
}
