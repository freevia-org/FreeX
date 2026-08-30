using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-21 review fixes for the database (D*) functions in BuiltInFunctions.Database.cs:
///   R21-database-functions-1: DMAX/DMIN must return 0 (not #NUM!) when no records match.
///   R21-database-functions-2: DGET's "more than one record matches" #NUM! rule must win
///     even when one of the matching rows' field cells is itself an error.
///   R21-database-functions-3: field-by-name lookup must match a numeric database header,
///     mirroring the criteria-column header lookup in the same file.
/// </summary>
public sealed class R21_DatabaseFunctionsTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=DMAX(A1:C5,\"Salary\",E1:E2)")]
    [InlineData("=DMIN(A1:C5,\"Salary\",E1:E2)")]
    public void DMaxDMin_NoRecordsMatchCriteria_ReturnsZeroNotNum(string formula)
    {
        // Age/Salary database; criteria filters on Age=99, which no row satisfies.
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
        Set(sheet, 5, 1, new TextValue("Dave"));
        Set(sheet, 5, 2, new NumberValue(40));
        Set(sheet, 5, 3, new NumberValue(400));
        Set(sheet, 1, 5, new TextValue("Age"));
        Set(sheet, 2, 5, new NumberValue(99));

        _eval.Evaluate(formula, sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void DGet_MultipleMatches_ReturnsNumEvenWhenAMatchedRowsFieldIsAnError()
    {
        // Two rows match Age=30 (row 2 and row 3); row 3's Salary cell is itself #DIV/0!.
        // Excel's documented "more than one record satisfies the criteria" #NUM! rule must
        // win over the raw field error, regardless of scan order.
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

        _eval.Evaluate("=DGET(A1:B4,\"Salary\",D1:D2)", sheet)
            .Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void FieldByName_NumericDatabaseHeader_ResolvesLikeCriteriaHeaderDoes()
    {
        // Database header B1 is a NumberValue (2020), e.g. a year-column template.
        // Field-by-name lookup ("2020") must resolve it the same way the criteria header plan
        // already resolves numeric headers for criteria-column matching.
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Item"));
        Set(sheet, 1, 2, new NumberValue(2020));
        Set(sheet, 2, 1, new TextValue("Widget"));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new TextValue("Gadget"));
        Set(sheet, 3, 2, new NumberValue(200));
        Set(sheet, 1, 5, new TextValue("Item"));
        Set(sheet, 2, 5, new TextValue("Widget"));

        _eval.Evaluate("=DSUM(A1:B3,\"2020\",E1:E2)", sheet)
            .Should().Be(new NumberValue(100));
    }

    private static void Set(Sheet sheet, uint row, uint col, ScalarValue value)
        => sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
}
