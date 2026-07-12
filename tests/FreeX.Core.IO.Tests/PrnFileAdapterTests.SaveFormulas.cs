using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using static FreeX.Core.IO.Tests.TextFileAdapterTestHelper;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R32-io-print-export-fidelity-deep-1: PRN (Formatted Text, space delimited) has no formula
/// syntax, so real Excel's PRN Save-As always writes a formula cell's calculated
/// <see cref="Cell.Value"/>, never its formula source text (e.g. "=SUM(B1:B2)") — matching
/// <see cref="DelimitedTextWorkbookWriter"/> (CSV)'s documented behaviour.
/// </summary>
public sealed class PrnFileAdapterTestsSaveFormulas
{
    [Fact]
    public void Save_WritesFormulaCellAsComputedValue_NotFormulaText()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(22));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 1), "SUM(A1:B1)");
        sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.Value = new NumberValue(42);

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        text.Should().NotContain("SUM", "the formula source text must not be exported");
        text.Should().NotContain("=");
        text.Should().Contain("42");
    }

    [Fact]
    public void Save_WritesComputedValueEvenWhenFormulaTextHasLeadingEquals()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            FormulaText = "=A1*2",
            Value = new NumberValue(4)
        });

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        text.Trim().Should().Be("4");
    }

    // Sibling case that must keep working: a plain (non-formula) value cell still exports its
    // value normally.
    [Fact]
    public void Save_PlainValueCellStillExportsNormally()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(99));

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        text.Trim().Should().Be("99");
    }
}
