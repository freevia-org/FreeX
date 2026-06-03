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
    private readonly Dictionary<CellAddress, List<CellAddress>> _dependents = [];

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

        // Topological sort via Kahn's algorithm
        var inDegree = new Dictionary<CellAddress, int>(toRecalc.Count);
        foreach (var cell in toRecalc)
            inDegree[cell] = 0;

        foreach (var cell in toRecalc)
        {
            inDegree[cell] = CountPrecedentsWithin(cell, toRecalc);
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

        // Any remaining cells with in-degree > 0 are part of cycles
        List<CellAddress>? cycles = null;
        foreach (var (cell, degree) in inDegree)
        {
            if (degree > 0)
            {
                cycles ??= [];
                cycles.Add(cell);
            }
        }

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

        var first = dependents[0];
        if (!TryCountSingleRootExactChain(first, out var count))
            return false;

        var ordered = new List<CellAddress>(count);
        var current = first;
        for (var i = 0; i < count; i++)
        {
            ordered.Add(current);
            if (i + 1 < count)
                current = _dependents[current][0];
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

            current = dependents[0];
        }
    }

    /// <summary>
    /// Topologically order a known dirty set, including the dirty roots themselves.
    /// </summary>
    public RecalcPlan GetEvaluationOrder(IReadOnlyCollection<CellAddress> dirtyCells)
    {
        if (dirtyCells.Count == 0)
            return EmptyPlan;

        var candidates = dirtyCells as HashSet<CellAddress> ?? new HashSet<CellAddress>(dirtyCells);
        var inDegree = new Dictionary<CellAddress, int>(candidates.Count);
        foreach (var cell in candidates)
            inDegree[cell] = CountPrecedentsWithin(cell, candidates);

        var sorted = new List<CellAddress>(candidates.Count);
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

        List<CellAddress>? cycles = null;
        foreach (var (cell, degree) in inDegree)
        {
            if (degree > 0)
            {
                cycles ??= [];
                cycles.Add(cell);
            }
        }

        return new RecalcPlan(sorted, cycles ?? []);
    }

    private bool HasAnyDependents(IReadOnlyList<CellAddress> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (_dependents.TryGetValue(cell, out var exactDeps) && exactDeps.Count > 0)
                return true;

            if (_rangeDependentsBySheet.TryGetValue(cell.Sheet, out var rangeDeps) &&
                HasAnyRangeDependent(cell, rangeDeps))
                return true;
        }

        return false;
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
        List<CellAddress>? exactDeps,
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
        List<CellAddress>? exactDeps,
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

            for (var i = 0; i < group.Count; i++)
            {
                var dependent = group.GetDependent(i);
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

            for (var i = 0; i < group.Count; i++)
            {
                var dependent = group.GetDependent(i);
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

            for (var i = 0; i < group.Count; i++)
            {
                result ??= [];
                result.Add(group.GetDependent(i));
            }
        }

        return result;
    }

    private int CountPrecedentsWithin(CellAddress cell, HashSet<CellAddress> candidates)
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

        foreach (var candidate in candidates)
        {
            foreach (var range in ranges)
            {
                if (range.Contains(candidate) && AddUnique(candidate))
                {
                    count++;
                    break;
                }
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
}

internal readonly record struct RangeDependency(GridRange Range, CellAddress Dependent);

internal sealed class RangeDependencyGroup
{
    private readonly List<CellAddress> _dependents = [];

    public RangeDependencyGroup(GridRange range)
    {
        Range = range;
    }

    public GridRange Range { get; }
    public int Count => _dependents.Count;

    public CellAddress GetDependent(int index) => _dependents[index];

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
