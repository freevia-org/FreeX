using FreeX.Core.Formula;

namespace FreeX.App.Avalonia.Dialogs;

/// <summary>
/// One function row in the Insert Function dialog: the display name, the category it falls under, and
/// a short description shown in the syntax/help pane.
/// </summary>
internal sealed record InsertFunctionCatalogEntry(string Name, string Category, string Description);

/// <summary>
/// Portable, UI-free catalog backing the Avalonia Insert Function dialog: the full built-in function
/// list (from <see cref="BuiltInFunctions.Names"/>) bucketed into Excel-style categories, the
/// category-plus-search filter, the Most Recently Used list, and the formula text inserted when a
/// function (optionally with typed arguments) is chosen. Kept free of Avalonia types so the glue is
/// unit-testable without a running window.
/// </summary>
/// <remarks>
/// This mirrors the WPF host's <c>InsertFunctionCatalogPlanner</c>, which lives in a project the
/// Avalonia shell does not reference. The per-argument specs and the live <c>=FUNC(a, b)</c> preview
/// come from <see cref="FreeX.App.Presentation.Dialogs.FunctionArgumentCatalog"/>; this type adds only
/// the function list, category bucketing, filter, and MRU tracking that catalog lacks.
/// </remarks>
internal static class InsertFunctionCatalog
{
    public const string MostRecentlyUsedCategory = "Most Recently Used";
    public const string AllCategory = "All";

    private const int MostRecentlyUsedLimit = 10;

    private static readonly string[] DefaultMostRecentlyUsedFunctions =
        ["SUM", "AVERAGE", "COUNT", "MAX", "MIN", "IF", "XLOOKUP", "VLOOKUP"];

    /// <summary>The default Most Recently Used seed, in display order (most recent first).</summary>
    public static IReadOnlyList<string> DefaultMostRecentlyUsed => DefaultMostRecentlyUsedFunctions;

    /// <summary>The full alphabetical catalog of built-in functions with category and description.</summary>
    public static IReadOnlyList<InsertFunctionCatalogEntry> BuildCatalog() =>
        BuiltInFunctions.Names
            .Select(name => name.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new InsertFunctionCatalogEntry(name, GetCategory(name), GetDescription(name)))
            .ToArray();

