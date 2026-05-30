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
