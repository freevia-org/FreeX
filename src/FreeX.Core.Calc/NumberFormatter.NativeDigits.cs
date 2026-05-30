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

    private static readonly string[] ChineseLowerPlaceDigits =
        ["\u96F6", "\u4E00", "\u4E8C", "\u4E09", "\u56DB", "\u4E94", "\u516D", "\u4E03", "\u516B", "\u4E5D"];

    private static readonly string[] ChineseLowerSmallUnits =
        ["", "\u5341", "\u767E", "\u5343"];

    private static readonly string[] ChineseFinancialSmallUnits =
        ["", "\u62FE", "\u4F70", "\u4EDF"];

    private static readonly string[] ChineseSimplifiedLargeUnits =
        ["", "\u4E07", "\u4EBF", "\u5146"];

    private static readonly string[] ChineseTraditionalLargeUnits =
        ["", "\u842C", "\u5104", "\u5146"];

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

    private static bool TryResolveNativeDigitMap(string format, out string[] digits)
    {
        if (!TryResolveNativeDigitDirective(format, out var kind, out var variant, out var locale))
        {
            digits = [];
            return false;
        }

        if (string.Equals(kind, "DBNum", StringComparison.OrdinalIgnoreCase))
            return TryResolveDbNumDigitMap(variant, locale, out digits);

        return TryResolveNatNumDigitMap(variant, locale, out digits);
    }

    private static bool TryResolveNativeDigitDirective(
        string format,
        out string kind,
        out int variant,
        out string? locale)
    {
        var match = NativeDigitDirectiveRegex.Match(format);
        if (!match.Success ||
            !int.TryParse(match.Groups["variant"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out variant))
        {
            kind = "";
            variant = 0;
            locale = null;
            return false;
        }

        kind = match.Groups["kind"].Value;
        locale = ResolveNativeDigitLocale(format);
        return true;
    }

    private static bool TryFormatDbNumIntegerPlaceValue(
        double value,
        string nativeDigitFormat,
        string integerFormat,
        string prefix,
        string suffix,
        out string text)
    {
        text = "";
        if (!TryResolveNativeDigitDirective(nativeDigitFormat, out var kind, out var variant, out var locale) ||
            !string.Equals(kind, "DBNum", StringComparison.OrdinalIgnoreCase) ||
            !IsChineseDbNumPlaceValueLocale(locale) ||
            variant is not (1 or 2) ||
            !IsDbNumIntegerPattern(integerFormat))
        {
            return false;
        }

        var rounded = Math.Round(Math.Abs(value), 0, MidpointRounding.AwayFromZero);
        if (double.IsNaN(rounded) || double.IsInfinity(rounded) || rounded > 999_999_999_999_999d)
            return false;

        var digits = variant == 1
            ? ChineseLowerPlaceDigits
            : IsTraditionalChineseLocale(locale)
                ? ChineseTraditionalFinancialDigits
                : ChineseSimplifiedFinancialDigits;
        var largeUnits = IsTraditionalChineseLocale(locale)
            ? ChineseTraditionalLargeUnits
            : ChineseSimplifiedLargeUnits;
        var smallUnits = variant == 1
            ? ChineseLowerSmallUnits
            : ChineseFinancialSmallUnits;

        var sign = value < 0 ? "-" : "";
        text = sign + prefix + FormatChineseDbNumInteger(
            (long)rounded,
            digits,
            smallUnits,
            largeUnits,
            omitLeadingOneBeforeTen: variant == 1) + suffix;
        return true;
    }

    private static bool IsChineseDbNumPlaceValueLocale(string? locale)
        => string.IsNullOrEmpty(locale) ||
            string.Equals(locale, "804", StringComparison.OrdinalIgnoreCase) ||
            IsTraditionalChineseLocale(locale);

    private static bool IsDbNumIntegerPattern(string format)
    {
        var hasDigitPlaceholder = false;
        foreach (var c in format)
        {
            switch (c)
            {
                case '0':
                case '#':
                    hasDigitPlaceholder = true;
                    break;
                case ',':
                case ' ':
                    break;
                default:
                    return false;
            }
        }

        return hasDigitPlaceholder;
    }

    private static string FormatChineseDbNumInteger(
        long value,
        string[] digits,
        string[] smallUnits,
        string[] largeUnits,
        bool omitLeadingOneBeforeTen)
    {
        if (value == 0)
            return digits[0];

        var groups = new List<int>();
        while (value > 0)
        {
            groups.Add((int)(value % 10_000));
            value /= 10_000;
        }

        var sb = new System.Text.StringBuilder();
        var pendingZero = false;
        for (var groupIndex = groups.Count - 1; groupIndex >= 0; groupIndex--)
        {
            var group = groups[groupIndex];
            if (group == 0)
            {
                pendingZero = sb.Length > 0;
                continue;
            }

            if (pendingZero || (sb.Length > 0 && group < 1000))
                sb.Append(digits[0]);

            sb.Append(FormatChineseDbNumGroup(group, digits, smallUnits, omitLeadingOneBeforeTen));
            if (groupIndex < largeUnits.Length)
                sb.Append(largeUnits[groupIndex]);

            pendingZero = false;
        }

        return sb.ToString();
    }

    private static string FormatChineseDbNumGroup(
        int group,
        string[] digits,
        string[] smallUnits,
        bool omitLeadingOneBeforeTen)
    {
        var sb = new System.Text.StringBuilder();
        var pendingZero = false;
        for (var place = 3; place >= 0; place--)
        {
            var factor = (int)Math.Pow(10, place);
            var digit = group / factor % 10;
            if (digit == 0)
            {
                if (sb.Length > 0 && group % factor > 0)
                    pendingZero = true;
                continue;
            }

            if (pendingZero)
            {
                sb.Append(digits[0]);
                pendingZero = false;
            }

            if (!(omitLeadingOneBeforeTen && place == 1 && digit == 1 && sb.Length == 0))
                sb.Append(digits[digit]);
            sb.Append(smallUnits[place]);
        }

        return sb.ToString();
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

        if (variant != 1 || string.IsNullOrEmpty(locale))
        {
            digits = [];
            return false;
        }

        return NatNumDigitCatalog.TryGetValue(locale, out digits!);
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
}
