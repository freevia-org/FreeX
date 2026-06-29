using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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

        PopulateChoiceBoxes();
        OrientationBox.SelectedIndex = PageSetupDialogPlanner.OrientationChoices.IndexOf(fields.Orientation);
        PaperSizeBox.SelectedIndex = PageSetupDialogPlanner.PaperSizeChoices.IndexOf(fields.PaperSize);
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
        PageOrderBox.SelectedIndex = PageSetupDialogPlanner.PageOrderChoices.IndexOf(fields.PageOrder);
        PrintBlackAndWhiteBox.IsChecked = fields.PrintBlackAndWhite;
        PrintDraftQualityBox.IsChecked = fields.PrintDraftQuality;
        PrintErrorValueBox.SelectedIndex = PageSetupDialogPlanner.PrintErrorValueChoices.IndexOf(fields.PrintErrorValue);
        PrintCommentsBox.SelectedIndex = PageSetupDialogPlanner.PrintCommentChoices.IndexOf(fields.PrintComments);
        PopulateHeaderFooterPresetBoxes();
        SelectPreset(HeaderPresetBox, Header.Center);
        SelectPreset(FooterPresetBox, Footer.Center);
        DifferentFirstPageBox.IsChecked = DifferentFirstPage;
        DifferentOddEvenBox.IsChecked = DifferentOddEvenPages;
        ScaleWithDocumentBox.IsChecked = ScaleHeaderFooterWithDocument;
        AlignWithMarginsBox.IsChecked = AlignHeaderFooterWithMargins;
        UpdateScalingInputState();
        UpdateHeaderFooterPreview();
    }

    private void PopulateChoiceBoxes()
    {
        PopulateChoiceBox(OrientationBox, PageSetupDialogPlanner.OrientationChoices);
        PopulateChoiceBox(PaperSizeBox, PageSetupDialogPlanner.PaperSizeChoices);
        PopulateChoiceBox(PageOrderBox, PageSetupDialogPlanner.PageOrderChoices);
        PopulateChoiceBox(PrintErrorValueBox, PageSetupDialogPlanner.PrintErrorValueChoices);
        PopulateChoiceBox(PrintCommentsBox, PageSetupDialogPlanner.PrintCommentChoices);
    }

    private static void PopulateChoiceBox<T>(ComboBox comboBox, PageSetupChoicePlan<T> plan)
    {
        comboBox.Items.Clear();

        foreach (var choice in plan.Choices)
        {
            comboBox.Items.Add(new ComboBoxItem
            {
                Content = UiText.Get(choice.LabelResourceKey),
                Tag = choice.Value?.ToString() ?? string.Empty
            });
        }
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
        var openPlan = PageSetupDialogPlanner.PlanOpen(_initialFocusTarget);
        switch (openPlan.InitialFocusTarget)
        {
            case PageSetupInitialFocusTarget.Margins:
                PageSetupTabs.SelectedItem = MarginsTab;
                DialogFocus.FocusAndSelect(LeftMarginBox);
                return;
            case PageSetupInitialFocusTarget.PaperSize:
                PageSetupTabs.SelectedItem = PageTab;
                PaperSizeBox.Focus();
                Keyboard.Focus(PaperSizeBox);
                return;
            case PageSetupInitialFocusTarget.RepeatRows:
                PageSetupTabs.SelectedItem = SheetTab;
                DialogFocus.FocusAndSelect(RowsRepeatBox);
                return;
            case PageSetupInitialFocusTarget.ScaleToFit:
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
