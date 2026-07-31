using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R107 fix: <see cref="FormulaEvaluator"/>'s <c>SheetEvalContext.GetCellValue</c>/<c>GetRangeValues</c>
/// treated an UNRESOLVABLE bracketed external-workbook reference (<c>ExternalSheetReferenceResolver
/// .TryResolve</c> returning <see langword="null"/>) as a normal <see cref="ErrorValue.Ref"/> result,
/// instead of throwing <see cref="FormulaParseException"/> like the sibling resolved-but-uncached-cell
/// branch two lines above already does. Because #REF! was a normal (non-throwing) evaluation result,
/// <see cref="RecalcEngine"/>'s external-workbook-reference preservation guard
/// (catch(FormulaParseException) + IsLikelyExternalWorkbookReferenceFormula / CachedAst-not-null) never
/// fired, so <c>cell.Value = result;</c> stored ErrorValue.Ref straight into the cell -- permanently
/// discarding the value Excel actually cached in the worksheet's &lt;f&gt;/&lt;v&gt; pair at load time.
///
/// TryResolve returns null for a bracketed reference in two real-world shapes:
///  (a) a numeric index ([n]) landing on an ExternalLinkModel placeholder that
///      XlsxExternalLinkMetadataReader deliberately creates (with an empty SheetNames list) for a
///      blank/duplicate/unresolvable r:id already broken in the SOURCE workbook, or
///  (b) a sheet name that legitimately resolves the external link's book/index but isn't present in
///      that link's cached SheetNames.
/// Both must behave exactly like the resolved-but-uncached-cell case: preserve the last-known loaded
/// value instead of clobbering it with a freshly-computed #REF!.
/// </summary>
public sealed class R107_UnresolvableExternalReferencePreservesCachedValueTests
{
    private static RecalcEngine Engine() => new(new DependencyGraph(), new FormulaEvaluator());

    /// <summary>Build a cell the way the loader would: formula text set, Value pre-populated from the
    /// file's cached &lt;v&gt;, no CachedAst yet.</summary>
    private static Cell LoadedExternalRefCell(string formulaText, ScalarValue cachedValue) =>
        new() { FormulaText = formulaText, Value = cachedValue };

