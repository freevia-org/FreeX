using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Editing;
using FreeX.App.Services;
using FreeX.Core.Commands;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle InsertDeleteCellsDialogChromeStyle => new(FormulaBarFontFamily);

    // Home ▸ Cells ▸ Insert Cells / Delete Cells (parity gap: the ribbon buttons were no-ops). A small
    // shift-direction dialog mirrors Excel's prompt; WorkbookSession owns portable command
    // construction, grouped-sheet targeting, repeat behavior, and selection preservation.
    //
    // R79-commands-insert-delete-shift-5-2: a whole-row/whole-column selection must route to
    // InsertRowsCommand/InsertColumnsCommand (and their Delete- counterparts) exactly as the WPF
    // host's KeyboardInsertDeletePlanner does — never through the band-scoped InsertCellsCommand/
    // DeleteCellsCommand, which builds its shift band from the selection's row/col span and would
    // treat a whole-column selection's full row span (1..MaxRow) as a near-sheet-height band. That
    // both spuriously trips the table/AutoFilter overlap guard for unrelated tables elsewhere in the
    // column and skips the whole-column-only state (AutoFilter.Reference, FilterHiddenRows, form
    // controls, pivot tables, sparklines, watched cells) that only the Rows/Columns commands shift.

    private async Task ShowInsertCellsDialogAsync()
    {
        var range = _session.SelectedRange;
        if (SelectionRangeService.IsWholeRowSelection(range))
        {
            ApplyWorksheetStructureResult(
                _session.InsertSelectedRows(),
                "Inserted rows",
                "Could not insert rows.");
            return;
        }
        if (SelectionRangeService.IsWholeColumnSelection(range))
        {
            ApplyWorksheetStructureResult(
                _session.InsertSelectedColumns(),
                "Inserted columns",
                "Could not insert columns.");
            return;
        }

        var choice = await ShowShiftDirectionAsync(CellShiftDialogMode.Insert);
        if (choice is null)
            return;
        var direction = CellShiftDialogPlanner.ToKeyboardChoice(CellShiftDialogMode.Insert, choice.Value) == KeyboardInsertDeleteDialogChoice.ShiftDown
            ? InsertCellsShiftDirection.Down
            : InsertCellsShiftDirection.Right;
        ApplyWorksheetStructureResult(
            _session.InsertSelectedCells(direction),
            $"Inserted cells ({(direction == InsertCellsShiftDirection.Right ? "shift right" : "shift down")})",
            "Could not insert cells.");
    }

    private async Task ShowDeleteCellsDialogAsync()
    {
        var range = _session.SelectedRange;
        if (SelectionRangeService.IsWholeRowSelection(range))
        {
            ApplyWorksheetStructureResult(
                _session.DeleteSelectedRows(),
                "Deleted rows",
                "Could not delete rows.");
            return;
        }
        if (SelectionRangeService.IsWholeColumnSelection(range))
        {
            ApplyWorksheetStructureResult(
                _session.DeleteSelectedColumns(),
                "Deleted columns",
                "Could not delete columns.");
            return;
        }

        var choice = await ShowShiftDirectionAsync(CellShiftDialogMode.Delete);
        if (choice is null)
            return;
        var direction = CellShiftDialogPlanner.ToKeyboardChoice(CellShiftDialogMode.Delete, choice.Value) == KeyboardInsertDeleteDialogChoice.ShiftUp
            ? DeleteCellsShiftDirection.Up
            : DeleteCellsShiftDirection.Left;
        ApplyWorksheetStructureResult(
            _session.DeleteSelectedCells(direction),
            $"Deleted cells ({(direction == DeleteCellsShiftDirection.Left ? "shift left" : "shift up")})",
            "Could not delete cells.");
    }

    private void ApplyWorksheetStructureResult(
        WorkbookWorksheetStructureResult result,
        string successStatus,
        string failureStatus,
        bool recalculateWorkbook = false)
    {
        if (result.Success && !result.IsNoOp)
        {
            if (result.InvalidatesFormulaTraceArrows)
                ClearFormulaTraceArrowsAfterStructuralEdit();
            SetClipboardMarquee(null, isCut: false);

            if (result.ViewportRowDelta != 0)
                ShiftScrollOriginForRowEdit(result.TargetRange.Start.Row, result.ViewportRowDelta);
            if (result.ViewportColumnDelta != 0)
                ShiftScrollOriginForColEdit(result.TargetRange.Start.Col, result.ViewportColumnDelta);

            if (recalculateWorkbook)
                _session.RecalculateWorkbook();
        }

        RefreshShell(result.Success
            ? successStatus
            : result.ErrorMessage ?? failureStatus);
    }

    private async Task<CellShiftDialogChoice?> ShowShiftDirectionAsync(CellShiftDialogMode mode)
    {
        var surface = CellShiftDialogPlanner.GetSurface(mode);
        var options = CellShiftDialogPlanner.GetCellSelectionChoices(mode);
        var firstOption = options[0];
        var secondOption = options[1];
        var first = new RadioButton { Content = StripDisplayMnemonic(UiText.Get(firstOption.LabelKey)), GroupName = "shift", IsChecked = true, Margin = new Thickness(0, 2) };
        var second = new RadioButton { Content = StripDisplayMnemonic(UiText.Get(secondOption.LabelKey)), GroupName = "shift", Margin = new Thickness(0, 2) };
        var ok = new Button { Content = UiText.CreateAutomationName(UiText.Get("Common_Ok")), IsDefault = true };
        var cancel = new Button { Content = UiText.CreateAutomationName(UiText.Get("Common_Cancel")), IsCancel = true };
        ApplyCellShiftAutomation(first, firstOption);
        ApplyCellShiftAutomation(second, secondOption);
        AvaloniaCompactDialogChrome.ApplyRadioButton(first, InsertDeleteCellsDialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(second, InsertDeleteCellsDialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyButton(ok, InsertDeleteCellsDialogChromeStyle, 84, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(cancel, InsertDeleteCellsDialogChromeStyle, 84);

        var dialog = new Window
        {
            Title = UiText.Get(surface.TitleKey),
            Width = 320,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Children =
                {
                    first,
                    second,
                    AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)),
                },
            },
        };

        ok.Click += (_, _) => dialog.Close(second.IsChecked == true ? secondOption.Choice : firstOption.Choice);
        cancel.Click += (_, _) => dialog.Close((CellShiftDialogChoice?)null);
        return await dialog.ShowDialog<CellShiftDialogChoice?>(this);
    }

    private static void ApplyCellShiftAutomation(
        RadioButton button,
        CellShiftDialogOptionPresentation option)
    {
        AutomationProperties.SetName(button, option.AutomationName);
        AutomationProperties.SetAutomationId(button, option.AutomationId);
        AutomationProperties.SetHelpText(button, option.HelpText);
    }
}
