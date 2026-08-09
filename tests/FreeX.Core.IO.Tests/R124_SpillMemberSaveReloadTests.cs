using System.IO;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R124-io-spill-member-save-stale-extent: "Save/reload silently discards a value typed into a
/// spill member once the anchor next recalculates" -- see docs/parity findings for this wave.
///
/// The full failure chain, replayed through the real product entry points:
///   1. A dynamic-array formula (e.g. "=SEQUENCE(3,1)") spills to A1:A3.
///   2. The user types a literal directly into a non-anchor member (A2) -- EditCellsCommand.Apply,
///      exactly as CommitCellText constructs and executes it, with CommandGuards'
///      allowDynamicSpillMemberWrite letting the write through (R123).
///   3. The workbook is saved via XlsxFileAdapter.Save and reloaded via XlsxFileAdapter.Load --
///      the real file round-trip.
///   4. A later full recalculation (F9 / RecalcEngine.RecalculateAllFormulas) must NOT silently
///      erase the user's literal at A2.
///
/// <see cref="ManualModeStyle_MemberWriteWithNoFollowUpRecalc_ThenSaveReload_ThenF9_PreservesUserLiteral"/>
/// is the genuine fail-before-fix reproduction: a companion fix already landed in RecalcEngine.cs
/// (R124-calc-spill-member-write-anchor-recalc, ExpandChangedCellsWithSpillMemberAnchors) that makes
/// the anchor collapse to #SPILL! in the SAME recalc pass as the member write -- but that fix only
/// helps when RecalcEngine.Recalculate is actually invoked for the write. Not every real path is
/// guaranteed to call it: WorkbookCellEditService's Manual-calculation-mode handling
/// (RecalculateFreshlyEnteredFormulasOnce) restricts recalculation to affected cells that are
/// THEMSELVES formulas, so committing a plain literal over a non-formula spill member calls
/// RecalcEngine.Recalculate for zero cells, and Sheet._spillAnchors keeps reporting the anchor's
/// stale pre-edit extent all the way to Save -- with no chance for the companion RecalcEngine fix to
/// ever run. This test replays exactly that: the command Apply with no follow-up Recalculate call at
/// all (also matches the SAME reachable Automatic-mode window between opening a not-yet-recalculated
/// file and its first recalculation of a given anchor). The fix lives entirely in
/// XlsxFileAdapter.Save.cs: before trusting a live Sheet.TryGetSpillExtent-sourced extent, it now
/// calls Sheet.IsSpillBlocked (the exact same live-occupancy check RecalcEngine itself uses to decide
/// #SPILL!) to catch a registered extent that recalculation just has not caught up to yet, and falls
/// back to writing the anchor as a single-cell array formula (not folding the blocking member's
/// address into the declared ref) exactly as it already does for a formula that IS already cached as
/// #SPILL!.
///
/// <see cref="MemberWrite_ThenSaveReload_ThenFullRecalc_PreservesUserLiteral"/> exercises the SAME
/// save/reload path end-to-end through the companion-fixed RecalcEngine path (Recalculate IS called
/// after the write) -- it already passed before this Save.cs fix (the companion fix alone was
/// sufficient for that reachable path) and must keep passing after it, covering the OTHER half of
/// hasLiveSpillExtent's replacement logic (cell.Value already cached as #SPILL!).
/// </summary>
public sealed class R124SpillMemberSaveReloadTests
{
    /// <summary>
    /// Genuine fail-before-fix repro for the XlsxFileAdapter.Save.cs fix (R124-io-spill-member-save
    /// -stale-extent): before the fix, this failed with the reloaded member showing the anchor's
    /// respilled value (2) instead of the user's literal (999) -- see the failBeforeEvidence in the
    /// task report for the exact assertion failure captured with the fix hand-reverted.
    /// </summary>
    [Fact]
    public void ManualModeStyle_MemberWriteWithNoFollowUpRecalc_ThenSaveReload_ThenF9_PreservesUserLiteral()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var workbook = new Workbook("test0");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        var member = new CellAddress(sheet.Id, 2, 1); // A2 - covered, non-anchor

        sheet.SetFormula(anchor, "SEQUENCE(3,1)");
        engine.RebuildFormulaDependencies(workbook);
        engine.Recalculate(workbook, [anchor]);

        sheet.GetValue(anchor).Should().Be(new NumberValue(1));
        sheet.GetValue(member).Should().Be(new NumberValue(2));

        // Real product entry point for "user types over a spill member" -- EditCellsCommand.Apply,
        // exactly as CommitCellText constructs and executes it, with CommandGuards'
        // allowDynamicSpillMemberWrite letting the write through (R123). Deliberately NO follow-up
        // RecalcEngine.Recalculate call -- mirrors WorkbookCellEditService.ApplyHistoryOutcome in
        // Manual calculation mode, where RecalculateFreshlyEnteredFormulasOnce is a no-op for a
        // non-formula affected cell.
        var ctx = new TestCommandContext(workbook);
        var outcome = EditCellsCommand.ForValue(sheet.Id, member, new NumberValue(999)).Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(member).Should().Be(new NumberValue(999), "the member write itself always takes effect immediately");

