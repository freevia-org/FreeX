using Free.Shared.Drawing;

namespace FreeP.Core.Model;

/// <summary>
/// Maps the presentation outline dash onto the shared DrawingML preset dash so every renderer
/// reads its dash/gap lengths from <see cref="DrawingMlPresetDashPattern"/> instead of a local switch.
/// </summary>
public static class OutlineDashExtensions
{
    public static DrawingMlPresetDash ToPresetDash(this OutlineDash dash) => dash switch
    {
        OutlineDash.Dash => DrawingMlPresetDash.Dash,
        OutlineDash.Dot => DrawingMlPresetDash.Dot,
        OutlineDash.DashDot => DrawingMlPresetDash.DashDot,
        OutlineDash.LongDash => DrawingMlPresetDash.LongDash,
        OutlineDash.LongDashDot => DrawingMlPresetDash.LongDashDot,
        OutlineDash.LongDashDotDot => DrawingMlPresetDash.LongDashDotDot,
        OutlineDash.SystemDash => DrawingMlPresetDash.SystemDash,
        OutlineDash.SystemDot => DrawingMlPresetDash.SystemDot,
        OutlineDash.SystemDashDot => DrawingMlPresetDash.SystemDashDot,
        _ => DrawingMlPresetDash.Solid,
    };
}
