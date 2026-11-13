namespace FreeX.Core.Model;

/// <summary>
/// Maps the workbook outline dash onto the shared DrawingML preset dash so every renderer reads
/// its dash/gap lengths from <see cref="DrawingMlPresetDashPattern"/> instead of a local switch.
/// </summary>
public static class DrawingShapeOutlineDashExtensions
{
    public static DrawingMlPresetDash ToPresetDash(this DrawingShapeOutlineDash dash) => dash switch
    {
        DrawingShapeOutlineDash.Dash => DrawingMlPresetDash.Dash,
        DrawingShapeOutlineDash.Dot => DrawingMlPresetDash.Dot,
        DrawingShapeOutlineDash.DashDot => DrawingMlPresetDash.DashDot,
        DrawingShapeOutlineDash.LongDash => DrawingMlPresetDash.LongDash,
        DrawingShapeOutlineDash.LongDashDot => DrawingMlPresetDash.LongDashDot,
        DrawingShapeOutlineDash.LongDashDotDot => DrawingMlPresetDash.LongDashDotDot,
        DrawingShapeOutlineDash.SystemDash => DrawingMlPresetDash.SystemDash,
        DrawingShapeOutlineDash.SystemDot => DrawingMlPresetDash.SystemDot,
        DrawingShapeOutlineDash.SystemDashDot => DrawingMlPresetDash.SystemDashDot,
        _ => DrawingMlPresetDash.Solid,
    };
}
