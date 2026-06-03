using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    private Sheet MakeDbSheet()
    {
        // Database A1:C5 (1 header row + 4 data rows):
        //   Name   Age  Salary
        //   Alice  30   100
        //   Bob    25   200
        //   Carol  30   300
        //   Dave   40   400
        return MakeSheet(
            (1, 1, new TextValue("Name")), (1, 2, new TextValue("Age")), (1, 3, new TextValue("Salary")),
            (2, 1, new TextValue("Alice")), (2, 2, new NumberValue(30)), (2, 3, new NumberValue(100)),
            (3, 1, new TextValue("Bob")),   (3, 2, new NumberValue(25)), (3, 3, new NumberValue(200)),
            (4, 1, new TextValue("Carol")), (4, 2, new NumberValue(30)), (4, 3, new NumberValue(300)),
            (5, 1, new TextValue("Dave")),  (5, 2, new NumberValue(40)), (5, 3, new NumberValue(400)),
            // Criteria E1:E2 = Age | 30
            (1, 5, new TextValue("Age")),
            (2, 5, new NumberValue(30)));
    }

    [Fact]
    public void DSum_FilterByAge_SumsMatchingSalaries()
    {
        var sheet = MakeDbSheet();
        // Rows where Age=30 (Alice 100, Carol 300) → Sum = 400
        _eval.Evaluate("=DSUM(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(400));
    }

    [Fact]
    public void DAverage_FilterByAge_AveragesMatchingSalaries()
    {
        var sheet = MakeDbSheet();
        _eval.Evaluate("=DAVERAGE(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(200));
    }

    [Fact]
    public void DCount_FilterByAge_CountsMatching()
    {
        var sheet = MakeDbSheet();
        _eval.Evaluate("=DCOUNT(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(2));
    }

    [Fact]
    public void DCountA_FilterByAge_CountsNonBlank()
    {
        var sheet = MakeDbSheet();
        _eval.Evaluate("=DCOUNTA(A1:C5,\"Name\",E1:E2)", sheet)
            .Should().Be(new NumberValue(2));
    }

    [Fact]
    public void DGet_UniqueMatch_ReturnsValue()
    {
        var sheet = MakeDbSheet();
        // Filter Age=25 (Bob) → single match → return Salary 200
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Age"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new NumberValue(25));
        _eval.Evaluate("=DGET(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(200));
    }

    [Fact]
    public void DGet_MultipleMatches_ReturnsNum()
    {
        var sheet = MakeDbSheet();
        // Age=30 matches 2 rows → #NUM!
        _eval.Evaluate("=DGET(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void DMax_FilterByAge_ReturnsMax()
    {
        var sheet = MakeDbSheet();
        _eval.Evaluate("=DMAX(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(300));
    }

    [Fact]
    public void DMin_FilterByAge_ReturnsMin()
    {
        var sheet = MakeDbSheet();
        _eval.Evaluate("=DMIN(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(100));
    }

    [Fact]
    public void DProduct_FilterByAge_ReturnsProduct()
    {
        var sheet = MakeDbSheet();
        _eval.Evaluate("=DPRODUCT(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(30000));
    }
}
