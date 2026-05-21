using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.Host;

public partial class FormatCellsDialog : Window
{
    public StyleDiff? ResultDiff { get; private set; }

    private readonly CellStyle _current;

    private static readonly string[] NumberFormatCodes =
    [
        "General",
        "#,##0.00",
        "$#,##0.00",
        "$#,##0.00",
        "0%",
        "0.00%",
        "m/d/yyyy",
        "h:mm AM/PM",
        "# ?/?",
        "0.00E+00",
        "@"
    ];

    private static readonly string[] NumberFormatLabels =
    [
        "General",
        "Number (#,##0.00)",
        "Currency ($#,##0.00)",
        "Accounting ($#,##0.00)",
        "Percentage (0%)",
        "Percentage (0.00%)",
        "Date (m/d/yyyy)",
        "Time (h:mm AM/PM)",
        "Fraction (# ?/?)",
        "Scientific (0.00E+00)",
        "Text (@)"
    ];

    private static readonly string[] NumberCategories =
    [
        "General",
        "Number",
        "Currency",
        "Accounting",
        "Date",
        "Time",
        "Percentage",
        "Fraction",
        "Scientific",
        "Text",
        "Custom"
    ];

    public FormatCellsDialog(CellStyle current, FormatCellsDialogTab initialTab = FormatCellsDialogTab.Number)
    {
        _current = current.Clone();
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Populate(_current);
            Tabs.SelectedIndex = (int)initialTab;
        };
    }

    private void Populate(CellStyle s)
    {
        NumberCategoryList.ItemsSource = NumberCategories;
        NumberSymbolCombo.ItemsSource = new[] { "$", "EUR", "GBP", "JPY", "None" };
        NumberSymbolCombo.SelectedIndex = 0;
        NumberNegativeNumbersList.ItemsSource = new[]
        {
            "-1234.10",
            "1234.10",
            "(1234.10)",
            "-1234.10"
        };
        NumberNegativeNumbersList.SelectedIndex = 0;
        NumberFormatCombo.ItemsSource = NumberFormatLabels;
        var idx = Array.IndexOf(NumberFormatCodes, s.NumberFormat);
        if (idx >= 0)
        {
            NumberFormatCombo.SelectedIndex = idx;
            NumberCategoryList.SelectedItem = CategoryForFormatIndex(idx);
        }
        else
        {
            NumberFormatCombo.SelectedIndex = -1;
            NumberFormatCombo.Text = s.NumberFormat;
            NumberCategoryList.SelectedItem = "Custom";
        }

        DlgFontNameBox.ItemsSource  = new[] { "Calibri", "Arial", "Times New Roman", "Courier New", "Segoe UI", "Verdana" };
        DlgFontNameBox.SelectedItem = s.FontName;
        DlgFontSizeBox.ItemsSource  = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36" };
        DlgFontSizeBox.Text         = s.FontSize.ToString("0.#");
        DlgFontStyleList.ItemsSource = new[] { "Regular", "Italic", "Bold", "Bold Italic" };
        DlgBoldCheck.IsChecked      = s.Bold;
        DlgItalicCheck.IsChecked    = s.Italic;
        DlgFontStyleList.SelectedItem = FontStyleLabel(s.Bold, s.Italic);
        DlgUnderlineStyleBox.ItemsSource = new[] { "None", "Single", "Double" };
        DlgUnderlineCheck.IsChecked = s.Underline;
        DlgDoubleUnderlineCheck.IsChecked = s.DoubleUnderline;
        DlgUnderlineStyleBox.SelectedItem = s.DoubleUnderline ? "Double" : s.Underline ? "Single" : "None";
        DlgStrikeCheck.IsChecked    = s.Strikethrough;
        DlgSuperscriptCheck.IsChecked = s.Superscript;
        DlgSubscriptCheck.IsChecked = s.Subscript;
        DlgFontColorBox.Text        = ColorInputParser.FormatRgbColor(s.FontColor);

        DlgFillColorBox.Text = s.FillColor.HasValue
            ? ColorInputParser.FormatRgbColor(s.FillColor.Value)
            : "";
        DlgClearFillCheck.IsChecked = false;
        DlgFillPatternColorBox.Text = "";
        DlgFillPatternStyleBox.ItemsSource = new[] { "None", "Solid", "Gray 25%", "Gray 50%", "Horizontal", "Vertical" };
        DlgFillPatternStyleBox.SelectedIndex = 0;

        DlgHAlignBox.ItemsSource  = Enum.GetNames(typeof(CellHAlign));
        DlgHAlignBox.SelectedItem = s.HorizontalAlignment.ToString();
        DlgVAlignBox.ItemsSource  = Enum.GetNames(typeof(CellVAlign));
        DlgVAlignBox.SelectedItem = s.VerticalAlignment.ToString();
        DlgWrapTextCheck.IsChecked = s.WrapText;
        DlgShrinkToFitCheck.IsChecked = s.ShrinkToFit;
        DlgIndentLevelBox.Text = s.IndentLevel.ToString();
        DlgTextRotationBox.Text = s.TextRotation.ToString();

        PopulateBorder(DlgBorderTopStyleBox, DlgBorderTopColorBox, s.BorderTop);
        PopulateBorder(DlgBorderRightStyleBox, DlgBorderRightColorBox, s.BorderRight);
        PopulateBorder(DlgBorderBottomStyleBox, DlgBorderBottomColorBox, s.BorderBottom);
        PopulateBorder(DlgBorderLeftStyleBox, DlgBorderLeftColorBox, s.BorderLeft);
        DlgBorderLineStyleBox.ItemsSource = Enum.GetNames(typeof(BorderStyle));
        DlgBorderLineStyleBox.SelectedItem = s.BorderBottom.Style == BorderStyle.None
            ? nameof(BorderStyle.Thin)
            : s.BorderBottom.Style.ToString();
        DlgBorderLineColorBox.Text = ColorInputParser.FormatRgbColor(s.BorderBottom.Color);

        DlgLockedCheck.IsChecked = s.Locked;

        UpdateFontPreview();
        UpdateFillPreview();
        UpdateBorderPreview();
    }

    private void NumberFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NumberPreview is not null)
            NumberPreview.Text = PreviewForFormat(NumberFormatCombo.SelectedIndex);
    }

    private void NumberCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NumberCategoryList.SelectedItem is not string category)
            return;

        var index = category switch
        {
            "General" => 0,
            "Number" => 1,
            "Currency" => 2,
            "Accounting" => 3,
            "Date" => 6,
            "Time" => 7,
            "Percentage" => 5,
            "Fraction" => 8,
            "Scientific" => 9,
            "Text" => 10,
            _ => NumberFormatCombo.SelectedIndex
        };

        if (index >= 0 && index < NumberFormatLabels.Length)
            NumberFormatCombo.SelectedIndex = index;
    }

    private void FontStyleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DlgFontStyleList.SelectedItem is not string style)
            return;

        DlgBoldCheck.IsChecked = style.Contains("Bold", StringComparison.OrdinalIgnoreCase);
        DlgItalicCheck.IsChecked = style.Contains("Italic", StringComparison.OrdinalIgnoreCase);
        UpdateFontPreview();
    }

    private void UnderlineStyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DlgUnderlineStyleBox.SelectedItem is not string underline)
            return;

        DlgUnderlineCheck.IsChecked = underline == "Single";
        DlgDoubleUnderlineCheck.IsChecked = underline == "Double";
        UpdateFontPreview();
    }

    private void FontPreviewInput_Changed(object sender, RoutedEventArgs e)
    {
        if (DlgFontSamplePreview is null)
            return;

        UpdateFontPreview();
    }

    private void FillPreviewInput_Changed(object sender, RoutedEventArgs e)
    {
        if (DlgFillSamplePreview is null)
            return;

        UpdateFillPreview();
    }

    private void BorderPreviewInput_Changed(object sender, RoutedEventArgs e)
    {
        if (DlgBorderPreviewArea is null)
            return;

        UpdateBorderPreview();
    }

    public static string? ResolveNumberFormat(string text, int selectedIndex)
    {
        var trimmedText = text.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedText)
            && (selectedIndex < 0
                || selectedIndex >= NumberFormatCodes.Length
                || (trimmedText != NumberFormatLabels[selectedIndex]
                    && trimmedText != NumberFormatCodes[selectedIndex])))
        {
            return trimmedText;
        }

        if (selectedIndex >= 0 && selectedIndex < NumberFormatCodes.Length)
            return NumberFormatCodes[selectedIndex];

        return string.IsNullOrWhiteSpace(trimmedText) ? null : trimmedText;
    }

    public static int? TryParseSupportedTextRotation(string text)
        => FormatCellsInputParser.TryParseSupportedTextRotation(text);

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        CellColor? fontColor = TryParseColor(DlgFontColorBox.Text);
        CellColor? fillColor = TryParseColor(DlgFillColorBox.Text);
        bool clearFill = DlgClearFillCheck.IsChecked == true;

        string? numFmt = ResolveNumberFormat(NumberFormatCombo.Text, NumberFormatCombo.SelectedIndex);

        double? fontSize = FormatCellsInputParser.TryParseFontSize(DlgFontSizeBox.Text);

        CellHAlign? hAlign = null;
        if (DlgHAlignBox.SelectedItem is string ha && Enum.TryParse(ha, out CellHAlign h)) hAlign = h;
        CellVAlign? vAlign = null;
        if (DlgVAlignBox.SelectedItem is string va && Enum.TryParse(va, out CellVAlign v)) vAlign = v;

        int? indentLevel = FormatCellsInputParser.TryParseIndentLevel(DlgIndentLevelBox.Text);

        int? textRotation = FormatCellsInputParser.TryParseSupportedTextRotation(DlgTextRotationBox.Text);

        CellBorder borderTop = ParseBorder(DlgBorderTopStyleBox, DlgBorderTopColorBox, _current.BorderTop);
        CellBorder borderRight = ParseBorder(DlgBorderRightStyleBox, DlgBorderRightColorBox, _current.BorderRight);
        CellBorder borderBottom = ParseBorder(DlgBorderBottomStyleBox, DlgBorderBottomColorBox, _current.BorderBottom);
        CellBorder borderLeft = ParseBorder(DlgBorderLeftStyleBox, DlgBorderLeftColorBox, _current.BorderLeft);

        ResultDiff = new StyleDiff(
            Bold:            DlgBoldCheck.IsChecked,
            Italic:          DlgItalicCheck.IsChecked,
            Underline:       DlgUnderlineCheck.IsChecked,
            Strikethrough:   DlgStrikeCheck.IsChecked,
            Superscript:     DlgSuperscriptCheck.IsChecked,
            Subscript:       DlgSubscriptCheck.IsChecked,
            FontName:        DlgFontNameBox.SelectedItem as string,
            FontSize:        fontSize,
            FontColor:       fontColor,
            FillColor:       clearFill ? null : fillColor,
            HAlign:          hAlign,
            VAlign:          vAlign,
            WrapText:        DlgWrapTextCheck.IsChecked,
            ShrinkToFit:     DlgShrinkToFitCheck.IsChecked,
            NumberFormat:    numFmt,
            DoubleUnderline: DlgDoubleUnderlineCheck.IsChecked,
            IndentLevel:     indentLevel,
            TextRotation:    textRotation,
            BorderTop:       borderTop,
            BorderRight:     borderRight,
            BorderBottom:    borderBottom,
            BorderLeft:      borderLeft,
            Locked:          DlgLockedCheck.IsChecked,
            ClearFill:       clearFill ? true : null
        );

        DialogResult = true;
    }

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

    private void DlgFontColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgFontColorBox, allowNoColor: false);

    private void DlgFillColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgFillColorBox, allowNoColor: true);

    private void DlgBorderLineColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderLineColorBox, allowNoColor: false);

    private void DlgBorderTopColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderTopColorBox, allowNoColor: false);

    private void DlgBorderRightColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderRightColorBox, allowNoColor: false);

    private void DlgBorderBottomColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderBottomColorBox, allowNoColor: false);

    private void DlgBorderLeftColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderLeftColorBox, allowNoColor: false);

    private void DlgBorderPresetNoneButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderPreset(BorderStyle.None);

    private void DlgBorderPresetOutlineButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderPreset(SelectedBorderLineStyle());

    private void DlgBorderPresetInsideButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderPreset(SelectedBorderLineStyle());

    private void DlgBorderPreviewTopButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderSide(DlgBorderTopStyleBox, DlgBorderTopColorBox);

    private void DlgBorderPreviewRightButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderSide(DlgBorderRightStyleBox, DlgBorderRightColorBox);

    private void DlgBorderPreviewBottomButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderSide(DlgBorderBottomStyleBox, DlgBorderBottomColorBox);

    private void DlgBorderPreviewLeftButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBorderSide(DlgBorderLeftStyleBox, DlgBorderLeftColorBox);

    private void PickColorInto(TextBox target, bool allowNoColor)
    {
        var initial = TryParseColor(target.Text);
        var dialog = new ColorPickerDialog(initial, allowNoColor) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        target.Text = dialog.SelectedColor is { } color ? ColorInputParser.FormatRgbColor(color) : "";
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
        => DlgBorderLineStyleBox.SelectedItem is string selectedStyle
            && Enum.TryParse(selectedStyle, out BorderStyle parsedStyle)
            && Enum.IsDefined(parsedStyle)
                ? parsedStyle
                : BorderStyle.Thin;

    private void UpdateFontPreview()
    {
        if (DlgFontSamplePreview is null)
            return;

        DlgFontSamplePreview.FontFamily = new FontFamily(DlgFontNameBox.Text);
        DlgFontSamplePreview.FontSize = FormatCellsInputParser.TryParseFontSize(DlgFontSizeBox.Text) ?? 11;
        DlgFontSamplePreview.FontWeight = DlgBoldCheck.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
        DlgFontSamplePreview.FontStyle = DlgItalicCheck.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
        DlgFontSamplePreview.Foreground = BrushForColor(TryParseColor(DlgFontColorBox.Text), Brushes.Black);

        var decorations = new TextDecorationCollection();
        if (DlgUnderlineCheck.IsChecked == true || DlgDoubleUnderlineCheck.IsChecked == true)
        {
            foreach (var decoration in TextDecorations.Underline)
                decorations.Add(decoration);
        }

        if (DlgStrikeCheck.IsChecked == true)
        {
            foreach (var decoration in TextDecorations.Strikethrough)
                decorations.Add(decoration);
        }

        DlgFontSamplePreview.TextDecorations = decorations;
    }

    private void UpdateFillPreview()
    {
        if (DlgFillSamplePreview is null)
            return;

        var fillBrush = DlgClearFillCheck.IsChecked == true
            ? Brushes.White
            : BrushForColor(TryParseColor(DlgFillColorBox.Text), Brushes.White);

        DlgFillBackgroundPreview.Background = fillBrush;
        DlgFillSamplePreview.Background = fillBrush;
    }

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

    private static double PreviewThickness(string? selectedStyle)
        => selectedStyle switch
        {
            nameof(BorderStyle.None) => 0,
            nameof(BorderStyle.Medium) => 2,
            nameof(BorderStyle.Thick) => 3,
            nameof(BorderStyle.Double) => 3,
            _ => 1
        };

    private static string FontStyleLabel(bool bold, bool italic) => (bold, italic) switch
    {
        (true, true) => "Bold Italic",
        (true, false) => "Bold",
        (false, true) => "Italic",
        _ => "Regular"
    };

    private static string CategoryForFormatIndex(int index) => index switch
    {
        1 => "Number",
        2 => "Currency",
        3 => "Accounting",
        4 or 5 => "Percentage",
        6 => "Date",
        7 => "Time",
        8 => "Fraction",
        9 => "Scientific",
        10 => "Text",
        _ => "General"
    };

    private static string PreviewForFormat(int index) => index switch
    {
        1 => "1,234.56",
        2 or 3 => "$1,234.56",
        4 => "123456%",
        5 => "123456.00%",
        6 => "5/21/2026",
        7 => "1:30 PM",
        8 => "1234 1/2",
        9 => "1.23E+03",
        10 => "1234.56",
        _ => "1234.56"
    };
}

public enum FormatCellsDialogTab
{
    Number,
    Alignment,
    Font,
    Fill,
    Border,
    Protection
}
