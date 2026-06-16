namespace Free.Shared.Ribbon;

/// <summary>Ambient parameters passed to a command when it executes (e.g. selected gallery item).</summary>
public sealed record RibbonCommandContext(IReadOnlyDictionary<string, object?> Parameters)
{
    public static readonly RibbonCommandContext Empty =
        new(new Dictionary<string, object?>());
}

/// <summary>Render-time state of a command: enablement, checked-ness, value, and dynamic content.</summary>
public sealed record RibbonCommandState(
    bool IsEnabled = true,
    bool IsChecked = false,
    string? Value = null,
    object? DynamicContent = null)
{
    public static readonly RibbonCommandState Default = new();
}

public interface IRibbonCommand
{
    void Execute(RibbonCommandContext context);
}

public interface IRibbonStatefulCommand : IRibbonCommand
{
    RibbonCommandState GetState();
}
