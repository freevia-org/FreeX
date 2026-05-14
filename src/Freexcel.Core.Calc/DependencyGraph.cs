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

    /// <summary>
    /// Set the dependencies for a cell (what cells its formula references).
    /// Replaces any previous dependencies.
    /// </summary>
    public void SetDependencies(CellAddress cell, IEnumerable<CellAddress> precedents)
    {
        // Remove old dependencies
        ClearDependencies(cell);

        var precSet = new HashSet<CellAddress>(precedents);
        _precedents[cell] = precSet;

        // Register reverse links
        foreach (var prec in precSet)
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
    }

    /// <summary>Remove every dependency edge from the graph.</summary>
    public void ClearAll()
    {
        _precedents.Clear();
        _dependents.Clear();
    }

    /// <summary>Get all cells that directly depend on the given cell.</summary>
    public IReadOnlySet<CellAddress> GetDirectDependents(CellAddress cell)
    {
        return _dependents.TryGetValue(cell, out var deps) ? deps : new HashSet<CellAddress>();
    }

    /// <summary>Get all cells that the given cell directly references.</summary>
    public IReadOnlySet<CellAddress> GetDirectPrecedents(CellAddress cell)
    {
        return _precedents.TryGetValue(cell, out var precs) ? precs : new HashSet<CellAddress>();
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
            if (_dependents.TryGetValue(cell, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (toRecalc.Add(dep))
                        queue.Enqueue(dep);
                }
            }
        }

        // Topological sort via Kahn's algorithm
        var inDegree = new Dictionary<CellAddress, int>();
        foreach (var cell in toRecalc)
            inDegree[cell] = 0;

        foreach (var cell in toRecalc)
        {
            if (_precedents.TryGetValue(cell, out var precs))
            {
                foreach (var prec in precs)
                {
                    if (toRecalc.Contains(prec))
                    {
                        inDegree[cell]++;
                    }
                }
            }
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

            if (_dependents.TryGetValue(cell, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (inDegree.ContainsKey(dep))
                    {
                        inDegree[dep]--;
                        if (inDegree[dep] == 0)
                            ready.Enqueue(dep);
                    }
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
}

/// <summary>Result of computing a recalculation order.</summary>
public sealed record RecalcPlan(
    IReadOnlyList<CellAddress> OrderedCells,
    IReadOnlyList<CellAddress> CyclicCells);
