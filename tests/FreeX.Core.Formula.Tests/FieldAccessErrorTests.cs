using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Tests for R35-deferred-field-error-1: Excel's linked-data-type field-access syntax
/// (e.g. <c>=A1.Price</c>) must surface #FIELD! rather than being misrouted through
/// named-range lookup to #NAME?, and ERROR.TYPE must report the correct Excel code for it.
/// </summary>
public class FieldAccessErrorTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void CellRefDotSuffix_ReturnsFieldError_NotNameError()
    {
        // A1 is a plain number, not a linked data type — Excel still returns #FIELD! for the
        // dotted field-access shape rather than #NAME?, since FreeX has no linked-data-type model.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));

        var result = _evaluator.Evaluate("=A1.Price", sheet, workbook);

        result.Should().Be(ErrorValue.Field);
    }

    [Fact]
    public void CellRefDotSuffix_LowercaseAndAbsolute_StillReturnsFieldError()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var result = _evaluator.Evaluate("=$A$1.price", sheet, workbook);

        result.Should().Be(ErrorValue.Field);
    }

    [Fact]
    public void ErrorType_OfFieldError_ReturnsExcelCode13()
    {
        // ERROR.TYPE(#FIELD!) — Excel's documented code for the #FIELD! error is 13.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=ERROR.TYPE(#FIELD!)", sheet, workbook);

        result.Should().Be(new NumberValue(13));
    }

    // ── Sibling no-regression cases ────────────────────────────────────────────

    [Fact]
    public void PlainDefinedName_WithNoDot_StillResolvesNormally()
    {
        // A normal named range (no dot in its name) must be entirely unaffected by the
        // cell-ref-dot-suffix detection.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(b1, new NumberValue(7));
        workbook.NamedRanges["Rate"] = new GridRange(b1, b1);

        // Route through SUM so this assertion is independent of whether a bare named-range
        // reference is returned as a raw scalar or wrapped in a single-cell RangeValue.
        var result = _evaluator.Evaluate("=SUM(Rate)", sheet, workbook);

        result.Should().Be(new NumberValue(7));
    }

    [Fact]
    public void UndefinedPlainName_WithNoDot_StillReturnsNameError()
    {
        // A genuinely undefined bare name (no dot, not a cell-ref shape) must keep returning
        // #NAME? — only the cell-ref-dot-suffix shape is redirected to #FIELD!.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=TotallyUndefinedName", sheet, workbook);

        result.Should().Be(ErrorValue.Name);
    }
}
