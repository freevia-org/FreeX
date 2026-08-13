using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationNotesPanePlan(
    string Text,
    PresentationNotesPagePreviewPlan Preview);

public sealed record PresentationNotesPaneMutationResult(
    bool Changed,
    PresentationNotesPanePlan Plan);

/// <summary>
/// Owns notes-pane text projection, no-op detection, and undoable note mutation.
/// Hosts retain their native text control, focus, and accessibility metadata.
/// </summary>
public sealed class PresentationNotesPaneSession
{
    private readonly Func<EditingSession> _getEditor;

    public PresentationNotesPaneSession(Func<EditingSession> getEditor)
    {
        _getEditor = getEditor ?? throw new ArgumentNullException(nameof(getEditor));
    }

    public PresentationNotesPanePlan BuildProjection()
    {
        var editor = _getEditor();
        return new PresentationNotesPanePlan(
            FormatText(editor.CurrentSlideNotes),
            PresentationNotesPagePreviewPlanner.Build(
                editor.Presentation,
                editor.CurrentSlideIndex));
    }

    public PresentationNotesPaneMutationResult ApplyText(string? text)
    {
        var editor = _getEditor();
        var normalized = text ?? string.Empty;
        var changed = !string.Equals(
            normalized,
            FormatText(editor.CurrentSlideNotes),
            StringComparison.Ordinal);
        if (changed)
            editor.SetCurrentSlideNotesText(normalized);

        return new PresentationNotesPaneMutationResult(changed, BuildProjection());
    }

    public static string FormatText(TextBody? notes) => notes is null
        ? string.Empty
        : string.Join(
            Environment.NewLine,
            notes.Paragraphs.Select(paragraph =>
                string.Concat(paragraph.Runs.Select(run => run.Text))));
}
