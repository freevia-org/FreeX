using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowMediaNativeEntryRequest(
    Slide Slide,
    double SlideWidthDip,
    double SlideHeightDip,
    double CanvasWidth,
    double CanvasHeight,
    IReadOnlyList<PresentationMediaTranscriptTrackDescriptor>? CaptionTracks = null,
    uint? PreferredCaptionShapeId = null,
    int? PreferredCaptionTrackIndex = null,
    int? CaptionSlideIndex = null,
    int? PreferredCaptionSlideIndex = null,
    bool ShowMediaControls = true,
    bool ShowNarration = true)
{
    public static SlideShowMediaNativeEntryRequest FromDisplayPlan(
        SlideShowRuntimeDisplayPlan plan,
        double canvasWidth,
        double canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(
            plan.Slide ?? throw new ArgumentException("The display plan does not contain a slide.", nameof(plan)),
            plan.Metrics.WidthDip,
            plan.Metrics.HeightDip,
            canvasWidth,
            canvasHeight,
            plan.CaptionTracks,
            plan.PreferredCaptionShapeId,
            plan.PreferredCaptionTrackIndex,
            plan.CaptionSlideIndex,
            plan.PreferredCaptionSlideIndex,
            plan.ShowMediaControls,
            plan.ShowNarration);
    }
}

/// <summary>
/// Owns the active-slide geometry and interaction policy shared by native media hosts.
/// Media backend sessions and native overlay controls remain renderer responsibilities.
/// </summary>
public sealed class SlideShowMediaNativeInteractionSession
{
    private Slide? _activeSlide;
    private double _slideWidthDip;
    private double _slideHeightDip;
    private double _canvasWidth;
    private double _canvasHeight;
    private bool _showMediaControls = true;
    private bool _showNarration = true;

    public Slide? ActiveSlide => _activeSlide;
    public double SlideWidthDip => _slideWidthDip;
    public double SlideHeightDip => _slideHeightDip;
    public double CanvasWidth => _canvasWidth;
    public double CanvasHeight => _canvasHeight;

    public SlideShowMediaSlideEntryPlan Enter(SlideShowMediaNativeEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Slide);

        _activeSlide = request.Slide;
        _slideWidthDip = request.SlideWidthDip;
        _slideHeightDip = request.SlideHeightDip;
        _canvasWidth = request.CanvasWidth;
        _canvasHeight = request.CanvasHeight;
        _showMediaControls = request.ShowMediaControls;
        _showNarration = request.ShowNarration;

        return SlideShowMediaInteractionPlanner.PlanSlideEntry(
            request.Slide,
            request.SlideWidthDip,
            request.SlideHeightDip,
            request.CanvasWidth,
            request.CanvasHeight,
            request.CaptionTracks,
            request.PreferredCaptionShapeId,
            request.PreferredCaptionTrackIndex,
            request.CaptionSlideIndex,
            request.PreferredCaptionSlideIndex,
            request.ShowMediaControls,
            request.ShowNarration);
    }

    public bool UpdateLayout(Slide slide, double canvasWidth, double canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(slide);
        if (_activeSlide is not null && !ReferenceEquals(_activeSlide, slide))
        {
            return false;
        }

        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        return true;
    }

    public void SetCanvasBounds(double canvasWidth, double canvasHeight)
    {
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
    }

    public void UpdateLayout(
        Slide slide,
        double slideWidthDip,
        double slideHeightDip,
        double canvasWidth,
        double canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(slide);
        _activeSlide = slide;
        _slideWidthDip = slideWidthDip;
        _slideHeightDip = slideHeightDip;
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
    }

    public SlideShowMediaClickPlan PlanClick(double canvasX, double canvasY)
    {
        if (_activeSlide is null)
        {
            return SlideShowMediaClickPlan.NotMedia;
        }

        return SlideShowMediaInteractionPlanner.PlanClick(
            _activeSlide,
            _slideWidthDip,
            _slideHeightDip,
            _canvasWidth,
            _canvasHeight,
            canvasX,
            canvasY,
            _showMediaControls,
            _showNarration);
    }

    public SlideShowMediaClickPlan PlanClick(
        Slide slide,
        double slideWidthDip,
        double slideHeightDip,
        double canvasWidth,
        double canvasHeight,
        double canvasX,
        double canvasY)
    {
        ArgumentNullException.ThrowIfNull(slide);
        return SlideShowMediaInteractionPlanner.PlanClick(
            slide,
            slideWidthDip,
            slideHeightDip,
            canvasWidth,
            canvasHeight,
            canvasX,
            canvasY,
            _showMediaControls,
            _showNarration);
    }

    public void Clear()
    {
        _activeSlide = null;
        _canvasWidth = 0;
        _canvasHeight = 0;
    }
}
