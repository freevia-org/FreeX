using System.Diagnostics.CodeAnalysis;

namespace FreeW.App.Presentation.Editing;

public static class DocumentNoteNavigationPlanner
{
    /// <summary>
    /// Selects the adjacent marker from a document-ordered native marker list, wrapping at either end.
    /// The host supplies only its framework-specific comparison with the current caret.
    /// </summary>
    public static bool TryFindAdjacent<T>(
        IReadOnlyList<T> orderedMarkers,
        Func<T, int> compareToCaret,
        bool previous,
        [MaybeNullWhen(false)] out T target)
    {
        ArgumentNullException.ThrowIfNull(orderedMarkers);
        ArgumentNullException.ThrowIfNull(compareToCaret);

        if (orderedMarkers.Count == 0)
        {
            target = default;
            return false;
        }

        if (previous)
        {
            for (var index = orderedMarkers.Count - 1; index >= 0; index--)
            {
                if (compareToCaret(orderedMarkers[index]) < 0)
                {
                    target = orderedMarkers[index];
                    return true;
                }
            }

            target = orderedMarkers[^1];
            return true;
        }

        foreach (var marker in orderedMarkers)
        {
            if (compareToCaret(marker) > 0)
            {
                target = marker;
                return true;
            }
        }

        target = orderedMarkers[0];
        return true;
    }
}
