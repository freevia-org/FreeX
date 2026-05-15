using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Adds or replaces a <see cref="DataValidation"/> rule on a sheet by Id.
/// Undo removes the rule (or restores the previous version when replacing by Id).
/// </summary>
public sealed class SetDataValidationCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly DataValidation _rule;
    private DataValidation? _previous;   // non-null only when replacing an existing rule with the same Id

    public string Label => "Set Data Validation";

    public SetDataValidationCommand(SheetId sheetId, DataValidation rule)
    {
        _sheetId = sheetId;
        _rule    = rule;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (_rule.AppliesTo.Start.Sheet != _sheetId || _rule.AppliesTo.End.Sheet != _sheetId)
            return new CommandOutcome(false, "Data validation range must be on the target sheet.");
        if (!Enum.IsDefined(_rule.Type))
            return new CommandOutcome(false, "Data validation type is not supported.");
        if (!Enum.IsDefined(_rule.Operator))
            return new CommandOutcome(false, "Data validation operator is not supported.");
        if (!Enum.IsDefined(_rule.AlertStyle))
            return new CommandOutcome(false, "Data validation alert style is not supported.");

        var idx = sheet.DataValidations.FindIndex(r =>
            r.Id == _rule.Id || r.AppliesTo == _rule.AppliesTo);
        if (idx >= 0)
        {
            _previous = sheet.DataValidations[idx];
            sheet.DataValidations[idx] = _rule;
        }
        else
        {
            _previous = null;
            sheet.DataValidations.Add(_rule);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);

        if (_previous is not null)
        {
            var idx = sheet.DataValidations.FindIndex(r => r.Id == _rule.Id);
            if (idx >= 0)
                sheet.DataValidations[idx] = _previous;
        }
        else
        {
            sheet.DataValidations.RemoveAll(r => r.Id == _rule.Id);
        }
    }
}

/// <summary>
/// Clears data validation rules that intersect a selected range.
/// </summary>
public sealed class ClearDataValidationCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private List<(int Index, DataValidation Rule)>? _removed;
    private List<(int Index, DataValidation Rule)>? _added;

    public string Label => "Clear Data Validation";

    public ClearDataValidationCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        _removed = [];
        _added = [];
        for (var i = sheet.DataValidations.Count - 1; i >= 0; i--)
        {
            var rule = sheet.DataValidations[i];
            if (!Intersects(rule.AppliesTo, _range))
                continue;

            _removed.Add((i, rule));
            sheet.DataValidations.RemoveAt(i);
            var replacements = Subtract(rule.AppliesTo, _range)
                .Select(range => CloneForRange(rule, range))
                .ToList();
            for (var r = replacements.Count - 1; r >= 0; r--)
            {
                var replacement = replacements[r];
                sheet.DataValidations.Insert(i, replacement);
                _added.Add((i, replacement));
            }
        }

        _removed.Reverse();
        _added.Reverse();
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_removed is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        if (_added is not null)
            foreach (var (_, rule) in _added)
                sheet.DataValidations.Remove(rule);

        foreach (var (index, rule) in _removed)
            sheet.DataValidations.Insert(Math.Min(index, sheet.DataValidations.Count), rule);
    }

    private static bool Intersects(GridRange a, GridRange b) =>
        a.Start.Sheet == b.Start.Sheet &&
        a.Start.Row <= b.End.Row &&
        a.End.Row >= b.Start.Row &&
        a.Start.Col <= b.End.Col &&
        a.End.Col >= b.Start.Col;

    private static IEnumerable<GridRange> Subtract(GridRange source, GridRange remove)
    {
        if (!Intersects(source, remove))
        {
            yield return source;
            yield break;
        }

        var top = Math.Max(source.Start.Row, remove.Start.Row);
        var bottom = Math.Min(source.End.Row, remove.End.Row);
        var left = Math.Max(source.Start.Col, remove.Start.Col);
        var right = Math.Min(source.End.Col, remove.End.Col);
        var sheet = source.Start.Sheet;

        if (source.Start.Row < top)
            yield return MakeRange(sheet, source.Start.Row, source.Start.Col, top - 1, source.End.Col);

        if (bottom < source.End.Row)
            yield return MakeRange(sheet, bottom + 1, source.Start.Col, source.End.Row, source.End.Col);

        if (source.Start.Col < left)
            yield return MakeRange(sheet, top, source.Start.Col, bottom, left - 1);

        if (right < source.End.Col)
            yield return MakeRange(sheet, top, right + 1, bottom, source.End.Col);
    }

    private static GridRange MakeRange(SheetId sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet, startRow, startCol), new CellAddress(sheet, endRow, endCol));

    private static DataValidation CloneForRange(DataValidation source, GridRange range) =>
        new()
        {
            AppliesTo = range,
            Type = source.Type,
            Operator = source.Operator,
            Formula1 = source.Formula1,
            Formula2 = source.Formula2,
            AllowBlank = source.AllowBlank,
            ShowDropdown = source.ShowDropdown,
            AlertStyle = source.AlertStyle,
            ShowInputMessage = source.ShowInputMessage,
            ShowErrorMessage = source.ShowErrorMessage,
            ErrorTitle = source.ErrorTitle,
            ErrorMessage = source.ErrorMessage,
            PromptTitle = source.PromptTitle,
            PromptMessage = source.PromptMessage
        };
}
