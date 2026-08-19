using FreeX.Core.Model;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FreeX.Core.Commands;

public interface IFilterCriterion
{
    bool Matches(ScalarValue value);
}

/// <summary>
/// Translates Excel-style wildcard patterns (? = any single character, * = any run of
/// characters, ~ escapes a following ?, *, or ~ as a literal) into anchored/unanchored
/// regex matches, matching the semantics used by AutoFilter custom filters and
/// Advanced Filter criteria.
/// </summary>
internal static class FilterWildcard
{
    public static bool IsMatch(string text, string pattern, bool anchorStart, bool anchorEnd)
    {
        var regexPattern = ToRegexPattern(pattern, anchorStart, anchorEnd);
        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool ContainsWildcardCharacter(string text) =>
        text.IndexOfAny(['*', '?', '~']) >= 0;

    private static string ToRegexPattern(string pattern, bool anchorStart, bool anchorEnd)
    {
        var builder = new StringBuilder(pattern.Length + 4);
        if (anchorStart)
            builder.Append('^');

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '~' && i + 1 < pattern.Length)
            {
                builder.Append(Regex.Escape(pattern[++i].ToString()));
            }
            else if (c == '*')
            {
                builder.Append(".*");
            }
            else if (c == '?')
            {
                builder.Append('.');
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
            }
        }

        if (anchorEnd)
            builder.Append('$');

        return builder.ToString();
    }
}

public sealed record CompositeFilterCriterion(
    IFilterCriterion First,
    IFilterCriterion Second,
    bool UseAnd) : IFilterCriterion
{
    public bool Matches(ScalarValue value) =>
        UseAnd
            ? First.Matches(value) && Second.Matches(value)
            : First.Matches(value) || Second.Matches(value);
}

public sealed record BlankFilterCriterion : IFilterCriterion
{
    public bool Matches(ScalarValue value) => value is BlankValue;
}

public sealed record NonBlankFilterCriterion : IFilterCriterion
{
    public bool Matches(ScalarValue value) => value is not BlankValue;
}

public sealed record TextContainsFilterCriterion(string Text) : IFilterCriterion
{
    public bool Matches(ScalarValue value)
    {
        var text = FilterValueFormatter.ToText(value);
        return FilterWildcard.IsMatch(text, Text, anchorStart: false, anchorEnd: false);
    }
}

public sealed record TextDoesNotContainFilterCriterion(string Text) : IFilterCriterion
{
    public bool Matches(ScalarValue value)
    {
        var text = FilterValueFormatter.ToText(value);
        return !FilterWildcard.IsMatch(text, Text, anchorStart: false, anchorEnd: false);
    }
}

public sealed record TextBeginsWithFilterCriterion(string Text) : IFilterCriterion
{
    public bool Matches(ScalarValue value)
    {
        var text = FilterValueFormatter.ToText(value);
        return FilterWildcard.IsMatch(text, Text, anchorStart: true, anchorEnd: false);
    }
}

public sealed record TextEndsWithFilterCriterion(string Text) : IFilterCriterion
{
    public bool Matches(ScalarValue value)
    {
        var text = FilterValueFormatter.ToText(value);
        return FilterWildcard.IsMatch(text, Text, anchorStart: false, anchorEnd: true);
    }
}

public sealed record TextEqualsFilterCriterion(string Text) : IFilterCriterion
{
    public bool Matches(ScalarValue value)
    {
        var text = FilterValueFormatter.ToText(value);
        return FilterWildcard.IsMatch(text, Text, anchorStart: true, anchorEnd: true);
    }
}

public sealed record TextNotEqualsFilterCriterion(string Text) : IFilterCriterion
{
    public bool Matches(ScalarValue value)
    {
        var text = FilterValueFormatter.ToText(value);
        return !FilterWildcard.IsMatch(text, Text, anchorStart: true, anchorEnd: true);
    }
}

public sealed record NumberGreaterThanFilterCriterion(double Threshold) : IFilterCriterion
{
    public bool Matches(ScalarValue value) => value is NumberValue number && number.Value > Threshold;
}

