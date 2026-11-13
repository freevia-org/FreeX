using FreeX.Core.Formula;

namespace FreeX.App.Presentation.Dialogs;

/// <summary>
/// One function row in the Insert Function dialog: the display name, the category it falls under, and
/// a short description shown in the syntax/help pane.
/// </summary>
public sealed record InsertFunctionCatalogEntry(string Name, string Category, string Description);

/// <summary>
/// Portable, UI-free catalog backing the Insert Function dialog on every app shell: the full built-in
/// function list (from <see cref="BuiltInFunctions.Names"/>) bucketed into Excel-style categories, the
/// category-plus-search filter, the Most Recently Used list, and the formula text inserted when a
/// function is chosen. Kept free of UI types so the glue is unit-testable without a running window.
/// </summary>
/// <remarks>
/// The per-argument specs and the live <c>=FUNC(a, b)</c> preview come from
/// <see cref="FunctionArgumentCatalog"/>; this type adds the function list, category bucketing, filter,
/// and MRU tracking that catalog lacks.
/// </remarks>
public static class InsertFunctionCatalogPlanner
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

    /// <summary>
    /// The functions a Formulas &gt; Function Library category button lists: every implemented
    /// function in that category, alphabetically, as in Excel.
    /// </summary>
    public static IReadOnlyList<string> FunctionLibraryNames(string category) =>
        BuildCatalog()
            .Where(entry => string.Equals(entry.Category, category, StringComparison.Ordinal))
            .Select(entry => entry.Name)
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
        // Excel's own Insert Function / Function Library categories. Anything not listed below
        // (SUM, ROUND, SEQUENCE, RANDARRAY, SUBTOTAL, AGGREGATE, ...) is Math & Trig, as in Excel.
        if (LogicalFunctions.Contains(name)) return "Logical";
        if (LookupFunctions.Contains(name)) return "Lookup & Reference";
        if (TextFunctions.Contains(name)) return "Text";
        if (DateTimeFunctions.Contains(name)) return "Date & Time";
        if (StatisticalFunctions.Contains(name)) return "Statistical";
        if (FinancialFunctions.Contains(name)) return "Financial";
        if (InformationFunctions.Contains(name)) return "Information";
        if (DatabaseFunctions.Contains(name)) return "Database";
        if (EngineeringFunctions.Contains(name)) return "Engineering";
        if (CompatibilityFunctions.Contains(name)) return "Compatibility";
        if (WebFunctions.Contains(name)) return "Web";
        if (CubeFunctions.Contains(name)) return "Cube";
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

    private static readonly HashSet<string> LogicalFunctions = new(StringComparer.OrdinalIgnoreCase) { "AND", "BYCOL", "BYROW", "FALSE", "IF", "IFERROR", "IFNA", "IFS", "LAMBDA", "LET", "MAKEARRAY", "MAP", "NOT", "OR", "REDUCE", "SCAN", "SWITCH", "TRUE", "XOR" };
    private static readonly HashSet<string> LookupFunctions = new(StringComparer.OrdinalIgnoreCase) { "ADDRESS", "AREAS", "CHOOSE", "CHOOSECOLS", "CHOOSEROWS", "COLUMN", "COLUMNS", "DROP", "EXPAND", "FILTER", "FORMULATEXT", "GETPIVOTDATA", "GROUPBY", "HLOOKUP", "HSTACK", "HYPERLINK", "IMAGE", "INDEX", "INDIRECT", "LOOKUP", "MATCH", "OFFSET", "PIVOTBY", "ROW", "ROWS", "RTD", "SORT", "SORTBY", "TAKE", "TOCOL", "TOROW", "TRANSPOSE", "TRIMRANGE", "UNIQUE", "VLOOKUP", "VSTACK", "WRAPCOLS", "WRAPROWS", "XLOOKUP", "XMATCH" };
    private static readonly HashSet<string> TextFunctions = new(StringComparer.OrdinalIgnoreCase) { "ARRAYTOTEXT", "ASC", "BAHTTEXT", "CHAR", "CLEAN", "CODE", "CONCAT", "DBCS", "DOLLAR", "EXACT", "FIND", "FINDB", "FIXED", "LEFT", "LEFTB", "LEN", "LENB", "LOWER", "MID", "MIDB", "NUMBERVALUE", "PHONETIC", "PROPER", "REGEXEXTRACT", "REGEXREPLACE", "REGEXTEST", "REPLACE", "REPLACEB", "REPT", "RIGHT", "RIGHTB", "SEARCH", "SEARCHB", "SUBSTITUTE", "T", "TEXT", "TEXTAFTER", "TEXTBEFORE", "TEXTJOIN", "TEXTSPLIT", "TRIM", "UNICHAR", "UNICODE", "UPPER", "VALUE", "VALUETOTEXT" };
    private static readonly HashSet<string> DateTimeFunctions = new(StringComparer.OrdinalIgnoreCase) { "DATE", "DATEDIF", "DATEVALUE", "DAY", "DAYS", "DAYS360", "EDATE", "EOMONTH", "HOUR", "ISOWEEKNUM", "MINUTE", "MONTH", "NETWORKDAYS", "NETWORKDAYS.INTL", "NOW", "SECOND", "TIME", "TIMEVALUE", "TODAY", "WEEKDAY", "WEEKNUM", "WORKDAY", "WORKDAY.INTL", "YEAR", "YEARFRAC" };
    private static readonly HashSet<string> StatisticalFunctions = new(StringComparer.OrdinalIgnoreCase) { "AVEDEV", "AVERAGE", "AVERAGEA", "AVERAGEIF", "AVERAGEIFS", "BETA.DIST", "BETA.INV", "BINOM.DIST", "BINOM.DIST.RANGE", "BINOM.INV", "CHISQ.DIST", "CHISQ.DIST.RT", "CHISQ.INV", "CHISQ.INV.RT", "CHISQ.TEST", "CONFIDENCE.NORM", "CONFIDENCE.T", "CORREL", "COUNT", "COUNTA", "COUNTBLANK", "COUNTIF", "COUNTIFS", "COVARIANCE.P", "COVARIANCE.S", "DEVSQ", "EXPON.DIST", "F.DIST", "F.DIST.RT", "F.INV", "F.INV.RT", "F.TEST", "FISHER", "FISHERINV", "FORECAST.ETS", "FORECAST.ETS.CONFINT", "FORECAST.ETS.SEASONALITY", "FORECAST.ETS.STAT", "FORECAST.LINEAR", "FREQUENCY", "GAMMA", "GAMMA.DIST", "GAMMA.INV", "GAMMALN", "GAMMALN.PRECISE", "GAUSS", "GEOMEAN", "GROWTH", "HARMEAN", "HYPGEOM.DIST", "INTERCEPT", "KURT", "LARGE", "LINEST", "LOGEST", "LOGNORM.DIST", "LOGNORM.INV", "MAX", "MAXA", "MAXIFS", "MEDIAN", "MIN", "MINA", "MINIFS", "MODE.MULT", "MODE.SNGL", "NEGBINOM.DIST", "NORM.DIST", "NORM.INV", "NORM.S.DIST", "NORM.S.INV", "PEARSON", "PERCENTILE.EXC", "PERCENTILE.INC", "PERCENTRANK.EXC", "PERCENTRANK.INC", "PERMUT", "PERMUTATIONA", "PHI", "POISSON.DIST", "PROB", "QUARTILE.EXC", "QUARTILE.INC", "RANK.AVG", "RANK.EQ", "RSQ", "SKEW", "SKEW.P", "SLOPE", "SMALL", "STANDARDIZE", "STDEV.P", "STDEV.S", "STDEVA", "STDEVPA", "STEYX", "T.DIST", "T.DIST.2T", "T.DIST.RT", "T.INV", "T.INV.2T", "T.TEST", "TREND", "TRIMMEAN", "VAR.P", "VAR.S", "VARA", "VARPA", "WEIBULL.DIST", "Z.TEST" };
    private static readonly HashSet<string> InformationFunctions = new(StringComparer.OrdinalIgnoreCase) { "CELL", "ERROR.TYPE", "INFO", "ISBLANK", "ISERR", "ISERROR", "ISEVEN", "ISFORMULA", "ISLOGICAL", "ISNA", "ISNONTEXT", "ISNUMBER", "ISODD", "ISOMITTED", "ISREF", "ISTEXT", "N", "NA", "SHEET", "SHEETS", "TYPE" };
    private static readonly HashSet<string> CompatibilityFunctions = new(StringComparer.OrdinalIgnoreCase) { "BETADIST", "BETAINV", "BINOMDIST", "CEILING", "CHIDIST", "CHIINV", "CHITEST", "CONCATENATE", "CONFIDENCE", "COVAR", "CRITBINOM", "EXPONDIST", "FDIST", "FINV", "FLOOR", "FORECAST", "FTEST", "GAMMADIST", "GAMMAINV", "HYPGEOMDIST", "LOGINV", "LOGNORMDIST", "MODE", "NEGBINOMDIST", "NORMDIST", "NORMINV", "NORMSDIST", "NORMSINV", "PERCENTILE", "PERCENTRANK", "POISSON", "QUARTILE", "RANK", "STDEV", "STDEVP", "TDIST", "TINV", "TTEST", "VAR", "VARP", "WEIBULL", "ZTEST" };
    private static readonly HashSet<string> WebFunctions = new(StringComparer.OrdinalIgnoreCase) { "ENCODEURL", "FILTERXML", "WEBSERVICE" };
    private static readonly HashSet<string> CubeFunctions = new(StringComparer.OrdinalIgnoreCase) { "CUBEKPIMEMBER", "CUBEMEMBER", "CUBEMEMBERPROPERTY", "CUBERANKEDMEMBER", "CUBESET", "CUBESETCOUNT", "CUBEVALUE" };
    private static readonly HashSet<string> FinancialFunctions = ["PMT", "NPV", "IRR", "RATE", "PV", "FV", "IPMT", "PPMT", "NPER", "CUMIPMT", "CUMPRINC", "EFFECT", "NOMINAL", "MIRR", "XIRR", "XNPV", "RRI", "PDURATION", "FVSCHEDULE", "DB", "DDB", "VDB", "SYD", "AMORDEGRC", "AMORLINC", "DOLLARDE", "DOLLARFR", "DISC", "INTRATE", "RECEIVED", "ACCRINT", "ACCRINTM", "TBILLEQ", "TBILLPRICE", "TBILLYIELD", "COUPDAYBS", "COUPDAYS", "COUPDAYSNC", "COUPNCD", "COUPNUM", "COUPPCD", "PRICE", "YIELD", "PRICEDISC", "PRICEMAT", "YIELDDISC", "YIELDMAT", "DURATION", "MDURATION", "ODDFPRICE", "ODDFYIELD", "ODDLPRICE", "ODDLYIELD"];
    private static readonly HashSet<string> DatabaseFunctions = ["DSUM", "DAVERAGE", "DCOUNT", "DCOUNTA", "DGET", "DMAX", "DMIN", "DPRODUCT", "DSTDEV", "DSTDEVP", "DVAR", "DVARP"];
    private static readonly HashSet<string> EngineeringFunctions = ["BESSELI", "BESSELJ", "BESSELK", "BESSELY", "BIN2DEC", "BIN2HEX", "BIN2OCT", "BITAND", "BITLSHIFT", "BITOR", "BITRSHIFT", "BITXOR", "COMPLEX", "CONVERT", "DEC2BIN", "DEC2HEX", "DEC2OCT", "DELTA", "ERF", "ERF.PRECISE", "ERFC", "ERFC.PRECISE", "GESTEP", "HEX2BIN", "HEX2DEC", "HEX2OCT", "IMABS", "IMAGINARY", "IMARGUMENT", "IMCONJUGATE", "IMCOS", "IMCOSH", "IMCOT", "IMCSC", "IMCSCH", "IMDIV", "IMEXP", "IMLN", "IMLOG10", "IMLOG2", "IMPOWER", "IMPRODUCT", "IMREAL", "IMSEC", "IMSECH", "IMSIN", "IMSINH", "IMSQRT", "IMSUB", "IMSUM", "IMTAN", "OCT2BIN", "OCT2DEC", "OCT2HEX"];

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
