using System.Windows.Controls;
using System.Windows.Documents;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Shared cross-page undo coordinator for <see cref="PaginatedEditorPanel"/>.
///
/// <para>
/// <strong>Approach:</strong> Each page box's <see cref="RichTextBox"/> has its own native
/// WPF undo stack (ApplicationCommands.Undo / Redo), which is separate from the model-level
/// <see cref="DocumentCommandBus"/> used by the continuous editor.  For the paged editor a
/// panel-level ordered log of undo snapshots is maintained: whenever the user edits a box,
/// the coordinator captures a full model snapshot <em>before</em> the edit (taken at the next
/// TextChanged event, i.e. the snapshot is the pre-edit model from the last commit).  Ctrl+Z
/// walks this log in reverse, restoring the pre-edit snapshot; Ctrl+Y re-applies.
/// </para>
///
/// <para>
/// <strong>Why a snapshot log rather than per-box undo routing:</strong>
/// <list type="bullet">
///   <item>The DocumentCommandBus is designed for structured model-level commands (InsertBlock,
///   ReplaceBlocks, etc.), not for free-form rich-text edits.  Wiring each RichTextBox keystroke
///   into the command bus would require serializing every WPF text edit into a reversible
///   IDocumentCommand — significant complexity for what is a DEBUG-only editor mode.</item>
///   <item>The repagination loop already takes full model snapshots (commit → shard → rebuild),
///   so snapshotting is a natural fit.</item>
///   <item>Correctness is provable: restoring a complete model snapshot always gives the exact
///   pre-edit state including cross-box edits in any order.</item>
/// </list>
/// </para>
///
/// <para>
/// <strong>Snapshot timing:</strong> The coordinator registers a TextChanged handler on each
/// page box body.  On the first TextChanged after a quiescent state (no pending undo in
/// progress), it calls <see cref="CaptureSnapshot"/> which commits the current boxes and
/// pushes a snapshot of the resulting model.  Rapid keystrokes within the same 300 ms
/// debounce window do NOT push multiple snapshots — only the pre-burst state is captured.
/// </para>
///
/// <para>
/// <strong>Undo/Redo:</strong> <see cref="Undo"/> pops the top snapshot and rebuilds the
/// panel from it.  <see cref="PaginatedEditorPanel.Rebuild"/> discards the old boxes and
/// constructs new ones with the restored content already set, so it never raises a TextChanged
/// of its own -- only the burst flag needs resetting so the next real edit captures a fresh
/// pre-edit snapshot.  <see cref="Redo"/> re-applies the snapshot that was just undone.
/// </para>
///
/// <para>Must be called on the UI/STA thread.</para>
/// </summary>
internal sealed class CrossPageUndoCoordinator
{
    private const int MaxUndoDepth = 50;

    // ── snapshot stacks ───────────────────────────────────────────────────────────────────────────

    /// <summary>Each entry is a complete model block list captured just before an edit burst.</summary>
    private readonly Stack<ModelSnapshot> _undoStack = new();
    private readonly Stack<ModelSnapshot> _redoStack = new();

    // ── state ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether a pre-edit snapshot for the current burst has already been captured (so we don't
    /// capture it again for every keystroke in the same burst).
    /// </summary>
    private bool _pendingBurstCaptured;

    // ── back-reference set by panel ───────────────────────────────────────────────────────────────
    private PaginatedEditorPanel? _panel;
    private DocumentView? _sourceEditor;

    // ── public surface ────────────────────────────────────────────────────────────────────────────

    internal bool CanUndo => _undoStack.Count > 0;
    internal bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Attaches the coordinator to <paramref name="panel"/>.  Must be called after the panel is
    /// fully built.  Hooks TextChanged on every box and registers the keyboard handler.
    /// </summary>
    internal void Attach(PaginatedEditorPanel panel, DocumentView sourceEditor)
    {
        _panel = panel;
        _sourceEditor = sourceEditor;

        foreach (var box in panel.PageBoxes)
            HookBox(box);
    }

    /// <summary>
    /// Called by <see cref="PaginatedEditorPanel"/> after a Repaginate rebuilds the box list.
    /// Re-hooks TextChanged on new boxes.
    /// </summary>
    internal void ReAttach(IReadOnlyList<PageBox> newBoxes)
    {
        // Reset burst flag so the first edit after repagination captures fresh pre-edit state.
        _pendingBurstCaptured = false;

        foreach (var box in newBoxes)
            HookBox(box);
    }

    internal void HookBox(PageBox box)
    {
        box.Body.TextChanged += OnBodyTextChanged;
        box.Body.PreviewKeyDown += OnBodyPreviewKeyDown;
    }

