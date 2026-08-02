using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Shared mapping, length, and DBCS helpers for text functions.

    private static ScalarValue TextResult(string text) =>
        ExceedsExcelTextLimit(text) ? ErrorValue.Value : new TextValue(text);

    internal static bool ExceedsExcelTextLimit(string text) =>
        text.Length > 32767;

    private static RangeValue MapUnaryTextRange(RangeValue range, Func<ScalarValue, ScalarValue> map)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                cells[r, c] = value is ErrorValue e ? e : map(value);
            }

        // Preserve the source range's absolute origin so a legacy (Implicit) formula that broadcasts a scalar
        // function over a range — e.g. =ABS(K1:N1), =ACOS(K8:N8) — can implicitly intersect the result to the
        // cell sharing the formula's row/column. Without the origin the intersection looks off-axis (#VALUE!).
        return new RangeValue(cells, range.StartRow, range.StartCol);
    }

    /// <summary>
    /// Maps a 3-argument function over its (possibly array) arguments, growing to the bounding
    /// Max(rows)/Max(cols) 2-D broadcast shape (via <see cref="TryGrowBroadcastShape"/>) rather than
    /// requiring every range argument to either match the FIRST non-scalar range's exact shape or be
    /// a 1x1 scalar. A genuine row-vector x column-vector pair (e.g. PMT's rate/nper) must 2-D
    /// cross-broadcast into a spilled matrix, matching Excel dynamic arrays and the identical rule
    /// already used by MapBinaryMathArgs (BuiltInFunctions.MathCore.Helpers.cs) and the binary
    /// operators (FormulaEvaluator.Operators' ElementwiseOp) -- see R118-formula-arity3plus-cross-broadcast.
    /// </summary>
    private static ScalarValue MapTernaryTextArgs(
        ScalarValue first,
        ScalarValue second,
        ScalarValue third,
        Func<ScalarValue, ScalarValue, ScalarValue, ScalarValue> map)
    {
        var firstRange = first as RangeValue;
        var secondRange = second as RangeValue;
        var thirdRange = third as RangeValue;
        if (firstRange is null && secondRange is null && thirdRange is null)
            return map(first, second, third);
        if (!TryGrowBroadcastShape([firstRange, secondRange, thirdRange], out int rows, out int cols))
            return ErrorValue.Value;

        var cells = new ScalarValue[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var firstValue = firstRange is null ? first : ValueAtBroadcastCell(firstRange, r, c);
                var secondValue = secondRange is null ? second : ValueAtBroadcastCell(secondRange, r, c);
                var thirdValue = thirdRange is null ? third : ValueAtBroadcastCell(thirdRange, r, c);
                cells[r, c] = map(firstValue, secondValue, thirdValue);
            }

        return new RangeValue(cells);
    }

    private static bool CanBroadcastToShape(RangeValue range, int rows, int cols) =>
        (range.RowCount == rows && range.ColCount == cols) || (range.RowCount == 1 && range.ColCount == 1);

    // Per-axis: an axis whose extent is 1 is held fixed (broadcast) rather than indexed. Safe for
    // every existing caller of CanBroadcastToShape (which only ever admits a range that either
    // matches the target shape exactly on both axes, or is a full 1x1 scalar), and required for
    // TryGrowBroadcastShape's row-vector x column-vector 2-D grow below, where a range can be 1 on
    // only ONE axis while still needing its other axis indexed by the running row/col.
    private static ScalarValue ValueAtBroadcastCell(RangeValue range, int row, int col) =>
        range.Cells[range.RowCount == 1 ? 0 : row, range.ColCount == 1 ? 0 : col];

    /// <summary>Whether a dimension of size <paramref name="source"/> can broadcast to <paramref name="target"/> (equal, or either side is 1).</summary>
    private static bool CanBroadcastAxis(int target, int source) => target == source || target == 1 || source == 1;

    /// <summary>
    /// Grows a running (rows, cols) broadcast shape to the bounding Max(rows)/Max(cols) across all
    /// present (non-null) ranges, matching Excel's 2-D dynamic-array broadcast rule: two ranges are
    /// compatible on an axis when their extents are equal or either is 1 -- e.g. a 2x1 column vector
    /// and a 1x2 row vector cross-broadcast into a 2x2 spilled result, rather than requiring an exact
    /// shape match or a full 1x1 scalar. This mirrors FormulaEvaluator.ControlFlow's
    /// TryExpandBroadcastShape/BroadcastElementAt (used by IF/CHOOSE) and FormulaEvaluator.Operators'
    /// ElementwiseOp (used by binary operators), which already implement this same rule; the Map*Args
    /// helpers below previously only accepted an exact shape match or a 1x1 scalar (see
    /// R62-formula-array-broadcast-6-1). Returns false when any two ranges are incompatible on the
    /// same axis -- Excel's #VALUE! for a genuine array-shape mismatch. With no ranges present at all
    /// (all null), returns true with rows=cols=1 -- callers should prefer the plain scalar path in
    /// that case rather than wrapping a trivial 1x1 RangeValue.
    /// </summary>
    private static bool TryGrowBroadcastShape(RangeValue?[] ranges, out int rows, out int cols)
    {
        rows = 1;
        cols = 1;
        foreach (var range in ranges)
        {
            if (range is null) continue;
            if (!CanBroadcastAxis(rows, range.RowCount) || !CanBroadcastAxis(cols, range.ColCount))
            {
                rows = 0;
                cols = 0;
                return false;
            }

            rows = Math.Max(rows, range.RowCount);
            cols = Math.Max(cols, range.ColCount);
        }

        return true;
    }

    private static RangeValue? ChooseBroadcastShape(params RangeValue?[] ranges)
    {
        RangeValue? fallback = null;
        foreach (var range in ranges)
        {
            if (range is null) continue;
            fallback ??= range;
            if (range.RowCount != 1 || range.ColCount != 1) return range;
        }

        return fallback;
    }

    /// <summary>
    /// 4-argument counterpart of <see cref="MapTernaryTextArgs"/> -- same Max(rows)/Max(cols)
    /// grow-broadcast rule, see that method's doc comment for rationale
    /// (R118-formula-arity3plus-cross-broadcast).
    /// </summary>
    private static ScalarValue MapQuaternaryTextArgs(
        ScalarValue first,
        ScalarValue second,
        ScalarValue third,
        ScalarValue fourth,
        Func<ScalarValue, ScalarValue, ScalarValue, ScalarValue, ScalarValue> map)
    {
        var firstRange = first as RangeValue;
        var secondRange = second as RangeValue;
        var thirdRange = third as RangeValue;
        var fourthRange = fourth as RangeValue;
        if (firstRange is null && secondRange is null && thirdRange is null && fourthRange is null)
            return map(first, second, third, fourth);
        if (!TryGrowBroadcastShape([firstRange, secondRange, thirdRange, fourthRange], out int rows, out int cols))
            return ErrorValue.Value;

        var cells = new ScalarValue[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var firstValue = firstRange is null ? first : ValueAtBroadcastCell(firstRange, r, c);
                var secondValue = secondRange is null ? second : ValueAtBroadcastCell(secondRange, r, c);
                var thirdValue = thirdRange is null ? third : ValueAtBroadcastCell(thirdRange, r, c);
                var fourthValue = fourthRange is null ? fourth : ValueAtBroadcastCell(fourthRange, r, c);
                cells[r, c] = map(firstValue, secondValue, thirdValue, fourthValue);
            }

        return new RangeValue(cells);
    }

    /// <summary>
    /// N-argument (params-list) counterpart of <see cref="MapTernaryTextArgs"/> -- same
    /// Max(rows)/Max(cols) grow-broadcast rule, see that method's doc comment for rationale
    /// (R118-formula-arity3plus-cross-broadcast). Backs ~30 financial/statistical/text functions
    /// (PMT/PV/FV/NPER/RATE/IPMT/PPMT, bond/depreciation, CEILING.MATH/FLOOR.MATH, DATEDIF/DAYS360/
    /// YEARFRAC, CONVERT, the NORM.DIST/GAMMA.DIST/WEIBULL.DIST/BINOM.DIST/POISSON.DIST-style
    /// distribution family, etc.) so a row-vector argument crossed with a column-vector argument
    /// (e.g. PMT's rate/nper) now spills an MxN matrix instead of wrongly returning #VALUE!.
    /// </summary>
    private static ScalarValue MapScalarArgs(
        IReadOnlyList<ScalarValue> args,
        Func<IReadOnlyList<ScalarValue>, ScalarValue> map)
    {
        var ranges = new RangeValue?[args.Count];
        bool anyRange = false;
        for (int i = 0; i < args.Count; i++)
        {
            ranges[i] = args[i] as RangeValue;
            anyRange |= ranges[i] is not null;
        }

        if (!anyRange) return map(args);
        if (!TryGrowBroadcastShape(ranges, out int rows, out int cols))
            return ErrorValue.Value;

        var cells = new ScalarValue[rows, cols];
        var scalarArgs = new ScalarValue[args.Count];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                for (int i = 0; i < args.Count; i++)
                    scalarArgs[i] = args[i] is RangeValue range ? ValueAtBroadcastCell(range, r, c) : args[i];
                cells[r, c] = map(scalarArgs);
            }

        return new RangeValue(cells);
    }

    /// <summary>
    /// Lookup-family alias of <see cref="MapScalarArgs"/>, kept as a distinctly-named call-site
    /// marker for VLOOKUP/HLOOKUP/MATCH/INDEX (which route their non-table array arguments --
    /// lookup_value/col_index_num/row_index_num/match_type/area_num -- through this) even though
    /// both now share the exact same grow-broadcast implementation
    /// (R118-formula-arity3plus-cross-broadcast folded the two-tier "exact-match-only" vs
    /// "grow-broadcast" split back into one, since MapScalarArgs itself now grow-broadcasts).
    /// </summary>
    private static ScalarValue MapScalarArgsGrowBroadcast(
        IReadOnlyList<ScalarValue> args,
        Func<IReadOnlyList<ScalarValue>, ScalarValue> map) => MapScalarArgs(args, map);

    /// <summary>
    /// Lookup-family alias of <see cref="MapTernaryTextArgs"/>, kept as a distinctly-named
    /// call-site marker for XLOOKUP/XMATCH (lookup_value/match_mode/search_mode) -- see
    /// <see cref="MapScalarArgsGrowBroadcast"/> above for rationale.
    /// </summary>
    private static ScalarValue MapTernaryTextArgsGrowBroadcast(
        ScalarValue first,
        ScalarValue second,
        ScalarValue third,
        Func<ScalarValue, ScalarValue, ScalarValue, ScalarValue> map) =>
        MapTernaryTextArgs(first, second, third, map);

    private static bool ContainsSurrogatePair(string text)
    {
        for (int i = 0; i + 1 < text.Length; i++)
            if (char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1]))
                return true;
        return false;
    }

    private static int TextElementIndexFromOneBasedPosition(string text, int position)
    {
        int index = 0;
        for (int current = 1; current < position && index < text.Length; current++)
            index += IsSurrogatePairAt(text, index) ? 2 : 1;

        return index;
    }

    private static int AdvanceTextElements(string text, int index, int count)
    {
        for (int taken = 0; taken < count && index < text.Length; taken++)
            index += IsSurrogatePairAt(text, index) ? 2 : 1;

        return index;
    }

    private static int CountTextElements(string text)
    {
        int count = 0;
        for (int index = 0; index < text.Length; count++)
            index += IsSurrogatePairAt(text, index) ? 2 : 1;

        return count;
    }

    private static int OneBasedTextPositionFromUtf16Index(string text, int index)
    {
        int position = 1;
        for (int i = 0; i < index && i < text.Length; position++)
            i += IsSurrogatePairAt(text, i) ? 2 : 1;

        return position;
    }

    private static bool IsSurrogatePairAt(string text, int index) =>
        index + 1 < text.Length && char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1]);

    private static int CountDbcsBytes(string text)
    {
        int bytes = 0;
        for (int index = 0; index < text.Length;)
        {
            bytes += DbcsByteWidthAt(text, index);
            index += IsSurrogatePairAt(text, index) ? 2 : 1;
        }

        return bytes;
    }

    private static int DbcsByteWidthAt(string text, int index)
    {
        // Excel's *B functions only apply DBCS 2-byte-per-character widths when the running
        // Office/Windows language is itself a DBCS language (Japanese/Chinese/Korean). Under any
        // other culture (e.g. en-US, the common default), they behave exactly like their SBCS
        // counterparts (LEN/LEFT/RIGHT/MID/...) regardless of the string's content -- see
        // ConvertDbcsWidthForCurrentCulture()/IsDbcsCulture(), the same gate BuiltInFunctions.TextAdvanced.cs
        // already uses for ASC/DBCS.
        if (!ConvertDbcsWidthForCurrentCulture())
            return IsSurrogatePairAt(text, index) ? 2 : 1;

        if (IsSurrogatePairAt(text, index)) return 2;
        return IsDbcsWide(text[index]) ? 2 : 1;
    }

    // Real Excel *B functions (LENB/LEFTB/RIGHTB/MIDB/REPLACEB/FINDB/SEARCHB) only double-count characters
    // that are genuinely double-byte under an active DBCS codepage (Shift-JIS/GBK/Big5/EUC-KR): CJK ideographs,
    // kana, hangul, and fullwidth forms. Single-byte scripts above U+00FF -- Cyrillic, Greek, Hebrew, Arabic,
    // Thai, Devanagari, Latin Extended, etc. -- are 1 byte per character, same as LEN, in every DBCS codepage.
    private static bool IsDbcsWide(char ch) =>
        (ch >= '\u1100' && ch <= '\u11ff') ||   // Hangul Jamo
        (ch >= '\u2e80' && ch <= '\ua4cf') ||   // CJK radicals/symbols, Hiragana, Katakana, Bopomofo, Hangul
                                                 // compatibility jamo, CJK strokes/compat/ext-A, Yi
        (ch >= '\uac00' && ch <= '\ud7a3') ||   // Hangul syllables
        (ch >= '\uf900' && ch <= '\ufaff') ||   // CJK compatibility ideographs
        (ch >= '\ufe30' && ch <= '\ufe4f') ||   // CJK compatibility forms
        (ch >= '\uff00' && ch <= '\uff60') ||   // Fullwidth forms (halfwidth kana \uff61-\uff9f stays 1 byte)
        (ch >= '\uffe0' && ch <= '\uffe6');     // Fullwidth signs

    private static int DbcsByteOffsetToUtf16Index(string text, int byteOffset)
    {
        int bytes = 0;
        for (int index = 0; index < text.Length;)
        {
            int width = DbcsByteWidthAt(text, index);
            if (bytes + width > byteOffset)
                return bytes == byteOffset ? index : index + (IsSurrogatePairAt(text, index) ? 2 : 1);

            bytes += width;
            index += IsSurrogatePairAt(text, index) ? 2 : 1;
        }

        return text.Length;
    }

    private static int DbcsBytePositionFromUtf16Index(string text, int utf16Index)
    {
        int bytes = 0;
        for (int index = 0; index < utf16Index && index < text.Length;)
        {
            bytes += DbcsByteWidthAt(text, index);
            index += IsSurrogatePairAt(text, index) ? 2 : 1;
        }

        return bytes + 1;
    }

    private static string SliceDbcsBytes(string text, int startByteOffset, int byteCount)
    {
        int endByteOffset = startByteOffset + byteCount;
        int start = text.Length;
        int end = text.Length;
        int bytes = 0;
        for (int index = 0; index < text.Length;)
        {
            int width = DbcsByteWidthAt(text, index);
            int nextBytes = bytes + width;
            int nextIndex = index + (IsSurrogatePairAt(text, index) ? 2 : 1);
            if (start == text.Length && bytes >= startByteOffset)
                start = index;
            if (nextBytes > endByteOffset)
            {
                end = index;
                break;
            }

            if (nextBytes <= endByteOffset)
                end = nextIndex;

            bytes = nextBytes;
            index = nextIndex;
        }

        if (startByteOffset >= bytes && start == text.Length)
            start = end = text.Length;
        if (end < start) end = start;
        return text[start..end];
    }

}
