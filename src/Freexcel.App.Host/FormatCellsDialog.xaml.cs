using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.Host;

public partial class FormatCellsDialog : Window
{
    public StyleDiff? ResultDiff { get; private set; }
    public FormatCellsBorderSelection ResultBorderSelection { get; private set; } = FormatCellsBorderSelection.None;

    private readonly CellStyle _current;
    private bool _syncingNumberControls;
    private bool _borderPresetClearRequested;
    private CellBorder? _borderPresetOutline;
    private CellBorder? _borderPresetInside;

    public FormatCellsDialog(CellStyle current, FormatCellsDialogTab initialTab = FormatCellsDialogTab.Number)
    {
        _current = current.Clone();
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Populate(_current);
            Tabs.SelectedIndex = (int)initialTab;
            FocusInitialKeyboardTarget();
        };
    }

    private void FocusInitialKeyboardTarget()
    {
        Control target = Tabs.SelectedIndex switch
        {
            (int)FormatCellsDialogTab.Alignment => DlgHAlignBox,
            (int)FormatCellsDialogTab.Font => DlgFontNameBox,
            (int)FormatCellsDialogTab.Fill => DlgFillColorBox,
            (int)FormatCellsDialogTab.Border => DlgBorderLineStyleBox,
            (int)FormatCellsDialogTab.Protection => DlgLockedCheck,
            _ => NumberCategoryList
        };

        target.Focus();
        Keyboard.Focus(target);
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
        ResultBorderSelection = new FormatCellsBorderSelection(
            _borderPresetClearRequested,
            _borderPresetOutline,
            _borderPresetInside);

        DialogResult = true;
    }

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
