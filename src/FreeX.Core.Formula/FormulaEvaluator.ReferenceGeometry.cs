using FreeX.Core.Model;

namespace FreeX.Core.Formula;

/// <summary>Outcome of resolving a formula as worksheet reference geometry.</summary>
public enum ReferenceGeometryStatus { Success, Error, Unsupported }

/// <summary>
/// Ordered areas on success. Error is an Excel error for invalid references; Unsupported means
/// the formula may be valid but cannot yet be resolved without changing its meaning.
/// </summary>
public sealed record ReferenceGeometryResult(
    ReferenceGeometryStatus Status, IReadOnlyList<GridRange> Areas, ErrorValue? Error = null);

public sealed partial class FormulaEvaluator
{
    /// <summary>
    /// Resolves worksheet-reference geometry without reading or allocating cells in the result.
    /// Scalar arguments are evaluated against the workbook's current cached values; this method
    /// does not recalculate the workbook. External workbooks and unsupported expression forms are
    /// reported separately from Excel errors. Multi-area results retain their original order.
    /// </summary>
    public ReferenceGeometryResult ResolveReferenceGeometry(
        string formulaText, Sheet sheet, Workbook? workbook = null, CellAddress? currentCell = null)
    {
        ArgumentNullException.ThrowIfNull(formulaText);
        ArgumentNullException.ThrowIfNull(sheet);
        try
        {
            var context = new SheetEvalContext(sheet, workbook, this, currentCell);
            _effectiveMaxEvalDepth = MaxEvalDepth;
            _evalDepth = 0;
            var ast = ParseReferenceGeometryFormula(formulaText);
            return ResolveGeometryNode(ast, context, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, new GeometryBudget());
        }
        catch (FormulaParseException)
        {
            // A syntax rejection can also mean Excel syntax this parser does not support.
            // The caller must not treat that uncertainty as an Excel error and discard state.
            return GeometryUnsupported();
        }
        catch (FormulaEvalException ex)
        {
            return GeometryError(ErrorFromCode(ex.ErrorCode));
        }
    }

    private static FormulaNode ParseReferenceGeometryFormula(string formulaText)
    {
        try { return ParseFormula(formulaText); }
        catch (FormulaParseException)
        {
            // Defined-name unions are commonly written at top level, while the formula parser
            // requires the union to be parenthesized. Only retry after ordinary parsing fails.
            var body = formulaText.StartsWith('=') ? formulaText[1..] : formulaText;
            return ParseFormula("(" + body + ")");
        }
    }

    // A reference union/intersection may expand combinatorially. Keep resolution bounded without
    // pretending a valid, over-budget expression is an Excel error.
    private const int MaxGeometryAreas = 4096;
    private const long MaxGeometryIntersectionPairs = 65536;
    private const int MaxGeometryScalarVisits = 65536;
    private sealed class GeometryBudget
    {
        public long IntersectionPairs;
        public int ScalarVisits;
    }

    private static ReferenceGeometryResult GeometrySuccess(params GridRange[] areas) =>
        new(ReferenceGeometryStatus.Success, areas);
    private static ReferenceGeometryResult GeometryError(ErrorValue error) =>
        new(ReferenceGeometryStatus.Error, Array.Empty<GridRange>(), error);
    private static ReferenceGeometryResult GeometryUnsupported() =>
        new(ReferenceGeometryStatus.Unsupported, Array.Empty<GridRange>());

