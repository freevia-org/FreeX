using FreeX.Core.Model;
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
    public bool Matches(ScalarValue value) =>
        value is DateTimeValue date && DateOnly.FromDateTime(date.ToDateTime()) == Expected;
}

public sealed record DateNotEqualsFilterCriterion(DateOnly Expected) : IFilterCriterion
{
    // Excel semantics: "does not equal" hides only values that ARE the matching date.
    // Non-date values (text/blank/bool/error) are a different type than the expected date,
    // so they are never "equal" to it and must stay visible (matching DateEquals' inverse).
    public bool Matches(ScalarValue value) =>
        !(value is DateTimeValue date && DateOnly.FromDateTime(date.ToDateTime()) == Expected);
}

public sealed record DateAfterFilterCriterion(DateOnly Threshold) : IFilterCriterion
{
    public bool Matches(ScalarValue value) =>
        value is DateTimeValue date && DateOnly.FromDateTime(date.ToDateTime()) > Threshold;
}

public sealed record DateOnOrAfterFilterCriterion(DateOnly Threshold) : IFilterCriterion
{
    public bool Matches(ScalarValue value) =>
        value is DateTimeValue date && DateOnly.FromDateTime(date.ToDateTime()) >= Threshold;
}

public sealed record DateBeforeFilterCriterion(DateOnly Threshold) : IFilterCriterion
{
    public bool Matches(ScalarValue value) =>
        value is DateTimeValue date && DateOnly.FromDateTime(date.ToDateTime()) < Threshold;
}

public sealed record DateOnOrBeforeFilterCriterion(DateOnly Threshold) : IFilterCriterion
{
    public bool Matches(ScalarValue value) =>
        value is DateTimeValue date && DateOnly.FromDateTime(date.ToDateTime()) <= Threshold;
}

public sealed record DateBetweenFilterCriterion(DateOnly Start, DateOnly End) : IFilterCriterion
{
    public bool Matches(ScalarValue value)
    {
        if (value is not DateTimeValue date)
            return false;

        var current = DateOnly.FromDateTime(date.ToDateTime());
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
        for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
        {
            var value = sheet.GetValue(row, filterCol);
            FilterHiddenRowUpdater.ApplyColumnOwnedVisibility(sheet, filterCol, row, _criterion.Matches(value));
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_undoSnapshot.HasSnapshot)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        _undoSnapshot.Restore(sheet);
    }
}
