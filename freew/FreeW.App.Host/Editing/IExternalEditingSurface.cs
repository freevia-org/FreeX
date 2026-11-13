namespace FreeW.App.Host.Editing;

/// <summary>
/// An editing surface that temporarily owns the body of a <see cref="DocumentView"/>'s model while
/// the view itself is hidden -- today the paged (Print Layout page-by-page) editor,
/// <see cref="PaginatedEditorPanel"/>.
///
/// <para>
/// While one is attached (<see cref="DocumentView.ExternalEditingSurface"/>), the hidden view's own
/// FlowDocument is no longer the authority for the body text, so every path that would normally read
/// it back -- <see cref="DocumentView.CommitToModel"/> (Save, AutoSave, Print, every ribbon command
/// that commits first) -- asks the surface to flush instead, and the view's Undo/Redo route through
/// the surface so they act on the same <see cref="FreeW.Core.Model.DocumentCommandBus"/> history the
/// continuous editor uses. Without this, Save in paged mode re-read the stale hidden surface and
/// silently overwrote everything typed on the pages.
/// </para>
/// </summary>
internal interface IExternalEditingSurface
{
    /// <summary>Writes any edits not yet in the model into it as one undoable unit.</summary>
    void CommitPendingEdits();

    bool CanUndo { get; }

    bool CanRedo { get; }

    void Undo();

    void Redo();

    /// <summary>The hidden view loaded a different document; the surface must stop writing to the old one.</summary>
    void OnSourceModelReloaded();

    /// <summary>
    /// The editor the user is typing in (its selection and command target): native editing commands
    /// with no model-level path -- formatting the caret's next typed text, say -- act on it rather than
    /// on the hidden view. Null when the surface has no active editor.
    /// </summary>
    System.Windows.Controls.RichTextBox? ActiveEditor { get; }
}
