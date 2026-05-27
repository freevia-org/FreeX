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

    public static bool TryParseCriterion(string input, out IFilterCriterion? criterion, out string? error) =>
        FilterCriterionInputParser.TryParseCriterion(input, out criterion, out error);
}
