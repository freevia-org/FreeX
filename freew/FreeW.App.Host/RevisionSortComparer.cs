using System;
using System.Collections.Generic;
using System.Linq;
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
/// Applies a <see cref="RevisionSortOrder"/> to a list of <see cref="RevisionEntry"/> values from
/// <see cref="RevisionList.Enumerate"/>. Pure (no mutation of the document model); the result is a
/// fresh list suitable for the Reviewing Pane's display. Sequence order is a no-op (reading order is
/// already the enumeration order); all other orders use a stable sort.
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

        return order switch
        {
            RevisionSortOrder.Sequence => entries,
            RevisionSortOrder.Author => entries
                .OrderBy(e => e.Author ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.BlockIndex)
                .ToList(),
            RevisionSortOrder.Kind => entries
                .OrderBy(e => (int)e.Kind)
                .ThenBy(e => e.BlockIndex)
                .ToList(),
            RevisionSortOrder.Date => entries
                .OrderBy(e => e.DateXml, StringComparer.Ordinal)   // null sorts first; callers will see blank
                .ThenBy(e => e.BlockIndex)
                .ToList(),
            _ => entries,
        };
    }
}
