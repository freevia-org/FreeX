using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation;
using FreeX.App.Presentation.SheetUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // -------------------------------------------------------------------------------------------------------
    // Outline dialog chrome helpers
    // -------------------------------------------------------------------------------------------------------

    private static AvaloniaCompactDialogChromeStyle OutlineDialogChromeStyle => new(FormulaBarFontFamily);

    private static void ApplyOutlineButtonChrome(Button button, double minWidth = 84, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, OutlineDialogChromeStyle, minWidth, isDefault);

    private static void ApplyOutlineCheckBoxChrome(CheckBox cb)
    {
        StripContentMnemonic(cb);
        AvaloniaCompactDialogChrome.ApplyCheckBox(cb, OutlineDialogChromeStyle);
    }

    // Data ▸ Outline ▸ Group / Ungroup (parity gap: the ribbon buttons were no-ops). Groups the
    // selected rows/columns, nesting the outline level the same way WPF's CreateGroupCommand does
    // (MainWindow.OutlineCommands.cs): a whole-column selection groups by column instead of
    // marking every row on the sheet, and the level is the next nesting depth (existing deepest
    // level in the range + 1), not always a hardcoded 1. Ungroup (data.ungroup / the "Ungroup"
    // submenu item / the grid context-menu Ungroup action -- all wired to ClearWorksheetOutline())
    // is scoped to the current selection, mirroring the WPF host's Ungroup fix
    // (MainWindow.OutlineCommands.cs): it decrements the deepest existing outline level in the
    // selected row/column range by one, leaving unrelated groups elsewhere on the sheet untouched
    // (R37-commands-outline-subtotal-2-1). The separate "Clear Outline" menu item shares this same
    // entry point but is invoked without a deliberate multi-row/column selection (a bare active
    // cell), so a trivial single-cell selection still falls back to the legacy whole-sheet clear
    // that command requires. Routed through the generic review-command executor so both get
    // undo/redo. Kept in the Avalonia shell (no WorkbookSession change) to avoid churn with the
    // concurrently-active FreeW/macOS sessions.

    private void GroupSelectedRows()
    {
        var range = _session.SelectedRange;
        var sheet = _session.ActiveSheet;

        if (OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns)
        {
            var colLevel = OutlineGroupingPlanner.GetNextOutlineLevel(
                range.Start.Col, range.End.Col, sheet.ColOutlineLevels);
            var colResult = _session.ExecuteReviewCommand(
                new GroupColumnsCommand(sheet.Id, range.Start.Col, range.End.Col, colLevel, preserveExistingHierarchy: true));
            RefreshShell(colResult.Success
                ? $"Grouped columns {range.Start.Col}–{range.End.Col}"
                : colResult.ErrorMessage ?? "Could not group columns.");
            return;
        }

        var rowLevel = OutlineGroupingPlanner.GetNextOutlineLevel(
            range.Start.Row, range.End.Row, sheet.RowOutlineLevels);
        var result = _session.ExecuteReviewCommand(
            new GroupRowsCommand(sheet.Id, range.Start.Row, range.End.Row, rowLevel, preserveExistingHierarchy: true));
        RefreshShell(result.Success
            ? $"Grouped rows {range.Start.Row}–{range.End.Row}"
            : result.ErrorMessage ?? "Could not group rows.");
    }

    private void ClearWorksheetOutline()
    {
        var range = _session.SelectedRange;
        var sheet = _session.ActiveSheet;

        if (!IsSingleCellSelection(range))
        {
            if (OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns)
            {
                var newColLevel = GetUngroupedOutlineLevel(sheet.ColOutlineLevels, range.Start.Col, range.End.Col);
                var colResult = _session.ExecuteReviewCommand(
                    new GroupColumnsCommand(sheet.Id, range.Start.Col, range.End.Col, newColLevel));
                RefreshShell(colResult.Success
                    ? $"Ungrouped columns {range.Start.Col}–{range.End.Col}"
                    : colResult.ErrorMessage ?? "Could not ungroup columns.");
                return;
            }

            var newRowLevel = GetUngroupedOutlineLevel(sheet.RowOutlineLevels, range.Start.Row, range.End.Row);
            var rowResult = _session.ExecuteReviewCommand(
                new GroupRowsCommand(sheet.Id, range.Start.Row, range.End.Row, newRowLevel));
            RefreshShell(rowResult.Success
                ? $"Ungrouped rows {range.Start.Row}–{range.End.Row}"
                : rowResult.ErrorMessage ?? "Could not ungroup rows.");
            return;
        }

        var result = _session.ExecuteReviewCommand(new ClearWorksheetOutlineCommand(_session.ActiveSheet.Id));
        RefreshShell(result.Success
            ? "Cleared the worksheet outline."
            : result.ErrorMessage ?? "Could not clear the outline.");
    }

    private static bool IsSingleCellSelection(GridRange range) =>
        range.Start.Row == range.End.Row && range.Start.Col == range.End.Col;

    /// <summary>
    /// Excel's Ungroup decrements the deepest outline level found across the given row/column
    /// range by exactly one (never straight to 0), so a range that is only the innermost part of a
    /// wider, still-nested group drops out of just its own nesting level and remains part of the
    /// outer group. Mirrors <see cref="OutlineGroupingPlanner.GetNextOutlineLevel"/>'s "deepest
    /// level already present in the range" scan, but subtracts instead of adds.
    /// </summary>
    private static int GetUngroupedOutlineLevel(IReadOnlyDictionary<uint, int> levels, uint start, uint end)
    {
        var maxLevel = 0;
        for (var i = start; i <= end; i++)
        {
            if (levels.TryGetValue(i, out var level) && level > maxLevel)
                maxLevel = level;
        }

        return Math.Max(maxLevel - 1, 0);
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
        ApplyOutlineCheckBoxChrome(summaryBelowBox);
        AutomationProperties.SetAutomationId(summaryBelowBox, "OutlineSettingsSummaryBelowCheckBox");

        var summaryRightBox = new CheckBox
        {
            Content = UiText.Get("OutlineSettings_SummaryColumnsRight"),
            IsChecked = current.SummaryRight,
        };
        ApplyOutlineCheckBoxChrome(summaryRightBox);
        AutomationProperties.SetAutomationId(summaryRightBox, "OutlineSettingsSummaryRightCheckBox");

        var autoStylesBox = new CheckBox
        {
            Content = UiText.Get("OutlineSettings_AutomaticStyles"),
            IsChecked = current.ApplyStyles,
        };
        ApplyOutlineCheckBoxChrome(autoStylesBox);
        AutomationProperties.SetAutomationId(autoStylesBox, "OutlineSettingsAutomaticStylesCheckBox");

        var okButton = new Button
        {
            Content = UiText.Get("OutlineSettings_Ok"),
            IsDefault = true,
            MinWidth = 84,
        };
        ApplyOutlineButtonChrome(okButton, minWidth: 84, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "OutlineSettingsOkButton");
        var cancelButton = new Button
        {
            Content = UiText.Get("OutlineSettings_Cancel"),
            IsCancel = true,
            MinWidth = 84,
            Margin = new Thickness(8, 0, 0, 0),
        };
        ApplyOutlineButtonChrome(cancelButton, minWidth: 84);
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
                new TextBlock { Text = UiText.Get("OutlineSettings_Direction"), FontWeight = FontWeight.SemiBold, FontSize = 12, FontFamily = FormulaBarFontFamily },
                summaryBelowBox,
                summaryRightBox,
                autoStylesBox,
                AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 12, 0, 0)),
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
