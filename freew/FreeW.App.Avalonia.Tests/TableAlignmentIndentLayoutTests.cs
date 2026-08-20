using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 144 remediation (freew-avalonia-table-ignores-alignment-and-indent): Avalonia's
/// <c>DocumentView.LayoutTablePaged</c> built every row's X origin purely from
/// <c>ColumnLeftFor(rowContentY)</c> (the column-band left edge), never consulting
/// <see cref="Table.Alignment"/> (<c>w:jc</c>) or <see cref="Table.IndentFromLeftPt"/> (<c>w:tblInd</c>),
/// so a centred/right-aligned/indented table always rendered flush against the left content margin --
/// unlike the WPF twin's <c>ResolveTableBlockMargin</c>, which switches on both. These tests exercise the
/// real, unmodified layout path (<c>DocumentView.Measure</c> -&gt; <c>LayoutTablePaged</c>) and read the
/// laid-out cell rectangle back off the production <c>_cellHits</c> list -- no reflection into the fix
/// itself, only into the pre-existing test-visible layout output.
/// </summary>
public sealed class TableAlignmentIndentLayoutTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    /// <summary>
    /// A single-cell, single-column table with an explicit fixed column width (narrower than the
    /// default-page content width), so <see cref="TableColumnLayoutPlanner.AllocateColumnWidths"/>
    /// returns that width unchanged and there is always slack to distribute.
    /// </summary>
    private static (DocumentView View, Table Table) MakeSingleCellTable(
        TableAlignment alignment, double? indentFromLeftPt, double columnWidthPt = 200)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0] = new TableCell("X");
        table.ColumnWidthsPt.Add(columnWidthPt);
        table.Alignment = alignment;
        table.IndentFromLeftPt = indentFromLeftPt;
        doc.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(900, 4000));
        return (view, table);
    }

    private static Rect FirstCellRect(DocumentView view)
    {
        var field = typeof(DocumentView).GetField("_cellHits", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("_cellHits");
        var hits = ((System.Collections.IEnumerable)field.GetValue(view)!).Cast<object>().ToList();
        hits.Should().NotBeEmpty("the table must have laid out at least one cell");
        var type = hits[0].GetType();
        return (Rect)type.GetField("Item1")!.GetValue(hits[0])!;
    }

    [Fact]
    public async Task LeftAligned_NoIndent_table_position_is_the_unchanged_baseline()
    {
        double cellX = 0, contentLeft = 0;
        var ran = await OnUiThread(() =>
        {
            var (view, _) = MakeSingleCellTable(TableAlignment.Left, indentFromLeftPt: null);
            cellX = FirstCellRect(view).X;
            contentLeft = view.LayoutColumnBand(0).Left;
        });
        if (!ran) return;

        cellX.Should().BeApproximately(contentLeft, 0.5,
            "sibling/regression guard: the overwhelmingly common Left-aligned, no-indent table must render " +
            "exactly where it always did -- flush with the content margin");
    }

    [Fact]
    public async Task CenterAligned_table_is_shifted_right_by_half_the_available_slack()
    {
        double leftX = 0, centerX = 0, slack = 0;
        var ran = await OnUiThread(() =>
        {
            var (leftView, leftTable) = MakeSingleCellTable(TableAlignment.Left, indentFromLeftPt: null);
            leftX = FirstCellRect(leftView).X;
            var textWidth = leftView.LayoutColumnWidth;
            var tableWidth = TableColumnLayoutPlanner.AllocateColumnWidths(leftTable, 1, textWidth)[0];
            slack = textWidth - tableWidth;

            var (centerView, _) = MakeSingleCellTable(TableAlignment.Center, indentFromLeftPt: null);
            centerX = FirstCellRect(centerView).X;
        });
        if (!ran) return;

        slack.Should().BeGreaterThan(50,
            "the narrow 200pt fixed-width table must leave real slack against the default page content " +
            "width for this test to be meaningful");
        centerX.Should().BeApproximately(leftX + slack / 2, 0.5,
            "w:jc=\"center\" must center the table in the content column, matching WPF's " +
            "ResolveTableBlockMargin Thickness(indent + slack / 2, ...)");
    }

    [Fact]
    public async Task RightAligned_table_is_shifted_right_by_the_full_slack()
    {
        double leftX = 0, rightX = 0, slack = 0;
        var ran = await OnUiThread(() =>
        {
            var (leftView, leftTable) = MakeSingleCellTable(TableAlignment.Left, indentFromLeftPt: null);
            leftX = FirstCellRect(leftView).X;
            var textWidth = leftView.LayoutColumnWidth;
            var tableWidth = TableColumnLayoutPlanner.AllocateColumnWidths(leftTable, 1, textWidth)[0];
            slack = textWidth - tableWidth;

            var (rightView, _) = MakeSingleCellTable(TableAlignment.Right, indentFromLeftPt: null);
            rightX = FirstCellRect(rightView).X;
        });
        if (!ran) return;

        rightX.Should().BeApproximately(leftX + slack, 0.5,
            "w:jc=\"right\" must push the table's right edge flush with the content margin, matching " +
            "WPF's ResolveTableBlockMargin Thickness(indent + slack, 0, 0, 0)");
    }

    [Fact]
    public async Task IndentedTable_is_shifted_right_by_the_indent_amount()
    {
        double leftX = 0, indentedX = 0;
        const double indentPt = 36; // 0.5in, a normal w:tblInd value
        var ran = await OnUiThread(() =>
        {
            var (leftView, _) = MakeSingleCellTable(TableAlignment.Left, indentFromLeftPt: null);
            leftX = FirstCellRect(leftView).X;

            var (indentedView, _) = MakeSingleCellTable(TableAlignment.Left, indentFromLeftPt: indentPt);
            indentedX = FirstCellRect(indentedView).X;
        });
        if (!ran) return;

        var expectedIndentDip = PageLayout.PointsToDip(indentPt);
        indentedX.Should().BeApproximately(leftX + expectedIndentDip, 0.5,
            "w:tblInd must shift the table right by the indent amount, matching WPF's " +
            "ResolveTableBlockMargin indent term");
    }
}
