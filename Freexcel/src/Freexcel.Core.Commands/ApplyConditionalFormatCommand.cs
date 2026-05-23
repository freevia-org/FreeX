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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
            return protectedOutcome;
        if (ConditionalFormatValidator.Validate(_sheetId, _format) is { } validationOutcome)
            return validationOutcome;

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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
            return protectedOutcome;

        _removed = [];
        for (var i = sheet.ConditionalFormats.Count - 1; i >= 0; i--)
        {
            var rule = sheet.ConditionalFormats[i];
            if (rule.AppliesTo.Start.Sheet == _sheetId && _range.Overlaps(rule.AppliesTo))
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

/// <summary>
/// Atomically replaces all conditional formatting rules on a sheet.
/// Used by the Manage Rules dialog to commit reordering, edits, and deletions as one undo step.
/// </summary>
public sealed class ReplaceAllConditionalFormatsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<ConditionalFormat> _newRules;
    private List<ConditionalFormat>? _previousRules;

    public string Label => "Manage Conditional Formatting Rules";

    public ReplaceAllConditionalFormatsCommand(SheetId sheetId, IReadOnlyList<ConditionalFormat> newRules)
    {
        _sheetId = sheetId;
        _newRules = newRules;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
            return protectedOutcome;
        foreach (var rule in _newRules)
            if (ConditionalFormatValidator.Validate(_sheetId, rule) is { } validationOutcome)
                return validationOutcome;

        _previousRules = [.. sheet.ConditionalFormats];
        sheet.ConditionalFormats.Clear();
        sheet.ConditionalFormats.AddRange(_newRules);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousRules is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        if (sheet is null) return;
        sheet.ConditionalFormats.Clear();
        sheet.ConditionalFormats.AddRange(_previousRules);
    }
}

internal static class ConditionalFormatValidator
{
    public static CommandOutcome? Validate(SheetId sheetId, ConditionalFormat format)
    {
        if (format.AppliesTo.Start.Sheet != sheetId || format.AppliesTo.End.Sheet != sheetId)
            return new CommandOutcome(false, "Conditional format range must be on the target sheet.");
        if (!Enum.IsDefined(format.RuleType))
            return new CommandOutcome(false, "Conditional format rule type is not supported.");
        if (!Enum.IsDefined(format.Operator))
            return new CommandOutcome(false, "Conditional format operator is not supported.");

        return null;
    }
}
