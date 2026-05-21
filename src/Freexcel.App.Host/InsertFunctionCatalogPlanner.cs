using Freexcel.Core.Formula;

namespace Freexcel.App.Host;

public static class InsertFunctionCatalogPlanner
{
    public const string MostRecentlyUsedCategory = "Most Recently Used";
    public const string AllCategory = "All";
    private static readonly string[] MostRecentlyUsedFunctions = ["SUM", "AVERAGE", "COUNT", "MAX", "MIN", "IF", "XLOOKUP", "VLOOKUP"];

    public static IReadOnlyList<InsertFunctionCatalogEntry> BuildCatalog() =>
        BuiltInFunctions.Names
            .Select(name => name.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new InsertFunctionCatalogEntry(name, GetCategory(name), GetDescription(name)))
            .ToArray();

    public static IReadOnlyList<InsertFunctionCatalogEntry> FilterCatalog(
        IReadOnlyList<InsertFunctionCatalogEntry> catalog,
        string? category,
        string? searchText)
    {
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? AllCategory : category.Trim();
        var search = searchText?.Trim() ?? "";
        var searchSpansCatalog = search.Length > 0 && normalizedCategory == MostRecentlyUsedCategory;
        return catalog
            .Where(entry =>
                normalizedCategory == AllCategory ||
                searchSpansCatalog ||
                (normalizedCategory == MostRecentlyUsedCategory && MostRecentlyUsedFunctions.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)) ||
                entry.Category == normalizedCategory)
            .Where(entry =>
                search.Length == 0 ||
                entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => normalizedCategory == MostRecentlyUsedCategory && search.Length == 0
                ? Array.IndexOf(MostRecentlyUsedFunctions, entry.Name)
                : 0)
            .ToArray();
    }

    public static string CreateFormula(string functionName) =>
        $"{functionName.Trim().ToUpperInvariant()}()";

    public static string GetCategory(string name)
    {
        if (LogicalFunctions.Contains(name)) return "Logical";
        if (LookupFunctions.Contains(name)) return "Lookup & Reference";
        if (TextFunctions.Contains(name)) return "Text";
        if (DateTimeFunctions.Contains(name)) return "Date & Time";
        if (StatisticalFunctions.Contains(name)) return "Statistical";
        if (DynamicArrayFunctions.Contains(name)) return "Dynamic Array";
        if (FinancialFunctions.Contains(name)) return "Financial";
        if (InformationFunctions.Contains(name)) return "Information";
        return "Math & Trig";
    }

    public static string GetDescription(string name) =>
        KnownDescriptions.TryGetValue(name, out var description)
            ? description
            : $"{name} function.";

    private static readonly HashSet<string> LogicalFunctions = ["IF", "IFS", "AND", "OR", "NOT", "IFERROR", "IFNA", "LET", "LAMBDA"];
    private static readonly HashSet<string> LookupFunctions = ["VLOOKUP", "HLOOKUP", "XLOOKUP", "INDEX", "MATCH", "XMATCH", "INDIRECT", "OFFSET"];
    private static readonly HashSet<string> TextFunctions = ["CONCAT", "TEXTJOIN", "LEFT", "RIGHT", "MID", "LEN", "TRIM", "TEXT", "UPPER", "LOWER", "PROPER", "SUBSTITUTE", "FIND", "SEARCH", "REPT", "VALUE"];
    private static readonly HashSet<string> DateTimeFunctions = ["TODAY", "NOW", "DATE", "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "WEEKDAY", "EDATE", "DATEDIF", "EOMONTH", "WORKDAY", "NETWORKDAYS"];
    private static readonly HashSet<string> StatisticalFunctions = ["AVERAGE", "COUNT", "COUNTA", "MIN", "MAX", "COUNTIF", "COUNTIFS", "AVERAGEIF", "MEDIAN", "STDEV.S", "VAR.S", "RANK.EQ", "PERCENTILE.INC"];
    private static readonly HashSet<string> DynamicArrayFunctions = ["FILTER", "SORT", "UNIQUE", "SEQUENCE", "RANDARRAY", "TRANSPOSE", "MAP", "REDUCE", "SCAN", "BYROW", "BYCOL", "MAKEARRAY"];
    private static readonly HashSet<string> FinancialFunctions = ["PMT", "NPV", "IRR", "RATE", "PV", "FV"];
    private static readonly HashSet<string> InformationFunctions = ["ISBLANK", "ISNUMBER", "ISTEXT", "ISERROR", "NA", "CELL", "INFO"];

    private static readonly Dictionary<string, string> KnownDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SUM"] = "Adds numbers.",
        ["AVERAGE"] = "Returns the average of numbers.",
        ["COUNT"] = "Counts numeric values.",
        ["COUNTA"] = "Counts non-empty values.",
        ["IF"] = "Returns one value if a condition is true and another if false.",
        ["VLOOKUP"] = "Looks up a value in the first column of a table.",
        ["HLOOKUP"] = "Looks up a value in the first row of a table.",
        ["XLOOKUP"] = "Searches a range and returns a matching item.",
        ["INDEX"] = "Returns a value from a range by position.",
        ["MATCH"] = "Returns the relative position of an item.",
        ["XMATCH"] = "Returns the relative position of an item with modern match options.",
        ["CONCAT"] = "Joins text values.",
        ["TEXT"] = "Formats a value as text.",
        ["TODAY"] = "Returns the current date.",
        ["NOW"] = "Returns the current date and time.",
        ["ROUND"] = "Rounds a number to a specified number of digits.",
        ["FILTER"] = "Filters a range by included rows or columns.",
        ["SORT"] = "Sorts a range or array.",
        ["UNIQUE"] = "Returns unique values from a range or array."
    };
}
