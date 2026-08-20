using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

/// <summary>
/// Coordinates recalculation of formula cells when values change.
/// Uses the dependency graph to determine order and evaluates only dirty cells.
/// </summary>
public sealed class RecalcEngine
{
    // Keep only tiny ranges as exact cell edges; larger ranges avoid repeated dependent-list fan-out.
    private const long CompactRangeCellThreshold = 8;
    private const int MaxDependencyPlanCacheEntries = 1024;
    // Floor on chained spill-dependent follow-up passes (see the loop in
    // ResolveSpillTargetDependentsFixpoint): a safety net so a pathological/self-perpetuating chain
    // cannot spin forever even in a workbook with very few formula cells. Ordinary sheets converge
    // in 1-2 passes; the loop's real ceiling is the larger of this floor and the workbook's total
    // formula-cell count (a chain of dependent spilling formulas cannot be deeper than the number
    // of formula cells that exist to form it), so a legitimately long chain of plain-address spill
    // readers is never truncated mid-convergence. See finding R112 spill-chain-depth-cap.
    private const int MaxSpillDependentPasses = 64;
    private static readonly IReadOnlySet<CellAddress> EmptyDependencyCells = FrozenSet<CellAddress>.Empty;
    private static readonly IReadOnlyList<CellAddress> EmptyCells = [];
    private static readonly IReadOnlyList<(CellAddress Cell, string Error)> EmptyErrors = [];
    private static readonly RecalcReport EmptyReport = new(EmptyCells, EmptyErrors, EmptyCells);

    private readonly DependencyGraph _graph;
    private readonly FormulaEvaluator _evaluator;
    // Single-threaded only. If multi-threaded recalc is added (Phase 4), protect with a lock.
    private readonly HashSet<CellAddress> _volatileCells = [];
    private readonly Dictionary<DependencyPlanCacheKey, FormulaDependencyPlan> _dependencyPlanCache = [];
    private readonly Queue<DependencyPlanCacheKey> _dependencyPlanCacheOrder = [];
    // Anchors that most recently evaluated to #SPILL! because IsSpillBlocked found an occupied
    // target cell. The blocking cell (e.g. a plain value pasted into the spill range) has no
    // dependency-graph edge back to the anchor — its formula's static references never included
    // it — so an edit that only clears the blocker would otherwise never re-dirty the anchor and
    // the stale #SPILL! would persist until a full recalc. Tracked here (not in Sheet, which has
    // no notion of "why" a spill is blocked) and retried as extra changed-roots every recalc pass
    // so a cleared blocker makes the anchor spill again immediately, matching Excel.
    private readonly HashSet<CellAddress> _spillBlockedAnchors = [];
    // Cells most recently classified as part of a non-iterative circular reference (seeded to 0
    // by AddCyclicCell below), kept live across incremental recalc passes so a caller with no
    // access to a single Recalculate()'s transient RecalcReport - e.g. FormulaAuditingService's
    // error-checking rule for "Formulas with circular references" - can still find out which
    // cells are currently cyclic. Never populated while Workbook.IterativeCalculation is on: once
    // iterative calc resolves a cycle to a converged value, Excel no longer treats it as an error
    // (see RunIterativeCalc). Entries are removed once the cell evaluates normally again as part
    // of the ordinary evaluation loop (its formula no longer participates in a cycle).
    private readonly HashSet<CellAddress> _cyclicCells = [];
    // Cells most recently resolved by RunIterativeCalc for an ACTIVE iterative circular-reference
    // group (Workbook.IterativeCalculation on). In real Excel, an active iterative circular group
    // re-iterates on every recalculation pass -- including a bare F9 "Calculate Now" that touches
    // nothing else in the workbook -- so this is seeded into the traversal exactly like
    // _volatileCells (see BuildChangedSetForTraversal) rather than only ever being reached when the
    // caller happens to supply the cyclic cell(s) as changedCells. Refreshed every pass: entries not
    // still reported cyclic by that pass's own fresh GetRecalcOrder/GetEvaluationOrder are dropped,
    // so a since-fixed formula or a switch to non-iterative mode (which re-marks it via
    // AddCyclicCell instead) naturally ages out. See R92-calc-iterative-convergence-5-1.
    private readonly HashSet<CellAddress> _activeIterativeCyclicCells = [];
    // Anchor cell -> the set of formula cells whose dependency-graph edges are a live union of
    // that anchor's spill extent with a literal end cell (an ANCHORARRAY(anchor,end) 2-arg
    // reference, e.g. "=SUM(A1#:B5)"). RegisterFormulaDependencies only runs once per formula
    // cell (see the CachedAst gate in the main evaluation loop below), so if the anchor's spill
    // extent later grows, shrinks, appears, or disappears on some LATER edit, the registered
    // union rectangle for every dependent recorded here goes stale — a plain data cell that is
    // newly inside (or newly outside) the TRUE current union has no graph edge (or a spurious
    // one) and would not (or would wrongly) dirty the dependent until an unrelated full recalc
    // rebuilt every dependency from scratch. Populated by RegisterFormulaDependencies (via
    // FormulaDependencySet.AnchorArrayAnchors) and consulted by the main evaluation loop every
    // time a cell that IS such an anchor finishes evaluating, so the affected dependents'
    // dependencies are re-derived from the anchor's now-current live extent immediately. See
    // R92-calc-array-recalc-order-5-1.
    private readonly Dictionary<CellAddress, HashSet<CellAddress>> _anchorArraySpillDependents = [];
    // Reverse index of the above: dependent formula cell -> the anchor addresses it is currently
    // registered under, so a later re-registration (the formula changed to no longer read that
    // anchor, or to read a different one) can remove the stale forward-map entries instead of
    // leaking them for the lifetime of the engine.
    private readonly Dictionary<CellAddress, List<CellAddress>> _anchorArraySpillDependentAnchors = [];

    // R95-calc-deleted-sheet-leak: the sheet ids each workbook had as of its OWN most recent
    // RebuildFormulaDependencies/RetireWorkbook call. RebuildFormulaDependencies necessarily scopes
    // its ClearForSheets/_volatileCells/_spillBlockedAnchors purge to workbook.Sheets AS IT
    // CURRENTLY STANDS (see that method's remarks on why a blanket clear would wipe every other
    // open workbook's edges out of this shared graph) -- but a sheet removed since the last call
    // (Delete Sheet, or Undo of Add Sheet) is by definition no longer in that current list, so
    // without this tracked baseline its graph edges, volatile-cell registrations, spill-blocked
    // anchors, cyclic-cell markers, and cached dependency plans would never be purged and would
    // leak for the life of the process (this engine is an app-lifetime singleton -- see the class
    // remarks). Keyed by Workbook reference (not WorkbookId) via a weak table so a since-discarded
    // workbook's own baseline entry does not itself outlive it. Diffing "previous vs. current" on
    // every RebuildFormulaDependencies call turns sheet removal into something this shared choke
    // point detects and cleans up on its own, instead of requiring every command/service call site
    // that can remove a sheet to separately remember to report it.
    private readonly ConditionalWeakTable<Workbook, HashSet<SheetId>> _lastKnownSheetIdsByWorkbook = new();

    /// <summary>
    /// Cells currently classified as part of a non-iterative circular reference by the most
    /// recent recalculation(s). See <see cref="_cyclicCells"/> for lifecycle details.
    /// </summary>
    public IReadOnlyCollection<CellAddress> CyclicCells => _cyclicCells;

    /// <summary>Test-only: current volatile-cell registration count (see <see cref="_volatileCells"/>).</summary>
    internal int VolatileCellCountForTests => _volatileCells.Count;

    /// <summary>Test-only: current dependency-plan cache dictionary entry count.</summary>
    internal int DependencyPlanCacheCountForTests => _dependencyPlanCache.Count;

    /// <summary>Test-only: current anchor-&gt;dependents forward-map entry count (see <see cref="_anchorArraySpillDependents"/>).</summary>
    internal int AnchorArraySpillDependentsCountForTests => _anchorArraySpillDependents.Count;

    /// <summary>Test-only: current dependent-&gt;anchors reverse-map entry count (see <see cref="_anchorArraySpillDependentAnchors"/>).</summary>
    internal int AnchorArraySpillDependentAnchorsCountForTests => _anchorArraySpillDependentAnchors.Count;

    /// <summary>Test-only: current dependency-plan cache FIFO order queue entry count.</summary>
    internal int DependencyPlanCacheOrderCountForTests => _dependencyPlanCacheOrder.Count;

    /// <summary>Test-only: how many of the FIFO order queue's entries belong to <paramref name="sheetId"/>.</summary>
    internal int DependencyPlanCacheOrderCountForSheetForTests(SheetId sheetId) =>
        _dependencyPlanCacheOrder.Count(key => key.SheetId.Equals(sheetId));

    public RecalcEngine(DependencyGraph graph, FormulaEvaluator evaluator)
    {
        _graph = graph;
        _evaluator = evaluator;
    }

    /// <summary>
    /// Recalculate all cells affected by changes to the given cells.
    /// Returns a report of what was recalculated.
    /// </summary>
    /// <param name="skipDataTableBodyCells">
    /// R118-calc-except-data-tables: when true, any formula cell that falls inside a registered
    /// What-If Analysis Data Table's result body (<see cref="Sheet.TryGetDataTableRange"/>) is left
    /// exactly as it was instead of being evaluated -- mirroring how <paramref name="restrictWritesToSheet"/>
    /// (below) lets the topological order still cross into cells it must not mutate. This is how
    /// <see cref="Model.WorkbookCalculationMode.AutomaticExceptDataTables"/> ("Automatic Except for
    /// Data Tables") is honored: everything else recalculates live, but a Data Table's results stay
    /// frozen until an explicit <see cref="RecalculateAllFormulas"/> (F9) or
    /// <see cref="RecalculateSheetFormulas"/> (Shift+F9) call, both of which never pass this flag so
    /// they always force a fresh Data Table result. Defaults to false so every other caller (F9,
    /// Shift+F9, Goal Seek, Scenario Summary, and ordinary plain-Automatic-mode recalculation) keeps
    /// computing Data Tables exactly as before.
    /// </param>
    /// <param name="includeVolatileCells">
    /// R149-formula-volatility-manual-mode-fresh-formula-recalc: when false, this pass excludes
    /// every registered volatile cell (NOW/TODAY/RAND/OFFSET/INDIRECT/...) and their dependents
    /// from the traversal entirely, evaluating ONLY <paramref name="changedCells"/> (and their
    /// downstream dependents). Defaults to true so every ordinary caller (Automatic-mode edits,
    /// F9, Shift+F9, Goal Seek, Undo/Redo) keeps re-rolling volatile cells exactly as before --
    /// this exists solely for WorkbookCellEditService.RecalculateFreshlyEnteredFormulasOnce, whose
    /// whole contract in Manual calculation mode is to compute ONLY the just-typed formula cell(s)
    /// themselves and nothing else in the workbook (see that method's doc comment). Without this,
    /// that single call unconditionally swept every pre-existing volatile cell (and everything
    /// downstream of one) into the same pass, silently re-rolling e.g. a RAND()/NOW() cell the
    /// instant the user typed an unrelated brand-new formula anywhere else on the sheet, even
    /// though Manual mode defers all other recalculation until the next F9.
    /// </param>
    public RecalcReport Recalculate(Workbook workbook, IReadOnlyList<CellAddress> changedCells, bool skipDataTableBodyCells = false, bool includeVolatileCells = true) =>
        Recalculate(workbook, changedCells, resolveSpillDependents: true, skipDataTableBodyCells: skipDataTableBodyCells, includeVolatileCells: includeVolatileCells);

