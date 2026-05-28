using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class PageSetupDialog
{
    private void PopulateFields()
    {
        OrientationBox.SelectedIndex = Orientation == WorksheetPageOrientation.Landscape ? 1 : 0;
        PaperSizeBox.SelectedIndex = PaperSize switch
        {
            WorksheetPaperSize.Letter => 0,
            WorksheetPaperSize.Legal => 2,
            _ => 1
        };
        LeftMarginBox.Text = Margins.Left.ToString(CultureInfo.InvariantCulture);
        RightMarginBox.Text = Margins.Right.ToString(CultureInfo.InvariantCulture);
        TopMarginBox.Text = Margins.Top.ToString(CultureInfo.InvariantCulture);
        BottomMarginBox.Text = Margins.Bottom.ToString(CultureInfo.InvariantCulture);
        HeaderMarginBox.Text = HeaderMargin.ToString(CultureInfo.InvariantCulture);
        FooterMarginBox.Text = FooterMargin.ToString(CultureInfo.InvariantCulture);
        CenterHorizontallyBox.IsChecked = CenterHorizontally;
        CenterVerticallyBox.IsChecked = CenterVertically;
        if (ScaleToFit.ScalePercent.HasValue)
        {
            AdjustToRadioButton.IsChecked = true;
            ScalePercentBox.Text = ScaleToFit.ScalePercent.Value.ToString(CultureInfo.InvariantCulture);
            FitPagesWideBox.Text = "1";
            FitPagesTallBox.Text = "1";
        }
        else
        {
            FitToRadioButton.IsChecked = true;
            ScalePercentBox.Text = "100";
            FitPagesWideBox.Text = (ScaleToFit.FitToPagesWide ?? 1).ToString(CultureInfo.InvariantCulture);
            FitPagesTallBox.Text = (ScaleToFit.FitToPagesTall ?? 1).ToString(CultureInfo.InvariantCulture);
        }

        FirstPageNumberBox.Text = FirstPageNumber?.ToString(CultureInfo.InvariantCulture) ?? "";
        PrintQualityBox.Text = PrintQualityDpi?.ToString(CultureInfo.InvariantCulture) ?? "";
        PrintAreaBox.Text = PrintArea is { } printArea
            ? PageSetupRangeSelectionFormatter.Format(PageSetupRangeSelectionTarget.PrintArea, printArea, useR1C1ReferenceStyle: false)
            : "";
        RowsRepeatBox.Text = PrintTitleRows is { } rows ? $"${rows.Start}:${rows.End}" : "";
        ColumnsRepeatBox.Text = PrintTitleColumns is { } cols
            ? $"${CellAddress.NumberToColumnName(cols.Start)}:${CellAddress.NumberToColumnName(cols.End)}"
            : "";
        PrintGridlinesBox.IsChecked = PrintGridlines;
        PrintHeadingsBox.IsChecked = PrintHeadings;
        PageOrderBox.SelectedIndex = PageOrder == WorksheetPageOrder.OverThenDown ? 1 : 0;
        PrintBlackAndWhiteBox.IsChecked = PrintBlackAndWhite;
        PrintDraftQualityBox.IsChecked = PrintDraftQuality;
        PrintErrorValueBox.SelectedIndex = PrintErrorValue switch
        {
            WorksheetPrintErrorValue.Blank => 1,
            WorksheetPrintErrorValue.Dash => 2,
            WorksheetPrintErrorValue.NotAvailable => 3,
            _ => 0
        };
        PrintCommentsBox.SelectedIndex = PrintComments switch
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
