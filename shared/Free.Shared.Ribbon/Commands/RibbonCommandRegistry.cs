namespace Free.Shared.Ribbon;

public interface IRibbonCommandRegistry
{
    void Register(RibbonCommandId id, IRibbonCommand command);
    bool TryGet(RibbonCommandId id, out IRibbonCommand? command);
}

/// <summary>
/// Maps command ids declared in a <see cref="RibbonDefinition"/> to the host's behavior.
/// A control whose id is unregistered renders disabled (the renderer consults this); we never throw.
/// </summary>
public sealed class RibbonCommandRegistry : IRibbonCommandRegistry
{
    private readonly Dictionary<RibbonCommandId, IRibbonCommand> _commands = new();

    public void Register(RibbonCommandId id, IRibbonCommand command)
        => _commands[id] = command;

    public bool TryGet(RibbonCommandId id, out IRibbonCommand? command)
        => _commands.TryGetValue(id, out command);
}
