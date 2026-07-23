using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R75-formula-lookup-vhx-4-2: approximate-match VLOOKUP/HLOOKUP/MATCH/LOOKUP
/// (BuiltInFunctions.Lookup.Legacy.cs) returned #N/A for a BLANK lookup_value even when a valid
/// numeric match existed, because the type-class filter
/// "if (cv is not BlankValue && ApproxLookupTypeClass(cv) != lookupClass) continue;" computed
/// lookupClass from ApproxLookupTypeClass(lookupValue), which is the dedicated "blank" class (0)
/// for a blank lookup value -- so every non-blank numeric candidate row was skipped as a
/// "foreign type", exactly like a blank-typed named-range table with no eligible candidates.
/// Excel instead coerces a blank lookup_value to 0 for the comparison (mirroring the R47 fix that
/// already handled the blank-CANDIDATE side via CompareScalar's CoerceBlankForCompare). Fixed by
/// adding ApproxLookupClassForLookupValue, which treats a blank lookup value as the numeric class
/// for the type-class filter, and routing VlookupScalar/HlookupScalar/MatchScalar (match_type
/// 1/-1)/Lookup()/LookupVectorForm through it instead of the raw ApproxLookupTypeClass call.
/// </summary>
public class R75_ApproximateLookupBlankLookupValueTests
{
    private readonly FormulaEvaluator _eval = new();

    // B1:B5 = -5,-2,0,3,7 (ascending); C1:C5 = 100,200,300,400,500. A1 is left unset (blank).
    private static (Workbook workbook, Sheet sheet) MakeWorkbook()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        double[] keys = [-5, -2, 0, 3, 7];
        for (uint r = 1; r <= 5; r++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(keys[r - 1]));
            sheet.SetCell(new CellAddress(sheet.Id, r, 3), new NumberValue(r * 100));
        }
        return (workbook, sheet);
    }

    [Fact]
    public void Vlookup_BlankLookupValue_ApproximateMatch_FindsNumericCandidate_InsteadOfNA()
    {
        var (workbook, sheet) = MakeWorkbook();

        // A1 is blank -> coerces to 0 -> largest key <= 0 is row 3 (key 0) -> C3 = 300.
        var result = _eval.Evaluate("=VLOOKUP(A1,B1:C5,2,TRUE)", sheet, workbook);

        result.Should().Be(new NumberValue(300));
    }

    [Fact]
    public void Match_BlankLookupValue_AscendingApproximate_FindsNumericCandidate()
    {
        var (workbook, sheet) = MakeWorkbook();

        var result = _eval.Evaluate("=MATCH(A1,B1:B5,1)", sheet, workbook);

        result.Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Match_BlankLookupValue_DescendingApproximate_FindsNumericCandidate()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        double[] keys = [7, 3, 0, -2, -5]; // descending
        for (uint r = 1; r <= 5; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(keys[r - 1]));

        // Descending approximate: smallest value >= lookupValue(0) -> key 0 at row 3.
        var result = _eval.Evaluate("=MATCH(A1,B1:B5,-1)", sheet, workbook);

        result.Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Lookup_BlankLookupValue_VectorForm_FindsNumericCandidate()
    {
        var (workbook, sheet) = MakeWorkbook();

        var result = _eval.Evaluate("=LOOKUP(A1,B1:B5,C1:C5)", sheet, workbook);

        result.Should().Be(new NumberValue(300));
    }

    [Fact]
    public void Vlookup_BlankLookupValue_ExactMatch_StillBehavesPerExcel_SiblingNoRegression()
    {
        var (workbook, sheet) = MakeWorkbook();

        // Exact match is untouched by this fix: ScalarEquals already coerced blank->0 for the
        // comparison before and after, so the blank lookup_value still matches the row whose key
        // is genuinely 0 (row 3, key 0) under FALSE (exact) semantics too.
        var result = _eval.Evaluate("=VLOOKUP(A1,B1:C5,2,FALSE)", sheet, workbook);

        result.Should().Be(new NumberValue(300));
    }

    [Fact]
    public void Vlookup_NonBlankApproximateLookup_Unchanged_SiblingNoRegression()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(3));

        // A1 = 3 (non-blank) -> largest key <= 3 is row 4 (key 3) -> C4 = 400.
        var result = _eval.Evaluate("=VLOOKUP(A1,B1:C5,2,TRUE)", sheet, workbook);

        result.Should().Be(new NumberValue(400));
    }
}
