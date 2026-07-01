using System.Windows;
using System.Windows.Controls;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Host;

public partial class PageSetupDialog
{
    private void PopulateFields()
    {
        var surface = PageSetupDialogPlanner.PlanSurface(_sourceSheet, Fields);
        var fields = surface.Fields;

        PopulateChoiceBoxes();
        OrientationBox.SelectedIndex = surface.ChoiceIndexes.Orientation;
        PaperSizeBox.SelectedIndex = surface.ChoiceIndexes.PaperSize;
        LeftMarginBox.Text = surface.Margins.Left;
        RightMarginBox.Text = surface.Margins.Right;
        TopMarginBox.Text = surface.Margins.Top;
        BottomMarginBox.Text = surface.Margins.Bottom;
        HeaderMarginBox.Text = surface.HeaderMarginText;
        FooterMarginBox.Text = surface.FooterMarginText;
        CenterHorizontallyBox.IsChecked = fields.CenterHorizontally;
        CenterVerticallyBox.IsChecked = fields.CenterVertically;
        if (surface.Scaling.IsAdjustToPercent)
        {
            AdjustToRadioButton.IsChecked = true;
            ScalePercentBox.Text = surface.Scaling.ScalePercentText;
            FitPagesWideBox.Text = surface.Scaling.FitToWideText;
            FitPagesTallBox.Text = surface.Scaling.FitToTallText;
        }
        else
        {
            FitToRadioButton.IsChecked = true;
            ScalePercentBox.Text = surface.Scaling.ScalePercentText;
            FitPagesWideBox.Text = surface.Scaling.FitToWideText;
            FitPagesTallBox.Text = surface.Scaling.FitToTallText;
        }

        FirstPageNumberBox.Text = surface.FirstPageNumberText;
        PrintQualityBox.Text = surface.PrintQualityDpiText;
        PrintAreaBox.Text = surface.PrintAreaText;
        RowsRepeatBox.Text = surface.RepeatRowsText;
        ColumnsRepeatBox.Text = surface.RepeatColumnsText;
        PrintGridlinesBox.IsChecked = fields.PrintGridlines;
        PrintHeadingsBox.IsChecked = fields.PrintHeadings;
        PageOrderBox.SelectedIndex = surface.ChoiceIndexes.PageOrder;
        PrintBlackAndWhiteBox.IsChecked = fields.PrintBlackAndWhite;
        PrintDraftQualityBox.IsChecked = fields.PrintDraftQuality;
        PrintErrorValueBox.SelectedIndex = surface.ChoiceIndexes.PrintErrorValue;
        PrintCommentsBox.SelectedIndex = surface.ChoiceIndexes.PrintComments;
        PopulateHeaderFooterPresetBoxes();
        HeaderPresetBox.SelectedIndex = surface.ChoiceIndexes.HeaderPreset;
        FooterPresetBox.SelectedIndex = surface.ChoiceIndexes.FooterPreset;
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

    private void ScalingMode_Changed(object sender, RoutedEventArgs e) => UpdateScalingInputState();

    private void FocusInitialKeyboardTarget()
    {
        var plan = PageSetupDialogPlanner.PlanInitialFocus(
            PageSetupDialogPlanner.PlanOpen(_initialFocusTarget),
            FitToRadioButton.IsChecked == true
                ? PageSetupScalingMode.FitToPages
                : PageSetupScalingMode.AdjustToPercent);
        FocusDialogTarget(plan);
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
