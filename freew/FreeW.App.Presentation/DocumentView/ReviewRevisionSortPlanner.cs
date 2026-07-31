using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>Sort orders exposed by the Reviewing Pane in both FreeW hosts.</summary>
public enum ReviewRevisionSortOrder
{
    Sequence,
    Author,
    Kind,
    Date,
}

/// <summary>
/// Applies the WPF Reviewing Pane ordering contract without mutating the document model.
/// </summary>
public static class ReviewRevisionSortPlanner
{
    public static IReadOnlyList<RevisionEntry> Sort(
        IReadOnlyList<RevisionEntry> entries,
        ReviewRevisionSortOrder order)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return order switch
        {
            ReviewRevisionSortOrder.Sequence => entries,
            ReviewRevisionSortOrder.Author => entries
                .OrderBy(entry => entry.Author ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.BlockIndex)
                .ToList(),
            ReviewRevisionSortOrder.Kind => entries
                .OrderBy(entry => (int)entry.Kind)
                .ThenBy(entry => entry.BlockIndex)
                .ToList(),
            ReviewRevisionSortOrder.Date => entries
                .OrderBy(entry => entry.DateXml, StringComparer.Ordinal)
                .ThenBy(entry => entry.BlockIndex)
                .ToList(),
            _ => entries,
        };
    }
}