    private RecalcReport Recalculate(
        Workbook workbook,
        IReadOnlyList<CellAddress> changedCells,
        bool resolveSpillDependents,
        SheetId? restrictWritesToSheet = null,
        bool skipDataTableBodyCells = false,
        bool includeVolatileCells = true)
    {
        // See the includeVolatileCells parameter doc above. Every reference to the workbook's
        // registered volatile-cell set within this pass goes through this local instead of the
        // raw _volatileCells field, so a caller that opts out (currently only
        // RecalculateFreshlyEnteredFormulasOnce) sees an empty set everywhere in this method
        // without permanently clearing (or otherwise disturbing) the real registration, which
        // must survive for the NEXT ordinary (includeVolatileCells: true) recalculation pass.
        var volatileCellsForPass = includeVolatileCells ? _volatileCells : EmptyDependencyCells;
        // R124-calc-spill-member-write-anchor-recalc: CommandGuards.RejectIfSplitsArray's
        // allowDynamicSpillMemberWrite branch (see its doc comment) lets EditCellsCommand/
        // ClearContentsCommand/the paste family/the fill family write a literal value directly
        // into a non-anchor member of a LIVE dynamic-array spill, on the claim that "the owning
        // anchor's next recalculation naturally detects the now-occupied cell" (via
        // Sheet.IsSpillBlocked). That claim is only true if the anchor's formula actually gets
        // re-evaluated -- but a typical spilling formula (e.g. "=SEQUENCE(3,1)") has no cell
        // references at all, so there is no dependency-graph edge from the freshly-written member
        // address back to the anchor, and every caller's CommandOutcome.AffectedCells reports only
        // the member address that was actually written (see e.g. EditCellsCommand's constructor).
        // Without this expansion the anchor is never enqueued: it keeps showing its stale
        // pre-write value (and Sheet's _spillAnchors keeps recording the old, now-wrong, extent)
        // until an unrelated edit happens to dirty the anchor's real precedents, or the user
        // explicitly presses F9/Shift+F9 -- unlike real Excel, where typing over a spill member
        // collapses the anchor to #SPILL! in the very same keystroke. Fix at this single choke
        // point (every Recalculate call, forward apply AND Undo/Redo, funnels through here) rather
        // than touching every command that can write into a spill member: for each changed address
        // that is a non-anchor member of a still-registered live spill, add the owning anchor to
        // the changed set so it is revisited (and IsSpillBlocked re-run) in this very pass. A
        // change that targets the anchor itself, or a sheet with no spills at all
        // (Sheet.HasArrayOrSpillMembers), is untouched -- this is a no-op for the overwhelming
        // majority of edits.
        changedCells = ExpandChangedCellsWithSpillMemberAnchors(workbook, changedCells);

        if (changedCells.Count == 0 &&
            volatileCellsForPass.Count == 0 &&
            // An active iterative circular-reference group must re-iterate every pass (see
            // _activeIterativeCyclicCells) even when nothing else in the workbook changed -- e.g.
            // a bare F9 with no other dirty cells (R92-calc-iterative-convergence-5-1).
            _activeIterativeCyclicCells.Count == 0 &&
            // Mirror the fallthrough guard below (P73): an edit with no changed cells at all (e.g.
            // Unmerge, which never populates CommandOutcome.AffectedCells) must still reach the
            // spill-blocked-anchor retry pass further down instead of short-circuiting here first.
            !(resolveSpillDependents && _spillBlockedAnchors.Count > 0))
        {
            return EmptyReport;
        }

        var changedFormulaCells = CollectChangedFormulaCells(workbook, changedCells);

        // Register dependencies for freshly-edited formula cells before computing the recalc
        // order. Otherwise a formula that now references another cell dirtied in the same batch
        // has no edge in the graph yet, and the topological sort can run it before that precedent.
        EnsureChangedFormulaDependenciesRegistered(workbook, changedFormulaCells);

        // Clear stale graph entries for any changed address that is no longer a formula (e.g. a
        // row/column/cell insert-delete relocated its formula's Cell object elsewhere, leaving this
        // address blank -- R100). This must scan the FULL changedCells list, not changedFormulaCells,
        // since CollectChangedFormulaCells deliberately drops now-blank addresses.
        ClearVacatedFormulaDependencies(workbook, changedCells);

        // Include volatile cells in the dependency traversal so their dependents appear in the plan
        var changedForTraversal = BuildChangedSetForTraversal(changedCells, includeVolatileCells);
        var plan = _graph.GetRecalcOrder(changedForTraversal);
        if (plan.OrderedCells.Count == 0 &&
            plan.CyclicCells.Count == 0 &&
            volatileCellsForPass.Count == 0 &&
            changedFormulaCells is null &&
            // A cleared/edited cell that was blocking a #SPILL! anchor has no dependency-graph edge
            // back to that anchor (P73), so the traversal above is empty even though the anchor now
            // needs to re-spill. Fall through to the spill-anchor retry pass below instead of
            // short-circuiting here.
            !(resolveSpillDependents && _spillBlockedAnchors.Count > 0))
        {
            return EmptyReport;
        }

        var recalculatedCount = 0;
        var singleRecalculated = default(CellAddress);
        List<CellAddress>? recalculated = null;
        List<(CellAddress Cell, string Error)>? errors = null;
        List<CellAddress>? cyclicCells = null;
        HashSet<CellAddress>? seenCyclicCells = null;
        // Set whenever a spill range is created, resized, or cleared this pass, so formula cells
        // that read spill-target cells get a follow-up re-evaluation (those targets are not formula
        // cells and have no node in the dependency graph, so the topo sort cannot order them).
        var spillTargetsMayHaveChanged = false;
        // Cells vacated by a spill that shrank or fully cleared this pass. Once ClearSpillRange
        // (or the shrinking SetSpillRange) removes them from Sheet's spill-value table, the normal
        // spill-target follow-up scan (which only enumerates CURRENTLY spilled cells) can no longer
        // find them, so a formula that directly references one (e.g. B1=A2+1, where A2 was a spill
        // member that just went blank) would otherwise stay stale forever. Captured here and fed
        // into ResolveSpillTargetDependentsFixpoint so their direct dependents get one more pass.
        List<CellAddress>? vacatedSpillCells = null;

        // Mark cyclic cells with error, or run iterative calc if enabled.
        // seenIterativeCells tracks which cyclic cells have already been handled by the iterative
        // loop so that a second plan (evaluationPlan below) does not re-run them.
        HashSet<CellAddress>? seenIterativeCells = null;
        if (plan.CyclicCells.Count > 0)
        {
            if (workbook.IterativeCalculation)
            {
                seenIterativeCells = [.. plan.CyclicCells];
                RunIterativeCalc(workbook, plan.CyclicCells, ref recalculatedCount, ref singleRecalculated, ref recalculated, ref errors, ref spillTargetsMayHaveChanged, ref vacatedSpillCells, restrictWritesToSheet);
                foreach (var cyclic in plan.CyclicCells)
                    _activeIterativeCyclicCells.Add(cyclic);
            }
            else
            {
                foreach (var cyclic in plan.CyclicCells)
                {
                    AddCyclicCell(workbook, cyclic, ref cyclicCells, ref seenCyclicCells, ref errors, restrictWritesToSheet);
                    _activeIterativeCyclicCells.Remove(cyclic);
                }
            }
        }

        var evaluationPlan = plan;
        IReadOnlyCollection<CellAddress>? directFormulaRoots = null;
        if (volatileCellsForPass.Count > 0 || changedFormulaCells is not null)
        {
            if (CanEvaluateChangedFormulaRootsDirectly(plan, changedFormulaCells, volatileCellsForPass.Count))
            {
                directFormulaRoots = changedFormulaCells!.Count == 1
                    ? changedFormulaCells
                    : new HashSet<CellAddress>(changedFormulaCells);
            }
            else
            {
                // Directly-changed formula cells, volatile cells, and downstream dependents
                // share one dirty set. Topologically order that set so changed formula roots
                // do not run before dirty formula precedents.
                var dirtyCells = new HashSet<CellAddress>(
                    plan.OrderedCells.Count + volatileCellsForPass.Count + (changedFormulaCells?.Count ?? 0));

                if (changedFormulaCells is not null)
                {
                    foreach (var addr in changedFormulaCells)
                        dirtyCells.Add(addr);
                }

                foreach (var addr in volatileCellsForPass)
                    dirtyCells.Add(addr);

                foreach (var addr in plan.OrderedCells)
                    dirtyCells.Add(addr);

                if (plan.CyclicCells.Count > 0)
                {
                    // A cell already marked cyclic above (its #CIRCULAR! error, or its
                    // iterative-converged value, was just recorded) must not be resurrected into
                    // this second, volatile-driven pass: GetEvaluationOrder below is scoped to only
                    // dirtyCells, so if a cyclic cell's partner isn't also in this restricted set,
                    // the edge back to that partner is invisible and the cell looks acyclic -
                    // letting it be freshly (and wrongly) re-evaluated, e.g. clobbering #CIRCULAR!
                    // with a real number for a volatile formula that is also part of a circular
                    // reference (A1="=IFERROR(B1,0)+NOW()", B1="=A1").
                    foreach (var cyclic in plan.CyclicCells)
                        dirtyCells.Remove(cyclic);
                }

                // Volatile functions (OFFSET/INDIRECT/CELL/...) can dynamically read a cell that has
                // no registered dependency edge back to them (only their static argument cells get an
                // edge - see CollectReferences' FunctionCallNode case). Left unordered relative to an
                // unrelated dirty cell they dynamically read, Kahn's ready-queue (backed by HashSet
                // enumeration) picks an arbitrary order between two cells that both reach in-degree 0
                // at the same time - so the volatile cell can run first and observe that cell's
                // pre-edit value for this pass (P78; self-heals only next recalc since volatiles
                // always re-run). GetEvaluationOrder's deprioritized-tie-break keeps every REAL
                // dependency edge intact (a non-volatile cell that statically references a volatile
                // one is still correctly ordered after it) while making volatile cells lose every
                // ready-queue tie against a non-volatile cell, so by the time a volatile cell
                // evaluates, every unrelated same-pass dirty cell has already settled.
                evaluationPlan = _graph.GetEvaluationOrder(dirtyCells, deprioritized: volatileCellsForPass);

                if (evaluationPlan.CyclicCells.Count > 0)
                {
                    if (workbook.IterativeCalculation)
                    {
                        // Only run iterative calc for cells not already handled above.
                        List<CellAddress>? newCyclicCells = null;
                        foreach (var cyclic in evaluationPlan.CyclicCells)
                        {
                            if (seenIterativeCells is null || !seenIterativeCells.Contains(cyclic))
                            {
                                newCyclicCells ??= [];
                                newCyclicCells.Add(cyclic);
                            }
                        }
                        if (newCyclicCells is not null)
                        {
                            RunIterativeCalc(workbook, newCyclicCells, ref recalculatedCount, ref singleRecalculated, ref recalculated, ref errors, ref spillTargetsMayHaveChanged, ref vacatedSpillCells, restrictWritesToSheet);
                            foreach (var cyclic in newCyclicCells)
                                _activeIterativeCyclicCells.Add(cyclic);
                        }
                    }
                    else
                    {
                        foreach (var cyclic in evaluationPlan.CyclicCells)
                        {
                            AddCyclicCell(workbook, cyclic, ref cyclicCells, ref seenCyclicCells, ref errors, restrictWritesToSheet);
                            _activeIterativeCyclicCells.Remove(cyclic);
                        }
                    }
                }
            }
        }

        if (_activeIterativeCyclicCells.Count > 0)
        {
            // Drop any previously-tracked iterative cyclic cell that this pass's own fresh
            // GetRecalcOrder/GetEvaluationOrder traversal (which always re-seeds every entry here
            // into changedForTraversal, mirroring _volatileCells) no longer reports as cyclic -- the
            // formula was edited to no longer self-reference, so it ages out instead of forcing an
            // extra RunIterativeCalc pass on it forever.
            var stillCyclic = new HashSet<CellAddress>(plan.CyclicCells);
            if (!ReferenceEquals(evaluationPlan, plan))
            {
                foreach (var addr in evaluationPlan.CyclicCells)
                    stillCyclic.Add(addr);
            }
            _activeIterativeCyclicCells.RemoveWhere(addr => !stillCyclic.Contains(addr));
        }

        foreach (var addr in directFormulaRoots ?? evaluationPlan.OrderedCells)
        {
            // Shift+F9 "Calculate Sheet" (RecalculateSheetFormulas) restricts writes to the target
            // sheet: the dependency traversal above still crosses sheet boundaries to keep the
            // topological order correct, but a cross-sheet dependent must not be mutated here.
            // Leave it exactly as it was -- Excel shows it stale until its own sheet is calculated
            // (a later Shift+F9 on that sheet, or a full F9, which always re-evaluates every
            // formula cell in the workbook regardless of this pass).
            if (restrictWritesToSheet is { } restrictSheet0 && !addr.Sheet.Equals(restrictSheet0))
                continue;

            var sheet = workbook.GetSheet(addr.Sheet);
            if (sheet is null) continue;

            // R118-calc-except-data-tables: leave a Data Table result cell exactly as it was rather
            // than evaluating it -- see the skipDataTableBodyCells parameter doc above. Cheaply
            // skipped via HasDataTableRanges for the overwhelming majority of sheets that have never
            // created a Data Table.
            if (skipDataTableBodyCells && sheet.HasDataTableRanges && sheet.TryGetDataTableRange(addr, out _))
                continue;

            var cell = sheet.GetCell(addr);
            if (cell is null || !cell.HasFormula) continue;

            // This cell is about to evaluate through the ordinary (non-cyclic) path, so it is no
            // longer part of any circular reference even if a prior pass had classified it that
            // way (e.g. its formula was edited to break the cycle). Keep the persisted cyclic-cell
            // set (see field comment) from going stale.
            if (_cyclicCells.Count > 0)
                _cyclicCells.Remove(addr);

            // Did this cell own a spill before re-evaluation? If so, any outcome that does not
            // re-establish the same spill clears its target cells and downstream readers go stale.
            uint priorSpillRows = 0, priorSpillCols = 0;
            var hadSpill = sheet.HasSpillValues &&
                sheet.TryGetSpillExtent(addr, out priorSpillRows, out priorSpillCols);

            try
            {
                // Use cached AST to avoid re-running Lexer+Parser on every recalc pass.
                if (cell.CachedAst is not FormulaNode cachedAst)
                {
                    cachedAst = FormulaEvaluator.ParseFormula(cell.FormulaText!);
                    cell.CachedAst = cachedAst;
                    RegisterFormulaDependencies(addr, cachedAst, addr.Sheet, workbook);
                }
                // Dynamic-array formulas spill: a top-level bare range reference (e.g. =A1:C3) must
                // return the whole range so it spills, rather than collapsing to its top-left cell
                // via implicit intersection (which is the legacy/Implicit mode behaviour).
                var result = cell.ArrayMode == FormulaArrayMode.Dynamic
                    ? _evaluator.EvaluateSpilling(cachedAst, sheet, workbook, addr)
                    : _evaluator.Evaluate(cachedAst, sheet, workbook, addr);

                if (result is RangeValue implicitRange && cell.ArrayMode == FormulaArrayMode.Implicit)
                {
                    // Legacy implicit intersection (@): resolve the range to the single cell that shares
                    // this formula's row/column instead of spilling.
                    sheet.ClearSpillRange(addr);
                    if (hadSpill)
                    {
                        spillTargetsMayHaveChanged = true;
                        CaptureVacatedSpillCells(addr, priorSpillRows, priorSpillCols, 0, 0, ref vacatedSpillCells);
                    }
                    cell.Value = ImplicitIntersection.Resolve(cachedAst, implicitRange, addr.Row, addr.Col);
                    RoundPrecisionAsDisplayed(workbook, cell);
                    _spillBlockedAnchors.Remove(addr);
                    AddRecalculatedCell(ref recalculatedCount, ref singleRecalculated, ref recalculated, addr);
                }
                else if (result is RangeValue rv)
                {
                    sheet.ClearSpillRange(addr);
                    if (cell.LegacyArrayRows > 0)
                    {
                        // Legacy multi-cell CSE array formula (Ctrl+Shift+Enter; <f t="array" ref="...">):
                        // confined to its originally declared ref extent. Unlike a modern dynamic-array
                        // formula it never negotiates with neighboring cells -- no #SPILL!, and it never
                        // touches a cell outside the declared box. A natural result larger than the
                        // declared extent has its extra values silently dropped; a natural result smaller
                        // than the declared extent leaves the uncovered declared cells as #N/A. See
                        // R80-formula-array-cse-5-2.
                        var declaredRows = (int)cell.LegacyArrayRows;
                        var declaredCols = (int)cell.LegacyArrayCols;
                        if (declaredRows != rv.RowCount || declaredCols != rv.ColCount)
                        {
                            var confinedCells = new ScalarValue[declaredRows, declaredCols];
                            for (var r = 0; r < declaredRows; r++)
                                for (var c = 0; c < declaredCols; c++)
                                    confinedCells[r, c] = r < rv.RowCount && c < rv.ColCount
                                        ? rv.Cells[r, c]
                                        : ErrorValue.NA;
                            rv = new RangeValue(confinedCells);
                        }
                        cell.Value = rv.Cells[0, 0];
                        RoundPrecisionAsDisplayed(workbook, cell);
                        sheet.SetSpillRange(addr, rv);
                        spillTargetsMayHaveChanged = true;
                        if (hadSpill)
                            CaptureVacatedSpillCells(addr, priorSpillRows, priorSpillCols, rv.RowCount, rv.ColCount, ref vacatedSpillCells);
                        _spillBlockedAnchors.Remove(addr);
                        AddRecalculatedCell(ref recalculatedCount, ref singleRecalculated, ref recalculated, addr);
                    }
                    else if (sheet.IsSpillBlocked(addr, rv.RowCount, rv.ColCount))
                    {
                        cell.Value = ErrorValue.Spill;
                        if (hadSpill)
                        {
                            spillTargetsMayHaveChanged = true;
                            CaptureVacatedSpillCells(addr, priorSpillRows, priorSpillCols, 0, 0, ref vacatedSpillCells);
                        }
                        // Remember this anchor so a later recalc (triggered by an edit that clears
                        // the blocking cell, which has no dependency-graph edge back to us) retries
                        // it instead of leaving a stale #SPILL! forever. See field comment.
                        _spillBlockedAnchors.Add(addr);
                        AddError(ref errors, addr, "#SPILL!");
                    }
                    else
                    {
                        cell.Value = rv.Cells[0, 0];
                        RoundPrecisionAsDisplayed(workbook, cell);
                        sheet.SetSpillRange(addr, rv);
                        spillTargetsMayHaveChanged = true;
                        // A shrinking respill (e.g. 5 rows -> 3 rows) vacates the rows/cols beyond
                        // the new extent; those cells drop out of Sheet's spill-value table and the
                        // normal follow-up scan (EnumerateSpillTargetCells) can no longer see them.
                        if (hadSpill)
                            CaptureVacatedSpillCells(addr, priorSpillRows, priorSpillCols, rv.RowCount, rv.ColCount, ref vacatedSpillCells);
                        _spillBlockedAnchors.Remove(addr);
                        AddRecalculatedCell(ref recalculatedCount, ref singleRecalculated, ref recalculated, addr);
                    }
                }
                else if (cell.LegacyArrayRows > 0 && !ReferenceEquals(result, ErrorValue.RuntimeCircularSelfReference))
                {
                    // Legacy multi-cell CSE array formula (Ctrl+Shift+Enter; <f t="array" ref="...">)
                    // whose natural result is a plain SCALAR (e.g. {=SUM(A1:A5)} entered over a
                    // declared 2x1 block). Excel fills every cell of the declared block with this
                    // same scalar value, and the whole block keeps behaving as one CSE array for
                    // editing purposes. Replicate the scalar across the declared extent and route it
                    // through SetSpillRange -- exactly like the natural-RangeValue branch above --
                    // so every member cell is written and TryGetArrayExtent keeps recognizing the
                    // whole declared block after this recalc (its provisional load-time membership
                    // is torn down the moment ClearSpillRange runs below). See R80-formula-array-cse-5-2
                    // and R141 cse-scalar-result-not-replicated.
                    var declaredRows = (int)cell.LegacyArrayRows;
                    var declaredCols = (int)cell.LegacyArrayCols;
                    var replicated = new ScalarValue[declaredRows, declaredCols];
                    for (var r = 0; r < declaredRows; r++)
                        for (var c = 0; c < declaredCols; c++)
                            replicated[r, c] = result;
                    var replicatedRv = new RangeValue(replicated);
                    sheet.ClearSpillRange(addr);
                    cell.Value = replicatedRv.Cells[0, 0];
                    RoundPrecisionAsDisplayed(workbook, cell);
                    sheet.SetSpillRange(addr, replicatedRv);
                    spillTargetsMayHaveChanged = true;
                    if (hadSpill)
                        CaptureVacatedSpillCells(addr, priorSpillRows, priorSpillCols, replicatedRv.RowCount, replicatedRv.ColCount, ref vacatedSpillCells);
                    _spillBlockedAnchors.Remove(addr);
                    AddRecalculatedCell(ref recalculatedCount, ref singleRecalculated, ref recalculated, addr);
                }
                else
                {
                    sheet.ClearSpillRange(addr);
                    if (hadSpill)
                    {
                        spillTargetsMayHaveChanged = true;
                        CaptureVacatedSpillCells(addr, priorSpillRows, priorSpillCols, 0, 0, ref vacatedSpillCells);
                    }

                    // R86-calc-volatile-circular-5-2: this cell reached its own address only
                    // through INDIRECT's dynamic string argument (e.g. A1=INDIRECT("A1")+1) --
                    // invisible to the static dependency graph, so plan.CyclicCells never included
                    // it (see BuiltInFunctions.Lookup.Indirect.cs's IsIndirectSelfReference, which
                    // produced this sentinel).
                    if (ReferenceEquals(result, ErrorValue.RuntimeCircularSelfReference))
                    {
                        // R124-calc-indirect-iterative: with Iterative Calculation ON, Excel
                        // resolves this exactly like a statically-detected self-loop (A1=A1+1) --
                        // fixed-point iterate up to MaxCalculationIterations/MaxCalculationChange
                        // instead of fabricating #CIRCULAR!. Route it through the SAME per-cell
                        // iteration loop a statically-detected cycle uses (RunIterativeCalc),
                        // which passes isIterativeCalculationPass so INDIRECT's self-reference
                        // guard is suppressed for those re-evaluations and reads the cell's own
                        // previous-iterate value instead of re-emitting this sentinel every pass.
                        // Track it in _activeIterativeCyclicCells the same way a statically-cyclic
                        // cell is, mirroring the plan.CyclicCells routing above.
                        if (workbook.IterativeCalculation)
                        {
                            RunIterativeCalc(workbook, [addr], ref recalculatedCount, ref singleRecalculated, ref recalculated, ref errors, ref spillTargetsMayHaveChanged, ref vacatedSpillCells, restrictWritesToSheet);
                            _activeIterativeCyclicCells.Add(addr);
                        }
                        else
                        {
                            // Iterative calculation is off: route it through the same non-iterative
                            // circular-reference handling AddCyclicCell gives a statically-detected
                            // cycle (seed to 0, record "#CIRCULAR!", track in _cyclicCells) instead
                            // of storing the meaningless dynamic value INDIRECT would otherwise have
                            // read back.
                            AddCyclicCell(workbook, addr, ref cyclicCells, ref seenCyclicCells, ref errors, restrictWritesToSheet);
                            _activeIterativeCyclicCells.Remove(addr);
                        }

                        _spillBlockedAnchors.Remove(addr);
                        continue;
                    }

                    cell.Value = result;
                    RoundPrecisionAsDisplayed(workbook, cell);
                    _spillBlockedAnchors.Remove(addr);
                    AddRecalculatedCell(ref recalculatedCount, ref singleRecalculated, ref recalculated, addr);
                }
            }
            catch (FormulaParseException)
            {
                // Distinguish a genuine PARSE-time failure (the Lexer/Parser itself threw, so
                // cell.CachedAst never got (re)assigned above and is still null) from an EVAL-time
                // throw against an already-successfully-parsed AST — e.g. SheetEvalContext.GetCellValue
                // throwing because an external-workbook reference isn't cached yet (see
                // FormulaEvaluator.Contexts.cs). Only the former should wipe the cached AST and
                // dependency edges: a formula like "=A1+[1]Sheet1!C1" parses fine since r48's Lexer
                // fix (RegisterFormulaDependencies above already registered the local A1 edge) even
                // though evaluating the uncached external C1 reference throws — clearing the AST and
                // dependencies here would silently drop that just-registered local edge, so a later
                // edit to A1 would no longer mark this cell dirty.
                if (cell.CachedAst is not null)
                {
                    // Parsed fine; the throw came from evaluation (an uncached external-workbook
                    // reference). Preserve the cached AST, the dependency edges just registered for
                    // it, and the cell's last-known value/spill instead of treating this like an
                    // unparseable formula.
                    continue;
                }

                ClearFormulaDependencies(addr);

                // Excel keeps an external-workbook reference's last-known cached value until the
                // user explicitly updates links — it never blanks it out just from a recalc. Our
                // lexer/parser have no concept of the '[Book.xlsx]Sheet!A1' external-reference
                // syntax (see Lexer's structured-reference handling), so such a formula always
                // fails to parse here even though XlsxFileAdapter loaded a perfectly good cached
                // value for it. Leave that cached value (and any existing spill) alone instead of
                // clobbering it with #VALUE! on every ordinary recalc/Calculate Now.
                if (IsLikelyExternalWorkbookReferenceFormula(cell.FormulaText))
                    continue;

                sheet.ClearSpillRange(addr);
                if (hadSpill)
                {
                    spillTargetsMayHaveChanged = true;
                    CaptureVacatedSpillCells(addr, priorSpillRows, priorSpillCols, 0, 0, ref vacatedSpillCells);
                }
                cell.Value = ErrorValue.Value;
                _spillBlockedAnchors.Remove(addr);
                AddError(ref errors, addr, "#VALUE!");
            }
            catch (FormulaEvalException ex)
            {
                sheet.ClearSpillRange(addr);
                if (hadSpill)
                {
                    spillTargetsMayHaveChanged = true;
                    CaptureVacatedSpillCells(addr, priorSpillRows, priorSpillCols, 0, 0, ref vacatedSpillCells);
                }
                cell.Value = new ErrorValue(ex.ErrorCode);
                _spillBlockedAnchors.Remove(addr);
                AddError(ref errors, addr, ex.ErrorCode);
            }
            catch (Exception)
            {
#if DEBUG
                // In debug/test builds, surface unexpected evaluator exceptions instead of
                // masking them as #VALUE! — a swallowed exception here is a built-in-function bug.
                throw;
#else
                // Release: any unhandled exception from the evaluator (e.g. inverted range,
                // overflow) must not crash the app — surface it as #VALUE! instead.
                sheet.ClearSpillRange(addr);
                if (hadSpill)
                {
                    spillTargetsMayHaveChanged = true;
                    CaptureVacatedSpillCells(addr, priorSpillRows, priorSpillCols, 0, 0, ref vacatedSpillCells);
                }
                cell.Value = ErrorValue.Value;
                _spillBlockedAnchors.Remove(addr);
                AddError(ref errors, addr, "#VALUE!");
#endif
            }

            // addr just (re-)evaluated, so if it is itself the anchor of one or more live
            // ANCHORARRAY(anchor,end) spill-union dependencies, those dependents' registered
            // dependency edges may now be stale (addr's spill extent could have grown, shrunk,
            // appeared, or disappeared). Re-derive them now rather than leaving that a one-time
            // snapshot from first registration. Cheap in the overwhelming common case: the
            // dictionary is empty unless some OTHER formula reads addr via a live spill union.
            if (_anchorArraySpillDependents.Count > 0)
                RefreshAnchorArraySpillDependents(workbook, addr);
        }

        var report = new RecalcReport(
            BuildRecalculatedCells(recalculatedCount, singleRecalculated, recalculated),
            errors ?? EmptyErrors,
            cyclicCells ?? EmptyCells);

        // Follow-up passes: formula cells that read spill-target cells could not be ordered
        // relative to the spill anchor (the targets have no graph node), so re-evaluate them now
        // that all spill ranges are populated. A follow-up pass can itself spill (e.g. a chain of
        // dependent dynamic arrays), which would dirty a further generation of spill-target
        // readers — so this is not capped at one recursion level; it iterates to a fixpoint
        // (bounded by MaxSpillDependentPasses as a sane guard against runaway chains).
        if (resolveSpillDependents && spillTargetsMayHaveChanged)
        {
            ResolveSpillTargetDependentsFixpoint(workbook, ref report, restrictWritesToSheet, vacatedSpillCells, skipDataTableBodyCells);
        }

        // Retry anchors that were showing #SPILL! as of some earlier pass. Excel re-spills the
        // instant the cell(s) blocking a dynamic array are cleared/moved away, even when the edit
        // that cleared them (e.g. deleting a plain value that happened to sit in the spill range)
        // has no dependency-graph edge back to the anchor's formula (its precedents are only its
        // own static references, which never included the blocking cell). Without this, a blocked
        // anchor's #SPILL! is stuck until a full recalc / F9. Gated to the outermost call only —
        // the retry itself runs through the normal Recalculate path (resolveSpillDependents: false)
        // so it cannot re-enter this block and recurse.
        if (resolveSpillDependents && _spillBlockedAnchors.Count > 0)
        {
            List<CellAddress>? retryAnchors = null;
            List<CellAddress>? staleAnchors = null;
            foreach (var anchor in _spillBlockedAnchors)
            {
                var sheet = workbook.GetSheet(anchor.Sheet);
                if (sheet is null)
                {
                    // This anchor's sheet does not belong to `workbook` at all — it belongs to a
                    // different open workbook sharing this engine (see RecalcEngine class remarks).
                    // Neither retry nor evict it: evicting here would strand that other workbook's
                    // dynamic array at a stale #SPILL! forever, since only that workbook's own
                    // Recalculate call would ever revisit it.
                    continue;
                }

                var cell = sheet.GetCell(anchor);
                if (cell is null || !cell.HasFormula || cell.Value is not ErrorValue { Code: "#SPILL!" })
                {
                    // No longer this engine's concern (cell cleared, overwritten, or already
                    // resolved by some other path) — stop tracking it so the set cannot grow
                    // without bound across a long editing session.
                    (staleAnchors ??= []).Add(anchor);
                    continue;
                }

                (retryAnchors ??= []).Add(anchor);
            }

            if (staleAnchors is not null)
            {
                foreach (var stale in staleAnchors)
                    _spillBlockedAnchors.Remove(stale);
            }

            if (retryAnchors is not null)
            {
                var retryReport = Recalculate(workbook, retryAnchors, resolveSpillDependents: false, restrictWritesToSheet: restrictWritesToSheet, skipDataTableBodyCells: skipDataTableBodyCells);
                report = MergeRecalcReports(report, retryReport);

                // If a retried anchor actually re-spilled (its #SPILL! cleared because the blocker
                // was cleared/moved), formulas that read its newly-populated spill-target cells were
                // never ordered relative to it — those targets have no dependency-graph node — so
                // the retry pass above did not refresh them. Run the same spill-target dependent
                // fixpoint used for the main pass so those readers reflect the fresh values in this
                // same recalc, instead of staying stale until the next full recalc/F9.
                var anyReSpilled = retryAnchors.Exists(anchor =>
                {
                    var anchorSheet = workbook.GetSheet(anchor.Sheet);
                    return anchorSheet?.GetCell(anchor)?.Value is not ErrorValue { Code: "#SPILL!" };
                });

                if (anyReSpilled)
                    ResolveSpillTargetDependentsFixpoint(workbook, ref report, restrictWritesToSheet, skipDataTableBodyCells: skipDataTableBodyCells);
            }
        }

        // Excel's "Precision as displayed" option (calcPr/@fullPrecision="0") permanently rounds
        // stored numeric values to the precision shown on screen once a workbook uses it, rather
        // than retaining full internal (~15 significant digit) precision. The main evaluation loop
        // above already rounds each formula cell's own result in place via RoundPrecisionAsDisplayed
        // right after it is assigned (so a dependent evaluated later in this same topological pass,
        // e.g. B1=A1*3, reads A1's already-rounded value, matching Excel's same-pass propagation).
        // This sweep is the catch-all for values set through the paths that loop doesn't cover
        // (iterative/cyclic-calc convergence) -- gated to the top-level (outermost) pass only so
        // recursive spill-dependent follow-ups do not redo it redundantly.
        if (resolveSpillDependents && !workbook.FullPrecision)
            ApplyPrecisionAsDisplayed(workbook, report.RecalculatedCells);

        // Recalculation writes fresh values directly into Cell.Value across the whole pass above
        // (main loop, iterative/cyclic calc, spill-dependent follow-ups, precision rounding) without
        // going through Sheet.SetCell/SetFormula, so none of that bumps Sheet.ContentVersion on its
        // own. Caches keyed on ContentVersion (e.g. the conditional-format evaluation context) must
        // still see those sheets as changed -- notably a same-sheet volatile recalc (F9) and a
        // cross-sheet dependency update (this sheet holds a formula referencing another sheet that
        // just changed) both land here. Gated to the outermost call only: by the time it returns,
        // `report` already reflects every recursive follow-up pass merged in above.
        if (resolveSpillDependents)
            NotifySheetsRecalculated(workbook, report);

        return report;
    }

