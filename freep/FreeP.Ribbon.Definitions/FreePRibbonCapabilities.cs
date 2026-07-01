namespace FreeP.Ribbon.Definitions;

/// <summary>
/// Describes the FreeP ribbon surface a host can safely expose.
/// </summary>
public sealed record FreePRibbonCapabilities(
    string Name,
    bool UseAvaloniaBackedSurface)
{
    public static FreePRibbonCapabilities Wpf { get; } = new(
        "WPF",
        UseAvaloniaBackedSurface: false);

    public static FreePRibbonCapabilities Avalonia { get; } = new(
        "Avalonia",
        UseAvaloniaBackedSurface: true);
}
