using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// The type-specific "Current Selection ▸ Format" chart dialogs for the cross-platform shell: Format
/// Bar/Column (gap width + overlap), Format Pie/Doughnut (first-slice angle, exploded slice, hole size),
/// Format Bubble Chart (scale, negative bubbles, size-represents) and Format Stock Chart (up/down-bar gap +
/// colors, high-low line). Each opens a compact modal, hands the input to the matching portable planner
/// (<see cref="ChartBarFormatPlanner"/>, <see cref="ChartPieFormatPlanner"/>,
/// <see cref="ChartBubbleFormatPlanner"/>, <see cref="ChartStockFormatPlanner"/>) which validates and projects
/// it onto a <see cref="ChartLayoutOptions"/> applied through the shared <see cref="SetChartLayoutCommand"/>
/// via <see cref="ApplyChartLayout"/>, then re-resolves the selected chart after closing. The WPF host's
/// <c>ChartBarFormatDialog</c> / <c>ChartPieFormatDialog</c> / <c>ChartBubbleFormatDialog</c> /
/// <c>ChartStockFormatDialog</c> are the behavior reference. Shared dialog plumbing (NewChartDialog,
/// CreateChartDialogButtons, DescribeColor, ShowMoreColorsDialogAsync) lives in MainWindow.ChartFormatDialogs.
/// </summary>
public sealed partial class MainWindow
{
    // ---- Format Bar/Column (real, SetChartLayoutCommand via ChartBarFormatPlanner) --------------------

    private async Task ShowChartBarFormatDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Format Bar/Column", out var chart))
            return;

        if (!ChartBarFormatPlanner.Supports(chart))
        {
            RefreshShell(UiText.Get("ChartLoc_GapWidthOverlapAvailableOn"));
            return;
        }

        var current = ChartBarFormatPlanner.Read(chart);
        var result = await ShowChartBarFormatDialogAsync(current);
        if (result is not { } edited)
            return;

        var error = ChartBarFormatPlanner.Validate(edited);
        if (error is not null)
        {
            RefreshShell(error);
            return;
        }

        if (!TryGetSelectedChart("Format Bar/Column", out chart))
            return;

