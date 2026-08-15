using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Fill ▸ Series dialog for the Avalonia/macOS shell (Home ▸ Fill ▸ Series). It picks a series direction
/// (rows/columns), a type (linear/growth/date/autofill), a date unit, and step/stop values, then fills the
/// selected range from its seed cell. The input parsing/validation and the series edit plan come from the
/// portable <see cref="FillSeriesPlanner"/>; edits run through the shared session command path (undoable +
/// refreshing). User-facing strings route through <see cref="UiText"/>.
/// </summary>
public sealed partial class MainWindow
{
    // ── Home ▸ Fill ▸ Series entry point ───────────────────────────────────────
    private void FillSeries() => _ = ShowFillSeriesDialogAsync();

    private async Task ShowFillSeriesDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var dialog = new Window
        {
            Title = UiText.Get("FillSeries_Title"),
            Width = 380,
            Height = 356,
            MinWidth = 360,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "FillSeriesDialog");

        var rowsButton = new RadioButton { Content = UiText.Get("FillSeries_Rows"), GroupName = "FillSeriesIn" };
        ApplyDataOpsRadioButtonChrome(rowsButton);
        AutomationProperties.SetAutomationId(rowsButton, "FillSeriesRowsButton");
        var columnsButton = new RadioButton { Content = UiText.Get("FillSeries_Columns"), GroupName = "FillSeriesIn", IsChecked = true };
        ApplyDataOpsRadioButtonChrome(columnsButton);
        AutomationProperties.SetAutomationId(columnsButton, "FillSeriesColumnsButton");

        var linearButton = new RadioButton { Content = UiText.Get("FillSeries_Linear"), GroupName = "FillSeriesType", IsChecked = true };
        ApplyDataOpsRadioButtonChrome(linearButton);
        AutomationProperties.SetAutomationId(linearButton, "FillSeriesLinearButton");
        var growthButton = new RadioButton { Content = UiText.Get("FillSeries_Growth"), GroupName = "FillSeriesType" };
        ApplyDataOpsRadioButtonChrome(growthButton);
        AutomationProperties.SetAutomationId(growthButton, "FillSeriesGrowthButton");
        var dateButton = new RadioButton { Content = UiText.Get("FillSeries_Date"), GroupName = "FillSeriesType" };
        ApplyDataOpsRadioButtonChrome(dateButton);
        AutomationProperties.SetAutomationId(dateButton, "FillSeriesDateButton");
        var autoFillButton = new RadioButton { Content = UiText.Get("FillSeries_AutoFill"), GroupName = "FillSeriesType" };
        ApplyDataOpsRadioButtonChrome(autoFillButton);
        AutomationProperties.SetAutomationId(autoFillButton, "FillSeriesAutoFillButton");

        var dayButton = new RadioButton { Content = UiText.Get("FillSeries_Day"), GroupName = "FillSeriesDateUnit", IsChecked = true };
        ApplyDataOpsRadioButtonChrome(dayButton);
        AutomationProperties.SetAutomationId(dayButton, "FillSeriesDayButton");
        var weekdayButton = new RadioButton { Content = UiText.Get("FillSeries_Weekday"), GroupName = "FillSeriesDateUnit" };
        ApplyDataOpsRadioButtonChrome(weekdayButton);
        AutomationProperties.SetAutomationId(weekdayButton, "FillSeriesWeekdayButton");
        var monthButton = new RadioButton { Content = UiText.Get("FillSeries_Month"), GroupName = "FillSeriesDateUnit" };
        ApplyDataOpsRadioButtonChrome(monthButton);
        AutomationProperties.SetAutomationId(monthButton, "FillSeriesMonthButton");
        var yearButton = new RadioButton { Content = UiText.Get("FillSeries_Year"), GroupName = "FillSeriesDateUnit" };
        ApplyDataOpsRadioButtonChrome(yearButton);
        AutomationProperties.SetAutomationId(yearButton, "FillSeriesYearButton");

        var stepBox = new TextBox { Text = "1", MinWidth = 110 };
        ApplyDataOpsTextBoxChrome(stepBox);
        AutomationProperties.SetAutomationId(stepBox, "FillSeriesStepValueBox");
        var stopBox = new TextBox { Text = string.Empty, MinWidth = 110 };
        ApplyDataOpsTextBoxChrome(stopBox);
        AutomationProperties.SetAutomationId(stopBox, "FillSeriesStopValueBox");

        var trendBox = new CheckBox { Content = UiText.Get("FillSeries_Trend") };
        ApplyDataOpsCheckBoxChrome(trendBox);
        AutomationProperties.SetAutomationId(trendBox, "FillSeriesTrendCheckBox");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(warningText, "FillSeriesWarningText");

        void UpdateDateUnitAvailability()
        {
            var isDateSeries = dateButton.IsChecked == true;
            dayButton.IsEnabled = isDateSeries;
            weekdayButton.IsEnabled = isDateSeries;
            monthButton.IsEnabled = isDateSeries;
            yearButton.IsEnabled = isDateSeries;
        }

        void UpdateTrendAvailability()
        {
            var isTrendEligible = FillSeriesPlanner.IsTrendEnabled(SelectedType());
            trendBox.IsEnabled = isTrendEligible;
            if (!isTrendEligible)
                trendBox.IsChecked = false;

            // Excel's Step value plays no part in Trend mode -- the box is disabled while Trend is checked.
            stepBox.IsEnabled = !(isTrendEligible && trendBox.IsChecked == true);
        }

