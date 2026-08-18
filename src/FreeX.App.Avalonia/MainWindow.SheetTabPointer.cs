using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.SheetUI;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const double SheetTabNavScrollAmount = 140;
    private const double SheetTabDragThreshold = 4;

    private SheetId? _sheetTabDragId;
    private Point _sheetTabDragStart;
    private int? _sheetTabDragPendingToIndex;
    private IPointer? _sheetTabDragPointer;
    private SheetId? _sheetTabModifierClickSuppressionId;
    private bool _activateSheetDialogOpenOrPending;

    private void BeginSheetTabPointer(SheetId sheetId, PointerPressedEventArgs args)
    {
        var point = args.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            // WPF selects the tab before ContextMenuOpening. Preserve an existing group when the
            // clicked tab is already in it, but make an outside tab current before its menu opens.
            SelectSheetForContextCommand(sheetId);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (TryBeginFormulaSheetSpanTabPointer(sheetId, args.KeyModifiers))
        {
            args.Handled = true;
            return;
        }

        if (args.ClickCount >= 2)
        {
            args.Handled = true;
            if (SelectSheetForContextCommand(sheetId))
                RunGuarded(() => RenameActiveSheetAsync());
            return;
        }

        if (BeginSheetTabPointer(sheetId, args.KeyModifiers))
            args.Handled = true;

        ClearSheetTabDragState();
        _sheetTabDragId = sheetId;
        _sheetTabDragStart = args.GetPosition(_sheetTabsHost);
        _sheetTabDragPendingToIndex = null;
        _sheetTabDragPointer = args.Pointer;
        _sheetTabsHost.PointerMoved += SheetTabDragPointerMoved;
        _sheetTabsHost.PointerReleased += SheetTabDragPointerReleased;
        _sheetTabsHost.PointerCaptureLost += SheetTabDragPointerCaptureLost;
        args.Pointer.Capture(_sheetTabsHost);
    }

    private bool BeginSheetTabPointer(SheetId sheetId, KeyModifiers modifiers)
    {
        if (TryBeginFormulaSheetSpanTabPointer(sheetId, modifiers))
            return true;

        var selectRange = modifiers.HasFlag(KeyModifiers.Shift);
        var toggle = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        SelectSheet(sheetId, selectRange, toggle);
        _sheetTabModifierClickSuppressionId = selectRange || toggle ? sheetId : null;
        return selectRange || toggle;
    }

    private bool TryBeginFormulaSheetSpanTabPointer(SheetId sheetId, KeyModifiers modifiers)
    {
        // WPF keeps an existing formula Edit session alive while sheet tabs seed the next
        // cross-sheet point reference. Point mode may be entered afterward with F2, so the
        // shared span planner must see both lifecycle states.
        if (GetFormulaReferenceHighlightEditor() is null ||
            _session.Workbook.GetSheet(sheetId) is not { } clickedSheet)
        {
            return false;
        }

        _formulaRangeEditingSession.ApplySheetTabSelection(
            _session.ActiveSheet.Name,
            clickedSheet.Name,
            modifiers.HasFlag(KeyModifiers.Shift));
        SelectSheet(
            sheetId,
            selectRange: modifiers.HasFlag(KeyModifiers.Shift),
            toggle: modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta));
        _sheetTabModifierClickSuppressionId = sheetId;
        return true;
    }

    private void SheetTabDragPointerMoved(object? sender, PointerEventArgs args)
    {
        if (_sheetTabDragId is not { } draggedId)
            return;

        var point = args.GetCurrentPoint(_sheetTabsHost);
        if (!point.Properties.IsLeftButtonPressed)
        {
            CompleteSheetTabPointerRelease();
            return;
        }

        var current = args.GetPosition(_sheetTabsHost);
        if (Math.Abs(current.X - _sheetTabDragStart.X) < SheetTabDragThreshold)
            return;

        _sheetTabModifierClickSuppressionId = null;

        var target = FindSheetTabDragTarget(current, draggedId);
        if (target is not { } dragTarget)
            return;

        var fromIndex = FindSheetIndex(draggedId);
        var targetIndex = FindSheetIndex(dragTarget.SheetId);
        var insertAfterTarget = current.X >= dragTarget.Bounds.Left + dragTarget.Bounds.Width / 2;
        var toIndex = SheetTabPointerPlanner.CalculateDropIndex(fromIndex, targetIndex, insertAfterTarget);
        if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex)
            return;

        _sheetTabDragPendingToIndex = toIndex;
    }

    private void SheetTabDragPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_sheetTabDragPointer is not null && args.Pointer != _sheetTabDragPointer)
            return;

        CompleteSheetTabPointerRelease();
    }

    private void CompleteSheetTabPointerRelease()
    {
        CommitSheetTabDragDrop();
        ClearSheetTabDragState();
        _sheetTabModifierClickSuppressionId = null;
    }

    private void SheetTabDragPointerCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        // Detach before Capture(null); Avalonia can synchronously raise PointerCaptureLost while
        // releasing capture, and re-entering this method would leave the drag state half-cleared.
        CommitSheetTabDragDrop();
        _sheetTabModifierClickSuppressionId = null;
        ClearSheetTabDragState(releasePointer: false);
    }

    private void CompleteSheetTabClick(SheetId sheetId)
    {
        if (_sheetTabModifierClickSuppressionId == sheetId)
        {
            _sheetTabModifierClickSuppressionId = null;
            return;
        }

        _sheetTabModifierClickSuppressionId = null;
        SelectSheet(sheetId);
    }

    private void CommitSheetTabDragDrop()
    {
        if (_sheetTabDragId is not { } draggedId || _sheetTabDragPendingToIndex is not { } toIndex)
            return;

        if (FindSheetIndex(draggedId) < 0)
            return;

        if (_session.ActiveSheet.Id != draggedId)
            SelectSheet(draggedId);

        var result = _session.MoveActiveSheetTo(toIndex);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("ShellLoc_MoveSheetFailed"));
            return;
        }

        RefreshShell(UiText.Format("MoveCopySheet_MovedStatus", _session.ActiveSheet.Name));
    }

    private void ClearSheetTabDragState(bool releasePointer = true)
    {
        _sheetTabsHost.PointerMoved -= SheetTabDragPointerMoved;
        _sheetTabsHost.PointerReleased -= SheetTabDragPointerReleased;
        _sheetTabsHost.PointerCaptureLost -= SheetTabDragPointerCaptureLost;

        var pointer = _sheetTabDragPointer;
        _sheetTabDragPointer = null;
        _sheetTabDragId = null;
        _sheetTabDragPendingToIndex = null;
        if (releasePointer)
            pointer?.Capture(null);
    }

    private (SheetId SheetId, Rect Bounds)? FindSheetTabDragTarget(Point position, SheetId draggedId)
    {
        if (_sheetTabsHost.Content is not Panel panel)
            return null;

        foreach (var child in panel.Children)
        {
            if (child is not Button button || button.Tag is not SheetId sheetId || sheetId == draggedId)
                continue;

            if (button.Bounds.Width <= 0 || button.Bounds.Height <= 0)
                continue;

            var origin = button.TranslatePoint(new Point(0, 0), _sheetTabsHost);
            if (origin is not { } topLeft)
                continue;

            var bounds = new Rect(topLeft, button.Bounds.Size);
            if (bounds.Contains(position))
                return (sheetId, bounds);
        }

        return null;
    }

    private int FindSheetIndex(SheetId sheetId)
    {
        for (var index = 0; index < _session.SheetTabs.Count; index++)
            if (_session.SheetTabs[index].Id == sheetId)
                return index;

        return -1;
    }

    private void ScrollSheetTabs(int direction)
    {
        var delta = Math.Sign(direction) * SheetTabNavScrollAmount;
        var offset = SheetTabPointerPlanner.CalculateHorizontalScrollOffset(
            _sheetTabsScroller.Offset.X,
            _sheetTabsScroller.Extent.Width,
            _sheetTabsScroller.Viewport.Width,
            delta);
        _sheetTabsScroller.Offset = new Vector(offset, _sheetTabsScroller.Offset.Y);
        UpdateSheetTabNavigationVisibility();
    }

    private void BeginShowActivateSheetDialogFromSheetNav()
    {
        if (_activateSheetDialogOpenOrPending)
            return;

        _activateSheetDialogOpenOrPending = true;
        RunGuarded(() => ShowActivateSheetDialogFromSheetNavAsync());
    }

    private async Task ShowActivateSheetDialogFromSheetNavAsync()
    {
        try
        {
            var targets = SheetDialogPlanner.BuildActivateSheetTargets(_session.Workbook);
            var selected = SheetDialogPlanner.FindInitialActivateSheetTarget(targets, _session.ActiveSheet.Id);
            var list = new ListBox
            {
                ItemsSource = targets,
                SelectedItem = selected,
                SelectionMode = SelectionMode.Single,
                MinHeight = 240,
                Background = Brushes.White,
            };
            AutomationProperties.SetName(list, UiText.Get("ActivateSheet_ListAutomationName"));
            AutomationProperties.SetAutomationId(list, FreeXAutomationIdCatalog.ActivateSheetList);
            AutomationProperties.SetHelpText(list, UiText.Get("ActivateSheet_ListHelpText"));

            var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, IsEnabled = selected is not null };
            var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
            ApplyDialogButtonChrome(ok, width: 90, isDefault: true);
            ApplyDialogButtonChrome(cancel, width: 90);
            AutomationProperties.SetName(ok, UiText.Get("ActivateSheet_OkAutomationName"));
            AutomationProperties.SetAutomationId(ok, FreeXAutomationIdCatalog.ActivateSheetOkButton);
            AutomationProperties.SetHelpText(ok, UiText.Get("ActivateSheet_OkHelpText"));
            AutomationProperties.SetName(cancel, UiText.Get("ActivateSheet_CancelAutomationName"));
            AutomationProperties.SetAutomationId(cancel, FreeXAutomationIdCatalog.ActivateSheetCancelButton);
            AutomationProperties.SetHelpText(cancel, UiText.Get("ActivateSheet_CancelHelpText"));

            var dialog = new Window
            {
                Title = UiText.Get("ActivateSheet_Title"),
                Width = 352,
                Height = 380,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            void Accept()
            {
                if (list.SelectedItem is not SheetDialogTarget target)
                    return;

                dialog.Close();
                _session.SelectSheet(target.SheetId);
                RefreshShell(UiText.Format("MainLoc_SelectedX", _session.ActiveSheet.Name));
            }

            list.SelectionChanged += (_, _) => ok.IsEnabled = list.SelectedItem is SheetDialogTarget;
            list.DoubleTapped += (_, args) =>
            {
                Accept();
                args.Handled = true;
            };
            ok.Click += (_, _) => Accept();
            cancel.Click += (_, _) => dialog.Close();

            dialog.Content = new StackPanel
            {
                Margin = new Thickness(10, 8, 10, 10),
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = UiText.Get("ActivateSheet_Title") + ":" },
                    list,
                    AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 10, 0, 0)),
                },
            };
            dialog.Opened += (_, _) => list.Focus();
            await dialog.ShowDialog(this);
        }
        finally
        {
            _activateSheetDialogOpenOrPending = false;
        }
    }

    private void HandleSheetTabNavigationPointerPressed(Button button, PointerPressedEventArgs args)
    {
        var point = args.GetCurrentPoint(button);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed)
            return;

        args.Handled = true;
        BeginShowActivateSheetDialogFromSheetNav();
    }
}