    private ReferenceGeometryResult ResolveGeometryNode(
        FormulaNode node, IEvalContext context, HashSet<string> resolvingNames, int depth, GeometryBudget budget)
    {
        if (depth >= MaxEvalDepth) return GeometryUnsupported();
        switch (node)
        {
            case ErrorNode error:
                return GeometryError(error.Error);
            case CellRefNode cell:
                return GeometryFromCoordinates(cell.SheetName, cell.Row, cell.ColumnNumber,
                    cell.Row, cell.ColumnNumber, context);
            case RangeRefNode { EndSheetName: not null }:
                return GeometryUnsupported();
            case RangeRefNode range:
                return GeometryFromCoordinates(range.SheetName ?? range.Start.SheetName,
                    range.Start.Row, range.Start.ColumnNumber, range.End.Row, range.End.ColumnNumber, context);
            case FullColumnRangeRefNode columns:
                return GeometryFromCoordinates(columns.SheetName, 1, columns.StartColumnNumber,
                    CellAddress.MaxRow, columns.EndColumnNumber, context);
            case FullRowRangeRefNode rows:
                return GeometryFromCoordinates(rows.SheetName, rows.StartRow, 1,
                    rows.EndRow, CellAddress.MaxCol, context);
            case NamedRangeNode named:
                return ResolveGeometryName(named, context, resolvingNames, depth + 1, budget);
            case StructuredReferenceNode structured:
                return TryResolveStructuredReferenceRange(structured, context) is { } structuredRange
                    ? GeometrySuccess(structuredRange) : GeometryError(ErrorValue.Ref);
            case UnionNode union:
            {
                var areas = new List<GridRange>();
                foreach (var area in union.Areas)
                {
                    var result = ResolveGeometryNode(area, context, resolvingNames, depth + 1, budget);
                    if (result.Status != ReferenceGeometryStatus.Success) return result;
                    if (result.Areas.Count > MaxGeometryAreas - areas.Count) return GeometryUnsupported();
                    areas.AddRange(result.Areas);
                }
                return new ReferenceGeometryResult(ReferenceGeometryStatus.Success, areas);
            }
            case IntersectionNode intersection:
            {
                var left = ResolveGeometryNode(intersection.Left, context, resolvingNames, depth + 1, budget);
                if (left.Status != ReferenceGeometryStatus.Success) return left;
                var right = ResolveGeometryNode(intersection.Right, context, resolvingNames, depth + 1, budget);
                if (right.Status != ReferenceGeometryStatus.Success) return right;
                var pairCount = (long)left.Areas.Count * right.Areas.Count;
                if (pairCount > MaxGeometryIntersectionPairs - budget.IntersectionPairs) return GeometryUnsupported();
                budget.IntersectionPairs += pairCount;
                var intersections = new List<GridRange>();
                foreach (var a in left.Areas)
                    foreach (var b in right.Areas)
                        if (GridRange.TryIntersect(a, b, out var overlap))
                        {
                            if (intersections.Count >= MaxGeometryAreas) return GeometryUnsupported();
                            intersections.Add(overlap);
                        }
                return intersections.Count == 0 ? GeometryError(ErrorValue.Null)
                    : new ReferenceGeometryResult(ReferenceGeometryStatus.Success, intersections);
            }
            case NamedRangeEndpointNode endpoint:
            {
                var left = ResolveGeometryNode(endpoint.Start, context, resolvingNames, depth + 1, budget);
                if (left.Status != ReferenceGeometryStatus.Success) return left;
                var right = ResolveGeometryNode(endpoint.End, context, resolvingNames, depth + 1, budget);
                if (right.Status != ReferenceGeometryStatus.Success) return right;
                if (left.Areas.Count != 1 || right.Areas.Count != 1 ||
                    left.Areas[0].Start.Sheet != right.Areas[0].Start.Sheet)
                    return GeometryError(ErrorValue.Value);
                return GeometrySuccess(new GridRange(left.Areas[0].Start, right.Areas[0].Start));
            }
            case FunctionCallNode function:
                return ResolveGeometryFunction(function, context, resolvingNames, depth + 1, budget);
            case UnaryOpNode { Operator: UnaryOperator.ImplicitIntersection } implicitIntersection:
            {
                var result = ResolveGeometryNode(implicitIntersection.Operand, context, resolvingNames, depth + 1, budget);
                if (result.Status != ReferenceGeometryStatus.Success) return result;
                if (result.Areas.Count != 1) return GeometryUnsupported();
                var area = result.Areas[0];
                if (area.CellCount == 1) return result;
                if (context.CurrentCellAddress is not { } current) return GeometryUnsupported();
                bool rowIn = current.Row >= area.Start.Row && current.Row <= area.End.Row;
                bool colIn = current.Col >= area.Start.Col && current.Col <= area.End.Col;
                uint? row = area.RowCount == 1 ? area.Start.Row : rowIn ? current.Row : null;
                uint? col = area.ColCount == 1 ? area.Start.Col : colIn ? current.Col : null;
                if (row is null || col is null) return GeometryError(ErrorValue.Value);
                var target = new CellAddress(area.Start.Sheet, row.Value, col.Value);
                return GeometrySuccess(new GridRange(target, target));
            }
            case NumberNode or StringNode or BooleanNode or ArrayConstantNode or BinaryOpNode or UnaryOpNode:
                return GeometryError(ErrorValue.Value);
            default:
                return GeometryUnsupported();
        }
    }

