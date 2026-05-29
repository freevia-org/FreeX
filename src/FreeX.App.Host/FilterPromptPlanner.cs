using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public enum FilterPromptPlanKind
{
    TopBottom,
    Average,
    Condition,
    AllowedValues
}

public sealed record FilterPromptPlan(
    FilterPromptPlanKind Kind,
    uint Count = 0,
    bool Top = true,
    bool Percent = false,
    bool AboveAverage = true,
    IFilterCriterion? Criterion = null,
    IReadOnlyList<string>? AllowedValues = null)
{
    public IWorkbookCommand CreateCommand(SheetId sheetId, GridRange range, uint filterColOffset) =>
        Kind switch
        {
            FilterPromptPlanKind.TopBottom => Percent
                ? TopBottomFilterCommand.Percent(sheetId, range, filterColOffset, Count, Top)
                : new TopBottomFilterCommand(sheetId, range, filterColOffset, Count, Top),
            FilterPromptPlanKind.Average => new AverageFilterCommand(sheetId, range, filterColOffset, AboveAverage),
            FilterPromptPlanKind.Condition => new FilterConditionCommand(sheetId, range, filterColOffset, Criterion!),
            _ => new FilterCommand(sheetId, range, filterColOffset, AllowedValues ?? [])
        };
}

public static class FilterPromptPlanner
{
    private static readonly string[] TopBottomPrefixes =
    [
        "top:",
        "toppercent:",
        "bottompercent:",
        "bottom:"
    ];

    private static readonly string[] CaseInsensitiveCriterionPrefixes =
    [
        "and:",
        "or:",
        "date=",
        "date<>",
        "date>=",
        "date>",
        "date<=",
        "date<",
        "datebetween:",
        "contains:",
        "notcontains:",
        "begins:",
        "ends:",
        "equals:",
        "text=",
        "text<>",
        "between:"
    ];

    private static readonly string[] SymbolCriterionPrefixes =
    [
        "<>",
        ">=",
        "<="
    ];

    public static bool TryPlan(string input, out FilterPromptPlan? plan, out string? error)
    {
        plan = null;
        error = null;

        var filterText = input.TrimStart();
        if (IsTopBottomInput(filterText))
        {
            if (!FilterInputParser.TryParseTopBottom(input, out var count, out var top, out var percent, out error))
                return false;

            plan = new FilterPromptPlan(FilterPromptPlanKind.TopBottom, Count: count, Top: top, Percent: percent);
            return true;
        }

        if (FilterInputParser.TryParseAverage(input, out var aboveAverage))
        {
            plan = new FilterPromptPlan(FilterPromptPlanKind.Average, AboveAverage: aboveAverage);
            return true;
        }

        if (IsCriterionInput(filterText))
        {
            if (!FilterInputParser.TryParseCriterion(input, out var criterion, out error) || criterion is null)
                return false;

            plan = new FilterPromptPlan(FilterPromptPlanKind.Condition, Criterion: criterion);
            return true;
        }

        plan = new FilterPromptPlan(
            FilterPromptPlanKind.AllowedValues,
            AllowedValues: FilterInputParser.ParseAllowedValues(input));
        return true;
    }

    private static bool IsTopBottomInput(string filterText) =>
        StartsWithAny(filterText, TopBottomPrefixes, StringComparison.OrdinalIgnoreCase);

    private static bool IsCriterionInput(string filterText) =>
        filterText.Equals("blank", StringComparison.OrdinalIgnoreCase) ||
        filterText.Equals("nonblank", StringComparison.OrdinalIgnoreCase) ||
        filterText.Equals("non-blank", StringComparison.OrdinalIgnoreCase) ||
        StartsWithAny(filterText, CaseInsensitiveCriterionPrefixes, StringComparison.OrdinalIgnoreCase) ||
        StartsWithAny(filterText, SymbolCriterionPrefixes, StringComparison.Ordinal) ||
        filterText.StartsWith('>') ||
        filterText.StartsWith('<') ||
        filterText.StartsWith('=');

    private static bool StartsWithAny(string value, IReadOnlyList<string> prefixes, StringComparison comparison)
    {
        foreach (var prefix in prefixes)
        {
            if (value.StartsWith(prefix, comparison))
                return true;
        }

        return false;
    }
}
