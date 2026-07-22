using System.Linq;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R72-meta-2 (src/FreeX.Core.Commands/SheetCommands.cs): R71 fixed the forward Do path of
/// RenameSheetCommand's T7 pass and RemoveSheetCommand's X3 pass to call
/// <see cref="ConditionalFormatCollection.NotifyRulesChanged"/> after rewriting a CF rule's
/// FormulaText in place, so the viewport CF-context cache (keyed on
/// (sheet.Id, sheet.ContentVersion, sheet.ConditionalFormats.Version)) gets invalidated. But the
/// UNDO/Revert paths that restore "cf.FormulaText = oldValue;" did NOT call NotifyRulesChanged(),
/// so after a render-then-Undo the cache key stays byte-identical to the cached post-rename entry
/// and the stale precompiled AST (still referencing the renamed sheet name) keeps being evaluated —
/// the CF highlighting freezes at the pre-Undo state instead of reflecting the restored formula.
/// </summary>
public sealed class R72_CfUndoStaleSweepTests
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

    // ── RenameSheetCommand.Revert (T7 restore) ──────────────────────────────

    [Fact]
    public void RenameSheet_Undo_RestoresCFFormula_BumpsConditionalFormatsVersionOnHostSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);

        AddFormulaRule(report, Range(report.Id, 1, 1, 1, 1), "Sheet1!A1>10", new CellColor(255, 0, 0));

        var cmd = new RenameSheetCommand(sheet1.Id, "Data");
        cmd.Apply(ctx).Success.Should().BeTrue();
        report.ConditionalFormats[0].FormulaText.Should().Be("Data!A1>10");
        var versionAfterRename = report.ConditionalFormats.Version;

        cmd.Revert(ctx);

        report.ConditionalFormats[0].FormulaText.Should().Be("Sheet1!A1>10",
            because: "Undo must restore the CF rule's original formula text");
        report.ConditionalFormats.Version.Should().BeGreaterThan(versionAfterRename,
            because: "restoring cf.FormulaText in place must also bump Version (mirroring the Do " +
                     "path) so the viewport CF-context cache is invalidated after Undo");
    }

    [Fact]
    public void RenameSheet_Undo_NoCFRuleTouched_DoesNotSpuriouslyBumpConditionalFormatsVersion()
    {
        // Sibling no-regression: undoing a rename that never touched a given sheet's CF rules
        // must not bump that sheet's ConditionalFormats.Version.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var other = wb.AddSheet("Other");
        var ctx = new TestCommandContext(wb);

        AddFormulaRule(other, Range(other.Id, 1, 1, 1, 1), "Other!B1>10", new CellColor(0, 255, 0));

        var cmd = new RenameSheetCommand(sheet1.Id, "Data");
        cmd.Apply(ctx).Success.Should().BeTrue();
        var versionAfterRename = other.ConditionalFormats.Version;

        cmd.Revert(ctx);

        other.ConditionalFormats[0].FormulaText.Should().Be("Other!B1>10");
        other.ConditionalFormats.Version.Should().Be(versionAfterRename,
            because: "a sheet whose CF rules were never rewritten by the rename must not have its " +
                     "Version bumped by Undo either");
    }

    [Fact]
    public void RenameSheet_UndoAfterRender_StaleCacheWouldFreezeCFEvaluation_FixRebuildsContext()
    {
        // End-to-end: rename (rewriting the CF formula), render (caching the post-rename context),
        // then Undo (restoring the original formula). Without the fix, the cache key is unchanged
        // and the stale post-rename context/result keeps being served after Undo.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), Cell.FromValue(new NumberValue(15)));
        report.SetCell(new CellAddress(report.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        var highlight = new CellColor(255, 0, 0);
        AddFormulaRule(report, Range(report.Id, 1, 1, 1, 1), "Sheet1!A1>10", highlight);

        var cmd = new RenameSheetCommand(sheet1.Id, "Data");
        cmd.Apply(ctx).Success.Should().BeTrue();
        report.ConditionalFormats[0].FormulaText.Should().Be("Data!A1>10");

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 500, 500);

        // Prime the cache post-rename: Data!A1 = 15 > 10 is true, so Report!A1 is highlighted.
        var vpAfterRename = svc.GetViewport(wb, report.Id, request);
        vpAfterRename.Cells.Single(c => c.Row == 1 && c.Col == 1).Style!.FillColor.Should().Be(highlight);
        var buildCountAfterRename = svc.CfContextBuildCount;

        cmd.Revert(ctx);
        report.ConditionalFormats[0].FormulaText.Should().Be("Sheet1!A1>10");

        // Sheet1!A1 is still 15 (never touched), so the restored formula "Sheet1!A1>10" is still
        // true and Report!A1 should STILL be highlighted post-undo — but only if the CF context is
        // actually rebuilt against the restored formula. Prove the rebuild happened by changing
        // Sheet1!A1 to something the restored formula would evaluate differently.
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), Cell.FromValue(new NumberValue(5)));

        var vpAfterUndo = svc.GetViewport(wb, report.Id, request);

        svc.CfContextBuildCount.Should().BeGreaterThan(buildCountAfterRename,
            because: "Undo restored Report's CF FormulaText; without a Version bump the cache key " +
                     "is unchanged and the stale post-rename context would be reused");
        vpAfterUndo.Cells.Single(c => c.Row == 1 && c.Col == 1).Style?.FillColor.Should().NotBe(highlight,
            because: "a stale cache would keep returning the frozen post-rename cached result " +
                     "instead of re-evaluating the restored Sheet1!A1>10 formula against the new value 5");
    }

    // ── RemoveSheetCommand.Revert (X3 restore) ──────────────────────────────

    [Fact]
    public void DeleteSheet_Undo_RestoresSurvivingCFFormula_BumpsConditionalFormatsVersion()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);

        AddFormulaRule(report, Range(report.Id, 1, 1, 1, 1), "Sheet1!A1>10", new CellColor(255, 0, 0));

        var cmd = new RemoveSheetCommand(sheet1.Id);
        cmd.Apply(ctx).Success.Should().BeTrue();
        report.ConditionalFormats[0].FormulaText.Should().Contain("#REF!");
        var versionAfterDelete = report.ConditionalFormats.Version;

        cmd.Revert(ctx);

        report.ConditionalFormats[0].FormulaText.Should().Be("Sheet1!A1>10",
            because: "Undo must restore the surviving sheet's CF formula text to its pre-delete value");
        report.ConditionalFormats.Version.Should().BeGreaterThan(versionAfterDelete,
            because: "restoring cf.FormulaText in place must also bump Version so the stale " +
                     "viewport CF-context cache is invalidated after Undo, just like the rename Revert");
    }

    [Fact]
    public void DeleteSheet_Undo_NoCFRuleTouched_DoesNotSpuriouslyBumpConditionalFormatsVersion()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var other = wb.AddSheet("Other");
        var ctx = new TestCommandContext(wb);

        AddFormulaRule(other, Range(other.Id, 1, 1, 1, 1), "Other!B1>10", new CellColor(0, 255, 0));

        var cmd = new RemoveSheetCommand(sheet1.Id);
        cmd.Apply(ctx).Success.Should().BeTrue();
        var versionAfterDelete = other.ConditionalFormats.Version;

        cmd.Revert(ctx);

        other.ConditionalFormats[0].FormulaText.Should().Be("Other!B1>10");
        other.ConditionalFormats.Version.Should().Be(versionAfterDelete,
            because: "a surviving sheet whose CF rules were never rewritten by the delete must not " +
                     "have its Version bumped by Undo either");
    }
}
