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
    public RecalcReport Recalculate(Workbook workbook, IReadOnlyList<CellAddress> changedCells)
    {
        if (changedCells.Count == 0 && _volatileCells.Count == 0)
            return EmptyReport;

        // Include volatile cells in the dependency traversal so their dependents appear in the plan
        var changedForTraversal = BuildChangedSetForTraversal(changedCells);
        var plan = _graph.GetRecalcOrder(changedForTraversal);
        var changedFormulaCells = CollectChangedFormulaCells(workbook, changedCells);
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

        // Mark cyclic cells with error
        foreach (var cyclic in plan.CyclicCells)
            AddCyclicCell(workbook, cyclic, ref cyclicCells, ref seenCyclicCells, ref errors);

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
                foreach (var cyclic in evaluationPlan.CyclicCells)
                    AddCyclicCell(workbook, cyclic, ref cyclicCells, ref seenCyclicCells, ref errors);
            }
        }

        foreach (var addr in directFormulaRoots ?? evaluationPlan.OrderedCells)
        {
            var sheet = workbook.GetSheet(addr.Sheet);
            if (sheet is null) continue;

            var cell = sheet.GetCell(addr);
            if (cell is null || !cell.HasFormula) continue;

            try
            {
                // Use cached AST to avoid re-running Lexer+Parser on every recalc pass.
                if (cell.CachedAst is not FormulaNode cachedAst)
                {
                    cachedAst = FormulaEvaluator.ParseFormula(cell.FormulaText!);
                    cell.CachedAst = cachedAst;
                    RegisterFormulaDependencies(addr, cachedAst, addr.Sheet, workbook);
                }
                var result = _evaluator.Evaluate(cachedAst, sheet, workbook, addr);

                if (result is RangeValue rv)
                {
                    sheet.ClearSpillRange(addr);
                    if (sheet.IsSpillBlocked(addr, rv.RowCount, rv.ColCount))
                    {
                        cell.Value = ErrorValue.Spill;
                        AddError(ref errors, addr, "#SPILL!");
                    }
                    else
                    {
                        cell.Value = rv.Cells[0, 0];
                        sheet.SetSpillRange(addr, rv);
                        AddRecalculatedCell(ref recalculatedCount, ref singleRecalculated, ref recalculated, addr);
                    }
                }
                else
                {
                    sheet.ClearSpillRange(addr);
                    cell.Value = result;
                    AddRecalculatedCell(ref recalculatedCount, ref singleRecalculated, ref recalculated, addr);
                }
            }
            catch (FormulaParseException)
            {
                cell.CachedAst = null;
                sheet.ClearSpillRange(addr);
                ClearFormulaDependencies(addr);
                cell.Value = ErrorValue.Value;
                AddError(ref errors, addr, "#VALUE!");
            }
            catch (FormulaEvalException ex)
            {
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
                cell.Value = ErrorValue.Value;
                AddError(ref errors, addr, "#VALUE!");
#endif
            }
        }

        return new RecalcReport(
            BuildRecalculatedCells(recalculatedCount, singleRecalculated, recalculated),
            errors ?? EmptyErrors,
            cyclicCells ?? EmptyCells);
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
    /// Extract cell references from a formula AST and register them in the dependency graph.
    /// Call this whenever a formula is set on a cell.
    /// </summary>
    public void RegisterFormulaDependencies(CellAddress formulaCell, FormulaNode ast, SheetId sheetId, FreeX.Core.Model.Workbook? workbook = null)
    {
        var cacheKey = new DependencyPlanCacheKey(ast, sheetId);
        if (_dependencyPlanCache.TryGetValue(cacheKey, out var cachedPlan))
        {
            ApplyDependencyPlan(formulaCell, cachedPlan);
            return;
        }

        var refs = new FormulaDependencySet();
        var cacheableForDependencyPlan = true;
        var containsVolatileFunction = CollectReferences(ast, sheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan);
        _graph.SetDependencies(formulaCell, refs.Cells, refs.Ranges);

        SetVolatileTracking(formulaCell, containsVolatileFunction);

        if (cacheableForDependencyPlan)
            AddDependencyPlanToCache(cacheKey, refs, containsVolatileFunction);
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

        return Recalculate(workbook, formulaCells);
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
        ref bool cacheableForDependencyPlan)
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
                if (workbook is not null && workbook.TryGetNamedRange(named.Name, out var namedRange))
                {
                    refs.AddRange(namedRange);
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
                var leftHasVolatile = CollectReferences(binary.Left, defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan);
                var rightHasVolatile = CollectReferences(binary.Right, defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan);
                return leftHasVolatile || rightHasVolatile;
            }

            case UnaryOpNode unary:
                return CollectReferences(unary.Operand, defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan);

            case FunctionCallNode func:
            {
                var containsVolatileFunction = IsVolatileFunctionName(func.FunctionName);
                var arguments = func.Arguments;
                for (var i = 0; i < arguments.Count; i++)
                {
                    if (CollectReferences(arguments[i], defaultSheetId, formulaCell, workbook, refs, ref cacheableForDependencyPlan))
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
    }

}

/// <summary>Report of a recalculation pass.</summary>
public sealed record RecalcReport(
    IReadOnlyList<CellAddress> RecalculatedCells,
    IReadOnlyList<(CellAddress Cell, string Error)> Errors,
    IReadOnlyList<CellAddress> CyclicCells);
