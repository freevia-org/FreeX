using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class FormatCellsDialog
{
    private static void PopulateBorder(ComboBox styleBox, TextBox colorBox, CellBorder border)
    {
        styleBox.ItemsSource = Enum.GetNames(typeof(BorderStyle));
        styleBox.SelectedItem = border.Style.ToString();
        colorBox.Text = ColorInputParser.FormatRgbColor(border.Color);
    }

    private static CellBorder ParseBorder(ComboBox styleBox, TextBox colorBox, CellBorder current)
    {
        var style = current.Style;
        if (styleBox.SelectedItem is string selectedStyle
            && Enum.TryParse(selectedStyle, out BorderStyle parsedStyle)
            && Enum.IsDefined(parsedStyle))
        {
            style = parsedStyle;
        }

        var color = TryParseColor(colorBox.Text) ?? current.Color;
        return new CellBorder(style, color);
    }

    private void DlgBorderLineColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderLineColorBox, allowNoColor: false, "Border Color");

    private void DlgBorderLineColorSwatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string colorText })
            DlgBorderLineColorBox.Text = colorText;
    }

    private void DlgBorderTopColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderTopColorBox, allowNoColor: false, "Top Border Color");

    private void DlgBorderRightColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderRightColorBox, allowNoColor: false, "Right Border Color");

    private void DlgBorderBottomColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderBottomColorBox, allowNoColor: false, "Bottom Border Color");

    private void DlgBorderLeftColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderLeftColorBox, allowNoColor: false, "Left Border Color");

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
        SetBorderSide(styleBox, colorBox, SelectedBorderLineStyle());
        UpdateBorderPreview();
    }

    private void SetBorderSide(ComboBox styleBox, TextBox colorBox, BorderStyle style)
    {
        styleBox.SelectedItem = style.ToString();
        if (style != BorderStyle.None)
            colorBox.Text = DlgBorderLineColorBox.Text;
    }

    private BorderStyle SelectedBorderLineStyle()
        => (DlgBorderLineStyleList.SelectedItem as string ?? DlgBorderLineStyleBox.SelectedItem as string) is string selectedStyle
            && Enum.TryParse(selectedStyle, out BorderStyle parsedStyle)
            && Enum.IsDefined(parsedStyle)
                ? parsedStyle
                : BorderStyle.Thin;

    private CellBorder SelectedBorderLine() =>
        new(SelectedBorderLineStyle(), TryParseColor(DlgBorderLineColorBox.Text) ?? CellColor.Black);

    private void UpdateBorderPreview()
    {
        if (DlgBorderPreviewArea is null)
            return;

        var top = PreviewThickness(DlgBorderTopStyleBox.SelectedItem as string);
        var right = PreviewThickness(DlgBorderRightStyleBox.SelectedItem as string);
        var bottom = PreviewThickness(DlgBorderBottomStyleBox.SelectedItem as string);
        var left = PreviewThickness(DlgBorderLeftStyleBox.SelectedItem as string);

        DlgBorderPreviewArea.BorderThickness = new Thickness(left, top, right, bottom);
        DlgBorderPreviewArea.BorderBrush = BrushForColor(
            TryParseColor(DlgBorderLineColorBox.Text) ?? TryParseColor(DlgBorderBottomColorBox.Text),
            Brushes.Black);
        DlgBorderLineColorPreview.Background = BrushForColor(TryParseColor(DlgBorderLineColorBox.Text), Brushes.Black);

        var insideThickness = _borderPresetInside is { } inside
            ? PreviewThickness(inside.Style.ToString())
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

    private static double PreviewThickness(string? selectedStyle)
        => selectedStyle switch
        {
            nameof(BorderStyle.None) => 0,
            nameof(BorderStyle.Medium) => 2,
            nameof(BorderStyle.Thick) => 3,
            nameof(BorderStyle.Double) => 3,
            _ => 1
        };
}

public sealed record FormatCellsBorderSelection(
    bool Clear,
    CellBorder? Outline,
    CellBorder? Inside)
{
    public static FormatCellsBorderSelection None { get; } = new(false, null, null);

    public bool HasRangeOperations => Clear || Outline is not null || Inside is not null;
}
