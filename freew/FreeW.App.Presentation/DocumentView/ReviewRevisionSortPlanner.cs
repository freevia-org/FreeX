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

/// <summary>One renderer-neutral option in the Reviewing Pane's Sort menu.</summary>
public sealed record ReviewRevisionSortOption(
    ReviewRevisionSortOrder Order,
    string Label);

/// <summary>
/// Applies the WPF Reviewing Pane ordering contract without mutating the document model.
/// </summary>
public static class ReviewRevisionSortPlanner
{
    public static IReadOnlyList<ReviewRevisionSortOption> Options { get; } =
    [
        new(ReviewRevisionSortOrder.Sequence, "By Sequence"),
        new(ReviewRevisionSortOrder.Author, "By Author"),
        new(ReviewRevisionSortOrder.Kind, "By Type"),
        new(ReviewRevisionSortOrder.Date, "By Date"),
    ];

    public static int IndexOf(ReviewRevisionSortOrder order)
    {
        for (var index = 0; index < Options.Count; index++)
        {
            if (Options[index].Order == order)
                return index;
        }

        throw new ArgumentOutOfRangeException(nameof(order));
    }

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
