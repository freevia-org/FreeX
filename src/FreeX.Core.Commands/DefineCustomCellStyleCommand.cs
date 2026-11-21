using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Adds a user-defined named cell style, or replaces the one with the same name (Excel's New/Modify Cell Style).</summary>
public sealed class DefineCustomCellStyleCommand : IWorkbookCommand
{
    private readonly CustomCellStyle _style;
    private int _previousIndex = -1;
    private CustomCellStyle? _previous;
    private bool _applied;

    public DefineCustomCellStyleCommand(CustomCellStyle style)
    {
        _style = style ?? throw new ArgumentNullException(nameof(style));
    }

    public string Label => "Define Cell Style";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _applied = false;
        var name = _style.Name?.Trim() ?? "";
        if (name.Length == 0)
            return new CommandOutcome(false, "A cell style needs a name.");
        if (name.Length > CustomCellStyle.MaxNameLength)
            return new CommandOutcome(false, "The cell style name is too long.");

        var styles = ctx.Workbook.CustomCellStyles;
        _previousIndex = styles.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        _previous = _previousIndex >= 0 ? styles[_previousIndex] : null;
        var definition = _style with { Name = name };
        if (_previous is not null && _previous.Style.Equals(definition.Style) && _previous.Parts == definition.Parts &&
            string.Equals(_previous.Name, name, StringComparison.Ordinal))
            return new CommandOutcome(true, IsNoOp: true);

        if (_previousIndex >= 0)
            styles[_previousIndex] = definition;
        else
            styles.Add(definition);

        _applied = true;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        var styles = ctx.Workbook.CustomCellStyles;
        var name = _style.Name.Trim();
        var index = styles.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (_previous is not null)
        {
            if (index >= 0)
                styles[index] = _previous;
        }
        else if (index >= 0)
        {
            styles.RemoveAt(index);
        }
    }
}
