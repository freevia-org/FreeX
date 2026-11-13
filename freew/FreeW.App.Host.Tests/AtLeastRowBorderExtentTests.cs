using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;
using ModelTable = FreeW.Core.Model.Table;
using ModelTableCell = FreeW.Core.Model.TableCell;
using ModelTableRow = FreeW.Core.Model.TableRow;

namespace FreeW.App.Host.Tests;

/// <summary>
/// An "at least" row whose cells carry their own (double/thick) border rules is laid out with those
/// rules outside the authored height, as Word does.
/// </summary>
public sealed class AtLeastRowBorderExtentTests
{
    private const double PxPerPoint = 96.0 / 72.0;

    [StaTheory]
    [InlineData(TableRowHeightRule.AtLeast, (3 * 1.25 + 2 * 1.25) * 96.0 / 72.0)]
    [InlineData(TableRowHeightRule.Auto, (3 * 1.25 + 2 * 1.25) * 96.0 / 72.0)]
    public void BorderedCellHost_MinimumHeight_AddsTheBorderExtentToTheAuthoredMinimum(
        TableRowHeightRule rule,
        double expectedExtraDip)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = new ModelTable();
        table.ColumnWidthsPt.Add(200);
        table.Rows.Add(new ModelTableRow
        {
            HeightPt = 30,
            HeightRule = rule,
            Cells =
            {
                new ModelTableCell("Region")
                {
                    Borders = new CellBorders
                    {
                        Top = new CellBorderEdge(BorderLineStyle.Double, "#1F4E79", 1.25),
                        Bottom = new CellBorderEdge(BorderLineStyle.Thick, "#1F4E79", 1.25),
                    }
                }
            }
        });
        doc.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(doc);
        var wpfTable = view.Document.Blocks.OfType<System.Windows.Documents.Table>().Single();
        var host = wpfTable.RowGroups[0].Rows[0].Cells[0].Blocks.OfType<BlockUIContainer>().First().Child
            as System.Windows.Controls.Grid;

        Assert.NotNull(host);
        Assert.Equal(30 * PxPerPoint + expectedExtraDip, host!.MinHeight, 6);
    }
}
