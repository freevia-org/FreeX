using Avalonia;
using Avalonia.Controls;

namespace FreeW.App.Avalonia.Editing;

internal sealed partial class ScreenClipOverlay
{
    internal static Window CreateForVisualHarness()
    {
        var overlay = new ScreenClipOverlay(new PixelRect(0, 0, 560, 600), 1d);
        overlay.ApplySelectionVisual(overlay._selectionSession.Begin(80, 90));
        _ = overlay._selectionSession.Complete(360, 300);

        var canvas = (Canvas)overlay.Content!;
        overlay.Content = null;
        Canvas.SetLeft(overlay._selection, 80);
        Canvas.SetTop(overlay._selection, 90);
        overlay._selection.Width = 280;
        overlay._selection.Height = 210;
        overlay._selection.IsVisible = true;
        var surface = new Grid
        {
            Background = new global::Avalonia.Media.SolidColorBrush(
                global::Avalonia.Media.Color.FromRgb(0xE8, 0xEB, 0xF0)),
        };
        surface.Children.Add(new Border { Background = overlay.Background });
        surface.Children.Add(canvas);
        return new Window
        {
            Width = 560,
            Height = 600,
            Content = surface,
            Title = "Screen Clip Overlay Capture",
        };
    }
}