        linearButton.IsCheckedChanged += (_, _) => UpdateDateUnitAvailability();
        growthButton.IsCheckedChanged += (_, _) => UpdateDateUnitAvailability();
        dateButton.IsCheckedChanged += (_, _) => UpdateDateUnitAvailability();
        autoFillButton.IsCheckedChanged += (_, _) => UpdateDateUnitAvailability();
        linearButton.IsCheckedChanged += (_, _) => UpdateTrendAvailability();
        growthButton.IsCheckedChanged += (_, _) => UpdateTrendAvailability();
        dateButton.IsCheckedChanged += (_, _) => UpdateTrendAvailability();
        autoFillButton.IsCheckedChanged += (_, _) => UpdateTrendAvailability();
        trendBox.IsCheckedChanged += (_, _) => UpdateTrendAvailability();
        UpdateDateUnitAvailability();
        UpdateTrendAvailability();

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 72 };
        ApplyDataOpsButtonChrome(okButton, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "FillSeriesOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 72 };
        ApplyDataOpsButtonChrome(cancelButton);
        AutomationProperties.SetAutomationId(cancelButton, "FillSeriesCancelButton");

        void ShowWarning(string message)
        {
            warningText.Text = message;
            warningText.IsVisible = true;
        }

        FillSeriesType SelectedType() =>
            growthButton.IsChecked == true ? FillSeriesType.Growth :
            dateButton.IsChecked == true ? FillSeriesType.Date :
            autoFillButton.IsChecked == true ? FillSeriesType.AutoFill :
            FillSeriesType.Linear;

        FillSeriesDateUnit SelectedDateUnit() =>
            weekdayButton.IsChecked == true ? FillSeriesDateUnit.Weekday :
            monthButton.IsChecked == true ? FillSeriesDateUnit.Month :
            yearButton.IsChecked == true ? FillSeriesDateUnit.Year :
            FillSeriesDateUnit.Day;

        okButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;

            var seriesIn = rowsButton.IsChecked == true ? FillSeriesDirection.Rows : FillSeriesDirection.Columns;
            var trend = trendBox.IsEnabled && trendBox.IsChecked == true;
            if (!FillSeriesPlanner.TryCreateOptions(
                    seriesIn,
                    SelectedType(),
                    SelectedDateUnit(),
                    stepBox.Text,
                    stopBox.Text,
                    trend,
                    out var options,
                    out var inputError))
            {
                ShowWarning(FillSeriesPlanner
                    .DescribeInputError(inputError)
                    .Message
                    .Resolve(UiText.Get, UiText.Format));
                return;
            }

            var range = _session.SelectedRange;
            var sheet = _session.ActiveSheet;
            var edits = FillSeriesPlanner.BuildSeriesEdits(sheet, range, options);
            if (edits.Count == 0)
            {
                ShowWarning(FillSeriesPlanner.DescribeNoSeed().Resolve(UiText.Get, UiText.Format));
                return;
            }

            // R136-fillseries-grouped-sheets-1: Excel's Group Editing mode mirrors Fill ▸ Series onto
            // every other grouped sheet (matching the WPF host's FillSeriesMenuItem_Click, which fans
            // the same computed edits out via GroupedEditCellsCommand). GetCurrentGroupedEditSheetIds
            // returns just [sheet.Id] when the workbook isn't grouped, so the single-sheet case is
            // unchanged.
            var targetSheetIds = _session.GetCurrentGroupedEditSheetIds();
            IWorkbookCommand command = targetSheetIds.Count > 1
                ? new GroupedEditCellsCommand(targetSheetIds, sheet.Id, edits)
                : new EditCellsCommand(sheet.Id, edits);
            var result = _session.ExecuteReviewCommand(command);
            if (!result.Success)
            {
                ShowWarning(FillSeriesPlanner
                    .DescribeCommandFailure(result.ErrorMessage)
                    .Resolve(UiText.Get, UiText.Format));
                return;
            }

            RefreshShell(FillSeriesPlanner
                .DescribeSuccess(FormatRangeReference(range))
                .Resolve(UiText.Get, UiText.Format));
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { cancelButton, okButton },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = UiText.Get("FillSeries_SeriesInHeader"), FontWeight = FontWeight.SemiBold, FontSize = 12, FontFamily = FormulaBarFontFamily },
                            FillSeriesRow(rowsButton, columnsButton),
                            new TextBlock { Text = UiText.Get("FillSeries_TypeHeader"), FontWeight = FontWeight.SemiBold, FontSize = 12, FontFamily = FormulaBarFontFamily },
                            FillSeriesRow(linearButton, growthButton, dateButton, autoFillButton),
                            new TextBlock { Text = UiText.Get("FillSeries_DateUnitHeader"), FontWeight = FontWeight.SemiBold, FontSize = 12, FontFamily = FormulaBarFontFamily },
                            FillSeriesRow(dayButton, weekdayButton, monthButton, yearButton),
                            FillSeriesLabeledBox(UiText.Get("FillSeries_StepValueLabel"), stepBox),
                            FillSeriesLabeledBox(UiText.Get("FillSeries_StopValueLabel"), stopBox),
                            trendBox,
                            warningText,
                        },
                    },
                },
            },
        };

        await dialog.ShowDialog(this);
    }

    private static StackPanel FillSeriesRow(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    private static Grid FillSeriesLabeledBox(string label, Control field)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        var labelBlock = new TextBlock
        {
            Text = StripDisplayMnemonic(label),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        return grid;
    }
}
