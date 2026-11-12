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
                () => _session.InsertSelectedRows(),
                "Inserted rows",
                "Could not insert rows.");
            return;
        }
        if (SelectionRangeService.IsWholeColumnSelection(range))
        {
            ApplyWorksheetStructureResult(
                () => _session.InsertSelectedColumns(),
                "Inserted columns",
                "Could not insert columns.");
            return;
        }

        var choice = await ShowShiftDirectionAsync(CellShiftDialogMode.Insert);
        if (choice is null)
            return;
        // WPF InsertCellsMenuItem_Click: the entire-row/entire-column choices route to the whole-band
        // session operations, the shift choices to the band-scoped cell insert.
        switch (CellShiftDialogPlanner.ToKeyboardChoice(CellShiftDialogMode.Insert, choice.Value))
        {
            case KeyboardInsertDeleteDialogChoice.EntireRow:
                ApplyWorksheetStructureResult(
                    () => _session.InsertSelectedRows(),
                    "Inserted rows",
                    "Could not insert rows.");
                return;
            case KeyboardInsertDeleteDialogChoice.EntireColumn:
                ApplyWorksheetStructureResult(
                    () => _session.InsertSelectedColumns(),
                    "Inserted columns",
                    "Could not insert columns.");
                return;
            case KeyboardInsertDeleteDialogChoice.ShiftDown:
                ApplyWorksheetStructureResult(
                    () => _session.InsertSelectedCells(InsertCellsShiftDirection.Down),
                    "Inserted cells (shift down)",
                    "Could not insert cells.");
                return;
            default:
                ApplyWorksheetStructureResult(
                    () => _session.InsertSelectedCells(InsertCellsShiftDirection.Right),
                    "Inserted cells (shift right)",
                    "Could not insert cells.");
                return;
        }
    }

    private async Task ShowDeleteCellsDialogAsync()
    {
        var range = _session.SelectedRange;
        if (SelectionRangeService.IsWholeRowSelection(range))
        {
            ApplyWorksheetStructureResult(
                () => _session.DeleteSelectedRows(),
                "Deleted rows",
                "Could not delete rows.");
            return;
        }
        if (SelectionRangeService.IsWholeColumnSelection(range))
        {
            ApplyWorksheetStructureResult(
                () => _session.DeleteSelectedColumns(),
                "Deleted columns",
                "Could not delete columns.");
            return;
        }

        var choice = await ShowShiftDirectionAsync(CellShiftDialogMode.Delete);
        if (choice is null)
            return;
        switch (CellShiftDialogPlanner.ToKeyboardChoice(CellShiftDialogMode.Delete, choice.Value))
        {
            case KeyboardInsertDeleteDialogChoice.EntireRow:
                ApplyWorksheetStructureResult(
                    () => _session.DeleteSelectedRows(),
                    "Deleted rows",
                    "Could not delete rows.");
                return;
            case KeyboardInsertDeleteDialogChoice.EntireColumn:
                ApplyWorksheetStructureResult(
                    () => _session.DeleteSelectedColumns(),
                    "Deleted columns",
                    "Could not delete columns.");
                return;
            case KeyboardInsertDeleteDialogChoice.ShiftUp:
                ApplyWorksheetStructureResult(
                    () => _session.DeleteSelectedCells(DeleteCellsShiftDirection.Up),
                    "Deleted cells (shift up)",
                    "Could not delete cells.");
                return;
            default:
                ApplyWorksheetStructureResult(
                    () => _session.DeleteSelectedCells(DeleteCellsShiftDirection.Left),
                    "Deleted cells (shift left)",
                    "Could not delete cells.");
                return;
        }
    }

    /// <summary>
    /// Takes the operation as a delegate rather than its result so the PRE-EDIT scroll origin can be
    /// captured first. The structural edit re-anchors the view onto the selection on its way through
    /// the session, so a shift computed afterwards starts from the already-clobbered origin and lands
    /// one row/column away from the edit point instead of preserving the visible content (R76).
    /// </summary>
    private void ApplyWorksheetStructureResult(
        Func<WorkbookWorksheetStructureResult> operation,
        string successStatus,
        string failureStatus,
        bool recalculateWorkbook = false)
    {
        var (topRowBeforeEdit, leftColBeforeEdit) = _session.ViewportOrigin;
        var result = operation();

        if (result.Success && !result.IsNoOp)
        {
            if (result.InvalidatesFormulaTraceArrows)
                ClearFormulaTraceArrowsAfterStructuralEdit();
            SetClipboardMarquee(null, isCut: false);

            if (result.ViewportRowDelta != 0)
                ShiftScrollOriginForRowEdit(
                    result.TargetRange.Start.Row,
                    result.ViewportRowDelta,
                    topRowBeforeEdit,
                    leftColBeforeEdit);
            if (result.ViewportColumnDelta != 0)
                ShiftScrollOriginForColEdit(
                    result.TargetRange.Start.Col,
                    result.ViewportColumnDelta,
                    topRowBeforeEdit,
                    leftColBeforeEdit);

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
        // WPF CellShiftDialog lists every surface option: the two shift directions followed by the
        // entire-row and entire-column choices, the first one checked.
        var optionButtons = new List<RadioButton>();
        var optionPanel = new StackPanel { Margin = new Thickness(8, 6, 8, 8) };
        foreach (var option in surface.Options)
        {
            var button = new RadioButton
            {
                Content = StripDisplayMnemonic(UiText.Get(option.LabelKey)),
                GroupName = "shift",
                IsChecked = optionButtons.Count == 0,
                Tag = option.Choice,
                Margin = new Thickness(0, 0, 0, 2),
            };
            ApplyCellShiftAutomation(button, option);
            AvaloniaCompactDialogChrome.ApplyRadioButton(button, InsertDeleteCellsDialogChromeStyle);
            // WPF radios are ~16px rows with a 6px bottom margin; a 20px compact row plus 2px keeps the pitch.
            button.MinHeight = 20;
            button.MaxHeight = 20;
            optionButtons.Add(button);
            optionPanel.Children.Add(button);
        }
        var ok = new Button { Content = UiText.CreateAutomationName(UiText.Get("Common_Ok")), IsDefault = true };
        var cancel = new Button { Content = UiText.CreateAutomationName(UiText.Get("Common_Cancel")), IsCancel = true };
        // WPF DialogButtonRowFactory.Create(Accept, buttonWidth: 72).
        AvaloniaCompactDialogChrome.ApplyButton(ok, InsertDeleteCellsDialogChromeStyle, 72, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(cancel, InsertDeleteCellsDialogChromeStyle, 72);

        // Layout mirrors WPF CellShiftDialog: a 310x245 *outer* NoResize window, a 12px root
        // DockPanel whose first child is the semibold prompt (docked left, the DockPanel default),
        // the option group (stack margin 8,6,8,8) docked top beside it and the OK/Cancel row below.
        // WPF lets the button row fill the leftover height; here the row keeps standard-height
        // buttons on the bottom margin of the frame-corrected client area.
        var prompt = new TextBlock
        {
            Text = UiText.Get(surface.PromptKey),
            FontWeight = global::Avalonia.Media.FontWeight.SemiBold,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 0, 0, 6),
        };
        var optionGroup = new GroupBox
        {
            Header = UiText.Get(surface.GroupHeaderKey),
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Content = optionPanel,
        };
        ApplyPlainWindowGroupBoxChrome(optionGroup, InsertDeleteCellsDialogChromeStyle);
        DockPanel.SetDock(optionGroup, Dock.Top);
        var actionRow = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0));
        actionRow.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom;
        DockPanel.SetDock(actionRow, Dock.Bottom);

        var dialog = new Window
        {
            Title = UiText.Get(surface.TitleKey),
            Width = 310 - WpfNoResizeFrameWidth,
            MinWidth = 310 - WpfNoResizeFrameWidth,
            MaxWidth = 310 - WpfNoResizeFrameWidth,
            Height = 245 - WpfNoResizeClientFrameHeight,
            MinHeight = 245 - WpfNoResizeClientFrameHeight,
            MaxHeight = 245 - WpfNoResizeClientFrameHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
            Content = new DockPanel
            {
                Margin = new Thickness(12),
                Children =
                {
                    prompt,
                    optionGroup,
                    actionRow,
                },
            },
        };

        ok.Click += (_, _) =>
        {
            var selected = optionButtons.FirstOrDefault(button => button.IsChecked == true);
            dialog.Close(selected?.Tag is CellShiftDialogChoice selectedChoice
                ? selectedChoice
                : (CellShiftDialogChoice?)surface.Options[0].Choice);
        };
        dialog.Opened += (_, _) => optionButtons[0].Focus();
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
