using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-30 review fixes:
/// - R30-formula-database-dfns-2: DCOUNT must ignore an error in a matched field cell (like
///   plain COUNT) instead of propagating it; DCOUNTA must count that error cell as a present
///   non-blank value (like plain COUNTA) instead of propagating it.
/// - R30-formula-database-dfns-3: DPRODUCT must return 0 (not 1) when zero matching rows have
///   a numeric field value, mirroring MathCore.Aggregates.Product's sawNumeric ? result : 0.
/// </summary>
public sealed class R30_DatabaseDCountDProductTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void DCount_MatchedFieldCellIsError_IgnoresErrorAndCountsRemainingNumerics()
    {
        // Age(col A)/Salary(col B): row2 Age=30 Salary=100, row3 Age=30 Salary=#DIV/0!,
        // row4 Age=40 Salary=400. Criteria matches Age=30 (rows 2 and 3).
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Age"));
        Set(sheet, 1, 2, new TextValue("Salary"));
        Set(sheet, 2, 1, new NumberValue(30));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new NumberValue(30));
        Set(sheet, 3, 2, ErrorValue.DivByZero);
        Set(sheet, 4, 1, new NumberValue(40));
        Set(sheet, 4, 2, new NumberValue(400));
        Set(sheet, 1, 4, new TextValue("Age"));
        Set(sheet, 2, 4, new NumberValue(30));

        _eval.Evaluate("=DCOUNT(A1:B4,\"Salary\",D1:D2)", sheet)
            .Should().Be(new NumberValue(1));
    }

    [Fact]
    public void DCountA_MatchedFieldCellIsError_CountsErrorAsPresent()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Age"));
        Set(sheet, 1, 2, new TextValue("Salary"));
        Set(sheet, 2, 1, new NumberValue(30));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new NumberValue(30));
        Set(sheet, 3, 2, ErrorValue.DivByZero);
        Set(sheet, 4, 1, new NumberValue(40));
        Set(sheet, 4, 2, new NumberValue(400));
        Set(sheet, 1, 4, new TextValue("Age"));
        Set(sheet, 2, 4, new NumberValue(30));

        _eval.Evaluate("=DCOUNTA(A1:B4,\"Salary\",D1:D2)", sheet)
            .Should().Be(new NumberValue(2));
    }

    [Fact]
    public void DCount_NoErrors_StillCountsOnlyNumericMatchesCorrectly()
    {
        // Sibling regression guard: ordinary (error-free) database is unaffected.
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Age"));
        Set(sheet, 1, 2, new TextValue("Salary"));
        Set(sheet, 2, 1, new NumberValue(30));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new NumberValue(30));
        Set(sheet, 3, 2, new TextValue("n/a"));
        Set(sheet, 4, 1, new NumberValue(40));
        Set(sheet, 4, 2, new NumberValue(400));
        Set(sheet, 1, 4, new TextValue("Age"));
        Set(sheet, 2, 4, new NumberValue(30));

        _eval.Evaluate("=DCOUNT(A1:B4,\"Salary\",D1:D2)", sheet)
            .Should().Be(new NumberValue(1));
        _eval.Evaluate("=DCOUNTA(A1:B4,\"Salary\",D1:D2)", sheet)
            .Should().Be(new NumberValue(2));
    }

    [Fact]
    public void DProduct_NoNumericMatches_ReturnsZeroNotOne()
    {
        // Database A1:C5 (Name/Age/Salary); criteria matches zero rows (Age=99).
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Name"));
        Set(sheet, 1, 2, new TextValue("Age"));
        Set(sheet, 1, 3, new TextValue("Salary"));
        Set(sheet, 2, 1, new TextValue("Alice"));
        Set(sheet, 2, 2, new NumberValue(30));
        Set(sheet, 2, 3, new NumberValue(100));
        Set(sheet, 3, 1, new TextValue("Bob"));
        Set(sheet, 3, 2, new NumberValue(25));
        Set(sheet, 3, 3, new NumberValue(200));
        Set(sheet, 1, 5, new TextValue("Age"));
        Set(sheet, 2, 5, new NumberValue(99));

        _eval.Evaluate("=DPRODUCT(A1:C3,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(0));
    }

    [Fact]
    public void DProduct_WithNumericMatches_StillMultipliesCorrectly()
    {
        // Sibling regression guard: DPRODUCT with actual numeric matches is unaffected.
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Name"));
        Set(sheet, 1, 2, new TextValue("Age"));
        Set(sheet, 1, 3, new TextValue("Salary"));
        Set(sheet, 2, 1, new TextValue("Alice"));
        Set(sheet, 2, 2, new NumberValue(30));
        Set(sheet, 2, 3, new NumberValue(10));
        Set(sheet, 3, 1, new TextValue("Carol"));
        Set(sheet, 3, 2, new NumberValue(30));
        Set(sheet, 3, 3, new NumberValue(5));
        Set(sheet, 1, 5, new TextValue("Age"));
        Set(sheet, 2, 5, new NumberValue(30));

        _eval.Evaluate("=DPRODUCT(A1:C3,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(50));
    }

    private static void Set(Sheet sheet, uint row, uint col, ScalarValue value)
        => sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
}
