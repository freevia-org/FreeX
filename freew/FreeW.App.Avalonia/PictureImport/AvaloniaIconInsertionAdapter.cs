using Free.Shared.Ribbon.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// Native SVG rasterization adapter for the shared icon-insertion policy. It translates an
/// Avalonia drawing into PNG pixels; source geometry and the resulting model size remain shared.
/// </summary>
internal static class AvaloniaIconInsertionAdapter
{
    public static InlineImage Rasterize(IconPickerSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        var drawing = SvgIconRasterizer.LoadFileToPaintedBounds(selection.Path);
        var drawingSize = drawing.Size;
        var surface = PictureInsertionPlanner.BuildVectorRasterSurface(
            drawingSize.Width,
            drawingSize.Height);
        var pngBytes = SvgIconRasterizer.RasterizeToPng(
            drawing,
            surface.PixelWidth,
            surface.PixelHeight);
        return PictureInsertionPlanner.CreatePngIcon(
            pngBytes,
            surface.PixelWidth,
            surface.PixelHeight);
    }
}
