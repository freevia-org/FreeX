using System.Globalization;
using System.Text.RegularExpressions;

namespace FreeX.Core.Formula;

public static partial class NumberFormatter
{
    private static readonly Regex FractionQuotedTextRegex = new("\"[^\"]*\"");
    private static readonly Regex FixedDenominatorRegex = new(@"^/(\d+)");
    private static readonly Regex FractionDenominatorPlaceholderRegex = new(@"/(\?+)");
    private static readonly Regex VariableFractionPlaceholderRegex = new(@"(?<numerator>\?+)/(?<denominator>\?+)");
    private static readonly Regex FixedFractionNumeratorPlaceholderRegex = new(@"(?<numerator>\?+)$");
    private static readonly Regex FixedDenominatorFractionFormatRegex = new(@"\?+/\d+");
    private static readonly Regex ScientificFormatRegex = new(@"E[+-]0+", RegexOptions.IgnoreCase);

    private static bool IsSimpleFractionFormat(string format)
    {
        if (format.IndexOf('?') < 0)
            return false;

        var stripped = FractionQuotedTextRegex.Replace(format, "");
        return stripped.Contains("?/?", StringComparison.Ordinal) ||
               stripped.Contains("?/??", StringComparison.Ordinal) ||
               stripped.Contains("??/??", StringComparison.Ordinal) ||
               FixedDenominatorFractionFormatRegex.IsMatch(stripped);
    }

    private static string FormatSimpleFraction(double value, string format)
    {
        var (prefix, numericFormat, suffix) = ExtractNumericAffixes(format);
        var stripped = FractionQuotedTextRegex.Replace(numericFormat, "");
        int? fixedDenominator = null;
        var fixedDenominatorMatch = FixedDenominatorRegex.Match(suffix);
        if (fixedDenominatorMatch.Success &&
            int.TryParse(fixedDenominatorMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDenominator) &&
            parsedDenominator > 0)
        {
            fixedDenominator = parsedDenominator;
            suffix = suffix[fixedDenominatorMatch.Length..];
        }

        var denominatorPattern = FractionDenominatorPlaceholderRegex.Match(stripped);
        int denominatorPlaceholderWidth = denominatorPattern.Success ? denominatorPattern.Groups[1].Value.Length : 1;
        int maxDenominator = (int)Math.Pow(10, denominatorPlaceholderWidth) - 1;
        var (numeratorWidth, denominatorWidth) = GetFractionPlaceholderWidths(stripped, fixedDenominator);

        double absValue = Math.Abs(value);
        // '0' (always-show) and '#' (suppress-if-zero) are both "there is a whole-number
        // section", but they render a zero whole part differently: "0 ?/?" on 0.5 must show
        // the "0" digit, while "# ?/?" on 0.5 suppresses it (leaving only the separator space).
        bool hasZeroWholePlaceholder = stripped.Contains('0');
        bool hasWholeSection = stripped.Contains('#') || hasZeroWholePlaceholder;
        int whole = hasWholeSection ? (int)Math.Floor(absValue) : 0;
        double fractional = absValue - whole;
        // A ',' grouping separator in the integer section (e.g. "#,##0 ?/?") applies to the
        // whole-number part the same way it would for a plain number format.
        bool useThousandsGrouping = stripped.Contains(',');
        string FormatWhole(int w) => useThousandsGrouping
            ? w.ToString("N0", CultureInfo.InvariantCulture)
            : w.ToString(CultureInfo.InvariantCulture);

        var (numerator, denominator) = fixedDenominator is { } denominatorValue
            ? ((int)Math.Round(fractional * denominatorValue, MidpointRounding.AwayFromZero), denominatorValue)
            : ApproximateFraction(fractional, maxDenominator);
        // When numerator equals denominator (e.g. 8/8 = 1), promote to the whole-number
        // section only if the format HAS a whole-number section.  For pure-fraction formats
        // like "?/8" there is nowhere to show the incremented whole, so keep "8/8" as-is.
        if (numerator == denominator && hasWholeSection)
        {
            whole++;
            numerator = 0;
        }

        var sign = value < 0 ? "-" : "";
        if (numerator == 0)
        {
            if (!hasWholeSection)
            {
                // Pure fraction format with no whole-number section (e.g. "?/8" or "?/?").
                // Value is 0 or rounds to 0 — still render the fraction (e.g. "0/8" or
                // "0/1") so the denominator stays visible, matching Excel's display.
                string numStr = FormatFractionPart(0, numeratorWidth, padLeft: true);
                string denStr = FormatFractionPart(denominator, denominatorWidth, padLeft: false);
                return prefix + sign + numStr + "/" + denStr + suffix;
            }

            string wholeText = whole == 0 ? "0" : FormatWhole(whole);
            // Excel pads the fraction field with spaces to preserve column alignment.
            // Only applies when the format has an explicit whole-number section (# or 0 before the fraction).
            // Padding: one separator space + spaces(numeratorWidth) + "/" + spaces(denominatorWidth).
            // E.g. "# ?/?" with value 2 → "2    " (2 + space + 1 space + "/" + 1 space).
            if (hasWholeSection && numeratorWidth > 0 && denominatorWidth > 0)
            {
                // Excel replaces the entire fraction part (including the "/" character) with
                // spaces when the fractional value is zero, so columns stay aligned.
                // Width = 1 (separator) + numeratorWidth + 1 (slash→space) + denominatorWidth.
                string padding = new string(' ', 1 + numeratorWidth + 1 + denominatorWidth);
                return prefix + sign + wholeText + padding + suffix;
            }
            return prefix + sign + wholeText + suffix;
        }

        string fraction = FormatFractionPart(numerator, numeratorWidth, padLeft: true) + "/" +
                          FormatFractionPart(denominator, denominatorWidth, padLeft: false);
        // When there is a whole-number section (# or 0) but the whole part is 0, Excel
        // suppresses the whole digit (# prints nothing for zero) but keeps the separator
        // space that sits between the whole and the fraction.  Include that one space so
        // the fraction aligns in the same column as non-zero whole values.
        string number = whole > 0
            ? sign + FormatWhole(whole) + " " + fraction
            : hasWholeSection
                ? sign + (hasZeroWholePlaceholder ? "0" : "") + " " + fraction
                : sign + fraction;
        return prefix + number + suffix;
    }

