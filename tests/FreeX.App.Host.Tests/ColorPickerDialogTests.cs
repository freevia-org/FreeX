using FreeX.Core.Model;
using FluentAssertions;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreeX.App.Host.Tests;

public sealed partial class ColorPickerDialogTests
{
    private static Button FindSwatchButton(Panel panel, CellColor color) =>
        panel.Children
            .OfType<Button>()
            .Single(button => button.Tag is CellColor swatchColor && swatchColor == color);

    private static CellColor GetForegroundPreviewColor(TextBlock preview)
    {
        var brush = preview.Foreground.Should().BeOfType<SolidColorBrush>().Subject;
        return new CellColor(brush.Color.R, brush.Color.G, brush.Color.B);
    }

    private static CellColor GetBackgroundPreviewColor(Border preview)
    {
        var brush = preview.Background.Should().BeOfType<SolidColorBrush>().Subject;
        return new CellColor(brush.Color.R, brush.Color.G, brush.Color.B);
    }
}