    /// <summary>
    /// Honor <see cref="Workbook.FullPrecision"/> == false ("Precision as displayed") for the given
    /// set of just-recalculated cells. See <see cref="RoundPrecisionAsDisplayed"/> for the per-cell
    /// rounding rule; this just sweeps every cell the report says was touched this pass, as a
    /// catch-all for values set through a path that does not call it directly.
    /// </summary>
    private static void ApplyPrecisionAsDisplayed(Workbook workbook, IReadOnlyList<CellAddress> recalculatedCells)
    {
        for (var i = 0; i < recalculatedCells.Count; i++)
        {
            var addr = recalculatedCells[i];
            var sheet = workbook.GetSheet(addr.Sheet);
            var cell = sheet?.GetCell(addr);
            if (cell is not null)
                RoundPrecisionAsDisplayed(workbook, cell);
        }
    }

    /// <summary>
    /// Round a single cell's stored value to the decimal-place count its own number format
    /// displays (e.g. "0.00" -> 2 decimals) when <see cref="Workbook.FullPrecision"/> is false
    /// ("Precision as displayed"), matching Excel's permanent-rounding behavior for this option.
    /// Called right after the main evaluation loop assigns a formula cell's fresh result, so a
    /// dependent evaluated later in the same topological pass reads the already-rounded value
    /// (e.g. B1=A1*3 sees A1's rounded 0.33, not its raw ~0.333333333333333). For formats this
    /// simple decimal-placeholder scan can't confidently parse (General, percent, scientific,
    /// fractions, text, or anything containing a date/time letter token or an [Red]-style section
    /// qualifier), falls back to the pre-existing ~15 significant-digit display-ceiling clamp,
    /// which is a safe no-op for ordinary values rather than a mis-rounding of a format we didn't
    /// actually resolve.
    /// </summary>
    private static void RoundPrecisionAsDisplayed(Workbook workbook, Cell cell)
    {
        if (workbook.FullPrecision)
            return;

        if (cell.Value is not NumberValue { Value: var raw } || !double.IsFinite(raw))
            return;

        var numberFormat = workbook.GetStyle(cell.StyleId).NumberFormat;
        cell.Value = new NumberValue(TryGetFixedDecimalPlaces(numberFormat, out var decimals)
            ? Math.Round(raw, Math.Clamp(decimals, 0, 15), MidpointRounding.AwayFromZero)
            : ExcelNumericPrecision.CapSignificantDigits(raw));
    }

