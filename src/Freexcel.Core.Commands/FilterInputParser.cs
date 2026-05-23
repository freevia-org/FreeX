namespace Freexcel.Core.Commands;

public static class FilterInputParser
{
    public static IReadOnlyList<string> ParseAllowedValues(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        return input
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool TryParseTopBottom(string input, out uint count, out bool top, out string? error)
    {
        var parsed = TryParseTopBottom(input, out count, out top, out _, out error);
        return parsed;
    }

    public static bool TryParseTopBottom(string input, out uint count, out bool top, out bool percent, out string? error)
    {
        count = 0;
        top = true;
        percent = false;
        error = null;

        var trimmed = input.Trim();
        string? countText = null;
        if (trimmed.StartsWith("toppercent:", StringComparison.OrdinalIgnoreCase))
        {
            top = true;
            percent = true;
            countText = trimmed["toppercent:".Length..];
        }
        else if (trimmed.StartsWith("bottompercent:", StringComparison.OrdinalIgnoreCase))
        {
            top = false;
            percent = true;
            countText = trimmed["bottompercent:".Length..];
        }
        else if (trimmed.StartsWith("top:", StringComparison.OrdinalIgnoreCase))
        {
            top = true;
            countText = trimmed["top:".Length..];
        }
        else if (trimmed.StartsWith("bottom:", StringComparison.OrdinalIgnoreCase))
        {
            top = false;
            countText = trimmed["bottom:".Length..];
        }
        else
        {
            error = "Enter top:n, bottom:n, toppercent:n, or bottompercent:n.";
            return false;
        }

        if (!uint.TryParse(countText.Trim(), out count) || count == 0 || (percent && count > 100))
        {
            error = percent ? "Enter a percentage from 1 to 100." : "Enter a positive item count.";
            return false;
        }

        return true;
    }

    public static bool TryParseAverage(string input, out bool above)
    {
        var trimmed = input.Trim();
        var compact = trimmed.Replace(" ", "", StringComparison.Ordinal);
        if (compact.Equals("aboveavg", StringComparison.OrdinalIgnoreCase) ||
            compact.Equals("aboveaverage", StringComparison.OrdinalIgnoreCase))
        {
            above = true;
            return true;
        }

        if (compact.Equals("belowavg", StringComparison.OrdinalIgnoreCase) ||
            compact.Equals("belowaverage", StringComparison.OrdinalIgnoreCase))
        {
            above = false;
            return true;
        }

        above = true;
        return false;
    }

    public static bool TryParseCriterion(string input, out IFilterCriterion? criterion, out string? error)
    {
        criterion = null;
        error = null;
        var trimmed = input.Trim();
        if (trimmed.StartsWith("and:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("or:", StringComparison.OrdinalIgnoreCase))
        {
            var useAnd = trimmed.StartsWith("and:", StringComparison.OrdinalIgnoreCase);
            var compositeText = trimmed[(useAnd ? "and:" : "or:").Length..];
            var parts = compositeText.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                string.IsNullOrWhiteSpace(parts[0]) ||
                string.IsNullOrWhiteSpace(parts[1]))
            {
                error = "Enter a composite filter as and:criterion1|criterion2 or or:criterion1|criterion2.";
                return false;
            }

            if (!TryParseCriterion(parts[0], out var first, out error) || first is null)
                return false;

            if (!TryParseCriterion(parts[1], out var second, out error) || second is null)
                return false;

            criterion = new CompositeFilterCriterion(first, second, useAnd);
            return true;
        }

        if (trimmed.Equals("blank", StringComparison.OrdinalIgnoreCase))
        {
            criterion = new BlankFilterCriterion();
            return true;
        }

        if (trimmed.Equals("nonblank", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("non-blank", StringComparison.OrdinalIgnoreCase))
        {
            criterion = new NonBlankFilterCriterion();
            return true;
        }

        const string dateBetweenPrefix = "datebetween:";
        if (trimmed.StartsWith(dateBetweenPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var bounds = trimmed[dateBetweenPrefix.Length..]
                .Split(':', StringSplitOptions.TrimEntries);
            if (bounds.Length != 2 ||
                !TryParseDate(bounds[0], out var start, out error) ||
                !TryParseDate(bounds[1], out var end, out error))
            {
                error ??= "Enter a date-between filter as datebetween:yyyy-mm-dd:yyyy-mm-dd.";
                return false;
            }

            criterion = new DateBetweenFilterCriterion(
                start < end ? start : end,
                start < end ? end : start);
            return true;
        }

        if (trimmed.StartsWith("date>=", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("date<=", StringComparison.OrdinalIgnoreCase))
        {
            var isAfterOrEqual = trimmed.StartsWith("date>=", StringComparison.OrdinalIgnoreCase);
            if (!TryParseDate(trimmed[6..], out var date, out error))
                return false;

            criterion = isAfterOrEqual
                ? new DateOnOrAfterFilterCriterion(date)
                : new DateOnOrBeforeFilterCriterion(date);
            return true;
        }

        if (trimmed.StartsWith("date<>", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseDate(trimmed[6..], out var date, out error))
                return false;

            criterion = new DateNotEqualsFilterCriterion(date);
            return true;
        }

        if (trimmed.StartsWith("date=", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("date>", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("date<", StringComparison.OrdinalIgnoreCase))
        {
            var op = trimmed[4];
            if (!TryParseDate(trimmed[5..], out var date, out error))
                return false;

            criterion = op switch
            {
                '=' => new DateEqualsFilterCriterion(date),
                '>' => new DateAfterFilterCriterion(date),
                _ => new DateBeforeFilterCriterion(date)
            };
            return true;
        }

        const string betweenPrefix = "between:";
        if (trimmed.StartsWith(betweenPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var bounds = trimmed[betweenPrefix.Length..]
                .Split(':', StringSplitOptions.TrimEntries);
            if (bounds.Length != 2 ||
                !TryParseThreshold(bounds[0], out var minimum, out error) ||
                !TryParseThreshold(bounds[1], out var maximum, out error))
            {
                error ??= "Enter a between filter as between:min:max.";
                return false;
            }

            criterion = new NumberBetweenFilterCriterion(Math.Min(minimum, maximum), Math.Max(minimum, maximum));
            return true;
        }

        if (trimmed.StartsWith("<>", StringComparison.Ordinal))
        {
            if (!TryParseThreshold(trimmed[2..], out var threshold, out error))
                return false;

            criterion = new NumberNotEqualsFilterCriterion(threshold);
            return true;
        }

        if (trimmed.StartsWith(">=", StringComparison.Ordinal) ||
            trimmed.StartsWith("<=", StringComparison.Ordinal))
        {
            var isGreaterThanOrEqual = trimmed.StartsWith(">=", StringComparison.Ordinal);
            if (!TryParseThreshold(trimmed[2..], out var threshold, out error))
                return false;

            criterion = isGreaterThanOrEqual
                ? new NumberGreaterThanOrEqualFilterCriterion(threshold)
                : new NumberLessThanOrEqualFilterCriterion(threshold);
            return true;
        }

        if (trimmed.StartsWith('>') || trimmed.StartsWith('<') || trimmed.StartsWith('='))
        {
            var isGreaterThan = trimmed[0] == '>';
            var isLessThan = trimmed[0] == '<';
            if (!TryParseThreshold(trimmed[1..], out var threshold, out error))
                return false;

            criterion = isGreaterThan
                ? new NumberGreaterThanFilterCriterion(threshold)
                : isLessThan
                    ? new NumberLessThanFilterCriterion(threshold)
                    : new NumberEqualsFilterCriterion(threshold);
            return true;
        }

        const string containsPrefix = "contains:";
        const string notContainsPrefix = "notcontains:";
        const string beginsPrefix = "begins:";
        const string endsPrefix = "ends:";
        const string textNotEqualsPrefix = "text<>";
        const string textEqualsPrefix = "text=";
        const string equalsPrefix = "equals:";

        if (trimmed.StartsWith(notContainsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseTextCriterion(
                trimmed[notContainsPrefix.Length..],
                text => new TextDoesNotContainFilterCriterion(text),
                out criterion,
                out error);
        }

        if (trimmed.StartsWith(containsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseTextCriterion(
                trimmed[containsPrefix.Length..],
                text => new TextContainsFilterCriterion(text),
                out criterion,
                out error);
        }

        if (trimmed.StartsWith(beginsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseTextCriterion(
                trimmed[beginsPrefix.Length..],
                text => new TextBeginsWithFilterCriterion(text),
                out criterion,
                out error);
        }

        if (trimmed.StartsWith(endsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseTextCriterion(
                trimmed[endsPrefix.Length..],
                text => new TextEndsWithFilterCriterion(text),
                out criterion,
                out error);
        }

        if (trimmed.StartsWith(textNotEqualsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseTextCriterion(
                trimmed[textNotEqualsPrefix.Length..],
                text => new TextNotEqualsFilterCriterion(text),
                out criterion,
                out error);
        }

        if (trimmed.StartsWith(textEqualsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseTextCriterion(
                trimmed[textEqualsPrefix.Length..],
                text => new TextEqualsFilterCriterion(text),
                out criterion,
                out error);
        }

        if (trimmed.StartsWith(equalsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseTextCriterion(
                trimmed[equalsPrefix.Length..],
                text => new TextEqualsFilterCriterion(text),
                out criterion,
                out error);
        }

        error = "Enter a supported filter criterion.";
        return false;
    }

    private static bool TryParseTextCriterion(
        string textInput,
        Func<string, IFilterCriterion> createCriterion,
        out IFilterCriterion? criterion,
        out string? error)
    {
        var text = textInput.Trim();
        if (text.Length == 0)
        {
            error = "Enter text to match.";
            criterion = null;
            return false;
        }

        criterion = createCriterion(text);
        error = null;
        return true;
    }

    private static bool TryParseThreshold(string text, out double threshold, out string? error)
    {
        if (!double.TryParse(
                text.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture,
                out threshold))
        {
            error = "Enter a valid number after the comparison operator.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseDate(string text, out DateOnly date, out string? error)
    {
        if (!DateOnly.TryParseExact(
                text.Trim(),
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out date))
        {
            error = "Enter dates as yyyy-mm-dd.";
            return false;
        }

        error = null;
        return true;
    }
}
