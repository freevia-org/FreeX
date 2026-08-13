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
        var command = ChartWorkflowCommandCatalog.FormatBarColumn;
        if (!TryGetSelectedChart(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            RefreshUnsupportedChartWorkflow(command);
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

        if (!TryGetSelectedChart(command, out chart))
            return;

        ApplyChartLayout(command, chart, ChartBarFormatPlanner.Plan(edited));
    }

    private async Task<ChartBarFormatInput?> ShowChartBarFormatDialogAsync(ChartBarFormatInput current)
    {
        var gapWidthField = ChartBarFormatPlanner.GetDialogField(ChartBarFormatDialogFieldId.GapWidth);
        var overlapField = ChartBarFormatPlanner.GetDialogField(ChartBarFormatDialogFieldId.Overlap);

        var gapWidthBox = CreateChartTextBox(current.BarGapWidth.ToString(CultureInfo.InvariantCulture), 260);
        ApplyTypeFormatDescriptorAutomation(gapWidthBox, gapWidthField.LabelResourceKey, gapWidthField.AutomationId);
        var overlapBox = CreateChartTextBox(current.BarOverlap.ToString(CultureInfo.InvariantCulture), 260);
        ApplyTypeFormatDescriptorAutomation(overlapBox, overlapField.LabelResourceKey, overlapField.AutomationId);

        var dialog = NewChartDialog(UiText.Get(ChartBarFormatPlanner.TitleResourceKey), ChartBarFormatPlanner.DialogAutomationId);

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartBarFormat");
        okButton.Click += (_, _) =>
        {
            if (!ChartBarFormatPlanner.TryParseDialogInput(
                    gapWidthBox.Text ?? string.Empty,
                    overlapBox.Text ?? string.Empty,
                    out var input,
                    out var issue))
            {
                RefreshShell(ChartValidationPresentationPlanner.Describe(issue).Message.Resolve(UiText.Get, UiText.Format));
                return;
            }

            dialog.Close((ChartBarFormatInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartBarFormatInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                TypeFormatDescriptorLabel(gapWidthField.LabelResourceKey),
                gapWidthBox,
                TypeFormatDescriptorLabel(overlapField.LabelResourceKey),
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
        var command = ChartWorkflowCommandCatalog.FormatPieDoughnut;
        if (!TryGetSelectedChart(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            RefreshUnsupportedChartWorkflow(command);
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

        if (!TryGetSelectedChart(command, out chart))
            return;

        ApplyChartLayout(command, chart, ChartPieFormatPlanner.Plan(edited));
    }

    private async Task<ChartPieFormatInput?> ShowChartPieFormatDialogAsync(ChartPieFormatInput current, bool isDoughnut)
    {
        var angleField = ChartPieFormatPlanner.GetDialogField(ChartPieFormatDialogFieldId.FirstSliceAngle);
        var explodedIndexField = ChartPieFormatPlanner.GetDialogField(ChartPieFormatDialogFieldId.ExplodedSliceIndex);
        var explodedDistanceField = ChartPieFormatPlanner.GetDialogField(ChartPieFormatDialogFieldId.ExplodedSliceDistance);
        var holeField = ChartPieFormatPlanner.GetDialogField(ChartPieFormatDialogFieldId.DoughnutHoleSize);

        var angleBox = CreateChartTextBox(current.FirstSliceAngle.ToString(CultureInfo.InvariantCulture), 260);
        ApplyTypeFormatDescriptorAutomation(angleBox, angleField.LabelResourceKey, angleField.AutomationId);
        var explodedIndexBox = CreateChartTextBox(current.ExplodedSliceIndex.ToString(CultureInfo.InvariantCulture), 260);
        ApplyTypeFormatDescriptorAutomation(explodedIndexBox, explodedIndexField.LabelResourceKey, explodedIndexField.AutomationId);
        var explodedDistBox = CreateChartTextBox(
            ChartPieFormatPlanner.ToDisplayPercent(current.ExplodedSliceDistance).ToString(CultureInfo.InvariantCulture),
            260);
        ApplyTypeFormatDescriptorAutomation(explodedDistBox, explodedDistanceField.LabelResourceKey, explodedDistanceField.AutomationId);
        var holeBox = CreateChartTextBox(
            ChartPieFormatPlanner.ToDisplayPercent(current.DoughnutHoleSize).ToString(CultureInfo.InvariantCulture),
            260);
        ApplyTypeFormatDescriptorAutomation(holeBox, holeField.LabelResourceKey, holeField.AutomationId);

        var dialog = NewChartDialog(UiText.Get(ChartPieFormatPlanner.TitleResourceKey), ChartPieFormatPlanner.DialogAutomationId);

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartPieFormat");
        okButton.Click += (_, _) =>
        {
            if (!ChartPieFormatPlanner.TryParseDialogInput(
                    angleBox.Text ?? string.Empty,
                    explodedIndexBox.Text ?? string.Empty,
                    explodedDistBox.Text ?? string.Empty,
                    holeBox.Text ?? string.Empty,
                    isDoughnut,
                    out var input,
                    out var issue))
            {
                RefreshShell(ChartValidationPresentationPlanner.Describe(issue).Message.Resolve(UiText.Get, UiText.Format));
                return;
            }

            dialog.Close((ChartPieFormatInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartPieFormatInput?)null);

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8, MinWidth = 300 };
        panel.Children.Add(TypeFormatDescriptorLabel(angleField.LabelResourceKey));
        panel.Children.Add(angleBox);
        panel.Children.Add(TypeFormatDescriptorLabel(explodedIndexField.LabelResourceKey));
        panel.Children.Add(explodedIndexBox);
        panel.Children.Add(TypeFormatDescriptorLabel(explodedDistanceField.LabelResourceKey));
        panel.Children.Add(explodedDistBox);
        if (isDoughnut)
        {
            panel.Children.Add(TypeFormatDescriptorLabel(holeField.LabelResourceKey));
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
        var command = ChartWorkflowCommandCatalog.FormatBubbleChart;
        if (!TryGetSelectedChart(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            RefreshUnsupportedChartWorkflow(command);
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

        if (!TryGetSelectedChart(command, out chart))
            return;

        ApplyChartLayout(command, chart, ChartBubbleFormatPlanner.Plan(edited));
    }

    private async Task<ChartBubbleFormatInput?> ShowChartBubbleFormatDialogAsync(ChartBubbleFormatInput current)
    {
        var scaleField = ChartBubbleFormatPlanner.GetDialogField(ChartBubbleFormatDialogFieldId.BubbleScale);
        var negativeField = ChartBubbleFormatPlanner.GetDialogField(ChartBubbleFormatDialogFieldId.ShowNegativeBubbles);
        var sizeField = ChartBubbleFormatPlanner.GetDialogField(ChartBubbleFormatDialogFieldId.SizeRepresents);

        var scaleBox = CreateChartTextBox(current.BubbleScale.ToString(CultureInfo.InvariantCulture), 260);
        ApplyTypeFormatDescriptorAutomation(scaleBox, scaleField.LabelResourceKey, scaleField.AutomationId);

        var negativeCheck = CreateChartCheckBox(
            TypeFormatDescriptorText(negativeField.LabelResourceKey),
            current.ShowNegativeBubbles);
        ApplyTypeFormatDescriptorAutomation(negativeCheck, negativeField.LabelResourceKey, negativeField.AutomationId);

        var sizeChoices = ChartBubbleFormatPlanner.GetSizeRepresentsChoices();
        var sizeCombo = CreateChartComboBox(260, sizeChoices);
        ApplyTypeFormatDescriptorAutomation(sizeCombo, sizeField.LabelResourceKey, sizeField.AutomationId);
        sizeCombo.SelectedItem = current.BubbleSizeRepresents;

        var dialog = NewChartDialog(UiText.Get(ChartBubbleFormatPlanner.TitleResourceKey), ChartBubbleFormatPlanner.DialogAutomationId);

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartBubbleFormat");
        okButton.Click += (_, _) =>
        {
            var sizeRepresents = sizeCombo.SelectedItem is ChartBubbleSizeRepresents picked
                ? picked
                : (ChartBubbleSizeRepresents?)null;
            if (!ChartBubbleFormatPlanner.TryParseDialogInput(
                    scaleBox.Text ?? string.Empty,
                    negativeCheck.IsChecked == true,
                    sizeRepresents,
                    out var input,
                    out var issue))
            {
                RefreshShell(ChartValidationPresentationPlanner.Describe(issue).Message.Resolve(UiText.Get, UiText.Format));
                return;
            }

            dialog.Close((ChartBubbleFormatInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartBubbleFormatInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                TypeFormatDescriptorLabel(scaleField.LabelResourceKey),
                scaleBox,
                negativeCheck,
                TypeFormatDescriptorLabel(sizeField.LabelResourceKey),
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
        var command = ChartWorkflowCommandCatalog.FormatStockChart;
        if (!TryGetSelectedChart(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            RefreshUnsupportedChartWorkflow(command);
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

        if (!TryGetSelectedChart(command, out chart))
            return;

        ApplyChartLayout(command, chart, ChartStockFormatPlanner.Plan(edited));
    }

    private async Task<ChartStockFormatInput?> ShowChartStockFormatDialogAsync(ChartStockFormatInput current)
    {
        var state = current;
        var gapWidthField = ChartStockFormatPlanner.GetDialogField(ChartStockFormatDialogFieldId.GapWidth);
        var upFillField = ChartStockFormatPlanner.GetDialogField(ChartStockFormatDialogFieldId.UpBarFill);
        var upBorderField = ChartStockFormatPlanner.GetDialogField(ChartStockFormatDialogFieldId.UpBarBorder);
        var downFillField = ChartStockFormatPlanner.GetDialogField(ChartStockFormatDialogFieldId.DownBarFill);
        var downBorderField = ChartStockFormatPlanner.GetDialogField(ChartStockFormatDialogFieldId.DownBarBorder);
        var highLowColorField = ChartStockFormatPlanner.GetDialogField(ChartStockFormatDialogFieldId.HighLowLineColor);
        var thicknessField = ChartStockFormatPlanner.GetDialogField(ChartStockFormatDialogFieldId.HighLowLineThickness);

        var gapWidthBox = CreateChartTextBox(current.UpDownBarGapWidth.ToString(CultureInfo.InvariantCulture), 260);
        ApplyTypeFormatDescriptorAutomation(gapWidthBox, gapWidthField.LabelResourceKey, gapWidthField.AutomationId);

        var upFillButton = ColorPickerButton(upFillField, current.UpBarFillColor);
        var upBorderButton = ColorPickerButton(upBorderField, current.UpBarBorderColor);
        var downFillButton = ColorPickerButton(downFillField, current.DownBarFillColor);
        var downBorderButton = ColorPickerButton(downBorderField, current.DownBarBorderColor);
        var highLowButton = ColorPickerButton(highLowColorField, current.HighLowLineColor);

        var thicknessBox = CreateChartTextBox(current.HighLowLineThickness.ToString("G", CultureInfo.InvariantCulture), 260);
        ApplyTypeFormatDescriptorAutomation(thicknessBox, thicknessField.LabelResourceKey, thicknessField.AutomationId);

        upFillButton.Click += async (_, _) =>
        {
            var label = TypeFormatDescriptorText(upFillField.LabelResourceKey);
            var chosen = await ShowMoreColorsDialogAsync(
                label,
                state.UpBarFillColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color) { state = state with { UpBarFillColor = color }; upFillButton.Content = DescribeColor(label, color); }
        };
        upBorderButton.Click += async (_, _) =>
        {
            var label = TypeFormatDescriptorText(upBorderField.LabelResourceKey);
            var chosen = await ShowMoreColorsDialogAsync(
                label,
                state.UpBarBorderColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color) { state = state with { UpBarBorderColor = color }; upBorderButton.Content = DescribeColor(label, color); }
        };
        downFillButton.Click += async (_, _) =>
        {
            var label = TypeFormatDescriptorText(downFillField.LabelResourceKey);
            var chosen = await ShowMoreColorsDialogAsync(
                label,
                state.DownBarFillColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color) { state = state with { DownBarFillColor = color }; downFillButton.Content = DescribeColor(label, color); }
        };
        downBorderButton.Click += async (_, _) =>
        {
            var label = TypeFormatDescriptorText(downBorderField.LabelResourceKey);
            var chosen = await ShowMoreColorsDialogAsync(
                label,
                state.DownBarBorderColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color) { state = state with { DownBarBorderColor = color }; downBorderButton.Content = DescribeColor(label, color); }
        };
        highLowButton.Click += async (_, _) =>
        {
            var label = TypeFormatDescriptorText(highLowColorField.LabelResourceKey);
            var chosen = await ShowMoreColorsDialogAsync(
                label,
                state.HighLowLineColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color) { state = state with { HighLowLineColor = color }; highLowButton.Content = DescribeColor(label, color); }
        };

        var dialog = NewChartDialog(UiText.Get(ChartStockFormatPlanner.TitleResourceKey), ChartStockFormatPlanner.DialogAutomationId);

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartStockFormat");
        okButton.Click += (_, _) =>
        {
            if (!ChartStockFormatPlanner.TryParseDialogInput(
                    gapWidthBox.Text ?? string.Empty,
                    state.UpBarFillColor,
                    state.UpBarBorderColor,
                    state.DownBarFillColor,
                    state.DownBarBorderColor,
                    state.HighLowLineColor,
                    thicknessBox.Text ?? string.Empty,
                    out var input,
                    out var issue))
            {
                RefreshShell(ChartValidationPresentationPlanner.Describe(issue).Message.Resolve(UiText.Get, UiText.Format));
                return;
            }

            dialog.Close((ChartStockFormatInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartStockFormatInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                TypeFormatDescriptorLabel(gapWidthField.LabelResourceKey),
                gapWidthBox,
                TypeFormatDescriptorLabel(ChartStockFormatPlanner.BarsGroupResourceKey, new Thickness(0, 6, 0, 0)),
                upFillButton,
                upBorderButton,
                downFillButton,
                downBorderButton,
                TypeFormatDescriptorLabel(ChartStockFormatPlanner.HighLowGroupResourceKey, new Thickness(0, 6, 0, 0)),
                highLowButton,
                TypeFormatDescriptorLabel(thicknessField.LabelResourceKey),
                thicknessBox,
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartStockFormatInput?>(this);
    }

    private static string TypeFormatDescriptorText(string resourceKey) =>
        StripDisplayMnemonic(UiText.Get(resourceKey));

    private static TextBlock TypeFormatDescriptorLabel(string resourceKey, Thickness? margin = null) =>
        new()
        {
            Text = TypeFormatDescriptorText(resourceKey),
            FontSize = 12,
            Margin = margin ?? default,
        };

    private static void ApplyTypeFormatDescriptorAutomation(Control control, string labelResourceKey, string automationId)
    {
        AutomationProperties.SetName(control, TypeFormatDescriptorText(labelResourceKey));
        AutomationProperties.SetAutomationId(control, automationId);
    }

    /// <summary>Builds a 260-wide color-picker button labelled by a shared descriptor, showing the current color.</summary>
    private static Button ColorPickerButton(ChartStockFormatDialogFieldDescriptor field, CellColor? color)
    {
        var label = TypeFormatDescriptorText(field.LabelResourceKey);
        var button = CreateChartButton(DescribeColor(label, color), 260);
        ApplyTypeFormatDescriptorAutomation(button, field.LabelResourceKey, field.AutomationId);
        return button;
    }
}
