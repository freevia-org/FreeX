using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>A renderer-neutral stroke: ARGB color, width, and optional dash pattern (in stroke widths).</summary>
public sealed record SelectionAdornerStroke(uint Argb, double Width, IReadOnlyList<double>? Dashes = null);

/// <summary>
/// The selection-chrome style table both native adorners paint from. Colors, widths, and dash
/// patterns used to be re-typed in the WPF adorner and the Avalonia layer.
/// </summary>
public static class SelectionAdornerStyle
{
    public const uint AccentArgb = 0xFF2196F3;

    public static SelectionAdornerStroke Selection { get; } = new(AccentArgb, 1.5);
    public static SelectionAdornerStroke Preview { get; } = new(0xCC2196F3, 1.5, [4.0, 2.0]);
    public static uint HandleFillArgb => 0xFFFFFFFF;
    public static SelectionAdornerStroke HandleBorder { get; } = new(AccentArgb, 1.5);
    public static SelectionAdornerStroke RotateHandleStem { get; } = new(AccentArgb, 1.0);
    public static uint RotateHandleFillArgb => AccentArgb;
    public static SelectionAdornerStroke RotateHandleBorder { get; } = new(0xFFFFFFFF, 1.0);
    public static SelectionAdornerStroke Marquee { get; } = new(0xBB2196F3, 1.0, [2.0, 2.0]);
    public static uint MarqueeFillArgb => 0x222196F3;
    public static SelectionAdornerStroke SnapGuide { get; } = new(0xCCE91E63, 1.0);
    public static uint EditPointFillArgb => 0xFFFFC107;
    public static SelectionAdornerStroke EditPointBorder { get; } = new(0xFFFFFFFF, 1.0);
    public static SelectionAdornerStroke EditPointPreview { get; } = new(0xFFFF9800, 1.5, [2.0, 2.0]);
    public const double EditPointRadius = 5.0;
    public const double EditPointPreviewRadiusExtra = 2.0;
}

public enum SelectionAdornerDrawOpKind
{
    SnapGuides,
    Marquee,
    SelectionRect,
    GeometryHandles,
    PreviewRect,
}

/// <summary>One paint step in the adorner's draw order.</summary>
public sealed record SelectionAdornerDrawOp(
    SelectionAdornerDrawOpKind Kind,
    SelectionAdornerRect Rect = default,
    bool DrawHandles = false,
    double RotationDeg = 0);

/// <summary>
/// Owns the selection adorner's draw order so the WPF adorner and the Avalonia layer only map each
/// step onto native drawing calls: snap guides, marquee, selection boxes (one group box carries the
/// handles for a multi-selection), geometry handles, per-member previews, then the live preview.
/// </summary>
public static class SelectionAdornerDrawPlanner
{
    public static IReadOnlyList<SelectionAdornerDrawOp> Plan(SelectionAdornerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var ops = new List<SelectionAdornerDrawOp>();

        if (state.SnapGuides is { Count: > 0 })
            ops.Add(new SelectionAdornerDrawOp(SelectionAdornerDrawOpKind.SnapGuides));

        if (state.MarqueeRect is { } marquee)
            ops.Add(new SelectionAdornerDrawOp(SelectionAdornerDrawOpKind.Marquee, marquee));

        if (state.Selections.Count == 1)
        {
            ops.Add(new SelectionAdornerDrawOp(
                SelectionAdornerDrawOpKind.SelectionRect,
                state.Selections[0].ScreenRect,
                DrawHandles: true));
        }
        else
        {
            foreach (var selection in state.Selections)
            {
                ops.Add(new SelectionAdornerDrawOp(
                    SelectionAdornerDrawOpKind.SelectionRect,
                    selection.ScreenRect));
            }

            if (state.SelectionBounds is { } groupBounds)
            {
                ops.Add(new SelectionAdornerDrawOp(
                    SelectionAdornerDrawOpKind.SelectionRect,
                    groupBounds,
                    DrawHandles: true));
            }
        }

        ops.Add(new SelectionAdornerDrawOp(SelectionAdornerDrawOpKind.GeometryHandles));

        foreach (var preview in state.TransformPreview)
        {
            ops.Add(new SelectionAdornerDrawOp(
                SelectionAdornerDrawOpKind.PreviewRect,
                ToRect(preview.ScreenBounds),
                RotationDeg: preview.RotationDeg));
        }

        if (state.PreviewRect is { } previewRect)
        {
            ops.Add(new SelectionAdornerDrawOp(
                SelectionAdornerDrawOpKind.PreviewRect,
                previewRect,
                RotationDeg: state.PreviewRotationDeg));
        }

        return ops;
    }

    private static SelectionAdornerRect ToRect(SlideScreenRect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);
}
