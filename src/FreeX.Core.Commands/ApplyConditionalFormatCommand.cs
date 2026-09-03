using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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
        var idx = FindConditionalFormatIndex(sheet, _format.Id);
        if (idx >= 0)
        {
            // r249: the Conditional Formatting rules dialog pre-fills the rule being edited,
            // so pressing OK without changing anything replaces a rule with an equal one.
            // ConditionalFormat.SameAs compares content, because the type is a class with
            // reference equality and sixty members -- and its coverage contract derives that
            // member list from Clone, which the type has to keep correct anyway.
            if (sheet.ConditionalFormats[idx].SameAs(_format))
                return new CommandOutcome(true, IsNoOp: true);

            _previous = sheet.ConditionalFormats[idx];
            sheet.ConditionalFormats[idx] = _format;
        }
        else
        {
            _previous = null;
            // Newly-added rules must not silently reuse an existing rule's Priority: every
            // ConditionalFormat.Priority defaults to 1 (see ConditionalFormat.cs), and none of the
            // single-rule callers (ConditionalFormatRuleBuilder, the preset gallery planner, the icon
            // set catalog) assign a distinct one before constructing this command. Excel never leaves
            // two active rules tied at the same priority on one sheet, so give the new rule the next
            // free slot after the sheet's current max instead of trusting whatever it arrived with.
            // This only touches the incoming rule -- existing rules' Priority values are left exactly
            // as-is, so it cannot affect ManageConditionalFormatsPlanner's renumbering behavior
            // (ApplyRuleRange/MoveRule/Reprioritize), which always replaces the whole rule list itself.
            if (sheet.ConditionalFormats.Count > 0)
            {
                var maxPriority = sheet.ConditionalFormats.Max(f => f.Priority);
                if (_format.Priority <= maxPriority)
                    _format.Priority = maxPriority + 1;
            }
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
            var idx = FindConditionalFormatIndex(sheet, _format.Id);
            if (idx >= 0)
                sheet.ConditionalFormats[idx] = _previous;
        }
        else
        {
            // Remove the rule we added
            sheet.ConditionalFormats.RemoveAll(f => f.Id == _format.Id);
        }
    }

    private static int FindConditionalFormatIndex(Sheet sheet, Guid formatId)
    {
        for (var index = 0; index < sheet.ConditionalFormats.Count; index++)
        {
            if (sheet.ConditionalFormats[index].Id == formatId)
                return index;
        }

        return -1;
    }
}

public sealed class ClearConditionalFormatsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private List<ConditionalFormat>? _previousRules;

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

        _previousRules = [.. sheet.ConditionalFormats];

        // R40-commands-clear-delete-3-2: "Clear Rules from Selected Cells" in real Excel only
        // removes the rule from the cells the user actually selected -- a rule whose range extends
        // beyond the selection keeps applying to the un-selected portion. Deleting the whole rule
        // (the old behavior) silently wiped formatting on cells the user never touched. Subtract the
        // selected range from every range the rule covers; only drop the rule entirely when nothing
        // is left of its range afterward.
        var newRules = new List<ConditionalFormat>(sheet.ConditionalFormats.Count);
        foreach (var rule in sheet.ConditionalFormats)
        {
            if (rule.AppliesTo.Start.Sheet != _sheetId || !rule.Overlaps(_range))
            {
                newRules.Add(rule);
                continue;
            }

            var remaining = new List<GridRange>();
            foreach (var r in rule.AllRanges)
                remaining.AddRange(GridRangeSubtraction.Subtract(r, _range));

            if (remaining.Count == 0)
                continue; // whole rule range was selected -- drop the rule

            var shrunk = rule.Clone();
            shrunk.AppliesTo = remaining[0];
            shrunk.AdditionalRanges = remaining.Count > 1 ? remaining.Skip(1).ToList() : null;
            newRules.Add(shrunk);
        }

        // r220: reference equality is exactly the right test here, and that is not a shortcut. A
        // rule the loop left alone is added to newRules BY REFERENCE; a rule it shrank is a fresh
        // Clone; a rule it dropped is missing. So "same count and every element the same object"
        // means the loop changed nothing -- Clear Rules over a selection no rule covers.
        if (newRules.Count == sheet.ConditionalFormats.Count
            && !newRules.Where((rule, index) => !ReferenceEquals(rule, sheet.ConditionalFormats[index])).Any())
        {
            _previousRules = null;
            return new CommandOutcome(true, IsNoOp: true);
        }

        sheet.ConditionalFormats.Clear();
        sheet.ConditionalFormats.AddRange(newRules);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousRules is null)
            return;

        var rules = ctx.GetSheet(_sheetId).ConditionalFormats;
        rules.Clear();
        rules.AddRange(_previousRules);
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

        // r200: the Manage Rules dialog rebuilds this list and commits whether or not the user
        // changed anything, so closing it with OK after only looking pushed an undo entry -- which
        // clears redo. Reference equality is the right test: an untouched rule is the very instance
        // the dialog was handed, and an edited one is a rebuilt object.
        if (_newRules.Count == sheet.ConditionalFormats.Count &&
            !_newRules.Where((rule, index) => !ReferenceEquals(rule, sheet.ConditionalFormats[index])).Any())
        {
            return new CommandOutcome(true, IsNoOp: true);
        }

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
