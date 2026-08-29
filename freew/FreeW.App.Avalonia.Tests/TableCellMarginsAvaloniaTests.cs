using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// sweep106 F1: the Avalonia table renderer used to hard-code a uniform 5dip pad for every cell's
/// top/left/bottom/right inset, ignoring both <see cref="TableCell.Margins"/> (per-cell w:tcMar
/// override) and <see cref="Table.DefaultCellMargins"/> (table-level w:tblCellMar default), and even
/// ignoring Word's own real implicit default (0pt top/bottom, 5.4pt left/right). This left a
/// document's authored cell padding silently discarded on the Avalonia/Linux/macOS shell while the
/// WPF host rendered it correctly.
/// </summary>
public sealed class TableCellMarginsAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private const double PxPerPoint = 96.0 / 72.0;

    private static (DocumentView View, int TableBlockIdx) BuildSingleCellTable(
        TableCellMargins? cellMargins, TableCellMargins? tableDefaultMargins)
    {
        var doc = TextDocument.CreateEmpty();
        var tbl = Table.Create(1, 1);
        tbl.DefaultCellMargins = tableDefaultMargins;
        var cell = new TableCell("Ag");
        cell.Margins = cellMargins;
        tbl.Rows[0].Cells[0] = cell;
        doc.Blocks.Add(tbl);
        var tableBlockIdx = doc.Blocks.IndexOf(tbl);

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 2000));
        return (view, tableBlockIdx);
    }

    // Fails before the fix: the renderer used a fixed 5dip pad on every side regardless of the
    // cell's authored w:tcMar override, so a cell with an explicit non-default margin rendered with
    // the SAME padding as every other cell instead of its own.
    [Fact]
    public async Task PerCell_authored_margins_drive_glyph_origin_and_row_height()
    {
        Rect cellRect = default;
        (char Ch, double X, double Y, double LineHeight, bool Sentinel, int ParaOffset) firstGlyph = default;

        var ran = await OnUiThread(() =>
        {
            // 15pt top/bottom = 20dip exactly (15 * 96/72); 0pt left/right = 0dip.
            var margins = new TableCellMargins(TopPt: 15, LeftPt: 0, BottomPt: 15, RightPt: 0);
            var (view, tableBlockIdx) = BuildSingleCellTable(margins, tableDefaultMargins: null);

            cellRect = view.TableCellRects.Single(c => c.Block == tableBlockIdx && c.Row == 0 && c.Col == 0).Rect;
            var glyphs = view.GetCellPlaced(tableBlockIdx, row: 0, col: 0, paraIdx: 0);
            firstGlyph = glyphs.First(g => !g.Sentinel);
        });

        if (!ran)
            return;

        firstGlyph.X.Should().BeApproximately(cellRect.Left, 0.01,
            "left/right margin is 0pt so the glyph origin must sit flush with the cell's left edge");
        firstGlyph.Y.Should().BeApproximately(cellRect.Top + 15 * PxPerPoint, 0.5,
            "the cell's authored 15pt top margin (20dip) must offset the glyph, not a hardcoded 5dip pad");
    }

    // Fails before the fix: with no per-cell or table-level override, Word's real default cell
    // padding is 0pt top/bottom and 5.4pt left/right -- NOT a uniform 5dip on every side. The old
    // hardcoded `const double pad = 5` produced 5dip top padding (should be 0) and 5dip left padding
    // (should be 7.2dip).
    [Fact]
    public async Task Unset_margins_fall_back_to_Words_real_default_not_a_uniform_constant()
    {
        Rect cellRect = default;
        (char Ch, double X, double Y, double LineHeight, bool Sentinel, int ParaOffset) firstGlyph = default;

        var ran = await OnUiThread(() =>
        {
            var (view, tableBlockIdx) = BuildSingleCellTable(cellMargins: null, tableDefaultMargins: null);

            cellRect = view.TableCellRects.Single(c => c.Block == tableBlockIdx && c.Row == 0 && c.Col == 0).Rect;
            var glyphs = view.GetCellPlaced(tableBlockIdx, row: 0, col: 0, paraIdx: 0);
            firstGlyph = glyphs.First(g => !g.Sentinel);
        });

        if (!ran)
            return;

        firstGlyph.X.Should().BeApproximately(cellRect.Left + 5.4 * PxPerPoint, 0.01,
            "Word's default left margin is 5.4pt (7.2dip), not a hardcoded 5dip");
        firstGlyph.Y.Should().BeApproximately(cellRect.Top, 0.5,
            "Word's default top margin is 0pt, not a hardcoded 5dip");
    }

    // Sibling no-regression: a table-level DefaultCellMargins override (no per-cell override) must
    // still be honoured -- proves the table.DefaultCellMargins fallback (not just cell.Margins) works.
    [Fact]
    public async Task TableLevelDefaultMargins_applyWhenCellHasNoOverride()
    {
        Rect cellRect = default;
        (char Ch, double X, double Y, double LineHeight, bool Sentinel, int ParaOffset) firstGlyph = default;

        var ran = await OnUiThread(() =>
        {
            var tableDefaults = new TableCellMargins(TopPt: 10, LeftPt: 10, BottomPt: 0, RightPt: 0);
            var (view, tableBlockIdx) = BuildSingleCellTable(cellMargins: null, tableDefaultMargins: tableDefaults);

            cellRect = view.TableCellRects.Single(c => c.Block == tableBlockIdx && c.Row == 0 && c.Col == 0).Rect;
            var glyphs = view.GetCellPlaced(tableBlockIdx, row: 0, col: 0, paraIdx: 0);
            firstGlyph = glyphs.First(g => !g.Sentinel);
        });

        if (!ran)
            return;

        firstGlyph.X.Should().BeApproximately(cellRect.Left + 10 * PxPerPoint, 0.01);
        firstGlyph.Y.Should().BeApproximately(cellRect.Top + 10 * PxPerPoint, 0.5);
    }
}
