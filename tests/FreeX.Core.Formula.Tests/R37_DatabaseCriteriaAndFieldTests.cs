using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-37 review fixes for the database (D*) functions:
///   R37-formula-database-2-1: a plain-text database criterion (no wildcard, no leading "=")
///     must do Excel's documented "begins with" prefix match, not exact equality — e.g.
///     criterion "Dav" must match "Davolio" and "David". An explicit "=Dav" criterion must
///     still force exact match. This is specific to database/Advanced-Filter criteria ranges;
///     COUNTIF/SUMIF/etc. (which share the same CompileCriteria matcher) must keep their
///     existing exact-match behavior for a bare text criterion.
///   R37-formula-database-2-2: DCOUNT/DCOUNTA must return #VALUE! (like every sibling
///     D-function) when the field argument doesn't resolve to a database column, instead of
///     silently returning 0.
/// </summary>
public sealed class R37_DatabaseCriteriaAndFieldTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet BuildNameSalaryDatabase()
    {
        // Name(col A)/Salary(col B): Davolio=100, David=200, Smith=300.
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Name"));
        Set(sheet, 1, 2, new TextValue("Salary"));
        Set(sheet, 2, 1, new TextValue("Davolio"));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new TextValue("David"));
        Set(sheet, 3, 2, new NumberValue(200));
        Set(sheet, 4, 1, new TextValue("Smith"));
        Set(sheet, 4, 2, new NumberValue(300));
        return sheet;
    }

    [Fact]
    public void DSum_BareTextCriterion_MatchesAsBeginsWithPrefix()
    {
        var sheet = BuildNameSalaryDatabase();
        Set(sheet, 1, 4, new TextValue("Name"));
        Set(sheet, 2, 4, new TextValue("Dav"));

        // "Dav" (no wildcard, no leading "=") must begins-with match "Davolio" and "David",
        // per Excel's documented database-criteria behavior -> 100 + 200 = 300.
        _eval.Evaluate("=DSUM(A1:B4,\"Salary\",D1:D2)", sheet)
            .Should().Be(new NumberValue(300));
    }

    [Fact]
    public void DCount_BareTextCriterion_MatchesAsBeginsWithPrefix()
    {
        var sheet = BuildNameSalaryDatabase();
        Set(sheet, 1, 4, new TextValue("Name"));
        Set(sheet, 2, 4, new TextValue("Dav"));

        _eval.Evaluate("=DCOUNT(A1:B4,\"Salary\",D1:D2)", sheet)
            .Should().Be(new NumberValue(2));
    }

    [Fact]
    public void DSum_ExplicitEqualsTextCriterion_StaysExactMatch()
    {
        // Sibling regression guard: an explicit "=Dav" criterion must still require exact
        // equality (matches neither "Davolio" nor "David") — the begins-with fix must not
        // bleed into the explicit-equals path.
        var sheet = BuildNameSalaryDatabase();
        Set(sheet, 1, 4, new TextValue("Name"));
        Set(sheet, 2, 4, new TextValue("=Dav"));

        _eval.Evaluate("=DSUM(A1:B4,\"Salary\",D1:D2)", sheet)
            .Should().Be(new NumberValue(0));
    }

    [Fact]
    public void DSum_ExplicitEqualsFullNameCriterion_StillMatchesExactRow()
    {
        var sheet = BuildNameSalaryDatabase();
        Set(sheet, 1, 4, new TextValue("Name"));
        Set(sheet, 2, 4, new TextValue("=David"));

        _eval.Evaluate("=DSUM(A1:B4,\"Salary\",D1:D2)", sheet)
            .Should().Be(new NumberValue(200));
    }

    [Fact]
    public void CountIf_BareTextCriterion_StaysExactMatch_NotRegressedByDatabasePrefixFix()
    {
        // COUNTIF/SUMIF share BuiltInFunctions.Criteria's CompileCriteria matcher with the
        // database functions. Excel's COUNTIF requires EXACT match for a bare text criterion
        // (unlike database criteria ranges), so this must NOT begin matching "Davolio"/"David"
        // as a side effect of the R37-formula-database-2-1 fix.
        var sheet = BuildNameSalaryDatabase();

        _eval.Evaluate("=COUNTIF(A2:A4,\"Dav\")", sheet)
            .Should().Be(new NumberValue(0));
        _eval.Evaluate("=COUNTIF(A2:A4,\"David\")", sheet)
            .Should().Be(new NumberValue(1));
    }

    [Fact]
    public void DCount_UnresolvableField_ReturnsValueError()
    {
        // Database A1:C5 (Name/Age/Salary); criteria matches Age=30 (Alice, Carol).
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
        Set(sheet, 4, 1, new TextValue("Carol"));
        Set(sheet, 4, 2, new NumberValue(30));
        Set(sheet, 4, 3, new NumberValue(300));
        Set(sheet, 1, 5, new TextValue("Age"));
        Set(sheet, 2, 5, new NumberValue(30));

        // "Bogus" is not a header in the database -> #VALUE!, matching DSUM/DAVERAGE/etc.
        _eval.Evaluate("=DCOUNT(A1:C4,\"Bogus\",E1:E2)", sheet)
            .Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DCOUNTA(A1:C4,\"Bogus\",E1:E2)", sheet)
            .Should().Be(ErrorValue.Value);

        // Sibling regression guard: DSUM already returned #VALUE! for the identical bad field.
        _eval.Evaluate("=DSUM(A1:C4,\"Bogus\",E1:E2)", sheet)
            .Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void DCount_ValidField_StillCountsCorrectly()
    {
        // No-regression: a valid, resolvable field still counts numeric matches normally.
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
        Set(sheet, 4, 1, new TextValue("Carol"));
        Set(sheet, 4, 2, new NumberValue(30));
        Set(sheet, 4, 3, new NumberValue(300));
        Set(sheet, 1, 5, new TextValue("Age"));
        Set(sheet, 2, 5, new NumberValue(30));

        _eval.Evaluate("=DCOUNT(A1:C4,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(2));
        _eval.Evaluate("=DCOUNTA(A1:C4,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(2));
    }

    private static void Set(Sheet sheet, uint row, uint col, ScalarValue value)
        => sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
}
