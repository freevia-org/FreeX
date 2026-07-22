using System.Linq;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R71-twoway-map-stale-sweep-1 (src/FreeX.Core.Commands/SheetCommands.cs): RenameSheetCommand's
/// T7 pass and RemoveSheetCommand's X3 pass rewrite a custom-formula CF rule's FormulaText in
/// place (e.g. "Sheet1!A1&gt;10" becomes "Data!A1&gt;10" after Sheet1 is renamed to Data) but never
/// called <see cref="ConditionalFormatCollection.NotifyRulesChanged"/> or otherwise bumped the
/// host sheet's rule-set version. The viewport CF-context cache
/// (<c>ViewportService</c>'s internal cache) is keyed on
/// (sheet.Id, sheet.ContentVersion, sheet.ConditionalFormats.Version) and, once built, holds a
/// precompiled formula AST keyed by the <see cref="ConditionalFormat"/> object reference (never
/// re-parsed on eval — see ViewportConditionalFormatEvaluator.Formulas.cs PrecomputeFormulaCaches).
/// Because the rename/delete rewrite mutates that same CF instance's FormulaText without bumping
/// the version, the cache key stays byte-identical after the rewrite, so the next CF evaluation is
/// a stale cache HIT that keeps using the pre-rewrite parsed formula/cached per-cell result —
/// silently freezing the rule's behavior as of the moment just before the rename, even though
/// Manage Rules already shows the rewritten text.
/// </summary>
public sealed class R71_CfTwoWayMapStaleSweepTests
{
    private static GridRange Range(SheetId id, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(id, r1, c1), new CellAddress(id, r2, c2));

    private static ConditionalFormat AddFormulaRule(Sheet hostSheet, GridRange appliesTo, string formulaText, CellColor color)
    {
        var cf = new ConditionalFormat
        {
            AppliesTo = appliesTo,
            RuleType = CfRuleType.Formula,
            Priority = 1,
            FormulaText = formulaText,
            FormatIfTrue = new CellStyle { FillColor = color }
        };
        hostSheet.ConditionalFormats.Add(cf);
        return cf;
    }

    // ── RenameSheetCommand T7 pass ──────────────────────────────────────────

