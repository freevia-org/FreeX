namespace Free.Shared.Ribbon;

public enum RibbonMenuItemKind { Command, Separator }

public sealed record RibbonMenu(IReadOnlyList<RibbonMenuItem> Items)
{
    public static readonly RibbonMenu Empty = new(Array.Empty<RibbonMenuItem>());
}

public sealed record RibbonMenuItem(
    string Header,
    RibbonCommandId? CommandId = null,
    string? KeyTip = null,
    string? InputGesture = null,
    RibbonMenuItemKind Kind = RibbonMenuItemKind.Command,
    IReadOnlyList<RibbonMenuItem>? Children = null,
    RibbonCommandIcon? Icon = null)
{
    public IReadOnlyList<RibbonMenuItem> Children { get; init; } =
        Children ?? Array.Empty<RibbonMenuItem>();

    /// <summary>Whether the item is invokable. Defaults to <c>true</c>; context menus drive this
    /// from selection state (e.g. comment/filter availability).</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Checkable state. <c>null</c> (default) means the item is not checkable and renders as a
    /// plain command; a non-null value makes the item checkable with the given check state
    /// (e.g. the waterfall "Set as Total" toggle).</summary>
    public bool? IsChecked { get; init; }

    public static RibbonMenuItem Separator() =>
        new("", Kind: RibbonMenuItemKind.Separator);
}