    /// <summary>
    /// Resolve the decimal-place count a plain fixed-point number format displays (its first
    /// section's run of '0'/'#'/'?' placeholders right after the decimal point, or 0 for an explicit
    /// integer format with no decimal point at all). Deliberately narrow: bails out (returns false)
    /// on "General"/blank, and on any format containing a letter (date/time tokens, [Red]-style
    /// section color qualifiers, scientific 'E'), '%', '/', or '@' -- those need a full number-format
    /// renderer to resolve correctly, which Core.Calc does not have, so they're left to the existing
    /// significant-digit clamp instead of risking rounding a value to the wrong precision.
    /// </summary>
    private static bool TryGetFixedDecimalPlaces(string? numberFormat, out int decimals)
    {
        decimals = 0;
        if (string.IsNullOrWhiteSpace(numberFormat) || string.Equals(numberFormat, "General", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var c in numberFormat)
        {
            if (char.IsLetter(c) || c is '%' or '/' or '@')
                return false;
        }

        var firstSection = NumberFormatSectionTokenizer.Split(numberFormat)[0];
        var dotIndex = firstSection.IndexOf('.');
        if (dotIndex < 0)
            return true; // Explicit integer format (e.g. "#,##0") -- round to a whole number.

        var count = 0;
        for (var i = dotIndex + 1; i < firstSection.Length && firstSection[i] is '0' or '#' or '?'; i++)
            count++;

        decimals = count;
        return true;
    }

    /// <summary>
    /// Parse and register dependencies for freshly-edited formula cells (those whose AST has not
    /// been cached yet) so the dependency graph reflects their current precedents before the
    /// recalc order is computed.
    /// Also covers a cell that already carries a cached AST but has no graph entry of its own —
    /// e.g. a same-position paste (zero row/col delta), where Cell.Clone() copies the source
    /// cell's CachedAst by reference and Sheet.SetCell never touches the dependency graph. Such a
    /// cell must still be registered under its own address, or it silently goes stale forever.
    /// </summary>
    private void EnsureChangedFormulaDependenciesRegistered(
        Workbook workbook,
        IReadOnlyList<CellAddress>? changedFormulaCells)
    {
        if (changedFormulaCells is null)
            return;

        for (var i = 0; i < changedFormulaCells.Count; i++)
        {
            var addr = changedFormulaCells[i];
            var sheet = workbook.GetSheet(addr.Sheet);
            var cell = sheet?.GetCell(addr);
            if (cell is null || !cell.HasFormula)
                continue;

            if (cell.CachedAst is FormulaNode existingAst)
            {
                // Always (re-)register from the cell's own current AST rather than trusting
                // _graph.HasDependencies(addr) as a proxy for "this address is already correctly
                // registered". A structural command (row/column/cell insert-delete) can relocate a
                // formula's Cell object -- CachedAst intact -- onto an address that was already
                // registered from a DIFFERENT, now-relocated-or-cleared occupant's formula (R100:
                // e.g. inserting rows moves A13's "=B2" out and A10's "=B1" into (sheet,13,A), which
                // already carries a stale {B2} precedent entry from the pre-insert A13). Skipping
                // registration here because *some* entry already exists leaves the graph believing
                // this address still depends on the OLD occupant's precedents forever, so edits to
                // the relocated formula's real precedent never dirty it, while edits to the stale
                // precedent spuriously do.
                // RegisterFormulaDependencies calls DependencyGraph.SetDependencies, which always
                // fully REPLACES (never merges with) whatever precedents were previously registered
                // for this address, so re-registering here is safe even when the existing entry
                // already happened to be correct -- it is a no-op re-derivation in that case.
                RegisterFormulaDependencies(addr, existingAst, addr.Sheet, workbook);
                continue;
            }

            try
            {
                var ast = FormulaEvaluator.ParseFormula(cell.FormulaText!);
                cell.CachedAst = ast;
                RegisterFormulaDependencies(addr, ast, addr.Sheet, workbook);
            }
            catch (FormulaParseException)
            {
                // Invalid text is surfaced as #VALUE! by the evaluation loop; it has no dependencies.
            }
        }
    }

    /// <summary>
    /// Clear stale dependency-graph entries for any changed address that no longer holds a formula
    /// (blank, or now holds a plain value/other content). A structural command (row/column/cell
    /// insert-delete) can move a formula's Cell object off of an address in-place via Sheet.SetCell,
    /// leaving the vacated address blank but its OLD precedents/range-precedents entry untouched in
    /// the graph (R100). <see cref="CollectChangedFormulaCells"/> only surfaces addresses whose LIVE
    /// cell currently has a formula, so a vacated address is otherwise never visited by
    /// <see cref="EnsureChangedFormulaDependenciesRegistered"/> and its phantom edge (pointing at an
    /// address that can never fire again) would persist for the lifetime of the workbook.
    /// </summary>
    private void ClearVacatedFormulaDependencies(Workbook workbook, IReadOnlyList<CellAddress> changedCells)
    {
        for (var i = 0; i < changedCells.Count; i++)
        {
            var addr = changedCells[i];
            var sheet = workbook.GetSheet(addr.Sheet);
            var cell = sheet?.GetCell(addr);
            if (cell is not null && cell.HasFormula)
                continue;

            if (_graph.HasDependencies(addr))
                ClearFormulaDependencies(addr);
        }
    }

    // includeVolatileCells: see the Recalculate(..., includeVolatileCells) parameter doc. When
    // false, this pass's traversal omits _volatileCells entirely -- _activeIterativeCyclicCells is
    // untouched either way, since the opt-out exists only to stop an unrelated formula entry from
    // re-rolling volatile functions, not to change active-iterative-circular-reference handling.
    private IEnumerable<CellAddress> BuildChangedSetForTraversal(
        IReadOnlyList<CellAddress> changedCells, bool includeVolatileCells = true)
    {
        var volatileCount = includeVolatileCells ? _volatileCells.Count : 0;
        if (volatileCount == 0 && _activeIterativeCyclicCells.Count == 0)
            return changedCells;

        var allChanged = new List<CellAddress>(
            changedCells.Count + volatileCount + _activeIterativeCyclicCells.Count);
        foreach (var addr in changedCells)
            allChanged.Add(addr);
        if (includeVolatileCells)
        {
            foreach (var addr in _volatileCells)
                allChanged.Add(addr);
        }
        foreach (var addr in _activeIterativeCyclicCells)
            allChanged.Add(addr);
        return allChanged;
    }

    /// <summary>
    /// See the R124-calc-spill-member-write-anchor-recalc comment at the top of the private
    /// <see cref="Recalculate"/> overload. Returns <paramref name="changedCells"/> unchanged
    /// (same reference, no allocation) whenever none of them is a non-anchor member of a
    /// currently-registered live spill -- the common case for every edit that does not touch a
    /// dynamic array's spill range at all.
    /// </summary>
    private static IReadOnlyList<CellAddress> ExpandChangedCellsWithSpillMemberAnchors(
        Workbook workbook, IReadOnlyList<CellAddress> changedCells)
    {
        if (changedCells.Count == 0)
            return changedCells;

        HashSet<CellAddress>? extraAnchors = null;
        foreach (var addr in changedCells)
        {
            var sheet = workbook.GetSheet(addr.Sheet);
            // Cheap bypass: HasArrayOrSpillMembers is an O(1) count check, so a sheet (or
            // workbook) with no arrays/spills at all never pays for the TryGetArrayExtent scan.
            if (sheet is null || !sheet.HasArrayOrSpillMembers)
                continue;

            // Only a non-anchor MEMBER needs the owning anchor added -- a change that targets the
            // anchor address itself is already a changed formula cell and needs no help.
            if (!sheet.TryGetArrayExtent(addr, out var anchor, out _, out _) || anchor.Equals(addr))
                continue;

            (extraAnchors ??= []).Add(anchor);
        }

        if (extraAnchors is null)
            return changedCells;

        var expanded = new List<CellAddress>(changedCells.Count + extraAnchors.Count);
        expanded.AddRange(changedCells);
        expanded.AddRange(extraAnchors);
        return expanded;
    }

    private static IReadOnlyList<CellAddress>? CollectChangedFormulaCells(Workbook workbook, IReadOnlyList<CellAddress> changedCells)
    {
        if (changedCells.Count == 1)
        {
            var addr = changedCells[0];
            var sheet = workbook.GetSheet(addr.Sheet);
            return sheet?.GetCell(addr)?.HasFormula == true ? changedCells : null;
        }

        List<CellAddress>? formulaCells = null;
        for (var i = 0; i < changedCells.Count; i++)
        {
            var addr = changedCells[i];
            var sheet = workbook.GetSheet(addr.Sheet);
            if (sheet?.GetCell(addr)?.HasFormula == true)
            {
                formulaCells ??= [];
                formulaCells.Add(addr);
            }
        }

        return formulaCells;
    }

    private static void AddRecalculatedCell(
        ref int count,
        ref CellAddress single,
        ref List<CellAddress>? multiple,
        CellAddress address)
    {
        if (count == 0)
        {
            single = address;
            count = 1;
            return;
        }

        multiple ??= new List<CellAddress>(4) { single };
        multiple.Add(address);
        count++;
    }

    private static IReadOnlyList<CellAddress> BuildRecalculatedCells(
        int count,
        CellAddress single,
        List<CellAddress>? multiple) =>
        count switch
        {
            0 => EmptyCells,
            1 => [single],
            _ => multiple!
        };

    private static bool CanEvaluateChangedFormulaRootsDirectly(
        RecalcPlan dependencyPlan,
        IReadOnlyList<CellAddress>? changedFormulaCells,
        int volatileCellCount) =>
        volatileCellCount == 0 &&
        changedFormulaCells is { Count: > 0 } &&
        dependencyPlan.OrderedCells.Count == 0 &&
        dependencyPlan.CyclicCells.Count == 0;

    private void AddCyclicCell(
        Workbook workbook,
        CellAddress cyclic,
        ref List<CellAddress>? cyclicCells,
        ref HashSet<CellAddress>? seenCyclicCells,
        ref List<(CellAddress Cell, string Error)>? errors,
        SheetId? restrictWritesToSheet = null)
    {
        seenCyclicCells ??= [];
        if (!seenCyclicCells.Add(cyclic))
            return;

        // Shift+F9 "Calculate Sheet": a cyclic cell on another sheet must not be flagged/mutated by
        // this pass -- leave it exactly as it was until its own sheet is calculated (see the
        // matching restriction in the main evaluation loop above).
        if (restrictWritesToSheet is { } restrictSheet1 && !cyclic.Sheet.Equals(restrictSheet1))
            return;

        (cyclicCells ??= []).Add(cyclic);
        _cyclicCells.Add(cyclic);

        var sheet = workbook.GetSheet(cyclic.Sheet);
        if (sheet is null) return;

        var cell = sheet.GetCell(cyclic);
        if (cell is null) return;

        // Excel does not fabricate an error value for a circular reference with iterative calc off:
        // it shows a warning dialog/status but the cell itself (and everything downstream of it)
        // computes as 0. Seed the cyclic cell with that same 0 here so IFERROR does not fire and
        // arithmetic reads a real number instead of propagating a manufactured #CIRCULAR! error.
        // The "#CIRCULAR!" warning is still recorded via AddError below for callers that surface a
        // circular-reference notice to the user; only the cell VALUE changes.
        cell.Value = new NumberValue(0);
        AddError(ref errors, cyclic, "#CIRCULAR!");
    }

    private static void AddError(
        ref List<(CellAddress Cell, string Error)>? errors,
        CellAddress cell,
        string error)
    {
        errors ??= [];
        errors.Add((cell, error));
    }

    /// <summary>
    /// Structural shape of an OOXML external-workbook reference: a bracketed workbook token
    /// (filename or link index, e.g. <c>[Book1.xlsx]</c> / <c>[1]</c>) immediately followed — with
    /// only an optional (possibly quoted) sheet-name run in between, and no further brackets — by
    /// the <c>!</c> that sheet-qualifies the reference. Requires the closing <c>]</c> to actually be
    /// present, unlike a bare <c>Contains('[')</c> check, so a merely-malformed formula that happens
    /// to contain an unmatched bracket (e.g. <c>=SUM([</c>) or a structured-table-reference typo
    /// (e.g. <c>=SUM(Table1[Column1</c>) does not get misclassified as a preservable external link.
    /// </summary>
    private static readonly Regex ExternalWorkbookReferencePattern =
        new(@"\[[^\[\]]+\][^!\[\]]*!", RegexOptions.Compiled);

    /// <summary>
    /// Heuristic match for a formula that references another workbook, e.g. <c>[Book1.xlsx]Sheet1!A1</c>
    /// or a defined-name form like <c>[1]Sheet1!A1</c>. The Lexer/Parser have no concept of this OOXML
    /// external-reference syntax (a bracketed workbook token immediately followed by a sheet-qualified
    /// cell/range reference), so parsing such a formula always throws <see cref="FormulaParseException"/>
    /// — even though the file's Excel-computed cached value was loaded correctly and is still perfectly
    /// valid. Unlike <c>XlsxClosedXmlCellMapper.ShouldUseCachedExternalFormulaValue</c> (which only ever
    /// runs after ClosedXML itself has already thrown trying to evaluate a formula it can't handle, so a
    /// bare bracket check there cannot misfire on a merely-malformed formula), this heuristic must be
    /// structural: it runs for ANY unparseable formula, so it needs to actually resemble the external
    /// reference shape rather than just contain a '[' somewhere.
    /// </summary>
    private static bool IsLikelyExternalWorkbookReferenceFormula(string? formulaText) =>
        formulaText is not null && ExternalWorkbookReferencePattern.IsMatch(formulaText);

    /// <summary>
    /// Run a bounded fixed-point iteration over a set of cyclic cells when
    /// <see cref="Workbook.IterativeCalculation"/> is enabled.  Each pass re-evaluates every cell
    /// in <paramref name="cyclicAddresses"/> in the order the dependency graph reported them, using
    /// whatever value each cell currently holds as the previous iterate (Excel seeds unresolved cells
    /// at 0 before the first pass, which is already the default for blank/error cells — we only
    /// reset cells that currently hold <see cref="ErrorValue.Circular"/> so that prior non-iterative
    /// runs do not pollute the seed).  Iteration stops early when the maximum absolute change across
    /// all cells between two consecutive passes is &lt;= <see cref="Workbook.MaxCalculationChange"/>
    /// (default 0.001), or after <see cref="Workbook.MaxCalculationIterations"/> passes (default 100),
    /// whichever comes first.  Non-converging cycles always terminate — they return the last iterate.
    /// </summary>
    private void RunIterativeCalc(
        Workbook workbook,
        IReadOnlyList<CellAddress> cyclicAddresses,
        ref int recalculatedCount,
        ref CellAddress singleRecalculated,
        ref List<CellAddress>? recalculated,
        ref List<(CellAddress Cell, string Error)>? errors,
        ref bool spillTargetsMayHaveChanged,
        ref List<CellAddress>? vacatedSpillCells,
        SheetId? restrictWritesToSheet = null)
    {
        const int DefaultMaxIterations = 100;
        const double DefaultMaxChange = 0.001;

        var maxIterations = workbook.MaxCalculationIterations ?? DefaultMaxIterations;
        // Guard: ensure a sane positive cap even if caller supplies 0 or negative.
        if (maxIterations <= 0) maxIterations = DefaultMaxIterations;

        var maxChange = workbook.MaxCalculationChange ?? DefaultMaxChange;

        // Seed: cells that previously received #CIRCULAR! get reset to 0 (blank) so the first
        // iteration starts from the Excel-compatible seed value, not from a prior error. Shift+F9
        // "Calculate Sheet": skip cells on another sheet entirely -- they must stay untouched (see
        // the matching restriction in the main evaluation loop above).
        for (var i = 0; i < cyclicAddresses.Count; i++)
        {
            if (restrictWritesToSheet is { } restrictSheet2 && !cyclicAddresses[i].Sheet.Equals(restrictSheet2))
                continue;

            var addr = cyclicAddresses[i];
            var seedSheet = workbook.GetSheet(addr.Sheet);
            var seedCell = seedSheet?.GetCell(addr);
            if (seedCell is not null && ReferenceEquals(seedCell.Value, ErrorValue.Circular))
                seedCell.Value = new BlankValue();

            // Iterative calculation resolves this cycle to a converged value, not a fabricated
            // error -- Excel does not flag it via "Formulas with circular references" while
            // iterative calc is on, so drop any stale entry left by a prior non-iterative pass.
            if (_cyclicCells.Count > 0)
                _cyclicCells.Remove(addr);
        }

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var maxAbsChange = 0.0;

            for (var i = 0; i < cyclicAddresses.Count; i++)
            {
                var addr = cyclicAddresses[i];
                if (restrictWritesToSheet is { } restrictSheet3 && !addr.Sheet.Equals(restrictSheet3))
                    continue;

                var sheet = workbook.GetSheet(addr.Sheet);
                if (sheet is null) continue;

                var cell = sheet.GetCell(addr);
                if (cell is null || !cell.HasFormula) continue;

                // Snapshot the value before this eval so we can compute the delta. Keep the whole
                // ScalarValue (not just its numeric projection) — see ComputeConvergenceDelta,
                // which needs the actual prior value to detect a change confined to a
                // Boolean/Text/Error/Blank cell (R92-calc-iterative-convergence-5-2).
                var prevValue = cell.Value;

                // Did this dynamic-array formula own a spill before re-evaluation? Mirrors the
                // "hadSpill" tracking in the ordinary (non-cyclic) evaluation loop above
                // (RecalcEngine.cs:326-330) so a pass that stops spilling (or respills a smaller
                // extent) still vacates its old target cells instead of leaving them stale forever.
                uint priorSpillRows = 0, priorSpillCols = 0;
                var hadSpill = cell.ArrayMode == FormulaArrayMode.Dynamic && sheet.HasSpillValues &&
                    sheet.TryGetSpillExtent(addr, out priorSpillRows, out priorSpillCols);

                try
                {
                    if (cell.CachedAst is not FormulaNode cachedAst)
                    {
                        cachedAst = FormulaEvaluator.ParseFormula(cell.FormulaText!);
                        cell.CachedAst = cachedAst;
                        RegisterFormulaDependencies(addr, cachedAst, addr.Sheet, workbook);
                    }

                    // isIterativeCalculationPass: true -- see R124-calc-indirect-iterative comment
                    // in BuiltInFunctions.Lookup.Indirect.cs. Suppresses INDIRECT's self-reference
                    // sentinel for THIS evaluation so a dynamic self-reference (no static graph
                    // edge, only reachable through RunIterativeCalc via the sentinel-triggered
                    // routing in the main evaluation loop below) reads its own previous-iterate
                    // value like Excel does, instead of re-emitting the sentinel and getting stuck.
                    var result = cell.ArrayMode == FormulaArrayMode.Dynamic
                        ? _evaluator.EvaluateSpilling(cachedAst, sheet, workbook, addr, isIterativeCalculationPass: true)
                        : _evaluator.Evaluate(cachedAst, sheet, workbook, addr, isIterativeCalculationPass: true);

                    if (cell.ArrayMode == FormulaArrayMode.Dynamic && result is RangeValue rv)
                    {
                        // Reconcile the spill table on every pass, mirroring the ordinary
                        // evaluation loop above (RecalcEngine.cs:363-424). Without this, a
                        // dynamic-array formula that becomes part of an active iterative
                        // circular-reference group never calls SetSpillRange/ClearSpillRange, so
                        // its non-anchor spill cells (and anything downstream that reads them)
                        // freeze at whatever they last held instead of converging along with the
                        // anchor. See R94-iterative-dynamic-array-spill.
                        sheet.ClearSpillRange(addr);
                        if (sheet.IsSpillBlocked(addr, rv.RowCount, rv.ColCount))
                        {
                            cell.Value = ErrorValue.Spill;
                            if (hadSpill)
                            {
                                spillTargetsMayHaveChanged = true;
                                CaptureVacatedSpillCells(addr, priorSpillRows, priorSpillCols, 0, 0, ref vacatedSpillCells);
                            }
                            _spillBlockedAnchors.Add(addr);
                        }
                        else
                        {
                            cell.Value = rv.Cells[0, 0];
                            sheet.SetSpillRange(addr, rv);
                            spillTargetsMayHaveChanged = true;
                            if (hadSpill)
                                CaptureVacatedSpillCells(addr, priorSpillRows, priorSpillCols, rv.RowCount, rv.ColCount, ref vacatedSpillCells);
                            _spillBlockedAnchors.Remove(addr);
                        }
                    }
                    else
                    {
                        if (hadSpill)
                        {
                            sheet.ClearSpillRange(addr);
                            spillTargetsMayHaveChanged = true;
                            CaptureVacatedSpillCells(addr, priorSpillRows, priorSpillCols, 0, 0, ref vacatedSpillCells);
                        }

                        cell.Value = result is RangeValue rvNonDynamic ? rvNonDynamic.Cells[0, 0] : result;
                    }
                }
                catch (FormulaParseException)
                {
                    cell.CachedAst = null;
                    ClearFormulaDependencies(addr);

                    // See the matching guard in the main evaluation loop above: an unparseable
                    // external-workbook reference must keep its last-known cached value, not be
                    // blanked to #VALUE! just because it was swept up in an iterative-calc pass.
                    if (!IsLikelyExternalWorkbookReferenceFormula(cell.FormulaText))
                        cell.Value = ErrorValue.Value;
                }
                catch (FormulaEvalException ex)
                {
                    cell.Value = new ErrorValue(ex.ErrorCode);
                }
                catch (Exception)
                {
#if DEBUG
                    throw;
#else
                    cell.Value = ErrorValue.Value;
#endif
                }

                var delta = ComputeConvergenceDelta(prevValue, cell.Value);
                // Guard: NaN/Infinity in the delta (e.g. the formula produced #DIV/0! on this pass,
                // or a Boolean/Text/Error cell's value actually changed — see
                // ComputeConvergenceDelta) must not prevent termination — treat them as "large but
                // not infinite" change so the loop simply runs to maxIterations and returns the
                // last value.
                if (double.IsFinite(delta) && delta > maxAbsChange)
                    maxAbsChange = delta;
            }

            // Converged: every cell changed by <= maxChange on this pass.
            if (maxAbsChange <= maxChange)
                break;
        }

        // Record the cyclic cells as recalculated (not as errors/cyclic). Cells on another sheet
        // were never touched above (restrictWritesToSheet) and must not be reported either.
        for (var i = 0; i < cyclicAddresses.Count; i++)
        {
            if (restrictWritesToSheet is { } restrictSheet4 && !cyclicAddresses[i].Sheet.Equals(restrictSheet4))
                continue;

            AddRecalculatedCell(ref recalculatedCount, ref singleRecalculated, ref recalculated, cyclicAddresses[i]);
        }
    }

    /// <summary>
    /// Extract a finite double from a cell value for use in the iterative-calc convergence check.
    /// Returns 0 for blank/bool/text/error (matching Excel's seed behaviour).
    /// </summary>
    private static double ToNumericForConvergence(ScalarValue? value) =>
        value switch
        {
            NumberValue nv when double.IsFinite(nv.Value) => nv.Value,
            DateTimeValue dv when double.IsFinite(dv.Value) => dv.Value,
            _ => 0.0
        };

    /// <summary>
    /// True when <paramref name="value"/> is a kind <see cref="ToNumericForConvergence"/> maps to
    /// its OWN finite magnitude (a real number/date, not the shared 0.0 sentinel every other kind
    /// collapses to).
    /// </summary>
    private static bool IsFiniteNumericForConvergence(ScalarValue? value) =>
        value switch
        {
            NumberValue nv => double.IsFinite(nv.Value),
            DateTimeValue dv => double.IsFinite(dv.Value),
            _ => false
        };

    /// <summary>
    /// Compute the per-cell convergence delta <see cref="RunIterativeCalc"/> uses for its
    /// per-pass stopping condition. When both the previous and current value are finite
    /// numbers/dates, this is the plain numeric magnitude of change (unchanged from before).
    /// Otherwise — a Boolean, Text, Error, or Blank value on either side (or a pass that flips
    /// between numeric and non-numeric) — <see cref="ToNumericForConvergence"/> would collapse
    /// both sides to the identical 0.0 sentinel regardless of their actual content, making a
    /// cyclic cell whose value keeps changing every pass (e.g. A1=NOT(A1), a Boolean toggle;
    /// or any concatenation/IF/error-based cyclic formula) look falsely converged after a single
    /// pass. Compare the values themselves instead: an actual change reports a large-but-finite
    /// delta (so it counts exactly like a genuine numeric change and keeps the loop running
    /// toward MaxCalculationIterations, matching the numeric oscillator A1=-A1's already-correct
    /// behaviour); no change reports a true 0. See R92-calc-iterative-convergence-5-2.
    /// </summary>
    private static double ComputeConvergenceDelta(ScalarValue? previous, ScalarValue? current)
    {
        if (IsFiniteNumericForConvergence(previous) && IsFiniteNumericForConvergence(current))
            return Math.Abs(ToNumericForConvergence(current) - ToNumericForConvergence(previous));

        return Equals(previous, current) ? 0.0 : double.MaxValue;
    }

    /// <summary>
    /// Extract cell references from a formula AST and register them in the dependency graph.
    /// Call this whenever a formula is set on a cell.
    /// </summary>
    public void RegisterFormulaDependencies(CellAddress formulaCell, FormulaNode ast, SheetId sheetId, FreeX.Core.Model.Workbook? workbook = null)
    {
        // SUBTOTAL/AGGREGATE ignore other SUBTOTAL/AGGREGATE cells (including themselves) within their range,
        // so such a formula must not depend on its own cell — otherwise a totals-style =SUBTOTAL(109,B4:B12)
        // placed inside B4:B12 is wrongly flagged circular. This exclusion is cell-specific, so these
        // formulas bypass the (cell-independent) dependency-plan cache.
        //
        // The bypass check (containsSelfExcludingCall) only asks "could self-exclusion ever apply to
        // this formula" and stays formula-wide/cache-key-shaped. Whether formulaCell should ACTUALLY be
        // excluded is a separate, narrower question answered below by IsCellExcludedBySelfExcludingCall:
        // formulaCell must fall inside one of THAT SPECIFIC SUBTOTAL/AGGREGATE call's own range
        // argument(s) — a self-reference term that is independent of the call's range (e.g.
        // "=B10+SUBTOTAL(9,B1:B9)" at B10) is genuinely circular and must not be excluded just because
        // the formula happens to contain a SUBTOTAL/AGGREGATE call somewhere else in the expression.
        var containsSelfExcludingCall = ContainsSelfExcludingSubtotalOrAggregate(ast);

        var cacheKey = new DependencyPlanCacheKey(ast, sheetId);
        if (!containsSelfExcludingCall && _dependencyPlanCache.TryGetValue(cacheKey, out var cachedPlan))
        {
            ApplyDependencyPlan(formulaCell, cachedPlan);
            // A cached plan is never produced for an ANCHORARRAY live-spill-union formula (see the
            // cacheableForDependencyPlan gate below), so reaching this branch means formulaCell's
            // current formula does not need anchor-array spill tracking. Drop any stale tracking
            // left over from a PRIOR formula at this same address.
            ClearAnchorArraySpillTracking(formulaCell);
            return;
        }

        var refs = new FormulaDependencySet();
        var cacheableForDependencyPlan = true;
        var containsVolatileFunction = CollectReferences(
            ast,
            sheetId,
            formulaCell,
            workbook,
            refs,
            ref cacheableForDependencyPlan,
            namedFormulaStack: null,
            localScopeNames: null);

        if (containsSelfExcludingCall &&
            IsCellExcludedBySelfExcludingCall(ast, sheetId, formulaCell, workbook))
        {
            refs.ExcludeCell(formulaCell);
        }

        _graph.SetDependencies(formulaCell, refs.Cells, refs.Ranges);

        SetVolatileTracking(formulaCell, containsVolatileFunction);
        UpdateAnchorArraySpillTracking(formulaCell, refs.AnchorArrayAnchors);

        if (cacheableForDependencyPlan && !containsSelfExcludingCall)
            AddDependencyPlanToCache(cacheKey, refs, containsVolatileFunction);
    }

