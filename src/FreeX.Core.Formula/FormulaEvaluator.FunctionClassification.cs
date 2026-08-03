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

    // R84-calc-crosssheet-3d-5-2: Excel restricts 3-D sheet-span references (e.g.
    // Sheet1:Sheet3!A1) to exactly this subset of aggregate functions -- every other function,
    // including several members of AggregateFunctions above (MEDIAN, MODE*, AND, OR, XOR,
    // CONCAT(ENATE), GEOMEAN, HARMEAN, AVEDEV, GCD, LCM, SUMSQ/SUMX2*/SUMXMY2, NPV), rejects a
    // 3-D span with #VALUE!. AggregateFunctions itself must stay broader than this set: it also
    // gates unrelated concerns (variadic arity for MEDIAN/AND/OR/CONCAT, and flattening an
    // array/named-formula RangeValue result into scalar args), which those extra functions
    // legitimately need. Only the sheet-span-expansion decision in FormulaEvaluator.Functions.cs
    // (the RangeRefNode-with-EndSheetName branch, and its named-formula-span counterpart) should
    // consult this narrower set.
    private static readonly HashSet<string> SheetSpanAggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "AVERAGE", "AVERAGEA", "COUNT", "COUNTA", "MAX", "MAXA", "MIN", "MINA", "PRODUCT",
        "STDEV", "STDEV.S", "STDEVA", "STDEVP", "STDEV.P", "STDEVPA",
        "VAR", "VAR.S", "VARA", "VARP", "VAR.P", "VARPA"
    };

    private static readonly HashSet<string> DirectTextCoercingAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "AVERAGE", "AVERAGEA", "MIN", "MINA", "MAX", "MAXA", "COUNT", "PRODUCT", "SUMSQ", "SUMX2MY2", "SUMX2PY2", "SUMXMY2",
        "STDEV", "STDEV.S", "STDEVP", "STDEV.P", "STDEVA", "STDEVPA",
        "VAR", "VAR.S", "VAR.P", "VARP", "VARA", "VARPA",
        "MEDIAN",
        "GEOMEAN", "HARMEAN", "AVEDEV", "DEVSQ",
        "COVAR", "COVARIANCE.P", "COVARIANCE.S", "INTERCEPT", "PEARSON", "RSQ", "SLOPE", "STEYX",
        "CORREL", "FORECAST", "FORECAST.LINEAR",
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
        // R84-formula-stat-regression-5-1: CORREL/PEARSON are literally the same computation
        // (Pearson just calls Correl) and FORECAST(.LINEAR) shares the same paired-source
        // machinery, so a bare single-cell reference argument (e.g. A1, not A1:A1) must be
        // wrapped into a ReferencedScalarValue exactly like their SLOPE/INTERCEPT/RSQ/STEYX/
        // COVARIANCE.P/S siblings -- otherwise BuiltInFunctions.StatisticalCore.Regression.cs's
        // BuildPairedSource falls to its raw-ToNumber fallback and throws #VALUE! on a
        // non-numeric bare cell (text/blank/logical) instead of ignoring it like Excel does.
        "CORREL", "FORECAST", "FORECAST.LINEAR",
        "MODE", "MODE.SNGL", "MODE.MULT",
        "NPV",
        "GCD", "LCM"
    };

    // R71-formula-logical-info-4-1: the error-inspecting IS*/N/TYPE/ERROR.TYPE family must be able
    // to see a RangeMaterializationErrorValue argument (e.g. a disjoint intersection's #NULL!, or a
    // named-range endpoint's #NAME?) and report on it -- ISERROR(A1:A2 C1:C2)=TRUE,
    // ERROR.TYPE(A1:A2 C1:C2)=1 -- rather than having it short-circuit the whole call the way every
    // other function (SUM, VLOOKUP, ...) must. ISREF is included for documentation even though the
    // evaluator already dispatches it to an AST-aware path before this classification is consulted.
    private static readonly HashSet<string> ErrorInspectingFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ISERROR", "ISERR", "ISNA", "ISBLANK", "ISNUMBER", "ISTEXT", "ISNONTEXT", "ISLOGICAL", "ISREF",
        "N", "TYPE", "ERROR.TYPE"
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

    private static bool IsSheetSpanAggregateFunction(string name) =>
        SheetSpanAggregateFunctions.Contains(name);

    private static bool IsErrorInspectingFunction(string name) =>
        ErrorInspectingFunctions.Contains(name);

    private static bool IsDirectTextCoercingAggregate(string name) =>
        DirectTextCoercingAggregates.Contains(name);

    private static bool IsReferenceProvenanceAggregate(string name) =>
        ReferenceProvenanceAggregates.Contains(name);

    private static bool IsSingleCellReferenceProvenanceArgument(
        string name,
        int argIndex,
        bool preservesReferenceProvenance) =>
        preservesReferenceProvenance &&
        (name != "NPV" || argIndex > 0) &&
        // FORECAST(.LINEAR)'s first argument (x, the value to forecast) is a plain scalar that
        // BuiltInFunctions.StatisticalCore.Regression.cs's Forecast() coerces directly via
        // ToNumber -- unlike known_ys/known_xs (args 1/2), it never goes through the
        // ReferencedScalarValue-aware BuildPairedSource/TryReferencedNumber path, so wrapping a
        // bare cell ref there would make ToNumber throw #VALUE! on its unhandled type instead of
        // reading the number (same reasoning as the NPV rate-argument exclusion above).
        (name is not ("FORECAST" or "FORECAST.LINEAR") || argIndex > 0);

    // R94-formula-union-selection-range: the subset of StructuredRangeFunctions whose
    // range/array argument is consumed as a flat, shape-agnostic bag of numeric cell values
    // (CollectRangeNumbers/CollectRangeNumbersForSelection iterate a RangeValue's Cells in
    // row-major order regardless of RowCount/ColCount) via each function's own
    // "args[i] is RangeValue r ? r : wrap-as-1x1" fallback -- e.g. LargeScalar/SmallScalar's
    // range, RankScalar/RankAvgScalar's range, PercentileIncScalar/QuartileIncScalar/
    // TrimmeanScalar's rv, PercentrankIncScalar/PercentrankExcScalar's rv, Countblank's range.
    // A parenthesized union argument (e.g. LARGE((A1:A5,C1:C5),1)) evaluates to a UnionValue,
    // not a RangeValue, so that per-function fallback silently misreads the whole UnionValue
    // object as one opaque scalar cell -- CollectRangeNumbersForSelection's cell-type switch
    // doesn't recognize UnionValue, so it contributes zero numbers instead of every cell across
    // every area (see BuiltInFunctions.StatisticalCore.Helpers.cs). Since these functions only
    // ever flatten their range argument (never index into its 2-D shape), it's safe to
    // materialize a UnionValue argument into one synthetic Nx1 RangeValue holding every area's
    // cells concatenated in order (MaterializeUnionRangeValue in FormulaEvaluator.References.cs)
    // -- unlike VLOOKUP/INDEX/MATCH/FILTER/SORT/MMULT and the rest of StructuredRangeFunctions,
    // which index by row/column shape or pair multiple same-shaped ranges and are NOT safe to
    // treat this way without per-function verification (left as siblingLeads).
    //
    // R97-union-deferred-backlog additions:
    //  - DEVSQ: its variadic loop (BuiltInFunctions.StatisticalCore.Variance.cs) has its own
    //    "args[i] is RangeValue rv -> CollectRangeNumbers(rv)" case-by-case switch with NO
    //    range-wrap fallback for anything else -- a raw UnionValue argument matched none of the
    //    switch's arms and was silently skipped (contributed zero numbers) rather than misread,
    //    a differently-shaped bug from LARGE/SMALL's "zero across the board" #NUM!. DEVSQ only
    //    ever flattens its arguments (same as SUM/AVERAGE's shape-agnostic bag, just without
    //    AggregateFunctions' variadic-scalar-spread contract because DEVSQ is a
    //    StructuredRangeFunction), so materializing here is exactly as safe as for LARGE/SMALL.
    //  - FREQUENCY: takes two INDEPENDENT array arguments, data_array and bins_array, each of
    //    which is separately flattened into a flat list of numbers (BuiltInFunctions.
    //    StatisticalDistributions.Descriptive.cs's Frequency: "args[0] is RangeValue rvd" and
    //    "args[1] is RangeValue rvb" are two unrelated checks, never paired/shape-compared against
    //    each other unlike MAXIFS/MINIFS's range+criteria pairing below). The per-argument
    //    expansion loop in FormulaEvaluator.Functions.cs that consults this set runs once per
    //    argument position independently, so adding FREQUENCY here materializes data_array and/or
    //    bins_array separately, whichever one(s) are unions -- safe for the same reason DEVSQ is.
    //    Before this fix a union data_array/bins_array matched neither Frequency's
    //    "is RangeValue"/"TryCellNumber" branches and silently contributed nothing (an empty
    //    data set / zero bins) instead of erroring or computing the right answer.
    //
    // R120 addition:
    //  - TEXTJOIN: its per-argument FlattenTextjoinArgument (BuiltInFunctions.TextAdvanced.cs)
    //    special-cases only RangeValue -- anything else (including a raw UnionValue) falls to the
    //    `else { text.Add(ToText(value)); }` branch, and ToText has no UnionValue case either, so
    //    it fell to its `_ => v.ToString()` default and embedded the literal .NET record dump
    //    (e.g. "UnionValue { Areas = System.Collections.Generic.List`1[...] }") into the joined
    //    text instead of every cell across the union's areas. TEXTJOIN only ever flattens each of
    //    its text arguments into a bag of strings in row-major order (never indexes 2-D shape),
    //    the exact same shape contract as DEVSQ/COUNTBLANK above, so it is safe to materialize
    //    here -- each variadic text argument position is checked independently by the per-argument
    //    expansion loop in FormulaEvaluator.Functions.cs, so a union in any one of TEXTJOIN's
    //    delimiter/ignore_empty/text* positions is handled without needing per-function code.
    private static readonly HashSet<string> UnionMaterializableRangeFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "LARGE", "SMALL", "RANK", "RANK.EQ", "RANK.AVG",
        "PERCENTILE", "PERCENTILE.INC", "PERCENTILE.EXC",
        "QUARTILE", "QUARTILE.INC", "QUARTILE.EXC",
        "TRIMMEAN",
        "PERCENTRANK", "PERCENTRANK.INC", "PERCENTRANK.EXC",
        "COUNTBLANK",
        "DEVSQ", "FREQUENCY",
        "TEXTJOIN"
    };

    private static bool IsUnionMaterializableRangeFunction(string name) =>
        UnionMaterializableRangeFunctions.Contains(name);

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