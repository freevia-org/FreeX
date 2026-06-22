using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// The three remaining chart contextual-tab dialogs for the cross-platform shell: Combo Chart (per-series
/// line overlay + secondary-axis assignment), Move Chart (to a new sheet or an existing sheet), and Format
/// Chart Area (chart-area / plot-area fill and border). Each opens a compact modal, hands the input to the
/// matching portable planner (<see cref="ChartComboPlanner"/>, <see cref="ChartMovePlanner"/>,
/// <see cref="ChartAreaFormatPlanner"/>) which validates and projects it, then drives the existing Core
/// commands (<see cref="SetChartLayoutCommand"/>, <see cref="MoveChartCommand"/>,
/// <see cref="MoveChartToNewSheetCommand"/>). The WPF host's <c>MoveChartDialog</c> /
/// <c>ChartAreaLegendDialog</c> / combo command are the behavior reference.
/// </summary>
public sealed partial class MainWindow
{
    // ---- Combo Chart (real, SetChartLayoutCommand via ChartComboPlanner) ------------------------------

    private async Task ShowChartComboDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Combo Chart", out var chart))
            return;

        if (!ChartComboPlanner.SupportsCombo(chart))
        {
            RefreshShell(UiText.Get("ChartLoc_ComboChartsNeed"));
            return;
        }

