using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class FormatCellsDialog
{
    private static void PopulateBorder(ComboBox styleBox, TextBox colorBox, CellBorder border)
    {
        styleBox.ItemsSource = FormatCellsBorderPalettePlanner.StyleChoices;
        styleBox.SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(border.Style);
        colorBox.Text = ColorInputParser.FormatRgbColor(border.Color);
    }

    private void PopulateBorderColorPalette()
    {
        DlgBorderLinePalettePanel.Children.Clear();
        foreach (var entry in FormatCellsBorderPalettePlanner.ColorEntries)
        {
            var label = UiText.Get(entry.ResourceKey);
            var button = new Button
            {
                Width = 22,
                Height = 18,
                Padding = new Thickness(0),
                BorderBrush = Brushes.Gray,
                ToolTip = label,
                Tag = entry
            };
            AutomationProperties.SetName(button, label);

            if (entry.Color is { } color)
            {
                button.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
                button.Click += DlgBorderLineColorSwatchButton_Click;
            }
            else
            {
                button.Content = "...";
                button.Click += DlgBorderLineColorPickerButton_Click;
            }

            DlgBorderLinePalettePanel.Children.Add(button);
        }
    }

    private void DlgBorderLineColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderLineColorBox, allowNoColor: false, UiText.Get("FormatCells_BorderColorTitle"));

    private void DlgBorderLineColorSwatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FormatCellsBorderColorEntry { Color: { } color } })
            DlgBorderLineColorBox.Text = ColorInputParser.FormatRgbColor(color);
    }

    private void DlgBorderTopColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderTopColorBox, allowNoColor: false, UiText.Get("FormatCells_TopBorderColorTitle"));

    private void DlgBorderRightColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderRightColorBox, allowNoColor: false, UiText.Get("FormatCells_RightBorderColorTitle"));

    private void DlgBorderBottomColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderBottomColorBox, allowNoColor: false, UiText.Get("FormatCells_BottomBorderColorTitle"));

    private void DlgBorderLeftColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderLeftColorBox, allowNoColor: false, UiText.Get("FormatCells_LeftBorderColorTitle"));

    private void DlgBorderPresetNoneButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderClearPreset();

    private void DlgBorderPresetOutlineButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderOutlinePreset();

    private void DlgBorderPresetInsideButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderInsidePreset();

    private void DlgBorderPreviewTopButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderSide(DlgBorderTopStyleBox, DlgBorderTopColorBox);

    private void DlgBorderPreviewRightButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderSide(DlgBorderRightStyleBox, DlgBorderRightColorBox);

    private void DlgBorderPreviewBottomButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderSide(DlgBorderBottomStyleBox, DlgBorderBottomColorBox);

    private void DlgBorderPreviewLeftButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderSide(DlgBorderLeftStyleBox, DlgBorderLeftColorBox);

    private void ApplyBorderClearPreset()
    {
        _borderPresetClearRequested = true;
        _borderPresetOutline = null;
        _borderPresetInside = null;
        ApplyBorderPreset(BorderStyle.None);
    }

    private void ApplyBorderOutlinePreset()
    {
        _borderPresetClearRequested = false;
        _borderPresetOutline = SelectedBorderLine();
        ApplyBorderPreset(_borderPresetOutline.Value.Style);
    }

    private void ApplyBorderInsidePreset()
    {
        _borderPresetClearRequested = false;
        _borderPresetInside = SelectedBorderLine();
        UpdateBorderPreview();
    }

    private void ApplyBorderPreset(BorderStyle style)
    {
        SetBorderSide(DlgBorderTopStyleBox, DlgBorderTopColorBox, style);
        SetBorderSide(DlgBorderRightStyleBox, DlgBorderRightColorBox, style);
        SetBorderSide(DlgBorderBottomStyleBox, DlgBorderBottomColorBox, style);
        SetBorderSide(DlgBorderLeftStyleBox, DlgBorderLeftColorBox, style);
        UpdateBorderPreview();
    }

    private void ApplyBorderSide(ComboBox styleBox, TextBox colorBox)
    {
        var nextStyle = FormatCellsDialogPlanner.NextBorderSideStyle(
            SelectedBorderStyle(styleBox.SelectedItem),
            SelectedBorderStyle(DlgBorderLineStyleList.SelectedItem ?? DlgBorderLineStyleBox.SelectedItem, BorderStyle.Thin));
        SetBorderSide(styleBox, colorBox, nextStyle);
        UpdateBorderPreview();
    }

    private void SetBorderSide(ComboBox styleBox, TextBox colorBox, BorderStyle style)
    {
        styleBox.SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(style);
        if (style != BorderStyle.None)
            colorBox.Text = DlgBorderLineColorBox.Text;
    }

    private CellBorder SelectedBorderLine() =>
        FormatCellsDialogPlanner.CreateSelectedBorderLine(
            SelectedBorderStyle(DlgBorderLineStyleList.SelectedItem ?? DlgBorderLineStyleBox.SelectedItem, BorderStyle.Thin),
            DlgBorderLineColorBox.Text);

    private void UpdateBorderPreview()
    {
        if (DlgBorderPreviewArea is null)
            return;

        var top = PreviewThickness(SelectedBorderStyle(DlgBorderTopStyleBox.SelectedItem));
        var right = PreviewThickness(SelectedBorderStyle(DlgBorderRightStyleBox.SelectedItem));
        var bottom = PreviewThickness(SelectedBorderStyle(DlgBorderBottomStyleBox.SelectedItem));
        var left = PreviewThickness(SelectedBorderStyle(DlgBorderLeftStyleBox.SelectedItem));

        DlgBorderPreviewArea.BorderThickness = new Thickness(left, top, right, bottom);
        DlgBorderPreviewArea.BorderBrush = BrushForColor(
            TryParseColor(DlgBorderLineColorBox.Text) ?? TryParseColor(DlgBorderBottomColorBox.Text),
            Brushes.Black);
        DlgBorderLineColorPreview.Background = BrushForColor(TryParseColor(DlgBorderLineColorBox.Text), Brushes.Black);

        var insideThickness = _borderPresetInside is { } inside
            ? PreviewThickness(inside.Style)
            : 0;
        var insideBrush = _borderPresetInside is { } insideBorder
            ? BrushForColor(insideBorder.Color, Brushes.Black)
            : Brushes.Black;
        DlgBorderPreviewInsideVertical.BorderThickness = new Thickness(insideThickness, 0, 0, 0);
        DlgBorderPreviewInsideHorizontal.BorderThickness = new Thickness(0, insideThickness, 0, 0);
        DlgBorderPreviewInsideVertical.BorderBrush = insideBrush;
        DlgBorderPreviewInsideHorizontal.BorderBrush = insideBrush;
        DlgBorderPreviewInsideVertical.Visibility = insideThickness > 0 ? Visibility.Visible : Visibility.Collapsed;
        DlgBorderPreviewInsideHorizontal.Visibility = insideThickness > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static BorderStyle SelectedBorderStyle(object? selectedItem, BorderStyle fallback = BorderStyle.None) =>
        selectedItem is FormatCellsBorderStyleChoice choice ? choice.Style : fallback;

    private static double PreviewThickness(BorderStyle selectedStyle)
        => selectedStyle switch
        {
            BorderStyle.None => 0,
            BorderStyle.Medium => 2,
            BorderStyle.Thick => 3,
            BorderStyle.Double => 3,
            _ => 1
        };
}
