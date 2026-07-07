using System.Collections.Frozen;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

/// <summary>
/// Tracks cell-to-cell dependencies and performs topological recalculation.
/// The engine owns the dependency graph — we never trust external calc chains.
/// </summary>
public sealed class DependencyGraph
{
    // Cell -> set of cells it depends on (precedents)
    private readonly Dictionary<CellAddress, IReadOnlySet<CellAddress>> _precedents = [];

    // Cell -> cells that depend on it. Exact dependency inputs are deduplicated before insertion.
    // HashSet gives O(1) Remove instead of List's O(n), which matters during bulk formula rewrites.
    private readonly Dictionary<CellAddress, HashSet<CellAddress>> _dependents = [];

    // Cell -> compact range precedents it depends on.
    private readonly Dictionary<CellAddress, IReadOnlyList<GridRange>> _rangePrecedents = [];

    // Sheet -> compact range index and the cells that depend on those ranges.
    private readonly Dictionary<SheetId, RangeDependencyIndex> _rangeDependentsBySheet = [];

    /// <summary>
    /// Set the dependencies for a cell (what cells its formula references).
    /// Replaces any previous dependencies.
    /// </summary>
    public void SetDependencies(CellAddress cell, IEnumerable<CellAddress> precedents)
    {
        SetDependenciesFromOwnedSet(cell, new HashSet<CellAddress>(precedents));
    }

    /// <summary>
    /// Set dependencies using a fresh, caller-owned set that will not be mutated after transfer.
    /// </summary>
    internal void SetDependenciesFromOwnedSet(CellAddress cell, HashSet<CellAddress> precedents)
        => SetDependenciesCore(cell, precedents, Array.Empty<GridRange>(), copyRangePrecedents: false);

    /// <summary>
    /// Set dependencies from cached immutable templates.
    /// </summary>
    internal void SetDependenciesFromTemplate(
        CellAddress cell,
        IReadOnlySet<CellAddress> precedents,
        IReadOnlyList<GridRange> rangePrecedents)
        => SetDependenciesCore(cell, precedents, rangePrecedents, copyRangePrecedents: false);

    /// <summary>
    /// Set dependencies using a fresh, caller-owned set plus compact range precedents.
    /// </summary>
    internal void SetDependencies(
        CellAddress cell,
        HashSet<CellAddress> precedents,
        IReadOnlyList<GridRange> rangePrecedents)
        => SetDependenciesCore(cell, precedents, rangePrecedents, copyRangePrecedents: true);

    private void SetDependenciesCore(
        CellAddress cell,
        IReadOnlySet<CellAddress> precedents,
        IReadOnlyList<GridRange> rangePrecedents,
        bool copyRangePrecedents)
    {
        ClearDependencies(cell);

        _precedents[cell] = precedents;
        if (rangePrecedents.Count > 0)
        {
            var ranges = copyRangePrecedents
                ? new List<GridRange>(rangePrecedents)
                : rangePrecedents;
            _rangePrecedents[cell] = ranges;

            foreach (var range in ranges)
            {
                if (!_rangeDependentsBySheet.TryGetValue(range.Start.Sheet, out var deps))
                {
                    deps = new RangeDependencyIndex();
                    _rangeDependentsBySheet[range.Start.Sheet] = deps;
                }
                deps.Add(new RangeDependency(range, cell));
            }
        }

        AddDependentLinks(precedents, cell);
    }

    /// <summary>Remove all dependencies for a cell.</summary>
    public void ClearDependencies(CellAddress cell)
    {
        if (_precedents.TryGetValue(cell, out var oldPrecs))
        {
            RemoveDependentLinks(oldPrecs, cell);
            _precedents.Remove(cell);
        }

        if (_rangePrecedents.Remove(cell, out var oldRanges))
        {
            foreach (var range in oldRanges)
            {
                if (!_rangeDependentsBySheet.TryGetValue(range.Start.Sheet, out var deps))
                    continue;

                deps.Remove(new RangeDependency(range, cell));
                if (deps.Count == 0)
                    _rangeDependentsBySheet.Remove(range.Start.Sheet);
            }
        }
    }

    private void AddDependentLinks(IReadOnlySet<CellAddress> precedents, CellAddress cell)
    {
        if (precedents is HashSet<CellAddress> hashSet)
        {
            foreach (var prec in hashSet)
                AddDependentLink(prec, cell);
            return;
        }

        if (precedents is FrozenSet<CellAddress> frozenSet)
        {
            foreach (var prec in frozenSet)
                AddDependentLink(prec, cell);
            return;
        }

        foreach (var prec in precedents)
            AddDependentLink(prec, cell);
    }

    private void AddDependentLink(CellAddress precedent, CellAddress cell)
    {
        if (!_dependents.TryGetValue(precedent, out var deps))
        {
            deps = [];
            _dependents[precedent] = deps;
        }

        deps.Add(cell);
    }

    private void RemoveDependentLinks(IReadOnlySet<CellAddress> precedents, CellAddress cell)
    {
        if (precedents is HashSet<CellAddress> hashSet)
        {
            foreach (var prec in hashSet)
                RemoveDependentLink(prec, cell);
            return;
        }

        if (precedents is FrozenSet<CellAddress> frozenSet)
        {
            foreach (var prec in frozenSet)
                RemoveDependentLink(prec, cell);
            return;
        }

        foreach (var prec in precedents)
            RemoveDependentLink(prec, cell);
    }

    private void RemoveDependentLink(CellAddress precedent, CellAddress cell)
    {
        if (!_dependents.TryGetValue(precedent, out var deps))
            return;

        deps.Remove(cell);
        if (deps.Count == 0)
            _dependents.Remove(precedent);
    }

