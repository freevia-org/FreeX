namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Ribbon ids that are pure menu openers: the button only opens its sub-menu and has no direct
/// command action, so it is deliberately absent from the command registry. The WPF parity suite
/// (FreeWRibbonParityTests) skips exactly this set for the same reason; registering them would mean
/// inventing an action for a control that Word also treats as a gallery opener.
/// </summary>
internal static class RibbonMenuOpenerIds
{
    private static readonly HashSet<string> Ids = new(StringComparer.Ordinal)
    {
        "freew.image-wrap",
        "freew.image-rotate",
        "freew.image-corrections",
        "freew.image-color",
        "freew.image-transparency",
        "freew.image-effects",
    };

    internal static bool IsMenuOpener(string id) => Ids.Contains(id);
}
