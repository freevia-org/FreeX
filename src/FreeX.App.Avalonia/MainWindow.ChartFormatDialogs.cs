using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// The remaining chart-edit dialogs for the cross-platform shell: Data Labels, Format Axis (X/Y), Format
/// Series, and Trendline. Each opens a small modal that collects input, then hands it to the matching
/// portable planner (<see cref="ChartDataLabelsPlanner"/>, <see cref="ChartAxisPlanner"/>,
/// <see cref="ChartSeriesFormatPlanner"/>, <see cref="ChartTrendlinePlanner"/>) which validates the input
/// and projects it onto a <see cref="ChartLayoutOptions"/> applied through the shared
/// <see cref="SetChartLayoutCommand"/> via <see cref="ApplyChartLayout"/>. The dialogs re-resolve the
/// selected chart after closing (the selection may have changed while the dialog was open). The WPF host's
/// <c>ChartDataLabelsDialog</c> / <c>ChartAxisFormatDialog</c> / <c>ChartSeriesFormatDialog</c> /
/// <c>ChartTrendlineOptionsDialog</c> are the behavior reference.
/// </summary>
public sealed partial class MainWindow
{
    // ---- Data Labels (real, SetChartLayoutCommand via ChartDataLabelsPlanner) -------------------------

    private async Task ShowChartDataLabelsDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Data Labels", out var chart))
            return;

