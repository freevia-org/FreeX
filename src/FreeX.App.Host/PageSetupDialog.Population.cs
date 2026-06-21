using System.Globalization;
using System.Windows;
using System.Windows.Input;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class PageSetupDialog
{
    private void PopulateFields()
    {
        var fields = Fields;
        var margins = ParseMarginsForDisplay(fields.MarginsText);

        OrientationBox.SelectedIndex = fields.Orientation == WorksheetPageOrientation.Landscape ? 1 : 0;
        PaperSizeBox.SelectedIndex = fields.PaperSize switch
        {
            WorksheetPaperSize.Letter => 0,
            WorksheetPaperSize.Legal => 2,
            _ => 1
        };
        LeftMarginBox.Text = margins.Left.ToString(CultureInfo.InvariantCulture);
        RightMarginBox.Text = margins.Right.ToString(CultureInfo.InvariantCulture);
        TopMarginBox.Text = margins.Top.ToString(CultureInfo.InvariantCulture);
        BottomMarginBox.Text = margins.Bottom.ToString(CultureInfo.InvariantCulture);
        HeaderMarginBox.Text = fields.HeaderMarginText;
        FooterMarginBox.Text = fields.FooterMarginText;
        CenterHorizontallyBox.IsChecked = fields.CenterHorizontally;
        CenterVerticallyBox.IsChecked = fields.CenterVertically;
        if (fields.ScalingMode == PageSetupScalingMode.AdjustToPercent)
        {
            AdjustToRadioButton.IsChecked = true;
            ScalePercentBox.Text = fields.ScalePercentText;
            FitPagesWideBox.Text = "1";
            FitPagesTallBox.Text = "1";
        }
        else
        {
            FitToRadioButton.IsChecked = true;
            ScalePercentBox.Text = "100";
            FitPagesWideBox.Text = FitToDisplayText(fields.FitToWideText);
            FitPagesTallBox.Text = FitToDisplayText(fields.FitToTallText);
        }

        FirstPageNumberBox.Text = fields.FirstPageNumberText;
        PrintQualityBox.Text = fields.PrintQualityDpiText;
        PrintAreaBox.Text = _sourceSheet.PrintArea is { } printArea
            ? PageSetupRangeSelectionFormatter.Format(PageSetupRangeSelectionTarget.PrintArea, printArea, useR1C1ReferenceStyle: false)
            : fields.PrintAreaText;
        RowsRepeatBox.Text = _sourceSheet.PrintTitleRows is { } rows ? $"${rows.Start}:${rows.End}" : fields.RepeatRowsText;
        ColumnsRepeatBox.Text = _sourceSheet.PrintTitleColumns is { } cols
            ? $"${CellAddress.NumberToColumnName(cols.Start)}:${CellAddress.NumberToColumnName(cols.End)}"
            : fields.RepeatColumnsText;
        PrintGridlinesBox.IsChecked = fields.PrintGridlines;
        PrintHeadingsBox.IsChecked = fields.PrintHeadings;
        PageOrderBox.SelectedIndex = fields.PageOrder == WorksheetPageOrder.OverThenDown ? 1 : 0;
        PrintBlackAndWhiteBox.IsChecked = fields.PrintBlackAndWhite;
        PrintDraftQualityBox.IsChecked = fields.PrintDraftQuality;
        PrintErrorValueBox.SelectedIndex = fields.PrintErrorValue switch
        {
            WorksheetPrintErrorValue.Blank => 1,
            WorksheetPrintErrorValue.Dash => 2,
            WorksheetPrintErrorValue.NotAvailable => 3,
            _ => 0
        };
        PrintCommentsBox.SelectedIndex = fields.PrintComments switch
        {
            WorksheetPrintComments.AtEnd => 1,
            WorksheetPrintComments.AsDisplayed => 2,
            _ => 0
        };
        SelectPreset(HeaderPresetBox, Header.Center);
        SelectPreset(FooterPresetBox, Footer.Center);
        DifferentFirstPageBox.IsChecked = DifferentFirstPage;
        DifferentOddEvenBox.IsChecked = DifferentOddEvenPages;
        ScaleWithDocumentBox.IsChecked = ScaleHeaderFooterWithDocument;
        AlignWithMarginsBox.IsChecked = AlignHeaderFooterWithMargins;
        UpdateScalingInputState();
        UpdateHeaderFooterPreview();
    }

    private static WorksheetPageMargins ParseMarginsForDisplay(string marginsText) =>
        PageMarginInputParser.TryParse(marginsText, out var margins, out _)
            ? margins
            : WorksheetPageMargins.Narrow;

    private static string FitToDisplayText(string text) =>
        string.IsNullOrWhiteSpace(text) ? "1" : text;

    private void ScalingMode_Changed(object sender, RoutedEventArgs e) => UpdateScalingInputState();

    private void FocusInitialKeyboardTarget()
    {
        if (_initialFocusTarget == PageSetupInitialFocusTarget.RepeatRows)
        {
            PageSetupTabs.SelectedItem = SheetTab;
            DialogFocus.FocusAndSelect(RowsRepeatBox);
            return;
        }

        if (_initialFocusTarget == PageSetupInitialFocusTarget.ScaleToFit)
        {
            PageSetupTabs.SelectedItem = PageTab;
            var target = AdjustToRadioButton.IsChecked == true
                ? ScalePercentBox
                : FitPagesWideBox;
            DialogFocus.FocusAndSelect(target);
            return;
        }

        OrientationBox.Focus();
        Keyboard.Focus(OrientationBox);
    }

    private void UpdateScalingInputState()
    {
        if (ScalePercentBox is null || FitPagesWideBox is null || FitPagesTallBox is null)
            return;

        var adjustTo = AdjustToRadioButton.IsChecked == true;
        var fitTo = FitToRadioButton.IsChecked == true;
        ScalePercentBox.IsEnabled = adjustTo;
        FitPagesWideBox.IsEnabled = fitTo;
        FitPagesTallBox.IsEnabled = fitTo;
    }
}