        ApplyChartLayout("Format Bar/Column", chart, ChartBarFormatPlanner.Plan(edited));
    }

    private async Task<ChartBarFormatInput?> ShowChartBarFormatDialogAsync(ChartBarFormatInput current)
    {
        var gapWidthBox = new TextBox { Text = current.BarGapWidth.ToString(CultureInfo.InvariantCulture), Width = 260 };
        AutomationProperties.SetName(gapWidthBox, "Gap width");
        AutomationProperties.SetAutomationId(gapWidthBox, "ChartBarFormatGapWidthBox");
        var overlapBox = new TextBox { Text = current.BarOverlap.ToString(CultureInfo.InvariantCulture), Width = 260 };
        AutomationProperties.SetName(overlapBox, "Series overlap");
        AutomationProperties.SetAutomationId(overlapBox, "ChartBarFormatOverlapBox");

        var dialog = NewChartDialog(UiText.Get("ChartFmt_BarTitle"), "ChartBarFormatDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartBarFormat");
        okButton.Click += (_, _) =>
        {
            if (!TryParseIntInRange(gapWidthBox.Text, ChartBarFormatPlanner.MinGapWidth, ChartBarFormatPlanner.MaxGapWidth, out var gapWidth)
                || !TryParseIntInRange(overlapBox.Text, ChartBarFormatPlanner.MinOverlap, ChartBarFormatPlanner.MaxOverlap, out var overlap))
            {
                RefreshShell(UiText.Get("ChartLoc_EnterWholeNumbersGapOverlap"));
                return;
            }

            dialog.Close((ChartBarFormatInput?)new ChartBarFormatInput(gapWidth, overlap));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartBarFormatInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                new TextBlock { Text = UiText.Get("ChartFmt_BarGapWidthLabel") },
                gapWidthBox,
                new TextBlock { Text = UiText.Get("ChartFmt_BarOverlapLabel") },
                overlapBox,
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartBarFormatInput?>(this);
    }

    // ---- Format Pie/Doughnut (real, SetChartLayoutCommand via ChartPieFormatPlanner) ------------------

    private async Task ShowChartPieFormatDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Format Pie/Doughnut", out var chart))
            return;

        if (!ChartPieFormatPlanner.Supports(chart))
        {
            RefreshShell(UiText.Get("ChartLoc_OptionsAvailablePieDoughnut"));
            return;
        }

        var current = ChartPieFormatPlanner.Read(chart);
        var result = await ShowChartPieFormatDialogAsync(current, ChartPieFormatPlanner.SupportsHoleSize(chart));
        if (result is not { } edited)
            return;

        var error = ChartPieFormatPlanner.Validate(edited);
        if (error is not null)
        {
            RefreshShell(error);
            return;
        }

        if (!TryGetSelectedChart("Format Pie/Doughnut", out chart))
            return;

        ApplyChartLayout("Format Pie/Doughnut", chart, ChartPieFormatPlanner.Plan(edited));
    }

    private async Task<ChartPieFormatInput?> ShowChartPieFormatDialogAsync(ChartPieFormatInput current, bool isDoughnut)
    {
        var angleBox = new TextBox { Text = current.FirstSliceAngle.ToString(CultureInfo.InvariantCulture), Width = 260 };
        AutomationProperties.SetName(angleBox, "First slice angle");
        AutomationProperties.SetAutomationId(angleBox, "ChartPieFormatAngleBox");
        var explodedIndexBox = new TextBox { Text = current.ExplodedSliceIndex.ToString(CultureInfo.InvariantCulture), Width = 260 };
        AutomationProperties.SetName(explodedIndexBox, "Exploded slice index");
        AutomationProperties.SetAutomationId(explodedIndexBox, "ChartPieFormatExplodedIndexBox");
        var explodedDistBox = new TextBox
        {
            Text = ((int)Math.Round(current.ExplodedSliceDistance * 100)).ToString(CultureInfo.InvariantCulture),
            Width = 260,
        };
        AutomationProperties.SetName(explodedDistBox, "Exploded slice distance percent");
        AutomationProperties.SetAutomationId(explodedDistBox, "ChartPieFormatExplodedDistanceBox");
        var holeBox = new TextBox
        {
            Text = ((int)Math.Round(current.DoughnutHoleSize * 100)).ToString(CultureInfo.InvariantCulture),
            Width = 260,
        };
        AutomationProperties.SetName(holeBox, "Doughnut hole size percent");
        AutomationProperties.SetAutomationId(holeBox, "ChartPieFormatHoleSizeBox");

        var dialog = NewChartDialog(UiText.Get("ChartFmt_PieTitle"), "ChartPieFormatDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartPieFormat");
        okButton.Click += (_, _) =>
        {
            if (!TryParseIntInRange(angleBox.Text, ChartPieFormatPlanner.MinFirstSliceAngle, ChartPieFormatPlanner.MaxFirstSliceAngle, out var angle))
            {
                RefreshShell(UiText.Format("ChartLoc_EnterFirstSliceAngle", ChartPieFormatPlanner.MinFirstSliceAngle, ChartPieFormatPlanner.MaxFirstSliceAngle));
                return;
            }

            if (!int.TryParse((explodedIndexBox.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var explodedIndex))
            {
                RefreshShell(UiText.Get("ChartLoc_EnterExplodedSliceIndex"));
                return;
            }

            if (!TryParseIntInRange(explodedDistBox.Text, 0, 50, out var explodedDistPct))
            {
                RefreshShell(UiText.Get("ChartLoc_EnterExplodedSliceDistance"));
                return;
            }

            var holePct = (int)Math.Round(current.DoughnutHoleSize * 100);
            if (isDoughnut && !TryParseIntInRange(holeBox.Text, 10, 90, out holePct))
            {
                RefreshShell(UiText.Get("ChartLoc_EnterDoughnutHoleSize"));
                return;
            }

            dialog.Close((ChartPieFormatInput?)new ChartPieFormatInput(
                angle,
                explodedIndex,
                explodedDistPct / 100.0,
                holePct / 100.0));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartPieFormatInput?)null);

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8, MinWidth = 300 };
        panel.Children.Add(new TextBlock { Text = UiText.Get("ChartFmt_PieFirstSliceAngleLabel") });
        panel.Children.Add(angleBox);
        panel.Children.Add(new TextBlock { Text = UiText.Get("ChartFmt_PieExplodedIndexLabel") });
        panel.Children.Add(explodedIndexBox);
        panel.Children.Add(new TextBlock { Text = UiText.Get("ChartFmt_PieExplodedDistanceLabel") });
        panel.Children.Add(explodedDistBox);
        if (isDoughnut)
        {
            panel.Children.Add(new TextBlock { Text = UiText.Get("ChartFmt_PieHoleSizeLabel") });
            panel.Children.Add(holeBox);
        }

        panel.Children.Add(buttonRow);
        dialog.Content = panel;

        return await dialog.ShowDialog<ChartPieFormatInput?>(this);
    }

    // ---- Format Bubble Chart (real, SetChartLayoutCommand via ChartBubbleFormatPlanner) ---------------

    private async Task ShowChartBubbleFormatDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Format Bubble Chart", out var chart))
            return;

        if (!ChartBubbleFormatPlanner.Supports(chart))
        {
            RefreshShell(UiText.Get("ChartLoc_OptionsAvailableBubble"));
            return;
        }

        var current = ChartBubbleFormatPlanner.Read(chart);
        var result = await ShowChartBubbleFormatDialogAsync(current);
        if (result is not { } edited)
            return;

        var error = ChartBubbleFormatPlanner.Validate(edited);
        if (error is not null)
        {
            RefreshShell(error);
            return;
        }

        if (!TryGetSelectedChart("Format Bubble Chart", out chart))
            return;

        ApplyChartLayout("Format Bubble Chart", chart, ChartBubbleFormatPlanner.Plan(edited));
    }

    private async Task<ChartBubbleFormatInput?> ShowChartBubbleFormatDialogAsync(ChartBubbleFormatInput current)
    {
        var scaleBox = new TextBox { Text = current.BubbleScale.ToString(CultureInfo.InvariantCulture), Width = 260 };
        AutomationProperties.SetName(scaleBox, "Bubble scale");
        AutomationProperties.SetAutomationId(scaleBox, "ChartBubbleFormatScaleBox");

        var negativeCheck = new CheckBox { Content = UiText.Get("ChartFmt_BubbleShowNegative"), IsChecked = current.ShowNegativeBubbles };
        AutomationProperties.SetAutomationId(negativeCheck, "ChartBubbleFormatNegativeCheck");

        var sizeChoices = ChartBubbleFormatPlanner.GetSizeRepresentsChoices();
        var sizeCombo = new ComboBox { Width = 260, ItemsSource = sizeChoices };
        AutomationProperties.SetName(sizeCombo, "Bubble size represents");
        AutomationProperties.SetAutomationId(sizeCombo, "ChartBubbleFormatSizeCombo");
        sizeCombo.SelectedItem = current.BubbleSizeRepresents;

        var dialog = NewChartDialog(UiText.Get("ChartFmt_BubbleTitle"), "ChartBubbleFormatDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartBubbleFormat");
        okButton.Click += (_, _) =>
        {
            if (!TryParseIntInRange(scaleBox.Text, ChartBubbleFormatPlanner.MinBubbleScale, ChartBubbleFormatPlanner.MaxBubbleScale, out var scale))
            {
                RefreshShell(UiText.Format("ChartLoc_EnterBubbleScale", ChartBubbleFormatPlanner.MinBubbleScale, ChartBubbleFormatPlanner.MaxBubbleScale));
                return;
            }

            var sizeRepresents = sizeCombo.SelectedItem is ChartBubbleSizeRepresents picked ? picked : ChartBubbleSizeRepresents.Area;
            dialog.Close((ChartBubbleFormatInput?)new ChartBubbleFormatInput(scale, negativeCheck.IsChecked == true, sizeRepresents));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartBubbleFormatInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                new TextBlock { Text = UiText.Get("ChartFmt_BubbleScaleLabel") },
                scaleBox,
                negativeCheck,
                new TextBlock { Text = UiText.Get("ChartFmt_BubbleSizeRepresentsLabel") },
                sizeCombo,
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartBubbleFormatInput?>(this);
    }

    // ---- Format Stock Chart (real, SetChartLayoutCommand via ChartStockFormatPlanner) -----------------

    private async Task ShowChartStockFormatDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Format Stock Chart", out var chart))
            return;

        if (!ChartStockFormatPlanner.Supports(chart))
        {
            RefreshShell(UiText.Get("ChartLoc_OptionsAvailableStock"));
            return;
        }

        var current = ChartStockFormatPlanner.Read(chart);
        var result = await ShowChartStockFormatDialogAsync(current);
        if (result is not { } edited)
            return;

        var error = ChartStockFormatPlanner.Validate(edited);
        if (error is not null)
        {
            RefreshShell(error);
            return;
        }

        if (!TryGetSelectedChart("Format Stock Chart", out chart))
            return;

        ApplyChartLayout("Format Stock Chart", chart, ChartStockFormatPlanner.Plan(edited));
    }

    private async Task<ChartStockFormatInput?> ShowChartStockFormatDialogAsync(ChartStockFormatInput current)
    {
        var state = current;

        var gapWidthBox = new TextBox { Text = current.UpDownBarGapWidth.ToString(CultureInfo.InvariantCulture), Width = 260 };
        AutomationProperties.SetName(gapWidthBox, "Up/down bar gap width");
        AutomationProperties.SetAutomationId(gapWidthBox, "ChartStockFormatGapWidthBox");

        var upFillButton = ColorPickerButton("ChartFmt_StockUpBarFill", "ChartStockFormatUpFillButton", current.UpBarFillColor);
        var upBorderButton = ColorPickerButton("ChartFmt_StockUpBarBorder", "ChartStockFormatUpBorderButton", current.UpBarBorderColor);
        var downFillButton = ColorPickerButton("ChartFmt_StockDownBarFill", "ChartStockFormatDownFillButton", current.DownBarFillColor);
        var downBorderButton = ColorPickerButton("ChartFmt_StockDownBarBorder", "ChartStockFormatDownBorderButton", current.DownBarBorderColor);
        var highLowButton = ColorPickerButton("ChartFmt_StockHighLowLineColor", "ChartStockFormatHighLowButton", current.HighLowLineColor);

        var thicknessBox = new TextBox { Text = current.HighLowLineThickness.ToString("G", CultureInfo.InvariantCulture), Width = 260 };
        AutomationProperties.SetName(thicknessBox, "High-low line thickness");
        AutomationProperties.SetAutomationId(thicknessBox, "ChartStockFormatThicknessBox");

        upFillButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(UiText.Get("ChartFmt_StockUpBarFill"), state.UpBarFillColor ?? ChartCycleBlue);
            if (chosen is { } color) { state = state with { UpBarFillColor = color }; upFillButton.Content = DescribeColor(UiText.Get("ChartFmt_StockUpBarFill"), color); }
        };
        upBorderButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(UiText.Get("ChartFmt_StockUpBarBorder"), state.UpBarBorderColor ?? ChartCycleBlue);
            if (chosen is { } color) { state = state with { UpBarBorderColor = color }; upBorderButton.Content = DescribeColor(UiText.Get("ChartFmt_StockUpBarBorder"), color); }
        };
        downFillButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(UiText.Get("ChartFmt_StockDownBarFill"), state.DownBarFillColor ?? ChartCycleBlue);
            if (chosen is { } color) { state = state with { DownBarFillColor = color }; downFillButton.Content = DescribeColor(UiText.Get("ChartFmt_StockDownBarFill"), color); }
        };
        downBorderButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(UiText.Get("ChartFmt_StockDownBarBorder"), state.DownBarBorderColor ?? ChartCycleBlue);
            if (chosen is { } color) { state = state with { DownBarBorderColor = color }; downBorderButton.Content = DescribeColor(UiText.Get("ChartFmt_StockDownBarBorder"), color); }
        };
        highLowButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(UiText.Get("ChartFmt_StockHighLowLineColor"), state.HighLowLineColor ?? ChartCycleBlue);
            if (chosen is { } color) { state = state with { HighLowLineColor = color }; highLowButton.Content = DescribeColor(UiText.Get("ChartFmt_StockHighLowLineColor"), color); }
        };

        var dialog = NewChartDialog(UiText.Get("ChartFmt_StockTitle"), "ChartStockFormatDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartStockFormat");
        okButton.Click += (_, _) =>
        {
            if (!TryParseIntInRange(gapWidthBox.Text, ChartStockFormatPlanner.MinGapWidth, ChartStockFormatPlanner.MaxGapWidth, out var gapWidth))
            {
                RefreshShell(UiText.Format("ChartLoc_EnterUpDownBarGapWidth", ChartStockFormatPlanner.MinGapWidth, ChartStockFormatPlanner.MaxGapWidth));
                return;
            }

            if (!double.TryParse((thicknessBox.Text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var thickness)
                || !double.IsFinite(thickness)
                || thickness < ChartStockFormatPlanner.MinLineThickness
                || thickness > ChartStockFormatPlanner.MaxLineThickness)
            {
                RefreshShell(UiText.Format("ChartLoc_EnterHighLowLineThickness", ChartStockFormatPlanner.MinLineThickness, ChartStockFormatPlanner.MaxLineThickness));
                return;
            }

            dialog.Close((ChartStockFormatInput?)new ChartStockFormatInput(
                gapWidth,
                state.UpBarFillColor,
                state.UpBarBorderColor,
                state.DownBarFillColor,
                state.DownBarBorderColor,
                state.HighLowLineColor,
                thickness));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartStockFormatInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                new TextBlock { Text = UiText.Get("ChartFmt_StockGapWidthLabel") },
                gapWidthBox,
                new TextBlock { Text = UiText.Get("ChartFmt_StockBarsLabel"), Margin = new Thickness(0, 6, 0, 0) },
                upFillButton,
                upBorderButton,
                downFillButton,
                downBorderButton,
                new TextBlock { Text = UiText.Get("ChartFmt_StockHighLowLabel"), Margin = new Thickness(0, 6, 0, 0) },
                highLowButton,
                new TextBlock { Text = UiText.Get("ChartFmt_StockLineThicknessLabel") },
                thicknessBox,
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartStockFormatInput?>(this);
    }

    /// <summary>Builds a 260-wide color-picker button labelled by a UiText key, showing the current color.</summary>
    private static Button ColorPickerButton(string labelKey, string automationId, CellColor? color)
    {
        var button = new Button { Content = DescribeColor(UiText.Get(labelKey), color), Width = 260 };
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }
}
