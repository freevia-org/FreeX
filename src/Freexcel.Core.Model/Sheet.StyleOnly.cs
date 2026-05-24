namespace Freexcel.Core.Model;

public sealed partial class Sheet
{
    /// <summary>Returns the style-only override for an empty cell, or null if none exists.</summary>
    public StyleId? GetStyleOnly(uint row, uint col)
        => _styleOnly.TryGetValue((row, col), out var s) ? s : null;

    /// <summary>Sets a style-only override for an empty cell.</summary>
    public void SetStyleOnly(uint row, uint col, StyleId styleId)
        => _styleOnly[(row, col)] = styleId;

    /// <summary>Removes the style-only override for an empty cell.</summary>
    public void ClearStyleOnly(uint row, uint col)
        => _styleOnly.Remove((row, col));

    /// <summary>Enumerates all style-only entries (for empty cells that have been styled).</summary>
    public IEnumerable<((uint Row, uint Col) Key, StyleId StyleId)> GetStyleOnlyEntries()
        => _styleOnly.Select(kv => (kv.Key, kv.Value));
}
