using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Real handlers for the Chart Design and Chart Format contextual ribbon tabs (activation key
/// <c>chart.selected</c>). These resolve the currently selected <see cref="ChartModel"/> from the
/// drawing-object selection state (<see cref="MainWindow._selectedDrawingObjectKind"/> /
/// <see cref="MainWindow._selectedDrawingObjectId"/>) on the active sheet, then drive existing Core
/// commands through <see cref="WorkbookSession.ExecuteReviewCommand"/>:
/// <list type="bullet">
///   <item><see cref="ChangeChartTypeCommand"/> — Change Chart Type (combo-box picker dialog).</item>
///   <item><see cref="ChangeChartSourceCommand"/> — Select Data Source (range + categories dialog).</item>
///   <item><see cref="SetChartLayoutCommand"/> with <see cref="ChartLayoutOptions"/> — the chart-area /
///   plot-area / title / legend / data-label / axis-gridline / series formatting toggles. Core fully
///   supports these via <c>ApplyOptions</c>.</item>
/// </list>
/// Commands without Core support (combo overlays needing series pickers, Move Chart's sheet-target
/// dialog, and the type-specific Bar/Pie/Bubble/Stock format dialogs) report an honest "not yet
/// available" status rather than inventing behavior. Shared chart cycling and command-planning policy
/// lives in <see cref="FreeX.App.Presentation.Charts.Editing"/> so this renderer only resolves selection,
/// gathers user input, and applies Core commands.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Resolves the chart the contextual tabs target: the selected drawing object on the active sheet,
    /// when it is a visible, non-PivotChart <see cref="ChartModel"/> (the same predicate the WPF host's
    /// <c>IsChartContextualRibbonTarget</c> uses). Reports an honest status and returns null otherwise.
    /// </summary>
    private bool TryGetSelectedChart(string commandLabel, out ChartModel chart)
    {
        chart = null!;
        if (_selectedDrawingObjectKind != SelectionPaneObjectKind.Chart || _selectedDrawingObjectId is not { } id)
        {
            RefreshShell(UiText.Format("ChartLoc_SelectChartBeforeUsing", commandLabel));
            return false;
        }

        foreach (var candidate in _session.ActiveSheet.Charts)
        {
            if (candidate.Id == id && candidate.IsVisible && !candidate.IsPivotChart)
            {
                chart = candidate;
                return true;
            }
        }

        RefreshShell(UiText.Format("ChartLoc_SelectChartBeforeUsing", commandLabel));
        return false;
    }

    /// <summary>
    /// Applies a <see cref="ChartLayoutOptions"/> delta to the selected chart through the shared
    /// <see cref="SetChartLayoutCommand"/>, surfacing the Core guard message on failure and refreshing
    /// the shell (which repaints the chart overlay) on success.
    /// </summary>
    private void ApplyChartLayout(string commandLabel, ChartModel chart, ChartLayoutOptions options)
    {
        var result = _session.ExecuteReviewCommand(new SetChartLayoutCommand(_session.ActiveSheet.Id, chart.Id, options));
        RefreshShell(result.Success
            ? UiText.Format("ChartLoc_CommandApplied", commandLabel)
            : result.ErrorMessage ?? UiText.Format("ChartLoc_CommandFailed", commandLabel));
    }

    // ---- Chart Design: Change Chart Type (real, ChangeChartTypeCommand) -------------------------------

    private async Task ShowChangeChartTypeDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Change Chart Type", out var chart))
            return;

        var chosen = await ShowChartTypePickerAsync(chart.Type);
        if (chosen is not { } type)
            return;

        // Validate the requested change through the shared planner: it filters out no-ops (same type)
        // and deferred-authoring families, surfacing an honest message instead of a pointless command.
        var plan = ChartTypeChangePlanner.Plan(chart.Type, type);
        if (!plan.HasChange)
        {
            RefreshShell(plan.Message ?? UiText.Get("ChartLoc_ChangeChartTypeFailed"));
            return;
        }

        // Re-resolve after the dialog: the selection may have changed (or the chart been deleted)
        // while it was open, so act on what is selected now rather than the captured reference.
        if (!TryGetSelectedChart("Change Chart Type", out chart))
            return;

        var result = _session.ExecuteReviewCommand(new ChangeChartTypeCommand(_session.ActiveSheet.Id, chart.Id, plan.AppliedType!.Value));
        RefreshShell(result.Success
            ? UiText.Format("ChartLoc_ChangedChartTypeTo", ChartTypeChangePlanner.DisplayName(plan.AppliedType!.Value))
            : result.ErrorMessage ?? UiText.Get("ChartLoc_ChangeChartTypeFailed"));
    }

    /// <summary>
    /// Small combo-box chart-type picker. Lists the authorable, non-deferred chart families from the
    /// shared <see cref="ChartTypeChangePlanner.GetSupportedChoices"/> (which filters out families like
    /// Map that Core renders but cannot author/convert to) with their English labels. Returns the chosen
    /// <see cref="ChartType"/> or null on cancel.
    /// </summary>
    private async Task<ChartType?> ShowChartTypePickerAsync(ChartType currentType)
    {
        var choices = ChartTypeChangePlanner.GetSupportedChoices();

        var combo = new ComboBox
        {
            Width = 260,
            ItemsSource = choices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartTypeChoice.DisplayName)),
        };
        AutomationProperties.SetName(combo, "Chart type");
        AutomationProperties.SetAutomationId(combo, "ChangeChartTypeCombo");
        combo.SelectedItem =
            choices.FirstOrDefault(c => c.Type == currentType)
            ?? (choices.Count > 0 ? choices[0] : null);

        var dialog = new Window
        {
            Title = UiText.Get("ChartLoc_ChangeChartTypeTitle"),
            Width = 340,
            Height = 160,
            MinWidth = 340,
            MinHeight = 160,
            MaxWidth = 340,
            MaxHeight = 160,
            Background = Brushes.White,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ChangeChartTypeDialog");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), Width = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(okButton, "ChangeChartTypeOkButton");
        okButton.Click += (_, _) => dialog.Close(combo.SelectedItem is ChartTypeChoice picked ? (ChartType?)picked.Type : null);

        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, "ChangeChartTypeCancelButton");
        cancelButton.Click += (_, _) => dialog.Close((ChartType?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            MinWidth = 292,
            Children =
            {
                new TextBlock { Text = UiText.Get("ChartLoc_ChooseChartType") },
                combo,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                    Children = { okButton, cancelButton },
                },
            },
        };

        return await dialog.ShowDialog<ChartType?>(this);
    }

    // ---- Chart Design: Select Data Source (real, ChangeChartSourceCommand) ----------------------------

    private async Task ShowSelectChartDataDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Select Data", out var chart))
            return;

        var result = await ShowSelectDataDialogAsync(
            FormatRangeReference(chart.DataRange),
            chart.FirstColIsCategories);
        if (result is not { } choice)
            return;

        if (!TryParseDefinedNameRange(choice.RangeText, out var dataRange))
        {
            RefreshShell(UiText.Get("ChartLoc_EnterValidChartDataRange"));
            return;
        }

        // Re-resolve after the dialog in case the selection changed while it was open.
        if (!TryGetSelectedChart("Select Data", out chart))
            return;

        var commandResult = _session.ExecuteReviewCommand(new ChangeChartSourceCommand(
            _session.ActiveSheet.Id,
            chart.Id,
            dataRange,
            firstRowIsHeader: chart.FirstRowIsHeader,
            firstColIsCategories: choice.FirstColumnIsCategories));
        RefreshShell(commandResult.Success
            ? UiText.Format("ChartLoc_ChartDataSourceSetTo", FormatRangeReference(dataRange))
            : commandResult.ErrorMessage ?? UiText.Get("ChartLoc_SelectDataFailed"));
    }

    private async Task<(string RangeText, bool FirstColumnIsCategories)?> ShowSelectDataDialogAsync(
        string initialRange,
        bool firstColumnIsCategories)
    {
        var rangeBox = new TextBox
        {
            Text = initialRange,
            Width = 260,
            PlaceholderText = UiText.Get("ChartLoc_RangePlaceholder"),
        };
        AutomationProperties.SetName(rangeBox, "Chart data range");
        AutomationProperties.SetAutomationId(rangeBox, "SelectChartDataRangeBox");

        var categoriesCheck = new CheckBox
        {
            Content = UiText.Get("ChartLoc_FirstColumnContainsCategories"),
            IsChecked = firstColumnIsCategories,
        };
        AutomationProperties.SetAutomationId(categoriesCheck, "SelectChartDataCategoriesCheck");

        var dialog = new Window
        {
            Title = UiText.Get("ChartLoc_SelectDataSourceTitle"),
            Width = 360,
            Height = 190,
            MinWidth = 360,
            MinHeight = 190,
            MaxWidth = 360,
            MaxHeight = 190,
            Background = Brushes.White,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "SelectChartDataDialog");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), Width = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(okButton, "SelectChartDataOkButton");
        okButton.Click += (_, _) => dialog.Close(((string, bool)?)(rangeBox.Text ?? string.Empty, categoriesCheck.IsChecked == true));

        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, "SelectChartDataCancelButton");
        cancelButton.Click += (_, _) => dialog.Close(((string, bool)?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            MinWidth = 292,
            Children =
            {
                new TextBlock { Text = UiText.Get("ChartLoc_ChartDataRangeLabel") },
                rangeBox,
                categoriesCheck,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                    Children = { okButton, cancelButton },
                },
            },
        };

        return await dialog.ShowDialog<(string, bool)?>(this);
    }

    // ---- Chart Design: layout toggles (real, SetChartLayoutCommand) -----------------------------------

    private async Task ShowChartTitlesDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Chart Titles", out var chart))
            return;

        var current = ChartTitlesPlanner.Read(chart);
        var result = await ShowChartTitlesDialogAsync(current.ChartTitle, current.XAxisTitle, current.YAxisTitle);
        if (result is not { } titles)
            return;

        if (!TryGetSelectedChart("Chart Titles", out chart))
            return;

        // The shared planner trims/collapses each title and drops axis titles for axis-less chart types
        // (pie/doughnut), matching Core's EnforceAxisTitleSupport.
        var options = ChartTitlesPlanner.Plan(
            chart.Type,
            new ChartTitlesInput(titles.ChartTitle, titles.XAxisTitle, titles.YAxisTitle));
        ApplyChartLayout("Chart Titles", chart, options);
    }

    private async Task<(string ChartTitle, string XAxisTitle, string YAxisTitle)?> ShowChartTitlesDialogAsync(
        string chartTitle,
        string xAxisTitle,
        string yAxisTitle)
    {
        var chartTitleBox = new TextBox { Text = chartTitle, Width = 260 };
        AutomationProperties.SetAutomationId(chartTitleBox, "ChartTitleBox");
        var xAxisBox = new TextBox { Text = xAxisTitle, Width = 260 };
        AutomationProperties.SetAutomationId(xAxisBox, "ChartXAxisTitleBox");
        var yAxisBox = new TextBox { Text = yAxisTitle, Width = 260 };
        AutomationProperties.SetAutomationId(yAxisBox, "ChartYAxisTitleBox");

        var dialog = new Window
        {
            Title = UiText.Get("ChartLoc_ChartTitlesTitle"),
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ChartTitlesDialog");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), Width = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(okButton, "ChartTitlesOkButton");
        okButton.Click += (_, _) => dialog.Close(((string, string, string)?)(
            chartTitleBox.Text ?? string.Empty,
            xAxisBox.Text ?? string.Empty,
            yAxisBox.Text ?? string.Empty));

        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, "ChartTitlesCancelButton");
        cancelButton.Click += (_, _) => dialog.Close(((string, string, string)?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 292,
            Children =
            {
                new TextBlock { Text = UiText.Get("ChartLoc_ChartTitleLabel") },
                chartTitleBox,
                new TextBlock { Text = UiText.Get("ChartLoc_HorizontalAxisTitleLabel") },
                xAxisBox,
                new TextBlock { Text = UiText.Get("ChartLoc_VerticalAxisTitleLabel") },
                yAxisBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                    Children = { okButton, cancelButton },
                },
            },
        };

        return await dialog.ShowDialog<(string, string, string)?>(this);
    }

    // ---- Chart Design: Legend options (real, SetChartLayoutCommand via ChartLegendPlanner) ------------

    /// <summary>
    /// Opens the Legend options dialog (show/hide + top/bottom/left/right placement) for the selected
    /// chart, then applies the shared <see cref="ChartLegendPlanner"/> result through
    /// <see cref="SetChartLayoutCommand"/>. The planner keeps the chosen placement even when the legend is
    /// hidden so re-showing restores it.
    /// </summary>
    private async Task ShowChartLegendDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Legend", out var chart))
            return;

        var current = ChartLegendPlanner.Read(chart);
        var result = await ShowChartLegendDialogAsync(current.ShowLegend, current.Position);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart("Legend", out chart))
            return;

        ApplyChartLayout("Legend", chart, ChartLegendPlanner.Plan(edited));
    }

    private async Task<ChartLegendInput?> ShowChartLegendDialogAsync(bool showLegend, ChartLegendPosition position)
    {
        var showCheck = new CheckBox
        {
            Content = UiText.Get("ChartLoc_ShowLegend"),
            IsChecked = showLegend,
        };
        AutomationProperties.SetAutomationId(showCheck, "ChartLegendShowCheck");

        var positionChoices = ChartLegendPlanner.GetPositionChoices();
        var positionCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = positionChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartLegendPositionChoice.DisplayName)),
        };
        AutomationProperties.SetName(positionCombo, "Legend position");
        AutomationProperties.SetAutomationId(positionCombo, "ChartLegendPositionCombo");
        positionCombo.SelectedItem =
            positionChoices.FirstOrDefault(c => c.Position == position)
            ?? (positionChoices.Count > 0 ? positionChoices[0] : null);

        var dialog = new Window
        {
            Title = UiText.Get("ChartLoc_LegendTitle"),
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ChartLegendDialog");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), Width = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(okButton, "ChartLegendOkButton");
        okButton.Click += (_, _) =>
        {
            var chosenPosition = positionCombo.SelectedItem is ChartLegendPositionChoice picked
                ? picked.Position
                : ChartLegendPosition.Right;
            dialog.Close((ChartLegendInput?)new ChartLegendInput(showCheck.IsChecked == true, chosenPosition));
        };

        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, "ChartLegendCancelButton");
        cancelButton.Click += (_, _) => dialog.Close((ChartLegendInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            MinWidth = 292,
            Children =
            {
                showCheck,
                new TextBlock { Text = UiText.Get("ChartLoc_LegendPositionLabel") },
                positionCombo,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                    Children = { okButton, cancelButton },
                },
            },
        };

        return await dialog.ShowDialog<ChartLegendInput?>(this);
    }

    private void CycleChartDataLabelPosition()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Data Label Position", out var chart))
            return;

        ApplyChartLayout("Data Label Position", chart, new ChartLayoutOptions(
            ShowDataLabels: true,
            DataLabelPosition: ChartQuickFormatCycler.NextDataLabelPosition(chart.DataLabelPosition)));
    }

    private void CycleChartStyle()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Chart Styles", out var chart))
            return;

        // Chart styles are 1..48 (SetChartStyleCommand clamps). Step in fours like Excel's gallery rows;
        // wrap back to 1 after 48.
        var current = chart.ChartStyleId ?? 0;
        var next = current >= 45 ? 1 : current + 4;
        var result = _session.ExecuteReviewCommand(new SetChartStyleCommand(_session.ActiveSheet.Id, chart.Id, next));
        RefreshShell(result.Success
            ? UiText.Format("ChartLoc_AppliedChartStyle", next)
            : result.ErrorMessage ?? UiText.Get("ChartLoc_ChartStylesFailed"));
    }

    private void CycleChartSecondaryAxis()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Secondary Axis", out var chart))
            return;

        if (!ChartTypeSupport.SupportsSecondaryAxis(chart.Type) || ChartTypeSupport.GetDataSeriesCount(chart) < 2)
        {
            RefreshShell(UiText.Get("ChartLoc_SecondaryAxisNeeds"));
            return;
        }

        // Toggle the second series (index 1) on/off the secondary axis.
        var enable = !chart.ShowSecondaryAxis;
        ApplyChartLayout("Secondary Axis", chart, new ChartLayoutOptions(
            ShowSecondaryAxis: enable,
            SecondaryAxisSeriesIndexes: enable ? new[] { 1 } : Array.Empty<int>()));
    }

    // ---- Chart Format: shape fill / outline + formatting toggles (real, SetChartLayoutCommand) --------

    private async Task ShowChartShapeFillDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Chart Shape Fill", out var chart))
            return;

        var color = await ShowMoreColorsDialogAsync(
            UiText.Get("ChartLoc_ChartAreaFill"),
            chart.ChartAreaFillColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
        if (color is { } chosen && TryGetSelectedChart("Chart Area Fill", out chart))
            ApplyChartLayout("Chart Area Fill", chart, new ChartLayoutOptions(ChartAreaFillColor: chosen));
    }

    private async Task ShowChartShapeOutlineDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Chart Shape Outline", out var chart))
            return;

        var color = await ShowMoreColorsDialogAsync(
            UiText.Get("ChartLoc_PlotAreaBorder"),
            chart.PlotAreaBorderColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
        if (color is { } chosen && TryGetSelectedChart("Plot Area Border", out chart))
        {
            var thickness = chart.PlotAreaBorderThickness >= 3 ? 0.75 : chart.PlotAreaBorderThickness + 0.75;
            ApplyChartLayout("Plot Area Border", chart, new ChartLayoutOptions(
                PlotAreaBorderColor: chosen,
                PlotAreaBorderThickness: thickness));
        }
    }

    private async Task ShowChartPlotAreaFillDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Plot Area Fill", out var chart))
            return;

        var color = await ShowMoreColorsDialogAsync(
            UiText.Get("ChartLoc_PlotAreaFill"),
            chart.PlotAreaFillColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
        if (color is { } chosen && TryGetSelectedChart("Plot Area Fill", out chart))
            ApplyChartLayout("Plot Area Fill", chart, new ChartLayoutOptions(PlotAreaFillColor: chosen));
    }

    private async Task ShowChartSeriesColorDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Series Color", out var chart))
            return;

        if (ChartTypeSupport.GetDataSeriesCount(chart) <= 0)
        {
            RefreshShell(UiText.Get("ChartLoc_NoDataSeriesToColor"));
            return;
        }

        var existing = ChartQuickFormatCycler.ReadFirstSeriesFormat(chart).FillColor;
        var color = await ShowMoreColorsDialogAsync(
            UiText.Get("ChartLoc_SeriesColor"),
            existing ?? ChartQuickFormatCycler.DefaultSeriesColor);
        if (color is not { } chosen)
            return;

        // Re-resolve after the dialog in case the selection changed while it was open.
        if (!TryGetSelectedChart("Series Color", out chart))
            return;

        var formats = new List<ChartSeriesFormat>(chart.SeriesFormats);
        var index = formats.FindIndex(f => f.SeriesIndex == 0);
        var current = index >= 0 ? formats[index] : new ChartSeriesFormat(0);
        var updated = current with { FillColor = chosen, FillThemeColor = null };
        if (index >= 0)
            formats[index] = updated;
        else
            formats.Add(updated);

        ApplyChartLayout("Series Color", chart, new ChartLayoutOptions(SeriesFormats: formats));
    }

    private void CycleChartXAxisGridlines()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("X Axis Gridlines", out var chart))
            return;

        if (!ChartTypeSupport.SupportsAxes(chart.Type))
        {
            RefreshShell(UiText.Get("ChartLoc_NoAxesForGridlines"));
            return;
        }

        var (showMajor, showMinor) = ChartQuickFormatCycler.NextGridlineState(
            chart.ShowXAxisMajorGridlines,
            chart.ShowXAxisMinorGridlines);
        ApplyChartLayout("X Axis Gridlines", chart, new ChartLayoutOptions(
            ShowXAxisMajorGridlines: showMajor,
            ShowXAxisMinorGridlines: showMinor));
    }

    private void CycleChartYAxisGridlines()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Y Axis Gridlines", out var chart))
            return;

        if (!ChartTypeSupport.SupportsAxes(chart.Type))
        {
            RefreshShell(UiText.Get("ChartLoc_NoAxesForGridlines"));
            return;
        }

        var (showMajor, showMinor) = ChartQuickFormatCycler.NextGridlineState(
            chart.ShowYAxisMajorGridlines,
            chart.ShowYAxisMinorGridlines);
        ApplyChartLayout("Y Axis Gridlines", chart, new ChartLayoutOptions(
            ShowYAxisMajorGridlines: showMajor,
            ShowYAxisMinorGridlines: showMinor));
    }

    private void ToggleChartXAxisLabels()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("X Axis Labels", out var chart))
            return;

        if (!ChartTypeSupport.SupportsAxes(chart.Type))
        {
            RefreshShell(UiText.Get("ChartLoc_NoAxes"));
            return;
        }

        ApplyChartLayout("X Axis Labels", chart, new ChartLayoutOptions(ShowXAxisLabels: !chart.ShowXAxisLabels));
    }

    private void ToggleChartYAxisLabels()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Y Axis Labels", out var chart))
            return;

        if (!ChartTypeSupport.SupportsAxes(chart.Type))
        {
            RefreshShell(UiText.Get("ChartLoc_NoAxes"));
            return;
        }

        ApplyChartLayout("Y Axis Labels", chart, new ChartLayoutOptions(ShowYAxisLabels: !chart.ShowYAxisLabels));
    }

    /// <summary>Reports that a Chart-tab command has no Core support yet (no silent no-op, no invented behavior).</summary>
    private void ReportChartCommandNotYetAvailable(string commandLabel)
        => RefreshShell(UiText.Format("ChartLoc_CommandNotYetAvailable", commandLabel));
}
