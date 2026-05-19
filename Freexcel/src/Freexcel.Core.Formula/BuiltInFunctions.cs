using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

/// <summary>
/// Registry of built-in spreadsheet functions.
/// Phase 1 provides the 16 most essential functions per the build plan.
/// </summary>
public static class BuiltInFunctions
{
    /// <summary>
    /// Delegate type for a built-in function.
    /// Receives evaluated arguments and a context for resolving ranges.
    /// </summary>
    public delegate ScalarValue FormulaFunction(IReadOnlyList<ScalarValue> args, IEvalContext ctx);

    private static readonly Dictionary<string, (FormulaFunction Func, int MinArgs, int MaxArgs)> Functions = new()
    {
        // ── Existing Phase-1 functions ──────────────────────────────────────
        ["SUM"]         = (Sum, 1, 255),
        ["AVERAGE"]     = (Average, 1, 255),
        ["MIN"]         = (Min, 1, 255),
        ["MAX"]         = (Max, 1, 255),
        ["COUNT"]       = (Count, 1, 255),
        ["COUNTA"]      = (CountA, 1, 255),
        ["IF"]          = (If, 2, 3),
        ["AND"]         = (And, 1, 255),
        ["OR"]          = (Or, 1, 255),
        ["NOT"]         = (Not, 1, 1),
        ["ROUND"]       = (Round, 2, 2),
        ["ABS"]         = (Abs, 1, 1),
        ["CONCAT"]      = (Concat, 1, 255),
        ["LEN"]         = (Len, 1, 1),
        ["LEFT"]        = (Left, 1, 2),
        ["RIGHT"]       = (Right, 1, 2),
        ["NOW"]         = (Now, 0, 0),
        ["TODAY"]       = (Today, 0, 0),
        ["RAND"]        = (Rand, 0, 0),

        // ── Phase 4.2: Error handling ────────────────────────────────────────
        ["IFERROR"]     = (IfError, 2, 2),
        ["IFNA"]        = (IfNa, 2, 2),
        ["NA"]          = (NaFunc, 0, 0),

        // ── Phase 4.2: Lookup ────────────────────────────────────────────────
        ["VLOOKUP"]     = (Vlookup, 3, 4),
        ["HLOOKUP"]     = (Hlookup, 3, 4),
        ["INDEX"]       = (Index, 2, 3),
        ["MATCH"]       = (Match, 2, 3),
        ["XMATCH"]      = (Xmatch, 2, 4),

        // ── Phase 4.2: Conditional aggregation ──────────────────────────────
        ["SUMIF"]       = (Sumif, 2, 3),
        ["COUNTIF"]     = (Countif, 2, 2),
        ["AVERAGEIF"]   = (Averageif, 2, 3),

        // ── Phase 4.2: Text ──────────────────────────────────────────────────
        ["TEXT"]        = (TextFunc, 2, 2),
        ["TRIM"]        = (Trim, 1, 1),
        ["UPPER"]       = (Upper, 1, 1),
        ["LOWER"]       = (Lower, 1, 1),
        ["PROPER"]      = (Proper, 1, 1),
        ["SUBSTITUTE"]  = (Substitute, 3, 4),
        ["FIND"]        = (Find, 2, 3),
        ["SEARCH"]      = (Search, 2, 3),
        ["MID"]         = (Mid, 3, 3),
        ["REPT"]        = (Rept, 2, 2),
        ["VALUE"]       = (ValueFunc, 1, 1),

        // ── Phase 4.2: Date & time ───────────────────────────────────────────
        ["DATE"]        = (Date, 3, 3),
        ["YEAR"]        = (Year, 1, 1),
        ["MONTH"]       = (Month, 1, 1),
        ["DAY"]         = (Day, 1, 1),
        ["HOUR"]        = (Hour, 1, 1),
        ["MINUTE"]      = (Minute, 1, 1),
        ["SECOND"]      = (Second, 1, 1),
        ["WEEKDAY"]     = (Weekday, 1, 2),
        ["EDATE"]       = (Edate, 2, 2),
        ["DATEDIF"]     = (Datedif, 3, 3),

        // ── Phase 4.2: Math ──────────────────────────────────────────────────
        ["MOD"]         = (Mod, 2, 2),
        ["POWER"]       = (Power, 2, 2),
        ["SQRT"]        = (Sqrt, 1, 1),
        ["INT"]         = (IntFunc, 1, 1),
        ["CEILING"]     = (Ceiling, 2, 2),
        ["FLOOR"]       = (Floor, 2, 2),
        ["RANDBETWEEN"] = (Randbetween, 2, 2),
        ["SIGN"]        = (Sign, 1, 1),
        ["LOG"]         = (Log, 1, 2),
        ["LN"]          = (Ln, 1, 1),
        ["EXP"]         = (Exp, 1, 1),
        ["PI"]          = (Pi, 0, 0),
        ["FACT"]        = (Fact, 1, 1),

        // ── Phase 4.2: Statistical ───────────────────────────────────────────
        ["LARGE"]       = (Large, 2, 2),
        ["SMALL"]       = (Small, 2, 2),
        ["RANK"]        = (Rank, 2, 3),
        ["RANK.EQ"]     = (RankEq, 2, 3),
        ["RANK.AVG"]    = (RankAvg, 2, 3),
        ["STDEV"]       = (Stdev, 1, 255),
        ["STDEV.S"]     = (Stdev, 1, 255),
        ["MEDIAN"]      = (Median, 1, 255),
        ["DEVSQ"]       = (Devsq, 1, 255),

        // ── Phase 5: Additional commonly-used functions ──────────────────────

        // Multi-criteria aggregation
        ["SUMIFS"]      = (Sumifs, 3, 255),
        ["COUNTIFS"]    = (Countifs, 2, 255),
        ["AVERAGEIFS"]  = (Averageifs2, 3, 255),

        // Modern lookup
        ["XLOOKUP"]     = (Xlookup, 3, 6),

        // Multi-condition logic
        ["IFS"]         = (Ifs, 2, 255),
        ["SWITCH"]      = (Switch, 3, 255),

        // IS functions
        ["ISBLANK"]     = (Isblank, 1, 1),
        ["ISNUMBER"]    = (Isnumber, 1, 1),
        ["ISTEXT"]      = (Istext, 1, 1),
        ["ISERROR"]     = (Iserror, 1, 1),
        ["ISNA"]        = (Isna, 1, 1),
        ["ISLOGICAL"]   = (Islogical, 1, 1),

        // Reference helpers
        ["ROW"]         = (Row, 0, 1),
        ["COLUMN"]      = (Column, 0, 1),
        ["ROWS"]        = (Rows, 1, 1),
        ["COLUMNS"]     = (Columns, 1, 1),

        // Text
        ["TEXTJOIN"]    = (Textjoin, 3, 255),

        // Count
        ["COUNTBLANK"]  = (Countblank, 1, 1),

        // Misc
        ["CHOOSE"]      = (Choose, 2, 255),
        ["SUMPRODUCT"]  = (Sumproduct, 1, 255),
        ["ROUNDDOWN"]   = (Rounddown, 2, 2),
        ["ROUNDUP"]     = (Roundup, 2, 2),
        ["TRUNC"]       = (Trunc, 1, 2),
        ["EXACT"]       = (Exact, 2, 2),
        ["CODE"]        = (Code, 1, 1),
        ["CHAR"]        = (Char, 1, 1),

        // ── Phase 4a: Math / Trig ────────────────────────────────────────────
        ["SIN"]      = (Sin, 1, 1),
        ["COS"]      = (Cos, 1, 1),
        ["TAN"]      = (Tan, 1, 1),
        ["ASIN"]     = (Asin, 1, 1),
        ["ACOS"]     = (Acos, 1, 1),
        ["ATAN"]     = (Atan, 1, 1),
        ["ATAN2"]    = (Atan2Func, 2, 2),
        ["DEGREES"]  = (Degrees, 1, 1),
        ["RADIANS"]  = (Radians, 1, 1),
        ["PRODUCT"]  = (Product, 1, 255),
        ["QUOTIENT"] = (Quotient, 2, 2),
        ["GCD"]      = (Gcd, 1, 255),
        ["LCM"]      = (Lcm, 1, 255),
        ["MROUND"]   = (Mround, 2, 2),
        ["COMBIN"]   = (Combin, 2, 2),
        ["PERMUT"]   = (Permut, 2, 2),
        ["ODD"]      = (Odd, 1, 1),
        ["EVEN"]     = (Even, 1, 1),

        // ── Phase 4a: Date / Time ────────────────────────────────────────────
        ["TIME"]         = (TimeFunc, 3, 3),
        ["TIMEVALUE"]    = (Timevalue, 1, 1),
        ["DATEVALUE"]    = (Datevalue, 1, 1),
        ["EOMONTH"]      = (Eomonth, 2, 2),
        ["WEEKNUM"]      = (Weeknum, 1, 2),
        ["ISOWEEKNUM"]   = (Isoweeknum, 1, 1),
        ["WORKDAY"]      = (Workday, 2, 3),
        ["NETWORKDAYS"]  = (Networkdays, 2, 3),
        ["DAYS"]         = (Days, 2, 2),
        ["DAYS360"]      = (Days360, 2, 3),
        ["YEARFRAC"]     = (Yearfrac, 2, 3),

        // ── Phase 4a: Statistical ────────────────────────────────────────────
        ["VAR"]              = (VarS, 1, 255),
        ["VAR.S"]            = (VarS, 1, 255),
        ["VAR.P"]            = (VarP, 1, 255),
        ["STDEV.P"]          = (StdevP, 1, 255),
        ["PERCENTILE"]       = (PercentileInc, 2, 2),
        ["PERCENTILE.INC"]   = (PercentileInc, 2, 2),
        ["PERCENTILE.EXC"]   = (PercentileExc, 2, 2),
        ["QUARTILE"]         = (QuartileInc, 2, 2),
        ["QUARTILE.INC"]     = (QuartileInc, 2, 2),
        ["GEOMEAN"]          = (Geomean, 1, 255),
        ["HARMEAN"]          = (Harmean, 1, 255),
        ["AVEDEV"]           = (Avedev, 1, 255),
        ["PERCENTRANK"]      = (PercentrankInc, 2, 3),
        ["PERCENTRANK.INC"]  = (PercentrankInc, 2, 3),
        ["MODE"]             = (ModeSngl, 1, 255),
        ["MODE.SNGL"]        = (ModeSngl, 1, 255),
        ["CORREL"]           = (Correl, 2, 2),
        ["FORECAST"]         = (Forecast, 3, 3),
        ["FORECAST.LINEAR"]  = (Forecast, 3, 3),

        // ── Phase 4a: Financial ──────────────────────────────────────────────
        ["PMT"]  = (Pmt, 3, 5),
        ["PV"]   = (Pv, 3, 5),
        ["FV"]   = (Fv, 3, 5),
        ["NPER"] = (Nper, 3, 5),
        ["RATE"] = (Rate, 3, 6),
        ["NPV"]  = (Npv, 2, 255),
        ["IRR"]  = (Irr, 1, 2),
        ["SLN"]  = (Sln, 3, 3),

        // ── Phase 4a: Logical / Text ─────────────────────────────────────────
        ["XOR"]         = (Xor, 1, 255),
        ["TRUE"]        = (TrueFunc, 0, 0),
        ["FALSE"]       = (FalseFunc, 0, 0),
        ["ISEVEN"]      = (Iseven, 1, 1),
        ["ISODD"]       = (Isodd, 1, 1),
        ["REPLACE"]     = (Replace, 4, 4),
        ["CONCATENATE"] = (Concatenate, 1, 255),
        ["T"]           = (TFunc, 1, 1),
        ["HYPERLINK"]   = (Hyperlink, 1, 2),
        ["FIXED"]       = (Fixed, 1, 3),
        ["CLEAN"]       = (Clean, 1, 1),
        ["DOLLAR"]      = (Dollar, 1, 2),

        // ── Phase 4a: Reference ──────────────────────────────────────────────
        ["INDIRECT"] = (Indirect, 1, 2),
        ["ADDRESS"]  = (Address, 2, 5),
        ["LOOKUP"]   = (Lookup, 2, 3),
        ["N"]        = (NFunc, 1, 1),

        // ── Phase 4b: Dynamic arrays ─────────────────────────────────────────
        ["SEQUENCE"] = (Sequence, 1, 4),
        ["RANDARRAY"] = (RandArray, 0, 5),
        ["FILTER"]   = (Filter, 2, 3),
        ["SORT"]     = (Sort, 1, 4),
        ["SORTBY"]   = (SortBy, 2, 255),
        ["TAKE"]     = (Take, 2, 3),
        ["DROP"]     = (Drop, 2, 3),
        ["CHOOSEROWS"] = (ChooseRows, 2, 255),
        ["CHOOSECOLS"] = (ChooseCols, 2, 255),
        ["VSTACK"]   = (VStack, 1, 255),
        ["HSTACK"]   = (HStack, 1, 255),
        ["TOROW"]    = (ToRow, 1, 3),
        ["TOCOL"]    = (ToCol, 1, 3),
        ["WRAPROWS"] = (WrapRows, 2, 3),
        ["WRAPCOLS"] = (WrapCols, 2, 3),
        ["EXPAND"]   = (Expand, 2, 4),
        ["UNIQUE"]   = (Unique, 1, 3),

        // ── Subtotal ─────────────────────────────────────────────────────────
        ["SUBTOTAL"] = (Subtotal, 2, 255),

        // ── Phase A1: Text ───────────────────────────────────────────────────
        ["UNICHAR"]     = (Unichar, 1, 1),
        ["UNICODE"]     = (UnicodeFunc, 1, 1),
        ["NUMBERVALUE"] = (Numbervalue, 1, 3),

        // ── Phase A1: Math ───────────────────────────────────────────────────
        ["SQRTPI"]      = (Sqrtpi, 1, 1),
        ["MULTINOMIAL"] = (Multinomial, 1, 255),
        ["SERIESSUM"]   = (SeriesSum, 4, 4),

        // ── Phase A1: Matrix ─────────────────────────────────────────────────
        ["MMULT"]    = (Mmult, 2, 2),
        ["MINVERSE"] = (Minverse, 1, 1),
        ["MDETERM"]  = (Mdeterm, 1, 1),

        // ── Phase A1: Date (weekend mask) ───────────────────────────────────
        ["NETWORKDAYS.INTL"] = (NetworkdaysIntl, 2, 4),
        ["WORKDAY.INTL"]     = (WorkdayIntl, 2, 4),

        // ── Phase A1: Lookup ────────────────────────────────────────────────
        ["TRANSPOSE"] = (Transpose, 1, 1),

        // ── Phase A1: Information ────────────────────────────────────────────
        ["TYPE"]       = (TypeFunc, 1, 1),
        ["ERROR.TYPE"] = (ErrorTypeFunc, 1, 1),

        // ── Phase A2: AST-aware reference / information functions ───────────
        // (Implementations live in FormulaEvaluator.EvaluateAstAware; the
        // entries below exist so BuiltInFunctions.Exists/ValidateArgCount
        // recognize them. The Func slot returns #VALUE! as a defensive
        // fallback — the evaluator routes these names to the AST-aware
        // path before this delegate is ever invoked.)
        ["ISREF"]       = (AstAwareStub, 1, 1),
        ["ISFORMULA"]   = (AstAwareStub, 1, 1),
        ["FORMULATEXT"] = (AstAwareStub, 1, 1),
        ["OFFSET"]      = (AstAwareStub, 3, 5),

        // ── Phase A2: Context-aware information ─────────────────────────────
        ["CELL"]        = (CellInfo, 1, 2),
        ["INFO"]        = (InfoFunc, 1, 1),

        // ── Phase A2: AGGREGATE ─────────────────────────────────────────────
        ["AGGREGATE"]   = (Aggregate, 3, 255),
        ["GETPIVOTDATA"] = (GetPivotData, 2, 255),

        // ── Phase A2: CONVERT ───────────────────────────────────────────────
        ["CONVERT"]     = (Convert, 3, 3),
        ["BIN2DEC"]     = (Bin2Dec, 1, 1),
        ["BIN2HEX"]     = (Bin2Hex, 1, 2),
        ["BIN2OCT"]     = (Bin2Oct, 1, 2),
        ["DEC2BIN"]     = (Dec2Bin, 1, 2),
        ["DEC2HEX"]     = (Dec2Hex, 1, 2),
        ["DEC2OCT"]     = (Dec2Oct, 1, 2),
        ["HEX2BIN"]     = (Hex2Bin, 1, 2),
        ["HEX2DEC"]     = (Hex2Dec, 1, 1),
        ["HEX2OCT"]     = (Hex2Oct, 1, 2),
        ["OCT2BIN"]     = (Oct2Bin, 1, 2),
        ["OCT2DEC"]     = (Oct2Dec, 1, 1),
        ["OCT2HEX"]     = (Oct2Hex, 1, 2),
        ["BITAND"]      = (BitAnd, 2, 2),
        ["BITOR"]       = (BitOr, 2, 2),
        ["BITXOR"]      = (BitXor, 2, 2),
        ["BITLSHIFT"]   = (BitLShift, 2, 2),
        ["BITRSHIFT"]   = (BitRShift, 2, 2),

        // ── Phase A1: Database functions ─────────────────────────────────────
        ["DSUM"]     = (DSum, 3, 3),
        ["DAVERAGE"] = (DAverage, 3, 3),
        ["DCOUNT"]   = (DCount, 3, 3),
        ["DCOUNTA"]  = (DCountA, 3, 3),
        ["DGET"]     = (DGet, 3, 3),
        ["DMAX"]     = (DMax, 3, 3),
        ["DMIN"]     = (DMin, 3, 3),
        ["DPRODUCT"] = (DProduct, 3, 3),
        ["DSTDEV"]   = (DStdev, 3, 3),
        ["DSTDEVP"]  = (DStdevP, 3, 3),
        ["DVAR"]     = (DVar, 3, 3),
        ["DVARP"]    = (DVarP, 3, 3),

        // ── Phase B1: Normal distribution ───────────────────────────────────
        ["NORM.DIST"]    = (NormDist, 4, 4),
        ["NORM.INV"]     = (NormInv, 3, 3),
        ["NORM.S.DIST"]  = (NormSDist, 2, 2),
        ["NORM.S.INV"]   = (NormSInvFunc, 1, 1),
        ["STANDARDIZE"]  = (Standardize, 3, 3),

        // ── Phase B2: T, F, Chi-Squared + Tests ─────────────────────────────
        ["T.DIST"]       = (TDist, 3, 3),
        ["T.DIST.RT"]    = (TDistRt, 2, 2),
        ["T.DIST.2T"]    = (TDist2T, 2, 2),
        ["T.INV"]        = (TInvFunc, 2, 2),
        ["T.INV.2T"]     = (TInv2TFunc, 2, 2),
        ["T.TEST"]       = (TTest, 4, 4),
        ["F.DIST"]       = (FDist, 4, 4),
        ["F.DIST.RT"]    = (FDistRt, 3, 3),
        ["F.INV"]        = (FInvFunc, 3, 3),
        ["F.INV.RT"]     = (FInvRt, 3, 3),
        ["F.TEST"]       = (FTest, 2, 2),
        ["CHISQ.DIST"]   = (ChiSqDist, 3, 3),
        ["CHISQ.DIST.RT"]= (ChiSqDistRt, 2, 2),
        ["CHISQ.INV"]    = (ChiSqInvFunc, 2, 2),
        ["CHISQ.INV.RT"] = (ChiSqInvRt, 2, 2),
        ["CHISQ.TEST"]   = (ChiSqTest, 2, 2),

        // ── Phase B3: Descriptive statistics ────────────────────────────────
        ["SKEW"]             = (Skew, 1, 255),
        ["SKEW.P"]           = (SkewP, 1, 255),
        ["KURT"]             = (Kurt, 1, 255),
        ["FREQUENCY"]        = (Frequency, 2, 2),
        ["CONFIDENCE"]       = (ConfidenceNorm, 3, 3),
        ["CONFIDENCE.NORM"]  = (ConfidenceNorm, 3, 3),
        ["CONFIDENCE.T"]     = (ConfidenceT, 3, 3),

        // ── Phase B4: Discrete distributions ────────────────────────────────
        ["BINOM.DIST"]       = (BinomDist, 4, 4),
        ["BINOM.DIST.RANGE"] = (BinomDistRange, 3, 4),
        ["BINOM.INV"]        = (BinomInv, 3, 3),
        ["NEGBINOM.DIST"]    = (NegbinomDist, 4, 4),
        ["POISSON.DIST"]     = (PoissonDist, 3, 3),
        ["HYPERGEOM.DIST"]   = (HypergeomDist, 5, 5),

        // ── Phase B5: Continuous distributions ──────────────────────────────
        ["EXPON.DIST"]       = (ExponDist, 3, 3),
        ["WEIBULL.DIST"]     = (WeibullDist, 4, 4),
        ["GAMMA.DIST"]       = (GammaDist, 4, 4),
        ["GAMMA.INV"]        = (GammaInvFunc, 3, 3),
        ["GAMMALN"]          = (GammaLnFunc, 1, 1),
        ["GAMMALN.PRECISE"]  = (GammaLnFunc, 1, 1),
        ["GAMMA"]            = (GammaFunc, 1, 1),
        ["BETA.DIST"]        = (BetaDist, 4, 6),
        ["BETA.INV"]         = (BetaInvFunc, 3, 5),
        ["LOGNORM.DIST"]     = (LognormDist, 4, 4),
        ["LOGNORM.INV"]      = (LognormInv, 3, 3),

        // ── Phase C: Financial functions ─────────────────────────────────────
        ["IPMT"]      = (Ipmt, 4, 6),
        ["PPMT"]      = (Ppmt, 4, 6),
        ["CUMIPMT"]   = (Cumipmt, 6, 6),
        ["CUMPRINC"]  = (Cumprinc, 6, 6),
        ["EFFECT"]    = (Effect, 2, 2),
        ["NOMINAL"]   = (Nominal, 2, 2),
        ["MIRR"]      = (Mirr, 3, 3),
        ["XIRR"]      = (Xirr, 2, 3),
        ["XNPV"]      = (Xnpv, 3, 3),
        ["RRI"]       = (Rri, 3, 3),
        ["PDURATION"] = (Pduration, 3, 3),
        ["FVSCHEDULE"]= (Fvschedule, 2, 2),
        // Depreciation
        ["DB"]        = (Db, 4, 5),
        ["DDB"]       = (Ddb, 4, 5),
        ["VDB"]       = (Vdb, 5, 7),
        ["SYD"]       = (Syd, 4, 4),
        ["AMORDEGRC"] = (Amordegrc, 6, 7),
        ["AMORLINC"]  = (Amorlinc, 6, 7),
        // Dollar conversion
        ["DOLLARDE"]  = (Dollarde, 2, 2),
        ["DOLLARFR"]  = (Dollarfr, 2, 2),
        // Bond/discount
        ["DISC"]      = (Disc, 4, 5),
        ["INTRATE"]   = (Intrate, 4, 5),
        ["RECEIVED"]  = (Received, 4, 5),
        ["ACCRINT"]   = (Accrint, 6, 8),
        ["TBILLEQ"]   = (Tbilleq, 3, 3),
        ["TBILLPRICE"]= (Tbillprice, 3, 3),
        ["TBILLYIELD"]= (Tbillyield, 3, 3),
        // Coupon
        ["COUPDAYBS"] = (Coupdaybs, 3, 4),
        ["COUPDAYS"]  = (Coupdays, 3, 4),
        ["COUPDAYSNC"]= (Coupdaysnc, 3, 4),
        ["COUPNCD"]   = (Coupncd, 3, 4),
        ["COUPNUM"]   = (Coupnum, 3, 4),
        ["COUPPCD"]   = (Couppcd, 3, 4),
        // Price/Yield
        ["PRICE"]     = (Price, 6, 7),
        ["YIELD"]     = (Yield, 6, 7),
        ["PRICEDISC"] = (Pricedisc, 4, 5),
        ["PRICEMAT"]  = (Pricemat, 5, 6),
        ["YIELDDISC"] = (Yielddisc, 4, 5),
        ["YIELDMAT"]  = (Yieldmat, 5, 6),
        ["DURATION"]  = (Duration, 5, 6),
        ["MDURATION"] = (Mduration, 5, 6),
        // Odd coupon
        ["ODDFPRICE"] = (Oddfprice, 8, 9),
        ["ODDFYIELD"] = (Oddfyield, 8, 9),
        ["ODDLPRICE"] = (Oddlprice, 7, 8),
        ["ODDLYIELD"] = (Oddlyield, 7, 8),

        // ── Phase D: Lambda / higher-order functions ─────────────────────────
        // LET and LAMBDA are AST-aware special forms handled by FormulaEvaluator;
        // they are not registered here but are recognised by name before the built-in
        // lookup so they don't return #NAME?.  The higher-order helpers below ARE
        // registered because they receive their lambda arg as a pre-evaluated LambdaValue.
        ["MAP"]       = (MapFunc,       2, 255),
        ["REDUCE"]    = (ReduceFunc,    3, 3),
        ["SCAN"]      = (ScanFunc,      3, 3),
        ["BYROW"]     = (ByRowFunc,     2, 2),
        ["BYCOL"]     = (ByColFunc,     2, 2),
        ["MAKEARRAY"] = (MakeArrayFunc, 3, 3),
    };

    private static readonly HashSet<string> VolatileFunctions = ["NOW", "TODAY", "RAND", "RANDBETWEEN", "RANDARRAY", "INDIRECT", "OFFSET", "CELL", "INFO"];
    private static readonly string[] SpecialFunctionNames = ["LET", "LAMBDA"];

    /// <summary>Recognized built-in and special-form function names.</summary>
    public static IReadOnlyCollection<string> Names => Functions.Keys.Concat(SpecialFunctionNames).ToArray();

    /// <summary>True if the function recalculates on every pass regardless of input changes.</summary>
    public static bool IsVolatile(string name) => VolatileFunctions.Contains(name);

    /// <summary>Check if a function name is registered.</summary>
    public static bool Exists(string name) => Functions.ContainsKey(name);

    /// <summary>Get a function by name.</summary>
    public static (FormulaFunction Func, int MinArgs, int MaxArgs) Get(string name) => Functions[name];

    /// <summary>Validate argument count for a function.</summary>
    public static bool ValidateArgCount(string name, int count)
    {
        if (!Functions.TryGetValue(name, out var entry)) return false;
        return count >= entry.MinArgs && count <= entry.MaxArgs;
    }

    // ── Helper: coerce a ScalarValue to double ──

    private static double ToNumber(ScalarValue v) => v switch
    {
        NumberValue n => n.Value,
        DateTimeValue d => d.Value,
        BoolValue b => b.Value ? 1.0 : 0.0,
        BlankValue => 0.0,
        DirectTextLiteralValue t when double.TryParse(t.Value, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
        TextValue t when double.TryParse(t.Value, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
        _ => throw new FormulaEvalException("#VALUE!", $"Cannot convert {v} to number")
    };

    internal static bool ToBool(ScalarValue v) => v switch
    {
        BoolValue b => b.Value,
        NumberValue n => n.Value != 0.0,
        DateTimeValue d => d.Value != 0.0,
        BlankValue => false,
        _ => throw new FormulaEvalException("#VALUE!", $"Cannot convert {v} to boolean")
    };

    private static string ToText(ScalarValue v) => v switch
    {
        DirectTextLiteralValue t => t.Value,
        TextValue t => t.Value,
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        DateTimeValue d => d.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        BlankValue => "",
        ErrorValue e => e.Code,
        _ => v.ToString() ?? ""
    };

    private static bool TryDirectTextNumber(DirectTextLiteralValue value, out double number) =>
        double.TryParse(value.Value, System.Globalization.CultureInfo.InvariantCulture, out number);

    private static bool TryCellNumber(ScalarValue value, out double number)
    {
        switch (value)
        {
            case NumberValue n:
                number = n.Value;
                return true;
            case DateTimeValue d:
                number = d.Value;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static bool SameShape(RangeValue left, RangeValue right) =>
        left.RowCount == right.RowCount && left.ColCount == right.ColCount;

    private static bool TryReferencedNumber(ReferencedScalarValue value, out double number, out ErrorValue? error)
    {
        number = 0;
        error = null;
        switch (value.Value)
        {
            case ErrorValue e:
                error = e;
                return false;
            case NumberValue n:
                number = n.Value;
                return true;
            case DateTimeValue d:
                number = d.Value;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReferencedBool(ReferencedScalarValue value, out bool boolean, out ErrorValue? error)
    {
        boolean = false;
        error = null;
        switch (value.Value)
        {
            case ErrorValue e:
                error = e;
                return false;
            case BoolValue b:
                boolean = b.Value;
                return true;
            case NumberValue n:
                boolean = n.Value != 0.0;
                return true;
            case DateTimeValue d:
                boolean = d.Value != 0.0;
                return true;
            default:
                return false;
        }
    }

    private static ErrorValue? FirstError(IReadOnlyList<ScalarValue> args)
    {
        foreach (var arg in args)
            if (arg is ErrorValue e) return e;
        return null;
    }

    // ── Function implementations ──

    private static ScalarValue Sum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double total = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError)) total += value;
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                total += value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue; // SUM ignores text and blanks in ranges
            total += ToNumber(arg);
        }
        return NumberResult(total);
    }

    private static ScalarValue Average(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double total = 0;
        int count = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    total += value;
                    count++;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                total += value;
                count++;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            total += ToNumber(arg);
            count++;
        }
        return count == 0 ? ErrorValue.DivByZero : NumberResult(total / count);
    }

    private static ScalarValue Min(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double? min = null;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (min is null || value < min) min = value;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                if (min is null || value < min) min = value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            var val = ToNumber(arg);
            if (min is null || val < min) min = val;
        }
        return min.HasValue ? NumberResult(min.Value) : new NumberValue(0);
    }

