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
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

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
    /// when it is eligible for shared chart workflows. Reports an honest status and returns null otherwise.
    /// </summary>
    private bool TryGetSelectedChart(string commandLabel, out ChartModel chart)
    {
        chart = null!;
        if (_selectedDrawingObjectKind != SelectionPaneObjectKind.Chart)
        {
            RefreshShell(UiText.Format("ChartLoc_SelectChartBeforeUsing", commandLabel));
            return false;
        }

        if (ChartWorkflowTargetPlanner.FindSelectedChart(_session.ActiveSheet, _selectedDrawingObjectId) is { } selectedChart)
        {
            chart = selectedChart;
            return true;
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
        ApplyChartComboBoxChrome(combo);
        combo.SelectedItem =
            choices.FirstOrDefault(c => c.Type == currentType)
            ?? (choices.Count > 0 ? choices[0] : null);

        var dialog = new Window
        {
            Title = UiText.Get("ChartLoc_ChangeChartTypeTitle"),
            Width = 380,
            SizeToContent = SizeToContent.Height,
            MinWidth = 340,
            MinHeight = 200,
            Background = Brushes.White,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ChangeChartTypeDialog");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), Width = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(okButton, "ChangeChartTypeOkButton");
        ApplyChartButtonChrome(okButton, 80, isDefault: true);
        okButton.Click += (_, _) => dialog.Close(combo.SelectedItem is ChartTypeChoice picked ? (ChartType?)picked.Type : null);

        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, "ChangeChartTypeCancelButton");
        ApplyChartButtonChrome(cancelButton, 80);
        cancelButton.Click += (_, _) => dialog.Close((ChartType?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 292,
            Children =
            {
                // WPF "All Charts" section header + help text
                new TextBlock
                {
                    Text = UiText.Get("ChartTypePicker_AllChartsHeading"),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = UiText.Get("ChartTypePicker_AllChartsHelpText"),
                    FontSize = 11,
                    FontFamily = FormulaBarFontFamily,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush(96, 96, 96),
                    Margin = new Thickness(0, 0, 0, 4),
                },
                new TextBlock { Text = UiText.Get("ChartLoc_ChooseChartType"), FontSize = 12, FontFamily = FormulaBarFontFamily },
                combo,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                    Margin = new Thickness(0, 4, 0, 0),
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

        var result = await ShowSelectDataSourceDialogAsync(
            FormatRangeReference(chart.DataRange),
            chart.FirstColIsCategories);
        if (result is not { } choice)
            return;

        if (!TryParseDefinedNameRange(choice.SourceRangeText, out var dataRange))
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

    /// <summary>
    /// Full "Select Data Source" dialog with Legend Entries (Series) list, Horizontal (Category)
    /// Axis Labels list, Add/Edit/Remove series buttons, Switch Row/Column checkbox, and a range
    /// text-box.  Matches the WPF <c>SelectDataSourceDialog</c> layout and feature set.
    /// </summary>
    private async Task<SelectDataSourceResult?> ShowSelectDataSourceDialogAsync(
        string initialRange,
        bool firstColumnIsCategories)
    {
        // ---- Range text box -------------------------------------------------------------------
        var rangeBox = new TextBox
        {
            Text = initialRange,
            Width = 380,
            PlaceholderText = UiText.Get("ChartLoc_RangePlaceholder"),
        };
        AutomationProperties.SetName(rangeBox, "Chart data range");
        AutomationProperties.SetAutomationId(rangeBox, "SelectChartDataRangeBox");
        ApplyChartTextBoxChrome(rangeBox);

        // ---- Switch Row/Column checkbox -------------------------------------------------------
        var switchRowColumnCheck = new CheckBox
        {
            Content = UiText.Get("ChartLoc_SwitchRowColumn"),
            IsChecked = false,
            Margin = new Thickness(0, 4, 0, 0),
        };
        AutomationProperties.SetAutomationId(switchRowColumnCheck, "SelectChartDataSwitchRowColumnCheck");

        // ---- Series ListBox + buttons ---------------------------------------------------------
        var seriesList = new ListBox
        {
            Height = 80,
            Width = 320,
            SelectionMode = SelectionMode.Single,
        };
        AutomationProperties.SetName(seriesList, "Series list");
        AutomationProperties.SetAutomationId(seriesList, "SelectChartDataSeriesList");

        var addSeriesButton = new Button { Content = UiText.Get("ChartLoc_AddSeriesButton"), Width = 100 };
        AutomationProperties.SetAutomationId(addSeriesButton, "SelectChartDataAddSeriesButton");
        ApplyChartButtonChrome(addSeriesButton, 100);

        var editSeriesButton = new Button { Content = UiText.Get("ChartLoc_EditSeriesButton"), Width = 100, IsEnabled = false };
        AutomationProperties.SetAutomationId(editSeriesButton, "SelectChartDataEditSeriesButton");
        ApplyChartButtonChrome(editSeriesButton, 100);

        var removeSeriesButton = new Button { Content = UiText.Get("ChartLoc_RemoveSeriesButton"), Width = 100, IsEnabled = false };
        AutomationProperties.SetAutomationId(removeSeriesButton, "SelectChartDataRemoveSeriesButton");
        ApplyChartButtonChrome(removeSeriesButton, 100);

        // ---- Axis Labels ListBox + button -----------------------------------------------------
        var axisLabelsList = new ListBox
        {
            Height = 80,
            Width = 320,
            SelectionMode = SelectionMode.Single,
        };
        AutomationProperties.SetName(axisLabelsList, "Axis label list");
        AutomationProperties.SetAutomationId(axisLabelsList, "SelectChartDataAxisLabelsList");

        var editAxisLabelsButton = new Button { Content = UiText.Get("ChartLoc_EditAxisLabelsButton"), Width = 100, IsEnabled = false };
        AutomationProperties.SetAutomationId(editAxisLabelsButton, "SelectChartDataEditAxisLabelsButton");
        ApplyChartButtonChrome(editAxisLabelsButton, 100);

        // ---- First column contains category labels checkbox -----------------------------------
        var categoriesCheck = new CheckBox
        {
            Content = UiText.Get("ChartLoc_FirstColumnContainsCategories"),
            IsChecked = firstColumnIsCategories,
            Margin = new Thickness(0, 4, 0, 0),
        };
        AutomationProperties.SetAutomationId(categoriesCheck, "SelectChartDataCategoriesCheck");

        // ---- State management helpers --------------------------------------------------------
        // The series ListBox items: stored as a mutable list so Add/Remove work independently of
        // the planner inference.  Refreshed from the planner when the range text or categories
        // checkbox changes; after user-driven Add/Remove the list is in "manual" mode until the
        // range box is edited again.
        var seriesItems = new List<string>();
        var inManualSeriesMode = false;

        void RefreshButtonState()
        {
            editSeriesButton.IsEnabled = seriesList.SelectedIndex >= 0;
            removeSeriesButton.IsEnabled = seriesList.SelectedIndex >= 0;
            editAxisLabelsButton.IsEnabled = axisLabelsList.SelectedIndex >= 0;
        }

        void RefreshLists()
        {
            if (inManualSeriesMode)
                return;

            var preview = SelectDataSourcePlanner.InferPreviewEntries(
                rangeBox.Text ?? string.Empty,
                categoriesCheck.IsChecked == true);

            seriesItems.Clear();
            foreach (var s in preview.Series)
                seriesItems.Add(SelectDataSourcePlanner.FormatSeriesListItem(s.Name, s.ValuesRangeText));

            seriesList.ItemsSource = null;
            seriesList.ItemsSource = seriesItems;
            seriesList.SelectedIndex = seriesItems.Count > 0 ? 0 : -1;

            var axisItems = preview.Categories.Select(c => c.Label).ToList();
            axisLabelsList.ItemsSource = null;
            axisLabelsList.ItemsSource = axisItems;
            axisLabelsList.SelectedIndex = axisItems.Count > 0 ? 0 : -1;

            RefreshButtonState();
        }

        // ---- Event handlers ------------------------------------------------------------------
        rangeBox.TextChanged += (_, _) =>
        {
            inManualSeriesMode = false;
            RefreshLists();
        };
        categoriesCheck.IsCheckedChanged += (_, _) =>
        {
            inManualSeriesMode = false;
            RefreshLists();
        };
        seriesList.SelectionChanged += (_, _) => RefreshButtonState();
        axisLabelsList.SelectionChanged += (_, _) => RefreshButtonState();

        addSeriesButton.Click += (_, _) =>
        {
            inManualSeriesMode = true;
            var newItem = SelectDataSourcePlanner.FormatNewSeriesItem(seriesItems.Count + 1);
            seriesItems.Add(newItem);
            seriesList.ItemsSource = null;
            seriesList.ItemsSource = seriesItems;
            seriesList.SelectedIndex = seriesItems.Count - 1;
            RefreshButtonState();
        };

        editSeriesButton.Click += (_, _) =>
        {
            if (seriesList.Items.Count > 0 && seriesList.SelectedIndex < 0)
                seriesList.SelectedIndex = 0;
        };

        removeSeriesButton.Click += (_, _) =>
        {
            var idx = seriesList.SelectedIndex;
            if (idx < 0)
                return;
            inManualSeriesMode = true;
            seriesItems.RemoveAt(idx);
            seriesList.ItemsSource = null;
            seriesList.ItemsSource = seriesItems;
            seriesList.SelectedIndex = seriesItems.Count == 0 ? -1 : Math.Min(idx, seriesItems.Count - 1);
            RefreshButtonState();
        };

        editAxisLabelsButton.Click += (_, _) =>
        {
            if (axisLabelsList.Items.Count > 0)
                axisLabelsList.SelectedIndex = 0;
        };

        // ---- Initial population of the preview lists -----------------------------------------
        RefreshLists();

        // ---- Dialog layout -------------------------------------------------------------------
        var dialog = new Window
        {
            Title = UiText.Get("ChartLoc_SelectDataSourceTitle"),
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 460,
            Background = Brushes.White,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "SelectChartDataDialog");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), Width = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(okButton, "SelectChartDataOkButton");
        ApplyChartButtonChrome(okButton, 80, isDefault: true);
        okButton.Click += (_, _) =>
        {
            var rangeText = rangeBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rangeText))
            {
                // Keep dialog open; let the shell report the invalid range after close.
                dialog.Close((SelectDataSourceResult?)null);
                return;
            }

            dialog.Close((SelectDataSourceResult?)SelectDataSourcePlanner.CreateResult(
                rangeText,
                categoriesCheck.IsChecked == true,
                switchRowColumnCheck.IsChecked == true));
        };

        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, "SelectChartDataCancelButton");
        ApplyChartButtonChrome(cancelButton, 80);
        cancelButton.Click += (_, _) => dialog.Close((SelectDataSourceResult?)null);

        // Helper to build a panel with a list on the left and buttons stacked on the right.
        Grid MakeListPanel(string title, string helpText, ListBox list, IEnumerable<Button> buttons)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Spacing = 2 };
            header.Children.Add(new TextBlock { Text = title, FontSize = 12, FontFamily = FormulaBarFontFamily, FontWeight = FontWeight.SemiBold });
            header.Children.Add(new TextBlock { Text = helpText, FontSize = 11, FontFamily = FormulaBarFontFamily, Foreground = Brush(96, 96, 96), TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
            grid.Children.Add(header);

            Grid.SetRow(list, 1);
            grid.Children.Add(list);

            var buttonStack = new StackPanel { Margin = new Thickness(8, 20, 0, 0), Spacing = 4 };
            foreach (var b in buttons)
                buttonStack.Children.Add(b);
            Grid.SetColumn(buttonStack, 1);
            Grid.SetRowSpan(buttonStack, 2);
            grid.Children.Add(buttonStack);

            return grid;
        }

        // ---- Hidden and Empty Cells info button -----------------------------------------------
        var hiddenEmptyButton = new Button
        {
            Content = UiText.Get("ChartLoc_HiddenEmptyCellsButton"),
            Width = 180,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
        };
        AutomationProperties.SetAutomationId(hiddenEmptyButton, "SelectChartDataHiddenEmptyButton");
        ApplyChartButtonChrome(hiddenEmptyButton, 180);
        hiddenEmptyButton.Click += async (_, _) =>
        {
            var infoDialog = new Window
            {
                Title = UiText.Get("ChartLoc_HiddenEmptyCellsTitle"),
                SizeToContent = SizeToContent.WidthAndHeight,
                Background = Brushes.White,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            var closeBtn = new Button { Content = UiText.Get("Common_Ok"), Width = 80, IsDefault = true };
            ApplyChartButtonChrome(closeBtn, 80, isDefault: true);
            closeBtn.Click += (_, _) => infoDialog.Close();
            infoDialog.Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                MinWidth = 300,
                Children =
                {
                    new TextBlock
                    {
                        Text = UiText.Get("ChartLoc_HiddenEmptyCellsMessage"),
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 340,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                        Children = { closeBtn },
                    },
                },
            };
            await infoDialog.ShowDialog(dialog);
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 420,
            Children =
            {
                // Range label + text box
                new TextBlock { Text = UiText.Get("ChartLoc_ChartDataRangeLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily },
                rangeBox,
                switchRowColumnCheck,
                // Legend Entries (Series) panel
                MakeListPanel(
                    UiText.Get("ChartLoc_SeriesPanelTitle"),
                    UiText.Get("ChartLoc_SeriesListHelpText"),
                    seriesList,
                    new[] { addSeriesButton, editSeriesButton, removeSeriesButton }),
                // Horizontal (Category) Axis Labels panel
                MakeListPanel(
                    UiText.Get("ChartLoc_AxisLabelsPanelTitle"),
                    UiText.Get("ChartLoc_AxisLabelsListHelpText"),
                    axisLabelsList,
                    new[] { editAxisLabelsButton }),
                // First column is categories checkbox
                categoriesCheck,
                hiddenEmptyButton,
                // OK / Cancel
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                    Margin = new Thickness(0, 4, 0, 0),
                    Children = { okButton, cancelButton },
                },
            },
        };

        return await dialog.ShowDialog<SelectDataSourceResult?>(this);
    }

    /// <summary>
    /// Parity-capture entry point for the Select Data Source dialog.  Replaced by the full series
    /// management dialog so the captured surface now shows the complete WPF-parity UI.
    /// </summary>
    private Task<SelectDataSourceResult?> ShowSelectDataDialogAsync(
        string initialRange,
        bool firstColumnIsCategories) =>
        ShowSelectDataSourceDialogAsync(initialRange, firstColumnIsCategories);

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
        ApplyChartTextBoxChrome(chartTitleBox);
        var xAxisBox = new TextBox { Text = xAxisTitle, Width = 260 };
        AutomationProperties.SetAutomationId(xAxisBox, "ChartXAxisTitleBox");
        ApplyChartTextBoxChrome(xAxisBox);
        var yAxisBox = new TextBox { Text = yAxisTitle, Width = 260 };
        AutomationProperties.SetAutomationId(yAxisBox, "ChartYAxisTitleBox");
        ApplyChartTextBoxChrome(yAxisBox);

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
        ApplyChartButtonChrome(okButton, 80, isDefault: true);
        okButton.Click += (_, _) => dialog.Close(((string, string, string)?)(
            chartTitleBox.Text ?? string.Empty,
            xAxisBox.Text ?? string.Empty,
            yAxisBox.Text ?? string.Empty));

        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, "ChartTitlesCancelButton");
        ApplyChartButtonChrome(cancelButton, 80);
        cancelButton.Click += (_, _) => dialog.Close(((string, string, string)?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 292,
            Children =
            {
                new TextBlock { Text = UiText.Get("ChartLoc_ChartTitleLabel"), FontSize = 12 },
                chartTitleBox,
                new TextBlock { Text = UiText.Get("ChartLoc_HorizontalAxisTitleLabel"), FontSize = 12 },
                xAxisBox,
                new TextBlock { Text = UiText.Get("ChartLoc_VerticalAxisTitleLabel"), FontSize = 12 },
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
        ApplyChartComboBoxChrome(positionCombo);
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
        ApplyChartButtonChrome(okButton, 80, isDefault: true);
        okButton.Click += (_, _) =>
        {
            var chosenPosition = positionCombo.SelectedItem is ChartLegendPositionChoice picked
                ? picked.Position
                : ChartLegendPosition.Right;
            dialog.Close((ChartLegendInput?)new ChartLegendInput(showCheck.IsChecked == true, chosenPosition));
        };

        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, "ChartLegendCancelButton");
        ApplyChartButtonChrome(cancelButton, 80);
        cancelButton.Click += (_, _) => dialog.Close((ChartLegendInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            MinWidth = 292,
            Children =
            {
                showCheck,
                new TextBlock { Text = UiText.Get("ChartLoc_LegendPositionLabel"), FontSize = 12 },
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
        var next = ChartQuickFormatCycler.NextChartStyleId(chart.ChartStyleId);
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
            ApplyChartLayout("Plot Area Border", chart, new ChartLayoutOptions(
                PlotAreaBorderColor: chosen,
                PlotAreaBorderThickness: ChartQuickFormatCycler.NextPlotAreaBorderThickness(chart.PlotAreaBorderThickness)));
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

        ApplyChartLayout("Series Color", chart, new ChartLayoutOptions(
            SeriesFormats: ChartQuickFormatCycler.MergeFirstSeriesFillColor(chart, chosen)));
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
