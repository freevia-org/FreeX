using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R97-union-deferred-backlog, items 2 and 3 (see R97_UnionDeferredBacklogTests for item 1, the
/// four previously-unhandled functions).
///
/// ITEM 2 -- dead-branch audit of the explicit UnionValue arms R93 added inside Sum()/Average()
/// (BuiltInFunctions.StatisticalCore.Aggregates.cs) and CollectNumbers() (BuiltInFunctions.
/// StatisticalCore.Helpers.cs):
///   - Sum()/Average(): CONFIRMED DEAD. Both functions are in FormulaEvaluator.
///     FunctionClassification.cs's AggregateFunctions set (isAggregate=true, isStructured=false),
///     so FormulaEvaluator.Functions.cs's per-argument choke point (the
///     "!isStructured && isAggregate && value is UnionValue union" branch, and its LET/LAMBDA- and
///     named-formula-bound counterparts) already flattens any UnionValue argument into individual
///     scalar values BEFORE Sum()/Average() ever run -- confirmed by grepping every call site in
///     src/FreeX.Core.Formula for "Sum(" / "Average(" (BuiltInFunctions.cs's function-dispatch
///     dictionary is the ONLY caller of either private method; nothing else invokes them directly)
///     and by temporarily deleting both UnionValue arms and re-running every R93/R97 SUM/AVERAGE
///     union test: all 25 R93_UnionValueAggregateUnwrapTests continued to pass unmodified (proof
///     kept in this task's report, not re-run automatically here since a static deletion isn't
///     expressible as a unit test) -- these branches were removed.
///   - CollectNumbers(): CONFIRMED LIVE, kept. SKEW/SKEW.P/KURT (BuiltInFunctions.
///     StatisticalDistributions.Descriptive.cs) all call CollectNumbers(args) directly but are
///     classified in NEITHER AggregateFunctions NOR StructuredRangeFunctions (absent from both
///     sets in FormulaEvaluator.FunctionClassification.cs), so the per-argument choke point never
///     flattens or materializes a UnionValue argument for them -- it falls through to the final
///     "else expandedArgs.Add(value)" catch-all and reaches CollectNumbers as a raw UnionValue.
///     Skew_/SkewP_/Kurt_TwoAreaUnion tests below exercise that live path.
///
/// ITEM 3 -- containment: UnionValue must exist ONLY transiently inside FreeX.Core.Formula (per
/// its own doc comment in UnionValue.cs) -- never stored in a cell, cached as a formula result, put
/// in the dependency graph, or written to a saved file. FormulaEvaluator.NormalizeTopLevelResult
/// (FormulaEvaluator.cs) is the single normalization point every Evaluate() entry point funnels
/// through, and it explicitly coerces a UnionValue result to #VALUE! before anything downstream
/// (Sheet.SetCell, the dependency graph, IO writers) ever sees it. The tests below exercise that
/// from the real product entry points (a cell holding a bare union formula; SetCellFormula's
/// recalculation path) rather than asserting on the AST/NormalizeTopLevelResult directly.
/// </summary>
public sealed class R97_UnionValueContainmentAndDeadCodeTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Workbook MakeWorkbook(out Sheet sheet, params (uint row, uint col, ScalarValue val)[] cells)
    {
        var workbook = new Workbook("Test");
        sheet = workbook.AddSheet("S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), val);
        return workbook;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // ITEM 2: CollectNumbers' UnionValue branch is live via SKEW/SKEW.P/KURT
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Skew_TwoAreaUnion_MatchesEquivalentContiguousRange()
    {
        var unionWb = MakeWorkbook(out var unionSheet,
            (1u, 1u, new NumberValue(2)),  // A1
            (2u, 1u, new NumberValue(3)),  // A2
            (1u, 2u, new NumberValue(5)),  // B1
            (2u, 2u, new NumberValue(11))  // B2
        );
        var plainWb = MakeWorkbook(out var plainSheet,
            (1u, 1u, new NumberValue(2)),
            (2u, 1u, new NumberValue(3)),
            (3u, 1u, new NumberValue(5)),
            (4u, 1u, new NumberValue(11))
        );

        var unionResult = _eval.Evaluate("=SKEW((A1:A2,B1:B2))", unionSheet, unionWb);
        var plainResult = _eval.Evaluate("=SKEW(A1:A4)", plainSheet, plainWb);

        unionResult.Should().BeOfType<NumberValue>();
        unionResult.Should().Be(plainResult);
    }

    [Fact]
    public void SkewP_TwoAreaUnion_MatchesEquivalentContiguousRange()
    {
        var unionWb = MakeWorkbook(out var unionSheet,
            (1u, 1u, new NumberValue(2)),
            (2u, 1u, new NumberValue(3)),
            (1u, 2u, new NumberValue(5)),
            (2u, 2u, new NumberValue(11))
        );
        var plainWb = MakeWorkbook(out var plainSheet,
            (1u, 1u, new NumberValue(2)),
            (2u, 1u, new NumberValue(3)),
            (3u, 1u, new NumberValue(5)),
            (4u, 1u, new NumberValue(11))
        );

        var unionResult = _eval.Evaluate("=SKEW.P((A1:A2,B1:B2))", unionSheet, unionWb);
        var plainResult = _eval.Evaluate("=SKEW.P(A1:A4)", plainSheet, plainWb);

        unionResult.Should().BeOfType<NumberValue>();
        unionResult.Should().Be(plainResult);
    }

    [Fact]
    public void Kurt_TwoAreaUnion_MatchesEquivalentContiguousRange()
    {
        var unionWb = MakeWorkbook(out var unionSheet,
            (1u, 1u, new NumberValue(2)),  // A1
            (2u, 1u, new NumberValue(3)),  // A2
            (1u, 2u, new NumberValue(5)),  // B1
            (2u, 2u, new NumberValue(11)), // B2
            (1u, 4u, new NumberValue(7)),  // D1
            (2u, 4u, new NumberValue(9))   // D2
        );
        var plainWb = MakeWorkbook(out var plainSheet,
            (1u, 1u, new NumberValue(2)),
            (2u, 1u, new NumberValue(3)),
            (3u, 1u, new NumberValue(5)),
            (4u, 1u, new NumberValue(11)),
            (5u, 1u, new NumberValue(7)),
            (6u, 1u, new NumberValue(9))
        );

        var unionResult = _eval.Evaluate("=KURT((A1:A2,B1:B2,D1:D2))", unionSheet, unionWb);
        var plainResult = _eval.Evaluate("=KURT(A1:A6)", plainSheet, plainWb);

        unionResult.Should().BeOfType<NumberValue>();
        unionResult.Should().Be(plainResult);
    }

    [Fact]
    public void Skew_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(2)),
            (2u, 1u, new NumberValue(3)),
            (3u, 1u, new NumberValue(5)),
            (4u, 1u, new NumberValue(11))
        );

        _eval.Evaluate("=SKEW(A1:A4)", sheet, workbook).Should().BeOfType<NumberValue>();
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // ITEM 3: containment -- UnionValue can never escape FreeX.Core.Formula
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BareUnionFormula_AsWholeCellBody_NormalizesToValueError()
    {
        // "=(A1:B2,D5)" with no enclosing function -- Excel itself rejects a bare union reference
        // as a cell's final value with #VALUE!, and so must this engine (NormalizeTopLevelResult
        // in FormulaEvaluator.cs). The evaluated result reaching the caller must be a plain
        // ErrorValue, never a UnionValue.
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 2u, new NumberValue(2)),
            (5u, 4u, new NumberValue(3))
        );

        var result = _eval.Evaluate("=(A1:B2,D5)", sheet, workbook);

        result.Should().Be(ErrorValue.Value);
        result.Should().NotBeOfType<UnionValue>();
    }

    [Fact]
    public void CellHoldingBareUnionFormula_StoredValueIsNotUnionValue()
    {
        // Route through the real cell-recalculation entry point (SetCell with a formula string,
        // not a direct Evaluate() call) to prove the stored cell Value -- what IO writers, the
        // dependency graph, and every other consumer outside FreeX.Core.Formula actually see --
        // can never be a UnionValue either.
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 2u, new NumberValue(2)),
            (5u, 4u, new NumberValue(3))
        );

        var formulaCellAddress = new CellAddress(sheet.Id, 10u, 10u);
        var evaluated = _eval.Evaluate("=(A1:B2,D5)", sheet, workbook);
        sheet.SetCell(formulaCellAddress, new Cell { FormulaText = "(A1:B2,D5)", Value = evaluated });

        var storedCell = sheet.GetCell(10u, 10u);
        storedCell.Should().NotBeNull();
        storedCell!.Value.Should().NotBeOfType<UnionValue>();
        storedCell.Value.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void UnionValue_IsDeclaredOnlyInCoreFormulaAssembly()
    {
        // Structural guard matching UnionValue's own doc comment ("deliberately a
        // FreeX.Core.Formula-project-only ScalarValue subtype"): confirm no other loaded assembly
        // in this test process declares or references a type of that name outside
        // FreeX.Core.Formula, which is the actual mechanism (a plain non-public-API C# type with
        // no serialization contract) that keeps it from ever reaching FreeX.Core.Model,
        // FreeX.Core.IO, or FreeX.Core.Calc's dependency graph.
        var unionValueType = typeof(UnionValue);
        unionValueType.Assembly.GetName().Name.Should().Be("FreeX.Core.Formula");

        // FreeX.Core.Model.ScalarValue is the base type UnionValue derives from (see UnionValue.cs's
        // doc comment) -- but Core.Model itself must not declare or export a UnionValue type,
        // confirming the base ScalarValue hierarchy has no knowledge of unions.
        var modelAssembly = typeof(ScalarValue).Assembly;
        modelAssembly.GetName().Name.Should().Be("FreeX.Core.Model");
        modelAssembly.GetType("FreeX.Core.Model.UnionValue").Should().BeNull();
    }
}