    /// <summary>
    /// Refresh <see cref="_anchorArraySpillDependents"/>/<see cref="_anchorArraySpillDependentAnchors"/>
    /// so they reflect exactly the anchor addresses <paramref name="formulaCell"/>'s just-registered
    /// dependencies actually read via a live ANCHORARRAY spill-extent union, dropping any anchors it
    /// no longer reads (e.g. the formula was edited to a different anchor, or to stop using
    /// ANCHORARRAY entirely).
    /// </summary>
    private void UpdateAnchorArraySpillTracking(CellAddress formulaCell, IReadOnlyList<CellAddress> anchors)
    {
        ClearAnchorArraySpillTracking(formulaCell);

        if (anchors.Count == 0)
            return;

        var anchorList = new List<CellAddress>(anchors);
        _anchorArraySpillDependentAnchors[formulaCell] = anchorList;
        foreach (var anchor in anchorList)
        {
            if (!_anchorArraySpillDependents.TryGetValue(anchor, out var set))
            {
                set = [];
                _anchorArraySpillDependents[anchor] = set;
            }
            set.Add(formulaCell);
        }
    }

    /// <summary>Remove formulaCell from every anchor's dependent set it was previously registered under.</summary>
    private void ClearAnchorArraySpillTracking(CellAddress formulaCell)
    {
        if (!_anchorArraySpillDependentAnchors.TryGetValue(formulaCell, out var previousAnchors))
            return;

        foreach (var previousAnchor in previousAnchors)
        {
            if (_anchorArraySpillDependents.TryGetValue(previousAnchor, out var set))
            {
                set.Remove(formulaCell);
                if (set.Count == 0)
                    _anchorArraySpillDependents.Remove(previousAnchor);
            }
        }

        _anchorArraySpillDependentAnchors.Remove(formulaCell);
    }

    /// <summary>
    /// Purge every <see cref="_anchorArraySpillDependents"/>/<see cref="_anchorArraySpillDependentAnchors"/>
    /// entry that references one of <paramref name="sheetIds"/> from either side, so a closed
    /// workbook's (or a since-deleted sheet's) ANCHORARRAY(anchor,end) spill-union bookkeeping does
    /// not leak in this shared, app-lifetime engine forever -- see <see cref="PurgeSheetsFromSharedState"/>.
    /// Two independent scans are needed: a dependent formula cell can live on a retired sheet while
    /// its anchor lives elsewhere (or vice versa), so purging only one direction would leave the
    /// other side's stale CellAddress reachable from a surviving workbook's live recalc.
    /// </summary>
    private void PurgeAnchorArraySpillTrackingForSheets(HashSet<SheetId> sheetIds)
    {
        // Dependents (formula cells) whose own sheet is retired: fully clear their tracking, which
        // also removes them from whichever anchors' forward-map sets they were registered under
        // (including anchors on sheets NOT in sheetIds).
        if (_anchorArraySpillDependentAnchors.Count > 0)
        {
            List<CellAddress>? retiredDependents = null;
            foreach (var dependent in _anchorArraySpillDependentAnchors.Keys)
            {
                if (sheetIds.Contains(dependent.Sheet))
                    (retiredDependents ??= []).Add(dependent);
            }

            if (retiredDependents is not null)
            {
                foreach (var dependent in retiredDependents)
                    ClearAnchorArraySpillTracking(dependent);
            }
        }

        // Anchors whose own sheet is retired: drop the forward-map entry, and scrub this now-gone
        // anchor out of the reverse map's list for any surviving dependent (a dependent can outlive
        // its anchor when only the anchor's sheet -- not the dependent's -- is being purged, e.g.
        // R95-calc-deleted-sheet-leak's partial-workbook Delete Sheet path).
        if (_anchorArraySpillDependents.Count == 0)
            return;

        List<CellAddress>? retiredAnchors = null;
        foreach (var anchor in _anchorArraySpillDependents.Keys)
        {
            if (sheetIds.Contains(anchor.Sheet))
                (retiredAnchors ??= []).Add(anchor);
        }

        if (retiredAnchors is null)
            return;

        foreach (var anchor in retiredAnchors)
        {
            if (!_anchorArraySpillDependents.Remove(anchor, out var dependents))
                continue;

            foreach (var dependent in dependents)
            {
                if (!_anchorArraySpillDependentAnchors.TryGetValue(dependent, out var anchorList))
                    continue;

                anchorList.Remove(anchor);
                if (anchorList.Count == 0)
                    _anchorArraySpillDependentAnchors.Remove(dependent);
            }
        }
    }

    /// <summary>
    /// If <paramref name="anchorCell"/> is itself the anchor of one or more live
    /// ANCHORARRAY(anchor,end) spill-union dependencies (<see cref="_anchorArraySpillDependents"/>),
    /// re-run <see cref="RegisterFormulaDependencies"/> for each of those dependents so their
    /// graph edges are re-derived from this anchor's CURRENT live spill extent. Call this
    /// whenever a cell that might be such an anchor finishes evaluating — cheap in the common
    /// case, since the dictionary lookup is empty for the overwhelming majority of cells.
    /// See R92-calc-array-recalc-order-5-1.
    /// </summary>
    private void RefreshAnchorArraySpillDependents(FreeX.Core.Model.Workbook workbook, CellAddress anchorCell)
    {
        if (!_anchorArraySpillDependents.TryGetValue(anchorCell, out var dependents) || dependents.Count == 0)
            return;

        // Snapshot first: RegisterFormulaDependencies below mutates this same set (and possibly
        // others) via UpdateAnchorArraySpillTracking, which would otherwise invalidate the
        // in-progress enumeration.
        foreach (var dependent in dependents.ToArray())
        {
            var depSheet = workbook.GetSheet(dependent.Sheet);
            var depCell = depSheet?.GetCell(dependent);
            if (depCell?.CachedAst is FormulaNode depAst)
                RegisterFormulaDependencies(dependent, depAst, dependent.Sheet, workbook);
        }
    }

    /// <summary>
    /// Does this formula contain a SUBTOTAL/AGGREGATE call — anywhere in the expression, not only
    /// as the formula's literal root call (e.g. "=1+SUBTOTAL(9,B4:B12)" must be recognized too) —
    /// whose own nested-ignore rule would exclude this formula's cell from a self-referencing
    /// range? This mirrors BuiltInFunctions.Subtotal's ContainsFunctionCall text-scan, which
    /// already applies the same "anywhere in the expression" rule when excluding OTHER cells
    /// within an aggregated range; the self-exclusion check here must agree with it.
    ///
    /// SUBTOTAL always ignores nested SUBTOTAL/AGGREGATE cells (including itself), regardless of
    /// its function_num (1-11 or the hidden-row-aware 101-111). AGGREGATE only does so for
    /// options 0-3 ("...and ignore nested SUBTOTAL and AGGREGATE functions"); options 4-7
    /// explicitly do NOT ignore nested cells, so a self-range AGGREGATE with options 4-7 is
    /// genuinely circular, exactly like any other self-referencing formula. When the options
    /// argument isn't a statically-resolvable literal, we conservatively keep the previous
    /// always-exclude behavior rather than risk a false circular-reference warning.
    /// </summary>
    private static bool ContainsSelfExcludingSubtotalOrAggregate(FormulaNode ast)
    {
        switch (ast)
        {
            case FunctionCallNode func when string.Equals(func.FunctionName, "SUBTOTAL", StringComparison.OrdinalIgnoreCase):
                return true;

            case FunctionCallNode func when string.Equals(func.FunctionName, "AGGREGATE", StringComparison.OrdinalIgnoreCase):
                return AggregateOptionsExcludeNestedSelf(func) || ContainsSelfExcludingCallInAny(func.Arguments);

            case FunctionCallNode func:
                return ContainsSelfExcludingCallInAny(func.Arguments);

            case BinaryOpNode binary:
                return ContainsSelfExcludingSubtotalOrAggregate(binary.Left) ||
                       ContainsSelfExcludingSubtotalOrAggregate(binary.Right);

            case UnaryOpNode unary:
                return ContainsSelfExcludingSubtotalOrAggregate(unary.Operand);

            case ArrayConstantNode array:
                foreach (var row in array.Rows)
                    if (ContainsSelfExcludingCallInAny(row))
                        return true;
                return false;

            default:
                return false;
        }
    }

