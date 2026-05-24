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

        DlgFillBackgroundPreview.Background = fillBrush;
        DlgFillSamplePreview.Background = fillBrush;
        DlgFillPatternSamplePreview.Background = fillBrush;
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
        return ColorInputParser.TryParseRgbColorText(text, out var color)
            ? color
            : null;
    }

    private static Brush BrushForColor(CellColor? color, Brush fallback)
        => color is { } rgb
            ? new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B))
            : fallback;

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
