using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetAutoFilterCustomFilterMatcher
{
    public static bool TryCreate(
        WorksheetAutoFilterColumnModel column,
        out Func<ScalarValue, bool>? matcher)
    {
        matcher = null;
        if (column.CustomFilters.Count == 0 ||
            column.CustomFilters.Any(filter => filter.Value is null || !IsSupportedOperator(filter.Operator)))
        {
            return false;
        }

        var filters = new List<Func<ScalarValue, bool>>(column.CustomFilters.Count);
        foreach (var filter in column.CustomFilters)
        {
            if (!TryCreateSingle(filter.Operator, filter.Value!, out var predicate))
                return false;
            filters.Add(predicate);
        }

        matcher = value => column.CustomFiltersAnd
            ? filters.All(filter => filter(value))
            : filters.Any(filter => filter(value));
        return true;
    }

    private static bool IsSupportedOperator(string? op) => op is null or "" or "notEqual" or
        "greaterThan" or "greaterThanOrEqual" or "lessThan" or "lessThanOrEqual";

    private static bool TryCreateSingle(
        string? op,
        string pattern,
        out Func<ScalarValue, bool> predicate)
    {
        predicate = null!;
        var isNotEqual = string.Equals(op, "notEqual", StringComparison.OrdinalIgnoreCase);
        if (op is not null and not "" and not "notEqual" &&
            op is not ("greaterThan" or "greaterThanOrEqual" or "lessThan" or "lessThanOrEqual"))
        {
            return false;
        }

        // r577: IsFinite -- pattern is a customFilters/@val read out of the file, and
        // NumberStyles.Float accepts "NaN"/"Infinity" and an overflowing magnitude. A NaN threshold
        // makes every ordering Compare false (the filter hides every row) and makes notEqual true
        // for every row. This is the IO-side twin of PersistedCustomFilterCriterion.Matches in
        // FreeX.Core.Commands, fixed in the same round: two implementations of one rule, so the
        // defect had to be fixed in both. Falling back to the wildcard/text matcher is what an
        // unparseable pattern already does.
        var hasNumericThreshold = double.TryParse(
            pattern,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var threshold) && double.IsFinite(threshold);
        var textMatcher = CreateWildcardMatcher(pattern);
        predicate = value =>
        {
            if (hasNumericThreshold)
            {
                var numeric = value switch
                {
                    NumberValue number => number.Value,
                    DateTimeValue date when date.TryToDateTime(out var dateValue) => DateTimeValue
                        .FromDateTime(DateOnly.FromDateTime(dateValue).ToDateTime(TimeOnly.MinValue))
                        .Value,
                    _ => (double?)null
                };
                if (numeric is { } numericValue)
                    return Compare(numericValue, threshold, op);
            }

            if (op is not null and not "" and not "notEqual")
                return false;

            var matched = textMatcher.IsMatch(XlsxFilterValueTextFormatter.ToFilterText(value));
            return isNotEqual ? !matched : matched;
        };
        return true;
    }

    private static bool Compare(double actual, double threshold, string? op) => op switch
    {
        "greaterThan" => actual > threshold,
        "greaterThanOrEqual" => actual >= threshold,
        "lessThan" => actual < threshold,
        "lessThanOrEqual" => actual <= threshold,
        "notEqual" => Math.Abs(actual - threshold) >= double.Epsilon,
        _ => Math.Abs(actual - threshold) < double.Epsilon
    };

    private static Regex CreateWildcardMatcher(string pattern)
    {
        var regex = new StringBuilder(pattern.Length + 2).Append('^');
        for (var i = 0; i < pattern.Length; i++)
        {
            var character = pattern[i];
            if (character == '~' && i + 1 < pattern.Length)
                regex.Append(Regex.Escape(pattern[++i].ToString()));
            else if (character == '*')
                regex.Append(".*");
            else if (character == '?')
                regex.Append('.');
            else
                regex.Append(Regex.Escape(character.ToString()));
        }

        regex.Append('$');
        return new Regex(
            regex.ToString(),
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
