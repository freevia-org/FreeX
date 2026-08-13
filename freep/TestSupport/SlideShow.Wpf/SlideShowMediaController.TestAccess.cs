using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed partial class SlideShowMediaController
{
    internal string? CaptionTextForTest(uint shapeId) =>
        _slots.FirstOrDefault(slot => slot.CaptionTrack?.ShapeId == shapeId)?.CaptionText?.Text;

    internal void RefreshCaptionsForTest(TimeSpan? playbackPosition = null) =>
        UpdateCaptions(playbackPosition);

    internal uint? LastMediaClickShapeIdForTest { get; private set; }

    partial void ObserveMediaClick(SlideShowMediaClickPlan click) =>
        LastMediaClickShapeIdForTest = click.Media?.ShapeId;
}
