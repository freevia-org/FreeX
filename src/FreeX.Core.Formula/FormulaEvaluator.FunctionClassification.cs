namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private static readonly HashSet<string> AggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "AVERAGE", "AVERAGEA", "MIN", "MINA", "MAX", "MAXA", "COUNT", "COUNTA", "AND", "OR", "CONCAT",
        "STDEV", "STDEV.S", "MEDIAN",
        "PRODUCT", "SUMSQ", "SUMX2MY2", "SUMX2PY2", "SUMXMY2", "XOR", "GCD", "LCM",
        "VAR", "VAR.S", "VARA", "VARP", "VAR.P", "VARPA", "STDEVP", "STDEV.P", "STDEVA", "STDEVPA",
        "GEOMEAN", "HARMEAN", "AVEDEV",
        "MODE", "MODE.SNGL", "MODE.MULT",
        "CONCATENATE",
        "NPV"
    };

    private static readonly HashSet<string> DirectTextCoercingAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "AVERAGE", "AVERAGEA", "MIN", "MINA", "MAX", "MAXA", "COUNT", "PRODUCT", "SUMSQ", "SUMX2MY2", "SUMX2PY2", "SUMXMY2",
        "STDEV", "STDEV.S", "STDEVP", "STDEV.P", "STDEVA", "STDEVPA",
        "VAR", "VAR.S", "VAR.P", "VARP", "VARA", "VARPA",
        "MEDIAN",
        "GEOMEAN", "HARMEAN", "AVEDEV", "DEVSQ",
        "COVAR", "COVARIANCE.P", "COVARIANCE.S", "INTERCEPT", "PEARSON", "RSQ", "SLOPE", "STEYX",
        "MODE", "MODE.SNGL", "MODE.MULT",
        "NPV",
        "GCD", "LCM"
    };

    private static readonly HashSet<string> ReferenceProvenanceAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "AVERAGE", "AVERAGEA", "MIN", "MINA", "MAX", "MAXA", "COUNT", "COUNTA", "PRODUCT", "SUMSQ", "SUMX2MY2", "SUMX2PY2", "SUMXMY2", "AND", "OR", "XOR",
        "STDEV", "STDEV.S", "STDEVP", "STDEV.P", "STDEVA", "STDEVPA",
        "VAR", "VAR.S", "VAR.P", "VARP", "VARA", "VARPA",
        "MEDIAN",
        "GEOMEAN", "HARMEAN", "AVEDEV",
        "COVAR", "COVARIANCE.P", "COVARIANCE.S", "INTERCEPT", "PEARSON", "RSQ", "SLOPE", "STEYX",
        "MODE", "MODE.SNGL", "MODE.MULT",
        "NPV",
        "GCD", "LCM"
    };

    private static readonly HashSet<string> StructuredRangeFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "VLOOKUP", "HLOOKUP", "INDEX", "MATCH", "XMATCH",
        "SUMIF", "COUNTIF", "AVERAGEIF",
        "SUMPRODUCT",
        "LARGE", "SMALL", "RANK", "RANK.EQ", "RANK.AVG", "DEVSQ",
        "MULTINOMIAL", "SERIESSUM", "SUMSQ", "SUMX2MY2", "SUMX2PY2", "SUMXMY2",
        "MMULT", "MINVERSE", "MDETERM", "MUNIT",
        "SUMIFS", "COUNTIFS", "AVERAGEIFS", "MAXIFS", "MINIFS",
        "XLOOKUP",
        "WORKDAY", "NETWORKDAYS", "WORKDAY.INTL", "NETWORKDAYS.INTL",
        "CORREL", "COVAR", "COVARIANCE.P", "COVARIANCE.S",
        "FORECAST", "FORECAST.LINEAR",
        "INTERCEPT", "PEARSON", "RSQ", "SLOPE", "STEYX",
        "PERCENTILE", "PERCENTILE.INC", "PERCENTILE.EXC",
        "QUARTILE", "QUARTILE.INC", "QUARTILE.EXC",
        "TRIMMEAN",
        "PERCENTRANK", "PERCENTRANK.INC", "PERCENTRANK.EXC",
        "PROB",
        "PERCENTOF",
        "LOOKUP",
        "IRR",
        "RANDARRAY",
        "FILTER", "SORT", "SORTBY", "TAKE", "DROP", "TRANSPOSE",
        "CHOOSEROWS", "CHOOSECOLS", "VSTACK", "HSTACK",
        "TOROW", "TOCOL", "WRAPROWS", "WRAPCOLS", "EXPAND", "UNIQUE",
        "TRIMRANGE",
        "SUBTOTAL",
        "DSUM", "DAVERAGE", "DCOUNT", "DCOUNTA", "DGET",
        "DMAX", "DMIN", "DPRODUCT", "DSTDEV", "DSTDEVP",
        "DVAR", "DVARP",
        "ROW", "COLUMN", "ROWS", "COLUMNS", "AREAS", "SHEET", "SHEETS", "COUNTBLANK",
        "AGGREGATE", "CELL", "GETPIVOTDATA",
        "TTEST", "T.TEST", "ZTEST", "Z.TEST", "FTEST", "F.TEST", "CHITEST", "CHISQ.TEST",
        "FREQUENCY",
        "MIRR", "XIRR", "XNPV", "FVSCHEDULE",
        "PMT", "PV", "FV", "NPER", "RATE", "ISPMT", "IPMT", "PPMT",
        "CUMIPMT", "CUMPRINC",
        "EFFECT", "NOMINAL", "RRI", "PDURATION",
        "SLN", "SYD", "DB", "DDB", "VDB", "AMORDEGRC", "AMORLINC",
        "DOLLARDE", "DOLLARFR",
        "DISC", "INTRATE", "RECEIVED",
        "ACCRINT", "ACCRINTM", "ODDFPRICE", "ODDFYIELD", "ODDLPRICE", "ODDLYIELD",
        "TBILLEQ", "TBILLPRICE", "TBILLYIELD",
        "COUPDAYBS", "COUPDAYS", "COUPDAYSNC", "COUPNCD", "COUPNUM", "COUPPCD",
        "PRICE", "YIELD", "PRICEDISC", "PRICEMAT", "YIELDDISC", "YIELDMAT", "DURATION", "MDURATION",
        "MAP", "REDUCE", "SCAN", "BYROW", "BYCOL",
        "TEXTJOIN", "TEXTBEFORE", "TEXTAFTER", "TEXTSPLIT",
        "REGEXTEST", "REGEXEXTRACT", "REGEXREPLACE",
        "EXACT", "CODE", "CHAR", "LEN", "LENB", "LEFT", "LEFTB", "RIGHT", "RIGHTB", "MID", "MIDB", "REPLACE", "REPLACEB",
        "FIND", "FINDB", "SEARCH", "SEARCHB",
        "TRIM", "UPPER", "LOWER", "PROPER", "CLEAN",
        "ARABIC", "ROMAN",
        "TEXT", "VALUE",
        "SUBSTITUTE", "REPT", "CONCATENATE",
        "FIXED", "DOLLAR", "T", "HYPERLINK", "ENCODEURL", "FILTERXML", "BAHTTEXT",
        "VALUETOTEXT", "ARRAYTOTEXT",
        "ASC", "DBCS", "JIS",
        "UNICHAR", "UNICODE", "NUMBERVALUE",
        "ABS", "SQRT", "INT", "SIGN",
        "MOD", "POWER", "LOG", "LOG10", "QUOTIENT", "CEILING", "FLOOR", "MROUND",
        "SIN", "SINH", "COS", "COSH", "TAN", "TANH", "DEGREES", "RADIANS",
        "ASIN", "ASINH", "ACOS", "ACOSH", "ATAN", "ATAN2", "ATANH", "LN", "EXP", "FACT",
        "ROUND", "ROUNDUP", "ROUNDDOWN", "TRUNC",
        "ISBLANK", "ISNUMBER", "ISTEXT", "ISERROR", "ISERR", "ISNA", "ISNONTEXT", "ISLOGICAL", "NOT",
        "ISEVEN", "ISODD", "ODD", "EVEN",
        "DATE", "TIME",
        "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND",
        "WEEKDAY", "WEEKNUM", "ISOWEEKNUM", "EDATE", "EOMONTH", "DATEDIF",
        "DATEVALUE", "TIMEVALUE",
        "DAYS", "DAYS360", "YEARFRAC",
        "CEILING.MATH", "CEILING.PRECISE", "FLOOR.MATH", "FLOOR.PRECISE", "ISO.CEILING", "SQRTPI", "SERIESSUM",
        "RANDBETWEEN",
        "N", "TYPE", "ERROR.TYPE",
        "COMBIN", "COMBINA", "FACTDOUBLE", "PERMUT", "PERMUTATIONA",
        "ACOT", "ACOTH", "COT", "COTH", "CSC", "CSCH", "SEC", "SECH",
        "BITAND", "BITOR", "BITXOR", "BITLSHIFT", "BITRSHIFT",
        "BASE", "DECIMAL",
        "BIN2DEC", "HEX2DEC", "OCT2DEC",
        "DEC2BIN", "DEC2HEX", "DEC2OCT",
        "BIN2HEX", "BIN2OCT", "HEX2BIN", "HEX2OCT", "OCT2BIN", "OCT2HEX",
        "COMPLEX", "DELTA", "ERF", "ERF.PRECISE", "ERFC", "ERFC.PRECISE", "GESTEP", "IMABS", "IMARGUMENT", "IMAGINARY", "IMCONJUGATE", "IMCOS", "IMCOSH", "IMCOT", "IMCSC", "IMCSCH", "IMDIV", "IMEXP", "IMLN", "IMLOG10", "IMLOG2", "IMPOWER", "IMPRODUCT", "IMREAL", "IMSEC", "IMSECH", "IMSIN", "IMSINH", "IMSQRT", "IMSUB", "IMSUM", "IMTAN",
        "BESSELI", "BESSELJ", "BESSELK", "BESSELY",
        "CONVERT",
        "NORMDIST", "NORM.DIST", "NORMINV", "NORM.INV", "NORMSDIST", "NORM.S.DIST", "NORMSINV", "NORM.S.INV", "PHI", "GAUSS", "FISHER", "FISHERINV", "STANDARDIZE",
        "GAMMA", "GAMMALN", "GAMMALN.PRECISE", "GAMMADIST", "GAMMA.DIST", "GAMMAINV", "GAMMA.INV",
        "LOGNORM.DIST", "LOGNORMDIST", "LOGNORM.INV", "LOGINV",
        "BETA.DIST", "BETADIST", "BETA.INV", "BETAINV",
        "EXPONDIST", "EXPON.DIST", "WEIBULL", "WEIBULL.DIST", "POISSON", "POISSON.DIST",
        "TDIST", "T.DIST", "T.DIST.RT", "T.DIST.2T", "TINV", "T.INV", "T.INV.2T",
        "FDIST", "F.DIST", "F.DIST.RT", "FINV", "F.INV", "F.INV.RT",
        "CHIDIST", "CHISQ.DIST", "CHISQ.DIST.RT", "CHIINV", "CHISQ.INV", "CHISQ.INV.RT",
        "BINOMDIST", "BINOM.DIST", "BINOM.DIST.RANGE", "CRITBINOM", "BINOM.INV", "NEGBINOMDIST", "NEGBINOM.DIST", "HYPGEOMDIST", "HYPGEOM.DIST",
        "CONFIDENCE", "CONFIDENCE.NORM", "CONFIDENCE.T"
    };

    private static readonly HashSet<string> SingleCellReferenceRangeFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ROW", "COLUMN", "ROWS", "COLUMNS", "AREAS", "SHEET", "SHEETS", "COUNTBLANK", "CELL", "GETPIVOTDATA",
        // SUBTOTAL/AGGREGATE must see a bare single-cell reference argument (e.g. A5, not A5:A5)
        // as a 1-cell RangeValue carrying real sheet/row provenance, exactly like a multi-cell
        // range, so ShouldSkipSubtotalRow/IsAggregateRowHidden and the nested-subtotal exclusion
        // can run against it. Without this, a bare CellRefNode falls through to a plain scalar
        // with no row provenance and the hidden-row/nested-aggregate checks are silently skipped
        // (see R57-formula-subtotal-aggregate-5-2).
        "SUBTOTAL", "AGGREGATE"
    };

    private static bool IsAggregateFunction(string name) =>
        AggregateFunctions.Contains(name);

    private static bool IsDirectTextCoercingAggregate(string name) =>
        DirectTextCoercingAggregates.Contains(name);

    private static bool IsReferenceProvenanceAggregate(string name) =>
        ReferenceProvenanceAggregates.Contains(name);

    private static bool IsSingleCellReferenceProvenanceArgument(
        string name,
        int argIndex,
        bool preservesReferenceProvenance) =>
        preservesReferenceProvenance && (name != "NPV" || argIndex > 0);

    private static bool IsStructuredRangeFunction(string name) =>
        StructuredRangeFunctions.Contains(name);

    private static bool IsSingleCellReferenceRangeFunction(string name) =>
        SingleCellReferenceRangeFunctions.Contains(name);

    // SUBTOTAL/AGGREGATE's leading control arguments (SUBTOTAL's function_num; AGGREGATE's
    // function_num and options) must NOT be wrapped into a 1x1 RangeValue when they happen to be
    // bare cell references (e.g. =AGGREGATE(B1,C1,A5)) -- only the actual data/range arguments
    // need the RangeValue-with-provenance treatment for hidden-row/nested-aggregate exclusion.
    // Wrapping the control args breaks ToNumber(func_num)/ToNumber(options), which has no
    // RangeValue case (see R58-meta-1).
    private static bool IsSingleCellReferenceRangeDataArgument(string name, int argIndex) =>
        name switch
        {
            "SUBTOTAL" => argIndex >= 1,
            "AGGREGATE" => argIndex >= 2,
            _ => true
        };

    private static bool IsConditionalAggregateRangeArgument(string name, int argIndex) =>
        name switch
        {
            "SUMIF" or "AVERAGEIF" => argIndex is 0 or 2,
            "COUNTIF" => argIndex == 0,
            "SUMIFS" or "AVERAGEIFS" or "MAXIFS" or "MINIFS" => argIndex == 0 || (argIndex > 0 && (argIndex & 1) == 1),
            "COUNTIFS" => (argIndex & 1) == 0,
            _ => false
        };
}