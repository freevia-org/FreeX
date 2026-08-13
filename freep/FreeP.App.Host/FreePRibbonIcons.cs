using Free.Shared.Ribbon.Wpf;

namespace FreeP.App.Host;

/// <summary>
/// Supplies icon fallbacks for legacy animation commands that are not controls in the current WPF ribbon
/// definition. Control icons come directly from definition metadata and must not be repeated here.
/// </summary>
internal static class FreePRibbonIcons
{
    /// <summary>Installs the FreeP legacy command-id to glyph resolver on the shared icon factory.</summary>
    public static void Install() => RibbonIconFactory.CommandIconKindResolver = Resolve;

    public static RibbonCommandIconKind? Resolve(string commandId) =>
        FallbackMap.TryGetValue(commandId, out var kind) ? kind : null;

    internal static IReadOnlyDictionary<string, RibbonCommandIconKind> Fallbacks => FallbackMap;

    private static readonly IReadOnlyDictionary<string, RibbonCommandIconKind> FallbackMap =
        new Dictionary<string, RibbonCommandIconKind>(StringComparer.OrdinalIgnoreCase)
        {
            // Legacy entrance effects retained by command registration and older documents.
            ["freep.anim.entrance.peek"] = RibbonCommandIconKind.ArrowRight,
            ["freep.anim.entrance.spiral"] = RibbonCommandIconKind.Rotate,
            ["freep.anim.entrance.swivel"] = RibbonCommandIconKind.Rotate,
            ["freep.anim.entrance.bounce"] = RibbonCommandIconKind.ArrowUp,
            ["freep.anim.entrance.float"] = RibbonCommandIconKind.ArrowUp,
            ["freep.anim.entrance.swoop"] = RibbonCommandIconKind.ArrowUp,
            ["freep.anim.entrance.boomerang"] = RibbonCommandIconKind.Effects,

            // Legacy exit effects retained by command registration and older documents.
            ["freep.anim.exit.peek-out"] = RibbonCommandIconKind.ArrowLeft,
            ["freep.anim.exit.spiral-out"] = RibbonCommandIconKind.Rotate,
            ["freep.anim.exit.swivel-out"] = RibbonCommandIconKind.Rotate,
            ["freep.anim.exit.bounce-out"] = RibbonCommandIconKind.ArrowDown,
            ["freep.anim.exit.float-out"] = RibbonCommandIconKind.ArrowDown,
            ["freep.anim.exit.swoop-out"] = RibbonCommandIconKind.ArrowDown,
            ["freep.anim.exit.boomerang-out"] = RibbonCommandIconKind.Effects,
        };
}
