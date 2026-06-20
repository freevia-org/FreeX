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
            Width = 400,
            Height = 340,
            MinWidth = 360,
            MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "FillSeriesDialog");

        var rowsButton = new RadioButton { Content = UiText.Get("FillSeries_Rows"), GroupName = "FillSeriesIn" };
        AutomationProperties.SetAutomationId(rowsButton, "FillSeriesRowsButton");
        var columnsButton = new RadioButton { Content = UiText.Get("FillSeries_Columns"), GroupName = "FillSeriesIn", IsChecked = true };
        AutomationProperties.SetAutomationId(columnsButton, "FillSeriesColumnsButton");

        var linearButton = new RadioButton { Content = UiText.Get("FillSeries_Linear"), GroupName = "FillSeriesType", IsChecked = true };
        AutomationProperties.SetAutomationId(linearButton, "FillSeriesLinearButton");
        var growthButton = new RadioButton { Content = UiText.Get("FillSeries_Growth"), GroupName = "FillSeriesType" };
        AutomationProperties.SetAutomationId(growthButton, "FillSeriesGrowthButton");
        var dateButton = new RadioButton { Content = UiText.Get("FillSeries_Date"), GroupName = "FillSeriesType" };
        AutomationProperties.SetAutomationId(dateButton, "FillSeriesDateButton");
        var autoFillButton = new RadioButton { Content = UiText.Get("FillSeries_AutoFill"), GroupName = "FillSeriesType" };
        AutomationProperties.SetAutomationId(autoFillButton, "FillSeriesAutoFillButton");

        var dayButton = new RadioButton { Content = UiText.Get("FillSeries_Day"), GroupName = "FillSeriesDateUnit", IsChecked = true };
        AutomationProperties.SetAutomationId(dayButton, "FillSeriesDayButton");
        var weekdayButton = new RadioButton { Content = UiText.Get("FillSeries_Weekday"), GroupName = "FillSeriesDateUnit" };
        AutomationProperties.SetAutomationId(weekdayButton, "FillSeriesWeekdayButton");
        var monthButton = new RadioButton { Content = UiText.Get("FillSeries_Month"), GroupName = "FillSeriesDateUnit" };
        AutomationProperties.SetAutomationId(monthButton, "FillSeriesMonthButton");
        var yearButton = new RadioButton { Content = UiText.Get("FillSeries_Year"), GroupName = "FillSeriesDateUnit" };
        AutomationProperties.SetAutomationId(yearButton, "FillSeriesYearButton");

        var stepBox = new TextBox { Text = "1", MinWidth = 100 };
        AutomationProperties.SetAutomationId(stepBox, "FillSeriesStepValueBox");
        var stopBox = new TextBox { Text = string.Empty, MinWidth = 100 };
        AutomationProperties.SetAutomationId(stopBox, "FillSeriesStopValueBox");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
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

        linearButton.IsCheckedChanged += (_, _) => UpdateDateUnitAvailability();
        growthButton.IsCheckedChanged += (_, _) => UpdateDateUnitAvailability();
        dateButton.IsCheckedChanged += (_, _) => UpdateDateUnitAvailability();
        autoFillButton.IsCheckedChanged += (_, _) => UpdateDateUnitAvailability();
        UpdateDateUnitAvailability();

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(okButton, "FillSeriesOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 84 };
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
            if (!FillSeriesPlanner.TryCreateOptions(
                    seriesIn,
                    SelectedType(),
                    SelectedDateUnit(),
                    stepBox.Text,
                    stopBox.Text,
                    out var options,
                    out var inputError))
            {
                ShowWarning(inputError == FillSeriesInputError.InvalidStop
                    ? UiText.Get("FillSeries_InvalidStop")
                    : UiText.Get("FillSeries_InvalidStep"));
                return;
            }

            var range = _session.SelectedRange;
            var sheet = _session.ActiveSheet;
            var edits = FillSeriesPlanner.BuildSeriesEdits(sheet, range, options);
            if (edits.Count == 0)
            {
                ShowWarning(UiText.Get("FillSeries_NoSeed"));
                return;
            }

            var command = new EditCellsCommand(sheet.Id, edits);
            var result = _session.ExecuteReviewCommand(command);
            if (!result.Success)
            {
                ShowWarning(result.ErrorMessage ?? UiText.Get("FillSeries_Failed"));
                return;
            }

            RefreshShell(UiText.Format("FillSeries_Filled", FormatRangeReference(range)));
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
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
                            new TextBlock { Text = UiText.Get("FillSeries_SeriesInHeader"), FontWeight = FontWeight.SemiBold },
                            FillSeriesRow(rowsButton, columnsButton),
                            new TextBlock { Text = UiText.Get("FillSeries_TypeHeader"), FontWeight = FontWeight.SemiBold },
                            FillSeriesRow(linearButton, growthButton, dateButton, autoFillButton),
                            new TextBlock { Text = UiText.Get("FillSeries_DateUnitHeader"), FontWeight = FontWeight.SemiBold },
                            FillSeriesRow(dayButton, weekdayButton, monthButton, yearButton),
                            FillSeriesLabeledBox(UiText.Get("FillSeries_StepValueLabel"), stepBox),
                            FillSeriesLabeledBox(UiText.Get("FillSeries_StopValueLabel"), stopBox),
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

    private static StackPanel FillSeriesLabeledBox(string label, Control field) =>
        new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = label, VerticalAlignment = AvaloniaVerticalAlignment.Center, MinWidth = 96 },
                field,
            },
        };
}
