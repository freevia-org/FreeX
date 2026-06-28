namespace Free.Shared.Ribbon;

/// <summary>
/// Lightweight command wrapper for ribbon commands backed by a parameterless delegate.
/// </summary>
public sealed class ActionRibbonCommand(Action execute) : IRibbonCommand
{
    public void Execute(RibbonCommandContext context) => execute();
}

/// <summary>
/// Lightweight command wrapper for ribbon commands that need the full execution context.
/// </summary>
public sealed class ContextRibbonCommand(Action<RibbonCommandContext> execute) : IRibbonCommand
{
    public void Execute(RibbonCommandContext context) => execute(context);
}

/// <summary>
/// Lightweight command wrapper for combo boxes, galleries, and other value-bearing controls.
/// </summary>
public sealed class ValueRibbonCommand(Action<string?> execute) : IRibbonCommand
{
    public void Execute(RibbonCommandContext context) => execute(context.SelectedValue);
}

/// <summary>
/// Shared no-op command for dropdown openers and deferred ribbon commands.
/// </summary>
public sealed class EmptyRibbonCommand : IRibbonCommand
{
    public static readonly EmptyRibbonCommand Instance = new();

    private EmptyRibbonCommand()
    {
    }

    public void Execute(RibbonCommandContext context)
    {
    }
}