    private static bool ContainsSelfExcludingCallInAny(IReadOnlyList<FormulaNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (ContainsSelfExcludingSubtotalOrAggregate(nodes[i]))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Is <paramref name="formulaCell"/> actually excluded by a SUBTOTAL/AGGREGATE call somewhere in
    /// this formula? This is narrower than <see cref="ContainsSelfExcludingSubtotalOrAggregate"/>: it
    /// is not enough for the formula to merely CONTAIN a self-excluding call anywhere — the self-cell
    /// must fall inside THAT SPECIFIC call's own range argument(s) (SUBTOTAL's arguments after
    /// function_num; AGGREGATE's arguments after function_num/options). A self-reference term that is
    /// independent of the call's range — e.g. "=B10+SUBTOTAL(9,B1:B9)" at B10, where the bare "B10"
    /// term has nothing to do with SUBTOTAL's nested-ignore rule — is genuinely circular and must not
    /// be excluded just because a SUBTOTAL/AGGREGATE call happens to appear elsewhere in the formula.
    /// </summary>
    private static bool IsCellExcludedBySelfExcludingCall(
        FormulaNode ast,
        SheetId defaultSheetId,
        CellAddress formulaCell,
        FreeX.Core.Model.Workbook? workbook)
    {
        switch (ast)
        {
            case FunctionCallNode func when string.Equals(func.FunctionName, "SUBTOTAL", StringComparison.OrdinalIgnoreCase):
                return RangeArgumentsContainCell(func.Arguments, 1, defaultSheetId, formulaCell, workbook) ||
                       AnyArgumentExcludesCell(func.Arguments, defaultSheetId, formulaCell, workbook);

            case FunctionCallNode func when string.Equals(func.FunctionName, "AGGREGATE", StringComparison.OrdinalIgnoreCase):
                return (AggregateOptionsExcludeNestedSelf(func) &&
                        RangeArgumentsContainCell(func.Arguments, 2, defaultSheetId, formulaCell, workbook)) ||
                       AnyArgumentExcludesCell(func.Arguments, defaultSheetId, formulaCell, workbook);

            case FunctionCallNode func:
                return AnyArgumentExcludesCell(func.Arguments, defaultSheetId, formulaCell, workbook);

            case BinaryOpNode binary:
                return IsCellExcludedBySelfExcludingCall(binary.Left, defaultSheetId, formulaCell, workbook) ||
                       IsCellExcludedBySelfExcludingCall(binary.Right, defaultSheetId, formulaCell, workbook);

            case UnaryOpNode unary:
                return IsCellExcludedBySelfExcludingCall(unary.Operand, defaultSheetId, formulaCell, workbook);

            case ArrayConstantNode array:
                foreach (var row in array.Rows)
                    if (AnyArgumentExcludesCell(row, defaultSheetId, formulaCell, workbook))
                        return true;
                return false;

            default:
                return false;
        }
    }

    private static bool AnyArgumentExcludesCell(
        IReadOnlyList<FormulaNode> nodes,
        SheetId defaultSheetId,
        CellAddress formulaCell,
        FreeX.Core.Model.Workbook? workbook)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (IsCellExcludedBySelfExcludingCall(nodes[i], defaultSheetId, formulaCell, workbook))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Does formulaCell fall inside one of this SUBTOTAL/AGGREGATE call's own range arguments,
    /// starting at <paramref name="startIndex"/> (1 for SUBTOTAL's args-after-function_num; 2 for
    /// AGGREGATE's args-after-function_num/options)? Mirrors the reference-resolution rules
    /// <see cref="CollectReferences"/> applies to the same node shapes, but only for THESE specific
    /// arguments rather than the whole formula.
    /// </summary>
    private static bool RangeArgumentsContainCell(
        IReadOnlyList<FormulaNode> arguments,
        int startIndex,
        SheetId defaultSheetId,
        CellAddress formulaCell,
        FreeX.Core.Model.Workbook? workbook)
    {
        for (var i = startIndex; i < arguments.Count; i++)
        {
            switch (arguments[i])
            {
                case CellRefNode cellRef when cellRef.SheetName is not null:
                {
                    var targetSheet = workbook?.GetSheet(cellRef.SheetName);
                    if (targetSheet is not null &&
                        formulaCell.Equals(new CellAddress(targetSheet.Id, cellRef.Row, cellRef.ColumnNumber)))
                        return true;
                    break;
                }
                case CellRefNode cellRef:
                    if (formulaCell.Equals(new CellAddress(defaultSheetId, cellRef.Row, cellRef.ColumnNumber)))
                        return true;
                    break;

                case RangeRefNode range when range.SheetName is not null:
                {
                    var targetSheet = workbook?.GetSheet(range.SheetName);
                    if (targetSheet is not null && CreateGridRange(targetSheet.Id, range).Contains(formulaCell))
                        return true;
                    break;
                }
                case RangeRefNode range:
                    if (CreateGridRange(defaultSheetId, range).Contains(formulaCell))
                        return true;
                    break;

                case FullColumnRangeRefNode range when range.SheetName is not null:
                {
                    var targetSheet = workbook?.GetSheet(range.SheetName);
                    if (targetSheet is not null && CreateGridRange(targetSheet.Id, range).Contains(formulaCell))
                        return true;
                    break;
                }
                case FullColumnRangeRefNode range:
                    if (CreateGridRange(defaultSheetId, range).Contains(formulaCell))
                        return true;
                    break;

                case FullRowRangeRefNode range when range.SheetName is not null:
                {
                    var targetSheet = workbook?.GetSheet(range.SheetName);
                    if (targetSheet is not null && CreateGridRange(targetSheet.Id, range).Contains(formulaCell))
                        return true;
                    break;
                }
                case FullRowRangeRefNode range:
                    if (CreateGridRange(defaultSheetId, range).Contains(formulaCell))
                        return true;
                    break;

                case NamedRangeNode named:
                    if (workbook is not null &&
                        workbook.TryGetNamedRange(named.Name, defaultSheetId, out var namedRange) &&
                        namedRange.Contains(formulaCell))
                        return true;
                    break;
            }
        }
        return false;
    }

    /// <summary>
    /// True when this AGGREGATE call's own options argument (arg 1) statically resolves to 0-3
    /// (ignore nested SUBTOTAL/AGGREGATE) or can't be statically resolved at all (non-literal —
    /// keep the previous always-exclude behavior). False only when the options literal is
    /// definitively 4-7 (ignore nothing/hidden-rows/errors, but NOT nested calls).
    /// </summary>
    private static bool AggregateOptionsExcludeNestedSelf(FunctionCallNode aggregateCall)
    {
        if (aggregateCall.Arguments.Count < 2)
            return true;

        return !TryGetLiteralIntegerValue(aggregateCall.Arguments[1], out var options) || options <= 3;
    }

    private static bool TryGetLiteralIntegerValue(FormulaNode node, out int value)
    {
        switch (node)
        {
            case NumberNode number:
                value = (int)number.Value;
                return true;
            case UnaryOpNode { Operator: UnaryOperator.Negate } unary when TryGetLiteralIntegerValue(unary.Operand, out var inner):
                value = -inner;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private void ApplyDependencyPlan(CellAddress formulaCell, FormulaDependencyPlan plan)
    {
        _graph.SetDependenciesFromTemplate(formulaCell, plan.Cells, plan.Ranges);
        SetVolatileTracking(formulaCell, plan.ContainsVolatileFunction);
    }

    private void SetVolatileTracking(CellAddress formulaCell, bool containsVolatileFunction)
    {
        if (containsVolatileFunction)
            _volatileCells.Add(formulaCell);
        else
            _volatileCells.Remove(formulaCell);
    }

    private void AddDependencyPlanToCache(
        DependencyPlanCacheKey cacheKey,
        FormulaDependencySet refs,
        bool containsVolatileFunction)
    {
        // Keep dequeuing until an actual dictionary entry is evicted (skipping over any phantom
        // keys left behind by RetireWorkbook) so the cache stays truly bounded even if the FIFO
        // order queue ever falls out of sync with the dictionary.
        while (_dependencyPlanCache.Count >= MaxDependencyPlanCacheEntries &&
               _dependencyPlanCacheOrder.TryDequeue(out var oldest))
        {
            _dependencyPlanCache.Remove(oldest);
        }

        var cells = refs.Cells.Count == 0 ? EmptyDependencyCells : refs.Cells.ToFrozenSet();
        var ranges = refs.Ranges.Count == 0 ? Array.Empty<GridRange>() : refs.Ranges.ToArray();

        _dependencyPlanCache[cacheKey] = new FormulaDependencyPlan(cells, ranges, containsVolatileFunction);
        _dependencyPlanCacheOrder.Enqueue(cacheKey);
    }

    /// <summary>Remove a cell's dependencies (when its formula is cleared).</summary>
    public void ClearFormulaDependencies(CellAddress cell)
    {
        _graph.ClearDependencies(cell);
        _volatileCells.Remove(cell);
        ClearAnchorArraySpillTracking(cell);

        // R111-calc-stale-cyclic-leak: a cell whose formula has just been cleared/replaced (this is
        // the choke point every real caller routes through -- WorkbookCellEditService.
        // UpdateFormulaDependencies for a plain edit or Undo/Redo, and ClearVacatedFormulaDependencies
        // above for a structural relocation) can by definition no longer participate in ANY circular
        // reference. Without this, a cell that was previously classified cyclic and is then edited to
        // a plain value (not a new formula) never runs through the ordinary evaluation loop's own
        // _cyclicCells.Remove (that loop only visits cells that still HasFormula), so the stale
        // #CIRCULAR! entry would otherwise survive forever -- including across F9/full recalc and
        // save/reload -- and keep being reported by FormulaAuditingService's "Formulas with circular
        // references" error-checking rule (WPF Formulas > Error Checking, File > Info's
        // circular-reference count, and the Avalonia Error Checking command all read CyclicCells
        // straight through) even though Excel never flags a non-formula cell this way.
        if (_cyclicCells.Count > 0)
            _cyclicCells.Remove(cell);
        if (_activeIterativeCyclicCells.Count > 0)
            _activeIterativeCyclicCells.Remove(cell);
    }

    /// <summary>Rebuild dependency and volatile-function tracking from every formula in a workbook.</summary>
    public void RebuildFormulaDependencies(Workbook workbook)
    {
        // This engine/graph is a single WPF-host-wide singleton shared by every open workbook (see
        // RecalcEngine class remarks), so rebuilding one workbook's formulas must only clear THIS
        // workbook's own state — a blanket ClearAll()/Clear() here would wipe every other open
        // workbook's dependency edges, volatile-cell tracking, and spill-blocked anchors out from
        // under it. Sheet ids are globally unique, so scoping to this workbook's own sheet ids is
        // both necessary and sufficient.
        var sheetIds = new HashSet<SheetId>(workbook.Sheets.Count);
        foreach (var sheet in workbook.Sheets)
            sheetIds.Add(sheet.Id);

        // R95-calc-deleted-sheet-leak: fold in any sheet this workbook had as of its own last
        // RebuildFormulaDependencies call that has since dropped out of workbook.Sheets (Delete
        // Sheet, or Undo of Add Sheet) -- see _lastKnownSheetIdsByWorkbook's remarks. Purge those
        // fully (graph edges, volatile/spill-blocked tracking, cyclic markers, cached dependency
        // plans) before the below scopes its own clear-and-rebuild strictly to CURRENTLY existing
        // sheets, so a deleted sheet's dependency subgraph does not linger in this shared,
        // app-lifetime graph forever.
        if (_lastKnownSheetIdsByWorkbook.TryGetValue(workbook, out var previousSheetIds))
        {
            HashSet<SheetId>? removedSheetIds = null;
            foreach (var id in previousSheetIds)
            {
                if (!sheetIds.Contains(id))
                    (removedSheetIds ??= []).Add(id);
            }

            if (removedSheetIds is not null)
                PurgeSheetsFromSharedState(removedSheetIds);

            previousSheetIds.Clear();
            previousSheetIds.UnionWith(sheetIds);
        }
        else
        {
            _lastKnownSheetIdsByWorkbook.Add(workbook, new HashSet<SheetId>(sheetIds));
        }

        _graph.ClearForSheets(sheetIds);
        _volatileCells.RemoveWhere(cell => sheetIds.Contains(cell.Sheet));
        _spillBlockedAnchors.RemoveWhere(cell => sheetIds.Contains(cell.Sheet));
        var formulaCellCount = 0;

        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.HasFormulas)
                formulaCellCount += sheet.FormulaCellCount;
        }

        if (formulaCellCount == 0)
            return;

        _graph.EnsureFormulaCapacity(formulaCellCount);

        foreach (var sheet in workbook.Sheets)
        {
            if (!sheet.HasFormulas)
                continue;

            foreach (var addr in sheet.EnumerateFormulaCells())
            {
                var cell = sheet.GetCell(addr);
                if (cell?.FormulaText is null)
                    continue;

                try
                {
                    if (cell.CachedAst is not FormulaNode ast)
                    {
                        ast = FormulaEvaluator.ParseFormula(cell.FormulaText!);
                        cell.CachedAst = ast;
                    }

                    RegisterFormulaDependencies(addr, ast, sheet.Id, workbook);
                }
                catch (FormulaParseException)
                {
                    // Invalid formula text evaluates as an error during recalc; it contributes no dependencies.
                }
            }
        }
    }

    /// <summary>
    /// Purge a closed/replaced workbook's sheets from this engine's shared per-sheet tracking:
    /// dependency graph edges, volatile-cell registrations, spill-blocked anchors, cached
    /// dependency plans, and cyclic-cell markers. This engine is an app-lifetime singleton shared
    /// by every open workbook (see the class remarks on <see cref="RebuildFormulaDependencies"/>),
    /// so a workbook that is closed or replaced (File &gt; Open / File &gt; New / final window
    /// close) must have its SheetId-keyed state released here — otherwise it leaks forever and
    /// every subsequent recalc keeps folding the growing stale entries into its dirty-cell scan.
    /// Call this BEFORE the caller drops its last reference to <paramref name="workbook"/>, and
    /// only when no other window still shares that same workbook instance.
    /// </summary>
    public void RetireWorkbook(Workbook workbook)
    {
        var sheetIds = new HashSet<SheetId>(workbook.Sheets.Count);
        foreach (var sheet in workbook.Sheets)
            sheetIds.Add(sheet.Id);

        // R95-calc-deleted-sheet-leak: also fold in any sheet this workbook once had (tracked by
        // RebuildFormulaDependencies -- see _lastKnownSheetIdsByWorkbook's remarks) that had
        // already dropped out of workbook.Sheets by the time this runs. Without this, a sheet
        // deleted earlier in the session with no RebuildFormulaDependencies call in between (e.g.
        // the workbook closed immediately after) would be excluded here for the exact same reason
        // it would have been excluded from a same-shaped scan below -- this is the terminal cleanup
        // path, so it must not leave anything for a workbook about to disappear.
        if (_lastKnownSheetIdsByWorkbook.TryGetValue(workbook, out var everKnownSheetIds))
            sheetIds.UnionWith(everKnownSheetIds);

        _lastKnownSheetIdsByWorkbook.Remove(workbook);

        PurgeSheetsFromSharedState(sheetIds);
    }

    /// <summary>
    /// Shared purge of every SheetId-keyed piece of this engine's shared state for the given
    /// sheets: dependency graph edges, volatile-cell registrations, spill-blocked anchors,
    /// non-iterative/active-iterative cyclic-cell markers, ANCHORARRAY spill-union anchor/dependent
    /// tracking, and cached dependency plans (plus their FIFO eviction-order companion queue). Used
    /// both by <see cref="RetireWorkbook"/> (a whole workbook's sheets, at close) and by
    /// <see cref="RebuildFormulaDependencies"/> (just the sheets that dropped out of a still-open
    /// workbook since its last call).
    /// </summary>
    private void PurgeSheetsFromSharedState(HashSet<SheetId> sheetIds)
    {
        if (sheetIds.Count == 0)
            return;

        _graph.ClearForSheets(sheetIds);
        _volatileCells.RemoveWhere(cell => sheetIds.Contains(cell.Sheet));
        _spillBlockedAnchors.RemoveWhere(cell => sheetIds.Contains(cell.Sheet));
        _cyclicCells.RemoveWhere(cell => sheetIds.Contains(cell.Sheet));
        _activeIterativeCyclicCells.RemoveWhere(cell => sheetIds.Contains(cell.Sheet));
        PurgeAnchorArraySpillTrackingForSheets(sheetIds);

        if (_dependencyPlanCache.Count == 0)
            return;

        List<DependencyPlanCacheKey>? staleKeys = null;
        foreach (var key in _dependencyPlanCache.Keys)
        {
            if (sheetIds.Contains(key.SheetId))
                (staleKeys ??= []).Add(key);
        }

        if (staleKeys is null)
            return;

        foreach (var key in staleKeys)
            _dependencyPlanCache.Remove(key);

        // The FIFO order queue is a companion structure that mirrors the dictionary's keys so
        // AddDependencyPlanToCache's eviction stays bounded; rebuild it here (preserving the
        // surviving keys' relative order) so it doesn't keep phantom entries for these purged
        // sheets indefinitely.
        var staleKeySet = new HashSet<DependencyPlanCacheKey>(staleKeys);
        var orderCount = _dependencyPlanCacheOrder.Count;
        for (var i = 0; i < orderCount; i++)
        {
            var key = _dependencyPlanCacheOrder.Dequeue();
            if (!staleKeySet.Contains(key))
                _dependencyPlanCacheOrder.Enqueue(key);
        }
    }

    /// <summary>Rebuild dependencies and evaluate every formula cell in the workbook.</summary>
    public RecalcReport RecalculateAllFormulas(Workbook workbook)
    {
        RebuildFormulaDependencies(workbook);
        var formulaCells = CollectFormulaCells(workbook);

        // Recalculate runs the spill-target dependent follow-up pass itself (see the
        // spillTargetsMayHaveChanged path), so no separate second pass is needed here.
        var report = Recalculate(workbook, formulaCells);

        NotifyAllSheetsRecalculated(workbook);

        return report;
    }

    /// <summary>
    /// Marks every sheet in <paramref name="workbook"/> as having just been through a genuine,
    /// workbook-wide recalculation pass (<see cref="Sheet.NotifyContentRecalculated"/>), regardless
    /// of whether that pass actually touched any of a given sheet's cells.
    ///
    /// <para>
    /// Call this from every choke point that implements a real "Calculate Now"-shaped user gesture
    /// (unlike an ordinary edit, which only dirties the cells it actually touches): plain F9 in
    /// Automatic mode (<see cref="RecalcEngine"/>'s caller in
    /// <c>WorkbookCellEditService.RecalculateDirty</c>) and full recalculation
    /// (<see cref="RecalculateAllFormulas"/>, i.e. Ctrl+Alt+F9 / plain F9 in Manual mode). Plain
    /// <see cref="Recalculate"/> itself only bumps <see cref="Sheet.ContentVersion"/> for sheets
    /// that had a RecalculatedCells/CyclicCells/Errors entry, so a sheet holding zero formula cells
    /// -- e.g. plain data plus a volatile Formula-type conditional-format rule like "=RAND()>0.5"
    /// with no helper formula column -- never advances ContentVersion no matter how many times a
    /// genuine recalc gesture runs. ContentVersion's only consumer is the CF viewport-context cache
    /// (ViewportService.BuildConditionalFormatContext), so unconditionally notifying every sheet
    /// here is safe: it does exactly what a real recalc pass should -- let every sheet's cached
    /// volatile CF results re-roll -- without affecting any other cache.
    /// </para>
    /// </summary>
    public void NotifyAllSheetsRecalculated(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
            sheet.NotifyContentRecalculated();
    }

    /// <summary>
    /// Re-evaluate formula cells that read spill-target cells (which have no dependency-graph
    /// node of their own) until a fixpoint, so they reflect the current contents of every spill
    /// range in the workbook. Shared by the main "a spill range changed this pass" path and by
    /// the #SPILL! anchor retry path below (a re-spilled anchor's newly-populated targets need
    /// the same follow-up, or their readers would stay stale until the next full recalc/F9).
    /// </summary>
    /// <param name="vacatedSpillCells">
    /// Cells that were spill members before this pass but were just vacated (their owning spill
    /// shrank or fully cleared), so they are already gone from every sheet's live spill-value
    /// table by the time this runs. <see cref="CollectSpillTargetDependentFormulaCells"/> normally
    /// discovers dependents only by enumerating CURRENTLY spilled cells, which can no longer find
    /// these — so they are fed in explicitly for the first pass only (their direct dependents need
    /// exactly one re-evaluation to observe the now-blank/changed cell; they are not spill targets
    /// themselves and never need to be re-checked on later passes).
    /// </param>
    private void ResolveSpillTargetDependentsFixpoint(
        Workbook workbook,
        ref RecalcReport report,
        SheetId? restrictWritesToSheet = null,
        IReadOnlyList<CellAddress>? vacatedSpillCells = null,
        bool skipDataTableBodyCells = false)
    {
        // Track, per dependent cell, how many distinct spill-target precedents it read the
        // last time it was scheduled. A cell must only be skipped as "already handled" if its
        // spill-target input count has not grown since — otherwise a cell that depends on two
        // spill targets from different "generations" (one resolved this pass, another that only
        // materializes in a later pass) would be permanently skipped after its first, incomplete
        // evaluation, keeping a stale value forever. See finding H3.
        var seenSpillDependentInputCounts = new Dictionary<CellAddress, int>();

        // The natural termination condition below (spillDependents.Count == 0, i.e. a real
        // fixpoint) already stops the loop as soon as no further generation of spill-target
        // readers is discovered — MaxSpillDependentPasses on its own would truncate a chain of
        // more than 64 dependent spilling formulas that plain-address-reference each other's
        // spill members (rather than via ANCHORARRAY/#, which gets a real dependency-graph edge
        // and never needs this loop at all) while it is still legitimately converging. Raise the
        // ceiling to the workbook's total formula-cell count when that is larger: a chain of
        // dependent spilling formulas cannot have more links than there are formula cells to form
        // them, so this bound is provably never hit by any legal spill nesting depth while still
        // guarding against a pathological/self-perpetuating chain spinning forever.
        var maxPasses = MaxSpillDependentPasses;
        var totalFormulaCellCount = 0;
        foreach (var sheet in workbook.Sheets)
            totalFormulaCellCount += sheet.FormulaCellCount;
        if (totalFormulaCellCount > maxPasses)
            maxPasses = totalFormulaCellCount;

        for (var pass = 0; pass < maxPasses; pass++)
        {
            var spillDependents = CollectSpillTargetDependentFormulaCells(
                workbook,
                pass == 0 ? vacatedSpillCells : null,
                out var inputCounts);
            spillDependents.RemoveAll(addr =>
            {
                var currentCount = inputCounts[addr];
                if (seenSpillDependentInputCounts.TryGetValue(addr, out var previousCount) &&
                    currentCount <= previousCount)
                {
                    return true;
                }

                seenSpillDependentInputCounts[addr] = currentCount;
                return false;
            });
            if (spillDependents.Count == 0)
                break;

            var spillReport = Recalculate(workbook, spillDependents, resolveSpillDependents: false, restrictWritesToSheet: restrictWritesToSheet, skipDataTableBodyCells: skipDataTableBodyCells);
            report = MergeRecalcReports(report, spillReport);
        }
    }

    /// <summary>
    /// Collect the set of formula cells that directly reference at least one spill-target cell
    /// (a cell whose value comes from another formula's spill, not from its own formula).
    /// These are the cells that may have received an incorrect blank in the first recalc pass.
    /// Also reports, per dependent cell, the number of distinct spill-target cells it currently
    /// reads — callers use this to detect a dependent that has gained a new spill-target input
    /// since it was last scheduled (e.g. a chained spill that materializes on a later pass), which
    /// must not be treated as "already handled" even though its address was seen before.
    /// </summary>
    /// <param name="extraTargets">
    /// Additional cell addresses to treat as spill targets for this call only, even though they
    /// are no longer present in any sheet's live spill-value table (e.g. cells vacated by a spill
    /// that just shrank or cleared — see <see cref="ResolveSpillTargetDependentsFixpoint"/>).
    /// </param>
    private List<CellAddress> CollectSpillTargetDependentFormulaCells(
        Workbook workbook,
        IReadOnlyList<CellAddress>? extraTargets,
        out Dictionary<CellAddress, int> inputCounts)
    {
        List<CellAddress>? result = null;
        var counts = new Dictionary<CellAddress, int>();

        void CollectDependentsOf(CellAddress spillTarget)
        {
            var deps = _graph.GetDirectDependents(spillTarget);
            foreach (var dep in deps)
            {
                var depSheet = workbook.GetSheet(dep.Sheet);
                if (depSheet?.GetCell(dep)?.HasFormula != true)
                    continue;

                if (counts.TryGetValue(dep, out var count))
                {
                    counts[dep] = count + 1;
                    continue;
                }

                counts[dep] = 1;
                result ??= [];
                result.Add(dep);
            }
        }

        foreach (var sheet in workbook.Sheets)
        {
            if (!sheet.HasSpillValues)
                continue;

            foreach (var spillTarget in sheet.EnumerateSpillTargetCells())
                CollectDependentsOf(spillTarget);
        }

        if (extraTargets is not null)
        {
            foreach (var target in extraTargets)
                CollectDependentsOf(target);
        }

        inputCounts = counts;
        return result ?? [];
    }

    /// <summary>
    /// Records, into <paramref name="vacatedSpillCells"/>, every cell in the anchor's prior spill
    /// extent (<paramref name="priorRows"/> x <paramref name="priorCols"/>, excluding the anchor
    /// itself) that is not covered by its new extent (<paramref name="newRows"/> x
    /// <paramref name="newCols"/> — pass 0,0 when the spill was fully cleared rather than shrunk).
    /// These cells are about to disappear from Sheet's spill-value table, so any formula that
    /// directly references one of them (e.g. B1=A2+1, where A2 was a spill member) would otherwise
    /// never be re-evaluated. See R22-calc-engine-dependency-1.
    /// </summary>
    private static void CaptureVacatedSpillCells(
        CellAddress anchor,
        uint priorRows,
        uint priorCols,
        int newRows,
        int newCols,
        ref List<CellAddress>? vacatedSpillCells)
    {
        var keepRows = (uint)Math.Max(0, newRows);
        var keepCols = (uint)Math.Max(0, newCols);
        for (var r = 0u; r < priorRows; r++)
        {
            for (var c = 0u; c < priorCols; c++)
            {
                if (r == 0 && c == 0) continue;
                if (r < keepRows && c < keepCols) continue;

                (vacatedSpillCells ??= []).Add(new CellAddress(anchor.Sheet, anchor.Row + r, anchor.Col + c));
            }
        }
    }

    private static RecalcReport MergeRecalcReports(RecalcReport first, RecalcReport second)
    {
        if (second.RecalculatedCells.Count == 0 && second.Errors.Count == 0 && second.CyclicCells.Count == 0)
            return first;
        if (first.RecalculatedCells.Count == 0 && first.Errors.Count == 0 && first.CyclicCells.Count == 0)
            return second;

        var recalculated = new List<CellAddress>(first.RecalculatedCells.Count + second.RecalculatedCells.Count);
        recalculated.AddRange(first.RecalculatedCells);
        recalculated.AddRange(second.RecalculatedCells);

        var errors = new List<(CellAddress Cell, string Error)>(first.Errors.Count + second.Errors.Count);
        errors.AddRange(first.Errors);
        errors.AddRange(second.Errors);

        var cyclic = new List<CellAddress>(first.CyclicCells.Count + second.CyclicCells.Count);
        cyclic.AddRange(first.CyclicCells);
        cyclic.AddRange(second.CyclicCells);

        return new RecalcReport(recalculated, errors, cyclic);
    }

    /// <summary>
    /// Bumps <see cref="Sheet.ContentVersion"/> once per distinct sheet touched by this recalc pass
    /// (recalculated, cyclic, and errored cells all count -- every branch that writes a fresh value
    /// into a cell also lands its address in one of those three lists). See the call site's comment
    /// for why this is necessary: RecalcEngine mutates Cell.Value in place without routing through
    /// Sheet.SetCell/SetFormula.
    /// </summary>
    private static void NotifySheetsRecalculated(Workbook workbook, RecalcReport report)
    {
        if (report.RecalculatedCells.Count == 0 && report.CyclicCells.Count == 0 && report.Errors.Count == 0)
            return;

        HashSet<SheetId>? notified = null;
        void Notify(SheetId sheetId)
        {
            notified ??= [];
            if (!notified.Add(sheetId)) return;
            workbook.GetSheet(sheetId)?.NotifyContentRecalculated();
        }

        for (var i = 0; i < report.RecalculatedCells.Count; i++)
            Notify(report.RecalculatedCells[i].Sheet);
        for (var i = 0; i < report.CyclicCells.Count; i++)
            Notify(report.CyclicCells[i].Sheet);
        for (var i = 0; i < report.Errors.Count; i++)
            Notify(report.Errors[i].Cell.Sheet);
    }

    /// <summary>Rebuild dependencies and evaluate formula cells on a single worksheet.</summary>
    public RecalcReport RecalculateSheetFormulas(Workbook workbook, SheetId sheetId)
    {
        RebuildFormulaDependencies(workbook);
        var sheet = workbook.GetSheet(sheetId);
        if (sheet is null)
            return new RecalcReport([], [], []);

        var formulaCells = CollectFormulaCells(sheet);

        // Shift+F9 "Calculate Sheet" recalculates only the active worksheet (see
        // WorkbookCellEditService.RecalculateSheet doc comment). RebuildFormulaDependencies just
        // re-registered every volatile cell in the whole workbook into the shared _volatileCells
        // set, and Recalculate unconditionally folds ALL of them into its dirty set -- which would
        // silently re-evaluate (and mutate) volatile cells on OTHER sheets of this same workbook as
        // a side effect. Temporarily hide those other-sheet volatile cells from _volatileCells for
        // the duration of this call so only the target sheet's volatile cells are treated as dirty,
        // then restore them so a later full/other-sheet recalc still tracks them correctly.
        List<CellAddress>? otherSheetVolatileCells = null;
        foreach (var addr in _volatileCells)
        {
            if (!addr.Sheet.Equals(sheetId))
                (otherSheetVolatileCells ??= []).Add(addr);
        }

        if (otherSheetVolatileCells is not null)
        {
            foreach (var addr in otherSheetVolatileCells)
                _volatileCells.Remove(addr);
        }

        try
        {
            // Restrict every write inside this pass to sheetId's own cells: the dependency
            // traversal still crosses sheet boundaries (needed to keep the target sheet's own
            // formulas correctly ordered against their precedents), but Excel's Shift+F9 "Calculate
            // Sheet" must never mutate a cross-sheet dependent -- it stays exactly as it was until
            // its own sheet is calculated (a later Shift+F9 there, or a full F9, which always
            // re-evaluates every formula cell in the workbook regardless of this pass). See the
            // matching restriction checks throughout Recalculate/AddCyclicCell/RunIterativeCalc/
            // ResolveSpillTargetDependentsFixpoint.
            var report = Recalculate(workbook, formulaCells, resolveSpillDependents: true, restrictWritesToSheet: sheetId);

            // Shift+F9 "Calculate Sheet" is a genuine "Calculate Now"-shaped gesture for sheetId --
            // see NotifyAllSheetsRecalculated's doc comment for why a real recalc pass must
            // unconditionally let the target sheet's cached volatile CF results re-roll even when
            // Recalculate's own report is empty (e.g. a sheet holding only literal data plus a
            // volatile Formula-type CF rule like "=RAND()>0.5", with zero formula cells of its own).
            // Unlike NotifyAllSheetsRecalculated, this must stay scoped to sheetId alone: Shift+F9
            // deliberately restricts every write to the target sheet (see the restrictWritesToSheet
            // comment above and RecalculateSheetFormulasVolatileScopeTests), so bumping any other
            // sheet's ContentVersion here would contradict that same "only this sheet" contract.
            sheet.NotifyContentRecalculated();

            return FilterReportForSheet(report, sheetId);
        }
        finally
        {
            if (otherSheetVolatileCells is not null)
            {
                foreach (var addr in otherSheetVolatileCells)
                    _volatileCells.Add(addr);
            }
        }
    }

    private static List<CellAddress> CollectFormulaCells(Workbook workbook)
    {
        var formulaCellCount = 0;
        foreach (var sheet in workbook.Sheets)
            formulaCellCount += sheet.FormulaCellCount;

        if (formulaCellCount == 0)
            return [];

        var formulaCells = new List<CellAddress>(formulaCellCount);
        foreach (var sheet in workbook.Sheets)
            AddFormulaCells(sheet, formulaCells);

        return formulaCells;
    }

    private static List<CellAddress> CollectFormulaCells(Sheet sheet)
    {
        if (!sheet.HasFormulas)
            return [];

        var formulaCells = new List<CellAddress>(sheet.FormulaCellCount);
        AddFormulaCells(sheet, formulaCells);
        return formulaCells;
    }

    private static void AddFormulaCells(Sheet sheet, List<CellAddress> formulaCells)
    {
        if (!sheet.HasFormulas)
            return;

        foreach (var address in sheet.EnumerateFormulaCells())
            formulaCells.Add(address);
    }

    private static RecalcReport FilterReportForSheet(RecalcReport report, SheetId sheetId)
    {
        if (AllCellsAreOnSheet(report.RecalculatedCells, sheetId) &&
            AllErrorsAreOnSheet(report.Errors, sheetId) &&
            AllCellsAreOnSheet(report.CyclicCells, sheetId))
        {
            return report;
        }

        var recalculated = new List<CellAddress>();
        foreach (var address in report.RecalculatedCells)
        {
            if (address.Sheet.Equals(sheetId))
                recalculated.Add(address);
        }

        var errors = new List<(CellAddress Cell, string Error)>();
        foreach (var error in report.Errors)
        {
            if (error.Cell.Sheet.Equals(sheetId))
                errors.Add(error);
        }

        var cyclic = new List<CellAddress>();
        foreach (var address in report.CyclicCells)
        {
            if (address.Sheet.Equals(sheetId))
                cyclic.Add(address);
        }

        return new RecalcReport(recalculated, errors, cyclic);
    }

    private static bool AllCellsAreOnSheet(IReadOnlyList<CellAddress> addresses, SheetId sheetId)
    {
        for (var i = 0; i < addresses.Count; i++)
        {
            if (!addresses[i].Sheet.Equals(sheetId))
                return false;
        }

        return true;
    }

    private static bool AllErrorsAreOnSheet(IReadOnlyList<(CellAddress Cell, string Error)> errors, SheetId sheetId)
    {
        for (var i = 0; i < errors.Count; i++)
        {
            if (!errors[i].Cell.Sheet.Equals(sheetId))
                return false;
        }

        return true;
    }

    private static bool CollectReferences(
        FormulaNode node,
        SheetId defaultSheetId,
        CellAddress formulaCell,
        FreeX.Core.Model.Workbook? workbook,
        FormulaDependencySet refs,
        ref bool cacheableForDependencyPlan,
        HashSet<string>? namedFormulaStack,
        HashSet<string>? localScopeNames)
    {
        switch (node)
        {
            case CellRefNode cellRef when cellRef.SheetName is not null:
            {
                cacheableForDependencyPlan = false;
                var targetSheet = workbook?.GetSheet(cellRef.SheetName);
                if (targetSheet is not null)
                    refs.Add(new CellAddress(targetSheet.Id, cellRef.Row, cellRef.ColumnNumber));
                return false;
            }
            case CellRefNode cellRef:
                refs.Add(new CellAddress(defaultSheetId, cellRef.Row, cellRef.ColumnNumber));
                return false;

            // A 3-D sheet-span reference (e.g. Sheet1:Sheet3!A1) must register a dependency edge on
            // the referenced cell/range on EVERY sheet the span covers — start, end, and every sheet
            // between them in workbook tab order — so editing any spanned sheet's cell recalculates
            // the formula that references the span (matching normal Excel dependency behaviour).
            // This must be checked before the plain-SheetName case below, since a span also has
            // SheetName set (to its start sheet).
            case RangeRefNode range when range.EndSheetName is not null:
            {
                cacheableForDependencyPlan = false;
                if (workbook is null)
                    return false;

                var startIndex = FindSheetIndex(workbook, range.SheetName!);
                var endIndex = FindSheetIndex(workbook, range.EndSheetName);
                if (startIndex < 0 || endIndex < 0)
                    return false;

                var firstIndex = Math.Min(startIndex, endIndex);
                var lastIndex = Math.Max(startIndex, endIndex);
                for (var sheetIndex = firstIndex; sheetIndex <= lastIndex; sheetIndex++)
                    refs.AddRange(CreateGridRange(workbook.Sheets[sheetIndex].Id, range));

                return false;
            }

            case RangeRefNode range when range.SheetName is not null:
            {
                cacheableForDependencyPlan = false;
                var targetSheet = workbook?.GetSheet(range.SheetName);
                if (targetSheet is not null)
                {
                    refs.AddRange(CreateGridRange(targetSheet.Id, range));
                }
                return false;
            }
            case RangeRefNode range:
            {
                refs.AddRange(CreateGridRange(defaultSheetId, range));
                return false;
            }

            case FullColumnRangeRefNode range when range.SheetName is not null:
            {
                cacheableForDependencyPlan = false;
                var targetSheet = workbook?.GetSheet(range.SheetName);
                if (targetSheet is not null)
                    refs.AddRange(CreateGridRange(targetSheet.Id, range));
                return false;
            }
            case FullColumnRangeRefNode range:
            {
                refs.AddRange(CreateGridRange(defaultSheetId, range));
                return false;
            }

            case FullRowRangeRefNode range when range.SheetName is not null:
            {
                cacheableForDependencyPlan = false;
                var targetSheet = workbook?.GetSheet(range.SheetName);
                if (targetSheet is not null)
                    refs.AddRange(CreateGridRange(targetSheet.Id, range));
                return false;
            }
            case FullRowRangeRefNode range:
            {
                refs.AddRange(CreateGridRange(defaultSheetId, range));
                return false;
            }

            case NamedRangeNode named:
            {
                // A LET binding name or LAMBDA parameter name shadows any same-named workbook/
                // sheet-scoped defined name for the whole body/scope, exactly like
                // FormulaEvaluator.LocalScopes.cs's EvaluateLet/EvaluateLambda and
                // FormulaEvaluator.References.cs's EvaluateNamedRange (which checks
                // context.TryResolveLambdaBinding BEFORE ever consulting the workbook's named
                // ranges). The evaluator never reads the shadowed outer name here, so the
                // dependency graph must not register a (possibly self-looping, see
                // R114-calc-let-lambda-shadow) edge onto it either. An explicit sheet qualifier
                // (Sheet2!x) can never refer to a local binding -- those are always bare
                // identifiers -- so only the unqualified form is checked.
                if (named.SheetQualifier is null &&
                    localScopeNames is not null && localScopeNames.Contains(named.Name))
                {
                    return false;
                }

                cacheableForDependencyPlan = false;

                // An explicit sheet qualifier (e.g. the "Sheet2" in "Sheet2!Data") must resolve
                // against THAT sheet's own defined-name scope, not the FORMULA cell's own sheet —
                // exactly like FormulaEvaluator.References.cs's TryResolveSheetQualifiedName
                // (which the eval side already uses for this identical SheetQualifier field).
                // Two sheets can each define their own LOCAL name with the same text (Sheet1's
                // own "Data" vs Sheet2's own "Data"); resolving against defaultSheetId here would
                // register the dependency edge on the WRONG sheet's name whenever a formula
                // explicitly qualifies a reference to the OTHER sheet's local name. See
                // R92-io-defined-name-scope-eval-5-1.
                var resolveSheetId = defaultSheetId;
                if (named.SheetQualifier is { } sheetQualifier)
                {
                    var qualifiedSheet = workbook?.GetSheet(sheetQualifier);
                    if (qualifiedSheet is null)
                    {
                        // Unresolvable qualifier (deleted sheet, or a bracket-prefixed external-
                        // workbook qualifier): the evaluator surfaces #REF!/reads an external
                        // cache, either way nothing in THIS workbook's dependency graph to wire.
                        return false;
                    }
                    resolveSheetId = qualifiedSheet.Id;
                }

                // Sheet-scope-first, and scope wins over kind: a sheet-scoped named FORMULA must
                // take precedence over a same-named workbook-global named RANGE, exactly like
                // FormulaEvaluator.IsSheetScopedName/EvaluateNamedRange resolve it (Excel rule
                // §18.2.6 is per-name, not per-kind). Workbook.TryGetNamedRange only distinguishes
                // scoped-range vs global-range and falls through to the global range dictionary
                // when no scoped RANGE exists — it does not know about a scoped FORMULA of the same
                // name shadowing that global range, so calling it first (as this case used to)
                // would register a dependency on the wrong cells whenever the scoped name is a
                // formula. Check for an explicit sheet-scoped FORMULA first, mirroring the eval
                // side's precedence exactly, before ever consulting the range resolver.
                var sheetScopedIsFormula = workbook is not null &&
                    workbook.ScopedNamedFormulas.ContainsKey((named.Name, resolveSheetId));

                if (!sheetScopedIsFormula &&
                    workbook is not null && workbook.TryGetNamedRange(named.Name, resolveSheetId, out var namedRange))
                {
                    refs.AddRange(namedRange);
                    return false;
                }

                var formulaText = workbook?.TryGetNamedFormulaText(named.Name, resolveSheetId);
                if (formulaText is not null && !string.IsNullOrWhiteSpace(formulaText))
                {
                    // Keyed by (name, defining-scope) — not the bare name — via the exact same
                    // FormulaEvaluator.NamedFormulaVisitingKey the evaluator's own identical-purpose
                    // cycle guard uses (FormulaEvaluator.References.cs), so two textually-distinct
                    // sheet-scoped formulas that happen to share a name (e.g. Sheet1's own "Foo" and
                    // Sheet2's own "Foo") don't falsely collide with each other here just because one
                    // references the other via an explicit sheet qualifier (R118-calc-named-formula-
                    // scope-key). sheetScopedIsFormula/resolveSheetId above already computed exactly
                    // which scope this formulaText actually came from -- reuse that, don't re-derive.
                    var visitingKey = FormulaEvaluator.NamedFormulaVisitingKey(
                        named.Name, sheetScopedIsFormula ? resolveSheetId : null);
                    namedFormulaStack ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!namedFormulaStack.Add(visitingKey))
                        return false;

                    try
                    {
                        var namedAst = FormulaEvaluator.ParseFormula(formulaText);

                        // Re-anchor the name's relative (non-$) references to formulaCell, mirroring
                        // FormulaEvaluator.ApplyRelativeNameAnchor's per-using-cell shift exactly (same
                        // implicit A1-of-formulaCell's-sheet anchor convention), so the dependency graph
                        // tracks the SAME target the evaluator actually reads: a name with RefersTo="=B2"
                        // used from D10 evaluates against (and must depend on) the shifted E11, not the
                        // literal unshifted B2.
                        var anchor = new CellAddress(formulaCell.Sheet, 1, 1);
                        var shiftedAst = FormulaEvaluator.ShiftFormulaForCell(namedAst, anchor, formulaCell);

                        // Mirror ApplyRelativeNameAnchor's self-reference guard: if the shift happens to
                        // land on formulaCell itself (an artifact of the implicit A1 anchor, not a genuine
                        // circular formula), the evaluator falls back to evaluating the literal unshifted
                        // form -- so the dependency graph must track that same unshifted target rather than
                        // manufacture a self-dependency edge the evaluator never actually reads.
                        var effectiveAst = FormulaReferenceContainment.ContainsUnqualifiedCell(shiftedAst, formulaCell)
                            ? namedAst
                            : shiftedAst;

                        return CollectReferences(
                            effectiveAst,
                            defaultSheetId,
                            formulaCell,
                            workbook,
                            refs,
                            ref cacheableForDependencyPlan,
                            namedFormulaStack,
                            localScopeNames);
                    }
                    catch (FormulaParseException)
                    {
                        return false;
                    }
                    finally
                    {
                        namedFormulaStack.Remove(visitingKey);
                    }
                }
                return false;
            }

            case StructuredReferenceNode structured:
            {
                cacheableForDependencyPlan = false;
                if (workbook is null)
                    return false;

                var structuredRange = StructuredReferenceResolver.ResolveDataBodyColumn(
                    workbook,
                    workbook.GetSheet(defaultSheetId),
                    structured.TableName,
                    structured.ColumnName,
                    formulaCell);
                if (structuredRange is null)
                    return false;

                refs.AddRange(structuredRange.Value);
                return false;
            }

            case StructuredCurrentRowReferenceNode currentRow:
            {
                cacheableForDependencyPlan = false;
                var address = StructuredReferenceResolver.ResolveCurrentRowColumn(
                    workbook,
                    workbook?.GetSheet(defaultSheetId),
                    formulaCell,
                    currentRow.TableName,
                    currentRow.ColumnName);
                if (address is not null)
                    refs.Add(address.Value);
                return false;
            }

            case BinaryOpNode binary:
            {
                var leftHasVolatile = CollectReferences(binary.Left, defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack, localScopeNames);
                var rightHasVolatile = CollectReferences(binary.Right, defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack, localScopeNames);
                return leftHasVolatile || rightHasVolatile;
            }

            case UnaryOpNode unary:
                return CollectReferences(unary.Operand, defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack, localScopeNames);

            // R111-calc-union-intersection-endpoint-deps: a parenthesized multi-area union
            // (e.g. "(A1:A5,C1:C5)", parsed to UnionNode -- see R93_AreasUnionValueModelTests),
            // a space-intersection ("A1:B5 B1:C10", IntersectionNode), or an INDEX(...)-anchored
            // range endpoint (NamedRangeEndpointNode, e.g. "A1:INDEX(B:B,5)") are all fully
            // evaluated shapes (FormulaEvaluator.cs's EvaluateUnionNode/EvaluateIntersectionNode/
            // EvaluateNamedRangeEndpointNode) that can appear as a bare argument to a function
            // (e.g. "=SUM((A1:A5,C1:C5))") without ever going through BinaryOpNode/FunctionCallNode.
            // Before this case existed, none of these three node kinds had ANY arm in this switch,
            // so falling into one contributed zero dependency edges and zero volatility signal --
            // silently the same defect class R29/R92 already fixed for ANCHORARRAY's implicit union
            // rectangle (see the case immediately below), just one AST level higher. Recurse into
            // every constituent sub-expression and OR their volatility, exactly like the existing
            // BinaryOpNode/FunctionCallNode cases do, so a plain precedent inside any area still
            // gets a dependency edge and a volatile function nested anywhere inside still marks
            // the whole formula volatile.
            case UnionNode union:
            {
                var areas = union.Areas;
                var hasVolatile = false;
                for (var i = 0; i < areas.Count; i++)
                {
                    if (CollectReferences(areas[i], defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack, localScopeNames))
                        hasVolatile = true;
                }
                return hasVolatile;
            }

            case IntersectionNode intersection:
            {
                // True dependency tracking would ideally register only the cells in the actual
                // intersected range, but recursing into both sides (mirroring BinaryOpNode) is at
                // minimum correct for the common case: any cell that could affect either operand's
                // extent or value still dirties this formula, and a volatile function on either
                // side still marks it volatile -- never silently dropped as before.
                var leftHasVolatile = CollectReferences(intersection.Left, defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack, localScopeNames);
                var rightHasVolatile = CollectReferences(intersection.Right, defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack, localScopeNames);
                return leftHasVolatile || rightHasVolatile;
            }

            case NamedRangeEndpointNode endpointNode:
            {
                var startHasVolatile = CollectReferences(endpointNode.Start, defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack, localScopeNames);
                var endHasVolatile = CollectReferences(endpointNode.End, defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack, localScopeNames);
                return startHasVolatile || endHasVolatile;
            }

            // A1#:B5 spill-range-union reference (ANCHORARRAY(anchor, end), see Parser.cs's ':' handling
            // after a '#' anchor). FormulaEvaluator.Functions.cs EvaluateAnchorArray reads every cell in
            // the bounding rectangle that unions the anchor's LIVE spill extent with the end cell -- not
            // just the literal anchor..end rectangle -- so once the anchor's spill grows past the end
            // cell (e.g. A1=SEQUENCE(3) growing to SEQUENCE(10) while the formula reads A1#:B5), cells
            // inside the true (grown) union that fall outside the literal anchor..end rectangle need a
            // dependency edge too, or editing them never marks the dependent formula dirty. Consult
            // sheet.TryGetSpillExtent for the anchor -- exactly like EvaluateAnchorArray does -- and
            // union that with the end cell; when the extent isn't reachable yet (anchor hasn't spilled
            // as of this registration), fall back to the literal anchor..end rectangle, same as before.
            // Also mark this dependency plan non-cacheable: the correct edge depends on live sheet
            // state (the spill extent) that can change without the formula's own AST changing, so a
            // later re-registration (e.g. RebuildFormulaDependencies) must re-run this branch instead of
            // reusing a stale cached rectangle from before the anchor grew. The anchor is resolved via
            // TryResolveAnchorForDependencies, which handles a bare same-sheet CellRefNode, a
            // cross-sheet CellRefNode (Sheet1!A1#:C5), and a single-cell NamedRangeNode anchor
            // (Anchor#:C5 where Anchor points at one cell) -- mirroring TryResolveAnchorAddress in
            // FormulaEvaluator.Functions.cs so the dependency graph covers exactly what the evaluator
            // can read. Per that evaluator's own documented semantics, the end cell is always parsed
            // unqualified relative to the ANCHOR's own sheet, not the formula's sheet, so the union
            // rectangle (and the live-spill-extent lookup) is built on the anchor's resolved sheet.
            case FunctionCallNode anchorArray when anchorArray.FunctionName == "ANCHORARRAY" &&
                anchorArray.Arguments.Count == 2 &&
                TryResolveAnchorForDependencies(anchorArray.Arguments[0], defaultSheetId, workbook, out var anchorSheetId, out var anchorRow, out var anchorCol) &&
                anchorArray.Arguments[1] is CellRefNode { SheetName: null } endCell:
            {
                cacheableForDependencyPlan = false;

                var unionEndRow = Math.Max(anchorRow, endCell.Row);
                var unionEndCol = Math.Max(anchorCol, endCell.ColumnNumber);

                if (workbook?.GetSheet(anchorSheetId) is { } anchorSheet &&
                    anchorSheet.TryGetSpillExtent(
                        new CellAddress(anchorSheetId, anchorRow, anchorCol),
                        out var spillRows, out var spillCols))
                {
                    unionEndRow = Math.Max(unionEndRow, anchorRow + spillRows - 1);
                    unionEndCol = Math.Max(unionEndCol, anchorCol + spillCols - 1);
                }

                refs.AddRange(new GridRange(
                    new CellAddress(anchorSheetId, anchorRow, anchorCol),
                    new CellAddress(anchorSheetId, unionEndRow, unionEndCol)));

                // Record that formulaCell's registered edges depend on this anchor's LIVE spill
                // extent, not just the rectangle computed above -- so the main evaluation loop can
                // re-derive that rectangle whenever the anchor's extent later changes (grows,
                // shrinks, appears, or disappears), instead of leaving this a one-time snapshot.
                // See R92-calc-array-recalc-order-5-1 / _anchorArraySpillDependents.
                refs.AddAnchorArrayAnchor(new CellAddress(anchorSheetId, anchorRow, anchorCol));
                return false;
            }

            // LET(name1, val1, ..., nameN, valN, calc_expr): the binding-name slots (even-indexed
            // arguments) are pure identifier declarations parsed as NamedRangeNode -- see
            // FormulaEvaluator.LocalScopes.cs's EvaluateLet -- never evaluated as a name reference,
            // so they must NOT fall into the NamedRangeNode case below (which would otherwise
            // resolve them against any coincidentally-same-named workbook/sheet-scoped defined
            // name and register a bogus, possibly self-looping dependency edge -- R114-calc-let-
            // lambda-shadow). Each value expression can see only the bindings already assigned by
            // EARLIER pairs (EvaluateLet builds its dictionary sequentially, so a later pair's
            // value expression sees the earlier names, but not itself or any later one), and
            // calc_expr sees every binding. Recurse with an accumulated local-scope set so any
            // NamedRangeNode matching an in-scope binding name (checked in the NamedRangeNode case
            // above) is treated as a pure local reference -- no dependency edge -- exactly as the
            // evaluator's own shadowing means no such dependency is ever actually read.
            case FunctionCallNode letCall when string.Equals(letCall.FunctionName, "LET", StringComparison.OrdinalIgnoreCase) &&
                letCall.Arguments.Count >= 3 && letCall.Arguments.Count % 2 == 1:
            {
                var letArgs = letCall.Arguments;
                var pairCount = (letArgs.Count - 1) / 2;
                var hasVolatile = false;
                HashSet<string>? scopeNames = localScopeNames is null
                    ? null
                    : new HashSet<string>(localScopeNames, StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < pairCount; i++)
                {
                    if (CollectReferences(letArgs[i * 2 + 1], defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack, scopeNames))
                        hasVolatile = true;

                    if (letArgs[i * 2] is NamedRangeNode letName)
                    {
                        scopeNames ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        scopeNames.Add(letName.Name);
                    }
                }

                if (CollectReferences(letArgs[^1], defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack, scopeNames))
                    hasVolatile = true;

                return hasVolatile;
            }

            // LAMBDA([param1, param2, ...,] body): every argument except the last is a parameter
            // name, parsed as NamedRangeNode -- see FormulaEvaluator.LocalScopes.cs's
            // EvaluateLambda -- never a name reference, and all of them are simultaneously in
            // scope for the single body expression (unlike LET's sequential bindings). Same
            // rationale as the LET case above: skip the parameter-name slots and recurse into the
            // body with them added to the local scope so a same-named workbook/sheet-scoped
            // defined name is correctly shadowed instead of contributing a bogus dependency edge.
            case FunctionCallNode lambdaCall when string.Equals(lambdaCall.FunctionName, "LAMBDA", StringComparison.OrdinalIgnoreCase) &&
                lambdaCall.Arguments.Count >= 1:
            {
                var lambdaArgs = lambdaCall.Arguments;
                HashSet<string>? scopeNames = localScopeNames is null
                    ? null
                    : new HashSet<string>(localScopeNames, StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < lambdaArgs.Count - 1; i++)
                {
                    if (lambdaArgs[i] is NamedRangeNode paramName)
                    {
                        scopeNames ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        scopeNames.Add(paramName.Name);
                    }
                }

                return CollectReferences(lambdaArgs[^1], defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack, scopeNames);
            }

            // R156-freex-recalc-order-F1: a literal-string INDIRECT("A1") / INDIRECT("Sheet!A1")
            // target is exactly as static as a plain cell reference, so register the same
            // dependency edge a bare `=A1` reference would. Without this, a mixed cycle (one leg an
            // ordinary reference, the other leg this literal INDIRECT hop -- e.g. A1=B1+1,
            // B1=INDIRECT("A1")) is invisible to DependencyGraph: B1 registers zero precedents, so
            // GetRecalcOrder sees an ordinary acyclic chain and never routes either cell through
            // cyclic-cell handling, and the pair drifts by +1 forever instead of freezing at 0 with
            // #CIRCULAR!. Deliberately skip when the resolved target IS formulaCell itself -- that
            // direct single-cell self-reference (e.g. A1=INDIRECT("A1")+1) is already fully handled
            // by IsIndirectSelfReference's own runtime sentinel + RunIterativeCalc pairing (see its
            // remarks in BuiltInFunctions.Lookup.Indirect.cs), which this static edge must not
            // disturb -- adding a self-edge here would route it through structural cycle detection
            // instead and risk breaking that already-verified iterative-calculation convergence.
            case FunctionCallNode indirectCall when
                string.Equals(indirectCall.FunctionName, "INDIRECT", StringComparison.OrdinalIgnoreCase) &&
                indirectCall.Arguments.Count == 1 &&
                indirectCall.Arguments[0] is StringNode { Value: var indirectRefText } &&
                BuiltInFunctions.TryResolveIndirectStaticCellTarget(indirectRefText, out var indirectSheetName, out var indirectRow, out var indirectCol):
            {
                var indirectTargetSheetId = indirectSheetName is null
                    ? defaultSheetId
                    : workbook?.GetSheet(indirectSheetName)?.Id;

                if (indirectTargetSheetId is { } resolvedIndirectSheetId)
                {
                    cacheableForDependencyPlan = false;
                    var indirectTarget = new CellAddress(resolvedIndirectSheetId, indirectRow, indirectCol);
                    if (indirectTarget != formulaCell)
                        refs.Add(indirectTarget);
                }

                return true; // INDIRECT is always volatile -- see IsVolatileFunctionName.
            }

            case FunctionCallNode func:
            {
                var containsVolatileFunction = IsVolatileFunctionName(func.FunctionName) && !IsNonVolatileCellOrInfoCall(func);
                var arguments = func.Arguments;
                for (var i = 0; i < arguments.Count; i++)
                {
                    if (CollectReferences(arguments[i], defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack, localScopeNames))
                        containsVolatileFunction = true;
                }

                return containsVolatileFunction;
            }
        }

        return false;
    }

