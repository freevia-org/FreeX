using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// freew-table-layout F1: AutoFit-to-Contents tables must shrink-wrap their columns to measured
/// cell content on the Avalonia (Linux/macOS) shell, exactly like the WPF host already does via
/// ResolveContentAutoFitColumnWidths (FreeW.App.Host/Editing/DocumentView.cs). Before the fix,
/// DocumentView.ComputeColumnWidths ignored table.AutoFit entirely and always called
/// TableColumnLayoutPlanner.AllocateColumnWidths, which distributes/stretches columns across the
/// full available width -- correct for Fixed/Window tables, wrong for Contents ones.
/// </summary>
public sealed class TableAutoFitContentsColumnWidthTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    /// <summary>Single-column US letter page, 1" margins: contentWidth = (612-144)pt * 96/72 = 624 DIP.</summary>
    private static TextDocument DocSingleCol(Block contentBlock)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 792;
        doc.Page.MarginLeftPt = 72;
        doc.Page.MarginRightPt = 72;
        doc.Page.MarginTopPt = 72;
        doc.Page.MarginBottomPt = 72;
        doc.Page.ColumnCount = 1;
        doc.Page.ColumnSpacingPt = 0;
        doc.Blocks.Add(contentBlock);
        return doc;
    }

    /// <summary>Builds a 2-column, 1-row table with short cell text and no declared column widths.</summary>
    private static Table ShortContentTable(AutoFitMode autoFit)
    {
        var table = Table.Create(1, 2);
        table.AutoFit = autoFit;
        table.Rows[0].Cells[0] = new TableCell("Hi");
        table.Rows[0].Cells[1] = new TableCell("Yo");
        return table;
    }

    [Fact]
    public async Task AutoFitContents_table_shrink_wraps_columns_to_content_not_full_width()
    {
        double contentWidth = -1;
        double tableWidth = -1;
        int rectCount = 0;

        var ran = await OnUiThread(() =>
        {
            var table = ShortContentTable(AutoFitMode.Contents);
            var doc = DocSingleCol(table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            var band0 = view.LayoutColumnBand(0);
            contentWidth = band0.Width;

            var rects = view.TableCellRects.Select(r => r.Rect).ToList();
            rectCount = rects.Count;
            tableWidth = rectCount > 0 ? rects.Max(r => r.Right) - rects.Min(r => r.Left) : -1;
        });

        if (!ran) return;
        rectCount.Should().BeGreaterThan(0, "AutoFit-to-Contents table must still produce cell rects");
        // Two 2-character cells must shrink-wrap well under half the 624-DIP page content width.
        // Before the fix, ComputeColumnWidths ignored AutoFit and stretched to the full contentWidth.
        tableWidth.Should().BeLessThan(contentWidth * 0.5,
            $"AutoFit-to-Contents table (measured width {tableWidth:F1} DIP) must shrink-wrap to its " +
            $"short cell content, not stretch to the full {contentWidth:F1}-DIP content width");
    }

    [Fact]
    public async Task Fixed_table_with_no_declared_widths_still_stretches_to_fill_available_width()
    {
        // Sibling/no-regression case: a Fixed (or Window) table with no declared column widths must
        // keep distributing evenly across the full available width via AllocateColumnWidths, exactly
        // as before the fix -- only AutoFitMode.Contents changes behaviour.
        double contentWidth = -1;
        double tableWidth = -1;
        int rectCount = 0;

        var ran = await OnUiThread(() =>
        {
            var table = ShortContentTable(AutoFitMode.Fixed);
            var doc = DocSingleCol(table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            var band0 = view.LayoutColumnBand(0);
            contentWidth = band0.Width;

            var rects = view.TableCellRects.Select(r => r.Rect).ToList();
            rectCount = rects.Count;
            tableWidth = rectCount > 0 ? rects.Max(r => r.Right) - rects.Min(r => r.Left) : -1;
        });

        if (!ran) return;
        rectCount.Should().BeGreaterThan(0, "Fixed table must still produce cell rects");
        tableWidth.Should().BeGreaterThan(contentWidth * 0.9,
            $"Fixed table with no declared widths (measured width {tableWidth:F1} DIP) must still stretch " +
            $"to fill the {contentWidth:F1}-DIP content width (no regression from the AutoFit-Contents fix)");
    }
}
