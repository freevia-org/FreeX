using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Freexcel.Core.Calc;
using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.Host;

public partial class FormatCellsDialog : Window
{
    public StyleDiff? ResultDiff { get; private set; }

    private readonly CellStyle _current;
    private bool _syncingNumberControls;

    private sealed record NumberFormatOption(string Category, string Label, string Code, string Preview);

    private sealed record FillPatternOption(CellFillPatternStyle Style, string Label);

    private static readonly NumberFormatOption[] NumberFormatOptions =
    [
        new("General", "General", "General", "1234.56"),
        new("Number", "Number (#,##0.00)", "#,##0.00", "1,234.56"),
        new("Number", "0", "0", "1235"),
        new("Number", "0.00", "0.00", "1234.56"),
        new("Number", "#,##0", "#,##0", "1,235"),
        new("Number", "#,##0.00", "#,##0.00", "1,234.56"),
        new("Number", "#,##0_);[Red](#,##0)", "#,##0_);[Red](#,##0)", "1,235"),
        new("Number", "#,##0.00_);[Red](#,##0.00)", "#,##0.00_);[Red](#,##0.00)", "1,234.56"),
        new("Currency", "Currency ($#,##0.00)", "$#,##0.00", "$1,234.56"),
        new("Currency", "$#,##0", "$#,##0", "$1,235"),
        new("Currency", "$#,##0.00", "$#,##0.00", "$1,234.56"),
        new("Currency", "$#,##0;[Red]($#,##0)", "$#,##0;[Red]($#,##0)", "$1,235"),
        new("Currency", "$#,##0.00;[Red]($#,##0.00)", "$#,##0.00;[Red]($#,##0.00)", "$1,234.56"),
        new("Accounting", "Accounting ($#,##0.00)", "$#,##0.00", "$1,234.56"),
        new("Accounting", "_($* #,##0_);_($* (#,##0);_($* \"-\"_);_(@_)", "_($* #,##0_);_($* (#,##0);_($* \"-\"_);_(@_)", "$  1,235"),
        new("Accounting", "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)", "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)", "$  1,234.56"),
        new("Date", "Date (m/d/yyyy)", "m/d/yyyy", "5/21/2026"),
        new("Date", "m/d/yyyy", "m/d/yyyy", "5/21/2026"),
        new("Date", "d-mmm-yy", "d-mmm-yy", "21-May-26"),
        new("Date", "mmmm d, yyyy", "mmmm d, yyyy", "May 21, 2026"),
        new("Date", "m/d/yy h:mm", "m/d/yy h:mm", "5/21/26 13:30"),
        new("Time", "Time (h:mm AM/PM)", "h:mm AM/PM", "1:30 PM"),
        new("Time", "h:mm AM/PM", "h:mm AM/PM", "1:30 PM"),
        new("Time", "h:mm:ss AM/PM", "h:mm:ss AM/PM", "1:30:00 PM"),
        new("Time", "h:mm", "h:mm", "13:30"),
        new("Time", "h:mm:ss", "h:mm:ss", "13:30:00"),
        new("Time", "[h]:mm:ss", "[h]:mm:ss", "37:30:00"),
        new("Percentage", "Percentage (0%)", "0%", "123456%"),
        new("Percentage", "Percentage (0.00%)", "0.00%", "123456.00%"),
        new("Percentage", "0%", "0%", "123456%"),
        new("Percentage", "0.00%", "0.00%", "123456.00%"),
        new("Fraction", "Fraction (# ?/?)", "# ?/?", "1234 1/2"),
        new("Fraction", "# ?/?", "# ?/?", "1234 1/2"),
        new("Fraction", "# ??/??", "# ??/??", "1234 56/100"),
        new("Fraction", "# ?/2", "# ?/2", "1234 1/2"),
        new("Fraction", "# ?/4", "# ?/4", "1234 2/4"),
        new("Scientific", "Scientific (0.00E+00)", "0.00E+00", "1.23E+03"),
        new("Scientific", "0E+00", "0E+00", "1E+03"),
        new("Scientific", "0.00E+00", "0.00E+00", "1.23E+03"),
        new("Text", "Text (@)", "@", "1234.56"),
        new("Text", "@", "@", "1234.56"),
        new("Special", "00000", "00000", "01235"),
        new("Special", "00000-0000", "00000-0000", "01234-5600"),
        new("Special", "[<=9999999]###-####;(###) ###-####", "[<=9999999]###-####;(###) ###-####", "(123) 456-7890"),
        new("Custom", "General", "General", "1234.56"),
        new("Custom", "#,##0.00", "#,##0.00", "1,234.56"),
        new("Custom", "$#,##0.00", "$#,##0.00", "$1,234.56"),
        new("Custom", "0.00%", "0.00%", "123456.00%"),
        new("Custom", "m/d/yyyy", "m/d/yyyy", "5/21/2026"),
        new("Custom", "h:mm AM/PM", "h:mm AM/PM", "1:30 PM")
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
        "Special",
        "Custom"
    ];

