using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Adds a <see cref="ConditionalFormat"/> to a sheet.
/// Undo removes it (or restores the previous version when replacing by Id).
/// </summary>
public sealed class ApplyConditionalFormatCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly ConditionalFormat _format;
    private ConditionalFormat? _previous;   // non-null only when replacing an existing rule with the same Id

    public string Label => "Apply Conditional Format";

    public ApplyConditionalFormatCommand(SheetId sheetId, ConditionalFormat format)
    {
        _sheetId = sheetId;
        _format  = format;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        // Replace an existing rule that shares the same Id (for edits), or just add.
        var idx = sheet.ConditionalFormats.FindIndex(f => f.Id == _format.Id);
        if (idx >= 0)
        {
            _previous = sheet.ConditionalFormats[idx];
            sheet.ConditionalFormats[idx] = _format;
        }
        else
        {
            _previous = null;
            sheet.ConditionalFormats.Add(_format);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);

        if (_previous is not null)
        {
            // Restore the rule that was there before
            var idx = sheet.ConditionalFormats.FindIndex(f => f.Id == _format.Id);
            if (idx >= 0)
                sheet.ConditionalFormats[idx] = _previous;
        }
        else
        {
            // Remove the rule we added
            sheet.ConditionalFormats.RemoveAll(f => f.Id == _format.Id);
        }
    }
}

public sealed class ClearConditionalFormatsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private List<(int Index, ConditionalFormat Rule)>? _removed;

    public string Label => "Clear Conditional Formatting Rules";

    public ClearConditionalFormatsCommand(SheetId sheetId, GridRange range)
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
        for (var i = sheet.ConditionalFormats.Count - 1; i >= 0; i--)
        {
            var rule = sheet.ConditionalFormats[i];
            if (rule.AppliesTo.Start.Sheet == _sheetId && _range.Contains(rule.AppliesTo.Start))
            {
                _removed.Add((i, rule));
                sheet.ConditionalFormats.RemoveAt(i);
            }
        }

        _removed.Reverse();
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_removed is null)
            return;

        var rules = ctx.GetSheet(_sheetId).ConditionalFormats;
        foreach (var (index, rule) in _removed)
            rules.Insert(Math.Min(index, rules.Count), rule);
    }
}