        // Real file round-trip: Sheet._spillAnchors for the anchor still reports the stale pre-edit
        // 1x3 extent at this point (nothing invalidated it), so this is exactly the "stale extent
        // reaches Save" scenario the finding describes.
        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.GetValue(member).Should().Be(new NumberValue(999),
            "the user's literal must survive the save/reload round-trip");

        // A later full recalculation (F9) must not silently overwrite the user's literal with the
        // anchor's freshly re-spilled output -- the member must have round-tripped as independent
        // content, not an anchor-owned provisional spill cell.
        var reloadedGraph = new DependencyGraph();
        var reloadedEvaluator = new FormulaEvaluator();
        var reloadedEngine = new RecalcEngine(reloadedGraph, reloadedEvaluator);
        reloadedEngine.RebuildFormulaDependencies(reloaded);
        reloadedEngine.RecalculateAllFormulas(reloaded);

        reloadedSheet.GetValue(member).Should().Be(new NumberValue(999),
            "F9 after reload must not silently erase the user's typed-over value");
    }

    [Fact]
    public void MemberWrite_ThenSaveReload_ThenFullRecalc_PreservesUserLiteral()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        var member = new CellAddress(sheet.Id, 2, 1); // A2 - covered, non-anchor

        sheet.SetFormula(anchor, "SEQUENCE(3,1)");
        engine.RebuildFormulaDependencies(workbook);
        engine.Recalculate(workbook, [anchor]);

        // Spilled successfully: A1:A3 = 1,2,3.
        sheet.GetValue(anchor).Should().Be(new NumberValue(1));
        sheet.GetValue(member).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));

        // Step 2+3: real product entry point for "user types over a spill member" --
        // EditCellsCommand.Apply followed by RecalcEngine.Recalculate fed exactly
        // outcome.AffectedCells, mirroring WorkbookCellEditService.ApplyHistoryOutcome.
        var ctx = new TestCommandContext(workbook);
        var outcome = EditCellsCommand.ForValue(sheet.Id, member, new NumberValue(999)).Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var affected = outcome.AffectedCells ?? [];
        foreach (var addr in affected)
        {
            var cell = workbook.GetSheet(addr.Sheet)?.GetCell(addr);
            if (cell?.FormulaText is null)
                engine.ClearFormulaDependencies(addr);
        }
        engine.Recalculate(workbook, affected);

        // The member write took effect and the anchor collapsed to #SPILL! in the same pass.
        sheet.GetValue(member).Should().Be(new NumberValue(999));
        sheet.GetValue(anchor).Should().Be(ErrorValue.Spill);

        // Step 4: real file round-trip.
        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.GetValue(member).Should().Be(new NumberValue(999),
            "the user's literal must survive the save/reload round-trip");

        // Step 5: a later full recalculation (F9) must not silently overwrite the user's literal
        // with the anchor's freshly re-spilled output.
        var reloadedGraph = new DependencyGraph();
        var reloadedEvaluator = new FormulaEvaluator();
        var reloadedEngine = new RecalcEngine(reloadedGraph, reloadedEvaluator);
        reloadedEngine.RebuildFormulaDependencies(reloaded);
        reloadedEngine.RecalculateAllFormulas(reloaded);

        reloadedSheet.GetValue(member).Should().Be(new NumberValue(999),
            "F9 after reload must not silently erase the user's typed-over value");
        reloadedSheet.GetValue(anchor).Should().Be(ErrorValue.Spill,
            "the anchor must stay blocked (#SPILL!) after reload+F9, matching its state at save time");
    }

    /// <summary>
    /// No-regression sibling: an ordinary, never-touched spill (no member write) must still
    /// round-trip its full spilled extent and keep spilling correctly after reload -- the
    /// companion RecalcEngine fix and this save/reload path must not disturb the common case.
    /// </summary>
    [Fact]
    public void UntouchedSpill_SaveReload_StillSpillsAllMembersAfterFullRecalc()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var workbook = new Workbook("test2");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1

        sheet.SetFormula(anchor, "SEQUENCE(3,1)");
        engine.RebuildFormulaDependencies(workbook);
        engine.Recalculate(workbook, [anchor]);

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        reloadedSheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        reloadedSheet.GetValue(3, 1).Should().Be(new NumberValue(3));

        var reloadedGraph = new DependencyGraph();
        var reloadedEvaluator = new FormulaEvaluator();
        var reloadedEngine = new RecalcEngine(reloadedGraph, reloadedEvaluator);
        reloadedEngine.RebuildFormulaDependencies(reloaded);
        reloadedEngine.RecalculateAllFormulas(reloaded);

        reloadedSheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        reloadedSheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        reloadedSheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }
}
