using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-68 fix-bucket "fml-external" regression test.
///
/// R68-io-external-links-6-1: <see cref="FormulaEvaluator"/>'s
/// <c>TryExpandSheetSpanAggregateRange</c> (the argument-expansion helper that materializes a
/// 3-D sheet-span aggregate argument like <c>Sheet1:Sheet3!A1</c> across every spanned sheet)
/// resolved BOTH endpoint sheet names via <c>FindSheetIndex</c>, which only searches the LOCAL
/// workbook's own <see cref="Workbook.Sheets"/>. An external-workbook 3-D span (e.g.
/// <c>'[1]Sheet1:Sheet3'!A1:A5</c>, Excel's on-disk shape for an external reference spanning
/// sheets) never resolves through that local-only search, so it always fell through to a bare
/// #REF! -- clobbering the cached value Excel preserves for an external reference, unlike the
/// single-sheet external path (<see cref="ExternalSheetReferenceResolver"/> /
/// <c>SheetEvalContext.GetCellValue</c>/<c>GetRangeValues</c> in
/// FormulaEvaluator.Contexts.cs), which already throws <see cref="FormulaParseException"/> on an
/// unresolvable/uncached external reference so RecalcEngine's external-workbook-reference guard
/// preserves the cell's last-known loaded value instead of overwriting it.
///
/// The fix detects the external '[n]' marker on the span's start sheet name (via
/// <see cref="ExternalSheetReferenceResolver.TryResolve"/>) before surfacing #REF!, and throws
/// the same <see cref="FormulaParseException"/> the single-sheet path throws -- observable here
/// as the direct AST-based <c>Evaluate(FormulaNode, ...)</c> overload (which, unlike the
/// string-based overload, does not itself catch <see cref="FormulaParseException"/>) propagating
/// the exception instead of returning a plain #REF! <see cref="ScalarValue"/>.
/// </summary>
public sealed class R68_ExternalWorkbookSheetSpanAggregateTests
{
    private readonly FormulaEvaluator _evaluator = new();

    private static (Workbook workbook, Sheet sheet) MakeWorkbookWithExternalSheetSpanLink()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Local");

        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Data File.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1");
        link.SheetNames.Add("Sheet2");
        link.SheetNames.Add("Sheet3");

        var cachedSheet = new ExternalCachedSheetModel { SheetId = 0 };
        cachedSheet.Values[(1u, 1u)] = new NumberValue(10);
        link.CachedSheetData.Add(cachedSheet);
        workbook.ExternalLinks.Add(link);

        return (workbook, sheet);
    }

    [Fact]
    public void ExternalSheetSpan_ThrowsParseException_InsteadOfReturningRef()
    {
        var (workbook, sheet) = MakeWorkbookWithExternalSheetSpanLink();

        // Before the fix: TryExpandSheetSpanAggregateRange's FindSheetIndex search against the
        // LOCAL workbook (which has no "[1]Sheet1" / "Sheet3" sheets at all) always missed for an
        // external span, so the whole SUM call short-circuited to a bare ErrorValue.Ref return --
        // never routing through RecalcEngine's external-workbook-reference preservation guard the
        // way the single-sheet external path already does.
        var ast = FormulaEvaluator.ParseFormula("SUM('[1]Sheet1:Sheet3'!A1:A5)");

        var act = () => _evaluator.Evaluate(ast, sheet, workbook, null);

        act.Should().Throw<FormulaParseException>(
            "an external-workbook 3-D span must be treated exactly like the single-sheet external " +
            "path: throwing lets RecalcEngine preserve the cell's last-known cached value instead " +
            "of clobbering it with #REF!");
    }

    [Fact]
    public void ExternalSheetSpan_ViaStringEvaluate_NoLongerSurfacesRef()
    {
        var (workbook, sheet) = MakeWorkbookWithExternalSheetSpanLink();

        // The string-based Evaluate(...) overload DOES catch FormulaParseException and converts it
        // to #VALUE! (mirroring the single-sheet external path's own direct-evaluation behavior --
        // see R24_ExternalLinkRangeCacheMissTests). Before the fix this returned #REF! directly;
        // after the fix it must no longer do so, even though this layer alone cannot exercise
        // RecalcEngine's actual preserve-the-cached-value behavior.
        var result = _evaluator.Evaluate("=SUM('[1]Sheet1:Sheet3'!A1:A5)", sheet, workbook);

        result.Should().NotBe(ErrorValue.Ref,
            "an external-workbook 3-D span must no longer be clobbered with #REF!");
    }

    [Fact]
    public void LocalSheetSpan_StillEvaluatesNormally()
    {
        // No-regression sibling: an ordinary LOCAL 3-D span (no external marker) must still resolve
        // and sum across the spanned sheets exactly as before.
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));

        var result = _evaluator.Evaluate("=SUM(Sheet1:Sheet3!A1)", sheet1, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void BrokenLocalSheetSpan_StillReturnsRef()
    {
        // No-regression sibling: a span naming sheets that exist neither locally nor as a
        // resolvable external reference is a genuine broken reference and must still be #REF!.
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=SUM(NoSuchSheet1:NoSuchSheet3!A1)", sheet1, workbook);

        result.Should().Be(ErrorValue.Ref);
    }
}
