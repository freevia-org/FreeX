using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R65-formula-database-6-1: Excel allows the 2-arg field-OMITTED form
/// =DCOUNT(database,criteria)/=DCOUNTA(database,criteria) (counts ALL records matching
/// criteria, regardless of any particular field's numeric/non-blank content). FreeX registered
/// DCOUNT/DCOUNTA with MinArgs=3, so the 2-arg call was rejected as #VALUE! before the function
/// body ever ran.
/// </summary>
public sealed class R65_DatabaseFieldOmittedTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void DCount_FieldOmitted_CountsAllMatchingRecords()
    {
        // Age(col A)/Salary(col B), 3 data rows, all Age > 20 -> criteria matches all 3 rows.
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Age"));
        Set(sheet, 1, 2, new TextValue("Salary"));
        Set(sheet, 2, 1, new NumberValue(25));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new NumberValue(30));
        Set(sheet, 3, 2, new NumberValue(200));
        Set(sheet, 4, 1, new NumberValue(40));
        Set(sheet, 4, 2, new NumberValue(300));
        Set(sheet, 1, 4, new TextValue("Age"));
        Set(sheet, 2, 4, new TextValue(">20"));

        _eval.Evaluate("=DCOUNT(A1:B4,D1:D2)", sheet)
            .Should().Be(new NumberValue(3));
    }

    [Fact]
    public void DCountA_FieldOmitted_CountsAllMatchingRecords()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Age"));
        Set(sheet, 1, 2, new TextValue("Salary"));
        Set(sheet, 2, 1, new NumberValue(25));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new NumberValue(30));
        Set(sheet, 3, 2, new NumberValue(200));
        Set(sheet, 4, 1, new NumberValue(40));
        Set(sheet, 4, 2, new NumberValue(300));
        Set(sheet, 1, 4, new TextValue("Age"));
        Set(sheet, 2, 4, new TextValue(">20"));

        _eval.Evaluate("=DCOUNTA(A1:B4,D1:D2)", sheet)
            .Should().Be(new NumberValue(3));
    }

    [Fact]
    public void DCount_FieldOmitted_OnlyRowsMatchingCriteriaAreCounted()
    {
        // Sibling regression guard: field-omitted form still filters by criteria (not just "all rows").
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Age"));
        Set(sheet, 1, 2, new TextValue("Salary"));
        Set(sheet, 2, 1, new NumberValue(15));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new NumberValue(30));
        Set(sheet, 3, 2, new NumberValue(200));
        Set(sheet, 4, 1, new NumberValue(40));
        Set(sheet, 4, 2, new NumberValue(300));
        Set(sheet, 1, 4, new TextValue("Age"));
        Set(sheet, 2, 4, new TextValue(">20"));

        _eval.Evaluate("=DCOUNT(A1:B4,D1:D2)", sheet)
            .Should().Be(new NumberValue(2));
    }

    [Fact]
    public void DCount_ThreeArgFieldSpecified_StillWorks()
    {
        // Sibling regression guard: the original 3-arg field-specified form is unaffected.
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Age"));
        Set(sheet, 1, 2, new TextValue("Salary"));
        Set(sheet, 2, 1, new NumberValue(25));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new NumberValue(30));
        Set(sheet, 3, 2, new NumberValue(200));
        Set(sheet, 4, 1, new NumberValue(40));
        Set(sheet, 4, 2, new NumberValue(300));
        Set(sheet, 1, 4, new TextValue("Age"));
        Set(sheet, 2, 4, new TextValue(">20"));

        _eval.Evaluate("=DCOUNT(A1:B4,\"Age\",D1:D2)", sheet)
            .Should().Be(new NumberValue(3));
    }

    [Fact]
    public void DCountA_ThreeArgFieldSpecified_StillWorks()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Age"));
        Set(sheet, 1, 2, new TextValue("Salary"));
        Set(sheet, 2, 1, new NumberValue(25));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new NumberValue(30));
        Set(sheet, 3, 2, new NumberValue(200));
        Set(sheet, 4, 1, new NumberValue(40));
        Set(sheet, 4, 2, new NumberValue(300));
        Set(sheet, 1, 4, new TextValue("Age"));
        Set(sheet, 2, 4, new TextValue(">20"));

        _eval.Evaluate("=DCOUNTA(A1:B4,\"Salary\",D1:D2)", sheet)
            .Should().Be(new NumberValue(3));
    }

    private static void Set(Sheet sheet, uint row, uint col, ScalarValue value)
        => sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
}
