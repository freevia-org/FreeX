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
    // Sane upper bound on chained spill-dependent follow-up passes (see the loop in Recalculate),
    // so a pathological/self-perpetuating chain cannot spin forever; ordinary sheets converge in 1-2.
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

    public RecalcEngine(DependencyGraph graph, FormulaEvaluator evaluator)
    {
        _graph = graph;
        _evaluator = evaluator;
    }

    /// <summary>
    /// Recalculate all cells affected by changes to the given cells.
    /// Returns a report of what was recalculated.
    /// </summary>
    public RecalcReport Recalculate(Workbook workbook, IReadOnlyList<CellAddress> changedCells) =>
        Recalculate(workbook, changedCells, resolveSpillDependents: true);

    private RecalcReport Recalculate(
        Workbook workbook,
        IReadOnlyList<CellAddress> changedCells,
        bool resolveSpillDependents)
    {
        if (changedCells.Count == 0 &&
            _volatileCells.Count == 0 &&
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

        // Include volatile cells in the dependency traversal so their dependents appear in the plan
        var changedForTraversal = BuildChangedSetForTraversal(changedCells);
        var plan = _graph.GetRecalcOrder(changedForTraversal);
        if (plan.OrderedCells.Count == 0 &&
            plan.CyclicCells.Count == 0 &&
            _volatileCells.Count == 0 &&
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
                RunIterativeCalc(workbook, plan.CyclicCells, ref recalculatedCount, ref singleRecalculated, ref recalculated, ref errors);
            }
            else
            {
                foreach (var cyclic in plan.CyclicCells)
                    AddCyclicCell(workbook, cyclic, ref cyclicCells, ref seenCyclicCells, ref errors);
            }
        }

        var evaluationPlan = plan;
        IReadOnlyCollection<CellAddress>? directFormulaRoots = null;
        if (_volatileCells.Count > 0 || changedFormulaCells is not null)
        {
            if (CanEvaluateChangedFormulaRootsDirectly(plan, changedFormulaCells, _volatileCells.Count))
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
                    plan.OrderedCells.Count + _volatileCells.Count + (changedFormulaCells?.Count ?? 0));

                if (changedFormulaCells is not null)
                {
                    foreach (var addr in changedFormulaCells)
                        dirtyCells.Add(addr);
                }

                foreach (var addr in _volatileCells)
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
                evaluationPlan = _graph.GetEvaluationOrder(dirtyCells, deprioritized: _volatileCells);

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
                            RunIterativeCalc(workbook, newCyclicCells, ref recalculatedCount, ref singleRecalculated, ref recalculated, ref errors);
                    }
                    else
                    {
                        foreach (var cyclic in evaluationPlan.CyclicCells)
                            AddCyclicCell(workbook, cyclic, ref cyclicCells, ref seenCyclicCells, ref errors);
                    }
                }
            }
        }

        foreach (var addr in directFormulaRoots ?? evaluationPlan.OrderedCells)
        {
            var sheet = workbook.GetSheet(addr.Sheet);
            if (sheet is null) continue;

            var cell = sheet.GetCell(addr);
            if (cell is null || !cell.HasFormula) continue;

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
                    cell.Value = ImplicitIntersection.Resolve(implicitRange, addr.Row, addr.Col);
                    _spillBlockedAnchors.Remove(addr);
                    AddRecalculatedCell(ref recalculatedCount, ref singleRecalculated, ref recalculated, addr);
                }
                else if (result is RangeValue rv)
                {
                    sheet.ClearSpillRange(addr);
                    if (sheet.IsSpillBlocked(addr, rv.RowCount, rv.ColCount))
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
                else
                {
                    sheet.ClearSpillRange(addr);
                    if (hadSpill)
                    {
                        spillTargetsMayHaveChanged = true;
                        CaptureVacatedSpillCells(addr, priorSpillRows, priorSpillCols, 0, 0, ref vacatedSpillCells);
                    }
                    cell.Value = result;
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
            ResolveSpillTargetDependentsFixpoint(workbook, ref report, vacatedSpillCells);
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
                var retryReport = Recalculate(workbook, retryAnchors, resolveSpillDependents: false);
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
                    ResolveSpillTargetDependentsFixpoint(workbook, ref report);
            }
        }

        // Excel's "Precision as displayed" option (calcPr/@fullPrecision="0") permanently rounds
        // stored numeric values to the precision shown on screen once a workbook uses it, rather
        // than retaining full internal (~15 significant digit) precision. Doing this faithfully
        // requires resolving each cell's effective *displayed* decimal-place count from its number
        // format (and column width / General-format significant-digit rules), which lives in the
        // number-format rendering layer above Core.Calc — RecalcEngine has no such dependency and
        // must not acquire a new cross-tier one just for this. Only apply the top-level (outermost,
        // resolveSpillDependents == true) pass so recursive spill-dependent follow-ups do not redo
        // the (currently minimal) rounding pass redundantly.
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
    /// set of just-recalculated cells.
    /// TODO(N30 follow-up, needs FreeX.Core.Presentation/number-format access which Core.Calc cannot
    /// reference): this currently only clamps stored values to Excel's ~15 significant-digit display
    /// ceiling, which is a no-op for ordinary double-precision results and does not yet round to each
    /// cell's actual displayed decimal count (its number format, e.g. "0.00" -> 2 decimals). A full
    /// fix needs to resolve each cell's effective displayed-decimal-place count (number format +
    /// General-format significant-digit rules) and round to that instead.
    /// </summary>
    private static void ApplyPrecisionAsDisplayed(Workbook workbook, IReadOnlyList<CellAddress> recalculatedCells)
    {
        for (var i = 0; i < recalculatedCells.Count; i++)
        {
            var addr = recalculatedCells[i];
            var sheet = workbook.GetSheet(addr.Sheet);
            var cell = sheet?.GetCell(addr);
            if (cell?.Value is NumberValue { Value: var raw } && double.IsFinite(raw))
                cell.Value = new NumberValue(RoundToSignificantDigits(raw, 15));
        }
    }

    /// <summary>Round <paramref name="value"/> to at most <paramref name="digits"/> significant decimal digits.</summary>
    private static double RoundToSignificantDigits(double value, int digits)
    {
        if (value == 0)
            return 0;

        var scale = digits - (int)Math.Floor(Math.Log10(Math.Abs(value))) - 1;
        if (scale < 0)
        {
            // The value has more integer digits than the significant-digit cap (e.g. an 18-digit
            // integer). Excel does not round such values to the nearest 10^-scale -- it truncates
            // (chops) the excess low-order digits to zero, matching its 15-significant-digit storage
            // cap. Math.Round(double, int) only accepts digits in [0, 15] and cannot express a
            // negative scale, so replicate the truncation directly instead of clamping to a no-op.
            var divisor = Math.Pow(10, -scale);
            return Math.Truncate(value / divisor) * divisor;
        }

        // Math.Round(double,int) only accepts digits in [0, 15]; a small-magnitude value (|value| <
        // 0.1) gives scale > 15, which would throw. A double already carries at most ~15-17
        // significant digits, so rounding at the 15th place is a safe no-op for those values.
        return Math.Round(value, Math.Min(scale, 15), MidpointRounding.AwayFromZero);
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
                // Cached AST present, but this address may still be unregistered (shared-reference
                // clone case above) — register it under its own address without re-parsing.
                if (!_graph.HasDependencies(addr))
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

    private IEnumerable<CellAddress> BuildChangedSetForTraversal(IReadOnlyList<CellAddress> changedCells)
    {
        if (_volatileCells.Count == 0)
            return changedCells;

        var allChanged = new List<CellAddress>(changedCells.Count + _volatileCells.Count);
        foreach (var addr in changedCells)
            allChanged.Add(addr);
        foreach (var addr in _volatileCells)
            allChanged.Add(addr);
        return allChanged;
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

    private static void AddCyclicCell(
        Workbook workbook,
        CellAddress cyclic,
        ref List<CellAddress>? cyclicCells,
        ref HashSet<CellAddress>? seenCyclicCells,
        ref List<(CellAddress Cell, string Error)>? errors)
    {
        seenCyclicCells ??= [];
        if (!seenCyclicCells.Add(cyclic))
            return;

        (cyclicCells ??= []).Add(cyclic);

        var sheet = workbook.GetSheet(cyclic.Sheet);
        if (sheet is null) return;

        var cell = sheet.GetCell(cyclic);
        if (cell is null) return;

        cell.Value = ErrorValue.Circular;
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
        ref List<(CellAddress Cell, string Error)>? errors)
    {
        const int DefaultMaxIterations = 100;
        const double DefaultMaxChange = 0.001;

        var maxIterations = workbook.MaxCalculationIterations ?? DefaultMaxIterations;
        // Guard: ensure a sane positive cap even if caller supplies 0 or negative.
        if (maxIterations <= 0) maxIterations = DefaultMaxIterations;

        var maxChange = workbook.MaxCalculationChange ?? DefaultMaxChange;

        // Seed: cells that previously received #CIRCULAR! get reset to 0 (blank) so the first
        // iteration starts from the Excel-compatible seed value, not from a prior error.
        for (var i = 0; i < cyclicAddresses.Count; i++)
        {
            var addr = cyclicAddresses[i];
            var seedSheet = workbook.GetSheet(addr.Sheet);
            var seedCell = seedSheet?.GetCell(addr);
            if (seedCell is not null && ReferenceEquals(seedCell.Value, ErrorValue.Circular))
                seedCell.Value = new BlankValue();
        }

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var maxAbsChange = 0.0;

            for (var i = 0; i < cyclicAddresses.Count; i++)
            {
                var addr = cyclicAddresses[i];
                var sheet = workbook.GetSheet(addr.Sheet);
                if (sheet is null) continue;

                var cell = sheet.GetCell(addr);
                if (cell is null || !cell.HasFormula) continue;

                // Snapshot the value before this eval so we can compute the delta.
                var prevNumeric = ToNumericForConvergence(cell.Value);

                try
                {
                    if (cell.CachedAst is not FormulaNode cachedAst)
                    {
                        cachedAst = FormulaEvaluator.ParseFormula(cell.FormulaText!);
                        cell.CachedAst = cachedAst;
                        RegisterFormulaDependencies(addr, cachedAst, addr.Sheet, workbook);
                    }

                    var result = cell.ArrayMode == FormulaArrayMode.Dynamic
                        ? _evaluator.EvaluateSpilling(cachedAst, sheet, workbook, addr)
                        : _evaluator.Evaluate(cachedAst, sheet, workbook, addr);

                    // Iterative calc does not support spilling out of cyclic cells — use the
                    // scalar value at [0,0] to stay safe.
                    cell.Value = result is RangeValue rv ? rv.Cells[0, 0] : result;
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

                var newNumeric = ToNumericForConvergence(cell.Value);
                var delta = Math.Abs(newNumeric - prevNumeric);
                // Guard: NaN/Infinity in the delta (e.g. the formula produced #DIV/0! on this pass)
                // must not prevent termination — treat them as "large but not infinite" change so
                // the loop simply runs to maxIterations and returns the last value.
                if (double.IsFinite(delta) && delta > maxAbsChange)
                    maxAbsChange = delta;
            }

            // Converged: every cell changed by <= maxChange on this pass.
            if (maxAbsChange <= maxChange)
                break;
        }

        // Record the cyclic cells as recalculated (not as errors/cyclic).
        for (var i = 0; i < cyclicAddresses.Count; i++)
            AddRecalculatedCell(ref recalculatedCount, ref singleRecalculated, ref recalculated, cyclicAddresses[i]);
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
            namedFormulaStack: null);

        if (containsSelfExcludingCall &&
            IsCellExcludedBySelfExcludingCall(ast, sheetId, formulaCell, workbook))
        {
            refs.ExcludeCell(formulaCell);
        }

        _graph.SetDependencies(formulaCell, refs.Cells, refs.Ranges);

        SetVolatileTracking(formulaCell, containsVolatileFunction);

        if (cacheableForDependencyPlan && !containsSelfExcludingCall)
            AddDependencyPlanToCache(cacheKey, refs, containsVolatileFunction);
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
        if (_dependencyPlanCache.Count >= MaxDependencyPlanCacheEntries &&
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

    /// <summary>Rebuild dependencies and evaluate every formula cell in the workbook.</summary>
    public RecalcReport RecalculateAllFormulas(Workbook workbook)
    {
        RebuildFormulaDependencies(workbook);
        var formulaCells = CollectFormulaCells(workbook);

        // Recalculate runs the spill-target dependent follow-up pass itself (see the
        // spillTargetsMayHaveChanged path), so no separate second pass is needed here.
        return Recalculate(workbook, formulaCells);
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
        IReadOnlyList<CellAddress>? vacatedSpillCells = null)
    {
        // Track, per dependent cell, how many distinct spill-target precedents it read the
        // last time it was scheduled. A cell must only be skipped as "already handled" if its
        // spill-target input count has not grown since — otherwise a cell that depends on two
        // spill targets from different "generations" (one resolved this pass, another that only
        // materializes in a later pass) would be permanently skipped after its first, incomplete
        // evaluation, keeping a stale value forever. See finding H3.
        var seenSpillDependentInputCounts = new Dictionary<CellAddress, int>();
        for (var pass = 0; pass < MaxSpillDependentPasses; pass++)
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

            var spillReport = Recalculate(workbook, spillDependents, resolveSpillDependents: false);
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
            var report = Recalculate(workbook, formulaCells);
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
        HashSet<string>? namedFormulaStack)
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
                cacheableForDependencyPlan = false;
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
                    workbook.ScopedNamedFormulas.ContainsKey((named.Name, defaultSheetId));

                if (!sheetScopedIsFormula &&
                    workbook is not null && workbook.TryGetNamedRange(named.Name, defaultSheetId, out var namedRange))
                {
                    refs.AddRange(namedRange);
                    return false;
                }

                var formulaText = workbook?.TryGetNamedFormulaText(named.Name, defaultSheetId);
                if (formulaText is not null && !string.IsNullOrWhiteSpace(formulaText))
                {
                    namedFormulaStack ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!namedFormulaStack.Add(named.Name))
                        return false;

                    try
                    {
                        var namedAst = FormulaEvaluator.ParseFormula(formulaText);
                        return CollectReferences(
                            namedAst,
                            defaultSheetId,
                            formulaCell,
                            workbook,
                            refs,
                            ref cacheableForDependencyPlan,
                            namedFormulaStack);
                    }
                    catch (FormulaParseException)
                    {
                        return false;
                    }
                    finally
                    {
                        namedFormulaStack.Remove(named.Name);
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
                var leftHasVolatile = CollectReferences(binary.Left, defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack);
                var rightHasVolatile = CollectReferences(binary.Right, defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack);
                return leftHasVolatile || rightHasVolatile;
            }

            case UnaryOpNode unary:
                return CollectReferences(unary.Operand, defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack);

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
                return false;
            }

            case FunctionCallNode func:
            {
                var containsVolatileFunction = IsVolatileFunctionName(func.FunctionName);
                var arguments = func.Arguments;
                for (var i = 0; i < arguments.Count; i++)
                {
                    if (CollectReferences(arguments[i], defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan, namedFormulaStack))
                        containsVolatileFunction = true;
                }

                return containsVolatileFunction;
            }
        }

        return false;
    }

    private static bool IsVolatileFunctionName(string name) =>
        name is "NOW" or "TODAY" or "RAND" or "RANDBETWEEN" or "RANDARRAY" or "INDIRECT" or "OFFSET" or "CELL" or "INFO";

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

        public HashSet<CellAddress> Cells { get; } = [];
        public IReadOnlyList<GridRange> Ranges => _ranges is null ? Array.Empty<GridRange>() : _ranges;

        public void Add(CellAddress address) => Cells.Add(address);

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
