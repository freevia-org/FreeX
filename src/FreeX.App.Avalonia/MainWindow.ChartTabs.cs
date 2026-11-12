using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.Charts;
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
/// Shared chart cycling, support gates, validation, and command-planning policy live in
/// <see cref="FreeX.App.Presentation.Charts.Editing"/>. This renderer resolves selection, gathers native
/// input, and applies the resulting Core commands, including Move Chart and the type-specific
/// Bar/Pie/Bubble/Stock formatting workflows.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Resolves the chart the contextual tabs target: the selected drawing object on the active sheet,
    /// when it is eligible for shared chart workflows. Reports an honest status and returns null otherwise.
    /// </summary>
    private bool TryGetSelectedChart(ChartWorkflowCommandDescriptor command, out ChartModel chart) =>
        TryGetSelectedChart(ChartWorkflowCaption(command), out chart);

    private bool TryGetSelectedChart(string commandLabel, out ChartModel chart)
    {
        chart = null!;
        if (_selectedDrawingObjectKind != SelectionPaneObjectKind.Chart)
        {
            RefreshShell(UiText.Format(ChartWorkflowCommandCatalog.SelectChartBeforeUsingStatusResourceKey, commandLabel));
            return false;
        }

        if (ChartWorkflowTargetPlanner.FindSelectedChart(_session.ActiveSheet, _selectedDrawingObjectId) is { } selectedChart)
        {
            chart = selectedChart;
            return true;
        }

        RefreshShell(UiText.Format(ChartWorkflowCommandCatalog.SelectChartBeforeUsingStatusResourceKey, commandLabel));
        return false;
    }

    /// <summary>
    /// Applies a <see cref="ChartLayoutOptions"/> delta to the selected chart through the shared
    /// <see cref="SetChartLayoutCommand"/>, surfacing the Core guard message on failure and refreshing
    /// the shell (which repaints the chart overlay) on success.
    /// </summary>
    private void ApplyChartLayout(ChartWorkflowCommandDescriptor command, ChartModel chart, ChartLayoutOptions options) =>
        ApplyChartLayout(ChartWorkflowCaption(command), chart, options);

    private void ApplyChartLayout(string commandLabel, ChartModel chart, ChartLayoutOptions options)
    {
        var result = _session.ExecuteReviewCommand(
            ChartCommandWorkflowPlanner.BuildLayoutCommand(_session.ActiveSheet.Id, chart, options));
        RefreshShell(ChartWorkflowCommandCatalog
            .DescribeCommandResult(result.Success, commandLabel, result.ErrorMessage)
            .Resolve(UiText.Get, UiText.Format));
    }

    private static string ChartWorkflowCaption(ChartWorkflowCommandDescriptor command) =>
        command.TitleResourceKey is { } resourceKey ? UiText.Get(resourceKey) : command.Label;

    private static string ChartWorkflowCaption(ChartAxisWorkflowCommandDescriptor command) =>
        UiText.Get(command.TitleResourceKey);

    private static string ChartWorkflowUnsupportedStatus(ChartWorkflowCommandDescriptor command) =>
        command.UnsupportedStatusResourceKey is { } resourceKey
            ? UiText.Get(resourceKey)
            : UiText.Format(ChartWorkflowCommandCatalog.CommandNotYetAvailableStatusResourceKey, ChartWorkflowCaption(command));

    private void RefreshUnsupportedChartWorkflow(ChartWorkflowCommandDescriptor command) =>
        RefreshShell(ChartWorkflowUnsupportedStatus(command));

    // ---- Chart Design: Change Chart Type (real, ChangeChartTypeCommand) -------------------------------

    private async Task ShowChangeChartTypeDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var command = ChartWorkflowCommandCatalog.ChangeChartType;
        if (!TryGetSelectedChart(command, out var chart))
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
        if (!TryGetSelectedChart(command, out chart))
            return;

        var result = _session.ExecuteReviewCommand(
            ChartCommandWorkflowPlanner.BuildChangeTypeCommand(
                _session.ActiveSheet.Id,
                chart,
                plan.AppliedType!.Value));
        RefreshShell(result.Success
            ? UiText.Format("ChartLoc_ChangedChartTypeTo", ChartTypeChangePlanner.DisplayName(plan.AppliedType!.Value))
            : result.ErrorMessage ?? UiText.Get("ChartLoc_ChangeChartTypeFailed"));
    }

    /// <summary>
    /// "Change Chart Type" picker matching the WPF <c>ChangeChartTypeDialog</c> ("All Charts" panel):
    /// a left category list (Column, Line, Pie, Bar, Area, X Y (Scatter), …) and a right area whose top
    /// shows a subtype gallery for the selected category and whose side carries a preview panel. Both the
    /// category grouping and the authorable subtype set come from the shared
    /// <see cref="ChartTypeChangePlanner"/> so the surface stays in lock-step with the renderer's
    /// authorable families. Returns the chosen <see cref="ChartType"/> or null on cancel.
    /// </summary>
    private async Task<ChartType?> ShowChartTypePickerAsync(ChartType currentType)
    {
        // The category list, subtype gallery and preview are the shared All Charts body, which the
        // Insert Chart dialog hosts on its second tab.
        var (bodyGrid, _, subtypeGallery) = BuildAllChartsPickerBody(currentType, "ChangeChartType");

        var dialog = NewChartDialog(UiText.Get("ChangeChartType_Title"), "ChangeChartTypeDialog");
        dialog.Width = ChartTypeChangePlanner.DialogWidth;
        dialog.Height = ChartTypeChangePlanner.DialogHeight;
        dialog.MinWidth = ChartTypeChangePlanner.DialogWidth;
        dialog.MinHeight = ChartTypeChangePlanner.DialogHeight;
        dialog.SizeToContent = SizeToContent.Manual;

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons(
            "ChangeChartType",
            ChartTypeChangePlanner.PickerButtonWidth);
        okButton.Click += (_, _) => dialog.Close(subtypeGallery.SelectedItem is ChartTypePickerOptionPlan picked ? (ChartType?)picked.Type : null);
        cancelButton.Click += (_, _) => dialog.Close((ChartType?)null);
        buttonRow.Margin = new Thickness(0, 8, 0, 0);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 0,
            Children =
            {
                // WPF "Choose a chart type" heading.
                new TextBlock
                {
                    Text = UiText.Get(ChartTypePickerPlanner.ChooseChartTypeHeadingKey),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8),
                },
                bodyGrid,
                buttonRow,
            },
        };
        AvaloniaCompactDialogChrome.ApplyWindow(dialog, ChartDialogChromeStyle);
        ConfigureChartDialogKeyboardLifecycle(dialog, subtypeGallery);

        return await dialog.ShowDialog<ChartType?>(this);
    }

    /// <summary>The All Charts picker body with the two lists the hosting dialog reads and focuses.</summary>
    private readonly record struct ChartTypePickerBody(Grid Body, ListBox CategoryList, ListBox SubtypeGallery);

    /// <summary>
    /// Builds the All Charts panel shared by the Change Chart Type dialog and the Insert Chart dialog's
    /// second tab (WPF <c>InsertChartDialog.CreateAllChartsPanel</c>): heading and help above a category
    /// list, the subtype gallery for the selected category, and the preview panel. The lists are seeded
    /// from <paramref name="currentType"/> when one is given, otherwise from the first category.
    /// </summary>
    private ChartTypePickerBody BuildAllChartsPickerBody(ChartType? currentType, string automationIdPrefix)
    {
        var categories = ChartTypePickerPlanner.GetCategories();
        var panel = ChartTypePickerPlanner.GetAllChartsPanel();

        // ---- Category list (left) ------------------------------------------------------------------
        var categoryList = new ListBox
        {
            Width = ChartTypeChangePlanner.PickerCategoryWidth,
            Height = ChartTypeChangePlanner.PickerListHeight,
            SelectionMode = SelectionMode.Single,
            ItemsSource = categories,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(string.Empty)
            {
                Converter = new FuncValueConverter<ChartTypePickerCategoryPlan, string>(c => c is null ? string.Empty : UiText.Get(c.NameKey)),
            },
        };
        AutomationProperties.SetName(categoryList, UiText.Get(panel.CategoryListAutomationNameResourceKey!));
        AutomationProperties.SetAutomationId(categoryList, $"{automationIdPrefix}CategoryList");

        // ---- Subtype gallery (right top) -----------------------------------------------------------
        var subtypeGallery = new ListBox
        {
            Width = ChartTypeChangePlanner.PickerSubtypeWidth,
            Height = ChartTypeChangePlanner.PickerListHeight,
            SelectionMode = SelectionMode.Single,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(string.Empty)
            {
                Converter = new FuncValueConverter<ChartTypePickerOptionPlan, string>(o => o is null ? string.Empty : UiText.Get(o.DisplayNameKey)),
            },
        };
        AutomationProperties.SetName(subtypeGallery, UiText.Get(panel.SubtypeGalleryAutomationNameResourceKey));
        AutomationProperties.SetAutomationId(subtypeGallery, $"{automationIdPrefix}SubtypeGallery");

        categoryList.SelectionChanged += (_, _) =>
        {
            if (categoryList.SelectedItem is not ChartTypePickerCategoryPlan category)
                return;
            subtypeGallery.ItemsSource = category.Options;
            subtypeGallery.SelectedIndex = category.Options.Count > 0 ? 0 : -1;
        };

        // Select the category/subtype that owns the chart's current type (fall back to the first).
        var initialCategory =
            categories.FirstOrDefault(c => c.Options.Any(o => o.Type == currentType))
            ?? (categories.Count > 0 ? categories[0] : null);
        categoryList.SelectedItem = initialCategory;
        if (initialCategory is not null)
        {
            subtypeGallery.ItemsSource = initialCategory.Options;
            subtypeGallery.SelectedItem =
                initialCategory.Options.FirstOrDefault(o => o.Type == currentType)
                ?? (initialCategory.Options.Count > 0 ? initialCategory.Options[0] : null);
        }

        // ---- Preview panel (right side) ------------------------------------------------------------
        var preview = BuildChartTypePreviewPanel(panel.Preview);

        // WPF keeps the All Charts heading/help in the picker grid's first row. The lists begin in
        // the second row, while the preview spans both rows; preserve that vertical relationship.
        var bodyGrid = new Grid
        {
            Height = ChartTypeChangePlanner.PickerPanelHeight,
            Margin = new Thickness(ChartTypeChangePlanner.PickerColumnGap),
        };
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ChartTypeChangePlanner.PickerCategoryColumnWidth) });
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ChartTypeChangePlanner.PickerSubtypeColumnWidth) });
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ChartTypeChangePlanner.PickerPreviewWidth) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var pickerHeading = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get(panel.HeadingResourceKey),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 2),
                },
                // WPF CreateInlineHelp: dialog-size grey text with 6px below.
                new TextBlock
                {
                    Text = UiText.Get(panel.HelpResourceKey),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = ChartDialogHelpBrush,
                    Margin = new Thickness(0, 0, 0, 6),
                },
            },
        };
        Grid.SetColumnSpan(pickerHeading, 3);
        bodyGrid.Children.Add(pickerHeading);

        categoryList.Margin = new Thickness(0, ChartTypeChangePlanner.PickerColumnGap * 2, ChartTypeChangePlanner.PickerColumnGap, 0);
        Grid.SetColumn(categoryList, 0);
        Grid.SetRow(categoryList, 1);
        bodyGrid.Children.Add(categoryList);

        subtypeGallery.Margin = new Thickness(0, ChartTypeChangePlanner.PickerColumnGap * 2, ChartTypeChangePlanner.PickerColumnGap, 0);
        Grid.SetColumn(subtypeGallery, 1);
        Grid.SetRow(subtypeGallery, 1);
        bodyGrid.Children.Add(subtypeGallery);

        preview.Margin = new Thickness(0, ChartTypeChangePlanner.PickerColumnGap * 2, 0, 0);
        Grid.SetColumn(preview, 2);
        Grid.SetRowSpan(preview, 2);
        bodyGrid.Children.Add(preview);

        return new ChartTypePickerBody(bodyGrid, categoryList, subtypeGallery);
    }

    /// <summary>
    /// The "Preview" side panel for the Change Chart Type dialog: a bordered box with the preview
    /// title/body text and a small bar-chart sample, mirroring WPF's <c>CreatePreviewPanel</c>.
    /// </summary>
    private Border BuildChartTypePreviewPanel(ChartTypePickerPreviewDescriptor preview)
    {
        var sampleBars = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Bottom,
            Spacing = 8,
        };
        foreach (var height in new double[] { 26, 54, 38, 72 })
        {
            sampleBars.Children.Add(new Border
            {
                Width = 22,
                Height = height,
                Background = Brush(0, 120, 215),
            });
        }

        var sampleArea = new Grid { Height = 92 };
        sampleArea.Children.Add(new Border
        {
            BorderBrush = Brush(150, 150, 150),
            BorderThickness = new Thickness(0, 0, 0, 1),
            VerticalAlignment = AvaloniaVerticalAlignment.Bottom,
        });
        sampleArea.Children.Add(sampleBars);

        return new Border
        {
            BorderBrush = Brush(150, 150, 150),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            // WPF leaves the preview border stretched down both picker rows.
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = UiText.Get(preview.TitleResourceKey),
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 0, 0, 12),
                    },
                    new TextBlock
                    {
                        Text = UiText.Get(preview.BodyResourceKey),
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 14),
                    },
                    new TextBlock
                    {
                        Text = UiText.Get(preview.SampleLabelResourceKey),
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 0, 0, 8),
                    },
                    sampleArea,
                },
            },
        };
    }

    // ---- Chart Design: Select Data Source (real, ChangeChartSourceCommand) ----------------------------

    private static AvaloniaCompactDialogChromeStyle SelectDataSourceDialogChromeStyle =>
        ChartDialogChromeStyle with
        {
            ControlHeight = 22,
            TextBoxHeight = 22,
            ButtonHeight = 22,
            ButtonPadding = new Thickness(8, 1),
            ListBoxItemMinHeight = 22,
            KeepListRowMinHeight = true,
        };

    private async Task ShowSelectChartDataDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var command = ChartWorkflowCommandCatalog.SelectDataSource;
        if (!TryGetSelectedChart(command, out var chart))
            return;

        var result = await ShowSelectDataSourceDialogAsync(
            FormatRangeReference(chart.DataRange),
            chart.FirstColIsCategories,
            chart.BlankDisplayMode,
            chart.ShowDataInHiddenRowsAndColumns,
            chart.SeriesInRows);
        if (result is not { } choice)
            return;

        if (!TryParseDefinedNameRange(choice.SourceRangeText, out var dataRange))
        {
            RefreshShell(UiText.Get("ChartLoc_EnterValidChartDataRange"));
            return;
        }

        // Re-resolve after the dialog in case the selection changed while it was open.
        if (!TryGetSelectedChart(command, out chart))
            return;

        var commandResult = _session.ExecuteReviewCommand(
            ChartCommandWorkflowPlanner.BuildChangeSourceCommand(
                _session.ActiveSheet.Id,
                chart,
                dataRange,
                choice.FirstColumnIsCategories,
                choice.SwitchRowColumn));
        if (!commandResult.Success)
        {
            RefreshShell(commandResult.ErrorMessage ?? UiText.Get("ChartLoc_SelectDataFailed"));
            return;
        }

        // WPF replays each Remove Series click in click order as its own undoable command; a single
        // rejected removal does not block the rest.
        if (choice.PendingSeriesRemovals is { Count: > 0 } pendingRemovals)
        {
            foreach (var seriesIndex in pendingRemovals)
            {
                _session.ExecuteReviewCommand(
                    ChartCommandWorkflowPlanner.BuildRemoveSeriesCommand(_session.ActiveSheet.Id, chart, seriesIndex));
            }
        }

        // Only issue a hidden/empty-cell undo step when the sub-dialog actually changed something.
        if (choice.BlankDisplayMode != chart.BlankDisplayMode ||
            choice.ShowDataInHiddenRowsAndColumns != chart.ShowDataInHiddenRowsAndColumns)
        {
            _session.ExecuteReviewCommand(
                ChartCommandWorkflowPlanner.BuildHiddenEmptyCellsCommand(
                    _session.ActiveSheet.Id,
                    chart,
                    choice.BlankDisplayMode,
                    choice.ShowDataInHiddenRowsAndColumns));
        }

        RefreshShell(UiText.Format("ChartLoc_ChartDataSourceSetTo", FormatRangeReference(dataRange)));
    }

    /// <summary>
    /// Full "Select Data Source" dialog with Legend Entries (Series) list, Horizontal (Category)
    /// Axis Labels list, Add/Edit/Remove series buttons, Switch Row/Column checkbox, and a range
    /// text-box.  Matches the WPF <c>SelectDataSourceDialog</c> layout and feature set.
    /// </summary>
    private async Task<SelectDataSourceResult?> ShowSelectDataSourceDialogAsync(
        string initialRange,
        bool firstColumnIsCategories,
        ChartBlankDisplayMode blankDisplayMode = ChartBlankDisplayMode.Gap,
        bool showDataInHiddenRowsAndColumns = false,
        bool switchRowColumn = false)
    {
        var rangeField = SelectDataSourcePlanner.GetChartDataRangeField();
        var switchField = SelectDataSourcePlanner.GetSwitchRowColumnField();
        var seriesPanel = SelectDataSourcePlanner.GetSeriesPanel();
        var axisLabelsPanel = SelectDataSourcePlanner.GetAxisLabelsPanel();
        var firstColumnField = SelectDataSourcePlanner.GetFirstColumnCategoriesField();
        var hiddenEmptyAction = SelectDataSourcePlanner.GetHiddenEmptyCellsAction();

        // ---- Range text box -------------------------------------------------------------------
        var rangeBox = CreateChartTextBox(initialRange, 540, UiText.Get("ChartLoc_RangePlaceholder"));
        AutomationProperties.SetName(rangeBox, UiText.Get(rangeField.AutomationNameResourceKey!));
        AutomationProperties.SetAutomationId(rangeBox, rangeField.AutomationId);

        // Reference-picker ("...") button to the left of the range box, matching the WPF
        // DialogReferencePicker editor.
        var rangePickButton = CreateChartButton("...", 28);
        AutomationProperties.SetName(rangePickButton, UiText.Get(SelectDataSourcePlanner.SelectRangeAutomationNameResourceKey));
        AutomationProperties.SetAutomationId(rangePickButton, "SelectChartDataRangePickButton");

        // WPF DialogReferencePicker.CreateEditor docks the 28px picker left with a 6px gap and lets the
        // reference box fill the rest of the row.
        rangePickButton.Margin = new Thickness(0, 0, 6, 0);
        rangeBox.Width = double.NaN;
        rangeBox.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;
        Grid.SetColumn(rangeBox, 1);
        var rangeRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { rangePickButton, rangeBox },
        };

        // ---- Switch Row/Column checkbox -------------------------------------------------------
        var switchRowColumnCheck = CreateChartCheckBox(StripDisplayMnemonic(UiText.Get(switchField.LabelResourceKey)), switchRowColumn);
        // WPF: a 16px check box with a 0,10,0,8 margin. The Avalonia check row is 20px, so 2px comes off
        // each side to keep the 34px row pitch.
        switchRowColumnCheck.MinHeight = 20;
        switchRowColumnCheck.MaxHeight = 20;
        switchRowColumnCheck.Margin = new Thickness(0, 8, 0, 6);
        AutomationProperties.SetAutomationId(switchRowColumnCheck, switchField.AutomationId);

        // ---- Series ListBox + buttons ---------------------------------------------------------
        var seriesList = new ListBox
        {
            Height = 72,
            SelectionMode = SelectionMode.Single,
        };
        AutomationProperties.SetName(seriesList, UiText.Get(seriesPanel.ListField.AutomationNameResourceKey!));
        AutomationProperties.SetAutomationId(seriesList, seriesPanel.ListField.AutomationId);
        AutomationProperties.SetHelpText(seriesList, UiText.Get(seriesPanel.ListField.HelpResourceKey!));

        var addSeriesAction = seriesPanel.Actions.Single(action => action.Id == SelectDataSourceDialogActionId.AddSeries);
        var addSeriesButton = CreateChartButton(UiText.Get(addSeriesAction.LabelResourceKey), 92);
        AutomationProperties.SetAutomationId(addSeriesButton, addSeriesAction.AutomationId);

        var editSeriesAction = seriesPanel.Actions.Single(action => action.Id == SelectDataSourceDialogActionId.EditSeries);
        var editSeriesButton = CreateChartButton(UiText.Get(editSeriesAction.LabelResourceKey), 92);
        editSeriesButton.IsEnabled = false;
        AutomationProperties.SetAutomationId(editSeriesButton, editSeriesAction.AutomationId);

        var removeSeriesAction = seriesPanel.Actions.Single(action => action.Id == SelectDataSourceDialogActionId.RemoveSeries);
        var removeSeriesButton = CreateChartButton(UiText.Get(removeSeriesAction.LabelResourceKey), 92);
        removeSeriesButton.IsEnabled = false;
        AutomationProperties.SetAutomationId(removeSeriesButton, removeSeriesAction.AutomationId);

        // ---- Axis Labels ListBox + button -----------------------------------------------------
        var axisLabelsList = new ListBox
        {
            Height = 72,
            SelectionMode = SelectionMode.Single,
        };
        AutomationProperties.SetName(axisLabelsList, UiText.Get(axisLabelsPanel.ListField.AutomationNameResourceKey!));
        AutomationProperties.SetAutomationId(axisLabelsList, axisLabelsPanel.ListField.AutomationId);
        AutomationProperties.SetHelpText(axisLabelsList, UiText.Get(axisLabelsPanel.ListField.HelpResourceKey!));

        var editAxisLabelsAction = axisLabelsPanel.Actions.Single(action => action.Id == SelectDataSourceDialogActionId.EditAxisLabels);
        var editAxisLabelsButton = CreateChartButton(UiText.Get(editAxisLabelsAction.LabelResourceKey), 92);
        editAxisLabelsButton.IsEnabled = false;
        AutomationProperties.SetAutomationId(editAxisLabelsButton, editAxisLabelsAction.AutomationId);

        // ---- First column contains category labels checkbox -----------------------------------
        var categoriesCheck = CreateChartCheckBox(StripDisplayMnemonic(UiText.Get(firstColumnField.LabelResourceKey)), firstColumnIsCategories);
        categoriesCheck.MinHeight = 20;
        categoriesCheck.MaxHeight = 20;
        categoriesCheck.Margin = new Thickness(0, 8, 0, 6);
        AutomationProperties.SetAutomationId(categoriesCheck, firstColumnField.AutomationId);

        // ---- State management helpers --------------------------------------------------------
        // The series ListBox items: stored as a mutable list so Add/Remove work independently of
        // the planner inference.  Refreshed from the planner when the range text or categories
        // checkbox changes; after user-driven Add/Remove the list is in "manual" mode until the
        // range box is edited again.
        var seriesItems = new List<string>();
        var inManualSeriesMode = false;
        // WPF queues each Remove Series click on a chart-backed row (in click order) and replays it
        // after OK; rows appended by Add Series sit after the real ones and are list-only.
        var pendingSeriesRemovals = new List<int>();
        var realSeriesCount = 0;

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
                categoriesCheck.IsChecked == true,
                index => UiText.Format("SelectDataSource_SeriesNameFormat", index),
                index => UiText.Format("SelectDataSource_CategoryNameFormat", index),
                UiText.Get("SelectDataSource_CategoryLabelsFallback"),
                switchRowColumnCheck.IsChecked == true);

            seriesItems.Clear();
            foreach (var s in preview.Series)
                seriesItems.Add(SelectDataSourcePlanner.FormatSeriesListItem(s.Name, s.ValuesRangeText));
            // A fresh preview re-derives the list, so queued removals no longer refer to a row.
            pendingSeriesRemovals.Clear();
            realSeriesCount = seriesItems.Count;

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
        switchRowColumnCheck.IsCheckedChanged += (_, _) =>
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

        // WPF routes the Edit button and a double-click on the list through the same handler.
        void EditSelectedSeries()
        {
            if (seriesList.Items.Count > 0 && seriesList.SelectedIndex < 0)
                seriesList.SelectedIndex = 0;
        }

        void EditAxisLabels()
        {
            if (axisLabelsList.Items.Count > 0)
                axisLabelsList.SelectedIndex = 0;
        }

        editSeriesButton.Click += (_, _) => EditSelectedSeries();
        seriesList.DoubleTapped += (_, _) => EditSelectedSeries();
        axisLabelsList.DoubleTapped += (_, _) => EditAxisLabels();

        removeSeriesButton.Click += (_, _) =>
        {
            var idx = seriesList.SelectedIndex;
            if (idx < 0)
                return;
            inManualSeriesMode = true;
            if (idx < realSeriesCount)
            {
                pendingSeriesRemovals.Add(idx);
                realSeriesCount--;
            }

            seriesItems.RemoveAt(idx);
            seriesList.ItemsSource = null;
            seriesList.ItemsSource = seriesItems;
            seriesList.SelectedIndex = seriesItems.Count == 0 ? -1 : Math.Min(idx, seriesItems.Count - 1);
            RefreshButtonState();
        };

        editAxisLabelsButton.Click += (_, _) => EditAxisLabels();

        // ---- Initial population of the preview lists -----------------------------------------
        RefreshLists();

        // ---- Dialog layout -------------------------------------------------------------------
        var dialog = NewChartDialog(
            UiText.Get(SelectDataSourcePlanner.DialogTitleResourceKey),
            SelectDataSourcePlanner.DialogAutomationId);
        AvaloniaCompactDialogChrome.ApplyWindow(dialog, SelectDataSourceDialogChromeStyle);
        // WPF SelectDataSourceDialog is a 620x500 outer window; Avalonia sizes are client sizes.
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Width = 620;
        dialog.Height = 500 - WpfNoResizeFrameHeight;
        dialog.MinWidth = 620;
        dialog.MinHeight = 500 - WpfNoResizeFrameHeight;

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("SelectChartData");
        okButton.Click += (_, _) => RunGuarded(async () =>
        {
            var rangeText = rangeBox.Text ?? string.Empty;
            // WPF validates in the dialog: an unusable range warns, keeps the dialog open and puts the
            // caret back in the range editor with its text selected.
            if (!ChartInputParser.TryParseDataRange(
                    rangeText,
                    _session.ActiveSheet.Id,
                    name => _session.Workbook.GetSheet(name)?.Id,
                    out _))
            {
                await AvaloniaUserMessageDialog.ShowWarningAsync(
                    dialog,
                    UiText.Get(SelectDataSourcePlanner.InvalidRangeMessageResourceKey),
                    UiText.Get(SelectDataSourcePlanner.DialogTitleResourceKey));
                rangeBox.Focus();
                rangeBox.SelectAll();
                return;
            }

            // The planner result carries the range and the two check boxes; the queued series
            // removals and the hidden/empty-cell choice ride along on the same result.
            SelectDataSourceResult? WithPendingEdits(SelectDataSourceResult accepted) =>
                accepted with
                {
                    PendingSeriesRemovals = pendingSeriesRemovals.ToList(),
                    BlankDisplayMode = blankDisplayMode,
                    ShowDataInHiddenRowsAndColumns = showDataInHiddenRowsAndColumns,
                };

            var accepted = WithPendingEdits(SelectDataSourcePlanner.CreateResult(
                rangeText,
                categoriesCheck.IsChecked == true,
                switchRowColumnCheck.IsChecked == true));
            dialog.Close(accepted);
        });
        cancelButton.Click += (_, _) => dialog.Close((SelectDataSourceResult?)null);

        // Helper to build a panel with a list on the left and buttons stacked on the right.
        Grid MakeListPanel(SelectDataSourceListPanelDescriptor panel, ListBox list, IEnumerable<Button> buttons)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // WPF: a plain title 2px above grey inline help, which carries 6px below.
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = StripDisplayMnemonic(UiText.Get(panel.TitleResourceKey)), FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 0, 0, 2) });
            header.Children.Add(CreateChartDialogInlineHelp(StripDisplayMnemonic(UiText.Get(panel.ListField.HelpResourceKey!))));
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
        // WPF: 150 wide, left aligned, 16px above the OK/Cancel row. The compact Avalonia button has
        // wider padding than the WPF one, so 150 is the minimum and a longer caption can still grow it.
        var hiddenEmptyButton = CreateChartButton(UiText.Get(hiddenEmptyAction.LabelResourceKey), 150);
        hiddenEmptyButton.Width = double.NaN;
        hiddenEmptyButton.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
        hiddenEmptyButton.Margin = new Thickness(0, 0, 0, 16);
        AutomationProperties.SetAutomationId(hiddenEmptyButton, hiddenEmptyAction.AutomationId);
        hiddenEmptyButton.Click += (_, _) => RunGuarded(async () =>
        {
            var settings = await ShowHiddenEmptyCellSettingsDialogAsync(
                dialog,
                blankDisplayMode,
                showDataInHiddenRowsAndColumns);
            if (settings is not { } chosen)
                return;

            blankDisplayMode = chosen.BlankDisplayMode;
            showDataInHiddenRowsAndColumns = chosen.ShowDataInHiddenRowsAndColumns;
        });

        // WPF stacks the rows with their own margins (no uniform spacing): label 4px above the range
        // editor, check boxes 10px below / 8px above their neighbours, list panels 8px apart.
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 0,
            MinWidth = 588,
            Children =
            {
                // Range label + picker button + text box
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get(rangeField.LabelResourceKey)), FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 0, 0, 4) },
                rangeRow,
                switchRowColumnCheck,
                // Legend Entries (Series) panel
                MakeListPanel(
                    seriesPanel,
                    seriesList,
                    new[] { addSeriesButton, editSeriesButton, removeSeriesButton }),
                // Horizontal (Category) Axis Labels panel
                MakeListPanel(
                    axisLabelsPanel,
                    axisLabelsList,
                    new[] { editAxisLabelsButton }),
                // First column is categories checkbox
                categoriesCheck,
                hiddenEmptyButton,
                // OK / Cancel
                buttonRow,
            },
        };
        AttachDialogRangePicker(dialog, rangePickButton, rangeBox, "range.chart-data-source.range");

        return await dialog.ShowDialog<SelectDataSourceResult?>(this);
    }

    /// <summary>The blank-cell display mode and hidden-data visibility chosen in the sub-dialog.</summary>
    private readonly record struct HiddenEmptyCellSettingsChoice(
        ChartBlankDisplayMode BlankDisplayMode,
        bool ShowDataInHiddenRowsAndColumns);

    /// <summary>
    /// The Select Data Source sub-dialog matching WPF <c>HiddenEmptyCellSettingsDialog</c>: three radio
    /// buttons for how blank cells plot and a check box for hidden rows and columns. Returns null when
    /// cancelled; the caller applies the choice through
    /// <see cref="ChartCommandWorkflowPlanner.BuildHiddenEmptyCellsCommand"/> after the parent dialog is
    /// accepted.
    /// </summary>
    private static async Task<HiddenEmptyCellSettingsChoice?> ShowHiddenEmptyCellSettingsDialogAsync(
        Window owner,
        ChartBlankDisplayMode blankDisplayMode,
        bool showDataInHiddenRowsAndColumns)
    {
        const string BlankDisplayGroup = "SelectDataSourceHiddenEmptyBlankDisplay";
        var gapsButton = CreateChartRadioButton(
            UiText.Get("SelectDataSource_HiddenEmptyCells_GapsOption"),
            BlankDisplayGroup,
            blankDisplayMode is not (ChartBlankDisplayMode.Zero or ChartBlankDisplayMode.Span));
        var zeroButton = CreateChartRadioButton(
            UiText.Get("SelectDataSource_HiddenEmptyCells_ZeroOption"),
            BlankDisplayGroup,
            blankDisplayMode == ChartBlankDisplayMode.Zero);
        var connectButton = CreateChartRadioButton(
            UiText.Get("SelectDataSource_HiddenEmptyCells_ConnectOption"),
            BlankDisplayGroup,
            blankDisplayMode == ChartBlankDisplayMode.Span);
        var showHiddenDataBox = CreateChartCheckBox(
            UiText.Get("SelectDataSource_HiddenEmptyCells_ShowHiddenDataOption"),
            showDataInHiddenRowsAndColumns);
        AutomationProperties.SetAutomationId(gapsButton, "HiddenEmptyCellsGapsOption");
        AutomationProperties.SetAutomationId(zeroButton, "HiddenEmptyCellsZeroOption");
        AutomationProperties.SetAutomationId(connectButton, "HiddenEmptyCellsConnectOption");
        AutomationProperties.SetAutomationId(showHiddenDataBox, "HiddenEmptyCellsShowHiddenDataCheck");

        // WPF: radios 4px apart (16px glyph rows), the check box 16px below them. The Avalonia rows are
        // 20px, so 2px comes off each gap to keep the pitch.
        gapsButton.Margin = new Thickness(0, 0, 0, 0);
        zeroButton.Margin = new Thickness(0, 0, 0, 0);
        connectButton.Margin = new Thickness(0);
        showHiddenDataBox.MinHeight = 20;
        showHiddenDataBox.MaxHeight = 20;
        showHiddenDataBox.Margin = new Thickness(0, 14, 0, 0);

        var checkedButton = zeroButton.IsChecked == true ? zeroButton
            : connectButton.IsChecked == true ? connectButton
            : gapsButton;
        var settingsDialog = NewChartDialog(
            UiText.Get(SelectDataSourcePlanner.HiddenEmptyCellsTitleResourceKey),
            "SelectChartDataHiddenEmptyDialog",
            checkedButton);

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("HiddenEmptyCells");
        buttonRow.Margin = new Thickness(0, 20, 0, 0);
        okButton.Click += (_, _) =>
        {
            var mode = zeroButton.IsChecked == true
                ? ChartBlankDisplayMode.Zero
                : connectButton.IsChecked == true
                    ? ChartBlankDisplayMode.Span
                    : ChartBlankDisplayMode.Gap;
            settingsDialog.Close((HiddenEmptyCellSettingsChoice?)new HiddenEmptyCellSettingsChoice(
                mode,
                showHiddenDataBox.IsChecked == true));
        };
        cancelButton.Click += (_, _) => settingsDialog.Close((HiddenEmptyCellSettingsChoice?)null);

        // WPF HiddenEmptyCellSettingsDialog is a 360x280 window with a 16px margin.
        HostChartDialogContent(
            settingsDialog,
            360,
            new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    new TextBlock
                    {
                        Text = UiText.Get("SelectDataSource_HiddenEmptyCells_ShowEmptyCellsAsLabel"),
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                        Margin = new Thickness(0, 0, 0, 8),
                    },
                    gapsButton,
                    zeroButton,
                    connectButton,
                    showHiddenDataBox,
                },
            },
            buttonRow,
            fixedWpfHeight: 280);

        return await settingsDialog.ShowDialog<HiddenEmptyCellSettingsChoice?>(owner);
    }

    // ---- Chart Design: layout toggles (real, SetChartLayoutCommand) -----------------------------------

    private async Task ShowChartTitlesDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var command = ChartWorkflowCommandCatalog.ChartTitles;
        if (!TryGetSelectedChart(command, out var chart))
            return;

        var current = ChartTitlesPlanner.Read(chart);
        var result = await ShowChartTitlesDialogAsync(current.ChartTitle, current.XAxisTitle, current.YAxisTitle);
        if (result is not { } titles)
            return;

        if (!TryGetSelectedChart(command, out chart))
            return;

        // The shared planner trims/collapses each title and drops axis titles for axis-less chart types
        // (pie/doughnut), matching Core's EnforceAxisTitleSupport.
        var options = ChartTitlesPlanner.Plan(
            chart.Type,
            new ChartTitlesInput(titles.ChartTitle, titles.XAxisTitle, titles.YAxisTitle));
        ApplyChartLayout(command, chart, options);
    }

    private async Task<(string ChartTitle, string XAxisTitle, string YAxisTitle)?> ShowChartTitlesDialogAsync(
        string chartTitle,
        string xAxisTitle,
        string yAxisTitle)
    {
        var chartTitleBox = CreateChartTextBox(chartTitle, 260);
        AutomationProperties.SetAutomationId(chartTitleBox, "ChartTitleBox");
        var xAxisBox = CreateChartTextBox(xAxisTitle, 260);
        AutomationProperties.SetAutomationId(xAxisBox, "ChartXAxisTitleBox");
        var yAxisBox = CreateChartTextBox(yAxisTitle, 260);
        AutomationProperties.SetAutomationId(yAxisBox, "ChartYAxisTitleBox");

        var dialog = NewChartDialog(UiText.Get("ChartTitles_Title"), "ChartTitlesDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartTitles");
        okButton.Click += (_, _) => dialog.Close(((string, string, string)?)(
            chartTitleBox.Text ?? string.Empty,
            xAxisBox.Text ?? string.Empty,
            yAxisBox.Text ?? string.Empty));
        cancelButton.Click += (_, _) => dialog.Close(((string, string, string)?)null);

        // WPF ChartTitlesDialog (380x240): each label sits 4px above a full-width box that leaves 8px
        // below, and the button row follows the last box directly.
        var titleFields = new StackPanel();
        void AddTitleInput(string labelResourceKey, TextBox box)
        {
            titleFields.Children.Add(new TextBlock
            {
                Text = StripDisplayMnemonic(UiText.Get(labelResourceKey)),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                Margin = new Thickness(0, 0, 0, 4),
            });
            box.Width = double.NaN;
            box.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;
            box.Margin = new Thickness(0, 0, 0, 8);
            titleFields.Children.Add(box);
        }

        AddTitleInput("ChartTitles_ChartTitleLabel", chartTitleBox);
        AddTitleInput("ChartTitles_XAxisTitleLabel", xAxisBox);
        AddTitleInput("ChartTitles_YAxisTitleLabel", yAxisBox);
        HostChartDialogContent(dialog, 380, titleFields, buttonRow);

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
        var commandLabel = UiText.Get("ChartLoc_LegendTitle");
        if (!TryGetSelectedChart(commandLabel, out var chart))
            return;

        var current = ChartLegendPlanner.Read(chart);
        var result = await ShowChartLegendDialogAsync(current.ShowLegend, current.Position);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart(commandLabel, out chart))
            return;

        ApplyChartLayout(commandLabel, chart, ChartLegendPlanner.Plan(edited));
    }

    private async Task<ChartLegendInput?> ShowChartLegendDialogAsync(bool showLegend, ChartLegendPosition position)
    {
        var showCheck = CreateChartCheckBox(UiText.Get("ChartLoc_ShowLegend"), showLegend);
        AutomationProperties.SetAutomationId(showCheck, "ChartLegendShowCheck");

        var positionChoices = ChartLegendPlanner.GetPositionChoices();
        var positionCombo = CreateChartComboBox(260, positionChoices);
        positionCombo.DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartLegendPositionChoice.DisplayName));
        AutomationProperties.SetName(
            positionCombo,
            UiText.CreateAutomationName(UiText.Get("ChartLoc_LegendPositionLabel")));
        AutomationProperties.SetAutomationId(positionCombo, "ChartLegendPositionCombo");
        positionCombo.SelectedItem =
            positionChoices.FirstOrDefault(c => c.Position == position)
            ?? (positionChoices.Count > 0 ? positionChoices[0] : null);

        var dialog = NewChartDialog(UiText.Get("ChartLoc_LegendTitle"), "ChartLegendDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartLegend");
        okButton.Click += (_, _) =>
        {
            var chosenPosition = positionCombo.SelectedItem is ChartLegendPositionChoice picked
                ? picked.Position
                : ChartLegendPosition.Right;
            dialog.Close((ChartLegendInput?)new ChartLegendInput(showCheck.IsChecked == true, chosenPosition));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartLegendInput?)null);
        buttonRow.Margin = new Thickness(0, 8, 0, 0);

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
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartLegendInput?>(this);
    }

    private void CycleChartDataLabelPosition()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var commandLabel = UiText.Get("MainWindow_TooltipTitle_DataLabelPosition");
        if (!TryGetSelectedChart(commandLabel, out var chart))
            return;

        ApplyChartLayout(commandLabel, chart, new ChartLayoutOptions(
            ShowDataLabels: true,
            DataLabelPosition: ChartQuickFormatCycler.NextDataLabelPosition(chart.DataLabelPosition)));
    }

    private void CycleChartStyle()
    {
        RunGuarded(ShowChartStyleDialogAsync);
    }

    private void CycleChartSecondaryAxis()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var command = ChartWorkflowCommandCatalog.SecondaryAxis;
        if (!TryGetSelectedChart(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            RefreshUnsupportedChartWorkflow(command);
            return;
        }

        ApplyChartLayout(command, chart, ChartAxisPlanner.PlanSecondaryAxisToggle(chart));
    }

    // ---- Chart Format: shape fill / outline + formatting toggles (real, SetChartLayoutCommand) --------

    private async Task ShowChartShapeFillDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var commandLabel = UiText.Get("ChartLoc_ChartAreaFill");
        if (!TryGetSelectedChart(commandLabel, out var chart))
            return;

        var color = await ShowMoreColorsDialogAsync(
            UiText.Get("ChartLoc_ChartAreaFill"),
            chart.ChartAreaFillColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
        if (color is { } chosen && TryGetSelectedChart(commandLabel, out chart))
            ApplyChartLayout(commandLabel, chart, new ChartLayoutOptions(ChartAreaFillColor: chosen));
    }

    private async Task ShowChartShapeOutlineDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var commandLabel = UiText.Get("ChartLoc_PlotAreaBorder");
        if (!TryGetSelectedChart(commandLabel, out var chart))
            return;

        var color = await ShowMoreColorsDialogAsync(
            UiText.Get("ChartLoc_PlotAreaBorder"),
            chart.PlotAreaBorderColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
        if (color is { } chosen && TryGetSelectedChart(commandLabel, out chart))
        {
            ApplyChartLayout(commandLabel, chart, new ChartLayoutOptions(
                PlotAreaBorderColor: chosen,
                PlotAreaBorderThickness: ChartQuickFormatCycler.NextPlotAreaBorderThickness(chart.PlotAreaBorderThickness)));
        }
    }

    private async Task ShowChartPlotAreaFillDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var commandLabel = UiText.Get("ChartLoc_PlotAreaFill");
        if (!TryGetSelectedChart(commandLabel, out var chart))
            return;

        var color = await ShowMoreColorsDialogAsync(
            UiText.Get("ChartLoc_PlotAreaFill"),
            chart.PlotAreaFillColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
        if (color is { } chosen && TryGetSelectedChart(commandLabel, out chart))
            ApplyChartLayout(commandLabel, chart, new ChartLayoutOptions(PlotAreaFillColor: chosen));
    }

    private void CycleChartXAxisGridlines()
    {
        ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.Gridlines(useXAxis: true), "ChartLoc_NoAxesForGridlines");
    }

    private void CycleChartYAxisGridlines()
    {
        ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.Gridlines(useXAxis: false), "ChartLoc_NoAxesForGridlines");
    }

    private void ToggleChartXAxisLabels()
    {
        ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.Labels(useXAxis: true), "ChartLoc_NoAxes");
    }

    private void ToggleChartYAxisLabels()
    {
        ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.Labels(useXAxis: false), "ChartLoc_NoAxes");
    }

    private void ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandDescriptor command, string unsupportedStatusResourceKey)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var commandLabel = ChartWorkflowCaption(command);
        if (!TryGetSelectedChart(commandLabel, out var chart))
            return;

        if (!ChartAxisPlanner.SupportsAxes(chart.Type))
        {
            RefreshShell(UiText.Get(unsupportedStatusResourceKey));
            return;
        }

        if (command.QuickCommand is not { } quickCommand)
            return;

        ApplyChartLayout(commandLabel, chart, ChartAxisPlanner.PlanQuickCommand(chart, command.UseXAxis, quickCommand));
    }

    private void ExecuteChartAxisPlannedCommand(
        ChartAxisWorkflowCommandDescriptor command,
        Func<Sheet, ChartModel, bool, ChartAxisCommandPlan> planner)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var commandLabel = ChartWorkflowCaption(command);
        if (!TryGetSelectedChart(commandLabel, out var chart))
            return;

        var plan = planner(_session.ActiveSheet, chart, command.UseXAxis);
        if (plan.Options is not { } options)
        {
            RefreshShell(ChartValidationPresentationPlanner
                .DescribeAxisCommandIssue(plan.Issue, command.UseXAxis)
                .Resolve(UiText.Get, UiText.Format));
            return;
        }

        ApplyChartLayout(commandLabel, chart, options);
    }

}