    [Fact]
    public void RenameSheet_RewritesCFFormula_BumpsConditionalFormatsVersionOnHostSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);

        AddFormulaRule(report, Range(report.Id, 1, 1, 1, 1), "Sheet1!A1>10", new CellColor(255, 0, 0));
        var versionBefore = report.ConditionalFormats.Version;

        new RenameSheetCommand(sheet1.Id, "Data").Apply(ctx).Success.Should().BeTrue();

        report.ConditionalFormats[0].FormulaText.Should().Be("Data!A1>10",
            because: "T7 must still rewrite the cross-sheet CF formula text to the new sheet name");
        report.ConditionalFormats.Version.Should().BeGreaterThan(versionBefore,
            because: "rewriting a CF rule's FormulaText in place must bump Version so the viewport " +
                     "CF-context cache (keyed on ConditionalFormats.Version) is invalidated");
    }

    [Fact]
    public void RenameSheet_NoCFRuleTouched_DoesNotSpuriouslyBumpConditionalFormatsVersion()
    {
        // Sibling no-regression: a rename that doesn't touch ANY CF rule on a given sheet must
        // not bump that sheet's ConditionalFormats.Version (would cause needless cache churn).
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var other = wb.AddSheet("Other");
        var ctx = new TestCommandContext(wb);

        // CF rule with a same-sheet-only formula: unaffected by renaming Sheet1.
        AddFormulaRule(other, Range(other.Id, 1, 1, 1, 1), "Other!B1>10", new CellColor(0, 255, 0));
        var versionBefore = other.ConditionalFormats.Version;

        new RenameSheetCommand(sheet1.Id, "Data").Apply(ctx).Success.Should().BeTrue();

        other.ConditionalFormats[0].FormulaText.Should().Be("Other!B1>10",
            because: "the rule never referenced the renamed sheet, so FormulaRewriter must leave it untouched");
        other.ConditionalFormats.Version.Should().Be(versionBefore,
            because: "a sheet whose CF rules were not rewritten must not have its Version bumped");
    }

    [Fact]
    public void RenameSheet_StaleCacheWouldFreezeCFEvaluation_FixRebuildsContextAndReflectsNewFormula()
    {
        // End-to-end: build the viewport CF context once (caching the pre-rename formula AST and
        // its evaluated per-cell result), rename the referenced sheet, then change the underlying
        // value that only the FRESH ("Data!A1>10") formula would see. A stale cache serves the
        // frozen pre-rename result (still referencing the no-longer-existing "Sheet1" name, which
        // resolves to #REF! and never matches); the fix rebuilds the context so the rule tracks
        // the renamed sheet correctly.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), Cell.FromValue(new NumberValue(15)));
        // Give Report!A1 actual content (rather than leaving it blank) so the viewport always
        // emits a DisplayCell for it regardless of whether the CF rule matches — the blank-cell
        // fast path only emits an entry when a conditional format actually changes its style.
        report.SetCell(new CellAddress(report.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        var highlight = new CellColor(255, 0, 0);
        AddFormulaRule(report, Range(report.Id, 1, 1, 1, 1), "Sheet1!A1>10", highlight);

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 500, 500);

        // Prime the cache: 15 > 10 is true, so Report!A1 is highlighted, and the context (with its
        // precomputed AST + per-cell result cache) is now cached under Report's pre-rename key.
        var vpBefore = svc.GetViewport(wb, report.Id, request);
        vpBefore.Cells.Single(c => c.Row == 1 && c.Col == 1).Style!.FillColor.Should().Be(highlight);
        var buildCountBefore = svc.CfContextBuildCount;

        new RenameSheetCommand(sheet1.Id, "Data").Apply(ctx).Success.Should().BeTrue();

        // Change the renamed sheet's referenced cell so the pre-rename ("Sheet1!A1>10") and
        // post-rename ("Data!A1>10") formulas would disagree: pre-rename resolves "Sheet1" to
        // nothing (#REF!, never matches); post-rename correctly resolves "Data"!A1 = 5 > 10 = false.
        // Either way the cell must NOT be highlighted post-rename — but a stale cache would instead
        // keep serving the FROZEN pre-rename cached result (true, from the priming call above),
        // which is the actual bug this test guards against.
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), Cell.FromValue(new NumberValue(5)));

        var vpAfter = svc.GetViewport(wb, report.Id, request);

        svc.CfContextBuildCount.Should().BeGreaterThan(buildCountBefore,
            because: "renaming the sheet rewrote Report's CF FormulaText in place; without a Version " +
                     "bump the (sheet.Id, ContentVersion, ConditionalFormats.Version) cache key is " +
                     "unchanged and the stale context (and its cached true result) would be reused");
        vpAfter.Cells.Single(c => c.Row == 1 && c.Col == 1).Style?.FillColor.Should().NotBe(highlight,
            because: "a stale cache would keep returning the frozen pre-rename cached result (true) " +
                     "instead of re-evaluating against the current Data!A1 = 5");
    }

    // ── RemoveSheetCommand X3 pass ──────────────────────────────────────────

    [Fact]
    public void DeleteSheet_RewritesSurvivingCFFormulaToRef_BumpsConditionalFormatsVersion()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);

        AddFormulaRule(report, Range(report.Id, 1, 1, 1, 1), "Sheet1!A1>10", new CellColor(255, 0, 0));
        var versionBefore = report.ConditionalFormats.Version;

        new RemoveSheetCommand(sheet1.Id).Apply(ctx).Success.Should().BeTrue();

        report.ConditionalFormats[0].FormulaText.Should().Contain("#REF!",
            because: "X3 rewrites a surviving sheet's CF formula referencing the deleted sheet to #REF!");
        report.ConditionalFormats.Version.Should().BeGreaterThan(versionBefore,
            because: "rewriting a surviving sheet's CF rule to #REF! must bump Version so the stale " +
                     "viewport CF-context cache is invalidated, just like RenameSheetCommand's T7 pass");
    }

    [Fact]
    public void DeleteSheet_NoCFRuleTouched_DoesNotSpuriouslyBumpConditionalFormatsVersion()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var other = wb.AddSheet("Other");
        var ctx = new TestCommandContext(wb);

        AddFormulaRule(other, Range(other.Id, 1, 1, 1, 1), "Other!B1>10", new CellColor(0, 255, 0));
        var versionBefore = other.ConditionalFormats.Version;

        new RemoveSheetCommand(sheet1.Id).Apply(ctx).Success.Should().BeTrue();

        other.ConditionalFormats[0].FormulaText.Should().Be("Other!B1>10");
        other.ConditionalFormats.Version.Should().Be(versionBefore,
            because: "a surviving sheet whose CF rules never referenced the deleted sheet must not " +
                     "have its Version bumped");
    }
}
