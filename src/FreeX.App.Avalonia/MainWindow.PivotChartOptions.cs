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
/// Windows-parity PivotChart contextual commands for the Avalonia/macOS shell that operate on the chart bound
/// to the active PivotTable: Change Chart Type (reuses the shared chart-type picker + planner and applies
/// <see cref="ChangePivotChartTypeCommand"/>) and PivotChart Options (a focused dialog over the field-button
/// visibility flags, the data-table toggle + legend keys, and rounded corners, applied through
/// <see cref="ConfigurePivotChartOptionsCommand"/>). Both resolve the PivotChart via
/// <see cref="FindPivotChartForActivePivot"/> (the active pivot's first chart, by name/cache binding — the
/// same match the WPF host's <c>FindPivotChartForPivotTable</c> uses) and report an honest status when the
/// pivot has no chart yet. Mirrors the WPF host's PivotChartChangeTypeBtn / PivotChartOptionsBtn handlers.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Analyze ▸ PivotChart ▸ Change Chart Type — re-types the active pivot's chart.</summary>
    private async Task ChangeActivePivotChartTypeAsync()
    {
        if (!TryResolveActivePivotChart(UiText.Get("PivotChart_ChangeTypeInsertFirst"), out var chart))
            return;

        var chosen = await ShowChartTypePickerAsync(chart!.Type);
        if (chosen is not { } type)
            return;

        var plan = ChartTypeChangePlanner.Plan(chart.Type, type);
        if (!plan.HasChange)
        {
            RefreshShell(plan.Message ?? UiText.Get("PivotChart_ChangeTypeInsertFirst"));
            return;
        }

        // Re-resolve after the dialog in case the selection drifted while it was open.
        if (!TryResolveActivePivotChart(UiText.Get("PivotChart_ChangeTypeInsertFirst"), out chart))
            return;

        var result = _session.ExecuteReviewCommand(
            ChartCommandWorkflowPlanner.BuildChangePivotChartTypeCommand(
                _session.ActiveSheet.Id,
                chart!,
                plan.AppliedType!.Value));
        RefreshShell(result.Success
            ? UiText.Format("PivotChart_TypeChanged", ChartTypeChangePlanner.DisplayName(plan.AppliedType!.Value))
            : result.ErrorMessage ?? UiText.Get("PivotChart_ChangeTypeInsertFirst"));
    }

    /// <summary>Analyze ▸ PivotChart ▸ PivotChart Options — opens the field-button / data-table options dialog.</summary>
    private async Task OpenPivotChartOptionsAsync()
    {
        if (!TryResolveActivePivotChart(UiText.Get("PivotChart_OptionsInsertFirst"), out var chart))
            return;

        var current = PivotChartOptionsPlanner.Read(chart!);

        var showFieldButtons = MakeCheck(PivotChartOptionsDialogFieldId.ShowFieldButtons, current.ShowFieldButtons);
        var reportFilterButtons = MakeCheck(PivotChartOptionsDialogFieldId.ShowReportFilterButtons, current.ShowReportFilterButtons, new Thickness(18, 0, 0, 0));
        var axisFieldButtons = MakeCheck(PivotChartOptionsDialogFieldId.ShowAxisFieldButtons, current.ShowAxisFieldButtons, new Thickness(18, 0, 0, 0));
        var valueFieldButtons = MakeCheck(PivotChartOptionsDialogFieldId.ShowValueFieldButtons, current.ShowValueFieldButtons, new Thickness(18, 0, 0, 0));
        var showDataTable = MakeCheck(PivotChartOptionsDialogFieldId.ShowDataTable, current.ShowDataTable);
        var dataTableLegendKeys = MakeCheck(PivotChartOptionsDialogFieldId.ShowDataTableLegendKeys, current.ShowDataTableLegendKeys, new Thickness(18, 0, 0, 0));
        var roundedCorners = MakeCheck(PivotChartOptionsDialogFieldId.RoundedCorners, current.RoundedCorners);
        var showHiddenData = MakeCheck(PivotChartOptionsDialogFieldId.ShowHiddenData, current.ShowHiddenData);

        var blankChoices = PivotChartOptionsPlanner.GetBlankDisplayChoices()
            .Select(choice => new PivotChartBlankDisplayOption(UiText.Get(choice.LabelResourceKey), choice.Mode))
            .ToList();
        var blankDisplayBox = new ComboBox
        {
            Width = 260,
            ItemsSource = blankChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(PivotChartBlankDisplayOption.Label)),
        };
        ApplyPivotComboBoxChrome(blankDisplayBox);
        ApplyFieldAutomation(blankDisplayBox, PivotChartOptionsDialogFieldId.BlankDisplayMode);
        blankDisplayBox.SelectedItem =
            blankChoices.FirstOrDefault(choice => choice.Mode == current.BlankDisplayMode)
            ?? (blankChoices.Count > 0 ? blankChoices[0] : null);

        void SyncSubButtons()
        {
            var master = showFieldButtons.IsChecked == true;
            reportFilterButtons.IsEnabled = master;
            axisFieldButtons.IsEnabled = master;
            valueFieldButtons.IsEnabled = master;
            dataTableLegendKeys.IsEnabled = showDataTable.IsChecked == true;
        }

        showFieldButtons.IsCheckedChanged += (_, _) => SyncSubButtons();
        showDataTable.IsCheckedChanged += (_, _) => SyncSubButtons();
        SyncSubButtons();

        var dialog = new Window
        {
            Title = UiText.Get(PivotChartOptionsPlanner.DialogTitleResourceKey),
            Width = 420,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, PivotChartOptionsPlanner.DialogAutomationId);

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "PivotChartOptionsOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancel, 80);
        AutomationProperties.SetAutomationId(cancel, "PivotChartOptionsCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) => dialog.Close(true);

        var content = new StackPanel { Spacing = 6, Margin = new Thickness(16) };
        content.Children.Add(showFieldButtons);
        content.Children.Add(reportFilterButtons);
        content.Children.Add(axisFieldButtons);
        content.Children.Add(valueFieldButtons);
        content.Children.Add(showDataTable);
        content.Children.Add(dataTableLegendKeys);
        content.Children.Add(roundedCorners);
        content.Children.Add(showHiddenData);
        content.Children.Add(new TextBlock
        {
            Text = FieldLabel(PivotChartOptionsDialogFieldId.BlankDisplayMode),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 4, 0, 0),
        });
        content.Children.Add(blankDisplayBox);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { ok, cancel },
        });
        dialog.Content = content;
        ConfigurePivotDialogLifecycle(dialog, showFieldButtons);

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        // Re-resolve after the dialog in case the selection drifted while it was open.
        if (!TryResolveActivePivotChart(UiText.Get("PivotChart_OptionsInsertFirst"), out chart))
            return;

        var blankDisplayMode = blankDisplayBox.SelectedItem is PivotChartBlankDisplayOption pickedBlankDisplay
            ? pickedBlankDisplay.Mode
            : current.BlankDisplayMode;
        var input = PivotChartOptionsPlanner.CreateResult(
            current.ChartStyleId,
            showFieldButtons.IsChecked == true,
            reportFilterButtons.IsChecked == true,
            axisFieldButtons.IsChecked == true,
            valueFieldButtons.IsChecked == true,
            showDataTable.IsChecked == true,
            dataTableLegendKeys.IsChecked == true,
            roundedCorners.IsChecked == true,
            showHiddenData.IsChecked == true,
            blankDisplayMode);

        var command = ChartCommandWorkflowPlanner.BuildPivotChartOptionsCommand(
            _session.ActiveSheet.Id,
            chart!,
            input);

        var result = _session.ExecuteReviewCommand(command);
        RefreshShell(result.Success
            ? UiText.Get("PivotChart_OptionsApplied")
            : result.ErrorMessage ?? UiText.Get("PivotChart_OptionsInsertFirst"));

        CheckBox MakeCheck(PivotChartOptionsDialogFieldId fieldId, bool isChecked, Thickness? margin = null)
        {
            var checkBox = new CheckBox
            {
                Content = FieldLabel(fieldId),
                IsChecked = isChecked,
                Margin = margin ?? new Thickness(0),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                MinHeight = 20,
                MaxHeight = 20,
            };
            ApplyFieldAutomation(checkBox, fieldId);
            return checkBox;
        }

        string FieldLabel(PivotChartOptionsDialogFieldId fieldId) =>
            StripDisplayMnemonic(UiText.Get(PivotChartOptionsPlanner.GetDialogField(fieldId).LabelResourceKey));

        void ApplyFieldAutomation(Control control, PivotChartOptionsDialogFieldId fieldId)
        {
            var descriptor = PivotChartOptionsPlanner.GetDialogField(fieldId);
            AutomationProperties.SetName(control, FieldLabel(fieldId));
            AutomationProperties.SetAutomationId(control, descriptor.AutomationId);
        }
    }

    private sealed record PivotChartBlankDisplayOption(string Label, ChartBlankDisplayMode Mode);

    /// <summary>
    /// Resolves the active pivot and its bound PivotChart, reporting an honest status (and returning false)
    /// when no pivot is active or the pivot has no chart yet.
    /// </summary>
    private bool TryResolveActivePivotChart(string noChartMessage, out ChartModel? chart)
    {
        chart = null;
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return false;

        var pivot = ResolveInsertControlPivot();
        if (pivot is null)
        {
            RefreshShell(UiText.Get("PivotAnalyze_SelectPivotPrompt"));
            return false;
        }

        chart = FindPivotChartForActivePivot(pivot);
        if (chart is null)
        {
            RefreshShell(noChartMessage);
            return false;
        }

        return true;
    }

    /// <summary>The PivotChart bound (by name) to <paramref name="pivot"/> on the active sheet, or null.</summary>
    private ChartModel? FindPivotChartForActivePivot(PivotTableModel pivot)
    {
        foreach (var chart in _session.ActiveSheet.Charts)
        {
            if (chart.IsPivotChart &&
                string.Equals(chart.PivotTableName, pivot.Name, StringComparison.OrdinalIgnoreCase))
            {
                return chart;
            }
        }

        return null;
    }
}
