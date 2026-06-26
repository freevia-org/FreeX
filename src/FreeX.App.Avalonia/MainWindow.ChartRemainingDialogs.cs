using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

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
                MinHeight = 20,
                MaxHeight = 20,
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
            };
            AutomationProperties.SetAutomationId(lineCheck, $"ChartComboLineCheck{series.SeriesIndex}");
            lineChecks.Add(lineCheck);

            var secondaryCheck = new CheckBox
            {
                Content = UiText.Get("ChartCombo_SecondaryAxis"),
                IsChecked = series.OnSecondaryAxis,
                IsEnabled = !isBase,
                MinHeight = 20,
                MaxHeight = 20,
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
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
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                        VerticalAlignment = AvaloniaVerticalAlignment.Center,
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
                new TextBlock { Text = UiText.Get("ChartCombo_Instruction"), FontSize = 12, FontFamily = FormulaBarFontFamily },
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
            Content = StripDisplayMnemonic(UiText.Get("MoveChart_ObjectInSheet")),
            GroupName = "MoveChartTarget",
            IsChecked = current.TargetKind == ChartMoveTargetKind.ObjectInSheet,
            MinHeight = 20,
            MaxHeight = 20,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(objectRadio, "MoveChartObjectRadio");

        var newSheetRadio = new RadioButton
        {
            Content = StripDisplayMnemonic(UiText.Get("MoveChart_NewChartSheet")),
            GroupName = "MoveChartTarget",
            IsChecked = current.TargetKind == ChartMoveTargetKind.NewSheet,
            MinHeight = 20,
            MaxHeight = 20,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(newSheetRadio, "MoveChartNewSheetRadio");

        var targetBox = new TextBox
        {
            Text = current.TargetName,
            Width = 260,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
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
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("MoveChart_TargetNameLabel")), FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 6, 0, 0) },
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
        // Per-color edit state so each picker button updates its own field and label. Layout matches the
        // WPF ChartAreaLegendDialog: two group boxes — "Fill & Line" (chart-area / plot-area fill +
        // border, border width) and "Legend" (show / position / overlay + legend text/fill/border colors,
        // border width, font size) — followed by an [OK][Cancel] row (primary on the left).
        var state = current;

        // Button width matches WPF inner content area (~380px dialog → 300px control width).
        const int ControlWidth = 300;

        // ---- "Fill & Line" group controls ----------------------------------------------------------
        var chartAreaButton = new Button { Content = DescribeColor(UiText.Get("ChartArea_ChartAreaFill"), current.ChartAreaFillColor), Width = ControlWidth };
        ApplyChartButtonChrome(chartAreaButton, ControlWidth);
        AutomationProperties.SetAutomationId(chartAreaButton, "ChartAreaFillButton");
        var plotAreaButton = new Button { Content = DescribeColor(UiText.Get("ChartArea_PlotAreaFill"), current.PlotAreaFillColor), Width = ControlWidth };
        ApplyChartButtonChrome(plotAreaButton, ControlWidth);
        AutomationProperties.SetAutomationId(plotAreaButton, "ChartAreaPlotFillButton");
        var plotBorderButton = new Button { Content = DescribeColor(UiText.Get("ChartArea_PlotAreaBorder"), current.PlotAreaBorderColor), Width = ControlWidth };
        ApplyChartButtonChrome(plotBorderButton, ControlWidth);
        AutomationProperties.SetAutomationId(plotBorderButton, "ChartAreaPlotBorderButton");

        var borderWidthBox = MakeChartNumberBox(
            current.PlotAreaBorderThickness.ToString(CultureInfo.InvariantCulture),
            ControlWidth,
            "Plot area border width",
            "ChartAreaPlotBorderWidthBox");

        chartAreaButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(
                UiText.Get("ChartArea_ChartAreaFill"),
                state.ChartAreaFillColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color)
            {
                state = state with { ChartAreaFillColor = color };
                chartAreaButton.Content = DescribeColor(UiText.Get("ChartArea_ChartAreaFill"), color);
            }
        };
        plotAreaButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(
                UiText.Get("ChartArea_PlotAreaFill"),
                state.PlotAreaFillColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color)
            {
                state = state with { PlotAreaFillColor = color };
                plotAreaButton.Content = DescribeColor(UiText.Get("ChartArea_PlotAreaFill"), color);
            }
        };
        plotBorderButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(
                UiText.Get("ChartArea_PlotAreaBorder"),
                state.PlotAreaBorderColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color)
            {
                state = state with { PlotAreaBorderColor = color };
                plotBorderButton.Content = DescribeColor(UiText.Get("ChartArea_PlotAreaBorder"), color);
            }
        };

        // "Fill & Line" group box — matches WPF CreateGroupBox(ChartDialog_FillLineGroup, ...) with
        // the inline help paragraph at the top (ChartAreaLegend_FillLineHelpText).
        var fillLineStack = new StackPanel
        {
            Margin = new Thickness(10, 8, 10, 10),
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get("ChartAreaLegend_FillLineHelpText"),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 96)),
                    Margin = new Thickness(0, 0, 0, 4),
                },
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("ChartAreaLegend_ChartAreaFillColorLabel")), FontSize = 12, FontFamily = FormulaBarFontFamily },
                chartAreaButton,
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("ChartAreaLegend_PlotAreaFillColorLabel")), FontSize = 12, FontFamily = FormulaBarFontFamily },
                plotAreaButton,
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("ChartAreaLegend_PlotAreaBorderColorLabel")), FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 4, 0, 0) },
                plotBorderButton,
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("ChartAreaLegend_PlotAreaBorderWidthLabel")), FontSize = 12, FontFamily = FormulaBarFontFamily },
                borderWidthBox,
            },
        };

        var fillLineGroup = new GroupBox
        {
            Header = StripDisplayMnemonic(UiText.Get("ChartDialog_FillLineGroup")),
            Content = fillLineStack,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 10),
        };

        // ---- "Legend" group controls ---------------------------------------------------------------
        var showLegendCheck = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("ChartAreaLegend_ShowLegend")),
            IsChecked = current.ShowLegend,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(showLegendCheck, "ChartAreaShowLegendCheck");

        var positionChoices = ChartLegendPlanner.GetPositionChoices();
        var positionCombo = new ComboBox
        {
            Width = ControlWidth,
            ItemsSource = positionChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartLegendPositionChoice.DisplayName)),
        };
        AutomationProperties.SetName(positionCombo, "Legend position");
        AutomationProperties.SetAutomationId(positionCombo, "ChartAreaLegendPositionCombo");
        ApplyChartComboBoxChrome(positionCombo);
        positionCombo.SelectedItem =
            positionChoices.FirstOrDefault(c => c.Position == current.LegendPosition)
            ?? (positionChoices.Count > 0 ? positionChoices[0] : null);

        var overlayCheck = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("ChartAreaLegend_OverlayLegend")),
            IsChecked = current.LegendOverlay,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(overlayCheck, "ChartAreaLegendOverlayCheck");

        var legendTextButton = new Button { Content = DescribeColor(UiText.Get("ChartAreaLegend_LegendTextColorLabel"), current.LegendTextColor), Width = ControlWidth };
        ApplyChartButtonChrome(legendTextButton, ControlWidth);
        AutomationProperties.SetAutomationId(legendTextButton, "ChartAreaLegendTextColorButton");
        var legendFillButton = new Button { Content = DescribeColor(UiText.Get("ChartAreaLegend_LegendFillColorLabel"), current.LegendFillColor), Width = ControlWidth };
        ApplyChartButtonChrome(legendFillButton, ControlWidth);
        AutomationProperties.SetAutomationId(legendFillButton, "ChartAreaLegendFillColorButton");
        var legendBorderButton = new Button { Content = DescribeColor(UiText.Get("ChartAreaLegend_LegendBorderColorLabel"), current.LegendBorderColor), Width = ControlWidth };
        ApplyChartButtonChrome(legendBorderButton, ControlWidth);
        AutomationProperties.SetAutomationId(legendBorderButton, "ChartAreaLegendBorderColorButton");

        legendTextButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(
                UiText.Get("ChartAreaLegend_LegendTextColorLabel"),
                state.LegendTextColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color)
            {
                state = state with { LegendTextColor = color };
                legendTextButton.Content = DescribeColor(UiText.Get("ChartAreaLegend_LegendTextColorLabel"), color);
            }
        };
        legendFillButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(
                UiText.Get("ChartAreaLegend_LegendFillColorLabel"),
                state.LegendFillColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color)
            {
                state = state with { LegendFillColor = color };
                legendFillButton.Content = DescribeColor(UiText.Get("ChartAreaLegend_LegendFillColorLabel"), color);
            }
        };
        legendBorderButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(
                UiText.Get("ChartAreaLegend_LegendBorderColorLabel"),
                state.LegendBorderColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color)
            {
                state = state with { LegendBorderColor = color };
                legendBorderButton.Content = DescribeColor(UiText.Get("ChartAreaLegend_LegendBorderColorLabel"), color);
            }
        };

        var legendBorderWidthBox = MakeChartNumberBox(
            current.LegendBorderThickness.ToString(CultureInfo.InvariantCulture),
            ControlWidth,
            "Legend border width",
            "ChartAreaLegendBorderWidthBox");
        var legendFontSizeBox = MakeChartNumberBox(
            current.LegendFontSize.ToString(CultureInfo.InvariantCulture),
            ControlWidth,
            "Legend font size",
            "ChartAreaLegendFontSizeBox");

        var legendStack = new StackPanel
        {
            Margin = new Thickness(10, 8, 10, 10),
            Spacing = 6,
            Children =
            {
                showLegendCheck,
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("ChartAreaLegend_LegendPositionLabel")), FontSize = 12, FontFamily = FormulaBarFontFamily },
                positionCombo,
                overlayCheck,
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("ChartAreaLegend_LegendTextColorLabel")), FontSize = 12, FontFamily = FormulaBarFontFamily },
                legendTextButton,
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("ChartAreaLegend_LegendFillColorLabel")), FontSize = 12, FontFamily = FormulaBarFontFamily },
                legendFillButton,
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("ChartAreaLegend_LegendBorderColorLabel")), FontSize = 12, FontFamily = FormulaBarFontFamily },
                legendBorderButton,
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("ChartAreaLegend_LegendBorderWidthLabel")), FontSize = 12, FontFamily = FormulaBarFontFamily },
                legendBorderWidthBox,
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("ChartAreaLegend_LegendFontSizeLabel")), FontSize = 12, FontFamily = FormulaBarFontFamily },
                legendFontSizeBox,
            },
        };

        var legendGroup = new GroupBox
        {
            Header = StripDisplayMnemonic(UiText.Get("ChartAreaLegend_LegendGroup")),
            Content = legendStack,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 10),
        };

        // Dialog title matches the WPF ChartAreaLegendDialog ("Format Chart Area").
        var dialog = NewChartDialog(UiText.Get("ChartAreaLegend_Title"), "FormatChartAreaDialog");
        // Explicit size so the headless parity capture (which reads dialog.Bounds verbatim) shows the full
        // two-group layout + OK/Cancel without clipping and without a stray scrollbar track on the right.
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Width = 432;
        dialog.Height = 760;

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("FormatChartArea");
        okButton.Click += (_, _) =>
        {
            if (!double.TryParse((borderWidthBox.Text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                || !double.IsFinite(width))
            {
                RefreshShell(UiText.Format("ChartLoc_EnterPlotAreaBorderWidth", ChartAreaFormatPlanner.MinBorderThickness, ChartAreaFormatPlanner.MaxBorderThickness));
                return;
            }

            if (!double.TryParse((legendBorderWidthBox.Text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var legendBorderWidth)
                || !double.IsFinite(legendBorderWidth))
            {
                RefreshShell(UiText.Format("ChartLoc_EnterPlotAreaBorderWidth", ChartAreaFormatPlanner.MinBorderThickness, ChartAreaFormatPlanner.MaxBorderThickness));
                return;
            }

            if (!double.TryParse((legendFontSizeBox.Text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var legendFontSize)
                || !double.IsFinite(legendFontSize))
            {
                legendFontSize = ChartAreaFormatPlanner.MinLegendFontSize;
            }

            var chosenPosition = positionCombo.SelectedItem is ChartLegendPositionChoice picked
                ? picked.Position
                : ChartLegendPosition.Right;

            dialog.Close((ChartAreaFormatInput?)(state with
            {
                PlotAreaBorderThickness = width,
                ShowLegend = showLegendCheck.IsChecked == true,
                LegendPosition = chosenPosition,
                LegendOverlay = overlayCheck.IsChecked == true,
                LegendBorderThickness = legendBorderWidth,
                LegendFontSize = legendFontSize,
            }));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartAreaFormatInput?)null);

        // The two group boxes stacked in a scroll viewer so the taller (legend) content stays reachable,
        // matching the WPF 420×590 task-pane dialog.
        var bodyStack = new StackPanel
        {
            Spacing = 0,
            Children = { fillLineGroup, legendGroup },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 0,
            MinWidth = 380,
            Children =
            {
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = bodyStack,
                },
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartAreaFormatInput?>(this);
    }

    /// <summary>A numeric text box with the standard chart-dialog chrome and automation metadata.</summary>
    private TextBox MakeChartNumberBox(string text, double width, string automationName, string automationId)
    {
        var box = new TextBox
        {
            Text = text,
            Width = width,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetName(box, automationName);
        AutomationProperties.SetAutomationId(box, automationId);
        return box;
    }
}
