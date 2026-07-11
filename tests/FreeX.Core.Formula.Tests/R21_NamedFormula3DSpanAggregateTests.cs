using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-21 finding R21-crosssheet-3d-refs-1: a defined name whose RefersTo is a bare 3-D
/// sheet-span (e.g. Sheet1:Sheet3!A1) must expand across the spanned sheets when used inside an
/// aggregate function (SUM, AVERAGE, ...), exactly like writing the span literally as the
/// argument. Previously this fell through to the generic named-formula evaluation path, which has
/// no aggregate-argument context and always surfaced #VALUE!.
/// </summary>
public class R21_NamedFormula3DSpanAggregateTests
{
    private readonly FormulaEvaluator _evaluator = new();

    private static Workbook ThreeSheetWorkbook(out Sheet sheet1, out Sheet sheet2, out Sheet sheet3)
    {
        var workbook = new Workbook("Test");
        sheet1 = workbook.AddSheet("Sheet1");
        sheet2 = workbook.AddSheet("Sheet2");
        sheet3 = workbook.AddSheet("Sheet3");
        return workbook;
    }

    [Fact]
    public void Sum_NamedFormulaBareSheetSpan_SumsAllThreeSheets()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out var sheet2, out var sheet3);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));
        workbook.NamedFormulas["All3D"] = "Sheet1:Sheet3!A1";

        var result = _evaluator.Evaluate("=SUM(All3D)", sheet1, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Average_NamedFormulaBareSheetSpan_AveragesAcrossSheets()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out var sheet2, out var sheet3);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(10));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(20));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(30));
        workbook.NamedFormulas["All3D"] = "Sheet1:Sheet3!A1";

        var result = _evaluator.Evaluate("=AVERAGE(All3D)", sheet1, workbook);

        result.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Sum_NamedFormulaBareSheetSpan_RangeForm_SumsAllCellsAcrossSheets()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 2), new NumberValue(2));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(3));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 2), new NumberValue(4));
        workbook.NamedFormulas["All3D"] = "Sheet1:Sheet2!A1:B1";

        var result = _evaluator.Evaluate("=SUM(All3D)", sheet1, workbook);

        result.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Sum_NamedFormulaBareSheetSpan_MissingSheet_ReturnsRefError()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["All3D"] = "Sheet1:NoSuchSheet!A1";

        var result = _evaluator.Evaluate("=SUM(All3D)", sheet1, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void NamedFormulaBareSheetSpan_NonAggregateContext_StillReturnsValueError()
    {
        // Outside an aggregate function, a 3-D span (whether literal or via a named formula)
        // remains a #VALUE! error — only the aggregate-argument path expands it.
        var workbook = ThreeSheetWorkbook(out var sheet1, out var sheet2, out var sheet3);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        workbook.NamedFormulas["All3D"] = "Sheet1:Sheet3!A1";

        var result = _evaluator.Evaluate("=All3D", sheet1, workbook);

        result.Should().Be(ErrorValue.Value);
    }
}
