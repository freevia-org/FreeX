using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private SheetEvalContext? _singleSheetEvalContext;

    private SheetEvalContext GetSingleSheetEvalContext(Sheet sheet)
    {
        var cached = _singleSheetEvalContext;
        if (cached is not null && ReferenceEquals(cached.SourceSheet, sheet))
            return cached;

        cached = new SheetEvalContext(sheet, null, this, null);
        _singleSheetEvalContext = cached;
        return cached;
    }


    // ── Evaluation contexts ────────────────────────────────────────────────

    private sealed class SheetEvalContext : IEvalContext
    {
        private readonly Sheet _sheet;
        private readonly FreeX.Core.Model.Workbook? _workbook;
        private readonly FormulaEvaluator _evaluator;
        private readonly FreeX.Core.Model.CellAddress? _currentCellAddress;
        private Dictionary<string, FreeX.Core.Model.Sheet?>? _sheetNameCache;

        public readonly Sheet SourceSheet;

        public SheetEvalContext(
            Sheet sheet,
            FreeX.Core.Model.Workbook? workbook,
            FormulaEvaluator evaluator,
            FreeX.Core.Model.CellAddress? currentCellAddress)
        {
            _sheet = sheet;
            SourceSheet = sheet;
            _workbook = workbook;
            _evaluator = evaluator;
            _currentCellAddress = currentCellAddress;
        }

        public ScalarValue GetCellValue(uint row, uint col) => _sheet.GetValue(row, col);

        public ScalarValue GetCellValue(string sheetName, uint row, uint col)
        {
            var target = ResolveSheet(sheetName);
            if (target is null) return ErrorValue.Ref;
            return target.GetValue(row, col);
        }

        public IReadOnlyList<ScalarValue> GetRangeValues(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var r0 = Math.Min(startRow, endRow); var r1 = Math.Max(startRow, endRow);
            var c0 = Math.Min(startCol, endCol); var c1 = Math.Max(startCol, endCol);
            var values = CreateRangeValueList(r0, c0, r1, c1);
            if (values is null) return [new RangeMaterializationErrorValue(ErrorValue.Ref)];
            for (var r = r0; r <= r1; r++)
                for (var c = c0; c <= c1; c++)
                    values.Add(_sheet.GetValue(r, c));
            return values;
        }

        public IReadOnlyList<ScalarValue> GetRangeValues(string sheetName, uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var target = ResolveSheet(sheetName);
            if (target is null) return [ErrorValue.Ref];
            var r0 = Math.Min(startRow, endRow); var r1 = Math.Max(startRow, endRow);
            var c0 = Math.Min(startCol, endCol); var c1 = Math.Max(startCol, endCol);
            var values = CreateRangeValueList(r0, c0, r1, c1);
            if (values is null) return [new RangeMaterializationErrorValue(ErrorValue.Ref)];
            for (var r = r0; r <= r1; r++)
                for (var c = c0; c <= c1; c++)
                    values.Add(target.GetValue(r, c));
            return values;
        }

        private static List<ScalarValue>? CreateRangeValueList(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var count = FormulaSafetyLimits.GetRangeCellCount(startRow, startCol, endRow, endCol);
            return count <= FormulaSafetyLimits.MaxMaterializedRangeCells
                ? new List<ScalarValue>((int)count)
                : null;
        }

        public FreeX.Core.Model.GridRange? TryResolveNamedRange(string name)
        {
            if (_workbook is null) return null;
            if (_workbook.TryGetNamedRange(name, out var range))
                return range;
            return null;
        }

        public string? TryGetSheetName(FreeX.Core.Model.SheetId sheetId)
            => _workbook?.GetSheet(sheetId)?.Name;

        public bool SheetExists(string sheetName) => ResolveSheet(sheetName) is not null;

        public bool IsRowHidden(uint row) => _sheet.IsRowEffectivelyHidden(row);

        public bool IsRowHidden(string sheetName, uint row)
            => _workbook?.GetSheet(sheetName)?.IsRowEffectivelyHidden(row) ?? false;

        public bool IsRowFilterHidden(uint row) => _sheet.FilterHiddenRows.Contains(row);

        public bool IsRowFilterHidden(string sheetName, uint row)
            => _workbook?.GetSheet(sheetName)?.FilterHiddenRows.Contains(row) ?? false;

        public FreeX.Core.Model.Sheet? CurrentSheet => _sheet;

        public FreeX.Core.Model.Workbook? CurrentWorkbook => _workbook;

        public FreeX.Core.Model.CellAddress? CurrentCellAddress => _currentCellAddress;

        public FreeX.Core.Model.Cell? TryGetCell(uint row, uint col) => _sheet.GetCell(row, col);

        public FreeX.Core.Model.Cell? TryGetCell(string sheetName, uint row, uint col)
            => ResolveSheet(sheetName)?.GetCell(row, col);

        public ScalarValue? TryResolveLambdaBinding(string name) => null;

        public FreeX.Core.Model.Sheet? ResolveSheetForFastRange(string? sheetName)
            => sheetName is null ? _sheet : ResolveSheet(sheetName);

        public ScalarValue InvokeLambda(LambdaValue lambda, IReadOnlyList<ScalarValue> args)
        {
            if (args.Count != lambda.Parameters.Count) return ErrorValue.Value;
            var bindings = new Dictionary<string, ScalarValue>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lambda.Parameters.Count; i++)
                bindings[lambda.Parameters[i]] = args[i];
            return _evaluator.EvaluateNode(lambda.Body, new ScopedEvalContext(this, bindings, _evaluator));
        }

        private FreeX.Core.Model.Sheet? ResolveSheet(string sheetName)
        {
            if (_workbook is null) return null;

            _sheetNameCache ??= new Dictionary<string, FreeX.Core.Model.Sheet?>(StringComparer.OrdinalIgnoreCase);
            if (_sheetNameCache.TryGetValue(sheetName, out var cachedSheet))
                return cachedSheet;

            var resolvedSheet = _workbook.GetSheet(sheetName);
            _sheetNameCache[sheetName] = resolvedSheet;
            return resolvedSheet;
        }
    }

    // Wraps an IEvalContext with an extra layer of local name→value bindings (from LET).
    // Bindings in this layer shadow the inner context and can be mutated by EvaluateLet
    // before the body is evaluated (enabling forward references within the same LET).
    private sealed class ScopedEvalContext : IEvalContext
    {
        private readonly IEvalContext _inner;
        private readonly Dictionary<string, ScalarValue> _bindings;
        private readonly FormulaEvaluator _evaluator;

        public ScopedEvalContext(IEvalContext inner, Dictionary<string, ScalarValue> bindings, FormulaEvaluator evaluator)
        {
            _inner = inner;
            _bindings = bindings;
            _evaluator = evaluator;
        }

        public ScalarValue GetCellValue(uint row, uint col) => _inner.GetCellValue(row, col);
        public ScalarValue GetCellValue(string sn, uint row, uint col) => _inner.GetCellValue(sn, row, col);
        public IReadOnlyList<ScalarValue> GetRangeValues(uint r0, uint c0, uint r1, uint c1) => _inner.GetRangeValues(r0, c0, r1, c1);
        public IReadOnlyList<ScalarValue> GetRangeValues(string sn, uint r0, uint c0, uint r1, uint c1) => _inner.GetRangeValues(sn, r0, c0, r1, c1);
        public FreeX.Core.Model.GridRange? TryResolveNamedRange(string name) => _inner.TryResolveNamedRange(name);
        public string? TryGetSheetName(FreeX.Core.Model.SheetId id) => _inner.TryGetSheetName(id);
        public bool SheetExists(string sn) => _inner.SheetExists(sn);
        public bool IsRowHidden(uint row) => _inner.IsRowHidden(row);
        public bool IsRowHidden(string sn, uint row) => _inner.IsRowHidden(sn, row);
        public bool IsRowFilterHidden(uint row) => _inner.IsRowFilterHidden(row);
        public bool IsRowFilterHidden(string sn, uint row) => _inner.IsRowFilterHidden(sn, row);
        public FreeX.Core.Model.Sheet? CurrentSheet => _inner.CurrentSheet;
        public FreeX.Core.Model.Workbook? CurrentWorkbook => _inner.CurrentWorkbook;
        public FreeX.Core.Model.CellAddress? CurrentCellAddress => _inner.CurrentCellAddress;
        public FreeX.Core.Model.Cell? TryGetCell(uint row, uint col) => _inner.TryGetCell(row, col);
        public FreeX.Core.Model.Cell? TryGetCell(string sn, uint row, uint col) => _inner.TryGetCell(sn, row, col);

        public ScalarValue? TryResolveLambdaBinding(string name) =>
            _bindings.TryGetValue(name, out var v) ? v : _inner.TryResolveLambdaBinding(name);

        public ScalarValue InvokeLambda(LambdaValue lambda, IReadOnlyList<ScalarValue> args)
        {
            if (args.Count != lambda.Parameters.Count) return ErrorValue.Value;
            var nb = new Dictionary<string, ScalarValue>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lambda.Parameters.Count; i++) nb[lambda.Parameters[i]] = args[i];
            return _evaluator.EvaluateNode(lambda.Body, new ScopedEvalContext(this, nb, _evaluator));
        }
    }
}
