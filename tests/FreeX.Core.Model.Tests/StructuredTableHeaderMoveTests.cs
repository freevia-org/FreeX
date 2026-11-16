using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class StructuredTableHeaderMoveTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingHeaderAndFormulaOutsideTable_QualifiesImplicitReferenceAndUndoes(bool crossSheet)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var body = new CellAddress(sheet.Id, 2, 1);
        var destination = new CellAddress(crossSheet ? other.Id : sheet.Id, 1, 8);
        var movedFormula = new CellAddress(destination.Sheet, 2, 8);
        sheet.SetCell(a, new TextValue("Alpha"));
        sheet.SetCell(b, new TextValue("Charlie"));
        sheet.SetCell(body, Cell.FromFormula("SUM([Charlie])"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 3, 2)), Columns = { new StructuredTableColumnModel(1, "Alpha"), new StructuredTableColumnModel(2, "Charlie") } });
        var command = new MoveRangeCommand(sheet.Id, new GridRange(a, body), destination);
        var ctx = new TestCommandContext(workbook);
        var result = command.Apply(ctx);
        result.Success.Should().BeTrue(result.ErrorMessage);
        ctx.GetSheet(destination.Sheet).GetCell(movedFormula)!.FormulaText.Should().Be("SUM(SalesTable[Charlie])");
        sheet.GetValue(a).Should().Be(new TextValue("Column1"));
        command.Revert(ctx);
        sheet.GetCell(body)!.FormulaText.Should().Be("SUM([Charlie])");
        command.Apply(ctx).Success.Should().BeTrue();
        ctx.GetSheet(destination.Sheet).GetCell(movedFormula)!.FormulaText.Should().Be("SUM(SalesTable[Charlie])");
    }

    [Theory]
    [InlineData("[@Charlie]+1", "SalesTable[@Charlie]+1")]
    [InlineData("SalesTable[@Charlie]+1", "SalesTable[@Charlie]+1")]
    [InlineData("Alias[@Charlie]+1", "Alias[@Charlie]+1")]
    public void MovingCurrentRowFormulaOutsideTable_QualifiesAndRetainsRowBinding(string original, string expected)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var body = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a, new TextValue("Alpha"));
        sheet.SetCell(b, new TextValue("Charlie"));
        sheet.SetCell(body, Cell.FromFormula(original));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1000));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "Alias", Range = new GridRange(a, new CellAddress(sheet.Id, 3, 2)), Columns = { new StructuredTableColumnModel(1, "Alpha"), new StructuredTableColumnModel(2, "Charlie") } });
        var command = new MoveRangeCommand(sheet.Id, new GridRange(a, body), new CellAddress(sheet.Id, 1, 8));
        var ctx = new TestCommandContext(workbook);
        var result = command.Apply(ctx);
        result.Success.Should().BeTrue(result.ErrorMessage);
        var moved = new CellAddress(sheet.Id, 2, 8);
        sheet.GetCell(moved)!.FormulaText.Should().Be(expected);
        new FormulaEvaluator().Evaluate(sheet.GetCell(moved)!.FormulaText!, sheet, workbook, moved).Should().Be(new NumberValue(1001));
        command.Revert(ctx);
        sheet.GetValue(a).Should().Be(new TextValue("Alpha"));
        sheet.GetCell(body)!.FormulaText.Should().Be(original);
    }

    [Fact]
    public void CrossSheetHeaderBlockMove_RewritesWorkbookMetadataAndRestoresOnUndo()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var dest = new CellAddress(other.Id, 1, 1);
        sheet.SetCell(a, new TextValue("Alpha"));
        sheet.SetCell(b, new TextValue("Bravo"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 3, 2)), Columns = { new StructuredTableColumnModel(1, "Alpha"), new StructuredTableColumnModel(2, "Bravo", TotalsRowFormula: "SUM(SalesTable[Alpha])") } });
        workbook.NamedFormulas["Amount"] = "SUM(SalesTable[Alpha])";
        workbook.DefineNamedFormula("Scoped", "SUM(SalesTable[Bravo])", other.Id);
        var ruleRange = new GridRange(new CellAddress(other.Id, 4, 1), new CellAddress(other.Id, 4, 1));
        var cf = new ConditionalFormat { AppliesTo = ruleRange, FormulaText = "SUM(SalesTable[Alpha])>0" };
        var dv = new DataValidation { AppliesTo = ruleRange, Formula1 = "SalesTable[Bravo]" };
        other.ConditionalFormats.Add(cf);
        other.DataValidations.Add(dv);
        var chart = new ChartModel { Type = ChartType.Column, VerbatimSeriesFormulas = [new ChartSeriesVerbatimFormulas(0, "SalesTable[Alpha]", null, null, null)] };
        other.Charts.Add(chart);
        var command = new MoveRangeCommand(sheet.Id, new GridRange(a, b), dest);
        var ctx = new TestCommandContext(workbook);
        var result = command.Apply(ctx);
        result.Success.Should().BeTrue(result.ErrorMessage);
        workbook.NamedFormulas["Amount"].Should().Be("SUM(SalesTable[Column1])");
        workbook.ScopedNamedFormulas[("Scoped", other.Id)].Should().Be("SUM(SalesTable[Column2])");
        cf.FormulaText.Should().Be("SUM(SalesTable[Column1])>0");
        dv.Formula1.Should().Be("SalesTable[Column2]");
        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("SalesTable[Column1]");
        sheet.StructuredTables[0].Columns[1].TotalsRowFormula.Should().Be("SUM(SalesTable[Column1])");
        command.Revert(ctx);
        workbook.NamedFormulas["Amount"].Should().Be("SUM(SalesTable[Alpha])");
        workbook.ScopedNamedFormulas[("Scoped", other.Id)].Should().Be("SUM(SalesTable[Bravo])");
        cf.FormulaText.Should().Be("SUM(SalesTable[Alpha])>0");
        dv.Formula1.Should().Be("SalesTable[Bravo]");
        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("SalesTable[Alpha]");
        sheet.StructuredTables[0].Columns[1].TotalsRowFormula.Should().Be("SUM(SalesTable[Alpha])");
    }

    [Fact]
    public void InvalidHeaderMetadata_RollsBackEntirePartialBlockMove()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var c = new CellAddress(sheet.Id, 1, 3);
        var dest = new CellAddress(sheet.Id, 1, 8);
        sheet.SetCell(a, new TextValue("Alpha"));
        sheet.SetCell(b, new TextValue("Bravo"));
        sheet.SetCell(c, new TextValue("Charlie"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 3, 3)), Columns = { new StructuredTableColumnModel(1, "Alpha") } });
        var command = new MoveRangeCommand(sheet.Id, new GridRange(a, b), dest);
        var result = command.Apply(new TestCommandContext(workbook));
        result.Success.Should().BeFalse();
        sheet.GetValue(a).Should().Be(new TextValue("Alpha"));
        sheet.GetValue(b).Should().Be(new TextValue("Bravo"));
        sheet.GetValue(dest).Should().Be(BlankValue.Instance);
        sheet.StructuredTables[0].Columns.Should().ContainSingle();
    }

    [Theory]
    [InlineData("out", "Column1", "Column2", "Charlie")]
    [InlineData("vertical", "Column1", "Bravo", "Charlie")]
    [InlineData("into", "NewOne", "NewTwo", "Charlie")]
    [InlineData("overlap", "Column1", "Alpha", "Bravo")]
    public void MovePartialTableHeaders_ReconcilesPositionallyAndUndoes(string mode, string first, string second, string third)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        CellAddress At(uint row, uint col) => new(sheet.Id, row, col);
        var formula = new CellAddress(other.Id, 1, 1);
        sheet.SetCell(At(1, 1), new TextValue("Alpha"));
        sheet.SetCell(At(1, 2), new TextValue("Bravo"));
        sheet.SetCell(At(1, 3), new TextValue("Charlie"));
        sheet.SetCell(At(2, 1), new NumberValue(10));
        sheet.SetCell(At(2, 2), new NumberValue(100));
        sheet.SetCell(At(2, 3), new NumberValue(1000));
        sheet.SetCell(At(1, 6), new TextValue("NewOne"));
        sheet.SetCell(At(1, 7), new TextValue("NewTwo"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(At(1, 1), At(3, 3)), Columns = { new StructuredTableColumnModel(1, "Alpha"), new StructuredTableColumnModel(2, "Bravo"), new StructuredTableColumnModel(3, "Charlie") } });
        other.SetCell(formula, Cell.FromFormula("SUM(SalesTable[Alpha])+SUM(SalesTable[Bravo])+SUM(SalesTable[Charlie])"));
        var (source, destination) = mode switch
        {
            "out" => (new GridRange(At(1, 1), At(1, 2)), At(1, 8)),
            "vertical" => (new GridRange(At(1, 1), At(2, 1)), At(1, 8)),
            "into" => (new GridRange(At(1, 6), At(1, 7)), At(1, 1)),
            _ => (new GridRange(At(1, 1), At(1, 2)), At(1, 2))
        };
        var command = new MoveRangeCommand(sheet.Id, source, destination);
        var ctx = new TestCommandContext(workbook);
        var result = command.Apply(ctx);
        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.StructuredTables[0].Columns.Select(column => column.Name).Should().Equal(first, second, third);
        other.GetCell(formula)!.FormulaText.Should().Be($"SUM(SalesTable[{first}])+SUM(SalesTable[{second}])+SUM(SalesTable[{third}])");
        if (mode == "vertical")
        {
            sheet.GetValue(2, 8).Should().Be(new NumberValue(10));
            sheet.GetValue(2, 1).Should().Be(BlankValue.Instance);
        }
        command.Revert(ctx);
        sheet.StructuredTables[0].Columns.Select(column => column.Name).Should().Equal("Alpha", "Bravo", "Charlie");
        other.GetCell(formula)!.FormulaText.Should().Be("SUM(SalesTable[Alpha])+SUM(SalesTable[Bravo])+SUM(SalesTable[Charlie])");
        sheet.GetValue(2, 1).Should().Be(new NumberValue(10));
        command.Apply(ctx).Success.Should().BeTrue();
        other.GetCell(formula)!.FormulaText.Should().Be($"SUM(SalesTable[{first}])+SUM(SalesTable[{second}])+SUM(SalesTable[{third}])");
    }
}
