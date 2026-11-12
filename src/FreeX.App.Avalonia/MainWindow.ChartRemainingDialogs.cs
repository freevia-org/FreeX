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
        var command = ChartWorkflowCommandCatalog.ComboChart;
        if (!TryGetSelectedChart(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            RefreshUnsupportedChartWorkflow(command);
            return;
        }

        var current = ChartComboPlanner.Read(chart);
        var result = await ShowChartComboDialogAsync(current);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart(command, out chart))
            return;

        ApplyChartLayout(command, chart, ChartComboPlanner.Plan(edited));
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
            };
            ApplyChartCheckBoxChrome(lineCheck);
            AutomationProperties.SetAutomationId(lineCheck, $"ChartComboLineCheck{series.SeriesIndex}");
            lineChecks.Add(lineCheck);

            var secondaryCheck = new CheckBox
            {
                Content = UiText.Get("ChartCombo_SecondaryAxis"),
                IsChecked = series.OnSecondaryAxis,
                IsEnabled = !isBase,
                MinHeight = 20,
                MaxHeight = 20,
            };
            ApplyChartCheckBoxChrome(secondaryCheck);
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
        buttonRow.Margin = new Thickness(0, 8, 0, 0);

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
        var command = ChartWorkflowCommandCatalog.MoveChart;
        if (!TryGetSelectedChart(command, out var chart))
            return;

        var current = ChartMovePlanner.DefaultFor(_session.ActiveSheet.Name);
        var result = await ShowMoveChartDialogAsync(current);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart(command, out chart))
            return;

        var movePlan = ChartCommandWorkflowPlanner.PlanMoveCommand(
            _session.Workbook,
            _session.ActiveSheet.Id,
            chart,
            edited);
        if (!movePlan.CanExecute)
        {
            RefreshShell(movePlan.Error ?? UiText.Get("ChartLoc_MoveChartFailed"));
            return;
        }

        var sourceSheetId = _session.ActiveSheet.Id;
        var commandResult = _session.ExecuteReviewCommand(movePlan.Command!);
        if (!commandResult.Success)
        {
            RefreshShell(commandResult.ErrorMessage ?? UiText.Get("ChartLoc_MoveChartFailed"));
            return;
        }

        ClearSelectedDrawingObject();
        var targetSheet = movePlan.ExistingTargetSheetId is { } existingTargetSheetId
            ? _session.Workbook.GetSheet(existingTargetSheetId)
            : _session.Workbook.GetSheet(movePlan.TargetName);
        if (targetSheet is null)
        {
            _session.SelectSheet(sourceSheetId);
            RefreshShell(UiText.Get("ChartLoc_MoveChartFailed"));
            return;
        }

        _session.SelectSheet(targetSheet.Id);
        RefreshShell(UiText.Format(
            movePlan.ExistingTargetSheetId is null
                ? "ChartLoc_MovedChartToNewSheet"
                : "ChartLoc_MovedChartTo",
            movePlan.TargetName));
    }

    private async Task<ChartMoveInput?> ShowMoveChartDialogAsync(ChartMoveInput current)
    {
        var objectTarget = ChartMovePlanner.GetTargetChoices().Single(choice => choice.TargetKind == ChartMoveTargetKind.ObjectInSheet);
        var newSheetTarget = ChartMovePlanner.GetTargetChoices().Single(choice => choice.TargetKind == ChartMoveTargetKind.NewSheet);
        var targetField = ChartMovePlanner.GetTargetNameField();

        var objectRadio = CreateChartRadioButton(
            StripDisplayMnemonic(UiText.Get(objectTarget.LabelResourceKey)),
            ChartMovePlanner.TargetGroupName,
            current.TargetKind == ChartMoveTargetKind.ObjectInSheet);
        AutomationProperties.SetAutomationId(objectRadio, objectTarget.AutomationId);

        var newSheetRadio = CreateChartRadioButton(
            StripDisplayMnemonic(UiText.Get(newSheetTarget.LabelResourceKey)),
            ChartMovePlanner.TargetGroupName,
            current.TargetKind == ChartMoveTargetKind.NewSheet);
        AutomationProperties.SetAutomationId(newSheetRadio, newSheetTarget.AutomationId);

        var targetBox = CreateChartTextBox(current.TargetName, 260);
        AutomationProperties.SetName(targetBox, StripDisplayMnemonic(UiText.Get(targetField.AutomationNameResourceKey!)));
        AutomationProperties.SetAutomationId(targetBox, targetField.AutomationId);
        AutomationProperties.SetHelpText(targetBox, UiText.Get(targetField.HelpResourceKey!));

        var dialog = NewChartDialog(UiText.Get("MoveChart_Title"), ChartMovePlanner.DialogAutomationId);

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("MoveChart");
        okButton.Click += (_, _) =>
        {
            var kind = newSheetRadio.IsChecked == true ? ChartMoveTargetKind.NewSheet : ChartMoveTargetKind.ObjectInSheet;
            dialog.Close((ChartMoveInput?)new ChartMoveInput(kind, targetBox.Text ?? string.Empty));
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartMoveInput?)null);

        // WPF MoveChartDialog (340x210): the second radio carries a 0,4,0,8 margin, the label sits 4px
        // above a full-width name box, and the button row follows the box directly. The content is
        // shorter than the window, so the WPF client height is kept.
        newSheetRadio.Margin = new Thickness(0, 4, 0, 8);
        targetBox.Width = double.NaN;
        targetBox.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;
        HostChartDialogContent(
            dialog,
            340,
            new StackPanel
            {
                Children =
                {
                    objectRadio,
                    newSheetRadio,
                    new TextBlock { Text = StripDisplayMnemonic(UiText.Get(targetField.LabelResourceKey)), FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 0, 0, 4) },
                    targetBox,
                },
            },
            buttonRow,
            fixedWpfHeight: 210);

        return await dialog.ShowDialog<ChartMoveInput?>(this);
    }

    // ---- Format Chart Area (real, SetChartLayoutCommand via ChartAreaFormatPlanner) -------------------

    private async Task ShowFormatChartAreaDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var command = ChartWorkflowCommandCatalog.FormatChartArea;
        if (!TryGetSelectedChart(command, out var chart))
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

        if (!TryGetSelectedChart(command, out chart))
            return;

        ApplyChartLayout(command, chart, ChartAreaFormatPlanner.Plan(edited));
    }

    private async Task<ChartAreaFormatInput?> ShowFormatChartAreaDialogAsync(ChartAreaFormatInput current)
    {
        // Per-color edit state so each picker button updates its own field and label. Layout matches the
        // WPF ChartAreaLegendDialog: two group boxes — "Fill & Line" (chart-area / plot-area fill +
        // border, border width) and "Legend" (show / position / overlay + legend text/fill/border colors,
        // border width, font size) — followed by an [OK][Cancel] row (primary on the left).
        // Nominal editor width; ApplyWpfChartFieldMetrics stretches every editor across its group, as
        // the WPF stack does.
        const int ControlWidth = 300;

        var fillLineSection = ChartAreaFormatPlanner.GetFillLineSection();
        var legendSection = ChartAreaFormatPlanner.GetLegendSection();

        // ---- "Fill & Line" group controls ----------------------------------------------------------
        var chartAreaEditor = MakeAreaColorEditor(ChartAreaFormatDialogFieldId.ChartAreaFillColor, current.ChartAreaFillColor);
        var plotAreaEditor = MakeAreaColorEditor(ChartAreaFormatDialogFieldId.PlotAreaFillColor, current.PlotAreaFillColor);
        var plotBorderEditor = MakeAreaColorEditor(ChartAreaFormatDialogFieldId.PlotAreaBorderColor, current.PlotAreaBorderColor);

        var borderWidthBox = MakeAreaNumberBox(
            ChartAreaFormatDialogFieldId.PlotAreaBorderThickness,
            current.PlotAreaBorderThickness.ToString(CultureInfo.InvariantCulture),
            ControlWidth);

        // "Fill & Line" group box — matches WPF CreateGroupBox(ChartDialog_FillLineGroup, ...) with
        // the inline help paragraph at the top (ChartAreaLegend_FillLineHelpText).
        var fillLineStack = ApplyWpfChartFieldMetrics(new StackPanel
        {
            Children =
            {
                CreateChartDialogInlineHelp(
                    UiText.Get(fillLineSection.HelpResourceKey ?? throw new InvalidOperationException("Fill-line section requires help text."))),
                MakeAreaDescriptorLabel(ChartAreaFormatDialogFieldId.ChartAreaFillColor),
                chartAreaEditor.Row,
                MakeAreaDescriptorLabel(ChartAreaFormatDialogFieldId.PlotAreaFillColor),
                plotAreaEditor.Row,
                MakeAreaDescriptorLabel(ChartAreaFormatDialogFieldId.PlotAreaBorderColor),
                plotBorderEditor.Row,
                MakeAreaDescriptorLabel(ChartAreaFormatDialogFieldId.PlotAreaBorderThickness),
                borderWidthBox,
            },
        });

        var fillLineGroup = MakeAreaDescriptorGroup(fillLineSection, fillLineStack);

        // ---- "Legend" group controls ---------------------------------------------------------------
        var showLegendCheck = MakeAreaDescriptorCheck(ChartAreaFormatDialogFieldId.ShowLegend, current.ShowLegend);

        var positionChoices = ChartAreaFormatPlanner
            .GetLegendPositionChoices()
            .Select(position => new ChartLegendPositionChoice(position, ChartLegendPlanner.DisplayName(position)))
            .ToList();
        var positionCombo = CreateChartComboBox(ControlWidth, positionChoices);
        positionCombo.DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartLegendPositionChoice.DisplayName));
        ApplyAreaDescriptorAutomation(positionCombo, ChartAreaFormatDialogFieldId.LegendPosition);
        positionCombo.SelectedItem =
            positionChoices.FirstOrDefault(c => c.Position == current.LegendPosition)
            ?? (positionChoices.Count > 0 ? positionChoices[0] : null);

        var overlayCheck = MakeAreaDescriptorCheck(ChartAreaFormatDialogFieldId.LegendOverlay, current.LegendOverlay);

        var legendTextEditor = MakeAreaColorEditor(ChartAreaFormatDialogFieldId.LegendTextColor, current.LegendTextColor);
        var legendFillEditor = MakeAreaColorEditor(ChartAreaFormatDialogFieldId.LegendFillColor, current.LegendFillColor);
        var legendBorderEditor = MakeAreaColorEditor(ChartAreaFormatDialogFieldId.LegendBorderColor, current.LegendBorderColor);

        var legendBorderWidthBox = MakeAreaNumberBox(
            ChartAreaFormatDialogFieldId.LegendBorderThickness,
            current.LegendBorderThickness.ToString(CultureInfo.InvariantCulture),
            ControlWidth);
        var legendFontSizeBox = MakeAreaNumberBox(
            ChartAreaFormatDialogFieldId.LegendFontSize,
            current.LegendFontSize.ToString(CultureInfo.InvariantCulture),
            ControlWidth);

        var legendStack = ApplyWpfChartFieldMetrics(new StackPanel
        {
            Children =
            {
                showLegendCheck,
                MakeAreaDescriptorLabel(ChartAreaFormatDialogFieldId.LegendPosition),
                positionCombo,
                overlayCheck,
                MakeAreaDescriptorLabel(ChartAreaFormatDialogFieldId.LegendTextColor),
                legendTextEditor.Row,
                MakeAreaDescriptorLabel(ChartAreaFormatDialogFieldId.LegendFillColor),
                legendFillEditor.Row,
                MakeAreaDescriptorLabel(ChartAreaFormatDialogFieldId.LegendBorderColor),
                legendBorderEditor.Row,
                MakeAreaDescriptorLabel(ChartAreaFormatDialogFieldId.LegendBorderThickness),
                legendBorderWidthBox,
                MakeAreaDescriptorLabel(ChartAreaFormatDialogFieldId.LegendFontSize),
                legendFontSizeBox,
            },
        });

        var legendGroup = MakeAreaDescriptorGroup(legendSection, legendStack);

        // Dialog title matches the WPF ChartAreaLegendDialog ("Format Chart Area").
        // WPF ChartAreaLegendDialog focuses the chart-area fill editor on load. Use the matching
        // Avalonia color control explicitly so nested scroll content does not lose the initial focus.
        var dialog = NewChartDialog(UiText.Get("ChartAreaLegend_Title"), "FormatChartAreaDialog", chartAreaEditor.Picker);
        // Shared explicit size so the headless parity capture (which reads dialog.Bounds verbatim) matches
        // the WPF chart-area dialog contract.
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Width = ChartAreaFormatPlanner.DialogWidth;
        dialog.Height = ChartAreaFormatPlanner.DialogHeight;

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("FormatChartArea");
        okButton.Click += (_, _) =>
        {
            var chosenPosition = positionCombo.SelectedItem is ChartLegendPositionChoice picked
                ? picked.Position
                : (ChartLegendPosition?)null;

            if (!ChartAreaFormatPlanner.TryParseDialogInput(
                    chartAreaEditor.Text,
                    plotAreaEditor.Text,
                    plotBorderEditor.Text,
                    borderWidthBox.Text,
                    showLegendCheck.IsChecked == true,
                    chosenPosition,
                    overlayCheck.IsChecked == true,
                    legendTextEditor.Text,
                    legendFillEditor.Text,
                    legendBorderEditor.Text,
                    legendBorderWidthBox.Text,
                    legendFontSizeBox.Text,
                    out var input,
                    out var issue))
            {
                RefreshShell(ChartValidationPresentationPlanner.Describe(issue).Message.Resolve(UiText.Get, UiText.Format));
                return;
            }

            dialog.Close((ChartAreaFormatInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartAreaFormatInput?)null);

        // The two group boxes stacked in a scroll viewer so the taller (legend) content stays reachable,
        // matching the WPF 420×590 task-pane dialog.
        var bodyStack = new StackPanel
        {
            Spacing = 0,
            Children = { fillLineGroup, legendGroup },
        };

        var scrollContent = new ScrollViewer
        {
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = bodyStack,
        };
        Grid.SetRow(scrollContent, 0);
        Grid.SetRow(buttonRow, 1);
        buttonRow.Margin = new Thickness(0, ChartDialogScrollButtonGap, 0, 0);

        var contentGrid = new Grid
        {
            Margin = new Thickness(ChartDialogMargin),
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { scrollContent, buttonRow },
        };

        dialog.Content = contentGrid;
        ApplyChartDialogTypography(dialog);

        return await dialog.ShowDialog<ChartAreaFormatInput?>(this);

        ChartAreaFormatDialogFieldDescriptor AreaField(ChartAreaFormatDialogFieldId fieldId) =>
            ChartAreaFormatPlanner.GetDialogField(fieldId);

        string AreaFieldLabel(ChartAreaFormatDialogFieldId fieldId) =>
            StripDisplayMnemonic(UiText.Get(AreaField(fieldId).LabelResourceKey));

        void ApplyAreaDescriptorAutomation(Control control, ChartAreaFormatDialogFieldId fieldId)
        {
            var descriptor = AreaField(fieldId);
            AutomationProperties.SetName(control, AreaFieldLabel(fieldId));
            AutomationProperties.SetAutomationId(control, descriptor.AutomationId);
            if (descriptor.HelpResourceKey is { } helpKey)
                AutomationProperties.SetHelpText(control, UiText.Get(helpKey));
        }

        CheckBox MakeAreaDescriptorCheck(ChartAreaFormatDialogFieldId fieldId, bool isChecked)
        {
            var checkBox = CreateChartCheckBox(AreaFieldLabel(fieldId), isChecked);
            ApplyAreaDescriptorAutomation(checkBox, fieldId);
            return checkBox;
        }

        TextBlock MakeAreaDescriptorLabel(ChartAreaFormatDialogFieldId fieldId) =>
            new()
            {
                Text = AreaFieldLabel(fieldId),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
            };

        ChartColorEditor MakeAreaColorEditor(ChartAreaFormatDialogFieldId fieldId, CellColor? color) =>
            CreateChartColorEditor(AreaFieldLabel(fieldId), AreaField(fieldId).AutomationId, color);

        TextBox MakeAreaNumberBox(ChartAreaFormatDialogFieldId fieldId, string text, double width)
        {
            var box = CreateChartTextBox(text, width);
            ApplyAreaDescriptorAutomation(box, fieldId);
            return box;
        }

        GroupBox MakeAreaDescriptorGroup(ChartAreaFormatDialogSectionDescriptor section, Control content) =>
            CreateChartDialogGroupBox(UiText.Get(section.HeaderResourceKey), content);
    }
}
