namespace FreeP.App.Avalonia;

internal sealed partial class AvaloniaSlideShowMediaController
{
    internal string? CaptionTextForTest(uint shapeId) =>
        _slots.FirstOrDefault(slot => slot.ShapeId == shapeId)?.CaptionText?.Text;

    internal void RefreshCaptionsForTest() => UpdateCaptions();
}
