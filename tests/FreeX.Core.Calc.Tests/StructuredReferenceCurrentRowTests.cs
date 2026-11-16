using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public sealed class StructuredReferenceCurrentRowTests
{
    [Fact]
    public void Recalculate_QualifiedCurrentRowOutsideTable_TracksCrossSheetCellsAndRanges()
    {
        var workbook = new Workbook("test");
        var source = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        var a = new CellAddress(source.Id, 1, 1);
        var b = new CellAddress(source.Id, 1, 2);
        var a2 = new CellAddress(source.Id, 2, 1);
        var b2 = new CellAddress(source.Id, 2, 2);
        source.SetCell(a, new TextValue("Alpha"));
        source.SetCell(b, new TextValue("Bravo"));
        source.SetCell(a2, new NumberValue(10));
        source.SetCell(b2, new NumberValue(100));
        source.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "Alias", Range = new GridRange(a, b2), Columns = { new StructuredTableColumnModel(1, "Alpha"), new StructuredTableColumnModel(2, "Bravo") } });
        var scalar = new CellAddress(other.Id, 2, 8);
        var range = new CellAddress(other.Id, 2, 9);
        var whole = new CellAddress(other.Id, 2, 10);
        other.SetFormula(scalar, "Alias[@Alpha]");
        other.SetFormula(range, "SUM(Alias[@[Alpha]:[Bravo]])");
        other.SetFormula(whole, "SUM(Alias[@])");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);
        other.GetValue(scalar).Should().Be(new NumberValue(10));
        other.GetValue(range).Should().Be(new NumberValue(110));
        other.GetValue(whole).Should().Be(new NumberValue(110));
        source.SetCell(a2, new NumberValue(20));
        var report = engine.Recalculate(workbook, [a2]);
        report.RecalculatedCells.Should().Contain([scalar, range, whole]);
        other.GetValue(scalar).Should().Be(new NumberValue(20));
        other.GetValue(range).Should().Be(new NumberValue(120));
        other.GetValue(whole).Should().Be(new NumberValue(120));
    }

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

    [Fact]
    public void Recalculate_ThisRowStructuredReference_UsesFormulaCellTableRow()
    {
        var workbook = new Workbook("StructuredReferenceThisRowTest");
        var sheet = workbook.AddSheet("Data");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Tax"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 4)),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Tax"));
        table.Columns.Add(new StructuredTableColumnModel(4, "Total"));
        sheet.StructuredTables.Add(table);

        var d2 = new CellAddress(sheet.Id, 2, 4);
        var d3 = new CellAddress(sheet.Id, 3, 4);
        sheet.SetFormula(d2, "SUM(Sales[[#This Row],[Amount]:[Tax]])");
        sheet.SetFormula(d3, "SUM(Sales[[#This Row],[Amount]:[Tax]])");

        engine.RecalculateAllFormulas(workbook);

        sheet.GetValue(d2).Should().Be(new NumberValue(11));
        sheet.GetValue(d3).Should().Be(new NumberValue(22));

        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(c3, new NumberValue(3));
        var report = engine.Recalculate(workbook, [c3]);

        report.RecalculatedCells.Should().Contain(d3);
        report.RecalculatedCells.Should().NotContain(d2);
        sheet.GetValue(d3).Should().Be(new NumberValue(23));
    }

    [Fact]
    public void Recalculate_UnqualifiedThisRowStructuredReference_UsesContainingTableRow()
    {
        var workbook = new Workbook("StructuredReferenceUnqualifiedThisRowTest");
        var sheet = workbook.AddSheet("Data");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Tax"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(2));

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
        table.Columns.Add(new StructuredTableColumnModel(1, "Amount"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Tax"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Total"));
        sheet.StructuredTables.Add(table);

        var c2 = new CellAddress(sheet.Id, 2, 3);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.SetFormula(c2, "SUM([[#This Row],[Amount]:[Tax]])");
        sheet.SetFormula(c3, "SUM([[#This Row],[Amount]:[Tax]])");

        engine.RecalculateAllFormulas(workbook);

        sheet.GetValue(c2).Should().Be(new NumberValue(11));
        sheet.GetValue(c3).Should().Be(new NumberValue(22));
    }
}
