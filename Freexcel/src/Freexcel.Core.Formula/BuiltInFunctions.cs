using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;
using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

/// <summary>
/// Registry of built-in spreadsheet functions.
/// Phase 1 provides the 16 most essential functions per the build plan.
/// </summary>
public static partial class BuiltInFunctions
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
        ["ASC"]         = (Asc, 1, 1),
        ["DBCS"]        = (Dbcs, 1, 1),
        ["PHONETIC"]    = (Phonetic, 1, 1),
        ["BAHTTEXT"]    = (BahtText, 1, 1),
        ["ENCODEURL"]   = (EncodeUrl, 1, 1),
        ["FILTERXML"]   = (FilterXml, 2, 2),

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
        if (!double.IsFinite(number)) return ErrorValue.Num;
        if (digits > 15) return new NumberValue(number);
        if (digits >= 0)
            return NumberResult(Math.Round(number, digits, MidpointRounding.AwayFromZero));

        return NumberResult(RoundWithExcelDigits(number, digits));
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
        count = ExtendPastTrailingHighSurrogate(text, count);
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
        int start = RetreatBeforeLeadingLowSurrogate(text, text.Length - count);
        return TextResult(text[start..]);
    }

    private static int ExtendPastTrailingHighSurrogate(string text, int length)
    {
        if (length > 0 && length < text.Length && char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length]))
            return length + 1;
        return length;
    }

    private static int RetreatBeforeLeadingLowSurrogate(string text, int start)
    {
        if (start > 0 && start < text.Length && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1]))
            return start - 1;
        return start;
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
        var table = args[1] is RangeValue tableRange
            ? tableRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (args[2] is ErrorValue e2) return e2;

        var lookupValue = args[0];
        double rawCol = ToNumber(args[2]);
        if (!double.IsFinite(rawCol) || rawCol > int.MaxValue) return ErrorValue.Value;
        int colIndex = (int)rawCol;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        bool rangeLookup = args.Count < 4 || args[3] is BlankValue || ToBool(args[3]); // default TRUE

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
        var table = args[1] is RangeValue tableRange
            ? tableRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (args[2] is ErrorValue e2) return e2;

        var lookupValue = args[0];
        double rawRow = ToNumber(args[2]);
        if (!double.IsFinite(rawRow) || rawRow > int.MaxValue) return ErrorValue.Value;
        int rowIndex = (int)rawRow;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        bool rangeLookup = args.Count < 4 || args[3] is BlankValue || ToBool(args[3]);

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
        var table = args[0] is RangeValue tableRange
            ? tableRange
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
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
        var table = args[1] is RangeValue tableRange
            ? tableRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (table.RowCount > 1 && table.ColCount > 1) return ErrorValue.NA;

        var lookupValue = args[0];
        double rawMatchType = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 1;
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
        var lookupArr = args[1] is RangeValue lookupRange
            ? lookupRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
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
            int best = FindApproximateMatchIndex(lookupFlat, lookupValue, indices, nextSmaller: true);
            return best >= 0 ? new NumberValue(best + 1) : ErrorValue.NA;
        }

        int nextLarger = FindApproximateMatchIndex(lookupFlat, lookupValue, indices, nextSmaller: false);
        return nextLarger >= 0 ? new NumberValue(nextLarger + 1) : ErrorValue.NA;
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

        // Plain wildcard criteria in Excel match text cells only.
        if (IsWildcardCriteria(crit))
            return cellValue is TextValue tv && WildcardMatch(tv.Value, crit, ignoreCase: true);

        var cellText = cellValue is TextValue text ? text.Value :
                       TryCellNumber(cellValue, out double numericValue) ? numericValue.ToString(System.Globalization.CultureInfo.InvariantCulture) :
                       cellValue is BoolValue bv ? (bv.Value ? "TRUE" : "FALSE") :
                       "";
        return string.Equals(cellText, crit, StringComparison.OrdinalIgnoreCase);
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

        if (IsWildcardCriteria(rhs) && op is "=" or "<>")
        {
            bool matches = cellValue is TextValue textValue && WildcardMatch(textValue.Value, rhs, ignoreCase: true);
            return op == "=" ? matches : !matches;
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

    private static bool IsWildcardCriteria(string criteria)
    {
        for (int i = 0; i < criteria.Length; i++)
        {
            char ch = criteria[i];
            if (ch is '*' or '?') return true;
            if (ch == '~' && i + 1 < criteria.Length && (criteria[i + 1] is '*' or '?' or '~')) return true;
        }

        return false;
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
        if (TryFormatDateTimeInline(value, fmt, out var dateText)) return dateText;
        try { return value.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }

    private static bool TryFormatDateTimeInline(double value, string fmt, out string text)
    {
        text = string.Empty;
        if (!LooksLikeDateTimeFormat(fmt)) return false;

        try
        {
            var dt = SerialToDate(value);
            text = dt.ToString(ToDotNetDateTimeFormat(fmt), CultureInfo.GetCultureInfo("en-US"));
            return true;
        }
        catch
        {
            text = string.Empty;
            return false;
        }
    }

    private static bool LooksLikeDateTimeFormat(string fmt) =>
        fmt.Contains("AM/PM", StringComparison.OrdinalIgnoreCase)
        || fmt.Any(c => c is 'y' or 'Y' or 'h' or 'H')
        || LooksLikeMonthFormat(fmt)
        || LooksLikeDayFormat(fmt);

    private static bool LooksLikeMonthFormat(string fmt)
    {
        for (int i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] is not ('m' or 'M')) continue;
            var prev = PreviousNonSpace(fmt, i);
            var next = NextNonSpace(fmt, i + CountSame(fmt, i));
            if (prev is '/' or '-' or '\0' || next is '/' or '-' or '\0') return true;
        }

        return false;
    }

    private static bool LooksLikeDayFormat(string fmt)
    {
        for (int i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] is not ('d' or 'D')) continue;
            var prev = PreviousNonSpace(fmt, i);
            var next = NextNonSpace(fmt, i + CountSame(fmt, i));
            if (prev is '/' or '-' or ',' || next is '/' or '-' or ',') return true;
        }

        return false;
    }

    private static string ToDotNetDateTimeFormat(string fmt)
    {
        var sb = new System.Text.StringBuilder(fmt.Length);
        bool lastWasHour = false;
        bool lastWasMinute = false;

        for (int i = 0; i < fmt.Length;)
        {
            if (MatchesAt(fmt, i, "AM/PM"))
            {
                sb.Append("tt");
                i += 5;
                lastWasHour = lastWasMinute = false;
                continue;
            }

            char ch = fmt[i];
            int count = CountSame(fmt, i);
            switch (char.ToLowerInvariant(ch))
            {
                case 'y':
                    sb.Append(count <= 2 ? "yy" : "yyyy");
                    lastWasHour = lastWasMinute = false;
                    break;
                case 'd':
                    sb.Append(new string('d', Math.Min(count, 4)));
                    lastWasHour = lastWasMinute = false;
                    break;
                case 'h':
                    sb.Append(count <= 1 ? "h" : "hh");
                    lastWasHour = true;
                    lastWasMinute = false;
                    break;
                case 's':
                    sb.Append(count <= 1 ? "s" : "ss");
                    lastWasHour = false;
                    lastWasMinute = false;
                    break;
                case 'm':
                    bool minute = lastWasHour || lastWasMinute || PreviousNonSpace(fmt, i) == ':' || NextNonSpace(fmt, i + count) == ':';
                    sb.Append(minute
                        ? count <= 1 ? "m" : "mm"
                        : count switch { 1 => "M", 2 => "MM", 3 => "MMM", _ => "MMMM" });
                    lastWasHour = false;
                    lastWasMinute = minute;
                    break;
                default:
                    sb.Append(ch);
                    lastWasHour = ch == ':' && lastWasHour;
                    lastWasMinute = ch == ':' && lastWasMinute;
                    break;
            }

            i += count;
        }

        return sb.ToString();
    }

    private static bool MatchesAt(string text, int index, string value) =>
        index + value.Length <= text.Length
        && string.Compare(text, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private static int CountSame(string text, int index)
    {
        char ch = char.ToLowerInvariant(text[index]);
        int end = index + 1;
        while (end < text.Length && char.ToLowerInvariant(text[end]) == ch) end++;
        return end - index;
    }

    private static char PreviousNonSpace(string text, int index)
    {
        for (int i = index - 1; i >= 0; i--)
            if (!char.IsWhiteSpace(text[i])) return text[i];
        return '\0';
    }

    private static char NextNonSpace(string text, int index)
    {
        for (int i = index; i < text.Length; i++)
            if (!char.IsWhiteSpace(text[i])) return text[i];
        return '\0';
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
        var usCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        if (text.EndsWith('%') &&
            double.TryParse(text[..^1].Trim(), System.Globalization.NumberStyles.Any,
                usCulture, out var pct))
            return new NumberValue(pct / 100.0);
        if (double.TryParse(text, System.Globalization.NumberStyles.Any,
                usCulture, out var d))
            return new NumberValue(d);
        if (TryParseExcelFakeLeapDayValueText(text, usCulture, out var fakeLeapSerial))
            return new NumberValue(fakeLeapSerial);
        if (DateTime.TryParse(text, usCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return new NumberValue(IsTimeOnlyText(text) ? dt.TimeOfDay.TotalDays : DateToSerial(dt));
        return ErrorValue.Value;
    }

    private static bool IsTimeOnlyText(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Contains('/') || trimmed.Contains('-')) return false;
        if (Regex.IsMatch(trimmed, @"\b(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)", RegexOptions.IgnoreCase))
            return false;

        return trimmed.Contains(':')
            || Regex.IsMatch(trimmed, @"\b(?:am|pm)\b", RegexOptions.IgnoreCase);
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
        if (year == 1900 && month < 1) return ErrorValue.Num;
        try
        {
            var dt = new DateTime(year, 1, 1)
                .AddMonths(month - 1)
                .AddDays(day - 1);
            double serial = DateToSerial(dt);
            if (serial < 0) return ErrorValue.Num;
            if (year == 1900 && month >= 3 && dt < new DateTime(1900, 3, 1))
                return new NumberValue(serial + 1);
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
        if (!double.IsFinite(num) || num < 0 || num > 2958465.0)
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

    private static bool TryNonNegativeSerialToTimeParts(ScalarValue v, out int hour, out int minute, out int second)
    {
        hour = minute = second = 0;
        var num = ToNumber(v);
        if (!double.IsFinite(num) || num < 0 || num > 2958465.0)
            return false;

        var fraction = num - Math.Floor(num);
        var totalSeconds = (int)Math.Floor(fraction * 86400.0 + 1e-9) % 86400;
        hour = totalSeconds / 3600;
        minute = totalSeconds % 3600 / 60;
        second = totalSeconds % 60;
        return true;
    }

    private static DateTime OADateToDateTime(ScalarValue v) =>
        DateTime.FromOADate(ToNumber(v));

    private static ScalarValue Year(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (IsExcelFakeLeapDay(args[0])) return new NumberValue(1900);
        if (IsExcelZeroDate(args[0])) return new NumberValue(1900);
        return TryOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Year) : ErrorValue.Num;
    }

    private static ScalarValue Month(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (IsExcelFakeLeapDay(args[0])) return new NumberValue(2);
        if (IsExcelZeroDate(args[0])) return new NumberValue(1);
        return TryOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Month) : ErrorValue.Num;
    }

    private static ScalarValue Day(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (IsExcelFakeLeapDay(args[0])) return new NumberValue(29);
        if (IsExcelZeroDate(args[0])) return new NumberValue(0);
        return TryOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Day) : ErrorValue.Num;
    }

    private static ScalarValue Hour(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TryNonNegativeSerialToTimeParts(args[0], out var hour, out _, out _) ? new NumberValue(hour) : ErrorValue.Num;
    }

    private static ScalarValue Minute(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TryNonNegativeSerialToTimeParts(args[0], out _, out var minute, out _) ? new NumberValue(minute) : ErrorValue.Num;
    }

    private static ScalarValue Second(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TryNonNegativeSerialToTimeParts(args[0], out _, out _, out var second) ? new NumberValue(second) : ErrorValue.Num;
    }

    private static ScalarValue Weekday(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue returnTypeError) return returnTypeError;
        double rawSerial = ToNumber(args[0]);
        if (!double.IsFinite(rawSerial) || rawSerial < 0) return ErrorValue.Num;
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
            var adjustedStart = anchor > end ? anchor.AddYears(-1) : anchor;
            return new NumberValue(DateToSerial(end) - DateToSerial(adjustedStart));
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
            return new NumberValue(end.Day + DaysInExcelMonth(prevYear, prevMonth) - start.Day);
        }
        catch (ArgumentOutOfRangeException) { return ErrorValue.Num; }
    }

    private static int DaysInExcelMonth(int year, int month) =>
        year == 1900 && month == 2 ? 29 : DateTime.DaysInMonth(year, month);

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
        var range = args[0] is RangeValue rangeArg
            ? rangeArg
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
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
        var range = args[0] is RangeValue rangeArg
            ? rangeArg
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
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
        var range = args[1] is RangeValue rangeArg
            ? rangeArg
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
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
        var lookupArr = args[1] is RangeValue lookupRange
            ? lookupRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (args[2] is ErrorValue e2) return e2;
        var returnArr = args[2] is RangeValue returnRange
            ? returnRange
            : new RangeValue(new ScalarValue[1, 1] { { args[2] } });
        var lookupIsVertical = lookupArr.ColCount == 1;
        var lookupIsHorizontal = lookupArr.RowCount == 1;
        if (!lookupIsVertical && !lookupIsHorizontal) return ErrorValue.Value;
        if (lookupIsVertical && returnArr.RowCount != lookupArr.RowCount) return ErrorValue.Value;
        if (lookupIsHorizontal && returnArr.ColCount != lookupArr.ColCount) return ErrorValue.Value;

        var lookupValue = args[0];
        var lookupFlat = lookupArr.Flatten();

        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        ScalarValue ifNotFound = args.Count > 3 && args[3] is not BlankValue ? args[3] : ErrorValue.NA;
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
            int best = FindApproximateMatchIndex(lookupFlat, lookupValue, indices, nextSmaller: true);
            return best >= 0 ? XlookupReturnAt(returnArr, best, lookupIsVertical) : ifNotFound;
        }
        else
        {
            int best = FindApproximateMatchIndex(lookupFlat, lookupValue, indices, nextSmaller: false);
            return best >= 0 ? XlookupReturnAt(returnArr, best, lookupIsVertical) : ifNotFound;
        }
    }

    private static int FindApproximateMatchIndex(
        IReadOnlyList<ScalarValue> lookupFlat,
        ScalarValue lookupValue,
        IReadOnlyList<int> searchIndices,
        bool nextSmaller)
    {
        foreach (int i in searchIndices)
            if (ScalarEquals(lookupFlat[i], lookupValue))
                return i;

        int best = -1;
        foreach (int i in searchIndices)
        {
            int candidateVsLookup = CompareScalar(lookupFlat[i], lookupValue);
            if (nextSmaller)
            {
                if (candidateVsLookup > 0) continue;
                if (best < 0 || CompareScalar(lookupFlat[i], lookupFlat[best]) > 0)
                    best = i;
            }
            else
            {
                if (candidateVsLookup < 0) continue;
                if (best < 0 || CompareScalar(lookupFlat[i], lookupFlat[best]) < 0)
                    best = i;
            }
        }

        return best;
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
        return new NumberValue(CharToExcelAnsiCode(text[0]));
    }

    private static ScalarValue Char(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Value;
        int code = (int)n;
        if (code <= 0 || code > 255) return ErrorValue.Value;
        return new TextValue(ExcelAnsiCodeToChar(code).ToString());
    }

    private static ScalarValue Asc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TextResult(ConvertToHalfWidth(ToText(args[0])));
    }

    private static ScalarValue Dbcs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TextResult(ConvertToFullWidth(ToText(args[0])));
    }

    private static ScalarValue Phonetic(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var value = args[0] is RangeValue rv ? rv.At(1, 1) : args[0];
        return value is ErrorValue rangeError ? rangeError : TextResult(ToText(value));
    }

    private static ScalarValue BahtText(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;

        var value = ToNumber(args[0]);
        if (!double.IsFinite(value)) return ErrorValue.Value;

        var rounded = Math.Round(Math.Abs(value), 2, MidpointRounding.AwayFromZero);
        if (rounded > long.MaxValue) return ErrorValue.Num;

        long baht = (long)Math.Floor(rounded);
        int satang = (int)Math.Round((rounded - baht) * 100, MidpointRounding.AwayFromZero);
        if (satang == 100)
        {
            baht++;
            satang = 0;
        }

        var result = new StringBuilder();
        if (value < 0) result.Append("ลบ");
        result.Append(ThaiNumberToText(baht));
        result.Append("บาท");
        result.Append(satang == 0 ? "ถ้วน" : ThaiNumberToText(satang) + "สตางค์");
        return TextResult(result.ToString());
    }

    private static ScalarValue EncodeUrl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TextResult(Uri.EscapeDataString(ToText(args[0])));
    }

    private static ScalarValue FilterXml(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using var stringReader = new StringReader(ToText(args[0]));
            using var xmlReader = XmlReader.Create(stringReader, settings);
            var document = new XPathDocument(xmlReader);
            var navigator = document.CreateNavigator();
            var xpath = ToText(args[1]);
            var result = navigator.Evaluate(xpath);

            return result switch
            {
                XPathNodeIterator nodes => FilterXmlNodeResult(nodes),
                string s when s.Length > 0 => TextResult(s),
                double d when double.IsFinite(d) => TextResult(d.ToString(CultureInfo.InvariantCulture)),
                bool b => TextResult(b ? "TRUE" : "FALSE"),
                _ => ErrorValue.Value
            };
        }
        catch (XmlException)
        {
            return ErrorValue.Value;
        }
        catch (XPathException)
        {
            return ErrorValue.Value;
        }
        catch (ArgumentException)
        {
            return ErrorValue.Value;
        }
    }

    private static ScalarValue FilterXmlNodeResult(XPathNodeIterator nodes)
    {
        var values = new List<ScalarValue>();
        while (nodes.MoveNext())
        {
            values.Add(TextResult(nodes.Current?.Value ?? ""));
        }

        return values.Count switch
        {
            0 => ErrorValue.Value,
            1 => values[0],
            _ => new RangeValue(ToVerticalRange(values))
        };
    }

    private static ScalarValue[,] ToVerticalRange(IReadOnlyList<ScalarValue> values)
    {
        var cells = new ScalarValue[values.Count, 1];
        for (int i = 0; i < values.Count; i++)
            cells[i, 0] = values[i];
        return cells;
    }

    private static string ConvertToHalfWidth(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == '\u3000')
            {
                sb.Append(' ');
            }
            else if (ch is >= '\uFF01' and <= '\uFF5E')
            {
                sb.Append((char)(ch - 0xFEE0));
            }
            else if (FullWidthKanaToHalfWidth.TryGetValue(ch, out var kana))
            {
                sb.Append(kana);
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static string ConvertToFullWidth(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == ' ')
            {
                sb.Append('\u3000');
            }
            else if (ch is >= '!' and <= '~')
            {
                sb.Append((char)(ch + 0xFEE0));
            }
            else if (i + 1 < text.Length &&
                     (text[i + 1] == '\uFF9E' || text[i + 1] == '\uFF9F') &&
                     HalfWidthKanaToFullWidth.TryGetValue(text.Substring(i, 2), out var combinedKana))
            {
                sb.Append(combinedKana);
                i++;
            }
            else if (HalfWidthKanaToFullWidth.TryGetValue(ch.ToString(), out var kana))
            {
                sb.Append(kana);
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static string ThaiNumberToText(long value)
    {
        if (value == 0) return "ศูนย์";
        if (value >= 1_000_000)
        {
            var high = value / 1_000_000;
            var low = value % 1_000_000;
            return ThaiNumberToText(high) + "ล้าน" + (low == 0 ? "" : ThaiNumberUnderMillionToText((int)low));
        }

        return ThaiNumberUnderMillionToText((int)value);
    }

    private static string ThaiNumberUnderMillionToText(int value)
    {
        string[] digits = ["", "หนึ่ง", "สอง", "สาม", "สี่", "ห้า", "หก", "เจ็ด", "แปด", "เก้า"];
        string[] positions = ["", "สิบ", "ร้อย", "พัน", "หมื่น", "แสน"];
        var chars = value.ToString(CultureInfo.InvariantCulture).Reverse().ToArray();
        var sb = new StringBuilder();

        for (int pos = chars.Length - 1; pos >= 0; pos--)
        {
            int digit = chars[pos] - '0';
            if (digit == 0) continue;

            if (pos == 1)
            {
                sb.Append(digit switch
                {
                    1 => "สิบ",
                    2 => "ยี่สิบ",
                    _ => digits[digit] + "สิบ"
                });
            }
            else if (pos == 0 && digit == 1 && value > 10)
            {
                sb.Append("เอ็ด");
            }
            else
            {
                sb.Append(digits[digit]);
                sb.Append(positions[pos]);
            }
        }

        return sb.ToString();
    }

    private static readonly Dictionary<char, string> FullWidthKanaToHalfWidth = new()
    {
        ['。'] = "｡", ['「'] = "｢", ['」'] = "｣", ['、'] = "､", ['・'] = "･",
        ['ヲ'] = "ｦ", ['ァ'] = "ｧ", ['ィ'] = "ｨ", ['ゥ'] = "ｩ", ['ェ'] = "ｪ", ['ォ'] = "ｫ",
        ['ャ'] = "ｬ", ['ュ'] = "ｭ", ['ョ'] = "ｮ", ['ッ'] = "ｯ", ['ー'] = "ｰ",
        ['ア'] = "ｱ", ['イ'] = "ｲ", ['ウ'] = "ｳ", ['エ'] = "ｴ", ['オ'] = "ｵ",
        ['カ'] = "ｶ", ['キ'] = "ｷ", ['ク'] = "ｸ", ['ケ'] = "ｹ", ['コ'] = "ｺ",
        ['サ'] = "ｻ", ['シ'] = "ｼ", ['ス'] = "ｽ", ['セ'] = "ｾ", ['ソ'] = "ｿ",
        ['タ'] = "ﾀ", ['チ'] = "ﾁ", ['ツ'] = "ﾂ", ['テ'] = "ﾃ", ['ト'] = "ﾄ",
        ['ナ'] = "ﾅ", ['ニ'] = "ﾆ", ['ヌ'] = "ﾇ", ['ネ'] = "ﾈ", ['ノ'] = "ﾉ",
        ['ハ'] = "ﾊ", ['ヒ'] = "ﾋ", ['フ'] = "ﾌ", ['ヘ'] = "ﾍ", ['ホ'] = "ﾎ",
        ['マ'] = "ﾏ", ['ミ'] = "ﾐ", ['ム'] = "ﾑ", ['メ'] = "ﾒ", ['モ'] = "ﾓ",
        ['ヤ'] = "ﾔ", ['ユ'] = "ﾕ", ['ヨ'] = "ﾖ",
        ['ラ'] = "ﾗ", ['リ'] = "ﾘ", ['ル'] = "ﾙ", ['レ'] = "ﾚ", ['ロ'] = "ﾛ",
        ['ワ'] = "ﾜ", ['ン'] = "ﾝ", ['゛'] = "ﾞ", ['゜'] = "ﾟ",
        ['ガ'] = "ｶﾞ", ['ギ'] = "ｷﾞ", ['グ'] = "ｸﾞ", ['ゲ'] = "ｹﾞ", ['ゴ'] = "ｺﾞ",
        ['ザ'] = "ｻﾞ", ['ジ'] = "ｼﾞ", ['ズ'] = "ｽﾞ", ['ゼ'] = "ｾﾞ", ['ゾ'] = "ｿﾞ",
        ['ダ'] = "ﾀﾞ", ['ヂ'] = "ﾁﾞ", ['ヅ'] = "ﾂﾞ", ['デ'] = "ﾃﾞ", ['ド'] = "ﾄﾞ",
        ['バ'] = "ﾊﾞ", ['ビ'] = "ﾋﾞ", ['ブ'] = "ﾌﾞ", ['ベ'] = "ﾍﾞ", ['ボ'] = "ﾎﾞ",
        ['パ'] = "ﾊﾟ", ['ピ'] = "ﾋﾟ", ['プ'] = "ﾌﾟ", ['ペ'] = "ﾍﾟ", ['ポ'] = "ﾎﾟ",
        ['ヴ'] = "ｳﾞ"
    };

    private static readonly Dictionary<string, string> HalfWidthKanaToFullWidth =
        FullWidthKanaToHalfWidth.ToDictionary(pair => pair.Value, pair => pair.Key.ToString(), StringComparer.Ordinal);

    private static char ExcelAnsiCodeToChar(int code) => code switch
    {
        128 => '\u20AC',
        130 => '\u201A',
        131 => '\u0192',
        132 => '\u201E',
        133 => '\u2026',
        134 => '\u2020',
        135 => '\u2021',
        136 => '\u02C6',
        137 => '\u2030',
        138 => '\u0160',
        139 => '\u2039',
        140 => '\u0152',
        142 => '\u017D',
        145 => '\u2018',
        146 => '\u2019',
        147 => '\u201C',
        148 => '\u201D',
        149 => '\u2022',
        150 => '\u2013',
        151 => '\u2014',
        152 => '\u02DC',
        153 => '\u2122',
        154 => '\u0161',
        155 => '\u203A',
        156 => '\u0153',
        158 => '\u017E',
        159 => '\u0178',
        _ => (char)code
    };

    private static int CharToExcelAnsiCode(char ch) => ch switch
    {
        '\u20AC' => 128,
        '\u201A' => 130,
        '\u0192' => 131,
        '\u201E' => 132,
        '\u2026' => 133,
        '\u2020' => 134,
        '\u2021' => 135,
        '\u02C6' => 136,
        '\u2030' => 137,
        '\u0160' => 138,
        '\u2039' => 139,
        '\u0152' => 140,
        '\u017D' => 142,
        '\u2018' => 145,
        '\u2019' => 146,
        '\u201C' => 147,
        '\u201D' => 148,
        '\u2022' => 149,
        '\u2013' => 150,
        '\u2014' => 151,
        '\u02DC' => 152,
        '\u2122' => 153,
        '\u0161' => 154,
        '\u203A' => 155,
        '\u0153' => 156,
        '\u017E' => 158,
        '\u0178' => 159,
        _ => ch
    };

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
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (digits > 15) return new NumberValue(n);
        double factor = Math.Pow(10, digits);
        if (factor == 0) return new NumberValue(0);
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
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (digits > 15) return new NumberValue(n);
        double factor = Math.Pow(10, digits);
        if (factor == 0) return new NumberValue(0);
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
        }
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (digits > 15) return new NumberValue(n);
        double factor = Math.Pow(10, digits);
        if (factor == 0) return new NumberValue(0);
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
        if (TryParseExcelFakeLeapDayValueText(text, CultureInfo.InvariantCulture, out _)) return new NumberValue(60);
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return new NumberValue(Math.Floor(DateToSerial(dt)));
        return ErrorValue.Value;
    }

    private static bool TryParseExcelFakeLeapDayValueText(string text, CultureInfo culture, out double serial)
    {
        serial = 0;
        var trimmed = text.Trim();
        var match = Regex.Match(trimmed, @"^(?:2/29/1900|02/29/1900|1900-02-29)(?:\s+(.+))?$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;

        serial = 60;
        if (match.Groups[1].Success)
        {
            if (!DateTime.TryParse(match.Groups[1].Value, culture, DateTimeStyles.None, out var time))
                return false;
            serial += time.TimeOfDay.TotalDays;
        }

        return true;
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
        if (Math.Floor(ToNumber(args[0])) == 0)
            return new NumberValue(0);
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
        var rv = args[0] is RangeValue range
            ? range
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
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
        var rv = args[0] is RangeValue range
            ? range
            : SingleCellArray(args[0]);
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
        var rv = args[0] is RangeValue range
            ? range
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
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
        var rv = args[0] is RangeValue range
            ? range
            : SingleCellArray(args[0]);
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
            pctRank = n == 1 ? 1.0 : (double)below / (n - 1);
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
        var rv1 = args[0] is RangeValue range1
            ? range1
            : SingleCellArray(args[0]);
        if (args[1] is ErrorValue e1) return e1;
        var rv2 = args[1] is RangeValue range2
            ? range2
            : SingleCellArray(args[1]);
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
        var knownY = args[1] is RangeValue knownYRange
            ? knownYRange
            : SingleCellArray(args[1]);
        if (args[2] is ErrorValue e2) return e2;
        var knownX = args[2] is RangeValue knownXRange
            ? knownXRange
            : SingleCellArray(args[2]);
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
        var valRange = args[0] is RangeValue valuesRange
            ? valuesRange
            : SingleCellArray(args[0]);
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

        double rounded = decimals <= 15 ? RoundWithExcelDigits(value, decimals) : value;
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
        var lookupVec = args[1] is RangeValue lookupRange
            ? lookupRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        if (args.Count == 2 && lookupVec.RowCount > 1 && lookupVec.ColCount > 1)
            return LookupArrayForm(args[0], lookupVec);

        var lookupFlat = lookupVec.Flatten();
        var resultFlat = args.Count > 2
            ? (args[2] is RangeValue rv
                ? rv.Flatten()
                : new[] { args[2] })
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

    private static ScalarValue LookupArrayForm(ScalarValue lookupVal, RangeValue array)
    {
        bool searchFirstRow = array.ColCount > array.RowCount;
        var lookupVector = searchFirstRow ? array.GetRow(1) : array.GetColumn(1);
        var resultVector = searchFirstRow ? array.GetRow(array.RowCount) : array.GetColumn(array.ColCount);

        int matchIdx = -1;
        for (int i = 0; i < lookupVector.Count; i++)
        {
            if (lookupVector[i] is ErrorValue lErr) return lErr;
            if (CompareScalar(lookupVector[i], lookupVal) <= 0)
                matchIdx = i;
        }

        if (matchIdx < 0) return ErrorValue.NA;
        return matchIdx < resultVector.Count ? resultVector[matchIdx] : ErrorValue.NA;
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




    // ════════════════════════════════════════════════════════════════════════
    // Phase A1 – Math: SQRTPI, MULTINOMIAL, SERIESSUM
    // ════════════════════════════════════════════════════════════════════════




    // ════════════════════════════════════════════════════════════════════════
    // Phase A1 – Matrix: MMULT, MINVERSE, MDETERM
    // ════════════════════════════════════════════════════════════════════════





    // ════════════════════════════════════════════════════════════════════════
    // Phase A1 – Date (weekend mask): NETWORKDAYS.INTL, WORKDAY.INTL
    // ════════════════════════════════════════════════════════════════════════


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



    // ════════════════════════════════════════════════════════════════════════
    // Phase A1 – Lookup: TRANSPOSE
    // ════════════════════════════════════════════════════════════════════════


    // ════════════════════════════════════════════════════════════════════════
    // Phase A1 – Information: TYPE, ERROR.TYPE
    // ════════════════════════════════════════════════════════════════════════



    // ════════════════════════════════════════════════════════════════════════
    // Phase A2 – AST-aware stub
    // ════════════════════════════════════════════════════════════════════════

    // ════════════════════════════════════════════════════════════════════════
    // Phase A2 – INFO(type_text)
    // ════════════════════════════════════════════════════════════════════════


    // ════════════════════════════════════════════════════════════════════════
    // Phase A2 – AGGREGATE(function_num, options, array/ref, [k])
    // ════════════════════════════════════════════════════════════════════════




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

    /// <summary>Returns the formula cell address, or null when evaluating without a host cell.</summary>
    Model.CellAddress? CurrentCellAddress => null;

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
