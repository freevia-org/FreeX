using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using FreeX.App.Services;
using FreeX.Core.Model;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

// WPF parity (f7abb1094f "edit tab names inline", 7614981719 "open sheet menu on right click"):
// double-clicking a sheet tab swaps the tab label for an in-place TextBox (Enter commits through
// the session rename, Escape cancels, no dialog); a right-press selects the tab and then opens
// the tab menu on the refreshed tab, because the selection refresh replaces the pressed button.
public sealed partial class MainWindow
{
    private SheetId? _inlineSheetTabRenameId;
    private string _inlineSheetTabRenameText = string.Empty;
    private TextBox? _inlineSheetTabRenameEditor;

    internal bool IsInlineSheetTabRenameActive => _inlineSheetTabRenameId is not null;

    internal string? InlineSheetTabRenameText =>
        _inlineSheetTabRenameId is null ? null : _inlineSheetTabRenameText;

    internal bool IsInlineSheetTabRenameActiveFor(SheetId sheetId) => _inlineSheetTabRenameId == sheetId;

    internal bool BeginInlineSheetTabRename(SheetId sheetId)
    {
        if (_isOpening || _isSaving)
            return false;

        if (!SelectSheetForContextCommand(sheetId))
            return false;

        if (_session.Workbook.GetSheet(sheetId) is not { } sheet)
            return false;

        _inlineSheetTabRenameId = sheetId;
        _inlineSheetTabRenameText = sheet.Name;
        RebuildSheetTabsForInlineRename();
        FocusInlineSheetTabRenameEditor();
        return true;
    }

    internal void SetInlineSheetTabRenameText(string? text)
    {
        if (_inlineSheetTabRenameId is null)
            return;

        _inlineSheetTabRenameText = text ?? string.Empty;
        if (_inlineSheetTabRenameEditor is { } editor &&
            !string.Equals(editor.Text, _inlineSheetTabRenameText, StringComparison.Ordinal))
        {
            editor.Text = _inlineSheetTabRenameText;
        }
    }

    internal bool CommitInlineSheetTabRename()
    {
        if (_inlineSheetTabRenameId is not { } sheetId)
            return false;

        var name = _inlineSheetTabRenameEditor?.Text ?? _inlineSheetTabRenameText;
        var current = _session.Workbook.GetSheet(sheetId)?.Name;
        if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, current, StringComparison.Ordinal))
        {
            if (_session.ActiveSheet.Id != sheetId)
                _session.SelectSheet(sheetId);

            var oldName = _session.ActiveSheet.Name;
            var result = _session.RenameActiveSheet(name);
            if (!result.Success)
            {
                // WPF leaves the editor open when the rename is rejected so the name can be fixed.
                ShowEditIssue(result.ErrorMessage ?? UiText.Get("MainLoc_RenameSheetFailed"));
                FocusInlineSheetTabRenameEditor();
                return false;
            }

            FinishInlineSheetTabRename(
                sheetId,
                $"Renamed {oldName} to {_session.ActiveSheet.Name}");
            return true;
        }

        FinishInlineSheetTabRename(sheetId, status: null);
        return true;
    }

    internal bool CancelInlineSheetTabRename()
    {
        if (_inlineSheetTabRenameId is not { } sheetId)
            return false;

        FinishInlineSheetTabRename(sheetId, status: null);
        return true;
    }

    private void FinishInlineSheetTabRename(SheetId sheetId, string? status)
    {
        _inlineSheetTabRenameId = null;
        _inlineSheetTabRenameText = string.Empty;
        _inlineSheetTabRenameEditor = null;
        if (status is not null)
            RefreshShell(status);
        else
            RebuildSheetTabsForInlineRename();

        Dispatcher.UIThread.Post(() => FocusSheetTab(sheetId), DispatcherPriority.Input);
    }

    private void RebuildSheetTabsForInlineRename()
    {
        _sheetTabsHost.Content = BuildSheetTabs();
        UpdateSheetTabNavigationVisibility();
    }

    private void FocusInlineSheetTabRenameEditor()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_inlineSheetTabRenameEditor is not { } editor)
                    return;

                editor.Focus();
                editor.SelectAll();
            },
            DispatcherPriority.Input);
    }

    /// <summary>
    /// Creates the in-place rename editor for <paramref name="tab"/> when it is the tab being
    /// renamed, otherwise returns null. A rename for a sheet that no longer exists is dropped.
    /// </summary>
    private TextBox? CreateInlineSheetTabRenameEditor(WorkbookSheetTab tab)
    {
        if (_inlineSheetTabRenameId is not { } renamingId)
            return null;

        if (_session.Workbook.GetSheet(renamingId) is null)
        {
            _inlineSheetTabRenameId = null;
            _inlineSheetTabRenameEditor = null;
            return null;
        }

        if (renamingId != tab.Id)
            return null;

        var editor = new TextBox
        {
            Name = "SheetTabRenameEditor",
            Text = _inlineSheetTabRenameText,
            MinWidth = 48,
            MinHeight = 0,
            Padding = new Thickness(1, 0),
            FontSize = 13,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(editor, UiText.Get("SheetName_RenameSheet"));
        AutomationProperties.SetAutomationId(editor, "SheetTabRenameEditor");
        editor.TextChanged += (_, _) => _inlineSheetTabRenameText = editor.Text ?? string.Empty;
        editor.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) => HandleInlineSheetTabRenameKeyDown(args),
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _inlineSheetTabRenameEditor = editor;
        return editor;
    }

    internal void HandleInlineSheetTabRenameKeyDown(KeyEventArgs args)
    {
        if (args.Key == Key.Enter)
        {
            args.Handled = true;
            CommitInlineSheetTabRename();
        }
        else if (args.Key == Key.Escape)
        {
            args.Handled = true;
            CancelInlineSheetTabRename();
        }
    }

    /// <summary>
    /// Right-press on a sheet tab: select it (keeping an existing group), then open the tab menu on
    /// the refreshed tab. The selection refresh rebuilds the tab buttons, so the menu would
    /// otherwise belong to a detached button and never show (WPF 7614981719).
    /// </summary>
    internal bool HandleSheetTabRightPress(SheetId sheetId)
    {
        if (SelectSheetForContextCommand(sheetId))
        {
            Dispatcher.UIThread.Post(() => OpenSheetTabContextMenuForMouse(sheetId), DispatcherPriority.Input);
            return true;
        }

        return false;
    }

    internal void OpenSheetTabContextMenuForMouse(SheetId sheetId)
    {
        if (FindSheetTabButton(sheetId) is { ContextMenu: { } contextMenu } button && !contextMenu.IsOpen)
            contextMenu.Open(button);
    }
}
