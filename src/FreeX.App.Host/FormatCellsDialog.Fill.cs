using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class FormatCellsDialog
{
    private sealed record FillPatternOption(CellFillPatternStyle Style, string ResourceKey)
    {
        public string Label => UiText.Get(ResourceKey);
    }

    private static readonly FillPatternOption[] FillPatternOptions =
    [
        new(CellFillPatternStyle.None, "FormatCells_FillPatternNone"),
        new(CellFillPatternStyle.Solid, "FormatCells_FillPatternSolid"),
        new(CellFillPatternStyle.Gray0625, "FormatCells_FillPatternGray0625"),
        new(CellFillPatternStyle.Gray125, "FormatCells_FillPatternGray125"),
        new(CellFillPatternStyle.LightGray, "FormatCells_FillPatternLightGray"),
        new(CellFillPatternStyle.MediumGray, "FormatCells_FillPatternMediumGray"),
        new(CellFillPatternStyle.DarkGray, "FormatCells_FillPatternDarkGray"),
        new(CellFillPatternStyle.LightHorizontal, "FormatCells_FillPatternLightHorizontal"),
        new(CellFillPatternStyle.LightVertical, "FormatCells_FillPatternLightVertical"),
        new(CellFillPatternStyle.LightDown, "FormatCells_FillPatternLightDown"),
        new(CellFillPatternStyle.LightUp, "FormatCells_FillPatternLightUp"),
        new(CellFillPatternStyle.LightGrid, "FormatCells_FillPatternLightGrid"),
        new(CellFillPatternStyle.LightTrellis, "FormatCells_FillPatternLightTrellis"),
        new(CellFillPatternStyle.DarkHorizontal, "FormatCells_FillPatternDarkHorizontal"),
        new(CellFillPatternStyle.DarkVertical, "FormatCells_FillPatternDarkVertical"),
        new(CellFillPatternStyle.DarkDown, "FormatCells_FillPatternDarkDown"),
        new(CellFillPatternStyle.DarkUp, "FormatCells_FillPatternDarkUp"),
        new(CellFillPatternStyle.DarkGrid, "FormatCells_FillPatternDarkGrid"),
        new(CellFillPatternStyle.DarkTrellis, "FormatCells_FillPatternDarkTrellis")
    ];

    private void DlgFontColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgFontColorBox, allowNoColor: false, UiText.Get("FormatCells_FontColorTitle"));

    private void DlgFontColorSwatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string colorText })
            DlgFontColorBox.Text = colorText;
    }

    private void DlgFillColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgFillColorBox, allowNoColor: true, UiText.Get("FormatCells_FillColorTitle"));

    private void DlgFillPatternColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgFillPatternColorBox, allowNoColor: true, UiText.Get("FormatCells_PatternColorTitle"));

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
            ? UiText.Get("FormatCells_NoFillPattern")
            : UiText.Format("FormatCells_FillPatternToolTip", FillPatternLabel(patternStyle));
        DlgFillSamplePreview.ToolTip = patternStyle == CellFillPatternStyle.None
            ? UiText.Get("FormatCells_NoFillPattern")
            : UiText.Format("FormatCells_FillPatternToolTip", FillPatternLabel(patternStyle));
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
        FillPatternOptions.FirstOrDefault(option => option.Style == style)?.Label
            ?? UiText.Get("FormatCells_FillPatternNone");
}
