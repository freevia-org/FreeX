using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue Textbefore(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        TextBeforeAfter(args, before: true);

    private static ScalarValue Textafter(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        TextBeforeAfter(args, before: false);

    private static ScalarValue TextBeforeAfter(IReadOnlyList<ScalarValue> args, bool before)
    {
        if (args[0] is ErrorValue textError) return textError;

        if (!TryTextBeforeAfterOptions(args, out var options, out var optionsError))
            return optionsError;

        if (args[0] is RangeValue textRange)
        {
            var cells = new ScalarValue[textRange.RowCount, textRange.ColCount];
            for (int r = 0; r < textRange.RowCount; r++)
                for (int c = 0; c < textRange.ColCount; c++)
                    cells[r, c] = TextBeforeAfterScalar(textRange.Cells[r, c], options, before);

            return new RangeValue(cells);
        }

        return TextBeforeAfterScalar(args[0], options, before);
    }

    private static bool TryTextBeforeAfterOptions(
        IReadOnlyList<ScalarValue> args,
        out TextExtractOptions options,
        out ScalarValue error)
    {
        options = default;
        error = ErrorValue.Value;

        if (!TryCollectTextDelimiters(args[1], allowBlankArgument: false, out var delimiters, out error))
            return false;
        if (delimiters.Count == 0) return false;

        if (!TryGetOptionalInteger(args, 2, defaultValue: 1, out int instanceNum, out error))
            return false;
        if (instanceNum == 0)
        {
            error = ErrorValue.Value;
            return false;
        }

        if (!TryGetOptionalMode(args, 3, defaultValue: 0, out int matchMode, out error))
            return false;
        if (matchMode is not (0 or 1))
        {
            error = ErrorValue.Value;
            return false;
        }

        if (!TryGetOptionalMode(args, 4, defaultValue: 0, out int matchEndMode, out error))
            return false;
        if (matchEndMode is not (0 or 1))
        {
            error = ErrorValue.Value;
            return false;
        }

        var ifNotFound = args.Count > 5 ? SingleValueOrErrorAsValue(args[5], out error) : ErrorValue.NA;
        if (ifNotFound is null) return false;

        options = new TextExtractOptions(
            delimiters,
            instanceNum,
            matchMode == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal,
            matchEndMode == 1,
            ifNotFound);
        return true;
    }

    private static ScalarValue TextBeforeAfterScalar(ScalarValue value, TextExtractOptions options, bool before)
    {
        if (value is ErrorValue e) return e;

        var text = ToText(value);
        if (text.Length == 0)
            return TextResult("");

        var textLength = ContainsSurrogatePair(text) ? CountTextElements(text) : text.Length;
        if (Math.Abs(options.InstanceNum) > textLength) return ErrorValue.Value;

        if (options.Delimiters.Any(delimiter => delimiter.Length == 0))
        {
            if (before)
                return TextResult(options.InstanceNum > 0 ? "" : text);

            return TextResult(options.InstanceNum > 0 ? text : "");
        }

        var match = FindDelimiterOccurrence(text, options);
        if (match is null)
            return options.IfNotFound;

        return before
            ? TextResult(text[..match.Value.Index])
            : TextResult(text[(match.Value.Index + match.Value.Length)..]);
    }

    private static TextDelimiterMatch? FindDelimiterOccurrence(string text, TextExtractOptions options)
    {
        var matches = FindAllDelimiterMatches(text, options.Delimiters, options.Comparison);
        if (options.MatchEnd)
            matches.Add(new TextDelimiterMatch(text.Length, 0));

        if (options.InstanceNum > 0)
        {
            return matches.Count >= options.InstanceNum ? matches[options.InstanceNum - 1] : null;
        }

        var fromEnd = -options.InstanceNum;
        return matches.Count >= fromEnd ? matches[^fromEnd] : null;
    }

    private static ScalarValue Textsplit(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var textValue = SingleValueOrErrorAsValue(args[0], out var textError);
        if (textValue is null) return textError;
        if (textValue is ErrorValue textValueError) return textValueError;

        if (!TryCollectTextDelimiters(args[1], allowBlankArgument: true, out var colDelimiters, out var error))
            return error;
        if (!TryCollectTextDelimiters(args.Count > 2 ? args[2] : BlankValue.Instance, allowBlankArgument: true, out var rowDelimiters, out error))
            return error;
        if (colDelimiters.Count == 0 && rowDelimiters.Count == 0)
            return ErrorValue.Value;
        if (colDelimiters.Any(d => d.Length == 0) || rowDelimiters.Any(d => d.Length == 0))
            return ErrorValue.Value;

        if (!TryGetOptionalBoolean(args, 3, defaultValue: false, out bool ignoreEmpty, out error))
            return error;
        if (!TryGetOptionalMode(args, 4, defaultValue: 0, out int matchMode, out error))
            return error;
        if (matchMode is not (0 or 1))
            return ErrorValue.Value;

        var padWith = args.Count > 5 && args[5] is not BlankValue
            ? SingleValueOrErrorAsValue(args[5], out error)
            : ErrorValue.NA;
        if (padWith is null) return error;

        var comparison = matchMode == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var text = ToText(textValue);
        var rowTexts = rowDelimiters.Count == 0
            ? [text]
            : SplitByAnyDelimiter(text, rowDelimiters, comparison, ignoreEmpty);

        var splitRows = new List<List<string>>(rowTexts.Count);
        var maxCols = 0;
        foreach (var rowText in rowTexts)
        {
            var cols = colDelimiters.Count == 0
                ? [rowText]
                : SplitByAnyDelimiter(rowText, colDelimiters, comparison, ignoreEmpty);
            splitRows.Add(cols);
            maxCols = Math.Max(maxCols, cols.Count);
        }

        if (splitRows.Count == 0 || maxCols == 0)
            return ErrorValue.Calc;

        var cells = new ScalarValue[splitRows.Count, maxCols];
        for (int r = 0; r < splitRows.Count; r++)
            for (int c = 0; c < maxCols; c++)
                cells[r, c] = c < splitRows[r].Count ? TextResult(splitRows[r][c]) : padWith;

        return new RangeValue(cells);
    }

    private static List<string> SplitByAnyDelimiter(
        string text,
        IReadOnlyList<string> delimiters,
        StringComparison comparison,
        bool ignoreEmpty)
    {
        var result = new List<string>();
        var pos = 0;
        while (pos <= text.Length)
        {
            var match = FindNextDelimiter(text, delimiters, comparison, pos);
            var end = match?.Index ?? text.Length;
            var token = text[pos..end];
            if (!ignoreEmpty || token.Length > 0)
                result.Add(token);

            if (match is null) break;
            pos = match.Value.Index + match.Value.Length;
        }

        return result;
    }

    private static List<TextDelimiterMatch> FindAllDelimiterMatches(
        string text,
        IReadOnlyList<string> delimiters,
        StringComparison comparison)
    {
        var matches = new List<TextDelimiterMatch>();
        var pos = 0;
        while (pos <= text.Length)
        {
            var match = FindNextDelimiter(text, delimiters, comparison, pos);
            if (match is null) break;

            matches.Add(match.Value);
            pos = match.Value.Index + Math.Max(1, match.Value.Length);
        }

        return matches;
    }

    private static TextDelimiterMatch? FindNextDelimiter(
        string text,
        IReadOnlyList<string> delimiters,
        StringComparison comparison,
        int start)
    {
        TextDelimiterMatch? best = null;
        foreach (var delimiter in delimiters)
        {
            var index = text.IndexOf(delimiter, start, comparison);
            if (index < 0) continue;

            if (best is null ||
                index < best.Value.Index ||
                (index == best.Value.Index && delimiter.Length > best.Value.Length))
            {
                best = new TextDelimiterMatch(index, delimiter.Length);
            }
        }

        return best;
    }

    private static bool TryCollectTextDelimiters(
        ScalarValue value,
        bool allowBlankArgument,
        out List<string> delimiters,
        out ScalarValue error)
    {
        delimiters = [];
        error = ErrorValue.Value;

        if (value is BlankValue && allowBlankArgument)
            return true;
        if (value is ErrorValue directError)
        {
            error = directError;
            return false;
        }

        if (value is RangeValue range)
        {
            foreach (var cell in range.Flatten())
            {
                if (cell is ErrorValue cellError)
                {
                    error = cellError;
                    return false;
                }

                delimiters.Add(ToText(cell));
            }

            return true;
        }

        delimiters.Add(ToText(value));
        return true;
    }

    private static bool TryGetOptionalInteger(
        IReadOnlyList<ScalarValue> args,
        int index,
        int defaultValue,
        out int value,
        out ScalarValue error)
    {
        value = defaultValue;
        error = ErrorValue.Value;
        if (args.Count <= index || args[index] is BlankValue) return true;
        if (!TryGetScalarControlArgument(args[index], out var scalar, out error)) return false;

        var number = ToNumber(scalar);
        if (!double.IsFinite(number) || number > int.MaxValue || number < int.MinValue)
            return false;

        value = (int)number;
        return true;
    }

    private static bool TryGetOptionalMode(
        IReadOnlyList<ScalarValue> args,
        int index,
        int defaultValue,
        out int value,
        out ScalarValue error)
    {
        value = defaultValue;
        error = ErrorValue.Value;
        if (args.Count <= index || args[index] is BlankValue) return true;
        if (!TryGetScalarControlArgument(args[index], out var scalar, out error)) return false;

        var number = ToNumber(scalar);
        if (!double.IsFinite(number) || number > int.MaxValue || number < int.MinValue)
            return false;

        value = (int)number;
        return true;
    }

    private static bool TryGetOptionalBoolean(
        IReadOnlyList<ScalarValue> args,
        int index,
        bool defaultValue,
        out bool value,
        out ScalarValue error)
    {
        value = defaultValue;
        error = ErrorValue.Value;
        if (args.Count <= index || args[index] is BlankValue) return true;
        if (!TryGetScalarControlArgument(args[index], out var scalar, out error)) return false;

        value = ToBool(scalar);
        return true;
    }

    private static ScalarValue? SingleValueOrErrorAsValue(ScalarValue value, out ScalarValue error)
    {
        error = ErrorValue.Value;
        if (value is RangeValue range)
        {
            if (range.RowCount != 1 || range.ColCount != 1)
                return null;

            return range.Cells[0, 0];
        }

        return value;
    }

    private readonly record struct TextExtractOptions(
        IReadOnlyList<string> Delimiters,
        int InstanceNum,
        StringComparison Comparison,
        bool MatchEnd,
        ScalarValue IfNotFound);

    private readonly record struct TextDelimiterMatch(int Index, int Length);
}
