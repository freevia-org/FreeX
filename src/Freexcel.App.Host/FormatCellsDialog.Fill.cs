using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class FormatCellsDialog
{
    private sealed record FillPatternOption(CellFillPatternStyle Style, string Label);

    private static readonly FillPatternOption[] FillPatternOptions =
    [
        new(CellFillPatternStyle.None, "None"),
        new(CellFillPatternStyle.Solid, "Solid"),
        new(CellFillPatternStyle.Gray0625, "6.25% Gray"),
        new(CellFillPatternStyle.Gray125, "12.5% Gray"),
        new(CellFillPatternStyle.LightGray, "25% Gray"),
        new(CellFillPatternStyle.MediumGray, "50% Gray"),
        new(CellFillPatternStyle.DarkGray, "75% Gray"),
        new(CellFillPatternStyle.LightHorizontal, "Thin Horizontal Stripe"),
        new(CellFillPatternStyle.LightVertical, "Thin Vertical Stripe"),
        new(CellFillPatternStyle.LightDown, "Thin Reverse Diagonal Stripe"),
        new(CellFillPatternStyle.LightUp, "Thin Diagonal Stripe"),
        new(CellFillPatternStyle.LightGrid, "Thin Horizontal Crosshatch"),
        new(CellFillPatternStyle.LightTrellis, "Thin Diagonal Crosshatch"),
        new(CellFillPatternStyle.DarkHorizontal, "Horizontal Stripe"),
        new(CellFillPatternStyle.DarkVertical, "Vertical Stripe"),
        new(CellFillPatternStyle.DarkDown, "Reverse Diagonal Stripe"),
        new(CellFillPatternStyle.DarkUp, "Diagonal Stripe"),
        new(CellFillPatternStyle.DarkGrid, "Diagonal Crosshatch"),
        new(CellFillPatternStyle.DarkTrellis, "Thick Diagonal Crosshatch")
    ];

    private void DlgFontColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgFontColorBox, allowNoColor: false, "Font Color");

    private void DlgFontColorSwatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string colorText })
            DlgFontColorBox.Text = colorText;
    }

    private void DlgFillColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgFillColorBox, allowNoColor: true, "Fill Color");

    private void DlgFillPatternColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgFillPatternColorBox, allowNoColor: true, "Pattern Color");

    private void DlgFillSwatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string colorText })
        {
            DlgFillColorBox.Text = colorText;
            DlgClearFillCheck.IsChecked = string.IsNullOrEmpty(colorText);
        }
    }

    private void DlgFillPatternSwatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string colorText })
            DlgFillPatternColorBox.Text = colorText;
    }

    private void PickColorInto(TextBox target, bool allowNoColor, string title)
    {
        var initial = TryParseColor(target.Text);
        var dialog = new ColorPickerDialog(initial, allowNoColor) { Owner = this, Title = title };
        if (dialog.ShowDialog() != true)
            return;

        target.Text = dialog.SelectedColor is { } color ? ColorInputParser.FormatRgbColor(color) : "";
    }

    private void UpdateFillPreview()
    {
        if (DlgFillSamplePreview is null)
            return;

        var fillBrush = DlgClearFillCheck.IsChecked == true
            ? Brushes.White
            : BrushForColor(TryParseColor(DlgFillColorBox.Text), Brushes.White);
        var patternStyle = SelectedFillPatternStyle();
        var patternColor = TryParseColor(DlgFillPatternColorBox.Text);
        var sampleBrush = CreateFillPreviewBrush(fillBrush, patternStyle, patternColor);

        DlgFillBackgroundPreview.Background = fillBrush;
        DlgFillSamplePreview.Background = sampleBrush;
        DlgFillPatternSamplePreview.Background = sampleBrush;
        DlgFillSamplePreview.BorderBrush = patternStyle == CellFillPatternStyle.None
            ? SystemColors.ControlDarkBrush
            : BrushForColor(patternColor, Brushes.Black);
        DlgFillPatternSamplePreview.BorderBrush = patternStyle == CellFillPatternStyle.None
            ? SystemColors.ControlDarkBrush
            : BrushForColor(patternColor, Brushes.Black);
        DlgFillPatternSamplePreview.ToolTip = patternStyle == CellFillPatternStyle.None
            ? "No fill pattern"
            : $"{FillPatternLabel(patternStyle)} pattern";
        DlgFillSamplePreview.ToolTip = patternStyle == CellFillPatternStyle.None
            ? "No fill pattern"
            : $"{FillPatternLabel(patternStyle)} pattern";
    }

    private static CellColor? TryParseColor(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return ColorInputParser.TryParseColorText(text, out var color)
            ? color
            : null;
    }

    private static bool TryParseRequiredColor(string text, out CellColor color) =>
        ColorInputParser.TryParseColorText(text, out color);

    private static bool TryParseOptionalColor(string text, out CellColor? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (!ColorInputParser.TryParseColorText(text, out var parsed))
            return false;

        color = parsed;
        return true;
    }

    private static Brush BrushForColor(CellColor? color, Brush fallback)
        => color is { } rgb
            ? new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B))
            : fallback;

    private static Brush CreateFillPreviewBrush(Brush fillBrush, CellFillPatternStyle patternStyle, CellColor? patternColor)
    {
        if (patternStyle == CellFillPatternStyle.None)
            return fillBrush;

        var color = patternColor ?? CellColor.Black;
        if (patternStyle == CellFillPatternStyle.Solid)
            return BrushForColor(color, fillBrush);

        const double tileSize = 16;
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var tile = new Rect(0, 0, tileSize, tileSize);
            dc.DrawRectangle(fillBrush, null, tile);
            DrawFillPatternPreview(dc, tile, patternStyle, color);
        }

        if (group.CanFreeze)
            group.Freeze();

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tileSize, tileSize),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, tileSize, tileSize),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }

    private static void DrawFillPatternPreview(
        DrawingContext dc,
        Rect tile,
        CellFillPatternStyle patternStyle,
        CellColor color)
    {
        var brush = BrushForColor(color, Brushes.Black);
        var pen = new Pen(brush, IsDarkPattern(patternStyle) ? 1.25 : 0.75);
        if (pen.CanFreeze)
            pen.Freeze();

        switch (patternStyle)
        {
            case CellFillPatternStyle.Gray0625:
            case CellFillPatternStyle.Gray125:
            case CellFillPatternStyle.LightGray:
            case CellFillPatternStyle.MediumGray:
            case CellFillPatternStyle.DarkGray:
                dc.DrawRectangle(CreateTransparentPatternBrush(color, GrayPatternOpacity(patternStyle)), null, tile);
                break;
            case CellFillPatternStyle.LightHorizontal:
            case CellFillPatternStyle.DarkHorizontal:
                DrawHorizontalPattern(dc, tile, pen);
                break;
            case CellFillPatternStyle.LightVertical:
            case CellFillPatternStyle.DarkVertical:
                DrawVerticalPattern(dc, tile, pen);
                break;
            case CellFillPatternStyle.LightGrid:
            case CellFillPatternStyle.DarkGrid:
                DrawHorizontalPattern(dc, tile, pen);
                DrawVerticalPattern(dc, tile, pen);
                break;
            case CellFillPatternStyle.LightDown:
            case CellFillPatternStyle.DarkDown:
                DrawDiagonalPattern(dc, tile, pen, descending: true);
                break;
            case CellFillPatternStyle.LightUp:
            case CellFillPatternStyle.DarkUp:
                DrawDiagonalPattern(dc, tile, pen, descending: false);
                break;
            case CellFillPatternStyle.LightTrellis:
            case CellFillPatternStyle.DarkTrellis:
                DrawDiagonalPattern(dc, tile, pen, descending: true);
                DrawDiagonalPattern(dc, tile, pen, descending: false);
                break;
        }
    }

    private static void DrawHorizontalPattern(DrawingContext dc, Rect tile, Pen pen)
    {
        const double step = 6;
        for (var y = tile.Top + step; y < tile.Bottom; y += step)
            dc.DrawLine(pen, new Point(tile.Left, y), new Point(tile.Right, y));
    }

    private static void DrawVerticalPattern(DrawingContext dc, Rect tile, Pen pen)
    {
        const double step = 6;
        for (var x = tile.Left + step; x < tile.Right; x += step)
            dc.DrawLine(pen, new Point(x, tile.Top), new Point(x, tile.Bottom));
    }

    private static void DrawDiagonalPattern(DrawingContext dc, Rect tile, Pen pen, bool descending)
    {
        const double step = 8;
        for (var offset = -tile.Height; offset < tile.Width; offset += step)
        {
            var start = descending
                ? new Point(tile.Left + offset, tile.Top)
                : new Point(tile.Left + offset, tile.Bottom);
            var end = descending
                ? new Point(tile.Left + offset + tile.Height, tile.Bottom)
                : new Point(tile.Left + offset + tile.Height, tile.Top);
            dc.DrawLine(pen, start, end);
        }
    }

    private static SolidColorBrush CreateTransparentPatternBrush(CellColor color, double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B));
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }

    private static double GrayPatternOpacity(CellFillPatternStyle patternStyle) => patternStyle switch
    {
        CellFillPatternStyle.Gray0625 => 0.12,
        CellFillPatternStyle.Gray125 => 0.18,
        CellFillPatternStyle.LightGray => 0.28,
        CellFillPatternStyle.MediumGray => 0.45,
        _ => 0.62
    };

    private static bool IsDarkPattern(CellFillPatternStyle patternStyle) => patternStyle is
        CellFillPatternStyle.DarkHorizontal or
        CellFillPatternStyle.DarkVertical or
        CellFillPatternStyle.DarkDown or
        CellFillPatternStyle.DarkUp or
        CellFillPatternStyle.DarkGrid or
        CellFillPatternStyle.DarkTrellis;

    private CellFillPatternStyle SelectedFillPatternStyle()
    {
        if (DlgFillPatternStyleBox?.SelectedItem is string label
            && FillPatternOptions.FirstOrDefault(option => option.Label == label) is { } option)
        {
            return option.Style;
        }

        return CellFillPatternStyle.None;
    }

    private static string FillPatternLabel(CellFillPatternStyle style) =>
        FillPatternOptions.FirstOrDefault(option => option.Style == style)?.Label ?? "None";
}