    /// <summary>Remove every dependency edge from the graph.</summary>
    public void ClearAll()
    {
        _precedents.Clear();
        _dependents.Clear();
        _rangePrecedents.Clear();
        _rangeDependentsBySheet.Clear();
    }

    internal void EnsureFormulaCapacity(int formulaCount)
    {
        if (formulaCount <= 0)
            return;

        _precedents.EnsureCapacity(formulaCount);
        _rangePrecedents.EnsureCapacity(formulaCount);
    }

    private static readonly IReadOnlySet<CellAddress> EmptySet =
        new HashSet<CellAddress>().ToFrozenSet();

    private static readonly RecalcPlan EmptyPlan = new([], []);

    /// <summary>Get all cells that directly depend on the given cell.</summary>
    public IReadOnlySet<CellAddress> GetDirectDependents(CellAddress cell)
    {
        var rangeDeps = CollectRangeDependents(cell);
        if (rangeDeps is null)
            return _dependents.TryGetValue(cell, out var deps) ? new HashSet<CellAddress>(deps) : EmptySet;

        var allDeps = _dependents.TryGetValue(cell, out var exactDeps)
            ? new HashSet<CellAddress>(exactDeps)
            : [];
        allDeps.UnionWith(rangeDeps);
        return allDeps;
    }

    /// <summary>Get all exact cells that the given cell directly references.</summary>
    public IReadOnlySet<CellAddress> GetDirectPrecedents(CellAddress cell)
    {
        return _precedents.TryGetValue(cell, out var precs) ? precs : EmptySet;
    }

    /// <summary>
    /// True if this cell already has a precedents entry in the graph (exact or range), i.e. its
    /// formula has actually been registered here — as opposed to merely holding a cached AST, which
    /// can be true for a cell whose cached AST was copied by reference from another cell (e.g.
    /// Cell.Clone(), as happens for a zero-delta paste) without RegisterFormulaDependencies ever
    /// having been called for this specific address.
    /// </summary>
    public bool HasDependencies(CellAddress cell) =>
        _precedents.ContainsKey(cell) || _rangePrecedents.ContainsKey(cell);

    /// <summary>Get all compact ranges that the given cell directly references.</summary>
    public IReadOnlyList<GridRange> GetDirectRangePrecedents(CellAddress cell)
    {
        return _rangePrecedents.TryGetValue(cell, out var ranges) ? ranges : [];
    }

    /// <summary>
    /// Get all cells that need recalculation when the given cells change,
    /// in topological order. Detects cycles.
    /// </summary>
    public RecalcPlan GetRecalcOrder(IEnumerable<CellAddress> changedCells)
    {
        if (changedCells is IReadOnlyList<CellAddress> changedList)
        {
            if (TryBuildSingleRootExactChainPlan(changedList, out var chainPlan))
                return chainPlan;

            if (TryBuildSingleLeafExactDependentPlan(changedList, out var leafPlan))
                return leafPlan;

            if (TryBuildSingleLeafRangeDependentPlan(changedList, out var rangeLeafPlan))
                return rangeLeafPlan;

            if (changedList.Count == 0 || !HasAnyDependents(changedList))
                return EmptyPlan;
        }

        var toRecalc = new HashSet<CellAddress>();

        // BFS to find all transitive dependents
        var queue = new Queue<CellAddress>(changedCells);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            EnqueueUnvisitedDependents(cell, toRecalc, queue);
        }

        // Topological sort via Kahn's algorithm — build a candidate index once to accelerate
        // CountPrecedentsWithin for cells that have compact range precedents.
        var candidateIndex = CandidateIndex.Build(toRecalc);
        var inDegree = new Dictionary<CellAddress, int>(toRecalc.Count);
        foreach (var cell in toRecalc)
            inDegree[cell] = 0;

        foreach (var cell in toRecalc)
        {
            inDegree[cell] = CountPrecedentsWithin(cell, toRecalc, candidateIndex);
        }

        var sorted = new List<CellAddress>(toRecalc.Count);
        var ready = new Queue<CellAddress>();

        foreach (var (cell, degree) in inDegree)
        {
            if (degree == 0)
                ready.Enqueue(cell);
        }

        while (ready.Count > 0)
        {
            var cell = ready.Dequeue();
            sorted.Add(cell);

            DecrementDependentInDegrees(cell, inDegree, ready);
        }

        ResolveResidualAfterKahn(inDegree, toRecalc, candidateIndex, sorted, out var cycles);

