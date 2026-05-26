using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

/// <summary>
/// Coordinates recalculation of formula cells when values change.
/// Uses the dependency graph to determine order and evaluates only dirty cells.
/// </summary>
public sealed class RecalcEngine
{
    private const long CompactRangeCellThreshold = 1024;

    private readonly DependencyGraph _graph;
    private readonly FormulaEvaluator _evaluator;
    // Single-threaded only. If multi-threaded recalc is added (Phase 4), protect with a lock.
    private readonly HashSet<CellAddress> _volatileCells = [];

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
        // Include volatile cells in the dependency traversal so their dependents appear in the plan
        var changedForTraversal = BuildChangedSetForTraversal(changedCells);
        var plan = _graph.GetRecalcOrder(changedForTraversal);
        var recalculated = new List<CellAddress>();
        var errors = new List<(CellAddress Cell, string Error)>();

        // Mark cyclic cells with error
        foreach (var cyclic in plan.CyclicCells)
        {
            var sheet = workbook.GetSheet(cyclic.Sheet);
            if (sheet is null) continue;

            var cell = sheet.GetCell(cyclic);
            if (cell is not null)
            {
                cell.Value = ErrorValue.Circular;
                errors.Add((cyclic, "#CIRCULAR!"));
            }
        }

        // Directly-changed formula cells must evaluate first (they are NOT included in
        // plan.OrderedCells, which only contains downstream dependents). Then volatile cells,
        // then the topological dependent order.
        var toEvaluate = new List<CellAddress>(
            changedCells.Count + _volatileCells.Count + plan.OrderedCells.Count);
        var seen = new HashSet<CellAddress>();

        foreach (var addr in changedCells)
        {
            var sheet = workbook.GetSheet(addr.Sheet);
            var cell = sheet?.GetCell(addr);
            if (cell?.HasFormula == true)
                AddIfNew(addr, toEvaluate, seen);
        }

        foreach (var addr in _volatileCells)
            AddIfNew(addr, toEvaluate, seen);

        foreach (var addr in plan.OrderedCells)
            AddIfNew(addr, toEvaluate, seen);

