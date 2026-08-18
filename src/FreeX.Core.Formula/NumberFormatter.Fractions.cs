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

    /// <summary>
    /// Formats a single-section fraction format, or returns false so the caller falls through to
    /// the ordinary numeric path.
    /// </summary>
    /// <remarks>
    /// Lives here, behind a one-character gate, deliberately. Fraction formats have to have their
    /// "_x"/"*x" spacing and fill directives stripped before <see cref="FormatSimpleFraction"/>
    /// sees them -- otherwise ExtractNumericAffixes treats the directive and the character it
    /// reserves as ordinary literal text and renders them visibly, e.g. "_(2 1/2_)" where Excel
    /// shows invisible padding. Doing that stripping inline in FormatNumber meant every plain
    /// numeric cell -- the hot path, and the overwhelming majority of cells -- paid for two string
    /// transforms before reaching its own fast path. A fraction format must contain '?', so the
    /// IndexOf below rejects the hot case without allocating anything.
    /// </remarks>
    private static bool TryFormatSingleSectionFraction(
        double value,
        string effectiveFormat,
        string? colorHex,
        int? targetWidthCharacters,
        out FormatResult result)
    {
        result = default;

        if (effectiveFormat.IndexOf('?') < 0)
            return false;

        var fractionFormat = RemoveSpacingAndFillDirectives(PreserveAccountingFillSpace(effectiveFormat));
        if (!IsSimpleFractionFormat(fractionFormat))
            return false;

        var text = ApplyNativeDigitSubstitution(FormatSimpleFraction(value, fractionFormat), effectiveFormat);
        result = new FormatResult(ApplyAccountingTargetWidth(text, effectiveFormat, targetWidthCharacters), colorHex);
        return true;
    }

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
            // The whole part (if any) is the only remaining magnitude once the fraction has
            // rounded away to nothing (numerator == 0). When that whole part is also 0 -- e.g.
            // a tiny negative like -0.001 that rounds down to nothing -- the FULL displayed
            // value is zero, and Excel never shows a sign on a displayed zero (mirroring the
            // IsNegativeZeroRepresentation/IsAllZeroText guard NumberFormatter.cs already
            // applies for plain numeric formats). Only suppress the sign in that all-zero
            // case; a genuine non-zero whole part (e.g. -2 with "# ?/?") still shows its "-".
            var displaySign = whole == 0 ? "" : sign;

            if (!hasWholeSection)
            {
                // Pure fraction format with no whole-number section (e.g. "?/8" or "?/?").
                // Value is 0 or rounds to 0 — still render the fraction (e.g. "0/8" or
                // "0/1") so the denominator stays visible, matching Excel's display.
                string numStr = FormatFractionPart(0, numeratorWidth, padLeft: true);
                string denStr = FormatFractionPart(denominator, denominatorWidth, padLeft: false);
                return prefix + displaySign + numStr + "/" + denStr + suffix;
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
                return prefix + displaySign + wholeText + padding + suffix;
            }
            return prefix + displaySign + wholeText + suffix;
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
        if (maxDenominator < 1) maxDenominator = 1;
        if (!double.IsFinite(value) || value <= 0) return (0, 1);

        // Continued-fraction / Stern-Brocot best-rational-approximation search bounded by
        // maxDenominator. This finds the same closest p/q (q <= maxDenominator) that a
        // brute-force scan over every candidate denominator would, but in O(number of
        // continued-fraction terms) rather than O(maxDenominator) — essential for wide "?"
        // denominator placeholders (e.g. "?????????/?????????" => maxDenominator ~ 10^9),
        // which would otherwise hang the evaluator for seconds at a time.
        long hPrev2 = 0, kPrev2 = 1;
        long hPrev1 = 1, kPrev1 = 0;
        long bestNumerator = 0, bestDenominator = 1;
        double x = value;

        for (int i = 0; i < 64; i++)
        {
            // Cap the partial quotient before it's used in arithmetic: once kPrev1 >= 1 (true
            // from the second iteration on), any term this large is already guaranteed to push
            // k past maxDenominator, so clamping avoids flooring a huge/overflowing double into
            // a long while still detecting the overshoot correctly below.
            long a = x > maxDenominator + 2 ? maxDenominator + 2 : (long)Math.Floor(x);
            long h = a * hPrev1 + hPrev2;
            long k = a * kPrev1 + kPrev2;

            if (k > maxDenominator || k <= 0)
            {
                // The full convergent overshoots the bound. Fall back to the best
                // semiconvergent (the largest partial term that still fits) and compare it
                // against the previous convergent, keeping whichever is numerically closer.
                long remainingA = kPrev1 > 0 ? (maxDenominator - kPrev2) / kPrev1 : 0;
                if (remainingA > 0)
                {
                    long semiNumerator = remainingA * hPrev1 + hPrev2;
                    long semiDenominator = remainingA * kPrev1 + kPrev2;
                    double semiError = Math.Abs(value - semiNumerator / (double)semiDenominator);
                    double prevError = kPrev1 > 0 ? Math.Abs(value - hPrev1 / (double)kPrev1) : double.MaxValue;
                    (bestNumerator, bestDenominator) = semiError <= prevError
                        ? (semiNumerator, semiDenominator)
                        : (hPrev1, kPrev1);
                }
                else if (kPrev1 > 0)
                {
                    bestNumerator = hPrev1;
                    bestDenominator = kPrev1;
                }
                break;
            }

            bestNumerator = h;
            bestDenominator = k;
            hPrev2 = hPrev1; kPrev2 = kPrev1;
            hPrev1 = h; kPrev1 = k;

            double remainder = x - a;
            if (remainder < 1e-12) break;
            x = 1.0 / remainder;
        }

        if (bestDenominator <= 0) bestDenominator = 1;
        int gcd = GreatestCommonDivisor((int)bestNumerator, (int)bestDenominator);
        if (gcd == 0) gcd = 1;
        return ((int)(bestNumerator / gcd), (int)(bestDenominator / gcd));
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

        // Round the mantissa to the required decimal places. Do this via a decimal-first
        // computation -- matching RoundWithExcelDigits (BuiltInFunctions.Coercion.cs) and the
        // FLOOR/CEILING-to-multiple helpers (BuiltInFunctions.MathCore.Rounding.cs) -- rather
        // than rounding the raw double `mantissa` computed above via Math.Round/"F". The raw
        // division absValue / Math.Pow(10, exponent) can leave a binary-fraction remainder that
        // makes Math.Round disagree with Excel at exact half-decimal boundaries: e.g. for
        // absValue == 1005 and exponent == 3, the true mantissa 1.005 is actually stored as the
        // double 1.0049999999999999..., so Math.Round rounds it DOWN to 1.00, whereas Excel (and
        // this codebase's own "0.00E+00" plain-scientific path and "0.00"/ROUND() paths) round
        // the value's shortest round-trippable decimal representation UP to 1.01. Re-derive the
        // mantissa from a decimal parsed out of absValue's "G15" string (the same technique
        // TryToExcelDecimal uses) so the division and rounding happen without that IEEE remainder.
        decimal? mantissaDecimal = null;
        if (absValue != 0 && TryComputeMantissaDecimal(absValue, exponent, decimalPlaces, out var roundedDecimal))
        {
            mantissaDecimal = roundedDecimal;
            mantissa = (double)roundedDecimal;
        }
        else
        {
            mantissa = Math.Round(mantissa, decimalPlaces, MidpointRounding.AwayFromZero);
        }

        // After rounding, the mantissa might overflow the integer width (e.g. 999.95 → 1000.0
        // when rounded to 1 decimal place). In that case bump the exponent.
        if (mantissa >= Math.Pow(10, exponentGroup))
        {
            exponent += exponentGroup;
            if (mantissaDecimal is { } md)
            {
                md = Math.Round(md / 1000m, decimalPlaces, MidpointRounding.AwayFromZero);
                mantissaDecimal = md;
                mantissa = (double)md;
            }
            else
            {
                mantissa /= Math.Pow(10, exponentGroup);
                mantissa = Math.Round(mantissa, decimalPlaces, MidpointRounding.AwayFromZero);
            }
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
            : mantissaDecimal is { } finalDecimal
                ? finalDecimal.ToString("F" + decimalPlaces.ToString(CultureInfo.InvariantCulture), formatProvider)
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

    /// <summary>
    /// Computes absValue / 10^exponent, rounded to decimalPlaces via decimal arithmetic
    /// (AwayFromZero), instead of raw double division + Math.Round. Mirrors the
    /// "G15"-round-trip-then-decimal-round technique used by TryToExcelDecimal in
    /// BuiltInFunctions.MathCore.Rounding.cs so this format path agrees with Excel (and with
    /// FreeX's own ROUND()/"0.00" paths) at exact half-decimal boundaries. Returns false
    /// (letting the caller fall back to double math) when absValue is non-finite, decimalPlaces
    /// is out of decimal.Round's supported range, or the exponent is too large for a decimal
    /// power-of-ten scale factor -- decimal can't represent those inputs anyway.
    /// </summary>
    private static bool TryComputeMantissaDecimal(double absValue, int exponent, int decimalPlaces, out decimal result)
    {
        result = 0m;
        if (!double.IsFinite(absValue) || decimalPlaces is < 0 or > 28 || Math.Abs(exponent) > 28)
            return false;

        if (!decimal.TryParse(absValue.ToString("G15", CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var baseDecimal))
            return false;

        decimal pow = 1m;
        for (int i = 0; i < Math.Abs(exponent); i++)
            pow *= 10m;

        try
        {
            decimal mantissaDecimal = exponent >= 0 ? baseDecimal / pow : baseDecimal * pow;
            result = Math.Round(mantissaDecimal, decimalPlaces, MidpointRounding.AwayFromZero);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
