using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.SheetUI;
using FreeX.Core.Commands;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Data ▸ Outline ▸ Group / Ungroup (parity gap: the ribbon buttons were no-ops). Groups the
    // selected rows at outline level 1; Ungroup clears the worksheet outline. Routed through the
    // generic review-command executor so both get undo/redo. Kept in the Avalonia shell (no
    // WorkbookSession change) to avoid churn with the concurrently-active FreeW/macOS sessions.

    private void GroupSelectedRows()
    {
        var range = _session.SelectedRange;
        var result = _session.ExecuteReviewCommand(
            new GroupRowsCommand(_session.ActiveSheet.Id, range.Start.Row, range.End.Row, level: 1));
        RefreshShell(result.Success
            ? $"Grouped rows {range.Start.Row}–{range.End.Row}"
            : result.ErrorMessage ?? "Could not group rows.");
    }

    private void ClearWorksheetOutline()
    {
        var result = _session.ExecuteReviewCommand(new ClearWorksheetOutlineCommand(_session.ActiveSheet.Id));
        RefreshShell(result.Success
            ? "Cleared the worksheet outline."
            : result.ErrorMessage ?? "Could not clear the outline.");
    }

    // Data ▸ Outline ▸ Settings (the small dialog launched from Excel's Outline group). The three
    // toggles — summary rows below detail, summary columns to right of detail, automatic styles —
    // are resolved/diffed by the portable OutlineSettingsPlanner and persisted through the additive
    // SetWorksheetOutlineSettingsCommand (undo/redo aware). Per-sheet, so it is also reachable from
    // the sheet-tab context menu.

    /// <summary>Opens the Outline Settings dialog for the active sheet.</summary>
    private void ShowOutlineSettingsDialog() => _ = ShowOutlineSettingsDialogAsync();

    private async Task ShowOutlineSettingsDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var sheet = _session.ActiveSheet;
        var current = OutlineSettingsPlanner.FromStored(
            sheet.OutlineSummaryBelow,
            sheet.OutlineSummaryRight,
            sheet.ApplyOutlineStyles);

        var summaryBelowBox = new CheckBox
        {
            Content = UiText.Get("OutlineSettings_SummaryRowsBelow"),
            IsChecked = current.SummaryBelow,
        };
        AutomationProperties.SetAutomationId(summaryBelowBox, "OutlineSettingsSummaryBelowCheckBox");

        var summaryRightBox = new CheckBox
        {
            Content = UiText.Get("OutlineSettings_SummaryColumnsRight"),
            IsChecked = current.SummaryRight,
        };
        AutomationProperties.SetAutomationId(summaryRightBox, "OutlineSettingsSummaryRightCheckBox");

        var autoStylesBox = new CheckBox
        {
            Content = UiText.Get("OutlineSettings_AutomaticStyles"),
            IsChecked = current.ApplyStyles,
        };
        AutomationProperties.SetAutomationId(autoStylesBox, "OutlineSettingsAutomaticStylesCheckBox");

        var okButton = new Button
        {
            Content = UiText.Get("OutlineSettings_Ok"),
            IsDefault = true,
            MinWidth = 84,
        };
        AutomationProperties.SetAutomationId(okButton, "OutlineSettingsOkButton");
        var cancelButton = new Button
        {
            Content = UiText.Get("OutlineSettings_Cancel"),
            IsCancel = true,
            MinWidth = 84,
            Margin = new Thickness(8, 0, 0, 0),
        };
        AutomationProperties.SetAutomationId(cancelButton, "OutlineSettingsCancelButton");

        var dialog = new Window
        {
            Title = UiText.Get("OutlineSettings_Title"),
            Width = 320,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "OutlineSettingsDialog");

        var accepted = false;
        okButton.Click += (_, _) =>
        {
            accepted = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = UiText.Get("OutlineSettings_Direction"), FontWeight = FontWeight.SemiBold },
                summaryBelowBox,
                summaryRightBox,
                autoStylesBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                    Margin = new Thickness(0, 12, 0, 0),
                    Children = { okButton, cancelButton },
                },
            },
        };

        await dialog.ShowDialog(this);
        if (!accepted)
            return;

        var resolvedSheet = _session.ActiveSheet;
        var acceptedState = new OutlineSettingsState(
            summaryBelowBox.IsChecked == true,
            summaryRightBox.IsChecked == true,
            autoStylesBox.IsChecked == true);

        if (!OutlineSettingsPlanner.HasChanges(
                acceptedState,
                resolvedSheet.OutlineSummaryBelow,
                resolvedSheet.OutlineSummaryRight,
                resolvedSheet.ApplyOutlineStyles))
        {
            RefreshShell(UiText.Get("OutlineSettings_NoChangeStatus"));
            return;
        }

        var result = _session.ExecuteReviewCommand(new SetWorksheetOutlineSettingsCommand(
            resolvedSheet.Id,
            acceptedState.SummaryBelow,
            acceptedState.SummaryRight,
            acceptedState.ApplyStyles));
        RefreshShell(result.Success
            ? UiText.Get("OutlineSettings_AppliedStatus")
            : result.ErrorMessage ?? UiText.Get("OutlineSettings_AppliedStatus"));
    }
}
