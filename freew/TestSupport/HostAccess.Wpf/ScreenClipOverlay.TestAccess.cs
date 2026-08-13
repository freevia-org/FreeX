using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FreeW.App.Host.Editing;

internal sealed partial class ScreenClipOverlay
{
    internal static Window CreateForVisualHarness(Window owner)
    {
        var overlay = new ScreenClipOverlay();
        var canvas = _DetachCanvas(overlay);
        Canvas.SetLeft(overlay._selection, 80);
        Canvas.SetTop(overlay._selection, 90);
        overlay._selection.Width = 280;
        overlay._selection.Height = 210;
        overlay._selection.Visibility = Visibility.Visible;

        var surface = new Grid { Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xEB, 0xF0)) };
        surface.Children.Add(new Border { Background = overlay.Background });
        surface.Children.Add(canvas);
        return new Window
        {
            Owner = owner,
            Width = 560,
            Height = 600,
            Content = surface,
            Title = "Screen Clip Overlay Capture",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
    }

    private static Canvas _DetachCanvas(ScreenClipOverlay overlay)
    {
        var canvas = (Canvas)overlay.Content;
        overlay.Content = null;
        return canvas;
    }
}