    private static ScalarValue Max(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double? max = null;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (max is null || value > max) max = value;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                if (max is null || value > max) max = value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            var val = ToNumber(arg);
            if (max is null || val > max) max = val;
        }
        return max.HasValue ? NumberResult(max.Value) : new NumberValue(0);
    }

    private static ScalarValue Count(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        int count = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out _, out var refError)) count++;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (TryDirectTextNumber(direct, out _)) count++;
                continue;
            }
            if (arg is NumberValue or BoolValue or DateTimeValue)
                count++;
        }
        return new NumberValue(count);
    }

    private static ScalarValue CountA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        int count = 0;
        foreach (var arg in args)
        {
            if (arg is not BlankValue)
                count++;
        }
        return new NumberValue(count);
    }

    private static ScalarValue If(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        var condition = ToBool(args[0]);
        if (condition)
            return args[1];
        return args.Count > 2 ? args[2] : new BoolValue(false);
    }

    private static ScalarValue And(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        bool hadUsableValue = false;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedBool(referenced, out bool value, out var refError))
                {
                    hadUsableValue = true;
                    if (!value) return new BoolValue(false);
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is TextValue or BlankValue) return ErrorValue.Value;
            hadUsableValue = true;
            if (!ToBool(arg)) return new BoolValue(false);
        }
        return hadUsableValue ? new BoolValue(true) : ErrorValue.Value;
    }

    private static ScalarValue Or(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        bool hadUsableValue = false;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedBool(referenced, out bool value, out var refError))
                {
                    hadUsableValue = true;
                    if (value) return new BoolValue(true);
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is TextValue or BlankValue) return ErrorValue.Value;
            hadUsableValue = true;
            if (ToBool(arg)) return new BoolValue(true);
        }
        return hadUsableValue ? new BoolValue(false) : ErrorValue.Value;
    }

    private static ScalarValue Not(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        return new BoolValue(!ToBool(args[0]));
    }

    private static ScalarValue Round(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err0) return err0;
        if (args[1] is ErrorValue err1) return err1;
        var number = ToNumber(args[0]);
        var rawDigits = ToNumber(args[1]);
        if (!double.IsFinite(rawDigits)) return ErrorValue.Num;
        int digits = (int)Math.Truncate(rawDigits);
        if (digits < -15 || digits > 15) return ErrorValue.Num;
        if (digits >= 0)
            return NumberResult(Math.Round(number, digits, MidpointRounding.AwayFromZero));

        double factor = Math.Pow(10, -digits);
        return NumberResult(Math.Round(number / factor, 0, MidpointRounding.AwayFromZero) * factor);
    }

    private static ScalarValue NumberResult(double value) =>
        double.IsFinite(value) ? new NumberValue(value) : ErrorValue.Num;

    private static bool TryTruncateToLong(double value, out long result)
    {
        result = 0;
        if (!double.IsFinite(value) || value < long.MinValue || value >= 9223372036854775808.0)
            return false;
        result = (long)Math.Truncate(value);
        return true;
    }

    private static double RoundWithExcelDigits(double number, int digits)
    {
        if (digits >= 0)
            return Math.Round(number, digits, MidpointRounding.AwayFromZero);

        double factor = Math.Pow(10, -digits);
        if (!double.IsFinite(factor)) return 0.0; // rounding to enormous scale → 0
        return Math.Round(number / factor, 0, MidpointRounding.AwayFromZero) * factor;
    }

    private static ScalarValue Abs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Abs(n));
    }

    private static ScalarValue Concat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            sb.Append(ToText(arg));
        }
        return TextResult(sb.ToString());
    }

    private static ScalarValue Len(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        return new NumberValue(ToText(args[0]).Length);
    }

    private static ScalarValue Left(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        var text  = ToText(args[0]);
        var rawCount = args.Count > 1 ? ToNumber(args[1]) : 1;
        if (!double.IsFinite(rawCount) || rawCount > int.MaxValue) return ErrorValue.Value;
        var count = (int)rawCount;
        if (count < 0) return ErrorValue.Value;
        count = Math.Min(count, text.Length);
        return TextResult(text[..count]);
    }

    private static ScalarValue Right(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        var text  = ToText(args[0]);
        var rawCount = args.Count > 1 ? ToNumber(args[1]) : 1;
        if (!double.IsFinite(rawCount) || rawCount > int.MaxValue) return ErrorValue.Value;
        var count = (int)rawCount;
        if (count < 0) return ErrorValue.Value;
        count = Math.Min(count, text.Length);
        return TextResult(count == 0 ? "" : text[^count..]);
    }

    private static ScalarValue Now(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        DateTimeValue.FromDateTime(DateTime.Now);

    private static ScalarValue Today(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        DateTimeValue.FromDateTime(DateTime.Today);

    private static ScalarValue Rand(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new NumberValue(Random.Shared.NextDouble());

    private static ScalarValue RandArray(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        foreach (var arg in args)
            if (arg is ErrorValue e) return e;

        double rowsD = args.Count > 0 && args[0] is not BlankValue ? ToNumber(args[0]) : 1;
        double colsD = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 1;
        double min = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 0;
        double max = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 1;
        bool wholeNumber = args.Count > 4 && args[4] is not BlankValue && ToBool(args[4]);

        if (!double.IsFinite(rowsD) || !double.IsFinite(colsD)) return ErrorValue.Value;
        int rows = (int)rowsD;
        int cols = (int)colsD;
        if (rows < 1 || cols < 1) return ErrorValue.Value;
        if ((long)rows * cols > 1_000_000) return ErrorValue.Value;
        if (!double.IsFinite(min) || !double.IsFinite(max) || min > max) return ErrorValue.Value;

        if (wholeNumber)
        {
            if (!TryTruncateToLong(Math.Ceiling(min), out long bottom) ||
                !TryTruncateToLong(Math.Floor(max), out long top))
                return ErrorValue.Value;
            if (bottom > top) return ErrorValue.Value;

            long randExclusiveTop;
            try { randExclusiveTop = checked(top + 1); }
            catch (OverflowException) { return ErrorValue.Value; }
            var integers = new ScalarValue[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    integers[r, c] = new NumberValue(Random.Shared.NextInt64(bottom, randExclusiveTop));
            return new RangeValue(integers);
        }

        double width = max - min;
        if (!double.IsFinite(width)) return ErrorValue.Value;
        var result = new ScalarValue[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var value = min + Random.Shared.NextDouble() * width;
                if (!double.IsFinite(value)) return ErrorValue.Value;
                result[r, c] = new NumberValue(value);
            }
        return new RangeValue(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Error handling
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue IfError(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue) return args[1];
        return args[0];
    }

    private static ScalarValue IfNa(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e && e.Code == "#N/A") return args[1];
        return args[0];
    }

    private static ScalarValue NaFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        ErrorValue.NA;

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Lookup
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Vlookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue table) return ErrorValue.Value;
        if (args[2] is ErrorValue e2) return e2;

        var lookupValue = args[0];
        double rawCol = ToNumber(args[2]);
        if (!double.IsFinite(rawCol) || rawCol > int.MaxValue) return ErrorValue.Value;
        int colIndex = (int)rawCol;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        bool rangeLookup = args.Count < 4 || ToBool(args[3]); // default TRUE

        if (colIndex < 1 || colIndex > (int)table.ColCount) return ErrorValue.Ref;

        if (rangeLookup)
        {
            // Approximate match – table must be sorted ascending on first column
            // Return last row where first-col value <= lookupValue
            int bestRow = -1;
            for (int r = 1; r <= table.RowCount; r++)
            {
                var cv = table.At(r, 1);
                if (cv is ErrorValue cvErr) return cvErr;
                if (CompareScalar(cv, lookupValue) <= 0)
                    bestRow = r;
                else
                    break;
            }
            if (bestRow < 0) return ErrorValue.NA;
            return table.At(bestRow, colIndex);
        }
        else
        {
            // Exact match — propagate errors encountered in the lookup column
            for (int r = 1; r <= table.RowCount; r++)
            {
                var cv = table.At(r, 1);
                if (cv is ErrorValue ev) return ev;
                if (MatchExactValue(cv, lookupValue))
                    return table.At(r, colIndex);
            }
            return ErrorValue.NA;
        }
    }

    private static ScalarValue Hlookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue table) return ErrorValue.Value;
        if (args[2] is ErrorValue e2) return e2;

        var lookupValue = args[0];
        double rawRow = ToNumber(args[2]);
        if (!double.IsFinite(rawRow) || rawRow > int.MaxValue) return ErrorValue.Value;
        int rowIndex = (int)rawRow;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        bool rangeLookup = args.Count < 4 || ToBool(args[3]);

        if (rowIndex < 1 || rowIndex > (int)table.RowCount) return ErrorValue.Ref;

        if (rangeLookup)
        {
            int bestCol = -1;
            for (int c = 1; c <= table.ColCount; c++)
            {
                var cv = table.At(1, c);
                if (cv is ErrorValue cvErr) return cvErr;
                if (CompareScalar(cv, lookupValue) <= 0)
                    bestCol = c;
                else
                    break;
            }
            if (bestCol < 0) return ErrorValue.NA;
            return table.At(rowIndex, bestCol);
        }
        else
        {
            // Exact match — propagate errors encountered in the lookup row
            for (int c = 1; c <= table.ColCount; c++)
            {
                var cv = table.At(1, c);
                if (cv is ErrorValue ev) return ev;
                if (MatchExactValue(cv, lookupValue))
                    return table.At(rowIndex, c);
            }
            return ErrorValue.NA;
        }
    }

    private static ScalarValue Index(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue table) return ErrorValue.Value;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        double rawRowNum = ToNumber(args[1]);
        if (!double.IsFinite(rawRowNum) || rawRowNum > int.MaxValue) return ErrorValue.Value;
        int rowNum = (int)rawRowNum;
        double rawColNum = args.Count > 2 ? ToNumber(args[2]) : 1.0;
        if (!double.IsFinite(rawColNum) || rawColNum > int.MaxValue) return ErrorValue.Value;
        int colNum = (int)rawColNum;

        // For a 1-D range with a single index argument, the index selects along the
        // only dimension (column for a 1-row range, row for a 1-column range).
        if (args.Count == 2)
        {
            if (table.RowCount == 1) { colNum = rowNum; rowNum = 1; }
            else if (table.ColCount == 1) { /* rowNum already correct, colNum = 1 */ }
        }

        // Negative indices → #VALUE! (out-of-range positive → #REF! per Excel)
        if (rowNum < 0) return ErrorValue.Value;
        if (colNum < 0) return ErrorValue.Value;
        if (rowNum > table.RowCount) return ErrorValue.Ref;
        if (colNum > table.ColCount) return ErrorValue.Ref;

        if (rowNum == 0 && colNum == 0)
            return table;

        if (rowNum == 0)
        {
            var col = new ScalarValue[table.RowCount, 1];
            for (int r = 0; r < table.RowCount; r++)
                col[r, 0] = table.Cells[r, colNum - 1];
            return new RangeValue(col);
        }

        if (colNum == 0)
        {
            var row = new ScalarValue[1, table.ColCount];
            for (int c = 0; c < table.ColCount; c++)
                row[0, c] = table.Cells[rowNum - 1, c];
            return new RangeValue(row);
        }

        return table.At(rowNum, colNum);
    }

    private static ScalarValue Match(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue table) return ErrorValue.Value;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        var lookupValue = args[0];
        double rawMatchType = args.Count > 2 ? ToNumber(args[2]) : 1;
        if (!double.IsFinite(rawMatchType)) return ErrorValue.NA;
        int matchType = (int)rawMatchType;
        if (matchType is not (-1 or 0 or 1)) return ErrorValue.NA;

        // Flatten to 1-D (single row or column expected)
        var flat = table.Flatten();

        if (matchType == 0)
        {
            // Exact match — propagate errors encountered in the lookup array
            for (int i = 0; i < flat.Count; i++)
            {
                if (flat[i] is ErrorValue ev) return ev;
                if (MatchExactValue(flat[i], lookupValue))
                    return new NumberValue(i + 1);
            }
            return ErrorValue.NA;
        }
        else if (matchType == 1)
        {
            // Ascending approximate: largest value <= lookupValue
            int best = -1;
            for (int i = 0; i < flat.Count; i++)
            {
                if (flat[i] is ErrorValue fErr) return fErr;
                if (CompareScalar(flat[i], lookupValue) <= 0)
                    best = i;
                else
                    break;
            }
            if (best < 0) return ErrorValue.NA;
            return new NumberValue(best + 1);
        }
        else // matchType == -1
        {
            // Descending approximate: smallest value >= lookupValue.
            // Assumes the lookup vector is sorted descending, matching Excel's contract.
            int best = -1;
            for (int i = 0; i < flat.Count; i++)
            {
                if (flat[i] is ErrorValue fErr) return fErr;
                if (CompareScalar(flat[i], lookupValue) >= 0)
                    best = i;
                else
                    break;
            }
            if (best < 0) return ErrorValue.NA;
            return new NumberValue(best + 1);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Conditional aggregation
    // ═══════════════════════════════════════════════════════════════════

    private static bool MatchExactValue(ScalarValue candidate, ScalarValue lookupValue)
    {
        if (lookupValue is TextValue pattern && candidate is TextValue text)
            return WildcardMatch(text.Value, pattern.Value, ignoreCase: true);

        return ScalarEquals(candidate, lookupValue);
    }

    private static ScalarValue Xmatch(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue lookupArr) return ErrorValue.Value;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        if (lookupArr.RowCount != 1 && lookupArr.ColCount != 1) return ErrorValue.Value;

        var lookupValue = args[0];
        var lookupFlat = lookupArr.Flatten();
        double rawMatchMode  = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 0;
        double rawSearchMode = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 1;
        if (!double.IsFinite(rawMatchMode) || !double.IsFinite(rawSearchMode)) return ErrorValue.Value;
        int matchMode  = (int)rawMatchMode;
        int searchMode = (int)rawSearchMode;
        if (matchMode is not (-1 or 0 or 1 or 2)) return ErrorValue.Value;
        if (searchMode is not (-2 or -1 or 1 or 2)) return ErrorValue.Value;

        var indices = Enumerable.Range(0, lookupFlat.Count).ToList();
        if (searchMode is -1 or -2) indices.Reverse();

        if (matchMode == 0)
        {
            foreach (int i in indices)
                if (ScalarEquals(lookupFlat[i], lookupValue))
                    return new NumberValue(i + 1);
            return ErrorValue.NA;
        }

        if (matchMode == 2)
        {
            string pattern = ToText(lookupValue);
            foreach (int i in indices)
                if (lookupFlat[i] is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                    return new NumberValue(i + 1);
            return ErrorValue.NA;
        }

        if (matchMode == -1)
        {
            int best = -1;
            foreach (int i in indices)
                if (CompareScalar(lookupFlat[i], lookupValue) <= 0)
                    best = i;
            return best >= 0 ? new NumberValue(best + 1) : ErrorValue.NA;
        }

        foreach (int i in indices)
            if (CompareScalar(lookupFlat[i], lookupValue) >= 0)
                return new NumberValue(i + 1);
        return ErrorValue.NA;
    }

    private static ScalarValue Sumif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue rangeError) return rangeError;
        if (args[0] is not RangeValue rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;
        if (args.Count > 2 && args[2] is ErrorValue sumRangeError) return sumRangeError;
        RangeValue? sumRange = args.Count > 2 ? args[2] as RangeValue : null;

        var rangeFlat = rangeArg.Flatten();
        IReadOnlyList<ScalarValue> sumFlat = sumRange is not null ? sumRange.Flatten() : rangeFlat;

        double total = 0;
        for (int i = 0; i < rangeFlat.Count; i++)
        {
            if (MatchesCriteria(rangeFlat[i], criteria))
            {
                var sv = i < sumFlat.Count ? sumFlat[i] : BlankValue.Instance;
                if (sv is ErrorValue e) return e;
                if (TryCellNumber(sv, out double value)) total += value;
                else if (sv is BlankValue) { /* skip */ }
            }
        }
        return NumberResult(total);
    }

    private static ScalarValue Countif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue rangeError) return rangeError;
        if (args[0] is not RangeValue rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;

        int count = 0;
        foreach (var v in rangeArg.Flatten())
            if (MatchesCriteria(v, criteria))
                count++;
        return new NumberValue(count);
    }

    private static ScalarValue Averageif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue rangeError) return rangeError;
        if (args[0] is not RangeValue rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;
        if (args.Count > 2 && args[2] is ErrorValue avgRangeError) return avgRangeError;
        RangeValue? avgRange = args.Count > 2 ? args[2] as RangeValue : null;

        var rangeFlat = rangeArg.Flatten();
        IReadOnlyList<ScalarValue> avgFlat = avgRange is not null ? avgRange.Flatten() : rangeFlat;

        double total = 0;
        int count = 0;
        for (int i = 0; i < rangeFlat.Count; i++)
        {
            if (MatchesCriteria(rangeFlat[i], criteria))
            {
                var sv = i < avgFlat.Count ? avgFlat[i] : BlankValue.Instance;
                if (sv is ErrorValue e) return e;
                if (TryCellNumber(sv, out double value)) { total += value; count++; }
            }
        }
        if (count == 0) return ErrorValue.DivByZero;
        return NumberResult(total / count);
    }

    /// <summary>
    /// Test a cell value against an Excel criteria string or value.
    /// Supports: number (exact), text (exact, case-insensitive),
    /// operator strings ">5", ">=5", "<5", "<=5", "<>5", "=text",
    /// and simple wildcard strings using * and ?.
    /// </summary>
    private static bool MatchesCriteria(ScalarValue cellValue, ScalarValue criteria)
    {
        if (criteria is BlankValue)
            criteria = new TextValue("");

        if (criteria is NumberValue cn)
            return TryCellNumber(cellValue, out double cellNumber) && cellNumber == cn.Value;

        if (criteria is DateTimeValue cdt)
            return TryCellNumber(cellValue, out double cellDateNum) && cellDateNum == cdt.Value;

        if (criteria is BoolValue cb)
            return cellValue is BoolValue cvb && cvb.Value == cb.Value;

        if (criteria is not TextValue ct) return false;
        var crit = ct.Value;

        // Operator prefix?
        if (crit.StartsWith(">=") || crit.StartsWith("<=") || crit.StartsWith("<>"))
        {
            var op  = crit[..2];
            var rhs = crit[2..];
            return ApplyComparisonCriteria(cellValue, op, rhs);
        }
        if (crit.StartsWith(">") || crit.StartsWith("<") || crit.StartsWith("="))
        {
            var op  = crit[..1];
            var rhs = crit[1..];
            return ApplyComparisonCriteria(cellValue, op, rhs);
        }

        // Plain text (supports wildcards * and ?)
        var cellText = cellValue is TextValue tv ? tv.Value :
                       TryCellNumber(cellValue, out double numericValue) ? numericValue.ToString(System.Globalization.CultureInfo.InvariantCulture) :
                       cellValue is BoolValue bv ? (bv.Value ? "TRUE" : "FALSE") :
                       "";
        return WildcardMatch(cellText, crit, ignoreCase: true);
    }

    private static bool ApplyComparisonCriteria(ScalarValue cellValue, string op, string rhs)
    {
        // Try numeric comparison first
        if (double.TryParse(rhs, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rhsNum))
        {
            if (!TryCellNumber(cellValue, out double value)) return false;
            return op switch
            {
                ">"  => value > rhsNum,
                ">=" => value >= rhsNum,
                "<"  => value < rhsNum,
                "<=" => value <= rhsNum,
                "="  => value == rhsNum,
                "<>" => value != rhsNum,
                _    => false
            };
        }
        // Text comparison
        var cellText = cellValue is TextValue tv ? tv.Value : ToText(cellValue);
        int cmp = string.Compare(cellText, rhs, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            ">"  => cmp > 0,
            ">=" => cmp >= 0,
            "<"  => cmp < 0,
            "<=" => cmp <= 0,
            "="  => cmp == 0,
            "<>" => cmp != 0,
            _    => false
        };
    }

    private static readonly ConcurrentDictionary<(string Pattern, bool IgnoreCase), Regex> WildcardCache = new();

    private static string WildcardToRegexPattern(string pattern, bool anchored = true)
    {
        var sb = new System.Text.StringBuilder(anchored ? "^" : "");
        for (int i = 0; i < pattern.Length; i++)
        {
            char ch = pattern[i];
            if (ch == '~' && i + 1 < pattern.Length && pattern[i + 1] is '*' or '?' or '~')
            {
                sb.Append(Regex.Escape(pattern[++i].ToString()));
                continue;
            }

            switch (ch)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                default:  sb.Append(Regex.Escape(ch.ToString())); break;
            }
        }
        if (anchored) sb.Append('$');
        return sb.ToString();
    }

    /// <summary>Simple Excel-style wildcard match (* = any chars, ? = any single char).</summary>
    private static bool WildcardMatch(string text, string pattern, bool ignoreCase)
    {
        var regex = WildcardCache.GetOrAdd((pattern, ignoreCase), key =>
        {
            var opts = key.IgnoreCase ? RegexOptions.IgnoreCase | RegexOptions.Compiled : RegexOptions.Compiled;
            return new Regex(WildcardToRegexPattern(key.Pattern), opts);
        });
        return regex.IsMatch(text);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Text functions
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue TextFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue formatError) return formatError;
        var fmt = ToText(args[1]);
        // Simple inline formatter (avoids depending on Freexcel.Core.Calc)
        var val = args[0];
        if (TryCellNumber(val, out double value))
            return TextResult(FormatNumberInline(value, fmt));
        return TextResult(ToText(val));
    }

    private static string FormatNumberInline(double value, string fmt)
    {
        if (string.IsNullOrEmpty(fmt)) return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try { return value.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }

    private static readonly Regex MultiSpaceRegex = new(@" {2,}", RegexOptions.Compiled);

    private static ScalarValue Trim(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text = MultiSpaceRegex.Replace(ToText(args[0]).Trim(), " ");
        return TextResult(text);
    }

    private static ScalarValue Upper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TextResult(ToText(args[0]).ToUpperInvariant());
    }

    private static ScalarValue Lower(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TextResult(ToText(args[0]).ToLowerInvariant());
    }

    private static ScalarValue Proper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text = ToText(args[0]);
        if (text.Length == 0) return new TextValue("");
        var sb = new System.Text.StringBuilder(text.Length);
        bool capitaliseNext = true;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || !char.IsLetter(ch)) { capitaliseNext = true; sb.Append(ch); }
            else if (capitaliseNext) { sb.Append(char.ToUpperInvariant(ch)); capitaliseNext = false; }
            else sb.Append(char.ToLowerInvariant(ch));
        }
        return TextResult(sb.ToString());
    }

    private static ScalarValue Substitute(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue oldTextError) return oldTextError;
        if (args[2] is ErrorValue newTextError) return newTextError;
        var text    = ToText(args[0]);
        var oldText = ToText(args[1]);
        var newText = ToText(args[2]);

        if (oldText.Length == 0) return TextResult(text);

        if (args.Count > 3)
        {
            // Replace the Nth occurrence only
            if (args[3] is ErrorValue e3) return e3;
            double rawInstanceNum = ToNumber(args[3]);
            if (!double.IsFinite(rawInstanceNum) || rawInstanceNum > int.MaxValue) return ErrorValue.Value;
            int instanceNum = (int)rawInstanceNum;
            if (instanceNum < 1) return ErrorValue.Value;
            int count = 0;
            int pos = 0;
            while (pos < text.Length)
            {
                int idx = text.IndexOf(oldText, pos, StringComparison.Ordinal);
                if (idx < 0) break;
                count++;
                if (count == instanceNum)
                    return TextResult(text[..idx] + newText + text[(idx + oldText.Length)..]);
                pos = idx + oldText.Length;
            }
            return TextResult(text); // instance not found
        }
        else
        {
            return TextResult(text.Replace(oldText, newText, StringComparison.Ordinal));
        }
    }

    private static ScalarValue TextResult(string text) =>
        text.Length > 32767 ? ErrorValue.Value : new TextValue(text);

    private static ScalarValue Find(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue withinError) return withinError;
        if (args.Count > 2 && args[2] is ErrorValue startError) return startError;
        var findText   = ToText(args[0]);
        var withinText = ToText(args[1]);
        int startNum = 1;
        if (args.Count > 2)
        {
            double rawStart = ToNumber(args[2]);
            if (!double.IsFinite(rawStart) || rawStart > int.MaxValue) return ErrorValue.Value;
            startNum = (int)rawStart;
        }
        if (startNum < 1) return ErrorValue.Value;
        int startIdx = startNum - 1;
        if (findText.Length == 0)
            return startIdx <= withinText.Length ? new NumberValue(startNum) : ErrorValue.Value;
        if (startIdx >= withinText.Length) return ErrorValue.Value;
        int pos = withinText.IndexOf(findText, startIdx, StringComparison.Ordinal);
        if (pos < 0) return ErrorValue.Value;
        return new NumberValue(pos + 1);
    }

    private static readonly ConcurrentDictionary<string, Regex> SearchCache = new();

    private static ScalarValue Search(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue withinError) return withinError;
        if (args.Count > 2 && args[2] is ErrorValue startError) return startError;
        var findText   = ToText(args[0]);
        var withinText = ToText(args[1]);
        int startNum = 1;
        if (args.Count > 2)
        {
            double rawStart = ToNumber(args[2]);
            if (!double.IsFinite(rawStart) || rawStart > int.MaxValue) return ErrorValue.Value;
            startNum = (int)rawStart;
        }
        if (startNum < 1) return ErrorValue.Value;
        int startIdx = startNum - 1;
        if (findText.Length == 0)
            return startIdx <= withinText.Length ? new NumberValue(startNum) : ErrorValue.Value;
        if (startIdx >= withinText.Length) return ErrorValue.Value;

        var regex = SearchCache.GetOrAdd(findText, pattern =>
        {
            return new Regex(WildcardToRegexPattern(pattern, anchored: false), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });
        var match = regex.Match(withinText, startIdx);
        if (!match.Success) return ErrorValue.Value;
        return new NumberValue(match.Index + 1);
    }

    private static ScalarValue Mid(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue startError) return startError;
        if (args[2] is ErrorValue lengthError) return lengthError;
        var text    = ToText(args[0]);
        double rawStart = ToNumber(args[1]);
        double rawLen   = ToNumber(args[2]);
        if (!double.IsFinite(rawStart) || !double.IsFinite(rawLen)) return ErrorValue.Value;
        if (rawStart < 1 || rawLen < 0 || rawStart > int.MaxValue || rawLen > int.MaxValue) return ErrorValue.Value;
        int start   = (int)rawStart - 1; // 1-based → 0-based
        int numChars = (int)rawLen;
        if (start >= text.Length) return new TextValue("");
        int actualLen = Math.Min(numChars, text.Length - start);
        return TextResult(text.Substring(start, actualLen));
    }

    private static ScalarValue Rept(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue repeatError) return repeatError;
        var text  = ToText(args[0]);
        var timesD = ToNumber(args[1]);
        if (!double.IsFinite(timesD) || timesD > int.MaxValue) return ErrorValue.Value;
        int times = (int)timesD;
        if (times < 0) return ErrorValue.Value;
        if ((long)text.Length * times > 32767) return ErrorValue.Value;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < times; i++) sb.Append(text);
        return new TextValue(sb.ToString());
    }

    private static ScalarValue ValueFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is NumberValue nv) return nv;
        var text = ToText(args[0]).Trim();
        if (text.EndsWith('%') &&
            double.TryParse(text[..^1].Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return new NumberValue(pct / 100.0);
        if (double.TryParse(text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
            return new NumberValue(d);
        return ErrorValue.Value;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Date & time
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Date(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double rawYear = ToNumber(args[0]);
        double rawMonth = ToNumber(args[1]);
        double rawDay = ToNumber(args[2]);
        if (!double.IsFinite(rawYear) || !double.IsFinite(rawMonth) || !double.IsFinite(rawDay))
            return ErrorValue.Num;
        if (rawYear > int.MaxValue || rawMonth > int.MaxValue || rawDay > int.MaxValue ||
            rawYear < int.MinValue || rawMonth < int.MinValue || rawDay < int.MinValue)
            return ErrorValue.Num;
        int year  = (int)rawYear;
        int month = (int)rawMonth;
        int day   = (int)rawDay;
        if (year >= 0 && year < 1900)
            year += 1900;
        if (year < 0 || year > 9999) return ErrorValue.Num;
        try
        {
            var dt = new DateTime(year, 1, 1)
                .AddMonths(month - 1)
                .AddDays(day - 1);
            double serial = DateToSerial(dt);
            if (serial < 0) return ErrorValue.Num;
            if (year == 1900 && month == 3 && day == 0)
                return new NumberValue(60);
            if (dt == new DateTime(1900, 3, 1) && month < 3)
                return new NumberValue(60);
            return new NumberValue(serial);
        }
        catch { return ErrorValue.Num; }
    }

    // OADate range supported by DateTime.FromOADate: -657435.0 to 2958465.0
    private static bool TryOADateToDateTime(ScalarValue v, out DateTime dt)
    {
        dt = default;
        var num = ToNumber(v);
        if (!double.IsFinite(num) || num < -657435.0 || num > 2958465.0)
            return false;
        dt = SerialToDate(num);
        return true;
    }

    private static bool TryNonNegativeOADateToDateTime(ScalarValue v, out DateTime dt)
    {
        dt = default;
        var num = ToNumber(v);
        if (!double.IsFinite(num) || num < 0 || num > 2958465.0)
            return false;
        dt = SerialToDate(num);
        return true;
    }

    private static DateTime OADateToDateTime(ScalarValue v) =>
        DateTime.FromOADate(ToNumber(v));

    private static ScalarValue Year(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (IsExcelFakeLeapDay(args[0])) return new NumberValue(1900);
        return TryOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Year) : ErrorValue.Num;
    }

    private static ScalarValue Month(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (IsExcelFakeLeapDay(args[0])) return new NumberValue(2);
        return TryOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Month) : ErrorValue.Num;
    }

    private static ScalarValue Day(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (IsExcelFakeLeapDay(args[0])) return new NumberValue(29);
        return TryOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Day) : ErrorValue.Num;
    }

    private static ScalarValue Hour(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TryNonNegativeOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Hour) : ErrorValue.Num;
    }

    private static ScalarValue Minute(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TryNonNegativeOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Minute) : ErrorValue.Num;
    }

    private static ScalarValue Second(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TryNonNegativeOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Second) : ErrorValue.Num;
    }

    private static ScalarValue Weekday(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue returnTypeError) return returnTypeError;
        double rawSerial = ToNumber(args[0]);
        if (!double.IsFinite(rawSerial)) return ErrorValue.Num;
        double rawReturnType = args.Count > 1 ? ToNumber(args[1]) : 1;
        if (!double.IsFinite(rawReturnType)) return ErrorValue.Num;
        int returnType = (int)rawReturnType;
        int daySerial = (int)Math.Floor(rawSerial);
        int dow = ((daySerial - 1) % 7 + 7) % 7; // 0=Sunday...6=Saturday in Excel's 1900 date system
        return returnType switch
        {
            1 => new NumberValue(dow + 1),                     // Sun=1..Sat=7
            2 or 11 => new NumberValue(dow == 0 ? 7 : dow),    // Mon=1..Sun=7
            3 => new NumberValue(dow == 0 ? 6 : dow - 1),      // Mon=0..Sun=6
            >= 12 and <= 17 => new NumberValue(((dow - (returnType - 10) + 7) % 7) + 1),
            _ => ErrorValue.Num
        };
    }

    private static ScalarValue Edate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (!TryOADateToDateTime(args[0], out var dt)) return ErrorValue.Num;
        double rawMonths = ToNumber(args[1]);
        if (!double.IsFinite(rawMonths) || rawMonths > int.MaxValue || rawMonths < int.MinValue) return ErrorValue.Num;
        int months = (int)rawMonths;
        try
        {
            var result = dt.AddMonths(months);
            return new NumberValue(DateToSerial(result));
        }
        catch { return ErrorValue.Num; }
    }

    private static ScalarValue Datedif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (!TryOADateToDateTime(args[0], out var startRaw)) return ErrorValue.Num;
        if (!TryOADateToDateTime(args[1], out var endRaw)) return ErrorValue.Num;
        // DATEDIF operates on whole dates — discard any time portion so that
        // e.g. DATEDIF(2024-01-01 23:00, 2024-01-02 01:00, "D") returns 1 (Excel)
        // rather than 0 (TimeSpan.Days would otherwise round toward zero).
        var start = startRaw.Date;
        var end = endRaw.Date;
        if (end < start) return ErrorValue.Num;
        var unit  = ToText(args[2]).ToUpperInvariant();

        return unit switch
        {
            "D"  => new NumberValue(DateToSerial(end) - DateToSerial(start)),
            "M"  => new NumberValue(MonthDiff(start, end)),
            "Y"  => new NumberValue(YearDiff(start, end)),
            "YM" => new NumberValue((int)MonthDiff(start, end) % 12),
            "YD" => DateDifYD(start, end),
            "MD" => DateDifMD(start, end),
            _    => ErrorValue.Value
        };
    }

    private static double MonthDiff(DateTime start, DateTime end)
    {
        int months = (end.Year - start.Year) * 12 + (end.Month - start.Month);
        if (end.Day < start.Day) months--;
        return months;
    }

    private static double YearDiff(DateTime start, DateTime end)
    {
        int years = end.Year - start.Year;
        if (end.Month < start.Month || (end.Month == start.Month && end.Day < start.Day))
            years--;
        return years;
    }

    // DATEDIF helpers that can throw ArgumentOutOfRangeException when start is a
    // leap day (Feb 29) and end.Year is not a leap year — catch → #NUM!
    private static ScalarValue DateDifYD(DateTime start, DateTime end)
    {
        try
        {
            var anchor = new DateTime(end.Year, start.Month, start.Day);
            return new NumberValue((end - (anchor > end ? anchor.AddYears(-1) : anchor)).Days);
        }
        catch (ArgumentOutOfRangeException) { return ErrorValue.Num; }
    }

    private static ScalarValue DateDifMD(DateTime start, DateTime end)
    {
        try
        {
            if (end.Day >= start.Day)
                return new NumberValue(end.Day - start.Day);
            int prevYear  = end.Month == 1 ? end.Year - 1 : end.Year;
            int prevMonth = end.Month == 1 ? 12 : end.Month - 1;
            return new NumberValue(end.Day + DateTime.DaysInMonth(prevYear, prevMonth) - start.Day);
        }
        catch (ArgumentOutOfRangeException) { return ErrorValue.Num; }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Math
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Mod(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var n = ToNumber(args[0]);
        var d = ToNumber(args[1]);
        if (!double.IsFinite(n) || !double.IsFinite(d)) return ErrorValue.Num;
        if (d == 0) return ErrorValue.DivByZero;
        return NumberResult(n - d * Math.Floor(n / d));
    }

    private static ScalarValue Power(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var number = ToNumber(args[0]);
        var power = ToNumber(args[1]);
        if (number == 0 && power < 0) return ErrorValue.DivByZero;
        if (number == 0 && power == 0) return ErrorValue.Num;
        var result = Math.Pow(number, power);
        if (double.IsNaN(result)) return ErrorValue.Num;
        if (double.IsInfinity(result)) return ErrorValue.Num;
        return new NumberValue(result);
    }

    private static ScalarValue Sqrt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n) || n < 0) return ErrorValue.Num;
        return new NumberValue(Math.Sqrt(n));
    }

    private static ScalarValue IntFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Floor(n));
    }

    private static ScalarValue Ceiling(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var n = ToNumber(args[0]);
        var sig = ToNumber(args[1]);
        if (sig == 0) return new NumberValue(0);
        if (!double.IsFinite(n) || !double.IsFinite(sig)) return ErrorValue.Num;
        if (n > 0 && sig < 0) return ErrorValue.Num;
        return NumberResult(Math.Ceiling(n / sig) * sig);
    }

    private static ScalarValue Floor(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var n = ToNumber(args[0]);
        var sig = ToNumber(args[1]);
        if (sig == 0) return new NumberValue(0);
        if (!double.IsFinite(n) || !double.IsFinite(sig)) return ErrorValue.Num;
        if (n > 0 && sig < 0) return ErrorValue.Num;
        return NumberResult(Math.Floor(n / sig) * sig);
    }

    private static ScalarValue Randbetween(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double db = ToNumber(args[0]);
        double dt = ToNumber(args[1]);
        if (!double.IsFinite(db) || !double.IsFinite(dt)) return ErrorValue.Num;
        if (!TryTruncateToLong(db, out long bottom) || !TryTruncateToLong(dt, out long top))
            return ErrorValue.Num;
        if (bottom > top) return ErrorValue.Num;
        // NextInt64(min, max) is [min, max) — add 1 to make top inclusive
        long exclusiveTop;
        try { exclusiveTop = checked(top + 1); }
        catch (OverflowException) { return ErrorValue.Num; }
        return new NumberValue(Random.Shared.NextInt64(bottom, exclusiveTop));
    }

    private static ScalarValue Sign(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(n > 0 ? 1 : n < 0 ? -1 : 0);
    }

    private static ScalarValue Log(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var n    = ToNumber(args[0]);
        var base_ = args.Count > 1 ? ToNumber(args[1]) : 10.0;
        if (!double.IsFinite(n) || !double.IsFinite(base_)) return ErrorValue.Num;
        if (n <= 0 || base_ <= 0 || base_ == 1) return ErrorValue.Num;
        return NumberResult(Math.Log(n) / Math.Log(base_));
    }

    private static ScalarValue Ln(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (n <= 0) return ErrorValue.Num;
        return NumberResult(Math.Log(n));
    }

    private static ScalarValue Exp(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var result = Math.Exp(ToNumber(args[0]));
        if (double.IsNaN(result) || double.IsInfinity(result)) return ErrorValue.Num;
        return new NumberValue(result);
    }

    private static ScalarValue Pi(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new NumberValue(Math.PI);

    private static ScalarValue Fact(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n) || n < 0 || n > 170) return ErrorValue.Num; // Excel limit; 171! overflows double
        int ni = (int)Math.Truncate(n);
        double result = 1;
        for (int i = 2; i <= ni; i++) result *= i;
        return new NumberValue(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Statistical
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Large(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue range) return ErrorValue.Value;
        if (args[1] is ErrorValue e1) return e1;
        var kD = ToNumber(args[1]);
        if (!double.IsFinite(kD)) return ErrorValue.Num;
        int k = (int)kD;
        var (values, err) = CollectRangeNumbers(range);
        if (err is not null) return err;
        var nums = values!.OrderByDescending(x => x).ToList();
        if (k < 1 || k > nums.Count) return ErrorValue.Num;
        return new NumberValue(nums[k - 1]);
    }

    private static ScalarValue Small(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue range) return ErrorValue.Value;
        if (args[1] is ErrorValue e1) return e1;
        var kD = ToNumber(args[1]);
        if (!double.IsFinite(kD)) return ErrorValue.Num;
        int k = (int)kD;
        var (values, err) = CollectRangeNumbers(range);
        if (err is not null) return err;
        var nums = values!.OrderBy(x => x).ToList();
        if (k < 1 || k > nums.Count) return ErrorValue.Num;
        return new NumberValue(nums[k - 1]);
    }

    private static ScalarValue Rank(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue range) return ErrorValue.Value;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var number = ToNumber(args[0]);
        if (!double.IsFinite(number)) return ErrorValue.Num;
        double rawOrder = args.Count > 2 ? ToNumber(args[2]) : 0;
        if (!double.IsFinite(rawOrder)) return ErrorValue.Num;
        int order  = (int)rawOrder;

        var (nums, err) = CollectRangeNumbers(range);
        if (err is not null) return err;

        if (!nums!.Contains(number)) return ErrorValue.NA;

        int rank;
        if (order == 0)
            rank = nums.Count(x => x > number) + 1;  // descending
        else
            rank = nums.Count(x => x < number) + 1;  // ascending

        return new NumberValue(rank);
    }

    private static ScalarValue Stdev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (numsOrNull, err) = CollectNumbers(args);
        if (err is not null) return err;
        var nums = numsOrNull!;
        if (nums.Count < 2) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double variance = nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1);
        return NumberResult(Math.Sqrt(variance));
    }

    private static ScalarValue Median(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (numsOrNull, err) = CollectNumbers(args);
        if (err is not null) return err;
        var nums = numsOrNull!;
        if (nums.Count == 0) return ErrorValue.Num;
        nums.Sort();
        int mid = nums.Count / 2;
        if (nums.Count % 2 == 1)
            return NumberResult(nums[mid]);
        return NumberResult((nums[mid - 1] + nums[mid]) / 2.0);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Multi-criteria aggregation
    // ═══════════════════════════════════════════════════════════════════

    // SUMIFS(sum_range, criteria_range1, criteria1, [criteria_range2, criteria2, ...])
    private static ScalarValue Sumifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue sumRangeError) return sumRangeError;
        if (args[0] is not RangeValue sumRange) return ErrorValue.Value;
        if (args.Count < 3 || (args.Count - 1) % 2 != 0) return ErrorValue.Value;
        var sumFlat = sumRange.Flatten();
        int len = sumFlat.Count;
        int pairCount = (args.Count - 1) / 2;
        var pairs = new (IReadOnlyList<ScalarValue> Flat, ScalarValue Criteria)[pairCount];
        for (int p = 0; p < pairCount; p++)
        {
            if (args[1 + p * 2] is ErrorValue rangeError) return rangeError;
            if (args[1 + p * 2] is not RangeValue cr) return ErrorValue.Value;
            if (!SameShape(sumRange, cr)) return ErrorValue.Value;
            if (args[2 + p * 2] is ErrorValue criteriaError) return criteriaError;
            pairs[p] = (cr.Flatten(), args[2 + p * 2]);
        }
        double total = 0;
        for (int i = 0; i < len; i++)
        {
            bool include = true;
            foreach (var (cf, criteria) in pairs)
            {
                if (!MatchesCriteria(i < cf.Count ? cf[i] : BlankValue.Instance, criteria))
                    { include = false; break; }
            }
            if (include)
            {
                if (sumFlat[i] is ErrorValue e) return e;
                if (TryCellNumber(sumFlat[i], out double value)) total += value;
            }
        }
        return NumberResult(total);
    }

    // COUNTIFS(criteria_range1, criteria1, ...)
    private static ScalarValue Countifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 2 || args.Count % 2 != 0) return ErrorValue.Value;
        int pairCount = args.Count / 2;
        var pairs = new (IReadOnlyList<ScalarValue> Flat, ScalarValue Criteria)[pairCount];
        RangeValue? firstRange = null;
        for (int p = 0; p < pairCount; p++)
        {
            if (args[p * 2] is ErrorValue rangeError) return rangeError;
            if (args[p * 2] is not RangeValue cr) return ErrorValue.Value;
            firstRange ??= cr;
            if (!SameShape(firstRange, cr)) return ErrorValue.Value;
            if (args[p * 2 + 1] is ErrorValue criteriaError) return criteriaError;
            pairs[p] = (cr.Flatten(), args[p * 2 + 1]);
        }
        int len = pairs[0].Flat.Count;
        int count = 0;
        for (int i = 0; i < len; i++)
        {
            bool include = true;
            foreach (var (cf, criteria) in pairs)
            {
                if (!MatchesCriteria(i < cf.Count ? cf[i] : BlankValue.Instance, criteria))
                    { include = false; break; }
            }
            if (include) count++;
        }
        return new NumberValue(count);
    }

    // AVERAGEIFS(avg_range, criteria_range1, criteria1, ...)
    private static ScalarValue Averageifs2(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue avgRangeError) return avgRangeError;
        if (args[0] is not RangeValue avgRange) return ErrorValue.Value;
        if (args.Count < 3 || (args.Count - 1) % 2 != 0) return ErrorValue.Value;
        var avgFlat = avgRange.Flatten();
        int len = avgFlat.Count;
        int pairCount = (args.Count - 1) / 2;
        var pairs = new (IReadOnlyList<ScalarValue> Flat, ScalarValue Criteria)[pairCount];
        for (int p = 0; p < pairCount; p++)
        {
            if (args[1 + p * 2] is ErrorValue rangeError) return rangeError;
            if (args[1 + p * 2] is not RangeValue cr) return ErrorValue.Value;
            if (!SameShape(avgRange, cr)) return ErrorValue.Value;
            if (args[2 + p * 2] is ErrorValue criteriaError) return criteriaError;
            pairs[p] = (cr.Flatten(), args[2 + p * 2]);
        }
        double total = 0;
        int count = 0;
        for (int i = 0; i < len; i++)
        {
            bool include = true;
            foreach (var (cf, criteria) in pairs)
            {
                if (!MatchesCriteria(i < cf.Count ? cf[i] : BlankValue.Instance, criteria))
                    { include = false; break; }
            }
            if (include)
            {
                if (avgFlat[i] is ErrorValue e) return e;
                if (TryCellNumber(avgFlat[i], out double value)) { total += value; count++; }
            }
        }
        if (count == 0) return ErrorValue.DivByZero;
        return NumberResult(total / count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Modern lookup: XLOOKUP
    // ═══════════════════════════════════════════════════════════════════

    // XLOOKUP(lookup_value, lookup_array, return_array, [if_not_found], [match_mode], [search_mode])
    private static ScalarValue Xlookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue lookupArr) return ErrorValue.Value;
        if (args[2] is ErrorValue e2) return e2;
        if (args[2] is not RangeValue returnArr) return ErrorValue.Value;
        var lookupIsVertical = lookupArr.ColCount == 1;
        var lookupIsHorizontal = lookupArr.RowCount == 1;
        if (!lookupIsVertical && !lookupIsHorizontal) return ErrorValue.Value;
        if (lookupIsVertical && returnArr.RowCount != lookupArr.RowCount) return ErrorValue.Value;
        if (lookupIsHorizontal && returnArr.ColCount != lookupArr.ColCount) return ErrorValue.Value;

        var lookupValue = args[0];
        var lookupFlat = lookupArr.Flatten();

        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        ScalarValue ifNotFound = args.Count > 3 ? args[3] : ErrorValue.NA;
        if (args.Count > 4 && args[4] is ErrorValue e4) return e4;
        if (args.Count > 5 && args[5] is ErrorValue e5) return e5;
        double rawXMatchMode  = args.Count > 4 ? ToNumber(args[4]) : 0;
        double rawXSearchMode = args.Count > 5 ? ToNumber(args[5]) : 1;
        if (!double.IsFinite(rawXMatchMode) || !double.IsFinite(rawXSearchMode)) return ErrorValue.Value;
        int matchMode  = (int)rawXMatchMode;  // 0=exact
        int searchMode = (int)rawXSearchMode; // 1=first-to-last
        if (matchMode is not (-1 or 0 or 1 or 2)) return ErrorValue.Value;
        if (searchMode is not (-2 or -1 or 1 or 2)) return ErrorValue.Value;

        var indices = Enumerable.Range(0, lookupFlat.Count).ToList();
        if (searchMode is -1 or -2) indices.Reverse();

        if (matchMode == 0)
        {
            // Exact match
            foreach (int i in indices)
                if (ScalarEquals(lookupFlat[i], lookupValue))
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            return ifNotFound;
        }
        else if (matchMode == 2)
        {
            string pattern = ToText(lookupValue);
            foreach (int i in indices)
                if (lookupFlat[i] is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            return ifNotFound;
        }
        else if (matchMode == -1)
        {
            // Exact or next smaller
            int best = -1;
            foreach (int i in indices)
                if (CompareScalar(lookupFlat[i], lookupValue) <= 0)
                    best = i;
            return best >= 0 ? XlookupReturnAt(returnArr, best, lookupIsVertical) : ifNotFound;
        }
        else
        {
            // Exact or next larger: return first element >= lookupValue
            foreach (int i in indices)
                if (CompareScalar(lookupFlat[i], lookupValue) >= 0)
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            return ifNotFound;
        }
    }

    private static ScalarValue XlookupReturnAt(RangeValue returnArr, int index, bool lookupIsVertical)
    {
        if (lookupIsVertical)
        {
            if (returnArr.ColCount == 1) return returnArr.Cells[index, 0];
            var row = new ScalarValue[1, returnArr.ColCount];
            for (int c = 0; c < returnArr.ColCount; c++)
                row[0, c] = returnArr.Cells[index, c];
            return new RangeValue(row);
        }

        if (returnArr.RowCount == 1) return returnArr.Cells[0, index];
        var col = new ScalarValue[returnArr.RowCount, 1];
        for (int r = 0; r < returnArr.RowCount; r++)
            col[r, 0] = returnArr.Cells[r, index];
        return new RangeValue(col);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Multi-condition logic: IFS, SWITCH
    // ═══════════════════════════════════════════════════════════════════

    // IFS(condition1, value1, [condition2, value2, ...])
    private static ScalarValue Ifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count % 2 != 0) return ErrorValue.Value;
        for (int i = 0; i < args.Count - 1; i += 2)
        {
            if (args[i] is ErrorValue e) return e;
            if (ToBool(args[i])) return args[i + 1];
        }
        return ErrorValue.NA;
    }

    // SWITCH(expr, val1, result1, [val2, result2, ...], [default])
    private static ScalarValue Switch(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var expr = args[0];
        // args: expr, val1, result1, val2, result2, ..., [default]
        bool hasDefault = (args.Count - 1) % 2 == 1;
        int pairCount = (args.Count - 1) / 2;
        for (int i = 0; i < pairCount; i++)
        {
            if (ScalarEquals(expr, args[1 + i * 2]))
                return args[1 + i * 2 + 1];
        }
        return hasDefault ? args[^1] : ErrorValue.NA;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – IS functions
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Isblank(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(args[0] is BlankValue);

    private static ScalarValue Isnumber(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(args[0] is NumberValue or DateTimeValue);

    private static ScalarValue Istext(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(args[0] is TextValue);

    private static ScalarValue Iserror(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(args[0] is ErrorValue);

    private static ScalarValue Isna(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(args[0] is ErrorValue e2 && e2.Code == "#N/A");

    private static ScalarValue Islogical(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(args[0] is BoolValue);

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Reference helpers: ROW, COLUMN, ROWS, COLUMNS
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Row(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count == 0) return ErrorValue.Value; // no cell reference available without context
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue rv) return new NumberValue(rv.StartRow);
        return ErrorValue.Value;
    }

    private static ScalarValue Column(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count == 0) return ErrorValue.Value;
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue rv) return new NumberValue(rv.StartCol);
        return ErrorValue.Value;
    }

    private static ScalarValue Rows(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue rv) return new NumberValue(rv.RowCount);
        return new NumberValue(1);
    }

    private static ScalarValue Columns(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue rv) return new NumberValue(rv.ColCount);
        return new NumberValue(1);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Text: TEXTJOIN, EXACT, CODE, CHAR
    // ═══════════════════════════════════════════════════════════════════

    // TEXTJOIN(delimiter, ignore_empty, text1, [text2, ...])
    private static ScalarValue Textjoin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 3) return ErrorValue.Value;
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var delimiter = ToText(args[0]);
        bool ignoreEmpty = ToBool(args[1]);
        var parts = new List<string>();
        for (int i = 2; i < args.Count; i++)
        {
            if (args[i] is ErrorValue e) return e;
            var t = ToText(args[i]);
            if (ignoreEmpty && t.Length == 0) continue;
            parts.Add(t);
        }
        var result = string.Join(delimiter, parts);
        return result.Length > 32767 ? ErrorValue.Value : new TextValue(result);
    }

    private static ScalarValue Exact(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return new BoolValue(string.Equals(ToText(args[0]), ToText(args[1]), StringComparison.Ordinal));
    }

    private static ScalarValue Code(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text = ToText(args[0]);
        if (text.Length == 0) return ErrorValue.Value;
        return new NumberValue(text[0]);
    }

    private static ScalarValue Char(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Value;
        int code = (int)n;
        if (code <= 0 || code > 255) return ErrorValue.Value;
        return new TextValue(((char)code).ToString());
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Count: COUNTBLANK
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Countblank(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is not RangeValue range) return ErrorValue.Value;
        int count = range.Flatten().Count(v => v is BlankValue || v is TextValue { Value.Length: 0 });
        return new NumberValue(count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Misc: CHOOSE, SUMPRODUCT, ROUNDDOWN, ROUNDUP, TRUNC
    // ═══════════════════════════════════════════════════════════════════

    // CHOOSE(index, val1, val2, ...)
    private static ScalarValue Choose(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Value;
        int idx = (int)n;
        if (idx < 1 || idx >= args.Count) return ErrorValue.Value;
        return args[idx];
    }

    // SUMPRODUCT(array1, [array2, ...])
    private static ScalarValue Sumproduct(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var arrays = new List<IReadOnlyList<ScalarValue>>();
        int firstRows = -1, firstCols = -1;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is RangeValue rv)
            {
                if (firstRows == -1) { firstRows = rv.RowCount; firstCols = rv.ColCount; }
                else if (rv.RowCount != firstRows || rv.ColCount != firstCols) return ErrorValue.Value;
                arrays.Add(rv.Flatten());
            }
            else if (a is NumberValue nv) arrays.Add([nv]);
            else arrays.Add([a]);
        }
        if (arrays.Count == 0) return new NumberValue(0);
        int len = arrays[0].Count;
        for (int k = 1; k < arrays.Count; k++)
            if (arrays[k].Count != len) return ErrorValue.Value;
        double total = 0;
        for (int i = 0; i < len; i++)
        {
            double product = 1;
            for (int k = 0; k < arrays.Count; k++)
            {
                var v = arrays[k][i];
                if (v is ErrorValue ev) return ev;
                product *= TryCellNumber(v, out double value) ? value : 0;
                if (!double.IsFinite(product)) return ErrorValue.Num;
            }
            total += product;
            if (!double.IsFinite(total)) return ErrorValue.Num;
        }
        return NumberResult(total);
    }

    private static ScalarValue Rounddown(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var n = ToNumber(args[0]);
        var rawDigits = ToNumber(args[1]);
        if (!double.IsFinite(rawDigits)) return ErrorValue.Num;
        int digits = (int)Math.Truncate(rawDigits);
        if (digits < -15 || digits > 15) return ErrorValue.Num;
        double factor = Math.Pow(10, digits);
        return NumberResult((n >= 0 ? Math.Floor(n * factor) : Math.Ceiling(n * factor)) / factor);
    }

    private static ScalarValue Roundup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var n = ToNumber(args[0]);
        var rawDigits = ToNumber(args[1]);
        if (!double.IsFinite(rawDigits)) return ErrorValue.Num;
        int digits = (int)Math.Truncate(rawDigits);
        if (digits < -15 || digits > 15) return ErrorValue.Num;
        double factor = Math.Pow(10, digits);
        return NumberResult((n >= 0 ? Math.Ceiling(n * factor) : Math.Floor(n * factor)) / factor);
    }

    private static ScalarValue Trunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        int digits = 0;
        if (args.Count > 1)
        {
            if (args[1] is ErrorValue e1) return e1;
            var rawDigits = ToNumber(args[1]);
            if (!double.IsFinite(rawDigits)) return ErrorValue.Num;
            digits = (int)Math.Truncate(rawDigits);
            if (digits < -15 || digits > 15) return ErrorValue.Num;
        }
        double factor = Math.Pow(10, digits);
        return NumberResult(Math.Truncate(n * factor) / factor);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helper: compare two ScalarValues (returns <0, 0, >0)
    // ═══════════════════════════════════════════════════════════════════

    private static int CompareScalar(ScalarValue a, ScalarValue b)
    {
        // Blank coerces to 0 when compared against numbers (Excel convention)
        if (a is BlankValue && TryCellNumber(b, out _)) a = new NumberValue(0);
        if (b is BlankValue && TryCellNumber(a, out _)) b = new NumberValue(0);

        var aIsNumber = TryCellNumber(a, out double aNumber);
        var bIsNumber = TryCellNumber(b, out double bNumber);
        if (aIsNumber && bIsNumber)
            return aNumber.CompareTo(bNumber);
        if (a is TextValue ta && b is TextValue tb)
            return string.Compare(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase);
        // Mixed: numbers < text
        return (aIsNumber ? 0 : 1) - (bIsNumber ? 0 : 1);
    }

    internal static bool ScalarEquals(ScalarValue a, ScalarValue b)
    {
        if (a is BlankValue && b is BlankValue) return true;
        // Blank coerces to 0 against numbers/dates, "" against text
        if (a is BlankValue) a = b is TextValue ? new TextValue("") : (ScalarValue)new NumberValue(0);
        if (b is BlankValue) b = a is TextValue ? new TextValue("") : (ScalarValue)new NumberValue(0);
        if (TryCellNumber(a, out double aNumber) && TryCellNumber(b, out double bNumber))
            return aNumber == bNumber;
        if (a is TextValue ta && b is TextValue tb)
            return string.Equals(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase);
        if (a is BoolValue ba && b is BoolValue bb)
            return ba.Value == bb.Value;
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Math / Trig
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Sin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Sin(n));
    }

    private static ScalarValue Cos(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Cos(n));
    }

    private static ScalarValue Tan(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Tan(n));
    }

    private static ScalarValue Asin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double n = ToNumber(args[0]);
        if (n < -1 || n > 1) return ErrorValue.Num;
        return new NumberValue(Math.Asin(n));
    }

    private static ScalarValue Acos(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double n = ToNumber(args[0]);
        if (n < -1 || n > 1) return ErrorValue.Num;
        return new NumberValue(Math.Acos(n));
    }

    private static ScalarValue Atan(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Atan(n));
    }

    // ATAN2(x_num, y_num) – matches Excel argument order (x first, then y)
    private static ScalarValue Atan2Func(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double x = ToNumber(args[0]);
        double y = ToNumber(args[1]);
        if (!double.IsFinite(x) || !double.IsFinite(y)) return ErrorValue.Num;
        if (x == 0 && y == 0) return ErrorValue.DivByZero;
        return new NumberValue(Math.Atan2(y, x));
    }

    private static ScalarValue Degrees(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(n * 180.0 / Math.PI);
    }

    private static ScalarValue Radians(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(n * Math.PI / 180.0);
    }

    private static ScalarValue Product(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double result = 1.0;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError)) result *= value;
                else if (refError is not null) return refError;
                continue;
            }
            if (a is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                result *= value;
            }
            else if (a is NumberValue or BoolValue or DateTimeValue) result *= ToNumber(a);
        }
        return NumberResult(result);
    }

    private static ScalarValue Quotient(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double n = ToNumber(args[0]);
        double d = ToNumber(args[1]);
        if (!double.IsFinite(n) || !double.IsFinite(d)) return ErrorValue.Num;
        if (d == 0) return ErrorValue.DivByZero;
        return NumberResult(Math.Truncate(n / d));
    }

    private static ScalarValue Gcd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        long result = 0;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (!double.IsFinite(value) || value < 0 || value > long.MaxValue) return ErrorValue.Num;
                    result = GcdCalc(result, (long)value);
                }
                else if (refError is not null) return refError;
                continue;
            }
            double d = ToNumber(a);
            if (!double.IsFinite(d) || d < 0 || d > long.MaxValue) return ErrorValue.Num;
            long n = (long)d;
            result = GcdCalc(result, n);
        }
        return new NumberValue(result);
    }

    private static long GcdCalc(long a, long b)
    {
        while (b != 0) { long t = b; b = a % b; a = t; }
        return a;
    }

    private static ScalarValue Lcm(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        long result = 1;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (!double.IsFinite(value) || value < 0 || value > long.MaxValue) return ErrorValue.Num;
                    long referencedNumber = (long)value;
                    if (referencedNumber == 0) return new NumberValue(0);
                    long referencedGcd = GcdCalc(result, referencedNumber);
                    if (result / referencedGcd > long.MaxValue / referencedNumber) return ErrorValue.Num;
                    result = result / referencedGcd * referencedNumber;
                }
                else if (refError is not null) return refError;
                continue;
            }
            double d = ToNumber(a);
            if (!double.IsFinite(d) || d < 0 || d > long.MaxValue) return ErrorValue.Num;
            long n = (long)d;
            if (n == 0) return new NumberValue(0);
            long g = GcdCalc(result, n);
            // Check overflow before multiplying
            if (result / g > long.MaxValue / n) return ErrorValue.Num;
            result = result / g * n;
        }
        return new NumberValue(result);
    }

    private static ScalarValue Mround(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double n = ToNumber(args[0]);
        double m = ToNumber(args[1]);
        if (!double.IsFinite(n) || !double.IsFinite(m)) return ErrorValue.Num;
        if (m == 0) return new NumberValue(0);
        if (n != 0 && (n < 0) != (m < 0)) return ErrorValue.Num;
        return NumberResult(Math.Round(n / m, MidpointRounding.AwayFromZero) * m);
    }

    private static ScalarValue Combin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double dn = ToNumber(args[0]); double dk = ToNumber(args[1]);
        if (!double.IsFinite(dn) || !double.IsFinite(dk)) return ErrorValue.Num;
        if (dn < 0 || dn > 1029 || dk < 0 || dk > int.MaxValue) return ErrorValue.Num;
        int n = (int)dn; int k = (int)dk;
        if (n < 0 || k < 0 || k > n) return ErrorValue.Num;
        if (k > n - k) k = n - k;
        double result = 1;
        for (int i = 0; i < k; i++)
            result = result * (n - i) / (i + 1);
        return NumberResult(Math.Round(result));
    }

    private static ScalarValue Permut(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double dn = ToNumber(args[0]); double dk = ToNumber(args[1]);
        if (!double.IsFinite(dn) || !double.IsFinite(dk)) return ErrorValue.Num;
        if (dn < 0 || dn > int.MaxValue || dk < 0 || dk > int.MaxValue) return ErrorValue.Num;
        int n = (int)dn; int k = (int)dk;
        if (n < 0 || k < 0 || k > n) return ErrorValue.Num;
        double result = 1;
        for (int i = 0; i < k; i++)
            result *= (n - i);
        return NumberResult(result);
    }

    private static ScalarValue Odd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (n == 0) return new NumberValue(1);
        double sign = n > 0 ? 1 : -1;
        double abs = Math.Ceiling(Math.Abs(n));
        if (abs > int.MaxValue) return ErrorValue.Num;
        int iabs = (int)abs;
        if (iabs % 2 == 0) iabs++;
        return new NumberValue(sign * iabs);
    }

    private static ScalarValue Even(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (n == 0) return new NumberValue(0);
        double sign = n > 0 ? 1 : -1;
        double abs = Math.Ceiling(Math.Abs(n));
        if (abs > int.MaxValue - 1) return ErrorValue.Num;
        int iabs = (int)abs;
        if (iabs % 2 != 0) iabs++;
        return new NumberValue(sign * iabs);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Date / Time
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue TimeFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double rawH = ToNumber(args[0]), rawM = ToNumber(args[1]), rawS = ToNumber(args[2]);
        if (!double.IsFinite(rawH) || !double.IsFinite(rawM) || !double.IsFinite(rawS)) return ErrorValue.Num;
        if (rawH < 0 || rawM < 0 || rawS < 0) return ErrorValue.Num;
        if (rawH > 32767 || rawM > 32767 || rawS > 32767) return ErrorValue.Num;
        int h = (int)rawH, m = (int)rawM, s = (int)rawS;
        double frac = (h * 3600 + m * 60 + s) / 86400.0;
        return new NumberValue(frac - Math.Floor(frac));
    }

    private static ScalarValue Timevalue(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text = ToText(args[0]);
        if (TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var ts) && ts.Days == 0)
            return new NumberValue(ts.TotalDays);
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return new NumberValue(dt.TimeOfDay.TotalDays);
        return ErrorValue.Value;
    }

    private static ScalarValue Datevalue(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text = ToText(args[0]);
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return new NumberValue(Math.Floor(DateToSerial(dt)));
        return ErrorValue.Value;
    }

    private static ScalarValue Eomonth(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (!TryOADateToDateTime(args[0], out var dt)) return ErrorValue.Num;
        double rawMonths = ToNumber(args[1]);
        if (!double.IsFinite(rawMonths) || rawMonths > int.MaxValue - 1 || rawMonths < int.MinValue) return ErrorValue.Num;
        int months = (int)rawMonths;
        try
        {
            var target = dt.AddMonths(months + 1);
            var eomonth = new DateTime(target.Year, target.Month, 1).AddDays(-1);
            return new NumberValue(DateToSerial(eomonth));
        }
        catch { return ErrorValue.Num; }
    }

    private static ScalarValue Weeknum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (!TryOADateToDateTime(args[0], out var dt)) return ErrorValue.Num;
        double rawReturnType = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 1;
        if (!double.IsFinite(rawReturnType)) return ErrorValue.Num;
        int returnType = (int)rawReturnType;
        if (returnType == 21)
            return new NumberValue(ExcelIsoWeeknum(dt));

        int firstDay = returnType switch
        {
            1 or 17 => 6,
            2 or 11 => 0,
            12 => 1,
            13 => 2,
            14 => 3,
            15 => 4,
            16 => 5,
            _ => -1
        };
        if (firstDay < 0) return ErrorValue.Num;
        var jan1 = new DateTime(dt.Year, 1, 1);
        int jan1Dow = (ExcelDowToMonIndex(jan1) - firstDay + 7) % 7;
        int dayOfYear = (dt - jan1).Days;
        return new NumberValue((dayOfYear + jan1Dow) / 7 + 1);
    }

    private static ScalarValue Isoweeknum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (!TryOADateToDateTime(args[0], out var dt)) return ErrorValue.Num;
        return new NumberValue(ExcelIsoWeeknum(dt));
    }

    private static ScalarValue Workday(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (!TryOADateToDateTime(args[0], out var current)) return ErrorValue.Num;
        double rawDays = ToNumber(args[1]);
        if (!double.IsFinite(rawDays)) return ErrorValue.Num;
        if (rawDays < int.MinValue + 1 || rawDays > int.MaxValue) return ErrorValue.Num;
        int days = (int)rawDays;
        var holidays = new HashSet<DateTime>();
        if (args.Count > 2 && args[2] is RangeValue hRange)
            foreach (var v in hRange.Flatten())
                if (TryCellNumber(v, out double holidaySerial))
                    holidays.Add(SerialToDate(holidaySerial).Date);
        int sign = days < 0 ? -1 : 1;
        int remaining = Math.Abs(days);
        // Skip full weeks when there are no holidays — 5 workdays = 7 calendar days
        if (remaining > 5 && holidays.Count == 0)
        {
            int fullWeeks = (remaining - 1) / 5; // keep ≥5 left so day-of-week boundary is handled correctly
            current = current.AddDays((long)sign * fullWeeks * 7);
            remaining -= fullWeeks * 5;
        }
        while (remaining > 0)
        {
            current = current.AddDays(sign);
            if (ExcelDowToMonIndex(current) < 5 &&
                !holidays.Contains(current.Date))
                remaining--;
        }
        return new NumberValue(DateToSerial(current));
    }

    private static ScalarValue Networkdays(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (!TryOADateToDateTime(args[0], out var startRaw)) return ErrorValue.Num;
        if (!TryOADateToDateTime(args[1], out var endRaw))   return ErrorValue.Num;
        var startDt = startRaw.Date;
        var endDt   = endRaw.Date;
        var holidays = new HashSet<DateTime>();
        if (args.Count > 2 && args[2] is RangeValue hRange)
            foreach (var v in hRange.Flatten())
                if (TryCellNumber(v, out double holidaySerial))
                    holidays.Add(DateTime.FromOADate(holidaySerial).Date);
        int sign = startDt <= endDt ? 1 : -1;
        var lo = startDt <= endDt ? startDt : endDt;
        var hi = startDt <= endDt ? endDt   : startDt;
        int count = CountExcelWeekdaysInclusive(lo, hi);
        foreach (var h in holidays)
            if (h >= lo && h <= hi && ExcelDowToMonIndex(h) < 5)
                count--;
        return new NumberValue(sign * count);
    }

    private static int CountWeekdaysInclusive(DateTime lo, DateTime hi)
    {
        int totalDays = (int)(hi - lo).TotalDays + 1;
        int fullWeeks = totalDays / 7;
        int count = fullWeeks * 5;
        int startDow = (int)lo.DayOfWeek; // 0=Sun, 1=Mon, …, 6=Sat
        for (int i = 0; i < totalDays % 7; i++)
        {
            int dow = (startDow + i) % 7;
            if (dow != 0 && dow != 6) count++;
        }
        return count;
    }

    private static int CountExcelWeekdaysInclusive(DateTime lo, DateTime hi)
    {
        int totalDays = (int)(hi - lo).TotalDays + 1;
        int fullWeeks = totalDays / 7;
        int count = fullWeeks * 5;
        int startDow = ExcelDowToMonIndex(lo);
        for (int i = 0; i < totalDays % 7; i++)
        {
            int dow = (startDow + i) % 7;
            if (dow < 5) count++;
        }
        return count;
    }

    private static ScalarValue Days(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (!TryOADateToDateTime(args[0], out var endDt))   return ErrorValue.Num;
        if (!TryOADateToDateTime(args[1], out var startDt)) return ErrorValue.Num;
        return new NumberValue(DateToSerial(endDt) - DateToSerial(startDt));
    }

    private static ScalarValue Days360(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (!TryOADateToDateTime(args[0], out var startRaw)) return ErrorValue.Num;
        if (!TryOADateToDateTime(args[1], out var endRaw))   return ErrorValue.Num;
        var startDt = startRaw.Date;
        var endDt   = endRaw.Date;
        bool european = args.Count > 2 && args[2] is not BlankValue && ToNumber(args[2]) != 0;
        double days = european ? Days30E360(startDt, endDt) : Days30US360(startDt, endDt);
        return new NumberValue(Math.Truncate(days));
    }

    private static ScalarValue Yearfrac(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (!TryOADateToDateTime(args[0], out var startRaw)) return ErrorValue.Num;
        if (!TryOADateToDateTime(args[1], out var endRaw))   return ErrorValue.Num;
        var startDt = startRaw.Date;
        var endDt   = endRaw.Date;
        double rawBasis = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 0;
        if (!double.IsFinite(rawBasis)) return ErrorValue.Num;
        int basis = (int)rawBasis;
        if (basis < 0 || basis > 4) return ErrorValue.Num;
        double totalDays = DateToSerial(endDt) - DateToSerial(startDt);
        double result = basis switch
        {
            1 => totalDays / ActualActualDenominator(startDt, endDt),
            2 => totalDays / 360.0,
            3 => totalDays / 365.0,
            4 => Days30E360(startDt, endDt) / 360.0,
            _ => Days30US360(startDt, endDt) / 360.0
        };
        return new NumberValue(result);
    }

    private static double ActualActualDenominator(DateTime start, DateTime end)
    {
        // Normalize order so the denominator is well-defined when callers pass
        // a reversed range (Excel allows YEARFRAC(start > end) and returns a
        // negative value — without this swap the loop is empty and we'd
        // divide by zero, yielding ±infinity instead of a finite result).
        if (start > end) (start, end) = (end, start);
        if (start.Year == end.Year)
            return DateTime.IsLeapYear(start.Year) ? 366.0 : 365.0;
        double total = 0;
        for (int y = start.Year; y <= end.Year; y++)
            total += DateTime.IsLeapYear(y) ? 366.0 : 365.0;
        return total / (end.Year - start.Year + 1);
    }

    private static double Days30US360(DateTime d1, DateTime d2)
    {
        int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
        int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
        if (dd1 == 31) dd1 = 30;
        if (dd2 == 31 && dd1 == 30) dd2 = 30;
        return 360.0 * (y2 - y1) + 30.0 * (m2 - m1) + (dd2 - dd1);
    }

    private static double Days30E360(DateTime d1, DateTime d2)
    {
        int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
        int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
        if (dd1 == 31) dd1 = 30;
        if (dd2 == 31) dd2 = 30;
        return 360.0 * (y2 - y1) + 30.0 * (m2 - m1) + (dd2 - dd1);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Statistical
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue VarS(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (list!.Count < 2) return ErrorValue.DivByZero;
        double mean = list.Average();
        return NumberResult(list.Sum(x => (x - mean) * (x - mean)) / (list.Count - 1));
    }

    private static ScalarValue VarP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (list!.Count == 0) return ErrorValue.DivByZero;
        double mean = list.Average();
        return NumberResult(list.Sum(x => (x - mean) * (x - mean)) / list.Count);
    }

    private static ScalarValue StdevP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var r = VarP(args, ctx);
        return r is NumberValue nv ? NumberResult(Math.Sqrt(nv.Value)) : r;
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectNumbers(IReadOnlyList<ScalarValue> args, int start = 0)
    {
        var list = new List<double>();
        for (int i = start; i < args.Count; i++)
        {
            var a = args[i];
            if (a is ErrorValue e) return (null, e);
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError)) list.Add(value);
                else if (refError is not null) return (null, refError);
            }
            else if (a is NumberValue nv) list.Add(nv.Value);
            else if (a is BoolValue bv) list.Add(bv.Value ? 1.0 : 0.0);
            else if (a is DateTimeValue dt) list.Add(dt.Value);
            else if (a is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return (null, ErrorValue.Value);
                list.Add(value);
            }
        }
        return (list, null);
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectRangeNumbers(RangeValue range)
    {
        var list = new List<double>();
        foreach (var value in range.Flatten())
        {
            if (value is ErrorValue e) return (null, e);
            if (value is NumberValue n) list.Add(n.Value);
            else if (value is DateTimeValue d) list.Add(d.Value);
        }
        return (list, null);
    }

    private static ScalarValue PercentileInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        double k = ToNumber(args[1]);
        if (!double.IsFinite(k)) return ErrorValue.Num;
        if (k < 0 || k > 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
        if (sorted.Count == 0) return ErrorValue.Num;
        double rank = k * (sorted.Count - 1);
        int lo = (int)rank;
        if (lo >= sorted.Count - 1) return NumberResult(sorted[^1]);
        return NumberResult(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue PercentileExc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        double k = ToNumber(args[1]);
        if (!double.IsFinite(k)) return ErrorValue.Num;
        if (k <= 0 || k >= 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
        int n = sorted.Count;
        if (n == 0) return ErrorValue.Num;
        double rank = k * (n + 1) - 1;
        if (rank < 0 || rank >= n) return ErrorValue.Num;
        int lo = (int)rank;
        if (lo >= n - 1) return NumberResult(sorted[n - 1]);
        return NumberResult(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue QuartileInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        double rawQuart = ToNumber(args[1]);
        if (!double.IsFinite(rawQuart)) return ErrorValue.Num;
        int quart = (int)rawQuart;
        if (quart < 0 || quart > 4) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
        if (sorted.Count == 0) return ErrorValue.Num;
        if (quart == 0) return NumberResult(sorted[0]);
        if (quart == 4) return NumberResult(sorted[^1]);
        double rank = (quart / 4.0) * (sorted.Count - 1);
        int lo = (int)rank;
        if (lo >= sorted.Count - 1) return NumberResult(sorted[^1]);
        return NumberResult(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue Geomean(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.Num;

        var logSum = 0.0;
        foreach (var value in nums)
        {
            if (value <= 0) return ErrorValue.Num;
            logSum += Math.Log(value);
        }
        return NumberResult(Math.Exp(logSum / nums.Count));
    }

    private static ScalarValue Harmean(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.Num;

        double recSum = 0;
        foreach (var value in nums)
        {
            if (value <= 0) return ErrorValue.Num;
            recSum += 1.0 / value;
        }
        return NumberResult(nums.Count / recSum);
    }

    private static ScalarValue Avedev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.DivByZero;
        double mean = nums.Average();
        return NumberResult(nums.Average(x => Math.Abs(x - mean)));
    }

    private static ScalarValue ModeSngl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;

        var freq = new Dictionary<double, int>();
        var order = new List<double>();
        foreach (var value in nums!)
        {
            if (!freq.ContainsKey(value)) order.Add(value);
            freq[value] = freq.GetValueOrDefault(value) + 1;
        }

        if (freq.Count == 0) return ErrorValue.NA;
        int maxFreq = freq.Values.Max();
        if (maxFreq < 2) return ErrorValue.NA;
        foreach (var key in order)
            if (freq[key] == maxFreq) return NumberResult(key);
        return ErrorValue.NA;
    }

    private static ScalarValue PercentrankInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        double x = ToNumber(args[1]);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        double rawSig = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 3;
        if (!double.IsFinite(rawSig) || rawSig > int.MaxValue) return ErrorValue.Num;
        int sig = (int)rawSig;
        if (sig < 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(v => v).ToList();
        int n = sorted.Count;
        if (n == 0 || x < sorted[0] || x > sorted[^1]) return ErrorValue.NA;
        double factor = Math.Pow(10, sig);
        if (!double.IsFinite(factor)) return ErrorValue.Num;

        int below = sorted.Count(v => v < x);
        int equal = sorted.Count(v => v == x);
        double pctRank;
        if (equal > 0)
        {
            pctRank = n == 1 ? 0.0 : (double)below / (n - 1);
        }
        else
        {
            // Excel interpolates between adjacent values when x is not in the array
            // but lies between sorted[0] and sorted[^1]. Find the largest index where
            // sorted[i] < x, then interpolate the rank between i and i+1.
            int lo = below - 1;
            if (lo < 0 || lo >= n - 1) return ErrorValue.NA;
            double lower = sorted[lo];
            double upper = sorted[lo + 1];
            double frac = upper > lower ? (x - lower) / (upper - lower) : 0.0;
            pctRank = ((double)lo + frac) / (n - 1);
        }

        return NumberResult(Math.Floor(pctRank * factor) / factor);
    }

    private static ScalarValue Correl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue rv1) return ErrorValue.Value;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue rv2) return ErrorValue.Value;
        var (xs, xErr) = CollectRangeNumbers(rv1);
        if (xErr is not null) return xErr;
        var (ys, yErr) = CollectRangeNumbers(rv2);
        if (yErr is not null) return yErr;
        if (xs!.Count != ys!.Count) return ErrorValue.NA;
        int n = xs.Count;
        if (n < 2) return ErrorValue.DivByZero;
        double xMean = xs.Average();
        double yMean = ys.Average();
        double cov = 0, varX = 0, varY = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = xs[i] - xMean, dy = ys[i] - yMean;
            cov  += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }
        if (varX == 0 || varY == 0) return ErrorValue.DivByZero;
        return NumberResult(cov / Math.Sqrt(varX * varY));
    }

    private static ScalarValue Forecast(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue knownY) return ErrorValue.Value;
        if (args[2] is ErrorValue e2) return e2;
        if (args[2] is not RangeValue knownX) return ErrorValue.Value;
        double x    = ToNumber(args[0]);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        var (ys, yErr) = CollectRangeNumbers(knownY);
        if (yErr is not null) return yErr;
        var (xs, xErr) = CollectRangeNumbers(knownX);
        if (xErr is not null) return xErr;
        if (xs!.Count != ys!.Count) return ErrorValue.NA;
        int n = xs.Count;
        if (n < 2) return ErrorValue.DivByZero;
        double xMean = xs.Average();
        double yMean = ys.Average();
        double sXX = 0, sXY = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = xs[i] - xMean;
            sXX += dx * dx;
            sXY += dx * (ys[i] - yMean);
        }
        if (sXX == 0) return ErrorValue.DivByZero;
        double b = sXY / sXX;
        double a = yMean - b * xMean;
        return NumberResult(a + b * x);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Financial
    // ═══════════════════════════════════════════════════════════════════

    private static bool IsValidPaymentType(double type) =>
        double.IsFinite(type) && (type == 0 || type == 1);

    private static ScalarValue Pmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        double nperValue = ToNumber(args[1]);
        double pv   = ToNumber(args[2]);
        double fv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (!double.IsFinite(rate) || !double.IsFinite(nperValue) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        double nper = nperValue;
        if (nper == 0) return ErrorValue.DivByZero;
        if (Math.Abs(rate) < 1e-10)
            return NumberResult(-(pv + fv) / nper);
        double rn  = Math.Pow(1 + rate, nper);
        double pmt = -(pv * rn + fv) * rate / ((1 + rate * type) * (rn - 1));
        return NumberResult(pmt);
    }

    private static ScalarValue Pv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        double nperValue = ToNumber(args[1]);
        double pmt  = ToNumber(args[2]);
        double fv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (!double.IsFinite(rate) || !double.IsFinite(nperValue) || !double.IsFinite(pmt) || !double.IsFinite(fv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        double nper = nperValue;
        if (nper == 0) return ErrorValue.DivByZero;
        if (Math.Abs(rate) < 1e-10)
            return NumberResult(-pmt * nper - fv);
        double rn = Math.Pow(1 + rate, nper);
        double pv = (-pmt * (1 + rate * type) * (rn - 1) / rate - fv) / rn;
        return NumberResult(pv);
    }

    private static ScalarValue Fv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        double nperValue = ToNumber(args[1]);
        double pmt  = ToNumber(args[2]);
        double pv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (!double.IsFinite(rate) || !double.IsFinite(nperValue) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        double nper = nperValue;
        if (Math.Abs(rate) < 1e-10)
            return NumberResult(-pv - pmt * nper);
        double rn = Math.Pow(1 + rate, nper);
        return NumberResult(-pv * rn - pmt * (1 + rate * type) * (rn - 1) / rate);
    }

    private static ScalarValue Nper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        double pmt  = ToNumber(args[1]);
        double pv   = ToNumber(args[2]);
        double fv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (!double.IsFinite(rate) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        if (Math.Abs(rate) < 1e-10)
        {
            if (Math.Abs(pmt) < 1e-10) return ErrorValue.DivByZero;
            return NumberResult(-(pv + fv) / pmt);
        }
        double pmtAdj = pmt * (1 + rate * type);
        double ratio  = (pmtAdj - fv * rate) / (pmtAdj + pv * rate);
        if (ratio <= 0) return ErrorValue.Num;
        return NumberResult(Math.Log(ratio) / Math.Log(1 + rate));
    }

    private static ScalarValue Rate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double nperValue = ToNumber(args[0]);
        double pmt   = ToNumber(args[1]);
        double pv    = ToNumber(args[2]);
        double fv    = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type  = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        double guess = args.Count > 5 && args[5] is not BlankValue ? ToNumber(args[5]) : 0.1;
        if (!double.IsFinite(nperValue) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type) || !double.IsFinite(guess))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        if (nperValue == 0) return ErrorValue.DivByZero;
        double nper = nperValue;
        double r = guess;
        for (int i = 0; i < 100; i++)
        {
            double rn   = Math.Pow(1 + r, nper);
            double rn1  = nper * Math.Pow(1 + r, nper - 1);
            double f, df;
            if (Math.Abs(r) < 1e-10)
            {
                f  = pv + pmt * nper + fv;
                df = pv * nper + pmt * nper * (nper - 1) / 2.0;
            }
            else
            {
                f  = pv * rn + pmt * (1 + r * type) * (rn - 1) / r + fv;
                df = pv * rn1
                   + pmt * type * (rn - 1) / r
                   + pmt * (1 + r * type) * (rn1 * r - (rn - 1)) / (r * r);
            }
            if (Math.Abs(df) < 1e-15) break;
            double delta = f / df;
            r -= delta;
            if (Math.Abs(delta) < 1e-10) break;
        }
        return double.IsNaN(r) || double.IsInfinity(r) ? ErrorValue.Num : new NumberValue(r);
    }

    private static ScalarValue Npv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double rate   = ToNumber(args[0]);
        if (!double.IsFinite(rate)) return ErrorValue.Num;
        var (values, err) = CollectNumbers(args, start: 1);
        if (err is not null) return err;

        double result = 0;
        for (int i = 0; i < values!.Count; i++)
            result += values[i] / Math.Pow(1 + rate, i + 1);
        return NumberResult(result);
    }

    private static ScalarValue Irr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        if (args[0] is not RangeValue valRange) return ErrorValue.Value;
        double guess = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 0.1;
        if (!double.IsFinite(guess) || guess <= -1) return ErrorValue.Num;
        var (values, err) = CollectRangeNumbers(valRange);
        if (err is not null) return err;
        var cashflows = values!;
        if (cashflows.Count < 2) return ErrorValue.Num;
        // Excel requires at least one positive and one negative cashflow.
        bool hasPositive = false, hasNegative = false;
        for (int i = 0; i < cashflows.Count; i++)
        {
            if (cashflows[i] > 0) hasPositive = true;
            else if (cashflows[i] < 0) hasNegative = true;
        }
        if (!hasPositive || !hasNegative) return ErrorValue.Num;
        double r = guess;
        for (int iter = 0; iter < 100; iter++)
        {
            double f = 0, df = 0;
            for (int i = 0; i < cashflows.Count; i++)
            {
                double denom = Math.Pow(1 + r, i);
                f  += cashflows[i] / denom;
                if (i > 0) df -= i * cashflows[i] / (denom * (1 + r));
            }
            if (Math.Abs(f) < 1e-10) break;
            if (Math.Abs(df) < 1e-15) return ErrorValue.Num;
            double delta = f / df;
            r -= delta;
            if (Math.Abs(delta) < 1e-10) break;
        }
        return double.IsNaN(r) || double.IsInfinity(r) ? ErrorValue.Num : new NumberValue(r);
    }

    private static ScalarValue Sln(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double cost    = ToNumber(args[0]);
        double salvage = ToNumber(args[1]);
        double life    = ToNumber(args[2]);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life))
            return ErrorValue.Num;
        if (life == 0) return ErrorValue.DivByZero;
        return NumberResult((cost - salvage) / life);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Logical / Text
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Xor(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        bool result = false;
        bool hadUsableValue = false;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedBool(referenced, out bool value, out var refError))
                {
                    hadUsableValue = true;
                    result ^= value;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (a is TextValue) return ErrorValue.Value;
            if (a is BlankValue) continue; // blank = FALSE, skip (no effect on XOR)
            hadUsableValue = true;
            result ^= ToBool(a);
        }
        return hadUsableValue ? new BoolValue(result) : ErrorValue.Value;
    }

    private static ScalarValue TrueFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(true);

    private static ScalarValue FalseFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(false);

    private static ScalarValue Iseven(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (!TryTruncateToLong(ToNumber(args[0]), out long n)) return ErrorValue.Num;
        return new BoolValue(n % 2 == 0);
    }

    private static ScalarValue Isodd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (!TryTruncateToLong(ToNumber(args[0]), out long n)) return ErrorValue.Num;
        return new BoolValue(n % 2 != 0);
    }

    private static ScalarValue Replace(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;

        var text = ToText(args[0]);
        double rawStart = ToNumber(args[1]);
        double rawNumChars = ToNumber(args[2]);
        if (!double.IsFinite(rawStart) || !double.IsFinite(rawNumChars)) return ErrorValue.Value;
        if (rawStart > int.MaxValue || rawNumChars > int.MaxValue) return ErrorValue.Value;

        int startNum = (int)rawStart;
        int numChars = (int)rawNumChars;
        if (startNum < 1 || numChars < 0) return ErrorValue.Value;

        int start = Math.Min(startNum - 1, text.Length);
        var newText = ToText(args[3]);
        int end = Math.Min(start + numChars, text.Length);
        return TextResult(text[..start] + newText + text[end..]);
    }

    private static ScalarValue Concatenate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            sb.Append(ToText(a));
        }
        return TextResult(sb.ToString());
    }

    private static ScalarValue TFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return args[0] is TextValue t ? TextResult(t.Value) : new TextValue("");
    }

    private static ScalarValue Hyperlink(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;

        var display = args.Count > 1 ? ToText(args[1]) : ToText(args[0]);
        return TextResult(display);
    }

    private static ScalarValue Fixed(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        double n = ToNumber(args[0]);
        int dec = 2;
        if (args.Count > 1 && args[1] is not BlankValue)
        {
            double rawDec = ToNumber(args[1]);
            if (!double.IsFinite(rawDec) || rawDec > int.MaxValue || rawDec < int.MinValue) return ErrorValue.Num;
            dec = (int)rawDec;
        }
        bool noCommas = args.Count > 2 && args[2] is not BlankValue && ToBool(args[2]);
        return TextResult(FormatRoundedNumber(n, dec, useCommas: !noCommas));
    }

    private static ScalarValue Clean(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var sb = new System.Text.StringBuilder();
        foreach (char c in ToText(args[0]))
            if (c >= 32) sb.Append(c);
        return TextResult(sb.ToString());
    }

    private static ScalarValue Dollar(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        double n = ToNumber(args[0]);
        int dec = 2;
        if (args.Count > 1 && args[1] is not BlankValue)
        {
            double rawDec = ToNumber(args[1]);
            if (!double.IsFinite(rawDec) || rawDec > int.MaxValue || rawDec < int.MinValue) return ErrorValue.Num;
            dec = (int)rawDec;
        }
        return TextResult("$" + FormatRoundedNumber(n, dec, useCommas: true));
    }

    private static string FormatRoundedNumber(double value, int decimals, bool useCommas)
    {
        if (!double.IsFinite(value)) throw new FormulaEvalException("#NUM!", "Invalid number");
        if (decimals > 32767) throw new FormulaEvalException("#VALUE!", "Formatted text exceeds Excel cell text limit");

        double rounded = decimals is >= -15 and <= 15 ? RoundWithExcelDigits(value, decimals) : value;
        int displayDecimals = Math.Clamp(decimals, 0, 99); // .NET "N"/"F" format supports 0-99 only
        string format = (useCommas ? "N" : "F") + displayDecimals;
        return rounded.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Reference
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Indirect(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var refText = ToText(args[0]).Trim();
        bool useA1 = args.Count < 2 || args[1] is BlankValue || ToBool(args[1]);
        string? sheetName = null;
        int bangIdx = refText.IndexOf('!');
        if (bangIdx >= 0)
        {
            var sheetPart = refText[..bangIdx];
            if (sheetPart.StartsWith('\'') && sheetPart.EndsWith('\'') && sheetPart.Length >= 2)
                sheetName = sheetPart[1..^1].Replace("''", "'");   // strip outer quotes and unescape ''→'
            else
                sheetName = sheetPart;
            refText = refText[(bangIdx + 1)..];
        }
        if (useA1
                ? !TryParseA1Ref(refText, out uint row, out uint col)
                : !TryParseR1C1Ref(refText, out row, out col))
            return ErrorValue.Ref;
        return sheetName is not null
            ? ctx.GetCellValue(sheetName, row, col)
            : ctx.GetCellValue(row, col);
    }

    private static bool TryParseA1Ref(string cellRef, out uint row, out uint col)
    {
        row = 0; col = 0;
        int i = 0;
        // Skip optional leading '$' (absolute column marker)
        if (i < cellRef.Length && cellRef[i] == '$') i++;
        while (i < cellRef.Length && char.IsLetter(cellRef[i])) i++;
        if (i == 0 || i >= cellRef.Length) return false;
        // Strip leading '$' from the column portion when building colStr
        int colStart = cellRef[0] == '$' ? 1 : 0;
        string colStr = cellRef[colStart..i].ToUpperInvariant();
        string rowPart = cellRef[i..];
        // Skip optional '$' before row number
        if (rowPart.Length > 0 && rowPart[0] == '$') rowPart = rowPart[1..];
        if (!uint.TryParse(rowPart, out row)) return false;
        col = CellAddress.ColumnNameToNumber(colStr);
        return row > 0 && row <= CellAddress.MaxRow && col > 0 && col <= CellAddress.MaxCol;
    }

    private static bool TryParseR1C1Ref(string cellRef, out uint row, out uint col)
    {
        row = 0; col = 0;
        var match = Regex.Match(cellRef, @"^R(\d+)C(\d+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        if (!uint.TryParse(match.Groups[1].Value, out row)) return false;
        if (!uint.TryParse(match.Groups[2].Value, out col)) return false;
        return row > 0 && row <= CellAddress.MaxRow && col > 0 && col <= CellAddress.MaxCol;
    }

    private static ScalarValue Address(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        if (args.Count > 4 && args[4] is ErrorValue e4) return e4;
        double dRow = ToNumber(args[0]); double dCol = ToNumber(args[1]);
        if (!double.IsFinite(dRow) || !double.IsFinite(dCol)) return ErrorValue.Num;
        int rowNum = (int)dRow; int colNum = (int)dCol;
        if (rowNum < 1 || rowNum > (int)CellAddress.MaxRow ||
            colNum < 1 || colNum > (int)CellAddress.MaxCol) return ErrorValue.Value;
        double rawAbsNum = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 1;
        if (!double.IsFinite(rawAbsNum)) return ErrorValue.Value;
        int absNum = (int)rawAbsNum;
        if (absNum is not (1 or 2 or 3 or 4)) return ErrorValue.Value;
        bool useA1 = args.Count < 4 || args[3] is BlankValue || ToBool(args[3]);
        string? sheetText = args.Count > 4 && args[4] is not BlankValue ? ToText(args[4]) : null;
        string colLetter = CellAddress.NumberToColumnName((uint)colNum);
        bool colAbs = absNum is 1 or 3;
        bool rowAbs = absNum is 1 or 2;
        string addr = useA1
            ? $"{(colAbs ? "$" : "")}{colLetter}{(rowAbs ? "$" : "")}{rowNum}"
            : $"{(rowAbs ? $"R{rowNum}" : $"R[{rowNum}]")}{(colAbs ? $"C{colNum}" : $"C[{colNum}]")}";
        if (!string.IsNullOrEmpty(sheetText))
            addr = $"'{sheetText}'!{addr}";
        return new TextValue(addr);
    }

    private static ScalarValue Lookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue lookupVec) return ErrorValue.Value;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var lookupFlat = lookupVec.Flatten();
        if (args.Count > 2 && args[2] is not RangeValue) return ErrorValue.Value;
        var resultFlat = args.Count > 2 && args[2] is RangeValue rv
            ? rv.Flatten()
            : lookupFlat;
        var lookupVal = args[0];
        int matchIdx = -1;
        for (int i = 0; i < lookupFlat.Count; i++)
        {
            if (lookupFlat[i] is ErrorValue lErr) return lErr;
            if (CompareScalar(lookupFlat[i], lookupVal) <= 0)
                matchIdx = i;
        }
        if (matchIdx < 0) return ErrorValue.NA;
        return matchIdx < resultFlat.Count ? resultFlat[matchIdx] : ErrorValue.NA;
    }

    private static ScalarValue NFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        args[0] switch
        {
            NumberValue nv   => nv,
            DateTimeValue dt => new NumberValue(dt.Value),
            BoolValue bv     => new NumberValue(bv.Value ? 1 : 0),
            ErrorValue ev    => ev,
            _                => new NumberValue(0)
        };

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4b  –  Dynamic arrays
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Sequence(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        double rawRows = ToNumber(args[0]);
        double rawCols = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 1;
        double start = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 1;
        double step  = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 1;
        if (!double.IsFinite(rawRows) || !double.IsFinite(rawCols)) return ErrorValue.Value;
        if (!double.IsFinite(start) || !double.IsFinite(step)) return ErrorValue.Num;
        int rows = (int)rawRows;
        int cols = (int)rawCols;
        if (rows < 1 || cols < 1) return ErrorValue.Value;
        if ((long)rows * cols > 1_000_000) return ErrorValue.Value;
        var cells = new ScalarValue[rows, cols];
        double val = start;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (!double.IsFinite(val)) return ErrorValue.Num;
                cells[r, c] = new NumberValue(val);
                val += step;
            }
        return new RangeValue(cells);
    }

    private static ScalarValue Filter(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args[1] is ErrorValue includeError) return includeError;
        if (args[1] is not RangeValue include) return ErrorValue.Value;
        var ifEmpty = args.Count > 2 ? args[2] : new TextValue("");

        if (include.ColCount == 1 && include.RowCount == arr.RowCount)
            return FilterRows(arr, include, ifEmpty);

        if (include.RowCount == 1 && include.ColCount == arr.ColCount)
            return FilterColumns(arr, include, ifEmpty);

        return ErrorValue.Value;
    }

    private static ScalarValue FilterRows(RangeValue arr, RangeValue include, ScalarValue ifEmpty)
    {
        var matchedRows = new List<int>();
        for (int i = 0; i < arr.RowCount; i++)
        {
            var v = include.Cells[i, 0];
            if (v is ErrorValue e) return e;
            if (IsFilterIncluded(v)) matchedRows.Add(i);
        }

        if (matchedRows.Count == 0)
            return FilterEmptyResult(ifEmpty);

        var result = new ScalarValue[matchedRows.Count, arr.ColCount];
        for (int ri = 0; ri < matchedRows.Count; ri++)
            for (int c = 0; c < arr.ColCount; c++)
                result[ri, c] = arr.Cells[matchedRows[ri], c];
        return new RangeValue(result);
    }

    private static ScalarValue FilterColumns(RangeValue arr, RangeValue include, ScalarValue ifEmpty)
    {
        var matchedCols = new List<int>();
        for (int c = 0; c < arr.ColCount; c++)
        {
            var v = include.Cells[0, c];
            if (v is ErrorValue e) return e;
            if (IsFilterIncluded(v)) matchedCols.Add(c);
        }

        if (matchedCols.Count == 0)
            return FilterEmptyResult(ifEmpty);

        var result = new ScalarValue[arr.RowCount, matchedCols.Count];
        for (int r = 0; r < arr.RowCount; r++)
            for (int ci = 0; ci < matchedCols.Count; ci++)
                result[r, ci] = arr.Cells[r, matchedCols[ci]];
        return new RangeValue(result);
    }

    private static bool IsFilterIncluded(ScalarValue value) =>
        value is BoolValue { Value: true } || (TryCellNumber(value, out double number) && number != 0);

    private static ScalarValue FilterEmptyResult(ScalarValue ifEmpty) =>
        ifEmpty is RangeValue rvEmpty
            ? rvEmpty
            : new RangeValue(new ScalarValue[1, 1] { { ifEmpty } });

    private static ScalarValue Sort(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        double sortIdxRaw   = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 1;
        double sortOrderRaw = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 1;
        if (!double.IsFinite(sortIdxRaw) || !double.IsFinite(sortOrderRaw)) return ErrorValue.Value;
        int sortIdx   = (int)sortIdxRaw - 1;
        if (sortIdx < 0) return ErrorValue.Value;
        int sortOrder = (int)sortOrderRaw;
        if (sortOrder != 1 && sortOrder != -1) return ErrorValue.Value;
        bool byCol    = args.Count > 3 && args[3] is not BlankValue && ToBool(args[3]);
        if (!byCol && sortIdx >= arr.ColCount) return ErrorValue.Value;
        if (byCol && sortIdx >= arr.RowCount) return ErrorValue.Value;

        if (!byCol)
        {
            var rowIndices = Enumerable.Range(0, arr.RowCount).ToList();
            rowIndices.Sort((a, b) =>
            {
                var va = sortIdx < arr.ColCount ? arr.Cells[a, sortIdx] : BlankValue.Instance;
                var vb = sortIdx < arr.ColCount ? arr.Cells[b, sortIdx] : BlankValue.Instance;
                return sortOrder * CompareScalar(va, vb);
            });
            var result = new ScalarValue[arr.RowCount, arr.ColCount];
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[r, c] = arr.Cells[rowIndices[r], c];
            return new RangeValue(result);
        }
        else
        {
            var colIndices = Enumerable.Range(0, arr.ColCount).ToList();
            colIndices.Sort((a, b) =>
            {
                var va = sortIdx < arr.RowCount ? arr.Cells[sortIdx, a] : BlankValue.Instance;
                var vb = sortIdx < arr.RowCount ? arr.Cells[sortIdx, b] : BlankValue.Instance;
                return sortOrder * CompareScalar(va, vb);
            });
            var result = new ScalarValue[arr.RowCount, arr.ColCount];
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[r, c] = arr.Cells[r, colIndices[c]];
            return new RangeValue(result);
        }
    }

    private static ScalarValue SortBy(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;

        var keys = new List<(RangeValue Range, int Order)>();
        bool? sortRows = null;

        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] is ErrorValue keyError) return keyError;
            if (args[i] is not RangeValue byArray) return ErrorValue.Value;

            if (!TryGetSortByOrientation(arr, byArray, out bool keySortsRows)) return ErrorValue.Value;
            if (sortRows.HasValue && sortRows.Value != keySortsRows) return ErrorValue.Value;
            sortRows ??= keySortsRows;

            int sortOrder = 1;
            if (i + 1 < args.Count && args[i + 1] is not RangeValue)
            {
                if (args[i + 1] is ErrorValue orderError) return orderError;
                double orderRaw = ToNumber(args[i + 1]);
                if (!double.IsFinite(orderRaw)) return ErrorValue.Value;
                sortOrder = (int)orderRaw;
                if (sortOrder != 1 && sortOrder != -1) return ErrorValue.Value;
                i++;
            }

            keys.Add((byArray, sortOrder));
        }

        if (keys.Count == 0) return ErrorValue.Value;
        return sortRows.GetValueOrDefault(true)
            ? SortByRows(arr, keys)
            : SortByColumns(arr, keys);
    }

    private static bool TryGetSortByOrientation(RangeValue arr, RangeValue byArray, out bool sortRows)
    {
        if (byArray.RowCount == arr.RowCount && byArray.ColCount == 1)
        {
            sortRows = true;
            return true;
        }

        if (byArray.RowCount == 1 && byArray.ColCount == arr.ColCount)
        {
            sortRows = false;
            return true;
        }

        sortRows = true;
        return false;
    }

    private static ScalarValue SortByRows(RangeValue arr, IReadOnlyList<(RangeValue Range, int Order)> keys)
    {
        var rowIndices = Enumerable.Range(0, arr.RowCount).ToList();
        rowIndices.Sort((a, b) =>
        {
            foreach (var key in keys)
            {
                int cmp = CompareScalar(key.Range.Cells[a, 0], key.Range.Cells[b, 0]);
                if (cmp != 0) return key.Order * cmp;
            }

            return a.CompareTo(b);
        });

        var result = new ScalarValue[arr.RowCount, arr.ColCount];
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[rowIndices[r], c];
        return new RangeValue(result);
    }

    private static ScalarValue SortByColumns(RangeValue arr, IReadOnlyList<(RangeValue Range, int Order)> keys)
    {
        var colIndices = Enumerable.Range(0, arr.ColCount).ToList();
        colIndices.Sort((a, b) =>
        {
            foreach (var key in keys)
            {
                int cmp = CompareScalar(key.Range.Cells[0, a], key.Range.Cells[0, b]);
                if (cmp != 0) return key.Order * cmp;
            }

            return a.CompareTo(b);
        });

        var result = new ScalarValue[arr.RowCount, arr.ColCount];
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[r, colIndices[c]];
        return new RangeValue(result);
    }

    private static ScalarValue Take(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args[1] is ErrorValue rowError) return rowError;
        if (args.Count > 2 && args[2] is ErrorValue colError) return colError;

        if (!TryGetArraySliceCount(args[1], arr.RowCount, isTake: true, out int rowStart, out int rowCount))
            return ErrorValue.Value;

        int colStart = 0;
        int colCount = arr.ColCount;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetArraySliceCount(args[2], arr.ColCount, isTake: true, out colStart, out colCount))
                return ErrorValue.Value;
        }

        return SliceRange(arr, rowStart, colStart, rowCount, colCount);
    }

    private static ScalarValue Drop(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args[1] is ErrorValue rowError) return rowError;
        if (args.Count > 2 && args[2] is ErrorValue colError) return colError;

        if (!TryGetArraySliceCount(args[1], arr.RowCount, isTake: false, out int rowStart, out int rowCount))
            return ErrorValue.Value;

        int colStart = 0;
        int colCount = arr.ColCount;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetArraySliceCount(args[2], arr.ColCount, isTake: false, out colStart, out colCount))
                return ErrorValue.Value;
        }

        return SliceRange(arr, rowStart, colStart, rowCount, colCount);
    }

    private static bool TryGetArraySliceCount(
        ScalarValue countValue,
        int dimensionLength,
        bool isTake,
        out int start,
        out int count)
    {
        double raw = ToNumber(countValue);
        if (!double.IsFinite(raw))
        {
            start = 0;
            count = 0;
            return false;
        }

        int requested = (int)raw;
        if (requested == 0)
        {
            if (!isTake)
            {
                // DROP 0 → return entire array unchanged
                start = 0;
                count = dimensionLength;
                return true;
            }
            // TAKE 0 → empty result
            start = 0;
            count = 0;
            return false;
        }

        if (isTake)
        {
            count = Math.Min(Math.Abs(requested), dimensionLength);
            start = requested > 0 ? 0 : dimensionLength - count;
            return count > 0;
        }

        if (Math.Abs(requested) >= dimensionLength)
        {
            start = 0;
            count = 0;
            return false;
        }

        if (requested > 0)
        {
            start = requested;
            count = dimensionLength - requested;
        }
        else
        {
            start = 0;
            count = dimensionLength + requested;
        }

        return count > 0;
    }

    private static RangeValue SliceRange(RangeValue arr, int rowStart, int colStart, int rowCount, int colCount)
    {
        var result = new ScalarValue[rowCount, colCount];
        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
                result[r, c] = arr.Cells[rowStart + r, colStart + c];
        return new RangeValue(result);
    }

    private static ScalarValue ChooseRows(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (!TryResolveChoiceIndexes(args, arr.RowCount, out var rowIndexes, out var error)) return error;

        var result = new ScalarValue[rowIndexes.Count, arr.ColCount];
        for (int r = 0; r < rowIndexes.Count; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[rowIndexes[r], c];
        return new RangeValue(result);
    }

    private static ScalarValue ChooseCols(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (!TryResolveChoiceIndexes(args, arr.ColCount, out var colIndexes, out var error)) return error;

        var result = new ScalarValue[arr.RowCount, colIndexes.Count];
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < colIndexes.Count; c++)
                result[r, c] = arr.Cells[r, colIndexes[c]];
        return new RangeValue(result);
    }

    private static bool TryResolveChoiceIndexes(
        IReadOnlyList<ScalarValue> args,
        int dimensionLength,
        out List<int> indexes,
        out ScalarValue error)
    {
        indexes = new List<int>();
        error = ErrorValue.Value;

        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] is ErrorValue e)
            {
                error = e;
                return false;
            }

            double raw = ToNumber(args[i]);
            if (!double.IsFinite(raw)) return false;

            int requested = (int)raw;
            if (requested == 0) return false;

            int zeroBased = requested > 0
                ? requested - 1
                : dimensionLength + requested;
            if (zeroBased < 0 || zeroBased >= dimensionLength) return false;

            indexes.Add(zeroBased);
        }

        return indexes.Count > 0;
    }

    private static ScalarValue VStack(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryCollectStackArrays(args, out var arrays, out var error)) return error;

        long rowCountL = 0;
        foreach (var a in arrays) rowCountL += a.RowCount;
        int colCount = arrays.Max(a => a.ColCount);
        if (rowCountL * colCount > 1_000_000) return ErrorValue.Value;
        int rowCount = (int)rowCountL;
        var result = CreateFilledRange(rowCount, colCount, ErrorValue.NA);

        int rowOffset = 0;
        foreach (var arr in arrays)
        {
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[rowOffset + r, c] = arr.Cells[r, c];
            rowOffset += arr.RowCount;
        }

        return new RangeValue(result);
    }

    private static ScalarValue HStack(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryCollectStackArrays(args, out var arrays, out var error)) return error;

        int rowCount = arrays.Max(a => a.RowCount);
        long colCountL = 0;
        foreach (var a in arrays) colCountL += a.ColCount;
        if ((long)rowCount * colCountL > 1_000_000) return ErrorValue.Value;
        int colCount = (int)colCountL;
        var result = CreateFilledRange(rowCount, colCount, ErrorValue.NA);

        int colOffset = 0;
        foreach (var arr in arrays)
        {
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[r, colOffset + c] = arr.Cells[r, c];
            colOffset += arr.ColCount;
        }

        return new RangeValue(result);
    }

    private static bool TryCollectStackArrays(
        IReadOnlyList<ScalarValue> args,
        out List<RangeValue> arrays,
        out ScalarValue error)
    {
        arrays = new List<RangeValue>();
        error = ErrorValue.Value;

        foreach (var arg in args)
        {
            if (arg is ErrorValue e)
            {
                error = e;
                return false;
            }

            if (arg is not RangeValue arr) return false;
            arrays.Add(arr);
        }

        return arrays.Count > 0;
    }

    private static ScalarValue[,] CreateFilledRange(int rowCount, int colCount, ScalarValue value)
    {
        var result = new ScalarValue[rowCount, colCount];
        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
                result[r, c] = value;
        return result;
    }

    private static ScalarValue ToRow(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryFlattenArray(args, out var values, out var error)) return error;
        if (values.Count == 0) return ErrorValue.Value;
        if (values.Count > 1_000_000) return ErrorValue.Value;

        var result = new ScalarValue[1, values.Count];
        for (int c = 0; c < values.Count; c++)
            result[0, c] = values[c];
        return new RangeValue(result);
    }

    private static ScalarValue ToCol(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryFlattenArray(args, out var values, out var error)) return error;
        if (values.Count == 0) return ErrorValue.Value;
        if (values.Count > 1_000_000) return ErrorValue.Value;

        var result = new ScalarValue[values.Count, 1];
        for (int r = 0; r < values.Count; r++)
            result[r, 0] = values[r];
        return new RangeValue(result);
    }

    private static bool TryFlattenArray(
        IReadOnlyList<ScalarValue> args,
        out List<ScalarValue> values,
        out ScalarValue error)
    {
        values = new List<ScalarValue>();
        error = ErrorValue.Value;

        if (args[0] is ErrorValue arrayError)
        {
            error = arrayError;
            return false;
        }

        if (args[0] is not RangeValue arr) return false;

        int ignore = 0;
        if (args.Count > 1 && args[1] is not BlankValue)
        {
            if (args[1] is ErrorValue ignoreError)
            {
                error = ignoreError;
                return false;
            }

            double rawIgnore = ToNumber(args[1]);
            if (!double.IsFinite(rawIgnore)) return false;
            ignore = (int)rawIgnore;
            if (ignore is < 0 or > 3) return false;
        }

        bool scanByColumn = false;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (args[2] is ErrorValue scanError)
            {
                error = scanError;
                return false;
            }

            scanByColumn = ToBool(args[2]);
        }

        bool ignoreBlanks = (ignore & 1) != 0;
        bool ignoreErrors = (ignore & 2) != 0;

        if (scanByColumn)
        {
            for (int c = 0; c < arr.ColCount; c++)
                for (int r = 0; r < arr.RowCount; r++)
                    AddFlattenedValue(arr.Cells[r, c], ignoreBlanks, ignoreErrors, values);
        }
        else
        {
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    AddFlattenedValue(arr.Cells[r, c], ignoreBlanks, ignoreErrors, values);
        }

        return true;
    }

    private static void AddFlattenedValue(
        ScalarValue value,
        bool ignoreBlanks,
        bool ignoreErrors,
        List<ScalarValue> values)
    {
        if (ignoreBlanks && (value is BlankValue || value is TextValue { Value.Length: 0 })) return;
        if (ignoreErrors && value is ErrorValue) return;
        values.Add(value);
    }

    private static ScalarValue WrapRows(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryGetWrapArgs(args, out var values, out int wrapCount, out var padWith, out var error)) return error;

        int rowCount = (values.Count + wrapCount - 1) / wrapCount;
        if ((long)rowCount * wrapCount > 1_000_000) return ErrorValue.Value;
        var result = CreateFilledRange(rowCount, wrapCount, padWith);
        for (int i = 0; i < values.Count; i++)
            result[i / wrapCount, i % wrapCount] = values[i];
        return new RangeValue(result);
    }

    private static ScalarValue WrapCols(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryGetWrapArgs(args, out var values, out int wrapCount, out var padWith, out var error)) return error;

        int colCount = (values.Count + wrapCount - 1) / wrapCount;
        if ((long)wrapCount * colCount > 1_000_000) return ErrorValue.Value;
        var result = CreateFilledRange(wrapCount, colCount, padWith);
        for (int i = 0; i < values.Count; i++)
            result[i % wrapCount, i / wrapCount] = values[i];
        return new RangeValue(result);
    }

    private static bool TryGetWrapArgs(
        IReadOnlyList<ScalarValue> args,
        out List<ScalarValue> values,
        out int wrapCount,
        out ScalarValue padWith,
        out ScalarValue error)
    {
        values = new List<ScalarValue>();
        wrapCount = 0;
        padWith = ErrorValue.NA;
        error = ErrorValue.Value;

        if (args[0] is ErrorValue arrayError)
        {
            error = arrayError;
            return false;
        }

        if (args[0] is not RangeValue arr) return false;
        if (!TryReadVector(arr, values)) return false;

        if (args[1] is ErrorValue countError)
        {
            error = countError;
            return false;
        }

        double rawWrapCount = ToNumber(args[1]);
        if (!double.IsFinite(rawWrapCount)) return false;
        wrapCount = (int)rawWrapCount;
        if (wrapCount < 1)
        {
            error = ErrorValue.Num;
            return false;
        }

        if (args.Count > 2) padWith = args[2];
        return values.Count > 0;
    }

    private static bool TryReadVector(RangeValue arr, List<ScalarValue> values)
    {
        if (arr.RowCount == 1)
        {
            for (int c = 0; c < arr.ColCount; c++)
                values.Add(arr.Cells[0, c]);
            return true;
        }

        if (arr.ColCount == 1)
        {
            for (int r = 0; r < arr.RowCount; r++)
                values.Add(arr.Cells[r, 0]);
            return true;
        }

        return false;
    }

    private static ScalarValue Expand(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args[1] is ErrorValue rowError) return rowError;
        if (args.Count > 2 && args[2] is ErrorValue colError) return colError;

        if (!TryGetExpandDimension(args[1], arr.RowCount, out int rowCount)) return ErrorValue.Value;
        int colCount = arr.ColCount;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetExpandDimension(args[2], arr.ColCount, out colCount)) return ErrorValue.Value;
        }

        if (rowCount < arr.RowCount || colCount < arr.ColCount) return ErrorValue.Value;

        var padWith = args.Count > 3 ? args[3] : ErrorValue.NA;
        var result = CreateFilledRange(rowCount, colCount, padWith);
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[r, c];
        return new RangeValue(result);
    }

    private static bool TryGetExpandDimension(ScalarValue value, int originalLength, out int dimension)
    {
        dimension = originalLength;
        if (value is BlankValue) return true;

        double raw = ToNumber(value);
        if (!double.IsFinite(raw) || raw > int.MaxValue) return false;
        dimension = (int)raw;
        return dimension >= 1;
    }

    private static ScalarValue Unique(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        bool byCol       = args.Count > 1 && args[1] is not BlankValue && ToBool(args[1]);
        bool exactlyOnce = args.Count > 2 && args[2] is not BlankValue && ToBool(args[2]);

        if (!byCol)
        {
            var keyOrder  = new List<string>();
            var keyIndex  = new Dictionary<string, int>();
            var keyCounts = new List<int>();
            var rowOfKey  = new List<int>();

            var keySb = new System.Text.StringBuilder();
            for (int r = 0; r < arr.RowCount; r++)
            {
                keySb.Clear();
                for (int c = 0; c < arr.ColCount; c++)
                {
                    if (c > 0) keySb.Append('\0');
                    keySb.Append(ToText(arr.Cells[r, c]));
                }
                var key = keySb.ToString();
                if (keyIndex.TryGetValue(key, out int idx))
                {
                    keyCounts[idx]++;
                }
                else
                {
                    keyIndex[key] = keyOrder.Count;
                    keyOrder.Add(key);
                    keyCounts.Add(1);
                    rowOfKey.Add(r);
                }
            }

            var selected = keyOrder
                .Select((k, i) => (key: k, idx: i))
                .Where(t => !exactlyOnce || keyCounts[t.idx] == 1)
                .Select(t => rowOfKey[t.idx])
                .ToList();

            if (selected.Count == 0) return ErrorValue.NA;
            var result = new ScalarValue[selected.Count, arr.ColCount];
            for (int ri = 0; ri < selected.Count; ri++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[ri, c] = arr.Cells[selected[ri], c];
            return new RangeValue(result);
        }
        else
        {
            var keyOrder  = new List<string>();
            var keyIndex  = new Dictionary<string, int>();
            var keyCounts = new List<int>();
            var colOfKey  = new List<int>();

            var colKeySb = new System.Text.StringBuilder();
            for (int c = 0; c < arr.ColCount; c++)
            {
                colKeySb.Clear();
                for (int r = 0; r < arr.RowCount; r++)
                {
                    if (r > 0) colKeySb.Append('\0');
                    colKeySb.Append(ToText(arr.Cells[r, c]));
                }
                var key = colKeySb.ToString();
                if (keyIndex.TryGetValue(key, out int idx))
                {
                    keyCounts[idx]++;
                }
                else
                {
                    keyIndex[key] = keyOrder.Count;
                    keyOrder.Add(key);
                    keyCounts.Add(1);
                    colOfKey.Add(c);
                }
            }

            var selected = keyOrder
                .Select((k, i) => (key: k, idx: i))
                .Where(t => !exactlyOnce || keyCounts[t.idx] == 1)
                .Select(t => colOfKey[t.idx])
                .ToList();

            if (selected.Count == 0) return ErrorValue.NA;
            var result = new ScalarValue[arr.RowCount, selected.Count];
            for (int r = 0; r < arr.RowCount; r++)
                for (int ci = 0; ci < selected.Count; ci++)
                    result[r, ci] = arr.Cells[r, selected[ci]];
            return new RangeValue(result);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // SUBTOTAL
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Subtotal(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var funcNumD = ToNumber(args[0]);
        if (!double.IsFinite(funcNumD)) return ErrorValue.Value;
        int funcNum = (int)funcNumD;
        bool skipHidden = funcNum >= 101;
        int baseFunc = funcNum > 100 ? funcNum - 100 : funcNum;

        // Collect all numeric values from remaining args, respecting hidden-row exclusion
        var nums = new List<double>();
        int countaCount = 0;
        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] is ErrorValue ei) return ei;
            if (args[i] is RangeValue rv)
            {
                for (int r = 0; r < rv.RowCount; r++)
                {
                    uint absRow = rv.StartRow + (uint)r;
                    if (skipHidden && ctx.IsRowHidden(absRow)) continue;
                    for (int c = 0; c < rv.ColCount; c++)
                    {
                        var cell = rv.Cells[r, c];
                        if (cell is ErrorValue err) return err;
                        if (TryCellNumber(cell, out double value)) nums.Add(value);
                        if (cell is not BlankValue) countaCount++;
                    }
                }
            }
            else if (TryCellNumber(args[i], out double scalarNum))
            {
                nums.Add(scalarNum);
                countaCount++;
            }
            else if (args[i] is not BlankValue)
            {
                countaCount++;
            }
        }

        return baseFunc switch
        {
            1  => nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(nums.Average()),
            2  => new NumberValue(nums.Count),
            3  => new NumberValue(countaCount),
            4  => nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(nums.Max()),
            5  => nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(nums.Min()),
            6  => NumberResult(nums.Count == 0 ? 0 : nums.Aggregate(1.0, (acc, x) => acc * x)),
            7  => nums.Count < 2 ? ErrorValue.DivByZero : NumberResult(SubtotalStdDevS(nums)),
            8  => nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(SubtotalStdDevP(nums)),
            9  => NumberResult(nums.Sum()),
            10 => nums.Count < 2 ? ErrorValue.DivByZero : NumberResult(SubtotalVarS(nums)),
            11 => nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(SubtotalVarP(nums)),
            _  => ErrorValue.Value
        };
    }

    private static double SubtotalVarS(List<double> nums)
    {
        double mean = nums.Average();
        return nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1);
    }

    private static double SubtotalVarP(List<double> nums)
    {
        double mean = nums.Average();
        return nums.Sum(x => (x - mean) * (x - mean)) / nums.Count;
    }

    private static double SubtotalStdDevS(List<double> nums) => Math.Sqrt(SubtotalVarS(nums));
    private static double SubtotalStdDevP(List<double> nums) => Math.Sqrt(SubtotalVarP(nums));

    // ════════════════════════════════════════════════════════════════════════
    // Phase A1 – Text: UNICHAR, UNICODE, NUMBERVALUE
    // ════════════════════════════════════════════════════════════════════════

    private static ScalarValue RankEq(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        Rank(args, ctx);

    private static ScalarValue RankAvg(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue range) return ErrorValue.Value;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var number = ToNumber(args[0]);
        if (!double.IsFinite(number)) return ErrorValue.Num;
        double rawOrder = args.Count > 2 ? ToNumber(args[2]) : 0;
        if (!double.IsFinite(rawOrder)) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(range);
        if (err is not null) return err;
        if (!nums!.Contains(number)) return ErrorValue.NA;
        int betterCount = rawOrder == 0 ? nums.Count(x => x > number) : nums.Count(x => x < number);
        int tieCount = nums.Count(x => x == number);
        return new NumberValue(betterCount + 1 + (tieCount - 1) / 2.0);
    }

    private static ScalarValue Devsq(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var nums = new List<double>();
        foreach (var arg in args)
        {
            if (arg is ErrorValue e) return e;
            if (arg is RangeValue rv)
            {
                var (rangeNums, rangeError) = CollectRangeNumbers(rv);
                if (rangeError is not null) return rangeError;
                nums.AddRange(rangeNums!);
            }
            else if (arg is NumberValue nv) nums.Add(nv.Value);
            else if (arg is DateTimeValue dt) nums.Add(dt.Value);
            else if (arg is BoolValue bv) nums.Add(bv.Value ? 1 : 0);
            else if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                nums.Add(value);
            }
        }
        if (nums.Count == 0) return ErrorValue.DivByZero;
        double mean = nums.Average();
        return NumberResult(nums.Sum(x => (x - mean) * (x - mean)));
    }

    private static ScalarValue Unichar(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Value;
        int codePoint = (int)Math.Truncate(n);
        if (codePoint <= 0 || codePoint > 0x10FFFF) return ErrorValue.Value;
        if (codePoint >= 0xD800 && codePoint <= 0xDFFF) return ErrorValue.Value; // surrogate halves
        if (n != codePoint) return ErrorValue.Value; // non-integer
        return new TextValue(char.ConvertFromUtf32(codePoint));
    }

    private static ScalarValue UnicodeFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is not TextValue and not DirectTextLiteralValue) return ErrorValue.Value;
        var text = ToText(args[0]);
        if (text.Length == 0) return ErrorValue.Value;
        if (char.IsHighSurrogate(text[0]))
        {
            if (text.Length < 2 || !char.IsLowSurrogate(text[1])) return ErrorValue.Value;
            return new NumberValue(char.ConvertToUtf32(text[0], text[1]));
        }
        if (char.IsLowSurrogate(text[0])) return ErrorValue.Value;
        return new NumberValue(text[0]);
    }

    private static ScalarValue Numbervalue(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        var text = ToText(args[0]).Trim();
        var decSep = args.Count > 1 && args[1] is not BlankValue ? ToText(args[1]) : ".";
        var grpSep = args.Count > 2 && args[2] is not BlankValue ? ToText(args[2]) : ",";

        // Validate separators per Excel spec
        if (decSep.Length != 1) return ErrorValue.Value;
        if (grpSep.Length == 0) return ErrorValue.Value;
        if (decSep == grpSep) return ErrorValue.Value;
        if (grpSep.Contains(decSep)) return ErrorValue.Value;

        // Strip whitespace (Excel allows whitespace anywhere)
        text = text.Replace(" ", "").Replace("\t", "");

        // Trailing percent
        int pctCount = 0;
        while (text.EndsWith('%'))
        {
            pctCount++;
            text = text[..^1];
        }

        // Remove all group separator characters
        text = text.Replace(grpSep, string.Empty, StringComparison.Ordinal);
        // Substitute decimal separator with '.'
        if (decSep != ".") text = text.Replace(decSep, ".", StringComparison.Ordinal);

        if (text.Length == 0) return new NumberValue(0);

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return ErrorValue.Value;

        for (int i = 0; i < pctCount; i++) v /= 100.0;
        return NumberResult(v);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Phase A1 – Math: SQRTPI, MULTINOMIAL, SERIESSUM
    // ════════════════════════════════════════════════════════════════════════

    private static ScalarValue Sqrtpi(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n) || n < 0) return ErrorValue.Num;
        return NumberResult(Math.Sqrt(n * Math.PI));
    }

    private static ScalarValue Multinomial(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var values = new List<int>();
        long sum = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is RangeValue rv)
            {
                foreach (var cell in rv.Flatten())
                {
                    if (cell is ErrorValue ec) return ec;
                    if (cell is BlankValue) continue;
                    if (!TryCellNumber(cell, out double d)) return ErrorValue.Value;
                    if (!double.IsFinite(d) || d < 0) return ErrorValue.Num;
                    int n = (int)Math.Truncate(d);
                    values.Add(n);
                    sum += n;
                }
            }
            else
            {
                if (arg is BlankValue) continue;
                double d;
                if (!TryCellNumber(arg, out d))
                {
                    d = ToNumber(arg);
                }
                if (!double.IsFinite(d) || d < 0) return ErrorValue.Num;
                int n = (int)Math.Truncate(d);
                values.Add(n);
                sum += n;
            }
        }
        if (values.Count == 0) return ErrorValue.Value;

        // Use log-gamma to avoid overflow: log(sum!) - sum(log(n_i!))
        double logResult = LogGamma(sum + 1.0);
        foreach (var v in values)
            logResult -= LogGamma(v + 1.0);

        if (logResult > Math.Log(1e308)) return ErrorValue.Num;
        double result = Math.Round(Math.Exp(logResult));
        return NumberResult(result);
    }

    /// <summary>Lanczos approximation of ln(Γ(x)) for x > 0.</summary>
    private static ScalarValue SeriesSum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;

        double x = ToNumber(args[0]);
        double n = ToNumber(args[1]);
        double m = ToNumber(args[2]);
        if (!double.IsFinite(x) || !double.IsFinite(n) || !double.IsFinite(m))
            return ErrorValue.Num;

        if (args[3] is not RangeValue coeffs) return ErrorValue.Value;

        double sum = 0;
        int i = 0;
        foreach (var cell in coeffs.Flatten())
        {
            if (cell is ErrorValue ec) return ec;
            if (cell is BlankValue) { i++; continue; }
            if (!TryCellNumber(cell, out double a)) return ErrorValue.Value;
            sum += a * Math.Pow(x, n + i * m);
            i++;
        }
        return NumberResult(sum);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Phase A1 – Matrix: MMULT, MINVERSE, MDETERM
    // ════════════════════════════════════════════════════════════════════════

    private static bool TryRangeToMatrix(ScalarValue value, out double[,] matrix, out ScalarValue? error)
    {
        matrix = null!;
        error = null;
        if (value is ErrorValue err) { error = err; return false; }
        if (value is not RangeValue rv) { error = ErrorValue.Value; return false; }
        int rows = rv.RowCount;
        int cols = rv.ColCount;
        var m = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var cell = rv.Cells[r, c];
                if (cell is ErrorValue ecell) { error = ecell; return false; }
                if (!TryCellNumber(cell, out double d))
                {
                    error = ErrorValue.Value;
                    return false;
                }
                m[r, c] = d;
            }
        }
        matrix = m;
        return true;
    }

    private static ScalarValue Mmult(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryRangeToMatrix(args[0], out var a, out var ea)) return ea!;
        if (!TryRangeToMatrix(args[1], out var b, out var eb)) return eb!;

        int m = a.GetLength(0);
        int k = a.GetLength(1);
        int k2 = b.GetLength(0);
        int n = b.GetLength(1);
        if (k != k2) return ErrorValue.Value;

        var result = new ScalarValue[m, n];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double sum = 0;
                for (int p = 0; p < k; p++)
                    sum += a[i, p] * b[p, j];
                if (!double.IsFinite(sum)) return ErrorValue.Num;
                result[i, j] = new NumberValue(sum);
            }
        }
        return new RangeValue(result);
    }

    private static ScalarValue Minverse(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryRangeToMatrix(args[0], out var a, out var ea)) return ea!;
        int n = a.GetLength(0);
        if (a.GetLength(1) != n) return ErrorValue.Value;

        // Build augmented matrix [A | I]
        var aug = new double[n, 2 * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) aug[i, j] = a[i, j];
            aug[i, n + i] = 1.0;
        }

        // Gauss-Jordan elimination with partial pivoting
        for (int col = 0; col < n; col++)
        {
            int pivotRow = col;
            double pivotMax = Math.Abs(aug[col, col]);
            for (int r = col + 1; r < n; r++)
            {
                double v = Math.Abs(aug[r, col]);
                if (v > pivotMax) { pivotMax = v; pivotRow = r; }
            }
            if (pivotMax < 1e-14) return ErrorValue.Num; // singular

            if (pivotRow != col)
            {
                for (int j = 0; j < 2 * n; j++)
                    (aug[col, j], aug[pivotRow, j]) = (aug[pivotRow, j], aug[col, j]);
            }

            double pivot = aug[col, col];
            for (int j = 0; j < 2 * n; j++) aug[col, j] /= pivot;

            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double factor = aug[r, col];
                if (factor == 0) continue;
                for (int j = 0; j < 2 * n; j++)
                    aug[r, j] -= factor * aug[col, j];
            }
        }

        var result = new ScalarValue[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                double v = aug[i, n + j];
                if (!double.IsFinite(v)) return ErrorValue.Num;
                result[i, j] = new NumberValue(v);
            }
        return new RangeValue(result);
    }

    private static ScalarValue Mdeterm(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryRangeToMatrix(args[0], out var a, out var ea)) return ea!;
        int n = a.GetLength(0);
        if (a.GetLength(1) != n) return ErrorValue.Value;

        // LU decomposition with partial pivoting; det = product of U diagonals * (-1)^swaps
        var lu = (double[,])a.Clone();
        int swaps = 0;
        for (int col = 0; col < n; col++)
        {
            int pivotRow = col;
            double pivotMax = Math.Abs(lu[col, col]);
            for (int r = col + 1; r < n; r++)
            {
                double v = Math.Abs(lu[r, col]);
                if (v > pivotMax) { pivotMax = v; pivotRow = r; }
            }
            if (pivotMax < 1e-300) return new NumberValue(0);
            if (pivotRow != col)
            {
                for (int j = 0; j < n; j++)
                    (lu[col, j], lu[pivotRow, j]) = (lu[pivotRow, j], lu[col, j]);
                swaps++;
            }

            double pivot = lu[col, col];
            for (int r = col + 1; r < n; r++)
            {
                double factor = lu[r, col] / pivot;
                lu[r, col] = factor;
                for (int j = col + 1; j < n; j++)
                    lu[r, j] -= factor * lu[col, j];
            }
        }

        double det = (swaps % 2 == 0) ? 1.0 : -1.0;
        for (int i = 0; i < n; i++) det *= lu[i, i];
        return NumberResult(det);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Phase A1 – Date (weekend mask): NETWORKDAYS.INTL, WORKDAY.INTL
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses a weekend argument: number code 1-17 OR 7-char "0"/"1" string.
    /// Returns Mon-Sun mask (mask[0]=Mon,…,mask[6]=Sun). True = weekend day.
    /// </summary>
    private static (bool[]? Mask, ErrorValue? Error) ParseWeekendMask(ScalarValue value)
    {
        var mask = new bool[7];
        if (value is BlankValue)
        {
            mask[5] = true; // Sat
            mask[6] = true; // Sun
            return (mask, null);
        }

        if (value is TextValue or DirectTextLiteralValue)
        {
            var pattern = ToText(value);
            if (pattern.Length != 7) return (null, ErrorValue.Value);
            if (pattern.Any(c => c is not '0' and not '1')) return (null, ErrorValue.Value);
            if (pattern.All(c => c == '1')) return (null, ErrorValue.Value); // all-weekend not allowed
            for (int i = 0; i < 7; i++) mask[i] = pattern[i] == '1';
            return (mask, null);
        }

        double rawCode = ToNumber(value);
        if (!double.IsFinite(rawCode)) return (null, ErrorValue.Value);
        int code = (int)rawCode;
        // Mon=0..Sun=6
        switch (code)
        {
            case 1: mask[5] = true; mask[6] = true; break;        // Sat, Sun
            case 2: mask[6] = true; mask[0] = true; break;        // Sun, Mon
            case 3: mask[0] = true; mask[1] = true; break;        // Mon, Tue
            case 4: mask[1] = true; mask[2] = true; break;        // Tue, Wed
            case 5: mask[2] = true; mask[3] = true; break;        // Wed, Thu
            case 6: mask[3] = true; mask[4] = true; break;        // Thu, Fri
            case 7: mask[4] = true; mask[5] = true; break;        // Fri, Sat
            case 11: mask[6] = true; break;                       // Sun
            case 12: mask[0] = true; break;                       // Mon
            case 13: mask[1] = true; break;                       // Tue
            case 14: mask[2] = true; break;                       // Wed
            case 15: mask[3] = true; break;                       // Thu
            case 16: mask[4] = true; break;                       // Fri
            case 17: mask[5] = true; break;                       // Sat
            default: return (null, ErrorValue.Num);
        }
        return (mask, null);
    }

    /// <summary>Map DayOfWeek to a Mon=0..Sun=6 index.</summary>
    private static int DowToMonIndex(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday    => 0,
        DayOfWeek.Tuesday   => 1,
        DayOfWeek.Wednesday => 2,
        DayOfWeek.Thursday  => 3,
        DayOfWeek.Friday    => 4,
        DayOfWeek.Saturday  => 5,
        _                   => 6 // Sunday
    };

    private static int ExcelDowToMonIndex(DateTime date)
    {
        int serial = (int)Math.Floor(DateToSerial(date));
        return ((serial + 5) % 7 + 7) % 7;
    }

    private static int ExcelDowToMonIndex(int serial) => ((serial + 5) % 7 + 7) % 7;

    private static int ExcelIsoWeeknum(DateTime date)
    {
        int serial = (int)Math.Floor(DateToSerial(date));
        int dowMon0 = ExcelDowToMonIndex(serial);
        int thursdaySerial = serial + (3 - dowMon0);
        int weekYear = SerialToDate(thursdaySerial).Year;
        int jan4Serial = (int)Math.Floor(DateToSerial(new DateTime(weekYear, 1, 4)));
        int week1MondaySerial = jan4Serial - ExcelDowToMonIndex(jan4Serial);
        return (serial - week1MondaySerial) / 7 + 1;
    }

    private static HashSet<DateTime> CollectHolidays(ScalarValue? arg)
    {
        var holidays = new HashSet<DateTime>();
        if (arg is RangeValue rv)
        {
            foreach (var v in rv.Flatten())
                if (TryCellNumber(v, out double serial))
                    holidays.Add(SerialToDate(serial).Date);
        }
        else if (arg is not null && TryCellNumber(arg, out double s))
        {
            holidays.Add(SerialToDate(s).Date);
        }
        return holidays;
    }

    private static ScalarValue NetworkdaysIntl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        if (!TryOADateToDateTime(args[0], out var startRaw)) return ErrorValue.Num;
        if (!TryOADateToDateTime(args[1], out var endRaw)) return ErrorValue.Num;

        var (mask, maskErr) = ParseWeekendMask(args.Count > 2 ? args[2] : BlankValue.Instance);
        if (maskErr is not null) return maskErr;
        var holidays = args.Count > 3 ? CollectHolidays(args[3]) : new HashSet<DateTime>();

        var startDt = startRaw.Date;
        var endDt = endRaw.Date;
        int sign = startDt <= endDt ? 1 : -1;
        var lo = startDt <= endDt ? startDt : endDt;
        var hi = startDt <= endDt ? endDt : startDt;

        int count = 0;
        for (var d = lo; d <= hi; d = d.AddDays(1))
        {
            if (mask![ExcelDowToMonIndex(d)]) continue;
            if (holidays.Contains(d.Date)) continue;
            count++;
        }
        return new NumberValue(sign * count);
    }

    private static ScalarValue WorkdayIntl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        if (!TryOADateToDateTime(args[0], out var current)) return ErrorValue.Num;
        double rawDays = ToNumber(args[1]);
        if (!double.IsFinite(rawDays)) return ErrorValue.Num;
        if (rawDays < int.MinValue + 1 || rawDays > int.MaxValue) return ErrorValue.Num;
        int days = (int)rawDays;

        var (mask, maskErr) = ParseWeekendMask(args.Count > 2 ? args[2] : BlankValue.Instance);
        if (maskErr is not null) return maskErr;
        var holidays = args.Count > 3 ? CollectHolidays(args[3]) : new HashSet<DateTime>();

        int sign = days < 0 ? -1 : 1;
        int remaining = Math.Abs(days);
        while (remaining > 0)
        {
            current = current.AddDays(sign);
            if (mask![ExcelDowToMonIndex(current)]) continue;
            if (holidays.Contains(current.Date)) continue;
            remaining--;
        }
        return new NumberValue(DateToSerial(current));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Phase A1 – Lookup: TRANSPOSE
    // ════════════════════════════════════════════════════════════════════════

    private static ScalarValue Transpose(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args[0] is not RangeValue rv) return args[0]; // scalar passes through
        int rows = rv.RowCount;
        int cols = rv.ColCount;
        var result = new ScalarValue[cols, rows];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                result[c, r] = rv.Cells[r, c];
        return new RangeValue(result);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Phase A1 – Information: TYPE, ERROR.TYPE
    // ════════════════════════════════════════════════════════════════════════

    private static ScalarValue TypeFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        return args[0] switch
        {
            ErrorValue => new NumberValue(16),
            RangeValue => new NumberValue(64),
            BoolValue  => new NumberValue(4),
            TextValue or DirectTextLiteralValue => new NumberValue(2),
            NumberValue or DateTimeValue => new NumberValue(1),
            BlankValue => new NumberValue(1),
            _ => new NumberValue(1)
        };
    }

    private static ScalarValue ErrorTypeFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not ErrorValue ev) return ErrorValue.NA;
        return ev.Code switch
        {
            "#NULL!"  => new NumberValue(1),
            "#DIV/0!" => new NumberValue(2),
            "#VALUE!" => new NumberValue(3),
            "#REF!"   => new NumberValue(4),
            "#NAME?"  => new NumberValue(5),
            "#NUM!"   => new NumberValue(6),
            "#N/A"    => new NumberValue(7),
            "#GETTING_DATA" => new NumberValue(8),
            "#SPILL!" => new NumberValue(14),
            _ => ErrorValue.NA
        };
    }

    // ════════════════════════════════════════════════════════════════════════
    // Phase A1 – Database functions
    // DSUM, DAVERAGE, DCOUNT, DCOUNTA, DGET, DMAX, DMIN, DPRODUCT, DSTDEV, DSTDEVP, DVAR, DVARP
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Resolve field arg to 0-based column index in database (or null if not found).</summary>
    private static int? ResolveDatabaseField(RangeValue database, ScalarValue field)
    {
        if (TryCellNumber(field, out double colIdx))
        {
            int idx = (int)colIdx;
            if (idx < 1 || idx > database.ColCount) return null;
            return idx - 1;
        }
        if (field is TextValue or DirectTextLiteralValue)
        {
            var name = ToText(field);
            for (int c = 0; c < database.ColCount; c++)
            {
                var header = database.Cells[0, c];
                if (header is TextValue or DirectTextLiteralValue)
                {
                    if (string.Equals(ToText(header), name, StringComparison.OrdinalIgnoreCase))
                        return c;
                }
            }
        }
        return null;
    }

    /// <summary>Find database column index matching the given header text (case-insensitive).</summary>
    private static int FindDbHeaderCol(RangeValue database, string headerText)
    {
        for (int c = 0; c < database.ColCount; c++)
        {
            var h = database.Cells[0, c];
            string hText = h is TextValue or DirectTextLiteralValue ? ToText(h) : ToText(h);
            if (string.Equals(hText, headerText, StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return -1;
    }

    /// <summary>Returns true if a single data row matches a single criteria row (AND across columns).</summary>
    private static bool DbRowMatchesCriteriaRow(RangeValue database, int dataRow, RangeValue criteria, int critRow)
    {
        bool hasAnyCriterion = false;
        for (int cc = 0; cc < criteria.ColCount; cc++)
        {
            var critHeader = criteria.Cells[0, cc];
            if (critHeader is BlankValue) continue;

            var critCell = criteria.Cells[critRow, cc];
            if (critCell is BlankValue) continue;
            if (critCell is TextValue tv && tv.Value.Length == 0) continue;

            hasAnyCriterion = true;
            var headerText = ToText(critHeader);
            int dbCol = FindDbHeaderCol(database, headerText);
            if (dbCol < 0) return false;

            var cellValue = database.Cells[dataRow, dbCol];
            if (!MatchesCriteria(cellValue, critCell)) return false;
        }
        return hasAnyCriterion;
    }

    /// <summary>Extract values from the field column for all matching rows.</summary>
    private static (List<ScalarValue> Matches, ErrorValue? Error) DatabaseExtract(
        RangeValue database, ScalarValue fieldArg, RangeValue criteria)
    {
        if (database.RowCount < 2) return (new List<ScalarValue>(), null);

        int? fieldCol = ResolveDatabaseField(database, fieldArg);
        if (fieldCol is null) return (new List<ScalarValue>(), ErrorValue.Value);

        var matches = new List<ScalarValue>();
        for (int r = 1; r < database.RowCount; r++)
        {
            bool rowMatches = false;
            // OR across criteria rows
            for (int cr = 1; cr < criteria.RowCount; cr++)
            {
                if (DbRowMatchesCriteriaRow(database, r, criteria, cr))
                {
                    rowMatches = true;
                    break;
                }
            }
            if (rowMatches)
            {
                var cell = database.Cells[r, fieldCol.Value];
                if (cell is ErrorValue ev) return (matches, ev);
                matches.Add(cell);
            }
        }
        return (matches, null);
    }

    private static (List<double> Nums, ErrorValue? Error) DatabaseExtractNumeric(
        RangeValue database, ScalarValue fieldArg, RangeValue criteria)
    {
        var (matches, err) = DatabaseExtract(database, fieldArg, criteria);
        if (err is not null) return (new List<double>(), err);
        var nums = new List<double>();
        foreach (var v in matches)
            if (TryCellNumber(v, out double d)) nums.Add(d);
        return (nums, null);
    }

    private static bool TryDbArgs(
        IReadOnlyList<ScalarValue> args,
        out RangeValue database,
        out ScalarValue field,
        out RangeValue criteria,
        out ScalarValue? error)
    {
        database = null!;
        field = null!;
        criteria = null!;
        error = null;
        if (args[0] is ErrorValue e0) { error = e0; return false; }
        if (args[1] is ErrorValue e1) { error = e1; return false; }
        if (args[2] is ErrorValue e2) { error = e2; return false; }
        if (args[0] is not RangeValue db) { error = ErrorValue.Value; return false; }
        if (args[2] is not RangeValue cr) { error = ErrorValue.Value; return false; }
        database = db;
        field = args[1];
        criteria = cr;
        return true;
    }

    private static ScalarValue DSum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        return NumberResult(nums.Sum());
    }

    private static ScalarValue DAverage(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count == 0) return ErrorValue.DivByZero;
        return NumberResult(nums.Average());
    }

    private static ScalarValue DCount(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        return new NumberValue(nums.Count);
    }

    private static ScalarValue DCountA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (matches, e) = DatabaseExtract(db, f, cr);
        if (e is not null) return e;
        int count = 0;
        foreach (var v in matches)
            if (v is not BlankValue && !(v is TextValue tv && tv.Value.Length == 0)) count++;
        return new NumberValue(count);
    }

    private static ScalarValue DGet(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (matches, e) = DatabaseExtract(db, f, cr);
        if (e is not null) return e;
        if (matches.Count == 0) return ErrorValue.Value;
        if (matches.Count > 1) return ErrorValue.Num;
        return matches[0];
    }

    private static ScalarValue DMax(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count == 0) return ErrorValue.Num;
        return NumberResult(nums.Max());
    }

    private static ScalarValue DMin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count == 0) return ErrorValue.Num;
        return NumberResult(nums.Min());
    }

    private static ScalarValue DProduct(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count == 0) return new NumberValue(1);
        double prod = 1;
        foreach (var x in nums) prod *= x;
        return NumberResult(prod);
    }

    private static ScalarValue DStdev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count < 2) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s = nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1);
        return NumberResult(Math.Sqrt(s));
    }

    private static ScalarValue DStdevP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count == 0) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s = nums.Sum(x => (x - mean) * (x - mean)) / nums.Count;
        return NumberResult(Math.Sqrt(s));
    }

    private static ScalarValue DVar(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count < 2) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s = nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1);
        return NumberResult(s);
    }

    private static ScalarValue DVarP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count == 0) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s = nums.Sum(x => (x - mean) * (x - mean)) / nums.Count;
        return NumberResult(s);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Phase A2 – AST-aware stub
    // ════════════════════════════════════════════════════════════════════════
    // Defensive fallback if EvaluateAstAware routing is bypassed; the
    // FormulaEvaluator dispatches ISREF/ISFORMULA/FORMULATEXT/OFFSET to
    // AST-aware code paths before invoking this delegate.
    private static ScalarValue AstAwareStub(IReadOnlyList<ScalarValue> args, IEvalContext ctx) => ErrorValue.Value;

    // ════════════════════════════════════════════════════════════════════════
    // Phase A2 – CELL(info_type, [reference])
    // ════════════════════════════════════════════════════════════════════════

    private static ScalarValue CellInfo(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var infoType = ToText(args[0]).Trim().ToLowerInvariant();

        // Resolve reference: use args[1] when present; otherwise default to A1.
        // We don't have access to the original AST node here, so we read the
        // computed scalar/range (built by the evaluator's standard arg expansion).
        uint row = 1, col = 1;
        ScalarValue cellValue = BlankValue.Instance;
        var sheet = ctx.CurrentSheet;
        if (args.Count >= 2)
        {
            if (args[1] is ErrorValue e1) return e1;
            if (args[1] is RangeValue rv)
            {
                row = rv.StartRow;
                col = rv.StartCol;
                cellValue = rv.Cells[0, 0];
            }
            else if (args[1] is BlankValue)
            {
                cellValue = ctx.GetCellValue(row, col);
            }
            else
            {
                // A non-range value — CELL needs a reference; treat as A1 of current sheet
                // but use the computed scalar as the value for "contents"/"type".
                cellValue = args[1];
            }
        }
        else
        {
            cellValue = ctx.GetCellValue(row, col);
        }

        var underlying = sheet?.GetCell(row, col);

        switch (infoType)
        {
            case "address":
                return new TextValue($"${CellAddress.NumberToColumnName(col)}${row}");
            case "col":
                return new NumberValue(col);
            case "row":
                return new NumberValue(row);
            case "contents":
                return cellValue;
            case "type":
                return new TextValue(cellValue switch
                {
                    BlankValue => "b",
                    TextValue  => "l",
                    _          => "v"
                });
            case "protect":
            {
                if (sheet is null) return new NumberValue(0);
                bool locked = true; // default style is locked
                if (underlying is not null && ctx.CurrentWorkbook is not null)
                {
                    var style = ctx.CurrentWorkbook.GetStyle(underlying.StyleId);
                    locked = style.Locked;
                }
                return new NumberValue(sheet.IsProtected && locked ? 1 : 0);
            }
            case "width":
            {
                if (sheet is null) return new NumberValue(8);
                if (sheet.ColumnWidths.TryGetValue(col, out var w)) return new NumberValue(w);
                return new NumberValue(sheet.DefaultColumnWidth);
            }
            case "filename":
                // In-memory workbook has no on-disk path; Excel compat is empty string.
                return new TextValue("");
            case "format":
            {
                if (underlying is null || ctx.CurrentWorkbook is null) return new TextValue("");
                var style = ctx.CurrentWorkbook.GetStyle(underlying.StyleId);
                return new TextValue(style.NumberFormat == "General" ? "" : style.NumberFormat);
            }
            case "color":
                return new NumberValue(0);
            case "parentheses":
                return new NumberValue(0);
            case "prefix":
                return new TextValue("");
            default:
                return ErrorValue.Value;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Phase A2 – INFO(type_text)
    // ════════════════════════════════════════════════════════════════════════

    private static ScalarValue InfoFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var infoType = ToText(args[0]).Trim().ToLowerInvariant();
        switch (infoType)
        {
            case "directory":
                try { return new TextValue(Environment.CurrentDirectory); }
                catch { return new TextValue(""); }
            case "numfile":
                return new NumberValue(ctx.CurrentWorkbook?.SheetCount ?? 1);
            case "origin":
                return new TextValue("$A:$A1");
            case "osversion":
                return new TextValue("Windows (32-bit) NT 10.00");
            case "recalc":
                return new TextValue(ctx.CurrentWorkbook?.CalculationMode == WorkbookCalculationMode.Manual
                    ? "Manual" : "Automatic");
            case "release":
                return new TextValue("16.0");
            case "system":
                return new TextValue("pcdos");
            case "memavail":
            case "memused":
            case "totmem":
                return new NumberValue(0);
            default:
                return ErrorValue.Value;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Phase A2 – AGGREGATE(function_num, options, array/ref, [k])
    // ════════════════════════════════════════════════════════════════════════

    private static ScalarValue Aggregate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var funcNumD = ToNumber(args[0]);
        var optionsD = ToNumber(args[1]);
        if (!double.IsFinite(funcNumD) || !double.IsFinite(optionsD)) return ErrorValue.Value;
        int funcNum = (int)funcNumD;
        int options = (int)optionsD;
        if (funcNum < 1 || funcNum > 19) return ErrorValue.Value;
        if (options < 0 || options > 7) return ErrorValue.Value;

        bool ignoreErrors = options == 2 || options == 3 || options == 6 || options == 7;
        // Hidden-row ignore (options 1, 3, 5, 7) is not honored here — see header note.

        bool needsK = funcNum is >= 14 and <= 19;
        if (needsK && args.Count < 4) return ErrorValue.Value;

        var nums = new List<double>();
        // Collect from positional value args (skip funcNum, options, and a potential k arg)
        int kIndex = needsK ? args.Count - 1 : -1;
        for (int i = 2; i < args.Count; i++)
        {
            if (i == kIndex) continue;
            var arg = args[i];
            if (arg is ErrorValue err)
            {
                if (ignoreErrors) continue;
                return err;
            }
            if (arg is RangeValue rv)
            {
                foreach (var cell in rv.Flatten())
                {
                    if (cell is ErrorValue ce)
                    {
                        if (ignoreErrors) continue;
                        return ce;
                    }
                    if (TryCellNumber(cell, out double v)) nums.Add(v);
                }
            }
            else if (TryCellNumber(arg, out double v)) nums.Add(v);
            else if (arg is DirectTextLiteralValue d && TryDirectTextNumber(d, out double dv)) nums.Add(dv);
        }

        double? k = null;
        if (needsK)
        {
            if (args[kIndex] is ErrorValue ek) return ek;
            var kc = ToNumber(args[kIndex]);
            if (!double.IsFinite(kc)) return ErrorValue.Num;
            k = kc;
        }

        switch (funcNum)
        {
            case 1:  return nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(nums.Average());
            case 2:  return new NumberValue(nums.Count);
            case 3:
            {
                int countA = 0;
                for (int i = 2; i < args.Count; i++)
                {
                    if (i == kIndex) continue;
                    var arg = args[i];
                    if (arg is ErrorValue err)
                    {
                        if (ignoreErrors) continue;
                        return err;
                    }
                    if (arg is RangeValue rv)
                    {
                        foreach (var cell in rv.Flatten())
                        {
                            if (cell is ErrorValue ce)
                            {
                                if (ignoreErrors) continue;
                                return ce;
                            }
                            if (cell is not BlankValue) countA++;
                        }
                    }
                    else if (arg is not BlankValue) countA++;
                }
                return new NumberValue(countA);
            }
            case 4:  return nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(nums.Max());
            case 5:  return nums.Count == 0 ? ErrorValue.DivByZero : NumberResult(nums.Min());
            case 6:  return NumberResult(nums.Count == 0 ? 0 : nums.Aggregate(1.0, (a, x) => a * x));
            case 7:
            {
                if (nums.Count < 2) return ErrorValue.DivByZero;
                double mean = nums.Average();
                return NumberResult(Math.Sqrt(nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1)));
            }
            case 8:
            {
                if (nums.Count == 0) return ErrorValue.DivByZero;
                double mean = nums.Average();
                return NumberResult(Math.Sqrt(nums.Sum(x => (x - mean) * (x - mean)) / nums.Count));
            }
            case 9:  return NumberResult(nums.Sum());
            case 10:
            {
                if (nums.Count < 2) return ErrorValue.DivByZero;
                double mean = nums.Average();
                return NumberResult(nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1));
            }
            case 11:
            {
                if (nums.Count == 0) return ErrorValue.DivByZero;
                double mean = nums.Average();
                return NumberResult(nums.Sum(x => (x - mean) * (x - mean)) / nums.Count);
            }
            case 12:
            {
                if (nums.Count == 0) return ErrorValue.Num;
                var s = nums.OrderBy(x => x).ToList();
                int n = s.Count;
                return NumberResult(n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2.0);
            }
            case 13: // MODE.SNGL
            {
                if (nums.Count == 0) return ErrorValue.NA;
                var counts = nums.GroupBy(x => x).Select(g => (g.Key, Count: g.Count())).ToList();
                int maxCount = counts.Max(c => c.Count);
                if (maxCount < 2) return ErrorValue.NA;
                return NumberResult(counts.First(c => c.Count == maxCount).Key);
            }
            case 14: // LARGE
            {
                if (nums.Count == 0) return ErrorValue.Num;
                int ki = (int)Math.Truncate(k!.Value);
                if (ki < 1 || ki > nums.Count) return ErrorValue.Num;
                var s = nums.OrderByDescending(x => x).ToList();
                return NumberResult(s[ki - 1]);
            }
            case 15: // SMALL
            {
                if (nums.Count == 0) return ErrorValue.Num;
                int ki = (int)Math.Truncate(k!.Value);
                if (ki < 1 || ki > nums.Count) return ErrorValue.Num;
                var s = nums.OrderBy(x => x).ToList();
                return NumberResult(s[ki - 1]);
            }
            case 16: // PERCENTILE.INC
            {
                if (nums.Count == 0) return ErrorValue.Num;
                if (k!.Value < 0 || k.Value > 1) return ErrorValue.Num;
                return NumberResult(PercentileIncCalc(nums, k.Value));
            }
            case 17: // QUARTILE.INC
            {
                if (nums.Count == 0) return ErrorValue.Num;
                int q = (int)Math.Truncate(k!.Value);
                if (q < 0 || q > 4) return ErrorValue.Num;
                return NumberResult(PercentileIncCalc(nums, q / 4.0));
            }
            case 18: // PERCENTILE.EXC
            {
                if (nums.Count == 0) return ErrorValue.Num;
                if (k!.Value <= 0 || k.Value >= 1) return ErrorValue.Num;
                return NumberResult(PercentileExcCalc(nums, k.Value));
            }
            case 19: // QUARTILE.EXC
            {
                if (nums.Count == 0) return ErrorValue.Num;
                int q = (int)Math.Truncate(k!.Value);
                if (q < 1 || q > 3) return ErrorValue.Num;
                return NumberResult(PercentileExcCalc(nums, q / 4.0));
            }
            default:
                return ErrorValue.Value;
        }
    }

    private static ScalarValue GetPivotData(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 2 || args.Count % 2 != 0)
            return ErrorValue.Value;
        if (ctx.CurrentSheet is null || ctx.CurrentWorkbook is null)
            return ErrorValue.Ref;
        if (args[0] is ErrorValue dataFieldError)
            return dataFieldError;
        if (args[1] is ErrorValue pivotRefError)
            return pivotRefError;
        if (args[1] is not RangeValue { RowCount: 1, ColCount: 1 } pivotReference)
            return ErrorValue.Ref;

        var dataFieldCaption = PivotText(args[0]);
        if (string.IsNullOrWhiteSpace(dataFieldCaption))
            return ErrorValue.Value;

        var filters = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        for (var index = 2; index < args.Count; index += 2)
        {
            if (args[index] is ErrorValue fieldError)
                return fieldError;
            if (args[index + 1] is ErrorValue itemError)
                return itemError;
            var fieldName = PivotText(args[index]);
            var itemName = PivotText(args[index + 1]);
            if (string.IsNullOrWhiteSpace(fieldName))
                return ErrorValue.Value;
            if (filters.TryGetValue(fieldName, out var existingItem) &&
                !string.Equals(existingItem, itemName, StringComparison.CurrentCultureIgnoreCase))
            {
                return ErrorValue.Ref;
            }
            filters[fieldName] = itemName;
        }

        var locatedPivot = FindPivotTableForReference(ctx, pivotReference);
        if (locatedPivot is null)
            return ErrorValue.Ref;
        var (pivotSheet, pivotTable) = locatedPivot.Value;

        var headers = ReadPivotSourceHeaders(ctx.CurrentWorkbook, pivotTable);
        var dataFieldIndex = pivotTable.DataFields.FindIndex(field =>
            string.Equals(field.Name, dataFieldCaption, StringComparison.CurrentCultureIgnoreCase));
        if (dataFieldIndex < 0)
            return ErrorValue.Ref;
        if (!GetPivotDataFilterFieldsAreVisible(pivotTable, headers, filters))
            return ErrorValue.Ref;
        if (!PageFieldFiltersMatch(pivotTable, headers, filters))
            return ErrorValue.Ref;

        var materialized = GetPivotMaterializedRange(pivotSheet, pivotTable);
        var headerRows = (uint)Math.Max(1, pivotTable.ColumnFields.Count);
        var firstDataRow = pivotTable.TargetRange.Start.Row + headerRows;
        var outputRow = ResolveGetPivotDataRow(pivotSheet, pivotTable, headers, filters, firstDataRow, materialized.End.Row);
        if (outputRow is null)
            return ErrorValue.Ref;

        var outputColumn = ResolveGetPivotDataColumn(pivotSheet, pivotTable, headers, filters, dataFieldIndex, materialized.End.Col);
        if (outputColumn is null)
            return ErrorValue.Ref;

        return pivotSheet.GetCell(outputRow.Value, outputColumn.Value)?.Value ?? ErrorValue.Ref;
    }

    private static bool GetPivotDataFilterFieldsAreVisible(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters)
    {
        var visibleFields = pivotTable.RowFields
            .Concat(pivotTable.ColumnFields)
            .Concat(pivotTable.PageFields)
            .Select(field => PivotHeader(headers, field.SourceFieldIndex))
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        return filters.Keys.All(visibleFields.Contains);
    }

    private static bool PageFieldFiltersMatch(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters)
    {
        foreach (var pageField in pivotTable.PageFields)
        {
            var header = PivotHeader(headers, pageField.SourceFieldIndex);
            if (!filters.TryGetValue(header, out var expected))
                continue;

            if (!string.IsNullOrWhiteSpace(pageField.SelectedItem))
                return string.Equals(pageField.SelectedItem, expected, StringComparison.CurrentCultureIgnoreCase);

            if (pageField.SelectedItems is { Count: > 0 } selectedItems)
                return selectedItems.Contains(expected, StringComparer.CurrentCultureIgnoreCase);
        }

        return true;
    }

    private static (Sheet Sheet, PivotTableModel PivotTable)? FindPivotTableForReference(
        IEvalContext ctx,
        RangeValue reference)
    {
        var row = reference.StartRow;
        var col = reference.StartCol;
        if (!string.IsNullOrWhiteSpace(reference.SheetName))
        {
            var sheet = ctx.CurrentWorkbook?.GetSheet(reference.SheetName);
            var address = sheet is null ? (CellAddress?)null : new CellAddress(sheet.Id, row, col);
            var pivot = address is null
                ? null
                : sheet!.PivotTables.FirstOrDefault(item => item.TargetRange.Contains(address.Value));
            return pivot is null ? null : (sheet!, pivot);
        }

        if (ctx.CurrentSheet is not null)
        {
            var currentAddress = new CellAddress(ctx.CurrentSheet.Id, row, col);
            var currentPivot = ctx.CurrentSheet.PivotTables.FirstOrDefault(pivot => pivot.TargetRange.Contains(currentAddress));
            if (currentPivot is not null)
                return (ctx.CurrentSheet, currentPivot);
        }

        if (ctx.CurrentWorkbook is null)
            return null;

        foreach (var sheet in ctx.CurrentWorkbook.Sheets)
        {
            var address = new CellAddress(sheet.Id, row, col);
            var pivot = sheet.PivotTables.FirstOrDefault(item => item.TargetRange.Contains(address));
            if (pivot is not null)
                return (sheet, pivot);
        }

        return null;
    }

    private static uint? ResolveGetPivotDataRow(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters,
        uint firstDataRow,
        uint lastRow)
    {
        var rowFields = pivotTable.RowFields.ToList();
        if (rowFields.Count == 0)
            return firstDataRow <= lastRow ? firstDataRow : null;

        var requestedRowFieldCount = rowFields.Count(field => filters.ContainsKey(PivotHeader(headers, field.SourceFieldIndex)));
        if (requestedRowFieldCount == 0)
        {
            for (var row = firstDataRow; row <= lastRow; row++)
            {
                if (IsPivotGrandTotalText(sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value))
                    return row;
            }
        }

        if (requestedRowFieldCount > 0 && requestedRowFieldCount < rowFields.Count)
        {
            for (var row = firstDataRow; row <= lastRow; row++)
            {
                if (TryReadPivotSubtotalCaption(sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value, out var subtotalItem) &&
                    PivotSubtotalMatches(sheet, pivotTable, headers, filters, firstDataRow, row, subtotalItem, requestedRowFieldCount))
                {
                    return row;
                }
            }
        }

        for (var row = firstDataRow; row <= lastRow; row++)
        {
            if (IsPivotGrandTotalText(sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value))
            {
                if (!rowFields.Any(field => filters.ContainsKey(PivotHeader(headers, field.SourceFieldIndex))))
                    return row;
                continue;
            }

            if (TryCompactPivotRowMatch(sheet, pivotTable, headers, filters, row, rowFields, requestedRowFieldCount))
                return row;

            var matches = true;
            for (var index = 0; index < rowFields.Count; index++)
            {
                var header = PivotHeader(headers, rowFields[index].SourceFieldIndex);
                if (!filters.TryGetValue(header, out var expected))
                    continue;

                var actual = ReadPivotRowItem(sheet, pivotTable, row, firstDataRow, index, rowFields.Count);
                if (!string.Equals(actual, expected, StringComparison.CurrentCultureIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return row;
        }

        return null;
    }

    private static bool TryCompactPivotRowMatch(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters,
        uint row,
        IReadOnlyList<PivotFieldModel> rowFields,
        int requestedRowFieldCount)
    {
        if (pivotTable.ReportLayout != PivotReportLayout.Compact || rowFields.Count <= 1)
            return false;
        if (requestedRowFieldCount != rowFields.Count)
            return false;

        var expectedParts = new List<string>(rowFields.Count);
        foreach (var field in rowFields)
        {
            var header = PivotHeader(headers, field.SourceFieldIndex);
            if (!filters.TryGetValue(header, out var expected))
                return false;
            expectedParts.Add(expected);
        }

        var actual = PivotText(sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value);
        var expectedCaption = string.Join(" ", expectedParts);
        return string.Equals(actual, expectedCaption, StringComparison.CurrentCultureIgnoreCase);
    }

    private static uint? ResolveGetPivotDataColumn(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters,
        int dataFieldIndex,
        uint lastColumn)
    {
        var rowFieldColumns = PivotRowFieldOutputColumnCount(pivotTable);
        var firstValueColumn = pivotTable.TargetRange.Start.Col + (uint)rowFieldColumns;
        if (pivotTable.ColumnFields.Count == 0)
            return firstValueColumn + (uint)dataFieldIndex <= lastColumn ? firstValueColumn + (uint)dataFieldIndex : null;

        if (!pivotTable.ColumnFields.Any(field => filters.ContainsKey(PivotHeader(headers, field.SourceFieldIndex))))
        {
            for (var col = firstValueColumn; col <= lastColumn; col++)
            {
                var columnDataFieldIndex = (int)((col - firstValueColumn) % (uint)Math.Max(1, pivotTable.DataFields.Count));
                if (columnDataFieldIndex != dataFieldIndex)
                    continue;
                for (var level = 0; level < pivotTable.ColumnFields.Count; level++)
                {
                    if (IsPivotGrandTotalText(sheet.GetCell(pivotTable.TargetRange.Start.Row + (uint)level, col)?.Value))
                        return col;
                }
            }
        }

        for (var col = firstValueColumn; col <= lastColumn; col++)
        {
            var columnDataFieldIndex = (int)((col - firstValueColumn) % (uint)Math.Max(1, pivotTable.DataFields.Count));
            if (columnDataFieldIndex != dataFieldIndex)
                continue;

            var matches = true;
            for (var level = 0; level < pivotTable.ColumnFields.Count; level++)
            {
                var field = pivotTable.ColumnFields[level];
                var header = PivotHeader(headers, field.SourceFieldIndex);
                if (!filters.TryGetValue(header, out var expected))
                    continue;

                var caption = PivotText(sheet.GetCell(pivotTable.TargetRange.Start.Row + (uint)level, col)?.Value);
                if (pivotTable.DataFields.Count > 1 && level == pivotTable.ColumnFields.Count - 1)
                {
                    var dataFieldName = pivotTable.DataFields[dataFieldIndex].Name;
                    if (caption.EndsWith(dataFieldName, StringComparison.CurrentCultureIgnoreCase))
                        caption = caption[..^dataFieldName.Length].TrimEnd();
                }

                if (!string.Equals(caption, expected, StringComparison.CurrentCultureIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return col;
        }

        return null;
    }

    private static bool PivotSubtotalMatches(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters,
        uint firstDataRow,
        uint subtotalRow,
        string subtotalItem,
        int requestedRowFieldCount)
    {
        for (var index = 0; index < pivotTable.RowFields.Count; index++)
        {
            var header = PivotHeader(headers, pivotTable.RowFields[index].SourceFieldIndex);
            if (!filters.TryGetValue(header, out var expected))
                continue;

            string? actual = null;
            if (index == 0)
                actual = subtotalItem;
            else if (index < requestedRowFieldCount)
                actual = ReadPivotRowItem(sheet, pivotTable, subtotalRow - 1, firstDataRow, index, pivotTable.RowFields.Count);

            if (!string.Equals(actual, expected, StringComparison.CurrentCultureIgnoreCase))
                return false;
        }

        return true;
    }

    private static string? ReadPivotRowItem(
        Sheet sheet,
        PivotTableModel pivotTable,
        uint row,
        uint firstDataRow,
        int fieldIndex,
        int rowFieldCount)
    {
        if (pivotTable.ReportLayout == PivotReportLayout.Compact && rowFieldCount > 1)
            return PivotText(sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value);

        var col = pivotTable.TargetRange.Start.Col + (uint)fieldIndex;
        for (var current = row; current >= firstDataRow; current--)
        {
            var value = sheet.GetCell(current, col)?.Value;
            if (value is not null)
                return PivotText(value);
            if (current == firstDataRow)
                break;
        }

        return null;
    }

    private static IReadOnlyList<string> ReadPivotSourceHeaders(Workbook workbook, PivotTableModel pivotTable)
    {
        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is null)
            return [];
        var headers = new List<string>();
        for (var col = pivotTable.SourceRange.Start.Col; col <= pivotTable.SourceRange.End.Col; col++)
            headers.Add(PivotText(sourceSheet.GetCell(pivotTable.SourceRange.Start.Row, col)?.Value));
        return headers;
    }

    private static string PivotHeader(IReadOnlyList<string> headers, int index) =>
        index >= 0 && index < headers.Count ? headers[index] : "";

    private static int PivotRowFieldOutputColumnCount(PivotTableModel pivotTable) =>
        pivotTable.ReportLayout == PivotReportLayout.Compact && pivotTable.RowFields.Count > 1
            ? 1
            : pivotTable.RowFields.Count;

    private static GridRange GetPivotMaterializedRange(Sheet sheet, PivotTableModel pivotTable)
    {
        uint? minRow = null;
        uint? minCol = null;
        uint? maxRow = null;
        uint? maxCol = null;
        for (var row = pivotTable.TargetRange.Start.Row; row <= pivotTable.TargetRange.End.Row; row++)
        for (var col = pivotTable.TargetRange.Start.Col; col <= pivotTable.TargetRange.End.Col; col++)
        {
            if (sheet.GetCell(row, col) is null)
                continue;
            minRow = minRow is null ? row : Math.Min(minRow.Value, row);
            minCol = minCol is null ? col : Math.Min(minCol.Value, col);
            maxRow = maxRow is null ? row : Math.Max(maxRow.Value, row);
            maxCol = maxCol is null ? col : Math.Max(maxCol.Value, col);
        }

        if (minRow is null || minCol is null || maxRow is null || maxCol is null)
            return new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start);
        return new GridRange(
            new CellAddress(sheet.Id, minRow.Value, minCol.Value),
            new CellAddress(sheet.Id, maxRow.Value, maxCol.Value));
    }

    private static bool IsPivotGrandTotalText(ScalarValue? value) =>
        value is TextValue text && text.Value.StartsWith("Grand Total", StringComparison.CurrentCultureIgnoreCase);

    private static bool TryReadPivotSubtotalCaption(ScalarValue? value, out string item)
    {
        item = "";
        if (value is not TextValue text ||
            !text.Value.EndsWith(" Total", StringComparison.CurrentCultureIgnoreCase) ||
            text.Value.StartsWith("Grand Total", StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        item = text.Value[..^" Total".Length];
        return item.Length > 0;
    }

    private static string PivotText(ScalarValue? value) => value switch
    {
        null or BlankValue => "",
        TextValue text => text.Value,
        DirectTextLiteralValue text => text.Value,
        NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        DateTimeValue date => date.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        ReferencedScalarValue referenced => PivotText(referenced.Value),
        _ => value.ToString() ?? ""
    };

    private static double PercentileIncCalc(List<double> nums, double p)
    {
        var s = nums.OrderBy(x => x).ToList();
        int n = s.Count;
        if (n == 1) return s[0];
        double pos = p * (n - 1);
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        if (lo == hi) return s[lo];
        return s[lo] + (pos - lo) * (s[hi] - s[lo]);
    }

    private static double PercentileExcCalc(List<double> nums, double p)
    {
        var s = nums.OrderBy(x => x).ToList();
        int n = s.Count;
        double pos = p * (n + 1) - 1;
        if (pos < 0 || pos > n - 1) throw new FormulaEvalException("#NUM!", "k out of range");
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        if (lo == hi) return s[lo];
        return s[lo] + (pos - lo) * (s[hi] - s[lo]);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Phase A2 – CONVERT(number, from_unit, to_unit)
    // ════════════════════════════════════════════════════════════════════════

    private enum UnitCategory { Weight, Distance, Time, Pressure, Force, Energy, Power, Area, Volume, Speed, Information, Temperature }

    private static readonly Dictionary<string, (UnitCategory Cat, double Factor)> ConvertUnits = BuildConvertUnits();

    private static Dictionary<string, (UnitCategory Cat, double Factor)> BuildConvertUnits()
    {
        var d = new Dictionary<string, (UnitCategory, double)>(StringComparer.Ordinal);
        void Add(UnitCategory cat, string unit, double factor) => d[unit] = (cat, factor);

        // Weight (base = gram)
        Add(UnitCategory.Weight, "g", 1);
        Add(UnitCategory.Weight, "kg", 1000);
        Add(UnitCategory.Weight, "lbm", 453.59237);
        Add(UnitCategory.Weight, "ozm", 28.349523);
        Add(UnitCategory.Weight, "stone", 6350.293);
        Add(UnitCategory.Weight, "ton", 907184.74);
        Add(UnitCategory.Weight, "uk_ton", 1016046.91);
        Add(UnitCategory.Weight, "mg", 0.001);
        Add(UnitCategory.Weight, "ug", 0.000001);
        Add(UnitCategory.Weight, "ng", 1e-9);
        Add(UnitCategory.Weight, "sg", 14593.903);
        Add(UnitCategory.Weight, "cwt", 45359.237);
        Add(UnitCategory.Weight, "uk_cwt", 50802.345);

        // Distance (base = meter)
        Add(UnitCategory.Distance, "m", 1);
        Add(UnitCategory.Distance, "km", 1000);
        Add(UnitCategory.Distance, "mi", 1609.344);
        Add(UnitCategory.Distance, "Nmi", 1852);
        Add(UnitCategory.Distance, "in", 0.0254);
        Add(UnitCategory.Distance, "ft", 0.3048);
        Add(UnitCategory.Distance, "yd", 0.9144);
        Add(UnitCategory.Distance, "ang", 1e-10);
        Add(UnitCategory.Distance, "Pica", 0.000423333);
        Add(UnitCategory.Distance, "cm", 0.01);
        Add(UnitCategory.Distance, "mm", 0.001);
        Add(UnitCategory.Distance, "um", 1e-6);
        Add(UnitCategory.Distance, "nm", 1e-9);
        Add(UnitCategory.Distance, "ly", 9.4607304725808e15);
        Add(UnitCategory.Distance, "au", 149597870700.0);
        Add(UnitCategory.Distance, "pc", 3.085677581491367e16);

        // Time (base = second)
        Add(UnitCategory.Time, "sec", 1);
        Add(UnitCategory.Time, "s", 1);
        Add(UnitCategory.Time, "min", 60);
        Add(UnitCategory.Time, "mn", 2629800);
        Add(UnitCategory.Time, "hr", 3600);
        Add(UnitCategory.Time, "day", 86400);
        Add(UnitCategory.Time, "yr", 31557600);

        // Pressure (base = Pa)
        Add(UnitCategory.Pressure, "Pa", 1);
        Add(UnitCategory.Pressure, "atm", 101325);
        Add(UnitCategory.Pressure, "mmHg", 133.322);
        Add(UnitCategory.Pressure, "psi", 6894.757);
        Add(UnitCategory.Pressure, "Torr", 133.322);

        // Force (base = N)
        Add(UnitCategory.Force, "N", 1);
        Add(UnitCategory.Force, "dyn", 1e-5);
        Add(UnitCategory.Force, "lbf", 4.44822);
        Add(UnitCategory.Force, "pond", 0.00980665);

        // Energy (base = J)
        Add(UnitCategory.Energy, "J", 1);
        Add(UnitCategory.Energy, "kJ", 1000);
        Add(UnitCategory.Energy, "e", 1e-7);
        Add(UnitCategory.Energy, "c", 4.184);
        Add(UnitCategory.Energy, "cal", 4.184);
        Add(UnitCategory.Energy, "eV", 1.60218e-19);
        Add(UnitCategory.Energy, "HPh", 2684519.54);
        Add(UnitCategory.Energy, "Wh", 3600);
        Add(UnitCategory.Energy, "flb", 1.35582);
        Add(UnitCategory.Energy, "BTU", 1055.056);

        // Power (base = W)
        Add(UnitCategory.Power, "W", 1);
        Add(UnitCategory.Power, "kW", 1000);
        Add(UnitCategory.Power, "HP", 745.69987);
        Add(UnitCategory.Power, "PS", 735.49875);

        // Temperature (special — base = K, with offsets handled separately)
        Add(UnitCategory.Temperature, "C", double.NaN);
        Add(UnitCategory.Temperature, "F", double.NaN);
        Add(UnitCategory.Temperature, "K", double.NaN);
        Add(UnitCategory.Temperature, "Rank", double.NaN);
        Add(UnitCategory.Temperature, "Reau", double.NaN);

        // Area (base = m^2)
        Add(UnitCategory.Area, "m2", 1);
        Add(UnitCategory.Area, "m^2", 1);
        Add(UnitCategory.Area, "km2", 1e6);
        Add(UnitCategory.Area, "km^2", 1e6);
        Add(UnitCategory.Area, "mi2", 2589988.11);
        Add(UnitCategory.Area, "mi^2", 2589988.11);
        Add(UnitCategory.Area, "ft2", 0.092903);
        Add(UnitCategory.Area, "ft^2", 0.092903);
        Add(UnitCategory.Area, "in2", 0.000645);
        Add(UnitCategory.Area, "in^2", 0.000645);
        Add(UnitCategory.Area, "yd2", 0.836127);
        Add(UnitCategory.Area, "yd^2", 0.836127);
        Add(UnitCategory.Area, "ha", 10000);
        Add(UnitCategory.Area, "acre", 4046.856);

        // Volume (base = liter)
        Add(UnitCategory.Volume, "l", 1);
        Add(UnitCategory.Volume, "L", 1);
        Add(UnitCategory.Volume, "tsp", 0.00492892);
        Add(UnitCategory.Volume, "tbs", 0.0147868);
        Add(UnitCategory.Volume, "oz", 0.0295735);
        Add(UnitCategory.Volume, "cup", 0.236588);
        Add(UnitCategory.Volume, "pt", 0.473176);
        Add(UnitCategory.Volume, "qt", 0.946353);
        Add(UnitCategory.Volume, "gal", 3.785412);
        Add(UnitCategory.Volume, "m3", 1000);
        Add(UnitCategory.Volume, "m^3", 1000);
        Add(UnitCategory.Volume, "mi3", 4168181825441);
        Add(UnitCategory.Volume, "mi^3", 4168181825441);
        Add(UnitCategory.Volume, "ft3", 28.3168);
        Add(UnitCategory.Volume, "ft^3", 28.3168);
        Add(UnitCategory.Volume, "in3", 0.0163871);
        Add(UnitCategory.Volume, "in^3", 0.0163871);
        Add(UnitCategory.Volume, "yd3", 764.555);
        Add(UnitCategory.Volume, "yd^3", 764.555);
        Add(UnitCategory.Volume, "ml", 0.001);
        Add(UnitCategory.Volume, "cl", 0.01);
        Add(UnitCategory.Volume, "dl", 0.1);
        Add(UnitCategory.Volume, "Nmi3", 6352182208);
        Add(UnitCategory.Volume, "Nmi^3", 6352182208);

        // Speed (base = m/s)
        Add(UnitCategory.Speed, "m/s", 1);
        Add(UnitCategory.Speed, "m/h", 1.0 / 3600);
        Add(UnitCategory.Speed, "mph", 0.44704);
        Add(UnitCategory.Speed, "kn", 0.514444);

        // Information (base = bit)
        Add(UnitCategory.Information, "bit", 1);
        Add(UnitCategory.Information, "byte", 8);
        Add(UnitCategory.Information, "kbit", 1000);
        Add(UnitCategory.Information, "kbyte", 8000);
        Add(UnitCategory.Information, "Mbit", 1e6);
        Add(UnitCategory.Information, "Mbyte", 8e6);
        Add(UnitCategory.Information, "Gbit", 1e9);
        Add(UnitCategory.Information, "Gbyte", 8e9);
        Add(UnitCategory.Information, "Tbit", 1e12);
        Add(UnitCategory.Information, "Tbyte", 8e12);

        return d;
    }

    private static readonly Dictionary<string, double> ConvertPrefixes = new(StringComparer.Ordinal)
    {
        ["Y"] = 1e24, ["Z"] = 1e21, ["E"] = 1e18, ["P"] = 1e15, ["T"] = 1e12,
        ["G"] = 1e9, ["M"] = 1e6, ["k"] = 1e3, ["h"] = 1e2, ["e"] = 1e1,
        ["d"] = 1e-1, ["c"] = 1e-2, ["m"] = 1e-3, ["u"] = 1e-6, ["n"] = 1e-9,
        ["p"] = 1e-12, ["f"] = 1e-15, ["a"] = 1e-18, ["z"] = 1e-21, ["y"] = 1e-24
    };

    private static bool TryResolveUnit(string unit, out UnitCategory cat, out double factor)
    {
        if (ConvertUnits.TryGetValue(unit, out var entry))
        {
            cat = entry.Cat;
            factor = entry.Factor;
            return true;
        }
        // Try a SI prefix only when at least 2 chars remain — we don't want
        // single-letter prefixes (e.g. "m") to be re-interpreted when they
        // already exist as base units in the table above.
        if (unit.Length >= 2)
        {
            string p = unit[..1];
            string rest = unit[1..];
            if (ConvertPrefixes.TryGetValue(p, out double pFactor)
                && ConvertUnits.TryGetValue(rest, out var rEntry)
                && rEntry.Cat != UnitCategory.Temperature)
            {
                cat = rEntry.Cat;
                factor = rEntry.Factor * pFactor;
                return true;
            }
        }
        cat = default; factor = 0; return false;
    }

    private static ScalarValue Convert(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;

        double n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Value;
        string from = ToText(args[1]);
        string to = ToText(args[2]);

        if (!TryResolveUnit(from, out var fromCat, out var fromFactor)) return ErrorValue.NA;
        if (!TryResolveUnit(to, out var toCat, out var toFactor)) return ErrorValue.NA;
        if (fromCat != toCat) return ErrorValue.NA;

        if (fromCat == UnitCategory.Temperature)
        {
            // Convert input to Kelvin, then to target.
            double k = from switch
            {
                "C"    => n + 273.15,
                "F"    => (n - 32) * 5.0 / 9.0 + 273.15,
                "K"    => n,
                "Rank" => n * 5.0 / 9.0,
                "Reau" => n * 5.0 / 4.0 + 273.15,
                _      => double.NaN
            };
            if (!double.IsFinite(k)) return ErrorValue.NA;
            double r = to switch
            {
                "C"    => k - 273.15,
                "F"    => (k - 273.15) * 9.0 / 5.0 + 32,
                "K"    => k,
                "Rank" => k * 9.0 / 5.0,
                "Reau" => (k - 273.15) * 4.0 / 5.0,
                _      => double.NaN
            };
            return double.IsFinite(r) ? NumberResult(r) : ErrorValue.NA;
        }

        return NumberResult(n * fromFactor / toFactor);
    }

    private static ScalarValue Bin2Dec(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToDecimal(args[0], 2, 10, 512L, 1024L);

    private static ScalarValue Bin2Hex(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToBase(args, 2, 10, 512L, 1024L, 16, upper: true);

    private static ScalarValue Bin2Oct(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToBase(args, 2, 10, 512L, 1024L, 8, upper: false);

    private static ScalarValue Dec2Bin(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        DecimalToBase(args, 2, -512L, 511L, 1024L, 10, upper: false);

    private static ScalarValue Dec2Hex(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        DecimalToBase(args, 16, -549755813888L, 549755813887L, 1099511627776L, 10, upper: true);

    private static ScalarValue Dec2Oct(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        DecimalToBase(args, 8, -536870912L, 536870911L, 1073741824L, 10, upper: false);

    private static ScalarValue Hex2Bin(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToBase(args, 16, 10, 549755813888L, 1099511627776L, 2, upper: false);

    private static ScalarValue Hex2Dec(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToDecimal(args[0], 16, 10, 549755813888L, 1099511627776L);

    private static ScalarValue Hex2Oct(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToBase(args, 16, 10, 549755813888L, 1099511627776L, 8, upper: false);

    private static ScalarValue Oct2Bin(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToBase(args, 8, 10, 536870912L, 1073741824L, 2, upper: false);

    private static ScalarValue Oct2Dec(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToDecimal(args[0], 8, 10, 536870912L, 1073741824L);

    private static ScalarValue Oct2Hex(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToBase(args, 8, 10, 536870912L, 1073741824L, 16, upper: true);

    private static ScalarValue BaseToDecimal(ScalarValue arg, int fromBase, int maxDigits, long signThreshold, long modulus)
    {
        if (arg is ErrorValue error) return error;
        return TryParseBaseNumber(arg, fromBase, maxDigits, signThreshold, modulus, out var value)
            ? new NumberValue(value)
            : ErrorValue.Num;
    }

    private static ScalarValue BaseToBase(IReadOnlyList<ScalarValue> args, int fromBase, int maxDigits, long signThreshold, long modulus, int toBase, bool upper)
    {
        if (args[0] is ErrorValue error) return error;
        if (!TryParseBaseNumber(args[0], fromBase, maxDigits, signThreshold, modulus, out var value)) return ErrorValue.Num;
        if (value < 0) return DecimalToBaseText(value, toBase, NegativeModulusForBase(toBase), 10, upper);
        return FormatBaseText(value, toBase, args.Count > 1 ? args[1] : null, upper);
    }

    private static ScalarValue DecimalToBase(IReadOnlyList<ScalarValue> args, int toBase, long min, long max, long modulus, int negativeWidth, bool upper)
    {
        if (args[0] is ErrorValue error) return error;
        if (!TryGetEngineeringInteger(args[0], out var value)) return ErrorValue.Num;
        if (value < min || value > max) return ErrorValue.Num;
        if (value < 0) return DecimalToBaseText(value, toBase, modulus, negativeWidth, upper);
        return FormatBaseText(value, toBase, args.Count > 1 ? args[1] : null, upper);
    }

    private static ScalarValue DecimalToBaseText(long value, int toBase, long modulus, int width, bool upper)
    {
        string converted = System.Convert.ToString(value < 0 ? modulus + value : value, toBase);
        if (upper) converted = converted.ToUpperInvariant();
        return new TextValue(converted.PadLeft(width, '0'));
    }

    private static ScalarValue FormatBaseText(long value, int toBase, ScalarValue? placesArg, bool upper)
    {
        string converted = System.Convert.ToString(value, toBase);
        if (upper) converted = converted.ToUpperInvariant();
        if (placesArg is null or BlankValue) return new TextValue(converted);
        if (!TryGetEngineeringInteger(placesArg, out var places) || places < 0 || places > int.MaxValue) return ErrorValue.Num;
        if (places < converted.Length) return ErrorValue.Num;
        return new TextValue(converted.PadLeft((int)places, '0'));
    }

    private static bool TryParseBaseNumber(ScalarValue arg, int fromBase, int maxDigits, long signThreshold, long modulus, out long value)
    {
        value = 0;
        string text = ToText(arg).Trim();
        if (text.Length == 0 || text.Length > maxDigits) return false;

        foreach (char ch in text)
        {
            int digit = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'A' and <= 'F' => ch - 'A' + 10,
                >= 'a' and <= 'f' => ch - 'a' + 10,
                _ => -1
            };
            if (digit < 0 || digit >= fromBase) return false;
            value = value * fromBase + digit;
        }

        if (text.Length == maxDigits && value >= signThreshold) value -= modulus;
        return true;
    }

    private static long NegativeModulusForBase(int toBase) => toBase switch
    {
        2 => 1024L,
        8 => 1073741824L,
        16 => 1099511627776L,
        _ => throw new ArgumentOutOfRangeException(nameof(toBase), toBase, null)
    };

    private static bool TryGetEngineeringInteger(ScalarValue arg, out long value)
    {
        value = 0;
        if (arg is ErrorValue) return false;
        double number = ToNumber(arg);
        if (!double.IsFinite(number) || Math.Truncate(number) != number) return false;
        if (number < long.MinValue || number > long.MaxValue) return false;
        value = (long)number;
        return true;
    }

    private const long MaxBitFunctionValue = 281474976710655L;

    private static ScalarValue BitAnd(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BitBinary(args, (left, right) => left & right);

    private static ScalarValue BitOr(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BitBinary(args, (left, right) => left | right);

    private static ScalarValue BitXor(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BitBinary(args, (left, right) => left ^ right);

    private static ScalarValue BitBinary(IReadOnlyList<ScalarValue> args, Func<long, long, long> op)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (!TryGetBitInteger(args[0], out var left)) return ErrorValue.Num;
        if (!TryGetBitInteger(args[1], out var right)) return ErrorValue.Num;
        return new NumberValue(op(left, right));
    }

    private static ScalarValue BitLShift(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BitShift(args, leftShift: true);

    private static ScalarValue BitRShift(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BitShift(args, leftShift: false);

    private static ScalarValue BitShift(IReadOnlyList<ScalarValue> args, bool leftShift)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (!TryGetBitInteger(args[0], out var number)) return ErrorValue.Num;
        if (!TryGetEngineeringInteger(args[1], out var shift) || Math.Abs(shift) > 53) return ErrorValue.Num;

        bool effectiveLeft = leftShift ? shift >= 0 : shift < 0;
        int bits = (int)Math.Abs(shift);
        if (effectiveLeft && bits > 0 && number > (MaxBitFunctionValue >> bits))
            return ErrorValue.Num;

        long result = effectiveLeft ? number << bits : number >> bits;
        return result > MaxBitFunctionValue ? ErrorValue.Num : new NumberValue(result);
    }

    private static bool TryGetBitInteger(ScalarValue arg, out long value)
    {
        if (!TryGetEngineeringInteger(arg, out value)) return false;
        return value >= 0 && value <= MaxBitFunctionValue;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase B — Statistical Distribution Functions
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Numerical primitives ─────────────────────────────────────────────────

    /// <summary>Horner's-polynomial approximation for erf(x).</summary>
    private static double Erf(double x)
    {
        const double a1 = 0.254829592, a2 = -0.284496736, a3 = 1.421413741,
                     a4 = -1.453152027, a5 = 1.061405429, p = 0.3275911;
        int sign = x < 0 ? -1 : 1;
        x = Math.Abs(x);
        double t = 1.0 / (1.0 + p * x);
        double y = 1.0 - ((((a5 * t + a4) * t + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
        return sign * y;
    }

    private static double NormSCdf(double z) => 0.5 * (1.0 + Erf(z / Math.Sqrt(2.0)));
    private static double NormSPdf(double z) => Math.Exp(-0.5 * z * z) / Math.Sqrt(2.0 * Math.PI);

    /// <summary>Inverse standard-normal CDF (Acklam rational approximation with CDF refinement).</summary>
    private static double NormSInv(double p)
    {
        if (p <= 0 || p >= 1) throw new FormulaEvalException("#NUM!", "probability out of range");
        if (p == 0.5) return 0.0;

        const double plow = 0.02425;
        const double phigh = 1.0 - plow;
        double x;

        if (p < plow)
        {
            double q = Math.Sqrt(-2.0 * Math.Log(p));
            x = (((((-0.007784894002430293 * q - 0.3223964580411365) * q - 2.400758277161838) * q - 2.549732539343734) * q + 4.374664141464968) * q + 2.938163982698783) /
                ((((0.007784695709041462 * q + 0.3224671290700398) * q + 2.445134137142996) * q + 3.754408661907416) * q + 1.0);
        }
        else if (p <= phigh)
        {
            double q = p - 0.5;
            double r = q * q;
            x = (((((-39.69683028665376 * r + 220.9460984245205) * r - 275.9285104469687) * r + 138.3577518672690) * r - 30.66479806614716) * r + 2.506628277459239) * q /
                (((((-54.47609879822406 * r + 161.5858368580409) * r - 155.6989798598866) * r + 66.80131188771972) * r - 13.28068155288572) * r + 1.0);
        }
        else
        {
            double q = Math.Sqrt(-2.0 * Math.Log(1.0 - p));
            x = -(((((-0.007784894002430293 * q - 0.3223964580411365) * q - 2.400758277161838) * q - 2.549732539343734) * q + 4.374664141464968) * q + 2.938163982698783) /
                ((((0.007784695709041462 * q + 0.3224671290700398) * q + 2.445134137142996) * q + 3.754408661907416) * q + 1.0);
        }

        for (int i = 0; i < 2; i++)
        {
            double pdf = NormSPdf(x);
            if (pdf == 0 || !double.IsFinite(pdf)) break;
            x -= (NormSCdf(x) - p) / pdf;
        }

        return x;
    }

    /// <summary>Lanczos approximation for ln(Gamma(x)), x > 0.</summary>
    private static double LogGamma(double x)
    {
        double[] c = { 76.18009172947146, -86.50532032941677, 24.01409824083091,
                       -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5 };
        double y = x, tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        double ser = 1.000000000190015;
        for (int j = 0; j < 6; j++) ser += c[j] / ++y;
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }

    /// <summary>Gamma function value via exp(LogGamma). Handles negative non-integer x via reflection.</summary>
    private static double GammaValue(double x)
    {
        if (x <= 0)
        {
            // Reflection: Gamma(x)*Gamma(1-x) = pi/sin(pi*x)
            if (x == Math.Floor(x)) return double.NaN; // pole
            return Math.PI / (Math.Sin(Math.PI * x) * GammaValue(1.0 - x));
        }
        return Math.Exp(LogGamma(x));
    }

    /// <summary>Regularised incomplete gamma P(a, x) using series (x &lt; a+1) or CF (x >= a+1).</summary>
    private static double GammaInc(double a, double x)
    {
        if (x < 0 || a <= 0) return double.NaN;
        if (x == 0) return 0;
        return x < a + 1.0 ? GammaIncSeries(a, x) : 1.0 - GammaIncCf(a, x);
    }

    private static double GammaIncSeries(double a, double x)
    {
        double ap = a, del = 1.0 / a, sum = del;
        for (int n = 1; n <= 300; n++)
        {
            ap++; del *= x / ap; sum += del;
            if (Math.Abs(del) < Math.Abs(sum) * 1e-12) break;
        }
        return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
    }

    private static double GammaIncCf(double a, double x)
    {
        double b = x + 1.0 - a, c = 1.0 / 1e-30, d = 1.0 / b, h = d;
        if (Math.Abs(d) < 1e-30) d = 1e-30;
        for (int i = 1; i <= 300; i++)
        {
            double an = -i * (i - a);
            b += 2.0;
            d = an * d + b; if (Math.Abs(d) < 1e-30) d = 1e-30;
            c = b + an / c; if (Math.Abs(c) < 1e-30) c = 1e-30;
            d = 1.0 / d; double del2 = d * c; h *= del2;
            if (Math.Abs(del2 - 1.0) < 1e-12) break;
        }
        return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
    }

    /// <summary>Inverse of GammaInc(a, x) = p via Newton refinement.</summary>
    private static double GammaInv(double p, double a)
    {
        if (p <= 0) return 0;
        if (p >= 1) return double.PositiveInfinity;
        // Initial guess via normal approximation
        double x = a * Math.Pow(NormSInv(p) / Math.Sqrt(9 * a) + 1 - 1.0 / (9 * a), 3);
        if (x <= 0) x = 0.01;
        for (int i = 0; i < 200; i++)
        {
            double f = GammaInc(a, x) - p;
            double df = Math.Exp((a - 1) * Math.Log(x) - x - LogGamma(a));
            if (df == 0) break;
            double dx = f / df;
            x -= dx;
            if (x <= 0) x = 1e-10;
            if (Math.Abs(dx) < x * 1e-10) break;
        }
        return x;
    }

    /// <summary>Regularised incomplete beta I_x(a, b).</summary>
    private static double BetaInc(double a, double b, double x)
    {
        if (x < 0 || x > 1) return double.NaN;
        if (x == 0) return 0;
        if (x == 1) return 1;
        // Use symmetry when x > (a+1)/(a+b+2) for better CF convergence
        if (x > (a + 1) / (a + b + 2))
            return 1.0 - BetaInc(b, a, 1.0 - x);
        double lbeta = LogGamma(a) + LogGamma(b) - LogGamma(a + b);
        double front = Math.Exp(Math.Log(x) * a + Math.Log(1 - x) * b - lbeta) / a;
        return front * BetaCf(a, b, x);
    }

    private static double BetaCf(double a, double b, double x)
    {
        const int maxIter = 300; const double eps = 3e-12;
        double qab = a + b, qap = a + 1, qam = a - 1;
        double c = 1, d = 1 - qab * x / qap;
        if (Math.Abs(d) < 1e-30) d = 1e-30;
        d = 1 / d; double h = d;
        for (int m = 1; m <= maxIter; m++)
        {
            int m2 = 2 * m;
            double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d; if (Math.Abs(d) < 1e-30) d = 1e-30;
            c = 1 + aa / c; if (Math.Abs(c) < 1e-30) c = 1e-30;
            d = 1 / d; h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d; if (Math.Abs(d) < 1e-30) d = 1e-30;
            c = 1 + aa / c; if (Math.Abs(c) < 1e-30) c = 1e-30;
            d = 1 / d; double del = d * c; h *= del;
            if (Math.Abs(del - 1) < eps) break;
        }
        return h;
    }

    /// <summary>Inverse regularised incomplete beta via Newton's method.</summary>
    private static double BetaInv(double p, double a, double b)
    {
        if (p <= 0) return 0;
        if (p >= 1) return 1;
        double x = a / (a + b); // initial guess: mean of beta
        for (int i = 0; i < 200; i++)
        {
            double f = BetaInc(a, b, x) - p;
            double lbeta = LogGamma(a) + LogGamma(b) - LogGamma(a + b);
            double df = Math.Exp((a - 1) * Math.Log(x) + (b - 1) * Math.Log(1 - x) - lbeta);
            if (df == 0) break;
            double dx = f / df;
            x -= dx;
            x = Math.Clamp(x, 1e-10, 1.0 - 1e-10);
            if (Math.Abs(dx) < 1e-10) break;
        }
        return x;
    }

    /// <summary>Student-t CDF using regularised incomplete beta.</summary>
    private static double TCdf(double t, double df)
    {
        double x = df / (df + t * t);
        double tail = 0.5 * BetaInc(df / 2.0, 0.5, x);
        return t >= 0 ? 1.0 - tail : tail;
    }

    private static double TPdf(double t, double df)
        => Math.Exp(LogGamma((df + 1) / 2.0) - LogGamma(df / 2.0))
           / (Math.Sqrt(df * Math.PI) * Math.Pow(1 + t * t / df, (df + 1) / 2.0));

    /// <summary>Inverse t-distribution CDF via bisection.</summary>
    private static double TInv(double p, double df)
    {
        if (p <= 0 || p >= 1) throw new FormulaEvalException("#NUM!", "p out of range");
        double lo = -1e9, hi = 1e9;
        for (int i = 0; i < 300; i++)
        {
            double mid = (lo + hi) / 2.0;
            if (TCdf(mid, df) < p) lo = mid; else hi = mid;
            if (hi - lo < 1e-10) break;
        }
        return (lo + hi) / 2.0;
    }

    /// <summary>F-distribution CDF.</summary>
    private static double FCdf(double x, double d1, double d2)
    {
        if (x <= 0) return 0;
        double t = d1 * x / (d1 * x + d2);
        return BetaInc(d1 / 2.0, d2 / 2.0, t);
    }

    private static double FPdf(double x, double d1, double d2)
    {
        if (x <= 0) return 0;
        double lbeta = LogGamma(d1 / 2.0) + LogGamma(d2 / 2.0) - LogGamma((d1 + d2) / 2.0);
        return Math.Exp((d1 / 2.0) * Math.Log(d1) + (d2 / 2.0) * Math.Log(d2)
                        + (d1 / 2.0 - 1) * Math.Log(x)
                        - ((d1 + d2) / 2.0) * Math.Log(d1 * x + d2) - lbeta);
    }

    /// <summary>Inverse F-distribution CDF via bisection.</summary>
    private static double FInv(double p, double d1, double d2)
    {
        if (p <= 0) return 0;
        if (p >= 1) throw new FormulaEvalException("#NUM!", "p >= 1");
        double lo = 0, hi = 1e9;
        for (int i = 0; i < 300; i++)
        {
            double mid = (lo + hi) / 2.0;
            if (FCdf(mid, d1, d2) < p) lo = mid; else hi = mid;
            if (hi - lo < 1e-9) break;
        }
        return (lo + hi) / 2.0;
    }

    /// <summary>Chi-squared CDF (special case of Gamma).</summary>
    private static double ChiSqCdf(double x, double df) => x <= 0 ? 0.0 : GammaInc(df / 2.0, x / 2.0);

    private static double ChiSqPdf(double x, double df)
    {
        if (x <= 0) return 0;
        return Math.Exp((df / 2.0 - 1) * Math.Log(x) - x / 2.0 - (df / 2.0) * Math.Log(2) - LogGamma(df / 2.0));
    }

    private static double ChiSqInv(double p, double df) => 2.0 * GammaInv(p, df / 2.0);

    // ── Helper: collect two parallel arrays from two args (range or scalar) ─

    private static (List<double>? A, List<double>? B, ErrorValue? Err)
        CollectPair(ScalarValue argA, ScalarValue argB)
    {
        var (a, ea) = argA is RangeValue rva ? CollectRangeNumbers(rva) : CollectNumbers(new[] { argA });
        if (ea is not null) return (null, null, ea);
        var (b, eb) = argB is RangeValue rvb ? CollectRangeNumbers(rvb) : CollectNumbers(new[] { argB });
        if (eb is not null) return (null, null, eb);
        return (a, b, null);
    }

    // ── B1: Normal distribution ───────────────────────────────────────────────

    private static ScalarValue NormDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        double x = ToNumber(args[0]), mean = ToNumber(args[1]), stdev = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
        if (stdev <= 0) return ErrorValue.Num;
        double z = (x - mean) / stdev;
        return cum ? NumberResult(NormSCdf(z)) : NumberResult(NormSPdf(z) / stdev);
    }

    private static ScalarValue NormInv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double prob = ToNumber(args[0]), mean = ToNumber(args[1]), stdev = ToNumber(args[2]);
        if (stdev <= 0 || prob <= 0 || prob >= 1) return ErrorValue.Num;
        return NumberResult(NormSInv(prob) * stdev + mean);
    }

    private static ScalarValue NormSDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double z = ToNumber(args[0]);
        bool cum = ToBool(args[1]);
        return cum ? NumberResult(NormSCdf(z)) : NumberResult(NormSPdf(z));
    }

    private static ScalarValue NormSInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        double prob = ToNumber(args[0]);
        if (prob <= 0 || prob >= 1) return ErrorValue.Num;
        return NumberResult(NormSInv(prob));
    }

    private static ScalarValue Standardize(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double x = ToNumber(args[0]), mean = ToNumber(args[1]), stdev = ToNumber(args[2]);
        if (stdev <= 0) return ErrorValue.Num;
        return NumberResult((x - mean) / stdev);
    }

    // ── B2: T distribution ────────────────────────────────────────────────────

    private static ScalarValue TDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double x = ToNumber(args[0]);
        double df = Math.Truncate(ToNumber(args[1]));
        bool cum = ToBool(args[2]);
        if (df < 1) return ErrorValue.Num;
        return cum ? NumberResult(TCdf(x, df)) : NumberResult(TPdf(x, df));
    }

    private static ScalarValue TDistRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double x = ToNumber(args[0]);
        double df = Math.Truncate(ToNumber(args[1]));
        if (df < 1) return ErrorValue.Num;
        if (x < 0) return ErrorValue.Num;
        return NumberResult(1.0 - TCdf(x, df));
    }

    private static ScalarValue TDist2T(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double x = ToNumber(args[0]);
        double df = Math.Truncate(ToNumber(args[1]));
        if (df < 1) return ErrorValue.Num;
        if (x < 0) return ErrorValue.Num;
        return NumberResult(2.0 * (1.0 - TCdf(x, df)));
    }

    private static ScalarValue TInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double prob = ToNumber(args[0]);
        double df = Math.Truncate(ToNumber(args[1]));
        if (df < 1 || prob <= 0 || prob >= 1) return ErrorValue.Num;
        return NumberResult(TInv(prob, df));
    }

    private static ScalarValue TInv2TFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double prob = ToNumber(args[0]);
        double df = Math.Truncate(ToNumber(args[1]));
        if (df < 1 || prob <= 0 || prob > 1) return ErrorValue.Num;
        // T.INV.2T(p, df) returns the positive t s.t. P(|T| > t) = p
        // i.e. the one-tail area is p/2, so we solve TCdf(-t) = p/2
        return NumberResult(TInv(1.0 - prob / 2.0, df));
    }

    private static ScalarValue TTest(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        var (a, b, err) = CollectPair(args[0], args[1]);
        if (err is not null) return err;
        int tails = (int)Math.Truncate(ToNumber(args[2]));
        int type = (int)Math.Truncate(ToNumber(args[3]));
        if (tails < 1 || tails > 2 || type < 1 || type > 3) return ErrorValue.Num;
        if (a!.Count == 0 || b!.Count == 0) return ErrorValue.NA;

        double t, df;
        if (type == 1) // paired
        {
            if (a.Count != b.Count) return ErrorValue.NA;
            int n = a.Count;
            double[] diffs = new double[n];
            for (int i = 0; i < n; i++) diffs[i] = a[i] - b[i];
            double meanD = diffs.Average();
            double s2 = diffs.Sum(d => (d - meanD) * (d - meanD)) / (n - 1);
            if (s2 == 0) return ErrorValue.DivByZero;
            t = meanD / Math.Sqrt(s2 / n);
            df = n - 1;
        }
        else if (type == 2) // equal variances
        {
            int n1 = a.Count, n2 = b.Count;
            double m1 = a.Average(), m2 = b.Average();
            double s1 = a.Sum(x => (x - m1) * (x - m1));
            double s2 = b.Sum(x => (x - m2) * (x - m2));
            double sp2 = (s1 + s2) / (n1 + n2 - 2);
            if (sp2 == 0) return ErrorValue.DivByZero;
            t = (m1 - m2) / Math.Sqrt(sp2 * (1.0 / n1 + 1.0 / n2));
            df = n1 + n2 - 2;
        }
        else // unequal variances (Welch)
        {
            int n1 = a.Count, n2 = b.Count;
            double m1 = a.Average(), m2 = b.Average();
            double v1 = a.Sum(x => (x - m1) * (x - m1)) / (n1 - 1);
            double v2 = b.Sum(x => (x - m2) * (x - m2)) / (n2 - 1);
            double se2 = v1 / n1 + v2 / n2;
            if (se2 == 0) return ErrorValue.DivByZero;
            t = (m1 - m2) / Math.Sqrt(se2);
            double v1n = v1 / n1, v2n = v2 / n2;
            df = (v1n + v2n) * (v1n + v2n) / (v1n * v1n / (n1 - 1) + v2n * v2n / (n2 - 1));
        }

        double p = tails == 1 ? 1.0 - TCdf(Math.Abs(t), df) : 2.0 * (1.0 - TCdf(Math.Abs(t), df));
        return NumberResult(p);
    }

    // ── B2: F distribution ────────────────────────────────────────────────────

    private static ScalarValue FDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        double x = ToNumber(args[0]);
        double d1 = Math.Truncate(ToNumber(args[1]));
        double d2 = Math.Truncate(ToNumber(args[2]));
        bool cum = ToBool(args[3]);
        if (d1 < 1 || d2 < 1 || x < 0) return ErrorValue.Num;
        return cum ? NumberResult(FCdf(x, d1, d2)) : NumberResult(FPdf(x, d1, d2));
    }

    private static ScalarValue FDistRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double x = ToNumber(args[0]);
        double d1 = Math.Truncate(ToNumber(args[1]));
        double d2 = Math.Truncate(ToNumber(args[2]));
        if (d1 < 1 || d2 < 1 || x < 0) return ErrorValue.Num;
        return NumberResult(1.0 - FCdf(x, d1, d2));
    }

    private static ScalarValue FInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double prob = ToNumber(args[0]);
        double d1 = Math.Truncate(ToNumber(args[1]));
        double d2 = Math.Truncate(ToNumber(args[2]));
        if (d1 < 1 || d2 < 1 || prob <= 0 || prob >= 1) return ErrorValue.Num;
        return NumberResult(FInv(prob, d1, d2));
    }

    private static ScalarValue FInvRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double prob = ToNumber(args[0]);
        double d1 = Math.Truncate(ToNumber(args[1]));
        double d2 = Math.Truncate(ToNumber(args[2]));
        if (d1 < 1 || d2 < 1 || prob <= 0 || prob >= 1) return ErrorValue.Num;
        return NumberResult(FInv(1.0 - prob, d1, d2));
    }

    private static ScalarValue FTest(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var (a, b, err) = CollectPair(args[0], args[1]);
        if (err is not null) return err;
        if (a!.Count < 2 || b!.Count < 2) return ErrorValue.DivByZero;
        double m1 = a.Average(), m2 = b.Average();
        double v1 = a.Sum(x => (x - m1) * (x - m1)) / (a.Count - 1);
        double v2 = b.Sum(x => (x - m2) * (x - m2)) / (b.Count - 1);
        if (v2 == 0) return ErrorValue.DivByZero;
        double f = v1 / v2;
        double d1 = a.Count - 1, d2 = b.Count - 1;
        double p1 = FCdf(f, d1, d2);
        return NumberResult(2.0 * Math.Min(p1, 1.0 - p1));
    }

    // ── B2: Chi-squared distribution ──────────────────────────────────────────

    private static ScalarValue ChiSqDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double x = ToNumber(args[0]);
        double df = Math.Truncate(ToNumber(args[1]));
        bool cum = ToBool(args[2]);
        if (df < 1 || x < 0) return ErrorValue.Num;
        return cum ? NumberResult(ChiSqCdf(x, df)) : NumberResult(ChiSqPdf(x, df));
    }

    private static ScalarValue ChiSqDistRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double x = ToNumber(args[0]);
        double df = Math.Truncate(ToNumber(args[1]));
        if (df < 1 || x < 0) return ErrorValue.Num;
        return NumberResult(1.0 - ChiSqCdf(x, df));
    }

    private static ScalarValue ChiSqInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double prob = ToNumber(args[0]);
        double df = Math.Truncate(ToNumber(args[1]));
        if (df < 1 || prob < 0 || prob >= 1) return ErrorValue.Num;
        return NumberResult(ChiSqInv(prob, df));
    }

    private static ScalarValue ChiSqInvRt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double prob = ToNumber(args[0]);
        double df = Math.Truncate(ToNumber(args[1]));
        if (df < 1 || prob <= 0 || prob > 1) return ErrorValue.Num;
        return NumberResult(ChiSqInv(1.0 - prob, df));
    }

    private static ScalarValue ChiSqTest(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[0] is not RangeValue rv0) return ErrorValue.Value;
        if (args[1] is not RangeValue rv1) return ErrorValue.Value;
        var actualFlat = rv0.Flatten().ToArray();
        var expectedFlat = rv1.Flatten().ToArray();
        if (actualFlat.Length != expectedFlat.Length) return ErrorValue.NA;
        int rows = rv0.RowCount, cols = rv0.ColCount;

        double chiSq = 0;
        int n = actualFlat.Length;
        for (int i = 0; i < n; i++)
        {
            if (actualFlat[i] is not NumberValue av) continue;
            if (expectedFlat[i] is not NumberValue ev) return ErrorValue.Value;
            if (ev.Value == 0) return ErrorValue.DivByZero;
            double diff = av.Value - ev.Value;
            chiSq += diff * diff / ev.Value;
        }

        // df = (rows-1)*(cols-1) for contingency, or (n-1) for one-way
        double df = rows == 1 || cols == 1
            ? n - 1
            : (double)(rows - 1) * (cols - 1);
        if (df < 1) return ErrorValue.Num;
        return NumberResult(1.0 - ChiSqCdf(chiSq, df));
    }

    // ── B3: Descriptive statistics ────────────────────────────────────────────

    private static ScalarValue Skew(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        int n = nums!.Count;
        if (n < 3) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s2 = nums.Sum(x => (x - mean) * (x - mean)) / (n - 1);
        if (s2 == 0) return ErrorValue.DivByZero;
        double s = Math.Sqrt(s2);
        double m3 = nums.Sum(x => Math.Pow((x - mean) / s, 3));
        return NumberResult(m3 * n / ((n - 1.0) * (n - 2.0)));
    }

    private static ScalarValue SkewP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        int n = nums!.Count;
        if (n < 1) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s2 = nums.Sum(x => (x - mean) * (x - mean)) / n;
        if (s2 == 0) return ErrorValue.DivByZero;
        double s = Math.Sqrt(s2);
        double m3 = nums.Sum(x => Math.Pow((x - mean) / s, 3));
        return NumberResult(m3 / n);
    }

    private static ScalarValue Kurt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        int n = nums!.Count;
        if (n < 4) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s2 = nums.Sum(x => (x - mean) * (x - mean)) / (n - 1);
        if (s2 == 0) return ErrorValue.DivByZero;
        double s = Math.Sqrt(s2);
        double m4 = nums.Sum(x => Math.Pow((x - mean) / s, 4));
        double kurtosis = (double)n * (n + 1) / ((n - 1.0) * (n - 2) * (n - 3)) * m4
                          - 3.0 * (n - 1) * (n - 1) / ((n - 2.0) * (n - 3));
        return NumberResult(kurtosis);
    }

    private static ScalarValue Frequency(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;

        // Collect data values — allow scalar or range
        var dataList = new List<double>();
        if (args[0] is RangeValue rvd)
        {
            foreach (var v in rvd.Flatten())
                if (v is NumberValue nv) dataList.Add(nv.Value);
        }
        else if (args[0] is NumberValue nva) dataList.Add(nva.Value);

        // Collect bins (sorted)
        var binsList = new List<double>();
        if (args[1] is RangeValue rvb)
        {
            foreach (var v in rvb.Flatten())
                if (v is NumberValue nv) binsList.Add(nv.Value);
        }
        else if (args[1] is NumberValue nvb) binsList.Add(nvb.Value);

        binsList.Sort();
        int binsCount = binsList.Count;
        int[] counts = new int[binsCount + 1];
        foreach (double d in dataList)
        {
            bool placed = false;
            for (int i = 0; i < binsCount; i++)
            {
                if (d <= binsList[i]) { counts[i]++; placed = true; break; }
            }
            if (!placed) counts[binsCount]++;
        }

        var result = new ScalarValue[binsCount + 1, 1];
        for (int i = 0; i <= binsCount; i++) result[i, 0] = new NumberValue(counts[i]);
        return new RangeValue(result);
    }

    private static ScalarValue ConfidenceNorm(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double alpha = ToNumber(args[0]), stdev = ToNumber(args[1]), size = ToNumber(args[2]);
        if (alpha <= 0 || alpha >= 1 || stdev <= 0 || size < 1) return ErrorValue.Num;
        int n = (int)Math.Truncate(size);
        return NumberResult(NormSInv(1.0 - alpha / 2.0) * stdev / Math.Sqrt(n));
    }

    private static ScalarValue ConfidenceT(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double alpha = ToNumber(args[0]), stdev = ToNumber(args[1]), size = ToNumber(args[2]);
        if (alpha <= 0 || alpha >= 1 || stdev <= 0 || size < 2) return ErrorValue.Num;
        int n = (int)Math.Truncate(size);
        double df = n - 1;
        return NumberResult(TInv(1.0 - alpha / 2.0, df) * stdev / Math.Sqrt(n));
    }

    // ── B4: Discrete distributions ────────────────────────────────────────────

    /// <summary>Log of binomial coefficient C(n,k).</summary>
    private static double LogBinom(int n, int k)
    {
        if (k < 0 || k > n) return double.NegativeInfinity;
        return LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1);
    }

    /// <summary>Binomial PMF P(X=k | n, p).</summary>
    private static double BinomPmf(int k, int n, double p)
        => Math.Exp(LogBinom(n, k) + k * Math.Log(p) + (n - k) * Math.Log(1 - p));

    /// <summary>Binomial CDF P(X &lt;= k | n, p) via regularised incomplete beta.</summary>
    private static double BinomCdf(int k, int n, double p)
    {
        if (k < 0) return 0;
        if (k >= n) return 1;
        // CDF = I_{1-p}(n-k, k+1)
        return BetaInc(n - k, k + 1, 1.0 - p);
    }

    private static ScalarValue BinomDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        int k = (int)Math.Truncate(ToNumber(args[0]));
        int n = (int)Math.Truncate(ToNumber(args[1]));
        double p = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
        if (k < 0 || n < 0 || k > n || p < 0 || p > 1) return ErrorValue.Num;
        return cum ? NumberResult(BinomCdf(k, n, p)) : NumberResult(BinomPmf(k, n, p));
    }

    private static ScalarValue BinomDistRange(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        int n = (int)Math.Truncate(ToNumber(args[0]));
        double p = ToNumber(args[1]);
        int k1 = (int)Math.Truncate(ToNumber(args[2]));
        int k2 = args.Count >= 4 && args[3] is not BlankValue ? (int)Math.Truncate(ToNumber(args[3])) : k1;
        if (n < 0 || p < 0 || p > 1 || k1 < 0 || k2 < k1 || k2 > n) return ErrorValue.Num;
        double sum = 0;
        for (int k = k1; k <= k2; k++) sum += BinomPmf(k, n, p);
        return NumberResult(sum);
    }

    private static ScalarValue BinomInv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        int n = (int)Math.Truncate(ToNumber(args[0]));
        double p = ToNumber(args[1]);
        double alpha = ToNumber(args[2]);
        if (n < 0 || p < 0 || p > 1 || alpha < 0 || alpha > 1) return ErrorValue.Num;
        double cumP = 0;
        for (int k = 0; k <= n; k++)
        {
            cumP += BinomPmf(k, n, p);
            if (cumP >= alpha) return new NumberValue(k);
        }
        return new NumberValue(n);
    }

    private static ScalarValue NegbinomDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        int f = (int)Math.Truncate(ToNumber(args[0]));
        int r = (int)Math.Truncate(ToNumber(args[1]));
        double p = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
        if (f < 0 || r < 1 || p <= 0 || p > 1) return ErrorValue.Num;

        if (!cum)
        {
            // PMF: C(f+r-1, f) * p^r * (1-p)^f
            double pmf = Math.Exp(LogBinom(f + r - 1, f) + r * Math.Log(p) + f * Math.Log(1 - p));
            return NumberResult(pmf);
        }
        // CDF = I_p(r, f+1)
        return NumberResult(BetaInc(r, f + 1, p));
    }

    private static ScalarValue PoissonDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        int x = (int)Math.Truncate(ToNumber(args[0]));
        double lambda = ToNumber(args[1]);
        bool cum = ToBool(args[2]);
        if (x < 0 || lambda < 0) return ErrorValue.Num;
        if (!cum)
        {
            // PMF: lambda^x * e^(-lambda) / x!
            double pmf = Math.Exp(x * Math.Log(lambda) - lambda - LogGamma(x + 1));
            return NumberResult(pmf);
        }
        // CDF = 1 - GammaInc(x+1, lambda) via regularised upper gamma = e^{-lambda} sum_{k=0}^{x} lambda^k / k!
        // = 1 - GammaInc(x+1, lambda)
        return NumberResult(1.0 - GammaInc(x + 1, lambda));
    }

    private static ScalarValue HypergeomDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        if (args[4] is ErrorValue e4) return e4;
        int s = (int)Math.Truncate(ToNumber(args[0]));   // sample successes
        int n = (int)Math.Truncate(ToNumber(args[1]));   // sample size
        int M = (int)Math.Truncate(ToNumber(args[2]));   // population successes
        int N = (int)Math.Truncate(ToNumber(args[3]));   // population size
        bool cum = ToBool(args[4]);
        if (s < 0 || n < 0 || M < 0 || N <= 0 || s > n || s > M || n > N || M > N) return ErrorValue.Num;

        if (!cum)
        {
            double pmf = Math.Exp(LogBinom(M, s) + LogBinom(N - M, n - s) - LogBinom(N, n));
            return NumberResult(pmf);
        }
        double cdf = 0;
        for (int k = Math.Max(0, n - (N - M)); k <= Math.Min(n, M) && k <= s; k++)
            cdf += Math.Exp(LogBinom(M, k) + LogBinom(N - M, n - k) - LogBinom(N, n));
        return NumberResult(cdf);
    }

    // ── B5: Continuous distributions ──────────────────────────────────────────

    private static ScalarValue ExponDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double x = ToNumber(args[0]), lambda = ToNumber(args[1]);
        bool cum = ToBool(args[2]);
        if (x < 0 || lambda <= 0) return ErrorValue.Num;
        return cum
            ? NumberResult(1.0 - Math.Exp(-lambda * x))
            : NumberResult(lambda * Math.Exp(-lambda * x));
    }

    private static ScalarValue WeibullDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        double x = ToNumber(args[0]), alpha = ToNumber(args[1]), beta = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
        if (x < 0 || alpha <= 0 || beta <= 0) return ErrorValue.Num;
        if (cum) return NumberResult(1.0 - Math.Exp(-Math.Pow(x / beta, alpha)));
        return NumberResult((alpha / beta) * Math.Pow(x / beta, alpha - 1) * Math.Exp(-Math.Pow(x / beta, alpha)));
    }

    private static ScalarValue GammaDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        double x = ToNumber(args[0]), alpha = ToNumber(args[1]), beta = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
        // Excel: beta is scale (theta), so mean = alpha*beta
        if (x < 0 || alpha <= 0 || beta <= 0) return ErrorValue.Num;
        if (cum) return NumberResult(GammaInc(alpha, x / beta));
        double pdf = Math.Exp((alpha - 1) * Math.Log(x) - x / beta - alpha * Math.Log(beta) - LogGamma(alpha));
        return NumberResult(pdf);
    }

    private static ScalarValue GammaInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double prob = ToNumber(args[0]), alpha = ToNumber(args[1]), beta = ToNumber(args[2]);
        if (prob < 0 || prob >= 1 || alpha <= 0 || beta <= 0) return ErrorValue.Num;
        return NumberResult(GammaInv(prob, alpha) * beta);
    }

    private static ScalarValue GammaLnFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        double x = ToNumber(args[0]);
        if (x <= 0) return ErrorValue.Num;
        return NumberResult(LogGamma(x));
    }

    private static ScalarValue GammaFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        double x = ToNumber(args[0]);
        if (x == 0 || (x < 0 && x == Math.Floor(x))) return ErrorValue.Num;
        double g = GammaValue(x);
        return double.IsFinite(g) ? NumberResult(g) : ErrorValue.Num;
    }

    private static ScalarValue BetaDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        double x = ToNumber(args[0]);
        double alpha = ToNumber(args[1]), beta = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
        double A = args.Count >= 5 && args[4] is not BlankValue ? ToNumber(args[4]) : 0.0;
        double B = args.Count >= 6 && args[5] is not BlankValue ? ToNumber(args[5]) : 1.0;
        if (alpha <= 0 || beta <= 0 || A >= B) return ErrorValue.Num;
        if (x < A || x > B) return ErrorValue.Num;
        double t = (x - A) / (B - A);
        if (cum) return NumberResult(BetaInc(alpha, beta, t));
        double lbeta = LogGamma(alpha) + LogGamma(beta) - LogGamma(alpha + beta);
        double pdf = Math.Exp((alpha - 1) * Math.Log(t) + (beta - 1) * Math.Log(1 - t) - lbeta) / (B - A);
        return NumberResult(pdf);
    }

    private static ScalarValue BetaInvFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double prob = ToNumber(args[0]);
        double alpha = ToNumber(args[1]), beta = ToNumber(args[2]);
        double A = args.Count >= 4 && args[3] is not BlankValue ? ToNumber(args[3]) : 0.0;
        double B = args.Count >= 5 && args[4] is not BlankValue ? ToNumber(args[4]) : 1.0;
        if (prob < 0 || prob > 1 || alpha <= 0 || beta <= 0 || A >= B) return ErrorValue.Num;
        return NumberResult(BetaInv(prob, alpha, beta) * (B - A) + A);
    }

    private static ScalarValue LognormDist(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        double x = ToNumber(args[0]), mean = ToNumber(args[1]), stdev = ToNumber(args[2]);
        bool cum = ToBool(args[3]);
        if (x <= 0 || stdev <= 0) return ErrorValue.Num;
        double z = (Math.Log(x) - mean) / stdev;
        if (cum) return NumberResult(NormSCdf(z));
        return NumberResult(NormSPdf(z) / (x * stdev));
    }

    private static ScalarValue LognormInv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double prob = ToNumber(args[0]), mean = ToNumber(args[1]), stdev = ToNumber(args[2]);
        if (prob <= 0 || prob >= 1 || stdev <= 0) return ErrorValue.Num;
        return NumberResult(Math.Exp(NormSInv(prob) * stdev + mean));
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase C  –  Financial functions
    // ═══════════════════════════════════════════════════════════════════

    // ── Private helpers ─────────────────────────────────────────────────

    private static double CalcPmt(double rate, double nper, double pv, double fv, int type)
    {
        if (Math.Abs(rate) < 1e-14) return -(pv + fv) / nper;
        double r1 = Math.Pow(1 + rate, nper);
        return -(pv * r1 + fv) * rate / ((1 + rate * type) * (r1 - 1));
    }

    private static double CalcIpmt(double rate, double per, double nper, double pv, double fv, int type)
    {
        double pmt = CalcPmt(rate, nper, pv, fv, type);
        if (Math.Abs(rate) < 1e-14) return 0.0;
        double pvAtPer = pv * Math.Pow(1 + rate, per - 1)
                       + pmt * (1 + rate * type) * (Math.Pow(1 + rate, per - 1) - 1) / rate;
        // Interest payment matches PMT sign convention: negative = outflow (borrower)
        return type == 0 ? -(pvAtPer * rate) : -((pvAtPer - pmt) * rate);
    }

    private static DateTime SerialToDate(double serial) =>
        serial < 60
            ? new DateTime(1899, 12, 30).AddDays(serial + 1)
            : new DateTime(1899, 12, 30).AddDays(serial);

    private static double DateToSerial(DateTime d) =>
        d < new DateTime(1900, 3, 1)
            ? (d - new DateTime(1899, 12, 30)).TotalDays - 1
            : (d - new DateTime(1899, 12, 30)).TotalDays;

    private static bool IsExcelFakeLeapDay(ScalarValue value)
    {
        if (value is ErrorValue) return false;
        double serial = ToNumber(value);
        return double.IsFinite(serial) && Math.Abs(serial - 60) < 1e-10;
    }

    private static double ActualYearLength(DateTime d1, DateTime d2)
    {
        if (d1.Year == d2.Year)
            return DateTime.IsLeapYear(d1.Year) ? 366.0 : 365.0;
        double years = d2.Year - d1.Year;
        double days = (d2 - d1).TotalDays;
        return days / years;
    }

    private static double DayCountFraction(DateTime d1, DateTime d2, int basis)
    {
        switch (basis)
        {
            case 0: // US 30/360 (NASD)
            {
                int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
                int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
                if (dd2 == 31 && dd1 >= 30) dd2 = 30;
                if (dd1 == 31) dd1 = 30;
                return ((y2 - y1) * 360 + (m2 - m1) * 30 + (dd2 - dd1)) / 360.0;
            }
            case 1: return (d2 - d1).TotalDays / ActualYearLength(d1, d2);
            case 2: return (d2 - d1).TotalDays / 360.0;
            case 3: return (d2 - d1).TotalDays / 365.0;
            case 4: // European 30/360
            {
                int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
                int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
                if (dd1 == 31) dd1 = 30;
                if (dd2 == 31) dd2 = 30;
                return ((y2 - y1) * 360 + (m2 - m1) * 30 + (dd2 - dd1)) / 360.0;
            }
            default: return (d2 - d1).TotalDays / 365.0;
        }
    }

    private static DateTime CouponDateBefore(DateTime settlement, DateTime maturity, int frequency)
    {
        int months = 12 / frequency;
        DateTime prev = maturity;
        // Walk backward from maturity until we pass settlement
        while (prev > settlement)
            prev = prev.AddMonths(-months);
        return prev;
    }

    private static DateTime CouponDateAfter(DateTime settlement, DateTime maturity, int frequency)
    {
        DateTime prev = CouponDateBefore(settlement, maturity, frequency);
        return prev.AddMonths(12 / frequency);
    }

    // Bond price helper (for PRICE/YIELD)
    private static double CalcBondPrice(DateTime settlement, DateTime maturity, double couponRate,
        double yld, double redemption, int frequency, int basis)
    {
        DateTime pcd = CouponDateBefore(settlement, maturity, frequency);
        DateTime ncd = CouponDateAfter(settlement, maturity, frequency);
        double daysInPeriod = (ncd - pcd).TotalDays;
        double daysToNext = (ncd - settlement).TotalDays;
        double a = daysInPeriod > 0 ? daysToNext / daysInPeriod : 1.0;

        // Count coupons from next coupon date to maturity
        int n = 0;
        DateTime d = ncd;
        int months = 12 / frequency;
        while (d <= maturity)
        {
            n++;
            d = d.AddMonths(months);
        }
        if (n == 0) n = 1;

        double c = couponRate / frequency * redemption;
        double y = yld / frequency;
        double price = 0;
        for (int k = 1; k <= n; k++)
            price += c / Math.Pow(1 + y, k - 1 + a);
        price += redemption / Math.Pow(1 + y, n - 1 + a);
        return price;
    }

    // ── C1: High-usage financial ─────────────────────────────────────────

    private static ScalarValue Ipmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate  = ToNumber(args[0]);
        double per   = ToNumber(args[1]);
        double nper  = ToNumber(args[2]);
        double pv    = ToNumber(args[3]);
        double fv    = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        double type  = args.Count > 5 && args[5] is not BlankValue ? ToNumber(args[5]) : 0;
        if (!double.IsFinite(rate) || !double.IsFinite(per) || !double.IsFinite(nper) ||
            !double.IsFinite(pv)   || !double.IsFinite(fv)  || !double.IsFinite(type))
            return ErrorValue.Num;
        int itype = (int)Math.Truncate(type);
        if (itype != 0 && itype != 1) return ErrorValue.Num;
        if (nper <= 0) return ErrorValue.Num;
        int iper = (int)Math.Truncate(per);
        if (iper < 1 || iper > (int)Math.Truncate(nper)) return ErrorValue.Num;
        return NumberResult(CalcIpmt(rate, iper, nper, pv, fv, itype));
    }

    private static ScalarValue Ppmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate  = ToNumber(args[0]);
        double per   = ToNumber(args[1]);
        double nper  = ToNumber(args[2]);
        double pv    = ToNumber(args[3]);
        double fv    = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        double type  = args.Count > 5 && args[5] is not BlankValue ? ToNumber(args[5]) : 0;
        if (!double.IsFinite(rate) || !double.IsFinite(per) || !double.IsFinite(nper) ||
            !double.IsFinite(pv)   || !double.IsFinite(fv)  || !double.IsFinite(type))
            return ErrorValue.Num;
        int itype = (int)Math.Truncate(type);
        if (itype != 0 && itype != 1) return ErrorValue.Num;
        if (nper <= 0) return ErrorValue.Num;
        int iper = (int)Math.Truncate(per);
        if (iper < 1 || iper > (int)Math.Truncate(nper)) return ErrorValue.Num;
        double pmt  = CalcPmt(rate, nper, pv, fv, itype);
        double ipmt = CalcIpmt(rate, iper, nper, pv, fv, itype);
        return NumberResult(pmt - ipmt);
    }

    private static ScalarValue Cumipmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate  = ToNumber(args[0]);
        double nper  = ToNumber(args[1]);
        double pv    = ToNumber(args[2]);
        double start = ToNumber(args[3]);
        double end   = ToNumber(args[4]);
        double type  = ToNumber(args[5]);
        if (!double.IsFinite(rate) || !double.IsFinite(nper) || !double.IsFinite(pv) ||
            !double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(type))
            return ErrorValue.Num;
        int itype  = (int)Math.Truncate(type);
        if (itype != 0 && itype != 1) return ErrorValue.Num;
        if (rate <= 0 || nper <= 0 || pv <= 0) return ErrorValue.Num;
        int is_ = (int)Math.Truncate(start), ie = (int)Math.Truncate(end);
        if (is_ < 1 || ie < is_ || ie > (int)Math.Truncate(nper)) return ErrorValue.Num;
        double sum = 0;
        for (int per = is_; per <= ie; per++)
            sum += CalcIpmt(rate, per, nper, pv, 0, itype);
        return NumberResult(sum);
    }

    private static ScalarValue Cumprinc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate  = ToNumber(args[0]);
        double nper  = ToNumber(args[1]);
        double pv    = ToNumber(args[2]);
        double start = ToNumber(args[3]);
        double end   = ToNumber(args[4]);
        double type  = ToNumber(args[5]);
        if (!double.IsFinite(rate) || !double.IsFinite(nper) || !double.IsFinite(pv) ||
            !double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(type))
            return ErrorValue.Num;
        int itype = (int)Math.Truncate(type);
        if (itype != 0 && itype != 1) return ErrorValue.Num;
        if (rate <= 0 || nper <= 0 || pv <= 0) return ErrorValue.Num;
        int is_ = (int)Math.Truncate(start), ie = (int)Math.Truncate(end);
        if (is_ < 1 || ie < is_ || ie > (int)Math.Truncate(nper)) return ErrorValue.Num;
        double pmt = CalcPmt(rate, nper, pv, 0, itype);
        double sum = 0;
        for (int per = is_; per <= ie; per++)
            sum += pmt - CalcIpmt(rate, per, nper, pv, 0, itype);
        return NumberResult(sum);
    }

    private static ScalarValue Effect(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double nomRate = ToNumber(args[0]);
        double npery   = Math.Truncate(ToNumber(args[1]));
        if (!double.IsFinite(nomRate) || !double.IsFinite(npery)) return ErrorValue.Num;
        if (nomRate <= 0 || npery < 1) return ErrorValue.Num;
        return NumberResult(Math.Pow(1 + nomRate / npery, npery) - 1);
    }

    private static ScalarValue Nominal(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double effectRate = ToNumber(args[0]);
        double npery      = Math.Truncate(ToNumber(args[1]));
        if (!double.IsFinite(effectRate) || !double.IsFinite(npery)) return ErrorValue.Num;
        if (effectRate <= 0 || npery < 1) return ErrorValue.Num;
        return NumberResult((Math.Pow(1 + effectRate, 1.0 / npery) - 1) * npery);
    }

    private static ScalarValue Mirr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        if (args[0] is not RangeValue valRange) return ErrorValue.Value;
        double financeRate  = ToNumber(args[1]);
        double reinvestRate = ToNumber(args[2]);
        if (!double.IsFinite(financeRate) || !double.IsFinite(reinvestRate)) return ErrorValue.Num;
        var (values, err) = CollectRangeNumbers(valRange);
        if (err is not null) return err;
        var cf = values!;
        int n = cf.Count;
        if (n < 2) return ErrorValue.Num;
        // NPV of negative flows at finance_rate
        double npvNeg = 0;
        for (int i = 0; i < n; i++)
            if (cf[i] < 0) npvNeg += cf[i] / Math.Pow(1 + financeRate, i);
        // NPV of positive flows at reinvest_rate
        double npvPos = 0;
        for (int i = 0; i < n; i++)
            if (cf[i] > 0) npvPos += cf[i] / Math.Pow(1 + reinvestRate, i);
        if (npvNeg == 0 || npvPos == 0) return ErrorValue.DivByZero;
        double mirr = Math.Pow((-npvPos * Math.Pow(1 + reinvestRate, n - 1)) / npvNeg, 1.0 / (n - 1)) - 1;
        return NumberResult(mirr);
    }

    private static ScalarValue Xirr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        if (args[0] is not RangeValue valRange) return ErrorValue.Value;
        if (args[1] is not RangeValue dateRange) return ErrorValue.Value;
        double guess = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 0.1;
        var (vals, ve) = CollectRangeNumbers(valRange);
        var (datesRaw, de) = CollectRangeNumbers(dateRange);
        if (ve is not null) return ve;
        if (de is not null) return de;
        var cf = vals!;
        var ds = datesRaw!;
        if (cf.Count < 2 || cf.Count != ds.Count) return ErrorValue.Num;
        var dates = ds.Select(SerialToDate).ToList();
        DateTime d0 = dates[0];
        // Newton-Raphson
        double r = guess;
        for (int iter = 0; iter < 200; iter++)
        {
            double f = 0, df = 0;
            for (int i = 0; i < cf.Count; i++)
            {
                double t = (dates[i] - d0).TotalDays / 365.0;
                double denom = Math.Pow(1 + r, t);
                f  += cf[i] / denom;
                df -= t * cf[i] / (denom * (1 + r));
            }
            if (Math.Abs(df) < 1e-14) break;
            double delta = f / df;
            r -= delta;
            if (Math.Abs(delta) < 1e-10) break;
        }
        if (!double.IsFinite(r)) return ErrorValue.Num;
        return NumberResult(r);
    }

    private static ScalarValue Xnpv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        if (args[1] is not RangeValue valRange) return ErrorValue.Value;
        if (args[2] is not RangeValue dateRange) return ErrorValue.Value;
        if (!double.IsFinite(rate) || rate <= -1) return ErrorValue.Num;
        var (vals, ve) = CollectRangeNumbers(valRange);
        var (datesRaw, de) = CollectRangeNumbers(dateRange);
        if (ve is not null) return ve;
        if (de is not null) return de;
        var cf = vals!;
        var ds = datesRaw!;
        if (cf.Count != ds.Count || cf.Count == 0) return ErrorValue.Num;
        var dates = ds.Select(SerialToDate).ToList();
        DateTime d0 = dates[0];
        double result = 0;
        for (int i = 0; i < cf.Count; i++)
        {
            double t = (dates[i] - d0).TotalDays / 365.0;
            result += cf[i] / Math.Pow(1 + rate, t);
        }
        return NumberResult(result);
    }

    private static ScalarValue Rri(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double nper = ToNumber(args[0]);
        double pv   = ToNumber(args[1]);
        double fv   = ToNumber(args[2]);
        if (!double.IsFinite(nper) || !double.IsFinite(pv) || !double.IsFinite(fv)) return ErrorValue.Num;
        if (nper <= 0 || pv == 0) return ErrorValue.Num;
        if ((pv > 0 && fv < 0) || (pv < 0 && fv > 0)) return ErrorValue.Num;
        double result = Math.Pow(fv / pv, 1.0 / nper) - 1;
        return NumberResult(result);
    }

    private static ScalarValue Pduration(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        double pv   = ToNumber(args[1]);
        double fv   = ToNumber(args[2]);
        if (!double.IsFinite(rate) || !double.IsFinite(pv) || !double.IsFinite(fv)) return ErrorValue.Num;
        if (rate <= 0 || pv <= 0 || fv <= 0) return ErrorValue.Num;
        return NumberResult((Math.Log(fv) - Math.Log(pv)) / Math.Log(1 + rate));
    }

    private static ScalarValue Fvschedule(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double principal = ToNumber(args[0]);
        if (args[1] is not RangeValue schedRange) return ErrorValue.Value;
        if (!double.IsFinite(principal)) return ErrorValue.Num;
        var (rates, re) = CollectRangeNumbers(schedRange);
        if (re is not null) return re;
        double result = principal;
        foreach (double r in rates!)
        {
            if (!double.IsFinite(r)) return ErrorValue.Num;
            result *= (1 + r);
        }
        return NumberResult(result);
    }

    // ── C2: Depreciation functions ───────────────────────────────────────

    private static ScalarValue Db(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double cost    = ToNumber(args[0]);
        double salvage = ToNumber(args[1]);
        double life    = ToNumber(args[2]);
        double period  = ToNumber(args[3]);
        double month   = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 12;
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life) ||
            !double.IsFinite(period) || !double.IsFinite(month))
            return ErrorValue.Num;
        int ilife = (int)Math.Truncate(life), iper = (int)Math.Truncate(period);
        int imonth = (int)Math.Truncate(month);
        if (cost <= 0 || salvage < 0 || ilife <= 0 || iper <= 0 || iper > ilife + 1) return ErrorValue.Num;
        if (imonth < 1 || imonth > 12) return ErrorValue.Num;
        if (salvage >= cost) return NumberResult(0);
        // Rate rounded to 3 decimal places
        double rate = Math.Round(1 - Math.Pow(salvage / cost, 1.0 / ilife), 3);
        double accumulated = 0;
        double dep = 0;
        for (int p = 1; p <= iper; p++)
        {
            if (p == 1)
                dep = cost * rate * imonth / 12.0;
            else if (p <= ilife)
                dep = (cost - accumulated) * rate;
            else // p == ilife + 1
                dep = (cost - accumulated) * rate * (12 - imonth + 1) / 12.0;
            if (p < iper) accumulated += dep;
        }
        return NumberResult(dep);
    }

    private static ScalarValue Ddb(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double cost    = ToNumber(args[0]);
        double salvage = ToNumber(args[1]);
        double life    = ToNumber(args[2]);
        double period  = ToNumber(args[3]);
        double factor  = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 2.0;
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life) ||
            !double.IsFinite(period) || !double.IsFinite(factor))
            return ErrorValue.Num;
        if (cost < 0 || salvage < 0 || life <= 0 || period <= 0 || factor <= 0) return ErrorValue.Num;
        double bookValue = cost;
        double accumulated = 0;
        int ip = (int)Math.Truncate(period);
        for (int p = 1; p <= ip; p++)
        {
            double dep = Math.Min(bookValue - salvage, bookValue * factor / life);
            dep = Math.Max(dep, 0);
            if (p < ip) { accumulated += dep; bookValue -= dep; }
            else return NumberResult(dep);
        }
        return NumberResult(0);
    }

    private static ScalarValue Vdb(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double cost        = ToNumber(args[0]);
        double salvage     = ToNumber(args[1]);
        double life        = ToNumber(args[2]);
        double startPeriod = ToNumber(args[3]);
        double endPeriod   = ToNumber(args[4]);
        double factor      = args.Count > 5 && args[5] is not BlankValue ? ToNumber(args[5]) : 2.0;
        bool noSwitch      = args.Count > 6 && args[6] is not BlankValue && ToBool(args[6]);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life) ||
            !double.IsFinite(startPeriod) || !double.IsFinite(endPeriod) || !double.IsFinite(factor))
            return ErrorValue.Num;
        if (cost < 0 || salvage < 0 || life <= 0 || startPeriod < 0 || endPeriod < startPeriod || factor <= 0)
            return ErrorValue.Num;
        if (endPeriod > life) return ErrorValue.Num;
        // Compute depreciation for fractional periods
        double totalDep = 0;
        double bookValue = cost;
        // Process each integer period between startPeriod and endPeriod
        double currentPeriod = startPeriod;
        while (currentPeriod < endPeriod)
        {
            double periodEnd = Math.Min(Math.Ceiling(currentPeriod + 1e-10), endPeriod);
            double fraction = periodEnd - currentPeriod;
            // Which integer period are we in?
            int p = (int)Math.Floor(currentPeriod + 1e-10);
            double ddbDep = bookValue * factor / life;
            double slnDep = (bookValue - salvage) / (life - p);
            double dep;
            if (!noSwitch && slnDep > ddbDep)
                dep = slnDep;
            else
                dep = ddbDep;
            dep = Math.Max(0, Math.Min(dep, bookValue - salvage));
            double partialDep = dep * fraction;
            totalDep += partialDep;
            bookValue -= partialDep;
            currentPeriod = periodEnd;
        }
        return NumberResult(totalDep);
    }

    private static ScalarValue Syd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double cost    = ToNumber(args[0]);
        double salvage = ToNumber(args[1]);
        double life    = ToNumber(args[2]);
        double per     = ToNumber(args[3]);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life) || !double.IsFinite(per))
            return ErrorValue.Num;
        if (life <= 0 || per <= 0 || per > life) return ErrorValue.Num;
        double result = (cost - salvage) * (life - per + 1) / (life * (life + 1) / 2.0);
        return NumberResult(result);
    }

    private static ScalarValue Amordegrc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double cost          = ToNumber(args[0]);
        double datePurchased = ToNumber(args[1]);
        double firstPeriod   = ToNumber(args[2]);
        double salvage       = ToNumber(args[3]);
        double period        = ToNumber(args[4]);
        double rate          = ToNumber(args[5]);
        int basis = args.Count > 6 && args[6] is not BlankValue ? (int)Math.Truncate(ToNumber(args[6])) : 0;
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(rate) || !double.IsFinite(period))
            return ErrorValue.Num;
        if (cost <= 0 || salvage < 0 || rate <= 0) return ErrorValue.Num;
        // Compute life in years
        double life = 1.0 / rate;
        // Determine coefficient
        double coeff;
        if (life < 3)       coeff = 1.0;
        else if (life < 5)  coeff = 1.5;
        else if (life <= 6) coeff = 2.0;
        else                coeff = 2.5;
        double depRate = rate * coeff;
        // First period proration
        DateTime dp = SerialToDate(datePurchased);
        DateTime fp = SerialToDate(firstPeriod);
        double firstFrac = DayCountFraction(dp, fp, basis);
        double bookValue = cost;
        int iper = (int)Math.Truncate(period);
        for (int p = 0; p <= iper; p++)
        {
            double dep;
            if (p == 0)
                dep = bookValue * depRate * firstFrac;
            else
                dep = bookValue * depRate;
            dep = Math.Max(0, Math.Min(dep, bookValue - salvage));
            if (p < iper)
                bookValue -= dep;
            else
                return NumberResult(dep);
        }
        return NumberResult(0);
    }

    private static ScalarValue Amorlinc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double cost          = ToNumber(args[0]);
        double datePurchased = ToNumber(args[1]);
        double firstPeriod   = ToNumber(args[2]);
        double salvage       = ToNumber(args[3]);
        double period        = ToNumber(args[4]);
        double rate          = ToNumber(args[5]);
        int basis = args.Count > 6 && args[6] is not BlankValue ? (int)Math.Truncate(ToNumber(args[6])) : 0;
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(rate) || !double.IsFinite(period))
            return ErrorValue.Num;
        if (cost <= 0 || salvage < 0 || rate <= 0) return ErrorValue.Num;
        DateTime dp = SerialToDate(datePurchased);
        DateTime fp = SerialToDate(firstPeriod);
        double firstFrac = DayCountFraction(dp, fp, basis);
        double annualDep = cost * rate;
        double bookValue = cost;
        int iper = (int)Math.Truncate(period);
        for (int p = 0; p <= iper; p++)
        {
            double dep;
            if (p == 0)
                dep = annualDep * firstFrac;
            else
                dep = annualDep;
            dep = Math.Max(0, Math.Min(dep, bookValue - salvage));
            if (p < iper)
                bookValue -= dep;
            else
                return NumberResult(dep);
        }
        return NumberResult(0);
    }

    // ── C3: Dollar conversion helpers ────────────────────────────────────

    private static ScalarValue Dollarde(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double d = ToNumber(args[0]);
        double f = Math.Truncate(ToNumber(args[1]));
        if (!double.IsFinite(d) || !double.IsFinite(f)) return ErrorValue.Num;
        if (f < 0) return ErrorValue.Num;
        if (f == 0) return ErrorValue.DivByZero;
        double intPart  = Math.Truncate(d);
        double fracPart = d - intPart;
        int digits = (int)Math.Ceiling(Math.Log10(f));
        if (digits < 1) digits = 1;
        return NumberResult(intPart + fracPart * Math.Pow(10, digits) / f);
    }

    private static ScalarValue Dollarfr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double d = ToNumber(args[0]);
        double f = Math.Truncate(ToNumber(args[1]));
        if (!double.IsFinite(d) || !double.IsFinite(f)) return ErrorValue.Num;
        if (f < 0) return ErrorValue.Num;
        if (f == 0) return ErrorValue.DivByZero;
        double intPart  = Math.Truncate(d);
        double fracPart = d - intPart;
        int digits = (int)Math.Ceiling(Math.Log10(f));
        if (digits < 1) digits = 1;
        return NumberResult(intPart + fracPart * f / Math.Pow(10, digits));
    }

    // ── C3: Bond/discount/settlement functions ───────────────────────────

    private static ScalarValue Disc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double pr          = ToNumber(args[2]);
        double redemption  = ToNumber(args[3]);
        int basis = args.Count > 4 && args[4] is not BlankValue ? (int)Math.Truncate(ToNumber(args[4])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(pr) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (pr <= 0 || redemption <= 0) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        double dcf = DayCountFraction(sd, md, basis);
        if (dcf <= 0) return ErrorValue.Num;
        return NumberResult((redemption - pr) / redemption / dcf);
    }

    private static ScalarValue Intrate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double investment  = ToNumber(args[2]);
        double redemption  = ToNumber(args[3]);
        int basis = args.Count > 4 && args[4] is not BlankValue ? (int)Math.Truncate(ToNumber(args[4])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(investment) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (investment <= 0 || redemption <= 0) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        double dcf = DayCountFraction(sd, md, basis);
        if (dcf <= 0) return ErrorValue.Num;
        return NumberResult((redemption - investment) / investment / dcf);
    }

    private static ScalarValue Received(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        double investment = ToNumber(args[2]);
        double discount   = ToNumber(args[3]);
        int basis = args.Count > 4 && args[4] is not BlankValue ? (int)Math.Truncate(ToNumber(args[4])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(investment) || !double.IsFinite(discount))
            return ErrorValue.Num;
        if (investment <= 0 || discount <= 0) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        double dcf = DayCountFraction(sd, md, basis);
        double denom = 1 - discount * dcf;
        if (Math.Abs(denom) < 1e-14) return ErrorValue.DivByZero;
        return NumberResult(investment / denom);
    }

    private static ScalarValue Accrint(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double issue         = ToNumber(args[0]);
        double firstInterest = ToNumber(args[1]);
        double settlement    = ToNumber(args[2]);
        double rate          = ToNumber(args[3]);
        double par           = ToNumber(args[4]);
        double frequency     = ToNumber(args[5]);
        int basis = args.Count > 6 && args[6] is not BlankValue ? (int)Math.Truncate(ToNumber(args[6])) : 0;
        if (!double.IsFinite(issue) || !double.IsFinite(settlement) || !double.IsFinite(rate) ||
            !double.IsFinite(par) || !double.IsFinite(frequency))
            return ErrorValue.Num;
        if (rate <= 0 || par <= 0 || frequency <= 0) return ErrorValue.Num;
        DateTime sd = SerialToDate(issue), sett = SerialToDate(settlement);
        if (sd >= sett) return ErrorValue.Num;
        double dcf = DayCountFraction(sd, sett, basis);
        return NumberResult(par * rate * dcf);
    }

    private static ScalarValue Tbilleq(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        double discount   = ToNumber(args[2]);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(discount))
            return ErrorValue.Num;
        if (discount <= 0 || discount >= 1) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        double dsm = (md - sd).TotalDays;
        if (dsm <= 0 || dsm > 182) return ErrorValue.Num;
        return NumberResult((365 * discount) / (360 - discount * dsm));
    }

    private static ScalarValue Tbillprice(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        double discount   = ToNumber(args[2]);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(discount))
            return ErrorValue.Num;
        if (discount <= 0) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        double dsm = (md - sd).TotalDays;
        if (dsm <= 0) return ErrorValue.Num;
        return NumberResult(100 * (1 - discount * dsm / 360.0));
    }

    private static ScalarValue Tbillyield(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        double pr         = ToNumber(args[2]);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(pr))
            return ErrorValue.Num;
        if (pr <= 0) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        double dsm = (md - sd).TotalDays;
        if (dsm <= 0) return ErrorValue.Num;
        return NumberResult((100 - pr) / pr * 360.0 / dsm);
    }

    // ── Coupon date helpers ──────────────────────────────────────────────

    private static ScalarValue Coupdaybs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        int frequency     = (int)Math.Truncate(ToNumber(args[2]));
        int basis = args.Count > 3 && args[3] is not BlankValue ? (int)Math.Truncate(ToNumber(args[3])) : 0;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        DateTime pcd = CouponDateBefore(sd, md, frequency);
        return NumberResult((sd - pcd).TotalDays);
    }

    private static ScalarValue Coupdays(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        int frequency     = (int)Math.Truncate(ToNumber(args[2]));
        int basis = args.Count > 3 && args[3] is not BlankValue ? (int)Math.Truncate(ToNumber(args[3])) : 0;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        DateTime pcd = CouponDateBefore(sd, md, frequency);
        DateTime ncd = CouponDateAfter(sd, md, frequency);
        if (basis == 1)
            return NumberResult((ncd - pcd).TotalDays);
        // Other bases use 360 or 365 adjusted
        return NumberResult(365.0 / frequency);
    }

    private static ScalarValue Coupdaysnc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        int frequency     = (int)Math.Truncate(ToNumber(args[2]));
        int basis = args.Count > 3 && args[3] is not BlankValue ? (int)Math.Truncate(ToNumber(args[3])) : 0;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        DateTime ncd = CouponDateAfter(sd, md, frequency);
        return NumberResult((ncd - sd).TotalDays);
    }

    private static ScalarValue Coupncd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        int frequency     = (int)Math.Truncate(ToNumber(args[2]));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        DateTime ncd = CouponDateAfter(sd, md, frequency);
        return NumberResult(DateToSerial(ncd));
    }

    private static ScalarValue Coupnum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        int frequency     = (int)Math.Truncate(ToNumber(args[2]));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        int months = 12 / frequency;
        int count = 0;
        DateTime d = md;
        while (d > sd) { count++; d = d.AddMonths(-months); }
        return NumberResult((double)count);
    }

    private static ScalarValue Couppcd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        int frequency     = (int)Math.Truncate(ToNumber(args[2]));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        DateTime pcd = CouponDateBefore(sd, md, frequency);
        return NumberResult(DateToSerial(pcd));
    }

    // ── Bond price/yield ─────────────────────────────────────────────────

    private static ScalarValue Price(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double rate        = ToNumber(args[2]);
        double yld         = ToNumber(args[3]);
        double redemption  = ToNumber(args[4]);
        int frequency      = (int)Math.Truncate(ToNumber(args[5]));
        int basis = args.Count > 6 && args[6] is not BlankValue ? (int)Math.Truncate(ToNumber(args[6])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(rate) ||
            !double.IsFinite(yld) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (rate < 0 || yld < 0 || redemption <= 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        double price = CalcBondPrice(sd, md, rate, yld, redemption, frequency, basis);
        return NumberResult(price);
    }

    private static ScalarValue Yield(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double rate        = ToNumber(args[2]);
        double pr          = ToNumber(args[3]);
        double redemption  = ToNumber(args[4]);
        int frequency      = (int)Math.Truncate(ToNumber(args[5]));
        int basis = args.Count > 6 && args[6] is not BlankValue ? (int)Math.Truncate(ToNumber(args[6])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(rate) ||
            !double.IsFinite(pr) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (rate < 0 || pr <= 0 || redemption <= 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        // Newton-Raphson: find y such that Price(y) = pr
        double y = 0.1;
        for (int iter = 0; iter < 200; iter++)
        {
            double p = CalcBondPrice(sd, md, rate, y, redemption, frequency, basis);
            double dy = 1e-6;
            double dp = (CalcBondPrice(sd, md, rate, y + dy, redemption, frequency, basis) - p) / dy;
            if (Math.Abs(dp) < 1e-14) break;
            double delta = (p - pr) / dp;
            y -= delta;
            if (y < -0.999) y = -0.999;
            if (Math.Abs(delta) < 1e-10) break;
        }
        return NumberResult(y);
    }

    private static ScalarValue Pricedisc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double discount    = ToNumber(args[2]);
        double redemption  = ToNumber(args[3]);
        int basis = args.Count > 4 && args[4] is not BlankValue ? (int)Math.Truncate(ToNumber(args[4])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(discount) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (discount <= 0 || redemption <= 0) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        double dcf = DayCountFraction(sd, md, basis);
        return NumberResult(redemption * (1 - discount * dcf));
    }

    private static ScalarValue Pricemat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double issue       = ToNumber(args[2]);
        double rate        = ToNumber(args[3]);
        double yld         = ToNumber(args[4]);
        int basis = args.Count > 5 && args[5] is not BlankValue ? (int)Math.Truncate(ToNumber(args[5])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(issue) ||
            !double.IsFinite(rate) || !double.IsFinite(yld))
            return ErrorValue.Num;
        if (rate < 0 || yld < 0) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity), id = SerialToDate(issue);
        if (sd >= md) return ErrorValue.Num;
        double dim = DayCountFraction(id, md, basis);
        double dsm = DayCountFraction(sd, md, basis);
        double result = 100.0 * (1 + rate * dim) / (1 + yld * dsm);
        return NumberResult(result);
    }

    private static ScalarValue Yielddisc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double pr          = ToNumber(args[2]);
        double redemption  = ToNumber(args[3]);
        int basis = args.Count > 4 && args[4] is not BlankValue ? (int)Math.Truncate(ToNumber(args[4])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(pr) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (pr <= 0 || redemption <= 0) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        double dcf = DayCountFraction(sd, md, basis);
        if (dcf <= 0) return ErrorValue.Num;
        return NumberResult((redemption / pr - 1) / dcf);
    }

    private static ScalarValue Yieldmat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double issue       = ToNumber(args[2]);
        double rate        = ToNumber(args[3]);
        double pr          = ToNumber(args[4]);
        int basis = args.Count > 5 && args[5] is not BlankValue ? (int)Math.Truncate(ToNumber(args[5])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(issue) ||
            !double.IsFinite(rate) || !double.IsFinite(pr))
            return ErrorValue.Num;
        if (rate < 0 || pr <= 0) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity), id = SerialToDate(issue);
        if (sd >= md) return ErrorValue.Num;
        double dim = DayCountFraction(id, md, basis);
        double dsm = DayCountFraction(sd, md, basis);
        if (dsm <= 0) return ErrorValue.Num;
        double num = (1 + rate * dim) / (pr / 100.0) - 1;
        return NumberResult(num / dsm);
    }

    private static ScalarValue Duration(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double coupon      = ToNumber(args[2]);
        double yld         = ToNumber(args[3]);
        int frequency      = (int)Math.Truncate(ToNumber(args[4]));
        int basis = args.Count > 5 && args[5] is not BlankValue ? (int)Math.Truncate(ToNumber(args[5])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(coupon) ||
            !double.IsFinite(yld))
            return ErrorValue.Num;
        if (coupon < 0 || yld < 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        if (sd >= md) return ErrorValue.Num;
        // Build coupon schedule
        DateTime pcd = CouponDateBefore(sd, md, frequency);
        DateTime ncd = CouponDateAfter(sd, md, frequency);
        double daysInPeriod = (ncd - pcd).TotalDays;
        double daysToNext   = (ncd - sd).TotalDays;
        double a = daysInPeriod > 0 ? daysToNext / daysInPeriod : 1.0;

        int months = 12 / frequency;
        var couponDates = new List<DateTime>();
        DateTime d = ncd;
        while (d <= md) { couponDates.Add(d); d = d.AddMonths(months); }
        if (couponDates.Count == 0) couponDates.Add(ncd);

        double c = coupon / frequency * 100;
        double y = yld / frequency;
        double price = 0, weightedTime = 0;
        for (int k = 0; k < couponDates.Count; k++)
        {
            double t = k + a;  // periods from settlement
            double cashflow = c;
            if (couponDates[k] == md) cashflow += 100;
            double pv = cashflow / Math.Pow(1 + y, t);
            price += pv;
            weightedTime += t / frequency * pv;
        }
        if (Math.Abs(price) < 1e-14) return ErrorValue.Num;
        return NumberResult(weightedTime / price);
    }

    private static ScalarValue Mduration(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var dur = Duration(args, ctx);
        if (dur is not NumberValue dv) return dur;
        double yld = ToNumber(args[3]);
        double frequency = Math.Truncate(ToNumber(args[4]));
        if (frequency <= 0) return ErrorValue.Num;
        return NumberResult(dv.Value / (1 + yld / frequency));
    }

    // ── Odd coupon period functions ──────────────────────────────────────

    private static ScalarValue Oddfprice(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // Odd first period: settlement, maturity, issue, first_coupon, rate, yld, redemption, frequency, [basis]
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double issue       = ToNumber(args[2]);
        double firstCoupon = ToNumber(args[3]);
        double rate        = ToNumber(args[4]);
        double yld         = ToNumber(args[5]);
        double redemption  = ToNumber(args[6]);
        int frequency      = (int)Math.Truncate(ToNumber(args[7]));
        int basis = args.Count > 8 && args[8] is not BlankValue ? (int)Math.Truncate(ToNumber(args[8])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(issue) ||
            !double.IsFinite(firstCoupon) || !double.IsFinite(rate) || !double.IsFinite(yld) ||
            !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (rate < 0 || yld < 0 || redemption <= 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (basis < 0 || basis > 4) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        DateTime id = SerialToDate(issue);
        DateTime fcd = SerialToDate(firstCoupon);
        if (!(md > fcd && fcd > sd && sd > id)) return ErrorValue.Num;
        double price = OddFirstPrice(id, sd, md, fcd, rate, yld, redemption, frequency, basis);
        return NumberResult(price);
    }

    private static ScalarValue Oddfyield(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // settlement, maturity, issue, first_coupon, rate, pr, redemption, frequency, [basis]
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double issue       = ToNumber(args[2]);
        double firstCoupon = ToNumber(args[3]);
        double rate        = ToNumber(args[4]);
        double pr          = ToNumber(args[5]);
        double redemption  = ToNumber(args[6]);
        int frequency      = (int)Math.Truncate(ToNumber(args[7]));
        int basis = args.Count > 8 && args[8] is not BlankValue ? (int)Math.Truncate(ToNumber(args[8])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(issue) ||
            !double.IsFinite(firstCoupon) || !double.IsFinite(rate) || !double.IsFinite(pr) ||
            !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (rate < 0 || pr <= 0 || redemption <= 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (basis < 0 || basis > 4) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        DateTime id = SerialToDate(issue), fcd = SerialToDate(firstCoupon);
        if (!(md > fcd && fcd > sd && sd > id)) return ErrorValue.Num;

        double y = 0.1;
        for (int iter = 0; iter < 200; iter++)
        {
            double p = OddFirstPrice(id, sd, md, fcd, rate, y, redemption, frequency, basis);
            double dy = 1e-6;
            double dp = (OddFirstPrice(id, sd, md, fcd, rate, y + dy, redemption, frequency, basis) - p) / dy;
            if (Math.Abs(dp) < 1e-14) break;
            double delta = (p - pr) / dp;
            y -= delta;
            if (y < -0.999) y = -0.999;
            if (Math.Abs(delta) < 1e-10) break;
        }
        return NumberResult(y);
    }

    private static ScalarValue Oddlprice(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // settlement, maturity, last_interest, rate, yld, redemption, frequency, [basis]
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double lastInterest = ToNumber(args[2]);
        double rate        = ToNumber(args[3]);
        double yld         = ToNumber(args[4]);
        double redemption  = ToNumber(args[5]);
        int frequency      = (int)Math.Truncate(ToNumber(args[6]));
        int basis = args.Count > 7 && args[7] is not BlankValue ? (int)Math.Truncate(ToNumber(args[7])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(lastInterest) ||
            !double.IsFinite(rate) || !double.IsFinite(yld) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (rate < 0 || yld < 0 || redemption <= 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (basis < 0 || basis > 4) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        DateTime li = SerialToDate(lastInterest);
        if (!(md > sd && sd > li)) return ErrorValue.Num;
        return NumberResult(OddLastPrice(li, sd, md, rate, yld, redemption, frequency, basis));
    }

    private static ScalarValue Oddlyield(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // settlement, maturity, last_interest, rate, pr, redemption, frequency, [basis]
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double lastInterest = ToNumber(args[2]);
        double rate        = ToNumber(args[3]);
        double pr          = ToNumber(args[4]);
        double redemption  = ToNumber(args[5]);
        int frequency      = (int)Math.Truncate(ToNumber(args[6]));
        int basis = args.Count > 7 && args[7] is not BlankValue ? (int)Math.Truncate(ToNumber(args[7])) : 0;
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(lastInterest) ||
            !double.IsFinite(rate) || !double.IsFinite(pr) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (rate < 0 || pr <= 0 || redemption <= 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (basis < 0 || basis > 4) return ErrorValue.Num;
        DateTime sd = SerialToDate(settlement), md = SerialToDate(maturity);
        DateTime li = SerialToDate(lastInterest);
        if (!(md > sd && sd > li)) return ErrorValue.Num;

        double daysInCoupon = CouponPeriodDays(li, frequency, basis);
        double accruedPeriods = FinancialDays(li, sd, basis) / daysInCoupon;
        double remainingPeriods = FinancialDays(sd, md, basis) / daysInCoupon;
        double oddCouponPeriods = FinancialDays(li, md, basis) / daysInCoupon;
        double couponAmt = rate / frequency * redemption;
        double numerator = redemption + couponAmt * oddCouponPeriods;
        double denominator = pr + couponAmt * accruedPeriods;
        if (Math.Abs(remainingPeriods) < 1e-14 || Math.Abs(denominator) < 1e-14) return ErrorValue.DivByZero;
        double y = (numerator / denominator - 1) / remainingPeriods * frequency;
        return NumberResult(y);
    }

    private static double OddFirstPrice(DateTime issue, DateTime settlement, DateTime maturity, DateTime firstCoupon,
        double rate, double yld, double redemption, int frequency, int basis)
    {
        int months = 12 / frequency;
        DateTime previousCoupon = firstCoupon.AddMonths(-months);
        double daysInCoupon = CouponPeriodDays(previousCoupon, frequency, basis);
        double accrued = FinancialDays(issue, settlement, basis) / daysInCoupon;
        double firstCouponPeriods = FinancialDays(issue, firstCoupon, basis) / daysInCoupon;
        double periodsToFirstCoupon = FinancialDays(settlement, firstCoupon, basis) / daysInCoupon;
        double couponAmt = rate / frequency * redemption;
        double yieldPerPeriod = yld / frequency;

        double price = couponAmt * firstCouponPeriods / Math.Pow(1 + yieldPerPeriod, periodsToFirstCoupon);
        int k = 1;
        for (DateTime d = firstCoupon.AddMonths(months); d <= maturity; d = d.AddMonths(months))
        {
            double cash = couponAmt;
            if (d == maturity)
                cash += redemption;
            price += cash / Math.Pow(1 + yieldPerPeriod, k + periodsToFirstCoupon);
            k++;
        }
        return price - couponAmt * accrued;
    }

    private static double OddLastPrice(DateTime lastInterest, DateTime settlement, DateTime maturity,
        double rate, double yld, double redemption, int frequency, int basis)
    {
        double daysInCoupon = CouponPeriodDays(lastInterest, frequency, basis);
        double accruedPeriods = FinancialDays(lastInterest, settlement, basis) / daysInCoupon;
        double remainingPeriods = FinancialDays(settlement, maturity, basis) / daysInCoupon;
        double oddCouponPeriods = FinancialDays(lastInterest, maturity, basis) / daysInCoupon;
        double couponAmt = rate / frequency * redemption;
        if (Math.Abs(remainingPeriods) < 1e-14) return double.NaN;
        double y = yld / frequency;
        return (redemption + couponAmt * oddCouponPeriods) / (1 + remainingPeriods * y)
             - couponAmt * accruedPeriods;
    }

    private static double CouponPeriodDays(DateTime periodStart, int frequency, int basis)
        => FinancialDays(periodStart, periodStart.AddMonths(12 / frequency), basis);

    private static double FinancialDays(DateTime d1, DateTime d2, int basis)
    {
        return basis switch
        {
            0 => Days360Us(d1, d2),
            1 => (d2 - d1).TotalDays,
            2 => (d2 - d1).TotalDays,
            3 => (d2 - d1).TotalDays,
            4 => Days360European(d1, d2),
            _ => (d2 - d1).TotalDays
        };
    }

    private static double Days360Us(DateTime d1, DateTime d2)
    {
        int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
        int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
        if (dd2 == 31 && dd1 >= 30) dd2 = 30;
        if (dd1 == 31) dd1 = 30;
        return (y2 - y1) * 360 + (m2 - m1) * 30 + (dd2 - dd1);
    }

    private static double Days360European(DateTime d1, DateTime d2)
    {
        int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
        int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
        if (dd1 == 31) dd1 = 30;
        if (dd2 == 31) dd2 = 30;
        return (y2 - y1) * 360 + (m2 - m1) * 30 + (dd2 - dd1);
    }
    // ── Phase D: Higher-order function implementations ───────────────────────

    // MAP(array1, [array2, ...], lambda(v1, [v2, ...])) → same-shape array
    private static ScalarValue MapFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 2) return ErrorValue.Value;
        if (args[^1] is not LambdaValue lambda) return ErrorValue.Value;

        var arrays = new List<RangeValue>(args.Count - 1);
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (args[i] is not RangeValue rv) return ErrorValue.Value;
            arrays.Add(rv);
        }

        int rows = arrays[0].RowCount, cols = arrays[0].ColCount;
        if (arrays.Any(a => a.RowCount != rows || a.ColCount != cols)) return ErrorValue.Value;
        if (lambda.Parameters.Count != arrays.Count) return ErrorValue.Value;

        var result = new ScalarValue[rows, cols];
        var invokeArgs = new ScalarValue[arrays.Count];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                for (int k = 0; k < arrays.Count; k++)
                    invokeArgs[k] = arrays[k].At(r + 1, c + 1);
                result[r, c] = ctx.InvokeLambda(lambda, invokeArgs);
            }
        return new RangeValue(result);
    }

    // REDUCE(initial, array, lambda(accumulator, value)) → scalar
    private static ScalarValue ReduceFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count != 3) return ErrorValue.Value;
        if (args[1] is not RangeValue rv) return ErrorValue.Value;
        if (args[2] is not LambdaValue lambda) return ErrorValue.Value;
        if (lambda.Parameters.Count != 2) return ErrorValue.Value;

        ScalarValue acc = args[0];
        var flat = rv.Flatten();
        foreach (var val in flat)
            acc = ctx.InvokeLambda(lambda, [acc, val]);
        return acc;
    }

    // SCAN(initial, array, lambda(accumulator, value)) → same-shape array of intermediates
    private static ScalarValue ScanFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count != 3) return ErrorValue.Value;
        if (args[1] is not RangeValue rv) return ErrorValue.Value;
        if (args[2] is not LambdaValue lambda) return ErrorValue.Value;
        if (lambda.Parameters.Count != 2) return ErrorValue.Value;

        int rows = rv.RowCount, cols = rv.ColCount;
        var result = new ScalarValue[rows, cols];
        ScalarValue acc = args[0];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                acc = ctx.InvokeLambda(lambda, [acc, rv.At(r + 1, c + 1)]);
                result[r, c] = acc;
            }
        return new RangeValue(result);
    }

    // BYROW(array, lambda(row)) → N×1 array
    private static ScalarValue ByRowFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count != 2) return ErrorValue.Value;
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is not LambdaValue lambda) return ErrorValue.Value;
        if (lambda.Parameters.Count != 1) return ErrorValue.Value;

        int rows = rv.RowCount, cols = rv.ColCount;
        var result = new ScalarValue[rows, 1];
        for (int r = 0; r < rows; r++)
        {
            var rowCells = new ScalarValue[1, cols];
            for (int c = 0; c < cols; c++) rowCells[0, c] = rv.At(r + 1, c + 1);
            result[r, 0] = ctx.InvokeLambda(lambda, [new RangeValue(rowCells)]);
        }
        return new RangeValue(result);
    }

    // BYCOL(array, lambda(col)) → 1×M array
    private static ScalarValue ByColFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count != 2) return ErrorValue.Value;
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is not LambdaValue lambda) return ErrorValue.Value;
        if (lambda.Parameters.Count != 1) return ErrorValue.Value;

        int rows = rv.RowCount, cols = rv.ColCount;
        var result = new ScalarValue[1, cols];
        for (int c = 0; c < cols; c++)
        {
            var colCells = new ScalarValue[rows, 1];
            for (int r = 0; r < rows; r++) colCells[r, 0] = rv.At(r + 1, c + 1);
            result[0, c] = ctx.InvokeLambda(lambda, [new RangeValue(colCells)]);
        }
        return new RangeValue(result);
    }

    // MAKEARRAY(rows, cols, lambda(row_num, col_num)) → rows×cols array
    private static ScalarValue MakeArrayFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count != 3) return ErrorValue.Value;
        if (args[2] is not LambdaValue lambda) return ErrorValue.Value;
        if (lambda.Parameters.Count != 2) return ErrorValue.Value;
        if (args[0] is not NumberValue rowsNv || args[1] is not NumberValue colsNv)
            return ErrorValue.Value;
        int rows = (int)rowsNv.Value, cols = (int)colsNv.Value;
        if (rows < 1 || cols < 1 || (long)rows * cols > 1_000_000L) return ErrorValue.Value;

        var result = new ScalarValue[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                result[r, c] = ctx.InvokeLambda(lambda, [new NumberValue(r + 1), new NumberValue(c + 1)]);
        return new RangeValue(result);
    }
}

