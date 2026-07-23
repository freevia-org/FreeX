using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R75-formula-lookup-vhx-4-1: FormulaEvaluator.LookupFastPaths.cs's direct-literal-range fast
/// path for VLOOKUP/HLOOKUP (TryEvaluateLegacyLookupDirectTable) computed
/// "rangeLookupValue is BlankValue || BuiltInFunctions.ToBool(rangeLookupValue)" for the
/// approximate flag -- and ToBool throws #VALUE! on a literal text "TRUE"/"FALSE" range_lookup
/// argument, so e.g. "=VLOOKUP(3,A1:B5,2,\"FALSE\")" (a literal range, not a defined name) hit the
/// fast path and returned #VALUE! even though the slow path (BuiltInFunctions.Lookup.Legacy.cs's
/// VlookupScalar/HlookupScalar, reached via a defined-name table -- see R53_FormulaFixesTests)
/// already coerced that same text correctly via TryCoerceRangeLookupBool. Fixed by routing the
/// fast path's approximate-flag coercion through that same shared helper (now internal) instead
/// of the throwing BuiltInFunctions.ToBool.
/// </summary>
public class R75_LookupFastPathTextRangeLookupTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook workbook, Sheet sheet) MakeSortedTable()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(50));
        return (workbook, sheet);
    }

    [Fact]
    public void Vlookup_LiteralRange_TextFalse_ReturnsExactMatch_InsteadOfValueError()
    {
        var (workbook, sheet) = MakeSortedTable();

        // Direct literal range (not a defined name) -- intercepted by the fast path.
        _eval.Evaluate("=VLOOKUP(3,A1:B5,2,\"FALSE\")", sheet, workbook).Should().Be(new NumberValue(30));
        _eval.Evaluate("=VLOOKUP(3,A1:B5,2,\"false\")", sheet, workbook).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Vlookup_LiteralRange_TextTrue_PerformsApproximateMatch()
    {
        var (workbook, sheet) = MakeSortedTable();

        _eval.Evaluate("=VLOOKUP(3,A1:B5,2,\"TRUE\")", sheet, workbook).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Hlookup_LiteralRange_TextFalse_ReturnsExactMatch()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(30));

        _eval.Evaluate("=HLOOKUP(3,A1:C2,2,\"FALSE\")", sheet, workbook).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Vlookup_LiteralRange_NumericRangeLookupArgument_StillWorks_SiblingNoRegression()
    {
        var (workbook, sheet) = MakeSortedTable();

        // Numeric (non-text) range_lookup was never affected by ToBool's throw -- must be unchanged.
        _eval.Evaluate("=VLOOKUP(3,A1:B5,2,0)", sheet, workbook).Should().Be(new NumberValue(30));
        _eval.Evaluate("=VLOOKUP(3,A1:B5,2,1)", sheet, workbook).Should().Be(new NumberValue(30));
        // An invalid, non-TRUE/FALSE text argument must still yield #VALUE! through the fast path too.
        _eval.Evaluate("=VLOOKUP(3,A1:B5,2,\"MAYBE\")", sheet, workbook).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Vlookup_LiteralRangeAndNamedRangeForms_NowAgree()
    {
        var (workbook, sheet) = MakeSortedTable();
        workbook.DefineNamedRange("Tbl", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 2)));

        var literalResult = _eval.Evaluate("=VLOOKUP(3,A1:B5,2,\"FALSE\")", sheet, workbook);
        var namedResult = _eval.Evaluate("=VLOOKUP(3,Tbl,2,\"FALSE\")", sheet, workbook);

        literalResult.Should().Be(namedResult);
        literalResult.Should().Be(new NumberValue(30));
    }
}