        foreach (var addr in toEvaluate)
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
                    cachedAst = new Parser(new Lexer("=" + cell.FormulaText).Tokenize()).Parse();
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
                        errors.Add((addr, "#SPILL!"));
                    }
                    else
                    {
                        cell.Value = rv.Cells[0, 0];
                        sheet.SetSpillRange(addr, rv);
                        recalculated.Add(addr);
                    }
                }
                else
                {
                    sheet.ClearSpillRange(addr);
                    cell.Value = result;
                    recalculated.Add(addr);
                }
            }
            catch (FormulaParseException)
            {
                cell.Value = ErrorValue.Value;
                errors.Add((addr, "#VALUE!"));
            }
            catch (FormulaEvalException ex)
            {
                cell.Value = new ErrorValue(ex.ErrorCode);
                errors.Add((addr, ex.ErrorCode));
            }
            catch (Exception)
            {
                // Defensive: any unhandled exception from the evaluator (e.g. inverted range,
                // overflow) must not crash the app — surface it as #VALUE! instead.
                cell.Value = ErrorValue.Value;
                errors.Add((addr, "#VALUE!"));
            }
        }

        return new RecalcReport(recalculated, errors, plan.CyclicCells);
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

    private static void AddIfNew(
        CellAddress addr,
        List<CellAddress> orderedCells,
        HashSet<CellAddress> seen)
    {
        if (seen.Add(addr))
            orderedCells.Add(addr);
    }

    /// <summary>
    /// Extract cell references from a formula AST and register them in the dependency graph.
    /// Call this whenever a formula is set on a cell.
    /// </summary>
    public void RegisterFormulaDependencies(CellAddress formulaCell, FormulaNode ast, SheetId sheetId, Freexcel.Core.Model.Workbook? workbook = null)
    {
        var refs = new FormulaDependencySet();
        CollectReferences(ast, sheetId, formulaCell, workbook, refs);
        _graph.SetDependencies(formulaCell, refs.Cells, refs.Ranges);

        if (ContainsVolatileFunction(ast))
            _volatileCells.Add(formulaCell);
        else
            _volatileCells.Remove(formulaCell);
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

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var (addr, cell) in sheet.EnumerateCells())
            {
                if (!cell.HasFormula || cell.FormulaText is null)
                    continue;

                try
                {
                    if (cell.CachedAst is not FormulaNode ast)
                    {
                        ast = new Parser(new Lexer("=" + cell.FormulaText).Tokenize()).Parse();
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
        var formulaCells = workbook.Sheets
            .SelectMany(sheet => sheet.EnumerateCells())
            .Where(entry => entry.Cell.HasFormula)
            .Select(entry => entry.Address)
            .ToList();

        return Recalculate(workbook, formulaCells);
    }

    /// <summary>Rebuild dependencies and evaluate formula cells on a single worksheet.</summary>
    public RecalcReport RecalculateSheetFormulas(Workbook workbook, SheetId sheetId)
    {
        RebuildFormulaDependencies(workbook);
        var sheet = workbook.GetSheet(sheetId);
        if (sheet is null)
            return new RecalcReport([], [], []);

        var formulaCells = sheet.EnumerateCells()
            .Where(entry => entry.Cell.HasFormula)
            .Select(entry => entry.Address)
            .ToList();

        var report = Recalculate(workbook, formulaCells);
        return new RecalcReport(
            report.RecalculatedCells.Where(addr => addr.Sheet.Equals(sheetId)).ToList(),
            report.Errors.Where(error => error.Cell.Sheet.Equals(sheetId)).ToList(),
            report.CyclicCells.Where(addr => addr.Sheet.Equals(sheetId)).ToList());
    }

    private static bool ContainsVolatileFunction(FormulaNode node)
    {
        return node switch
        {
            FunctionCallNode f => BuiltInFunctions.IsVolatile(f.FunctionName)
                                  || f.Arguments.Any(ContainsVolatileFunction),
            BinaryOpNode b => ContainsVolatileFunction(b.Left) || ContainsVolatileFunction(b.Right),
            UnaryOpNode u => ContainsVolatileFunction(u.Operand),
            _ => false
        };
    }

    private static void CollectReferences(
        FormulaNode node,
        SheetId defaultSheetId,
        CellAddress formulaCell,
        Freexcel.Core.Model.Workbook? workbook,
        FormulaDependencySet refs)
    {
        switch (node)
        {
            case CellRefNode cellRef when cellRef.SheetName is not null:
            {
                var targetSheet = workbook?.GetSheet(cellRef.SheetName);
                if (targetSheet is not null)
                    refs.Add(new CellAddress(targetSheet.Id, cellRef.Row, cellRef.ColumnNumber));
                break;
            }
            case CellRefNode cellRef:
                refs.Add(new CellAddress(defaultSheetId, cellRef.Row, cellRef.ColumnNumber));
                break;

            case RangeRefNode range when range.SheetName is not null:
            {
                var targetSheet = workbook?.GetSheet(range.SheetName);
                if (targetSheet is not null)
                {
                    refs.AddRange(CreateGridRange(targetSheet.Id, range));
                }
                break;
            }
            case RangeRefNode range:
            {
                refs.AddRange(CreateGridRange(defaultSheetId, range));
                break;
            }

            case NamedRangeNode named:
            {
                if (workbook is not null && workbook.TryGetNamedRange(named.Name, out var namedRange))
                {
                    refs.AddRange(namedRange);
                }
                break;
            }

            case StructuredReferenceNode structured:
            {
                if (workbook is null)
                    break;

                var structuredRange = StructuredReferenceResolver.ResolveDataBodyColumn(
                    workbook,
                    workbook.GetSheet(defaultSheetId),
                    structured.TableName,
                    structured.ColumnName,
                    formulaCell);
                if (structuredRange is null)
                    break;

                refs.AddRange(structuredRange.Value);
                break;
            }

            case StructuredCurrentRowReferenceNode currentRow:
            {
                var address = StructuredReferenceResolver.ResolveCurrentRowColumn(
                    workbook,
                    workbook?.GetSheet(defaultSheetId),
                    formulaCell,
                    currentRow.TableName,
                    currentRow.ColumnName);
                if (address is not null)
                    refs.Add(address.Value);
                break;
            }

            case BinaryOpNode binary:
                CollectReferences(binary.Left, defaultSheetId, formulaCell, workbook, refs);
                CollectReferences(binary.Right, defaultSheetId, formulaCell, workbook, refs);
                break;

            case UnaryOpNode unary:
                CollectReferences(unary.Operand, defaultSheetId, formulaCell, workbook, refs);
                break;

            case FunctionCallNode func:
                foreach (var arg in func.Arguments)
                    CollectReferences(arg, defaultSheetId, formulaCell, workbook, refs);
                break;
        }
    }

    private static GridRange CreateGridRange(SheetId sheetId, RangeRefNode range)
    {
        var start = new CellAddress(sheetId, range.Start.Row, range.Start.ColumnNumber);
        var end = new CellAddress(sheetId, range.End.Row, range.End.ColumnNumber);
        return new GridRange(start, end);
    }

    private sealed class FormulaDependencySet
    {
        public HashSet<CellAddress> Cells { get; } = [];
        public List<GridRange> Ranges { get; } = [];

        public void Add(CellAddress address) => Cells.Add(address);

        public void AddRange(GridRange range)
        {
            if (range.CellCount > CompactRangeCellThreshold)
            {
                Ranges.Add(range);
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