public sealed record NumberGreaterThanOrEqualFilterCriterion(double Threshold) : IFilterCriterion
{
    public bool Matches(ScalarValue value) => value is NumberValue number && number.Value >= Threshold;
}

public sealed record NumberLessThanFilterCriterion(double Threshold) : IFilterCriterion
{
    public bool Matches(ScalarValue value) => value is NumberValue number && number.Value < Threshold;
}

public sealed record NumberLessThanOrEqualFilterCriterion(double Threshold) : IFilterCriterion
{
    public bool Matches(ScalarValue value) => value is NumberValue number && number.Value <= Threshold;
}

public sealed record NumberEqualsFilterCriterion(double Expected) : IFilterCriterion
{
    public bool Matches(ScalarValue value) =>
        value is NumberValue number && Math.Abs(number.Value - Expected) < double.Epsilon;
}

public sealed record NumberNotEqualsFilterCriterion(double Expected) : IFilterCriterion
{
    // Excel semantics: "does not equal" hides only values that ARE the matching number.
    // Text, blanks, booleans, and errors are a different type than the expected number,
    // so they are never "equal" to it and must stay visible (matching NumberEquals' inverse).
    public bool Matches(ScalarValue value) =>
        !(value is NumberValue number && Math.Abs(number.Value - Expected) < double.Epsilon);
}

public sealed record NumberBetweenFilterCriterion(double Minimum, double Maximum) : IFilterCriterion
{
    public bool Matches(ScalarValue value) =>
        value is NumberValue number && number.Value >= Minimum && number.Value <= Maximum;
}

public sealed record DateEqualsFilterCriterion(DateOnly Expected) : IFilterCriterion
{
    // TryToDateTime, not ToDateTime: an out-of-range serial (negative, or beyond
    // DateTime.MaxValue -- reachable from a loaded file, date autofill extrapolation, or
    // Paste Special arithmetic on a date) must not crash the whole filter apply. Excel treats
    // such an unconvertible value as simply not matching the target date.
    public bool Matches(ScalarValue value) =>
        value is DateTimeValue date && date.TryToDateTime(out var dt) && DateOnly.FromDateTime(dt) == Expected;
}

public sealed record DateNotEqualsFilterCriterion(DateOnly Expected) : IFilterCriterion
{
    // Excel semantics: "does not equal" hides only values that ARE the matching date.
    // Non-date values (text/blank/bool/error) AND dates whose serial can't be converted are a
    // different "type" than the expected date, so they are never "equal" to it and must stay
    // visible (matching DateEquals' inverse) -- see TryToDateTime note on DateEqualsFilterCriterion.
    public bool Matches(ScalarValue value) =>
        !(value is DateTimeValue date && date.TryToDateTime(out var dt) && DateOnly.FromDateTime(dt) == Expected);
}

public sealed record DateAfterFilterCriterion(DateOnly Threshold) : IFilterCriterion
{
    // See TryToDateTime note on DateEqualsFilterCriterion.
    public bool Matches(ScalarValue value) =>
        value is DateTimeValue date && date.TryToDateTime(out var dt) && DateOnly.FromDateTime(dt) > Threshold;
}

public sealed record DateOnOrAfterFilterCriterion(DateOnly Threshold) : IFilterCriterion
{
    // See TryToDateTime note on DateEqualsFilterCriterion.
    public bool Matches(ScalarValue value) =>
        value is DateTimeValue date && date.TryToDateTime(out var dt) && DateOnly.FromDateTime(dt) >= Threshold;
}

public sealed record DateBeforeFilterCriterion(DateOnly Threshold) : IFilterCriterion
{
    // See TryToDateTime note on DateEqualsFilterCriterion.
    public bool Matches(ScalarValue value) =>
        value is DateTimeValue date && date.TryToDateTime(out var dt) && DateOnly.FromDateTime(dt) < Threshold;
}

