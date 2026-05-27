using System.Globalization;

using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Phase A1 text functions: ROMAN, UNICHAR, UNICODE, NUMBERVALUE.

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
        if (codePoint >= 0xD800 && codePoint <= 0xDFFF) return ErrorValue.Value; // surrogate halves
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

        var decSep = args.Count > 1 ? args[1] : new TextValue(".");
        var grpSep = args.Count > 2 ? args[2] : new TextValue(",");
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
        var text = ToText(value).Trim();
        // Validate separators per Excel spec
        if (decSep.Length != 1) return ErrorValue.Value;
        if (grpSep.Length == 0) return ErrorValue.Value;
        if (decSep == grpSep) return ErrorValue.Value;
        if (grpSep.Contains(decSep)) return ErrorValue.Value;

        // Strip whitespace (Excel allows whitespace anywhere)
        text = text.Replace(" ", "").Replace("\t", "");
        bool accountingNegative = text.StartsWith('(') && text.EndsWith(')');
        if (accountingNegative)
            text = text[1..^1];

        // Trailing percent
        int pctCount = 0;
        while (text.EndsWith('%'))
        {
            pctCount++;
            text = text[..^1];
        }

        // Remove all group separator characters
        text = text.Replace(grpSep, string.Empty, StringComparison.Ordinal);
        // Substitute decimal separator with '.'
        if (decSep != ".") text = text.Replace(decSep, ".", StringComparison.Ordinal);

        if (text.Length == 0) return new NumberValue(0);

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return ErrorValue.Value;

        for (int i = 0; i < pctCount; i++) v /= 100.0;
        if (accountingNegative) v = -v;
        return NumberResult(v);
    }
}
