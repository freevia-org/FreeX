namespace Free.Shared.Ribbon;

/// <summary>Ambient parameters passed to a command when it executes (e.g. selected gallery item).</summary>
public sealed record RibbonCommandContext(IReadOnlyDictionary<string, object?> Parameters)
{
    /// <summary>Parameter key carrying a combo box / gallery's selected value (a string) at execute time.</summary>
    public const string SelectedValueKey = "SelectedValue";

    public static readonly RibbonCommandContext Empty =
        new(new Dictionary<string, object?>());

    /// <summary>Builds a context carrying a single selected value under <see cref="SelectedValueKey"/>.</summary>
    public static RibbonCommandContext ForSelectedValue(string? value) =>
        new(new Dictionary<string, object?> { [SelectedValueKey] = value });

    /// <summary>The selected value if present (and a string), otherwise null.</summary>
    public string? SelectedValue =>
        Parameters.TryGetValue(SelectedValueKey, out var value) ? value as string : null;
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

/// <summary>
/// Optional live-preview contract for gallery and menu commands. Renderers begin preview from
/// native hover or keyboard focus, cancel it when the item/flyout is left, and call
/// <see cref="IRibbonCommand.Execute"/> to commit the selected value.
/// </summary>
public interface IRibbonPreviewCommand : IRibbonCommand
{
    void BeginPreview(RibbonCommandContext context);

    void CancelPreview();
}

public interface IRibbonStatefulCommand : IRibbonCommand
{
    RibbonCommandState GetState();
}
