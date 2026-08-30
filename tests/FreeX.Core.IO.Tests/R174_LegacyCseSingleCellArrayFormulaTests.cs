using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R174-formula-array-cse-1 (MED): a genuine single-cell (1x1-declared) legacy Ctrl+Shift+Enter
/// array formula loaded from a THIRD-PARTY .xlsx (i.e. one FreeX itself never saved) must stay
/// confined to its own anchor cell on recalculation -- never spilling into, or colliding with,
/// neighboring cells -- exactly like real Excel's classic single-cell array-formula rule.
/// <para>
/// Pre-fix, XlsxFileAdapter.cs only set <see cref="Cell.LegacyArrayRows"/>/<see
/// cref="Cell.LegacyArrayCols"/> (the field RecalcEngine uses to confine a CSE array to its
/// declared extent) when the declared &lt;f t="array" ref="..."&gt; range spanned MORE than one
/// cell. A declared 1x1 range was left with ArrayMode at its Dynamic default and no confinement at
/// all, so RecalcEngine evaluated it via the ordinary free-spilling dynamic-array path -- exactly
/// as if the user had typed a modern 365 dynamic-array formula.
/// </para>
/// <para>
/// The test files here are built directly with ClosedXML (never through
/// <see cref="XlsxFileAdapter"/>'s own save path) so they reproduce a genuine third-party-authored
/// single-cell CSE array formula -- the same shape XlsxFileAdapter.Save.cs ALSO happens to write
/// for its own currently-1x1 dynamic-array formulas (see R174_DynamicArrayCollapsedRoundTripTests),
/// which is exactly the ambiguity this fix resolves: a genuine legacy CSE 1x1 formula built this
/// way carries no dynamic-array `cm` metadata marker, so the loader must treat it as confined.
/// </para>
/// </summary>
public sealed class R174_LegacyCseSingleCellArrayFormulaTests
{
    private static MemoryStream BuildThirdPartySingleCellCseWorkbook(bool prePopulateC2)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Data");
        ws.Cell("A1").Value = 10;
        ws.Cell("A2").Value = 20;
        ws.Cell("A3").Value = 30;
        ws.Cell("A4").Value = 40;
        ws.Cell("A5").Value = 50;

        // A genuine Ctrl+Shift+Enter single-cell array formula: the declared ref is exactly one
        // cell (C1), but the formula body naturally evaluates to a 5-row range. Real Excel shows
        // only the top-left element (10) in C1 and never touches any other cell.
        ws.Range("C1:C1").FormulaArrayA1 = "A1:A5";

        if (prePopulateC2)
            ws.Cell("C2").Value = 999;

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void OneByOneLegacyCseArrayFormula_LoadedFromThirdPartyFile_StaysConfined_NoNeighborFill()
    {
        using var stream = BuildThirdPartySingleCellCseWorkbook(prePopulateC2: false);

        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet = workbook.GetSheetAt(0);

        var anchor = sheet.GetCell(1, 3);
        anchor.Should().NotBeNull();
        anchor!.FormulaText.Should().Be("A1:A5");
        anchor.LegacyArrayRows.Should().Be(1u,
            "a genuine single-cell declared CSE array formula must be confined to its declared " +
            "1x1 extent, not left free to spill like a modern dynamic-array formula");
        anchor.LegacyArrayCols.Should().Be(1u);

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        sheet.GetValue(1, 3).Should().Be(new NumberValue(10),
            "the anchor must show only the top-left element of the array result");
        sheet.GetValue(2, 3).Should().Be(BlankValue.Instance,
            "real Excel never fills neighboring cells for a single-cell CSE array formula -- " +
            "pre-fix, FreeX copied the whole A1:A5 source range down into C1:C5");
        sheet.GetValue(3, 3).Should().Be(BlankValue.Instance);
        sheet.GetValue(4, 3).Should().Be(BlankValue.Instance);
        sheet.GetValue(5, 3).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void OneByOneLegacyCseArrayFormula_LoadedFromThirdPartyFile_WithNeighborOccupied_NoFalseSpillError()
    {
        using var stream = BuildThirdPartySingleCellCseWorkbook(prePopulateC2: true);

        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet = workbook.GetSheetAt(0);

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        sheet.GetValue(1, 3).Should().Be(new NumberValue(10),
            "a single-cell CSE array formula must resolve to its top-left element even when a " +
            "neighboring cell is occupied -- pre-fix, FreeX misdetected this as a blocked modern " +
            "dynamic-array spill and surfaced a false #SPILL! error");
        sheet.GetValue(2, 3).Should().Be(new NumberValue(999),
            "the pre-existing, unrelated content of the neighboring cell must be left untouched");
    }
}
