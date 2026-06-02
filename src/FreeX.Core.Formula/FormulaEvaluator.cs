using FreeX.Core.Model;

namespace FreeX.Core.Formula;

/// <summary>
/// Evaluates a formula AST against a worksheet to produce a ScalarValue.
/// This is the heart of the formula engine.
/// </summary>
public sealed class FormulaEvaluator
{
    /// <summary>
    /// Maximum recursive evaluation depth before returning #NUM!.
    /// A single formula can nest at most this many EvaluateNode calls deep before we
    /// cut off. This prevents deeply-nested or circular-looking formulas from
    /// causing a StackOverflowException that would crash the process.
    /// Trade-off: extremely pathological nesting (>256 levels) returns #NUM!
    /// rather than the "correct" result, but such formulas don't arise in practice.
    /// </summary>
    private const int MaxEvalDepth = 256;

    /// <summary>
    /// Per-thread evaluation depth counter. ThreadStatic avoids the need to thread
    /// the counter through every EvaluateNode call or add it to IEvalContext
    /// (which has many implementations). Reset to 0 at each public Evaluate() entry.
    /// </summary>
    [ThreadStatic]
    private static int _evalDepth;

    private static readonly object ParsedFormulaCacheGate = new();
    private static readonly Dictionary<string, FormulaNode> ParsedFormulaCache = new(StringComparer.Ordinal);
    private static readonly Queue<string> ParsedFormulaCacheOrder = new();
    private static readonly BoolValue TrueValue = new(true);
    private static readonly BoolValue FalseValue = new(false);

    private ParsedFormulaEntry? _lastParsedFormula;
    private SheetEvalContext? _singleSheetEvalContext;

    private sealed record ParsedFormulaEntry(string FormulaText, FormulaNode Node);