    // Best-effort structural check for whether `node` contains an unqualified (implicit-sheet)
    // cell/range reference that covers `current` -- mirrors FormulaEvaluator's private
    // ReferencesCell (FormulaEvaluator.References.cs) used by ApplyRelativeNameAnchor's own
    // self-reference guard, re-declared here since the two projects share no InternalsVisibleTo.
    // Only the node kinds ShiftFormulaForCell actually rewrites are inspected; this is
    // intentionally narrow (not a full reference-tracking pass) to match that guard's purpose.
    private static bool IsVolatileFunctionName(string name) =>
        name is "NOW" or "TODAY" or "RAND" or "RANDBETWEEN" or "RANDARRAY" or "INDIRECT" or "OFFSET" or "CELL" or "INFO";

    // CELL and INFO are only SOMETIMES volatile in Excel: CELL("width", ...) reports static layout
    // metadata (not live state) so it never needs to recalc on unrelated edits, and INFO(...) is
    // non-volatile for a fixed set of constant info-types (directory/numfile/origin/osversion/recalc/
    // release/system) that don't change without an explicit recalc trigger elsewhere. When the first
    // argument isn't a compile-time constant string (e.g. a cell reference or nested expression) we
    // can't tell which case Excel would apply, so we conservatively leave the call volatile, same as
    // every other name in IsVolatileFunctionName.
    private static bool IsNonVolatileCellOrInfoCall(FunctionCallNode func)
    {
        if (func.Arguments.Count == 0 || func.Arguments[0] is not StringNode { Value: var infoTypeArg })
            return false;

        var infoType = infoTypeArg.Trim();
        return func.FunctionName switch
        {
            "CELL" => string.Equals(infoType, "width", StringComparison.OrdinalIgnoreCase),
            "INFO" => NonVolatileInfoTypes.Contains(infoType.ToLowerInvariant()),
            _ => false,
        };
    }

