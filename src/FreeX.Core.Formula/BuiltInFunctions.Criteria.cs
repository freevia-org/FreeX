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
    /// <param name="textPrefixMatch">
    /// When true, a bare (non-wildcard, non-numeric, non-operator) text criterion matches
    /// any cell whose text BEGINS WITH the criterion (case-insensitive), per Excel's
    /// documented database/Advanced-Filter criteria-range behavior (e.g. "Dav" matches
    /// "Davolio"). When false (the default, used by COUNTIF/SUMIF/etc. via
    /// <see cref="CompileCriteria"/>), a bare text criterion requires exact equality —
    /// only an explicit "=text" criterion forces exact match either way.
    /// </param>
    private static bool MatchesCriteria(ScalarValue cellValue, ScalarValue criteria, bool textPrefixMatch = false)
    {
        var matcher = CompileCriteria(criteria, textPrefixMatch);
        return matcher.Matches(cellValue);
    }

    internal static CriteriaMatcher CompileCriteria(ScalarValue criteria, bool textPrefixMatch = false) =>
        CriteriaMatcher.Create(criteria, textPrefixMatch);

    private enum CriteriaMatcherKind : byte
    {
        AlwaysFalse,
        NumberEquals,
        BoolEquals,
        TextEquals,
        TextBeginsWith,
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

    internal readonly struct CriteriaMatcher
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

        public static CriteriaMatcher Create(ScalarValue criteria, bool textPrefixMatch = false)
        {
            // Excel: a criteria that comes from an empty cell (BlankValue) matches cells equal to 0,
            // NOT blank cells. This is distinct from the empty string "" (TextValue) which matches blanks.
            if (criteria is BlankValue)
                return new CriteriaMatcher(CriteriaMatcherKind.NumberEquals, number: 0);

            // Round the criterion to 15 significant digits up front, matching CompareValues
            // in FormulaEvaluator.Operators.cs (the worksheet = operator). Without this, a
            // criteria value coming from a cell reference whose own raw double result differs
            // from its displayed/typed text only in the 16th+ significant digit (e.g. STDEV.S/
            // VAR results) would fail to match cells that the '=' operator treats as equal.
            if (criteria is NumberValue cn)
                return new CriteriaMatcher(CriteriaMatcherKind.NumberEquals, number: FormulaEvaluator.RoundTo15SignificantDigits(cn.Value));

            if (criteria is DateTimeValue cdt)
                return new CriteriaMatcher(CriteriaMatcherKind.NumberEquals, number: FormulaEvaluator.RoundTo15SignificantDigits(cdt.Value));

            if (criteria is BoolValue cb)
                return new CriteriaMatcher(CriteriaMatcherKind.BoolEquals, boolean: cb.Value);

            // An error-valued criteria cell (e.g. a database-criteria-range cell whose formula
            // evaluates to #REF!/#N/A/etc.) must propagate that error rather than silently being
            // treated as "never matches" — mirrors how SUMIF/SUMIFS/MAXIFS etc. explicitly check
            // `criteria is ErrorValue` and return it before ever reaching CompileCriteria. Those
            // call sites already guard against ErrorValue before calling here, so this only fires
            // for paths (like the DB functions' criteria-table cells) that don't pre-filter errors;
            // the thrown exception is converted back to the matching ErrorValue by the generic
            // built-in-function dispatch (see FormulaEvaluator.Functions.cs catch (FormulaEvalException)).
            if (criteria is ErrorValue ce)
                throw new FormulaEvalException(ce.Code, $"Criteria evaluated to error {ce.Code}");

            if (criteria is not TextValue ct)
                return new CriteriaMatcher(CriteriaMatcherKind.AlwaysFalse);

            var crit = ct.Value;
            if (TrySplitCriteriaComparison(crit, out var op, out var rhs))
            {
                if (TryParseCriteriaNumber(rhs, out var rhsNum))
                    return new CriteriaMatcher(CriteriaMatcherKind.NumericComparison, op, number: FormulaEvaluator.RoundTo15SignificantDigits(rhsNum));

                return IsWildcardCriteria(rhs) && op is CriteriaComparisonOp.Equal or CriteriaComparisonOp.NotEqual
                    ? new CriteriaMatcher(CriteriaMatcherKind.WildcardComparison, op, rhs)
                    : new CriteriaMatcher(CriteriaMatcherKind.TextComparison, op, rhs);
            }

            if (IsWildcardCriteria(crit))
                return new CriteriaMatcher(CriteriaMatcherKind.WildcardText, text: crit);

            if (TryParseCriteriaNumber(crit, out var numericCriteria))
                return new CriteriaMatcher(CriteriaMatcherKind.NumericOrTextEquals, text: crit, number: FormulaEvaluator.RoundTo15SignificantDigits(numericCriteria));

            return textPrefixMatch
                ? new CriteriaMatcher(CriteriaMatcherKind.TextBeginsWith, text: crit)
                : new CriteriaMatcher(CriteriaMatcherKind.TextEquals, text: crit);
        }

        public bool Matches(ScalarValue cellValue) => _kind switch
        {
            CriteriaMatcherKind.NumberEquals =>
                TryCellNumber(cellValue, out double cellNumber) && FormulaEvaluator.RoundTo15SignificantDigits(cellNumber) == _number,

            CriteriaMatcherKind.BoolEquals =>
                cellValue is BoolValue cvb && cvb.Value == _bool,

            CriteriaMatcherKind.TextEquals =>
                string.Equals(CriteriaComparableText(cellValue), _text, StringComparison.OrdinalIgnoreCase),

            CriteriaMatcherKind.TextBeginsWith =>
                CriteriaComparableText(cellValue).StartsWith(_text, StringComparison.OrdinalIgnoreCase),

            CriteriaMatcherKind.NumericOrTextEquals =>
                TryCellNumber(cellValue, out double comparableNumber)
                    ? FormulaEvaluator.RoundTo15SignificantDigits(comparableNumber) == _number
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
            // Excel rule: when the criterion is a number, text cells are treated as
            // "not equal to" (i.e. they are a different type and do not equal any number).
            // Only blank cells are excluded (blank coerces to 0 for equality, but for
            // *IF(S) matching, blank cells never match "<>0" — Excel treats blank as 0).
            // Concretely:
            //   "<>0"  → text cells count   (text ≠ 0),  blank cells don't (blank = 0)
            //   "=0"   → text cells don't   (text ≠ 0),  blank cells do    (blank = 0)
            //   ">5"   → text cells don't   (ordering across types is undefined)
            //   "<=5"  → text cells don't
            if (!TryCellNumber(cellValue, out double value))
            {
                // Non-numeric, non-blank cell: only "<>" (NotEqual) matches.
                // Blank is excluded entirely (returns false for all ops including "<>").
                if (cellValue is BlankValue) return false;
                return _op == CriteriaComparisonOp.NotEqual;
            }
            // Round the cell's numeric value to 15 significant digits before comparing,
            // matching CompareValues in FormulaEvaluator.Operators.cs (the worksheet
            // comparison operators) and this matcher's own criterion, which was rounded
            // at construction time above.
            value = FormulaEvaluator.RoundTo15SignificantDigits(value);
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
            // Excel rule: ordering ("<", "<=", ">", ">=") against a text-valued criterion is only
            // ever satisfied by an actual text cell — a number, boolean, date, or blank cell never
            // compares as greater/less than a text threshold (ordering across types is undefined,
            // mirroring MatchesNumericComparison's cross-type exclusion for numeric criteria).
            if (cellValue is not TextValue tv)
            {
                // Excel special-cases a blank cell against an empty-string threshold: a blank
                // cell coerces to "" for text equality (mirroring the engine's own equality
                // operator, FormulaEvaluator.Operators.cs, and the plain "" / "<>" criteria
                // paths which route through CriteriaComparableText/TextEquals). So bare "="
                // must match blanks and bare "<>" must NOT match blanks, even though every
                // other non-text cell (number/bool/date) still only satisfies NotEqual.
                if (cellValue is BlankValue && _text.Length == 0)
                {
                    return _op switch
                    {
                        CriteriaComparisonOp.Equal => true,
                        CriteriaComparisonOp.NotEqual => false,
                        _ => false
                    };
                }

                return _op switch
                {
                    CriteriaComparisonOp.NotEqual => true,
                    _ => false
                };
            }

            int cmp = string.Compare(tv.Value, _text, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>Simple Excel-style wildcard match (* = any chars, ? = any single char).</summary>
    internal static bool WildcardMatch(string text, string pattern, bool ignoreCase)
    {
        var regex = FormulaWildcardHelper.GetOrCreateRegex(pattern, ignoreCase);
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