    private static readonly HashSet<string> AggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "AVERAGE", "AVERAGEA", "MIN", "MINA", "MAX", "MAXA", "COUNT", "COUNTA", "AND", "OR", "CONCAT",
        "STDEV", "MEDIAN",
        "PRODUCT", "SUMSQ", "SUMX2MY2", "SUMX2PY2", "SUMXMY2", "XOR", "GCD", "LCM",
        "VAR", "VAR.S", "VARA", "VARP", "VAR.P", "VARPA", "STDEVP", "STDEV.P", "STDEVA", "STDEVPA",
        "GEOMEAN", "HARMEAN", "AVEDEV",
        "MODE", "MODE.SNGL",
        "CONCATENATE",
        "NPV"
    };

    private static readonly HashSet<string> DirectTextCoercingAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "AVERAGE", "AVERAGEA", "MIN", "MINA", "MAX", "MAXA", "COUNT", "PRODUCT", "SUMSQ", "SUMX2MY2", "SUMX2PY2", "SUMXMY2",
        "STDEV", "STDEV.S", "STDEVP", "STDEV.P", "STDEVA", "STDEVPA",
        "VAR", "VAR.S", "VAR.P", "VARP", "VARA", "VARPA",
        "MEDIAN",
        "GEOMEAN", "HARMEAN", "AVEDEV",
        "COVAR", "COVARIANCE.P", "COVARIANCE.S", "INTERCEPT", "PEARSON", "RSQ", "SLOPE", "STEYX",
        "MODE", "MODE.SNGL",
        "NPV",
        "GCD", "LCM"
    };

    private static readonly HashSet<string> ReferenceProvenanceAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "AVERAGE", "AVERAGEA", "MIN", "MINA", "MAX", "MAXA", "COUNT", "PRODUCT", "SUMSQ", "SUMX2MY2", "SUMX2PY2", "SUMXMY2", "AND", "OR", "XOR",
        "STDEV", "STDEV.S", "STDEVP", "STDEV.P", "STDEVA", "STDEVPA",
        "VAR", "VAR.S", "VAR.P", "VARP", "VARA", "VARPA",
        "MEDIAN",
        "GEOMEAN", "HARMEAN", "AVEDEV",
        "COVAR", "COVARIANCE.P", "COVARIANCE.S", "INTERCEPT", "PEARSON", "RSQ", "SLOPE", "STEYX",
        "MODE", "MODE.SNGL",
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
        "SUMIFS", "COUNTIFS", "AVERAGEIFS",
        "XLOOKUP",
        "WORKDAY", "NETWORKDAYS", "WORKDAY.INTL", "NETWORKDAYS.INTL",
        "CORREL", "COVAR", "COVARIANCE.P", "COVARIANCE.S",
        "FORECAST", "FORECAST.LINEAR",
        "INTERCEPT", "PEARSON", "RSQ", "SLOPE", "STEYX",
        "PERCENTILE", "PERCENTILE.INC", "PERCENTILE.EXC",
        "QUARTILE", "QUARTILE.INC", "QUARTILE.EXC",
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
        "ISBLANK", "ISNUMBER", "ISTEXT", "ISERROR", "ISERR", "ISNA", "ISNONTEXT", "ISLOGICAL",
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
        "CONVERT",
        "NORMDIST", "NORM.DIST", "NORMINV", "NORM.INV", "NORMSDIST", "NORM.S.DIST", "NORMSINV", "NORM.S.INV", "PHI", "GAUSS", "FISHER", "FISHERINV", "STANDARDIZE",
        "GAMMA", "GAMMALN", "GAMMALN.PRECISE", "GAMMADIST", "GAMMA.DIST", "GAMMAINV", "GAMMA.INV",
        "LOGNORM.DIST", "LOGNORMDIST", "LOGNORM.INV", "LOGINV",
        "BETA.DIST", "BETADIST", "BETA.INV", "BETAINV",
        "EXPONDIST", "EXPON.DIST", "WEIBULL", "WEIBULL.DIST", "POISSON", "POISSON.DIST",
        "TDIST", "T.DIST", "T.DIST.RT", "T.DIST.2T", "TINV", "T.INV", "T.INV.2T",
        "FDIST", "F.DIST", "F.DIST.RT", "FINV", "F.INV", "F.INV.RT",
        "CHIDIST", "CHISQ.DIST", "CHISQ.DIST.RT", "CHIINV", "CHISQ.INV", "CHISQ.INV.RT",
        "BINOMDIST", "BINOM.DIST", "BINOM.DIST.RANGE", "CRITBINOM", "BINOM.INV", "NEGBINOMDIST", "NEGBINOM.DIST", "HYPGEOMDIST", "HYPERGEOM.DIST",
        "CONFIDENCE", "CONFIDENCE.NORM", "CONFIDENCE.T"
    };

    private static readonly HashSet<string> SingleCellReferenceRangeFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ROW", "COLUMN", "ROWS", "COLUMNS", "AREAS", "SHEET", "SHEETS", "COUNTBLANK", "CELL", "GETPIVOTDATA"
    };

    /// <summary>
    /// Parse and evaluate a formula string against a sheet.
    /// </summary>
    public ScalarValue Evaluate(
        string formulaText,
        Sheet sheet,
        FreeX.Core.Model.Workbook? workbook = null,
        FreeX.Core.Model.CellAddress? currentCell = null)
    {
        try
        {
            _evalDepth = 0;
            var ast = GetOrParseFormulaForInstance(formulaText);
            var context = workbook is null && currentCell is null
                ? GetSingleSheetEvalContext(sheet)
                : new SheetEvalContext(sheet, workbook, this, currentCell);
            return NormalizeTopLevelResult(EvaluateNode(ast, context));
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
    }

    /// <summary>
    /// Evaluate a pre-parsed AST against a sheet.
    /// </summary>
    public ScalarValue Evaluate(
        FormulaNode ast,
        Sheet sheet,
        FreeX.Core.Model.Workbook? workbook = null,
        FreeX.Core.Model.CellAddress? currentCell = null)
    {
        try
        {
            _evalDepth = 0;
            var context = workbook is null && currentCell is null
                ? GetSingleSheetEvalContext(sheet)
                : new SheetEvalContext(sheet, workbook, this, currentCell);
            return NormalizeTopLevelResult(EvaluateNode(ast, context));
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
    }

    private FormulaNode GetOrParseFormulaForInstance(string formulaText)
    {
        var last = _lastParsedFormula;
        if (last is not null && string.Equals(last.FormulaText, formulaText, StringComparison.Ordinal))
            return last.Node;

        var parsed = GetOrParseFormula(formulaText);
        _lastParsedFormula = new ParsedFormulaEntry(formulaText, parsed);
        return parsed;
    }

    private SheetEvalContext GetSingleSheetEvalContext(Sheet sheet)
    {
        var cached = _singleSheetEvalContext;
        if (cached is not null && ReferenceEquals(cached.SourceSheet, sheet))
            return cached;

        cached = new SheetEvalContext(sheet, null, this, null);
        _singleSheetEvalContext = cached;
        return cached;
    }

    private static ScalarValue NormalizeTopLevelResult(ScalarValue value) =>
        value is LambdaValue ? ErrorValue.Calc : value;

    /// <summary>
    /// Parse a formula string using the shared text-to-AST cache. The returned AST is shared and should be treated as immutable.
    /// </summary>
    public static FormulaNode ParseFormula(string formulaText) =>
        GetOrParseFormula(formulaText);

    private static FormulaNode GetOrParseFormula(string formulaText)
    {
        formulaText = NormalizeFormulaCacheKey(formulaText);

        lock (ParsedFormulaCacheGate)
        {
            if (ParsedFormulaCache.TryGetValue(formulaText, out var cached))
                return cached;
        }

        var parsed = ParseFormulaUncached(formulaText);

        lock (ParsedFormulaCacheGate)
        {
            if (ParsedFormulaCache.TryGetValue(formulaText, out var cached))
                return cached;

            if (ParsedFormulaCache.Count >= FormulaSafetyLimits.MaxParsedFormulaCacheEntries &&
                ParsedFormulaCacheOrder.TryDequeue(out var oldest))
            {
                ParsedFormulaCache.Remove(oldest);
            }

            ParsedFormulaCache[formulaText] = parsed;
            ParsedFormulaCacheOrder.Enqueue(formulaText);
        }

        return parsed;
    }

    private static FormulaNode ParseFormulaUncached(string formulaText)
    {
        var lexer = new Lexer(formulaText);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    private static string NormalizeFormulaCacheKey(string formulaText) =>
        formulaText is { Length: > 0 } && formulaText[0] == '='
            ? formulaText[1..]
            : formulaText;

    /// <summary>
    /// Evaluate an AST node recursively.
    /// </summary>
    internal ScalarValue EvaluateNode(FormulaNode node, IEvalContext context)
    {
        if (_evalDepth >= MaxEvalDepth)
            return ErrorValue.Num;

        _evalDepth++;
        try
        {
            return node switch
            {
                NumberNode n => new NumberValue(n.Value),
                StringNode s => new TextValue(s.Value),
                BooleanNode b => b.Value ? TrueValue : FalseValue,
                OmittedArgumentNode => BlankValue.Instance,
                ArrayConstantNode array => EvaluateArrayConstant(array, context),
                ErrorNode err => err.Error,
                CellRefNode cell when cell.SheetName is not null
                    => context.GetCellValue(cell.SheetName, cell.Row, cell.ColumnNumber),
                CellRefNode cell => context.GetCellValue(cell.Row, cell.ColumnNumber),
                RangeRefNode range => EvaluateRange(range, context),
                FullColumnRangeRefNode range => EvaluateRange(ToRangeRef(range), context),
                FullRowRangeRefNode range => EvaluateRange(ToRangeRef(range), context),
                NamedRangeNode named => EvaluateNamedRange(named, context),
                StructuredReferenceNode structured => EvaluateStructuredReference(structured, context),
                StructuredCurrentRowReferenceNode currentRow => EvaluateCurrentRowReference(currentRow, context),
                BinaryOpNode binary => EvaluateBinaryOp(binary, context),
                UnaryOpNode unary => EvaluateUnaryOp(unary, context),
                FunctionCallNode func => EvaluateFunction(func, context),
                _ => throw new FormulaEvalException("#VALUE!", $"Unknown node type: {node.GetType().Name}")
            };
        }
        finally
        {
            _evalDepth--;
        }
    }

    private ScalarValue EvaluateArrayConstant(ArrayConstantNode node, IEvalContext context)
    {
        int rowCount = node.Rows.Count;
        int colCount = node.Rows[0].Count;
        var cells = new ScalarValue[rowCount, colCount];

        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
                cells[r, c] = EvaluateNode(node.Rows[r][c], context);

        return new RangeValue(cells);
    }

    private static ScalarValue EvaluateNamedRange(NamedRangeNode node, IEvalContext context)
    {
        // Local LET/LAMBDA bindings shadow workbook named ranges.
        var binding = context.TryResolveLambdaBinding(node.Name);
        if (binding is not null) return binding;

        var range = context.TryResolveNamedRange(node.Name);
        if (range is null)
            return ErrorValue.Name;

        // Bare named range reference outside a function: return top-left cell value.
        // For 2D named ranges this is intentionally lossy — full implicit-intersection
        // semantics (Excel 365 spill behaviour) are a Phase 5 enhancement.
        return BuildRangeValueOrError(range.Value, context);
    }

    private static ScalarValue EvaluateRange(RangeRefNode range, IEvalContext context)
    {
        // A bare range reference outside a function context returns the first value
        // (This matches Excel's implicit intersection behavior for simple cases)
        return range.SheetName is not null
            ? context.GetCellValue(range.SheetName, range.Start.Row, range.Start.ColumnNumber)
            : context.GetCellValue(range.Start.Row, range.Start.ColumnNumber);
    }

    private ScalarValue EvaluateBinaryOp(BinaryOpNode node, IEvalContext context)
    {
        if (IsArithmeticOperator(node.Operator) &&
            TryEvaluateNumericScalar(node, context, out var numericResult, out var numericError) != NumericScalarEvaluationState.Unsupported)
        {
            return numericError is not null ? numericError : new NumberValue(numericResult);
        }

        var left = EvaluateArrayOperand(node.Left, context);
        var right = EvaluateArrayOperand(node.Right, context);

        // Propagate errors
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;

        return node.Operator switch
        {
            BinaryOperator.Add => ArithOp(left, right, ArithmeticKind.Add),
            BinaryOperator.Subtract => ArithOp(left, right, ArithmeticKind.Subtract),
            BinaryOperator.Multiply => ArithOp(left, right, ArithmeticKind.Multiply),
            BinaryOperator.Divide => DivideOp(left, right),
            BinaryOperator.Power => PowerOp(left, right),
            BinaryOperator.Concatenate => ConcatOp(left, right),
            BinaryOperator.Equal => CompareOpEqual(left, right),
            BinaryOperator.NotEqual => CompareOpNotEqual(left, right),
            BinaryOperator.LessThan => CompareOpLessThan(left, right),
            BinaryOperator.GreaterThan => CompareOpGreaterThan(left, right),
            BinaryOperator.LessOrEqual => CompareOpLessOrEqual(left, right),
            BinaryOperator.GreaterOrEqual => CompareOpGreaterOrEqual(left, right),
            _ => throw new FormulaEvalException("#VALUE!", $"Unknown operator: {node.Operator}")
        };
    }

    private static bool IsArithmeticOperator(BinaryOperator op) =>
        op is BinaryOperator.Add
            or BinaryOperator.Subtract
            or BinaryOperator.Multiply
            or BinaryOperator.Divide
            or BinaryOperator.Power;

    private static NumericScalarEvaluationState TryEvaluateNumericScalar(
        FormulaNode node,
        IEvalContext context,
        out double value,
        out ErrorValue? error)
    {
        value = 0;
        error = null;

        switch (node)
        {
            case NumberNode number:
                value = number.Value;
                return NumericScalarEvaluationState.Value;
            case BooleanNode boolean:
                value = boolean.Value ? 1 : 0;
                return NumericScalarEvaluationState.Value;
            case StringNode text:
                if (ExcelTextNumberParser.TryParse(text.Value, out value))
                    return NumericScalarEvaluationState.Value;

                return NumericScalarEvaluationState.Unsupported;
            case ErrorNode errorNode:
                error = errorNode.Error;
                return NumericScalarEvaluationState.Error;
            case CellRefNode cell:
                return TryGetNumericCellValue(cell, context, out value, out error);
            case UnaryOpNode unary:
                return TryEvaluateNumericUnaryScalar(unary, context, out value, out error);
            case BinaryOpNode binary when IsArithmeticOperator(binary.Operator):
                return TryEvaluateNumericBinaryScalar(binary, context, out value, out error);
            default:
                return NumericScalarEvaluationState.Unsupported;
        }
    }

    private static NumericScalarEvaluationState TryGetNumericCellValue(
        CellRefNode cell,
        IEvalContext context,
        out double value,
        out ErrorValue? error)
    {
        var scalar = cell.SheetName is not null
            ? context.GetCellValue(cell.SheetName, cell.Row, cell.ColumnNumber)
            : context.GetCellValue(cell.Row, cell.ColumnNumber);

        if (scalar is ErrorValue cellError)
        {
            value = 0;
            error = cellError;
            return NumericScalarEvaluationState.Error;
        }

        if (TryCoerceToNumberValue(scalar, out value))
        {
            error = null;
            return NumericScalarEvaluationState.Value;
        }

        error = null;
        return NumericScalarEvaluationState.Unsupported;
    }

    private static NumericScalarEvaluationState TryEvaluateNumericUnaryScalar(
        UnaryOpNode node,
        IEvalContext context,
        out double value,
        out ErrorValue? error)
    {
        var operandState = TryEvaluateNumericScalar(node.Operand, context, out value, out error);
        if (operandState != NumericScalarEvaluationState.Value)
            return operandState;

        switch (node.Operator)
        {
            case UnaryOperator.Negate:
                value = -value;
                return NumericScalarEvaluationState.Value;
            case UnaryOperator.Percent:
                value /= 100.0;
                return NumericScalarEvaluationState.Value;
            default:
                return NumericScalarEvaluationState.Unsupported;
        }
    }

    private static NumericScalarEvaluationState TryEvaluateNumericBinaryScalar(
        BinaryOpNode node,
        IEvalContext context,
        out double value,
        out ErrorValue? error)
    {
        value = 0;
        var leftState = TryEvaluateNumericScalar(node.Left, context, out var left, out var leftError);
        var rightState = TryEvaluateNumericScalar(node.Right, context, out var right, out var rightError);

        if (leftState == NumericScalarEvaluationState.Unsupported ||
            rightState == NumericScalarEvaluationState.Unsupported)
        {
            error = null;
            return NumericScalarEvaluationState.Unsupported;
        }

        if (leftState == NumericScalarEvaluationState.Error)
        {
            error = leftError;
            return NumericScalarEvaluationState.Error;
        }

        if (rightState == NumericScalarEvaluationState.Error)
        {
            error = rightError;
            return NumericScalarEvaluationState.Error;
        }

        if (node.Operator == BinaryOperator.Divide && right == 0)
        {
            error = ErrorValue.DivByZero;
            return NumericScalarEvaluationState.Error;
        }

        if (node.Operator == BinaryOperator.Power && left == 0 && right <= 0)
        {
            error = right == 0 ? ErrorValue.Num : ErrorValue.DivByZero;
            return NumericScalarEvaluationState.Error;
        }

        value = node.Operator switch
        {
            BinaryOperator.Add => left + right,
            BinaryOperator.Subtract => left - right,
            BinaryOperator.Multiply => left * right,
            BinaryOperator.Divide => left / right,
            BinaryOperator.Power => Math.Pow(left, right),
            _ => 0
        };

        if (double.IsFinite(value))
        {
            error = null;
            return NumericScalarEvaluationState.Value;
        }

        error = ErrorValue.Num;
        return NumericScalarEvaluationState.Error;
    }

    private enum NumericScalarEvaluationState
    {
        Unsupported,
        Value,
        Error
    }

    private ScalarValue EvaluateArrayOperand(FormulaNode node, IEvalContext context)
    {
        if (node is RangeRefNode range)
            return BuildRangeValueOrError(range, context);

        if (node is NamedRangeNode named)
        {
            var binding = context.TryResolveLambdaBinding(named.Name);
            if (binding is not null)
                return binding;

            var resolvedRange = context.TryResolveNamedRange(named.Name);
            return resolvedRange is null
                ? ErrorValue.Name
                : BuildRangeValueOrError(resolvedRange.Value, context);
        }

        if (node is StructuredReferenceNode structured)
        {
            var resolvedRange = TryResolveStructuredReferenceRange(structured, context);
            return resolvedRange is null
                ? ErrorValue.Name
                : BuildRangeValueOrError(resolvedRange.Value, context);
        }

        if (node is StructuredCurrentRowReferenceNode currentRow)
            return EvaluateCurrentRowReference(currentRow, context);

        var value = EvaluateNode(node, context);
        return value;
    }

    private static ScalarValue EvaluateStructuredReference(StructuredReferenceNode node, IEvalContext context)
    {
        var range = TryResolveStructuredReferenceRange(node, context);
        return range is null
            ? ErrorValue.Name
            : BuildRangeValueOrError(range.Value, context);
    }

    private static ScalarValue EvaluateCurrentRowReference(StructuredCurrentRowReferenceNode node, IEvalContext context)
    {
        var address = StructuredReferenceResolver.ResolveCurrentRowColumn(
            context.CurrentWorkbook,
            context.CurrentSheet,
            context.CurrentCellAddress,
            node.TableName,
            node.ColumnName);
        return address is null
            ? ErrorValue.Name
            : context.GetCellValue(address.Value.Row, address.Value.Col);
    }

    private static ScalarValue PowerOp(ScalarValue left, ScalarValue right)
        => ElementwiseOp(left, right, PowerScalarOp);

    private static ScalarValue PowerScalarOp(ScalarValue left, ScalarValue right)
    {
        if (left is NumberValue leftNumber && right is NumberValue rightNumber)
            return PowerNumberValues(leftNumber.Value, rightNumber.Value);

        if (!TryCoerceToNumberValue(left, out var baseVal)) return NumericCoercionError(left);
        if (!TryCoerceToNumberValue(right, out var exp)) return NumericCoercionError(right);
        return PowerNumberValues(baseVal, exp);
    }

    private static ScalarValue PowerNumberValues(double baseVal, double exp)
    {
        if (baseVal == 0 && exp <= 0) return exp == 0 ? ErrorValue.Num : ErrorValue.DivByZero;
        double result = Math.Pow(baseVal, exp);
        return double.IsFinite(result) ? new NumberValue(result) : ErrorValue.Num;
    }

    private static ScalarValue ArithOp(ScalarValue left, ScalarValue right, ArithmeticKind kind)
    {
        if (left is not RangeValue && right is not RangeValue)
            return ArithScalarOp(left, right, kind);

        return kind switch
        {
            ArithmeticKind.Add => ElementwiseOp(left, right, AddScalarOp),
            ArithmeticKind.Subtract => ElementwiseOp(left, right, SubtractScalarOp),
            _ => ElementwiseOp(left, right, MultiplyScalarOp)
        };
    }

    private static ScalarValue AddScalarOp(ScalarValue left, ScalarValue right) =>
        ArithScalarOp(left, right, ArithmeticKind.Add);

    private static ScalarValue SubtractScalarOp(ScalarValue left, ScalarValue right) =>
        ArithScalarOp(left, right, ArithmeticKind.Subtract);

    private static ScalarValue MultiplyScalarOp(ScalarValue left, ScalarValue right) =>
        ArithScalarOp(left, right, ArithmeticKind.Multiply);

    private static ScalarValue ArithScalarOp(ScalarValue left, ScalarValue right, ArithmeticKind kind)
    {
        if (left is NumberValue leftNumberValue && right is NumberValue rightNumberValue)
            return ArithNumberValues(leftNumberValue.Value, rightNumberValue.Value, kind);

        if (!TryCoerceToNumberValue(left, out var leftNumber)) return NumericCoercionError(left);
        if (!TryCoerceToNumberValue(right, out var rightNumber)) return NumericCoercionError(right);
        return ArithNumberValues(leftNumber, rightNumber, kind);
    }

    private static ScalarValue ArithNumberValues(double leftNumber, double rightNumber, ArithmeticKind kind)
    {
        double result = kind switch
        {
            ArithmeticKind.Add => leftNumber + rightNumber,
            ArithmeticKind.Subtract => leftNumber - rightNumber,
            _ => leftNumber * rightNumber
        };
        return double.IsFinite(result) ? new NumberValue(result) : ErrorValue.Num;
    }

    private static ScalarValue DivideOp(ScalarValue left, ScalarValue right)
        => ElementwiseOp(left, right, DivideScalarOp);

    private static ScalarValue DivideScalarOp(ScalarValue left, ScalarValue right)
    {
        if (left is NumberValue leftNumber && right is NumberValue rightNumber)
            return DivideNumberValues(leftNumber.Value, rightNumber.Value);

        if (!TryCoerceToNumberValue(left, out var dividend)) return NumericCoercionError(left);
        if (!TryCoerceToNumberValue(right, out var divisor)) return NumericCoercionError(right);
        return DivideNumberValues(dividend, divisor);
    }

    private static ScalarValue DivideNumberValues(double dividend, double divisor)
    {
        if (divisor == 0) return ErrorValue.DivByZero;
        double result = dividend / divisor;
        return double.IsFinite(result) ? new NumberValue(result) : ErrorValue.Num;
    }

    private static ScalarValue ConcatOp(ScalarValue left, ScalarValue right)
        => ElementwiseOp(left, right, (l, r) => new TextValue(ValueToString(l) + ValueToString(r)));

    private static ScalarValue ElementwiseOp(
        ScalarValue left,
        ScalarValue right,
        Func<ScalarValue, ScalarValue, ScalarValue> scalarOp)
    {
        var leftRange = left as RangeValue;
        var rightRange = right as RangeValue;
        if (leftRange is null && rightRange is null)
            return scalarOp(left, right);

        if (leftRange is RangeValue lr && rightRange is RangeValue rr)
        {
            if (!CanBroadcast(lr.RowCount, rr.RowCount) || !CanBroadcast(lr.ColCount, rr.ColCount))
                return ErrorValue.Value;

            var rowCount = Math.Max(lr.RowCount, rr.RowCount);
            var colCount = Math.Max(lr.ColCount, rr.ColCount);
            var cells = new ScalarValue[rowCount, colCount];
            for (var row = 0; row < rowCount; row++)
                for (var col = 0; col < colCount; col++)
                    cells[row, col] = scalarOp(
                        lr.Cells[lr.RowCount == 1 ? 0 : row, lr.ColCount == 1 ? 0 : col],
                        rr.Cells[rr.RowCount == 1 ? 0 : row, rr.ColCount == 1 ? 0 : col]);
            return new RangeValue(cells, lr.StartRow, lr.StartCol) { SheetName = lr.SheetName };
        }

        var range = leftRange ?? rightRange!;
        var scalar = leftRange is null ? left : right;
        var scalarOnLeft = leftRange is null;
        var result = new ScalarValue[range.RowCount, range.ColCount];
        for (var row = 0; row < range.RowCount; row++)
        {
            for (var col = 0; col < range.ColCount; col++)
            {
                var rangeValue = range.Cells[row, col];
                result[row, col] = scalarOnLeft
                    ? scalarOp(scalar, rangeValue)
                    : scalarOp(rangeValue, scalar);
            }
        }

        return new RangeValue(result, range.StartRow, range.StartCol) { SheetName = range.SheetName };
    }

    private static bool CanBroadcast(int left, int right) => left == right || left == 1 || right == 1;

    private enum ArithmeticKind
    {
        Add,
        Subtract,
        Multiply
    }

    private static ScalarValue CompareOpEqual(ScalarValue left, ScalarValue right)
    {
        if (left is not RangeValue && right is not RangeValue)
            return CompareScalarOpEqual(left, right);

        return ElementwiseOp(left, right, CompareScalarOpEqual);
    }

    private static ScalarValue CompareScalarOpEqual(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return cmp == 0 ? TrueValue : FalseValue;
    }

    private static ScalarValue CompareOpNotEqual(ScalarValue left, ScalarValue right)
    {
        if (left is not RangeValue && right is not RangeValue)
            return CompareScalarOpNotEqual(left, right);

        return ElementwiseOp(left, right, CompareScalarOpNotEqual);
    }

    private static ScalarValue CompareScalarOpNotEqual(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return cmp != 0 ? TrueValue : FalseValue;
    }

    private static ScalarValue CompareOpLessThan(ScalarValue left, ScalarValue right)
    {
        if (left is not RangeValue && right is not RangeValue)
            return CompareScalarOpLessThan(left, right);

        return ElementwiseOp(left, right, CompareScalarOpLessThan);
    }

    private static ScalarValue CompareScalarOpLessThan(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return cmp < 0 ? TrueValue : FalseValue;
    }

    private static ScalarValue CompareOpGreaterThan(ScalarValue left, ScalarValue right)
    {
        if (left is not RangeValue && right is not RangeValue)
            return CompareScalarOpGreaterThan(left, right);

        return ElementwiseOp(left, right, CompareScalarOpGreaterThan);
    }

    private static ScalarValue CompareScalarOpGreaterThan(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return cmp > 0 ? TrueValue : FalseValue;
    }

    private static ScalarValue CompareOpLessOrEqual(ScalarValue left, ScalarValue right)
    {
        if (left is not RangeValue && right is not RangeValue)
            return CompareScalarOpLessOrEqual(left, right);

        return ElementwiseOp(left, right, CompareScalarOpLessOrEqual);
    }

    private static ScalarValue CompareScalarOpLessOrEqual(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return cmp <= 0 ? TrueValue : FalseValue;
    }

    private static ScalarValue CompareOpGreaterOrEqual(ScalarValue left, ScalarValue right)
    {
        if (left is not RangeValue && right is not RangeValue)
            return CompareScalarOpGreaterOrEqual(left, right);

        return ElementwiseOp(left, right, CompareScalarOpGreaterOrEqual);
    }

    private static ScalarValue CompareScalarOpGreaterOrEqual(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return cmp >= 0 ? TrueValue : FalseValue;
    }

    private static int CompareValues(ScalarValue left, ScalarValue right)
    {
        // Numbers and dates compare as numbers (dates are OADate serial numbers)
        bool lNum = left is NumberValue or DateTimeValue;
        bool rNum = right is NumberValue or DateTimeValue;
        if (lNum && rNum)
        {
            double lv = left is DateTimeValue ld ? ld.Value : ((NumberValue)left).Value;
            double rv = right is DateTimeValue rd ? rd.Value : ((NumberValue)right).Value;
            return lv.CompareTo(rv);
        }
        if (left is TextValue lt && right is TextValue rt)
            return string.Compare(lt.Value, rt.Value, StringComparison.OrdinalIgnoreCase);
        if (left is BoolValue lb && right is BoolValue rb)
            return lb.Value.CompareTo(rb.Value);

        // Mixed types: numbers/dates < text < booleans (Excel convention)
        return TypeOrder(left).CompareTo(TypeOrder(right));
    }

    private static int TypeOrder(ScalarValue v) => v switch
    {
        BlankValue => 0,
        NumberValue or DateTimeValue => 1,
        TextValue => 2,
        BoolValue => 3,
        _ => 4
    };

    private ScalarValue EvaluateUnaryOp(UnaryOpNode node, IEvalContext context)
    {
        var operand = EvaluateArrayOperand(node.Operand, context);
        if (operand is ErrorValue err) return err;

        return node.Operator switch
        {
            UnaryOperator.Negate => NegateOp(operand),
            UnaryOperator.Percent => PercentOp(operand),
            _ => throw new FormulaEvalException("#VALUE!", $"Unknown unary operator: {node.Operator}")
        };
    }

    private static ScalarValue NegateOp(ScalarValue v)
        => ElementwiseUnaryOp(v, NegateScalarOp);

    private static ScalarValue NegateScalarOp(ScalarValue v)
    {
        if (v is NumberValue numberValue)
            return new NumberValue(-numberValue.Value);

        if (!TryCoerceToNumberValue(v, out var number)) return NumericCoercionError(v);
        return new NumberValue(-number);
    }

    private static ScalarValue PercentOp(ScalarValue v)
        => ElementwiseUnaryOp(v, PercentScalarOp);

    private static ScalarValue PercentScalarOp(ScalarValue v)
    {
        if (v is NumberValue numberValue)
            return new NumberValue(numberValue.Value / 100.0);

        if (!TryCoerceToNumberValue(v, out var number)) return NumericCoercionError(v);
        return new NumberValue(number / 100.0);
    }

    private static ScalarValue ElementwiseUnaryOp(ScalarValue value, Func<ScalarValue, ScalarValue> scalarOp)
    {
        if (value is not RangeValue range)
            return scalarOp(value);

        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (var row = 0; row < range.RowCount; row++)
            for (var col = 0; col < range.ColCount; col++)
                cells[row, col] = scalarOp(range.Cells[row, col]);

        return new RangeValue(cells, range.StartRow, range.StartCol) { SheetName = range.SheetName };
    }

    private ScalarValue EvaluateFunction(FunctionCallNode node, IEvalContext context)
    {
        var functionName = node.FunctionName;

        // LET-scoped lambda bindings: a name like "double" resolves to a LambdaValue
        // before any built-in lookup, allowing user-defined functions to shadow nothing.
        var lambdaBinding = context.TryResolveLambdaBinding(functionName);
        if (lambdaBinding is LambdaValue lv)
            return InvokeLambdaWithArgs(lv, node.Arguments, context);
        if (lambdaBinding is ErrorValue bindingError)
            return bindingError;
        if (lambdaBinding is not null)
            return ErrorValue.Value;

        // LET and LAMBDA are AST-aware special forms not in the built-in registry.
        if (functionName is "LET" or "LAMBDA")
            return EvaluateAstAware(node, context);

        if (!BuiltInFunctions.TryGet(functionName, out var entry))
            return ErrorValue.Name;

        // Short-circuit functions evaluate arguments lazily to avoid propagating errors from untaken branches.
        if (functionName is "IF" or "IFERROR" or "IFNA" or "CHOOSE" or "IFS" or "SWITCH")
            return EvaluateShortCircuit(node, context);

        // AST-aware functions: must inspect the raw argument nodes before evaluation.
        if (functionName is "ISREF" or "ISFORMULA" or "FORMULATEXT" or "OFFSET" or "CELL")
            return EvaluateAstAware(node, context);

        var (func, minArgs, maxArgs) = entry;

        if (functionName == "INDEX" &&
            TryEvaluateIndexDirectRange(node, context, out var directIndexResult))
            return directIndexResult;

        if (TryEvaluateReferenceDimensionFunction(functionName, node, context, out var dimensionResult))
            return dimensionResult;

        bool isStructured = IsStructuredRangeFunction(functionName);
        bool isAggregate = IsAggregateFunction(functionName);
        bool isDirectTextCoercingAggregate = IsDirectTextCoercingAggregate(functionName);
        bool preservesReferenceProvenance = IsReferenceProvenanceAggregate(functionName);
        bool isSingleCellReferenceRangeFunction = IsSingleCellReferenceRangeFunction(functionName);

        if (node.Arguments.Count >= minArgs &&
            (isAggregate || node.Arguments.Count <= maxArgs) &&
            TryEvaluateRangeOnlyFastAggregate(functionName, node.Arguments, context, out var fastAggregate))
        {
            return fastAggregate;
        }

        // Expand range arguments into individual values for aggregate functions,
        // or wrap as RangeValue for structured functions that need 2-D access.
        var expandedArgs = new List<ScalarValue>(node.Arguments.Count);
        for (var argIndex = 0; argIndex < node.Arguments.Count; argIndex++)
        {
            var arg = node.Arguments[argIndex];
            if (TryAsRangeRef(arg, out var range))
            {
                if (range.SheetName is not null && !context.SheetExists(range.SheetName))
                {
                    expandedArgs.Add(ErrorValue.Ref);
                    continue;
                }

                // Full-column/full-row references nominally span the whole grid and would exceed the
                // materialization cap (returning #REF!). Excel only ever reads the populated extent,
                // so clamp the open end to the sheet's used range — both the streamed (GetRangeValues)
                // and structured (BuildRangeValue) branches below then operate on a bounded range.
                range = ClampOpenEndedRangeToUsed(range, context);

                if (isStructured)
                {
                    // Build a 2-D RangeValue for structured functions
                    expandedArgs.Add(BuildRangeValueOrError(range, context));
                }
                else
                {
                    IReadOnlyList<ScalarValue> values = range.SheetName is not null
                        ? context.GetRangeValues(range.SheetName,
                            range.Start.Row, range.Start.ColumnNumber,
                            range.End.Row, range.End.ColumnNumber)
                        : context.GetRangeValues(
                            range.Start.Row, range.Start.ColumnNumber,
                            range.End.Row, range.End.ColumnNumber);
                    AddRangeValues(expandedArgs, values, preservesReferenceProvenance);
                }
            }
            else if (arg is StringNode directText && isDirectTextCoercingAggregate)
            {
                expandedArgs.Add(new DirectTextLiteralValue(directText.Value));
            }
            else if (arg is CellRefNode structuredCell && IsConditionalAggregateRangeArgument(functionName, argIndex))
            {
                if (structuredCell.SheetName is not null && !context.SheetExists(structuredCell.SheetName))
                {
                    expandedArgs.Add(ErrorValue.Ref);
                    continue;
                }

                expandedArgs.Add(BuildRangeValueOrError(new RangeRefNode(structuredCell, structuredCell, structuredCell.SheetName), context));
            }
            else if (arg is CellRefNode aggregateCell && IsSingleCellReferenceProvenanceArgument(functionName, argIndex, preservesReferenceProvenance))
            {
                if (aggregateCell.SheetName is not null && !context.SheetExists(aggregateCell.SheetName))
                {
                    expandedArgs.Add(ErrorValue.Ref);
                    continue;
                }

                var value = aggregateCell.SheetName is not null
                    ? context.GetCellValue(aggregateCell.SheetName, aggregateCell.Row, aggregateCell.ColumnNumber)
                    : context.GetCellValue(aggregateCell.Row, aggregateCell.ColumnNumber);
                expandedArgs.Add(new ReferencedScalarValue(value));
            }
            else if (arg is CellRefNode cell && isSingleCellReferenceRangeFunction)
            {
                if (cell.SheetName is not null && !context.SheetExists(cell.SheetName))
                {
                    expandedArgs.Add(ErrorValue.Ref);
                    continue;
                }

                expandedArgs.Add(BuildRangeValueOrError(new RangeRefNode(cell, cell, cell.SheetName), context));
            }
            else if (arg is NamedRangeNode named)
            {
                // Check LET/LAMBDA bindings first — these shadow workbook named ranges.
                var lambdaBound = context.TryResolveLambdaBinding(named.Name);
                if (lambdaBound is not null)
                {
                    if (isStructured && lambdaBound is RangeValue)
                        expandedArgs.Add(lambdaBound);
                    else if (!isStructured && lambdaBound is RangeValue flatRv)
                        AddRangeValues(expandedArgs, flatRv.Flatten(), preservesReferenceProvenance);
                    else
                        expandedArgs.Add(lambdaBound);
                }
                else
                {
                    var resolvedRange = context.TryResolveNamedRange(named.Name);
                    if (resolvedRange is null)
                    {
                        expandedArgs.Add(ErrorValue.Name);
                    }
                    else
                    {
                        var r = resolvedRange.Value;
                        if (isStructured)
                        {
                            expandedArgs.Add(BuildRangeValueOrError(r, context));
                        }
                        else
                        {
                            // Resolve the sheet name when the named range lives on a different sheet
                            var sheetName = context.TryGetSheetName(r.Start.Sheet);
                            IReadOnlyList<ScalarValue> values = sheetName is not null
                                ? context.GetRangeValues(sheetName,
                                    r.Start.Row, r.Start.Col,
                                    r.End.Row, r.End.Col)
                                : context.GetRangeValues(
                                    r.Start.Row, r.Start.Col,
                                    r.End.Row, r.End.Col);
                            AddRangeValues(expandedArgs, values, preservesReferenceProvenance);
                        }
                    }
                }
            }
            else
            {
                var value = EvaluateNode(arg, context);
                if (!isStructured && isAggregate && value is RangeValue rangeValue)
                    AddRangeValues(expandedArgs, rangeValue.Flatten(), preservesReferenceProvenance);
                else
                    expandedArgs.Add(value);
            }
        }

        foreach (var expandedArg in expandedArgs)
        {
            if (expandedArg is RangeMaterializationErrorValue rangeError)
                return rangeError.Error;
        }

        // Always enforce minimum arg count for every function, including aggregates.
        if (node.Arguments.Count < minArgs)
            return ErrorValue.Value;
        // Enforce maximum only for non-aggregate functions (aggregates accept unbounded ranges).
        if (!isAggregate && node.Arguments.Count > maxArgs)
            return ErrorValue.Value;

        try
        {
            return func(expandedArgs, context);
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
        catch (OverflowException)
        {
            return ErrorValue.Num;
        }
        catch (ArgumentOutOfRangeException)
        {
            return ErrorValue.Num;
        }
        catch (IndexOutOfRangeException)
        {
            return ErrorValue.Ref;
        }
    }

    private static ErrorValue ErrorFromCode(string code) => code.ToUpperInvariant() switch
    {
        "#DIV/0!" => ErrorValue.DivByZero,
        "#VALUE!" => ErrorValue.Value,
        "#REF!" => ErrorValue.Ref,
        "#NAME?" => ErrorValue.Name,
        "#NULL!" => ErrorValue.Null,
        "#N/A" => ErrorValue.NA,
        "#NUM!" => ErrorValue.Num,
        "#SPILL!" => ErrorValue.Spill,
        "#CALC!" => ErrorValue.Calc,
        _ => ErrorValue.Value
    };

    private static void AddRangeValues(
        List<ScalarValue> expandedArgs,
        IReadOnlyList<ScalarValue> values,
        bool preservesReferenceProvenance)
    {
        if (values.Count == 1 && values[0] is RangeMaterializationErrorValue)
        {
            expandedArgs.Add(values[0]);
            return;
        }

        var finalCount = (long)expandedArgs.Count + values.Count;
        if (finalCount <= int.MaxValue)
            expandedArgs.EnsureCapacity((int)finalCount);

        if (preservesReferenceProvenance)
        {
            foreach (var value in values)
                expandedArgs.Add(new ReferencedScalarValue(value));
        }
        else
        {
            foreach (var value in values)
                expandedArgs.Add(value);
        }
    }

    private static bool TryEvaluateRangeOnlyFastAggregate(
        string functionName,
        IReadOnlyList<FormulaNode> arguments,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (!TryGetFastAggregateKind(functionName, out var kind))
            return false;

        var ranges = new List<FastAggregateRange>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            var resolution = TryResolveFastAggregateRange(kind, arguments[index], context, out var range, out var error);
            if (resolution == FastAggregateRangeResolution.Unsupported)
                return false;

            if (error is not null)
            {
                if (ranges.Count > 0 &&
                    TryFindFastRangeOnlyImmediateError(kind, ranges, context, out var priorError))
                    result = priorError;
                else
                    result = error;

                return true;
            }

            ranges.Add(range);
        }

        result = EvaluateFastRangeOnlyAggregate(kind, ranges, context);
        return true;
    }

    private static ScalarValue EvaluateFastRangeOnlyAggregate(
        FastAggregateKind kind,
        IReadOnlyList<FastAggregateRange> ranges,
        IEvalContext context)
    {
        return kind switch
        {
            FastAggregateKind.Sum => EvaluateFastRangeOnlySum(ranges, context),
            FastAggregateKind.Average => EvaluateFastRangeOnlyAverage(ranges, context),
            FastAggregateKind.Min => EvaluateFastRangeOnlyMinMax(ranges, context, findMax: false),
            FastAggregateKind.Max => EvaluateFastRangeOnlyMinMax(ranges, context, findMax: true),
            FastAggregateKind.CountBlank => EvaluateFastRangeOnlyCountBlank(ranges, context),
            FastAggregateKind.StdevS => EvaluateFastRangeOnlyVariance(ranges, context, sample: true, squareRoot: true),
            FastAggregateKind.StdevP => EvaluateFastRangeOnlyVariance(ranges, context, sample: false, squareRoot: true),
            FastAggregateKind.VarS => EvaluateFastRangeOnlyVariance(ranges, context, sample: true, squareRoot: false),
            FastAggregateKind.VarP => EvaluateFastRangeOnlyVariance(ranges, context, sample: false, squareRoot: false),
            _ => EvaluateFastRangeOnlyCount(ranges, context)
        };
    }

    private static bool TryFindFastRangeOnlyImmediateError(
        FastAggregateKind kind,
        IReadOnlyList<FastAggregateRange> ranges,
        IEvalContext context,
        out ErrorValue error)
    {
        error = null!;
        if (kind is FastAggregateKind.Count or FastAggregateKind.CountBlank)
            return false;

        foreach (var range in ranges)
        {
            if (context is SheetEvalContext sheetContext)
            {
                var sheet = ResolveFastAggregateSheet(range, sheetContext);
                if (sheet is null)
                {
                    error = ErrorValue.Ref;
                    return true;
                }

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        _ = TryDirectRangeNumber(sheet.GetValue(row, col), out _, out var cellError);
                        if (cellError is not null)
                        {
                            error = cellError;
                            return true;
                        }
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);
                        _ = TryDirectRangeNumber(value, out _, out var cellError);
                        if (cellError is not null)
                        {
                            error = cellError;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static ScalarValue EvaluateFastRangeOnlySum(IReadOnlyList<FastAggregateRange> ranges, IEvalContext context)
    {
        double total = 0;
        foreach (var range in ranges)
        {
            if (context is SheetEvalContext sheetContext)
            {
                var sheet = ResolveFastAggregateSheet(range, sheetContext);
                if (sheet is null) return ErrorValue.Ref;

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = sheet.GetValue(row, col);
                        if (TryDirectRangeNumber(value, out var number, out var error))
                        {
                            total += number;
                        }
                        else if (error is not null)
                        {
                            return error;
                        }
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);
                        if (TryDirectRangeNumber(value, out var number, out var error))
                        {
                            total += number;
                        }
                        else if (error is not null)
                        {
                            return error;
                        }
                    }
                }
            }
        }

        return double.IsFinite(total) ? new NumberValue(total) : ErrorValue.Num;
    }

    private static ScalarValue EvaluateFastRangeOnlyAverage(IReadOnlyList<FastAggregateRange> ranges, IEvalContext context)
    {
        double total = 0;
        long count = 0;
        foreach (var range in ranges)
        {
            if (context is SheetEvalContext sheetContext)
            {
                var sheet = ResolveFastAggregateSheet(range, sheetContext);
                if (sheet is null) return ErrorValue.Ref;

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = sheet.GetValue(row, col);
                        if (TryDirectRangeNumber(value, out var number, out var error))
                        {
                            total += number;
                            count++;
                        }
                        else if (error is not null)
                        {
                            return error;
                        }
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);
                        if (TryDirectRangeNumber(value, out var number, out var error))
                        {
                            total += number;
                            count++;
                        }
                        else if (error is not null)
                        {
                            return error;
                        }
                    }
                }
            }
        }

        return count == 0
            ? ErrorValue.DivByZero
            : double.IsFinite(total / count) ? new NumberValue(total / count) : ErrorValue.Num;
    }

    private static ScalarValue EvaluateFastRangeOnlyMinMax(
        IReadOnlyList<FastAggregateRange> ranges,
        IEvalContext context,
        bool findMax)
    {
        double? result = null;
        foreach (var range in ranges)
        {
            if (context is SheetEvalContext sheetContext)
            {
                var sheet = ResolveFastAggregateSheet(range, sheetContext);
                if (sheet is null) return ErrorValue.Ref;

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = sheet.GetValue(row, col);
                        if (TryDirectRangeNumber(value, out var number, out var error))
                        {
                            if (result is null ||
                                (findMax ? number > result.Value : number < result.Value))
                            {
                                result = number;
                            }
                        }
                        else if (error is not null)
                        {
                            return error;
                        }
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);
                        if (TryDirectRangeNumber(value, out var number, out var error))
                        {
                            if (result is null ||
                                (findMax ? number > result.Value : number < result.Value))
                            {
                                result = number;
                            }
                        }
                        else if (error is not null)
                        {
                            return error;
                        }
                    }
                }
            }
        }

        return result is null
            ? new NumberValue(0)
            : double.IsFinite(result.Value) ? new NumberValue(result.Value) : ErrorValue.Num;
    }

    private static ScalarValue EvaluateFastRangeOnlyCount(IReadOnlyList<FastAggregateRange> ranges, IEvalContext context)
    {
        long count = 0;
        foreach (var range in ranges)
        {
            if (context is SheetEvalContext sheetContext)
            {
                var sheet = ResolveFastAggregateSheet(range, sheetContext);
                if (sheet is null) return ErrorValue.Ref;

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = sheet.GetValue(row, col);
                        if (value is NumberValue or DateTimeValue)
                            count++;
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);
                        if (value is NumberValue or DateTimeValue)
                            count++;
                    }
                }
            }
        }

        return new NumberValue(count);
    }

    private static ScalarValue EvaluateFastRangeOnlyCountBlank(IReadOnlyList<FastAggregateRange> ranges, IEvalContext context)
    {
        long count = 0;
        foreach (var range in ranges)
        {
            if (context is SheetEvalContext sheetContext)
            {
                var sheet = ResolveFastAggregateSheet(range, sheetContext);
                if (sheet is null) return ErrorValue.Ref;

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = sheet.GetValue(row, col);

                        if (value is BlankValue || value is TextValue { Value.Length: 0 })
                            count++;
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);

                        if (value is BlankValue || value is TextValue { Value.Length: 0 })
                            count++;
                    }
                }
            }
        }

        return new NumberValue(count);
    }

    private static ScalarValue EvaluateFastRangeOnlyVariance(
        IReadOnlyList<FastAggregateRange> ranges,
        IEvalContext context,
        bool sample,
        bool squareRoot)
    {
        long count = 0;
        double mean = 0;
        double m2 = 0;

        foreach (var range in ranges)
        {
            if (context is SheetEvalContext sheetContext)
            {
                var sheet = ResolveFastAggregateSheet(range, sheetContext);
                if (sheet is null) return ErrorValue.Ref;

                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        if (!AccumulateFastVarianceValue(sheet.GetValue(row, col), ref count, ref mean, ref m2, out var error))
                            return error!;
                    }
                }
            }
            else
            {
                for (var row = range.StartRow; row <= range.EndRow; row++)
                {
                    for (var col = range.StartCol; col <= range.EndCol; col++)
                    {
                        var value = range.SheetName is not null
                            ? context.GetCellValue(range.SheetName, row, col)
                            : context.GetCellValue(row, col);
                        if (!AccumulateFastVarianceValue(value, ref count, ref mean, ref m2, out var error))
                            return error!;
                    }
                }
            }
        }

        if (count == 0 || (sample && count < 2))
            return ErrorValue.DivByZero;

        var variance = m2 / (sample ? count - 1 : count);
        var result = squareRoot ? Math.Sqrt(variance) : variance;
        return double.IsFinite(result) ? new NumberValue(result) : ErrorValue.Num;
    }

    private static bool AccumulateFastVarianceValue(
        ScalarValue value,
        ref long count,
        ref double mean,
        ref double m2,
        out ErrorValue? error)
    {
        if (!TryDirectRangeNumber(value, out var number, out error))
            return error is null;

        count++;
        var delta = number - mean;
        mean += delta / count;
        m2 += delta * (number - mean);
        return true;
    }

    private static Sheet? ResolveFastAggregateSheet(FastAggregateRange range, SheetEvalContext context)
        => context.ResolveSheetForFastRange(range.SheetName);

    // Intersect a full-column/full-row range with the target sheet's used (populated) extent.
    // Returns false when there is nothing to aggregate (empty sheet or no overlap), in which
    // case the caller should treat the range as containing zero cells. When the context cannot
    // resolve a sheet (non-sheet context), the range is left unchanged.
    private static bool TryClampFullRangeToUsed(
        string? sheetName,
        IEvalContext context,
        ref uint startRow,
        ref uint startCol,
        ref uint endRow,
        ref uint endCol)
    {
        if (context is not SheetEvalContext sheetContext)
            return true;

        var sheet = sheetContext.ResolveSheetForFastRange(sheetName);
        if (sheet is null)
            return true;

        if (sheet.GetUsedRange() is not { } used)
            return false;

        var clampedStartRow = Math.Max(startRow, used.Start.Row);
        var clampedEndRow = Math.Min(endRow, used.End.Row);
        var clampedStartCol = Math.Max(startCol, used.Start.Col);
        var clampedEndCol = Math.Min(endCol, used.End.Col);

        if (clampedStartRow > clampedEndRow || clampedStartCol > clampedEndCol)
            return false;

        startRow = clampedStartRow;
        endRow = clampedEndRow;
        startCol = clampedStartCol;
        endCol = clampedEndCol;
        return true;
    }

    private static FastAggregateRangeResolution TryResolveFastAggregateRange(
        FastAggregateKind kind,
        FormulaNode argument,
        IEvalContext context,
        out FastAggregateRange range,
        out ErrorValue? error)
    {
        range = default;
        error = null;

        if (TryAsRangeRef(argument, out var rangeRef))
        {
            if (rangeRef.SheetName is not null && !context.SheetExists(rangeRef.SheetName))
            {
                error = ErrorValue.Ref;
                return FastAggregateRangeResolution.Error;
            }

            var startRow = Math.Min(rangeRef.Start.Row, rangeRef.End.Row);
            var startCol = Math.Min(rangeRef.Start.ColumnNumber, rangeRef.End.ColumnNumber);
            var endRow = Math.Max(rangeRef.Start.Row, rangeRef.End.Row);
            var endCol = Math.Max(rangeRef.Start.ColumnNumber, rangeRef.End.ColumnNumber);

            // Full-column (A:C) / full-row (1:5) ranges nominally span 1,048,576 rows or
            // 16,384 columns. Excel aggregates only the populated extent; clamping to the
            // sheet's used range gives the same numeric result, keeps us under the streaming
            // cap (so e.g. SUM(A:C) no longer wrongly returns #REF!), and is far faster.
            // COUNTBLANK is excluded: it must count blanks across the whole nominal range.
            if (argument is FullColumnRangeRefNode or FullRowRangeRefNode
                && kind != FastAggregateKind.CountBlank)
            {
                if (!TryClampFullRangeToUsed(rangeRef.SheetName, context, ref startRow, ref startCol, ref endRow, ref endCol))
                {
                    // No populated cells overlap the range: emit an empty range (endRow < startRow
                    // so every aggregate loop iterates zero cells -> SUM/COUNT 0, AVERAGE #DIV/0!, etc.).
                    range = new FastAggregateRange(rangeRef.SheetName, 1, 1, 0, 0);
                    return FastAggregateRangeResolution.Range;
                }
            }

            var resolvedRange = new FastAggregateRange(rangeRef.SheetName, startRow, startCol, endRow, endCol);

            if (!TryAcceptFastAggregateRange(resolvedRange, kind, out error))
                return FastAggregateRangeResolution.Error;

            range = resolvedRange;
            return FastAggregateRangeResolution.Range;
        }

        if (argument is FunctionCallNode { FunctionName: "INDIRECT" } indirect)
        {
            if (!TryBuildLiteralIndirectArguments(indirect, out var indirectArgs, out error))
                return error is null
                    ? FastAggregateRangeResolution.Unsupported
                    : FastAggregateRangeResolution.Error;

            if (!BuiltInFunctions.TryResolveIndirectRangeReference(indirectArgs, context, out var indirectRange, out var indirectError))
            {
                error = indirectError as ErrorValue;
                return error is null
                    ? FastAggregateRangeResolution.Unsupported
                    : FastAggregateRangeResolution.Error;
            }

            var resolvedRange = new FastAggregateRange(
                indirectRange.SheetName,
                Math.Min(indirectRange.StartRow, indirectRange.EndRow),
                Math.Min(indirectRange.StartCol, indirectRange.EndCol),
                Math.Max(indirectRange.StartRow, indirectRange.EndRow),
                Math.Max(indirectRange.StartCol, indirectRange.EndCol));

            if (!TryAcceptFastAggregateRange(resolvedRange, kind, out error))
                return FastAggregateRangeResolution.Error;

            range = resolvedRange;
            return FastAggregateRangeResolution.Range;
        }

        if (argument is NamedRangeNode named)
        {
            if (context.TryResolveLambdaBinding(named.Name) is not null)
                return FastAggregateRangeResolution.Unsupported;

            var resolvedNamedRange = context.TryResolveNamedRange(named.Name);
            if (resolvedNamedRange is null)
                return FastAggregateRangeResolution.Unsupported;

            var gridRange = resolvedNamedRange.Value;
            var resolvedRange = new FastAggregateRange(
                context.TryGetSheetName(gridRange.Start.Sheet),
                gridRange.Start.Row,
                gridRange.Start.Col,
                gridRange.End.Row,
                gridRange.End.Col);

            if (!TryAcceptFastAggregateRange(resolvedRange, kind, out error))
                return FastAggregateRangeResolution.Error;

            range = resolvedRange;
            return FastAggregateRangeResolution.Range;
        }

        return FastAggregateRangeResolution.Unsupported;
    }

    private static bool TryAcceptFastAggregateRange(FastAggregateRange range, FastAggregateKind kind, out ErrorValue? error)
    {
        error = null;
        var cellCount = FormulaSafetyLimits.GetRangeCellCount(
            range.StartRow,
            range.StartCol,
            range.EndRow,
            range.EndCol);
        var maxCells = kind is FastAggregateKind.StdevS or FastAggregateKind.StdevP or FastAggregateKind.VarS or FastAggregateKind.VarP
            ? FormulaSafetyLimits.MaxMaterializedRangeCells
            : FormulaSafetyLimits.MaxStreamingRangeCells;
        if (cellCount <= maxCells)
            return true;

        error = ErrorValue.Ref;
        return false;
    }

    private static bool TryBuildLiteralIndirectArguments(
        FunctionCallNode node,
        out IReadOnlyList<ScalarValue> args,
        out ErrorValue? error)
    {
        args = [];
        error = null;
        if (node.Arguments.Count is < 1 or > 2)
        {
            error = ErrorValue.Value;
            return false;
        }

        if (!TryBuildLiteralIndirectArgument(node.Arguments[0], out var refText, out error))
            return false;

        if (node.Arguments.Count == 1)
        {
            args = [refText];
            return true;
        }

        if (!TryBuildLiteralIndirectArgument(node.Arguments[1], out var useA1, out error))
            return false;

        args = [refText, useA1];
        return true;
    }

    private static bool TryBuildLiteralIndirectArgument(
        FormulaNode node,
        out ScalarValue value,
        out ErrorValue? error)
    {
        value = BlankValue.Instance;
        error = null;
        switch (node)
        {
            case StringNode text:
                value = new TextValue(text.Value);
                return true;
            case BooleanNode boolean:
                value = boolean.Value ? TrueValue : FalseValue;
                return true;
            case NumberNode number:
                value = new NumberValue(number.Value);
                return true;
            case OmittedArgumentNode:
                value = BlankValue.Instance;
                return true;
            case ErrorNode errorNode:
                error = errorNode.Error;
                return false;
            default:
                return false;
        }
    }

    private static bool TryGetFastAggregateKind(string functionName, out FastAggregateKind kind)
    {
        switch (functionName)
        {
            case "SUM":
                kind = FastAggregateKind.Sum;
                return true;
            case "AVERAGE":
                kind = FastAggregateKind.Average;
                return true;
            case "MIN":
                kind = FastAggregateKind.Min;
                return true;
            case "MAX":
                kind = FastAggregateKind.Max;
                return true;
            case "COUNT":
                kind = FastAggregateKind.Count;
                return true;
            case "COUNTBLANK":
                kind = FastAggregateKind.CountBlank;
                return true;
            case "STDEV":
            case "STDEV.S":
                kind = FastAggregateKind.StdevS;
                return true;
            case "STDEVP":
            case "STDEV.P":
                kind = FastAggregateKind.StdevP;
                return true;
            case "VAR":
            case "VAR.S":
                kind = FastAggregateKind.VarS;
                return true;
            case "VARP":
            case "VAR.P":
                kind = FastAggregateKind.VarP;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private readonly record struct FastAggregateRange(
        string? SheetName,
        uint StartRow,
        uint StartCol,
        uint EndRow,
        uint EndCol);

    private enum FastAggregateKind
    {
        Sum,
        Average,
        Min,
        Max,
        Count,
        CountBlank,
        StdevS,
        StdevP,
        VarS,
        VarP
    }

    private enum FastAggregateRangeResolution
    {
        Unsupported,
        Range,
        Error
    }

    private static bool TryDirectRangeNumber(ScalarValue value, out double number, out ErrorValue? error)
    {
        number = 0;
        error = null;
        switch (value)
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

    private static RangeValue BuildRangeValue(RangeRefNode range, IEvalContext context)
    {
        // A full-column (A:A) / full-row (1:1) reference nominally spans 1,048,576 rows or 16,384
        // columns, which exceeds the materialization cap and would otherwise return #REF! — even for
        // a single column. Excel only ever materializes the populated extent, so clamp the open end
        // down to the sheet's used range. The start is left untouched so positional access (INDEX,
        // COLUMN, ...) keeps the same Nth-element / top-left meaning.
        range = ClampOpenEndedRangeToUsed(range, context);

        // Normalize so r0 ≤ r1 and c0 ≤ c1 — Excel accepts B5:A1 and treats it as A1:B5.
        // Without this, uint subtraction wraps and produces a negative dimension.
        uint r0 = Math.Min(range.Start.Row, range.End.Row);
        uint r1 = Math.Max(range.Start.Row, range.End.Row);
        uint c0 = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        uint c1 = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);
        long rows = r1 - r0 + 1;
        long cols = c1 - c0 + 1;
        if (rows * cols > FormulaSafetyLimits.MaxMaterializedRangeCells)
            throw new FormulaEvalException("#REF!", "Range contains more than 1,000,000 cells");
        var cells = new ScalarValue[(int)rows, (int)cols];
        for (int ri = 0; ri < rows; ri++)
            for (int ci = 0; ci < cols; ci++)
            {
                cells[ri, ci] = range.SheetName is not null
                    ? context.GetCellValue(range.SheetName, r0 + (uint)ri, c0 + (uint)ci)
                    : context.GetCellValue(r0 + (uint)ri, c0 + (uint)ci);
            }
        return new RangeValue(cells, r0, c0) { SheetName = range.SheetName };
    }

    // Clamp the open end of a full-column/full-row reference to the target sheet's used extent.
    // Only ranges that reach the grid limit (End at MaxRow/MaxCol) are touched; explicit bounded
    // ranges pass through unchanged. The start is preserved so element positions stay correct.
    private static RangeRefNode ClampOpenEndedRangeToUsed(RangeRefNode range, IEvalContext context)
    {
        bool fullColumn = range.End.Row >= FreeX.Core.Model.CellAddress.MaxRow;
        bool fullRow = range.End.ColumnNumber >= FreeX.Core.Model.CellAddress.MaxCol;
        if (!fullColumn && !fullRow)
            return range;

        if (context is not SheetEvalContext sheetContext)
            return range;

        var sheet = sheetContext.ResolveSheetForFastRange(range.SheetName);
        if (sheet is null)
            return range;

        uint endRow = range.End.Row;
        uint endCol = range.End.ColumnNumber;

        if (sheet.GetUsedRange() is { } used)
        {
            if (fullColumn) endRow = Math.Min(endRow, Math.Max(used.End.Row, range.Start.Row));
            if (fullRow) endCol = Math.Min(endCol, Math.Max(used.End.Col, range.Start.ColumnNumber));
        }
        else
        {
            // Empty sheet: collapse the open dimension to its start (a single blank line).
            if (fullColumn) endRow = range.Start.Row;
            if (fullRow) endCol = range.Start.ColumnNumber;
        }

        if (endRow == range.End.Row && endCol == range.End.ColumnNumber)
            return range;

        var end = range.End with
        {
            ColumnName = FreeX.Core.Model.CellAddress.NumberToColumnName(endCol),
            Row = endRow
        };
        return new RangeRefNode(range.Start, end, range.SheetName);
    }

    private static ScalarValue BuildRangeValueOrError(RangeRefNode range, IEvalContext context)
    {
        try
        {
            return BuildRangeValue(range, context);
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
    }

    private static RangeValue BuildRangeValue(FreeX.Core.Model.GridRange range, IEvalContext context)
    {
        var sheetName = context.TryGetSheetName(range.Start.Sheet);
        var start = new CellRefNode(
            FreeX.Core.Model.CellAddress.NumberToColumnName(range.Start.Col),
            range.Start.Row,
            SheetName: sheetName);
        var end = new CellRefNode(
            FreeX.Core.Model.CellAddress.NumberToColumnName(range.End.Col),
            range.End.Row,
            SheetName: sheetName);
        return BuildRangeValue(new RangeRefNode(start, end, sheetName), context);
    }

    private static ScalarValue BuildRangeValueOrError(FreeX.Core.Model.GridRange range, IEvalContext context)
    {
        try
        {
            return BuildRangeValue(range, context);
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
    }

    private static FreeX.Core.Model.GridRange? TryResolveStructuredReferenceRange(
        StructuredReferenceNode node,
        IEvalContext context)
        => StructuredReferenceResolver.ResolveDataBodyColumn(
            context.CurrentWorkbook,
            context.CurrentSheet,
            node.TableName,
            node.ColumnName,
            context.CurrentCellAddress);

    private static bool TryAsRangeRef(FormulaNode node, out RangeRefNode range)
    {
        range = node switch
        {
            RangeRefNode rr => rr,
            FullColumnRangeRefNode fcr => ToRangeRef(fcr),
            FullRowRangeRefNode frr => ToRangeRef(frr),
            _ => null!
        };
        return range is not null;
    }

    private static bool TryEvaluateReferenceDimensionFunction(
        string functionName,
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count != 1 || functionName is not ("ROWS" or "COLUMNS" or "AREAS"))
            return false;

        if (!TryAsRangeRef(node.Arguments[0], out var range))
            return false;

        if (range.SheetName is not null && !context.SheetExists(range.SheetName))
        {
            result = ErrorValue.Ref;
            return true;
        }

        if (functionName == "AREAS")
        {
            result = new NumberValue(1);
            return true;
        }

        uint r0 = Math.Min(range.Start.Row, range.End.Row);
        uint r1 = Math.Max(range.Start.Row, range.End.Row);
        uint c0 = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        uint c1 = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);
        result = functionName == "ROWS"
            ? new NumberValue(r1 - r0 + 1)
            : new NumberValue(c1 - c0 + 1);
        return true;
    }

    private bool TryEvaluateIndexDirectRange(FunctionCallNode node, IEvalContext context, out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (!TryAsRangeRef(node.Arguments.Count > 0 ? node.Arguments[0] : new OmittedArgumentNode(), out var range))
            return false;

        if (node.Arguments.Count is < 2 or > 3)
        {
            result = ErrorValue.Value;
            return true;
        }

        if (TryAsRangeRef(node.Arguments[1], out _) ||
            (node.Arguments.Count > 2 && TryAsRangeRef(node.Arguments[2], out _)))
            return false;

        if (range.SheetName is not null && !context.SheetExists(range.SheetName))
        {
            result = ErrorValue.Ref;
            return true;
        }

        var rowValue = EvaluateNode(node.Arguments[1], context);
        if (rowValue is ErrorValue rowError)
        {
            result = rowError;
            return true;
        }

        var columnValue = node.Arguments.Count > 2
            ? EvaluateNode(node.Arguments[2], context)
            : BlankValue.Instance;
        if (columnValue is ErrorValue columnError)
        {
            result = columnError;
            return true;
        }

        var rowCoerced = CoerceToNumber(rowValue);
        if (rowCoerced is ErrorValue rowCoerceError)
        {
            result = rowCoerceError;
            return true;
        }

        var columnCoerced = columnValue is BlankValue ? new NumberValue(1) : CoerceToNumber(columnValue);
        if (columnCoerced is ErrorValue columnCoerceError)
        {
            result = columnCoerceError;
            return true;
        }

        var rawRow = ((NumberValue)rowCoerced).Value;
        var rawColumn = ((NumberValue)columnCoerced).Value;
        if (!double.IsFinite(rawRow) || rawRow < int.MinValue || rawRow > int.MaxValue ||
            !double.IsFinite(rawColumn) || rawColumn < int.MinValue || rawColumn > int.MaxValue)
        {
            result = ErrorValue.Value;
            return true;
        }

        int rowIndex = (int)rawRow;
        int columnIndex = (int)rawColumn;

        uint startRow = Math.Min(range.Start.Row, range.End.Row);
        uint endRow = Math.Max(range.Start.Row, range.End.Row);
        uint startCol = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        uint endCol = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);
        long rowCount = endRow - startRow + 1L;
        long colCount = endCol - startCol + 1L;

        if (node.Arguments.Count == 2)
        {
            if (rowCount == 1)
            {
                columnIndex = rowIndex;
                rowIndex = 1;
            }
            else if (colCount == 1)
            {
                columnIndex = 1;
            }
        }

        if (rowIndex < 0 || columnIndex < 0)
        {
            result = ErrorValue.Value;
            return true;
        }

        if (rowIndex > rowCount || columnIndex > colCount)
        {
            result = ErrorValue.Ref;
            return true;
        }

        if (rowIndex == 0 && columnIndex == 0)
        {
            result = BuildRangeValueOrError(CreateRangeRef(startRow, startCol, endRow, endCol, range.SheetName), context);
            return true;
        }

        if (rowIndex == 0)
        {
            var targetCol = startCol + (uint)columnIndex - 1;
            result = BuildRangeValueOrError(CreateRangeRef(startRow, targetCol, endRow, targetCol, range.SheetName), context);
            return true;
        }

        if (columnIndex == 0)
        {
            var targetRow = startRow + (uint)rowIndex - 1;
            result = BuildRangeValueOrError(CreateRangeRef(targetRow, startCol, targetRow, endCol, range.SheetName), context);
            return true;
        }

        var row = startRow + (uint)rowIndex - 1;
        var col = startCol + (uint)columnIndex - 1;
        result = range.SheetName is not null
            ? context.GetCellValue(range.SheetName, row, col)
            : context.GetCellValue(row, col);
        return true;
    }

    private static RangeRefNode CreateRangeRef(uint startRow, uint startCol, uint endRow, uint endCol, string? sheetName)
    {
        var start = new CellRefNode(CellAddress.NumberToColumnName(startCol), startRow, SheetName: sheetName);
        var end = new CellRefNode(CellAddress.NumberToColumnName(endCol), endRow);
        return new RangeRefNode(start, end, sheetName);
    }

    private static RangeRefNode ToRangeRef(FullColumnRangeRefNode range)
    {
        var start = new CellRefNode(range.StartColumnName, 1, range.IsStartAbsolute, false, range.SheetName);
        var end = new CellRefNode(range.EndColumnName, CellAddress.MaxRow, range.IsEndAbsolute);
        return new RangeRefNode(start, end, range.SheetName);
    }

    private static RangeRefNode ToRangeRef(FullRowRangeRefNode range)
    {
        var start = new CellRefNode("A", range.StartRow, false, range.IsStartAbsolute, range.SheetName);
        var end = new CellRefNode(CellAddress.NumberToColumnName(CellAddress.MaxCol), range.EndRow, false, range.IsEndAbsolute);
        return new RangeRefNode(start, end, range.SheetName);
    }

    private ScalarValue EvaluateShortCircuit(FunctionCallNode node, IEvalContext context)
    {
        return node.FunctionName switch
        {
            "IF"      => EvaluateIf(node, context),
            "IFERROR" => EvaluateIfError(node, context),
            "IFNA"    => EvaluateIfNa(node, context),
            "CHOOSE"  => EvaluateChoose(node, context),
            "IFS"     => EvaluateIfs(node, context),
            "SWITCH"  => EvaluateSwitch(node, context),
            _         => ErrorValue.Value
        };
    }

    private ScalarValue EvaluateIf(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count is < 2 or > 3) return ErrorValue.Value;
        var cond = EvaluateArrayOperand(node.Arguments[0], context);
        if (cond is ErrorValue e) return e;
        if (cond is RangeValue conditionRange) return EvaluateIfConditionRange(node, context, conditionRange);
        bool? taken = cond switch
        {
            BoolValue b     => b.Value,
            NumberValue n   => n.Value != 0,
            DateTimeValue d => d.Value != 0,
            BlankValue      => false,
            _               => null   // text condition is #VALUE! in Excel
        };
        if (taken is null) return ErrorValue.Value;
        if (taken.Value)  return EvaluateArrayOperand(node.Arguments[1], context);
        if (node.Arguments.Count == 3) return EvaluateArrayOperand(node.Arguments[2], context);
        return FalseValue;
    }

    private ScalarValue EvaluateIfConditionRange(FunctionCallNode node, IEvalContext context, RangeValue conditionRange)
    {
        ScalarValue? trueBranch = null;
        ScalarValue? falseBranch = null;
        var cells = new ScalarValue[conditionRange.RowCount, conditionRange.ColCount];

        for (int r = 0; r < conditionRange.RowCount; r++)
            for (int c = 0; c < conditionRange.ColCount; c++)
            {
                var condition = conditionRange.Cells[r, c];
                if (condition is ErrorValue error)
                {
                    cells[r, c] = error;
                    continue;
                }

                bool? taken = condition switch
                {
                    BoolValue b     => b.Value,
                    NumberValue n   => n.Value != 0,
                    DateTimeValue d => d.Value != 0,
                    BlankValue      => false,
                    _               => null
                };
                if (taken is null)
                {
                    cells[r, c] = ErrorValue.Value;
                    continue;
                }

                var selected = taken.Value
                    ? trueBranch ??= EvaluateArrayOperand(node.Arguments[1], context)
                    : falseBranch ??= node.Arguments.Count == 3
                        ? EvaluateArrayOperand(node.Arguments[2], context)
                        : FalseValue;

                cells[r, c] = selected is RangeValue selectedRange
                    ? PickRangeElementForArrayResult(selectedRange, r, c, conditionRange.RowCount, conditionRange.ColCount)
                    : selected;
            }

        return new RangeValue(cells, conditionRange.StartRow, conditionRange.StartCol) { SheetName = conditionRange.SheetName };
    }

    private ScalarValue EvaluateIfError(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 2) return ErrorValue.Value;
        var value = EvaluateArrayOperand(node.Arguments[0], context);
        if (value is RangeValue range)
        {
            if (!RangeHasMatchingError(range, _ => true)) return value;
            var fallback = EvaluateArrayOperand(node.Arguments[1], context);
            return ReplaceRangeErrors(range, fallback, _ => true);
        }

        return value is ErrorValue ? EvaluateArrayOperand(node.Arguments[1], context) : value;
    }

    private ScalarValue EvaluateIfNa(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 2) return ErrorValue.Value;
        var value = EvaluateArrayOperand(node.Arguments[0], context);
        if (value is RangeValue range)
        {
            if (!RangeHasMatchingError(range, IsNAError)) return value;
            var fallback = EvaluateArrayOperand(node.Arguments[1], context);
            return ReplaceRangeErrors(range, fallback, IsNAError);
        }

        return value is ErrorValue e && IsNAError(e) ? EvaluateArrayOperand(node.Arguments[1], context) : value;
    }

    private static bool RangeHasMatchingError(RangeValue range, Func<ErrorValue, bool> catches)
    {
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
                if (range.Cells[r, c] is ErrorValue error && catches(error))
                    return true;

        return false;
    }

    private static ScalarValue ReplaceRangeErrors(RangeValue range, ScalarValue fallback, Func<ErrorValue, bool> catches)
    {
        RangeValue? fallbackRange = fallback as RangeValue;
        if (fallbackRange is not null && (fallbackRange.RowCount != range.RowCount || fallbackRange.ColCount != range.ColCount))
            return ErrorValue.Value;

        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                cells[r, c] = value is ErrorValue error && catches(error)
                    ? fallbackRange?.Cells[r, c] ?? fallback
                    : value;
            }

        return new RangeValue(cells, range.StartRow, range.StartCol) { SheetName = range.SheetName };
    }

    private static bool IsNAError(ErrorValue error) => error.Code == ErrorValue.NA.Code;

    private ScalarValue EvaluateChoose(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count < 2) return ErrorValue.Value;
        var indexVal = EvaluateArrayOperand(node.Arguments[0], context);
        if (indexVal is ErrorValue e) return e;
        if (indexVal is RangeValue indexRange) return EvaluateChooseIndexRange(node, context, indexRange);
        var coerced = CoerceToNumber(indexVal);
        if (coerced is ErrorValue ec) return ec;
        double rawIdx = ((NumberValue)coerced).Value;
        if (!double.IsFinite(rawIdx)) return ErrorValue.Value;
        int idx = (int)rawIdx;
        if (idx < 1 || idx >= node.Arguments.Count) return ErrorValue.Value;
        return EvaluateArrayOperand(node.Arguments[idx], context);
    }

    private ScalarValue EvaluateChooseIndexRange(FunctionCallNode node, IEvalContext context, RangeValue indexRange)
    {
        var branchCache = new Dictionary<int, ScalarValue>();
        var cells = new ScalarValue[indexRange.RowCount, indexRange.ColCount];

        for (int r = 0; r < indexRange.RowCount; r++)
            for (int c = 0; c < indexRange.ColCount; c++)
            {
                var indexValue = indexRange.Cells[r, c];
                if (indexValue is ErrorValue indexError)
                {
                    cells[r, c] = indexError;
                    continue;
                }

                var index = CoerceChooseIndex(indexValue, node.Arguments.Count);
                if (index is null)
                {
                    cells[r, c] = ErrorValue.Value;
                    continue;
                }

                if (!branchCache.TryGetValue(index.Value, out var selected))
                {
                    selected = EvaluateArrayOperand(node.Arguments[index.Value], context);
                    branchCache[index.Value] = selected;
                }

                cells[r, c] = selected is RangeValue selectedRange
                    ? PickRangeElementForArrayResult(selectedRange, r, c, indexRange.RowCount, indexRange.ColCount)
                    : selected;
            }

        return new RangeValue(cells, indexRange.StartRow, indexRange.StartCol) { SheetName = indexRange.SheetName };
    }

    private static ScalarValue PickRangeElementForArrayResult(RangeValue range, int row, int col, int targetRows, int targetCols)
    {
        if (range.RowCount == targetRows && range.ColCount == targetCols)
            return range.Cells[row, col];

        if (range.RowCount == 1 && range.ColCount == 1)
            return range.Cells[0, 0];

        return ErrorValue.Value;
    }

    private int? CoerceChooseIndex(ScalarValue value, int argumentCount)
    {
        if (value is ErrorValue) return null;
        var coerced = CoerceToNumber(value);
        if (coerced is not NumberValue number) return null;
        double rawIdx = number.Value;
        if (!double.IsFinite(rawIdx)) return null;
        int idx = (int)rawIdx;
        return idx >= 1 && idx < argumentCount ? idx : null;
    }

    private ScalarValue EvaluateIfs(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count < 2 || node.Arguments.Count % 2 != 0) return ErrorValue.Value;
        for (int i = 0; i < node.Arguments.Count - 1; i += 2)
        {
            var cond = EvaluateArrayOperand(node.Arguments[i], context);
            if (cond is ErrorValue e) return e;
            if (cond is RangeValue conditionRange) return EvaluateIfsConditionRange(node, context, conditionRange);
            bool? taken = cond switch
            {
                BoolValue b     => b.Value,
                NumberValue n   => n.Value != 0,
                DateTimeValue d => d.Value != 0,
                BlankValue      => false,
                _               => null
            };
            if (taken is null) return ErrorValue.Value;
            if (taken.Value) return EvaluateArrayOperand(node.Arguments[i + 1], context);
        }
        return ErrorValue.NA;
    }

    private ScalarValue EvaluateIfsConditionRange(FunctionCallNode node, IEvalContext context, RangeValue firstConditionRange)
    {
        var conditionCache = new Dictionary<int, ScalarValue> { [0] = firstConditionRange };
        var resultCache = new Dictionary<int, ScalarValue>();
        var cells = new ScalarValue[firstConditionRange.RowCount, firstConditionRange.ColCount];

        for (int r = 0; r < firstConditionRange.RowCount; r++)
            for (int c = 0; c < firstConditionRange.ColCount; c++)
                cells[r, c] = EvaluateIfsElement(node, context, conditionCache, resultCache, firstConditionRange, r, c);

        return new RangeValue(cells, firstConditionRange.StartRow, firstConditionRange.StartCol) { SheetName = firstConditionRange.SheetName };
    }

    private ScalarValue EvaluateIfsElement(
        FunctionCallNode node,
        IEvalContext context,
        Dictionary<int, ScalarValue> conditionCache,
        Dictionary<int, ScalarValue> resultCache,
        RangeValue shape,
        int row,
        int col)
    {
        for (int i = 0; i < node.Arguments.Count - 1; i += 2)
        {
            if (!conditionCache.TryGetValue(i, out var condition))
            {
                condition = EvaluateArrayOperand(node.Arguments[i], context);
                conditionCache[i] = condition;
            }

            var conditionElement = condition is RangeValue conditionRange
                ? PickRangeElementForArrayResult(conditionRange, row, col, shape.RowCount, shape.ColCount)
                : condition;

            if (conditionElement is ErrorValue error) return error;
            bool? taken = conditionElement switch
            {
                BoolValue b     => b.Value,
                NumberValue n   => n.Value != 0,
                DateTimeValue d => d.Value != 0,
                BlankValue      => false,
                _               => null
            };
            if (taken is null) return ErrorValue.Value;
            if (!taken.Value) continue;

            int resultIndex = i + 1;
            if (!resultCache.TryGetValue(resultIndex, out var result))
            {
                result = EvaluateArrayOperand(node.Arguments[resultIndex], context);
                resultCache[resultIndex] = result;
            }

            return result is RangeValue resultRange
                ? PickRangeElementForArrayResult(resultRange, row, col, shape.RowCount, shape.ColCount)
                : result;
        }

        return ErrorValue.NA;
    }

    private ScalarValue EvaluateAstAware(FunctionCallNode node, IEvalContext context)
    {
        return node.FunctionName switch
        {
            "ISREF"        => EvaluateIsRef(node, context),
            "ISFORMULA"    => EvaluateIsFormula(node, context),
            "FORMULATEXT"  => EvaluateFormulaText(node, context),
            "CELL"         => EvaluateCellInfo(node, context),
            "OFFSET"       => EvaluateOffset(node, context),
            "LET"          => EvaluateLet(node, context),
            "LAMBDA"       => EvaluateLambda(node, context),
            _              => ErrorValue.Value
        };
    }

    private ScalarValue EvaluateIsRef(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 1) return ErrorValue.Value;
        var arg = node.Arguments[0];
        return arg switch
        {
            CellRefNode cell  => cell.SheetName is null || context.SheetExists(cell.SheetName) ? TrueValue : FalseValue,
            RangeRefNode rng  => rng.SheetName is null || context.SheetExists(rng.SheetName) ? TrueValue : FalseValue,
            FullColumnRangeRefNode col => col.SheetName is null || context.SheetExists(col.SheetName) ? TrueValue : FalseValue,
            FullRowRangeRefNode row => row.SheetName is null || context.SheetExists(row.SheetName) ? TrueValue : FalseValue,
            NamedRangeNode nm => context.TryResolveNamedRange(nm.Name) is not null ? TrueValue : FalseValue,
            FunctionCallNode fn when fn.FunctionName is "OFFSET" or "INDIRECT"
                => EvaluateReferenceReturningIsRef(fn, context),
            _                 => FalseValue
        };
    }

    private ScalarValue EvaluateReferenceReturningIsRef(FunctionCallNode node, IEvalContext context)
    {
        var value = EvaluateNode(node, context);

        return value is ErrorValue error
            ? error == ErrorValue.Ref ? FalseValue : error
            : TrueValue;
    }

    private ScalarValue EvaluateIsFormula(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 1) return ErrorValue.Value;
        var arg = node.Arguments[0];
        if (arg is NamedRangeNode nm)
        {
            var range = context.TryResolveNamedRange(nm.Name);
            if (range is null) return ErrorValue.Name;
            var r = range.Value;
            var sheetName = context.TryGetSheetName(r.Start.Sheet);
            var cell = sheetName is not null
                ? context.TryGetCell(sheetName, r.Start.Row, r.Start.Col)
                : context.TryGetCell(r.Start.Row, r.Start.Col);
            return cell?.HasFormula == true ? TrueValue : FalseValue;
        }
        if (arg is CellRefNode cellRef)
        {
            if (cellRef.SheetName is not null && !context.SheetExists(cellRef.SheetName))
                return ErrorValue.Ref;
            var cell = cellRef.SheetName is not null
                ? context.TryGetCell(cellRef.SheetName, cellRef.Row, cellRef.ColumnNumber)
                : context.TryGetCell(cellRef.Row, cellRef.ColumnNumber);
            return cell?.HasFormula == true ? TrueValue : FalseValue;
        }
        if (arg is RangeRefNode rangeRef)
        {
            if (rangeRef.SheetName is not null && !context.SheetExists(rangeRef.SheetName))
                return ErrorValue.Ref;
            var cell = rangeRef.SheetName is not null
                ? context.TryGetCell(rangeRef.SheetName, rangeRef.Start.Row, rangeRef.Start.ColumnNumber)
                : context.TryGetCell(rangeRef.Start.Row, rangeRef.Start.ColumnNumber);
            return cell?.HasFormula == true ? TrueValue : FalseValue;
        }
        if (arg is FullColumnRangeRefNode fullColumnRangeRef)
            return EvaluateIsFormula(new FunctionCallNode(node.FunctionName, [ToRangeRef(fullColumnRangeRef)]), context);
        if (arg is FullRowRangeRefNode fullRowRangeRef)
            return EvaluateIsFormula(new FunctionCallNode(node.FunctionName, [ToRangeRef(fullRowRangeRef)]), context);
        if (arg is FunctionCallNode fn && fn.FunctionName is "OFFSET" or "INDIRECT")
        {
            var reference = EvaluateReferenceReturningFunction(fn, context);
            if (reference is ErrorValue error) return error;
            var range = (RangeValue)reference;
            var cell = range.SheetName is not null
                ? context.TryGetCell(range.SheetName, range.StartRow, range.StartCol)
                : context.TryGetCell(range.StartRow, range.StartCol);
            return cell?.HasFormula == true ? TrueValue : FalseValue;
        }
        return ErrorValue.Value;
    }

    private ScalarValue EvaluateFormulaText(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 1) return ErrorValue.NA;
        var arg = node.Arguments[0];
        FreeX.Core.Model.Cell? cell = null;
        if (arg is CellRefNode cellRef)
        {
            if (cellRef.SheetName is not null && !context.SheetExists(cellRef.SheetName))
                return ErrorValue.Ref;
            cell = cellRef.SheetName is not null
                ? context.TryGetCell(cellRef.SheetName, cellRef.Row, cellRef.ColumnNumber)
                : context.TryGetCell(cellRef.Row, cellRef.ColumnNumber);
        }
        else if (arg is RangeRefNode rangeRef)
        {
            if (rangeRef.SheetName is not null && !context.SheetExists(rangeRef.SheetName))
                return ErrorValue.Ref;
            cell = rangeRef.SheetName is not null
                ? context.TryGetCell(rangeRef.SheetName, rangeRef.Start.Row, rangeRef.Start.ColumnNumber)
                : context.TryGetCell(rangeRef.Start.Row, rangeRef.Start.ColumnNumber);
        }
        else if (arg is FullColumnRangeRefNode fullColumnRangeRef)
        {
            var range = ToRangeRef(fullColumnRangeRef);
            if (range.SheetName is not null && !context.SheetExists(range.SheetName))
                return ErrorValue.Ref;
            cell = range.SheetName is not null
                ? context.TryGetCell(range.SheetName, range.Start.Row, range.Start.ColumnNumber)
                : context.TryGetCell(range.Start.Row, range.Start.ColumnNumber);
        }
        else if (arg is FullRowRangeRefNode fullRowRangeRef)
        {
            var range = ToRangeRef(fullRowRangeRef);
            if (range.SheetName is not null && !context.SheetExists(range.SheetName))
                return ErrorValue.Ref;
            cell = range.SheetName is not null
                ? context.TryGetCell(range.SheetName, range.Start.Row, range.Start.ColumnNumber)
                : context.TryGetCell(range.Start.Row, range.Start.ColumnNumber);
        }
        else if (arg is NamedRangeNode nm)
        {
            var range = context.TryResolveNamedRange(nm.Name);
            if (range is null) return ErrorValue.Name;
            var r = range.Value;
            var sheetName = context.TryGetSheetName(r.Start.Sheet);
            cell = sheetName is not null
                ? context.TryGetCell(sheetName, r.Start.Row, r.Start.Col)
                : context.TryGetCell(r.Start.Row, r.Start.Col);
        }
        else if (arg is FunctionCallNode fn && fn.FunctionName is "OFFSET" or "INDIRECT")
        {
            var reference = EvaluateReferenceReturningFunction(fn, context);
            if (reference is ErrorValue error) return error == ErrorValue.Value ? ErrorValue.NA : error;
            var range = (RangeValue)reference;
            cell = range.SheetName is not null
                ? context.TryGetCell(range.SheetName, range.StartRow, range.StartCol)
                : context.TryGetCell(range.StartRow, range.StartCol);
        }
        else
        {
            return ErrorValue.NA;
        }
        if (cell is null || !cell.HasFormula) return ErrorValue.NA;
        var formulaText = cell.FormulaText!;
        return new TextValue(formulaText.StartsWith('=') ? formulaText : "=" + formulaText);
    }

    private ScalarValue EvaluateCellInfo(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count is < 1 or > 2) return ErrorValue.Value;

        var infoType = EvaluateNode(node.Arguments[0], context);
        if (infoType is ErrorValue error) return error;
        if (node.Arguments.Count == 1)
            return BuiltInFunctions.CellInfo([infoType], context);

        var reference = EvaluateCellReferenceArgument(node.Arguments[1], context);
        return reference is ErrorValue refError
            ? refError
            : BuiltInFunctions.CellInfo([infoType, reference], context);
    }

    private ScalarValue EvaluateCellReferenceArgument(FormulaNode node, IEvalContext context)
    {
        if (TryAsRangeRef(node, out var range))
        {
            if (range.SheetName is not null && !context.SheetExists(range.SheetName))
                return ErrorValue.Ref;
            return BuildRangeValueOrError(range, context);
        }

        if (node is CellRefNode cellRef)
        {
            if (cellRef.SheetName is not null && !context.SheetExists(cellRef.SheetName))
                return ErrorValue.Ref;
            return BuildRangeValueOrError(new RangeRefNode(cellRef, cellRef, cellRef.SheetName), context);
        }

        if (node is NamedRangeNode named)
        {
            var rangeRef = context.TryResolveNamedRange(named.Name);
            return rangeRef is null ? ErrorValue.Name : BuildRangeValueOrError(rangeRef.Value, context);
        }

        if (node is FunctionCallNode fn && fn.FunctionName is "OFFSET" or "INDIRECT")
        {
            var value = EvaluateReferenceReturningFunction(fn, context);
            return value is ErrorValue or RangeValue ? value : ErrorValue.Value;
        }

        return ErrorValue.Value;
    }

    private ScalarValue EvaluateReferenceReturningFunction(FunctionCallNode node, IEvalContext context)
    {
        return node.FunctionName switch
        {
            "OFFSET"   => EvaluateOffsetReference(node, context),
            "INDIRECT" => EvaluateIndirectReference(node, context),
            _          => ErrorValue.Value
        };
    }

    private ScalarValue EvaluateIndirectReference(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count is < 1 or > 2) return ErrorValue.Value;

        var args = new List<ScalarValue>(node.Arguments.Count);
        foreach (var argument in node.Arguments)
        {
            var value = EvaluateNode(argument, context);
            if (value is ErrorValue error) return error;
            args.Add(value);
        }

        return BuiltInFunctions.IndirectReference(args, context);
    }

    private ScalarValue EvaluateOffset(FunctionCallNode node, IEvalContext context)
    {
        var reference = EvaluateOffsetReference(node, context);
        if (reference is ErrorValue error) return error;
        var range = (RangeValue)reference;
        if (range.RowCount == 1 && range.ColCount == 1)
            return range.Cells[0, 0];
        return range;
    }

    private ScalarValue EvaluateOffsetReference(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count is < 3 or > 5) return ErrorValue.Value;
        var baseArg = node.Arguments[0];

        uint baseRow, baseCol; int baseHeight, baseWidth; string? baseSheet = null;
        switch (baseArg)
        {
            case CellRefNode cellRef:
                if (cellRef.SheetName is not null && !context.SheetExists(cellRef.SheetName))
                    return ErrorValue.Ref;
                baseRow = cellRef.Row; baseCol = cellRef.ColumnNumber;
                baseHeight = 1; baseWidth = 1;
                baseSheet = cellRef.SheetName;
                break;
            case RangeRefNode rangeRef:
                if (rangeRef.SheetName is not null && !context.SheetExists(rangeRef.SheetName))
                    return ErrorValue.Ref;
                uint r0 = Math.Min(rangeRef.Start.Row, rangeRef.End.Row);
                uint r1 = Math.Max(rangeRef.Start.Row, rangeRef.End.Row);
                uint c0 = Math.Min(rangeRef.Start.ColumnNumber, rangeRef.End.ColumnNumber);
                uint c1 = Math.Max(rangeRef.Start.ColumnNumber, rangeRef.End.ColumnNumber);
                baseRow = r0; baseCol = c0;
                baseHeight = (int)(r1 - r0 + 1);
                baseWidth = (int)(c1 - c0 + 1);
                baseSheet = rangeRef.SheetName;
                break;
            case FullColumnRangeRefNode fullColumnRange:
                if (fullColumnRange.SheetName is not null && !context.SheetExists(fullColumnRange.SheetName))
                    return ErrorValue.Ref;
                uint fullColumnStart = CellAddress.ColumnNameToNumber(fullColumnRange.StartColumnName);
                uint fullColumnEnd = CellAddress.ColumnNameToNumber(fullColumnRange.EndColumnName);
                uint fc0 = Math.Min(fullColumnStart, fullColumnEnd);
                uint fc1 = Math.Max(fullColumnStart, fullColumnEnd);
                baseRow = 1; baseCol = fc0;
                baseHeight = (int)CellAddress.MaxRow;
                baseWidth = (int)(fc1 - fc0 + 1);
                baseSheet = fullColumnRange.SheetName;
                break;
            case FullRowRangeRefNode fullRowRange:
                if (fullRowRange.SheetName is not null && !context.SheetExists(fullRowRange.SheetName))
                    return ErrorValue.Ref;
                uint fr0 = Math.Min(fullRowRange.StartRow, fullRowRange.EndRow);
                uint fr1 = Math.Max(fullRowRange.StartRow, fullRowRange.EndRow);
                baseRow = fr0; baseCol = 1;
                baseHeight = (int)(fr1 - fr0 + 1);
                baseWidth = (int)CellAddress.MaxCol;
                baseSheet = fullRowRange.SheetName;
                break;
            case NamedRangeNode nm:
                var nr = context.TryResolveNamedRange(nm.Name);
                if (nr is null) return ErrorValue.Name;
                var g = nr.Value;
                uint nr0 = Math.Min(g.Start.Row, g.End.Row);
                uint nr1 = Math.Max(g.Start.Row, g.End.Row);
                uint nc0 = Math.Min(g.Start.Col, g.End.Col);
                uint nc1 = Math.Max(g.Start.Col, g.End.Col);
                baseRow = nr0; baseCol = nc0;
                baseHeight = (int)(nr1 - nr0 + 1);
                baseWidth = (int)(nc1 - nc0 + 1);
                baseSheet = context.TryGetSheetName(g.Start.Sheet);
                break;
            default:
                return ErrorValue.Value;
        }

        var rowsArg = EvaluateNode(node.Arguments[1], context);
        if (rowsArg is ErrorValue er) return er;
        var colsArg = EvaluateNode(node.Arguments[2], context);
        if (colsArg is ErrorValue ec) return ec;
        var rowsCoerced = CoerceToNumber(rowsArg);
        if (rowsCoerced is ErrorValue erc) return erc;
        var colsCoerced = CoerceToNumber(colsArg);
        if (colsCoerced is ErrorValue ecc) return ecc;
        double dRows = ((NumberValue)rowsCoerced).Value;
        double dCols = ((NumberValue)colsCoerced).Value;
        if (!double.IsFinite(dRows) || !double.IsFinite(dCols)) return ErrorValue.Value;
        long rowsOff = (long)Math.Truncate(dRows);
        long colsOff = (long)Math.Truncate(dCols);

        int height = baseHeight;
        int width = baseWidth;
        if (node.Arguments.Count >= 4 && node.Arguments[3] is not OmittedArgumentNode)
        {
            var hArg = EvaluateNode(node.Arguments[3], context);
            if (hArg is ErrorValue eh) return eh;
            if (hArg is not BlankValue)
            {
                var hc = CoerceToNumber(hArg);
                if (hc is ErrorValue ehc) return ehc;
                double dh = ((NumberValue)hc).Value;
                if (!double.IsFinite(dh)) return ErrorValue.Value;
                height = (int)Math.Truncate(dh);
            }
        }
        if (node.Arguments.Count == 5 && node.Arguments[4] is not OmittedArgumentNode)
        {
            var wArg = EvaluateNode(node.Arguments[4], context);
            if (wArg is ErrorValue ew) return ew;
            if (wArg is not BlankValue)
            {
                var wc = CoerceToNumber(wArg);
                if (wc is ErrorValue ewc) return ewc;
                double dw = ((NumberValue)wc).Value;
                if (!double.IsFinite(dw)) return ErrorValue.Value;
                width = (int)Math.Truncate(dw);
            }
        }
        if (height < 0 || width < 0) return ErrorValue.Value;
        if (height == 0 || width == 0) return ErrorValue.Ref;

        long startRow = (long)baseRow + rowsOff;
        long startCol = (long)baseCol + colsOff;
        long endRow = startRow + height - 1;
        long endCol = startCol + width - 1;
        long r0Final = Math.Min(startRow, endRow);
        long r1Final = Math.Max(startRow, endRow);
        long c0Final = Math.Min(startCol, endCol);
        long c1Final = Math.Max(startCol, endCol);
        if (r0Final < 1 || c0Final < 1 ||
            r1Final > FreeX.Core.Model.CellAddress.MaxRow ||
            c1Final > FreeX.Core.Model.CellAddress.MaxCol)
            return ErrorValue.Ref;

        int rowSpan = (int)(r1Final - r0Final + 1);
        int colSpan = (int)(c1Final - c0Final + 1);
        if ((long)rowSpan * colSpan > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Ref;

        var cells = new ScalarValue[rowSpan, colSpan];
        for (int ri = 0; ri < rowSpan; ri++)
            for (int ci = 0; ci < colSpan; ci++)
            {
                cells[ri, ci] = baseSheet is not null
                    ? context.GetCellValue(baseSheet, (uint)(r0Final + ri), (uint)(c0Final + ci))
                    : context.GetCellValue((uint)(r0Final + ri), (uint)(c0Final + ci));
            }
        return new RangeValue(cells, (uint)r0Final, (uint)c0Final) { SheetName = baseSheet };
    }

    private ScalarValue EvaluateSwitch(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count < 3) return ErrorValue.Value;
        var expr = EvaluateArrayOperand(node.Arguments[0], context);
        if (expr is ErrorValue e) return e;
        if (expr is RangeValue exprRange) return EvaluateSwitchExpressionRange(node, context, exprRange);
        bool hasDefault = (node.Arguments.Count - 1) % 2 == 1;
        int pairCount = (node.Arguments.Count - 1) / 2;
        for (int i = 0; i < pairCount; i++)
        {
            var val = EvaluateNode(node.Arguments[1 + i * 2], context);
            if (val is ErrorValue ve) return ve;
            if (BuiltInFunctions.ScalarEquals(expr, val))
                return EvaluateArrayOperand(node.Arguments[1 + i * 2 + 1], context);
        }
        return hasDefault ? EvaluateArrayOperand(node.Arguments[^1], context) : ErrorValue.NA;
    }

    private ScalarValue EvaluateSwitchExpressionRange(FunctionCallNode node, IEvalContext context, RangeValue exprRange)
    {
        var valueCache = new Dictionary<int, ScalarValue>();
        var resultCache = new Dictionary<int, ScalarValue>();
        var cells = new ScalarValue[exprRange.RowCount, exprRange.ColCount];

        for (int r = 0; r < exprRange.RowCount; r++)
            for (int c = 0; c < exprRange.ColCount; c++)
                cells[r, c] = EvaluateSwitchElement(node, context, valueCache, resultCache, exprRange, r, c);

        return new RangeValue(cells, exprRange.StartRow, exprRange.StartCol) { SheetName = exprRange.SheetName };
    }

    private ScalarValue EvaluateSwitchElement(
        FunctionCallNode node,
        IEvalContext context,
        Dictionary<int, ScalarValue> valueCache,
        Dictionary<int, ScalarValue> resultCache,
        RangeValue exprRange,
        int row,
        int col)
    {
        var expr = exprRange.Cells[row, col];
        if (expr is ErrorValue error) return error;

        bool hasDefault = (node.Arguments.Count - 1) % 2 == 1;
        int pairCount = (node.Arguments.Count - 1) / 2;
        for (int i = 0; i < pairCount; i++)
        {
            int valueIndex = 1 + i * 2;
            if (!valueCache.TryGetValue(valueIndex, out var value))
            {
                value = EvaluateArrayOperand(node.Arguments[valueIndex], context);
                valueCache[valueIndex] = value;
            }

            var valueElement = value is RangeValue valueRange
                ? PickRangeElementForArrayResult(valueRange, row, col, exprRange.RowCount, exprRange.ColCount)
                : value;

            if (valueElement is ErrorValue valueError) return valueError;
            if (!BuiltInFunctions.ScalarEquals(expr, valueElement)) continue;

            int resultIndex = valueIndex + 1;
            if (!resultCache.TryGetValue(resultIndex, out var result))
            {
                result = EvaluateArrayOperand(node.Arguments[resultIndex], context);
                resultCache[resultIndex] = result;
            }

            return result is RangeValue resultRange
                ? PickRangeElementForArrayResult(resultRange, row, col, exprRange.RowCount, exprRange.ColCount)
                : result;
        }

        if (!hasDefault) return ErrorValue.NA;

        int defaultIndex = node.Arguments.Count - 1;
        if (!resultCache.TryGetValue(defaultIndex, out var defaultResult))
        {
            defaultResult = EvaluateArrayOperand(node.Arguments[defaultIndex], context);
            resultCache[defaultIndex] = defaultResult;
        }

        return defaultResult is RangeValue defaultRange
            ? PickRangeElementForArrayResult(defaultRange, row, col, exprRange.RowCount, exprRange.ColCount)
            : defaultResult;
    }

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

    private static bool IsConditionalAggregateRangeArgument(string name, int argIndex) =>
        name switch
        {
            "SUMIF" or "AVERAGEIF" => argIndex is 0 or 2,
            "COUNTIF" => argIndex == 0,
            "SUMIFS" or "AVERAGEIFS" => argIndex == 0 || (argIndex > 0 && (argIndex & 1) == 1),
            "COUNTIFS" => (argIndex & 1) == 0,
            _ => false
        };

    private static ScalarValue CoerceToNumber(ScalarValue v) => v switch
    {
        ErrorValue e => e,
        NumberValue => v,
        BlankValue => new NumberValue(0),
        BoolValue b => new NumberValue(b.Value ? 1 : 0),
        TextValue t when ExcelTextNumberParser.TryParse(t.Value, out var d) =>
            new NumberValue(d),
        TextValue => ErrorValue.Value,
        DateTimeValue dt => new NumberValue(dt.Value),
        _ => ErrorValue.Value
    };

    private static bool TryCoerceToNumberValue(ScalarValue value, out double number)
    {
        if (value is NumberValue n)
        {
            number = n.Value;
            return true;
        }

        if (value is BoolValue b)
        {
            number = b.Value ? 1 : 0;
            return true;
        }

        if (value is BlankValue)
        {
            number = 0;
            return true;
        }

        if (value is DateTimeValue dt)
        {
            number = dt.Value;
            return true;
        }

        if (value is TextValue t && ExcelTextNumberParser.TryParse(t.Value, out var parsed))
        {
            number = parsed;
            return true;
        }

        number = 0;
        return false;
    }

    private static ErrorValue NumericCoercionError(ScalarValue value) =>
        value is ErrorValue error ? error : ErrorValue.Value;

    private static string ValueToString(ScalarValue v) => v switch
    {
        TextValue t => t.Value,
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        BlankValue => "",
        ErrorValue e => e.Code,
        _ => v.ToString() ?? ""
    };

    // ── LET / LAMBDA evaluation ────────────────────────────────────────────

    private ScalarValue EvaluateLet(FunctionCallNode node, IEvalContext context)
    {
        // LET(name1, val1, ..., nameN, valN, calc_expr)
        // arg count must be odd and >= 3 (at least one binding pair + body)
        if (node.Arguments.Count < 3 || node.Arguments.Count % 2 == 0)
            return ErrorValue.Value;

        var bindings = new Dictionary<string, ScalarValue>(StringComparer.OrdinalIgnoreCase);
        var scoped = new ScopedEvalContext(context, bindings, this);

        int pairCount = (node.Arguments.Count - 1) / 2;
        for (int i = 0; i < pairCount; i++)
        {
            string? name = node.Arguments[i * 2] switch
            {
                NamedRangeNode nm => nm.Name,
                _                => null
            };
            if (name is not { } localName || !IsValidLocalFunctionName(localName)) return ErrorValue.Value;
            var value = EvaluateArrayOperand(node.Arguments[i * 2 + 1], scoped);
            if (value is ErrorValue error) return error;
            bindings[localName] = value;
        }

        return EvaluateNode(node.Arguments[^1], scoped);
    }

    private static ScalarValue EvaluateLambda(FunctionCallNode node, IEvalContext _)
    {
        // LAMBDA([param1, param2, ...,] body)
        // All args except the last must be identifier (NamedRangeNode) parameter names.
        if (node.Arguments.Count < 1) return ErrorValue.Value;

        var paramNames = new List<string>(node.Arguments.Count - 1);
        var seenParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < node.Arguments.Count - 1; i++)
        {
            if (node.Arguments[i] is NamedRangeNode nm)
            {
                if (!IsValidLambdaParameterName(nm.Name)) return ErrorValue.Value;
                if (!seenParamNames.Add(nm.Name)) return ErrorValue.Value;
                paramNames.Add(nm.Name);
            }
            else
                return ErrorValue.Value;
        }

        return new LambdaValue(paramNames, node.Arguments[^1]);
    }

    private static bool IsValidLocalFunctionName(string? name)
    {
        if (!IsValidExcelLocalName(name)) return false;

        return !ConflictsWithR1C1Reference(name!);
    }

    private static bool IsValidLambdaParameterName(string? name)
    {
        if (!IsValidExcelLocalName(name)) return false;
        if (name!.Contains('.', StringComparison.Ordinal)) return false;

        return !ConflictsWithR1C1Reference(name);
    }

    private static bool IsValidExcelLocalName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        char first = name[0];
        if (!char.IsLetter(first) && first != '_' && first != '\\') return false;

        for (int i = 1; i < name.Length; i++)
        {
            char ch = name[i];
            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '.' && ch != '\\')
                return false;
        }

        return true;
    }

    private static bool ConflictsWithR1C1Reference(string name)
    {
        var upper = name.ToUpperInvariant();
        if (upper is "R" or "C") return true;

        if (upper[0] == 'C')
            return upper.Length > 1 && AllDigits(upper, 1, upper.Length);

        if (upper[0] != 'R') return false;

        int index = 1;
        while (index < upper.Length && char.IsDigit(upper[index]))
            index++;

        if (index == upper.Length)
            return index > 1;

        if (upper[index] != 'C') return false;
        index++;

        return index == upper.Length || AllDigits(upper, index, upper.Length);
    }

    private static bool AllDigits(string text, int start, int end)
    {
        if (start >= end) return false;
        for (int i = start; i < end; i++)
            if (!char.IsDigit(text[i]))
                return false;
        return true;
    }

    private ScalarValue InvokeLambdaWithArgs(LambdaValue lambda, IReadOnlyList<FormulaNode> argNodes, IEvalContext context)
    {
        if (argNodes.Count != lambda.Parameters.Count) return ErrorValue.Value;
        if (lambda.Parameters.Any(ConflictsWithR1C1Reference)) return ErrorValue.Value;

        var args = new ScalarValue[argNodes.Count];
        for (int i = 0; i < argNodes.Count; i++)
            args[i] = argNodes[i] is OmittedArgumentNode
                ? OmittedLambdaArgumentValue.Instance
                : EvaluateArrayOperand(argNodes[i], context);
        return context.InvokeLambda(lambda, args);
    }

    // ── Evaluation contexts ────────────────────────────────────────────────

    private sealed class SheetEvalContext : IEvalContext
    {
        private readonly Sheet _sheet;
        private readonly FreeX.Core.Model.Workbook? _workbook;
        private readonly FormulaEvaluator _evaluator;
        private readonly FreeX.Core.Model.CellAddress? _currentCellAddress;
        private Dictionary<string, FreeX.Core.Model.Sheet?>? _sheetNameCache;

        public readonly Sheet SourceSheet;

        public SheetEvalContext(
            Sheet sheet,
            FreeX.Core.Model.Workbook? workbook,
            FormulaEvaluator evaluator,
            FreeX.Core.Model.CellAddress? currentCellAddress)
        {
            _sheet = sheet;
            SourceSheet = sheet;
            _workbook = workbook;
            _evaluator = evaluator;
            _currentCellAddress = currentCellAddress;
        }

        public ScalarValue GetCellValue(uint row, uint col) => _sheet.GetValue(row, col);

        public ScalarValue GetCellValue(string sheetName, uint row, uint col)
        {
            var target = ResolveSheet(sheetName);
            if (target is null) return ErrorValue.Ref;
            return target.GetValue(row, col);
        }

        public IReadOnlyList<ScalarValue> GetRangeValues(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var r0 = Math.Min(startRow, endRow); var r1 = Math.Max(startRow, endRow);
            var c0 = Math.Min(startCol, endCol); var c1 = Math.Max(startCol, endCol);
            var values = CreateRangeValueList(r0, c0, r1, c1);
            if (values is null) return [new RangeMaterializationErrorValue(ErrorValue.Ref)];
            for (var r = r0; r <= r1; r++)
                for (var c = c0; c <= c1; c++)
                    values.Add(_sheet.GetValue(r, c));
            return values;
        }

        public IReadOnlyList<ScalarValue> GetRangeValues(string sheetName, uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var target = ResolveSheet(sheetName);
            if (target is null) return [ErrorValue.Ref];
            var r0 = Math.Min(startRow, endRow); var r1 = Math.Max(startRow, endRow);
            var c0 = Math.Min(startCol, endCol); var c1 = Math.Max(startCol, endCol);
            var values = CreateRangeValueList(r0, c0, r1, c1);
            if (values is null) return [new RangeMaterializationErrorValue(ErrorValue.Ref)];
            for (var r = r0; r <= r1; r++)
                for (var c = c0; c <= c1; c++)
                    values.Add(target.GetValue(r, c));
            return values;
        }

        private static List<ScalarValue>? CreateRangeValueList(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var count = FormulaSafetyLimits.GetRangeCellCount(startRow, startCol, endRow, endCol);
            return count <= FormulaSafetyLimits.MaxMaterializedRangeCells
                ? new List<ScalarValue>((int)count)
                : null;
        }

        public FreeX.Core.Model.GridRange? TryResolveNamedRange(string name)
        {
            if (_workbook is null) return null;
            if (_workbook.TryGetNamedRange(name, out var range))
                return range;
            return null;
        }

        public string? TryGetSheetName(FreeX.Core.Model.SheetId sheetId)
            => _workbook?.GetSheet(sheetId)?.Name;

        public bool SheetExists(string sheetName) => ResolveSheet(sheetName) is not null;

        public bool IsRowHidden(uint row) => _sheet.IsRowEffectivelyHidden(row);

        public bool IsRowHidden(string sheetName, uint row)
            => _workbook?.GetSheet(sheetName)?.IsRowEffectivelyHidden(row) ?? false;

        public bool IsRowFilterHidden(uint row) => _sheet.FilterHiddenRows.Contains(row);

        public bool IsRowFilterHidden(string sheetName, uint row)
            => _workbook?.GetSheet(sheetName)?.FilterHiddenRows.Contains(row) ?? false;

        public FreeX.Core.Model.Sheet? CurrentSheet => _sheet;

        public FreeX.Core.Model.Workbook? CurrentWorkbook => _workbook;

        public FreeX.Core.Model.CellAddress? CurrentCellAddress => _currentCellAddress;

        public FreeX.Core.Model.Cell? TryGetCell(uint row, uint col) => _sheet.GetCell(row, col);

        public FreeX.Core.Model.Cell? TryGetCell(string sheetName, uint row, uint col)
            => ResolveSheet(sheetName)?.GetCell(row, col);

        public ScalarValue? TryResolveLambdaBinding(string name) => null;

        public FreeX.Core.Model.Sheet? ResolveSheetForFastRange(string? sheetName)
            => sheetName is null ? _sheet : ResolveSheet(sheetName);

        public ScalarValue InvokeLambda(LambdaValue lambda, IReadOnlyList<ScalarValue> args)
        {
            if (args.Count != lambda.Parameters.Count) return ErrorValue.Value;
            var bindings = new Dictionary<string, ScalarValue>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lambda.Parameters.Count; i++)
                bindings[lambda.Parameters[i]] = args[i];
            return _evaluator.EvaluateNode(lambda.Body, new ScopedEvalContext(this, bindings, _evaluator));
        }

        private FreeX.Core.Model.Sheet? ResolveSheet(string sheetName)
        {
            if (_workbook is null) return null;

            _sheetNameCache ??= new Dictionary<string, FreeX.Core.Model.Sheet?>(StringComparer.OrdinalIgnoreCase);
            if (_sheetNameCache.TryGetValue(sheetName, out var cachedSheet))
                return cachedSheet;

            var resolvedSheet = _workbook.GetSheet(sheetName);
            _sheetNameCache[sheetName] = resolvedSheet;
            return resolvedSheet;
        }
    }

    // Wraps an IEvalContext with an extra layer of local name→value bindings (from LET).
    // Bindings in this layer shadow the inner context and can be mutated by EvaluateLet
    // before the body is evaluated (enabling forward references within the same LET).
    private sealed class ScopedEvalContext : IEvalContext
    {
        private readonly IEvalContext _inner;
        private readonly Dictionary<string, ScalarValue> _bindings;
        private readonly FormulaEvaluator _evaluator;

        public ScopedEvalContext(IEvalContext inner, Dictionary<string, ScalarValue> bindings, FormulaEvaluator evaluator)
        {
            _inner = inner;
            _bindings = bindings;
            _evaluator = evaluator;
        }

        public ScalarValue GetCellValue(uint row, uint col) => _inner.GetCellValue(row, col);
        public ScalarValue GetCellValue(string sn, uint row, uint col) => _inner.GetCellValue(sn, row, col);
        public IReadOnlyList<ScalarValue> GetRangeValues(uint r0, uint c0, uint r1, uint c1) => _inner.GetRangeValues(r0, c0, r1, c1);
        public IReadOnlyList<ScalarValue> GetRangeValues(string sn, uint r0, uint c0, uint r1, uint c1) => _inner.GetRangeValues(sn, r0, c0, r1, c1);
        public FreeX.Core.Model.GridRange? TryResolveNamedRange(string name) => _inner.TryResolveNamedRange(name);
        public string? TryGetSheetName(FreeX.Core.Model.SheetId id) => _inner.TryGetSheetName(id);
        public bool SheetExists(string sn) => _inner.SheetExists(sn);
        public bool IsRowHidden(uint row) => _inner.IsRowHidden(row);
        public bool IsRowHidden(string sn, uint row) => _inner.IsRowHidden(sn, row);
        public bool IsRowFilterHidden(uint row) => _inner.IsRowFilterHidden(row);
        public bool IsRowFilterHidden(string sn, uint row) => _inner.IsRowFilterHidden(sn, row);
        public FreeX.Core.Model.Sheet? CurrentSheet => _inner.CurrentSheet;
        public FreeX.Core.Model.Workbook? CurrentWorkbook => _inner.CurrentWorkbook;
        public FreeX.Core.Model.CellAddress? CurrentCellAddress => _inner.CurrentCellAddress;
        public FreeX.Core.Model.Cell? TryGetCell(uint row, uint col) => _inner.TryGetCell(row, col);
        public FreeX.Core.Model.Cell? TryGetCell(string sn, uint row, uint col) => _inner.TryGetCell(sn, row, col);

        public ScalarValue? TryResolveLambdaBinding(string name) =>
            _bindings.TryGetValue(name, out var v) ? v : _inner.TryResolveLambdaBinding(name);

        public ScalarValue InvokeLambda(LambdaValue lambda, IReadOnlyList<ScalarValue> args)
        {
            if (args.Count != lambda.Parameters.Count) return ErrorValue.Value;
            var nb = new Dictionary<string, ScalarValue>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lambda.Parameters.Count; i++) nb[lambda.Parameters[i]] = args[i];
            return _evaluator.EvaluateNode(lambda.Body, new ScopedEvalContext(this, nb, _evaluator));
        }
    }
}

/// <summary>A first-class function value created by LAMBDA. Holds parameter names and the unevaluated body AST.</summary>
public sealed record LambdaValue(IReadOnlyList<string> Parameters, FormulaNode Body) : ScalarValue;

internal sealed record DirectTextLiteralValue(string Value) : ScalarValue;
internal sealed record ReferencedScalarValue(ScalarValue Value) : ScalarValue;
internal sealed record OmittedLambdaArgumentValue : ScalarValue
{
    public static readonly OmittedLambdaArgumentValue Instance = new();
}
