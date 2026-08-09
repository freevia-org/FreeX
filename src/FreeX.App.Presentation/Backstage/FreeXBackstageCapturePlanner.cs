namespace FreeX.App.Presentation.Backstage;

public enum FreeXBackstageCaptureHost
{
    Wpf,
    Avalonia
}

public enum FreeXBackstageCapturePane
{
    Info,
    Export,
    Account
}

public sealed record FreeXBackstageCaptureSurfacePlan(
    FreeXBackstageCapturePane Pane,
    string SurfaceId,
    string PngFileName,
    double Width,
    double Height,
    string WpfViewMethod,
    string? WpfFocusEntryId,
    bool UsesCaptureOnlyAccountPane,
    string WpfNote);

/// <summary>
/// Stable Backstage parity-capture catalog. Renderers retain their own visual construction and bitmap
/// APIs; this planner owns surface ids, output names, dimensions, ordering, and WPF capture options.
/// </summary>
public static class FreeXBackstageCapturePlanner
{
    public const double CaptureWidth = 1120;
    public const double CaptureHeight = 720;

    public static IReadOnlyList<FreeXBackstageCaptureSurfacePlan> Build(FreeXBackstageCaptureHost host) =>
        host switch
        {
            FreeXBackstageCaptureHost.Wpf => [Info, Export, Account],
            FreeXBackstageCaptureHost.Avalonia => [Export, Info, Account],
            _ => throw new ArgumentOutOfRangeException(nameof(host), host, null)
        };

    public static FreeXBackstageCaptureSurfacePlan Get(
        FreeXBackstageCaptureHost host,
        string surfaceId) =>
        Build(host).SingleOrDefault(
            plan => string.Equals(plan.SurfaceId, surfaceId, StringComparison.Ordinal))
        ?? throw new ArgumentOutOfRangeException(nameof(surfaceId), surfaceId, "Unknown FreeX Backstage capture surface.");

    private static readonly FreeXBackstageCaptureSurfacePlan Info = Create(
        FreeXBackstageCapturePane.Info,
        "backstage.Info",
        "ShowInfoView");

    private static readonly FreeXBackstageCaptureSurfacePlan Export = Create(
        FreeXBackstageCapturePane.Export,
        "backstage.Export",
        "ShowHomeView",
        wpfFocusEntryId: "BackstageExportButton",
        wpfNote: "WPF Export is a backstage rail action (opens Export dialog); rendered the backstage rail host with Export focused.");

    private static readonly FreeXBackstageCaptureSurfacePlan Account = Create(
        FreeXBackstageCapturePane.Account,
        "backstage.Account",
        "ShowHomeView",
        wpfFocusEntryId: "BackstageAccountButton",
        usesCaptureOnlyAccountPane: true,
        wpfNote: "WPF Account is a backstage rail action; rendered a capture-only Account content pane with the Account entry focused.");

    private static FreeXBackstageCaptureSurfacePlan Create(
        FreeXBackstageCapturePane pane,
        string surfaceId,
        string wpfViewMethod,
        string? wpfFocusEntryId = null,
        bool usesCaptureOnlyAccountPane = false,
        string wpfNote = "") =>
        new(
            pane,
            surfaceId,
            surfaceId + ".png",
            CaptureWidth,
            CaptureHeight,
            wpfViewMethod,
            wpfFocusEntryId,
            usesCaptureOnlyAccountPane,
            wpfNote);
}
