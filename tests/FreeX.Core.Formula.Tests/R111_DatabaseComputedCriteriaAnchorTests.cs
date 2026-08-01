using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R111-database-computed-criteria-anchor: Excel's documented "computed criteria" convention for
/// D-functions requires the authored formula to reference the database's own FIRST DATA ROW
/// directly (e.g. "=B6&gt;200" when the list's first data row is row 6); every other candidate
/// database row is then evaluated by shifting relative references by
/// (targetRow - firstDatabaseDataRow) -- completely independent of where the criteria formula
/// cell physically sits in its own (usually disjoint) criteria region.
///
/// Before the fix, TryEvaluateComputedCriterion anchored the shift on the criteria formula's OWN
/// physical row instead of the database's first data row. That only produced correct results by
/// coincidence when the criteria table happened to sit so its formula row equalled the database's
/// first data row number (e.g. the R47 tests, criteria formula at row 2, database data starting
/// at row 2). Placing the criteria table anywhere else -- including Microsoft's own recommended
/// layout of leaving several blank rows ABOVE the list for the criteria range -- shifted the
/// formula's references to unrelated/blank cells, silently under-counting DSUM/DCOUNT/etc.
/// </summary>
public sealed class R111_DatabaseComputedCriteriaAnchorTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeNameSalaryDatabaseAtRow5()
    {
        // Database A5:B8 (Name/Salary): header row 5; data rows 6-8: Davolio=100, David=200,
        // Smith=300. Criteria placed ABOVE the list (Microsoft's own recommended layout).
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 5, 1, new TextValue("Name"));
        Set(sheet, 5, 2, new TextValue("Salary"));
        Set(sheet, 6, 1, new TextValue("Davolio"));
        Set(sheet, 6, 2, new NumberValue(100));
        Set(sheet, 7, 1, new TextValue("David"));
        Set(sheet, 7, 2, new NumberValue(200));
        Set(sheet, 8, 1, new TextValue("Smith"));
        Set(sheet, 8, 2, new NumberValue(300));
        return sheet;
    }

    [Fact]
    public void DSum_ComputedCriterion_AnchorsOnDatabaseFirstDataRow_NotCriteriaCellsOwnRow()
    {
        // Criteria D1:D2 (D1 blank header, D2 formula referencing the database's actual first
        // data row B6): "=B6>200". Real Excel: row6 shift 0 (100>200=false), row7 shift +1
        // (200>200=false), row8 shift +2 (300>200=true) -> DSUM = 300.
        // Pre-fix (own-row anchor at row 2): row6 shift +4 -> B10 (blank) -> false; row7 shift +5
        // -> B11 (blank) -> false; row8 shift +6 -> B12 (blank) -> false -> DSUM = 0 (wrong).
        var sheet = MakeNameSalaryDatabaseAtRow5();
        SetFormula(sheet, 2, 4, "=B6>200");

        _eval.Evaluate("=DSUM(A5:B8,\"Salary\",D1:D2)", sheet)
            .Should().Be(new NumberValue(300));
    }

    [Fact]
    public void DCount_ComputedCriterion_AnchorsOnDatabaseFirstDataRow_NotCriteriaCellsOwnRow()
    {
        // Same disjoint layout, DCOUNT should count only the matching (Smith) record.
        var sheet = MakeNameSalaryDatabaseAtRow5();
        SetFormula(sheet, 2, 4, "=B6>200");

        _eval.Evaluate("=DCOUNT(A5:B8,\"Salary\",D1:D2)", sheet)
            .Should().Be(new NumberValue(1));
    }

    [Fact]
    public void DSum_ComputedCriterion_CoincidentalRowAlignment_StillWorks_NotRegressed()
    {
        // Sibling regression guard: the pre-existing R47 layout (criteria formula row happens to
        // equal the database's first data row) must keep working after anchoring on the
        // database's first data row instead of the formula's own row -- in this layout they are
        // the same row (2), so behaviour must be unchanged.
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Name"));
        Set(sheet, 1, 2, new TextValue("Salary"));
        Set(sheet, 2, 1, new TextValue("Davolio"));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new TextValue("David"));
        Set(sheet, 3, 2, new NumberValue(200));
        Set(sheet, 4, 1, new TextValue("Smith"));
        Set(sheet, 4, 2, new NumberValue(300));
        SetFormula(sheet, 2, 4, "=B2>200");

        _eval.Evaluate("=DSUM(A1:B4,\"Salary\",D1:D2)", sheet)
            .Should().Be(new NumberValue(300));
    }

    private static void Set(Sheet sheet, uint row, uint col, ScalarValue value)
        => sheet.SetCell(new CellAddress(sheet.Id, row, col), value);

    private static void SetFormula(Sheet sheet, uint row, uint col, string formulaText)
        => sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromFormula(formulaText));
}
