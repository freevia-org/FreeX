using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    internal static bool MatchExactValue(ScalarValue candidate, ScalarValue lookupValue)
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
        var matcher = CompileCriteria(criteria);
        return matcher.Matches(cellValue);
    }

    private static CriteriaMatcher CompileCriteria(ScalarValue criteria) =>
        CriteriaMatcher.Create(criteria);

    private enum CriteriaMatcherKind : byte
    {
        AlwaysFalse,
        NumberEquals,
        BoolEquals,
        TextEquals,
        NumericOrTextEquals,
        WildcardText,
        NumericComparison,
        TextComparison,
        WildcardComparison
    }

    private enum CriteriaComparisonOp : byte
    {
        None,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        Equal,
        NotEqual
    }

    private readonly struct CriteriaMatcher
    {
        private readonly CriteriaMatcherKind _kind;
        private readonly CriteriaComparisonOp _op;
        private readonly string _text;
        private readonly double _number;
        private readonly bool _bool;

        private CriteriaMatcher(CriteriaMatcherKind kind, CriteriaComparisonOp op = CriteriaComparisonOp.None, string? text = null, double number = 0, bool boolean = false)
        {
            _kind = kind;
            _op = op;
            _text = text ?? string.Empty;
            _number = number;
            _bool = boolean;
        }

        public static CriteriaMatcher Create(ScalarValue criteria)
        {
            if (criteria is BlankValue)
                return new CriteriaMatcher(CriteriaMatcherKind.TextEquals, text: string.Empty);

            if (criteria is NumberValue cn)
                return new CriteriaMatcher(CriteriaMatcherKind.NumberEquals, number: cn.Value);

            if (criteria is DateTimeValue cdt)
                return new CriteriaMatcher(CriteriaMatcherKind.NumberEquals, number: cdt.Value);

            if (criteria is BoolValue cb)
                return new CriteriaMatcher(CriteriaMatcherKind.BoolEquals, boolean: cb.Value);

            if (criteria is not TextValue ct)
                return new CriteriaMatcher(CriteriaMatcherKind.AlwaysFalse);

            var crit = ct.Value;
            if (TrySplitCriteriaComparison(crit, out var op, out var rhs))
            {
                if (TryParseCriteriaNumber(rhs, out var rhsNum))
                    return new CriteriaMatcher(CriteriaMatcherKind.NumericComparison, op, number: rhsNum);

                return IsWildcardCriteria(rhs) && op is CriteriaComparisonOp.Equal or CriteriaComparisonOp.NotEqual
                    ? new CriteriaMatcher(CriteriaMatcherKind.WildcardComparison, op, rhs)
                    : new CriteriaMatcher(CriteriaMatcherKind.TextComparison, op, rhs);
            }

            if (IsWildcardCriteria(crit))
                return new CriteriaMatcher(CriteriaMatcherKind.WildcardText, text: crit);

            if (TryParseCriteriaNumber(crit, out var numericCriteria))
                return new CriteriaMatcher(CriteriaMatcherKind.NumericOrTextEquals, text: crit, number: numericCriteria);

            return new CriteriaMatcher(CriteriaMatcherKind.TextEquals, text: crit);
        }

        public bool Matches(ScalarValue cellValue) => _kind switch
        {
            CriteriaMatcherKind.NumberEquals =>
                TryCellNumber(cellValue, out double cellNumber) && cellNumber == _number,

            CriteriaMatcherKind.BoolEquals =>
                cellValue is BoolValue cvb && cvb.Value == _bool,

            CriteriaMatcherKind.TextEquals =>
                string.Equals(CriteriaComparableText(cellValue), _text, StringComparison.OrdinalIgnoreCase),

            CriteriaMatcherKind.NumericOrTextEquals =>
                TryCellNumber(cellValue, out double comparableNumber)
                    ? comparableNumber == _number
                    : string.Equals(CriteriaComparableText(cellValue), _text, StringComparison.OrdinalIgnoreCase),

            CriteriaMatcherKind.WildcardText =>
                cellValue is TextValue tv && WildcardMatch(tv.Value, _text, ignoreCase: true),

            CriteriaMatcherKind.NumericComparison =>
                MatchesNumericComparison(cellValue),

            CriteriaMatcherKind.TextComparison =>
                MatchesTextComparison(cellValue),

            CriteriaMatcherKind.WildcardComparison =>
                MatchesWildcardComparison(cellValue),

            _ => false
        };

        private bool MatchesNumericComparison(ScalarValue cellValue)
        {
            if (!TryCellNumber(cellValue, out double value)) return false;
            return _op switch
            {
                CriteriaComparisonOp.GreaterThan => value > _number,
                CriteriaComparisonOp.GreaterThanOrEqual => value >= _number,
                CriteriaComparisonOp.LessThan => value < _number,
                CriteriaComparisonOp.LessThanOrEqual => value <= _number,
                CriteriaComparisonOp.Equal => value == _number,
                CriteriaComparisonOp.NotEqual => value != _number,
                _ => false
            };
        }

        private bool MatchesTextComparison(ScalarValue cellValue)
        {
            var cellText = cellValue is TextValue tv ? tv.Value : ToText(cellValue);
            int cmp = string.Compare(cellText, _text, StringComparison.OrdinalIgnoreCase);
            return _op switch
            {
                CriteriaComparisonOp.GreaterThan => cmp > 0,
                CriteriaComparisonOp.GreaterThanOrEqual => cmp >= 0,
                CriteriaComparisonOp.LessThan => cmp < 0,
                CriteriaComparisonOp.LessThanOrEqual => cmp <= 0,
                CriteriaComparisonOp.Equal => cmp == 0,
                CriteriaComparisonOp.NotEqual => cmp != 0,
                _ => false
            };
        }

        private bool MatchesWildcardComparison(ScalarValue cellValue)
        {
            bool matches = cellValue is TextValue textValue && WildcardMatch(textValue.Value, _text, ignoreCase: true);
            return _op == CriteriaComparisonOp.Equal ? matches : !matches;
        }
    }

    private static bool TrySplitCriteriaComparison(string criteria, out CriteriaComparisonOp op, out string rhs)
    {
        if (criteria.StartsWith(">="))
        {
            op = CriteriaComparisonOp.GreaterThanOrEqual;
            rhs = criteria[2..];
            return true;
        }

        if (criteria.StartsWith("<="))
        {
            op = CriteriaComparisonOp.LessThanOrEqual;
            rhs = criteria[2..];
            return true;
        }

        if (criteria.StartsWith("<>"))
        {
            op = CriteriaComparisonOp.NotEqual;
            rhs = criteria[2..];
            return true;
        }

        if (criteria.StartsWith(">"))
        {
            op = CriteriaComparisonOp.GreaterThan;
            rhs = criteria[1..];
            return true;
        }

        if (criteria.StartsWith("<"))
        {
            op = CriteriaComparisonOp.LessThan;
            rhs = criteria[1..];
            return true;
        }

        if (criteria.StartsWith("="))
        {
            op = CriteriaComparisonOp.Equal;
            rhs = criteria[1..];
            return true;
        }

        op = CriteriaComparisonOp.None;
        rhs = string.Empty;
        return false;
    }

    private static bool TryParseCriteriaNumber(string text, out double number) =>
        ExcelTextNumberParser.TryParse(text, out number);

    private static string CriteriaComparableText(ScalarValue value) => value switch
    {
        TextValue text => text.Value,
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        _ when TryCellNumber(value, out double numericValue) => numericValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => ""
    };

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
    internal static bool WildcardMatch(string text, string pattern, bool ignoreCase)
    {
        var key = (pattern, ignoreCase);
        if (!WildcardCache.ContainsKey(key) &&
            WildcardCache.Count >= FormulaSafetyLimits.MaxRegexCacheEntries)
        {
            WildcardCache.Clear();
        }

        var regex = WildcardCache.GetOrAdd((pattern, ignoreCase), key =>
        {
            var opts = key.IgnoreCase ? RegexOptions.IgnoreCase | RegexOptions.Compiled : RegexOptions.Compiled;
            return new Regex(WildcardToRegexPattern(key.Pattern), opts, FormulaSafetyLimits.RegexTimeout);
        });
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
