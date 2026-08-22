using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Inserts one of the legacy Excel Form Controls which FreeX can render, interact with, and
/// persist as a standard VML/ctrlProps control. ActiveX and display-only legacy kinds are
/// intentionally not accepted by this authoring command.
/// </summary>
public sealed class AddFormControlCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly FormControlModel _control;
    private bool _added;

    public string Label => "Insert " + DisplayName(_control.Kind);
    public FormControlModel Control => _control;

    public AddFormControlCommand(SheetId sheetId, CellAddress anchor, FormControlKind kind)
    {
        _sheetId = sheetId;
        _control = CreateDefaultControl(anchor, kind);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_control.Anchor is not { } anchor ||
            anchor.Start.Sheet != _sheetId || anchor.End.Sheet != _sheetId)
        {
            return new CommandOutcome(false, "Form control anchor must be on the target sheet.");
        }

        if (!IsInsertableKind(_control.Kind))
            return new CommandOutcome(false, "Form control kind is not supported for insertion.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        sheet.FormControls.Add(_control);
        _added = true;
        return new CommandOutcome(true, AffectedCells: [anchor.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_added)
            return;

        ctx.GetSheet(_sheetId).FormControls.Remove(_control);
        _added = false;
    }

    public static bool IsInsertableKind(FormControlKind kind) => kind is
        FormControlKind.CheckBox or
        FormControlKind.OptionButton or
        FormControlKind.Button or
        FormControlKind.DropDown or
        FormControlKind.ListBox or
        FormControlKind.Spinner or
        FormControlKind.ScrollBar;

    private static FormControlModel CreateDefaultControl(CellAddress anchor, FormControlKind kind)
    {
        var (rowSpan, colSpan, caption) = kind switch
        {
            FormControlKind.CheckBox => (1u, 3u, "Check Box"),
            FormControlKind.OptionButton => (1u, 3u, "Option Button"),
            FormControlKind.Button => (1u, 3u, "Button"),
            FormControlKind.DropDown => (1u, 3u, (string?)null),
            FormControlKind.ListBox => (5u, 3u, (string?)null),
            FormControlKind.Spinner => (2u, 1u, (string?)null),
            FormControlKind.ScrollBar => (1u, 4u, (string?)null),
            _ => (1u, 1u, (string?)null),
        };

        var end = new CellAddress(
            anchor.Sheet,
            Math.Min(CellAddress.MaxRow, anchor.Row + rowSpan - 1),
            Math.Min(CellAddress.MaxCol, anchor.Col + colSpan - 1));

        return new FormControlModel
        {
            Kind = kind,
            Caption = caption,
            Anchor = new GridRange(anchor, end),
            Value = kind is FormControlKind.Spinner or FormControlKind.ScrollBar ? 0 : null,
            Min = kind is FormControlKind.Spinner or FormControlKind.ScrollBar ? 0 : null,
            Max = kind is FormControlKind.Spinner or FormControlKind.ScrollBar ? 100 : null,
            Increment = kind is FormControlKind.Spinner or FormControlKind.ScrollBar ? 1 : null,
            PageChange = kind == FormControlKind.ScrollBar ? 10 : null,
        };
    }

    private static string DisplayName(FormControlKind kind) => kind switch
    {
        FormControlKind.CheckBox => "Check Box",
        FormControlKind.OptionButton => "Option Button",
        FormControlKind.DropDown => "Drop-Down",
        FormControlKind.ListBox => "List Box",
        FormControlKind.ScrollBar => "Scroll Bar",
        FormControlKind.Spinner => "Spin Button",
        _ => "Button",
    };
}
