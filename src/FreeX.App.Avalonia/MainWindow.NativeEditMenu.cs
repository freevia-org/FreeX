using Avalonia.Controls;
using Avalonia.VisualTree;

using FreeX.App.Presentation.Shell;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Native menu accelerators are dispatched independently of TextBox's routed key handling.
    // Resolve the editor before entering any workbook command: committing a formula edit just to
    // copy its selected text would change both the workbook and the clipboard unexpectedly.
    internal async Task ExecuteNativeEditMenuCommandAsync(NativeMenuItemId command)
    {
        if (GetNativeTextEditMenuTarget() is { } editor)
        {
            var canEdit = editor.IsEffectivelyEnabled && !editor.IsReadOnly;
            switch (command)
            {
                case NativeMenuItemId.Undo:
                    if (canEdit && editor.CanUndo)
                        editor.Undo();
                    break;
                case NativeMenuItemId.Redo:
                    if (canEdit && editor.CanRedo)
                        editor.Redo();
                    break;
                case NativeMenuItemId.Cut:
                    if (canEdit && editor.CanCut)
                        editor.Cut();
                    break;
                case NativeMenuItemId.Copy:
                    if (editor.IsEffectivelyEnabled && editor.CanCopy)
                        editor.Copy();
                    break;
                case NativeMenuItemId.Paste:
                    if (canEdit && editor.CanPaste)
                        editor.Paste();
                    break;
                case NativeMenuItemId.SelectAll:
                    if (editor.IsEffectivelyEnabled)
                        editor.SelectAll();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }

            RefreshNativeTextEditMenuAvailability();
            return;
        }

        // A modeless dialog can also focus a button or list. Its menu must not edit the
        // worksheet underneath simply because there is no text editor at that instant.
        if (IsNativeEditMenuFocusedInOwnedDialog())
            return;

        switch (command)
        {
            case NativeMenuItemId.Undo:
                UndoLastEdit();
                break;
            case NativeMenuItemId.Redo:
                RedoLastEdit();
                break;
            case NativeMenuItemId.Cut:
                await CutSelectedRangeToClipboardAsync();
                break;
            case NativeMenuItemId.Copy:
                await CopySelectedRangeToClipboardAsync();
                break;
            case NativeMenuItemId.Paste:
                await PasteClipboardTextAsync();
                break;
            case NativeMenuItemId.SelectAll:
                SelectCurrentRegionOrAll();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    internal void RefreshNativeTextEditMenuAvailability()
    {
        if (GetNativeTextEditMenuTarget() is not { } editor)
        {
            if (IsNativeEditMenuFocusedInOwnedDialog())
            {
                foreach (var item in new[] { _undoMenuItem, _redoMenuItem, _cutMenuItem,
                             _copyMenuItem, _pasteMenuItem, _selectAllMenuItem })
                    item.IsEnabled = false;
            }

            return;
        }

        var enabled = editor.IsEffectivelyEnabled;
        var canEdit = enabled && !editor.IsReadOnly;
        _undoMenuItem.IsEnabled = canEdit && editor.CanUndo;
        _redoMenuItem.IsEnabled = canEdit && editor.CanRedo;
        _cutMenuItem.IsEnabled = canEdit && editor.CanCut;
        _copyMenuItem.IsEnabled = enabled && editor.CanCopy;
        _pasteMenuItem.IsEnabled = canEdit && editor.CanPaste;
        _selectAllMenuItem.IsEnabled = enabled && !string.IsNullOrEmpty(editor.Text);
    }

    private TextBox? GetNativeTextEditMenuTarget()
    {
        if (FocusManager?.GetFocusedElement() is not global::Avalonia.Visual focused)
            return null;

        var editor = focused as TextBox ?? focused.GetVisualAncestors().OfType<TextBox>().FirstOrDefault();
        if (editor is null || TopLevel.GetTopLevel(editor) is not Window editorWindow)
            return null;

        // The focus manager is shared across windows. Modeless Find/Replace and other owned
        // dialogs use their owner's native menu, but an unrelated workbook's editor is not ours.
        return IsNativeEditMenuWindow(this, editorWindow) ? editor : null;
    }

    private bool IsNativeEditMenuFocusedInOwnedDialog() =>
        FocusManager?.GetFocusedElement() is global::Avalonia.Visual focused &&
        TopLevel.GetTopLevel(focused) is Window focusedWindow &&
        !ReferenceEquals(this, focusedWindow) && IsNativeEditMenuWindow(this, focusedWindow);

    private static bool IsNativeEditMenuWindow(Window owner, Window target) =>
        ReferenceEquals(owner, target) ||
        owner.OwnedWindows.Any(child => IsNativeEditMenuWindow(child, target));
}