public sealed record DateOnOrBeforeFilterCriterion(DateOnly Threshold) : IFilterCriterion
{
    // See TryToDateTime note on DateEqualsFilterCriterion.
    public bool Matches(ScalarValue value) =>
        value is DateTimeValue date && date.TryToDateTime(out var dt) && DateOnly.FromDateTime(dt) <= Threshold;
}

public sealed record DateBetweenFilterCriterion(DateOnly Start, DateOnly End) : IFilterCriterion
{
    // See TryToDateTime note on DateEqualsFilterCriterion.
    public bool Matches(ScalarValue value)
    {
        if (value is not DateTimeValue date || !date.TryToDateTime(out var dt))
            return false;

        var current = DateOnly.FromDateTime(dt);
        return current >= Start && current <= End;
    }
}

public sealed class FilterConditionCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private readonly IFilterCriterion _criterion;
    private FilterUndoSnapshot _undoSnapshot;
    // R38-commands-autofilter-advanced-2-2: keep the worksheet AutoFilter's <customFilters>
    // filterColumn model in sync with the interactively-applied custom criterion (AND/OR, wildcard,
    // comparison, date-bound), so it round-trips through XlsxWorksheetAutoFilterXmlMapper instead of
    // being silently dropped on save, matching the sibling FilterCommand/TopBottomFilterCommand/
    // AverageFilterCommand which already keep this in sync for their own criterion kinds.
    private List<WorksheetAutoFilterColumnModel>? _previousAutoFilterColumns;
    // R106-commands-autofilter-table-sync-1: WorksheetAutoFilterColumnSync above is a no-op whenever
    // _range is a structured table's own Range -- keep the TABLE's own FilterColumns model in sync
    // too (mirrors FilterCommand.ApplyToStructuredTableIfMatched for the value-list case, finding
    // H18), otherwise a Custom Filter applied from a Table's header dropdown hides/shows rows live
    // but is silently dropped from the table's <autoFilter> XML on save/reload.
    private StructuredTableFilterColumnSnapshot? _tableFilterSnapshot;

    public string Label => "Apply Filter";

    public FilterConditionCommand(SheetId sheetId, GridRange range, uint filterColOffset, IFilterCriterion criterion)
    {
        _sheetId = sheetId;
        _range = range;
        _filterColOffset = filterColOffset;
        _criterion = criterion;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, _filterColOffset) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _undoSnapshot.Capture(sheet);

        var filterCol = _range.Start.Col + _filterColOffset;

        // Best-effort: only mechanisms expressible as Excel <customFilter> elements (wildcard text
        // matches, numeric/date comparisons and AND/OR pairs of those, including between-bounds) are
        // persisted. Blank/non-blank criteria have no <customFilter> representation in Excel's schema
        // (they are the checklist's "Blanks" mechanism instead), so those intentionally leave the
        // model unmodified rather than emit a misleading customFilters entry.
        if (FilterCriterionAutoFilterModelBuilder.Build(_criterion) is { } built)
        {
            _previousAutoFilterColumns = WorksheetAutoFilterColumnSync.Apply(
                sheet,
                _range,
                (int)_filterColOffset,
                new WorksheetAutoFilterColumnModel(
                    ColumnId: (int)_filterColOffset,
                    Values: [],
                    IncludeBlank: false,
                    CustomFilters: built.Filters,
                    CustomFiltersAnd: built.And,
                    CustomFiltersAndRaw: null,
                    NativeCustomFiltersAttributes: null,
                    Top10: null,
                    DynamicFilter: null,
                    ColorFilter: null,
                    IconFilter: null,
                    DateGroups: [],
                    NativeFiltersAttributes: null,
                    NativeFilterXmls: []));

            // R106-commands-autofilter-table-sync-1: mirror the same custom criterion into the
            // owning structured table's FilterColumns model (a no-op when _range isn't a table's own
            // Range) -- unlike Top10/DynamicFilter, StructuredTableFilterColumnModel already has
            // first-class CustomFilters/CustomFiltersAnd fields (it round-trips a table's own
            // <customFilters> today), so no raw-XML passthrough is needed here.
            _tableFilterSnapshot = StructuredTableFilterColumnSync.Apply(
                sheet,
                _range,
                (int)_filterColOffset,
                new StructuredTableFilterColumnModel(
                    (int)_filterColOffset,
                    Values: [],
                    IncludeBlank: false,
                    CustomFilters: [.. built.Filters.Select(f => new StructuredTableCustomFilterModel(f.Operator, f.Value, f.NativeAttributes))],
                    CustomFiltersAnd: built.And,
                    NativeCustomFiltersAttributes: null,
                    NativeFilterXmls: []));
        }

        // R100-commands-filter-totalsrow-1: see FilterCommand.RecomputeHiddenRows -- exclude a
        // structured table's shown Totals Row from the custom-condition data set.
        var lastDataRow = StructuredTableEditEffects.GetFilterableLastRow(sheet, _range);
        // table-semantics-F1: see FilterHiddenRowUpdater.GetFilterableFirstRow -- a headerless
        // table's first row is itself a data row and must be evaluated against the criterion.
        var firstDataRow = FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, _range);
        for (uint row = firstDataRow; row <= lastDataRow; row++)
        {
            var value = sheet.GetValue(row, filterCol);
            FilterHiddenRowUpdater.ApplyColumnOwnedVisibility(sheet, filterCol, row, _criterion.Matches(value));
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        WorksheetAutoFilterColumnSync.Restore(sheet, _range, _previousAutoFilterColumns);
        StructuredTableFilterColumnSync.Restore(sheet, _tableFilterSnapshot);

        if (!_undoSnapshot.HasSnapshot)
            return;

        _undoSnapshot.Restore(sheet);
    }
}

