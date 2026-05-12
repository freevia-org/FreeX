using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

/// <summary>
/// Coordinates recalculation of formula cells when values change.
/// Uses the dependency graph to determine order and evaluates only dirty cells.
/// </summary>
public sealed class RecalcEngine
{
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
        var allChanged = changedCells.Concat(_volatileCells).ToList();
        var plan = _graph.GetRecalcOrder(allChanged);
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

        // Volatile cells must evaluate first; then non-volatile dependents in topological order
        var toEvaluate = _volatileCells
            .Concat(plan.OrderedCells.Where(c => !_volatileCells.Contains(c)))
            .ToList();

        foreach (var addr in toEvaluate)
        {
            var sheet = workbook.GetSheet(addr.Sheet);
            if (sheet is null) continue;

            var cell = sheet.GetCell(addr);
            if (cell is null || !cell.HasFormula) continue;

            try
            {
                var result = _evaluator.Evaluate("=" + cell.FormulaText, sheet, workbook);
                cell.Value = result;
                recalculated.Add(addr);
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
        }

        return new RecalcReport(recalculated, errors, plan.CyclicCells);
    }

    /// <summary>
    /// Extract cell references from a formula AST and register them in the dependency graph.
    /// Call this whenever a formula is set on a cell.
    /// </summary>
    public void RegisterFormulaDependencies(CellAddress formulaCell, FormulaNode ast, SheetId sheetId, Freexcel.Core.Model.Workbook? workbook = null)
    {
        var refs = new HashSet<CellAddress>();
        CollectReferences(ast, sheetId, workbook, refs);
        _graph.SetDependencies(formulaCell, refs);

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

    private static void CollectReferences(FormulaNode node, SheetId defaultSheetId, Freexcel.Core.Model.Workbook? workbook, HashSet<CellAddress> refs)
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
                    for (var r = range.Start.Row; r <= range.End.Row; r++)
                        for (var c = range.Start.ColumnNumber; c <= range.End.ColumnNumber; c++)
                            refs.Add(new CellAddress(targetSheet.Id, r, c));
                break;
            }
            case RangeRefNode range:
                for (var r = range.Start.Row; r <= range.End.Row; r++)
                    for (var c = range.Start.ColumnNumber; c <= range.End.ColumnNumber; c++)
                        refs.Add(new CellAddress(defaultSheetId, r, c));
                break;

            case BinaryOpNode binary:
                CollectReferences(binary.Left, defaultSheetId, workbook, refs);
                CollectReferences(binary.Right, defaultSheetId, workbook, refs);
                break;

            case UnaryOpNode unary:
                CollectReferences(unary.Operand, defaultSheetId, workbook, refs);
                break;

            case FunctionCallNode func:
                foreach (var arg in func.Arguments)
                    CollectReferences(arg, defaultSheetId, workbook, refs);
                break;
        }
    }
}

/// <summary>Report of a recalculation pass.</summary>
public sealed record RecalcReport(
    IReadOnlyList<CellAddress> RecalculatedCells,
    IReadOnlyList<(CellAddress Cell, string Error)> Errors,
    IReadOnlyList<CellAddress> CyclicCells);
