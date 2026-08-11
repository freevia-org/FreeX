using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Services;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.Host;

public partial class FormatCellsDialog : Window
{
    public StyleDiff? ResultDiff { get; private set; }
    public FormatCellsDialogBorderSelection ResultBorderSelection { get; private set; } = FormatCellsDialogBorderSelection.None;
    public bool? ResultMergeCells { get; private set; }

    private readonly CellStyle _current;
    private readonly bool _initialMergeCells;
    private readonly string? _numberPreviewText;
    private bool _syncingNumberControls;
    private bool _borderPresetClearRequested;
    private CellBorder? _borderPresetOutline;
    private CellBorder? _borderPresetInside;

    public FormatCellsDialog(
        CellStyle current,
        FormatCellsDialogTab initialTab = FormatCellsDialogTab.Number,
        bool mergeCells = false,
        string? numberPreviewText = null)
    {
        _current = current.Clone();
        _initialMergeCells = mergeCells;
        _numberPreviewText = string.IsNullOrWhiteSpace(numberPreviewText) ? null : numberPreviewText;
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
        PopulateFillPalettes();
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
        DlgFontStyleList.ItemsSource = new[]
        {
            FormatCellsDialogPlanner.FontStyleLabel(false, false, FontLabels()),
            FormatCellsDialogPlanner.FontStyleLabel(false, true, FontLabels()),
            FormatCellsDialogPlanner.FontStyleLabel(true, false, FontLabels()),
            FormatCellsDialogPlanner.FontStyleLabel(true, true, FontLabels())
        };
        DlgFontStyleList.SelectedItem = FontStyleLabel(s.Bold, s.Italic);
        DlgUnderlineStyleBox.ItemsSource = new[]
        {
            FontLabels().UnderlineNone,
            FontLabels().UnderlineSingle,
            FontLabels().UnderlineDouble,
            FontLabels().UnderlineSingleAccounting,
            FontLabels().UnderlineDoubleAccounting
        };
        DlgDoubleUnderlineCheck.IsChecked = s.DoubleUnderline;
        DlgUnderlineStyleBox.SelectedItem = s.DoubleUnderline
            ? UiText.Get("FormatCells_UnderlineDouble")
            : s.Underline
                ? FontLabels().UnderlineSingle
                : FontLabels().UnderlineNone;
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
        DlgFillPatternStyleBox.ItemsSource = FillPatternDisplayChoices().Select(option => option.Label).ToArray();
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

        DlgDoubleUnderlineCheck.IsChecked = IsDoubleUnderlineSelected(underline);
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
        => FormatCellsDialogPlanner.TryParseSupportedTextRotation(text);

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!FormatCellsDialogPlanner.TryCreateResult(
                _current,
                CreatePlannerInput(),
                out var result,
                out var validation))
        {
            ShowPlannerValidation(validation!);
            return;
        }

        ResultDiff = result!.Diff;
        ResultBorderSelection = new FormatCellsDialogBorderSelection(
            result.BorderSelection.Clear,
            result.BorderSelection.Outline,
            result.BorderSelection.Inside);
        ResultMergeCells = result.MergeCells;

