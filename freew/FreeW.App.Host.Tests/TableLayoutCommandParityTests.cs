using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using WpfTable = System.Windows.Documents.Table;

namespace FreeW.App.Host.Tests;

public sealed class TableLayoutCommandParityTests
{
    private static DocumentView CreateView(out Table source)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        source = Table.Create(3, 3);
        source.Rows[0].HeightPt = 12;
        source.Rows[1].HeightPt = 24;
        source.Rows[2].HeightPt = 36;
        source.ColumnWidthsPt.AddRange([60, 120, 180]);
        foreach (var row in source.Rows)
            for (var column = 0; column < row.Cells.Count; column++)
                row.Cells[column].WidthPt = source.ColumnWidthsPt[column];
        document.Blocks.Add(source);
        var view = new DocumentView();
        view.LoadModel(document);
        PlaceCaretInFirstCell(view);
        return view;
    }

    private static void PlaceCaretInFirstCell(DocumentView view)
    {
        var cell = view.Document.Blocks.OfType<WpfTable>().Single().RowGroups[0].Rows[0].Cells[0];
        view.CaretPosition = cell.ContentStart;
    }

    [StaFact]
    public void TableLayoutCommands_AreUndoableThroughTheWpfHost()
    {
        var view = CreateView(out var table);
        view.CommitToModel();
        PlaceCaretInFirstCell(view);
        table = view.Model.Blocks.OfType<Table>().Single();
        var priorRows = table.Rows.Select(row => (row.HeightPt, row.HeightRule)).ToArray();
        var explicitHeights = priorRows.Where(row => row.HeightPt.HasValue).Select(row => row.HeightPt!.Value).ToArray();
        var distributedHeight = explicitHeights.Length == 0 ? (double?)null : explicitHeights.Average();

        view.DistributeTableRows();
        table = view.Model.Blocks.OfType<Table>().Single();
        table.Rows.Should().OnlyContain(row => row.HeightPt == distributedHeight);
        view.Undo();
        table.Rows.Select(row => (row.HeightPt, row.HeightRule)).Should().Equal(priorRows);

        PlaceCaretInFirstCell(view);
        view.CommitToModel();
        PlaceCaretInFirstCell(view);
        table = view.Model.Blocks.OfType<Table>().Single();
        var priorGridWidths = table.ColumnWidthsPt.ToArray();
        var priorCellWidths = table.Rows.SelectMany(row => row.Cells).Select(cell => cell.WidthPt).ToArray();
        var distributedWidth = priorGridWidths.Length == table.ColumnCount
            ? priorGridWidths.Sum() / table.ColumnCount
            : (table.PreferredWidthPt ?? TableLayoutOperations.DefaultAutoFitWindowWidthPt) / table.ColumnCount;
        view.DistributeTableColumns();
        table = view.Model.Blocks.OfType<Table>().Single();
        table.ColumnWidthsPt.Should().OnlyContain(width => Math.Abs(width - distributedWidth) < 0.001);
        view.Undo();
        table.ColumnWidthsPt.Should().Equal(priorGridWidths);
        table.Rows.SelectMany(row => row.Cells).Select(cell => cell.WidthPt).Should().Equal(priorCellWidths);

        PlaceCaretInFirstCell(view);
        view.CommitToModel();
        PlaceCaretInFirstCell(view);
        table = view.Model.Blocks.OfType<Table>().Single();
        var priorAutoFit = table.AutoFit;
        priorGridWidths = table.ColumnWidthsPt.ToArray();
        view.SetTableAutoFit(AutoFitMode.Contents);
        table = view.Model.Blocks.OfType<Table>().Single();
        table.AutoFit.Should().Be(AutoFitMode.Contents);
        table.ColumnWidthsPt.Should().BeEmpty();
        view.Undo();
        table.AutoFit.Should().Be(priorAutoFit);
        table.ColumnWidthsPt.Should().Equal(priorGridWidths);
    }

    [StaFact]
    public void SplitCell_ForwardsRequestedSubdivisionThroughTheWpfHost()
    {
        var view = CreateView(out _);
        view.CommitToModel();
        PlaceCaretInFirstCell(view);

        view.SplitCell(rows: 2, columns: 2);

        var table = view.Model.Blocks.OfType<Table>().Single();
        table.Rows.Should().HaveCount(4);
        table.Rows[0].Cells.Should().HaveCount(4);
        table.Rows[2].Cells[0].GridSpan.Should().Be(2);
        table.Rows[0].Cells[2].VerticalMerge.Should().Be(VerticalMergeState.Restart);
        table.Rows[1].Cells[2].VerticalMerge.Should().Be(VerticalMergeState.Continue);

        view.Undo();
        table.Rows.Should().HaveCount(3);
        table.Rows[0].Cells.Should().HaveCount(3);
        table.Rows[1].Cells[0].GridSpan.Should().Be(1);
    }
}
