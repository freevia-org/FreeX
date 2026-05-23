using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public interface IFilterCriterion
{
    bool Matches(ScalarValue value);
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
        return text.Contains(Text, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record TextDoesNotContainFilterCriterion(string Text) : IFilterCriterion
{
    public bool Matches(ScalarValue value)
    {
        var text = FilterValueFormatter.ToText(value);
        return !text.Contains(Text, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record TextBeginsWithFilterCriterion(string Text) : IFilterCriterion
{
    public bool Matches(ScalarValue value)
    {
        var text = FilterValueFormatter.ToText(value);
        return text.StartsWith(Text, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record TextEndsWithFilterCriterion(string Text) : IFilterCriterion
{
    public bool Matches(ScalarValue value)
    {
        var text = FilterValueFormatter.ToText(value);
        return text.EndsWith(Text, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record TextEqualsFilterCriterion(string Text) : IFilterCriterion
{
    public bool Matches(ScalarValue value)
    {
        var text = FilterValueFormatter.ToText(value);
        return text.Equals(Text, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record TextNotEqualsFilterCriterion(string Text) : IFilterCriterion
{
    public bool Matches(ScalarValue value)
    {
        var text = FilterValueFormatter.ToText(value);
        return !text.Equals(Text, StringComparison.OrdinalIgnoreCase);
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
    public bool Matches(ScalarValue value) =>
        value is NumberValue number && Math.Abs(number.Value - Expected) >= double.Epsilon;
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
    public bool Matches(ScalarValue value) =>
        value is DateTimeValue date && DateOnly.FromDateTime(date.ToDateTime()) != Expected;
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
    private HashSet<uint>? _previousHiddenRows;
    private HashSet<uint>? _previousFilterHiddenRows;

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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _previousHiddenRows = [.. sheet.HiddenRows];
        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];

        var filterCol = _range.Start.Col + _filterColOffset;
        for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
            sheet.FilterHiddenRows.Remove(row);

        for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
        {
            var value = sheet.GetValue(row, filterCol);
            if (!_criterion.Matches(value))
                sheet.FilterHiddenRows.Add(row);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenRows is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        sheet.HiddenRows.Clear();
        sheet.HiddenRows.UnionWith(_previousHiddenRows);
        sheet.FilterHiddenRows.Clear();
        if (_previousFilterHiddenRows is not null)
            sheet.FilterHiddenRows.UnionWith(_previousFilterHiddenRows);
    }
}
