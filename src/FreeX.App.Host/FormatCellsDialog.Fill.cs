using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class FormatCellsDialog
{
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

    private void PopulateFillPalettes()
    {
        DlgFillPalettePanel.Children.Clear();
        foreach (var entry in FormatCellsFillPalettePlanner.BackgroundEntries)
        {
            var button = new Button
            {
                Width = 28,
                Height = 20,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Content = entry.IsMore ? "..." : null,
                Tag = entry.IsClear ? "" : entry.Color is { } color
                    ? ColorInputParser.FormatRgbColor(color)
                    : null,
                Background = entry.Color is { } rgb
                    ? new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B))
                    : Brushes.White,
                BorderBrush = Brushes.Gray,
            };
            var label = UiText.Get(entry.ResourceKey);
            button.ToolTip = label;
            AutomationProperties.SetName(button, label);
            button.Click += entry.IsMore ? DlgFillColorPickerButton_Click : DlgFillSwatchButton_Click;
            DlgFillPalettePanel.Children.Add(button);
        }

        DlgFillPatternColorPalettePanel.Children.Clear();
        foreach (var entry in FormatCellsFillPalettePlanner.PatternEntries)
        {
            var button = new Button
            {
                Width = 24,
                Height = 19,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Content = entry.IsMore ? "..." : null,
                Tag = entry.Color is { } color
                    ? ColorInputParser.FormatRgbColor(color)
                    : null,
                Background = entry.Color is { } rgb
                    ? new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B))
                    : Brushes.White,
                BorderBrush = Brushes.Gray,
            };
            var label = UiText.Get(entry.ResourceKey);
            button.ToolTip = label;
            AutomationProperties.SetName(button, label);
            button.Click += entry.IsMore ? DlgFillPatternColorPickerButton_Click : DlgFillPatternSwatchButton_Click;
            DlgFillPatternColorPalettePanel.Children.Add(button);
        }
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

    private static Brush BrushForColor(CellColor? color, Brush fallback)
        => color is { } rgb
            ? new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B))
            : fallback;

    private CellFillPatternStyle SelectedFillPatternStyle() =>
        FormatCellsDialogPlanner.ResolveFillPatternStyle(
            DlgFillPatternStyleBox?.SelectedItem as string,
            FillPatternDisplayChoices());

    private static IReadOnlyList<FormatCellsDialogFillPatternDisplayChoice> FillPatternDisplayChoices() =>
        FormatCellsDialogPlanner.CreateFillPatternDisplayChoices(UiText.Get);

    private static string FillPatternLabel(CellFillPatternStyle style) =>
        UiText.Get(FormatCellsDialogPlanner.GetFillPatternResourceKey(style));
}