/// <summary>
/// R38-commands-autofilter-advanced-2-2: converts an in-session <see cref="IFilterCriterion"/> (built
/// by <c>FilterInputParser</c>/<c>FilterCriterionInputParser</c> from a Custom AutoFilter dialog/text
/// prompt) into the <see cref="WorksheetAutoFilterCustomFilterModel"/> shape
/// <c>XlsxWorksheetAutoFilterXmlMapper</c> serializes as a worksheet AutoFilter column's
/// <c>&lt;customFilters&gt;</c> XML, so the criterion survives save/reopen instead of only affecting
/// the in-session hidden-row state. Returns <c>null</c> for criteria with no faithful
/// <c>&lt;customFilter&gt;</c> representation (blank/non-blank, or a composite whose operand is itself
/// unsupported) rather than emit an incorrect/incomplete entry.
/// </summary>
internal static class FilterCriterionAutoFilterModelBuilder
{
    public static (IReadOnlyList<WorksheetAutoFilterCustomFilterModel> Filters, bool And)? Build(IFilterCriterion criterion)
    {
        switch (criterion)
        {
            case CompositeFilterCriterion composite:
                return BuildSingle(composite.First) is { } first && BuildSingle(composite.Second) is { } second
                    ? (new[] { first, second }, composite.UseAnd)
                    : null;

            case NumberBetweenFilterCriterion between:
                return (new[]
                {
                    new WorksheetAutoFilterCustomFilterModel("greaterThanOrEqual", FormatNumber(between.Minimum)),
                    new WorksheetAutoFilterCustomFilterModel("lessThanOrEqual", FormatNumber(between.Maximum))
                }, true);

            case DateBetweenFilterCriterion between:
                return (new[]
                {
                    new WorksheetAutoFilterCustomFilterModel("greaterThanOrEqual", FormatDate(between.Start)),
                    new WorksheetAutoFilterCustomFilterModel("lessThanOrEqual", FormatDate(between.End))
                }, true);

            default:
                return BuildSingle(criterion) is { } single ? (new[] { single }, false) : null;
        }
    }

