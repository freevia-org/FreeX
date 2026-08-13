namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>
/// Dispatches shared Quick Analysis icon descriptors to native renderers. Platform shells still create
/// controls and brushes; this keeps descriptor traversal in one portable place.
/// </summary>
public static class QuickAnalysisPreviewIconRenderPlanner
{
    public static QuickAnalysisPreviewIconPlan Render(
        QuickAnalysisPreviewVisual visual,
        IQuickAnalysisPreviewIconRenderSink sink) =>
        Render(QuickAnalysisPreviewIconPlanner.Plan(visual), sink);

    public static QuickAnalysisPreviewIconPlan Render(
        QuickAnalysisPreviewIconPlan plan,
        IQuickAnalysisPreviewIconRenderSink sink)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sink);

        sink.Begin(plan);

        foreach (var element in plan.Elements)
            Dispatch(element, sink);

        return plan;
    }

    private static void Dispatch(
        QuickAnalysisPreviewIconElement element,
        IQuickAnalysisPreviewIconRenderSink sink)
    {
        switch (element)
        {
            case QuickAnalysisPreviewIconRectangle rectangle:
                sink.AddRectangle(rectangle);
                break;
            case QuickAnalysisPreviewIconEllipse ellipse:
                sink.AddEllipse(ellipse);
                break;
            case QuickAnalysisPreviewIconLine line:
                sink.AddLine(line);
                break;
            case QuickAnalysisPreviewIconPolygon polygon:
                sink.AddPolygon(polygon);
                break;
            case QuickAnalysisPreviewIconText text:
                sink.AddText(text);
                break;
        }
    }
}

public interface IQuickAnalysisPreviewIconRenderSink
{
    void Begin(QuickAnalysisPreviewIconPlan plan);

    void AddRectangle(QuickAnalysisPreviewIconRectangle rectangle);

    void AddEllipse(QuickAnalysisPreviewIconEllipse ellipse);

    void AddLine(QuickAnalysisPreviewIconLine line);

    void AddPolygon(QuickAnalysisPreviewIconPolygon polygon);

    void AddText(QuickAnalysisPreviewIconText text);
}