        var current = ChartComboPlanner.Read(chart);
        var result = await ShowChartComboDialogAsync(current);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart("Combo Chart", out chart))
            return;

        ApplyChartLayout("Combo Chart", chart, ChartComboPlanner.Plan(edited));
    }

    private async Task<ChartComboInput?> ShowChartComboDialogAsync(ChartComboInput current)
    {
        // One row per series: a label, a "Plot as line" checkbox and a "Secondary axis" checkbox. Series 0
        // is the base plot type (Excel anchors it) so its checkboxes are disabled.
        var lineChecks = new List<CheckBox>(current.Series.Count);
        var secondaryChecks = new List<CheckBox>(current.Series.Count);

        var rows = new StackPanel { Spacing = 6 };
        foreach (var series in current.Series)
        {
            var isBase = series.SeriesIndex == 0;

            var lineCheck = new CheckBox
            {
                Content = UiText.Get("ChartCombo_AsLine"),
                IsChecked = series.AsLine,
                IsEnabled = !isBase,
            };
            AutomationProperties.SetAutomationId(lineCheck, $"ChartComboLineCheck{series.SeriesIndex}");
            lineChecks.Add(lineCheck);

            var secondaryCheck = new CheckBox
            {
                Content = UiText.Get("ChartCombo_SecondaryAxis"),
                IsChecked = series.OnSecondaryAxis,
                IsEnabled = !isBase,
            };
            AutomationProperties.SetAutomationId(secondaryCheck, $"ChartComboSecondaryCheck{series.SeriesIndex}");
            secondaryChecks.Add(secondaryCheck);

            rows.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = string.Format(CultureInfo.CurrentCulture, UiText.Get("ChartCombo_SeriesRow"), series.SeriesIndex + 1),
                        Width = 90,
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    },
                    lineCheck,
                    secondaryCheck,
                },
            });
        }

        var dialog = NewChartDialog(UiText.Get("ChartCombo_Title"), "ChartComboDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartCombo");
        okButton.Click += (_, _) =>
        {
            var edited = new List<ChartComboSeriesInput>(current.Series.Count);
            for (var index = 0; index < current.Series.Count; index++)
            {
                edited.Add(new ChartComboSeriesInput(
                    current.Series[index].SeriesIndex,
                    lineChecks[index].IsChecked == true,
                    secondaryChecks[index].IsChecked == true));
            }

            dialog.Close((ChartComboInput?)new ChartComboInput(edited));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartComboInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 320,
            Children =
            {
                new TextBlock { Text = UiText.Get("ChartCombo_Instruction") },
                rows,
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartComboInput?>(this);
    }

    // ---- Move Chart (real, MoveChartCommand / MoveChartToNewSheetCommand via ChartMovePlanner) --------

    private async Task ShowMoveChartDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Move Chart", out var chart))
            return;

        var current = ChartMovePlanner.DefaultFor(_session.ActiveSheet.Name);
        var result = await ShowMoveChartDialogAsync(current);
        if (result is not { } edited)
            return;

        var plan = ChartMovePlanner.Plan(edited, name => _session.Workbook.GetSheet(name) is not null);
        if (!plan.IsValid)
        {
            RefreshShell(plan.Error ?? UiText.Get("ChartLoc_MoveChartFailed"));
            return;
        }

        if (!TryGetSelectedChart("Move Chart", out chart))
            return;

        if (plan.TargetKind == ChartMoveTargetKind.NewSheet)
        {
            var commandResult = _session.ExecuteReviewCommand(
                new MoveChartToNewSheetCommand(_session.ActiveSheet.Id, chart.Id, plan.TargetName));
            if (!commandResult.Success)
            {
                RefreshShell(commandResult.ErrorMessage ?? UiText.Get("ChartLoc_MoveChartFailed"));
                return;
            }

            ClearSelectedDrawingObject();
            if (_session.Workbook.GetSheet(plan.TargetName) is { } createdSheet)
                _session.SelectSheet(createdSheet.Id);
            RefreshShell(UiText.Format("ChartLoc_MovedChartToNewSheet", plan.TargetName));
            return;
        }

        var targetSheet = _session.Workbook.GetSheet(plan.TargetName);
        if (targetSheet is null)
        {
            RefreshShell(UiText.Format("ChartLoc_NoSheetNamed", plan.TargetName));
            return;
        }

        var moveResult = _session.ExecuteReviewCommand(
            new MoveChartCommand(_session.ActiveSheet.Id, chart.Id, targetSheet.Id));
        if (!moveResult.Success)
        {
            RefreshShell(moveResult.ErrorMessage ?? UiText.Get("ChartLoc_MoveChartFailed"));
            return;
        }

        ClearSelectedDrawingObject();
        _session.SelectSheet(targetSheet.Id);
        RefreshShell(UiText.Format("ChartLoc_MovedChartTo", plan.TargetName));
    }

    private async Task<ChartMoveInput?> ShowMoveChartDialogAsync(ChartMoveInput current)
    {
        var objectRadio = new RadioButton
        {
            Content = UiText.Get("MoveChart_ObjectInSheet"),
            GroupName = "MoveChartTarget",
            IsChecked = current.TargetKind == ChartMoveTargetKind.ObjectInSheet,
        };
        AutomationProperties.SetAutomationId(objectRadio, "MoveChartObjectRadio");

        var newSheetRadio = new RadioButton
        {
            Content = UiText.Get("MoveChart_NewChartSheet"),
            GroupName = "MoveChartTarget",
            IsChecked = current.TargetKind == ChartMoveTargetKind.NewSheet,
        };
        AutomationProperties.SetAutomationId(newSheetRadio, "MoveChartNewSheetRadio");

        var targetBox = new TextBox { Text = current.TargetName, Width = 260 };
        AutomationProperties.SetName(targetBox, "Move chart target sheet");
        AutomationProperties.SetAutomationId(targetBox, "MoveChartTargetBox");

        var dialog = NewChartDialog(UiText.Get("MoveChart_Title"), "MoveChartDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("MoveChart");
        okButton.Click += (_, _) =>
        {
            var kind = newSheetRadio.IsChecked == true ? ChartMoveTargetKind.NewSheet : ChartMoveTargetKind.ObjectInSheet;
            dialog.Close((ChartMoveInput?)new ChartMoveInput(kind, targetBox.Text ?? string.Empty));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartMoveInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                objectRadio,
                newSheetRadio,
                new TextBlock { Text = UiText.Get("MoveChart_TargetNameLabel"), Margin = new Thickness(0, 6, 0, 0) },
                targetBox,
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartMoveInput?>(this);
    }

    // ---- Format Chart Area (real, SetChartLayoutCommand via ChartAreaFormatPlanner) -------------------

    private async Task ShowFormatChartAreaDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Format Chart Area", out var chart))
            return;

        var current = ChartAreaFormatPlanner.Read(chart);
        var result = await ShowFormatChartAreaDialogAsync(current);
        if (result is not { } edited)
            return;

        var error = ChartAreaFormatPlanner.Validate(edited);
        if (error is not null)
        {
            RefreshShell(error);
            return;
        }

        if (!TryGetSelectedChart("Format Chart Area", out chart))
            return;

        ApplyChartLayout("Format Chart Area", chart, ChartAreaFormatPlanner.Plan(edited));
    }

    private async Task<ChartAreaFormatInput?> ShowFormatChartAreaDialogAsync(ChartAreaFormatInput current)
    {
        // Per-color edit state so each picker button updates its own field and label.
        var state = current;

        var chartAreaButton = new Button { Content = DescribeColor(UiText.Get("ChartArea_ChartAreaFill"), current.ChartAreaFillColor), Width = 260 };
        AutomationProperties.SetAutomationId(chartAreaButton, "ChartAreaFillButton");
        var plotAreaButton = new Button { Content = DescribeColor(UiText.Get("ChartArea_PlotAreaFill"), current.PlotAreaFillColor), Width = 260 };
        AutomationProperties.SetAutomationId(plotAreaButton, "ChartAreaPlotFillButton");
        var plotBorderButton = new Button { Content = DescribeColor(UiText.Get("ChartArea_PlotAreaBorder"), current.PlotAreaBorderColor), Width = 260 };
        AutomationProperties.SetAutomationId(plotBorderButton, "ChartAreaPlotBorderButton");

        var borderWidthBox = new TextBox
        {
            Text = current.PlotAreaBorderThickness.ToString(CultureInfo.InvariantCulture),
            Width = 260,
        };
        AutomationProperties.SetName(borderWidthBox, "Plot area border width");
        AutomationProperties.SetAutomationId(borderWidthBox, "ChartAreaPlotBorderWidthBox");

        chartAreaButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(UiText.Get("ChartArea_ChartAreaFill"), state.ChartAreaFillColor ?? ChartCycleBlue);
            if (chosen is { } color)
            {
                state = state with { ChartAreaFillColor = color };
                chartAreaButton.Content = DescribeColor(UiText.Get("ChartArea_ChartAreaFill"), color);
            }
        };
        plotAreaButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(UiText.Get("ChartArea_PlotAreaFill"), state.PlotAreaFillColor ?? ChartCycleBlue);
            if (chosen is { } color)
            {
                state = state with { PlotAreaFillColor = color };
                plotAreaButton.Content = DescribeColor(UiText.Get("ChartArea_PlotAreaFill"), color);
            }
        };
        plotBorderButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(UiText.Get("ChartArea_PlotAreaBorder"), state.PlotAreaBorderColor ?? ChartCycleBlue);
            if (chosen is { } color)
            {
                state = state with { PlotAreaBorderColor = color };
                plotBorderButton.Content = DescribeColor(UiText.Get("ChartArea_PlotAreaBorder"), color);
            }
        };

        var dialog = NewChartDialog(UiText.Get("ChartArea_Title"), "FormatChartAreaDialog");
        dialog.Width = 340;
        dialog.Height = 330;
        dialog.MinWidth = 340;
        dialog.MinHeight = 330;
        dialog.MaxWidth = 340;
        dialog.MaxHeight = 330;

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("FormatChartArea");
        okButton.Click += (_, _) =>
        {
            if (!double.TryParse((borderWidthBox.Text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                || !double.IsFinite(width))
            {
                RefreshShell(UiText.Format("ChartLoc_EnterPlotAreaBorderWidth", ChartAreaFormatPlanner.MinBorderThickness, ChartAreaFormatPlanner.MaxBorderThickness));
                return;
            }

            dialog.Close((ChartAreaFormatInput?)new ChartAreaFormatInput(
                state.ChartAreaFillColor,
                state.PlotAreaFillColor,
                state.PlotAreaBorderColor,
                width));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartAreaFormatInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                new TextBlock { Text = UiText.Get("ChartArea_FillLabel") },
                chartAreaButton,
                plotAreaButton,
                new TextBlock { Text = UiText.Get("ChartArea_BorderLabel"), Margin = new Thickness(0, 6, 0, 0) },
                plotBorderButton,
                new TextBlock { Text = UiText.Get("ChartArea_BorderWidthLabel") },
                borderWidthBox,
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartAreaFormatInput?>(this);
    }
}
