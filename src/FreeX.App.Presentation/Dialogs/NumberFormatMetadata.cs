using System.Globalization;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Dialogs;

/// <summary>The category a number format belongs to, as offered by the Format Cells "Number" tab.</summary>
public enum NumberFormatCategory
{
    General,
    Number,
    Currency,
    Accounting,
    Percentage,
    Fraction,
    Scientific,
    Special,
    Text,
    Custom
}

/// <summary>
/// The standard fraction presentations the Format Cells "Fraction" category offers: either a
/// variable denominator capped to one/two/three digits, or a fixed denominator (halves through
/// hundredths). Mirrors the desktop dialog's fraction list.
/// </summary>
public enum FractionType
{
    /// <summary>Up to one digit, variable denominator (<c># ?/?</c>).</summary>
    UpToOneDigit,

    /// <summary>Up to two digits, variable denominator (<c># ??/??</c>).</summary>
    UpToTwoDigits,

    /// <summary>Up to three digits, variable denominator (<c># ???/???</c>).</summary>
    UpToThreeDigits,

    /// <summary>As halves (<c># ?/2</c>).</summary>
    Halves,

    /// <summary>As quarters (<c># ?/4</c>).</summary>
    Quarters,

    /// <summary>As eighths (<c># ?/8</c>).</summary>
    Eighths,

    /// <summary>As sixteenths (<c># ??/16</c>).</summary>
    Sixteenths,

    /// <summary>As tenths (<c># ?/10</c>).</summary>
    Tenths,

    /// <summary>As hundredths (<c># ??/100</c>).</summary>
    Hundredths
}

/// <summary>
/// The locale-style "Special" formats the Format Cells dialog offers. Codes mirror the desktop
/// dialog's en-US special list.
/// </summary>
public enum SpecialType
{
    /// <summary>Five-digit postal code (<c>00000</c>).</summary>
    ZipCode,

    /// <summary>Nine-digit postal code with separator (<c>00000-0000</c>).</summary>
    ZipCodePlus4,

    /// <summary>US phone number (<c>[&lt;=9999999]###-####;(###) ###-####</c>).</summary>
    PhoneNumber,

    /// <summary>US social security number (<c>000-00-0000</c>).</summary>
    SocialSecurityNumber
}

/// <summary>
/// A currency entry the Currency / Accounting dropdown can offer: the symbol composed into the
/// format code plus a human-readable label for the picker.
/// </summary>
public sealed record CurrencySymbolEntry(string Symbol, string Label);

/// <summary>
/// How negative values are rendered for the Number / Currency categories. Mirrors the four
/// negative-number presentation choices the desktop format dialogs expose.
/// </summary>
public enum NegativeNumberStyle
{
    /// <summary>Leading minus sign, e.g. <c>-1234.10</c>.</summary>
    Minus,

    /// <summary>Leading minus sign, rendered red, e.g. <c>[Red]-1234.10</c>.</summary>
    RedMinus,

    /// <summary>Parentheses, e.g. <c>(1234.10)</c>.</summary>
    Parentheses,

    /// <summary>Parentheses, rendered red, e.g. <c>[Red](1234.10)</c>.</summary>
    RedParentheses
}

