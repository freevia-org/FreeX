using System.Globalization;
using System.Text.RegularExpressions;

namespace FreeX.Core.Calc;

public static partial class NumberFormatter
{
    private static readonly Regex NativeDigitDirectiveRegex = new(
        @"\[(?<kind>DBNum|NatNum)\s*(?<variant>\d+)(?:\s*-[^\]]+)?\]",
        RegexOptions.IgnoreCase);

    private static readonly string[] FullWidthDigits =
        ["０", "１", "２", "３", "４", "５", "６", "７", "８", "９"];

    private static readonly string[] ChineseLowerDigits =
        ["〇", "一", "二", "三", "四", "五", "六", "七", "八", "九"];

    private static readonly string[] ChineseSimplifiedFinancialDigits =
        ["零", "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖"];

    private static readonly string[] ChineseTraditionalFinancialDigits =
        ["零", "壹", "貳", "參", "肆", "伍", "陸", "柒", "捌", "玖"];

    private static readonly IReadOnlyDictionary<string, string[]> NatNumDigitCatalog =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["401"] = ["٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩"],
            ["C01"] = ["٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩"],
            ["1801"] = ["٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩"],
            ["3801"] = ["٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩"],
            ["429"] = ["۰", "۱", "۲", "۳", "۴", "۵", "۶", "۷", "۸", "۹"],
            ["439"] = ["०", "१", "२", "३", "४", "५", "६", "७", "८", "९"],
            ["445"] = ["০", "১", "২", "৩", "৪", "৫", "৬", "৭", "৮", "৯"],
            ["449"] = ["౦", "౧", "౨", "౩", "౪", "౫", "౬", "౭", "౮", "౯"],
            ["44A"] = ["೦", "೧", "೨", "೩", "೪", "೫", "೬", "೭", "೮", "೯"],
            ["44E"] = ["൦", "൧", "൨", "൩", "൪", "൫", "൬", "൭", "൮", "൯"],
            ["41E"] = ["๐", "๑", "๒", "๓", "๔", "๕", "๖", "๗", "๘", "๙"],
            ["454"] = ["໐", "໑", "໒", "໓", "໔", "໕", "໖", "໗", "໘", "໙"],
            ["455"] = ["၀", "၁", "၂", "၃", "၄", "၅", "၆", "၇", "၈", "၉"],
            ["453"] = ["០", "១", "២", "៣", "៤", "៥", "៦", "៧", "៨", "៩"]
        };

    private enum CjkNativeNumberStyle
    {
        Lower,
        Financial
    }

    private enum CjkOneOmission
    {
        None,
        Always,
        ExactPowerOnly
    }

    private sealed record CjkNativeNumberSpec(
        string[] Digits,
        string ZeroMarker,
        string DecimalPoint,
        string[] SmallUnits,
        string[] LargeUnits,
        bool InsertZeros,
        CjkOneOmission OneOmission);

    private static readonly string[] NativeGeneralFormatTokens =
    [
        "General",
        "G/\u901A\u7528\u683C\u5F0F",
        "G/\u6A19\u6E96"
    ];

    private static readonly string[] ChineseLowerSmallUnits =
        ["", "\u5341", "\u767E", "\u5343"];

    private static readonly string[] ChineseSimplifiedLargeUnits =
        ["", "\u4E07", "\u4EBF", "\u5146"];

    private static readonly string[] ChineseTraditionalLargeUnits =
        ["", "\u842C", "\u5104", "\u5146"];

    private static readonly string[] JapaneseLargeUnits =
        ["", "\u4E07", "\u5104", "\u5146"];

    private static readonly string[] ChineseFinancialSmallUnits =
        ["", "\u62FE", "\u4F70", "\u4EDF"];

    private static readonly string[] JapaneseFinancialDigits =
        ["\u3007", "\u58F1", "\u5F10", "\u53C2", "\u56DB", "\u4E94", "\u516D", "\u4E03", "\u516B", "\u4E5D"];

    private static readonly string[] JapaneseFinancialSmallUnits =
        ["", "\u62FE", "\u767E", "\u9621"];

    private static readonly string[] KoreanFinancialSmallUnits =
        ["", "\u62FE", "\u767E", "\u5343"];

    private static string ApplyNativeDigitSubstitution(string text, string format)
    {
        if (string.IsNullOrEmpty(text) ||
            !TryResolveNativeDigitMap(format, out var digits))
        {
            return text;
        }

        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= '0' and <= '9')
                sb.Append(digits[c - '0']);
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool TryFormatCjkNativeNumberText(double value, string format, out string text)
    {
        text = "";
        if (!TryResolveCjkNativeNumberSpec(format, out var spec))
            return false;

        var prepared = PreserveLocaleCurrencyTokens(format, out _, out _);
        prepared = NativeDigitDirectiveRegex.Replace(prepared, "");
        prepared = NumericBracketDirectiveRegex.Replace(prepared, "");
        prepared = PreserveAccountingFillSpace(prepared);
        prepared = RemoveSpacingAndFillDirectives(prepared);

        if (!TrySplitNativeGeneralAffixes(prepared, out var prefix, out var suffix))
            return false;

        var generalText = FormatNumberGeneral(value);
        if (!TryConvertAsciiGeneralNumber(generalText, spec, out var converted))
            return false;

        text = prefix + converted + suffix;
        return true;
    }

    private static bool TryResolveCjkNativeNumberSpec(string format, out CjkNativeNumberSpec spec)
    {
        spec = null!;
        var match = NativeDigitDirectiveRegex.Match(format);
        if (!match.Success ||
            !int.TryParse(match.Groups["variant"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var variant))
        {
            return false;
        }

        var locale = ResolveNativeDigitLocale(format);
        var kind = match.Groups["kind"].Value;
        if (string.Equals(kind, "DBNum", StringComparison.OrdinalIgnoreCase))
        {
            var style = variant switch
            {
                1 => CjkNativeNumberStyle.Lower,
                2 => CjkNativeNumberStyle.Financial,
                _ => (CjkNativeNumberStyle?)null
            };

            return style is { } dbNumStyle &&
                TryCreateCjkNativeNumberSpec(dbNumStyle, locale, allowDefaultChinese: true, out spec);
        }

        var natNumStyle = variant switch
        {
            4 => CjkNativeNumberStyle.Lower,
            5 => CjkNativeNumberStyle.Financial,
            _ => (CjkNativeNumberStyle?)null
        };

        return natNumStyle is { } styleValue &&
            TryCreateCjkNativeNumberSpec(styleValue, locale, allowDefaultChinese: false, out spec);
    }

    private static bool TryCreateCjkNativeNumberSpec(
        CjkNativeNumberStyle style,
        string? locale,
        bool allowDefaultChinese,
        out CjkNativeNumberSpec spec)
    {
        spec = null!;
        if (IsJapaneseLocale(locale))
        {
            spec = style == CjkNativeNumberStyle.Lower
                ? new CjkNativeNumberSpec(
                    ChineseLowerDigits,
                    "\u3007",
                    "\u70B9",
                    ChineseLowerSmallUnits,
                    JapaneseLargeUnits,
                    InsertZeros: false,
                    OneOmission: CjkOneOmission.Always)
                : new CjkNativeNumberSpec(
                    JapaneseFinancialDigits,
                    "\u3007",
                    "\u70B9",
                    JapaneseFinancialSmallUnits,
                    ChineseTraditionalLargeUnits,
                    InsertZeros: false,
                    OneOmission: CjkOneOmission.ExactPowerOnly);
            return true;
        }

        if (IsKoreanLocale(locale))
        {
            spec = style == CjkNativeNumberStyle.Lower
                ? new CjkNativeNumberSpec(
                    ChineseLowerDigits,
                    "\u3007",
                    "\u70B9",
                    ChineseLowerSmallUnits,
                    ChineseTraditionalLargeUnits,
                    InsertZeros: false,
                    OneOmission: CjkOneOmission.None)
                : new CjkNativeNumberSpec(
                    ChineseTraditionalFinancialDigits,
                    "\u96F6",
                    "\u70B9",
                    KoreanFinancialSmallUnits,
                    ChineseTraditionalLargeUnits,
                    InsertZeros: false,
                    OneOmission: CjkOneOmission.None);
            return true;
        }

        if (!allowDefaultChinese && string.IsNullOrEmpty(locale))
            return false;

        if (!allowDefaultChinese && !IsChineseLocale(locale))
            return false;

        var traditional = IsTraditionalChineseLocale(locale);
        spec = style == CjkNativeNumberStyle.Lower
            ? new CjkNativeNumberSpec(
                ChineseLowerDigits,
                "\u96F6",
                "\u70B9",
                ChineseLowerSmallUnits,
                traditional ? ChineseTraditionalLargeUnits : ChineseSimplifiedLargeUnits,
                InsertZeros: true,
                OneOmission: CjkOneOmission.None)
            : new CjkNativeNumberSpec(
                traditional ? ChineseTraditionalFinancialDigits : ChineseSimplifiedFinancialDigits,
                "\u96F6",
                "\u70B9",
                ChineseFinancialSmallUnits,
                traditional ? ChineseTraditionalLargeUnits : ChineseSimplifiedLargeUnits,
                InsertZeros: true,
                OneOmission: CjkOneOmission.None);
        return true;
    }

    private static bool TrySplitNativeGeneralAffixes(string format, out string prefix, out string suffix)
    {
        var visibleFormat = UnquoteNativeNumberFormatText(format).Trim();
        prefix = "";
        suffix = "";

        if (visibleFormat.IndexOfAny(['0', '#', '?']) >= 0)
            return false;

        if (visibleFormat.Length == 0)
            return true;

        foreach (var token in NativeGeneralFormatTokens)
        {
            var index = visibleFormat.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            prefix = visibleFormat[..index];
            suffix = visibleFormat[(index + token.Length)..];
            return true;
        }

        return false;
    }

    private static string UnquoteNativeNumberFormatText(string format)
    {
        var sb = new System.Text.StringBuilder(format.Length);
        bool inQuote = false;

        for (int i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && c == '\\' && i + 1 < format.Length)
            {
                sb.Append(format[++i]);
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool TryConvertAsciiGeneralNumber(
        string text,
        CjkNativeNumberSpec spec,
        out string converted)
    {
        converted = "";
        if (string.IsNullOrEmpty(text) ||
            text.IndexOf('E') >= 0 ||
            text.IndexOf('e') >= 0)
        {
            return false;
        }

        var sign = "";
        var start = 0;
        if (text[0] is '-' or '+')
        {
            sign = text[0] == '-' ? "-" : "";
            start = 1;
        }

        var numeric = text[start..];
        var decimalIndex = numeric.IndexOf('.');
        var integerDigits = decimalIndex >= 0 ? numeric[..decimalIndex] : numeric;
        var fractionalDigits = decimalIndex >= 0 ? numeric[(decimalIndex + 1)..] : "";

        if (integerDigits.Length == 0 ||
            !AllAsciiDigits(integerDigits) ||
            (fractionalDigits.Length > 0 && !AllAsciiDigits(fractionalDigits)))
        {
            return false;
        }

        if (!TryFormatCjkInteger(integerDigits, spec, out var integerText))
            return false;

        var sb = new System.Text.StringBuilder(sign.Length + integerText.Length + fractionalDigits.Length + 1);
        sb.Append(sign);
        sb.Append(integerText);

        if (fractionalDigits.Length > 0)
        {
            sb.Append(spec.DecimalPoint);
            foreach (var digit in fractionalDigits)
                sb.Append(spec.Digits[digit - '0']);
        }

        converted = sb.ToString();
        return true;
    }

    private static bool TryFormatCjkInteger(
        string integerDigits,
        CjkNativeNumberSpec spec,
        out string text)
    {
        text = "";
        var firstSignificant = 0;
        while (firstSignificant < integerDigits.Length && integerDigits[firstSignificant] == '0')
            firstSignificant++;

        if (firstSignificant == integerDigits.Length)
        {
            text = spec.Digits[0];
            return true;
        }

        var trimmed = integerDigits[firstSignificant..];
        var groupCount = (trimmed.Length + 3) / 4;
        if (groupCount > spec.LargeUnits.Length)
            return false;

        var sb = new System.Text.StringBuilder(trimmed.Length * 2);
        var skippedZeroGroup = false;
        var firstGroupLength = trimmed.Length % 4;
        if (firstGroupLength == 0)
            firstGroupLength = 4;
        var offset = 0;

        for (int groupPosition = 0; groupPosition < groupCount; groupPosition++)
        {
            var groupLength = groupPosition == 0 ? firstGroupLength : 4;
            var groupText = trimmed.Substring(offset, groupLength);
            var groupValue = int.Parse(groupText, CultureInfo.InvariantCulture);
            var largeUnitIndex = groupCount - groupPosition - 1;
            offset += groupLength;

            if (groupValue == 0)
            {
                if (sb.Length > 0)
                    skippedZeroGroup = true;
                continue;
            }

            if (spec.InsertZeros &&
                sb.Length > 0 &&
                (skippedZeroGroup || groupValue < 1000))
            {
                AppendCjkZero(sb, spec.ZeroMarker);
            }

            sb.Append(FormatCjkGroup(groupValue, spec));
            sb.Append(spec.LargeUnits[largeUnitIndex]);
            skippedZeroGroup = false;
        }

        text = sb.ToString();
        return true;
    }

    private static string FormatCjkGroup(int groupValue, CjkNativeNumberSpec spec)
    {
        var sb = new System.Text.StringBuilder(8);
        var pendingZero = false;
        ReadOnlySpan<int> divisors = [1000, 100, 10, 1];

        for (int i = 0; i < divisors.Length; i++)
        {
            var divisor = divisors[i];
            var digit = groupValue / divisor % 10;
            var smallUnitIndex = divisors.Length - i - 1;

            if (digit == 0)
            {
                if (spec.InsertZeros && sb.Length > 0 && groupValue % divisor != 0)
                    pendingZero = true;
                continue;
            }

            if (pendingZero)
            {
                AppendCjkZero(sb, spec.ZeroMarker);
                pendingZero = false;
            }

            if (!ShouldOmitOneBeforeSmallUnit(groupValue, divisor, digit, smallUnitIndex, spec.OneOmission))
                sb.Append(spec.Digits[digit]);

            if (smallUnitIndex > 0)
                sb.Append(spec.SmallUnits[smallUnitIndex]);
        }

        return sb.ToString();
    }

    private static bool ShouldOmitOneBeforeSmallUnit(
        int groupValue,
        int divisor,
        int digit,
        int smallUnitIndex,
        CjkOneOmission oneOmission)
    {
        if (digit != 1 || smallUnitIndex == 0)
            return false;

        return oneOmission switch
        {
            CjkOneOmission.Always => true,
            CjkOneOmission.ExactPowerOnly => groupValue == divisor,
            _ => false
        };
    }

    private static void AppendCjkZero(System.Text.StringBuilder sb, string zeroMarker)
    {
        if (!EndsWith(sb, zeroMarker))
            sb.Append(zeroMarker);
    }

    private static bool EndsWith(System.Text.StringBuilder sb, string value)
    {
        if (value.Length > sb.Length)
            return false;

        var start = sb.Length - value.Length;
        for (int i = 0; i < value.Length; i++)
        {
            if (sb[start + i] != value[i])
                return false;
        }

        return true;
    }

    private static bool AllAsciiDigits(string text)
    {
        foreach (var c in text)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return true;
    }

    private static bool TryResolveNativeDigitMap(string format, out string[] digits)
    {
        var match = NativeDigitDirectiveRegex.Match(format);
        if (!match.Success ||
            !int.TryParse(match.Groups["variant"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var variant))
        {
            digits = [];
            return false;
        }

        var locale = ResolveNativeDigitLocale(format);
        var kind = match.Groups["kind"].Value;
        if (string.Equals(kind, "DBNum", StringComparison.OrdinalIgnoreCase))
            return TryResolveDbNumDigitMap(variant, locale, out digits);

        return TryResolveNatNumDigitMap(variant, locale, out digits);
    }

    private static bool TryResolveDbNumDigitMap(int variant, string? locale, out string[] digits)
    {
        digits = variant switch
        {
            1 => ChineseLowerDigits,
            2 => IsTraditionalChineseLocale(locale)
                ? ChineseTraditionalFinancialDigits
                : ChineseSimplifiedFinancialDigits,
            3 => FullWidthDigits,
            _ => []
        };

        return digits.Length > 0;
    }

    private static bool TryResolveNatNumDigitMap(int variant, string? locale, out string[] digits)
    {
        if (variant == 3)
        {
            digits = FullWidthDigits;
            return true;
        }

        if (variant is 1 or 2 &&
            TryResolveCjkNatNumDigitMap(variant, locale, out digits))
        {
            return true;
        }

        if (variant != 1 || string.IsNullOrEmpty(locale))
        {
            digits = [];
            return false;
        }

        return NatNumDigitCatalog.TryGetValue(locale, out digits!);
    }

    private static bool TryResolveCjkNatNumDigitMap(int variant, string? locale, out string[] digits)
    {
        digits = [];
        if (string.IsNullOrEmpty(locale) || !IsCjkNativeLocale(locale))
            return false;

        digits = variant switch
        {
            1 => ChineseLowerDigits,
            2 => IsJapaneseLocale(locale)
                ? JapaneseFinancialDigits
                : IsTraditionalChineseLocale(locale) || IsKoreanLocale(locale)
                    ? ChineseTraditionalFinancialDigits
                    : ChineseSimplifiedFinancialDigits,
            _ => []
        };

        return digits.Length > 0;
    }

    private static string? ResolveNativeDigitLocale(string format)
    {
        bool inQuote = false;
        for (int i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (inQuote || c != '[' || i + 2 >= format.Length || format[i + 1] != '$')
                continue;

            int close = format.IndexOf(']', i + 2);
            if (close <= i)
                continue;

            var token = format[(i + 2)..close];
            var separator = token.LastIndexOf('-');
            if (separator < 0 || separator == token.Length - 1)
                continue;

            return NormalizeNativeDigitLocale(token[(separator + 1)..]);
        }

        return null;
    }

    private static string NormalizeNativeDigitLocale(string localeToken)
    {
        var normalized = localeToken.Trim().TrimStart('0').ToUpperInvariant();
        if (normalized.Length == 0)
            return "0";

        return normalized switch
        {
            "ZH-CN" or "ZH-SG" => "804",
            "ZH-TW" => "404",
            "ZH-HK" => "C04",
            "JA-JP" => "411",
            "KO-KR" => "412",
            "HI-IN" => "439",
            "BN-IN" => "445",
            "TE-IN" => "449",
            "KN-IN" => "44A",
            "ML-IN" => "44E",
            "TH-TH" => "41E",
            "LO-LA" => "454",
            "MY-MM" => "455",
            "KM-KH" => "453",
            "AR-SA" => "401",
            "AR-EG" => "C01",
            "FA-IR" => "429",
            _ => normalized
        };
    }

    private static bool IsTraditionalChineseLocale(string? locale)
        => string.Equals(locale, "404", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(locale, "C04", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(locale, "1004", StringComparison.OrdinalIgnoreCase);

    private static bool IsChineseLocale(string? locale)
        => string.IsNullOrEmpty(locale) ||
            string.Equals(locale, "804", StringComparison.OrdinalIgnoreCase) ||
            IsTraditionalChineseLocale(locale);

    private static bool IsJapaneseLocale(string? locale)
        => string.Equals(locale, "411", StringComparison.OrdinalIgnoreCase);

    private static bool IsKoreanLocale(string? locale)
        => string.Equals(locale, "412", StringComparison.OrdinalIgnoreCase);

    private static bool IsCjkNativeLocale(string? locale)
        => IsChineseLocale(locale) ||
            IsJapaneseLocale(locale) ||
            IsKoreanLocale(locale);
}
