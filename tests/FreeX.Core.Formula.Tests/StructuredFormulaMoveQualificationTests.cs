using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

public sealed class StructuredFormulaMoveQualificationTests
{
    [Theory]
    [InlineData(false, 1, false)]
    [InlineData(false, 2, true)]
    [InlineData(false, 3, true)]
    [InlineData(false, 4, false)]
    [InlineData(true, 2, true)]
    [InlineData(true, 3, true)]
    [InlineData(true, 6, false)]
    public void QualifiedCurrentRow_UsesCallerRowAcrossSheetsButRequiresDataBody(bool crossSheet, uint row, bool inBody)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        var a = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a, new TextValue("Charlie"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1000));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2000));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "Alias", Range = new GridRange(a, new CellAddress(sheet.Id, 3, 1)), Columns = { new StructuredTableColumnModel(1, "Charlie") } });
        var callerSheet = crossSheet ? other : sheet;
        var caller = new CellAddress(callerSheet.Id, row, 8);
        ScalarValue expected = inBody ? new NumberValue(row == 2 ? 1000 : 2000) : ErrorValue.Value;
        var evaluator = new FormulaEvaluator();
        evaluator.Evaluate("Alias[@Charlie]", callerSheet, workbook, caller).Should().Be(expected);
        evaluator.Evaluate("SUM(Alias[[#This Row],[Charlie]])", callerSheet, workbook, caller).Should().Be(expected);
    }

    [Fact]
    public void QualifiesOnlyImplicitSelectorsWithoutChangingOtherText()
    {
        const string formula = "= SUM([Charlie],Other[Charlie],\"[Charlie]\",[[#Data],[A'#B]])";
        StructuredFormulaMoveQualification.TryQualify(formula, "SalesTable", out var result).Should().BeTrue();
        result.Should().Be("= SUM(SalesTable[Charlie],Other[Charlie],\"[Charlie]\",SalesTable[[#Data],[A'#B]])");
    }

    [Theory]
    [InlineData("SUM([@Charlie])", "SUM(SalesTable[@Charlie])")]
    [InlineData("SUM([[#This Row],[Charlie]])", "SUM(SalesTable[[#This Row],[Charlie]])")]
    [InlineData("SUM(SalesTable[@Charlie])", "SUM(SalesTable[@Charlie])")]
    [InlineData("SUM(Alias[[#This Row],[Charlie]])", "SUM(Alias[[#This Row],[Charlie]])")]
    public void QualifiesCurrentRowSelectors(string formula, string expected)
    {
        StructuredFormulaMoveQualification.TryQualify(formula, "SalesTable", out var result).Should().BeTrue();
        result.Should().Be(expected);
    }
}