/// <summary>
/// Portable decomposition of a numeric format code into the discrete fields the Format Cells
/// "Number" tab edits: category, decimal places, currency symbol, negative style, and the
/// thousands separator. A <see cref="ToFormatCode"/> builder recomposes the code and
/// <see cref="BuildPreview"/> renders a sample using the shared number formatter, so the math is
/// reused rather than reinvented.
/// </summary>
public sealed record NumberFormatMetadata(
    NumberFormatCategory Category,
    int DecimalPlaces = 2,
    string? CurrencySymbol = null,
    NegativeNumberStyle NegativeStyle = NegativeNumberStyle.Minus,
    bool UseThousandsSeparator = true,
    FractionType Fraction = FractionType.UpToOneDigit,
    SpecialType Special = SpecialType.ZipCode)
{
    /// <summary>Inclusive bounds for <see cref="DecimalPlaces"/>, matching the dialog spinner range.</summary>
    public const int MinDecimalPlaces = 0;
    public const int MaxDecimalPlaces = 30;

    private const string SampleNumberValue = "1234.56";
    private const int AccountingPreviewWidth = 14;

    /// <summary>
    /// A catalog of common currency symbols a Currency / Accounting picker can offer. Each entry pairs
    /// the literal symbol composed into the format code with a readable label. The list is intentionally
    /// framework-free so any renderer can bind it to a dropdown.
    /// </summary>
    public static IReadOnlyList<CurrencySymbolEntry> CurrencySymbols { get; } =
    [
        new("$", "$ US Dollar"),
        new("€", "€ Euro"),
        new("£", "£ British Pound"),
        new("¥", "¥ Japanese Yen / Chinese Yuan"),
        new("₹", "₹ Indian Rupee"),
        new("₩", "₩ Korean Won"),
        new("₽", "₽ Russian Ruble"),
        new("₤", "₤ Turkish/Italian Lira"),
        new("R$", "R$ Brazilian Real"),
        new("CHF", "CHF Swiss Franc"),
        new("kr", "kr Scandinavian Krona/Krone"),
        new("zł", "zł Polish Złoty"),
        new("USD", "USD"),
        new("EUR", "EUR"),
        new("GBP", "GBP"),
        new("JPY", "JPY")
    ];

    /// <summary>True for categories whose decimal-places spinner is meaningful.</summary>
    public bool UsesDecimalPlaces => Category
        is NumberFormatCategory.Number
        or NumberFormatCategory.Currency
        or NumberFormatCategory.Accounting
        or NumberFormatCategory.Percentage
        or NumberFormatCategory.Scientific;

    /// <summary>True for categories that carry a currency symbol.</summary>
    public bool UsesCurrencySymbol => Category
        is NumberFormatCategory.Currency
        or NumberFormatCategory.Accounting;

    /// <summary>True for the Fraction category, whose fraction-type picker is meaningful.</summary>
    public bool UsesFractionType => Category is NumberFormatCategory.Fraction;

    /// <summary>True for the Special category, whose special-type picker is meaningful.</summary>
    public bool UsesSpecialType => Category is NumberFormatCategory.Special;

    /// <summary>True for categories whose negative-number style is configurable.</summary>
    public bool UsesNegativeStyle => Category
        is NumberFormatCategory.Number
        or NumberFormatCategory.Currency;

    /// <summary>True for categories whose thousands separator is configurable.</summary>
    public bool UsesThousandsSeparator => Category
        is NumberFormatCategory.Number
        or NumberFormatCategory.Currency;

    /// <summary>Composes the numeric format code described by these fields.</summary>
    public string ToFormatCode()
    {
        var decimals = Math.Clamp(DecimalPlaces, MinDecimalPlaces, MaxDecimalPlaces);
        return Category switch
        {
            NumberFormatCategory.General => "General",
            NumberFormatCategory.Text => "@",
            NumberFormatCategory.Number => BuildNumberFormat(decimals),
            NumberFormatCategory.Currency => BuildCurrencyFormat(decimals),
            NumberFormatCategory.Accounting => BuildAccountingFormat(decimals),
            NumberFormatCategory.Percentage => $"0{DecimalPart(decimals)}%",
            NumberFormatCategory.Fraction => FractionFormatCode(Fraction),
            NumberFormatCategory.Scientific => $"0{DecimalPart(decimals)}E+00",
            NumberFormatCategory.Special => SpecialFormatCode(Special),
            _ => "General"
        };
    }

    /// <summary>The Excel-style format code for each standard fraction option.</summary>
    public static string FractionFormatCode(FractionType type) => type switch
    {
        FractionType.UpToOneDigit => "# ?/?",
        FractionType.UpToTwoDigits => "# ??/??",
        FractionType.UpToThreeDigits => "# ???/???",
        FractionType.Halves => "# ?/2",
        FractionType.Quarters => "# ?/4",
        FractionType.Eighths => "# ?/8",
        FractionType.Sixteenths => "# ??/16",
        FractionType.Tenths => "# ?/10",
        FractionType.Hundredths => "# ??/100",
        _ => "# ?/?"
    };

    /// <summary>The format code for each locale-style special option.</summary>
    public static string SpecialFormatCode(SpecialType type) => type switch
    {
        SpecialType.ZipCode => "00000",
        SpecialType.ZipCodePlus4 => "00000-0000",
        SpecialType.PhoneNumber => "[<=9999999]###-####;(###) ###-####",
        SpecialType.SocialSecurityNumber => "000-00-0000",
        _ => "00000"
    };

    /// <summary>
    /// Renders a sample rendering of the composed format using the shared number formatter, so the
    /// preview always matches what the engine would actually display.
    /// </summary>
    public string BuildPreview()
    {
        var code = ToFormatCode();
        if (Category == NumberFormatCategory.Text)
            return NumberFormatter.Format(new TextValue("Sample"), code);

        // The Special formats are digit-layout codes; a generic decimal sample would not exercise the
        // ZIP / phone / SSN layouts, so each previews with a representative whole-number value.
        if (Category == NumberFormatCategory.Special)
            return NumberFormatter.Format(new NumberValue(SpecialSampleValue(Special)), code);

        var sample = new NumberValue(double.Parse(SampleNumberValue, CultureInfo.InvariantCulture));
        if (Category == NumberFormatCategory.Accounting)
            return NumberFormatter.Format(sample, code, AccountingPreviewWidth);

        return NumberFormatter.Format(sample, code);
    }

    /// <summary>A representative sample value for previewing each Special layout.</summary>
    private static double SpecialSampleValue(SpecialType type) => type switch
    {
        SpecialType.ZipCode => 1235,
        SpecialType.ZipCodePlus4 => 12345600,
        SpecialType.PhoneNumber => 1234567890,
        SpecialType.SocialSecurityNumber => 123456789,
        _ => 0
    };

    private string IntegerPart => UseThousandsSeparator && UsesThousandsSeparator ? "#,##0" : "0";

    private string BuildNumberFormat(int decimals)
    {
        var format = $"{IntegerPart}{DecimalPart(decimals)}";
        return ApplyNegativeStyle(format);
    }

    private string BuildCurrencyFormat(int decimals)
    {
        var format = $"{CurrencySymbolPart}{IntegerPart}{DecimalPart(decimals)}";
        return ApplyNegativeStyle(format);
    }

    private string BuildAccountingFormat(int decimals)
    {
        var symbol = CurrencySymbolPart;
        var decimalPart = DecimalPart(decimals);
        var padding = decimals > 0 ? new string('?', decimals) : string.Empty;
        var zeroPart = decimals > 0 ? $"\"-\"{padding}" : "\"-\"";
        return $"_({symbol}* #,##0{decimalPart}_);_({symbol}* (#,##0{decimalPart});_({symbol}* {zeroPart}_);_(@_)";
    }

    private string CurrencySymbolPart =>
        string.IsNullOrWhiteSpace(CurrencySymbol) ? string.Empty : CurrencySymbol.Trim();

    private string ApplyNegativeStyle(string format) => NegativeStyle switch
    {
        NegativeNumberStyle.RedMinus => $"{format};[Red]-{format}",
        NegativeNumberStyle.Parentheses => $"{format};({format})",
        NegativeNumberStyle.RedParentheses => $"{format};[Red]({format})",
        _ => format
    };

    private static string DecimalPart(int decimals) =>
        decimals > 0 ? "." + new string('0', decimals) : string.Empty;

    /// <summary>
    /// Decomposes a numeric format code back into discrete fields. Recognizes the codes this model
    /// composes (and the canonical General / Text codes); unrecognized custom codes resolve to
    /// <see cref="NumberFormatCategory.Custom"/> with best-effort decimal-place detection.
    /// </summary>
    public static NumberFormatMetadata FromFormatCode(string? formatCode)
    {
        if (string.IsNullOrWhiteSpace(formatCode))
            return new NumberFormatMetadata(NumberFormatCategory.General);

        var code = formatCode.Trim();
        var firstSection = NumberFormatSectionTokenizer.Split(code)[0];
        var decimals = DecimalPlacesIn(firstSection);
        var negativeStyle = DetectNegativeStyle(code);
        var thousands = firstSection.Contains("#,##", StringComparison.Ordinal)
            || firstSection.Contains(",0", StringComparison.Ordinal);

        if (string.Equals(code, "General", StringComparison.OrdinalIgnoreCase))
            return new NumberFormatMetadata(NumberFormatCategory.General);

        if (code == "@")
            return new NumberFormatMetadata(NumberFormatCategory.Text);

        // Special layouts match by their exact canonical code; they otherwise look like plain numbers
        // (e.g. "00000") and would be misread as Number, so resolve them up front.
        if (DetectSpecial(code) is { } special)
            return new NumberFormatMetadata(NumberFormatCategory.Special, Special: special);

        // A fraction is recognized by a "/" with question-mark placeholders on at least one side; the
        // exact fraction option is recovered where the code matches a standard one.
        if (DetectFraction(firstSection) is { } fraction)
            return new NumberFormatMetadata(NumberFormatCategory.Fraction, Fraction: fraction);

        if (firstSection.Contains("_(", StringComparison.Ordinal) && firstSection.Contains('*'))
            return new NumberFormatMetadata(
                NumberFormatCategory.Accounting,
                decimals,
                ExtractAccountingSymbol(firstSection));

        if (firstSection.Contains('%'))
            return new NumberFormatMetadata(NumberFormatCategory.Percentage, decimals);

        if (firstSection.Contains("E+", StringComparison.OrdinalIgnoreCase))
            return new NumberFormatMetadata(NumberFormatCategory.Scientific, decimals);

        if (ExtractCurrencySymbol(firstSection) is { } symbol)
            return new NumberFormatMetadata(
                NumberFormatCategory.Currency,
                decimals,
                symbol,
                negativeStyle,
                thousands);

        if (IsPlainNumber(firstSection))
            return new NumberFormatMetadata(
                NumberFormatCategory.Number,
                decimals,
                null,
                negativeStyle,
                thousands);

        return new NumberFormatMetadata(NumberFormatCategory.Custom, decimals);
    }

    private static bool IsPlainNumber(string section)
    {
        foreach (var ch in section)
        {
            if (ch is not ('0' or '#' or ',' or '.' or '-' or '(' or ')' or '[' or ']'
                or 'R' or 'e' or 'd' or ' '))
                return false;
        }

        return section.Contains('0') || section.Contains('#');
    }

    private static string? ExtractCurrencySymbol(string section)
    {
        var index = section.IndexOfAny(['0', '#']);
        if (index <= 0)
            return null;

        var prefix = section[..index].Trim();
        if (prefix.Length == 0)
            return null;

        // A leading bracketed clause (color/condition, e.g. "[Red]" or "[<=9999999]") is not a
        // currency symbol — those belong to Number/Custom codes, so decline rather than misread.
        return prefix.Contains('[') || prefix.Contains(']') ? null : prefix;
    }

    private static string ExtractAccountingSymbol(string section)
    {
        var open = section.IndexOf("_(", StringComparison.Ordinal);
        var star = section.IndexOf('*');
        if (open < 0 || star < 0 || star <= open + 2)
            return "$";

        var symbol = section[(open + 2)..star].Trim();
        return symbol.Length == 0 ? "$" : symbol;
    }

    /// <summary>
    /// Recognizes the canonical Special codes by exact match. Custom variants of the same idea fall
    /// through to <see cref="NumberFormatCategory.Custom"/> rather than being guessed at.
    /// </summary>
    private static SpecialType? DetectSpecial(string code) => code switch
    {
        "00000" => SpecialType.ZipCode,
        "00000-0000" => SpecialType.ZipCodePlus4,
        "[<=9999999]###-####;(###) ###-####" => SpecialType.PhoneNumber,
        "000-00-0000" => SpecialType.SocialSecurityNumber,
        _ => null
    };

    /// <summary>
    /// Recognizes a fraction layout and recovers the standard option where the code matches one. Codes
    /// shaped like a fraction but not in the standard set still resolve to the closest variable-width
    /// option so the category is preserved.
    /// </summary>
    private static FractionType? DetectFraction(string section)
    {
        var trimmed = section.Trim();
        switch (trimmed)
        {
            case "# ?/?": return FractionType.UpToOneDigit;
            case "# ??/??": return FractionType.UpToTwoDigits;
            case "# ???/???": return FractionType.UpToThreeDigits;
            case "# ?/2": return FractionType.Halves;
            case "# ?/4": return FractionType.Quarters;
            case "# ?/8": return FractionType.Eighths;
            case "# ??/16": return FractionType.Sixteenths;
            case "# ?/10": return FractionType.Tenths;
            case "# ??/100": return FractionType.Hundredths;
        }

        // Best-effort: any "?/?" or "?/<digits>" shape is still a fraction. Map to the variable-width
        // option whose denominator-placeholder count matches, defaulting to one digit.
        var slash = trimmed.IndexOf('/');
        if (slash <= 0 || trimmed.IndexOf('?') < 0)
            return null;

        var before = trimmed[..slash];
        if (!before.Contains('?'))
            return null;

        var denominatorDigits = 0;
        for (var i = slash + 1; i < trimmed.Length && trimmed[i] is '?'; i++)
            denominatorDigits++;

        return denominatorDigits switch
        {
            >= 3 => FractionType.UpToThreeDigits,
            2 => FractionType.UpToTwoDigits,
            _ => FractionType.UpToOneDigit
        };
    }

    private static int DecimalPlacesIn(string section)
    {
        var dotIndex = section.IndexOf('.');
        if (dotIndex < 0)
            return 0;

        var count = 0;
        for (var i = dotIndex + 1; i < section.Length && section[i] is '0' or '#' or '?'; i++)
            count++;

        return Math.Clamp(count, MinDecimalPlaces, MaxDecimalPlaces);
    }

    private static NegativeNumberStyle DetectNegativeStyle(string code)
    {
        var sections = NumberFormatSectionTokenizer.Split(code);
        if (sections.Length < 2)
            return NegativeNumberStyle.Minus;

        var negativeSection = sections[1];
        var red = negativeSection.Contains("[Red]", StringComparison.OrdinalIgnoreCase);
        var parens = negativeSection.Contains('(');
        return (red, parens) switch
        {
            (true, true) => NegativeNumberStyle.RedParentheses,
            (true, false) => NegativeNumberStyle.RedMinus,
            (false, true) => NegativeNumberStyle.Parentheses,
            _ => NegativeNumberStyle.Minus
        };
    }

}