    /// <summary>The category choices for the dropdown: MRU, All, then the present categories sorted.</summary>
    public static IReadOnlyList<string> BuildCategoryChoices(IReadOnlyList<InsertFunctionCatalogEntry> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return
        [
            MostRecentlyUsedCategory,
            AllCategory,
            .. catalog.Select(entry => entry.Category).Distinct().OrderBy(category => category, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// Filters the catalog by the selected category and free-text search. A non-empty search while the
    /// Most Recently Used category is selected spans the whole catalog (so users can find anything
    /// without first switching to All). Within Most Recently Used and no search, entries keep the
    /// supplied recent order; otherwise the alphabetical catalog order is preserved.
    /// </summary>
    public static IReadOnlyList<InsertFunctionCatalogEntry> FilterCatalog(
        IReadOnlyList<InsertFunctionCatalogEntry> catalog,
        string? category,
        string? searchText,
        IReadOnlyList<string>? mostRecentlyUsed = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var recent = mostRecentlyUsed ?? DefaultMostRecentlyUsedFunctions;
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? AllCategory : category.Trim();
        var search = searchText?.Trim() ?? "";
        var searchSpansCatalog = search.Length > 0 && normalizedCategory == MostRecentlyUsedCategory;

        return catalog
            .Where(entry =>
                normalizedCategory == AllCategory ||
                searchSpansCatalog ||
                (normalizedCategory == MostRecentlyUsedCategory && Contains(recent, entry.Name)) ||
                entry.Category == normalizedCategory)
            .Where(entry =>
                search.Length == 0 ||
                entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => normalizedCategory == MostRecentlyUsedCategory && search.Length == 0
                ? IndexOf(recent, entry.Name)
                : 0)
            .ToArray();
    }

    /// <summary>
    /// Moves <paramref name="functionName"/> to the front of the recent list (de-duplicated,
    /// case-insensitive) and trims it to the most recent entries. Returns a new list; the input is not
    /// mutated.
    /// </summary>
    public static IReadOnlyList<string> UpdateMostRecentlyUsed(
        IReadOnlyList<string> mostRecentlyUsed,
        string functionName)
    {
        ArgumentNullException.ThrowIfNull(mostRecentlyUsed);
        ArgumentNullException.ThrowIfNull(functionName);

        var normalized = functionName.Trim().ToUpperInvariant();
        if (normalized.Length == 0)
            return mostRecentlyUsed;

        var updated = new List<string> { normalized };
        updated.AddRange(mostRecentlyUsed
            .Where(name => !string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase)));

        if (updated.Count > MostRecentlyUsedLimit)
            updated.RemoveRange(MostRecentlyUsedLimit, updated.Count - MostRecentlyUsedLimit);

        return updated;
    }

    /// <summary>The seed formula text for a function with no typed arguments, e.g. <c>=SUM()</c>.</summary>
    public static string CreateFormula(string functionName)
    {
        ArgumentNullException.ThrowIfNull(functionName);
        return $"={functionName.Trim().ToUpperInvariant()}()";
    }

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
        if (DatabaseFunctions.Contains(name)) return "Database";
        if (EngineeringFunctions.Contains(name)) return "Engineering";
        return "Math & Trig";
    }

    public static string GetDescription(string name) =>
        KnownDescriptions.TryGetValue(name, out var description)
            ? description
            : $"{name} function.";

    private static bool Contains(IReadOnlyList<string> names, string candidate)
    {
        foreach (var name in names)
            if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static int IndexOf(IReadOnlyList<string> names, string candidate)
    {
        for (var index = 0; index < names.Count; index++)
            if (string.Equals(names[index], candidate, StringComparison.OrdinalIgnoreCase))
                return index;

        return int.MaxValue;
    }

    private static readonly HashSet<string> LogicalFunctions = ["IF", "IFS", "AND", "OR", "NOT", "XOR", "TRUE", "FALSE", "IFERROR", "IFNA", "LET", "LAMBDA"];
    private static readonly HashSet<string> LookupFunctions = ["VLOOKUP", "HLOOKUP", "XLOOKUP", "INDEX", "MATCH", "XMATCH", "LOOKUP", "INDIRECT", "OFFSET", "ADDRESS", "TRANSPOSE", "GETPIVOTDATA"];
    private static readonly HashSet<string> TextFunctions = ["CONCAT", "TEXTJOIN", "LEFT", "RIGHT", "MID", "LEN", "TRIM", "TEXT", "UPPER", "LOWER", "PROPER", "SUBSTITUTE", "FIND", "SEARCH", "REPT", "VALUE"];
    private static readonly HashSet<string> DateTimeFunctions = ["TODAY", "NOW", "DATE", "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "WEEKDAY", "EDATE", "DATEDIF", "EOMONTH", "WORKDAY", "NETWORKDAYS"];
    private static readonly HashSet<string> StatisticalFunctions = ["AVERAGE", "COUNT", "COUNTA", "MIN", "MAX", "COUNTIF", "COUNTIFS", "AVERAGEIF", "MEDIAN", "STDEV.S", "VAR.S", "RANK.EQ", "PERCENTILE.INC"];
    private static readonly HashSet<string> DynamicArrayFunctions = ["FILTER", "SORT", "SORTBY", "UNIQUE", "SEQUENCE", "RANDARRAY", "TAKE", "DROP", "EXPAND", "CHOOSEROWS", "CHOOSECOLS", "VSTACK", "HSTACK", "TOROW", "TOCOL", "WRAPROWS", "WRAPCOLS", "MAP", "REDUCE", "SCAN", "BYROW", "BYCOL", "MAKEARRAY"];
    private static readonly HashSet<string> FinancialFunctions = ["PMT", "NPV", "IRR", "RATE", "PV", "FV", "IPMT", "PPMT", "NPER", "CUMIPMT", "CUMPRINC", "EFFECT", "NOMINAL", "MIRR", "XIRR", "XNPV", "RRI", "PDURATION", "FVSCHEDULE", "DB", "DDB", "VDB", "SYD", "AMORDEGRC", "AMORLINC", "DOLLARDE", "DOLLARFR", "DISC", "INTRATE", "RECEIVED", "ACCRINT", "ACCRINTM", "TBILLEQ", "TBILLPRICE", "TBILLYIELD", "COUPDAYBS", "COUPDAYS", "COUPDAYSNC", "COUPNCD", "COUPNUM", "COUPPCD", "PRICE", "YIELD", "PRICEDISC", "PRICEMAT", "YIELDDISC", "YIELDMAT", "DURATION", "MDURATION", "ODDFPRICE", "ODDFYIELD", "ODDLPRICE", "ODDLYIELD"];
    private static readonly HashSet<string> InformationFunctions = ["ISBLANK", "ISNUMBER", "ISTEXT", "ISERROR", "ISREF", "ISFORMULA", "FORMULATEXT", "NA", "CELL", "INFO", "TYPE", "ERROR.TYPE", "N", "ISEVEN", "ISODD"];
    private static readonly HashSet<string> DatabaseFunctions = ["DSUM", "DAVERAGE", "DCOUNT", "DCOUNTA", "DGET", "DMAX", "DMIN", "DPRODUCT", "DSTDEV", "DSTDEVP", "DVAR", "DVARP"];
    private static readonly HashSet<string> EngineeringFunctions = ["BASE", "BIN2DEC", "BIN2HEX", "BIN2OCT", "BITAND", "BITLSHIFT", "BITOR", "BITRSHIFT", "BITXOR", "COMPLEX", "CONVERT", "DEC2BIN", "DEC2HEX", "DEC2OCT", "DECIMAL", "DELTA", "ERF", "ERF.PRECISE", "ERFC", "ERFC.PRECISE", "GESTEP", "HEX2BIN", "HEX2DEC", "HEX2OCT", "IMABS", "IMAGINARY", "IMARGUMENT", "IMCONJUGATE", "IMCOS", "IMCOSH", "IMCOT", "IMCSC", "IMCSCH", "IMDIV", "IMEXP", "IMLN", "IMLOG10", "IMLOG2", "IMPOWER", "IMPRODUCT", "IMREAL", "IMSEC", "IMSECH", "IMSIN", "IMSINH", "IMSQRT", "IMSUB", "IMSUM", "IMTAN", "OCT2BIN", "OCT2DEC", "OCT2HEX"];

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
        ["GETPIVOTDATA"] = "Returns data stored in a PivotTable report.",
        ["CONCAT"] = "Joins text values.",
        ["TEXT"] = "Formats a value as text.",
        ["TODAY"] = "Returns the current date.",
        ["NOW"] = "Returns the current date and time.",
        ["ROUND"] = "Rounds a number to a specified number of digits.",
        ["FILTER"] = "Filters a range by included rows or columns.",
        ["SORT"] = "Sorts a range or array.",
        ["UNIQUE"] = "Returns unique values from a range or array.",
        ["DSUM"] = "Adds numbers from records that match database criteria.",
        ["DGET"] = "Returns one database record value that matches criteria.",
        ["COMPLEX"] = "Converts real and imaginary coefficients into a complex number.",
        ["CONVERT"] = "Converts a number from one measurement system to another.",
        ["BITAND"] = "Returns a bitwise AND of two numbers.",
        ["DELTA"] = "Tests whether two values are equal.",
        ["GESTEP"] = "Tests whether a number is greater than a threshold value.",
        ["IMABS"] = "Returns the absolute value of a complex number.",
        ["MAP"] = "Maps a LAMBDA over one or more arrays.",
        ["LAMBDA"] = "Creates a reusable custom function.",
        ["LET"] = "Assigns names to calculation results."
    };
}
