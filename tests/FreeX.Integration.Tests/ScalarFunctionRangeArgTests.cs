using ClosedXML.Excel;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Integration.Tests;

// A legacy (plain <f>) scalar function applied to a range argument — e.g. =ABS(K1:N1), =ACOS(K8:N8) — uses
// Excel implicit intersection: the range resolves to the cell sharing the formula's row/column, then the
// scalar function applies. FreeX broadcasts the function over the range (returning an array) and then the
// Implicit array-mode intersects that array. The broadcast result must keep the source range's coordinates
// or the intersection looks off-axis and returns #VALUE!. Mirrors the FormulaEvalTestData fidelity finding
// where ABS/ACOS/ACOSH/ASIN/ASINH over ranges all returned #VALUE!.
public class ScalarFunctionRangeArgTests
{
    private static MemoryStream BuildXlsxWithPlainFormula(string cell, string formula)
    {
        var ms = new MemoryStream();
        using (var xl = new XLWorkbook())
        {
            var ws = xl.AddWorksheet("S");
            ws.Cell(1, 11).Value = 10; ws.Cell(1, 12).Value = 20; // K1, L1
            ws.Cell(1, 13).Value = 30; ws.Cell(1, 14).Value = 40; // M1, N1
            ws.Cell(cell).FormulaA1 = formula;                    // plain <f>
            xl.SaveAs(ms);
        }
        ms.Position = 0;
        return ms;
    }

    [Theory]
    [InlineData("N3", "ABS(K1:N1)", 40)] // formula col N(14) intersects K1:N1 -> N1 = 40
    [InlineData("M3", "ABS(K1:N1)", 30)] // formula col M(13) -> M1 = 30
    [InlineData("K3", "ABS(K1:N1)", 10)] // formula col K(11) -> K1 = 10
    public void LegacyScalarFunctionOverRange_IntersectsToFormulaColumn(string cell, string formula, double expected)
    {
        using var ms = BuildXlsxWithPlainFormula(cell, formula);
        var wb = new XlsxFileAdapter().Load(ms);
        var sheet = wb.GetSheetAt(0);

        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);

        var addr = ParseA1(cell);
        sheet.GetCell(addr.row, addr.col)!.Value.Should().Be(new NumberValue(expected));
    }

    private static (uint row, uint col) ParseA1(string a1)
    {
        int i = 0;
        uint col = 0;
        while (i < a1.Length && char.IsLetter(a1[i])) { col = col * 26 + (uint)(char.ToUpperInvariant(a1[i]) - 'A' + 1); i++; }
        return (uint.Parse(a1[i..]), col);
    }
}
