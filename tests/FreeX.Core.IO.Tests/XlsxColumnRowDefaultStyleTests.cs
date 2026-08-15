using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R136-io-worksheet-props-col-row-default-style: a real-world XLSX where the author formatted an
/// ENTIRE COLUMN (e.g. Currency) or an entire row (a "banner" row style) emits a
/// <c>&lt;col min max style="..."&gt;</c> or a row <c>s="..." customFormat="1"</c> with no per-cell
/// <c>s=</c> on the still-empty cells that live in that column/row. Before this fix,
/// <see cref="XlsxWorksheetRowColumnLayoutReader"/> never read either attribute into the
/// <see cref="Sheet"/> model at all (see the R75-io-worksheet-props-4-2/4-3 deferral comments this
/// change replaces), so an empty formatted cell rendered/typed as General instead of the column's
/// or row's real format, and no column/row fill ever appeared.
///
/// These tests build the source XLSX with raw ClosedXML (so the file on disk looks exactly like a
/// real Excel-authored whole-column/whole-row format, not something FreeX's own Sheet model
/// synthesizes), load it through the real production loader (<see cref="XlsxFileAdapter"/>), and
/// assert on <see cref="Sheet.GetStyleOnly"/> -- the single fallback chokepoint that
/// CellEntryParser.GetTargetStyleId (what a newly-typed cell's starting style is seeded from),
/// SpreadsheetDisplayFormatter, WorkbookSession's style resolution, and ~30 other call sites across
/// both shells all route through for an as-yet-unpopulated cell.
/// </summary>
public sealed class XlsxColumnRowDefaultStyleTests
{
    // Deliberately unusual (non-builtin) custom number formats so a builtin-format-id normalization
    // quirk in ClosedXML/Excel can't accidentally make the assertions pass for the wrong reason.
    private const string CurrencyFormat = "\"$\"#,##0.0000";
    private const string PercentFormat = "0.00000%";

