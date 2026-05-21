using Freexcel.Core.Calc;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Calc.Tests;

public sealed class StructuredReferenceCurrentRowTests
{
    [Fact]
    public void Recalculate_CurrentRowStructuredReference_UsesFormulaCellTableRow()
    {
        var workbook = new Workbook("StructuredReferenceCurrentRowTest");
        var sheet = workbook.AddSheet("Data");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Double"));
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
                new CellAddress(sheet.Id, 3, 3)),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Double"));
        sheet.StructuredTables.Add(table);

        var c2 = new CellAddress(sheet.Id, 2, 3);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.SetFormula(c2, "[@Amount]*2");
        sheet.SetFormula(c3, "[@Amount]*2");

        engine.RecalculateAllFormulas(workbook);

        sheet.GetValue(c2).Should().Be(new NumberValue(20));
        sheet.GetValue(c3).Should().Be(new NumberValue(40));

        var b3 = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(b3, new NumberValue(25));
        var report = engine.Recalculate(workbook, [b3]);

        report.RecalculatedCells.Should().Contain(c3);
        report.RecalculatedCells.Should().NotContain(c2);
        sheet.GetValue(c3).Should().Be(new NumberValue(50));
    }
}
