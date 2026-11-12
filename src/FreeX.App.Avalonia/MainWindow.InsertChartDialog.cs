using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// The Insert Chart dialog behind Insert ▸ Recommended Charts, matching the WPF
/// <c>InsertChartDialog</c>: a tab control with a Recommended Charts tab (a gallery seeded by the
/// shared <see cref="RecommendedChartPlanner"/> from the selection) and an All Charts tab (the same
/// category list / subtype gallery body the Change Chart Type dialog uses), a recommended-layout check
/// box, and the OK / Cancel row. The chosen type is inserted through the same
/// <see cref="InsertChartFromSelection"/> path the chart-type ribbon buttons use.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The Insert Chart dialog choice: the chart type and the recommended-layout check box.</summary>
    private readonly record struct InsertChartDialogChoice(ChartType ChartType, bool UseRecommendedLayout);

    // WPF InsertChartDialog: a 660x430 outer window whose tab control is 310 high with 12px below it.
    private const double InsertChartDialogWidth = 660;
    private const double InsertChartDialogHeight = 430;
    private const double ChartPickerTabsHeight = 310;

    private async Task ShowInsertChartDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var recommendations = RecommendedChartPlanner.Recommend(_session.ActiveSheet, _session.SelectedRange);
        if (await ShowInsertChartDialogAsync(recommendations) is not { } choice)
            return;

        InsertChartFromSelection(choice.ChartType);
    }

    private async Task<InsertChartDialogChoice?> ShowInsertChartDialogAsync(
        IReadOnlyList<ChartType>? recommendedTypes = null)
    {
        var recommendedPanel = ChartTypePickerPlanner.GetRecommendedPanel();
        var recommendedChoices = recommendedTypes is null
            ? ChartTypePickerPlanner.GetRecommendedGalleryChoices()
            : ChartTypePickerPlanner.GetRecommendedGalleryChoices(recommendedTypes);

        // ---- Recommended Charts tab ----------------------------------------------------------------
        var recommendedGallery = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            ItemsSource = recommendedChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(string.Empty)
            {
                Converter = new FuncValueConverter<ChartTypeGalleryChoicePlan, string>(
                    choice => choice is null ? string.Empty : UiText.Get(choice.SubtypeNameKey)),
            },
            // WPF: 34px under the heading row, with the picker column gap on the right.
            Margin = new Thickness(0, 34, ChartTypeChangePlanner.PickerColumnGap, 0),
        };
        recommendedGallery.SelectedIndex = recommendedChoices.Count > 0 ? 0 : -1;
        AutomationProperties.SetName(
            recommendedGallery,
            UiText.Get(recommendedPanel.SubtypeGalleryAutomationNameResourceKey));
        AutomationProperties.SetAutomationId(recommendedGallery, "InsertChartRecommendedGallery");

        var recommendedHeading = new StackPanel
        {
            // Same as the shared All Charts body: the heading keeps the picker column gap on its right
            // so the help line ends before the preview border instead of running into it.
            Margin = new Thickness(0, 0, ChartTypeChangePlanner.PickerColumnGap, 0),
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get(recommendedPanel.HeadingResourceKey),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 2),
                },
                CreateChartDialogInlineHelp(UiText.Get(recommendedPanel.HelpResourceKey)),
            },
        };

        var recommendedPreview = BuildChartTypePreviewPanel(recommendedPanel.Preview);
        recommendedPreview.Margin = new Thickness(0, ChartTypeChangePlanner.PickerColumnGap * 2, 0, 0);

        // WPF CreatePickerGrid: an auto column for the gallery, a star column for the preview and the
        // fixed preview-width column the All Charts tab uses, over an auto heading row and a star row.
        var recommendedGrid = new Grid
        {
            Margin = new Thickness(ChartTypeChangePlanner.PickerColumnGap),
            MinHeight = 250,
        };
        recommendedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        recommendedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        recommendedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ChartTypeChangePlanner.PickerPreviewWidth) });
        recommendedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        recommendedGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        recommendedGrid.Children.Add(recommendedHeading);
        Grid.SetRow(recommendedGallery, 1);
        recommendedGrid.Children.Add(recommendedGallery);
        Grid.SetColumn(recommendedPreview, 1);
        Grid.SetRowSpan(recommendedPreview, 2);
        recommendedGrid.Children.Add(recommendedPreview);

        // ---- All Charts tab ------------------------------------------------------------------------
        var allCharts = BuildAllChartsPickerBody(currentType: null, "InsertChart");
        var subtypeGallery = allCharts.SubtypeGallery;
        // Inside the tab the body follows the WPF picker grid: a 250px minimum whose lists fill the
        // star row, instead of the fixed panel height the standalone Change Chart Type dialog uses.
        allCharts.Body.Height = double.NaN;
        allCharts.Body.MinHeight = 250;
        allCharts.CategoryList.Height = double.NaN;
        subtypeGallery.Height = double.NaN;

        var recommendedTab = new TabItem
        {
            Header = StripDisplayMnemonic(UiText.Get("InsertChart_RecommendedChartsTab")),
            Content = recommendedGrid,
        };
        AutomationProperties.SetAutomationId(recommendedTab, "InsertChartRecommendedChartsTab");
        var allChartsTab = new TabItem
        {
            Header = StripDisplayMnemonic(UiText.Get("InsertChart_AllChartsTab")),
            Content = allCharts.Body,
        };
        AutomationProperties.SetAutomationId(allChartsTab, "InsertChartAllChartsTab");
        var tabs = new TabControl
        {
            Height = ChartPickerTabsHeight,
            Margin = new Thickness(0, 0, 0, 12),
            Items = { recommendedTab, allChartsTab },
        };
        AutomationProperties.SetAutomationId(tabs, "InsertChartTabs");
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(tabs, ChartDialogChromeStyle);

        var recommendedLayoutCheck = CreateChartCheckBox(
            StripDisplayMnemonic(UiText.Get("InsertChart_UseRecommendedLayout")),
            isChecked: true);
        // WPF: a 16px check box with 10px below; the Avalonia check row is 20px.
        recommendedLayoutCheck.MinHeight = 20;
        recommendedLayoutCheck.MaxHeight = 20;
        recommendedLayoutCheck.Margin = new Thickness(0, 0, 0, 6);
        AutomationProperties.SetAutomationId(recommendedLayoutCheck, "InsertChartUseRecommendedLayoutCheck");

        var dialog = NewChartDialog(UiText.Get("InsertChart_Title"), "InsertChartDialog");
        // WPF InsertChartDialog is a 660x430 outer NoResize window. The width loses the measured
        // Windows frame; its 310px tab control, check row and button row need more than the 391px
        // Windows client height, so the height follows the content with that client height as the
        // minimum rather than cutting the button row.
        dialog.SizeToContent = SizeToContent.Height;
        dialog.Width = InsertChartDialogWidth - WpfNoResizeFrameWidth;
        dialog.MinWidth = InsertChartDialogWidth - WpfNoResizeFrameWidth;
        dialog.MinHeight = InsertChartDialogHeight - WpfNoResizeClientFrameHeight;

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons(
            "InsertChart",
            ChartTypeChangePlanner.PickerButtonWidth);

        // The gallery on the visible tab is the choice: the recommended gallery while its tab is
        // selected, otherwise the All Charts subtype gallery. Column is the fallback, as in WPF.
        void Accept()
        {
            var chartType = ReferenceEquals(tabs.SelectedItem, allChartsTab)
                ? (subtypeGallery.SelectedItem as ChartTypePickerOptionPlan)?.Type ?? ChartType.Column
                : (recommendedGallery.SelectedItem as ChartTypeGalleryChoicePlan)?.Type ?? ChartType.Column;
            dialog.Close((InsertChartDialogChoice?)new InsertChartDialogChoice(
                chartType,
                recommendedLayoutCheck.IsChecked == true));
        }

        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close((InsertChartDialogChoice?)null);
        // WPF accepts the dialog on a double-click in either gallery.
        recommendedGallery.DoubleTapped += (_, args) =>
        {
            args.Handled = true;
            Accept();
        };
        subtypeGallery.DoubleTapped += (_, args) =>
        {
            args.Handled = true;
            Accept();
        };

        // WPF docks the tab control and the check box to the top and the button row to the bottom of a
        // 16px-margin dock panel.
        Grid.SetRow(tabs, 0);
        Grid.SetRow(recommendedLayoutCheck, 1);
        Grid.SetRow(buttonRow, 3);
        dialog.Content = new Grid
        {
            Margin = new Thickness(ChartDialogMargin),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Children = { tabs, recommendedLayoutCheck, buttonRow },
        };
        ApplyChartDialogTypography(dialog);
        AvaloniaCompactDialogChrome.ApplyWindow(dialog, ChartDialogChromeStyle);
        // WPF focuses the recommended gallery when the dialog loads.
        ConfigureChartDialogKeyboardLifecycle(dialog, recommendedGallery);

        return await dialog.ShowDialog<InsertChartDialogChoice?>(this);
    }
}
