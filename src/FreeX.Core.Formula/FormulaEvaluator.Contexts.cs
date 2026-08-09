using FreeX.Core.Model;

namespace FreeX.Core.Formula;

/// <summary>
/// Detects and resolves a bracketed external-workbook sheet reference (e.g. the literal string
/// <c>[Budget.xlsx]Sheet1</c> that <c>'[Budget.xlsx]Sheet1'!A1</c> lexes/parses to, or the
/// numeric-index form <c>[1]Sheet1</c>) against the source workbook's cached
/// <see cref="ExternalLinkModel"/> data, so formulas referencing an unopened external workbook can
/// still read the value Excel cached at last refresh instead of failing with #REF!.
/// </summary>
internal static class ExternalSheetReferenceResolver
{
    /// <summary>
    /// Parses <paramref name="sheetName"/> as <c>[book]sheet</c> and returns the matching
    /// <see cref="ExternalLinkModel"/> plus the 0-based cached-sheet index, or <see langword="null"/>
    /// when <paramref name="sheetName"/> is not a bracketed external reference, or no external link
    /// in <paramref name="workbook"/> matches the bracketed book/sheet.
    /// </summary>
    public static (ExternalLinkModel Link, int SheetIndex)? TryResolve(Workbook? workbook, string sheetName)
    {
        if (workbook is null || workbook.ExternalLinks.Count == 0)
            return null;

        if (!TrySplitBracketedReference(sheetName, out var book, out var sheet))
            return null;

        var link = TryFindExternalLink(workbook, book);
        if (link is null)
            return null;

        var sheetIndex = link.TryFindSheetIndex(sheet);
        if (sheetIndex is null)
            return null;

        return (link, sheetIndex.Value);
    }

    /// <summary>
    /// True when <paramref name="sheetName"/> has the bracketed external-workbook shape
    /// (<c>[book]sheet</c> or <c>[n]sheet</c>), regardless of whether it actually resolves against
    /// <paramref name="workbook"/>'s cached <see cref="ExternalLinkModel"/> data via
    /// <see cref="TryResolve"/>. Callers use this to distinguish "this really is an external-workbook
    /// reference that just can't be resolved right now" -- e.g. a numeric index landing on a
    /// broken-source-reference placeholder link (see XlsxExternalLinkMetadataReader) with no cached
    /// sheets at all, or a sheet name absent from an otherwise-resolvable link's cached SheetNames --
    /// from "this is simply a bad/unknown local sheet name", which must genuinely evaluate to #REF!
    /// rather than have a stale value preserved forever.
    /// </summary>
    public static bool IsExternalReferenceSyntax(string sheetName) =>
        TrySplitBracketedReference(sheetName, out _, out _);

    private static bool TrySplitBracketedReference(string sheetName, out string book, out string sheet)
    {
        book = "";
        sheet = "";
        if (string.IsNullOrEmpty(sheetName) || sheetName[0] != '[')
            return false;

        var closeIndex = sheetName.IndexOf(']');
        if (closeIndex < 1 || closeIndex == sheetName.Length - 1)
            return false;

        book = sheetName[1..closeIndex];
        sheet = sheetName[(closeIndex + 1)..];
        return book.Length > 0 && sheet.Length > 0;
    }

    private static ExternalLinkModel? TryFindExternalLink(Workbook workbook, string book)
    {
        // Numeric form: [1]Sheet1 addresses the external reference by its 1-based position in
        // workbook.xml's externalReferences list (same order XlsxExternalLinkMetadataReader builds
        // Workbook.ExternalLinks in).
        if (int.TryParse(book, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var index) &&
            index >= 1 &&
            index <= workbook.ExternalLinks.Count)
        {
            return workbook.ExternalLinks[index - 1];
        }

        // Filename form: [Budget.xlsx]Sheet1 addresses the external reference whose cached target
        // file name matches (Excel compares by file name only, not full path).
        foreach (var link in workbook.ExternalLinks)
        {
            if (FileNameMatches(link.TargetUri, book) || FileNameMatches(link.PackagePart, book))
                return link;
        }

        return null;
    }