    private static readonly HashSet<string> NonVolatileInfoTypes =
        ["directory", "numfile", "origin", "osversion", "recalc", "release", "system"];

    /// <summary>
    /// Resolves an ANCHORARRAY anchor argument to the concrete (sheet, row, col) cell it points at,
    /// for dependency-registration purposes. Mirrors TryResolveAnchorAddress in
    /// FormulaEvaluator.Functions.cs: a bare same-sheet CellRefNode, a cross-sheet CellRefNode
    /// (Sheet1!A1), and a NamedRangeNode that resolves (via Workbook.TryGetNamedRange, sheet-scope
    /// first) to a single-cell range are all supported. A named FORMULA anchor (sheet-scoped or
    /// global) and a multi-cell named range are NOT resolved here -- ANCHORARRAY itself rejects
    /// those (TryResolveAnchorAddress only accepts a single-cell reference), so returning false lets
    /// the caller fall through to the generic per-argument CollectReferences case, same as before.
    /// </summary>
    private static bool TryResolveAnchorForDependencies(
        FormulaNode arg,
        SheetId defaultSheetId,
        FreeX.Core.Model.Workbook? workbook,
        out SheetId anchorSheetId,
        out uint anchorRow,
        out uint anchorCol)
    {
        anchorSheetId = defaultSheetId;
        anchorRow = 0;
        anchorCol = 0;

        switch (arg)
        {
            case CellRefNode { SheetName: null } cellRef:
                anchorRow = cellRef.Row;
                anchorCol = cellRef.ColumnNumber;
                return true;

            case CellRefNode cellRef:
            {
                var targetSheet = workbook?.GetSheet(cellRef.SheetName!);
                if (targetSheet is null)
                    return false;

                anchorSheetId = targetSheet.Id;
                anchorRow = cellRef.Row;
                anchorCol = cellRef.ColumnNumber;
                return true;
            }

            case NamedRangeNode named:
            {
                if (workbook is null)
                    return false;

                // Sheet-scope precedence: a sheet-scoped named FORMULA must shadow a same-named
                // workbook-global named RANGE, mirroring the eval side (ResolveNamedRangeNodeAsReference)
                // and the generic NamedRangeNode dependency case above. A formula-valued name can't be
                // resolved to a static address here, so bail out (falls through to the generic case).
                if (workbook.ScopedNamedFormulas.ContainsKey((named.Name, defaultSheetId)))
                    return false;

                if (!workbook.TryGetNamedRange(named.Name, defaultSheetId, out var namedRange))
                    return false;

                if (namedRange.RowCount != 1 || namedRange.ColCount != 1)
                    return false;

                anchorSheetId = namedRange.Start.Sheet;
                anchorRow = namedRange.Start.Row;
                anchorCol = namedRange.Start.Col;
                return true;
            }

            default:
                return false;
        }
    }

    private static GridRange CreateGridRange(SheetId sheetId, RangeRefNode range)
    {
        var start = new CellAddress(sheetId, range.Start.Row, range.Start.ColumnNumber);
        var end = new CellAddress(sheetId, range.End.Row, range.End.ColumnNumber);
        return new GridRange(start, end);
    }

    /// <summary>Case-insensitive tab-order lookup of a sheet by name, or -1 if not found.</summary>
    private static int FindSheetIndex(FreeX.Core.Model.Workbook workbook, string sheetName)
    {
        var sheets = workbook.Sheets;
        for (var i = 0; i < sheets.Count; i++)
        {
            if (string.Equals(sheets[i].Name, sheetName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static GridRange CreateGridRange(SheetId sheetId, FullColumnRangeRefNode range)
    {
        var start = new CellAddress(sheetId, 1, range.StartColumnNumber);
        var end = new CellAddress(sheetId, CellAddress.MaxRow, range.EndColumnNumber);
        return new GridRange(start, end);
    }

    private static GridRange CreateGridRange(SheetId sheetId, FullRowRangeRefNode range)
    {
        var start = new CellAddress(sheetId, range.StartRow, 1);
        var end = new CellAddress(sheetId, range.EndRow, CellAddress.MaxCol);
        return new GridRange(start, end);
    }

    private readonly struct DependencyPlanCacheKey : IEquatable<DependencyPlanCacheKey>
    {
        private readonly FormulaNode _ast;
        private readonly SheetId _sheetId;

        public DependencyPlanCacheKey(FormulaNode ast, SheetId sheetId)
        {
            _ast = ast;
            _sheetId = sheetId;
        }

        public SheetId SheetId => _sheetId;

        public bool Equals(DependencyPlanCacheKey other) =>
            ReferenceEquals(_ast, other._ast) && _sheetId.Equals(other._sheetId);

        public override bool Equals(object? obj) =>
            obj is DependencyPlanCacheKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(_ast), _sheetId);
    }

    private sealed class FormulaDependencyPlan
    {
        public FormulaDependencyPlan(
            IReadOnlySet<CellAddress> cells,
            IReadOnlyList<GridRange> ranges,
            bool containsVolatileFunction)
        {
            Cells = cells;
            Ranges = ranges;
            ContainsVolatileFunction = containsVolatileFunction;
        }

        public IReadOnlySet<CellAddress> Cells { get; }
        public IReadOnlyList<GridRange> Ranges { get; }
        public bool ContainsVolatileFunction { get; }
    }

    private sealed class FormulaDependencySet
    {
        private List<GridRange>? _ranges;
        private List<CellAddress>? _anchorArrayAnchors;

        public HashSet<CellAddress> Cells { get; } = [];
        public IReadOnlyList<GridRange> Ranges => _ranges is null ? Array.Empty<GridRange>() : _ranges;

        /// <summary>
        /// Anchor cell addresses this formula reads via a live ANCHORARRAY(anchor,end) spill-extent
        /// union (see <see cref="_anchorArraySpillDependents"/>). Populated by the ANCHORARRAY case
        /// in <see cref="CollectReferences"/>.
        /// </summary>
        public IReadOnlyList<CellAddress> AnchorArrayAnchors => _anchorArrayAnchors is null ? Array.Empty<CellAddress>() : _anchorArrayAnchors;

        public void Add(CellAddress address) => Cells.Add(address);

        public void AddAnchorArrayAnchor(CellAddress anchor)
        {
            _anchorArrayAnchors ??= [];
            _anchorArrayAnchors.Add(anchor);
        }

        public void AddRange(GridRange range)
        {
            if (range.CellCount > CompactRangeCellThreshold)
            {
                _ranges ??= [];
                _ranges.Add(range);
                return;
            }

            foreach (var address in range.AllCells())
                Cells.Add(address);
        }

        /// <summary>
        /// Remove a single cell from this dependency set: drop it from the exact cells and split any range
        /// precedent that contains it into the (≤4) rectangular sub-ranges around it. Used so a SUBTOTAL/
        /// AGGREGATE cell does not depend on itself when it sits inside its own referenced range.
        /// </summary>
        public void ExcludeCell(CellAddress cell)
        {
            Cells.Remove(cell);

            if (_ranges is null)
                return;

            var containsCell = false;
            foreach (var range in _ranges)
            {
                if (range.Contains(cell)) { containsCell = true; break; }
            }

            if (!containsCell)
                return;

            var rebuilt = new List<GridRange>(_ranges.Count + 3);
            foreach (var range in _ranges)
            {
                if (range.Contains(cell))
                    AppendRangeMinusCell(range, cell, rebuilt);
                else
                    rebuilt.Add(range);
            }

            _ranges = rebuilt;
        }

        private static void AppendRangeMinusCell(GridRange range, CellAddress cell, List<GridRange> output)
        {
            var sheet = range.Start.Sheet;
            uint r0 = range.Start.Row, r1 = range.End.Row, c0 = range.Start.Col, c1 = range.End.Col;
            uint cr = cell.Row, cc = cell.Col;

            if (cr > r0) // rows above the cell, full width
                output.Add(new GridRange(new CellAddress(sheet, r0, c0), new CellAddress(sheet, cr - 1, c1)));
            if (cr < r1) // rows below the cell, full width
                output.Add(new GridRange(new CellAddress(sheet, cr + 1, c0), new CellAddress(sheet, r1, c1)));
            if (cc > c0) // cells left of the cell, on its row
                output.Add(new GridRange(new CellAddress(sheet, cr, c0), new CellAddress(sheet, cr, cc - 1)));
            if (cc < c1) // cells right of the cell, on its row
                output.Add(new GridRange(new CellAddress(sheet, cr, cc + 1), new CellAddress(sheet, cr, c1)));
        }
    }

}

/// <summary>Report of a recalculation pass.</summary>
public sealed record RecalcReport(
    IReadOnlyList<CellAddress> RecalculatedCells,
    IReadOnlyList<(CellAddress Cell, string Error)> Errors,
    IReadOnlyList<CellAddress> CyclicCells);