    private static WorksheetAutoFilterCustomFilterModel? BuildSingle(IFilterCriterion criterion) => criterion switch
    {
        TextContainsFilterCriterion c => new WorksheetAutoFilterCustomFilterModel(null, $"*{c.Text}*"),
        TextDoesNotContainFilterCriterion c => new WorksheetAutoFilterCustomFilterModel("notEqual", $"*{c.Text}*"),
        TextBeginsWithFilterCriterion c => new WorksheetAutoFilterCustomFilterModel(null, $"{c.Text}*"),
        TextEndsWithFilterCriterion c => new WorksheetAutoFilterCustomFilterModel(null, $"*{c.Text}"),
        TextEqualsFilterCriterion c => new WorksheetAutoFilterCustomFilterModel(null, c.Text),
        TextNotEqualsFilterCriterion c => new WorksheetAutoFilterCustomFilterModel("notEqual", c.Text),
        NumberGreaterThanFilterCriterion c => new WorksheetAutoFilterCustomFilterModel("greaterThan", FormatNumber(c.Threshold)),
        NumberGreaterThanOrEqualFilterCriterion c => new WorksheetAutoFilterCustomFilterModel("greaterThanOrEqual", FormatNumber(c.Threshold)),
        NumberLessThanFilterCriterion c => new WorksheetAutoFilterCustomFilterModel("lessThan", FormatNumber(c.Threshold)),
        NumberLessThanOrEqualFilterCriterion c => new WorksheetAutoFilterCustomFilterModel("lessThanOrEqual", FormatNumber(c.Threshold)),
        NumberEqualsFilterCriterion c => new WorksheetAutoFilterCustomFilterModel(null, FormatNumber(c.Expected)),
        NumberNotEqualsFilterCriterion c => new WorksheetAutoFilterCustomFilterModel("notEqual", FormatNumber(c.Expected)),
        DateEqualsFilterCriterion c => new WorksheetAutoFilterCustomFilterModel(null, FormatDate(c.Expected)),
        DateNotEqualsFilterCriterion c => new WorksheetAutoFilterCustomFilterModel("notEqual", FormatDate(c.Expected)),
        DateAfterFilterCriterion c => new WorksheetAutoFilterCustomFilterModel("greaterThan", FormatDate(c.Threshold)),
        DateOnOrAfterFilterCriterion c => new WorksheetAutoFilterCustomFilterModel("greaterThanOrEqual", FormatDate(c.Threshold)),
        DateBeforeFilterCriterion c => new WorksheetAutoFilterCustomFilterModel("lessThan", FormatDate(c.Threshold)),
        DateOnOrBeforeFilterCriterion c => new WorksheetAutoFilterCustomFilterModel("lessThanOrEqual", FormatDate(c.Threshold)),
        // Blank/NonBlank criteria have no <customFilter> equivalent in Excel's schema.
        _ => null
    };

