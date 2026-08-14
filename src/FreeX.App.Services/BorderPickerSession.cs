using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Renderer-neutral border picker state shared by the WPF and Avalonia shells.
/// Renderers own pointer capture and focus; this session owns picker defaults and
/// the begin/cancel/consume lifecycle for interactive border drawing.
/// </summary>
public sealed class BorderPickerSession
{
    public BorderStyle Style { get; private set; } = BorderStyle.Thin;

    public CellColor Color { get; private set; } = CellColor.Black;

    public BorderDrawMode DrawMode { get; private set; } = BorderDrawMode.None;

    public bool IsDrawModeActive => DrawMode != BorderDrawMode.None;

    public void SetStyle(BorderStyle style) => Style = style;

    public void SetColor(CellColor color) => Color = color;

    public void BeginDrawMode(BorderDrawMode mode)
    {
        if (mode == BorderDrawMode.None)
            throw new ArgumentException("Border draw mode must be active.", nameof(mode));

        DrawMode = mode;
    }

    public void CancelDrawMode() => DrawMode = BorderDrawMode.None;

    public bool TryConsumeDrawPlan(out BorderDrawExecutionPlan plan)
    {
        if (!IsDrawModeActive)
        {
            plan = default;
            return false;
        }

        plan = new BorderDrawExecutionPlan(DrawMode, Style, Color);
        DrawMode = BorderDrawMode.None;
        return true;
    }
}

public readonly record struct BorderDrawExecutionPlan(
    BorderDrawMode Mode,
    BorderStyle Style,
    CellColor Color);
