using System.Globalization;
using System.Text.RegularExpressions;

namespace Freexcel.Core.Calc;

public static partial class NumberFormatter
{
    private static bool IsSimpleFractionFormat(string format)
    {
        var stripped = Regex.Replace(format, "\"[^\"]*\"", "");
        return stripped.Contains("?/?", StringComparison.Ordinal) ||
               stripped.Contains("?/??", StringComparison.Ordinal) ||
               stripped.Contains("??/??", StringComparison.Ordinal) ||
               Regex.IsMatch(stripped, @"\?+/\d+");
    }

    private static string FormatSimpleFraction(double value, string format)
    {
        var (prefix, numericFormat, suffix) = ExtractNumericAffixes(format);
        var stripped = Regex.Replace(numericFormat, "\"[^\"]*\"", "");
        int? fixedDenominator = null;
        var fixedDenominatorMatch = Regex.Match(suffix, @"^/(\d+)");
        if (fixedDenominatorMatch.Success &&
            int.TryParse(fixedDenominatorMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDenominator) &&
            parsedDenominator > 0)
        {
            fixedDenominator = parsedDenominator;
            suffix = suffix[fixedDenominatorMatch.Length..];
        }

        var denominatorPattern = Regex.Match(stripped, @"/(\?+)");
        int maxDenominator = denominatorPattern.Success && denominatorPattern.Groups[1].Value.Length >= 2 ? 99 : 9;

        double absValue = Math.Abs(value);
        int whole = stripped.Contains('#') || stripped.Contains('0') ? (int)Math.Floor(absValue) : 0;
        double fractional = absValue - whole;

        var (numerator, denominator) = fixedDenominator is { } denominatorValue
            ? ((int)Math.Round(fractional * denominatorValue), denominatorValue)
            : ApproximateFraction(fractional, maxDenominator);
        if (numerator == denominator)
        {
            whole++;
            numerator = 0;
        }

        var sign = value < 0 ? "-" : "";
        if (numerator == 0)
            return prefix + sign + (whole == 0 ? "0" : whole.ToString(CultureInfo.InvariantCulture)) + suffix;

        string fraction = numerator.ToString(CultureInfo.InvariantCulture) + "/" +
                          denominator.ToString(CultureInfo.InvariantCulture);
        string number = whole > 0
            ? sign + whole.ToString(CultureInfo.InvariantCulture) + " " + fraction
            : sign + fraction;
        return prefix + number + suffix;
    }

    private static (int Numerator, int Denominator) ApproximateFraction(double value, int maxDenominator)
    {
        int bestNumerator = 0;
        int bestDenominator = 1;
        double bestError = double.MaxValue;

        for (int denominator = 1; denominator <= maxDenominator; denominator++)
        {
            int numerator = (int)Math.Round(value * denominator);
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

    private static bool IsScientificFormat(string format)
    {
        var stripped = Regex.Replace(format, "\"[^\"]*\"", "");
        return Regex.IsMatch(stripped, @"E[+-]0+", RegexOptions.IgnoreCase);
    }

    private static string FormatScientific(double value, string format, IFormatProvider formatProvider)
    {
        var (prefix, numericFormat, suffix) = ExtractNumericAffixes(format);
        var stripped = Regex.Replace(numericFormat, "\"[^\"]*\"", "");
        var mantissa = stripped.Split(['E', 'e'], 2)[0];
        int decimals = mantissa.Contains('.')
            ? mantissa[(mantissa.IndexOf('.') + 1)..].Count(c => c == '0' || c == '#')
            : 0;

        string result = value.ToString("E" + decimals.ToString(CultureInfo.InvariantCulture), formatProvider);
        result = Regex.Replace(result, @"E([+-])(\d{2,})$", match =>
        {
            string sign = match.Groups[1].Value;
            string exponent = match.Groups[2].Value;
            var exponentFormat = Regex.Match(stripped, @"E([+-])(0+)", RegexOptions.IgnoreCase);
            int minDigits = exponentFormat.Groups[2].Value.Length;
            exponent = int.Parse(exponent, CultureInfo.InvariantCulture).ToString("D" + minDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            string displaySign = exponentFormat.Groups[1].Value == "-" && sign == "+" ? "" : sign;
            return "E" + displaySign + exponent;
        });
        return prefix + result + suffix;
    }
}
