using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    /// <summary>
    /// Per-thread set of named formulas currently being evaluated.
    /// Used to detect and break circular name→name dependency chains.
    /// </summary>
    [ThreadStatic]
    private static HashSet<string>? _namedFormulaVisiting;

    private static ScalarValue EvaluateNamedRange(NamedRangeNode node, IEvalContext context)
    {
        // Local LET/LAMBDA bindings shadow workbook named ranges.
        var binding = context.TryResolveLambdaBinding(node.Name);
        if (binding is not null) return binding;

        // Excel scope precedence: a name scoped to the current sheet always wins over a
        // same-named workbook-global name, regardless of whether either name is a plain
        // range or a formula expression (§18.2.6). So a sheet-scoped named FORMULA must
        // take priority over a workbook-global named RANGE, not just over a workbook-global
        // named formula. Resolve sheet-scoped candidates (either kind) before falling back
        // to the workbook-global tier.
        if (IsSheetScopedName(node.Name, context, out var sheetScopedIsFormula))
        {
            if (sheetScopedIsFormula)
            {
                return TryEvaluateNamedFormula(node.Name, context, out var scopedFormulaValue)
                    ? scopedFormulaValue
                    : ErrorValue.Name;
            }

            var scopedRange = context.TryResolveNamedRange(node.Name);
            if (scopedRange is not null)
            {
                // Bare named range reference outside a function: return top-left cell value.
                // For 2D named ranges this is intentionally lossy — full implicit-intersection
                // semantics (Excel 365 spill behaviour) are a Phase 5 enhancement.
                return BuildRangeValueOrError(scopedRange.Value, context);
            }
        }

        var range = context.TryResolveNamedRange(node.Name);
        if (range is not null)
        {
            // Bare named range reference outside a function: return top-left cell value.
            // For 2D named ranges this is intentionally lossy — full implicit-intersection
            // semantics (Excel 365 spill behaviour) are a Phase 5 enhancement.
            return BuildRangeValueOrError(range.Value, context);
        }

        // Not a plain range — check whether it's a formula-expression named definition.
        return TryEvaluateNamedFormula(node.Name, context, out var formulaValue)
            ? formulaValue
            : ErrorValue.Name;
    }

    /// <summary>
    /// Determines whether <paramref name="name"/> has an explicit sheet-scoped definition
    /// (range or formula kind) on the context's current sheet, which must take precedence
    /// over any workbook-global name of either kind. Excel's scope resolution is per-name,
    /// not per-kind: a sheet-scoped formula named "Foo" outranks a workbook-global range
    /// named "Foo" on that sheet, even though a naive range-then-formula fallback would
    /// resolve the global range first.
    /// </summary>
    private static bool IsSheetScopedName(string name, IEvalContext context, out bool isFormula)
    {
        isFormula = false;
        var workbook = context.CurrentWorkbook;
        var sheet = context.CurrentSheet;
        if (workbook is null || sheet is null) return false;

        if (workbook.ScopedNamedFormulas.ContainsKey((name, sheet.Id)))
        {
            isFormula = true;
            return true;
        }

        return workbook.ScopedNamedRanges.ContainsKey((name, sheet.Id));
    }

    /// <summary>
    /// Evaluate a name that is bound to a formula expression rather than a plain cell range.
    /// Handles name→name dependencies and guards against cycles (returns #REF! on cycle).
    /// Returns the scalar result, which may itself be a RangeValue for array-valued names.
    /// </summary>
    private static bool TryEvaluateNamedFormula(string name, IEvalContext context, out ScalarValue result)
    {
        result = ErrorValue.Name;
        var formulaText = context.TryGetNamedFormulaText(name);
        if (formulaText is null)
            return false;

        // Cycle detection: if we're already evaluating this name (directly or transitively),
        // return #REF! to match Excel's circular-reference behaviour.
        var visiting = _namedFormulaVisiting ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!visiting.Add(name))
        {
            result = ErrorValue.Ref;
            return true;
        }

        try
        {
            var ast = GetOrParseFormula(formulaText);
            ast = ApplyRelativeNameAnchor(ast, context);
            result = EvaluateNamedFormulaAst(ast, context);
            return true;
        }
        catch (FormulaEvalException ex)
        {
            result = ErrorFromCode(ex.ErrorCode);
            return true;
        }
        catch (FormulaParseException)
        {
            result = ErrorValue.Value;
            return true;
        }
        finally
        {
            visiting.Remove(name);
        }
    }

    /// <summary>
    /// INDIRECT("Foo") support for a name whose RefersTo is a formula/dynamic expression rather
    /// than a plain named range (e.g. a dynamic named range built with OFFSET/COUNTA). Evaluates
    /// the named formula and, when it resolves to a reference (a <see cref="RangeValue"/>),
    /// exposes its bounds/sheet so BuiltInFunctions.Lookup.Indirect.cs's
    /// TryResolveIndirectRangeReference can materialize it exactly like a plain named range —
    /// previously that method's only named-name lookup was <c>ctx.TryResolveNamedRange</c>, which
    /// never consults formula-backed names at all, so INDIRECT("Foo") returned #REF! for any
    /// dynamic named range. Returns false (with <paramref name="error"/> left null) when
    /// <paramref name="name"/> isn't a formula-backed name at all, or when it is but evaluates to
    /// a plain scalar rather than a reference (matching Excel: INDIRECT needs an actual
    /// reference). Returns false with <paramref name="error"/> set when the named formula itself
    /// evaluates to an error (e.g. #REF! from a circular name chain), so the caller can propagate
    /// that error instead of falling through to a plain-text reference parse.
    /// </summary>
    internal static bool TryResolveIndirectNamedFormula(
        string name,
        IEvalContext context,
        out RangeValue range,
        out ScalarValue? error)
    {
        range = null!;
        error = null;

        if (!TryEvaluateNamedFormula(name, context, out var result))
            return false;

        if (result is RangeValue rangeValue)
        {
            range = rangeValue;
            return true;
        }

        if (result is ErrorValue namedFormulaError)
            error = namedFormulaError;

        return false;
    }

    /// <summary>
    /// Re-anchors the relative (non-$) references of a named formula's parsed AST to the cell
    /// that is actually using the name, matching Excel's per-cell relative-name evaluation: a
    /// name's RefersTo text is authored/stored with no persisted anchor cell (FreeX keeps only
    /// the raw formula text — see NamedFormulaTests / Workbook.NamedFormulas), so its implicit
    /// anchor is taken to be A1 of the using cell's sheet (Excel's own convention for a defined
    /// name's relative references), and the AST is shifted by the delta between that anchor and
    /// the current using cell. Absolute ($) references are left untouched by the underlying
    /// <see cref="ShiftFormulaForCell"/>. When there is no current-cell context (e.g. a
    /// convenience <c>Evaluate(formulaText, sheet, workbook)</c> call with no explicit
    /// <c>currentCell</c>), the AST is returned unshifted so that literal, cell-context-free
    /// evaluation keeps working exactly as before.
    /// </summary>
    /// <remarks>
    /// Narrow safety guard: FreeX's dependency graph for named formulas is built by
    /// RebuildFormulaDependencies from the LITERAL (unshifted, A1-anchored) RefersTo text, not
    /// from any per-using-cell shifted form — the graph has no notion of "this using cell now
    /// depends on itself because of the shift". If shifting here would manufacture a reference
    /// to the very cell currently being evaluated (a dependency edge the graph was never told
    /// about), applying the shift would silently read a stale self-value instead of raising a
    /// proper circular-reference error, which is worse than the bug being fixed. Making named-
    /// formula dependency tracking itself shift-aware is a broader change outside this method's
    /// file scope, so — until that lands — this falls back to the literal (unshifted) form only
    /// for that specific self-reference case, leaving every other relative-shift scenario fixed.
    /// </remarks>
    private static FormulaNode ApplyRelativeNameAnchor(FormulaNode ast, IEvalContext context)
    {
        if (context.CurrentCellAddress is not { } current)
            return ast;

        var anchor = new FreeX.Core.Model.CellAddress(current.Sheet, 1, 1);
        var shifted = ShiftFormulaForCell(ast, anchor, current);
        if (ReferenceEquals(shifted, ast))
            return ast;

        return ReferencesCell(shifted, current) ? ast : shifted;
    }

    // Best-effort structural check for whether `node` contains an unqualified (implicit-sheet)
    // cell/range reference that covers `current` — see ApplyRelativeNameAnchor's self-reference
    // guard. Only the node kinds that ShiftAst actually rewrites are inspected; this is
    // intentionally narrow (not a full reference-tracking pass) to match the guard's limited
    // purpose.
    private static bool ReferencesCell(FormulaNode node, FreeX.Core.Model.CellAddress current) => node switch
    {
        CellRefNode cr when cr.SheetName is null => cr.Row == current.Row && cr.ColumnNumber == current.Col,
        RangeRefNode rr when rr.SheetName is null =>
            current.Row >= Math.Min(rr.Start.Row, rr.End.Row) && current.Row <= Math.Max(rr.Start.Row, rr.End.Row) &&
            current.Col >= Math.Min(rr.Start.ColumnNumber, rr.End.ColumnNumber) && current.Col <= Math.Max(rr.Start.ColumnNumber, rr.End.ColumnNumber),
        FullColumnRangeRefNode fcr when fcr.SheetName is null =>
            current.Col >= Math.Min(fcr.StartColumnNumber, fcr.EndColumnNumber) && current.Col <= Math.Max(fcr.StartColumnNumber, fcr.EndColumnNumber),
        FullRowRangeRefNode frr when frr.SheetName is null =>
            current.Row >= Math.Min(frr.StartRow, frr.EndRow) && current.Row <= Math.Max(frr.StartRow, frr.EndRow),
        BinaryOpNode bin => ReferencesCell(bin.Left, current) || ReferencesCell(bin.Right, current),
        UnaryOpNode un => ReferencesCell(un.Operand, current),
        FunctionCallNode fn => ReferencesCellInAny(fn.Arguments, current),
        _ => false
    };

    private static bool ReferencesCellInAny(IReadOnlyList<FormulaNode> nodes, FreeX.Core.Model.CellAddress current)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (ReferencesCell(nodes[i], current))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Evaluate an AST node in "array-aware" mode for named formula expansion.
    /// Range-ref nodes are materialized into RangeValues (not collapsed to their top-left cell)
    /// so that array-valued named formulas (e.g. FortyTwoDays = COLUMN($A:$G)*ROW($1:$4))
    /// work correctly when passed to aggregate functions.
    /// </summary>
    private static ScalarValue EvaluateNamedFormulaAst(FormulaNode ast, IEvalContext context)
    {
        // FormulaEvaluator instances are lightweight (just a parse-cache slot).
        var evaluator = new FormulaEvaluator();
        return evaluator.EvaluateArrayOperand(ast, context);
    }

    private static ScalarValue EvaluateRange(RangeRefNode range, IEvalContext context)
    {
        // A 3-D sheet-span reference (Sheet1:Sheet3!A1) is only meaningful as an argument to the
        // aggregate functions that expand it across every spanned sheet (see
        // FormulaEvaluator.Functions.cs's TryExpandSheetSpanAggregateRange). Anywhere else —
        // a bare reference, an arithmetic operand, a non-aggregate function argument — Excel
        // evaluates it to #VALUE!, so surface that directly rather than silently collapsing to
        // just the start sheet's cell.
        if (range.EndSheetName is not null)
            return ErrorValue.Value;

        // A bare range reference outside a function context implicitly intersects with the
        // formula's own row/column (Excel's legacy @ behaviour), e.g. C5 = "=A1:A10" reads A5,
        // not A1 — not the range's top-left cell. This mirrors ResolveImplicitIntersection, the
        // same-project helper backing the explicit @ operator (FormulaEvaluator.Operators.cs), so
        // a bare multi-cell range and an explicit @range agree on which cell they read.
        // Excel normalizes a reversed range (e.g. B5:A1) to its top-left corner (A1:B5)
        // before reading it, so pick the min/max row/col rather than trusting range.Start/End literally.
        uint topRow = Math.Min(range.Start.Row, range.End.Row);
        uint bottomRow = Math.Max(range.Start.Row, range.End.Row);
        uint leftColumn = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        uint rightColumn = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);

        if (context.CurrentCellAddress is { } current && (bottomRow > topRow || rightColumn > leftColumn))
        {
            // Row-vector (single row) ranges intersect purely on column, column-vector (single
            // column) ranges intersect purely on row, and a genuine 2-D rectangle requires both
            // axes to match — otherwise the formula cell is off-axis and Excel returns #VALUE!.
            bool rowInBounds = current.Row >= topRow && current.Row <= bottomRow;
            bool colInBounds = current.Col >= leftColumn && current.Col <= rightColumn;
            uint? targetRow = bottomRow == topRow ? topRow : rowInBounds ? current.Row : null;
            uint? targetColumn = rightColumn == leftColumn ? leftColumn : colInBounds ? current.Col : null;

            if (targetRow is not { } resolvedRow || targetColumn is not { } resolvedColumn)
                return ErrorValue.Value;

            return range.SheetName is not null
                ? context.GetCellValue(range.SheetName, resolvedRow, resolvedColumn)
                : context.GetCellValue(resolvedRow, resolvedColumn);
        }

        // No current-cell context (e.g. a direct Evaluate(text, sheet) call with no currentCell)
        // or the range is already a single cell: fall back to the historical top-left reading.
        return range.SheetName is not null
            ? context.GetCellValue(range.SheetName, topRow, leftColumn)
            : context.GetCellValue(topRow, leftColumn);
    }


    private ScalarValue EvaluateArrayOperand(FormulaNode node, IEvalContext context)
    {
        if (node is RangeRefNode range)
            return BuildRangeValueOrError(range, context);

        // A bare full-column/full-row reference (e.g. =A:A, =1:1) as the entire body of a dynamic-array
        // formula must spill the used extent of that column/row, matching Excel. Route through the same
        // ToRangeRef + BuildRangeValueOrError machinery used for finite ranges (which already clamps the
        // open end via ClampOpenEndedRangeToUsed) instead of collapsing to a single scalar.
        if (node is FullColumnRangeRefNode fullColumn)
            return BuildRangeValueOrError(ToRangeRef(fullColumn), context);

        if (node is FullRowRangeRefNode fullRow)
            return BuildRangeValueOrError(ToRangeRef(fullRow), context);

        if (node is NamedRangeNode named)
        {
            var binding = context.TryResolveLambdaBinding(named.Name);
            if (binding is not null)
                return binding;

            // Sheet-scoped names (either kind) win over a same-named workbook-global name —
            // see IsSheetScopedName / EvaluateNamedRange for the full Excel-scope-precedence rationale.
            if (IsSheetScopedName(named.Name, context, out var sheetScopedIsFormula))
            {
                if (sheetScopedIsFormula)
                {
                    return TryEvaluateNamedFormula(named.Name, context, out var scopedFormulaValue)
                        ? scopedFormulaValue
                        : ErrorValue.Name;
                }

                var scopedRange = context.TryResolveNamedRange(named.Name);
                if (scopedRange is not null)
                    return BuildRangeValueOrError(scopedRange.Value, context);
            }

            var resolvedRange = context.TryResolveNamedRange(named.Name);
            if (resolvedRange is not null)
                return BuildRangeValueOrError(resolvedRange.Value, context);

            // Fall back to named formula evaluation (may return a RangeValue for array names).
            return TryEvaluateNamedFormula(named.Name, context, out var namedFormulaValue)
                ? namedFormulaValue
                : ErrorValue.Name;
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
        // A 3-D sheet-span reference (EndSheetName set) is only valid as a direct argument to the
        // aggregate functions that expand it across every spanned sheet (see
        // TryExpandSheetSpanAggregateRange in FormulaEvaluator.Functions.cs, which intercepts spans
        // before they ever reach this general-purpose single-sheet materializer). Every other
        // consumer of BuildRangeValue (INDEX, VLOOKUP, MMULT, structured functions, ISREF's 2-D
        // path, ...) reaches here for a span only when used outside an aggregate context, which is
        // exactly where Excel returns #VALUE!.
        if (range.EndSheetName is not null)
            throw new FormulaEvalException("#VALUE!", "3-D sheet-span reference used outside an aggregate function");

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
        // Materialized directly from a worksheet reference — its coordinates map to real cells, so
        // mark it so SUBTOTAL/AGGREGATE honour hidden-row / nested-aggregate exclusion (RangeValue.IsSheetReference).
        return new RangeValue(cells, r0, c0) { SheetName = range.SheetName, IsSheetReference = true };
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

        // Must construct a fresh CellRefNode via its constructor rather than `range.End with { ... }`.
        // CellRefNode.ColumnNumber is a property with a field initializer computed from ColumnName —
        // under a `with` expression the compiler-generated copy constructor copies that already-computed
        // backing field verbatim and does NOT re-run the initializer, so a `with` that changes ColumnName
        // (as the full-row clamp below does) would silently leave ColumnNumber stale at the old,
        // unclamped value (e.g. still 16384) even though ColumnName correctly shows the clamped letter.
        // Full-column clamping only changes Row (a plain copied field), which is why that case never
        // surfaced this bug — only changing ColumnName does.
        var end = new CellRefNode(
            FreeX.Core.Model.CellAddress.NumberToColumnName(endCol),
            endRow,
            range.End.IsColAbsolute,
            range.End.IsRowAbsolute,
            range.End.SheetName);
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

    /// <summary>
    /// Resolves a <see cref="NamedRangeNode"/> to a reference value honouring Excel's sheet-scope
    /// precedence (a name scoped to the current sheet — range or formula kind — always outranks a
    /// same-named workbook-global name; see <see cref="IsSheetScopedName"/>/<see cref="EvaluateNamedRange"/>).
    /// This is the reference-argument counterpart of that bare-name resolution: every call site that
    /// treats a name as a REFERENCE (OFFSET's base, CELL/FORMULATEXT/ISFORMULA/ISREF's argument, ANCHORARRAY)
    /// must apply the same precedence, not just <c>context.TryResolveNamedRange</c>, or a sheet-scoped
    /// named formula shadowing a workbook-global named range silently resolves to the wrong (global) range.
    /// Returns a <see cref="RangeValue"/> on success; an <see cref="ErrorValue"/> if the name doesn't
    /// resolve to a range/formula-that-evaluates-to-a-reference at all (#NAME?), or if a resolved scoped
    /// formula evaluates to a non-reference scalar (#VALUE!, matching Excel's treatment of a name whose
    /// formula body isn't itself a reference).
    /// </summary>
    private static ScalarValue ResolveNamedRangeNodeAsReference(NamedRangeNode node, IEvalContext context)
    {
        if (IsSheetScopedName(node.Name, context, out var sheetScopedIsFormula) && sheetScopedIsFormula)
        {
            if (!TryEvaluateNamedFormula(node.Name, context, out var formulaValue))
                return ErrorValue.Name;
            return formulaValue is RangeValue or ErrorValue ? formulaValue : ErrorValue.Value;
        }

        var range = context.TryResolveNamedRange(node.Name);
        if (range is not null)
            return BuildRangeValueOrError(range.Value, context);

        return TryEvaluateNamedFormula(node.Name, context, out var namedFormulaValue)
            ? namedFormulaValue is RangeValue or ErrorValue ? namedFormulaValue : ErrorValue.Value
            : ErrorValue.Name;
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
            // A 3-D sheet-span (EndSheetName set) is deliberately excluded here: every caller of
            // TryAsRangeRef is either a single-sheet "direct range" fast path (INDEX, MATCH, VLOOKUP,
            // NPV, ROWS/COLUMNS/AREAS, ...) or a structured-function argument builder, none of which
            // understand multi-sheet expansion. Returning false sends span arguments down the
            // generic per-argument loop in EvaluateFunction instead, where
            // TryExpandSheetSpanAggregateRange is the ONLY place that knows how to expand a span —
            // everywhere else a span reaching one of these call sites correctly ends up as #VALUE!
            // (matching Excel, which only accepts 3-D references inside aggregate functions).
            RangeRefNode { EndSheetName: not null } => null!,
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

        if (node.Arguments.Count is < 2 or > 4)
        {
            result = ErrorValue.Value;
            return true;
        }

        // The 4-argument reference form INDEX(ref, row, col, area_num) is area_num-aware and is
        // handled by the generic registry Index() implementation; defer to it rather than the
        // single-area fast path (which has no area_num slot).
        if (node.Arguments.Count == 4)
            return false;

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
            // A 3-D sheet-span (EndSheetName set) is still syntactically a reference for ISREF's
            // purposes — Excel accepts Sheet1:Sheet3!A1 here — so both the start and end sheet must
            // resolve, not just the start.
            RangeRefNode rng  => (rng.SheetName is null || context.SheetExists(rng.SheetName)) &&
                                  (rng.EndSheetName is null || context.SheetExists(rng.EndSheetName))
                                  ? TrueValue : FalseValue,
            FullColumnRangeRefNode col => col.SheetName is null || context.SheetExists(col.SheetName) ? TrueValue : FalseValue,
            FullRowRangeRefNode row => row.SheetName is null || context.SheetExists(row.SheetName) ? TrueValue : FalseValue,
            // Sheet-scope precedence: a sheet-scoped named FORMULA must shadow a same-named
            // workbook-global named RANGE here too (see ResolveNamedRangeNodeAsReference).
            // ISREF is true only when the name resolves to an actual reference — a scoped
            // formula that evaluates to a plain scalar is not a reference, matching Excel.
            NamedRangeNode nm => ResolveNamedRangeNodeAsReference(nm, context) is RangeValue ? TrueValue : FalseValue,
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

        // ISFORMULA officially supports a multi-cell reference argument and returns one result
        // per cell, spilling to match the reference's shape (e.g. ISFORMULA(A1:A3) in a
        // dynamic-array context returns a 1x3 array), rather than collapsing to the top-left cell.
        // Scoped to plain bounded ranges only (not full row/column, whose own top-left-collapse
        // behaviour is deliberate — see FormulaPredicates_UseTopLeftCellForFullRowAndColumnReferences
        // — and not 3-D sheet spans, which TryResolveReferenceTopLeftCell already rejects below).
        if (IsMultiCellBoundedRangeRef(node.Arguments[0], out var rangeRef))
            return BuildIsFormulaOrFormulaTextRangeValue(rangeRef, context,
                cell => cell?.HasFormula == true ? TrueValue : FalseValue);

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

        // Same multi-cell spill requirement as ISFORMULA above: FORMULATEXT(A1:A3) returns one
        // formula-text-or-#N/A result per cell, matching the reference's shape.
        if (IsMultiCellBoundedRangeRef(node.Arguments[0], out var rangeRef))
            return BuildIsFormulaOrFormulaTextRangeValue(rangeRef, context, FormulaTextCellValue);

        var error = TryResolveReferenceTopLeftCell(
            node.Arguments[0],
            context,
            unsupportedReferenceError: ErrorValue.NA,
            mapReferenceFunctionValueErrorToNA: true,
            out var cell);

        if (error is not null) return error;
        return FormulaTextCellValue(cell);
    }

    private static ScalarValue FormulaTextCellValue(Cell? cell)
    {
        if (cell is null || !cell.HasFormula) return ErrorValue.NA;
        var formulaText = cell.FormulaText!;
        return new TextValue(formulaText.StartsWith('=') ? formulaText : "=" + formulaText);
    }

    // True only for a plain, single-sheet, bounded RangeRefNode (e.g. A1:A3, B5:A1) that spans
    // more than one cell — deliberately excludes FullColumnRangeRefNode/FullRowRangeRefNode (whose
    // top-left-collapse behaviour for ISFORMULA/FORMULATEXT is pinned by
    // FormulaPredicates_UseTopLeftCellForFullRowAndColumnReferences) and 3-D sheet spans.
    private static bool IsMultiCellBoundedRangeRef(FormulaNode node, out RangeRefNode range)
    {
        range = null!;
        if (node is not RangeRefNode { EndSheetName: null } rr)
            return false;

        if (rr.Start.Row == rr.End.Row && rr.Start.ColumnNumber == rr.End.ColumnNumber)
            return false;

        range = rr;
        return true;
    }

    // Materializes a multi-cell reference argument to ISFORMULA/FORMULATEXT into a RangeValue,
    // applying cellValue per cell so the caller's function-level spilling machinery expands it
    // across the corresponding cells (see EvaluateSpilling's "functions already produce a
    // RangeValue when it yields an array" comment in FormulaEvaluator.cs).
    private static ScalarValue BuildIsFormulaOrFormulaTextRangeValue(
        RangeRefNode range,
        IEvalContext context,
        Func<Cell?, ScalarValue> cellValue)
    {
        if (range.SheetName is not null && !context.SheetExists(range.SheetName))
            return ErrorValue.Ref;

        range = ClampOpenEndedRangeToUsed(range, context);
        uint r0 = Math.Min(range.Start.Row, range.End.Row);
        uint r1 = Math.Max(range.Start.Row, range.End.Row);
        uint c0 = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        uint c1 = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);
        long rows = r1 - r0 + 1;
        long cols = c1 - c0 + 1;
        if (rows * cols > FormulaSafetyLimits.MaxMaterializedRangeCells)
            return ErrorValue.Ref;

        var cells = new ScalarValue[(int)rows, (int)cols];
        for (int ri = 0; ri < rows; ri++)
            for (int ci = 0; ci < cols; ci++)
            {
                var cell = range.SheetName is not null
                    ? context.TryGetCell(range.SheetName, r0 + (uint)ri, c0 + (uint)ci)
                    : context.TryGetCell(r0 + (uint)ri, c0 + (uint)ci);
                cells[ri, ci] = cellValue(cell);
            }

        return new RangeValue(cells, r0, c0) { SheetName = range.SheetName };
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
            // Sheet-scope precedence: a sheet-scoped named FORMULA must shadow a same-named
            // workbook-global named RANGE here too (see ResolveNamedRangeNodeAsReference), so
            // e.g. CELL/FORMULATEXT/ISFORMULA read the scoped formula's top-left cell, not the
            // global range's.
            var reference = ResolveNamedRangeNodeAsReference(named, context);
            if (reference is ErrorValue namedError)
                return mapReferenceFunctionValueErrorToNA && namedError == ErrorValue.Value ? ErrorValue.NA : namedError;

            var namedRange = (RangeValue)reference;
            return TryGetCell(namedRange.SheetName, namedRange.StartRow, namedRange.StartCol, context, out cell);
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

    private static ErrorValue? TryGetTopLeftCell(RangeRefNode range, IEvalContext context, out Cell? cell)
    {
        // Excel normalizes a reversed range (e.g. B5:A1) to its top-left corner (A1:B5) before
        // reading it, exactly like BuildRangeValue and every OFFSET/INDEX/lookup fast-path in
        // this file. range.Start/End are stored exactly as parsed, so pick the min row/col here
        // rather than trusting range.Start literally.
        uint row = Math.Min(range.Start.Row, range.End.Row);
        uint column = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        return TryGetCell(range.SheetName, row, column, context, out cell);
    }

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
            // Sheet-scope precedence: a sheet-scoped named FORMULA must shadow a same-named
            // workbook-global named RANGE here too (see ResolveNamedRangeNodeAsReference), so
            // e.g. CELL("address", Foo) reports the scoped formula's reference, not the global
            // range's.
            return ResolveNamedRangeNodeAsReference(named, context);
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
                // OFFSET always returns a single-sheet reference; a 3-D span base (Sheet1:Sheet3!A1)
                // has no single well-defined sheet to offset from, so Excel disallows it here.
                if (rangeRef.EndSheetName is not null)
                    return ErrorValue.Value;
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
                // Clamp the open row extent to the sheet's used range first — same as the direct
                // A:A reference path (BuildRangeValue/ClampOpenEndedRangeToUsed) — so that
                // OFFSET(A:A,...) materializes the populated extent instead of the full
                // 1,048,576-row column, which would otherwise always exceed the materialization cap.
                var clampedColumnRange = ClampOpenEndedRangeToUsed(ToRangeRef(fullColumnRange), context);
                uint fc0 = Math.Min(clampedColumnRange.Start.ColumnNumber, clampedColumnRange.End.ColumnNumber);
                uint fc1 = Math.Max(clampedColumnRange.Start.ColumnNumber, clampedColumnRange.End.ColumnNumber);
                uint fcr0 = Math.Min(clampedColumnRange.Start.Row, clampedColumnRange.End.Row);
                uint fcr1 = Math.Max(clampedColumnRange.Start.Row, clampedColumnRange.End.Row);
                baseRow = fcr0; baseCol = fc0;
                baseHeight = (int)(fcr1 - fcr0 + 1);
                baseWidth = (int)(fc1 - fc0 + 1);
                baseSheet = fullColumnRange.SheetName;
                break;
            case FullRowRangeRefNode fullRowRange:
                if (fullRowRange.SheetName is not null && !context.SheetExists(fullRowRange.SheetName))
                    return ErrorValue.Ref;
                // Same used-range clamp as above, for the open column extent of a full-row base.
                var clampedRowRange = ClampOpenEndedRangeToUsed(ToRangeRef(fullRowRange), context);
                uint fr0 = Math.Min(clampedRowRange.Start.Row, clampedRowRange.End.Row);
                uint fr1 = Math.Max(clampedRowRange.Start.Row, clampedRowRange.End.Row);
                uint frc0 = Math.Min(clampedRowRange.Start.ColumnNumber, clampedRowRange.End.ColumnNumber);
                uint frc1 = Math.Max(clampedRowRange.Start.ColumnNumber, clampedRowRange.End.ColumnNumber);
                baseRow = fr0; baseCol = frc0;
                baseHeight = (int)(fr1 - fr0 + 1);
                baseWidth = (int)(frc1 - frc0 + 1);
                baseSheet = fullRowRange.SheetName;
                break;
            case NamedRangeNode nm:
                // Sheet-scope precedence: a sheet-scoped named FORMULA must shadow a same-named
                // workbook-global named RANGE here too, matching bare-name resolution (see
                // ResolveNamedRangeNodeAsReference / IsSheetScopedName).
                var namedReference = ResolveNamedRangeNodeAsReference(nm, context);
                if (namedReference is ErrorValue namedError) return namedError;
                var namedRange = (RangeValue)namedReference;
                baseRow = namedRange.StartRow; baseCol = namedRange.StartCol;
                baseHeight = namedRange.RowCount; baseWidth = namedRange.ColCount;
                baseSheet = namedRange.SheetName;
                break;
            case FunctionCallNode fn when fn.FunctionName is "OFFSET" or "INDIRECT":
                // The base argument may itself be a reference-returning function call, e.g.
                // OFFSET(INDIRECT("A1"),1,1) or OFFSET(OFFSET(A1,0,0),1,1) — both are valid in
                // Excel. Resolve the nested call to its RangeValue via the same path used
                // elsewhere for reference-returning arguments (EvaluateCellReferenceArgument,
                // EvaluateIsRef) and use its bounds as the OFFSET base.
                var nestedReference = EvaluateReferenceReturningFunction(fn, context);
                if (nestedReference is ErrorValue nestedError) return nestedError;
                if (nestedReference is not RangeValue nestedRange) return ErrorValue.Value;
                baseRow = nestedRange.StartRow; baseCol = nestedRange.StartCol;
                baseHeight = nestedRange.RowCount; baseWidth = nestedRange.ColCount;
                baseSheet = nestedRange.SheetName;
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
        if (height < 0 || width < 0) return ErrorValue.Ref;
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
        // OFFSET yields a genuine worksheet reference — its coordinates map to real cells, so mark it
        // so SUBTOTAL/AGGREGATE honour hidden-row / nested-aggregate exclusion (RangeValue.IsSheetReference).
        return new RangeValue(cells, (uint)r0Final, (uint)c0Final) { SheetName = baseSheet, IsSheetReference = true };
    }

}