    private static string FormatNumber(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// R120-avalonia-datatools-reapply-1: the inverse of <see cref="FilterCriterionAutoFilterModelBuilder"/>
/// -- rebuilds a live <see cref="IFilterCriterion"/> from the persisted
/// <see cref="WorksheetAutoFilterCustomFilterModel"/> list a worksheet's AutoFilter column carries
/// (<c>&lt;customFilters&gt;</c>), so a shell's Data &gt; Reapply can re-run a Custom AutoFilter
/// condition (wildcard text, numeric/date comparison, or an AND/OR pair of those) purely from durable
/// worksheet metadata instead of needing an in-session record of the exact criterion object that
/// applied it.
/// </summary>
public static class CustomFilterModelReconstructor
{
    public static IFilterCriterion? Reconstruct(
        IReadOnlyList<WorksheetAutoFilterCustomFilterModel> filters,
        bool useAnd)
    {
        if (filters.Count is 0 or > 2)
            return null;

        var built = new IFilterCriterion[filters.Count];
        for (var i = 0; i < filters.Count; i++)
        {
            if (filters[i].Value is not { } value)
                return null;

            built[i] = new PersistedCustomFilterCriterion(filters[i].Operator, value);
        }

        return built.Length == 1
            ? built[0]
            : new CompositeFilterCriterion(built[0], built[1], useAnd);
    }
}

/// <summary>
/// Re-evaluates a persisted (Operator, Value) &lt;customFilter&gt; pair against the CURRENT value of a
/// cell, dispatching on that value's own runtime <see cref="ScalarValue"/> type rather than on any type
/// recorded at the moment the criterion was first applied -- the persisted model keeps no such record
/// (<see cref="FilterCriterionAutoFilterModelBuilder"/> formats a number/date threshold into the same
/// plain numeric string either way), so the cell's own type is the only faithful signal left.
///
/// This exactly reproduces every forward mapping in <see cref="FilterCriterionAutoFilterModelBuilder"/>:
/// <list type="bullet">
/// <item>A comparison operator (greaterThan/greaterThanOrEqual/lessThan/lessThanOrEqual) only ever came
/// from a Number/Date criterion, so it is evaluated numerically against a
/// <see cref="NumberValue"/> cell, or against a <see cref="DateTimeValue"/> cell using the same
/// day-precision OADate serial <see cref="FilterCriterionAutoFilterModelBuilder"/> persisted the
/// threshold as -- and never matches any other cell type, mirroring
/// NumberGreaterThanFilterCriterion/DateAfterFilterCriterion (etc.) only ever matching their own type.</item>
/// <item>A null/"notEqual" operator additionally covers the Text family (Contains/Begins/Ends/Equals),
/// whose <c>Matches</c> methods all compare <see cref="FilterValueFormatter.ToText"/> of ANY cell type
/// against the (possibly wildcarded) pattern -- reproduced here the same way once the numeric/date
/// interpretation above does not apply (Value fails to parse, or the cell isn't Number/DateTimeValue).</item>
/// </list>
/// </summary>
internal sealed record PersistedCustomFilterCriterion(string? Operator, string Value) : IFilterCriterion
{
    public bool Matches(ScalarValue value)
    {
        var isNotEqual = string.Equals(Operator, "notEqual", StringComparison.OrdinalIgnoreCase);

        if (double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold))
        {
            switch (value)
            {
                case NumberValue number:
                    return CompareNumeric(number.Value, threshold, Operator);
                // TryToDateTime, not ToDateTime: an out-of-range serial must not crash the
                // filter re-apply (see the note on DateEqualsFilterCriterion). When the cell's
                // serial can't be converted, fall out of the switch to the text-comparison
                // fallback below -- the same place execution lands for any non-Number/Date cell
                // type, which already yields Excel's "unconvertible value doesn't match a
                // comparison operator, but non-blank text still resolves through notEqual"
                // behaviour.
                case DateTimeValue date when date.TryToDateTime(out var dateValue):
                    var cellSerial = DateOnly
                        .FromDateTime(dateValue)
                        .ToDateTime(TimeOnly.MinValue)
                        .ToOADate();
                    return CompareNumeric(cellSerial, threshold, Operator);
            }
        }

        // Comparison operators never originated from a Text-family criterion (see
        // FilterCriterionAutoFilterModelBuilder.BuildSingle), so once Number/DateTimeValue has been
        // ruled out above, only a null/notEqual operator can still faithfully match here.
        if (Operator is not null && !isNotEqual)
            return false;

        var text = FilterValueFormatter.ToText(value);
        var matched = FilterWildcard.IsMatch(text, Value, anchorStart: true, anchorEnd: true);
        return isNotEqual ? !matched : matched;
    }

    private static bool CompareNumeric(double actual, double threshold, string? op) => op switch
    {
        "greaterThan" => actual > threshold,
        "greaterThanOrEqual" => actual >= threshold,
        "lessThan" => actual < threshold,
        "lessThanOrEqual" => actual <= threshold,
        "notEqual" => Math.Abs(actual - threshold) >= double.Epsilon,
        _ => Math.Abs(actual - threshold) < double.Epsilon,
    };
}
