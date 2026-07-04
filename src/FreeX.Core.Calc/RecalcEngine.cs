using System.Collections.Frozen;
using System.Runtime.CompilerServices;
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
        if (changedCells.Count == 0 && _volatileCells.Count == 0)
            return EmptyReport;

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
            changedFormulaCells is null)
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

                evaluationPlan = _graph.GetEvaluationOrder(dirtyCells);
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
            var hadSpill = sheet.HasSpillValues && sheet.TryGetSpillExtent(addr, out _, out _);

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
                    if (hadSpill) spillTargetsMayHaveChanged = true;
                    cell.Value = ImplicitIntersection.Resolve(implicitRange, addr.Row, addr.Col);
                    AddRecalculatedCell(ref recalculatedCount, ref singleRecalculated, ref recalculated, addr);
                }
                else if (result is RangeValue rv)
                {
                    sheet.ClearSpillRange(addr);
                    if (sheet.IsSpillBlocked(addr, rv.RowCount, rv.ColCount))
                    {
                        cell.Value = ErrorValue.Spill;
                        if (hadSpill) spillTargetsMayHaveChanged = true;
                        AddError(ref errors, addr, "#SPILL!");
                    }
                    else
                    {
                        cell.Value = rv.Cells[0, 0];
                        sheet.SetSpillRange(addr, rv);
                        spillTargetsMayHaveChanged = true;
                        AddRecalculatedCell(ref recalculatedCount, ref singleRecalculated, ref recalculated, addr);
                    }
                }
                else
                {
                    sheet.ClearSpillRange(addr);
                    if (hadSpill) spillTargetsMayHaveChanged = true;
                    cell.Value = result;
                    AddRecalculatedCell(ref recalculatedCount, ref singleRecalculated, ref recalculated, addr);
                }
            }
            catch (FormulaParseException)
            {
                cell.CachedAst = null;
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
                if (hadSpill) spillTargetsMayHaveChanged = true;
                cell.Value = ErrorValue.Value;
                AddError(ref errors, addr, "#VALUE!");
            }
            catch (FormulaEvalException ex)
            {
                sheet.ClearSpillRange(addr);
                if (hadSpill) spillTargetsMayHaveChanged = true;
                cell.Value = new ErrorValue(ex.ErrorCode);
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
                if (hadSpill) spillTargetsMayHaveChanged = true;
                cell.Value = ErrorValue.Value;
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
            // Track, per dependent cell, how many distinct spill-target precedents it read the
            // last time it was scheduled. A cell must only be skipped as "already handled" if its
            // spill-target input count has not grown since — otherwise a cell that depends on two
            // spill targets from different "generations" (one resolved this pass, another that only
            // materializes in a later pass) would be permanently skipped after its first, incomplete
            // evaluation, keeping a stale value forever. See finding H3.
            var seenSpillDependentInputCounts = new Dictionary<CellAddress, int>();
            for (var pass = 0; pass < MaxSpillDependentPasses; pass++)
            {
                var spillDependents = CollectSpillTargetDependentFormulaCells(workbook, out var inputCounts);
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

        return report;
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
    /// Heuristic match for a formula that references another workbook, e.g. <c>[Book1.xlsx]Sheet1!A1</c>
    /// or a defined-name form like <c>[1]Sheet1!A1</c>. The Lexer/Parser have no concept of this OOXML
    /// external-reference syntax (a bracketed workbook token immediately followed by a sheet-qualified
    /// cell/range reference), so parsing such a formula always throws <see cref="FormulaParseException"/>
    /// — even though the file's Excel-computed cached value was loaded correctly and is still perfectly
    /// valid. This mirrors the same bracket heuristic <c>XlsxClosedXmlCellMapper.ShouldUseCachedExternalFormulaValue</c>
    /// uses to decide whether to trust ClosedXML's cached value at load time, so recalc and load agree
    /// on which formulas are "external" for this purpose.
    /// </summary>
    private static bool IsLikelyExternalWorkbookReferenceFormula(string? formulaText) =>
        formulaText is not null && formulaText.Contains('[', StringComparison.Ordinal);

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
        var excludeSelf = IsSubtotalOrAggregateRoot(ast);

        var cacheKey = new DependencyPlanCacheKey(ast, sheetId);
        if (!excludeSelf && _dependencyPlanCache.TryGetValue(cacheKey, out var cachedPlan))
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

        if (excludeSelf)
            refs.ExcludeCell(formulaCell);

        _graph.SetDependencies(formulaCell, refs.Cells, refs.Ranges);

        SetVolatileTracking(formulaCell, containsVolatileFunction);

        if (cacheableForDependencyPlan && !excludeSelf)
            AddDependencyPlanToCache(cacheKey, refs, containsVolatileFunction);
    }

    private static bool IsSubtotalOrAggregateRoot(FormulaNode ast) =>
        ast is FunctionCallNode func &&
        (string.Equals(func.FunctionName, "SUBTOTAL", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(func.FunctionName, "AGGREGATE", StringComparison.OrdinalIgnoreCase));

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
        _graph.ClearAll();
        _volatileCells.Clear();
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
    /// Collect the set of formula cells that directly reference at least one spill-target cell
    /// (a cell whose value comes from another formula's spill, not from its own formula).
    /// These are the cells that may have received an incorrect blank in the first recalc pass.
    /// Also reports, per dependent cell, the number of distinct spill-target cells it currently
    /// reads — callers use this to detect a dependent that has gained a new spill-target input
    /// since it was last scheduled (e.g. a chained spill that materializes on a later pass), which
    /// must not be treated as "already handled" even though its address was seen before.
    /// </summary>
    private List<CellAddress> CollectSpillTargetDependentFormulaCells(
        Workbook workbook,
        out Dictionary<CellAddress, int> inputCounts)
    {
        List<CellAddress>? result = null;
        var counts = new Dictionary<CellAddress, int>();

        foreach (var sheet in workbook.Sheets)
        {
            if (!sheet.HasSpillValues)
                continue;

            foreach (var spillTarget in sheet.EnumerateSpillTargetCells())
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
        }

        inputCounts = counts;
        return result ?? [];
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

    /// <summary>Rebuild dependencies and evaluate formula cells on a single worksheet.</summary>
    public RecalcReport RecalculateSheetFormulas(Workbook workbook, SheetId sheetId)
    {
        RebuildFormulaDependencies(workbook);
        var sheet = workbook.GetSheet(sheetId);
        if (sheet is null)
            return new RecalcReport([], [], []);

        var formulaCells = CollectFormulaCells(sheet);

        var report = Recalculate(workbook, formulaCells);
        return FilterReportForSheet(report, sheetId);
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
                // Sheet-scope-first: a name scoped to defaultSheetId takes precedence
                // over a same-named workbook-global name (Excel rule §18.2.6).
                if (workbook is not null && workbook.TryGetNamedRange(named.Name, defaultSheetId, out var namedRange))
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
