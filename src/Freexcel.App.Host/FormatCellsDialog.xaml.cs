using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.Host;

public partial class FormatCellsDialog : Window
{
    public StyleDiff? ResultDiff { get; private set; }
    public FormatCellsBorderSelection ResultBorderSelection { get; private set; } = FormatCellsBorderSelection.None;
    public bool? ResultMergeCells { get; private set; }

    private readonly CellStyle _current;
    private readonly bool _initialMergeCells;
    private bool _syncingNumberControls;
    private bool _borderPresetClearRequested;
    private CellBorder? _borderPresetOutline;
    private CellBorder? _borderPresetInside;

    public FormatCellsDialog(
        CellStyle current,
        FormatCellsDialogTab initialTab = FormatCellsDialogTab.Number,
        bool mergeCells = false)
    {
        _current = current.Clone();
        _initialMergeCells = mergeCells;
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
            (int)FormatCellsDialogTab.Border => DlgBorderLineStyleList,
            (int)FormatCellsDialogTab.Protection => DlgLockedCheck,
            _ => NumberCategoryList
        };

        target.Focus();
        Keyboard.Focus(target);
    }

    private void Populate(CellStyle s)
    {
        NumberCategoryList.ItemsSource = FormatCellsNumberFormatPlanner.Categories;
        NumberSymbolCombo.ItemsSource = FormatCellsNumberFormatPlanner.Symbols;
        NumberSymbolCombo.SelectedIndex = 0;
        NumberNegativeNumbersList.ItemsSource = FormatCellsNumberFormatPlanner.NegativeOptions;
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
        DlgUnderlineStyleBox.ItemsSource = new[] { "None", "Single", "Double", "Single Accounting", "Double Accounting" };
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
        DlgMergeCellsCheck.IsChecked = _initialMergeCells;
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

        var labels = FormatCellsNumberFormatPlanner.LabelsForCategory(category);

        NumberFormatCombo.ItemsSource = labels;
        NumberFormatCombo.SelectedIndex = labels.Count > 0 ? 0 : -1;
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

        DlgDoubleUnderlineCheck.IsChecked = underline is "Double" or "Double Accounting";
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
        if (!TryParseRequiredColor(DlgFontColorBox.Text, out var fontColor))
        {
            Tabs.SelectedIndex = (int)FormatCellsDialogTab.Font;
            ShowInvalidInputWarning("Enter a font color as #RRGGBB or R, G, B.", DlgFontColorBox);
            return;
        }

        if (!TryParseOptionalColor(DlgFillColorBox.Text, out var fillColor))
        {
            Tabs.SelectedIndex = (int)FormatCellsDialogTab.Fill;
            ShowInvalidInputWarning("Enter a fill color as #RRGGBB or R, G, B, or leave it blank.", DlgFillColorBox);
            return;
        }

        if (!TryParseOptionalColor(DlgFillPatternColorBox.Text, out var fillPatternColor))
        {
            Tabs.SelectedIndex = (int)FormatCellsDialogTab.Fill;
            ShowInvalidInputWarning("Enter a pattern color as #RRGGBB or R, G, B, or leave it blank.", DlgFillPatternColorBox);
            return;
        }

        var fillPatternStyle = SelectedFillPatternStyle();
        bool clearFill = DlgClearFillCheck.IsChecked == true;

        if (!ValidateNumberInputs())
            return;

        string? numFmt = ResolveSelectedNumberFormat();

        double? fontSize = FormatCellsInputParser.TryParseFontSize(DlgFontSizeBox.Text);
        if (fontSize is null)
        {
            Tabs.SelectedIndex = (int)FormatCellsDialogTab.Font;
            ShowInvalidInputWarning("Enter a positive font size.", DlgFontSizeBox);
            return;
        }

        CellHAlign? hAlign = null;
        if (DlgHAlignBox.SelectedItem is string ha && Enum.TryParse(ha, out CellHAlign h)) hAlign = h;
        CellVAlign? vAlign = null;
        if (DlgVAlignBox.SelectedItem is string va && Enum.TryParse(va, out CellVAlign v)) vAlign = v;

        int? indentLevel = FormatCellsInputParser.TryParseIndentLevel(DlgIndentLevelBox.Text);
        if (indentLevel is null)
        {
            Tabs.SelectedIndex = (int)FormatCellsDialogTab.Alignment;
            ShowInvalidInputWarning("Enter an indent level from 0 to 15.", DlgIndentLevelBox);
            return;
        }

        int? textRotation = FormatCellsInputParser.TryParseSupportedTextRotation(DlgTextRotationBox.Text);
        if (textRotation is null)
        {
            Tabs.SelectedIndex = (int)FormatCellsDialogTab.Alignment;
            ShowInvalidInputWarning("Enter a text rotation from -90 to 90 degrees, or 255 for vertical text.", DlgTextRotationBox);
            return;
        }

        if (!ValidateBorderInputs())
            return;

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
            FontName:        ResolveSelectedFontName(),
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
        ResultMergeCells = DlgMergeCellsCheck.IsChecked == _initialMergeCells
            ? null
            : DlgMergeCellsCheck.IsChecked == true;

        DialogResult = true;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        MessageBox.Show(
            this,
            message,
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
        return true;
    }

    private bool ShowInvalidInputWarning(string message, ComboBox target)
    {
        MessageBox.Show(
            this,
            message,
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        target.Focus();
        Keyboard.Focus(target);
        return true;
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
