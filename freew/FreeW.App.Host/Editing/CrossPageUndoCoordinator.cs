using System.Windows.Input;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Undo/redo for <see cref="PaginatedEditorPanel"/>, routed through the source
/// <see cref="DocumentView"/>'s <see cref="FreeW.Core.Model.DocumentCommandBus"/> -- the same history
/// the continuous editor and every ribbon command use.
///
/// <para>
/// <strong>Unit of undo:</strong> page boxes are plain <see cref="System.Windows.Controls.RichTextBox"/>
/// instances whose native undo is per box; typing is collected there and written to the model as one
/// <see cref="FreeW.Core.Model.ReplaceBlocksCommand"/> per edit burst by
/// <see cref="PaginatedCommitCoordinator.Commit"/> (when the re-pagination debounce fires, or when
/// anything asks the hidden editor to commit). A burst that spans several pages -- a cross-page delete,
/// a paste that pushes text onto the next page -- is therefore a single undo unit, and reverting it
/// restores the exact pre-burst block instances, so the document is exactly equal to its prior state.
/// </para>
///
/// <para>
/// <strong>Undo/Redo:</strong> pending edits are committed first (so Ctrl+Z undoes what was just typed,
/// as in Word), then the bus reverts/reapplies and the panel rebuilds its pages from the model,
/// keeping the caret where it was. Because the history lives on the bus rather than here, it survives
/// leaving and re-entering the paged view.
/// </para>
///
/// <para>Must be called on the UI/STA thread.</para>
/// </summary>
internal sealed class CrossPageUndoCoordinator
{
    private PaginatedEditorPanel? _panel;
    private DocumentView? _sourceEditor;

    internal bool CanUndo =>
        _panel is not null && _sourceEditor is not null
        && (_panel.HasPendingBodyEdits || _sourceEditor.Commands.CanUndo);

    internal bool CanRedo =>
        _panel is not null && _sourceEditor is not null
        && !_panel.HasPendingBodyEdits && _sourceEditor.Commands.CanRedo;

    /// <summary>Attaches the coordinator to <paramref name="panel"/> and hooks Ctrl+Z / Ctrl+Y on every box.</summary>
    internal void Attach(PaginatedEditorPanel panel, DocumentView sourceEditor)
    {
        _panel = panel;
        _sourceEditor = sourceEditor;

        foreach (var box in panel.PageBoxes)
            HookBox(box);
    }

    /// <summary>Re-hooks the keyboard handler after the panel rebuilt its page boxes.</summary>
    internal void ReAttach(IReadOnlyList<PageBox> newBoxes)
    {
        foreach (var box in newBoxes)
            HookBox(box);
    }

    internal void HookBox(PageBox box) => box.Body.PreviewKeyDown += OnBodyPreviewKeyDown;

    internal void UnhookBox(PageBox box) => box.Body.PreviewKeyDown -= OnBodyPreviewKeyDown;

    /// <summary>Kept for the panel's rebuild sequence; the bus needs no per-rebuild state.</summary>
    internal void OnRepaginationStarting()
    {
    }

    /// <summary>Commits pending page edits, then reverts the most recent undo unit. False when there is nothing to undo.</summary>
    internal bool Undo()
    {
        if (_panel is null || _sourceEditor is null)
            return false;

        _panel.CommitPendingEdits();
        var commands = _sourceEditor.Commands;
        if (!commands.CanUndo)
            return false;

        var caret = _panel.CaptureCaret();
        _panel.RunOwnModelMutation(() => commands.Undo());
        _panel.Rebuild(caret);
        return true;
    }

    /// <summary>Re-applies the most recently undone unit. False when there is nothing to redo.</summary>
    internal bool Redo()
    {
        if (_panel is null || _sourceEditor is null)
            return false;

        // New typing since the undo invalidates redo, exactly as on the bus itself.
        _panel.CommitPendingEdits();
        var commands = _sourceEditor.Commands;
        if (!commands.CanRedo)
            return false;

        var caret = _panel.CaptureCaret();
        _panel.RunOwnModelMutation(() => commands.Redo());
        _panel.Rebuild(caret);
        return true;
    }

    private void OnBodyPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Control) == 0)
            return;

        if (e.Key == Key.Z && (modifiers & ModifierKeys.Shift) == 0)
        {
            // Handled even when there is nothing to undo, so the box's own native undo (which knows
            // nothing about the model or the other pages) never runs.
            Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
        }
    }
}