    private static ReferenceGeometryResult GeometryFromCoordinates(
        string? sheetName, uint startRow, uint startCol, uint endRow, uint endCol, IEvalContext context)
    {
        if (sheetName is not null && sheetName.Contains('[')) return GeometryUnsupported();
        Sheet? sheet = sheetName is null ? context.CurrentSheet : context.CurrentWorkbook?.GetSheet(sheetName);
        if (sheet is null && sheetName is not null && context.CurrentSheet is { } current &&
            string.Equals(current.Name, sheetName, StringComparison.OrdinalIgnoreCase)) sheet = current;
        if (sheet is null) return GeometryError(ErrorValue.Ref);
        if (startRow < 1 || endRow < 1 || startCol < 1 || endCol < 1 ||
            startRow > CellAddress.MaxRow || endRow > CellAddress.MaxRow ||
            startCol > CellAddress.MaxCol || endCol > CellAddress.MaxCol)
            return GeometryError(ErrorValue.Ref);
        return GeometrySuccess(new GridRange(new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol)));
    }

    private ReferenceGeometryResult ResolveGeometryName(
        NamedRangeNode node, IEvalContext context, HashSet<string> resolvingNames, int depth, GeometryBudget budget)
    {
        var workbook = context.CurrentWorkbook;
        var host = node.SheetQualifier is null ? context.CurrentSheet : workbook?.GetSheet(node.SheetQualifier);
        if (node.SheetQualifier?.Contains('[') == true || node.Name.Contains('[')) return GeometryUnsupported();
        if (host is null) return GeometryError(ErrorValue.Ref);
        if (workbook is null) return GeometryError(ErrorValue.Name);
        var key = host.Id + ":" + node.Name;
        if (!resolvingNames.Add(key)) return GeometryError(ErrorValue.Ref);
        try
        {
            // Workbook methods enforce scoped-formula precedence over a global named range.
            if (workbook.TryGetNamedRange(node.Name, host.Id, out var range))
                return GeometrySuccess(range);
            var text = workbook.TryGetNamedFormulaText(node.Name, host.Id);
            if (text is null) return GeometryError(ErrorValue.Name);
            // Match ordinary named-formula evaluation: the qualifier selects name scope, while
            // its expression is evaluated in the caller's context. Excel-authored definitions
            // normally make their target sheet explicit in the stored formula.
            try
            {
                var ast = ApplyRelativeNameAnchor(ParseReferenceGeometryFormula(text), context);
                return ResolveGeometryNode(ast, context, resolvingNames, depth + 1, budget);
            }
            catch (FormulaParseException) { return GeometryUnsupported(); }
        }
        finally { resolvingNames.Remove(key); }
    }

    private ReferenceGeometryResult ResolveGeometryFunction(
        FunctionCallNode node, IEvalContext context, HashSet<string> names, int depth, GeometryBudget budget)
    {
        switch (node.FunctionName.ToUpperInvariant())
        {
            case "OFFSET": return ResolveOffsetGeometry(node, context, names, depth, budget);
            case "INDEX": return ResolveIndexGeometry(node, context, names, depth, budget);
            case "NA":
            {
                var value = EvaluateNode(node, context);
                return GeometryError(value as ErrorValue ?? ErrorValue.Value);
            }
            case "ANCHORARRAY":
            {
                if (node.Arguments.Count is not (1 or 2)) return GeometryError(ErrorValue.Value);
                var anchor = ResolveGeometryNode(node.Arguments[0], context, names, depth + 1, budget);
                if (anchor.Status != ReferenceGeometryStatus.Success) return anchor;
                if (anchor.Areas.Count != 1 || anchor.Areas[0].CellCount != 1) return GeometryError(ErrorValue.Value);
                var address = anchor.Areas[0].Start;
                var sheet = context.CurrentWorkbook?.GetSheet(address.Sheet) ??
                    (context.CurrentSheet?.Id == address.Sheet ? context.CurrentSheet : null);
                if (sheet is null || !sheet.TryGetSpillExtent(address, out var rows, out var cols))
                    return GeometryError(ErrorValue.Ref);
                uint r0 = address.Row, c0 = address.Col;
                uint r1 = r0 + rows - 1, c1 = c0 + cols - 1;
                if (node.Arguments.Count == 2)
                {
                    if (node.Arguments[1] is not CellRefNode end) return GeometryError(ErrorValue.Value);
                    r0 = Math.Min(r0, end.Row); c0 = Math.Min(c0, end.ColumnNumber);
                    r1 = Math.Max(r1, end.Row); c1 = Math.Max(c1, end.ColumnNumber);
                }
                return GeometrySuccess(new GridRange(new CellAddress(sheet.Id, r0, c0),
                    new CellAddress(sheet.Id, r1, c1)));
            }
            case "CHOOSE":
            {
                if (node.Arguments.Count < 2) return GeometryError(ErrorValue.Value);
                var error = GeometryInteger(node.Arguments[0], context, names, depth, budget, out long index);
                if (error is not null) return error;
                if (index < 1 || index >= node.Arguments.Count) return GeometryError(ErrorValue.Value);
                return ResolveGeometryNode(node.Arguments[(int)index], context, names, depth + 1, budget);
            }
            case "INDIRECT":
            {
                if (node.Arguments.Count is < 1 or > 2) return GeometryError(ErrorValue.Value);
                var args = new List<ScalarValue>(node.Arguments.Count);
                foreach (var argument in node.Arguments)
                {
                    var evaluation = EvaluateGeometryScalar(argument, context, names, depth, budget, out var value);
                    if (evaluation is not null) return evaluation;
                    args.Add(value);
                }
                if (BuiltInFunctions.TryGetIndirectGeometryParts(args, context, out var reference,
                        out var referenceText, out var useA1, out var sheetName, out var error))
                    return GeometryFromCoordinates(reference.SheetName, reference.StartRow, reference.StartCol,
                        reference.EndRow, reference.EndCol, context);
                // CompleteIndirectRange rejects external references as #REF! because Excel's
                // INDIRECT requires the source workbook open. FreeX does not track that state;
                // preserve the distinction between unavailable and genuinely invalid input.
                if (sheetName?.Contains('[') == true) return GeometryUnsupported();
                if (error is ErrorValue indirectError) return GeometryError(indirectError);
                // Non-literal references can be defined names. R1C1's name syntax is still a name.
                if (referenceText.Length == 0) return GeometryError(ErrorValue.Ref);
                try
                {
                    var ast = ParseReferenceGeometryFormula(sheetName is null ? referenceText :
                        SheetNameFormatter.QuoteIfNeeded(sheetName) + "!" + referenceText);
                    if (ast is not NamedRangeNode) return GeometryError(ErrorValue.Ref);
                    var result = ResolveGeometryNode(ast, context, names, depth + 1, budget);
                    return result.Status == ReferenceGeometryStatus.Error ? GeometryError(ErrorValue.Ref) : result;
                }
                catch (FormulaParseException) { return GeometryError(ErrorValue.Ref); }
            }
            default:
            {
                // Explicitly scalar-only built-ins cannot denote a print reference. Do not
                // evaluate ranges merely to classify them: huge formulas must stay bounded.
                // Unrecognized and reference-capable functions remain Unsupported.
                if (!IsKnownScalarGeometryFunction(node.FunctionName)) return GeometryUnsupported();
                if (!node.Arguments.All(IsConstantGeometryScalar)) return GeometryError(ErrorValue.Value);
                var evaluation = EvaluateGeometryScalar(node, context, names, depth, budget, out _);
                return evaluation ?? GeometryError(ErrorValue.Value);
            }
        }
    }

    private static bool IsKnownScalarGeometryFunction(string name) => name is
        "SUM" or "AVERAGE" or "MIN" or "MAX" or "COUNT" or "COUNTA" or "ABS" or "ROUND" or
        "LEN" or "AND" or "OR" or "NOT" or "NOW" or "TODAY" or "RAND";

    private static bool IsConstantGeometryScalar(FormulaNode node) => node switch
    {
        NumberNode or StringNode or BooleanNode or ErrorNode or OmittedArgumentNode => true,
        BinaryOpNode binary => IsConstantGeometryScalar(binary.Left) && IsConstantGeometryScalar(binary.Right),
        UnaryOpNode { Operator: not UnaryOperator.ImplicitIntersection } unary => IsConstantGeometryScalar(unary.Operand),
        FunctionCallNode function => IsKnownScalarGeometryFunction(function.FunctionName) &&
                                     function.Arguments.All(IsConstantGeometryScalar),
        _ => false
    };

    // Inspect named scalar definitions before evaluating. An unavailable function or parser-
    // unsupported syntax may otherwise become #NAME?/#VALUE!, or be swallowed by IFERROR, and
    // appear to be a definitive Excel result. In either case the caller must preserve its state.
    // This is deliberately conservative for untaken branches: uncertain means Unsupported.
    private bool GeometryScalarMayBeUnsupported(
        FormulaNode node, IEvalContext context, HashSet<string> visiting, int depth, GeometryBudget budget)
    {
        if (depth >= MaxEvalDepth || ++budget.ScalarVisits > MaxGeometryScalarVisits) return true;
        switch (node)
        {
            case CellRefNode cell:
                return cell.SheetName?.Contains('[') == true;
            // A scalar expression containing a range, structured reference, or array can be
            // silently clamped or materialized by the ordinary evaluator. Until that function
            // has a geometry-aware implementation, preserve the existing print state instead.
            case RangeRefNode or FullColumnRangeRefNode or FullRowRangeRefNode or ArrayConstantNode or
                 StructuredReferenceNode or StructuredCurrentRowReferenceNode or UnionNode or
                 IntersectionNode or NamedRangeEndpointNode:
                return true;
            case FunctionCallNode function:
                // Only scalar-returning functions with scalar arguments are safe to evaluate
                // here. Root ROWS/COLUMNS/AREAS use exact geometry in EvaluateGeometryScalar.
                if (!IsKnownScalarArgumentFunction(function.FunctionName)) return true;
                return function.Arguments.Any(arg => GeometryScalarMayBeUnsupported(arg, context, visiting, depth + 1, budget));
            case NamedRangeNode named:
            {
                if (named.Name.Contains('[') || named.SheetQualifier?.Contains('[') == true) return true;
                var workbook = context.CurrentWorkbook;
                var scope = named.SheetQualifier is null ? context.CurrentSheet : workbook?.GetSheet(named.SheetQualifier);
                if (workbook is null || scope is null) return false; // ordinary #NAME?/#REF!
                if (workbook.TryGetNamedRange(named.Name, scope.Id, out _)) return true;
                var text = workbook.TryGetNamedFormulaText(named.Name, scope.Id);
                if (text is null) return false; // undefined name is a real #NAME?
                var key = scope.Id + ":" + named.Name;
                if (!visiting.Add(key)) return false; // let the evaluator report the real cycle
                try
                {
                    try
                    {
                        var body = ParseReferenceGeometryFormula(text);
                        return GeometryScalarMayBeUnsupported(body, context, visiting, depth + 1, budget);
                    }
                    catch (FormulaParseException) { return true; }
                }
                finally { visiting.Remove(key); }
            }
            case BinaryOpNode binary:
                return GeometryScalarMayBeUnsupported(binary.Left, context, visiting, depth + 1, budget) ||
                       GeometryScalarMayBeUnsupported(binary.Right, context, visiting, depth + 1, budget);
            case UnaryOpNode unary:
                return GeometryScalarMayBeUnsupported(unary.Operand, context, visiting, depth + 1, budget);
            default:
                return false;
        }
    }

    // Explicit whitelist of built-ins whose scalar arguments yield a scalar result. Functions
    // with unknown return/reference behavior are not evaluated speculatively in this path.
    private static bool IsKnownScalarArgumentFunction(string name) => name is
        "SUM" or "AVERAGE" or "MIN" or "MAX" or "COUNT" or "COUNTA" or "COUNTBLANK" or
        "COUNTIF" or "COUNTIFS" or "SUMIF" or "SUMIFS" or "AVERAGEIF" or "AVERAGEIFS" or
        "ABS" or "ROUND" or "ROUNDUP" or "ROUNDDOWN" or "INT" or "TRUNC" or "MOD" or
        "LEN" or "AND" or "OR" or "NOT" or "NOW" or "TODAY" or "RAND" or "NA" or
        "IF" or "IFERROR" or "IFNA" or "IFS" or "SWITCH" or "DATE" or "TIME" or "VALUE" or
        "ISNUMBER" or "ISTEXT" or "ISBLANK" or "ISERROR" or "ISERR" or "ISNA" or "N";

    private ReferenceGeometryResult? EvaluateGeometryScalar(
        FormulaNode node, IEvalContext context, HashSet<string> names, int depth, GeometryBudget budget, out ScalarValue value)
    {
        // Resolve reference dimensions directly from bounds, without evaluating/materializing
        // them. This also avoids truncating full-column names to the used worksheet extent.
        if (node is FunctionCallNode { FunctionName: "ROWS" or "COLUMNS" or "AREAS" } dimension)
        {
            value = BlankValue.Instance;
            if (dimension.Arguments.Count != 1) return GeometryError(ErrorValue.Value);
            var argument = dimension.Arguments[0];
            if (dimension.FunctionName != "AREAS")
            {
                if (argument is ArrayConstantNode array)
                {
                    value = new NumberValue(dimension.FunctionName == "ROWS" ? array.Rows.Count : array.Rows[0].Count);
                    return null;
                }
                if (argument is NumberNode or StringNode or BooleanNode)
                {
                    value = new NumberValue(1);
                    return null;
                }
                // Scalar computations are valid dimension arguments in Excel, but can also
                // produce arrays. Avoid treating their non-reference shape as a definite error.
                if (argument is BinaryOpNode or UnaryOpNode { Operator: not UnaryOperator.ImplicitIntersection } ||
                    argument is FunctionCallNode { FunctionName: not ("OFFSET" or "INDEX" or "CHOOSE" or "INDIRECT" or "ANCHORARRAY" or "NA") })
                    return GeometryUnsupported();
            }
            else if (argument is ArrayConstantNode or NumberNode or StringNode or BooleanNode or BinaryOpNode or UnaryOpNode)
                return GeometryUnsupported();
            var reference = ResolveGeometryNode(argument, context, names, depth + 1, budget);
            if (reference.Status != ReferenceGeometryStatus.Success)
            {
                // Names, INDEX, and CHOOSE may yield scalars or arrays that ROWS/COLUMNS
                // can measure. A value error does not establish their result type.
                if (argument is NamedRangeNode or FunctionCallNode { FunctionName: "INDEX" or "CHOOSE" } &&
                    reference.Status == ReferenceGeometryStatus.Error &&
                    reference.Error == ErrorValue.Value)
                    return GeometryUnsupported();
                return reference;
            }
            if (dimension.FunctionName == "AREAS")
            {
                value = new NumberValue(reference.Areas.Count);
                return null;
            }
            if (reference.Areas.Count != 1) return GeometryUnsupported();
            value = new NumberValue(dimension.FunctionName == "ROWS"
                ? reference.Areas[0].RowCount : reference.Areas[0].ColCount);
            return null;
        }
        // Perform this before EvaluateNode: IFERROR can otherwise hide an unavailable function
        // and produce a seemingly valid number, silently selecting the wrong reference.
        if (GeometryScalarMayBeUnsupported(node, context, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, budget))
        {
            value = BlankValue.Instance;
            return GeometryUnsupported();
        }
        value = EvaluateNode(node, context);
        return value is ErrorValue error ? GeometryError(error) : null;
    }

    private ReferenceGeometryResult? GeometryInteger(
        FormulaNode node, IEvalContext context, HashSet<string> names, int depth, GeometryBudget budget, out long number)
    {
        number = 0;
        var evaluation = EvaluateGeometryScalar(node, context, names, depth, budget, out var value);
        if (evaluation is not null) return evaluation;
        var coerced = CoerceToNumber(value);
        if (coerced is ErrorValue coerceError) return GeometryError(coerceError);
        var n = ((NumberValue)coerced).Value;
        if (!double.IsFinite(n) || n < int.MinValue || n > int.MaxValue) return GeometryError(ErrorValue.Value);
        number = (long)Math.Truncate(n);
        return null;
    }

    private ReferenceGeometryResult ResolveOffsetGeometry(
        FunctionCallNode node, IEvalContext context, HashSet<string> names, int depth, GeometryBudget budget)
    {
        if (node.Arguments.Count is < 3 or > 5) return GeometryError(ErrorValue.Value);
        var source = ResolveGeometryNode(node.Arguments[0], context, names, depth + 1, budget);
        if (source.Status != ReferenceGeometryStatus.Success) return source;
        if (source.Areas.Count != 1) return GeometryError(ErrorValue.Value);
        var area = source.Areas[0];
        var error = GeometryInteger(node.Arguments[1], context, names, depth, budget, out long rowOffset);
        if (error is not null) return error;
        error = GeometryInteger(node.Arguments[2], context, names, depth, budget, out long colOffset);
        if (error is not null) return error;
        long height = area.RowCount, width = area.ColCount;
        if (node.Arguments.Count >= 4 && node.Arguments[3] is not OmittedArgumentNode)
        {
            var evaluation = EvaluateGeometryScalar(node.Arguments[3], context, names, depth, budget, out var value);
            if (evaluation is not null) return evaluation;
            if (value is not BlankValue)
            {
                var coerced = CoerceToNumber(value);
                if (coerced is ErrorValue e2) return GeometryError(e2);
                double n = ((NumberValue)coerced).Value;
                if (!double.IsFinite(n) || n < int.MinValue || n > int.MaxValue) return GeometryError(ErrorValue.Value);
                height = (long)Math.Truncate(n);
            }
        }
        if (node.Arguments.Count >= 5 && node.Arguments[4] is not OmittedArgumentNode)
        {
            var evaluation = EvaluateGeometryScalar(node.Arguments[4], context, names, depth, budget, out var value);
            if (evaluation is not null) return evaluation;
            if (value is not BlankValue)
            {
                var coerced = CoerceToNumber(value);
                if (coerced is ErrorValue e2) return GeometryError(e2);
                double n = ((NumberValue)coerced).Value;
                if (!double.IsFinite(n) || n < int.MinValue || n > int.MaxValue) return GeometryError(ErrorValue.Value);
                width = (long)Math.Truncate(n);
            }
        }
        long r0 = area.Start.Row + rowOffset, c0 = area.Start.Col + colOffset;
        long r1 = r0 + height - 1, c1 = c0 + width - 1;
        if (height <= 0 || width <= 0 || r0 < 1 || c0 < 1 || r1 > CellAddress.MaxRow || c1 > CellAddress.MaxCol)
            return GeometryError(ErrorValue.Ref);
        return GeometrySuccess(new GridRange(new CellAddress(area.Start.Sheet, (uint)r0, (uint)c0),
            new CellAddress(area.Start.Sheet, (uint)r1, (uint)c1)));
    }

    private ReferenceGeometryResult ResolveIndexGeometry(
        FunctionCallNode node, IEvalContext context, HashSet<string> names, int depth, GeometryBudget budget)
    {
        if (node.Arguments.Count is < 2 or > 4) return GeometryError(ErrorValue.Value);
        var source = ResolveGeometryNode(node.Arguments[0], context, names, depth + 1, budget);
        if (source.Status != ReferenceGeometryStatus.Success) return source;
        long areaIndex = 1;
        if (node.Arguments.Count == 4 && node.Arguments[3] is not OmittedArgumentNode)
        {
            var areaError = GeometryInteger(node.Arguments[3], context, names, depth, budget, out areaIndex);
            if (areaError is not null) return areaError;
        }
        if (areaIndex < 1) return GeometryError(ErrorValue.Value);
        if (areaIndex > source.Areas.Count) return GeometryError(ErrorValue.Ref);
        var area = source.Areas[(int)areaIndex - 1];
        var error = GeometryInteger(node.Arguments[1], context, names, depth, budget, out long row);
        if (error is not null) return error;
        long col = 0;
        if (node.Arguments.Count >= 3 && node.Arguments[2] is not OmittedArgumentNode)
        {
            error = GeometryInteger(node.Arguments[2], context, names, depth, budget, out col);
            if (error is not null) return error;
        }
        else if (node.Arguments.Count == 2)
        {
            if (area.RowCount == 1) { col = row; row = 1; }
            else if (area.ColCount == 1) col = 1;
        }
        if (row < 0 || col < 0) return GeometryError(ErrorValue.Value);
        if (row > area.RowCount || col > area.ColCount) return GeometryError(ErrorValue.Ref);
        uint r0 = row == 0 ? area.Start.Row : area.Start.Row + (uint)row - 1;
        uint r1 = row == 0 ? area.End.Row : r0;
        uint c0 = col == 0 ? area.Start.Col : area.Start.Col + (uint)col - 1;
        uint c1 = col == 0 ? area.End.Col : c0;
        return GeometrySuccess(new GridRange(new CellAddress(area.Start.Sheet, r0, c0),
            new CellAddress(area.Start.Sheet, r1, c1)));
    }
}
