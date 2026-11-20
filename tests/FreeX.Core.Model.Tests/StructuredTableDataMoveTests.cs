using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class StructuredTableDataMoveTests
{
    [Fact]
    public void TotalsRowOnlyMove_RemainsRejected()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var header = new CellAddress(sheet.Id, 1, 1);
        var total = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(header, new TextValue("Alpha"));
        sheet.SetCell(total, new NumberValue(42));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(header, total), TotalsRowShown = true, Columns = { new StructuredTableColumnModel(1, "Alpha", TotalsRowFunction: "sum") } });
        var result = new MoveRangeCommand(sheet.Id, new GridRange(total, total), new CellAddress(sheet.Id, 3, 8)).Apply(new TestCommandContext(workbook));
        result.Success.Should().BeFalse();
        sheet.GetValue(total).Should().Be(new NumberValue(42));
        sheet.StructuredTables[0].Columns[0].TotalsRowFunction.Should().Be("sum");
    }

    [Theory]
    [InlineData("protected")]
    [InlineData("merged")]
    [InlineData("spill")]
    public void MoveTableData_RetainsExistingSafetyGuards(string kind)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Bravo"));
        sheet.SetCell(a2, new NumberValue(10));
        sheet.SetCell(a3, new NumberValue(20));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a1, new CellAddress(sheet.Id, 3, 2)), Columns = { new StructuredTableColumnModel(1, "Alpha"), new StructuredTableColumnModel(2, "Bravo") } });
        if (kind == "protected") sheet.IsProtected = true;
        if (kind == "merged") sheet.AddMergedRegion(new GridRange(a2, b2));
        if (kind == "spill") sheet.SetSpillRange(a2, new RangeValue(new ScalarValue[,] { { new NumberValue(10), new NumberValue(11) }, { new NumberValue(20), new NumberValue(21) } }));
        var destination = new CellAddress(sheet.Id, 2, 8);
        var result = new MoveRangeCommand(sheet.Id, new GridRange(a2, a3), destination).Apply(new TestCommandContext(workbook));
        result.Success.Should().BeFalse();
        sheet.GetValue(a2).Should().Be(new NumberValue(10));
        sheet.GetValue(destination).Should().Be(BlankValue.Instance);
        sheet.StructuredTables[0].Columns.Select(c => c.Name).Should().Equal("Alpha", "Bravo");
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("cross")]
    [InlineData("into")]
    [InlineData("overlap")]
    public void MoveTableData_PreservesIdentityFormulasAndUndoRedo(string mode)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        CellAddress At(uint row, uint col) => new(sheet.Id, row, col);
        var a1 = At(1, 1);
        sheet.SetCell(a1, new TextValue("Alpha"));
        sheet.SetCell(At(1, 2), new TextValue("Bravo"));
        sheet.SetCell(At(1, 3), new TextValue("Charlie"));
        sheet.SetFormula(At(2, 1), "[@Charlie]");
        sheet.SetCell(At(3, 1), new NumberValue(20));
        sheet.SetCell(At(2, 2), new NumberValue(100));
        sheet.SetCell(At(3, 2), new NumberValue(200));
        sheet.SetCell(At(2, 3), new NumberValue(1000));
        sheet.SetCell(At(3, 3), new NumberValue(2000));
        sheet.SetCell(At(2, 6), new NumberValue(7));
        sheet.SetCell(At(3, 6), new NumberValue(8));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a1, At(3, 3)), Columns = { new StructuredTableColumnModel(1, "Alpha"), new StructuredTableColumnModel(2, "Bravo"), new StructuredTableColumnModel(3, "Charlie") } });
        var bravoTotal = new CellAddress(other.Id, 5, 1);
        var charlieTotal = new CellAddress(other.Id, 6, 1);
        other.SetFormula(bravoTotal, "SUM(SalesTable[Bravo])");
        other.SetFormula(charlieTotal, "SUM(SalesTable[Charlie])");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);
        var (source, destination) = mode switch
        {
            "outside" => (new GridRange(At(2, 1), At(3, 1)), At(2, 8)),
            "cross" => (new GridRange(At(2, 1), At(3, 1)), new CellAddress(other.Id, 2, 8)),
            "into" => (new GridRange(At(2, 6), At(3, 6)), At(2, 2)),
            _ => (new GridRange(At(2, 1), At(3, 2)), At(2, 2))
        };
        var command = new MoveRangeCommand(sheet.Id, source, destination);
        var ctx = new TestCommandContext(workbook);
        var result = command.Apply(ctx);
        result.Success.Should().BeTrue(result.ErrorMessage);
        engine.RecalculateAllFormulas(workbook);
        sheet.StructuredTables[0].Columns.Select(c => c.Name).Should().Equal("Alpha", "Bravo", "Charlie");
        other.GetCell(bravoTotal)!.FormulaText.Should().Be("SUM(SalesTable[Bravo])");
        other.GetCell(charlieTotal)!.FormulaText.Should().Be("SUM(SalesTable[Charlie])");
        if (mode is "outside" or "cross")
        {
            var movedSheet = mode == "cross" ? other : sheet;
            var moved = new CellAddress(movedSheet.Id, 2, 8);
            movedSheet.GetCell(moved)!.FormulaText.Should().Be("SalesTable[@Charlie]");
            movedSheet.GetValue(moved).Should().Be(new NumberValue(1000));
            sheet.GetValue(At(2, 1)).Should().Be(BlankValue.Instance);
        }
        if (mode == "into") other.GetValue(bravoTotal).Should().Be(new NumberValue(15));
        if (mode == "overlap")
        {
            sheet.GetCell(At(2, 2))!.FormulaText.Should().Be("[@Charlie]");
            sheet.GetValue(At(2, 2)).Should().Be(new NumberValue(100));
            other.GetValue(bravoTotal).Should().Be(new NumberValue(120));
            other.GetValue(charlieTotal).Should().Be(new NumberValue(300));
        }
        command.Revert(ctx);
        engine.RecalculateAllFormulas(workbook);
        sheet.GetCell(At(2, 1))!.FormulaText.Should().Be("[@Charlie]");
        sheet.GetValue(At(2, 1)).Should().Be(new NumberValue(1000));
        sheet.GetValue(At(2, 2)).Should().Be(new NumberValue(100));
        sheet.GetValue(At(2, 3)).Should().Be(new NumberValue(1000));
        command.Apply(ctx).Success.Should().BeTrue();
        engine.RecalculateAllFormulas(workbook);
        if (mode == "overlap") other.GetValue(bravoTotal).Should().Be(new NumberValue(120));
        if (mode == "into") other.GetValue(bravoTotal).Should().Be(new NumberValue(15));
    }
}
