using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R143-extlink-2: a sheet-qualified external defined-name reference (e.g. <c>=[1]Sheet2!Total</c>)
/// ignored the sheet qualifier once resolution reached
/// <c>FormulaEvaluator.TryResolveExternalSheetQualifiedDefinedName</c> (FormulaEvaluator.References.cs)
/// -- it computed <c>resolved.SheetIndex</c> via <see cref="ExternalSheetReferenceResolver.TryResolve"/>
/// but then discarded it, rebuilding the sheet-LESS opaque <c>"[n]!Name"</c> lookup key that
/// <see cref="ExternalSheetReferenceResolver.TryResolveExternalDefinedName(Workbook?, string, out string)"/>
/// expects for the genuinely-unqualified <c>[n]!Name</c> shape (R58). That method's candidate loop
/// (<c>match ??= candidate</c>) then picked whichever sheet-scoped
/// <see cref="ExternalDefinedNameModel"/> candidate came first in
/// <see cref="ExternalLinkModel.DefinedNames"/> file order -- regardless of which external sheet the
/// formula actually qualified -- so two external sheets defining the identical name silently
/// resolved to the wrong one with no error.
///
/// Fixed by threading the resolved 0-based sheet index through to a new
/// <see cref="ExternalSheetReferenceResolver.TryResolveExternalDefinedName(Workbook?, string, int?, out string)"/>
/// overload that prefers an exact sheet-id match, falls back to the workbook-global candidate only
/// when no candidate is scoped to that sheet, and never falls back to a DIFFERENT sheet's
/// scoped candidate.
/// </summary>
public sealed class R143_ExternalSheetQualifiedDefinedNameDisambiguationTests
{
    private static Workbook BuildWorkbookWithTwoExternalSheetsSharingADefinedNameName()
    {
        var workbook = new Workbook("Test");
        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Book1.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1"); // external sheet index 0
        link.SheetNames.Add("Sheet2"); // external sheet index 1

        // Two DIFFERENT sheet-scoped defined names that happen to share the identical name
        // "Total" -- Sheet1's own local "Total" and Sheet2's own local "Total" -- exactly the
        // ambiguous shape the bug collapsed into a single "whichever came first" answer.
        link.DefinedNames.Add(new ExternalDefinedNameModel { Name = "Total", RefersTo = "Sheet1!$A$1", SheetId = 0 });
        link.DefinedNames.Add(new ExternalDefinedNameModel { Name = "Total", RefersTo = "Sheet2!$A$1", SheetId = 1 });

        // A workbook-global name present on neither sheet's own local scope, to prove the
        // sheet-qualified path still falls back to it exactly like the unqualified "[n]!Name"
        // form already does when the qualified sheet has no local candidate of its own.
        link.DefinedNames.Add(new ExternalDefinedNameModel { Name = "GlobalOnly", RefersTo = "Sheet1!$C$1", SheetId = null });

        // A name scoped ONLY to Sheet1, with no Sheet2-scoped sibling and no workbook-global
        // fallback -- qualifying it with Sheet2 must NOT silently borrow Sheet1's copy.
        link.DefinedNames.Add(new ExternalDefinedNameModel { Name = "Sheet1Only", RefersTo = "Sheet1!$D$1", SheetId = 0 });

        var sheet1Cache = new ExternalCachedSheetModel { SheetId = 0 };
        sheet1Cache.Values[(1u, 1u)] = new NumberValue(10);  // Sheet1!A1
        sheet1Cache.Values[(1u, 3u)] = new NumberValue(555); // Sheet1!C1
        sheet1Cache.Values[(1u, 4u)] = new NumberValue(777); // Sheet1!D1
        link.CachedSheetData.Add(sheet1Cache);

        var sheet2Cache = new ExternalCachedSheetModel { SheetId = 1 };
        sheet2Cache.Values[(1u, 1u)] = new NumberValue(99); // Sheet2!A1
        link.CachedSheetData.Add(sheet2Cache);

        workbook.ExternalLinks.Add(link);
        return workbook;
    }

    [Theory]
    [InlineData("=[1]Sheet1!Total", 10)]       // sheet-qualified: must pick Sheet1's OWN "Total"
    [InlineData("=[1]Sheet2!Total", 99)]       // the actual bug scenario: must pick Sheet2's OWN "Total", not Sheet1's
    [InlineData("=[1]Sheet1!GlobalOnly", 555)] // sheet-scoped tier empty on Sheet1 -> falls back to workbook-global
    [InlineData("=[1]Sheet2!GlobalOnly", 555)] // sheet-scoped tier empty on Sheet2 too -> same workbook-global
    public void SheetQualifiedExternalDefinedName_ResolvesTheQualifiedSheetsOwnCandidate(string formula, double expected)
    {
        var workbook = BuildWorkbookWithTwoExternalSheetsSharingADefinedNameName();
        var sheet = workbook.AddSheet("Local");

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate(formula, sheet, workbook);

        result.Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void SheetQualifiedExternalDefinedName_NeverFallsBackToADifferentSheetsScopedCandidate()
    {
        // "Sheet1Only" is scoped ONLY to Sheet1 (SheetId 0) with no workbook-global sibling.
        // Qualifying it with Sheet2 must return #REF!, not silently resolve to Sheet1's value --
        // the exact defect this fix closes (previously "match ??= candidate" picked whichever
        // sheet-scoped candidate came first regardless of the qualifier).
        var workbook = BuildWorkbookWithTwoExternalSheetsSharingADefinedNameName();
        var sheet = workbook.AddSheet("Local");

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=[1]Sheet2!Sheet1Only", sheet, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    // ── Sibling no-regression cases ─────────────────────────────────────────

    [Fact]
    public void SheetLessExternalDefinedName_UnqualifiedForm_StillPrefersWorkbookGlobalOverSheetScoped()
    {
        // The sheet-less "[n]!Name" form (R58) names no sheet at all -- its own documented
        // precedence (workbook-global candidate wins outright over any sheet-scoped candidate)
        // must be completely unaffected by threading a preferred-sheet tiebreaker through the
        // new overload for the sheet-qualified path.
        var workbook = BuildWorkbookWithTwoExternalSheetsSharingADefinedNameName();
        var sheet = workbook.AddSheet("Local");

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=[1]!GlobalOnly", sheet, workbook);

        result.Should().Be(new NumberValue(555));
    }

    [Fact]
    public void RealLocalSheetQualifiedNamedRange_StillResolves_AfterDisambiguationFix()
    {
        // A genuinely-local (non-external) Sheet!Name reference must be unaffected: it never
        // reaches TryResolveExternalSheetQualifiedDefinedName at all (workbook.GetSheet finds a
        // real sheet first).
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var a1 = new CellAddress(sheet2.Id, 1, 1);
        var a2 = new CellAddress(sheet2.Id, 2, 1);
        sheet2.SetCell(a1, new NumberValue(10));
        sheet2.SetCell(a2, new NumberValue(20));
        workbook.DefineNamedRange("LocalRange", new GridRange(a1, a2));

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=SUM(Sheet2!LocalRange)", sheet1, workbook);

        result.Should().Be(new NumberValue(30));
    }
}
