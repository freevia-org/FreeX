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
                rangeReference.EndCol);

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

        return false;
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
        int bangIdx = refText.IndexOf('!');
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

    private static ScalarValue BuildIndirectRange(
        IEvalContext ctx,
        string? sheetName,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol)
    {
        if (sheetName is not null && !ctx.SheetExists(sheetName)) return ErrorValue.Ref;

        uint r0 = Math.Min(startRow, endRow);
        uint r1 = Math.Max(startRow, endRow);
        uint c0 = Math.Min(startCol, endCol);
        uint c1 = Math.Max(startCol, endCol);
        if (FormulaSafetyLimits.GetRangeCellCount(r0, c0, r1, c1) > FormulaSafetyLimits.MaxMaterializedRangeCells)
            return ErrorValue.Ref;

        var cells = new ScalarValue[r1 - r0 + 1, c1 - c0 + 1];
        for (uint r = r0; r <= r1; r++)
            for (uint c = c0; c <= c1; c++)
                cells[r - r0, c - c0] = sheetName is not null
                    ? ctx.GetCellValue(sheetName, r, c)
                    : ctx.GetCellValue(r, c);

        return new RangeValue(cells, r0, c0) { SheetName = sheetName };
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