    private static (int NumeratorWidth, int DenominatorWidth) GetFractionPlaceholderWidths(
        string strippedFormat,
        int? fixedDenominator)
    {
        var variableMatch = VariableFractionPlaceholderRegex.Match(strippedFormat);
        if (variableMatch.Success)
        {
            return (
                variableMatch.Groups["numerator"].Value.Length,
                variableMatch.Groups["denominator"].Value.Length);
        }

        var fixedMatch = FixedFractionNumeratorPlaceholderRegex.Match(strippedFormat);
        return (
            fixedMatch.Success ? fixedMatch.Groups["numerator"].Value.Length : 0,
            fixedDenominator?.ToString(CultureInfo.InvariantCulture).Length ?? 0);
    }

    private static string FormatFractionPart(int value, int width, bool padLeft)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        if (width <= 1)
            return text;

        return padLeft
            ? text.PadLeft(width)
            : text.PadRight(width);
    }

    private static (int Numerator, int Denominator) ApproximateFraction(double value, int maxDenominator)
    {
        int bestNumerator = 0;
        int bestDenominator = 1;
        double bestError = double.MaxValue;

        for (int denominator = 1; denominator <= maxDenominator; denominator++)
        {
            int numerator = (int)Math.Round(value * denominator, MidpointRounding.AwayFromZero);
            double error = Math.Abs(value - numerator / (double)denominator);
            if (error < bestError)
            {
                bestError = error;
                bestNumerator = numerator;
                bestDenominator = denominator;
            }
        }

        int gcd = GreatestCommonDivisor(bestNumerator, bestDenominator);
        return (bestNumerator / gcd, bestDenominator / gcd);
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            int t = b;
            b = a % b;
            a = t;
        }
        return a == 0 ? 1 : a;
    }

    // Matches engineering notation: digit-placeholders before E include more than one leading # then
    // a 0 (e.g. ##0.0E+0, #0.0E+0) indicating the mantissa should span 1–3 digits so the exponent
    // is always a multiple of 3.  Standard scientific (0.00E+00, 0.0E+00) does NOT match because it
    // has only "0" (or "0.0" etc.) before the E with no leading #-group that forces exponent alignment.
    private static readonly Regex EngineeringFormatRegex =
        new(@"#+0[^E]*E[+-]0*", RegexOptions.IgnoreCase);

    private static bool IsScientificFormat(string format)
    {
        if (format.IndexOfAny(['e', 'E']) < 0)
            return false;

        var stripped = FractionQuotedTextRegex.Replace(format, "");
        return ScientificFormatRegex.IsMatch(stripped);
    }

    private static bool IsEngineeringFormat(string format)
    {
        var stripped = FractionQuotedTextRegex.Replace(format, "");
        return EngineeringFormatRegex.IsMatch(stripped);
    }

    private static string FormatScientific(double value, string format, IFormatProvider formatProvider)
    {
        if (IsEngineeringFormat(format))
            return FormatEngineering(value, format, formatProvider);

        var (prefix, numericFormat, suffix) = ExtractNumericAffixes(format);
        var stripped = FractionQuotedTextRegex.Replace(numericFormat, "");
        try
        {
            return prefix + value.ToString(stripped, formatProvider) + suffix;
        }
        catch
        {
            return prefix + value.ToString(formatProvider) + suffix;
        }
    }

    /// <summary>
    /// Formats a number using Excel engineering notation (##0.0E+0 style) where the
    /// exponent is always a multiple of 3 (matching the SI prefix boundaries) and the
    /// mantissa lies in [1, 1000) (or [0.001, 1) for sub-unity values).
    ///
    /// Excel's rules:
    ///   • The exponent is the largest multiple of 3 that is ≤ floor(log10(|value|)).
    ///   • The mantissa width before the decimal is determined by the count of digit-
    ///     placeholders before the E in the format (e.g. "##0" → up to 3 digits, "##00" →
    ///     up to 4 digits). For the canonical ##0 pattern the mantissa is 1–3 significant
    ///     digits with the exponent snapped to ±0, ±3, ±6, …
    ///   • The decimal-place count after "." in the format governs precision.
    ///   • For value == 0 the exponent is 0 and the mantissa is shown as "0.0…" (or similar).
    /// </summary>
    private static string FormatEngineering(double value, string format, IFormatProvider formatProvider)
    {
        var (prefix, numericFormat, suffix) = ExtractNumericAffixes(format);
        var stripped = FractionQuotedTextRegex.Replace(numericFormat, "");

        // Locate the E marker and split the format into mantissa and exponent parts.
        int eIndex = stripped.IndexOfAny(['e', 'E']);
        if (eIndex < 0)
            return prefix + value.ToString(stripped, formatProvider) + suffix;

        string mantissaFmt = stripped[..eIndex];
        string exponentFmt = stripped[eIndex..]; // "E+0", "E+00", "E+0", etc.

        // Count decimal places in the mantissa format (after ".").
        int dotIndex = mantissaFmt.IndexOf('.');
        int decimalPlaces = dotIndex >= 0 ? mantissaFmt.Length - dotIndex - 1 : 0;

        // Count digit-placeholders before the decimal (determines mantissa integer-digit width).
        string intPart = dotIndex >= 0 ? mantissaFmt[..dotIndex] : mantissaFmt;
        int intWidth = intPart.Count(static c => c is '0' or '#');

        // Determine exponent grouping: count the zeros in the exponent format.
        // "E+0" → group 1 (but snapped to 3 for engineering). For the ##0 pattern always snap to 3.
        // Excel always uses groups of 3 when there is a leading # block before the 0.
        const int exponentGroup = 3;

        bool negative = value < 0;
        double absValue = Math.Abs(value);

        int exponent;
        double mantissa;

        if (absValue == 0)
        {
            exponent = 0;
            mantissa = 0;
        }
        else
        {
            // Raw exponent (floor(log10(absValue)))
            int rawExp = (int)Math.Floor(Math.Log10(absValue));
            // Snap down to the nearest multiple of exponentGroup
            exponent = (int)Math.Floor((double)rawExp / exponentGroup) * exponentGroup;
            mantissa = absValue / Math.Pow(10, exponent);
        }

        // Round the mantissa to the required decimal places.
        mantissa = Math.Round(mantissa, decimalPlaces, MidpointRounding.AwayFromZero);

        // After rounding, the mantissa might overflow the integer width (e.g. 999.95 → 1000.0
        // when rounded to 1 decimal place). In that case bump the exponent.
        if (mantissa >= Math.Pow(10, exponentGroup))
        {
            exponent += exponentGroup;
            mantissa /= Math.Pow(10, exponentGroup);
            mantissa = Math.Round(mantissa, decimalPlaces, MidpointRounding.AwayFromZero);
        }

        // Format the mantissa using the decimal-place count.
        // For value == 0, Excel fills every digit placeholder (including # positions) with "0",
        // producing e.g. "000.0E+0" for "##0.0E+0".  Replicate that by using the full intWidth
        // as minimum padding when the mantissa is exactly zero.
        string mantissaFmtSpec = absValue == 0
            ? new string('0', intWidth) + (decimalPlaces > 0 ? "." + new string('0', decimalPlaces) : "")
            : "F" + decimalPlaces.ToString(CultureInfo.InvariantCulture);
        string mantissaStr = absValue == 0
            ? mantissa.ToString(mantissaFmtSpec, formatProvider)
            : mantissa.ToString("F" + decimalPlaces.ToString(CultureInfo.InvariantCulture),
                formatProvider);

        // Format the exponent part to match Excel's sign and padding.
        // Excel uses E+3, E-3 etc. (always sign, minimal digits unless padded).
        bool forcePositive = exponentFmt.Contains('+', StringComparison.Ordinal);
        int expZeroWidth = exponentFmt.Count(static c => c == '0');

        string sign = negative ? "-" : "";
        string expSign = exponent >= 0 ? (forcePositive ? "+" : "") : "-";
        string expAbs = Math.Abs(exponent).ToString(CultureInfo.InvariantCulture)
                                          .PadLeft(expZeroWidth, '0');
        char eLetter = exponentFmt[0]; // preserve original E or e

        return prefix + sign + mantissaStr + eLetter + expSign + expAbs + suffix;
    }
}
