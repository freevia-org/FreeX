using System.Windows;
using System.Windows.Documents;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Editing;

public sealed partial class DocumentView
{
    /// <summary>
    /// Resolves the footnote/endnote reference mark under a point in this editor (editor coordinates),
    /// so the shell can open the notes pane at that note when the mark is double-clicked.
    /// </summary>
    public bool TryGetNoteReferenceAt(Point point, out bool footnote, out int id)
    {
        footnote = false;
        id = 0;
        var position = GetPositionFromPoint(point, snapToText: true);
        if (position is null || !TryGetNoteReferenceAt(position, out footnote, out id))
            return false;

        // snapToText resolves any point to the nearest text position, so a double-click in the empty
        // area after a line-ending mark would otherwise activate it. Require the point to be on the mark.
        return NoteMarkerBoundsContain(position, point);
    }

    /// <summary>Resolves the reference mark that contains or directly touches <paramref name="position"/>.</summary>
    public bool TryGetNoteReferenceAt(TextPointer position, out bool footnote, out int id)
    {
        ArgumentNullException.ThrowIfNull(position);
        footnote = false;
        id = 0;
        foreach (var candidate in NoteMarkerCandidates(position))
        {
            switch (candidate?.Tag)
            {
                case FootnoteMarker marker:
                    footnote = true;
                    id = marker.FootnoteId;
                    return true;
                case EndnoteMarker marker:
                    footnote = false;
                    id = marker.EndnoteId;
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<WpfRun?> NoteMarkerCandidates(TextPointer position)
    {
        yield return position.Parent as WpfRun;
        yield return position.GetAdjacentElement(LogicalDirection.Forward) as WpfRun;
        yield return position.GetAdjacentElement(LogicalDirection.Backward) as WpfRun;
    }

    private bool NoteMarkerBoundsContain(TextPointer position, Point point)
    {
        WpfRun? marker = null;
        foreach (var candidate in NoteMarkerCandidates(position))
        {
            if (candidate?.Tag is FootnoteMarker or EndnoteMarker)
            {
                marker = candidate;
                break;
            }
        }

        if (marker is null)
            return false;

        var start = marker.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var end = marker.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        if (start.IsEmpty || end.IsEmpty)
            return true; // not laid out (headless): trust the text position
        var bounds = Rect.Union(start, end);
        bounds.Inflate(3, 3);
        return bounds.Contains(point);
    }
}
