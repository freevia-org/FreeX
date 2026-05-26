using System.Collections.Frozen;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

/// <summary>
/// Tracks cell-to-cell dependencies and performs topological recalculation.
/// The engine owns the dependency graph — we never trust external calc chains.
/// </summary>
public sealed class DependencyGraph
{
    // Cell -> set of cells it depends on (precedents)
    private readonly Dictionary<CellAddress, HashSet<CellAddress>> _precedents = [];

    // Cell -> set of cells that depend on it (dependents)
    private readonly Dictionary<CellAddress, HashSet<CellAddress>> _dependents = [];

    // Cell -> compact range precedents it depends on.
    private readonly Dictionary<CellAddress, List<GridRange>> _rangePrecedents = [];

    // Sheet -> compact ranges and the cells that depend on them.
    private readonly Dictionary<SheetId, List<RangeDependency>> _rangeDependentsBySheet = [];

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
        => SetDependencies(cell, precedents, []);

    /// <summary>
    /// Set dependencies using a fresh, caller-owned set plus compact range precedents.
    /// </summary>
    internal void SetDependencies(
        CellAddress cell,
        HashSet<CellAddress> precedents,
        IReadOnlyList<GridRange> rangePrecedents)
    {
        ClearDependencies(cell);

        _precedents[cell] = precedents;
        if (rangePrecedents.Count > 0)
        {
            var ranges = new List<GridRange>(rangePrecedents);
            _rangePrecedents[cell] = ranges;

            foreach (var range in ranges)
            {
                if (!_rangeDependentsBySheet.TryGetValue(range.Start.Sheet, out var deps))
                {
                    deps = [];
                    _rangeDependentsBySheet[range.Start.Sheet] = deps;
                }
                deps.Add(new RangeDependency(range, cell));
            }
        }

        // Register reverse links
        foreach (var prec in precedents)
        {
            if (!_dependents.TryGetValue(prec, out var deps))
            {
                deps = [];
                _dependents[prec] = deps;
            }
            deps.Add(cell);
        }
    }

    /// <summary>Remove all dependencies for a cell.</summary>
    public void ClearDependencies(CellAddress cell)
    {
        if (_precedents.TryGetValue(cell, out var oldPrecs))
        {
            foreach (var prec in oldPrecs)
            {
                if (_dependents.TryGetValue(prec, out var deps))
                {
                    deps.Remove(cell);
                    if (deps.Count == 0)
                        _dependents.Remove(prec);
                }
            }
            _precedents.Remove(cell);
        }

        if (_rangePrecedents.Remove(cell, out var oldRanges))
        {
            foreach (var range in oldRanges)
            {
                if (!_rangeDependentsBySheet.TryGetValue(range.Start.Sheet, out var deps))
                    continue;

                deps.RemoveAll(dep => dep.Dependent.Equals(cell));
                if (deps.Count == 0)
                    _rangeDependentsBySheet.Remove(range.Start.Sheet);
            }
        }
    }

    /// <summary>Remove every dependency edge from the graph.</summary>
    public void ClearAll()
    {
        _precedents.Clear();
        _dependents.Clear();
        _rangePrecedents.Clear();
        _rangeDependentsBySheet.Clear();
    }

    private static readonly IReadOnlySet<CellAddress> EmptySet =
        new HashSet<CellAddress>().ToFrozenSet();

    /// <summary>Get all cells that directly depend on the given cell.</summary>
    public IReadOnlySet<CellAddress> GetDirectDependents(CellAddress cell)
    {
        var rangeDeps = GetRangeDependents(cell);
        if (rangeDeps.Count == 0)
            return _dependents.TryGetValue(cell, out var deps) ? deps : EmptySet;

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
        var toRecalc = new HashSet<CellAddress>();
        var cycles = new List<CellAddress>();

        // BFS to find all transitive dependents
        var queue = new Queue<CellAddress>(changedCells);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var dep in GetDirectDependentSet(cell))
            {
                if (toRecalc.Add(dep))
                    queue.Enqueue(dep);
            }
        }

        // Topological sort via Kahn's algorithm
        var inDegree = new Dictionary<CellAddress, int>();
        foreach (var cell in toRecalc)
            inDegree[cell] = 0;

        foreach (var cell in toRecalc)
        {
            inDegree[cell] = CountPrecedentsWithin(cell, toRecalc);
        }

        var sorted = new List<CellAddress>();
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

            foreach (var dep in GetDirectDependentSet(cell))
            {
                if (inDegree.ContainsKey(dep))
                {
                    inDegree[dep]--;
                    if (inDegree[dep] == 0)
                        ready.Enqueue(dep);
                }
            }
        }

        // Any remaining cells with in-degree > 0 are part of cycles
        foreach (var (cell, degree) in inDegree)
        {
            if (degree > 0)
                cycles.Add(cell);
        }

        return new RecalcPlan(sorted, cycles);
    }

    private HashSet<CellAddress> GetDirectDependentSet(CellAddress cell)
    {
        var rangeDeps = GetRangeDependents(cell);
        if (_dependents.TryGetValue(cell, out var exactDeps))
        {
            if (rangeDeps.Count == 0)
                return new HashSet<CellAddress>(exactDeps);

            rangeDeps.UnionWith(exactDeps);
        }

        return rangeDeps;
    }

    private HashSet<CellAddress> GetRangeDependents(CellAddress cell)
    {
        var result = new HashSet<CellAddress>();
        if (!_rangeDependentsBySheet.TryGetValue(cell.Sheet, out var rangeDeps))
            return result;

        foreach (var dep in rangeDeps)
        {
            if (dep.Range.Contains(cell))
                result.Add(dep.Dependent);
        }

        return result;
    }

    private int CountPrecedentsWithin(CellAddress cell, HashSet<CellAddress> candidates)
    {
        HashSet<CellAddress>? counted = null;
        var count = 0;

        if (_precedents.TryGetValue(cell, out var exactPrecs))
        {
            foreach (var prec in exactPrecs)
            {
                if (candidates.Contains(prec) && AddUnique(prec))
                    count++;
            }
        }

        if (_rangePrecedents.TryGetValue(cell, out var ranges))
        {
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
        }

        return count;

        bool AddUnique(CellAddress address)
        {
            counted ??= [];
            return counted.Add(address);
        }
    }
}

internal readonly record struct RangeDependency(GridRange Range, CellAddress Dependent);

/// <summary>Result of computing a recalculation order.</summary>
public sealed record RecalcPlan(
    IReadOnlyList<CellAddress> OrderedCells,
    IReadOnlyList<CellAddress> CyclicCells);
