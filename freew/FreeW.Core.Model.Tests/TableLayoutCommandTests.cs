namespace FreeW.Core.Model.Tests;

public sealed class TableLayoutCommandTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static (DocumentCommandBus Bus, Table Table) CreateTable(int rows = 3, int columns = 3)
    {
        var document = new TextDocument();
        var table = Table.Create(rows, columns);
        document.Blocks.Add(table);
        return (new DocumentCommandBus(new Context(document)), table);
    }

    [Fact]
    public void DistributeRows_IsUndoableAndRedoable()
    {
        var (bus, table) = CreateTable();
        table.Rows[0].HeightPt = 12;
        table.Rows[0].HeightRule = TableRowHeightRule.AtLeast;
        table.Rows[1].HeightPt = null;
        table.Rows[1].HeightRule = TableRowHeightRule.Auto;
        table.Rows[2].HeightPt = 24;
        table.Rows[2].HeightRule = TableRowHeightRule.Exact;

        bus.Execute(new DistributeTableRowsCommand(0));

        table.Rows.Should().OnlyContain(row => row.HeightPt == 18);
        table.Rows.Should().OnlyContain(row => row.HeightRule == TableRowHeightRule.Exact);

        bus.Undo().Should().BeTrue();
        table.Rows.Select(row => row.HeightPt).Should().Equal(12, null, 24);
        table.Rows.Select(row => row.HeightRule).Should().Equal(
            TableRowHeightRule.AtLeast,
            TableRowHeightRule.Auto,
            TableRowHeightRule.Exact);

        bus.Redo().Should().BeTrue();
        table.Rows.Should().OnlyContain(row => row.HeightPt == 18);
    }

    [Fact]
    public void DistributeColumns_IsUndoableAndRedoable()
    {
        var (bus, table) = CreateTable(rows: 2);
        table.ColumnWidthsPt.AddRange([60, 120, 180]);
        var priorCellWidths = new double?[] { 55, 115, 175, 65, 125, 185 };
        var index = 0;
        foreach (var cell in table.Rows.SelectMany(row => row.Cells))
            cell.WidthPt = priorCellWidths[index++];

        bus.Execute(new DistributeTableColumnsCommand(0));

        table.ColumnWidthsPt.Should().Equal(120, 120, 120);
        table.Rows.SelectMany(row => row.Cells).Should().OnlyContain(cell => cell.WidthPt == 120);

        bus.Undo().Should().BeTrue();
        table.ColumnWidthsPt.Should().Equal(60, 120, 180);
        table.Rows.SelectMany(row => row.Cells).Select(cell => cell.WidthPt).Should().Equal(priorCellWidths);

        bus.Redo().Should().BeTrue();
        table.ColumnWidthsPt.Should().Equal(120, 120, 120);
    }

    [Fact]
    public void AutoFitContents_IsUndoableAndRedoable()
    {
        var (bus, table) = CreateTable(rows: 2, columns: 2);
        table.AutoFit = AutoFitMode.Fixed;
        table.PreferredWidthPt = 300;
        table.ColumnWidthsPt.AddRange([100, 200]);
        var priorCellWidths = new double?[] { 90, 190, 110, 210 };
        var index = 0;
        foreach (var cell in table.Rows.SelectMany(row => row.Cells))
            cell.WidthPt = priorCellWidths[index++];

        bus.Execute(new SetTableAutoFitCommand(0, AutoFitMode.Contents));

        table.AutoFit.Should().Be(AutoFitMode.Contents);
        table.ColumnWidthsPt.Should().BeEmpty();
        table.Rows.SelectMany(row => row.Cells).Should().OnlyContain(cell => cell.WidthPt == null);

        bus.Undo().Should().BeTrue();
        table.AutoFit.Should().Be(AutoFitMode.Fixed);
        table.PreferredWidthPt.Should().Be(300);
        table.ColumnWidthsPt.Should().Equal(100, 200);
        table.Rows.SelectMany(row => row.Cells).Select(cell => cell.WidthPt).Should().Equal(priorCellWidths);

        bus.Redo().Should().BeTrue();
        table.AutoFit.Should().Be(AutoFitMode.Contents);
        table.ColumnWidthsPt.Should().BeEmpty();
    }

    [Fact]
    public void AutoFitWindow_RestoresPriorPreferredWidthOnUndo()
    {
        var (bus, table) = CreateTable();
        table.PreferredWidthPt = 250;

        bus.Execute(new SetTableAutoFitCommand(0, AutoFitMode.Window));
        table.AutoFit.Should().Be(AutoFitMode.Window);
        table.PreferredWidthPt.Should().Be(TableLayoutOperations.DefaultAutoFitWindowWidthPt);

        bus.Undo().Should().BeTrue();
        table.AutoFit.Should().Be(AutoFitMode.Fixed);
        table.PreferredWidthPt.Should().Be(250);
    }
}
