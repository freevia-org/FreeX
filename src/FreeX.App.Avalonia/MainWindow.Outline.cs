using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation;
using FreeX.App.Presentation.SheetUI;
using FreeX.App.Services;
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
    // level in the range + 1), not always a hardcoded 1.
    //
    // Ungroup (data.ungroup / the "Ungroup" submenu item / the grid context-menu Ungroup action)
    // is ALWAYS scoped to the current selection via UngroupSelection(), mirroring the WPF host's
    // Ungroup fix (MainWindow.OutlineCommands.cs): it decrements the deepest existing outline
    // level in the selected row/column range by one, leaving unrelated groups elsewhere on the
    // sheet untouched (R37-commands-outline-subtotal-2-1). This must NOT be gated on selection
    // shape -- a single grouped row is a single-cell-tall selection, and routing that through the
    // whole-sheet clear would wipe every group on the sheet instead of just ungrouping that one
    // row (R38-meta-2). The separate "Clear Outline" menu item is a distinct command that always
    // clears the whole worksheet's outline via ClearWorksheetOutline(), regardless of the current
    // selection, exactly like the WPF host's separate ClearWorksheetOutlineCommand handler. Both
    // routed through the generic review-command executor so both get undo/redo. Kept in the
    // Avalonia shell (no WorkbookSession change) to avoid churn with the concurrently-active
    // FreeW/macOS sessions.

    // R124-outlinecmds-multiarea-group-1: a Ctrl+click multi-area row/column header selection
    // must group/ungroup EVERY disjoint area in one action, not just the active (last-clicked)
    // one -- Excel groups all selected areas together. ResolveOutlineSelectionRanges routes
    // through the same SelectionStyleCommandPlanner.ResolveRanges choke point the WPF host's
    // AutoFit Row Height/Column Width multi-area fix and cell-style commands already use, instead
    // of reading only the single active _session.SelectedRange.
    private IReadOnlyList<GridRange> ResolveOutlineSelectionRanges()
    {
        var ranges = SelectionStyleCommandPlanner.ResolveRanges(_session.SelectedRange, _session.SelectedRanges);
        return ranges.Count > 0 ? ranges : [_session.SelectedRange];
    }

    private void GroupSelectedRows()
    {
        var sheet = _session.ActiveSheet;
        var ranges = ResolveOutlineSelectionRanges();

        var commands = ranges.Select(range => CreateGroupCommand(sheet, range)).ToList();
        var command = commands.Count == 1 ? commands[0] : new CompositeWorkbookCommand("Group", commands);
        var result = _session.ExecuteReviewCommand(command);
        RefreshShell(result.Success
            ? DescribeOutlineOutcome("Grouped", ranges)
            : result.ErrorMessage ?? "Could not group.");
    }

    private static IWorkbookCommand CreateGroupCommand(Sheet sheet, GridRange range)
    {
        if (OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns)
        {
            var colLevel = OutlineGroupingPlanner.GetNextOutlineLevel(
                range.Start.Col, range.End.Col, sheet.ColOutlineLevels);
            return new GroupColumnsCommand(sheet.Id, range.Start.Col, range.End.Col, colLevel, preserveExistingHierarchy: true);
        }

        var rowLevel = OutlineGroupingPlanner.GetNextOutlineLevel(
            range.Start.Row, range.End.Row, sheet.RowOutlineLevels);
        return new GroupRowsCommand(sheet.Id, range.Start.Row, range.End.Row, rowLevel, preserveExistingHierarchy: true);
    }

    /// <summary>
    /// Data ▸ Outline ▸ Ungroup (and the "Ungroup" submenu / grid context-menu action): always
    /// scoped to the current selection, regardless of its shape. A single-cell selection inside a
    /// grouped row/column still only decrements that row/column's own outline level by one -- it
    /// must never fall back to clearing the whole sheet's outline (R38-meta-2). A Ctrl+click
    /// multi-area selection ungroups every disjoint area (R124-outlinecmds-multiarea-group-1).
    /// </summary>
    private void UngroupSelection()
    {
        var sheet = _session.ActiveSheet;
        var ranges = ResolveOutlineSelectionRanges();

        var commands = new List<IWorkbookCommand>();
        foreach (var range in ranges)
            commands.AddRange(CreateUngroupCommands(sheet, range));

        var result = _session.ExecuteReviewCommand(new CompositeWorkbookCommand("Ungroup", commands));
        RefreshShell(result.Success
            ? DescribeOutlineOutcome("Ungrouped", ranges)
            : result.ErrorMessage ?? "Could not ungroup.");
    }

    private static IReadOnlyList<IWorkbookCommand> CreateUngroupCommands(Sheet sheet, GridRange range)
    {
        if (OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns)
        {
            var colRuns = GetContiguousSameLevelRuns(range.Start.Col, range.End.Col, sheet.ColOutlineLevels);
            return colRuns
                .Select(run => (IWorkbookCommand)new GroupColumnsCommand(
                    sheet.Id,
                    run.Start,
                    run.End,
                    OutlineGroupingPlanner.GetUngroupedOutlineLevel(run.Start, run.End, sheet.ColOutlineLevels)))
                .ToList();
        }

        var rowRuns = GetContiguousSameLevelRuns(range.Start.Row, range.End.Row, sheet.RowOutlineLevels);
        return rowRuns
            .Select(run => (IWorkbookCommand)new GroupRowsCommand(
                sheet.Id,
                run.Start,
                run.End,
                OutlineGroupingPlanner.GetUngroupedOutlineLevel(run.Start, run.End, sheet.RowOutlineLevels)))
            .ToList();
    }

    private static string DescribeOutlineOutcome(string verb, IReadOnlyList<GridRange> ranges)
    {
        if (ranges.Count == 1)
        {
            var range = ranges[0];
            return OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns
                ? $"{verb} columns {range.Start.Col}–{range.End.Col}"
                : $"{verb} rows {range.Start.Row}–{range.End.Row}";
        }

        return $"{verb} {ranges.Count} selected areas";
    }

    // Splits [start, end] into contiguous runs of indices that currently share the same outline
    // level. Indices with no level / level 0 are excluded entirely (they have nothing to decrement
    // and must stay untouched). Mirrors the WPF host shell's outline-command helper of the same
    // name so both shells share the identical per-run splitting behavior.
    private static List<(uint Start, uint End)> GetContiguousSameLevelRuns(
        uint start, uint end, IReadOnlyDictionary<uint, int> outlineLevels)
    {
        var runs = new List<(uint Start, uint End)>();
        uint? runStart = null;
        int runLevel = 0;
        for (var index = start; index <= end; index++)
        {
            outlineLevels.TryGetValue(index, out var level);
            if (level <= 0)
            {
                if (runStart is { } pendingStart)
                {
                    runs.Add((pendingStart, index - 1));
                    runStart = null;
                }
                continue;
            }

            if (runStart is null)
            {
                runStart = index;
                runLevel = level;
            }
            else if (level != runLevel)
            {
                runs.Add((runStart.Value, index - 1));
                runStart = index;
                runLevel = level;
            }
        }

        if (runStart is { } finalStart)
            runs.Add((finalStart, end));

        return runs;
    }

    /// <summary>
    /// Data ▸ Outline ▸ Clear Outline: always clears the whole worksheet's outline (group
    /// structure on every row and column), regardless of the current selection.
    /// </summary>
    private void ClearWorksheetOutline()
    {
        var result = _session.ExecuteReviewCommand(new ClearWorksheetOutlineCommand(_session.ActiveSheet.Id));
        RefreshShell(result.Success
            ? "Cleared the worksheet outline."
            : result.ErrorMessage ?? "Could not clear the outline.");
    }

    /// <summary>
    /// Excel's Ungroup decrements the deepest outline level found across the given row/column
    /// range by exactly one (never straight to 0), so a range that is only the innermost part of a
    /// wider, still-nested group drops out of just its own nesting level and remains part of the
    /// outer group. Mirrors <see cref="OutlineGroupingPlanner.GetNextOutlineLevel"/>'s "deepest
    /// level already present in the range" scan, but subtracts instead of adds.
    /// </summary>
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
