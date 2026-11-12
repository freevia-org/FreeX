using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation;
using FreeW.App.Presentation.Panes;

namespace FreeW.App.Host;

/// <summary>
/// Word-like notes editing on top of the docked Notes pane: the Footnotes/Endnotes selector, opening the
/// pane at one note (after Insert Footnote/Endnote, or a double-click on a reference mark), and committing
/// the rich sub-editor back to the note as ONE undoable document edit whenever the user leaves it.
/// All note policy stays in <see cref="DocumentNotesPaneSession"/>; this file is toolkit plumbing only.
/// </summary>
public sealed partial class MainWindow
{
    private ComboBox _notesFilterBox = null!;
    private bool _notesEditPending;
    private bool _notesEditorLoading;
    private bool _notesFilterSyncing;

    private static readonly DocumentNotesPaneFilter[] NotesFilterOrder =
    [
        DocumentNotesPaneFilter.All,
        DocumentNotesPaneFilter.Footnotes,
        DocumentNotesPaneFilter.Endnotes,
    ];

    // Builds the Footnotes/Endnotes selector shown under the pane heading and wires the edit tracking of
    // the rich sub-editor. Called once from BuildNotesPane after the sub-editor exists.
    private UIElement BuildNotesFilterSelector()
    {
        _notesFilterBox = new ComboBox
        {
            Margin = new Thickness(8, 0, 8, 4),
            MinWidth = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = NotesFilterOrder.Select(FreeWUiTextCatalog.NotesFilterLabel).ToArray(),
            SelectedIndex = 0,
        };
        System.Windows.Automation.AutomationProperties.SetName(
            _notesFilterBox,
            FreeWUiTextCatalog.NotesFilterName);
        _notesFilterBox.SelectionChanged += (_, _) =>
        {
            if (_notesFilterSyncing || _notesFilterBox.SelectedIndex < 0)
                return;
            ApplyPendingNoteEdit();
            RenderNotesOutcome(_documentNotesPaneSession.SetFilter(NotesFilterOrder[_notesFilterBox.SelectedIndex]));
        };

        _notesSubEditor.TextChanged += (_, _) =>
        {
            if (!_notesEditorLoading)
                _notesEditPending = true;
        };
        // Leaving the note (back to the body, another note, the ribbon, ...) commits it, the way Word's
        // notes pane has no explicit "apply": the note simply is what was typed.
        _notesSubEditor.LostKeyboardFocus += (_, e) =>
        {
            if (e.NewFocus is DependencyObject target && IsWithinNotesPane(target))
                return;
            ApplyPendingNoteEdit();
        };
        _editor.PreviewMouseLeftButtonDown += OnEditorPreviewMouseDownForNotes;
        return _notesFilterBox;
    }

    private bool IsWithinNotesPane(DependencyObject target)
    {
        for (DependencyObject? current = target; current is not null; current = ParentOf(current))
        {
            if (ReferenceEquals(current, _notesPane))
                return true;
        }

        return false;

        static DependencyObject? ParentOf(DependencyObject element) =>
            element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
    }

    private void SyncNotesFilterSelector(DocumentNotesPaneFilter filter)
    {
        var index = Array.IndexOf(NotesFilterOrder, filter);
        if (_notesFilterBox is null || _notesFilterBox.SelectedIndex == index)
            return;
        _notesFilterSyncing = true;
        try
        {
            _notesFilterBox.SelectedIndex = index;
        }
        finally
        {
            _notesFilterSyncing = false;
        }
    }

    // Loads a note wrapper document into the sub-editor without counting the load as a user edit.
    private void LoadNoteIntoSubEditor(FreeW.Core.Model.TextDocument editorDocument)
    {
        _notesEditorLoading = true;
        try
        {
            ConfigureFieldEvaluationContext(_notesSubEditor);
            _notesSubEditor.LoadModel(editorDocument);
        }
        finally
        {
            _notesEditorLoading = false;
            _notesEditPending = false;
        }
    }

    /// <summary>
    /// Commits the sub-editor's unsaved edits to the selected note as one undoable document edit.
    /// Returns true when an edit was actually written. A no-op when nothing was typed.
    /// </summary>
    private bool ApplyPendingNoteEdit()
    {
        if (!_notesEditPending || _notesSubEditor is null)
            return false;

        _notesEditPending = false;
        _notesSubEditor.CommitToModel();
        var outcome = _documentNotesPaneSession.Apply(_notesSubEditor.Model.Blocks);
        if (outcome.MutationApplied)
            MarkDirtyOutsideBodyUndo();
        return outcome.MutationApplied;
    }

    /// <summary>
    /// Opens the Notes pane (if hidden) at one note and puts the caret in its rich editing region.
    /// Used right after Insert Footnote/Endnote and when a reference mark is double-clicked.
    /// </summary>
    private void OpenNotesPaneAtNote(bool footnote, int id)
    {
        ApplyPendingNoteEdit();
        if (!_notesPaneVisible)
        {
            _notesPaneVisible = true;
            _notesPane.Visibility = Visibility.Visible;
            _stateStore.SetChecked("freew.show-notes", true);
        }

        RenderNotesOutcome(_documentNotesPaneSession.ShowAndSelect(footnote, id));
        if (!_documentNotesPaneSession.State.HasSelection)
            return;

        _notesSubEditor.CaretPosition = _notesSubEditor.Document.ContentEnd;
        // Focus after layout: the pane may have just become visible, and a collapsed element cannot
        // take keyboard focus until the next layout pass.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (_notesPaneVisible && _notesSubEditor.IsVisible)
                    _notesSubEditor.Focus();
            }));
    }

    // Double-clicking a footnote/endnote reference mark opens the notes pane at that note (Word).
    private void OnEditorPreviewMouseDownForNotes(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
            return;
        if (!_editor.TryGetNoteReferenceAt(e.GetPosition(_editor), out var footnote, out var id))
            return;

        e.Handled = true;
        _editor.CommitToModel();
        OpenNotesPaneAtNote(footnote, id);
    }

    // Flushes docked sub-editor content that has not been written to the document yet, so Save never
    // persists a document that is missing what the user can see in a pane.
    private void CommitPendingPaneEdits()
    {
        ApplyPendingNoteEdit();
    }
}
