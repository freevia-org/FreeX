using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Phase A1 text functions: ARABIC, ROMAN, UNICHAR, UNICODE, NUMBERVALUE.

    private static readonly IReadOnlyDictionary<string, int> ArabicRomanRemainders = BuildArabicRemainderMap();

    private static ScalarValue Arabic(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ArabicScalar);
        return ArabicScalar(args[0]);
    }

    private static ScalarValue ArabicScalar(ScalarValue value)
    {
        if (value is ErrorValue e) return e;
        if (value is NumberValue or DateTimeValue or BoolValue) return ErrorValue.Value;

        var text = ToText(value).Trim();
        if (text.Length == 0) return new NumberValue(0);
        if (text.Length > 255) return ErrorValue.Value;

        var negative = text[0] == '-';
        if (negative)
        {
            text = text[1..].TrimStart();
            if (text.Length == 0 || text.Length > 255) return ErrorValue.Value;
        }

        if (!TryParseArabicRoman(text, out int result)) return ErrorValue.Value;
        return new NumberValue(negative ? -result : result);
    }

    private static bool TryParseArabicRoman(string text, out int result)
    {
        result = 0;
        var normalized = text.ToUpperInvariant();
        if (normalized.Any(static c => c is not ('I' or 'V' or 'X' or 'L' or 'C' or 'D' or 'M')))
            return false;

        int thousands = 0;
        while (thousands < normalized.Length && normalized[thousands] == 'M')
            thousands++;

        var remainder = normalized[thousands..];
        if (!ArabicRomanRemainders.TryGetValue(remainder, out int remainderValue)) return false;

        result = thousands * 1000 + remainderValue;
        return result <= 255000;
    }

    private static IReadOnlyDictionary<string, int> BuildArabicRemainderMap()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int n = 0; n < 1000; n++)
            for (int form = 0; form <= 4; form++)
                map.TryAdd(ToRoman(n, form), n);
        return map;
    }

    private static ScalarValue Roman(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue formError) return formError;

        var formArg = args.Count > 1 ? args[1] : new NumberValue(0);
        return MapBinaryMathArgs(args[0], formArg, RomanScalarWithForm);
    }

    private static ScalarValue RomanScalarWithForm(ScalarValue value, ScalarValue formValue)
    {
        if (!TryRomanForm(formValue, out int form)) return ErrorValue.Value;
        return RomanScalar(value, form);
    }

    private static bool TryRomanForm(ScalarValue value, out int form)
    {
        form = 0;
        if (value is BoolValue b)
        {
            form = b.Value ? 0 : 4;
            return true;
        }

        var number = ToNumber(value);
        if (!double.IsFinite(number)) return false;
        form = (int)Math.Truncate(number);
        return form is >= 0 and <= 4;
    }

    private static ScalarValue RomanScalar(ScalarValue value, int form)
    {
        if (value is ErrorValue e) return e;

        var number = ToNumber(value);
        if (!double.IsFinite(number)) return ErrorValue.Value;
        int n = (int)Math.Truncate(number);
        if (n < 0 || n > 3999) return ErrorValue.Value;
        return TextResult(ToRoman(n, form));
    }

    private static string ToRoman(int number, int form)
    {
        if (number == 0) return string.Empty;

        var tokens = RomanTokens(form);
        var sb = new System.Text.StringBuilder();
        var remaining = number;
        foreach (var (value, symbol) in tokens)
        {
            while (remaining >= value)
            {
                sb.Append(symbol);
                remaining -= value;
            }
        }

        return sb.ToString();
    }

    private static (int Value, string Symbol)[] RomanTokens(int form)
    {
        var tokens = new List<(int Value, string Symbol)>
        {
            (1000, "M"),
            (900, "CM"),
            (500, "D"),
            (400, "CD"),
            (100, "C"),
            (90, "XC"),
            (50, "L"),
            (40, "XL"),
            (10, "X"),
            (9, "IX"),
            (5, "V"),
            (4, "IV"),
            (1, "I")
        };

        if (form >= 1)
        {
            tokens.Add((950, "LM"));
            tokens.Add((450, "LD"));
            tokens.Add((95, "VC"));
            tokens.Add((45, "VL"));
        }

        if (form >= 2)
        {
            tokens.Add((990, "XM"));
            tokens.Add((490, "XD"));
            tokens.Add((99, "IC"));
            tokens.Add((49, "IL"));
        }

        if (form >= 3)
        {
            tokens.Add((995, "VM"));
            tokens.Add((495, "VD"));
        }

        if (form >= 4)
        {
            tokens.Add((999, "IM"));
            tokens.Add((499, "ID"));
        }

        return tokens
            .OrderByDescending(static token => token.Value)
            .ThenBy(static token => token.Symbol.Length)
            .ToArray();
    }

    private static ScalarValue Unichar(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, UnicharScalar);
        return UnicharScalar(args[0]);
    }

    private static ScalarValue UnicharScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Value;
        int codePoint = (int)Math.Truncate(n);
        if (codePoint <= 0 || codePoint > 0x10FFFF) return ErrorValue.Value;
        // Surrogate halves and the U+xxFFFE/U+xxFFFF noncharacters are #N/A in Excel (not #VALUE!).
        if ((codePoint >= 0xD800 && codePoint <= 0xDFFF) || (codePoint & 0xFFFE) == 0xFFFE) return ErrorValue.NA;
        return new TextValue(char.ConvertFromUtf32(codePoint));
    }

    private static ScalarValue UnicodeFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, UnicodeScalar);
        return UnicodeScalar(args[0]);
    }

    private static ScalarValue UnicodeScalar(ScalarValue value)
    {
        var text = ToText(value);
        if (text.Length == 0) return ErrorValue.Value;
        if (char.IsHighSurrogate(text[0]))
        {
            if (text.Length < 2 || !char.IsLowSurrogate(text[1])) return ErrorValue.Value;
            return new NumberValue(char.ConvertToUtf32(text[0], text[1]));
        }
        if (char.IsLowSurrogate(text[0])) return ErrorValue.Value;
        return new NumberValue(text[0]);
    }

    private static ScalarValue Numbervalue(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        var culture = ExcelCulture.Current;
        var decSep = args.Count > 1 ? args[1] : new TextValue(culture.NumberFormat.NumberDecimalSeparator);
        var grpSep = args.Count > 2 ? args[2] : new TextValue(culture.NumberFormat.NumberGroupSeparator);
        // With only decimal_separator given, a default group separator that would collide with it falls
        // back to the locale's decimal separator (NUMBERVALUE("1.5", ",") is 15 and ("1,5", ",") is 1.5).
        if (args.Count == 2 && ToText(args[1]).Length > 0 &&
            ToText(args[1])[..1] == culture.NumberFormat.NumberGroupSeparator)
            grpSep = new TextValue(culture.NumberFormat.NumberDecimalSeparator);
        return MapTernaryTextArgs(args[0], decSep, grpSep, NumbervalueScalarWithSeparators);
    }

    private static ScalarValue NumbervalueScalarWithSeparators(
        ScalarValue value,
        ScalarValue decimalSeparator,
        ScalarValue groupSeparator)
    {
        if (value is ErrorValue valueError) return valueError;
        if (decimalSeparator is ErrorValue decimalError) return decimalError;
        if (groupSeparator is ErrorValue groupError) return groupError;
        return NumbervalueScalar(value, ToText(decimalSeparator), ToText(groupSeparator));
    }

    private static ScalarValue NumbervalueScalar(ScalarValue value, string decSep, string grpSep)
    {
        // Only ASCII space/tab/CR/LF are ignorable to Excel here; other Unicode whitespace
        // (e.g. NBSP) must NOT be trimmed, or a leading/trailing NBSP would silently parse
        // instead of yielding #VALUE! as Excel does.
        var text = ToText(value).Trim(' ', '\t', '\r', '\n');
        // Excel uses only the first character when separator arguments contain more than one character.
        if (decSep.Length == 0) return ErrorValue.Value;
        if (grpSep.Length == 0) return ErrorValue.Value;
        decSep = decSep[..1];
        grpSep = grpSep[..1];
        if (decSep == grpSep) return ErrorValue.Value;
        if (grpSep.Contains(decSep)) return ErrorValue.Value;

        // Excel ignores these ASCII spacing controls in NUMBERVALUE text, but not
        // every Unicode whitespace character such as NBSP.
        text = ExcelNumberTextGrammar.NormalizeDigits(text)
            .Replace(" ", "")
            .Replace("\t", "")
            .Replace("\n", "")
            .Replace("\r", "");
        string whitespaceStripped = text;

        // A literal '.' that is neither the declared decimal_separator nor the declared
        // group_separator is not a valid character in NUMBERVALUE's input text -- e.g.
        // NUMBERVALUE("3.5","X","Y") must reject the stray '.' as #VALUE!, not silently
        // parse it as a decimal point via the later InvariantCulture double.TryParse (which
        // treats "." as a decimal point regardless of what decSep/grpSep were declared).
        // Only check when neither separator IS "." -- when either one legitimately uses '.'
        // the normal group-strip/decSep-substitution below handles it correctly.
        if (decSep != "." && grpSep != "." && text.Contains('.', StringComparison.Ordinal))
            return ErrorValue.Value;

        var decimalIndex = text.IndexOf(decSep, StringComparison.Ordinal);
        if (decimalIndex >= 0 &&
            text.IndexOf(grpSep, decimalIndex + decSep.Length, StringComparison.Ordinal) >= 0)
            return ErrorValue.Value;

        // Remove all group separator characters
        text = text.Replace(grpSep, string.Empty, StringComparison.Ordinal);
        // Substitute decimal separator with '.'
        if (decSep != ".") text = text.Replace(decSep, ".", StringComparison.Ordinal);

        // Nothing but percent signs (or nothing at all) is 0.
        if (text.All(static c => c == '%')) return new NumberValue(0);

        // Measured against Excel: a trailing sign is #VALUE! ("5-"), "$" and "(...)" are accepted
        // alongside any number of percent signs, and exponent overflow is #VALUE! rather than #NUM!.
        if (ExcelNumberTextGrammar.TryParseLenient(text, out var v))
            return double.IsFinite(v) ? NumberResult(v) : ErrorValue.Value;

        // Like VALUE, NUMBERVALUE falls back to date/time text ("1/2", "12:30", "1/1/2020") once the
        // spaces are gone; only meaningful with the default separators.
        if (decSep == "." && grpSep == "," &&
            ExcelTextDateTimeParser.TryParse(whitespaceStripped, DateTime.Today, out var parsed))
            return NumberResult(ExcelTextDateTimeParser.ToSerial(parsed, uses1904DateSystem: false));

        return ErrorValue.Value;
    }
}
