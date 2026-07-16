using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-43 finding R43-io-defined-name-evaluation-3-1: a sheet-qualified reference to a defined
/// name (e.g. <c>=SUM(Sheet2!TaxRate)</c>) — the syntax real Excel always writes for a name used
/// from a sheet other than its own — used to always throw a <see cref="FormulaParseException"/>
/// inside <see cref="Parser"/>'s <c>ParseSheetQualifiedReference</c>, which
/// <see cref="FormulaEvaluator"/>'s outer catch converted into a bare <c>#VALUE!</c> for every
/// formula using it. The token following the sheet qualifier's '!' lexes as
/// <c>TokenType.NamedRange</c> (not <c>CellRef</c>) whenever it isn't itself a valid cell address,
/// and the parser previously only accepted a following cell reference. Fixed by recognizing that
/// token shape and returning a plain <see cref="NamedRangeNode"/> (dropping the now-redundant
/// sheet qualifier), which resolves correctly for a workbook-global name — the ordinary case Excel
/// produces this syntax for.
/// </summary>
public class R43_SheetQualifiedNamedRangeTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void SheetQualifiedGlobalNamedRange_InAggregateFunction_ResolvesCorrectly()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var a1 = new CellAddress(sheet2.Id, 1, 1);
        var a2 = new CellAddress(sheet2.Id, 2, 1);
        var a3 = new CellAddress(sheet2.Id, 3, 1);
        sheet2.SetCell(a1, new NumberValue(10));
        sheet2.SetCell(a2, new NumberValue(20));
        sheet2.SetCell(a3, new NumberValue(30));
        workbook.DefineNamedRange("LocalRange", new GridRange(a1, a3));

        // Real Excel always writes exactly this shape ('Sheet2'!LocalRange, or Sheet2!LocalRange
        // when the sheet name needs no quoting) when a workbook-global name is used from a
        // different sheet than the formula's own — previously this threw and surfaced #VALUE!.
        var result = _evaluator.Evaluate("=SUM(Sheet2!LocalRange)", sheet1, workbook);

        result.Should().Be(new NumberValue(60));
    }

    [Fact]
    public void SheetQualifiedGlobalNamedRange_BareSingleCellReference_ResolvesValue()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var b2 = new CellAddress(sheet2.Id, 2, 2);
        sheet2.SetCell(b2, new NumberValue(0.5));
        workbook.DefineNamedRange("Rate", new GridRange(b2, b2));

        var result = _evaluator.Evaluate("=Sheet2!Rate*2", sheet1, workbook);

        result.Should().BeOfType<RangeValue>()
            .Subject.Cells[0, 0].Should().Be(new NumberValue(1));
    }

    [Fact]
    public void SheetQualifiedGlobalNamedRange_QuotedSheetNameWithSpace_ResolvesValue()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("My Sheet");
        var a1 = new CellAddress(sheet2.Id, 1, 1);
        sheet2.SetCell(a1, new NumberValue(7));
        workbook.DefineNamedRange("Value", new GridRange(a1, a1));

        var result = _evaluator.Evaluate("='My Sheet'!Value", sheet1, workbook);

        result.Should().BeOfType<RangeValue>()
            .Subject.Cells[0, 0].Should().Be(new NumberValue(7));
    }

    // ── Sibling no-regression cases ─────────────────────────────────────────

    [Fact]
    public void SheetQualifiedCellReference_StillWorksAfterNamedRangeFix()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(42));

        var result = _evaluator.Evaluate("=Sheet2!A1", sheet1, workbook);

        result.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void SheetQualifiedCellRange_StillWorksAfterNamedRangeFix()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(2));

        var result = _evaluator.Evaluate("=SUM(Sheet2!A1:A2)", sheet1, workbook);

        result.Should().Be(new NumberValue(3));
    }

    [Fact]
    public void UnqualifiedNamedRange_StillWorksAfterNamedRangeFix()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(9));
        workbook.DefineNamedRange("Solo", new GridRange(a1, a1));

        var result = _evaluator.Evaluate("=Solo*3", sheet, workbook);

        result.Should().BeOfType<RangeValue>()
            .Subject.Cells[0, 0].Should().Be(new NumberValue(27));
    }

    [Fact]
    public void SheetQualifiedUndefinedName_ReturnsNameError()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        var result = _evaluator.Evaluate("=Sheet2!NotDefinedAnywhere", sheet1, workbook);

        result.Should().Be(ErrorValue.Name);
    }
}
