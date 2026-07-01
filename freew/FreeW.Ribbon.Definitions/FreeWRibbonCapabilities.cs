namespace FreeW.Ribbon.Definitions;

/// <summary>
/// Describes the ribbon surface a host can safely expose. The WPF host keeps the full Windows gallery
/// surface; Avalonia keeps the currently backed portable subset and uses its own context adapters.
/// </summary>
public sealed record FreeWRibbonCapabilities(
    string Name,
    bool UseAvaloniaBackedSurface,
    string TableContextKey,
    string PictureContextKey,
    string DrawingContextKey,
    string ChartContextKey,
    string SmartArtContextKey)
{
    public static FreeWRibbonCapabilities Wpf { get; } = new(
        "WPF",
        UseAvaloniaBackedSurface: false,
        TableContextKey: "table",
        PictureContextKey: "picture",
        DrawingContextKey: "drawing",
        ChartContextKey: "chart",
        SmartArtContextKey: "smartart");

    public static FreeWRibbonCapabilities Avalonia { get; } = new(
        "Avalonia",
        UseAvaloniaBackedSurface: true,
        TableContextKey: "table",
        PictureContextKey: "picture",
        DrawingContextKey: "drawing",
        ChartContextKey: "chart",
        SmartArtContextKey: "smartart");
}
