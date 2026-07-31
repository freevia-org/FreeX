using System;
using System.Collections.Generic;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// The available sort orders for the Reviewing Pane's revisions list. Mirrors Word's
/// "Sort By" options in the Reviewing Pane header menu.
/// </summary>
public enum RevisionSortOrder
{
    /// <summary>Reading order (document position / insertion sequence). This is the default.</summary>
    Sequence,
    /// <summary>Alphabetical by author name (case-insensitive), stable within the same author.</summary>
    Author,
    /// <summary>By revision kind: Insertions first, Deletions second, Formatting last.</summary>
    Kind,
    /// <summary>By revision date (W3CDTF / ISO-8601 string, lexicographic; null dates sort last).</summary>
    Date,
}

/// <summary>
/// WPF compatibility facade over the renderer-neutral Reviewing Pane sort planner.
/// </summary>
public static class RevisionSortComparer
{
    /// <summary>
    /// Returns the entries sorted according to <paramref name="order"/>. The original list is never
    /// mutated; a new list is returned. <see cref="RevisionSortOrder.Sequence"/> returns the same
    /// order as the input (reading order).
    /// </summary>
    public static IReadOnlyList<RevisionEntry> Sort(
        IReadOnlyList<RevisionEntry> entries,
        RevisionSortOrder order)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return ReviewRevisionSortPlanner.Sort(entries, order switch
        {
            RevisionSortOrder.Sequence => ReviewRevisionSortOrder.Sequence,
            RevisionSortOrder.Author => ReviewRevisionSortOrder.Author,
            RevisionSortOrder.Kind => ReviewRevisionSortOrder.Kind,
            RevisionSortOrder.Date => ReviewRevisionSortOrder.Date,
            _ => ReviewRevisionSortOrder.Sequence,
        });
    }
}
