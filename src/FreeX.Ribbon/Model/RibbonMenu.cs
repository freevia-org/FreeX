namespace FreeX.Ribbon;

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
    IReadOnlyList<RibbonMenuItem>? Children = null)
{
    public IReadOnlyList<RibbonMenuItem> Children { get; init; } =
        Children ?? Array.Empty<RibbonMenuItem>();

    public static RibbonMenuItem Separator() =>
        new("", Kind: RibbonMenuItemKind.Separator);
}