        DialogResult = true;
    }

    private FormatCellsDialogInput CreatePlannerInput() =>
        new(
            Number: new FormatCellsDialogNumberInput(
                NumberCategoryList.SelectedItem as string,
                NumberFormatCombo.Text,
                NumberFormatCombo.SelectedIndex,
                NumberDecimalPlacesBox.Text,
                NumberSymbolCombo.SelectedItem as string ?? NumberSymbolCombo.Text,
                NumberNegativeNumbersList.SelectedIndex),
            Font: new FormatCellsDialogFontInput(
                FontLabels(),
                DlgFontNameBox.Text,
                DlgFontNameBox.SelectedItem as string,
                DlgFontSizeBox.Text,
                DlgFontStyleList.SelectedItem as string,
                DlgUnderlineStyleBox.SelectedItem as string,
                DlgDoubleUnderlineCheck.IsChecked,
                DlgStrikeCheck.IsChecked,
                DlgSuperscriptCheck.IsChecked,
                DlgSubscriptCheck.IsChecked,
                DlgFontColorBox.Text),
            Fill: new FormatCellsDialogFillInput(
                DlgFillColorBox.Text,
                DlgFillPatternColorBox.Text,
                SelectedFillPatternStyle(),
                DlgClearFillCheck.IsChecked == true),
            Alignment: new FormatCellsDialogAlignmentInput(
                DlgHAlignBox.SelectedItem as string,
                DlgVAlignBox.SelectedItem as string,
                DlgWrapTextCheck.IsChecked,
                DlgShrinkToFitCheck.IsChecked,
                DlgIndentLevelBox.Text,
                DlgTextRotationBox.Text,
                _initialMergeCells,
                DlgMergeCellsCheck.IsChecked),
            Border: new FormatCellsDialogBorderInput(
                DlgBorderLineColorBox.Text,
                new FormatCellsDialogBorderSideInput(DlgBorderTopStyleBox.SelectedItem as string, DlgBorderTopColorBox.Text),
                new FormatCellsDialogBorderSideInput(DlgBorderRightStyleBox.SelectedItem as string, DlgBorderRightColorBox.Text),
                new FormatCellsDialogBorderSideInput(DlgBorderBottomStyleBox.SelectedItem as string, DlgBorderBottomColorBox.Text),
                new FormatCellsDialogBorderSideInput(DlgBorderLeftStyleBox.SelectedItem as string, DlgBorderLeftColorBox.Text),
                _borderPresetClearRequested,
                _borderPresetOutline,
                _borderPresetInside),
            Protection: new FormatCellsDialogProtectionInput(
                DlgLockedCheck.IsChecked,
                DlgHiddenCheck.IsChecked));

    private void ShowPlannerValidation(FormatCellsDialogValidation validation)
    {
        Tabs.SelectedIndex = validation.Tab switch
        {
            FormatCellsDialogPlannerTab.Alignment => (int)FormatCellsDialogTab.Alignment,
            FormatCellsDialogPlannerTab.Font => (int)FormatCellsDialogTab.Font,
            FormatCellsDialogPlannerTab.Fill => (int)FormatCellsDialogTab.Fill,
            FormatCellsDialogPlannerTab.Border => (int)FormatCellsDialogTab.Border,
            FormatCellsDialogPlannerTab.Protection => (int)FormatCellsDialogTab.Protection,
            _ => (int)FormatCellsDialogTab.Number
        };

        var message = UiText.Get(validation.MessageResourceKey);
        switch (validation.Target)
        {
            case FormatCellsDialogValidationTarget.NumberFormat:
                ShowInvalidInputWarning(message, NumberFormatCombo);
                break;
            case FormatCellsDialogValidationTarget.FontSize:
                ShowInvalidInputWarning(message, DlgFontSizeBox);
                break;
            case FormatCellsDialogValidationTarget.FontColor:
                ShowInvalidInputWarning(message, DlgFontColorBox);
                break;
            case FormatCellsDialogValidationTarget.FillColor:
                ShowInvalidInputWarning(message, DlgFillColorBox);
                break;
            case FormatCellsDialogValidationTarget.FillPatternColor:
                ShowInvalidInputWarning(message, DlgFillPatternColorBox);
                break;
            case FormatCellsDialogValidationTarget.IndentLevel:
                ShowInvalidInputWarning(message, DlgIndentLevelBox);
                break;
            case FormatCellsDialogValidationTarget.TextRotation:
                ShowInvalidInputWarning(message, DlgTextRotationBox);
                break;
            case FormatCellsDialogValidationTarget.BorderLineColor:
                ShowInvalidInputWarning(message, DlgBorderLineColorBox);
                break;
            case FormatCellsDialogValidationTarget.BorderTopColor:
                ShowInvalidInputWarning(message, DlgBorderTopColorBox);
                break;
            case FormatCellsDialogValidationTarget.BorderRightColor:
                ShowInvalidInputWarning(message, DlgBorderRightColorBox);
                break;
            case FormatCellsDialogValidationTarget.BorderBottomColor:
                ShowInvalidInputWarning(message, DlgBorderBottomColorBox);
                break;
            case FormatCellsDialogValidationTarget.BorderLeftColor:
                ShowInvalidInputWarning(message, DlgBorderLeftColorBox);
                break;
            default:
                ShowInvalidInputWarning(message, NumberDecimalPlacesBox);
                break;
        }
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        return true;
    }

    private bool ShowInvalidInputWarning(string message, ComboBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        return true;
    }
}

public enum FormatCellsDialogTab
{
    Number,
    Alignment,
    Font,
    Border,
    Fill,
    Protection
}
