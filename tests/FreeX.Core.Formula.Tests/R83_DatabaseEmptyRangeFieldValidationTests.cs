using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-83 review fix for the database (D*) functions:
///   R83-formula-database-5-1: DSUM/DAVERAGE/DMAX/DMIN/DPRODUCT/DSTDEV(P)/DVAR(P) must return
///   #VALUE! when the field argument doesn't resolve to a database column, even when the
///   database range has no data rows (header-only range). Previously the RowCount &lt; 2
///   short-circuit in DatabaseExtract ran before field resolution, so an invalid field
///   silently produced a plausible-looking wrong number (0 or #DIV/0!) instead of #VALUE! --
///   inconsistent with the identical scenario when the database DOES have data rows (see
///   R37_DatabaseCriteriaAndFieldTests.DCount_UnresolvableField_ReturnsValueError).
/// </summary>
public sealed class R83_DatabaseEmptyRangeFieldValidationTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet BuildHeaderOnlyDatabase()
    {
        // A1:C1 header row only (Name/Age/Salary) -- zero data rows below.
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Name"));
        Set(sheet, 1, 2, new TextValue("Age"));
        Set(sheet, 1, 3, new TextValue("Salary"));
        Set(sheet, 1, 5, new TextValue("Age"));
        Set(sheet, 2, 5, new NumberValue(30));
        return sheet;
    }

    [Fact]
    public void DFunctions_EmptyDatabase_UnresolvableField_ReturnValueError()
    {
        var sheet = BuildHeaderOnlyDatabase();

        // "Bogus" is not a header in the header-only database -> #VALUE!, matching the
        // populated-database behavior for the identical invalid field (R37 test).
        _eval.Evaluate("=DSUM(A1:C1,\"Bogus\",E1:E2)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DAVERAGE(A1:C1,\"Bogus\",E1:E2)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DMAX(A1:C1,\"Bogus\",E1:E2)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DMIN(A1:C1,\"Bogus\",E1:E2)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DPRODUCT(A1:C1,\"Bogus\",E1:E2)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DSTDEV(A1:C1,\"Bogus\",E1:E2)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DSTDEVP(A1:C1,\"Bogus\",E1:E2)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DVAR(A1:C1,\"Bogus\",E1:E2)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DVARP(A1:C1,\"Bogus\",E1:E2)", sheet).Should().Be(ErrorValue.Value);

        // Also an out-of-range numeric field index.
        _eval.Evaluate("=DSUM(A1:C1,10,E1:E2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void DFunctions_EmptyDatabase_ValidField_StillReturnsEmptyAggregateNotError()
    {
        // No-regression: a valid, resolvable field on a header-only (no data rows) database
        // must still return the normal "no matches" result (0 for sum, #DIV/0! for average),
        // not #VALUE! -- the fix must only reject genuinely unresolvable fields.
        var sheet = BuildHeaderOnlyDatabase();

        _eval.Evaluate("=DSUM(A1:C1,\"Salary\",E1:E2)", sheet).Should().Be(new NumberValue(0));
        _eval.Evaluate("=DAVERAGE(A1:C1,\"Salary\",E1:E2)", sheet).Should().Be(ErrorValue.DivByZero);
        _eval.Evaluate("=DMAX(A1:C1,\"Salary\",E1:E2)", sheet).Should().Be(new NumberValue(0));
        _eval.Evaluate("=DMIN(A1:C1,\"Salary\",E1:E2)", sheet).Should().Be(new NumberValue(0));
        _eval.Evaluate("=DPRODUCT(A1:C1,\"Salary\",E1:E2)", sheet).Should().Be(new NumberValue(0));
    }

    private static void Set(Sheet sheet, uint row, uint col, ScalarValue value)
        => sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
}
