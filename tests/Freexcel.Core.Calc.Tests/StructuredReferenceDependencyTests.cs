using Freexcel.Core.Calc;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Calc.Tests;

public sealed class StructuredReferenceDependencyTests
{
    [Fact]
    public void Recalculate_TableColumnReference_TracksDataBodyCellDependencies()
    {
        var workbook = new Workbook("StructuredReferenceDependencyTest");
        var sheet = workbook.AddSheet("Data");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        sheet.StructuredTables.Add(table);

        var formula = new CellAddress(sheet.Id, 5, 2);
        sheet.SetFormula(formula, "SUM(Sales[Amount])");
        engine.RecalculateAllFormulas(workbook);
        sheet.GetValue(formula).Should().Be(new NumberValue(30));

        var changed = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(changed, new NumberValue(25));
        var report = engine.Recalculate(workbook, [changed]);

        report.RecalculatedCells.Should().Contain(formula);
        sheet.GetValue(formula).Should().Be(new NumberValue(35));
    }
}
