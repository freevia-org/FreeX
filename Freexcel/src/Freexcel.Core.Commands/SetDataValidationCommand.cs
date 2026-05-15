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

        var idx = sheet.DataValidations.FindIndex(r => r.Id == _rule.Id);
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