    private static readonly string[] NumberSymbols = ["$", "EUR", "GBP", "JPY", "€", "£", "¥", "None"];

    private static readonly string[] NegativeNumberOptions =
    [
        "-1234.10",
        "[Red] -1234.10",
        "(1234.10)",
        "[Red] (1234.10)"
    ];

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
        NumberSymbolCombo.ItemsSource = NumberSymbols;
        NumberSymbolCombo.SelectedIndex = 0;
        NumberNegativeNumbersList.ItemsSource = NegativeNumberOptions;
        NumberNegativeNumbersList.SelectedIndex = 0;
        NumberDecimalPlacesBox.Text = DecimalPlacesForFormat(s.NumberFormat).ToString();
        var option = FindNumberFormatOption(s.NumberFormat);
        if (option is not null)
        {
            NumberCategoryList.SelectedItem = option.Category;
            SelectNumberFormatOption(option);
        }
        else
        {
            NumberCategoryList.SelectedItem = "Custom";
            NumberFormatCombo.Text = s.NumberFormat;
        }
        UpdateNumberControlAvailability();
        UpdateNumberPreview();

        DlgFontNameBox.ItemsSource  = FontNamesWithFallback(s.FontName);
        DlgFontNameBox.SelectedItem = s.FontName;
        DlgFontSizeBox.ItemsSource  = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36" };
        DlgFontSizeBox.Text         = s.FontSize.ToString("0.#");
        DlgFontStyleList.ItemsSource = new[] { "Regular", "Italic", "Bold", "Bold Italic" };
        DlgFontStyleList.SelectedItem = FontStyleLabel(s.Bold, s.Italic);
        DlgUnderlineStyleBox.ItemsSource = new[] { "None", "Single", "Double" };
        DlgDoubleUnderlineCheck.IsChecked = s.DoubleUnderline;
        DlgUnderlineStyleBox.SelectedItem = s.DoubleUnderline ? "Double" : s.Underline ? "Single" : "None";
        DlgStrikeCheck.IsChecked    = s.Strikethrough;
        DlgSuperscriptCheck.IsChecked = s.Superscript;
        DlgSubscriptCheck.IsChecked = s.Subscript;
        DlgFontColorBox.Text        = ColorInputParser.FormatRgbColor(s.FontColor);

        DlgFillColorBox.Text = s.FillColor.HasValue
            ? ColorInputParser.FormatRgbColor(s.FillColor.Value)
            : "";
        DlgFillPatternColorBox.Text = s.FillPatternColor.HasValue
            ? ColorInputParser.FormatRgbColor(s.FillPatternColor.Value)
            : "";
        DlgFillPatternStyleBox.ItemsSource = FillPatternOptions.Select(option => option.Label).ToArray();
        DlgFillPatternStyleBox.SelectedItem = FillPatternLabel(s.FillPatternStyle);
        DlgClearFillCheck.IsChecked = false;

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
        var borderStyleNames = Enum.GetNames(typeof(BorderStyle));
        DlgBorderLineStyleBox.ItemsSource = borderStyleNames;
        DlgBorderLineStyleList.ItemsSource = borderStyleNames;
        DlgBorderLineStyleBox.SelectedItem = s.BorderBottom.Style == BorderStyle.None
            ? nameof(BorderStyle.Thin)
            : s.BorderBottom.Style.ToString();
        DlgBorderLineStyleList.SelectedItem = DlgBorderLineStyleBox.SelectedItem;
        DlgBorderLineColorBox.Text = ColorInputParser.FormatRgbColor(s.BorderBottom.Color);

        DlgLockedCheck.IsChecked = s.Locked;
        DlgHiddenCheck.IsChecked = s.Hidden;

