using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private readonly DialogRangeSelectionController<DialogRangePickerContext> _dialogRangeSelectionController = new();

    private void BeginDialogRangeSelection(
        Window? dialog,
        bool collapseDialog,
        Action<GridRange> applySelection)
    {
        if (dialog is null)
            return;

        var session = _dialogRangeSelectionController.Begin(
            new DialogRangePickerContext(
                dialog,
                applySelection,
                dialog.Left,
                dialog.Top,
                dialog.Opacity,
                dialog.IsHitTestVisible),
            originalText: string.Empty,
            DialogRangeSelectionFormat.Range,
            collapseDialog,
            IsEnabled,
            FinishDialogRangeSelectionTransition);
        SheetGrid.AddHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(DialogRangePicker_MouseLeftButtonUp),
            handledEventsToo: true);
        PreviewKeyDown += DialogRangePicker_KeyDown;
        dialog.Closed += DialogRangePickerDialog_Closed;

        if (collapseDialog)
            CollapseDialogForRangeSelection(session);

        SetDialogRangePickerOwnerInputEnabled(true);
        Activate();
        SheetGrid.Focus();
    }

    private void DialogRangePicker_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dialogRangeSelectionController.IsActive)
            return;

        Dispatcher.BeginInvoke(
            new Action(() => CompleteDialogRangeSelection(applySelection: true)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void DialogRangePicker_KeyDown(object sender, KeyEventArgs e)
    {
        var result = _dialogRangeSelectionController.HandleKey(e.Key switch
        {
            Key.Escape => DialogRangeSelectionKey.Escape,
            Key.Enter => DialogRangeSelectionKey.Enter,
            _ => DialogRangeSelectionKey.Other,
        }, SheetGrid.SelectedRange);
        if (!result.Handled)
            return;

        if (result.Transition is { } transition)
            FinishDialogRangeSelectionTransition(transition);
        e.Handled = true;
    }

    private void DialogRangePickerDialog_Closed(object? sender, EventArgs e) =>
        CancelDialogRangeSelection(restoreDialog: false);

    private void CompleteDialogRangeSelection(bool applySelection)
    {
        if (_dialogRangeSelectionController.Complete(SheetGrid.SelectedRange, applySelection) is { } transition)
            FinishDialogRangeSelectionTransition(transition);
    }

    private void CancelDialogRangeSelection(bool restoreDialog)
    {
        if (_dialogRangeSelectionController.Cancel(restoreDialog, restoreOriginalText: false) is { } transition)
            FinishDialogRangeSelectionTransition(transition);
    }

    private void FinishDialogRangeSelectionTransition(
        DialogRangeSelectionTransition<DialogRangePickerContext> transition) =>
        _dialogRangeSelectionController.FinishTransition(
            transition,
            DetachDialogRangeSelection,
            (state, selectedRange) => state.Context.ApplySelection(selectedRange),
            null,
            RestoreDialogAfterRangeSelection);

    private void DetachDialogRangeSelection(DialogRangePickerContext context)
    {
        SheetGrid.RemoveHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(DialogRangePicker_MouseLeftButtonUp));
        PreviewKeyDown -= DialogRangePicker_KeyDown;
        context.Dialog.Closed -= DialogRangePickerDialog_Closed;
    }

    private void RestoreDialogAfterRangeSelection(
        DialogRangeSelectionState<DialogRangePickerContext> session)
    {
        var context = session.Context;
        SetDialogRangePickerOwnerInputEnabled(session.OwnerWasEnabled);
        if (session.CollapseDialog)
        {
            context.Dialog.Left = context.DialogLeft;
            context.Dialog.Top = context.DialogTop;
            context.Dialog.Opacity = context.DialogOpacity;
            context.Dialog.IsHitTestVisible = context.DialogIsHitTestVisible;
        }

        if (context.Dialog.IsVisible)
            context.Dialog.Activate();
    }

    private void SetDialogRangePickerOwnerInputEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            NativeEnableWindow(handle, isEnabled);
    }

    private static void CollapseDialogForRangeSelection(
        DialogRangeSelectionState<DialogRangePickerContext> session)
    {
        var context = session.Context;
        var dialogWidth = DialogRangeSelectionGeometryPlanner.ResolveDimension(
            context.Dialog.ActualWidth,
            context.Dialog.Width,
            420);
        var dialogHeight = DialogRangeSelectionGeometryPlanner.ResolveDimension(
            context.Dialog.ActualHeight,
            context.Dialog.Height,
            560);
        context.Dialog.Opacity = 0;
        context.Dialog.IsHitTestVisible = false;
        context.Dialog.Left = SystemParameters.VirtualScreenLeft - dialogWidth - 32;
        context.Dialog.Top = SystemParameters.VirtualScreenTop - dialogHeight - 32;
    }

    [DllImport("user32.dll", EntryPoint = "EnableWindow")]
    private static extern bool NativeEnableWindow(IntPtr hWnd, bool bEnable);

    private sealed record DialogRangePickerContext(
        Window Dialog,
        Action<GridRange> ApplySelection,
        double DialogLeft,
        double DialogTop,
        double DialogOpacity,
        bool DialogIsHitTestVisible);
}