    private static bool FileNameMatches(string? path, string book)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var fileName = path.Contains('/') || path.Contains('\\')
            ? path[(path.LastIndexOfAny(['/', '\\']) + 1)..]
            : path;
        return string.Equals(fileName, book, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the external-workbook DEFINED-NAME reference shape <c>[n]!Name</c> (no sheet
    /// segment -- the opaque <see cref="NamedRangeNode.Name"/> text
    /// <c>Parser.ParseExternalDefinedNameReference</c> builds for e.g. <c>[1]!TaxRate</c>) into an
    /// already-parseable rewritten formula text that reuses the existing quoted external-sheet
    /// cell-reference machinery (<c>'[1]Sheet1'!$B$2</c>) when the cached RefersTo is itself
    /// sheet-qualified, or (when it isn't -- a workbook-scoped constant or non-reference formula
    /// such as <c>0.08</c>, which ECMA-376 18.14.4 CT_ExternalDefinedName permits) the cached
    /// RefersTo text verbatim, so the caller (<see cref="FormulaEvaluator"/>'s
    /// SheetEvalContext.TryGetNamedFormulaText) can hand the result straight to the ordinary
    /// named-formula parse/eval path exactly like any other named formula's RefersTo text -- which
    /// in turn resolves a rewritten cell reference through the SAME cached-value lookup
    /// <see cref="TryResolve"/> already provides for the sheet-qualified form, or evaluates a
    /// constant/formula RefersTo directly, so a mixed formula like <c>=[1]!TaxRate+B2</c>
    /// recomputes its local half live instead of failing to parse at all, whether TaxRate is a
    /// cell reference or a plain constant.
    /// Returns <see langword="false"/> when <paramref name="name"/> isn't this shape, the external
    /// index is out of range, or no defined name in that link matches (case-insensitively; a
    /// workbook-scoped candidate -- <see cref="ExternalDefinedNameModel.SheetId"/> null -- is
    /// preferred over a sheet-scoped one of the same name, matching Excel's own preference for the
    /// workbook-global definition when a bare, unscoped reference could mean either).
    /// </summary>
    public static bool TryResolveExternalDefinedName(Workbook? workbook, string name, out string formulaText)
    {
        formulaText = "";
        if (workbook is null || !TrySplitExternalDefinedNameReference(name, out var externalIndex, out var definedName))
            return false;

        if (externalIndex < 1 || externalIndex > workbook.ExternalLinks.Count)
            return false;

        var link = workbook.ExternalLinks[externalIndex - 1];
        ExternalDefinedNameModel? match = null;
        foreach (var candidate in link.DefinedNames)
        {
            if (!string.Equals(candidate.Name, definedName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (candidate.SheetId is null)
            {
                match = candidate;
                break;
            }

            match ??= candidate;
        }

        if (match?.RefersTo is not { Length: > 0 } refersTo)
        {
            return false;
        }

        if (!TrySplitSheetQualifiedRefersTo(refersTo, out var sheetPart, out var cellPart))
        {
            // The cached RefersTo isn't a "Sheet!Ref" shape at all -- e.g. a workbook-scoped
            // constant (<definedName name="TaxRate" refersTo="0.08"/>) or a non-reference
            // formula. ECMA-376 18.14.4 (CT_ExternalDefinedName) places no shape requirement on
            // refersTo, and Excel itself evaluates a reference to such a name using whatever the
            // cached text actually is, not just a cell reference. Hand the raw text straight to
            // the caller so it flows through the same GetOrParseFormula/EvaluateNamedFormulaText
            // path an ordinary local named formula's bare RefersTo already uses (see
            // TryEvaluateNamedFormula in FormulaEvaluator.References.cs) instead of failing
            // outright and leaving the reference to resolve to #NAME?.
            formulaText = refersTo;
            return true;
        }

        var quotedSheet = ("[" + externalIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]" + sheetPart)
            .Replace("'", "''");
        formulaText = "'" + quotedSheet + "'!" + cellPart;
        return true;
    }

    /// <summary>Splits the opaque "[n]!Name" NamedRangeNode.Name text into the numeric external
    /// index and the trailing defined-name identifier.</summary>
    private static bool TrySplitExternalDefinedNameReference(string name, out int externalIndex, out string definedName)
    {
        externalIndex = 0;
        definedName = "";
        if (string.IsNullOrEmpty(name) || name[0] != '[')
            return false;

        var closeIndex = name.IndexOf(']');
        if (closeIndex < 2)
            return false;

        if (!int.TryParse(
                name[1..closeIndex],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out externalIndex))
        {
            return false;
        }

        if (closeIndex + 1 >= name.Length || name[closeIndex + 1] != '!')
            return false;

        definedName = name[(closeIndex + 2)..];
        return definedName.Length > 0;
    }

    /// <summary>
    /// Splits an <see cref="ExternalDefinedNameModel.RefersTo"/> text (e.g. <c>Sheet1!$B$2</c>, or
    /// <c>'My Sheet'!$B$2</c> when the sheet name needs quoting) into its sheet-name and
    /// cell/range-reference parts, understanding the same doubled-apostrophe quoting convention as
    /// <see cref="Lexer.ReadQuotedSheetQualifier"/>.
    /// </summary>
    private static bool TrySplitSheetQualifiedRefersTo(string refersTo, out string sheetPart, out string cellPart)
    {
        sheetPart = "";
        cellPart = "";
        if (string.IsNullOrEmpty(refersTo))
            return false;

        if (refersTo[0] == '\'')
        {
            var sb = new System.Text.StringBuilder();
            var i = 1;
            while (i < refersTo.Length)
            {
                if (refersTo[i] == '\'')
                {
                    if (i + 1 < refersTo.Length && refersTo[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i += 2;
                        continue;
                    }

                    i++; // skip closing quote
                    break;
                }

                sb.Append(refersTo[i]);
                i++;
            }

            if (i >= refersTo.Length || refersTo[i] != '!')
                return false;

            sheetPart = sb.ToString();
            cellPart = refersTo[(i + 1)..];
            return sheetPart.Length > 0 && cellPart.Length > 0;
        }

        var bangIndex = refersTo.IndexOf('!');
        if (bangIndex < 0)
            return false;

        sheetPart = refersTo[..bangIndex];
        cellPart = refersTo[(bangIndex + 1)..];
        return sheetPart.Length > 0 && cellPart.Length > 0;
    }
}

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
        private readonly bool _isIterativeCalculationPass;
        private Dictionary<string, FreeX.Core.Model.Sheet?>? _sheetNameCache;

        public readonly Sheet SourceSheet;

        public SheetEvalContext(
            Sheet sheet,
            FreeX.Core.Model.Workbook? workbook,
            FormulaEvaluator evaluator,
            FreeX.Core.Model.CellAddress? currentCellAddress,
            bool isIterativeCalculationPass = false)
        {
            _sheet = sheet;
            SourceSheet = sheet;
            _workbook = workbook;
            _evaluator = evaluator;
            _currentCellAddress = currentCellAddress;
            _isIterativeCalculationPass = isIterativeCalculationPass;
        }

        public ScalarValue GetCellValue(uint row, uint col) => _sheet.GetValue(row, col);

        public ScalarValue GetCellValue(string sheetName, uint row, uint col)
        {
            var target = ResolveSheet(sheetName);
            if (target is not null) return target.GetValue(row, col);

            var external = ExternalSheetReferenceResolver.TryResolve(_workbook, sheetName);
            if (external is { } resolved)
            {
                // A resolvable external sheet caches only the cells a formula actually referenced at
                // last refresh; an uncached cell within it is a real blank, not a #REF! error — but
                // ONLY when the link's sheetDataSet cache exists at all. When the producer wrote no
                // (or an incomplete) sheetDataSet, there is no way to tell "genuinely blank" apart
                // from "never refreshed", and the formula's own cell already carries the correct
                // value Excel cached directly in the worksheet's <f>/<v> pair at load time (see
                // XlsxClosedXmlCellMapper.MapFormulaValue). Recomputing here and returning Blank would
                // silently overwrite that loaded value with 0/blank on every recalc. Throwing routes
                // through RecalcEngine's existing external-workbook-reference guard (see
                // RecalcEngine.IsLikelyExternalWorkbookReferenceFormula, which matches on the same
                // bracketed sheet-name text this quoted form also contains) so the cell's last-known
                // value is preserved instead — exactly like the unquoted '[1]Sheet1!A1' form that
                // never parses at all.
                if (resolved.Link.TryGetCachedValue(resolved.SheetIndex, row, col, out var cachedValue))
                    return cachedValue ?? BlankValue.Instance;

                throw new FormulaParseException(
                    $"External reference '{sheetName}' has no cached value for this cell; " +
                    "preserving the last-known loaded value instead of recomputing to blank.");
            }

            // The bracketed reference has the external-workbook shape but couldn't be resolved at
            // all -- e.g. a numeric index landing on a broken-source-reference placeholder link (see
            // XlsxExternalLinkMetadataReader's blank/duplicate/unresolvable r:id handling) with no
            // cached sheets, or a sheet name absent from an otherwise-resolvable link's cached
            // SheetNames. This is still an external-workbook reference Excel would keep showing its
            // last-known cached value for (flagging it as broken only once the user runs Edit Links >
            // Update Values), not a genuine local #REF!. Throw the same exception the resolved-but-
            // uncached branch above uses so RecalcEngine's external-workbook-reference preservation
            // guard fires here too, instead of returning ErrorValue.Ref as a normal result that would
            // get stored straight into the cell and permanently clobber its loaded value.
            if (ExternalSheetReferenceResolver.IsExternalReferenceSyntax(sheetName))
            {
                throw new FormulaParseException(
                    $"External reference '{sheetName}' could not be resolved against the workbook's " +
                    "external links; preserving the last-known loaded value instead of recomputing to #REF!.");
            }

            return ErrorValue.Ref;
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
            var r0 = Math.Min(startRow, endRow); var r1 = Math.Max(startRow, endRow);
            var c0 = Math.Min(startCol, endCol); var c1 = Math.Max(startCol, endCol);
            if (target is null)
            {
                var external = ExternalSheetReferenceResolver.TryResolve(_workbook, sheetName);
                if (external is not { } resolved)
                {
                    // Mirror the scalar GetCellValue(sheetName, ...) behavior: a bracketed reference
                    // that has the external-workbook shape but couldn't be resolved at all (broken
                    // placeholder link, or sheet name absent from the link's cached SheetNames) must
                    // still preserve the cell's last-known loaded value via RecalcEngine's guard,
                    // rather than returning ErrorValue.Ref as a normal result that clobbers it.
                    if (ExternalSheetReferenceResolver.IsExternalReferenceSyntax(sheetName))
                    {
                        throw new FormulaParseException(
                            $"External reference '{sheetName}' could not be resolved against the " +
                            "workbook's external links; preserving the last-known loaded value instead " +
                            "of recomputing to #REF!.");
                    }

                    return [ErrorValue.Ref];
                }

                var externalValues = CreateRangeValueList(r0, c0, r1, c1);
                if (externalValues is null) return [new RangeMaterializationErrorValue(ErrorValue.Ref)];
                for (var r = r0; r <= r1; r++)
                {
                    for (var c = c0; c <= c1; c++)
                    {
                        // Mirror the scalar GetCellValue(sheetName, ...) behavior above: a cache miss
                        // must throw (preserving the cell's last-known loaded value via RecalcEngine's
                        // external-workbook-reference guard) instead of silently substituting Blank,
                        // or a range-shaped external reference (e.g. inside MEDIAN/PRODUCT/VLOOKUP/
                        // LARGE) would overwrite a correct loaded value with a wrong recomputed one.
                        if (!resolved.Link.TryGetCachedValue(resolved.SheetIndex, r, c, out var cachedValue))
                        {
                            throw new FormulaParseException(
                                $"External reference '{sheetName}' has no cached value for this cell; " +
                                "preserving the last-known loaded value instead of recomputing to blank.");
                        }

                        externalValues.Add(cachedValue ?? BlankValue.Instance);
                    }
                }

                return externalValues;
            }

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
            // Sheet-scope-first: a name scoped to the current sheet takes precedence
            // over a same-named workbook-global name (Excel rule §18.2.6).
            if (_workbook.TryGetNamedRange(name, _sheet.Id, out var range))
                return range;
            return null;
        }

        public string? TryGetNamedFormulaText(string name)
        {
            if (_workbook is null) return null;

            // The opaque "[n]!Name" shape (an external-workbook DEFINED-NAME reference with no
            // sheet segment, e.g. [1]!TaxRate -- see Parser.ParseExternalDefinedNameReference)
            // is never a real workbook/sheet-scoped name, so check it first and rewrite it to the
            // already-supported quoted external-sheet cell-reference form.
            if (ExternalSheetReferenceResolver.TryResolveExternalDefinedName(_workbook, name, out var externalFormulaText))
                return externalFormulaText;

            // Sheet-scope-first for named formulas too.
            return _workbook.TryGetNamedFormulaText(name, _sheet.Id);
        }

        public string? TryGetSheetName(FreeX.Core.Model.SheetId sheetId)
            => _workbook?.GetSheet(sheetId)?.Name;

        public bool SheetExists(string sheetName) =>
            ResolveSheet(sheetName) is not null ||
            ExternalSheetReferenceResolver.TryResolve(_workbook, sheetName) is not null;

        public bool IsRowHidden(uint row) => _sheet.IsRowEffectivelyHidden(row);

        public bool IsRowHidden(string sheetName, uint row)
            => _workbook?.GetSheet(sheetName)?.IsRowEffectivelyHidden(row) ?? false;

        public bool IsRowFilterHidden(uint row) => _sheet.FilterHiddenRows.Contains(row);

        public bool IsRowFilterHidden(string sheetName, uint row)
            => _workbook?.GetSheet(sheetName)?.FilterHiddenRows.Contains(row) ?? false;

        public FreeX.Core.Model.Sheet? CurrentSheet => _sheet;

        public FreeX.Core.Model.Workbook? CurrentWorkbook => _workbook;

        public FreeX.Core.Model.CellAddress? CurrentCellAddress => _currentCellAddress;
        public bool IsIterativeCalculationPass => _isIterativeCalculationPass;

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
            // Lexical scoping: evaluate the body against the environment captured when the
            // LAMBDA was defined (lambda.Closure), not the call-site context (this).
            // Array-aware: a bare range body (e.g. LAMBDA(x, B1:B3)) must materialize into a
            // RangeValue instead of silently collapsing to its top-left cell via implicit
            // intersection, so callers like MAP/BYROW/BYCOL/SCAN/MAKEARRAY can detect and reject
            // a nested-array result (their `is RangeValue` #CALC! guard) instead of getting a
            // wrong scalar.
            return _evaluator.EvaluateArrayOperand(lambda.Body, new ScopedEvalContext(lambda.Closure ?? this, bindings, _evaluator));
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
        public bool IsIterativeCalculationPass => _inner.IsIterativeCalculationPass;
        public FreeX.Core.Model.Cell? TryGetCell(uint row, uint col) => _inner.TryGetCell(row, col);
        public FreeX.Core.Model.Cell? TryGetCell(string sn, uint row, uint col) => _inner.TryGetCell(sn, row, col);

        public ScalarValue? TryResolveLambdaBinding(string name) =>
            _bindings.TryGetValue(name, out var v) ? v : _inner.TryResolveLambdaBinding(name);

        public string? TryGetNamedFormulaText(string name) =>
            _inner.TryGetNamedFormulaText(name);

        public ScalarValue InvokeLambda(LambdaValue lambda, IReadOnlyList<ScalarValue> args)
        {
            if (args.Count != lambda.Parameters.Count) return ErrorValue.Value;
            var nb = new Dictionary<string, ScalarValue>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lambda.Parameters.Count; i++) nb[lambda.Parameters[i]] = args[i];
            // Lexical scoping: evaluate the body against the environment captured when the
            // LAMBDA was defined (lambda.Closure), not the call-site context (this).
            // Array-aware: see the matching comment in SheetEvalContext.InvokeLambda above.
            return _evaluator.EvaluateArrayOperand(lambda.Body, new ScopedEvalContext(lambda.Closure ?? this, nb, _evaluator));
        }
    }
}