    private static Workbook LoadThroughClosedXmlRoundTrip(Action<IXLWorksheet> configureSource)
    {
        using var stream = new MemoryStream();
        using (var xlWorkbook = new XLWorkbook())
        {
            var xlSheet = xlWorkbook.AddWorksheet("Sheet1");
            configureSource(xlSheet);
            xlWorkbook.SaveAs(stream);
        }

        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream);
    }

    // ------------------------------------------------------------------
    // Column default style applies to a still-empty cell (the core finding).
    // ------------------------------------------------------------------

    [Fact]
    public void ColumnDefaultStyle_AppliesToStillEmptyCell()
    {
        var workbook = LoadThroughClosedXmlRoundTrip(xlSheet =>
        {
            // Whole-column Currency format, exactly what Excel writes when a user selects column D
            // and applies Currency formatting -- no cell in the column has ever been touched.
            xlSheet.Column(4).Style.NumberFormat.Format = CurrencyFormat;
        });
        var sheet = workbook.GetSheetAt(0);

        // The cell is genuinely empty: no <c> element for it at all.
        sheet.GetCell(10, 4).Should().BeNull("the cell was never touched -- only the column carries a format");

        var resolvedStyleId = sheet.GetCell(10, 4)?.StyleId ?? sheet.GetStyleOnly(10, 4) ?? StyleId.Default;
        var resolvedStyle = workbook.GetStyle(resolvedStyleId);

        resolvedStyle.NumberFormat.Should().Be(CurrencyFormat,
            "an empty cell in a Currency-formatted column must resolve to Currency, not General");
    }

    [Fact]
    public void ColumnDefaultStyle_SeedsNewlyTypedCellStyle()
    {
        // Mirrors CellEntryParser.GetTargetStyleId's exact resolution chain: the style a NEWLY typed
        // value inherits is whatever GetCell(...)?.StyleId ?? GetStyleOnly(...) resolves to for the
        // address BEFORE the cell exists. This is the literal "typing 1234 into such a cell shows
        // 1234 where Excel shows $1,234.00" scenario from the finding.
        var workbook = LoadThroughClosedXmlRoundTrip(xlSheet =>
        {
            xlSheet.Column(4).Style.NumberFormat.Format = CurrencyFormat;
        });
        var sheet = workbook.GetSheetAt(0);

        var targetStyleId = sheet.GetCell(10, 4)?.StyleId ?? sheet.GetStyleOnly(10, 4) ?? StyleId.Default;
        var cell = Cell.FromValue(new NumberValue(1234));
        cell.StyleId = targetStyleId;
        sheet.SetCell(new CellAddress(sheet.Id, 10, 4), cell);

        workbook.GetStyle(sheet.GetCell(10, 4)!.StyleId).NumberFormat.Should().Be(CurrencyFormat,
            "a value typed into a previously-empty, column-formatted cell must be seeded with the column's format");
    }

    // ------------------------------------------------------------------
    // Row default style applies to a still-empty cell, and wins over a column default at their
    // intersection -- Excel's cell > row > column precedence (verified against real ClosedXML/Excel
    // behavior: setting a row style after a column style is what makes the row win at the
    // intersection, matching genuine Excel semantics).
    // ------------------------------------------------------------------

    [Fact]
    public void RowDefaultStyle_AppliesToStillEmptyCell_AndTakesPrecedenceOverColumnDefault()
    {
        var workbook = LoadThroughClosedXmlRoundTrip(xlSheet =>
        {
            xlSheet.Column(4).Style.NumberFormat.Format = CurrencyFormat;
            xlSheet.Row(5).Style.NumberFormat.Format = PercentFormat;
        });
        var sheet = workbook.GetSheetAt(0);

        // A cell in row 5 but a DIFFERENT column: only the row default applies.
        sheet.GetCell(5, 2).Should().BeNull();
        var rowOnlyStyleId = sheet.GetCell(5, 2)?.StyleId ?? sheet.GetStyleOnly(5, 2) ?? StyleId.Default;
        workbook.GetStyle(rowOnlyStyleId).NumberFormat.Should().Be(PercentFormat);

        // The D5 intersection: both a column default (Currency) and a row default (Percent) apply.
        // Excel's precedence says the ROW wins.
        sheet.GetCell(5, 4).Should().BeNull();
        var intersectionStyleId = sheet.GetCell(5, 4)?.StyleId ?? sheet.GetStyleOnly(5, 4) ?? StyleId.Default;
        workbook.GetStyle(intersectionStyleId).NumberFormat.Should().Be(PercentFormat,
            "Excel resolves cell > row > column, so the row's format must win over the column's at their intersection");
    }

    // ------------------------------------------------------------------
    // Sibling no-regression: an unstyled column/row must not spuriously acquire a default style,
    // and a cell's own explicit style-only entry must still be resolvable independently.
    // ------------------------------------------------------------------

    [Fact]
    public void UnstyledColumn_EmptyCell_StaysDefault_NoRegression()
    {
        var workbook = LoadThroughClosedXmlRoundTrip(xlSheet =>
        {
            // A column with a real custom width but no style at all (the ordinary, overwhelmingly
            // common case) must not pick up a spurious default style.
            xlSheet.Column(4).Width = 25;
        });
        var sheet = workbook.GetSheetAt(0);

        sheet.ColumnStyles.Should().NotContainKey(4u,
            "a column that never carried a style must not synthesize one");

        var resolvedStyleId = sheet.GetCell(10, 4)?.StyleId ?? sheet.GetStyleOnly(10, 4) ?? StyleId.Default;
        workbook.GetStyle(resolvedStyleId).NumberFormat.Should().Be("General");
    }

    [Fact]
    public void ColumnDefaultStyle_DoesNotOverridePreExistingStyleOnlyCell_NoRegression()
    {
        // A cell that already carries its OWN per-cell style-only entry (the pre-existing mechanism)
        // must keep its own style even though the column also carries a default -- cell-level always
        // wins over column-level.
        var workbook = LoadThroughClosedXmlRoundTrip(xlSheet =>
        {
            xlSheet.Column(4).Style.NumberFormat.Format = CurrencyFormat;
            // An empty cell with its OWN explicit style (a real "style-only" cell): bold, no number
            // format override, distinct from the column's Currency format.
            var styledEmptyCell = xlSheet.Cell(20, 4);
            styledEmptyCell.Style.Font.Bold = true;
        });
        var sheet = workbook.GetSheetAt(0);

        sheet.GetCell(20, 4).Should().BeNull("the cell carries a style but no value -- still a style-only cell");
        var cellLevelStyleId = sheet.GetStyleOnly(20, 4);
        cellLevelStyleId.Should().NotBeNull();
        workbook.GetStyle(cellLevelStyleId!.Value).Bold.Should().BeTrue(
            "the cell's own style-only entry must win over the column default");
    }

    // ------------------------------------------------------------------
    // Round-trip: the resolved default style must survive a FreeX full save + reload, proving the
    // write side (XlsxFileAdapter.Save.cs) actually re-emits the column/row default rather than
    // silently dropping it on the next save.
    // ------------------------------------------------------------------

    [Fact]
    public void ColumnAndRowDefaultStyle_RoundTripThroughFreeXFullSave()
    {
        var workbook = LoadThroughClosedXmlRoundTrip(xlSheet =>
        {
            xlSheet.Column(4).Style.NumberFormat.Format = CurrencyFormat;
            xlSheet.Row(5).Style.NumberFormat.Format = PercentFormat;
        });

        var adapter = new XlsxFileAdapter();
        using var resaved = new MemoryStream();
        adapter.Save(workbook, resaved);
        resaved.Position = 0;
        var reloaded = adapter.Load(resaved);
        var reloadedSheet = reloaded.GetSheetAt(0);

        var columnOnlyStyleId = reloadedSheet.GetCell(10, 4)?.StyleId ?? reloadedSheet.GetStyleOnly(10, 4) ?? StyleId.Default;
        reloaded.GetStyle(columnOnlyStyleId).NumberFormat.Should().Be(CurrencyFormat,
            "the column's default Currency format must survive a FreeX full save + reload");

        var rowIntersectionStyleId = reloadedSheet.GetCell(5, 4)?.StyleId ?? reloadedSheet.GetStyleOnly(5, 4) ?? StyleId.Default;
        reloaded.GetStyle(rowIntersectionStyleId).NumberFormat.Should().Be(PercentFormat,
            "the row's default must still win over the column's after a full save + reload round trip");
    }
}
