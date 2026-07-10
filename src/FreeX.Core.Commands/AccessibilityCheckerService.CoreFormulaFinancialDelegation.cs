using System;
using System.Collections.Generic;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

// De-duplication scaffolding: the accessibility checker's ConditionalFormula engine historically carried
// its own copy of the Excel function library (FormulaFinancial*Scalar etc.). Those copies drift from the
// single source of truth in FreeX.Core.Formula (BuiltInFunctions) -- e.g. they duplicated the round-20
// Actual/Actual + DDB bugs. This scaffolding lets the shadow financial scalars DELEGATE to the Core.Formula
// implementations by name (via the public BuiltInFunctions.TryGet registry), so a single implementation is
// maintained. The financial scalar functions operate purely on pre-evaluated scalar arguments and never
// touch cell/range/name/context state, so a no-op IEvalContext is sufficient.
public static partial class AccessibilityCheckerService
{
    private sealed class CoreFormulaScalarEvalContext : IEvalContext
    {
        public static readonly CoreFormulaScalarEvalContext Instance = new();

        public ScalarValue GetCellValue(uint row, uint col) => BlankValue.Instance;
        public ScalarValue GetCellValue(string sheetName, uint row, uint col) => BlankValue.Instance;
        public IReadOnlyList<ScalarValue> GetRangeValues(uint startRow, uint startCol, uint endRow, uint endCol) =>
            Array.Empty<ScalarValue>();
        public IReadOnlyList<ScalarValue> GetRangeValues(string sheetName, uint startRow, uint startCol, uint endRow, uint endCol) =>
            Array.Empty<ScalarValue>();
        public GridRange? TryResolveNamedRange(string name) => null;
        public string? TryGetSheetName(SheetId sheetId) => null;
        public bool SheetExists(string sheetName) => false;
        public bool IsRowHidden(uint row) => false;
        public bool IsRowHidden(string sheetName, uint row) => false;
        public bool IsRowFilterHidden(uint row) => false;
        public bool IsRowFilterHidden(string sheetName, uint row) => false;
        public Sheet? CurrentSheet => null;
        public Workbook? CurrentWorkbook => null;
        public Cell? TryGetCell(uint row, uint col) => null;
        public Cell? TryGetCell(string sheetName, uint row, uint col) => null;
    }

    /// <summary>
    /// Invokes a FreeX.Core.Formula built-in scalar function by its Excel name with pre-evaluated numeric
    /// arguments, so the accessibility checker's ConditionalFormula engine can reuse the single
    /// source-of-truth implementation instead of a duplicated copy.
    /// </summary>
    private static ScalarValue InvokeCoreFormulaScalarFunction(string name, params ScalarValue[] arguments) =>
        BuiltInFunctions.TryGet(name, out var entry)
            ? entry.Func(arguments, CoreFormulaScalarEvalContext.Instance)
            : ErrorValue.Name;
}