    [Fact]
    public void RecalculateAllFormulas_PreservesCachedValue_ForNumericIndexIntoBrokenPlaceholderLink()
    {
        // Arrange: workbook.xml had a blank/duplicate/unresolvable r:id for external reference slot 1
        // in the SOURCE workbook -- XlsxExternalLinkMetadataReader preserves that as an
        // ExternalLinkModel placeholder at its correct ordinal with an empty SheetNames list, exactly
        // what TryFindExternalLink's numeric branch returns for [1]. A formula referencing [1]Sheet1!A1
        // therefore can never resolve a sheet index -- TryResolve returns null -- even though the file's
        // cached formula value (99) loaded correctly.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ExternalLinks.Add(new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "",
            TargetMode = "External",
        }); // placeholder: no SheetNames, no cached data

        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, LoadedExternalRefCell("[1]Sheet1!A1", new NumberValue(99)));

        // Act: this is exactly what the "Calculate Now" QAT command invokes.
        var report = Engine().RecalculateAllFormulas(workbook);

        // Assert: Excel keeps showing the last-known cached value for a broken external link until the
        // user explicitly updates links -- it must NOT be clobbered with a freshly-computed #REF!.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(99),
            "a numeric external-reference index into a broken/placeholder link must preserve the " +
            "cell's last-known loaded value, not overwrite it with #REF!");
        report.Errors.Should().NotContain(e => e.Cell == addr);
    }

    [Fact]
    public void RecalculateAllFormulas_PreservesCachedValue_ForSheetNameAbsentFromLinkSheetNames()
    {
        // Arrange: the external link itself resolves fine (book/index matches), but the referenced
        // sheet name isn't among the link's cached SheetNames -- e.g. the source workbook's sheet was
        // renamed/removed after the link was last refreshed. Uses the QUOTED sheet-qualifier form
        // ('[Book1.xlsx]Sheet2'!B2) -- unlike the unquoted filename-bracket form, this one genuinely
        // PARSES (quoted sheet qualifiers are ordinary lexer/parser syntax), so it reaches
        // SheetEvalContext.GetCellValue's eval-time throw (this fix) rather than the separate,
        // already-existing genuine-parse-failure preservation path
        // (RecalcEngine.IsLikelyExternalWorkbookReferenceFormula) that an unquoted, unparseable
        // filename-bracket reference would take instead.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Book1.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1"); // only Sheet1 is cached -- "Sheet2" is not present
        workbook.ExternalLinks.Add(link);

        var addr = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(addr, LoadedExternalRefCell("'[Book1.xlsx]Sheet2'!B2", new TextValue("Acme Corp")));

        Engine().RecalculateAllFormulas(workbook);

        sheet.GetValue(2, 2).Should().Be(new TextValue("Acme Corp"),
            "a sheet name absent from the resolved link's cached SheetNames must still preserve the " +
            "cell's last-known loaded value, not overwrite it with #REF!");
    }

    [Fact]
    public void RangeShapedReference_PreservesCachedValue_ForNumericIndexIntoBrokenPlaceholderLink()
    {
        // Sibling of the scalar case above but for a RANGE-shaped reference (e.g. a bare
        // dynamic-array/spilling formula body -- Cell.ArrayMode defaults to Dynamic -- whose entire
        // formula is the range reference itself, no wrapping aggregate function). This reaches
        // BuildRangeValue's per-cell context.GetCellValue loop directly (EvaluateSpilling ->
        // EvaluateArrayOperand -> BuildRangeValueOrError), with no upstream SheetExists gate, so it
        // exercises exactly the same unresolvable-external-reference path the scalar case does, just
        // materialized across a range instead of a single cell.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ExternalLinks.Add(new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "",
            TargetMode = "External",
        });

        var addr = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(addr, LoadedExternalRefCell("[1]Sheet1!A1:A2", new NumberValue(55)));

        Engine().RecalculateAllFormulas(workbook);

        sheet.GetValue(3, 3).Should().Be(new NumberValue(55),
            "a range-shaped reference into a broken/placeholder external link must preserve the " +
            "cell's last-known loaded value, not overwrite it with #REF!");
    }

    [Fact]
    public void Evaluate_DirectCall_UnresolvableExternalReference_ThrowsConvertedToValueError()
    {
        // Direct FormulaEvaluator.Evaluate (no RecalcEngine catch/preserve guard involved) converts an
        // uncaught FormulaParseException to #VALUE! -- confirming the throw actually happens (not
        // silently returning ErrorValue.Ref) at the Core.Formula layer itself, exactly mirroring the
        // established resolved-but-uncached-cell assertion style (see R24_ExternalLinkRangeCacheMissTests).
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ExternalLinks.Add(new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "",
            TargetMode = "External",
        });

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=[1]Sheet1!A1", sheet, workbook);

        result.Should().Be(ErrorValue.Value,
            "the unresolvable external reference must throw FormulaParseException internally (converted " +
            "to #VALUE! by a direct caller with no preservation guard), not return ErrorValue.Ref as a " +
            "normal result");
    }

    [Fact]
    public void RecalculateAllFormulas_GenuinelyUnknownLocalSheetName_StillResolvesToRefError()
    {
        // Guard against over-broadening the fix: a formula referencing a sheet name that is simply
        // wrong/unknown in THIS workbook (no brackets at all -- not an external-workbook reference
        // shape) must still genuinely evaluate to #REF!, exactly as before. This must not get routed
        // through the new preservation throw, or a plain bad-sheet-name typo would incorrectly keep an
        // old value forever instead of surfacing #REF!.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new Cell { FormulaText = "NoSuchSheet!A1", Value = new NumberValue(1) });

        Engine().RecalculateAllFormulas(workbook);

        sheet.GetValue(1, 1).Should().Be(ErrorValue.Ref,
            "a reference to an unknown LOCAL sheet name (no external-workbook bracket shape) must still " +
            "resolve to a genuine #REF!, not have a stale value preserved forever");
    }

    [Fact]
    public void RangeFunction_WithFullyResolvableExternalRange_StillComputesCorrectly()
    {
        // Guard against over-broadening the fix: when the external link AND every cell in the range
        // resolve/cache correctly, the range function must still compute the real result (not fall into
        // the new throw/preserve path just because it once touched external-reference code).
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Data File.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1");
        var cachedSheet = new ExternalCachedSheetModel { SheetId = 0 };
        cachedSheet.Values[(1u, 1u)] = new NumberValue(10);
        cachedSheet.Values[(2u, 1u)] = new NumberValue(20);
        link.CachedSheetData.Add(cachedSheet);
        workbook.ExternalLinks.Add(link);

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=MEDIAN('[Data File.xlsx]Sheet1'!A1:A2)", sheet, workbook);

        result.Should().Be(new NumberValue(15));
    }
}
