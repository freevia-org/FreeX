using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    /// <summary>
    /// Per-thread set of named formulas currently being evaluated.
    /// Used to detect and break circular name→name dependency chains.
    /// Keyed by (name, defining-scope) rather than bare name — see <see cref="NamedFormulaVisitingKey"/> —
    /// so two textually-distinct sheet-scoped formulas that happen to share a name (e.g.
    /// Sheet1!Foo and Sheet2!Foo) don't collide with each other when one references the other
    /// via an explicit sheet qualifier (R50-meta-2): only genuine re-entry into the SAME
    /// (name, scope) definition is treated as a cycle.
    /// </summary>
    [ThreadStatic]
    private static HashSet<string>? _namedFormulaVisiting;

    /// <summary>
    /// Builds the cycle-detection key for a named formula, combining its bare name (matched
    /// case-insensitively, like Excel names) with the id of the sheet it is actually DEFINED/
    /// scoped in — or no scope suffix at all for a workbook-global definition. Two different
    /// (name, scope) pairs never collide even when the bare names match, while every path that
    /// resolves to the exact same underlying definition (whether reached via an unqualified
    /// reference or an explicit sheet-qualified one) produces the identical key, so real
    /// circular chains through that single definition are still caught.
    /// </summary>
    /// <remarks>
    /// Internal (not private) so <c>FreeX.Core.Calc.RecalcEngine.CollectReferences</c> can key its own
    /// identical-purpose dependency-graph recursion guard the exact same way (R118-calc-named-formula-scope-key)
    /// instead of the bare defined-name text, which falsely collided two distinct same-named sheet-scoped
    /// formulas and silently dropped the dependency edge onto the inner one's precedent cells.
    /// </remarks>
    internal static string NamedFormulaVisitingKey(string name, FreeX.Core.Model.SheetId? scopeSheetId) =>
        scopeSheetId is { } id ? name + "\u0001" + id.Value.ToString("N") : name;

    /// <summary>
    /// True when <paramref name="name"/> has the shape of a linked-data-type field access — a
    /// cell reference immediately followed by ".Field" (e.g. "A1.PRICE" from <c>=A1.Price</c>).
    /// Excel reserves this dotted syntax for Rich Data Type field access; since FreeX doesn't
    /// model linked data types, such a reference must surface Excel's #FIELD! error rather than
    /// being misrouted through named-range lookup to #NAME? (R35-deferred-field-error-1). A
    /// plain defined name with no dot — or one whose text before the dot isn't itself a valid
    /// cell reference (e.g. "Rate", "Q1_2023") — is unaffected and still resolves normally.
    /// </summary>
    private static bool IsLinkedDataTypeFieldAccessShape(string name)
    {
        var dot = name.IndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
            return false;

        return Lexer.IsCellReference(name.AsSpan(0, dot));
    }

    private static ScalarValue EvaluateNamedRange(NamedRangeNode node, IEvalContext context)
    {
        if (IsLinkedDataTypeFieldAccessShape(node.Name))
            return ErrorValue.Field;

        // Local LET/LAMBDA bindings shadow workbook named ranges (and any explicit sheet
        // qualifier below, which can never apply to a purely local binding).
        var binding = context.TryResolveLambdaBinding(node.Name);
        if (binding is not null) return binding;

        // An explicit sheet qualifier (the "Sheet2" in "Sheet2!MyName") forces resolution
        // against THAT sheet's own scope — never the formula's own current sheet — falling
        // back to workbook-global scope exactly as Excel does. Returns false (leaving the
        // rest of this method's current-sheet-then-workbook resolution untouched) when the
        // name was written unqualified. See TryResolveSheetQualifiedName.
        if (TryResolveSheetQualifiedName(node, context, out var qualifiedResult))
            return qualifiedResult;

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
                // Bare named range reference outside a function: apply the same current-cell
                // implicit intersection EvaluateRange applies to a bare cell-range reference
                // (see EvaluateNamedRangeScalar below).
                return EvaluateNamedRangeScalar(scopedRange.Value, context);
            }
        }

        var range = context.TryResolveNamedRange(node.Name);
        if (range is not null)
        {
            // Bare named range reference outside a function: apply the same current-cell
            // implicit intersection EvaluateRange applies to a bare cell-range reference
            // (see EvaluateNamedRangeScalar below).
            return EvaluateNamedRangeScalar(range.Value, context);
        }

        // Not a plain range — check whether it's a formula-expression named definition.
        return TryEvaluateNamedFormula(node.Name, context, out var formulaValue)
            ? formulaValue
            : ErrorValue.Name;
    }

    /// <summary>
    /// Evaluates a bare named-range reference (e.g. a Data-Validation Formula1 of "=Flags", or a
    /// Conditional-Format rule of "=Flags") the same way <see cref="EvaluateRange"/> evaluates a
    /// bare cell-range reference: when there's a genuine current-cell context and the named
    /// range spans more than one cell, implicitly intersect with the formula's own row/column
    /// (Excel's legacy @ behaviour) instead of returning the full multi-cell RangeValue. Without
    /// this, callers that only understand a scalar result (DataValidationService.ValidateCore,
    /// ViewportService.ConditionalFormatFormulas — both call Evaluate directly, with no
    /// implicit-intersection post-processing) silently coerce the raw RangeValue to false for
    /// every row. A direct range ARGUMENT to a function (e.g. SUM(Flags)) never reaches this
    /// method — FormulaEvaluator.Functions.cs special-cases NamedRangeNode as a function argument
    /// and builds the full RangeValue directly — so full-range semantics there are unaffected.
    /// </summary>
    private static ScalarValue EvaluateNamedRangeScalar(FreeX.Core.Model.GridRange range, IEvalContext context)
    {
        if (context.CurrentCellAddress is { } current && (range.RowCount > 1 || range.ColCount > 1))
        {
            bool rowInBounds = current.Row >= range.Start.Row && current.Row <= range.End.Row;
            bool colInBounds = current.Col >= range.Start.Col && current.Col <= range.End.Col;
            uint? targetRow = range.RowCount == 1 ? range.Start.Row : rowInBounds ? current.Row : null;
            uint? targetColumn = range.ColCount == 1 ? range.Start.Col : colInBounds ? current.Col : null;

            if (targetRow is not { } resolvedRow || targetColumn is not { } resolvedColumn)
                return ErrorValue.Value;

            var sheetName = context.TryGetSheetName(range.Start.Sheet);
            return sheetName is not null
                ? context.GetCellValue(sheetName, resolvedRow, resolvedColumn)
                : context.GetCellValue(resolvedRow, resolvedColumn);
        }

        return BuildRangeValueOrError(range, context);
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
        var formulaText = context.TryGetNamedFormulaText(name);
        if (formulaText is null)
        {
            result = ErrorValue.Name;
            return false;
        }

        // context.TryGetNamedFormulaText resolves sheet-scoped-on-the-current-sheet first, then
        // falls back to the workbook-global definition (see Workbook.TryGetNamedFormulaText) — so
        // re-derive which tier it actually came from here (via the same IsSheetScopedName check
        // EvaluateNamedRange/EvaluateArrayOperand already used) so the cycle-detection key below
        // reflects the formula's real defining scope, not just the calling cell's sheet.
        FreeX.Core.Model.SheetId? scopeSheetId =
            IsSheetScopedName(name, context, out var isScopedFormula) && isScopedFormula
                ? context.CurrentSheet!.Id
                : null;

        result = EvaluateNamedFormulaText(name, formulaText, context, scopeSheetId);
        return true;
    }

    /// <summary>
    /// Shared cycle-detection + evaluation body for a named formula's raw RefersTo text, factored
    /// out of <see cref="TryEvaluateNamedFormula"/> so <see cref="TryResolveSheetQualifiedName"/>
    /// can evaluate a formula-kind name it resolved directly from the QUALIFIED sheet's scope
    /// (bypassing <c>context.TryGetNamedFormulaText</c>, which is always anchored to the eval
    /// context's own current sheet — see that method's summary) using the exact same
    /// cycle-guard/error-handling semantics as the ordinary unqualified path.
    /// </summary>
    private static ScalarValue EvaluateNamedFormulaText(
        string name,
        string formulaText,
        IEvalContext context,
        FreeX.Core.Model.SheetId? scopeSheetId)
    {
        // Cycle detection: if we're already evaluating this exact (name, defining-scope) pair
        // (directly or transitively), return #REF! to match Excel's circular-reference behaviour.
        // Keyed by scope (not bare name) so two distinct same-named scoped formulas — e.g.
        // Sheet1!Foo and Sheet2!Foo — don't falsely collide when one references the other via an
        // explicit sheet qualifier (R50-meta-2); see NamedFormulaVisitingKey.
        var visiting = _namedFormulaVisiting ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var key = NamedFormulaVisitingKey(name, scopeSheetId);
        if (!visiting.Add(key))
            return ErrorValue.Ref;

        try
        {
            var ast = GetOrParseFormula(formulaText);
            ast = ApplyRelativeNameAnchor(ast, context);
            return EvaluateNamedFormulaAst(ast, context);
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
        catch (FormulaParseException)
        {
            return ErrorValue.Value;
        }
        finally
        {
            visiting.Remove(key);
        }
    }

    /// <summary>
    /// Resolves a <see cref="NamedRangeNode"/> that carries an explicit
    /// <see cref="NamedRangeNode.SheetQualifier"/> (e.g. the "Sheet2" in "Sheet2!MyName") against
    /// THAT sheet's own defined-name scope, falling back to workbook-global scope when the
    /// qualified sheet has no local name of that text — exactly Excel's rule for a sheet-qualified
    /// name reference, and independent of the formula's own current sheet (which
    /// <see cref="IsSheetScopedName"/>/<see cref="EvaluateNamedRange"/> use for an UNqualified
    /// name). Mirrors the same scoped-formula &gt; scoped-range &gt; global-range &gt; global-formula
    /// precedence as the unqualified path, just anchored to the qualified sheet's id instead of
    /// <c>context.CurrentSheet.Id</c>.
    /// Returns <see langword="false"/> (result left at #NAME?, not meaningful) when
    /// <paramref name="node"/> has no <see cref="NamedRangeNode.SheetQualifier"/> at all — callers
    /// should fall through to ordinary current-sheet-then-workbook resolution in that case.
    /// Returns <see langword="true"/> with <paramref name="result"/> set to <c>#REF!</c> when the
    /// qualifying sheet name itself doesn't resolve to a real sheet (e.g. deleted after the
    /// formula was authored), matching Excel's #REF! for a reference to a nonexistent sheet.
    /// </summary>
    private static bool TryResolveSheetQualifiedName(NamedRangeNode node, IEvalContext context, out ScalarValue result)
    {
        result = ErrorValue.Name;
        if (node.SheetQualifier is not { } sheetQualifier)
            return false;

        var workbook = context.CurrentWorkbook;
        var qualifiedSheet = workbook?.GetSheet(sheetQualifier);
        if (workbook is null || qualifiedSheet is null)
        {
            // workbook.GetSheet only ever matches a real LOCAL sheet name -- a bracket-prefixed
            // external-workbook qualifier (e.g. "[1]Sheet1", the on-disk shape for
            // =[1]Sheet1!TaxRate) can never match it and always lands here. Route those through
            // the same external-link resolver the plain-cell path (SheetEvalContext.GetCellValue)
            // already consults instead of concluding outright that the qualifying sheet is gone --
            // only genuinely unresolvable qualifiers (real missing local sheet, or an external
            // link/sheet/name that doesn't exist) fall through to #REF!.
            if (workbook is not null &&
                TryResolveExternalSheetQualifiedDefinedName(workbook, sheetQualifier, node.Name, context, out var externalResult))
            {
                result = externalResult;
                return true;
            }

            result = ErrorValue.Ref;
            return true;
        }

        // Scoped-formula tier: a formula named-definition scoped to the qualified sheet always
        // outranks a same-named workbook-global range, matching the unqualified precedence rule.
        if (workbook.ScopedNamedFormulas.TryGetValue((node.Name, qualifiedSheet.Id), out var scopedFormulaText))
        {
            result = EvaluateNamedFormulaText(node.Name, scopedFormulaText, context, qualifiedSheet.Id);
            return true;
        }

        // Scoped-range tier, falling back to the workbook-global range: Workbook.TryGetNamedRange
        // already checks the (name, sheetId) scoped dictionary first and falls back to the global
        // NamedRanges dictionary, so this single call covers both "range scoped to the qualified
        // sheet" and "workbook-global range" (bare named range reference outside a function:
        // return top-left cell value — for 2D named ranges this is intentionally lossy; full
        // implicit-intersection semantics are a Phase 5 enhancement).
        if (workbook.TryGetNamedRange(node.Name, qualifiedSheet.Id, out var range))
        {
            result = BuildRangeValueOrError(range, context);
            return true;
        }

        // Global-formula tier: Workbook.TryGetNamedFormulaText falls back to the workbook-global
        // formula text the same way, so by this point (no scoped formula, no scoped/global range)
        // this can only find a workbook-global formula, if any.
        if (workbook.TryGetNamedFormulaText(node.Name, qualifiedSheet.Id) is { } formulaText)
        {
            // Reached only after the scoped-formula tier above found nothing for
            // qualifiedSheet.Id, so whatever this call resolves must be the workbook-global
            // definition — key the cycle guard on the global scope (null), not qualifiedSheet.Id,
            // so this matches the same global definition's key when reached unqualified too.
            result = EvaluateNamedFormulaText(node.Name, formulaText, context, scopeSheetId: null);
            return true;
        }

        result = ErrorValue.Name;
        return true;
    }

    /// <summary>
    /// Resolves a bracket-prefixed external-workbook sheet qualifier (e.g. the "[1]Sheet1" in
    /// <c>=[1]Sheet1!TaxRate</c>) against that external link's cached
    /// <see cref="FreeX.Core.Model.ExternalLinkModel.DefinedNames"/>, returning the cached value
    /// Excel captured at last refresh -- mirroring <see cref="ExternalSheetReferenceResolver.TryResolveExternalDefinedName"/>,
    /// which already handles the sheet-less <c>[n]!Name</c> shape, but reached from the
    /// sheet-qualified shape instead. Returns <see langword="false"/> when
    /// <paramref name="sheetQualifier"/> isn't a resolvable external sheet reference at all (not a
    /// bracketed external qualifier, an unknown link, or an unknown sheet within it) or the named
    /// external link doesn't define <paramref name="name"/> -- callers should fall back to #REF! in
    /// either case, exactly as a genuinely missing local sheet would.
    /// </summary>
    private static bool TryResolveExternalSheetQualifiedDefinedName(
        FreeX.Core.Model.Workbook workbook, string sheetQualifier, string name, IEvalContext context, out ScalarValue result)
    {
        result = ErrorValue.Ref;

        var resolved = ExternalSheetReferenceResolver.TryResolve(workbook, sheetQualifier);
        if (resolved is not { } external)
            return false;

        // TryResolve identifies the link/sheet by matching the bracketed qualifier text, but the
        // opaque "[n]!Name" shape TryResolveExternalDefinedName expects needs the link's own
        // 1-based position in workbook.ExternalLinks (the same "n" the on-disk/serialized
        // reference form addresses it by) -- re-derive it from the resolved link instance.
        var externalIndex = workbook.ExternalLinks.IndexOf(external.Link) + 1;
        if (externalIndex < 1)
            return false;

        var opaqueName = "[" + externalIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]!" + name;
        if (!ExternalSheetReferenceResolver.TryResolveExternalDefinedName(workbook, opaqueName, out var formulaText))
            return false;

        result = EvaluateNamedFormulaText(name, formulaText, context, scopeSheetId: null);
        return true;
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

        return FormulaReferenceContainment.ContainsUnqualifiedCell(shifted, current) ? ast : shifted;
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

    // ── Explicit INTERSECTION operator (a space between two reference operands) ────────────────
    // and NAME:endpoint ranges (a defined name used as one side of ':') ─────────────────────────

    /// <summary>
    /// Evaluates a bare <see cref="IntersectionNode"/> used as a scalar (outside any aggregate
    /// function argument position) -- resolves the overlap rectangle, then applies the same
    /// current-cell implicit intersection a bare multi-cell <see cref="RangeRefNode"/> would (via
    /// <see cref="EvaluateRange"/>), so <c>=A1:C3 B2:D4</c> behaves exactly like an equivalent
    /// plain range reference once the overlap itself is known.
    /// </summary>
    private static ScalarValue EvaluateIntersectionNode(IntersectionNode node, IEvalContext context)
    {
        TryResolveIntersectionRange(node, context, out var range, out var error);
        return error is { } err ? err : EvaluateRange(range, context);
    }

    /// <summary>
    /// Evaluates a <see cref="UnionNode"/> -- resolves each comma-separated area to a
    /// <see cref="RangeValue"/> (via <see cref="EvaluateArrayOperand"/>, which already knows every
    /// reference shape: plain ranges, full column/row, named ranges, intersections, ...) and
    /// collects them into a <see cref="UnionValue"/>. A nested <see cref="UnionNode"/> area (from a
    /// defined name whose own RefersTo text is itself a union) is flattened into the same flat
    /// area list rather than nested, matching how Excel treats <c>((A1,B1),C1)</c> as three areas,
    /// not two. Propagates the first error encountered (a missing sheet -> #REF!, an unresolved
    /// name -> #NAME?, a disjoint intersection -> #NULL!) and rejects any area that isn't
    /// reference-shaped at all (e.g. a bare number) with #VALUE!, matching Excel's refusal to
    /// accept a non-reference operand in a union.
    /// </summary>
    private ScalarValue EvaluateUnionNode(UnionNode node, IEvalContext context)
    {
        var areas = new List<RangeValue>(node.Areas.Count);
        foreach (var areaNode in node.Areas)
        {
            // A single-cell area (e.g. the "D5" in (A1:B2,D5)) is a CellRefNode, which
            // EvaluateArrayOperand's generic fallback evaluates to the cell's raw scalar value
            // (not a RangeValue) everywhere else it's used -- correct for a plain scalar operand,
            // but wrong here, where every area must become a RangeValue so AREAS/SUM can treat a
            // lone cell exactly like a 1x1 range (matching every other reference-shaped operand).
            var value = areaNode is CellRefNode cellArea
                ? BuildRangeValueOrError(new RangeRefNode(cellArea, cellArea, cellArea.SheetName), context)
                : EvaluateArrayOperand(areaNode, context);
            switch (value)
            {
                case ErrorValue error:
                    return error;
                case UnionValue nestedUnion:
                    areas.AddRange(nestedUnion.Areas);
                    break;
                case RangeValue rangeValue:
                    // BuildRangeValue silently reads blank cells from a nonexistent sheet rather
                    // than throwing (its callers -- the per-argument expansion loop in
                    // FormulaEvaluator.Functions.cs -- are the ones that check SheetExists and
                    // surface #REF!, e.g. for AREAS(Missing!A:A)). EvaluateUnionNode bypasses that
                    // loop entirely (it evaluates each area directly), so it must make the same
                    // check itself, or a union area on a deleted/renamed sheet would silently
                    // count as a valid area instead of invalidating the whole reference like Excel.
                    if (rangeValue.SheetName is not null && !context.SheetExists(rangeValue.SheetName))
                        return ErrorValue.Ref;
                    areas.Add(rangeValue);
                    break;
                default:
                    // Not reference-shaped (e.g. a literal number/text/bool area) -- Excel doesn't
                    // even parse that as a union operand; treat it the same way ARGS/scalar misuse
                    // of a non-reference is treated elsewhere in this engine: #VALUE!.
                    return ErrorValue.Value;
            }
        }

        return new UnionValue(areas);
    }

    /// <summary>
    /// Evaluates a bare <see cref="NamedRangeEndpointNode"/> used as a scalar -- resolves any
    /// NamedRangeNode endpoint to its defined range's top-left cell, forms the effective range,
    /// then applies the same current-cell implicit intersection a bare multi-cell
    /// <see cref="RangeRefNode"/> would (via <see cref="EvaluateRange"/>).
    /// </summary>
    private static ScalarValue EvaluateNamedRangeEndpointNode(NamedRangeEndpointNode node, IEvalContext context)
    {
        TryResolveNamedRangeEndpointRange(node, context, out var range, out var error);
        return error is { } err ? err : EvaluateRange(range, context);
    }

    /// <summary>
    /// Resolves an <see cref="IntersectionNode"/> to the <see cref="RangeRefNode"/> covering the
    /// overlap rectangle of its two operands. Always returns true (the out-parameters fully
    /// describe every outcome): <paramref name="error"/> is non-null when either operand can't be
    /// resolved to a reference at all (#VALUE!) or the operands don't overlap / live on different
    /// sheets (#NULL!, matching Excel's error for a genuinely disjoint intersection); otherwise
    /// <paramref name="range"/> holds the resolved overlap and <paramref name="error"/> is null.
    /// </summary>
    private static bool TryResolveIntersectionRange(
        IntersectionNode node, IEvalContext context, out RangeRefNode range, out ErrorValue? error)
    {
        range = null!;

        if (!TryResolveOperandRectangle(node.Left, context, out var leftSheet, out var lr0, out var lc0, out var lr1, out var lc1) ||
            !TryResolveOperandRectangle(node.Right, context, out var rightSheet, out var rr0, out var rc0, out var rr1, out var rc1))
        {
            error = ErrorValue.Value;
            return true;
        }

        if (!TryReconcileSheetNames(leftSheet, rightSheet, out var sheetName))
        {
            error = ErrorValue.Null;
            return true;
        }

        var r0 = Math.Max(lr0, rr0);
        var r1 = Math.Min(lr1, rr1);
        var c0 = Math.Max(lc0, rc0);
        var c1 = Math.Min(lc1, rc1);

        if (r0 > r1 || c0 > c1)
        {
            error = ErrorValue.Null;
            return true;
        }

        var start = new CellRefNode(FreeX.Core.Model.CellAddress.NumberToColumnName(c0), r0, SheetName: sheetName);
        var end = new CellRefNode(FreeX.Core.Model.CellAddress.NumberToColumnName(c1), r1, SheetName: sheetName);
        range = new RangeRefNode(start, end, sheetName);
        error = null;
        return true;
    }

    /// <summary>
    /// Resolves a <see cref="NamedRangeEndpointNode"/> to the effective <see cref="RangeRefNode"/>,
    /// resolving any <see cref="NamedRangeNode"/> endpoint to its defined range's top-left cell
    /// first (Excel always anchors on the name's corner). Always returns true; <paramref name="error"/>
    /// is #NAME? when either endpoint name doesn't resolve, matching Excel for an undefined name.
    /// </summary>
    private static bool TryResolveNamedRangeEndpointRange(
        NamedRangeEndpointNode node, IEvalContext context, out RangeRefNode range, out ErrorValue? error)
    {
        range = null!;

        if (!TryResolveEndpointCell(node.Start, context, out var startCell) ||
            !TryResolveEndpointCell(node.End, context, out var endCell))
        {
            error = ErrorValue.Name;
            return true;
        }

        error = null;
        var sheetName = startCell.SheetName ?? endCell.SheetName;
        range = new RangeRefNode(startCell, endCell, sheetName);
        return true;
    }

    /// <summary>Resolves one <see cref="NamedRangeEndpointNode"/> side to a concrete cell.</summary>
    private static bool TryResolveEndpointCell(FormulaNode node, IEvalContext context, out CellRefNode cell)
    {
        if (node is CellRefNode direct)
        {
            cell = direct;
            return true;
        }

        if (node is NamedRangeNode named)
        {
            var resolved = context.TryResolveNamedRange(named.Name);
            if (resolved is null)
            {
                cell = null!;
                return false;
            }

            var gridRange = resolved.Value;
            cell = new CellRefNode(
                FreeX.Core.Model.CellAddress.NumberToColumnName(gridRange.Start.Col),
                gridRange.Start.Row,
                SheetName: context.TryGetSheetName(gridRange.Start.Sheet));
            return true;
        }

        cell = null!;
        return false;
    }

    /// <summary>
    /// Resolves an intersection operand (either side of an <see cref="IntersectionNode"/>) to its
    /// rectangle: a bare cell, a plain bounded range, a full column/row, a defined name (resolved
    /// via <paramref name="context"/>), or -- for a nested chain like <c>A1:A5 A2:C2 B1:B9</c> --
    /// another <see cref="IntersectionNode"/>/<see cref="NamedRangeEndpointNode"/>.
    /// </summary>
    private static bool TryResolveOperandRectangle(
        FormulaNode node, IEvalContext context,
        out string? sheetName, out uint r0, out uint c0, out uint r1, out uint c1)
    {
        switch (node)
        {
            case CellRefNode cell:
                sheetName = cell.SheetName;
                r0 = r1 = cell.Row;
                c0 = c1 = cell.ColumnNumber;
                return true;

            case RangeRefNode { EndSheetName: null } rr:
                sheetName = rr.SheetName;
                r0 = Math.Min(rr.Start.Row, rr.End.Row);
                r1 = Math.Max(rr.Start.Row, rr.End.Row);
                c0 = Math.Min(rr.Start.ColumnNumber, rr.End.ColumnNumber);
                c1 = Math.Max(rr.Start.ColumnNumber, rr.End.ColumnNumber);
                return true;

            case FullColumnRangeRefNode fcr:
                sheetName = fcr.SheetName;
                r0 = 1;
                r1 = FreeX.Core.Model.CellAddress.MaxRow;
                c0 = Math.Min(fcr.StartColumnNumber, fcr.EndColumnNumber);
                c1 = Math.Max(fcr.StartColumnNumber, fcr.EndColumnNumber);
                return true;

            case FullRowRangeRefNode frr:
                sheetName = frr.SheetName;
                c0 = 1;
                c1 = FreeX.Core.Model.CellAddress.MaxCol;
                r0 = Math.Min(frr.StartRow, frr.EndRow);
                r1 = Math.Max(frr.StartRow, frr.EndRow);
                return true;

            case NamedRangeNode named:
            {
                var resolved = context.TryResolveNamedRange(named.Name);
                if (resolved is null)
                {
                    sheetName = null; r0 = c0 = r1 = c1 = 0;
                    return false;
                }

                var gridRange = resolved.Value;
                sheetName = context.TryGetSheetName(gridRange.Start.Sheet);
                r0 = gridRange.Start.Row;
                c0 = gridRange.Start.Col;
                r1 = gridRange.End.Row;
                c1 = gridRange.End.Col;
                return true;
            }

            case NamedRangeEndpointNode endpoint:
            {
                if (!TryResolveNamedRangeEndpointRange(endpoint, context, out var endpointRange, out var endpointError) ||
                    endpointError is not null)
                {
                    sheetName = null; r0 = c0 = r1 = c1 = 0;
                    return false;
                }

                sheetName = endpointRange.SheetName;
                r0 = Math.Min(endpointRange.Start.Row, endpointRange.End.Row);
                r1 = Math.Max(endpointRange.Start.Row, endpointRange.End.Row);
                c0 = Math.Min(endpointRange.Start.ColumnNumber, endpointRange.End.ColumnNumber);
                c1 = Math.Max(endpointRange.Start.ColumnNumber, endpointRange.End.ColumnNumber);
                return true;
            }

            case IntersectionNode nested:
            {
                if (!TryResolveIntersectionRange(nested, context, out var nestedRange, out var nestedError) ||
                    nestedError is not null)
                {
                    sheetName = null; r0 = c0 = r1 = c1 = 0;
                    return false;
                }

                sheetName = nestedRange.SheetName;
                r0 = Math.Min(nestedRange.Start.Row, nestedRange.End.Row);
                r1 = Math.Max(nestedRange.Start.Row, nestedRange.End.Row);
                c0 = Math.Min(nestedRange.Start.ColumnNumber, nestedRange.End.ColumnNumber);
                c1 = Math.Max(nestedRange.Start.ColumnNumber, nestedRange.End.ColumnNumber);
                return true;
            }

            default:
                sheetName = null; r0 = c0 = r1 = c1 = 0;
                return false;
        }
    }

    /// <summary>
    /// Reconciles the two operand sheet names of an intersection: a null side means "the formula's
    /// own sheet" (implicitly matching whatever the other side names), so only two *different*
    /// explicit sheet names are a genuine mismatch (-> no possible overlap).
    /// </summary>
    private static bool TryReconcileSheetNames(string? a, string? b, out string? sheetName)
    {
        if (a is null || b is null || string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            sheetName = a ?? b;
            return true;
        }

        sheetName = null;
        return false;
    }

    private ScalarValue EvaluateArrayOperand(FormulaNode node, IEvalContext context)
    {
        if (node is IntersectionNode intersection)
        {
            TryResolveIntersectionRange(intersection, context, out var intersectionRange, out var intersectionError);
            return intersectionError is { } intersectionErr
                ? intersectionErr
                : BuildRangeValueOrError(intersectionRange, context);
        }

        if (node is NamedRangeEndpointNode namedEndpoint)
        {
            TryResolveNamedRangeEndpointRange(namedEndpoint, context, out var endpointRange, out var endpointError);
            return endpointError is { } endpointErr
                ? endpointErr
                : BuildRangeValueOrError(endpointRange, context);
        }

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
            if (IsLinkedDataTypeFieldAccessShape(named.Name))
                return ErrorValue.Field;

            var binding = context.TryResolveLambdaBinding(named.Name);
            if (binding is not null)
                return binding;

            // An explicit sheet qualifier forces resolution against that sheet's own scope
            // (falling back to workbook-global) instead of the current-sheet-then-workbook
            // resolution below — see TryResolveSheetQualifiedName / EvaluateNamedRange.
            if (TryResolveSheetQualifiedName(named, context, out var qualifiedResult))
                return qualifiedResult;

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
        // Table1[@] (and the bare [@] shorthand, table-unqualified) is Excel's shorthand for "this
        // entire row" — exactly equivalent to the long-form Table1[#This Row] / [#This Row], spanning
        // every column of the table's data body at the formula's own row, not a single named column.
        // The parser hands this shape through with an empty ColumnName (see Parser's
        // StructuredCurrentRowReferenceNode construction in both the table-qualified and bare
        // StructuredReferenceSelector cases). Route it to the same "#This Row" resolution the long
        // form already uses via StructuredReferenceResolver.Resolve, instead of falling into the
        // single-column / column-range lookups below — those have no column name to search for and
        // always returned null (-> #NAME?) for an empty selector.
        if (string.IsNullOrWhiteSpace(node.ColumnName))
        {
            var wholeRow = StructuredReferenceResolver.Resolve(
                context.CurrentWorkbook,
                context.CurrentSheet,
                node.TableName ?? "",
                "#This Row",
                context.CurrentCellAddress);
            return wholeRow is null
                ? ErrorValue.Name
                : BuildRangeValueOrError(wholeRow.Value, context);
        }

        var address = StructuredReferenceResolver.ResolveCurrentRowColumn(
            context.CurrentWorkbook,
            context.CurrentSheet,
            context.CurrentCellAddress,
            node.TableName,
            node.ColumnName);
        if (address is not null)
            return context.GetCellValue(address.Value.Row, address.Value.Col);

        // ResolveCurrentRowColumn only ever matches a single literal column name; a '@' shorthand
        // column-RANGE (Table1[@[Q1]:[Q2]]) falls through here with ColumnName holding the literal
        // bracketed range text "[Q1]:[Q2]" instead, which no single column is ever named. Route that
        // shape to the range-aware resolver instead of failing outright, so it evaluates the same
        // current-row slice its long-form equivalent =SUM(Table1[[#This Row],[Q1]:[Q2]]) does.
        var range = StructuredReferenceResolver.ResolveCurrentRowColumnRange(
            context.CurrentWorkbook,
            context.CurrentSheet,
            context.CurrentCellAddress,
            node.TableName,
            node.ColumnName);
        return range is null
            ? ErrorValue.Name
            : BuildRangeValueOrError(range.Value, context);
    }


    /// <summary>
    /// Flattens every area of a <see cref="UnionValue"/> into one flat cell-value list, in area
    /// order, for feeding to <see cref="AddRangeValues"/> exactly like a plain
    /// <see cref="RangeValue"/>'s <c>Flatten()</c> result. Areas are concatenated without
    /// deduplication -- a cell present in two overlapping areas (e.g. <c>(A1:A2,A1:A2)</c>)
    /// therefore appears twice in the result, matching Excel's own double-counting of overlapping
    /// union areas.
    /// </summary>
    private static List<ScalarValue> FlattenUnionAreas(UnionValue union)
    {
        var flattened = new List<ScalarValue>();
        foreach (var area in union.Areas)
            flattened.AddRange(area.Flatten());
        return flattened;
    }

    /// <summary>
    /// Materializes a <see cref="UnionValue"/> argument into one synthetic Nx1
    /// <see cref="RangeValue"/> holding every area's cells concatenated in order (via
    /// <see cref="FlattenUnionAreas"/>, so overlapping areas double-count exactly like the
    /// aggregate-function union unwrap above), for functions in
    /// <c>UnionMaterializableRangeFunctions</c> whose own "args[i] is RangeValue r : wrap-as-1x1"
    /// fallback would otherwise misread the whole UnionValue as one opaque scalar cell (see
    /// R94-formula-union-selection-range in FormulaEvaluator.FunctionClassification.cs). These
    /// functions only ever flatten their range argument's cells, never index into 2-D shape, so
    /// collapsing every area into a single column is a safe, sufficient representation.
    /// </summary>
    private static RangeValue MaterializeUnionRangeValue(UnionValue union)
    {
        var flat = FlattenUnionAreas(union);
        var cells = new ScalarValue[flat.Count, 1];
        for (var i = 0; i < flat.Count; i++)
            cells[i, 0] = flat[i];
        return new RangeValue(cells);
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
        //
        // R129-formula-sumproduct-mmult-sparse-allocation-1 (MEASURED, not assumed — see
        // R129_SumproductMmultFullColumnAllocationBoundTests): ClampOpenEndedRangeToUsed clamps the
        // open end to the SHEET-WIDE used-range bounding box (Sheet.GetUsedRange), not to the
        // specific referenced column/row's own populated extent. Before this method existed, an
        // open-ended reference like `=SUMPRODUCT(A:A,B:B)` on a sheet whose used range reaches far
        // down a wholly UNRELATED column (e.g. one stray value at Z900000) would materialize
        // 1,048,576 rows and hit the materialization cap -> #REF! (a crude but effective memory
        // guard). Post-clamp it now succeeds by allocating a dense array sized to the sheet's used
        // row count instead (measured: ~29MB / ~146ms total for SUMPRODUCT, ~14MB / ~57ms for MMULT,
        // vs ~1.5KB / <0.02ms when the sheet's used range genuinely matches the 10 populated rows in
        // A/B) -- real but BOUNDED cost, not a leak or an unbounded blowup: a single full-column
        // reference can never exceed CellAddress.MaxRow (1,048,576) rows regardless of how far the
        // stray data is, and FormulaSafetyLimits.MaxMaterializedRangeCells (16,777,216, ~134MB
        // worst-case per array) already exists specifically to bound the analogous explicit-range
        // case (e.g. `=SUMPRODUCT(A1:P1048576,Q1:AD1048576)`) -- this is the SAME designed ceiling,
        // just newly reachable via the A:A/1:1 shorthand as an unintended side effect of removing
        // the #REF!.
        //
        // Per-argument sparse clamping (intersecting THIS range independently with its own column's
        // used extent, mirroring the LARGE/SMALL/PERCENTILE/MEDIAN/AGGREGATE(13) "bag of numbers"
        // fix in FormulaEvaluator.SelectionFastPaths.cs) is NOT safe here and was deliberately
        // rejected: BuildRangeValue's dense 2-D array is consumed positionally/dimensionally by
        // every caller (INDEX, VLOOKUP/HLOOKUP/MATCH/XLOOKUP fallback paths, MMULT, structured-table
        // refs, OFFSET, ISFORMULA/FORMULATEXT's multi-cell path, INDIRECT, ISREF's 2-D path — see
        // FormulaSafetyLimits.cs's own enumeration), so there is no "shape-agnostic" subset left to
        // carve out the way the direct-selection fast path did. SUMPRODUCT itself flattens its
        // operands but still requires each argument's materialized shape to MATCH its siblings'; two
        // full-column references (A:A, B:B) always clamp to the identical sheet-wide row count today
        // specifically BECAUSE the clamp is sheet-wide rather than per-column — independently
        // intersecting A:A with column A's own extent and B:B with column B's own extent would make
        // their row counts diverge whenever the two columns' own data happens to end at different
        // rows, silently turning a legitimately-computable `=SUMPRODUCT(A:A,B:B)` into #VALUE!. A
        // correct fix would need to clamp every sibling argument in the SAME call to the union of
        // just the specific columns/rows that call actually references (not the whole sheet, but
        // also not each argument alone) -- that requires BuildRangeValue to know about its sibling
        // arguments, a real architectural change (equivalent to option (a), a sparse/lazy RangeValue
        // that preserves declared dimensions while materializing only populated cells) left as a
        // dedicated follow-up rather than attempted piecemeal here.
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
        if (IsLinkedDataTypeFieldAccessShape(node.Name))
            return ErrorValue.Field;

        // An explicit sheet qualifier forces resolution against that sheet's own scope (falling
        // back to workbook-global) instead of the current-sheet-then-workbook resolution below —
        // see TryResolveSheetQualifiedName / EvaluateNamedRange.
        if (TryResolveSheetQualifiedName(node, context, out var qualifiedResult))
            return qualifiedResult is RangeValue or ErrorValue ? qualifiedResult : ErrorValue.Value;

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

    /// <summary>
    /// Context-aware superset of <see cref="TryAsRangeRef"/> used by the aggregate/structured
    /// argument-expansion loop in FormulaEvaluator.Functions.cs. In addition to every plain
    /// reference shape <see cref="TryAsRangeRef"/> already understands, this also resolves an
    /// <see cref="IntersectionNode"/> (the space operator -- the overlapping rectangle of both
    /// operands, or #NULL! when they don't overlap) and a <see cref="NamedRangeEndpointNode"/> (a
    /// defined NAME used as one endpoint of ':' -- resolved to its top-left cell). Both need
    /// <paramref name="context"/> for name lookups, unlike the purely syntactic
    /// <see cref="TryAsRangeRef"/>, which is why they live here rather than there -- the many
    /// context-free "direct range" fast paths (INDEX/MATCH/VLOOKUP/ROWS/COLUMNS/...) keep calling
    /// <see cref="TryAsRangeRef"/> directly and simply treat these two shapes as "not a direct
    /// range", falling back to the slower general per-argument path, which calls this instead.
    /// Returns false when <paramref name="node"/> isn't any of these reference shapes at all (the
    /// caller's existing fallback handling applies, unchanged). Returns true with
    /// <paramref name="error"/> non-null when it IS one of these shapes but couldn't be resolved to
    /// a usable range (disjoint intersection -> #NULL!; an unresolvable name endpoint -> #NAME?;
    /// an operand that isn't itself reference-shaped -> #VALUE!) -- the caller should short-circuit
    /// to that error rather than reading <paramref name="range"/>.
    /// </summary>
    private static bool TryResolveReferenceRange(
        FormulaNode node, IEvalContext context, out RangeRefNode range, out ErrorValue? error)
    {
        error = null;

        if (TryAsRangeRef(node, out range))
            return true;

        if (node is IntersectionNode intersection)
            return TryResolveIntersectionRange(intersection, context, out range, out error);

        if (node is NamedRangeEndpointNode endpoint)
            return TryResolveNamedRangeEndpointRange(endpoint, context, out range, out error);

        range = null!;
        return false;
    }

    private static bool TryEvaluateReferenceDimensionFunction(
        string functionName,
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count != 1 || functionName is not ("ROWS" or "COLUMNS" or "AREAS" or "ROW" or "COLUMN"))
            return false;

        if (!TryAsRangeRef(node.Arguments[0], out var range))
        {
            // R74-formula-reference-fns-4-2: ROW/COLUMN additionally accept a NamedRangeNode
            // argument (e.g. =ROW(AllA) where AllA = Sheet1!$A:$A) -- TryAsRangeRef only knows the
            // syntactic RangeRefNode/FullColumnRangeRefNode/FullRowRangeRefNode shapes a literal
            // token can be, so a name never reaches it and would otherwise fall through to the
            // slow general per-argument path, which clamps the open end to the sheet's used range
            // (unlike the direct =ROW(A:A) form this fast path handles). ROWS/COLUMNS/AREAS are
            // deliberately excluded -- clamping is harmless for those (they only count cells) and
            // this fast path must not change their existing clamped behavior for a named range.
            if (functionName is not ("ROW" or "COLUMN") ||
                node.Arguments[0] is not NamedRangeNode named ||
                !TryResolveNamedRangeNodeToRawRangeRef(named, context, out range))
                return false;
        }

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

        // ROW/COLUMN report POSITIONAL row/column numbers, unlike every other structured-range
        // function here (ROWS/COLUMNS/AREAS/aggregates), which only ever count or fold values —
        // for those, silently clamping an open-ended full-column/full-row reference down to the
        // sheet's used range is harmless (blank cells beyond it don't change a count or a sum).
        // For ROW/COLUMN it is NOT harmless: ROW(A:A) must evaluate to the literal array
        // {1;2;...;1048576}, so INDEX(ROW(A:A),100) resolves to 100. Deliberately bypass the
        // generic per-argument ClampOpenEndedRangeToUsed path below (and its cell-value
        // materialization + MaxMaterializedRangeCells cap, neither of which apply here since we
        // only need the range's own coordinates, not any cell's contents) and compute directly
        // from the RAW, unclamped extent instead.
        if (functionName is "ROW" or "COLUMN")
        {
            result = functionName == "ROW"
                ? BuildPositionalNumbers(r0, r1, c0, range.SheetName, isRow: true)
                : BuildPositionalNumbers(c0, c1, r0, range.SheetName, isRow: false);
            return true;
        }

        result = functionName == "ROWS"
            ? new NumberValue(r1 - r0 + 1)
            : new NumberValue(c1 - c0 + 1);
        return true;
    }

    // R74-formula-reference-fns-4-2: resolves a NamedRangeNode to its RAW (unclamped) underlying
    // range for the ROW/COLUMN fast path above, mirroring context.TryResolveNamedRange's own
    // scoped-then-global precedence (the same lookup EvaluateNamedRange uses for a plain-range-kind
    // name) rather than duplicating it. GridRange already stores the unclamped MaxRow/MaxCol
    // sentinel for a full-column/full-row named range, so no separate "raw" representation is
    // needed -- only the CellRefNode/RangeRefNode wrapping the caller's fast path already expects.
    // Deliberately narrower than full name resolution: a sheet-scoped LAMBDA/LET binding or a
    // formula-kind name (dynamic OFFSET-based range, etc.) has no raw GridRange to hand back, so
    // both are left to return false here and fall through to the slower general per-argument path,
    // which still evaluates them correctly (just with the pre-existing clamped behavior).
    private static bool TryResolveNamedRangeNodeToRawRangeRef(NamedRangeNode node, IEvalContext context, out RangeRefNode range)
    {
        range = null!;
        if (context.TryResolveLambdaBinding(node.Name) is not null)
            return false;

        FreeX.Core.Model.GridRange? gridRange;
        if (node.SheetQualifier is { } sheetQualifier)
        {
            var workbook = context.CurrentWorkbook;
            var qualifiedSheet = workbook?.GetSheet(sheetQualifier);
            if (workbook is null || qualifiedSheet is null || !workbook.TryGetNamedRange(node.Name, qualifiedSheet.Id, out var qualifiedRange))
                return false;

            gridRange = qualifiedRange;
        }
        else
        {
            if (IsSheetScopedName(node.Name, context, out var isFormula) && isFormula)
                return false;

            gridRange = context.TryResolveNamedRange(node.Name);
        }

        if (gridRange is not { } resolved)
            return false;

        var sheetName = context.TryGetSheetName(resolved.Start.Sheet);
        var start = new CellRefNode(FreeX.Core.Model.CellAddress.NumberToColumnName(resolved.Start.Col), resolved.Start.Row, SheetName: sheetName);
        var end = new CellRefNode(FreeX.Core.Model.CellAddress.NumberToColumnName(resolved.End.Col), resolved.End.Row, SheetName: sheetName);
        range = new RangeRefNode(start, end, sheetName);
        return true;
    }

    // Builds the positional-number result for ROW/COLUMN over a (possibly full-column/full-row)
    // range: a single NumberValue when the range collapses to one row/column, otherwise a
    // RangeValue array of consecutive position numbers, matching Excel's array-form ROW/COLUMN.
    private static ScalarValue BuildPositionalNumbers(uint from, uint to, uint otherAxisStart, string? sheetName, bool isRow)
    {
        if (from == to)
            return new NumberValue(from);

        int count = (int)(to - from) + 1;
        var cells = isRow ? new ScalarValue[count, 1] : new ScalarValue[1, count];
        for (int i = 0; i < count; i++)
        {
            var value = new NumberValue(from + (uint)i);
            if (isRow) cells[i, 0] = value; else cells[0, i] = value;
        }

        return isRow
            ? new RangeValue(cells, from, otherAxisStart) { SheetName = sheetName }
            : new RangeValue(cells, otherAxisStart, from) { SheetName = sheetName };
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

        // If row_num or column_num itself evaluated to an array (e.g. MATCH({...}, ...) or a
        // literal array constant), defer to the generic Index path, which broadcasts the array
        // across the table and spills -- the fast path only handles a single scalar result.
        if (rowValue is RangeValue || columnValue is RangeValue)
            return false;

        var rowCoerced = CoerceToNumber(rowValue);
        if (rowCoerced is ErrorValue rowCoerceError)
        {
            result = rowCoerceError;
            return true;
        }

        // An explicitly-blank column_num (trailing comma, or a genuine blank-cell reference)
        // coerces to 0 -- Excel's "spill the whole row" behaviour -- mirroring rowCoerced's own
        // plain CoerceToNumber(BlankValue) above, and matching BuiltInFunctions.IndexScalar's
        // slow-path handling of the same explicit-blank-column form.
        var columnCoerced = CoerceToNumber(columnValue);
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
            else
            {
                // Genuine 2-D range with column_num omitted: modern Excel spills the whole
                // selected row as a 1xN array rather than collapsing to column 1 (mirrors
                // BuiltInFunctions.IndexScalar's singleIndexArgument handling for the same case).
                columnIndex = 0;
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
            // INDEX/CHOOSE both count as reference-returning for ISREF too, the same as OFFSET/
            // INDIRECT -- see R55-formula-lookup-offset-indirect-5-2.
            FunctionCallNode fn when fn.FunctionName is "OFFSET" or "INDIRECT" or "INDEX" or "CHOOSE"
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

        // A defined name (or an OFFSET/INDIRECT/INDEX/CHOOSE result) that resolves to a multi-cell
        // reference must spill exactly the same way a literal bounded range does -- names and these
        // functions are pure reference aliases in Excel, so ISFORMULA(Data) behaves identically to
        // ISFORMULA(A1:A3) when Data = Sheet1!$A$1:$A$3. See R86-formula-logical-info-5-2.
        var multiCellResult = TryEvaluateMultiCellNamedOrFunctionReference(
            node.Arguments[0], context, mapReferenceFunctionValueErrorToNA: false,
            cell => cell?.HasFormula == true ? TrueValue : FalseValue);
        if (multiCellResult is not null) return multiCellResult;

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

        // Same defined-name/OFFSET/INDIRECT/INDEX/CHOOSE spill requirement as ISFORMULA above --
        // see R86-formula-logical-info-5-2.
        var multiCellResult = TryEvaluateMultiCellNamedOrFunctionReference(
            node.Arguments[0], context, mapReferenceFunctionValueErrorToNA: true, FormulaTextCellValue);
        if (multiCellResult is not null) return multiCellResult;

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
        return BuildIsFormulaOrFormulaTextRangeValue(
            range.SheetName, r0, c0, (int)(r1 - r0 + 1), (int)(c1 - c0 + 1), context, cellValue);
    }

    // Same as above but for a reference already resolved to a RangeValue (a defined name, or an
    // OFFSET/INDIRECT/INDEX/CHOOSE result) -- its StartRow/StartCol/RowCount/ColCount already
    // describe the concrete, bounded worksheet rectangle the reference denotes (BuildRangeValue
    // clamps any open-ended shape before this point), so no further clamping is needed here. See
    // TryEvaluateMultiCellNamedOrFunctionReference / R86-formula-logical-info-5-2.
    private static ScalarValue BuildIsFormulaOrFormulaTextRangeValue(
        RangeValue range,
        IEvalContext context,
        Func<Cell?, ScalarValue> cellValue)
        => BuildIsFormulaOrFormulaTextRangeValue(
            range.SheetName, range.StartRow, range.StartCol, range.RowCount, range.ColCount, context, cellValue);

    private static ScalarValue BuildIsFormulaOrFormulaTextRangeValue(
        string? sheetName,
        uint r0,
        uint c0,
        int rows,
        int cols,
        IEvalContext context,
        Func<Cell?, ScalarValue> cellValue)
    {
        if (sheetName is not null && !context.SheetExists(sheetName))
            return ErrorValue.Ref;

        if ((long)rows * cols > FormulaSafetyLimits.MaxMaterializedRangeCells)
            return ErrorValue.Ref;

        var cells = new ScalarValue[rows, cols];
        for (int ri = 0; ri < rows; ri++)
            for (int ci = 0; ci < cols; ci++)
            {
                // Use the spill-aware TryGetCell helper (not context.TryGetCell directly) so a
                // multi-cell reference spanning a dynamic-array spill (e.g. ISFORMULA(A1:A3) where
                // A1 spills into A2:A3) reports every spill member as a formula cell too, matching
                // Excel — see TryGetCell's spill-fallback comment above.
                TryGetCell(sheetName, r0 + (uint)ri, c0 + (uint)ci, context, out var cell);
                cells[ri, ci] = cellValue(cell);
            }

        return new RangeValue(cells, r0, c0) { SheetName = sheetName };
    }

    // True when node is a NamedRangeNode or an OFFSET/INDIRECT/INDEX/CHOOSE call that resolves to
    // a genuine multi-cell reference (a RangeValue spanning more than one cell) -- names and these
    // functions are pure reference aliases in Excel, so ISFORMULA/FORMULATEXT must spill through
    // them exactly as they do for a literal bounded range (mirrors IsMultiCellBoundedRangeRef /
    // BuildIsFormulaOrFormulaTextRangeValue above). Returns null (not handled here) when the
    // resolved reference is a single cell -- the caller's existing TryResolveReferenceTopLeftCell
    // scalar path already handles that identically -- and also when node isn't one of these
    // reference forms at all. See R86-formula-logical-info-5-2.
    private ScalarValue? TryEvaluateMultiCellNamedOrFunctionReference(
        FormulaNode node,
        IEvalContext context,
        bool mapReferenceFunctionValueErrorToNA,
        Func<Cell?, ScalarValue> cellValue)
    {
        ScalarValue reference;
        if (node is NamedRangeNode named)
            reference = ResolveNamedRangeNodeAsReference(named, context);
        else if (node is FunctionCallNode fn && fn.FunctionName is "OFFSET" or "INDIRECT" or "INDEX" or "CHOOSE")
            reference = EvaluateReferenceReturningFunction(fn, context);
        else
            return null;

        if (reference is ErrorValue error)
            return mapReferenceFunctionValueErrorToNA && error == ErrorValue.Value ? ErrorValue.NA : error;

        if (reference is not RangeValue range || range.RowCount * range.ColCount <= 1)
            return null;

        return BuildIsFormulaOrFormulaTextRangeValue(range, context, cellValue);
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

        // INDEX/CHOOSE both count as reference-returning for ISFORMULA/FORMULATEXT's argument too,
        // the same as OFFSET/INDIRECT (and the same as the r55 ISREF/CELL fixes) -- see
        // R55-formula-lookup-offset-indirect-5-2 and R56-formula-information-fns-5-1.
        if (node is FunctionCallNode fn && fn.FunctionName is "OFFSET" or "INDIRECT" or "INDEX" or "CHOOSE")
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

        // ISFORMULA/FORMULATEXT are the only callers of this helper (via
        // TryResolveReferenceTopLeftCell/TryGetTopLeftCell), and Excel treats every cell covered
        // by a dynamic-array (or legacy CSE array) spill as part of the anchor's formula: a
        // non-anchor spill member has no Cell record of its own — its value lives only in the
        // sheet's spill overlay (Sheet._spillValues) — so TryGetCell above legitimately returns
        // null for it. Fall back to the spill anchor's own formula cell in that case so
        // ISFORMULA(spill member) reports TRUE and FORMULATEXT(spill member) returns the anchor's
        // formula text, matching Excel exactly (a plain blank cell still correctly reports
        // FALSE/#N/A since TryGetArrayExtent returns false for it).
        if (cell is null)
        {
            var sheet = sheetName is not null ? context.CurrentWorkbook?.GetSheet(sheetName) : context.CurrentSheet;
            if (sheet is not null &&
                sheet.TryGetArrayExtent(new CellAddress(sheet.Id, row, column), out var anchor, out _, out _) &&
                (anchor.Row != row || anchor.Col != column))
            {
                cell = sheet.GetCell(anchor.Row, anchor.Col);
            }
        }

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

        // INDEX/CHOOSE both count as reference-returning for CELL's reference argument too, the
        // same as OFFSET/INDIRECT -- see R55-formula-lookup-offset-indirect-5-2.
        if (node is FunctionCallNode fn && fn.FunctionName is "OFFSET" or "INDIRECT" or "INDEX" or "CHOOSE")
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
            // INDEX(ref, row, [col]) and CHOOSE(index_num, ref1, ref2, ...) both return a genuine
            // Excel reference when their source arguments are references -- e.g. OFFSET(INDEX(A1:
            // A5,3),1,0) or ISREF(CHOOSE(2,A1,B1)) -- the same nested-reference idiom already
            // supported for OFFSET/INDIRECT/ANCHORARRAY here. See R55-formula-lookup-offset-
            // indirect-5-1/-2.
            "INDEX"    => EvaluateIndexAsReference(node, context),
            "CHOOSE"   => EvaluateChooseAsReference(node, context),
            _          => ErrorValue.Value
        };
    }

    /// <summary>
    /// Resolves INDEX(ref, row_num, [col_num]) to the REFERENCE it selects (a RangeValue over the
    /// chosen cell/row/column of <paramref name="node"/>'s array argument) rather than that
    /// selection's value -- mirroring TryEvaluateIndexDirectRange's row/col resolution, but for use
    /// wherever INDEX's result flows into a reference-expecting position (OFFSET's base argument,
    /// ISREF, CELL's reference argument). Only the 2/3-argument single-area reference form of INDEX
    /// is supported here (matching Excel's own "INDEX returns a reference" behavior for a plain
    /// range array); the 4-argument area_num form and a non-reference (e.g. array-constant) first
    /// argument fall back to #VALUE!, same as OFFSET's other unsupported base shapes.
    /// </summary>
    private ScalarValue EvaluateIndexAsReference(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count is < 2 or > 3) return ErrorValue.Value;
        if (!TryAsRangeRef(node.Arguments[0], out var range))
        {
            // TryAsRangeRef only understands the literal RangeRefNode/FullColumnRangeRefNode/
            // FullRowRangeRefNode shapes a token can directly parse to. A bare single-cell
            // reference (CellRefNode, e.g. A1) and a defined name (NamedRangeNode) are ALSO valid
            // INDEX reference-source shapes in Excel -- INDEX(A1,1) returns a reference to A1
            // itself and INDEX(MyName,1) returns a reference into the named range, both usable
            // anywhere a reference is expected (OFFSET's base argument, CELL("address",...)'s
            // reference argument, etc.). Resolve these two shapes here directly, mirroring
            // EvaluateOffsetReference's own base-argument switch (case CellRefNode / case
            // NamedRangeNode above) for its OWN first argument.
            switch (node.Arguments[0])
            {
                case CellRefNode cellRef:
                    if (cellRef.SheetName is not null && !context.SheetExists(cellRef.SheetName))
                        return ErrorValue.Ref;
                    range = new RangeRefNode(cellRef, cellRef, cellRef.SheetName);
                    break;
                case NamedRangeNode namedRange:
                    var namedReference = ResolveNamedRangeNodeAsReference(namedRange, context);
                    if (namedReference is ErrorValue namedError) return namedError;
                    var resolved = (RangeValue)namedReference;
                    var startCellRef = new CellRefNode(
                        FreeX.Core.Model.CellAddress.NumberToColumnName(resolved.StartCol),
                        resolved.StartRow,
                        SheetName: resolved.SheetName);
                    var endCellRef = new CellRefNode(
                        FreeX.Core.Model.CellAddress.NumberToColumnName(resolved.StartCol + (uint)resolved.ColCount - 1),
                        resolved.StartRow + (uint)resolved.RowCount - 1,
                        SheetName: resolved.SheetName);
                    range = new RangeRefNode(startCellRef, endCellRef, resolved.SheetName);
                    break;
                default:
                    return ErrorValue.Value;
            }
        }

        if (range.SheetName is not null && !context.SheetExists(range.SheetName)) return ErrorValue.Ref;

        var rowValue = EvaluateNode(node.Arguments[1], context);
        if (rowValue is ErrorValue rowError) return rowError;
        var columnValue = node.Arguments.Count > 2 ? EvaluateNode(node.Arguments[2], context) : BlankValue.Instance;
        if (columnValue is ErrorValue columnError) return columnError;

        var rowCoerced = CoerceToNumber(rowValue);
        if (rowCoerced is ErrorValue rowCoerceError) return rowCoerceError;
        // An explicitly-blank column_num coerces to 0 (whole-row spill), matching the same fix
        // applied to TryEvaluateIndexDirectRange above and BuiltInFunctions.IndexScalar's slow path.
        var columnCoerced = CoerceToNumber(columnValue);
        if (columnCoerced is ErrorValue columnCoerceError) return columnCoerceError;

        var rawRow = ((NumberValue)rowCoerced).Value;
        var rawColumn = ((NumberValue)columnCoerced).Value;
        if (!double.IsFinite(rawRow) || rawRow < int.MinValue || rawRow > int.MaxValue ||
            !double.IsFinite(rawColumn) || rawColumn < int.MinValue || rawColumn > int.MaxValue)
            return ErrorValue.Value;

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
            if (rowCount == 1) { columnIndex = rowIndex; rowIndex = 1; }
            else if (colCount == 1) { columnIndex = 1; }
            else { columnIndex = 0; }
        }

        if (rowIndex < 0 || columnIndex < 0) return ErrorValue.Value;
        if (rowIndex > rowCount || columnIndex > colCount) return ErrorValue.Ref;

        if (rowIndex == 0 && columnIndex == 0)
            return BuildRangeValueOrError(CreateRangeRef(startRow, startCol, endRow, endCol, range.SheetName), context);

        if (rowIndex == 0)
        {
            var targetCol = startCol + (uint)columnIndex - 1;
            return BuildRangeValueOrError(CreateRangeRef(startRow, targetCol, endRow, targetCol, range.SheetName), context);
        }

        if (columnIndex == 0)
        {
            var targetRow = startRow + (uint)rowIndex - 1;
            return BuildRangeValueOrError(CreateRangeRef(targetRow, startCol, targetRow, endCol, range.SheetName), context);
        }

        var row = startRow + (uint)rowIndex - 1;
        var col = startCol + (uint)columnIndex - 1;
        return BuildRangeValueOrError(CreateRangeRef(row, col, row, col, range.SheetName), context);
    }

    /// <summary>
    /// Resolves CHOOSE(index_num, ref1, ref2, ...) to the REFERENCE its selected branch argument
    /// denotes (rather than that branch's value) -- used wherever CHOOSE's result flows into a
    /// reference-expecting position, mirroring <see cref="EvaluateIndexAsReference"/> for INDEX.
    /// </summary>
    private ScalarValue EvaluateChooseAsReference(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count < 2) return ErrorValue.Value;
        var indexVal = EvaluateNode(node.Arguments[0], context);
        if (indexVal is ErrorValue indexError) return indexError;
        var coerced = CoerceToNumber(indexVal);
        if (coerced is ErrorValue coerceError) return coerceError;
        double rawIdx = ((NumberValue)coerced).Value;
        if (!double.IsFinite(rawIdx)) return ErrorValue.Value;
        int idx = (int)rawIdx;
        if (idx < 1 || idx >= node.Arguments.Count) return ErrorValue.Value;

        var branch = node.Arguments[idx];
        return branch switch
        {
            CellRefNode cellRef => cellRef.SheetName is not null && !context.SheetExists(cellRef.SheetName)
                ? ErrorValue.Ref
                : BuildRangeValueOrError(new RangeRefNode(cellRef, cellRef, cellRef.SheetName), context),
            RangeRefNode { EndSheetName: null } rangeRef => rangeRef.SheetName is not null && !context.SheetExists(rangeRef.SheetName)
                ? ErrorValue.Ref
                : BuildRangeValueOrError(rangeRef, context),
            FullColumnRangeRefNode fullColumnRange => fullColumnRange.SheetName is not null && !context.SheetExists(fullColumnRange.SheetName)
                ? ErrorValue.Ref
                : BuildRangeValueOrError(ToRangeRef(fullColumnRange), context),
            FullRowRangeRefNode fullRowRange => fullRowRange.SheetName is not null && !context.SheetExists(fullRowRange.SheetName)
                ? ErrorValue.Ref
                : BuildRangeValueOrError(ToRangeRef(fullRowRange), context),
            NamedRangeNode namedRange => ResolveNamedRangeNodeAsReference(namedRange, context),
            FunctionCallNode fn when fn.FunctionName is "OFFSET" or "INDIRECT" or "INDEX" or "CHOOSE" =>
                EvaluateReferenceReturningFunction(fn, context),
            FunctionCallNode fn when fn.FunctionName == "ANCHORARRAY" => EvaluateAnchorArray(fn, context),
            _ => ErrorValue.Value
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
            case FunctionCallNode fn when fn.FunctionName is "OFFSET" or "INDIRECT" or "ANCHORARRAY" or "INDEX" or "CHOOSE":
                // The base argument may itself be a reference-returning function call, e.g.
                // OFFSET(INDIRECT("A1"),1,1), OFFSET(OFFSET(A1,0,0),1,1), OFFSET(INDEX(A1:A5,3),1,0),
                // or OFFSET(CHOOSE(2,A1,B1),1,0) — all are valid in Excel: INDEX and CHOOSE return a
                // genuine reference (not just a value) when their source arguments are references,
                // the same well-known idiom as nesting OFFSET/INDIRECT here (see
                // R55-formula-lookup-offset-indirect-5-1). It may also be a spill (#) reference,
                // e.g. OFFSET(A1#,1,0): Excel treats A1# as the current spill range and offsets from
                // its extent, so ANCHORARRAY(ref) — the node the parser produces for A1# — is
                // resolved the same way, via EvaluateAnchorArray directly (it isn't one of the
                // functions EvaluateReferenceReturningFunction dispatches). Resolve the nested call
                // to its RangeValue via the same path used elsewhere for reference-returning
                // arguments (EvaluateCellReferenceArgument, EvaluateIsRef) and use its bounds as the
                // OFFSET base.
                var nestedReference = fn.FunctionName == "ANCHORARRAY"
                    ? EvaluateAnchorArray(fn, context)
                    : EvaluateReferenceReturningFunction(fn, context);
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
