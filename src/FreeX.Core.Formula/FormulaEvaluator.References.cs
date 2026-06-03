using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private static ScalarValue EvaluateNamedRange(NamedRangeNode node, IEvalContext context)
    {
        // Local LET/LAMBDA bindings shadow workbook named ranges.
        var binding = context.TryResolveLambdaBinding(node.Name);
        if (binding is not null) return binding;

        var range = context.TryResolveNamedRange(node.Name);
        if (range is null)
            return ErrorValue.Name;

        // Bare named range reference outside a function: return top-left cell value.
        // For 2D named ranges this is intentionally lossy — full implicit-intersection
        // semantics (Excel 365 spill behaviour) are a Phase 5 enhancement.
        return BuildRangeValueOrError(range.Value, context);
    }

    private static ScalarValue EvaluateRange(RangeRefNode range, IEvalContext context)
    {
        // A bare range reference outside a function context returns the first value
        // (This matches Excel's implicit intersection behavior for simple cases)
        return range.SheetName is not null
            ? context.GetCellValue(range.SheetName, range.Start.Row, range.Start.ColumnNumber)
            : context.GetCellValue(range.Start.Row, range.Start.ColumnNumber);
    }


    private ScalarValue EvaluateArrayOperand(FormulaNode node, IEvalContext context)
    {
        if (node is RangeRefNode range)
            return BuildRangeValueOrError(range, context);

        if (node is NamedRangeNode named)
        {
            var binding = context.TryResolveLambdaBinding(named.Name);
            if (binding is not null)
                return binding;

            var resolvedRange = context.TryResolveNamedRange(named.Name);
            return resolvedRange is null
                ? ErrorValue.Name
                : BuildRangeValueOrError(resolvedRange.Value, context);
        }

        if (node is StructuredReferenceNode structured)
        {
            var resolvedRange = TryResolveStructuredReferenceRange(structured, context);
            return resolvedRange is null
                ? ErrorValue.Name
                : BuildRangeValueOrError(resolvedRange.Value, context);
        }

        if (node is StructuredCurrentRowReferenceNode currentRow)
            return EvaluateCurrentRowReference(currentRow, context);

        var value = EvaluateNode(node, context);
        return value;
    }

    private static ScalarValue EvaluateStructuredReference(StructuredReferenceNode node, IEvalContext context)
    {
        var range = TryResolveStructuredReferenceRange(node, context);
        return range is null
            ? ErrorValue.Name
            : BuildRangeValueOrError(range.Value, context);
    }

    private static ScalarValue EvaluateCurrentRowReference(StructuredCurrentRowReferenceNode node, IEvalContext context)
    {
        var address = StructuredReferenceResolver.ResolveCurrentRowColumn(
            context.CurrentWorkbook,
            context.CurrentSheet,
            context.CurrentCellAddress,
            node.TableName,
            node.ColumnName);
        return address is null
            ? ErrorValue.Name
            : context.GetCellValue(address.Value.Row, address.Value.Col);
    }


    private static void AddRangeValues(
        List<ScalarValue> expandedArgs,
        IReadOnlyList<ScalarValue> values,
        bool preservesReferenceProvenance)
    {
        if (values.Count == 1 && values[0] is RangeMaterializationErrorValue)
        {
            expandedArgs.Add(values[0]);
            return;
        }

        var finalCount = (long)expandedArgs.Count + values.Count;
        if (finalCount <= int.MaxValue)
            expandedArgs.EnsureCapacity((int)finalCount);

        if (preservesReferenceProvenance)
        {
            foreach (var value in values)
                expandedArgs.Add(new ReferencedScalarValue(value));
        }
        else
        {
            foreach (var value in values)
                expandedArgs.Add(value);
        }
    }

    private static RangeValue BuildRangeValue(RangeRefNode range, IEvalContext context)
    {
        // A full-column (A:A) / full-row (1:1) reference nominally spans 1,048,576 rows or 16,384
        // columns, which exceeds the materialization cap and would otherwise return #REF! — even for
        // a single column. Excel only ever materializes the populated extent, so clamp the open end
        // down to the sheet's used range. The start is left untouched so positional access (INDEX,
        // COLUMN, ...) keeps the same Nth-element / top-left meaning.
        range = ClampOpenEndedRangeToUsed(range, context);

        // Normalize so r0 ≤ r1 and c0 ≤ c1 — Excel accepts B5:A1 and treats it as A1:B5.
        // Without this, uint subtraction wraps and produces a negative dimension.
        uint r0 = Math.Min(range.Start.Row, range.End.Row);
        uint r1 = Math.Max(range.Start.Row, range.End.Row);
        uint c0 = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        uint c1 = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);
        long rows = r1 - r0 + 1;
        long cols = c1 - c0 + 1;
        if (rows * cols > FormulaSafetyLimits.MaxMaterializedRangeCells)
            throw new FormulaEvalException("#REF!", "Range contains more than 1,000,000 cells");
        var cells = new ScalarValue[(int)rows, (int)cols];
        for (int ri = 0; ri < rows; ri++)
            for (int ci = 0; ci < cols; ci++)
            {
                cells[ri, ci] = range.SheetName is not null
                    ? context.GetCellValue(range.SheetName, r0 + (uint)ri, c0 + (uint)ci)
                    : context.GetCellValue(r0 + (uint)ri, c0 + (uint)ci);
            }
        return new RangeValue(cells, r0, c0) { SheetName = range.SheetName };
    }

    // Clamp the open end of a full-column/full-row reference to the target sheet's used extent.
    // Only ranges that reach the grid limit (End at MaxRow/MaxCol) are touched; explicit bounded
    // ranges pass through unchanged. The start is preserved so element positions stay correct.
    private static RangeRefNode ClampOpenEndedRangeToUsed(RangeRefNode range, IEvalContext context)
    {
        bool fullColumn = range.End.Row >= FreeX.Core.Model.CellAddress.MaxRow;
        bool fullRow = range.End.ColumnNumber >= FreeX.Core.Model.CellAddress.MaxCol;
        if (!fullColumn && !fullRow)
            return range;

        if (context is not SheetEvalContext sheetContext)
            return range;

        var sheet = sheetContext.ResolveSheetForFastRange(range.SheetName);
        if (sheet is null)
            return range;

        uint endRow = range.End.Row;
        uint endCol = range.End.ColumnNumber;

        if (sheet.GetUsedRange() is { } used)
        {
            if (fullColumn) endRow = Math.Min(endRow, Math.Max(used.End.Row, range.Start.Row));
            if (fullRow) endCol = Math.Min(endCol, Math.Max(used.End.Col, range.Start.ColumnNumber));
        }
        else
        {
            // Empty sheet: collapse the open dimension to its start (a single blank line).
            if (fullColumn) endRow = range.Start.Row;
            if (fullRow) endCol = range.Start.ColumnNumber;
        }

        if (endRow == range.End.Row && endCol == range.End.ColumnNumber)
            return range;

        var end = range.End with
        {
            ColumnName = FreeX.Core.Model.CellAddress.NumberToColumnName(endCol),
            Row = endRow
        };
        return new RangeRefNode(range.Start, end, range.SheetName);
    }

    private static ScalarValue BuildRangeValueOrError(RangeRefNode range, IEvalContext context)
    {
        try
        {
            return BuildRangeValue(range, context);
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
    }

    private static RangeValue BuildRangeValue(FreeX.Core.Model.GridRange range, IEvalContext context)
    {
        var sheetName = context.TryGetSheetName(range.Start.Sheet);
        var start = new CellRefNode(
            FreeX.Core.Model.CellAddress.NumberToColumnName(range.Start.Col),
            range.Start.Row,
            SheetName: sheetName);
        var end = new CellRefNode(
            FreeX.Core.Model.CellAddress.NumberToColumnName(range.End.Col),
            range.End.Row,
            SheetName: sheetName);
        return BuildRangeValue(new RangeRefNode(start, end, sheetName), context);
    }

    private static ScalarValue BuildRangeValueOrError(FreeX.Core.Model.GridRange range, IEvalContext context)
    {
        try
        {
            return BuildRangeValue(range, context);
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
    }

    private static FreeX.Core.Model.GridRange? TryResolveStructuredReferenceRange(
        StructuredReferenceNode node,
        IEvalContext context)
        => StructuredReferenceResolver.ResolveDataBodyColumn(
            context.CurrentWorkbook,
            context.CurrentSheet,
            node.TableName,
            node.ColumnName,
            context.CurrentCellAddress);

    private static bool TryAsRangeRef(FormulaNode node, out RangeRefNode range)
    {
        range = node switch
        {
            RangeRefNode rr => rr,
            FullColumnRangeRefNode fcr => ToRangeRef(fcr),
            FullRowRangeRefNode frr => ToRangeRef(frr),
            _ => null!
        };
        return range is not null;
    }

    private static bool TryEvaluateReferenceDimensionFunction(
        string functionName,
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count != 1 || functionName is not ("ROWS" or "COLUMNS" or "AREAS"))
            return false;

        if (!TryAsRangeRef(node.Arguments[0], out var range))
            return false;

        if (range.SheetName is not null && !context.SheetExists(range.SheetName))
        {
            result = ErrorValue.Ref;
            return true;
        }

        if (functionName == "AREAS")
        {
            result = new NumberValue(1);
            return true;
        }

        uint r0 = Math.Min(range.Start.Row, range.End.Row);
        uint r1 = Math.Max(range.Start.Row, range.End.Row);
        uint c0 = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        uint c1 = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);
        result = functionName == "ROWS"
            ? new NumberValue(r1 - r0 + 1)
            : new NumberValue(c1 - c0 + 1);
        return true;
    }

    private bool TryEvaluateIndexDirectRange(FunctionCallNode node, IEvalContext context, out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (!TryAsRangeRef(node.Arguments.Count > 0 ? node.Arguments[0] : new OmittedArgumentNode(), out var range))
            return false;

        if (node.Arguments.Count is < 2 or > 3)
        {
            result = ErrorValue.Value;
            return true;
        }

        if (TryAsRangeRef(node.Arguments[1], out _) ||
            (node.Arguments.Count > 2 && TryAsRangeRef(node.Arguments[2], out _)))
            return false;

        if (range.SheetName is not null && !context.SheetExists(range.SheetName))
        {
            result = ErrorValue.Ref;
            return true;
        }

        var rowValue = EvaluateNode(node.Arguments[1], context);
        if (rowValue is ErrorValue rowError)
        {
            result = rowError;
            return true;
        }

        var columnValue = node.Arguments.Count > 2
            ? EvaluateNode(node.Arguments[2], context)
            : BlankValue.Instance;
        if (columnValue is ErrorValue columnError)
        {
            result = columnError;
            return true;
        }

        var rowCoerced = CoerceToNumber(rowValue);
        if (rowCoerced is ErrorValue rowCoerceError)
        {
            result = rowCoerceError;
            return true;
        }

        var columnCoerced = columnValue is BlankValue ? new NumberValue(1) : CoerceToNumber(columnValue);
        if (columnCoerced is ErrorValue columnCoerceError)
        {
            result = columnCoerceError;
            return true;
        }

        var rawRow = ((NumberValue)rowCoerced).Value;
        var rawColumn = ((NumberValue)columnCoerced).Value;
        if (!double.IsFinite(rawRow) || rawRow < int.MinValue || rawRow > int.MaxValue ||
            !double.IsFinite(rawColumn) || rawColumn < int.MinValue || rawColumn > int.MaxValue)
        {
            result = ErrorValue.Value;
            return true;
        }

        int rowIndex = (int)rawRow;
        int columnIndex = (int)rawColumn;

        uint startRow = Math.Min(range.Start.Row, range.End.Row);
        uint endRow = Math.Max(range.Start.Row, range.End.Row);
        uint startCol = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        uint endCol = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);
        long rowCount = endRow - startRow + 1L;
        long colCount = endCol - startCol + 1L;

        if (node.Arguments.Count == 2)
        {
            if (rowCount == 1)
            {
                columnIndex = rowIndex;
                rowIndex = 1;
            }
            else if (colCount == 1)
            {
                columnIndex = 1;
            }
        }

        if (rowIndex < 0 || columnIndex < 0)
        {
            result = ErrorValue.Value;
            return true;
        }

        if (rowIndex > rowCount || columnIndex > colCount)
        {
            result = ErrorValue.Ref;
            return true;
        }

        if (rowIndex == 0 && columnIndex == 0)
        {
            result = BuildRangeValueOrError(CreateRangeRef(startRow, startCol, endRow, endCol, range.SheetName), context);
            return true;
        }

        if (rowIndex == 0)
        {
            var targetCol = startCol + (uint)columnIndex - 1;
            result = BuildRangeValueOrError(CreateRangeRef(startRow, targetCol, endRow, targetCol, range.SheetName), context);
            return true;
        }

        if (columnIndex == 0)
        {
            var targetRow = startRow + (uint)rowIndex - 1;
            result = BuildRangeValueOrError(CreateRangeRef(targetRow, startCol, targetRow, endCol, range.SheetName), context);
            return true;
        }

        var row = startRow + (uint)rowIndex - 1;
        var col = startCol + (uint)columnIndex - 1;
        result = range.SheetName is not null
            ? context.GetCellValue(range.SheetName, row, col)
            : context.GetCellValue(row, col);
        return true;
    }

    private static RangeRefNode CreateRangeRef(uint startRow, uint startCol, uint endRow, uint endCol, string? sheetName)
    {
        var start = new CellRefNode(CellAddress.NumberToColumnName(startCol), startRow, SheetName: sheetName);
        var end = new CellRefNode(CellAddress.NumberToColumnName(endCol), endRow);
        return new RangeRefNode(start, end, sheetName);
    }

    private static RangeRefNode ToRangeRef(FullColumnRangeRefNode range)
    {
        var start = new CellRefNode(range.StartColumnName, 1, range.IsStartAbsolute, false, range.SheetName);
        var end = new CellRefNode(range.EndColumnName, CellAddress.MaxRow, range.IsEndAbsolute);
        return new RangeRefNode(start, end, range.SheetName);
    }

    private static RangeRefNode ToRangeRef(FullRowRangeRefNode range)
    {
        var start = new CellRefNode("A", range.StartRow, false, range.IsStartAbsolute, range.SheetName);
        var end = new CellRefNode(CellAddress.NumberToColumnName(CellAddress.MaxCol), range.EndRow, false, range.IsEndAbsolute);
        return new RangeRefNode(start, end, range.SheetName);
    }


    private ScalarValue EvaluateIsRef(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 1) return ErrorValue.Value;
        var arg = node.Arguments[0];
        return arg switch
        {
            CellRefNode cell  => cell.SheetName is null || context.SheetExists(cell.SheetName) ? TrueValue : FalseValue,
            RangeRefNode rng  => rng.SheetName is null || context.SheetExists(rng.SheetName) ? TrueValue : FalseValue,
            FullColumnRangeRefNode col => col.SheetName is null || context.SheetExists(col.SheetName) ? TrueValue : FalseValue,
            FullRowRangeRefNode row => row.SheetName is null || context.SheetExists(row.SheetName) ? TrueValue : FalseValue,
            NamedRangeNode nm => context.TryResolveNamedRange(nm.Name) is not null ? TrueValue : FalseValue,
            FunctionCallNode fn when fn.FunctionName is "OFFSET" or "INDIRECT"
                => EvaluateReferenceReturningIsRef(fn, context),
            _                 => FalseValue
        };
    }

    private ScalarValue EvaluateReferenceReturningIsRef(FunctionCallNode node, IEvalContext context)
    {
        var value = EvaluateNode(node, context);

        return value is ErrorValue error
            ? error == ErrorValue.Ref ? FalseValue : error
            : TrueValue;
    }

    private ScalarValue EvaluateIsFormula(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 1) return ErrorValue.Value;
        var error = TryResolveReferenceTopLeftCell(
            node.Arguments[0],
            context,
            unsupportedReferenceError: ErrorValue.Value,
            mapReferenceFunctionValueErrorToNA: false,
            out var cell);

        return error is not null ? error : cell?.HasFormula == true ? TrueValue : FalseValue;
    }

    private ScalarValue EvaluateFormulaText(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 1) return ErrorValue.NA;
        var error = TryResolveReferenceTopLeftCell(
            node.Arguments[0],
            context,
            unsupportedReferenceError: ErrorValue.NA,
            mapReferenceFunctionValueErrorToNA: true,
            out var cell);

        if (error is not null) return error;
        if (cell is null || !cell.HasFormula) return ErrorValue.NA;
        var formulaText = cell.FormulaText!;
        return new TextValue(formulaText.StartsWith('=') ? formulaText : "=" + formulaText);
    }

    private ErrorValue? TryResolveReferenceTopLeftCell(
        FormulaNode node,
        IEvalContext context,
        ErrorValue unsupportedReferenceError,
        bool mapReferenceFunctionValueErrorToNA,
        out Cell? cell)
    {
        cell = null;

        if (TryAsRangeRef(node, out var rangeRef))
            return TryGetTopLeftCell(rangeRef, context, out cell);

        if (node is CellRefNode cellRef)
            return TryGetCell(cellRef.SheetName, cellRef.Row, cellRef.ColumnNumber, context, out cell);

        if (node is NamedRangeNode named)
        {
            var range = context.TryResolveNamedRange(named.Name);
            if (range is null) return ErrorValue.Name;

            var modelRange = range.Value;
            return TryGetCell(
                context.TryGetSheetName(modelRange.Start.Sheet),
                modelRange.Start.Row,
                modelRange.Start.Col,
                context,
                out cell);
        }

        if (node is FunctionCallNode fn && fn.FunctionName is "OFFSET" or "INDIRECT")
        {
            var reference = EvaluateReferenceReturningFunction(fn, context);
            if (reference is ErrorValue error)
                return mapReferenceFunctionValueErrorToNA && error == ErrorValue.Value ? ErrorValue.NA : error;

            var range = (RangeValue)reference;
            return TryGetCell(range.SheetName, range.StartRow, range.StartCol, context, out cell);
        }

        return unsupportedReferenceError;
    }

    private static ErrorValue? TryGetTopLeftCell(RangeRefNode range, IEvalContext context, out Cell? cell) =>
        TryGetCell(range.SheetName, range.Start.Row, range.Start.ColumnNumber, context, out cell);

    private static ErrorValue? TryGetCell(
        string? sheetName,
        uint row,
        uint column,
        IEvalContext context,
        out Cell? cell)
    {
        if (sheetName is not null && !context.SheetExists(sheetName))
        {
            cell = null;
            return ErrorValue.Ref;
        }

        cell = sheetName is not null
            ? context.TryGetCell(sheetName, row, column)
            : context.TryGetCell(row, column);
        return null;
    }

    private ScalarValue EvaluateCellInfo(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count is < 1 or > 2) return ErrorValue.Value;

        var infoType = EvaluateNode(node.Arguments[0], context);
        if (infoType is ErrorValue error) return error;
        if (node.Arguments.Count == 1)
            return BuiltInFunctions.CellInfo([infoType], context);

        var reference = EvaluateCellReferenceArgument(node.Arguments[1], context);
        return reference is ErrorValue refError
            ? refError
            : BuiltInFunctions.CellInfo([infoType, reference], context);
    }

    private ScalarValue EvaluateCellReferenceArgument(FormulaNode node, IEvalContext context)
    {
        if (TryAsRangeRef(node, out var range))
        {
            if (range.SheetName is not null && !context.SheetExists(range.SheetName))
                return ErrorValue.Ref;
            return BuildRangeValueOrError(range, context);
        }

        if (node is CellRefNode cellRef)
        {
            if (cellRef.SheetName is not null && !context.SheetExists(cellRef.SheetName))
                return ErrorValue.Ref;
            return BuildRangeValueOrError(new RangeRefNode(cellRef, cellRef, cellRef.SheetName), context);
        }

        if (node is NamedRangeNode named)
        {
            var rangeRef = context.TryResolveNamedRange(named.Name);
            return rangeRef is null ? ErrorValue.Name : BuildRangeValueOrError(rangeRef.Value, context);
        }

        if (node is FunctionCallNode fn && fn.FunctionName is "OFFSET" or "INDIRECT")
        {
            var value = EvaluateReferenceReturningFunction(fn, context);
            return value is ErrorValue or RangeValue ? value : ErrorValue.Value;
        }

        return ErrorValue.Value;
    }

    private ScalarValue EvaluateReferenceReturningFunction(FunctionCallNode node, IEvalContext context)
    {
        return node.FunctionName switch
        {
            "OFFSET"   => EvaluateOffsetReference(node, context),
            "INDIRECT" => EvaluateIndirectReference(node, context),
            _          => ErrorValue.Value
        };
    }

    private ScalarValue EvaluateIndirectReference(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count is < 1 or > 2) return ErrorValue.Value;

        var args = new List<ScalarValue>(node.Arguments.Count);
        foreach (var argument in node.Arguments)
        {
            var value = EvaluateNode(argument, context);
            if (value is ErrorValue error) return error;
            args.Add(value);
        }

        return BuiltInFunctions.IndirectReference(args, context);
    }

    private ScalarValue EvaluateOffset(FunctionCallNode node, IEvalContext context)
    {
        var reference = EvaluateOffsetReference(node, context);
        if (reference is ErrorValue error) return error;
        var range = (RangeValue)reference;
        if (range.RowCount == 1 && range.ColCount == 1)
            return range.Cells[0, 0];
        return range;
    }

    private ScalarValue EvaluateOffsetReference(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count is < 3 or > 5) return ErrorValue.Value;
        var baseArg = node.Arguments[0];

        uint baseRow, baseCol; int baseHeight, baseWidth; string? baseSheet = null;
        switch (baseArg)
        {
            case CellRefNode cellRef:
                if (cellRef.SheetName is not null && !context.SheetExists(cellRef.SheetName))
                    return ErrorValue.Ref;
                baseRow = cellRef.Row; baseCol = cellRef.ColumnNumber;
                baseHeight = 1; baseWidth = 1;
                baseSheet = cellRef.SheetName;
                break;
            case RangeRefNode rangeRef:
                if (rangeRef.SheetName is not null && !context.SheetExists(rangeRef.SheetName))
                    return ErrorValue.Ref;
                uint r0 = Math.Min(rangeRef.Start.Row, rangeRef.End.Row);
                uint r1 = Math.Max(rangeRef.Start.Row, rangeRef.End.Row);
                uint c0 = Math.Min(rangeRef.Start.ColumnNumber, rangeRef.End.ColumnNumber);
                uint c1 = Math.Max(rangeRef.Start.ColumnNumber, rangeRef.End.ColumnNumber);
                baseRow = r0; baseCol = c0;
                baseHeight = (int)(r1 - r0 + 1);
                baseWidth = (int)(c1 - c0 + 1);
                baseSheet = rangeRef.SheetName;
                break;
            case FullColumnRangeRefNode fullColumnRange:
                if (fullColumnRange.SheetName is not null && !context.SheetExists(fullColumnRange.SheetName))
                    return ErrorValue.Ref;
                uint fullColumnStart = CellAddress.ColumnNameToNumber(fullColumnRange.StartColumnName);
                uint fullColumnEnd = CellAddress.ColumnNameToNumber(fullColumnRange.EndColumnName);
                uint fc0 = Math.Min(fullColumnStart, fullColumnEnd);
                uint fc1 = Math.Max(fullColumnStart, fullColumnEnd);
                baseRow = 1; baseCol = fc0;
                baseHeight = (int)CellAddress.MaxRow;
                baseWidth = (int)(fc1 - fc0 + 1);
                baseSheet = fullColumnRange.SheetName;
                break;
            case FullRowRangeRefNode fullRowRange:
                if (fullRowRange.SheetName is not null && !context.SheetExists(fullRowRange.SheetName))
                    return ErrorValue.Ref;
                uint fr0 = Math.Min(fullRowRange.StartRow, fullRowRange.EndRow);
                uint fr1 = Math.Max(fullRowRange.StartRow, fullRowRange.EndRow);
                baseRow = fr0; baseCol = 1;
                baseHeight = (int)(fr1 - fr0 + 1);
                baseWidth = (int)CellAddress.MaxCol;
                baseSheet = fullRowRange.SheetName;
                break;
            case NamedRangeNode nm:
                var nr = context.TryResolveNamedRange(nm.Name);
                if (nr is null) return ErrorValue.Name;
                var g = nr.Value;
                uint nr0 = Math.Min(g.Start.Row, g.End.Row);
                uint nr1 = Math.Max(g.Start.Row, g.End.Row);
                uint nc0 = Math.Min(g.Start.Col, g.End.Col);
                uint nc1 = Math.Max(g.Start.Col, g.End.Col);
                baseRow = nr0; baseCol = nc0;
                baseHeight = (int)(nr1 - nr0 + 1);
                baseWidth = (int)(nc1 - nc0 + 1);
                baseSheet = context.TryGetSheetName(g.Start.Sheet);
                break;
            default:
                return ErrorValue.Value;
        }

        var rowsArg = EvaluateNode(node.Arguments[1], context);
        if (rowsArg is ErrorValue er) return er;
        var colsArg = EvaluateNode(node.Arguments[2], context);
        if (colsArg is ErrorValue ec) return ec;
        var rowsCoerced = CoerceToNumber(rowsArg);
        if (rowsCoerced is ErrorValue erc) return erc;
        var colsCoerced = CoerceToNumber(colsArg);
        if (colsCoerced is ErrorValue ecc) return ecc;
        double dRows = ((NumberValue)rowsCoerced).Value;
        double dCols = ((NumberValue)colsCoerced).Value;
        if (!double.IsFinite(dRows) || !double.IsFinite(dCols)) return ErrorValue.Value;
        long rowsOff = (long)Math.Truncate(dRows);
        long colsOff = (long)Math.Truncate(dCols);

        int height = baseHeight;
        int width = baseWidth;
        if (node.Arguments.Count >= 4 && node.Arguments[3] is not OmittedArgumentNode)
        {
            var hArg = EvaluateNode(node.Arguments[3], context);
            if (hArg is ErrorValue eh) return eh;
            if (hArg is not BlankValue)
            {
                var hc = CoerceToNumber(hArg);
                if (hc is ErrorValue ehc) return ehc;
                double dh = ((NumberValue)hc).Value;
                if (!double.IsFinite(dh)) return ErrorValue.Value;
                height = (int)Math.Truncate(dh);
            }
        }
        if (node.Arguments.Count == 5 && node.Arguments[4] is not OmittedArgumentNode)
        {
            var wArg = EvaluateNode(node.Arguments[4], context);
            if (wArg is ErrorValue ew) return ew;
            if (wArg is not BlankValue)
            {
                var wc = CoerceToNumber(wArg);
                if (wc is ErrorValue ewc) return ewc;
                double dw = ((NumberValue)wc).Value;
                if (!double.IsFinite(dw)) return ErrorValue.Value;
                width = (int)Math.Truncate(dw);
            }
        }
        if (height < 0 || width < 0) return ErrorValue.Value;
        if (height == 0 || width == 0) return ErrorValue.Ref;

        long startRow = (long)baseRow + rowsOff;
        long startCol = (long)baseCol + colsOff;
        long endRow = startRow + height - 1;
        long endCol = startCol + width - 1;
        long r0Final = Math.Min(startRow, endRow);
        long r1Final = Math.Max(startRow, endRow);
        long c0Final = Math.Min(startCol, endCol);
        long c1Final = Math.Max(startCol, endCol);
        if (r0Final < 1 || c0Final < 1 ||
            r1Final > FreeX.Core.Model.CellAddress.MaxRow ||
            c1Final > FreeX.Core.Model.CellAddress.MaxCol)
            return ErrorValue.Ref;

        int rowSpan = (int)(r1Final - r0Final + 1);
        int colSpan = (int)(c1Final - c0Final + 1);
        if ((long)rowSpan * colSpan > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Ref;

        var cells = new ScalarValue[rowSpan, colSpan];
        for (int ri = 0; ri < rowSpan; ri++)
            for (int ci = 0; ci < colSpan; ci++)
            {
                cells[ri, ci] = baseSheet is not null
                    ? context.GetCellValue(baseSheet, (uint)(r0Final + ri), (uint)(c0Final + ci))
                    : context.GetCellValue((uint)(r0Final + ri), (uint)(c0Final + ci));
            }
        return new RangeValue(cells, (uint)r0Final, (uint)c0Final) { SheetName = baseSheet };
    }

}
