using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private DialogRangePickerSession? _dialogRangePickerSession;

    private void BeginDialogRangeSelection(
        Window? dialog,
        bool collapseDialog,
        Action<GridRange> applySelection)
    {
        if (dialog is null)
            return;

        CancelDialogRangeSelection(restoreDialog: true);
        var session = new DialogRangePickerSession(
            dialog,
            collapseDialog,
            applySelection,
            IsEnabled,
            dialog.Left,
            dialog.Top,
            dialog.Opacity,
            dialog.IsHitTestVisible);
        _dialogRangePickerSession = session;
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
        if (_dialogRangePickerSession is null)
            return;

        Dispatcher.BeginInvoke(
            new Action(() => CompleteDialogRangeSelection(applySelection: true)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void DialogRangePicker_KeyDown(object sender, KeyEventArgs e)
    {
        if (_dialogRangePickerSession is null)
            return;

        if (e.Key == Key.Escape)
        {
            CompleteDialogRangeSelection(applySelection: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CompleteDialogRangeSelection(applySelection: true);
            e.Handled = true;
        }
    }

    private void DialogRangePickerDialog_Closed(object? sender, EventArgs e) =>
        CancelDialogRangeSelection(restoreDialog: false);

    private void CompleteDialogRangeSelection(bool applySelection)
    {
        var session = _dialogRangePickerSession;
        if (session is null)
            return;

        CancelDialogRangeSelection(restoreDialog: false);
        if (applySelection && SheetGrid.SelectedRange is { } selectedRange)
            session.ApplySelection(selectedRange);

        RestoreDialogAfterRangeSelection(session);
    }

    private void CancelDialogRangeSelection(bool restoreDialog)
    {
        var session = _dialogRangePickerSession;
        if (session is null)
            return;

        _dialogRangePickerSession = null;
        SheetGrid.RemoveHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(DialogRangePicker_MouseLeftButtonUp));
        PreviewKeyDown -= DialogRangePicker_KeyDown;
        session.Dialog.Closed -= DialogRangePickerDialog_Closed;
        if (restoreDialog)
            RestoreDialogAfterRangeSelection(session);
    }

    private void RestoreDialogAfterRangeSelection(DialogRangePickerSession session)
    {
        SetDialogRangePickerOwnerInputEnabled(session.OwnerWasEnabled);
        if (session.CollapseDialog)
        {
            session.Dialog.Left = session.DialogLeft;
            session.Dialog.Top = session.DialogTop;
            session.Dialog.Opacity = session.DialogOpacity;
            session.Dialog.IsHitTestVisible = session.DialogIsHitTestVisible;
        }

        if (session.Dialog.IsVisible)
            session.Dialog.Activate();
    }

    private void SetDialogRangePickerOwnerInputEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            NativeEnableWindow(handle, isEnabled);
    }

    private static void CollapseDialogForRangeSelection(DialogRangePickerSession session)
    {
        var dialogWidth = EffectiveDialogRangeSelectionDimension(session.Dialog.ActualWidth, session.Dialog.Width, 420);
        var dialogHeight = EffectiveDialogRangeSelectionDimension(session.Dialog.ActualHeight, session.Dialog.Height, 560);
        session.Dialog.Opacity = 0;
        session.Dialog.IsHitTestVisible = false;
        session.Dialog.Left = SystemParameters.VirtualScreenLeft - dialogWidth - 32;
        session.Dialog.Top = SystemParameters.VirtualScreenTop - dialogHeight - 32;
    }

    private static double EffectiveDialogRangeSelectionDimension(double actual, double configured, double fallback)
    {
        if (!double.IsNaN(actual) && actual > 0)
            return actual;
        if (!double.IsNaN(configured) && configured > 0)
            return configured;
        return fallback;
    }

    [DllImport("user32.dll", EntryPoint = "EnableWindow")]
    private static extern bool NativeEnableWindow(IntPtr hWnd, bool bEnable);

    private sealed record DialogRangePickerSession(
        Window Dialog,
        bool CollapseDialog,
        Action<GridRange> ApplySelection,
        bool OwnerWasEnabled,
        double DialogLeft,
        double DialogTop,
        double DialogOpacity,
        bool DialogIsHitTestVisible);
}
