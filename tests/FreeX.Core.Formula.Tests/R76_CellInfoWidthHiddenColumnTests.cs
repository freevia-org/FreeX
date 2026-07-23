using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R76-formula-cell-info-4-1: CELL("width",A1) returned the STORED column width for a hidden
/// column, but Excel returns 0 for a hidden or outline-collapsed column (the DISPLAYED width, which
/// is 0 whether it's hidden by a direct HiddenCols entry or by an outline-group collapse). Fixed by
/// checking Sheet.IsColEffectivelyHidden(col) (which ORs both HiddenCols and GroupHiddenCols) before
/// falling back to the stored/default width.
/// </summary>
public sealed class R76_CellInfoWidthHiddenColumnTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook wb, Sheet sheet) MakeWb()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        return (wb, sheet);
    }

    [Fact]
    public void CellWidth_DirectlyHiddenColumn_ReturnsZero()
    {
        var (wb, sheet) = MakeWb();
        sheet.ColumnWidths[1] = 20;
        sheet.HiddenCols.Add(1);

        _eval.Evaluate("=CELL(\"width\",A1)", sheet, wb).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void CellWidth_OutlineCollapsedColumn_ReturnsZero()
    {
        var (wb, sheet) = MakeWb();
        sheet.ColumnWidths[1] = 20;
        sheet.GroupHiddenCols.Add(1);

        _eval.Evaluate("=CELL(\"width\",A1)", sheet, wb).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void CellWidth_VisibleColumn_StillReturnsRoundedStoredWidth()
    {
        var (wb, sheet) = MakeWb();
        sheet.ColumnWidths[1] = 12.6;

        _eval.Evaluate("=CELL(\"width\",A1)", sheet, wb).Should().Be(new NumberValue(13));
    }

    [Fact]
    public void CellWidth_NoOverrideAndNoHidden_StillReturnsRoundedDefaultWidth()
    {
        // Sibling no-regression: a visible column with no explicit ColumnWidths entry still falls
        // back to Sheet.DefaultColumnWidth (8.43, rounding to 8) exactly as before this change --
        // the new IsColEffectivelyHidden check must not affect an ordinary visible column.
        var (wb, sheet) = MakeWb();

        _eval.Evaluate("=CELL(\"width\",A1)", sheet, wb).Should().Be(new NumberValue(8));
    }
}
