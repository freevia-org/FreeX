using FreeX.App.Services;
using FreeX.Core.Model;
using FluentAssertions;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreeX.App.Host.Tests;

public sealed partial class ColorPickerDialogTests
{
    // R142-services-theme-colors-1: ColorPickerDialog now tags each swatch button with the whole
    // CellColorSwatch (so a click can recover which theme slot/tint an Accent1-6 swatch came from),
    // not just its resolved CellColor.
    private static Button FindSwatchButton(Panel panel, CellColor color) =>
        panel.Children
            .OfType<Button>()
            .Single(button => button.Tag is CellColorSwatch swatch && swatch.Color == color);

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