    internal void UnhookBox(PageBox box)
    {
        box.Body.TextChanged -= OnBodyTextChanged;
        box.Body.PreviewKeyDown -= OnBodyPreviewKeyDown;
    }

    // ── undo / redo ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reverts the most recent edit burst.  Rebuilds the panel from the pre-burst snapshot.
    /// Returns false when there is nothing to undo.
    /// </summary>
    internal bool Undo()
    {
        if (_panel is null || _sourceEditor is null || !_undoStack.TryPop(out var snapshot))
            return false;

        // Push the current state for redo.
        _redoStack.Push(CaptureCurrentSnapshot());

        // Reset the burst flag so the next real edit captures a fresh pre-edit snapshot from the
        // just-restored state.  Rebuild() below discards and reconstructs every PageBox with its
        // restored content already set at construction time, so it never raises a TextChanged of
        // its own -- there is nothing here that needs suppressing.
        _pendingBurstCaptured = false;

        RestoreSnapshot(snapshot);
        return true;
    }

    /// <summary>
    /// Re-applies the most recently undone edit.  Returns false when there is nothing to redo.
    /// </summary>
    internal bool Redo()
    {
        if (_panel is null || _sourceEditor is null || !_redoStack.TryPop(out var snapshot))
            return false;

        // Push the current state back onto the undo stack.
        _undoStack.Push(CaptureCurrentSnapshot());

        // See the matching comment in Undo(): Rebuild() never raises a synthetic TextChanged, so
        // only the burst flag needs resetting here.
        _pendingBurstCaptured = false;

        RestoreSnapshot(snapshot);
        return true;
    }

    // ── burst-end notification (called by panel after debounce timer fires) ───────────────────────

    /// <summary>
    /// Called by <see cref="PaginatedEditorPanel.Repaginate"/> just before it commits and
    /// rebuilds.  This resets the burst flag so the next edit will start a fresh undo unit.
    /// </summary>
    internal void OnRepaginationStarting()
    {
        _pendingBurstCaptured = false;
    }

    // ── private helpers ───────────────────────────────────────────────────────────────────────────

    private void OnBodyTextChanged(object? sender, TextChangedEventArgs e)
    {
        // Capture the pre-edit snapshot only once per burst.
        if (!_pendingBurstCaptured)
        {
            _pendingBurstCaptured = true;

            // Capture the current committed model as the pre-edit state.
            // We commit first to ensure the snapshot is fully consistent.
            if (_panel is not null && _sourceEditor is not null)
            {
                PaginatedCommitCoordinator.Commit(_panel, _sourceEditor);
                _undoStack.Push(CaptureCurrentSnapshot());
                _redoStack.Clear(); // new edit invalidates redo

                // Trim to depth limit.
                while (_undoStack.Count > MaxUndoDepth)
                {
                    // Stack doesn't have a direct trim; drain into temp, drop oldest, refill.
                    var tmp = _undoStack.ToArray(); // [0] = top (newest)
                    _undoStack.Clear();
                    foreach (var s in tmp.Take(MaxUndoDepth))
                        _undoStack.Push(s);
                    break;
                }
            }
        }
    }

    private void OnBodyPreviewKeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Z &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0 &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == 0)
        {
            if (Undo())
                e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Y &&
                 (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
        {
            if (Redo())
                e.Handled = true;
        }
    }

    private ModelSnapshot CaptureCurrentSnapshot()
    {
        // The model was already committed immediately before this call in the TextChanged handler,
        // or is fresh from a Repaginate.
        var blocks = _sourceEditor!.Model.Blocks
            .Select(b => b) // reference-preserve — same object references are fine for snapshot
            .ToList();
        return new ModelSnapshot(blocks);
    }

    private void RestoreSnapshot(ModelSnapshot snapshot)
    {
        if (_panel is null || _sourceEditor is null)
            return;

        // Replace model blocks with the snapshot.
        _sourceEditor.Model.Blocks.Clear();
        foreach (var block in snapshot.Blocks)
            _sourceEditor.Model.Blocks.Add(block);
        if (_sourceEditor.Model.Blocks.Count == 0)
            _sourceEditor.Model.Blocks.Add(new FreeW.Core.Model.Paragraph());

        // Rebuild the panel from the restored model.
        _panel.Rebuild();
    }

    // ── snapshot record ───────────────────────────────────────────────────────────────────────────

    private sealed class ModelSnapshot(IReadOnlyList<FreeW.Core.Model.Block> blocks)
    {
        internal IReadOnlyList<FreeW.Core.Model.Block> Blocks { get; } = blocks;
    }
}