        UpdateFontPreview();
        UpdateFillPreview();
        UpdateBorderPreview();
    }

    private void NumberFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncDecimalPlacesFromSelectedNumberFormat();
        UpdateNumberPreview();
    }

    private void NumberCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NumberCategoryList.SelectedItem is not string category)
            return;

        var labels = NumberFormatOptions
            .Where(option => option.Category == category)
            .Select(option => option.Label)
            .Distinct()
            .ToArray();

        NumberFormatCombo.ItemsSource = labels;
        NumberFormatCombo.SelectedIndex = labels.Length > 0 ? 0 : -1;
        SyncDecimalPlacesFromSelectedNumberFormat();
        UpdateNumberControlAvailability();
        UpdateNumberPreview();
    }

    private void NumberFormatControl_Changed(object sender, RoutedEventArgs e)
    {
        if (NumberPreview is null)
            return;

        UpdateNumberPreview();
    }

    private void FontStyleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DlgFontStyleList.SelectedItem is not string style)
            return;

        UpdateFontPreview();
    }

    private void UnderlineStyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DlgUnderlineStyleBox.SelectedItem is not string underline)
            return;

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
        if (!string.IsNullOrWhiteSpace(trimmedText))
            return FindNumberFormatOption(trimmedText)?.Code ?? trimmedText;

        return null;
    }

    public static string? ResolveNumberFormat(
        string text,
        int selectedIndex,
        string? category,
        string? decimalPlacesText,
        string? symbol,
        int negativeIndex)
    {
        var baseFormat = ResolveNumberFormat(text, selectedIndex);
        if (baseFormat is null)
            return null;

        var decimals = ParseDecimalPlaces(decimalPlacesText);
        if (decimals is null)
            return baseFormat;

        return category switch
        {
            "Number" => BuildNumberFormat(decimals.Value, negativeIndex),
            "Currency" => BuildCurrencyFormat(decimals.Value, NormalizeSymbol(symbol), negativeIndex),
            "Accounting" => BuildAccountingFormat(decimals.Value, NormalizeSymbol(symbol)),
            "Percentage" => BuildPercentageFormat(decimals.Value),
            "Scientific" => $"0{DecimalPart(decimals.Value)}E+00",
            _ => baseFormat
        };
    }

    private static int? ParseDecimalPlaces(string? text)
    {
        if (!int.TryParse(text?.Trim(), out var decimals))
            return null;

        return Math.Clamp(decimals, 0, 30);
    }

    private static string NormalizeSymbol(string? symbol) =>
        string.IsNullOrWhiteSpace(symbol) || string.Equals(symbol, "None", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : symbol.Trim();

    private static string BuildNumberFormat(int decimals, int negativeIndex)
    {
        var format = $"#,##0{DecimalPart(decimals)}";
        return ApplyNegativeFormat(format, negativeIndex);
    }

    private static string BuildCurrencyFormat(int decimals, string symbol, int negativeIndex)
    {
        var format = $"{symbol}#,##0{DecimalPart(decimals)}";
        return ApplyNegativeFormat(format, negativeIndex);
    }

    private static string BuildAccountingFormat(int decimals, string symbol)
    {
        var decimalPart = DecimalPart(decimals);
        var zeroPadding = decimals > 0 ? new string('?', decimals) : string.Empty;
        var zeroPart = decimals > 0 ? $"\"-\"{zeroPadding}" : "\"-\"";
        return $"_({symbol}* #,##0{decimalPart}_);_({symbol}* (#,##0{decimalPart});_({symbol}* {zeroPart}_);_(@_)";
    }

    private static string BuildPercentageFormat(int decimals) =>
        $"0{DecimalPart(decimals)}%";

    private static string ApplyNegativeFormat(string format, int negativeIndex) =>
        negativeIndex switch
        {
            1 => $"{format};[Red]-{format}",
            2 => $"{format};({format})",
            3 => $"{format};[Red]({format})",
            _ => format
        };

    private static string DecimalPart(int decimals) =>
        decimals > 0 ? "." + new string('0', decimals) : string.Empty;

    public static int? TryParseSupportedTextRotation(string text)
        => FormatCellsInputParser.TryParseSupportedTextRotation(text);

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        CellColor? fontColor = TryParseColor(DlgFontColorBox.Text);
        CellColor? fillColor = TryParseColor(DlgFillColorBox.Text);
        CellColor? fillPatternColor = TryParseColor(DlgFillPatternColorBox.Text);
        var fillPatternStyle = SelectedFillPatternStyle();
        bool clearFill = DlgClearFillCheck.IsChecked == true;

        string? numFmt = ResolveSelectedNumberFormat();

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
            Bold:            IsSelectedFontBold(),
            Italic:          IsSelectedFontItalic(),
            Underline:       IsSingleUnderlineSelected(),
            Strikethrough:   DlgStrikeCheck.IsChecked,
            Superscript:     DlgSuperscriptCheck.IsChecked,
            Subscript:       DlgSubscriptCheck.IsChecked,
            FontName:        DlgFontNameBox.SelectedItem as string,
            FontSize:        fontSize,
            FontColor:       fontColor,
            FillColor:       clearFill ? null : fillColor,
            FillPatternStyle: clearFill ? CellFillPatternStyle.None : fillPatternStyle,
            FillPatternColor: clearFill ? null : fillPatternColor,
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
            Hidden:          DlgHiddenCheck.IsChecked,
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

    private void DlgFillPatternColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgFillPatternColorBox, allowNoColor: true);

    private void DlgBorderLineColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderLineColorBox, allowNoColor: false);

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

    private void DlgBorderLineColorSwatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string colorText })
            DlgBorderLineColorBox.Text = colorText;
    }

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
        => (DlgBorderLineStyleList.SelectedItem as string ?? DlgBorderLineStyleBox.SelectedItem as string) is string selectedStyle
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
        DlgFontSamplePreview.FontWeight = IsSelectedFontBold() ? FontWeights.Bold : FontWeights.Normal;
        DlgFontSamplePreview.FontStyle = IsSelectedFontItalic() ? FontStyles.Italic : FontStyles.Normal;
        DlgFontSamplePreview.Foreground = BrushForColor(TryParseColor(DlgFontColorBox.Text), Brushes.Black);

        var decorations = new TextDecorationCollection();
        if (IsSingleUnderlineSelected() || DlgDoubleUnderlineCheck.IsChecked == true)
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

    private bool IsSelectedFontBold()
        => DlgFontStyleList.SelectedItem is string style
            && style.Contains("Bold", StringComparison.OrdinalIgnoreCase);

    private bool IsSelectedFontItalic()
        => DlgFontStyleList.SelectedItem is string style
            && style.Contains("Italic", StringComparison.OrdinalIgnoreCase);

    private bool IsSingleUnderlineSelected()
        => DlgUnderlineStyleBox.SelectedItem is string underline
            && underline == "Single";

    private static NumberFormatOption? FindNumberFormatOption(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmedText = text.Trim();
        return NumberFormatOptions.FirstOrDefault(option =>
            string.Equals(option.Label, trimmedText, StringComparison.OrdinalIgnoreCase)
            || string.Equals(option.Code, trimmedText, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectNumberFormatOption(NumberFormatOption option)
    {
        NumberFormatCombo.SelectedItem = option.Label;
        if (!string.Equals(NumberFormatCombo.SelectedItem as string, option.Label, StringComparison.Ordinal))
            NumberFormatCombo.Text = option.Label;
    }

    private string? ResolveSelectedNumberFormat()
    {
        var category = NumberCategoryList.SelectedItem as string;
        var decimals = SelectedDecimalPlaces();

        return category switch
        {
            "Number" => BuildSignedNumberFormat(NumberPattern(decimals)),
            "Currency" => BuildSignedNumberFormat(CurrencyPattern(decimals)),
            "Accounting" => AccountingPattern(decimals),
            "Percentage" => $"0{DecimalPattern(decimals)}%",
            "Scientific" => $"0{DecimalPattern(decimals)}E+00",
            _ => ResolveNumberFormat(NumberFormatCombo.Text, NumberFormatCombo.SelectedIndex)
        };
    }

    private void UpdateNumberControlAvailability()
    {
        if (NumberCategoryList?.SelectedItem is not string category)
            return;

        var usesDecimals = category is "Number" or "Currency" or "Accounting" or "Percentage" or "Scientific";
        var usesSymbol = category is "Currency" or "Accounting";
        var usesNegativeOptions = category is "Number" or "Currency";

        NumberDecimalPlacesBox.IsEnabled = usesDecimals;
        NumberSymbolCombo.IsEnabled = usesSymbol;
        NumberNegativeNumbersList.IsEnabled = usesNegativeOptions;
    }

    private void UpdateNumberPreview()
    {
        if (NumberPreview is null)
            return;

        NumberPreview.Text = ResolveSelectedNumberFormat() is { } generatedFormat
            ? PreviewForFormat(generatedFormat)
            : PreviewForFormat(NumberFormatCombo.SelectedItem as string ?? NumberFormatCombo.Text);
    }

    private void SyncDecimalPlacesFromSelectedNumberFormat()
    {
        if (_syncingNumberControls || NumberDecimalPlacesBox is null)
            return;

        var selectedFormat = ResolveNumberFormat(NumberFormatCombo.SelectedItem as string ?? NumberFormatCombo.Text, NumberFormatCombo.SelectedIndex);
        if (selectedFormat is null)
            return;

        _syncingNumberControls = true;
        NumberDecimalPlacesBox.Text = DecimalPlacesForFormat(selectedFormat).ToString();
        _syncingNumberControls = false;
    }

    private string BuildSignedNumberFormat(string positivePattern)
    {
        var negativePattern = NumberNegativeNumbersList.SelectedIndex switch
        {
            1 => $"[Red]-{positivePattern}",
            2 => $"({positivePattern})",
            3 => $"[Red]({positivePattern})",
            _ => ""
        };

        return string.IsNullOrEmpty(negativePattern)
            ? positivePattern
            : $"{positivePattern};{negativePattern}";
    }

    private string NumberPattern(int decimals) => $"#,##0{DecimalPattern(decimals)}";

    private string CurrencyPattern(int decimals)
    {
        var symbol = SelectedCurrencySymbol();
        return string.IsNullOrEmpty(symbol)
            ? NumberPattern(decimals)
            : $"{symbol}{NumberPattern(decimals)}";
    }

    private string AccountingPattern(int decimals)
    {
        var symbol = SelectedCurrencySymbol();
        var symbolToken = string.IsNullOrEmpty(symbol) ? "" : symbol;
        var decimalPattern = DecimalPattern(decimals);
        var zeroPadding = decimals > 0 ? "??" : "";

        return $"_({symbolToken}* #,##0{decimalPattern}_);_({symbolToken}* (#,##0{decimalPattern});_({symbolToken}* \"-\"{zeroPadding}_);_(@_)";
    }

    private int SelectedDecimalPlaces()
    {
        if (!int.TryParse(NumberDecimalPlacesBox.Text.Trim(), out var decimals))
            decimals = 2;

        return Math.Clamp(decimals, 0, 30);
    }

    private string SelectedCurrencySymbol()
    {
        var value = NumberSymbolCombo.SelectedItem as string ?? NumberSymbolCombo.Text;
        return string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) ? "" : value;
    }

    private static string DecimalPattern(int decimals)
        => decimals <= 0 ? "" : "." + new string('0', decimals);

    private static int DecimalPlacesForFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return 2;

        var firstSection = format.Split(';')[0];
        var dotIndex = firstSection.IndexOf('.');
        if (dotIndex < 0)
            return 0;

        var count = 0;
        for (var i = dotIndex + 1; i < firstSection.Length && firstSection[i] is '0' or '#'; i++)
            count++;

        return Math.Clamp(count, 0, 30);
    }

    private static string PreviewForFormat(string? text)
    {
        var format = FindNumberFormatOption(text)?.Code ?? text;
        if (string.IsNullOrWhiteSpace(format))
            return "1234.56";

        try
        {
            if (LooksLikeDateTimeFormat(format))
            {
                var sampleDate = new DateTime(2026, 5, 21, 13, 30, 0).ToOADate();
                return NumberFormatter.Format(new DateTimeValue(sampleDate), format);
            }

            if (format.Contains('@', StringComparison.Ordinal))
                return NumberFormatter.Format(new TextValue("Sample"), format);

            return NumberFormatter.Format(new NumberValue(1234.56), format);
        }
        catch
        {
            return FindNumberFormatOption(text)?.Preview ?? "1234.56";
        }
    }

    private static bool LooksLikeDateTimeFormat(string format)
    {
        var lower = format.ToLowerInvariant();
        return lower.Contains('y')
            || lower.Contains("am/pm", StringComparison.Ordinal)
            || lower.Contains("[h]", StringComparison.Ordinal)
            || lower.Contains("h:", StringComparison.Ordinal)
            || lower.Contains(":mm", StringComparison.Ordinal)
            || lower.Contains("m/d", StringComparison.Ordinal)
            || lower.Contains("d-m", StringComparison.Ordinal)
            || lower.Contains("mmmm", StringComparison.Ordinal);
    }

    private static string[] FontNamesWithFallback(string fontName)
    {
        var fonts = Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(font => font, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(fontName)
            && !fonts.Contains(fontName, StringComparer.CurrentCultureIgnoreCase))
        {
            fonts.Insert(0, fontName);
        }

        return fonts.ToArray();
    }
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
