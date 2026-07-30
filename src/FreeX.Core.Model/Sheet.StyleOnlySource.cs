namespace FreeX.Core.Model;

/// <summary>
/// Tags which kind of whole-row/whole-column format command most recently established a
/// style-only entry (an empty cell's format-only override). Used by ApplyStyleCommand to enforce
/// Excel's fixed row-beats-column precedence at a row/column intersection: a cell xf outranks a
/// row style, which outranks a column style, regardless of which format command was applied more
/// recently (R92-render-cellstyle-inheritance-5-3).
/// </summary>
public enum StyleOnlySource : byte
{
    /// <summary>The style-only entry was (most recently) established by a whole-row format command.</summary>
    Row,

    /// <summary>The style-only entry was (most recently) established by a whole-column format command.</summary>
    Column,
}

public sealed partial class Sheet
{
    // Sparse and deliberately NOT run-compressed (unlike _styleOnly/_styleOnlyRuns): this only
    // exists to disambiguate row-vs-column format precedence at an intersection, a comparatively
    // rare conflict, so the extra bookkeeping the run-compression scheme requires for bulk import
    // is not worth it here. Entries are a subset of (and always removed alongside) _styleOnly's
    // keyspace -- see ClearStyleOnly below -- so this dictionary never outlives the style-only
    // entry it describes.
    private Dictionary<(uint Row, uint Col), StyleOnlySource>? _styleOnlySource;

    /// <summary>
    /// Returns the row/column-format provenance tag for a style-only entry at (row, col), or null
    /// if the entry (if any) has no known provenance -- e.g. it was created by a bounded/cell-range
    /// format command, predates this tracking, or its provenance was lost across a row/column
    /// shift. Untagged is the safe default: callers fall back to plain merge-on-top behavior.
    /// </summary>
    public StyleOnlySource? GetStyleOnlySource(uint row, uint col)
    {
        return _styleOnlySource is { } map && map.TryGetValue((row, col), out var source)
            ? source
            : null;
    }

    /// <summary>Tags a style-only entry's provenance as coming from a row- or column-format command.</summary>
    public void SetStyleOnlySource(uint row, uint col, StyleOnlySource source)
    {
        (_styleOnlySource ??= [])[(row, col)] = source;
    }

    /// <summary>Removes the provenance tag for a cell, if any (a no-op if none is set).</summary>
    public void ClearStyleOnlySource(uint row, uint col)
    {
        _styleOnlySource?.Remove((row, col));
    }
}
