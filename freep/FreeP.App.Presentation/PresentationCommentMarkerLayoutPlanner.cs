using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

/// <summary>
/// Plans comment-anchor markers in the stage coordinate space shared by the
/// WPF and Avalonia presentation hosts.
/// </summary>
public static class PresentationCommentMarkerLayoutPlanner
{
    public const long DefaultSlideWidthEmu = 12_192_000;
    public const long DefaultSlideHeightEmu = 6_858_000;
    public const double NormalDiameter = 14;
    public const double SelectedDiameter = 18;
    public const double NormalBorderThickness = 1.5;
    public const double SelectedBorderThickness = 2;

    public static IReadOnlyList<PresentationCommentMarkerLayoutPlan> Build(
        IReadOnlyList<PresentationCommentDescriptor> comments,
        double stageWidth,
        double stageHeight,
        long slideWidthEmu,
        long slideHeightEmu,
        double canvasMargin = FreePShellVisualMetrics.CanvasMargin)
    {
        ArgumentNullException.ThrowIfNull(comments);

        if (comments.Count == 0
            || !double.IsFinite(stageWidth)
            || !double.IsFinite(stageHeight)
            || !double.IsFinite(canvasMargin)
            || stageWidth <= 0
            || stageHeight <= 0
            || canvasMargin < 0)
        {
            return [];
        }

        var availableWidth = stageWidth - (2 * canvasMargin);
        var availableHeight = stageHeight - (2 * canvasMargin);
        if (availableWidth <= 0 || availableHeight <= 0)
            return [];

        var resolvedSlideWidth = slideWidthEmu > 0 ? slideWidthEmu : DefaultSlideWidthEmu;
        var resolvedSlideHeight = slideHeightEmu > 0 ? slideHeightEmu : DefaultSlideHeightEmu;
        var scale = Math.Min(
            availableWidth / resolvedSlideWidth,
            availableHeight / resolvedSlideHeight);
        if (!double.IsFinite(scale) || scale <= 0)
            return [];

        var renderedWidth = resolvedSlideWidth * scale;
        var renderedHeight = resolvedSlideHeight * scale;
        var offsetX = canvasMargin + ((availableWidth - renderedWidth) / 2);
        var offsetY = canvasMargin + ((availableHeight - renderedHeight) / 2);

        var plans = new List<PresentationCommentMarkerLayoutPlan>(comments.Count);
        foreach (var comment in comments)
        {
            var diameter = comment.IsSelected ? SelectedDiameter : NormalDiameter;
            var radius = diameter / 2;
            var centerX = offsetX + (comment.Xemu * scale);
            var centerY = offsetY + (comment.Yemu * scale);

            plans.Add(new PresentationCommentMarkerLayoutPlan(
                comment.SlideIndex,
                comment.CommentIndex,
                comment.AccessibilityKey,
                $"{comment.Author}: {comment.TextPreview}",
                new LayoutRect(centerX - radius, centerY - radius, diameter, diameter),
                comment.IsSelected,
                comment.IsSelected ? SelectedBorderThickness : NormalBorderThickness));
        }

        return plans;
    }
}

public sealed record PresentationCommentMarkerLayoutPlan(
    int SlideIndex,
    int CommentIndex,
    string AutomationId,
    string ToolTip,
    LayoutRect Bounds,
    bool IsSelected,
    double BorderThickness);