/// <summary>
/// Context interface provided to built-in functions during evaluation.
/// </summary>
public interface IEvalContext
{
    ScalarValue GetCellValue(uint row, uint col);
    ScalarValue GetCellValue(string sheetName, uint row, uint col);
    IReadOnlyList<ScalarValue> GetRangeValues(uint startRow, uint startCol, uint endRow, uint endCol);
    IReadOnlyList<ScalarValue> GetRangeValues(string sheetName, uint startRow, uint startCol, uint endRow, uint endCol);

    /// <summary>
    /// Try to resolve a named range to a GridRange.
    /// Returns null if the name is not defined.
    /// </summary>
    Model.GridRange? TryResolveNamedRange(string name);

    /// <summary>
    /// Returns the sheet name for the given SheetId, or null if not found.
    /// Used by the evaluator to expand cross-sheet named ranges.
    /// </summary>
    string? TryGetSheetName(Model.SheetId sheetId);

    /// <summary>Returns true when the named sheet can be resolved in the current workbook context.</summary>
    bool SheetExists(string sheetName);

    /// <summary>Returns true if the row is hidden (filter, manual, or group collapse).</summary>
    bool IsRowHidden(uint row);

    /// <summary>Returns the current evaluation sheet (the formula's host sheet), or null when no sheet context.</summary>
    Model.Sheet? CurrentSheet { get; }

    /// <summary>Returns the current workbook, or null when no workbook context.</summary>
    Model.Workbook? CurrentWorkbook { get; }

    /// <summary>Try to get the underlying cell on the current sheet (for FORMULATEXT/ISFORMULA).</summary>
    Model.Cell? TryGetCell(uint row, uint col);

    /// <summary>Try to get the underlying cell on a named sheet (for FORMULATEXT/ISFORMULA).</summary>
    Model.Cell? TryGetCell(string sheetName, uint row, uint col);

    /// <summary>
    /// Look up a local variable binding created by LET or a LAMBDA invocation.
    /// Returns null if the name is not bound in the current scope.
    /// Default: no local bindings.
    /// </summary>
    ScalarValue? TryResolveLambdaBinding(string name) => null;

    /// <summary>
    /// Invoke a LambdaValue with pre-evaluated scalar arguments, creating a new scope.
    /// Default: returns #VALUE! — implementations that support LAMBDA must override this.
    /// </summary>
    ScalarValue InvokeLambda(LambdaValue lambda, IReadOnlyList<ScalarValue> args) => ErrorValue.Value;
}