        return new RecalcPlan(sorted, cycles ?? []);
    }

    private bool TryBuildSingleRootExactChainPlan(
        IReadOnlyList<CellAddress> changedCells,
        out RecalcPlan plan)
    {
        plan = EmptyPlan;
        if (changedCells.Count != 1 || _rangeDependentsBySheet.Count != 0)
            return false;

        var root = changedCells[0];
        if (!_dependents.TryGetValue(root, out var dependents) || dependents.Count == 0)
            return true;

        if (dependents.Count != 1)
            return false;

        var first = FirstOf(dependents);
        if (!TryCountSingleRootExactChain(first, out var count))
            return false;

        var ordered = new List<CellAddress>(count);
        var current = first;
        for (var i = 0; i < count; i++)
        {
            ordered.Add(current);
            if (i + 1 < count)
                current = FirstOf(_dependents[current]);
        }

        plan = new RecalcPlan(ordered, []);
        return true;
    }

    private bool TryCountSingleRootExactChain(CellAddress first, out int count)
    {
        count = 0;
        var current = first;
        var maxSteps = Math.Max(_precedents.Count, _dependents.Count) + 1;
        while (true)
        {
            if (++count > maxSteps)
                return false;

            if (!_dependents.TryGetValue(current, out var dependents) || dependents.Count == 0)
                return true;

            if (dependents.Count != 1)
                return false;

            current = FirstOf(dependents);
        }
    }

    private bool TryBuildSingleLeafExactDependentPlan(
        IReadOnlyList<CellAddress> changedCells,
        out RecalcPlan plan)
    {
        plan = EmptyPlan;
        if (changedCells.Count == 0 || _rangeDependentsBySheet.Count != 0)
            return false;

        var hasDependent = false;
        var leaf = default(CellAddress);
        for (var i = 0; i < changedCells.Count; i++)
        {
            if (!_dependents.TryGetValue(changedCells[i], out var dependents) || dependents.Count == 0)
                continue;

            foreach (var dependent in dependents)
            {
                if (!hasDependent)
                {
                    leaf = dependent;
                    hasDependent = true;
                    continue;
                }

                if (!dependent.Equals(leaf))
                    return false;
            }
        }

        if (!hasDependent)
            return false;

        if (_dependents.TryGetValue(leaf, out var downstream) && downstream.Count != 0)
            return false;

        plan = new RecalcPlan([leaf], []);
        return true;
    }

    private bool TryBuildSingleLeafRangeDependentPlan(
        IReadOnlyList<CellAddress> changedCells,
        out RecalcPlan plan)
    {
        plan = EmptyPlan;
        if (changedCells.Count != 1)
            return false;

        var root = changedCells[0];
        var hasDependent = false;
        var dependent = default(CellAddress);

        if (_dependents.TryGetValue(root, out var exactDependents))
        {
            foreach (var dep in exactDependents)
            {
                if (!TryCollectSingleDependent(dep, ref hasDependent, ref dependent))
                    return false;
            }
        }

        if (_rangeDependentsBySheet.TryGetValue(root.Sheet, out var rangeDependents))
        {
            if (!TryCollectSingleRangeDependent(
                    root,
                    rangeDependents.GetRowCandidates(root.Row),
                    ref hasDependent,
                    ref dependent) ||
                !TryCollectSingleRangeDependent(
                    root,
                    rangeDependents.GetColumnCandidates(root.Col),
                    ref hasDependent,
                    ref dependent))
            {
                return false;
            }
        }

        if (!hasDependent || HasAnyDependent(dependent))
            return false;

        plan = new RecalcPlan([dependent], []);
        return true;
    }

    /// <summary>
    /// Topologically order a known dirty set, including the dirty roots themselves.
    /// </summary>
    public RecalcPlan GetEvaluationOrder(IReadOnlyCollection<CellAddress> dirtyCells) =>
        GetEvaluationOrder(dirtyCells, deprioritized: null);

    /// <summary>
    /// Topologically order a known dirty set, including the dirty roots themselves. When
    /// <paramref name="deprioritized"/> is given, a cell in that set that ties for in-degree-0
    /// readiness against a cell NOT in that set always loses the tie (the non-deprioritized cell is
    /// dequeued first). This does not change the graph's real precedent/dependent edges or any
    /// ordering they imply — a deprioritized cell that is a genuine precedent of another candidate
    /// still unblocks (and is still correctly ordered before) that dependent exactly as before; the
    /// bias only resolves ties between cells that have NO edge between them, where Kahn's ready-queue
    /// order would otherwise be arbitrary HashSet/enqueue-order happenstance.
    ///
    /// Used for volatile-function cells (OFFSET/INDIRECT/...), which can dynamically read a cell
    /// that has no registered dependency edge back to them (P78): without this bias such a cell can
    /// tie for readiness with, and run before, an unrelated dirty cell it dynamically reads this pass.
    /// </summary>
    public RecalcPlan GetEvaluationOrder(
        IReadOnlyCollection<CellAddress> dirtyCells,
        IReadOnlyCollection<CellAddress>? deprioritized)
    {
        if (dirtyCells.Count == 0)
            return EmptyPlan;

        var candidates = dirtyCells as HashSet<CellAddress> ?? new HashSet<CellAddress>(dirtyCells);

        // Build a candidate index once so CountPrecedentsWithin avoids O(|candidates|) scans
        // for each cell with compact range precedents.
        var candidateIndex = CandidateIndex.Build(candidates);
        var inDegree = new Dictionary<CellAddress, int>(candidates.Count);
        foreach (var cell in candidates)
            inDegree[cell] = CountPrecedentsWithin(cell, candidates, candidateIndex);

        var sorted = new List<CellAddress>(candidates.Count);
        var ready = new Queue<CellAddress>();
        foreach (var (cell, degree) in inDegree)
        {
            if (degree == 0)
                ready.Enqueue(cell);
        }

        if (deprioritized is null || deprioritized.Count == 0)
        {
            while (ready.Count > 0)
            {
                var cell = ready.Dequeue();
                sorted.Add(cell);

                DecrementDependentInDegrees(cell, inDegree, ready);
            }
        }
        else
        {
            var deprioritizedSet = deprioritized as HashSet<CellAddress> ?? new HashSet<CellAddress>(deprioritized);

            // Rotate past any deprioritized cell at the front of the queue as long as a
            // non-deprioritized cell is also currently ready (bounded by ready.Count rotations —
            // once every remaining ready cell is deprioritized, the rotation stops and one is taken).
            while (ready.Count > 0)
            {
                var rotations = 0;
                while (deprioritizedSet.Contains(ready.Peek()) && rotations < ready.Count)
                {
                    ready.Enqueue(ready.Dequeue());
                    rotations++;
                }

                var cell = ready.Dequeue();
                sorted.Add(cell);

                DecrementDependentInDegrees(cell, inDegree, ready);
            }
        }

        ResolveResidualAfterKahn(inDegree, candidates, candidateIndex, sorted, out var cycles);

        return new RecalcPlan(sorted, cycles ?? []);
    }

    /// <summary>
    /// After Kahn's algorithm stalls, every cell left with in-degree &gt; 0 is "residual" — but
    /// residual does not mean "circular". A genuine cycle only exists among cells that are
    /// mutually reachable from one another (a strongly-connected component of size &gt; 1, or a
    /// self-reference). Cells that merely depend on a cyclic cell, directly or transitively, are
    /// stuck in the residual set too (their in-degree never reaches zero because one of their
    /// precedents is itself stuck) but are NOT part of any cycle — they must still evaluate (and
    /// naturally inherit the propagated error from their cyclic input) rather than being stamped
    /// #CIRCULAR! themselves.
    ///
    /// This runs Tarjan's SCC algorithm over the residual subgraph (edges restricted to
    /// <paramref name="candidates"/>) to separate true cycle members from their downstream
    /// dependents, then topologically appends the downstream dependents to <paramref name="sorted"/>
    /// so they still get evaluated.
    /// </summary>
    private void ResolveResidualAfterKahn(
        Dictionary<CellAddress, int> inDegree,
        HashSet<CellAddress> candidates,
        CandidateIndex candidateIndex,
        List<CellAddress> sorted,
        out List<CellAddress>? cycles)
    {
        cycles = null;

        List<CellAddress>? residual = null;
        foreach (var (cell, degree) in inDegree)
        {
            if (degree > 0)
            {
                residual ??= [];
                residual.Add(cell);
            }
        }

        if (residual is null)
            return;

        var cyclicMembers = FindCyclicMembers(residual, candidates, candidateIndex);
        if (cyclicMembers.Count == 0)
        {
            // Defensive: Kahn only stalls when a cycle exists, so this should not happen. If it
            // somehow does, fall back to the previous behaviour rather than silently dropping cells.
            cycles = residual;
            return;
        }

        cycles = new List<CellAddress>(cyclicMembers.Count);
        foreach (var cell in residual)
        {
            if (cyclicMembers.Contains(cell))
                cycles.Add(cell);
        }

        // The downstream (non-cyclic) residual cells still need a valid evaluation order. Re-run
        // Kahn over just those cells, treating cyclic members as already "satisfied" precedents
        // (their edges are excluded, matching how RecalcEngine will feed them the propagated error
        // from the cyclic input rather than waiting on it).
        var downstream = new List<CellAddress>();
        foreach (var cell in residual)
        {
            if (!cyclicMembers.Contains(cell))
                downstream.Add(cell);
        }

        if (downstream.Count == 0)
            return;

        AppendTopologicalOrder(downstream, cyclicMembers, candidates, candidateIndex, sorted);
    }

    /// <summary>
    /// Find the cells within <paramref name="residual"/> that are members of an actual cycle:
    /// a strongly-connected component of size &gt; 1, or a single cell that depends on itself
    /// (directly or via a compact range that includes its own address). Uses Tarjan's algorithm,
    /// with precedent edges restricted to <paramref name="candidates"/> (mirroring the in-degree
    /// computation Kahn's algorithm used).
    /// </summary>
    private HashSet<CellAddress> FindCyclicMembers(
        List<CellAddress> residual,
        HashSet<CellAddress> candidates,
        CandidateIndex candidateIndex)
    {
        var residualSet = new HashSet<CellAddress>(residual);
        var index = new Dictionary<CellAddress, int>(residual.Count);
        var lowLink = new Dictionary<CellAddress, int>(residual.Count);
        var onStack = new HashSet<CellAddress>();
        var stack = new Stack<CellAddress>();
        var nextIndex = 0;
        var cyclicMembers = new HashSet<CellAddress>();

        // Iterative Tarjan to avoid stack-overflow risk on long dependency chains.
        foreach (var start in residual)
        {
            if (index.ContainsKey(start))
                continue;

            var work = new Stack<TarjanFrame>();
            work.Push(new TarjanFrame(start, GetPrecedentsWithin(start, residualSet, candidateIndex).GetEnumerator()));
            index[start] = nextIndex;
            lowLink[start] = nextIndex;
            nextIndex++;
            stack.Push(start);
            onStack.Add(start);

            while (work.Count > 0)
            {
                var frame = work.Peek();
                var cell = frame.Cell;

                if (frame.Precedents.MoveNext())
                {
                    var prec = frame.Precedents.Current;
                    if (!index.ContainsKey(prec))
                    {
                        index[prec] = nextIndex;
                        lowLink[prec] = nextIndex;
                        nextIndex++;
                        stack.Push(prec);
                        onStack.Add(prec);
                        work.Push(new TarjanFrame(prec, GetPrecedentsWithin(prec, residualSet, candidateIndex).GetEnumerator()));
                    }
                    else if (onStack.Contains(prec))
                    {
                        lowLink[cell] = Math.Min(lowLink[cell], index[prec]);
                    }
                }
                else
                {
                    work.Pop();

                    if (work.Count > 0)
                    {
                        var parent = work.Peek().Cell;
                        lowLink[parent] = Math.Min(lowLink[parent], lowLink[cell]);
                    }

                    if (lowLink[cell] == index[cell])
                    {
                        // Pop the SCC rooted at `cell`.
                        var members = new List<CellAddress>();
                        CellAddress popped;
                        do
                        {
                            popped = stack.Pop();
                            onStack.Remove(popped);
                            members.Add(popped);
                        } while (!popped.Equals(cell));

                        if (members.Count > 1)
                        {
                            foreach (var member in members)
                                cyclicMembers.Add(member);
                        }
                        else
                        {
                            // Single-cell SCC: only a true cycle if the cell references itself
                            // (directly, or via a compact range spanning its own address).
                            var only = members[0];
                            foreach (var prec in GetPrecedentsWithin(only, residualSet, candidateIndex))
                            {
                                if (prec.Equals(only))
                                {
                                    cyclicMembers.Add(only);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        return cyclicMembers;
    }

    private readonly record struct TarjanFrame(CellAddress Cell, IEnumerator<CellAddress> Precedents);

    /// <summary>
    /// Re-run Kahn's algorithm restricted to <paramref name="downstream"/> cells, whose precedent
    /// edges into <paramref name="cyclicMembers"/> are treated as already satisfied (those inputs
    /// will hold the propagated cyclic error value, not block evaluation). Appends the resulting
    /// order to <paramref name="sorted"/>.
    /// </summary>
    private void AppendTopologicalOrder(
        List<CellAddress> downstream,
        HashSet<CellAddress> cyclicMembers,
        HashSet<CellAddress> candidates,
        CandidateIndex candidateIndex,
        List<CellAddress> sorted)
    {
        var downstreamSet = new HashSet<CellAddress>(downstream);
        var inDegree = new Dictionary<CellAddress, int>(downstream.Count);
        foreach (var cell in downstream)
        {
            var degree = 0;
            foreach (var prec in GetPrecedentsWithin(cell, candidates, candidateIndex))
            {
                if (downstreamSet.Contains(prec) && !cyclicMembers.Contains(prec))
                    degree++;
            }
            inDegree[cell] = degree;
        }

        var ready = new Queue<CellAddress>();
        foreach (var (cell, degree) in inDegree)
        {
            if (degree == 0)
                ready.Enqueue(cell);
        }

        var appended = 0;
        while (ready.Count > 0)
        {
            var cell = ready.Dequeue();
            sorted.Add(cell);
            appended++;

            DecrementDependentInDegrees(cell, inDegree, ready);
        }

        // Should not happen (downstream cells are, by construction, acyclic once cyclic members
        // are excluded), but guard against silently dropping cells if it ever does.
        if (appended < downstream.Count)
        {
            foreach (var cell in downstream)
            {
                if (!sorted.Contains(cell))
                    sorted.Add(cell);
            }
        }
    }

    /// <summary>
    /// Get the precedents of <paramref name="cell"/> that fall within <paramref name="candidates"/>,
    /// counting both exact and compact-range precedents. Mirrors <see cref="CountPrecedentsWithin"/>
    /// but yields the actual precedent addresses instead of just a count.
    /// </summary>
    private IEnumerable<CellAddress> GetPrecedentsWithin(
        CellAddress cell,
        HashSet<CellAddress> candidates,
        CandidateIndex candidateIndex)
    {
        HashSet<CellAddress>? seen = null;

        if (_precedents.TryGetValue(cell, out var exactPrecs))
        {
            foreach (var prec in exactPrecs)
            {
                if (!candidates.Contains(prec))
                    continue;

                seen ??= [];
                if (seen.Add(prec))
                    yield return prec;
            }
        }

        if (!_rangePrecedents.TryGetValue(cell, out var ranges))
            yield break;

        foreach (var range in ranges)
        {
            foreach (var candidate in candidateIndex.GetCandidatesInRange(range))
            {
                if (!candidates.Contains(candidate))
                    continue;

                seen ??= [];
                if (seen.Add(candidate))
                    yield return candidate;
            }
        }
    }

    private bool HasAnyDependents(IReadOnlyList<CellAddress> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (HasAnyDependent(cells[i]))
                return true;
        }

        return false;
    }

    private bool HasAnyDependent(CellAddress cell)
    {
        if (_dependents.TryGetValue(cell, out var exactDeps) && exactDeps.Count > 0)
            return true;

        return _rangeDependentsBySheet.TryGetValue(cell.Sheet, out var rangeDeps) &&
               HasAnyRangeDependent(cell, rangeDeps);
    }

    private void EnqueueUnvisitedDependents(
        CellAddress cell,
        HashSet<CellAddress> toRecalc,
        Queue<CellAddress> queue)
    {
        var exactDeps = _dependents.GetValueOrDefault(cell);
        if (!_rangeDependentsBySheet.TryGetValue(cell.Sheet, out var rangeDeps))
        {
            EnqueueExactDependents(exactDeps, toRecalc, queue);
            return;
        }

        var rangeSeen = EnqueueRangeDependents(cell, rangeDeps, toRecalc, queue);

        if (exactDeps is null)
            return;

        foreach (var dep in exactDeps)
        {
            if (rangeSeen?.Contains(dep) == true)
                continue;

            if (toRecalc.Add(dep))
                queue.Enqueue(dep);
        }
    }

    private static void EnqueueExactDependents(
        HashSet<CellAddress>? exactDeps,
        HashSet<CellAddress> toRecalc,
        Queue<CellAddress> queue)
    {
        if (exactDeps is null)
            return;

        foreach (var dep in exactDeps)
        {
            if (toRecalc.Add(dep))
                queue.Enqueue(dep);
        }
    }

    private void DecrementDependentInDegrees(
        CellAddress cell,
        Dictionary<CellAddress, int> inDegree,
        Queue<CellAddress> ready)
    {
        var exactDeps = _dependents.GetValueOrDefault(cell);
        if (!_rangeDependentsBySheet.TryGetValue(cell.Sheet, out var rangeDeps))
        {
            DecrementExactDependentInDegrees(exactDeps, inDegree, ready);
            return;
        }

        var rangeSeen = DecrementRangeDependentInDegrees(cell, rangeDeps, inDegree, ready);

        if (exactDeps is null)
            return;

        foreach (var dep in exactDeps)
        {
            if (rangeSeen?.Contains(dep) == true)
                continue;

            DecrementInDegree(dep, inDegree, ready);
        }
    }

    private static void DecrementExactDependentInDegrees(
        HashSet<CellAddress>? exactDeps,
        Dictionary<CellAddress, int> inDegree,
        Queue<CellAddress> ready)
    {
        if (exactDeps is null)
            return;

        foreach (var dep in exactDeps)
            DecrementInDegree(dep, inDegree, ready);
    }

    private static void DecrementInDegree(
        CellAddress dep,
        Dictionary<CellAddress, int> inDegree,
        Queue<CellAddress> ready)
    {
        if (!inDegree.TryGetValue(dep, out var degree))
            return;

        degree--;
        inDegree[dep] = degree;
        if (degree == 0)
            ready.Enqueue(dep);
    }

    private HashSet<CellAddress>? CollectRangeDependents(CellAddress cell)
    {
        if (!_rangeDependentsBySheet.TryGetValue(cell.Sheet, out var rangeDeps))
            return null;

        return CollectRangeDependents(cell, rangeDeps);
    }

    private static bool HasAnyRangeDependent(CellAddress cell, RangeDependencyIndex rangeDeps) =>
        HasAnyRangeDependent(cell, rangeDeps.GetRowCandidates(cell.Row)) ||
        HasAnyRangeDependent(cell, rangeDeps.GetColumnCandidates(cell.Col));

    private static bool HasAnyRangeDependent(
        CellAddress cell,
        List<RangeDependencyGroup>? candidates)
    {
        if (candidates is null)
            return false;

        foreach (var group in candidates)
        {
            if (group.Range.Contains(cell) && group.Count != 0)
                return true;
        }

        return false;
    }

    private static bool TryCollectSingleRangeDependent(
        CellAddress cell,
        List<RangeDependencyGroup>? candidates,
        ref bool hasDependent,
        ref CellAddress dependent)
    {
        if (candidates is null)
            return true;

        foreach (var group in candidates)
        {
            if (!group.Range.Contains(cell))
                continue;

            foreach (var dep in group.GetDependents())
            {
                if (!TryCollectSingleDependent(dep, ref hasDependent, ref dependent))
                    return false;
            }
        }

        return true;
    }

    private static bool TryCollectSingleDependent(
        CellAddress candidate,
        ref bool hasDependent,
        ref CellAddress dependent)
    {
        if (!hasDependent)
        {
            dependent = candidate;
            hasDependent = true;
            return true;
        }

        return candidate.Equals(dependent);
    }

    private static HashSet<CellAddress>? EnqueueRangeDependents(
        CellAddress cell,
        RangeDependencyIndex rangeDeps,
        HashSet<CellAddress> toRecalc,
        Queue<CellAddress> queue)
    {
        var rangeSeen = EnqueueRangeDependentCandidates(
            cell,
            rangeDeps.GetRowCandidates(cell.Row),
            null,
            toRecalc,
            queue);

        return EnqueueRangeDependentCandidates(
            cell,
            rangeDeps.GetColumnCandidates(cell.Col),
            rangeSeen,
            toRecalc,
            queue);
    }

    private static HashSet<CellAddress>? EnqueueRangeDependentCandidates(
        CellAddress cell,
        List<RangeDependencyGroup>? candidates,
        HashSet<CellAddress>? rangeSeen,
        HashSet<CellAddress> toRecalc,
        Queue<CellAddress> queue)
    {
        if (candidates is null)
            return rangeSeen;

        foreach (var group in candidates)
        {
            if (!group.Range.Contains(cell))
                continue;

            foreach (var dependent in group.GetDependents())
            {
                rangeSeen ??= [];
                if (rangeSeen.Add(dependent) && toRecalc.Add(dependent))
                    queue.Enqueue(dependent);
            }
        }

        return rangeSeen;
    }

    private static HashSet<CellAddress>? DecrementRangeDependentInDegrees(
        CellAddress cell,
        RangeDependencyIndex rangeDeps,
        Dictionary<CellAddress, int> inDegree,
        Queue<CellAddress> ready)
    {
        var rangeSeen = DecrementRangeDependentCandidates(
            cell,
            rangeDeps.GetRowCandidates(cell.Row),
            null,
            inDegree,
            ready);

        return DecrementRangeDependentCandidates(
            cell,
            rangeDeps.GetColumnCandidates(cell.Col),
            rangeSeen,
            inDegree,
            ready);
    }

    private static HashSet<CellAddress>? DecrementRangeDependentCandidates(
        CellAddress cell,
        List<RangeDependencyGroup>? candidates,
        HashSet<CellAddress>? rangeSeen,
        Dictionary<CellAddress, int> inDegree,
        Queue<CellAddress> ready)
    {
        if (candidates is null)
            return rangeSeen;

        foreach (var group in candidates)
        {
            if (!group.Range.Contains(cell))
                continue;

            foreach (var dependent in group.GetDependents())
            {
                rangeSeen ??= [];
                if (!rangeSeen.Add(dependent))
                    continue;

                DecrementInDegree(dependent, inDegree, ready);
            }
        }

        return rangeSeen;
    }

    private static HashSet<CellAddress>? CollectRangeDependents(
        CellAddress cell,
        RangeDependencyIndex rangeDeps)
    {
        var result = CollectRangeDependentCandidates(
            cell,
            rangeDeps.GetRowCandidates(cell.Row),
            null);

        return CollectRangeDependentCandidates(
            cell,
            rangeDeps.GetColumnCandidates(cell.Col),
            result);
    }

    private static HashSet<CellAddress>? CollectRangeDependentCandidates(
        CellAddress cell,
        List<RangeDependencyGroup>? candidates,
        HashSet<CellAddress>? result)
    {
        if (candidates is null)
            return result;

        foreach (var group in candidates)
        {
            if (!group.Range.Contains(cell))
                continue;

            foreach (var dep in group.GetDependents())
            {
                result ??= [];
                result.Add(dep);
            }
        }

        return result;
    }

    /// <summary>
    /// Count how many cells in <paramref name="candidates"/> are precedents of <paramref name="cell"/>,
    /// counting both exact and compact-range precedents. Uses <paramref name="candidateIndex"/> to avoid
    /// an O(|candidates|) scan for each compact range — instead only touches candidates that fall within
    /// the range's bounding row buckets.
    /// </summary>
    private int CountPrecedentsWithin(
        CellAddress cell,
        HashSet<CellAddress> candidates,
        CandidateIndex candidateIndex)
    {
        var count = 0;

        if (_precedents.TryGetValue(cell, out var exactPrecs))
            count = CountExactPrecedentsWithin(exactPrecs, candidates);

        if (!_rangePrecedents.TryGetValue(cell, out var ranges))
            return count;

        HashSet<CellAddress>? counted = null;
        if (count > 0 && exactPrecs is not null)
        {
            counted = new HashSet<CellAddress>(count);
            AddExactPrecedentsWithin(exactPrecs, candidates, counted);
        }

        foreach (var range in ranges)
        {
            foreach (var candidate in candidateIndex.GetCandidatesInRange(range))
            {
                if (AddUnique(candidate))
                    count++;
            }
        }

        return count;

        bool AddUnique(CellAddress address)
        {
            counted ??= [];
            return counted.Add(address);
        }
    }

    private static int CountExactPrecedentsWithin(
        IReadOnlySet<CellAddress> exactPrecs,
        HashSet<CellAddress> candidates)
    {
        var count = 0;
        if (exactPrecs is HashSet<CellAddress> hashSet)
        {
            foreach (var prec in hashSet)
            {
                if (candidates.Contains(prec))
                    count++;
            }
            return count;
        }

        if (exactPrecs is FrozenSet<CellAddress> frozenSet)
        {
            foreach (var prec in frozenSet)
            {
                if (candidates.Contains(prec))
                    count++;
            }
            return count;
        }

        foreach (var prec in exactPrecs)
        {
            if (candidates.Contains(prec))
                count++;
        }
        return count;
    }

    private static void AddExactPrecedentsWithin(
        IReadOnlySet<CellAddress> exactPrecs,
        HashSet<CellAddress> candidates,
        HashSet<CellAddress> counted)
    {
        if (exactPrecs is HashSet<CellAddress> hashSet)
        {
            foreach (var prec in hashSet)
            {
                if (candidates.Contains(prec))
                    counted.Add(prec);
            }
            return;
        }

        if (exactPrecs is FrozenSet<CellAddress> frozenSet)
        {
            foreach (var prec in frozenSet)
            {
                if (candidates.Contains(prec))
                    counted.Add(prec);
            }
            return;
        }

        foreach (var prec in exactPrecs)
        {
            if (candidates.Contains(prec))
                counted.Add(prec);
        }
    }

    /// <summary>Return the single element of a non-empty set without LINQ allocation.</summary>
    private static CellAddress FirstOf(HashSet<CellAddress> set)
    {
        foreach (var item in set)
            return item;
        throw new InvalidOperationException("Set is empty.");
    }
}

/// <summary>
/// A per-invocation spatial index over a dirty candidate set.
/// Built once per <c>GetRecalcOrder</c> / <c>GetEvaluationOrder</c> call and used by
/// <c>CountPrecedentsWithin</c> to answer "which candidates fall inside this GridRange?"
/// in O(candidates_in_overlapping_row_buckets) rather than O(|candidates|).
///
/// Strategy: mirror <see cref="RangeDependencyIndex"/>'s row-bucket approach.
/// Candidates are grouped by (SheetId, rowBucket). For a given range [r0..r1]×[c0..c1]
/// on sheet S, we visit only the buckets [bucket(r0)..bucket(r1)] for sheet S, and within
/// each bucket filter by row ∈ [r0..r1] and col ∈ [c0..c1].  Full-column ranges span all
/// row buckets; no special-casing is needed because those ranges are stored in the
/// column-bucket side of <see cref="RangeDependencyIndex"/> and tend to have only a few
/// dependent formula cells, so the Kahn init pass over range-precedent cells is bounded.
/// </summary>
internal sealed class CandidateIndex
{
    private const uint RowBucketSize = 256;

    // sheet -> (rowBucket -> list of candidates in that bucket)
    private readonly Dictionary<SheetId, Dictionary<uint, List<CellAddress>>> _bySheet;

    private CandidateIndex(Dictionary<SheetId, Dictionary<uint, List<CellAddress>>> bySheet)
    {
        _bySheet = bySheet;
    }

    /// <summary>Build the index from a set of candidate cells.</summary>
    internal static CandidateIndex Build(HashSet<CellAddress> candidates)
    {
        var bySheet = new Dictionary<SheetId, Dictionary<uint, List<CellAddress>>>();
        foreach (var candidate in candidates)
        {
            if (!bySheet.TryGetValue(candidate.Sheet, out var sheetBuckets))
            {
                sheetBuckets = [];
                bySheet[candidate.Sheet] = sheetBuckets;
            }

            var bucket = GetBucket(candidate.Row);
            if (!sheetBuckets.TryGetValue(bucket, out var list))
            {
                list = [];
                sheetBuckets[bucket] = list;
            }

            list.Add(candidate);
        }

        return new CandidateIndex(bySheet);
    }

    /// <summary>
    /// Enumerate all candidates that fall within <paramref name="range"/>.
    /// Never yields duplicates (each candidate is in exactly one row-bucket).
    /// </summary>
    internal IEnumerable<CellAddress> GetCandidatesInRange(GridRange range)
    {
        if (!_bySheet.TryGetValue(range.Start.Sheet, out var sheetBuckets))
            yield break;

        var startBucket = GetBucket(range.Start.Row);
        var endBucket = GetBucket(range.End.Row);
        var c0 = range.Start.Col;
        var c1 = range.End.Col;
        var r0 = range.Start.Row;
        var r1 = range.End.Row;

        for (var bucket = startBucket; bucket <= endBucket; bucket++)
        {
            if (!sheetBuckets.TryGetValue(bucket, out var list))
                continue;

            foreach (var candidate in list)
            {
                // Row is guaranteed to fall within [bucket*size..(bucket+1)*size-1], but the
                // first and last buckets may extend beyond [r0..r1], so check row bounds too.
                if (candidate.Row >= r0 && candidate.Row <= r1 &&
                    candidate.Col >= c0 && candidate.Col <= c1)
                {
                    yield return candidate;
                }
            }
        }
    }

    private static uint GetBucket(uint row) => (row - 1) / RowBucketSize;
}

internal readonly record struct RangeDependency(GridRange Range, CellAddress Dependent);

internal sealed class RangeDependencyGroup
{
    // HashSet gives O(1) Remove instead of List's O(n); enumeration order is not required
    // for correctness (the range-dependent fan-out feeds into a BFS/Kahn pass that is
    // order-independent).
    private readonly HashSet<CellAddress> _dependents = [];

    public RangeDependencyGroup(GridRange range)
    {
        Range = range;
    }

    public GridRange Range { get; }
    public int Count => _dependents.Count;

    public IEnumerable<CellAddress> GetDependents() => _dependents;

    public void Add(CellAddress dependent) => _dependents.Add(dependent);

    public bool Remove(CellAddress dependent) => _dependents.Remove(dependent);
}

internal sealed class RangeDependencyIndex
{
    private const uint RowBucketSize = 256;
    private const uint ColumnBucketSize = 16;

    private readonly Dictionary<uint, List<RangeDependencyGroup>> _rowBuckets = [];
    private readonly Dictionary<uint, List<RangeDependencyGroup>> _columnBuckets = [];

    public int Count { get; private set; }

    public void Add(RangeDependency dependency)
    {
        var range = dependency.Range;
        if (UseRowIndex(range))
            AddToBuckets(_rowBuckets, dependency, range.Start.Row, range.End.Row, RowBucketSize);
        else
            AddToBuckets(_columnBuckets, dependency, range.Start.Col, range.End.Col, ColumnBucketSize);

        Count++;
    }

    public void Remove(RangeDependency dependency)
    {
        var range = dependency.Range;
        var removed = UseRowIndex(range)
            ? RemoveFromBuckets(_rowBuckets, dependency, range.Start.Row, range.End.Row, RowBucketSize)
            : RemoveFromBuckets(_columnBuckets, dependency, range.Start.Col, range.End.Col, ColumnBucketSize);

        if (removed)
            Count--;
    }

    public List<RangeDependencyGroup>? GetRowCandidates(uint row) =>
        _rowBuckets.TryGetValue(GetBucket(row, RowBucketSize), out var deps) ? deps : null;

    public List<RangeDependencyGroup>? GetColumnCandidates(uint column) =>
        _columnBuckets.TryGetValue(GetBucket(column, ColumnBucketSize), out var deps) ? deps : null;

    private static bool UseRowIndex(GridRange range) =>
        GetBucketCount(range.Start.Row, range.End.Row, RowBucketSize) <=
        GetBucketCount(range.Start.Col, range.End.Col, ColumnBucketSize);

    private static void AddToBuckets(
        Dictionary<uint, List<RangeDependencyGroup>> buckets,
        RangeDependency dependency,
        uint start,
        uint end,
        uint bucketSize)
    {
        var startBucket = GetBucket(start, bucketSize);
        var endBucket = GetBucket(end, bucketSize);

        for (var bucket = startBucket; bucket <= endBucket; bucket++)
        {
            if (!buckets.TryGetValue(bucket, out var deps))
            {
                deps = [];
                buckets[bucket] = deps;
            }

            var group = FindGroup(deps, dependency.Range);
            if (group is null)
            {
                group = new RangeDependencyGroup(dependency.Range);
                deps.Add(group);
            }

            group.Add(dependency.Dependent);
        }
    }

    private static bool RemoveFromBuckets(
        Dictionary<uint, List<RangeDependencyGroup>> buckets,
        RangeDependency dependency,
        uint start,
        uint end,
        uint bucketSize)
    {
        var removed = false;
        var startBucket = GetBucket(start, bucketSize);
        var endBucket = GetBucket(end, bucketSize);

        for (var bucket = startBucket; bucket <= endBucket; bucket++)
        {
            if (!buckets.TryGetValue(bucket, out var deps))
                continue;

            var group = FindGroup(deps, dependency.Range);
            if (group is not null && group.Remove(dependency.Dependent))
            {
                removed = true;
                if (group.Count == 0)
                    deps.Remove(group);
            }

            if (deps.Count == 0)
                buckets.Remove(bucket);
        }

        return removed;
    }

    private static RangeDependencyGroup? FindGroup(
        List<RangeDependencyGroup> groups,
        GridRange range)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].Range == range)
                return groups[i];
        }

        return null;
    }

    private static uint GetBucketCount(uint start, uint end, uint bucketSize) =>
        GetBucket(end, bucketSize) - GetBucket(start, bucketSize) + 1;

    private static uint GetBucket(uint value, uint bucketSize) =>
        (value - 1) / bucketSize;
}

/// <summary>Result of computing a recalculation order.</summary>
public sealed record RecalcPlan(
    IReadOnlyList<CellAddress> OrderedCells,
    IReadOnlyList<CellAddress> CyclicCells);
