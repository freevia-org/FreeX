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
        if (args[0] is RangeValue range)
        {
            var cells = new ScalarValue[range.RowCount, range.ColCount];
            for (int r = 0; r < range.RowCount; r++)
                for (int c = 0; c < range.ColCount; c++)
                {
                    var value = range.Cells[r, c];
                    if (value is ErrorValue e) return e;
                    cells[r, c] = LenScalar(value);
                }

            return new RangeValue(cells);
        }

        return LenScalar(args[0]);
    }

    private static ScalarValue LenScalar(ScalarValue value)
    {
        var text = ToText(value);
        return new NumberValue(ContainsSurrogatePair(text) ? CountTextElements(text) : text.Length);
    }

    private static ScalarValue Left(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        var text  = ToText(args[0]);
        var rawCount = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 1;
        if (!double.IsFinite(rawCount) || rawCount > int.MaxValue) return ErrorValue.Value;
        var count = (int)rawCount;
        if (count < 0) return ErrorValue.Value;
        count = Math.Min(count, text.Length);
        if (ContainsSurrogatePair(text))
            return TextResult(text[..AdvanceTextElements(text, 0, count)]);
        return TextResult(text[..count]);
    }

    private static ScalarValue Right(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        var text  = ToText(args[0]);
        var rawCount = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 1;
        if (!double.IsFinite(rawCount) || rawCount > int.MaxValue) return ErrorValue.Value;
        var count = (int)rawCount;
        if (count < 0) return ErrorValue.Value;
        count = Math.Min(count, text.Length);
        int start = ContainsSurrogatePair(text)
            ? AdvanceTextElements(text, 0, Math.Max(0, CountTextElements(text) - count))
            : text.Length - count;
        return TextResult(text[start..]);
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





    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Conditional aggregation
    // ═══════════════════════════════════════════════════════════════════

    private static bool MatchExactValue(ScalarValue candidate, ScalarValue lookupValue)
    {
        if (lookupValue is TextValue pattern && candidate is TextValue text)
            return WildcardMatch(text.Value, pattern.Value, ignoreCase: true);

        return ScalarEquals(candidate, lookupValue);
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
    private const string RegexTextElement = @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\uD800-\uDFFF])";

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
                case '*': sb.Append(RegexTextElement).Append('*'); break;
                case '?': sb.Append(RegexTextElement); break;
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


























    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Date & time
    // ═══════════════════════════════════════════════════════════════════





    private static DateTime OADateToDateTime(ScalarValue v) =>
        DateTime.FromOADate(ToNumber(v));












    // DATEDIF helpers that can throw ArgumentOutOfRangeException when start is a
    // leap day (Feb 29) and end.Year is not a leap year — catch → #NUM!



    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Math
    // ═══════════════════════════════════════════════════════════════════














    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Statistical
    // ═══════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Multi-criteria aggregation
    // ═══════════════════════════════════════════════════════════════════

    // SUMIFS(sum_range, criteria_range1, criteria1, [criteria_range2, criteria2, ...])

    // COUNTIFS(criteria_range1, criteria1, ...)

    // AVERAGEIFS(avg_range, criteria_range1, criteria1, ...)

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Modern lookup: XLOOKUP
    // ═══════════════════════════════════════════════════════════════════

    // XLOOKUP(lookup_value, lookup_array, return_array, [if_not_found], [match_mode], [search_mode])
    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Multi-condition logic: IFS, SWITCH
    // ═══════════════════════════════════════════════════════════════════

    // IFS(condition1, value1, [condition2, value2, ...])
    // SWITCH(expr, val1, result1, [val2, result2, ...], [default])
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





















    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Count: COUNTBLANK
    // ═══════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Misc: CHOOSE, SUMPRODUCT, ROUNDDOWN, ROUNDUP, TRUNC
    // ═══════════════════════════════════════════════════════════════════






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







    // ATAN2(x_num, y_num) – matches Excel argument order (x first, then y)













    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Date / Time
    // ═══════════════════════════════════════════════════════════════════


















    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Statistical
    // ═══════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Financial
    // ═══════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Logical / Text
    // ═══════════════════════════════════════════════════════════════════

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

        bool hasSurrogatePair = ContainsSurrogatePair(text);
        int start = hasSurrogatePair
            ? TextElementIndexFromOneBasedPosition(text, startNum)
            : Math.Min(startNum - 1, text.Length);
        var newText = ToText(args[3]);
        int end = hasSurrogatePair
            ? AdvanceTextElements(text, start, numChars)
            : Math.Min(start + numChars, text.Length);
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

        var display = args.Count > 1 && args[1] is not BlankValue ? ToText(args[1]) : ToText(args[0]);
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
        if (args.Count > 1 && args[1] is BlankValue)
        {
            dec = 0;
        }
        else if (args.Count > 1)
        {
            double rawDec = ToNumber(args[1]);
            if (!double.IsFinite(rawDec) || rawDec > int.MaxValue || rawDec < int.MinValue) return ErrorValue.Num;
            dec = (int)rawDec;
        }
        var numberText = FormatRoundedNumber(Math.Abs(n), dec, useCommas: true);
        var formatted = "$" + numberText;
        return TextResult(n < 0 && (dec >= 0 || numberText != "0") ? "(" + formatted + ")" : formatted);
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


    private static int ExcelDowToMonIndex(int serial) => ((serial + 5) % 7 + 7) % 7;





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




