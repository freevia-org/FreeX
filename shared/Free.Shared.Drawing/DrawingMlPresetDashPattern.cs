namespace Free.Shared.Drawing;

/// <summary>
/// DrawingML preset line dashes (ECMA-376 <c>ST_PresetLineDashVal</c>, <c>&lt;a:prstDash val="..."/&gt;</c>).
/// The numeric values match the per-app outline dash enums so adapters can map by name.
/// </summary>
public enum DrawingMlPresetDash
{
    /// <summary><c>solid</c></summary>
    Solid = 0,
    /// <summary><c>dash</c></summary>
    Dash = 1,
    /// <summary><c>dot</c></summary>
    Dot = 2,
    /// <summary><c>dashDot</c></summary>
    DashDot = 3,
    /// <summary><c>lgDash</c></summary>
    LongDash = 4,
    /// <summary><c>lgDashDot</c></summary>
    LongDashDot = 5,
    /// <summary><c>lgDashDotDot</c></summary>
    LongDashDotDot = 6,
    /// <summary><c>sysDash</c></summary>
    SystemDash = 7,
    /// <summary><c>sysDot</c></summary>
    SystemDot = 8,
    /// <summary><c>sysDashDot</c></summary>
    SystemDashDot = 9,
    /// <summary><c>sysDashDotDot</c></summary>
    SystemDashDotDot = 10,
}

/// <summary>
/// The one portable dash/gap table for DrawingML preset outline dashes. Renderers wrap the
/// returned lengths in their native dash type (WPF <c>DashStyle</c>, Avalonia <c>DashStyle</c> /
/// <c>StrokeDashArray</c>) and must not carry their own preset-to-array switch.
/// </summary>
/// <remarks>
/// Lengths alternate dash, gap, dash, gap... and are expressed in multiples of the line width,
/// which is the unit both WPF and Avalonia dash arrays use with flat caps. Values follow the
/// ECMA-376 preset definitions Microsoft Office renders (for example <c>lgDash</c> is 8 on, 3 off).
/// </remarks>
public static class DrawingMlPresetDashPattern
{
    private static readonly IReadOnlyList<double> Dot = [1, 3];
    private static readonly IReadOnlyList<double> Dash = [4, 3];
    private static readonly IReadOnlyList<double> LongDash = [8, 3];
    private static readonly IReadOnlyList<double> DashDot = [4, 3, 1, 3];
    private static readonly IReadOnlyList<double> LongDashDot = [8, 3, 1, 3];
    private static readonly IReadOnlyList<double> LongDashDotDot = [8, 3, 1, 3, 1, 3];
    private static readonly IReadOnlyList<double> SystemDash = [3, 1];
    private static readonly IReadOnlyList<double> SystemDot = [1, 1];
    private static readonly IReadOnlyList<double> SystemDashDot = [3, 1, 1, 1];
    private static readonly IReadOnlyList<double> SystemDashDotDot = [3, 1, 1, 1, 1, 1];

    /// <summary>
    /// Returns the dash/gap lengths (in line widths) for <paramref name="dash"/>, or
    /// <see langword="null"/> for a solid line (and for any undefined value).
    /// </summary>
    public static IReadOnlyList<double>? Get(DrawingMlPresetDash dash) => dash switch
    {
        DrawingMlPresetDash.Dot => Dot,
        DrawingMlPresetDash.Dash => Dash,
        DrawingMlPresetDash.LongDash => LongDash,
        DrawingMlPresetDash.DashDot => DashDot,
        DrawingMlPresetDash.LongDashDot => LongDashDot,
        DrawingMlPresetDash.LongDashDotDot => LongDashDotDot,
        DrawingMlPresetDash.SystemDash => SystemDash,
        DrawingMlPresetDash.SystemDot => SystemDot,
        DrawingMlPresetDash.SystemDashDot => SystemDashDot,
        DrawingMlPresetDash.SystemDashDotDot => SystemDashDotDot,
        _ => null,
    };

    /// <summary>
    /// Returns a fresh mutable copy of the pattern for APIs that take an owned array
    /// (for example Avalonia <c>StrokeDashArray</c>), or <see langword="null"/> for solid.
    /// </summary>
    public static double[]? ToArray(DrawingMlPresetDash dash) =>
        Get(dash) is { } pattern ? [.. pattern] : null;
}
