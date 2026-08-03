using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R120-textjoin-union-materialization: TEXTJOIN is a StructuredRangeFunction (not an
/// AggregateFunction), so the aggregate-only UnionValue unwrap in FormulaEvaluator.Functions.cs
/// never ran for it, and TEXTJOIN was missing from UnionMaterializableRangeFunctions in
/// FormulaEvaluator.FunctionClassification.cs (unlike LARGE/SMALL/DEVSQ/FREQUENCY/COUNTBLANK,
/// which R94/R97 already added). A parenthesized union argument therefore reached
/// FlattenTextjoinArgument (BuiltInFunctions.TextAdvanced.cs) as a raw UnionValue, which only
/// special-cases RangeValue -- the fallback `text.Add(ToText(value))` then hit ToText's
/// `_ => v.ToString()` default arm and embedded the literal .NET record dump (e.g.
/// "UnionValue { Areas = System.Collections.Generic.List`1[...] }") into the joined result
/// instead of every cell across both union areas.
///
/// Fix: added "TEXTJOIN" to UnionMaterializableRangeFunctions, so the shared choke point in
/// FormulaEvaluator.Functions.cs materializes a union argument into one synthetic Nx1 RangeValue
/// before FlattenTextjoinArgument ever sees it -- reaching the already-correct RangeValue branch.
/// </summary>
public sealed class R120_TextjoinUnionArgumentTests
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

    [Fact]
    public void Textjoin_TwoAreaUnionArgument_JoinsCellsAcrossBothAreas_NotRawObjectDump()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new TextValue("a")),   // A1
            (2u, 1u, new TextValue("b")),   // A2
            (1u, 2u, new TextValue("c")),   // B1
            (2u, 2u, new TextValue("d"))    // B2
        );

        // Before the fix this returned the literal .NET UnionValue record dump embedded as text
        // instead of "a,b,c,d". After the fix, every cell across both union areas is joined in
        // area order, exactly like SUM/AGGREGATE/DEVSQ already do for a union argument.
        var result = _eval.Evaluate("=TEXTJOIN(\",\",TRUE,(A1:A2,B1:B2))", sheet, workbook);
        result.Should().Be(new TextValue("a,b,c,d"));
        result.Should().NotBe(new TextValue(
            "UnionValue { Areas = System.Collections.Generic.List`1[FreeX.Core.Formula.RangeValue] }"));
    }

    [Fact]
    public void Textjoin_ThreeAreaUnionWithBareSingleCell_JoinsAcrossAllAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new TextValue("a")),  // A1
            (2u, 1u, new TextValue("b")),  // A2
            (1u, 2u, new TextValue("c")),  // B1
            (5u, 4u, new TextValue("e"))   // D5 -- bare single-cell area
        );

        _eval.Evaluate("=TEXTJOIN(\"-\",TRUE,(A1:A2,B1:B1,D5))", sheet, workbook)
            .Should().Be(new TextValue("a-b-c-e"));
    }

    [Fact]
    public void Textjoin_UnionArgument_IgnoreEmptyTrue_SkipsBlankCellsAcrossAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new TextValue("a")),  // A1
            // A2 intentionally blank
            (1u, 2u, new TextValue("c"))   // B1
            // B2 intentionally blank
        );

        _eval.Evaluate("=TEXTJOIN(\",\",TRUE,(A1:A2,B1:B2))", sheet, workbook)
            .Should().Be(new TextValue("a,c"));
    }

    [Fact]
    public void Textjoin_UnionWithErrorCellInAnArea_PropagatesError()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new TextValue("a")),
            (1u, 2u, ErrorValue.DivByZero)
        );

        _eval.Evaluate("=TEXTJOIN(\",\",TRUE,(A1:A1,B1:B1))", sheet, workbook)
            .Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Textjoin_DefinedNameResolvingToUnion_JoinsAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new TextValue("a")),
            (2u, 1u, new TextValue("b")),
            (1u, 2u, new TextValue("c")),
            (2u, 2u, new TextValue("d"))
        );
        workbook.NamedFormulas["U"] = "(A1:A2,B1:B2)";

        _eval.Evaluate("=TEXTJOIN(\",\",TRUE,U)", sheet, workbook).Should().Be(new TextValue("a,b,c,d"));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // No-regression sibling: plain (non-union) range/text arguments must keep working exactly as
    // before -- the materialization only engages when the argument actually evaluates to a
    // UnionValue, so a normal RangeValue argument still goes straight into FlattenTextjoinArgument
    // unchanged.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Textjoin_PlainRangeArgument_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new TextValue("a")),
            (1u, 2u, new TextValue("b")),
            (1u, 3u, new TextValue("c"))
        );

        _eval.Evaluate("=TEXTJOIN(\"|\",TRUE,A1:C1)", sheet, workbook).Should().Be(new TextValue("a|b|c"));
    }

    [Fact]
    public void Textjoin_MixedPlainRangeAndLiteralArguments_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new TextValue("x")),
            (1u, 2u, new TextValue("y"))
        );

        _eval.Evaluate("=TEXTJOIN(\",\",TRUE,A1:B1,\"z\")", sheet, workbook)
            .Should().Be(new TextValue("x,y,z"));
    }
}