        var current = ChartDataLabelsPlanner.Read(chart);
        var result = await ShowChartDataLabelsDialogAsync(current);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart("Data Labels", out chart))
            return;

        ApplyChartLayout("Data Labels", chart, ChartDataLabelsPlanner.Plan(edited));
    }

    private async Task<ChartDataLabelsInput?> ShowChartDataLabelsDialogAsync(ChartDataLabelsInput current)
    {
        var showCheck = new CheckBox { Content = UiText.Get("ChartDataLabels_Show"), IsChecked = current.ShowDataLabels };
        AutomationProperties.SetAutomationId(showCheck, "ChartDataLabelsShowCheck");

        var positionChoices = ChartDataLabelsPlanner.GetPositionChoices();
        var positionCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = positionChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartDataLabelPositionChoice.DisplayName)),
        };
        AutomationProperties.SetName(positionCombo, "Data label position");
        AutomationProperties.SetAutomationId(positionCombo, "ChartDataLabelsPositionCombo");
        positionCombo.SelectedItem =
            positionChoices.FirstOrDefault(c => c.Position == current.Position)
            ?? (positionChoices.Count > 0 ? positionChoices[0] : null);

        var valueCheck = new CheckBox { Content = UiText.Get("ChartDataLabels_Value"), IsChecked = current.ShowValue };
        AutomationProperties.SetAutomationId(valueCheck, "ChartDataLabelsValueCheck");
        var categoryCheck = new CheckBox { Content = UiText.Get("ChartDataLabels_CategoryName"), IsChecked = current.ShowCategoryName };
        AutomationProperties.SetAutomationId(categoryCheck, "ChartDataLabelsCategoryCheck");
        var seriesCheck = new CheckBox { Content = UiText.Get("ChartDataLabels_SeriesName"), IsChecked = current.ShowSeriesName };
        AutomationProperties.SetAutomationId(seriesCheck, "ChartDataLabelsSeriesCheck");
        var percentCheck = new CheckBox { Content = UiText.Get("ChartDataLabels_Percentage"), IsChecked = current.ShowPercentage };
        AutomationProperties.SetAutomationId(percentCheck, "ChartDataLabelsPercentageCheck");
        var legendKeyCheck = new CheckBox { Content = UiText.Get("ChartDataLabels_LegendKey"), IsChecked = current.ShowLegendKey };
        AutomationProperties.SetAutomationId(legendKeyCheck, "ChartDataLabelsLegendKeyCheck");

        var dialog = NewChartDialog(UiText.Get("ChartDataLabels_Title"), "ChartDataLabelsDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartDataLabels");
        okButton.Click += (_, _) =>
        {
            var position = positionCombo.SelectedItem is ChartDataLabelPositionChoice picked
                ? picked.Position
                : ChartDataLabelPosition.BestFit;
            dialog.Close((ChartDataLabelsInput?)new ChartDataLabelsInput(
                showCheck.IsChecked == true,
                position,
                valueCheck.IsChecked == true,
                categoryCheck.IsChecked == true,
                seriesCheck.IsChecked == true,
                percentCheck.IsChecked == true,
                legendKeyCheck.IsChecked == true));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartDataLabelsInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                showCheck,
                new TextBlock { Text = UiText.Get("ChartDataLabels_PositionLabel") },
                positionCombo,
                new TextBlock { Text = UiText.Get("ChartDataLabels_ContainsLabel"), Margin = new Thickness(0, 6, 0, 0) },
                valueCheck,
                categoryCheck,
                seriesCheck,
                percentCheck,
                legendKeyCheck,
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartDataLabelsInput?>(this);
    }

    // ---- Format Axis (real, SetChartLayoutCommand via ChartAxisPlanner) -------------------------------

    private Task ShowChartXAxisFormatDialog() => ShowChartAxisFormatDialog(useXAxis: true, "X Axis");

    private Task ShowChartYAxisFormatDialog() => ShowChartAxisFormatDialog(useXAxis: false, "Y Axis");

    private async Task ShowChartAxisFormatDialog(bool useXAxis, string commandLabel)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart(commandLabel, out var chart))
            return;

        if (!ChartAxisPlanner.SupportsAxes(chart.Type))
        {
            RefreshShell("This chart type has no axes to format.");
            return;
        }

        var current = ChartAxisPlanner.Read(chart, useXAxis);
        var result = await ShowChartAxisFormatDialogAsync(current, commandLabel);
        if (result is not { } edited)
            return;

        var error = ChartAxisPlanner.Validate(edited);
        if (error is not null)
        {
            RefreshShell(error);
            return;
        }

        if (!TryGetSelectedChart(commandLabel, out chart))
            return;

        ApplyChartLayout(commandLabel, chart, ChartAxisPlanner.Plan(edited));
    }

    private async Task<ChartAxisInput?> ShowChartAxisFormatDialogAsync(ChartAxisInput current, string commandLabel)
    {
        var minimumBox = new TextBox { Text = FormatNullableDouble(current.Minimum), Width = 260, PlaceholderText = "Auto" };
        AutomationProperties.SetName(minimumBox, "Axis minimum");
        AutomationProperties.SetAutomationId(minimumBox, "ChartAxisMinimumBox");
        var maximumBox = new TextBox { Text = FormatNullableDouble(current.Maximum), Width = 260, PlaceholderText = "Auto" };
        AutomationProperties.SetName(maximumBox, "Axis maximum");
        AutomationProperties.SetAutomationId(maximumBox, "ChartAxisMaximumBox");
        var majorUnitBox = new TextBox { Text = FormatNullableDouble(current.MajorUnit), Width = 260, PlaceholderText = "Auto" };
        AutomationProperties.SetName(majorUnitBox, "Axis major unit");
        AutomationProperties.SetAutomationId(majorUnitBox, "ChartAxisMajorUnitBox");

        var logCheck = new CheckBox { Content = UiText.Get("ChartAxis_LogScale"), IsChecked = current.LogScale };
        AutomationProperties.SetAutomationId(logCheck, "ChartAxisLogScaleCheck");

        var numberFormatChoices = ChartAxisPlanner.GetNumberFormatChoices();
        var numberFormatCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = numberFormatChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartAxisNumberFormatChoice.DisplayName)),
        };
        AutomationProperties.SetName(numberFormatCombo, "Axis number format");
        AutomationProperties.SetAutomationId(numberFormatCombo, "ChartAxisNumberFormatCombo");
        numberFormatCombo.SelectedItem =
            numberFormatChoices.FirstOrDefault(c => c.NumberFormat == current.NumberFormat)
            ?? (numberFormatChoices.Count > 0 ? numberFormatChoices[0] : null);

        var majorGridCheck = new CheckBox { Content = UiText.Get("ChartAxis_ShowMajorGridlines"), IsChecked = current.ShowMajorGridlines };
        AutomationProperties.SetAutomationId(majorGridCheck, "ChartAxisMajorGridlinesCheck");
        var minorGridCheck = new CheckBox { Content = UiText.Get("ChartAxis_ShowMinorGridlines"), IsChecked = current.ShowMinorGridlines };
        AutomationProperties.SetAutomationId(minorGridCheck, "ChartAxisMinorGridlinesCheck");

        var dialog = NewChartDialog($"Format {commandLabel}", "ChartAxisFormatDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartAxisFormat");
        okButton.Click += (_, _) =>
        {
            if (!TryParseAutoDouble(minimumBox.Text, out var minimum)
                || !TryParseAutoDouble(maximumBox.Text, out var maximum)
                || !TryParseAutoDouble(majorUnitBox.Text, out var majorUnit))
            {
                RefreshShell("Enter a number or leave blank for Auto.");
                return;
            }

            var numberFormat = numberFormatCombo.SelectedItem is ChartAxisNumberFormatChoice picked
                ? picked.NumberFormat
                : ChartDataLabelNumberFormat.General;
            dialog.Close((ChartAxisInput?)new ChartAxisInput(
                current.UseXAxis,
                minimum,
                maximum,
                majorUnit,
                logCheck.IsChecked == true,
                numberFormat,
                majorGridCheck.IsChecked == true,
                minorGridCheck.IsChecked == true));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartAxisInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                new TextBlock { Text = UiText.Get("ChartAxis_MinimumLabel") },
                minimumBox,
                new TextBlock { Text = UiText.Get("ChartAxis_MaximumLabel") },
                maximumBox,
                new TextBlock { Text = UiText.Get("ChartAxis_MajorUnitLabel") },
                majorUnitBox,
                logCheck,
                new TextBlock { Text = UiText.Get("ChartAxis_NumberFormatLabel"), Margin = new Thickness(0, 6, 0, 0) },
                numberFormatCombo,
                majorGridCheck,
                minorGridCheck,
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartAxisInput?>(this);
    }

    // ---- Format Series (real, SetChartLayoutCommand via ChartSeriesFormatPlanner) ---------------------

    private async Task ShowChartSeriesFormatDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Format Series", out var chart))
            return;

        if (ChartTypeSupport.GetDataSeriesCount(chart) <= 0)
        {
            RefreshShell("This chart has no data series to format.");
            return;
        }

        var seriesCount = ChartSeriesFormatPlanner.GetSeriesCount(chart);
        var current = ChartSeriesFormatPlanner.Read(chart, 0);
        var result = await ShowChartSeriesFormatDialogAsync(chart, seriesCount, current);
        if (result is not { } edited)
            return;

        var error = ChartSeriesFormatPlanner.Validate(edited);
        if (error is not null)
        {
            RefreshShell(error);
            return;
        }

        if (!TryGetSelectedChart("Format Series", out chart))
            return;

        ApplyChartLayout("Format Series", chart, ChartSeriesFormatPlanner.Plan(chart, edited));
    }

    private async Task<ChartSeriesFormatInput?> ShowChartSeriesFormatDialogAsync(
        ChartModel chart,
        int seriesCount,
        ChartSeriesFormatInput current)
    {
        var seriesNames = Enumerable.Range(0, seriesCount).Select(i => $"Series {i + 1}").ToArray();
        var seriesCombo = new ComboBox { Width = 260, ItemsSource = seriesNames };
        AutomationProperties.SetName(seriesCombo, "Series");
        AutomationProperties.SetAutomationId(seriesCombo, "ChartSeriesFormatSeriesCombo");
        seriesCombo.SelectedIndex = Math.Clamp(current.SeriesIndex, 0, seriesCount - 1);

        // Per-series edit state, re-read from the chart whenever the chosen series changes so the dialog
        // shows each series' own format. Color buttons open the shared More Colors picker.
        var state = current;

        var fillButton = new Button { Content = DescribeColor(UiText.Get("ChartSeries_FillColor"),current.FillColor), Width = 260 };
        AutomationProperties.SetAutomationId(fillButton, "ChartSeriesFormatFillButton");
        var strokeButton = new Button { Content = DescribeColor(UiText.Get("ChartSeries_LineColor"),current.StrokeColor), Width = 260 };
        AutomationProperties.SetAutomationId(strokeButton, "ChartSeriesFormatLineButton");

        var strokeThicknessBox = new TextBox { Text = FormatNullableDouble(current.StrokeThickness), Width = 260, PlaceholderText = "Auto" };
        AutomationProperties.SetName(strokeThicknessBox, "Line width");
        AutomationProperties.SetAutomationId(strokeThicknessBox, "ChartSeriesFormatLineWidthBox");

        var markerChoices = new List<string> { "(None)" };
        markerChoices.AddRange(Enum.GetNames<ChartMarkerStyle>());
        var markerCombo = new ComboBox { Width = 260, ItemsSource = markerChoices };
        AutomationProperties.SetName(markerCombo, "Marker style");
        AutomationProperties.SetAutomationId(markerCombo, "ChartSeriesFormatMarkerCombo");

        var markerSizeBox = new TextBox { Text = FormatNullableDouble(current.MarkerSize), Width = 260, PlaceholderText = "Auto" };
        AutomationProperties.SetName(markerSizeBox, "Marker size");
        AutomationProperties.SetAutomationId(markerSizeBox, "ChartSeriesFormatMarkerSizeBox");

        void LoadState(ChartSeriesFormatInput value)
        {
            state = value;
            fillButton.Content = DescribeColor(UiText.Get("ChartSeries_FillColor"),value.FillColor);
            strokeButton.Content = DescribeColor(UiText.Get("ChartSeries_LineColor"),value.StrokeColor);
            strokeThicknessBox.Text = FormatNullableDouble(value.StrokeThickness);
            markerCombo.SelectedItem = value.MarkerStyle is { } m ? m.ToString() : "(None)";
            markerSizeBox.Text = FormatNullableDouble(value.MarkerSize);
        }

        LoadState(current);

        seriesCombo.SelectionChanged += (_, _) =>
        {
            var index = Math.Max(0, seriesCombo.SelectedIndex);
            LoadState(ChartSeriesFormatPlanner.Read(chart, index));
        };

        fillButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(UiText.Get("ChartSeries_FillColorDialogTitle"), state.FillColor ?? ChartCycleBlue);
            if (chosen is { } color)
            {
                state = state with { FillColor = color };
                fillButton.Content = DescribeColor(UiText.Get("ChartSeries_FillColor"),color);
            }
        };
        strokeButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(UiText.Get("ChartSeries_LineColorDialogTitle"), state.StrokeColor ?? ChartCycleBlue);
            if (chosen is { } color)
            {
                state = state with { StrokeColor = color };
                strokeButton.Content = DescribeColor(UiText.Get("ChartSeries_LineColor"),color);
            }
        };

        var dialog = NewChartDialog(UiText.Get("ChartSeries_Title"), "ChartSeriesFormatDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartSeriesFormat");
        okButton.Click += (_, _) =>
        {
            if (!TryParseAutoDouble(strokeThicknessBox.Text, out var thickness)
                || !TryParseAutoDouble(markerSizeBox.Text, out var markerSize))
            {
                RefreshShell("Enter a number or leave blank for Auto.");
                return;
            }

            var marker = markerCombo.SelectedItem is string name && Enum.TryParse<ChartMarkerStyle>(name, out var parsed)
                ? (ChartMarkerStyle?)parsed
                : null;

            dialog.Close((ChartSeriesFormatInput?)new ChartSeriesFormatInput(
                Math.Max(0, seriesCombo.SelectedIndex),
                state.FillColor,
                state.StrokeColor,
                thickness,
                marker,
                markerSize));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartSeriesFormatInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                new TextBlock { Text = UiText.Get("ChartSeries_SeriesLabel") },
                seriesCombo,
                new TextBlock { Text = UiText.Get("ChartSeries_FillAndLineLabel"), Margin = new Thickness(0, 6, 0, 0) },
                fillButton,
                strokeButton,
                new TextBlock { Text = UiText.Get("ChartSeries_LineWidthLabel") },
                strokeThicknessBox,
                new TextBlock { Text = UiText.Get("ChartSeries_MarkerLabel") },
                markerCombo,
                new TextBlock { Text = UiText.Get("ChartSeries_MarkerSizeLabel") },
                markerSizeBox,
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartSeriesFormatInput?>(this);
    }

    // ---- Trendline (real, SetChartLayoutCommand via ChartTrendlinePlanner) ----------------------------

    private async Task ShowChartTrendlineDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Trendline", out var chart))
            return;

        if (!ChartTrendlinePlanner.SupportsTrendlines(chart.Type))
        {
            RefreshShell("Trendlines are available on column, line, bar, scatter, bubble and area charts.");
            return;
        }

        var current = ChartTrendlinePlanner.Read(chart);
        var result = await ShowChartTrendlineDialogAsync(current);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart("Trendline", out chart))
            return;

        ApplyChartLayout("Trendline", chart, ChartTrendlinePlanner.Plan(edited));
    }

    private async Task<ChartTrendlineInput?> ShowChartTrendlineDialogAsync(ChartTrendlineInput current)
    {
        var showCheck = new CheckBox { Content = UiText.Get("ChartTrendline_Show"), IsChecked = current.ShowTrendline };
        AutomationProperties.SetAutomationId(showCheck, "ChartTrendlineShowCheck");

        var typeChoices = ChartTrendlinePlanner.GetTypeChoices();
        var typeCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = typeChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartTrendlineTypeChoice.DisplayName)),
        };
        AutomationProperties.SetName(typeCombo, "Trendline type");
        AutomationProperties.SetAutomationId(typeCombo, "ChartTrendlineTypeCombo");
        typeCombo.SelectedItem =
            typeChoices.FirstOrDefault(c => c.Type == current.Type)
            ?? (typeChoices.Count > 0 ? typeChoices[0] : null);

        var periodBox = new TextBox { Text = current.Period.ToString(CultureInfo.InvariantCulture), Width = 260 };
        AutomationProperties.SetName(periodBox, "Moving average period");
        AutomationProperties.SetAutomationId(periodBox, "ChartTrendlinePeriodBox");
        var orderBox = new TextBox { Text = current.Order.ToString(CultureInfo.InvariantCulture), Width = 260 };
        AutomationProperties.SetName(orderBox, "Polynomial order");
        AutomationProperties.SetAutomationId(orderBox, "ChartTrendlineOrderBox");

        var equationCheck = new CheckBox { Content = UiText.Get("ChartTrendline_ShowEquation"), IsChecked = current.ShowEquation };
        AutomationProperties.SetAutomationId(equationCheck, "ChartTrendlineEquationCheck");
        var rSquaredCheck = new CheckBox { Content = UiText.Get("ChartTrendline_ShowRSquared"), IsChecked = current.ShowRSquared };
        AutomationProperties.SetAutomationId(rSquaredCheck, "ChartTrendlineRSquaredCheck");

        var dialog = NewChartDialog(UiText.Get("ChartTrendline_Title"), "ChartTrendlineDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartTrendline");
        okButton.Click += (_, _) =>
        {
            if (!TryParseIntInRange(periodBox.Text, ChartTrendlinePlanner.MinPeriod, ChartTrendlinePlanner.MaxPeriod, out var period))
            {
                RefreshShell($"Enter a moving-average period between {ChartTrendlinePlanner.MinPeriod} and {ChartTrendlinePlanner.MaxPeriod}.");
                return;
            }

            if (!TryParseIntInRange(orderBox.Text, ChartTrendlinePlanner.MinOrder, ChartTrendlinePlanner.MaxOrder, out var order))
            {
                RefreshShell($"Enter a polynomial order between {ChartTrendlinePlanner.MinOrder} and {ChartTrendlinePlanner.MaxOrder}.");
                return;
            }

            var type = typeCombo.SelectedItem is ChartTrendlineTypeChoice picked ? picked.Type : ChartTrendlineType.Linear;
            dialog.Close((ChartTrendlineInput?)new ChartTrendlineInput(
                showCheck.IsChecked == true,
                type,
                period,
                order,
                equationCheck.IsChecked == true,
                rSquaredCheck.IsChecked == true));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartTrendlineInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                showCheck,
                new TextBlock { Text = UiText.Get("ChartTrendline_TypeLabel") },
                typeCombo,
                new TextBlock { Text = UiText.Get("ChartTrendline_PeriodLabel") },
                periodBox,
                new TextBlock { Text = UiText.Get("ChartTrendline_OrderLabel") },
                orderBox,
                equationCheck,
                rSquaredCheck,
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartTrendlineInput?>(this);
    }

    // ---- Error Bars (real, SetChartLayoutCommand via ChartErrorBarsPlanner) ---------------------------

    private async Task ShowChartErrorBarsDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Error Bars", out var chart))
            return;

        if (!ChartErrorBarsPlanner.SupportsErrorBars(chart.Type))
        {
            RefreshShell("Error bars are available on column, line, bar, scatter, bubble and area charts.");
            return;
        }

        var current = ChartErrorBarsPlanner.Read(chart);
        var result = await ShowChartErrorBarsDialogAsync(current);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart("Error Bars", out chart))
            return;

        ApplyChartLayout("Error Bars", chart, ChartErrorBarsPlanner.Plan(edited));
    }

    private async Task<ChartErrorBarsInput?> ShowChartErrorBarsDialogAsync(ChartErrorBarsInput current)
    {
        var showCheck = new CheckBox { Content = UiText.Get("ChartErrorBars_Show"), IsChecked = current.ShowErrorBars };
        AutomationProperties.SetAutomationId(showCheck, "ChartErrorBarsShowCheck");

        var kindChoices = ChartErrorBarsPlanner.GetKindChoices();
        var kindCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = kindChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartErrorBarKindChoice.DisplayName)),
        };
        AutomationProperties.SetName(kindCombo, "Error amount");
        AutomationProperties.SetAutomationId(kindCombo, "ChartErrorBarsKindCombo");
        kindCombo.SelectedItem =
            kindChoices.FirstOrDefault(c => c.Kind == current.Kind)
            ?? (kindChoices.Count > 0 ? kindChoices[0] : null);

        var directionChoices = ChartErrorBarsPlanner.GetDirectionChoices();
        var directionCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = directionChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartErrorBarDirectionChoice.DisplayName)),
        };
        AutomationProperties.SetName(directionCombo, "Error bar direction");
        AutomationProperties.SetAutomationId(directionCombo, "ChartErrorBarsDirectionCombo");
        directionCombo.SelectedItem =
            directionChoices.FirstOrDefault(c => c.Direction == current.Direction)
            ?? (directionChoices.Count > 0 ? directionChoices[0] : null);

        var valueBox = new TextBox { Text = current.Value.ToString(CultureInfo.InvariantCulture), Width = 260 };
        AutomationProperties.SetName(valueBox, "Error bar amount");
        AutomationProperties.SetAutomationId(valueBox, "ChartErrorBarsValueBox");

        var endCapsCheck = new CheckBox { Content = UiText.Get("ChartErrorBars_EndCaps"), IsChecked = current.EndCaps };
        AutomationProperties.SetAutomationId(endCapsCheck, "ChartErrorBarsEndCapsCheck");

        var dialog = NewChartDialog(UiText.Get("ChartErrorBars_Title"), "ChartErrorBarsDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartErrorBars");
        okButton.Click += (_, _) =>
        {
            if (!TryParseAutoDouble(valueBox.Text, out var value) || value is not { } amount
                || amount < ChartErrorBarsPlanner.MinValue || amount > ChartErrorBarsPlanner.MaxValue)
            {
                RefreshShell($"Enter an error-bar amount between {ChartErrorBarsPlanner.MinValue} and {ChartErrorBarsPlanner.MaxValue}.");
                return;
            }

            var kind = kindCombo.SelectedItem is ChartErrorBarKindChoice pickedKind ? pickedKind.Kind : ChartErrorBarKind.StandardError;
            var direction = directionCombo.SelectedItem is ChartErrorBarDirectionChoice pickedDir ? pickedDir.Direction : ChartErrorBarDirection.Both;
            dialog.Close((ChartErrorBarsInput?)new ChartErrorBarsInput(
                showCheck.IsChecked == true,
                kind,
                direction,
                amount,
                endCapsCheck.IsChecked == true));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartErrorBarsInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                showCheck,
                new TextBlock { Text = UiText.Get("ChartErrorBars_KindLabel") },
                kindCombo,
                new TextBlock { Text = UiText.Get("ChartErrorBars_DirectionLabel") },
                directionCombo,
                new TextBlock { Text = UiText.Get("ChartErrorBars_ValueLabel") },
                valueBox,
                endCapsCheck,
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartErrorBarsInput?>(this);
    }

    // ---- Shared dialog plumbing -----------------------------------------------------------------------

    private static Window NewChartDialog(string title, string automationId)
    {
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, automationId);
        return dialog;
    }

    private static (Button Ok, Button Cancel, StackPanel Row) CreateChartDialogButtons(string idPrefix)
    {
        var okButton = new Button { Content = UiText.Get("Common_Ok"), Width = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(okButton, $"{idPrefix}OkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, $"{idPrefix}CancelButton");
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { okButton, cancelButton },
        };
        return (okButton, cancelButton, row);
    }

    private static string FormatNullableDouble(double? value) =>
        value is { } v ? v.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static string DescribeColor(string label, CellColor? color) =>
        color is { } c
            ? $"{label}: #{c.R:X2}{c.G:X2}{c.B:X2}"
            : $"{label}: (default)";

    /// <summary>Parses a number, treating blank/whitespace as "auto" (null). Returns false only on bad text.</summary>
    private static bool TryParseAutoDouble(string? text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryParseIntInRange(string? text, int min, int max, out int value)
    {
        value = min;
        return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value >= min
            && value <= max;
    }
}
