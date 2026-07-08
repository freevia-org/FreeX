using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round-13 fix-bucket S12 regression test.
///
/// R13-external-links-2: recalculating a QUOTED external-workbook reference (e.g.
/// <c>='[Data File.xlsx]Sheet1'!A1</c>) parses successfully (unlike the unquoted
/// <c>=[1]Sheet1!A1</c> form, which always throws <see cref="FormulaParseException"/> and is
/// already protected by RecalcEngine's cached-value-preservation guard). When the external link's
/// <c>sheetDataSet</c> cache has no entry for the referenced cell (a broken/incomplete producer, or
/// a cell simply never referenced at last refresh), <see cref="FormulaEvaluator"/> used to fall back
/// to <see cref="BlankValue"/>, so a recalc/"Calculate Now" silently overwrote the cell's correct
/// value (loaded from the worksheet's own cached &lt;f&gt;/&lt;v&gt; pair) with blank/0. The fix
/// routes the "no cached value" case through the same preserve-and-keep-last-value guard the
/// unquoted form already relies on (see <see cref="ExternalWorkbookReferenceRecalcTests"/> for the
/// sibling unquoted-reference coverage).
/// </summary>
public sealed class FreeXR13S12Tests
{
    private static RecalcEngine Engine() =>
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

    [Fact]
    public void RecalculateAllFormulas_PreservesCachedValue_ForQuotedExternalReference_WithNoSheetDataSetCache()
    {
        // Arrange: a workbook loaded from disk with A1 = '[Data File.xlsx]Sheet1'!A1, whose
        // last-Excel-computed cached value (from the worksheet's own <f>/<v>) was 100 — but whose
        // externalLink part's sheetDataSet cache is empty (some producers write only sheetNames, no
        // sheetDataSet — see XlsxNonChartSchemaValidationTests.ExternalLinks.cs's own fixture).
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1); // A1

        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Data File.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1");
        // Deliberately no CachedSheetData entries — simulates a missing/incomplete sheetDataSet.
        workbook.ExternalLinks.Add(link);

        sheet.SetCell(addr, new Cell
        {
            FormulaText = "'[Data File.xlsx]Sheet1'!A1",
            Value = new NumberValue(100),
        });

        // Act: this is exactly what the "Calculate Now" QAT command invokes.
        var report = Engine().RecalculateAllFormulas(workbook);

        // Assert: the loaded value must survive untouched — Excel never blanks out an external
        // reference's last-known result just because a recalc runs while the link source is
        // unavailable/uncached.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(100),
            "recalculating a quoted external reference with no sheetDataSet cache entry must " +
            "preserve the previously loaded value instead of overwriting it with blank");
        report.Errors.Should().NotContain(e => e.Cell == addr);
    }

    [Fact]
    public void GetCellValue_QuotedExternalReference_WithCachedSheetDataSetEntry_StillReturnsCachedValue()
    {
        // Guard against over-broadening the fix: when the sheetDataSet DOES cache the referenced
        // cell, the formula must still use that cached value (not fall into the preserve/throw path).
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);

        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Data File.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1");
        var cachedSheet = new ExternalCachedSheetModel { SheetId = 0 };
        cachedSheet.Values[(1u, 1u)] = new NumberValue(55);
        link.CachedSheetData.Add(cachedSheet);
        workbook.ExternalLinks.Add(link);

        sheet.SetCell(addr, new Cell
        {
            FormulaText = "'[Data File.xlsx]Sheet1'!A1",
            Value = new NumberValue(1), // stale, must be replaced by the cached value below
        });

        Engine().RecalculateAllFormulas(workbook);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(55));
    }
}
