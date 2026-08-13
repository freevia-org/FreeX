using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>Builds the portable image model for a host-encoded PNG screen capture.</summary>
public static class ScreenClipImageFactory
{
    public static InlineImage Create(byte[] pngBytes, int pixelWidth, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0)
            throw new ArgumentException("Screenshot bytes are empty.", nameof(pngBytes));

        var plan = ScreenClipPlanner.BuildImageInsertionPlan(pixelWidth, pixelHeight);
        return new InlineImage(pngBytes, plan.WidthPt, plan.HeightPt, plan.Format)
        {
            OriginalPixelWidth = plan.OriginalPixelWidth,
            OriginalPixelHeight = plan.OriginalPixelHeight,
        };
    }
}
