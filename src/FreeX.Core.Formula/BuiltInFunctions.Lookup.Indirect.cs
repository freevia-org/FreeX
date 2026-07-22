using FreeX.Core.Model;

using System.Text.RegularExpressions;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    internal readonly record struct IndirectRangeReference(
        string? SheetName,
        uint StartRow,
        uint StartCol,
        uint EndRow,
        uint EndCol,
        bool IsFullRowRange,
        bool IsFullColumnRange);

    private static ScalarValue Indirect(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => IndirectCore(args, ctx, unwrapSingleCell: true);

    internal static ScalarValue IndirectReference(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => IndirectCore(args, ctx, unwrapSingleCell: false);

    private static ScalarValue IndirectCore(IReadOnlyList<ScalarValue> args, IEvalContext ctx, bool unwrapSingleCell)
    {
        if (!TryGetIndirectReferenceParts(args, out var refText, out var useA1, out var sheetName, out var error))
            return error ?? ErrorValue.Value;

        if (TryResolveIndirectRangeReference(refText, useA1, sheetName, ctx, out var rangeReference, out error))
            return BuildIndirectRange(
                ctx,
                rangeReference.SheetName,
                rangeReference.StartRow,
                rangeReference.StartCol,
                rangeReference.EndRow,
                rangeReference.EndCol,
                rangeReference.IsFullColumnRange,
                rangeReference.IsFullRowRange);

        if (error is not null)
            return error;

        if (useA1
                ? !TryParseA1Ref(refText, out uint row, out uint col)
                : !TryParseR1C1Ref(refText, ctx.CurrentCellAddress, out row, out col))
            return ErrorValue.Ref;

        return unwrapSingleCell
            ? sheetName is not null
                ? ctx.GetCellValue(sheetName, row, col)
                : ctx.GetCellValue(row, col)
            : BuildIndirectRange(ctx, sheetName, row, col, row, col);
    }

    internal static bool TryResolveIndirectRangeReference(
        IReadOnlyList<ScalarValue> args,
        IEvalContext ctx,
        out IndirectRangeReference range,
        out ScalarValue? error)
    {
        range = default;
        if (!TryGetIndirectReferenceParts(args, out var refText, out var useA1, out var sheetName, out error))
            return false;

        return TryResolveIndirectRangeReference(refText, useA1, sheetName, ctx, out range, out error);
    }

    private static bool TryResolveIndirectRangeReference(
        string refText,
        bool useA1,
        string? sheetName,
        IEvalContext ctx,
        out IndirectRangeReference range,
        out ScalarValue? error)
    {
        range = default;
        error = null;

        if (useA1 && TryParseA1RangeRef(refText, out var startRow, out var startCol, out var endRow, out var endCol))
            return CompleteIndirectRange(ctx, sheetName, startRow, startCol, endRow, endCol, out range, out error);
        if (useA1 && TryParseA1FullRowRangeRef(refText, out startRow, out endRow))
            return CompleteIndirectRange(ctx, sheetName, startRow, 1, endRow, CellAddress.MaxCol, out range, out error, isFullRowRange: true);
        if (useA1 && TryParseA1FullColumnRangeRef(refText, out startCol, out endCol))
            return CompleteIndirectRange(ctx, sheetName, 1, startCol, CellAddress.MaxRow, endCol, out range, out error, isFullColumnRange: true);
        if (!useA1 && TryParseR1C1RangeRef(refText, ctx.CurrentCellAddress, out startRow, out startCol, out endRow, out endCol))
            return CompleteIndirectRange(ctx, sheetName, startRow, startCol, endRow, endCol, out range, out error);
        if (!useA1 && TryParseR1C1FullRowRangeRef(refText, ctx.CurrentCellAddress, out startRow, out endRow))
            return CompleteIndirectRange(ctx, sheetName, startRow, 1, endRow, CellAddress.MaxCol, out range, out error, isFullRowRange: true);
        if (!useA1 && TryParseR1C1FullColumnRangeRef(refText, ctx.CurrentCellAddress, out startCol, out endCol))
            return CompleteIndirectRange(ctx, sheetName, 1, startCol, CellAddress.MaxRow, endCol, out range, out error, isFullColumnRange: true);
        if (!useA1 && TryParseR1C1FullRowRef(refText, ctx.CurrentCellAddress, out startRow))
            return CompleteIndirectRange(ctx, sheetName, startRow, 1, startRow, CellAddress.MaxCol, out range, out error, isFullRowRange: true);
        if (!useA1 && TryParseR1C1FullColumnRef(refText, ctx.CurrentCellAddress, out startCol))
            return CompleteIndirectRange(ctx, sheetName, 1, startCol, CellAddress.MaxRow, startCol, out range, out error, isFullColumnRange: true);

        // Excel's name-scope rule (§18.2.6): a name scoped to the current sheet always shadows a
        // same-named workbook-global name, regardless of whether either name is a plain range or a
        // formula expression — so a sheet-scoped named FORMULA must shadow a workbook-global named
        // RANGE. Mirror EvaluateNamedRange/IsSheetScopedName in FormulaEvaluator.References.cs:
        // resolve a sheet-scoped named formula first, before ever falling through to the naive
        // ctx.TryResolveNamedRange lookup below, which only ever sees ScopedNamedRanges (never
        // ScopedNamedFormulas) and would otherwise resolve the shadowed workbook-global range.
        if (sheetName is null && IsSheetScopedNamedFormula(refText, ctx))
        {
            return FormulaEvaluator.TryResolveIndirectNamedFormula(refText, ctx, out var scopedFormulaRange, out error)
                && CompleteIndirectRangeFromNamedFormula(ctx, scopedFormulaRange, out range, out error);
        }

        if (sheetName is null && ctx.TryResolveNamedRange(refText) is { } namedRange)
        {
            var namedSheetName = ctx.TryGetSheetName(namedRange.Start.Sheet);
            if (namedSheetName is null)
            {
                error = ErrorValue.Ref;
                return false;
            }

            return CompleteIndirectRange(
                ctx,
                namedSheetName,
                namedRange.Start.Row,
                namedRange.Start.Col,
                namedRange.End.Row,
                namedRange.End.Col,
                out range,
                out error);
        }

        // The plain-range lookup above only finds range-kind names. A name whose RefersTo is a
        // formula/dynamic expression (e.g. "=OFFSET($A$1,0,0,COUNTA($A:$A),1)" for a growing
        // named range) is invisible to it, so also try resolving refText as a named formula that
        // evaluates to a reference — see FormulaEvaluator.TryResolveIndirectNamedFormula.
        if (sheetName is null && FormulaEvaluator.TryResolveIndirectNamedFormula(refText, ctx, out var namedFormulaRange, out error))
            return CompleteIndirectRangeFromNamedFormula(ctx, namedFormulaRange, out range, out error);

        // R74-formula-reference-fns-4-1: none of the address parses above ever match plain name
        // text (e.g. "Rate" has no digits/colon), so a sheet-qualified name reference like
        // INDIRECT("Sheet2!Rate") reached this point and fell straight through to the raw-address
        // parse in IndirectCore, which also fails and returns #REF! -- even though a sheet-scoped
        // name used off its own sheet is exactly what Excel's own "SheetName!Name" syntax denotes
        // (mirrors how a direct =Sheet2!Rate formula reference resolves via
        // FormulaEvaluator.TryResolveSheetQualifiedName). Resolve refText as a named range scoped
        // to the qualifying sheet, falling back to the workbook-global range of that name --
        // Workbook.TryGetNamedRange(name, sheetId) already implements exactly that precedence.
        if (sheetName is not null &&
            ctx.CurrentWorkbook is { } qualifiedWorkbook &&
            qualifiedWorkbook.GetSheet(sheetName) is { } qualifiedSheet &&
            qualifiedWorkbook.TryGetNamedRange(refText, qualifiedSheet.Id, out var qualifiedNamedRange))
        {
            var qualifiedRangeSheetName = ctx.TryGetSheetName(qualifiedNamedRange.Start.Sheet);
            if (qualifiedRangeSheetName is null)
            {
                error = ErrorValue.Ref;
                return false;
            }

            return CompleteIndirectRange(
                ctx,
                qualifiedRangeSheetName,
                qualifiedNamedRange.Start.Row,
                qualifiedNamedRange.Start.Col,
                qualifiedNamedRange.End.Row,
                qualifiedNamedRange.End.Col,
                out range,
                out error);
        }

        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="name"/> has an explicit sheet-scoped named-FORMULA
    /// definition on the context's current sheet, which must take precedence over any
    /// workbook-global name (range or formula) of the same name — mirrors
    /// FormulaEvaluator.IsSheetScopedName's formula branch.
    /// </summary>
    private static bool IsSheetScopedNamedFormula(string name, IEvalContext ctx)
    {
        var workbook = ctx.CurrentWorkbook;
        var sheet = ctx.CurrentSheet;
        return workbook is not null && sheet is not null && workbook.ScopedNamedFormulas.ContainsKey((name, sheet.Id));
    }

    private static bool CompleteIndirectRangeFromNamedFormula(
        IEvalContext ctx,
        RangeValue namedFormulaRange,
        out IndirectRangeReference range,
        out ScalarValue? error)
    {
        var formulaStartRow = namedFormulaRange.StartRow;
        var formulaStartCol = namedFormulaRange.StartCol;
        var formulaEndRow = formulaStartRow + (uint)namedFormulaRange.RowCount - 1;
        var formulaEndCol = formulaStartCol + (uint)namedFormulaRange.ColCount - 1;
        return CompleteIndirectRange(
            ctx,
            namedFormulaRange.SheetName,
            formulaStartRow,
            formulaStartCol,
            formulaEndRow,
            formulaEndCol,
            out range,
            out error);
    }

    private static bool CompleteIndirectRange(
        IEvalContext ctx,
        string? sheetName,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol,
        out IndirectRangeReference range,
        out ScalarValue? error,
        bool isFullRowRange = false,
        bool isFullColumnRange = false)
    {
        range = default;
        error = null;
        if (sheetName is not null && !ctx.SheetExists(sheetName))
        {
            error = ErrorValue.Ref;
            return false;
        }

        range = new IndirectRangeReference(sheetName, startRow, startCol, endRow, endCol, isFullRowRange, isFullColumnRange);
        return true;
    }

    private static bool TryGetIndirectReferenceParts(
        IReadOnlyList<ScalarValue> args,
        out string refText,
        out bool useA1,
        out string? sheetName,
        out ScalarValue? error)
    {
        refText = "";
        useA1 = true;
        sheetName = null;
        error = null;

        if (args.Count is < 1 or > 2)
        {
            error = ErrorValue.Value;
            return false;
        }

        if (args[0] is ErrorValue e)
        {
            error = e;
            return false;
        }

        if (args.Count > 1 && args[1] is ErrorValue e1)
        {
            error = e1;
            return false;
        }

        refText = ToText(args[0]).Trim();
        useA1 = args.Count < 2 || args[1] is BlankValue || ToBool(args[1]);
        int bangIdx = FindSheetQualifierBangIndex(refText);
        if (bangIdx >= 0)
        {
            var sheetPart = refText[..bangIdx];
            if (sheetPart.StartsWith('\'') && sheetPart.EndsWith('\'') && sheetPart.Length >= 2)
                sheetName = sheetPart[1..^1].Replace("''", "'");
            else
            {
                if (!IsSimpleSheetQualifier(sheetPart))
                {
                    error = ErrorValue.Ref;
                    return false;
                }

                sheetName = sheetPart;
            }

            refText = refText[(bangIdx + 1)..];
        }

        return true;
    }

    // Finds the index of the '!' that separates the sheet qualifier from the reference text,
    // matching Lexer.ReadQuotedSheetQualifier's rule that a quoted sheet name may itself contain
    // '!' (or any other character) and only the '!' immediately after the closing, non-escaped
    // quote counts as the delimiter. For an unquoted qualifier, Excel sheet names may still
    // legally contain '!' (Workbook.InvalidSheetNameChars does not exclude it), so the LAST '!'
    // in the text is the delimiter — mirroring how a direct formula reference like
    // 'Sheet1!A1!B2'!A1 would only ever treat the trailing '!' as the sheet/cell separator.
    private static int FindSheetQualifierBangIndex(string refText)
    {
        if (refText.Length > 0 && refText[0] == '\'')
        {
            var i = 1;
            while (i < refText.Length)
            {
                if (refText[i] == '\'')
                {
                    if (i + 1 < refText.Length && refText[i + 1] == '\'')
                    {
                        i += 2; // escaped '' inside the quoted name
                        continue;
                    }

                    // Closing quote: the delimiter (if any) is the very next character.
                    return i + 1 < refText.Length && refText[i + 1] == '!' ? i + 1 : -1;
                }

                i++;
            }

            return -1; // unterminated quote — no valid delimiter
        }

        return refText.LastIndexOf('!');
    }

    private static ScalarValue BuildIndirectRange(
        IEvalContext ctx,
        string? sheetName,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol,
        bool isFullColumnRange = false,
        bool isFullRowRange = false)
    {
        if (sheetName is not null && !ctx.SheetExists(sheetName)) return ErrorValue.Ref;

        uint r0 = Math.Min(startRow, endRow);
        uint r1 = Math.Max(startRow, endRow);
        uint c0 = Math.Min(startCol, endCol);
        uint c1 = Math.Max(startCol, endCol);

        // A full-column (A:A) / full-row (1:1) text reference nominally spans the whole grid,
        // which would exceed the materialization cap below and always return #REF! — even on an
        // otherwise-empty sheet. Excel only ever materializes the populated extent, so clamp the
        // open end down to the target sheet's used range, mirroring ClampOpenEndedRangeToUsed's
        // treatment of direct full-column/full-row references (FormulaEvaluator.References.cs).
        if (isFullColumnRange || isFullRowRange)
        {
            var targetSheet = sheetName is not null ? ctx.CurrentWorkbook?.GetSheet(sheetName) : ctx.CurrentSheet;
            if (targetSheet is not null)
            {
                if (targetSheet.GetUsedRange() is { } used)
                {
                    if (isFullColumnRange) r1 = Math.Min(r1, Math.Max(used.End.Row, r0));
                    if (isFullRowRange) c1 = Math.Min(c1, Math.Max(used.End.Col, c0));
                }
                else
                {
                    // Empty sheet: collapse the open dimension to its start (a single blank line).
                    if (isFullColumnRange) r1 = r0;
                    if (isFullRowRange) c1 = c0;
                }
            }
        }

        if (FormulaSafetyLimits.GetRangeCellCount(r0, c0, r1, c1) > FormulaSafetyLimits.MaxMaterializedRangeCells)
            return ErrorValue.Ref;

        var cells = new ScalarValue[r1 - r0 + 1, c1 - c0 + 1];
        for (uint r = r0; r <= r1; r++)
            for (uint c = c0; c <= c1; c++)
                cells[r - r0, c - c0] = sheetName is not null
                    ? ctx.GetCellValue(sheetName, r, c)
                    : ctx.GetCellValue(r, c);

        // INDIRECT resolves to a genuine worksheet reference — its coordinates map to real cells, so
        // mark it so SUBTOTAL/AGGREGATE honour hidden-row / nested-aggregate exclusion (RangeValue.IsSheetReference).
        return new RangeValue(cells, r0, c0) { SheetName = sheetName, IsSheetReference = true };
    }


    private static bool TryParseA1RangeRef(string refText, out uint startRow, out uint startCol, out uint endRow, out uint endCol)
    {
        startRow = startCol = endRow = endCol = 0;
        int colon = refText.IndexOf(':');
        if (colon < 0 || colon != refText.LastIndexOf(':')) return false;

        return TryParseA1Ref(refText[..colon], out startRow, out startCol)
            && TryParseA1Ref(refText[(colon + 1)..], out endRow, out endCol);
    }

    private static bool TryParseA1FullRowRangeRef(string refText, out uint startRow, out uint endRow)
    {
        startRow = endRow = 0;
        int colon = refText.IndexOf(':');
        if (colon < 0 || colon != refText.LastIndexOf(':')) return false;

        return TryParseA1RowNumber(refText[..colon], out startRow)
            && TryParseA1RowNumber(refText[(colon + 1)..], out endRow);
    }

    private static bool TryParseA1FullColumnRangeRef(string refText, out uint startCol, out uint endCol)
    {
        startCol = endCol = 0;
        int colon = refText.IndexOf(':');
        if (colon < 0 || colon != refText.LastIndexOf(':')) return false;

        return TryParseA1ColumnName(refText[..colon], out startCol)
            && TryParseA1ColumnName(refText[(colon + 1)..], out endCol);
    }

    private static bool TryParseA1RowNumber(string text, out uint row)
    {
        row = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.StartsWith('$')) text = text[1..];
        if (text.Length == 0 || text.Any(ch => !char.IsDigit(ch))) return false;
        return uint.TryParse(text, out row) && row is >= 1 and <= CellAddress.MaxRow;
    }

    private static bool TryParseA1ColumnName(string text, out uint col)
    {
        col = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.StartsWith('$')) text = text[1..];
        if (text.Length == 0 || text.Any(ch => !char.IsLetter(ch))) return false;
        col = CellAddress.ColumnNameToNumber(text.ToUpperInvariant());
        return col is >= 1 and <= CellAddress.MaxCol;
    }

    private static bool TryParseR1C1RangeRef(
        string refText,
        CellAddress? currentCell,
        out uint startRow,
        out uint startCol,
        out uint endRow,
        out uint endCol)
    {
        startRow = startCol = endRow = endCol = 0;
        int colon = refText.IndexOf(':');
        if (colon < 0 || colon != refText.LastIndexOf(':')) return false;

        return TryParseR1C1Ref(refText[..colon], currentCell, out startRow, out startCol)
            && TryParseR1C1Ref(refText[(colon + 1)..], currentCell, out endRow, out endCol);
    }

    // R1C1's documented whole-row ("R5"/"R[-1]") and whole-column ("C3"/"C[2]") forms — the
    // R1C1-style counterparts to TryParseA1FullRowRangeRef/TryParseA1FullColumnRangeRef above.
    private static bool TryParseR1C1FullRowRangeRef(string refText, CellAddress? currentCell, out uint startRow, out uint endRow)
    {
        startRow = endRow = 0;
        int colon = refText.IndexOf(':');
        if (colon < 0 || colon != refText.LastIndexOf(':')) return false;

        return TryParseR1C1FullRowRef(refText[..colon], currentCell, out startRow)
            && TryParseR1C1FullRowRef(refText[(colon + 1)..], currentCell, out endRow);
    }

    private static bool TryParseR1C1FullColumnRangeRef(string refText, CellAddress? currentCell, out uint startCol, out uint endCol)
    {
        startCol = endCol = 0;
        int colon = refText.IndexOf(':');
        if (colon < 0 || colon != refText.LastIndexOf(':')) return false;

        return TryParseR1C1FullColumnRef(refText[..colon], currentCell, out startCol)
            && TryParseR1C1FullColumnRef(refText[(colon + 1)..], currentCell, out endCol);
    }

    private static bool TryParseR1C1FullRowRef(string refText, CellAddress? currentCell, out uint row)
    {
        row = 0;
        var match = Regex.Match(refText, @"^R(?:(\d+)|\[(-?\d+)\])$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        return ResolveR1C1Part(match.Groups[1].Value, match.Groups[2].Value, currentCell?.Row, CellAddress.MaxRow, out row);
    }

    private static bool TryParseR1C1FullColumnRef(string refText, CellAddress? currentCell, out uint col)
    {
        col = 0;
        var match = Regex.Match(refText, @"^C(?:(\d+)|\[(-?\d+)\])$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        return ResolveR1C1Part(match.Groups[1].Value, match.Groups[2].Value, currentCell?.Col, CellAddress.MaxCol, out col);
    }

    private static bool TryParseA1Ref(string cellRef, out uint row, out uint col)
    {
        row = 0; col = 0;
        int i = 0;
        // Skip optional leading '$' (absolute column marker)
        if (i < cellRef.Length && cellRef[i] == '$') i++;
        while (i < cellRef.Length && char.IsLetter(cellRef[i])) i++;
        if (i == 0 || i >= cellRef.Length) return false;
        // Strip leading '$' from the column portion when building colStr
        int colStart = cellRef[0] == '$' ? 1 : 0;
        string colStr = cellRef[colStart..i].ToUpperInvariant();
        string rowPart = cellRef[i..];
        // Skip optional '$' before row number
        if (rowPart.Length > 0 && rowPart[0] == '$') rowPart = rowPart[1..];
        if (!uint.TryParse(rowPart, out row)) return false;
        col = CellAddress.ColumnNameToNumber(colStr);
        return row > 0 && row <= CellAddress.MaxRow && col > 0 && col <= CellAddress.MaxCol;
    }

    private static bool TryParseR1C1Ref(string cellRef, CellAddress? currentCell, out uint row, out uint col)
    {
        row = 0; col = 0;
        var match = Regex.Match(cellRef, @"^R(?:(\d+)|\[(-?\d+)\])?C(?:(\d+)|\[(-?\d+)\])?$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        if (!ResolveR1C1Part(match.Groups[1].Value, match.Groups[2].Value, currentCell?.Row, CellAddress.MaxRow, out row))
            return false;
        if (!ResolveR1C1Part(match.Groups[3].Value, match.Groups[4].Value, currentCell?.Col, CellAddress.MaxCol, out col))
            return false;
        return true;
    }

    private static bool ResolveR1C1Part(string absoluteText, string relativeText, uint? current, uint max, out uint value)
    {
        value = 0;
        if (absoluteText.Length > 0)
            return uint.TryParse(absoluteText, out value) && value > 0 && value <= max;

        if (current is null) return false;

        long resolved = current.Value;
        if (relativeText.Length > 0)
        {
            if (!long.TryParse(relativeText, out var offset)) return false;
            resolved += offset;
        }

        if (resolved <= 0 || resolved > max) return false;
        value = (uint)resolved;
        return true;
    }
}
