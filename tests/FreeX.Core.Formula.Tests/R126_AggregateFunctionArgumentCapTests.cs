using System.Linq;
using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R126-aggregate-arg-cap: aggregate/variadic functions (SUM, AVERAGE, AND, OR, MEDIAN,
/// CONCATENATE, and the rest of <c>FormulaEvaluator.AggregateFunctions</c>) are genuinely
/// variadic in Excel, but NOT unbounded -- Excel caps every function, including these, at 255
/// syntax arguments (already each function's registered <c>MaxArgs</c> in
/// <see cref="BuiltInFunctions"/>). Typing a 256th comma-separated argument to any of them pops
/// Excel's "You've entered too many arguments for this function" dialog and refuses to commit.
///
/// Previously both <see cref="FormulaEvaluator.ValidateBuiltInFunctionArity"/> (the formula-entry
/// gate) and <see cref="FormulaEvaluator"/>'s own recalculation-time arity check in
/// FormulaEvaluator.Functions.cs explicitly skipped the upper-bound check whenever
/// <c>IsAggregateFunction</c> was true, so no amount of literal arguments was ever rejected for
/// this function family. This suite proves both choke points now enforce the registered 255-arg
/// cap uniformly, without regressing the family's genuine variadic behaviour below that cap.
///
/// Note throughout: the cap is on literal comma-separated argument SLOTS in the formula syntax,
/// not on expanded cell values -- =SUM(A1:A10000) is a single range argument and stays
/// unaffected; only e.g. =SUM(1,2,3,...,256 literals) is rejected, matching Excel exactly.
/// </summary>
public sealed class R126_AggregateFunctionArgumentCapTests
{
    private readonly FormulaEvaluator _evaluator = new();

    // Representative cross-section of FormulaEvaluator.AggregateFunctions covering every
    // registered MinArgs shape in that set (1 for most, 2 for NPV) and a spread of argument
    // kinds (numeric, logical, textual) -- proving the fix at the shared choke point applies
    // uniformly across the family rather than function-by-function.
    public static TheoryData<string, int> AggregateFunctionsWithMinArgs => new()
    {
        { "SUM", 1 },
        { "AVERAGE", 1 },
        { "AND", 1 },
        { "OR", 1 },
        { "MEDIAN", 1 },
        { "CONCATENATE", 1 },
        { "PRODUCT", 1 },
        { "MIN", 1 },
        { "MAX", 1 },
        { "COUNT", 1 },
        { "XOR", 1 },
        { "GCD", 1 },
        { "LCM", 1 },
        { "VAR", 1 },
        { "STDEV", 1 },
        { "MODE", 1 },
        { "GEOMEAN", 1 },
        { "HARMEAN", 1 },
        { "AVEDEV", 1 },
        { "SUMSQ", 1 },
        { "NPV", 2 },
    };

    private static string BuildCall(string functionName, int argumentCount)
    {
        // Plain positive integer literals are valid arguments to every function in the family:
        // numeric for SUM/AVERAGE/etc., truthy for AND/OR/XOR, and text-coercible for
        // CONCATENATE. NPV's first argument is conventionally a rate; a small positive number
        // works fine there too.
        var args = string.Join(",", Enumerable.Range(1, argumentCount));
        return $"{functionName}({args})";
    }

    [Theory]
    [MemberData(nameof(AggregateFunctionsWithMinArgs))]
    public void ValidateBuiltInFunctionArity_AggregateWith256Arguments_Throws(string functionName, int minArgs)
    {
        _ = minArgs;
        var ast = FormulaEvaluator.ParseFormula(BuildCall(functionName, 256));

        var act = () => FormulaEvaluator.ValidateBuiltInFunctionArity(ast);

        act.Should().Throw<FormulaParseException>(
            $"Excel caps {functionName} (and every other function) at 255 arguments, exactly like its registered MaxArgs");
    }

    [Theory]
    [MemberData(nameof(AggregateFunctionsWithMinArgs))]
    public void ValidateBuiltInFunctionArity_AggregateWith255Arguments_DoesNotThrow(string functionName, int minArgs)
    {
        _ = minArgs;
        var ast = FormulaEvaluator.ParseFormula(BuildCall(functionName, 255));

        var act = () => FormulaEvaluator.ValidateBuiltInFunctionArity(ast);

        act.Should().NotThrow(
            $"255 arguments is exactly {functionName}'s registered MaxArgs and must still be accepted");
    }

    [Fact]
    public void Evaluate_SumWith256LiteralArguments_ReturnsValueError()
    {
        // Recalculation-time enforcement (FormulaEvaluator.Functions.cs EvaluateFunction), the
        // second of the two previously-exempted choke points, reached through the real
        // evaluator entry point rather than by calling an internal method directly.
        var sheet = new Sheet(SheetId.New(), "S");

        var result = _evaluator.Evaluate("=" + BuildCall("SUM", 256), sheet);

        result.Should().Be(ErrorValue.Value,
            "Excel rejects a 256-argument SUM call outright; FreeX's recalc path must match, not silently sum all 256");
    }

    [Fact]
    public void Evaluate_SumWith255LiteralArguments_StillSumsCorrectly()
    {
        // No-regression sibling: exactly at the boundary (255, SUM's registered MaxArgs) the
        // call must still evaluate normally and produce the correct total -- the fix must not
        // become stricter than Excel's own limit.
        var sheet = new Sheet(SheetId.New(), "S");
        var expectedTotal = Enumerable.Range(1, 255).Sum();

        var result = _evaluator.Evaluate("=" + BuildCall("SUM", 255), sheet);

        result.Should().Be(new NumberValue(expectedTotal));
    }

    [Fact]
    public void Evaluate_SumOfLargeRange_StillWorksUnaffectedByArgumentCap()
    {
        // No-regression sibling: the cap counts literal comma-separated argument slots, not
        // expanded cell values, so a single range argument spanning far more than 255 cells
        // must remain completely unaffected by this fix.
        var sheet = new Sheet(SheetId.New(), "S");
        for (uint row = 1; row <= 400; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(1));

        var result = _evaluator.Evaluate("=SUM(A1:A400)", sheet);

        result.Should().Be(new NumberValue(400));
    }

    [Fact]
    public void ValidateBuiltInFunctionArity_NonAggregateFunctionStillRejectedIdentically()
    {
        // No-regression sibling: the fix removes the isAggregate branch entirely from the
        // upper-bound check, so it must not have accidentally changed behaviour for ordinary
        // (non-aggregate) functions, which were never exempted in the first place.
        var ast = FormulaEvaluator.ParseFormula("LEFT(\"x\",1,2,3)");

        var act = () => FormulaEvaluator.ValidateBuiltInFunctionArity(ast);

        act.Should().Throw<FormulaParseException>();
    }
}
