namespace FreeX.App.Presentation.QuickAnalysis;

public sealed class QuickAnalysisPreviewIconRenderAdapter<TRoot, TElement>(
    IQuickAnalysisPreviewIconRenderPrimitives<TRoot, TElement> primitives)
    : IQuickAnalysisPreviewIconRenderSink
{
    private TRoot _root = default!;
    private bool _isInitialized;

    public TRoot Root =>
        _isInitialized
            ? _root
            : throw new InvalidOperationException("Quick Analysis icon rendering was not initialized.");

    public static TRoot Render(
        QuickAnalysisPreviewIconPlan plan,
        IQuickAnalysisPreviewIconRenderPrimitives<TRoot, TElement> renderPrimitives)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(renderPrimitives);

        var renderer = new QuickAnalysisPreviewIconRenderAdapter<TRoot, TElement>(renderPrimitives);
        QuickAnalysisPreviewIconRenderPlanner.Render(plan, renderer);
        return renderer.Root;
    }

    public void Begin(QuickAnalysisPreviewIconPlan plan)
    {
        _root = primitives.CreateRoot(plan);
        _isInitialized = true;
    }

    public void AddRectangle(QuickAnalysisPreviewIconRectangle rectangle)
    {
        Add(primitives.CreateRectangle(rectangle));
    }

    public void AddEllipse(QuickAnalysisPreviewIconEllipse ellipse)
    {
        Add(primitives.CreateEllipse(ellipse));
    }

    public void AddLine(QuickAnalysisPreviewIconLine line)
    {
        Add(primitives.CreateLine(line));
    }

    public void AddPolygon(QuickAnalysisPreviewIconPolygon polygon)
    {
        Add(primitives.CreatePolygon(polygon));
    }

    public void AddText(QuickAnalysisPreviewIconText text)
    {
        Add(primitives.CreateText(text));
    }

    private void Add(TElement element)
    {
        primitives.AddChild(Root, element);
    }
}

public interface IQuickAnalysisPreviewIconRenderPrimitives<TRoot, TElement>
{
    TRoot CreateRoot(QuickAnalysisPreviewIconPlan plan);

    TElement CreateRectangle(QuickAnalysisPreviewIconRectangle rectangle);

    TElement CreateEllipse(QuickAnalysisPreviewIconEllipse ellipse);

    TElement CreateLine(QuickAnalysisPreviewIconLine line);

    TElement CreatePolygon(QuickAnalysisPreviewIconPolygon polygon);

    TElement CreateText(QuickAnalysisPreviewIconText text);

    void AddChild(TRoot root, TElement element);
}
