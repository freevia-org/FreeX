namespace FreeW.App.Presentation.Shell;

/// <summary>
/// Shared Read Mode choices and host-neutral presentation values.
/// WPF and Avalonia apply the returned values to their own chrome controls; neither host owns a
/// second copy of the Read Mode token vocabulary or the WPF-authority dimensions/colors.
/// </summary>
public static class FreeWReadModePlanner
{
    public const string NarrowColumn = "narrow";
    public const string DefaultColumn = "default";
    public const string WideColumn = "wide";

    public const string NoColor = "none";
    public const string SepiaColor = "sepia";
    public const string InverseColor = "inverse";

    public const double NarrowColumnWidth = 560;
    public const double DefaultColumnWidth = 760;
    public const double WideColumnWidth = 1024;

    public const string NoColorHex = "#FFFFFF";
    public const string SepiaColorHex = "#F0E0C0";
    public const string InverseColorHex = "#1E1E1E";

    public static double ColumnWidth(string? token) => NormalizeColumnWidth(token) switch
    {
        NarrowColumn => NarrowColumnWidth,
        WideColumn => WideColumnWidth,
        _ => DefaultColumnWidth,
    };

    public static string PageColorHex(string? token) => NormalizePageColor(token) switch
    {
        SepiaColor => SepiaColorHex,
        InverseColor => InverseColorHex,
        _ => NoColorHex,
    };

    public static string NormalizeColumnWidth(string? token) => token?.Trim().ToLowerInvariant() switch
    {
        NarrowColumn => NarrowColumn,
        WideColumn => WideColumn,
        _ => DefaultColumn,
    };

    public static string NormalizePageColor(string? token) => token?.Trim().ToLowerInvariant() switch
    {
        SepiaColor => SepiaColor,
        InverseColor => InverseColor,
        _ => NoColor,
    };
}
