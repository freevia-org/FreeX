using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R28-lambda-helpers-deep-2: direct LAMBDA invocation rejected omitted trailing optional
/// parameters unless the caller wrote an explicit trailing comma (e.g. f(1,)). Calling a
/// 2-parameter lambda with just one argument (f(5), no trailing comma) -- the normal Excel
/// idiom for omitting trailing optional parameters, exactly like LEFT(text) omitting
/// num_chars -- produced argNodes.Count != lambda.Parameters.Count and was rejected with
/// #VALUE! before any ISOMITTED binding could happen. Real Excel returns 5 for
/// =LET(f, LAMBDA(x,y, IF(ISOMITTED(y), x, x+y)), f(5)). Fixed in
/// InvokeLambdaWithArgs (FormulaEvaluator.LocalScopes.cs) to only reject calls that supply
/// MORE arguments than the lambda declares, padding any missing trailing arguments with the
/// "omitted" sentinel the same way an explicit trailing comma already did.
/// </summary>
public sealed class Round28LambdaOmittedTrailingArgTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet Sheet() => new(SheetId.New(), "S");

    [Fact]
    public void Lambda_OmittedTrailingArg_NoTrailingComma_IsOmittedAndUsesDefault()
    {
        // Bug case: f(5) with no trailing comma used to return #VALUE! instead of binding y
        // to the omitted sentinel.
        var result = _eval.Evaluate(
            "=LET(f, LAMBDA(x,y, IF(ISOMITTED(y), x, x+y)), f(5))", Sheet());
        Assert.Equal(new NumberValue(5.0), result);
    }

    [Fact]
    public void Lambda_OmittedTrailingArg_ExplicitTrailingComma_StillWorks()
    {
        // Sibling (already-working) case: the explicit-comma form f(1,) must keep working.
        var result = _eval.Evaluate(
            "=LET(f, LAMBDA(x,y, IF(ISOMITTED(y), \"Missing second argument\", x+y)), f(1,))",
            Sheet());
        Assert.Equal(new TextValue("Missing second argument"), result);
    }

    [Fact]
    public void Lambda_AllArgumentsProvided_StillComputesNormally()
    {
        // Sibling (already-working) case: supplying every argument must be unaffected.
        var result = _eval.Evaluate(
            "=LET(f, LAMBDA(x,y, IF(ISOMITTED(y), x, x+y)), f(5,7))", Sheet());
        Assert.Equal(new NumberValue(12.0), result);
    }

    [Fact]
    public void Lambda_TooManyArguments_StillReturnsValueError()
    {
        // Must NOT swing to the opposite extreme: calling with MORE arguments than the
        // lambda declares is still an arity error.
        var result = _eval.Evaluate(
            "=LET(f, LAMBDA(x,y, x+y), f(1,2,3))", Sheet());
        Assert.Equal(ErrorValue.Value, result);
    }

    [Fact]
    public void Lambda_OmittedMiddleArg_ViaExplicitComma_BindsOmittedNotTrailingOnly()
    {
        // Sibling: an omitted argument in a non-trailing position (via explicit comma) still
        // binds the omitted sentinel for that specific parameter.
        var result = _eval.Evaluate(
            "=LET(f, LAMBDA(x,y,z, IF(ISOMITTED(y), x+z, x+y+z)), f(1,,3))", Sheet());
        Assert.Equal(new NumberValue(4.0), result);
    }
}
