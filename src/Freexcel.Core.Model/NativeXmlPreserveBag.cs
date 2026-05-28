using System.Collections.ObjectModel;

namespace Freexcel.Core.Model;

/// <summary>
/// Preserves raw XML fragments from XLSX features not fully modelled by Freexcel.
/// Used to maintain round-trip fidelity without parsing every feature.
///
/// Each entry is keyed by a descriptive name (e.g. "pageSetup", "pageMargins") and stores
/// the residual XML content — attributes and child elements that Freexcel does not yet model —
/// as a serialized XML string enclosed in a wrapper element named "e".
/// Example value: &lt;e customAttr="foo"&gt;&lt;child /&gt;&lt;/e&gt;
/// </summary>
public sealed class NativeXmlPreserveBag
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the preserved XML fragment for the given key, or null if not present.</summary>
    public string? Get(string key) => _entries.TryGetValue(key, out var v) ? v : null;

    /// <summary>Sets or removes the preserved XML fragment for the given key.
    /// A null value removes the key.</summary>
    public void Set(string key, string? value)
    {
        if (value is null)
            _entries.Remove(key);
        else
            _entries[key] = value;
    }

    /// <summary>Returns true if the bag contains an entry for the given key.</summary>
    public bool Contains(string key) => _entries.ContainsKey(key);

    /// <summary>All entries in this bag (read-only view).</summary>
    public IReadOnlyDictionary<string, string> All => new ReadOnlyDictionary<string, string>(_entries);

    /// <summary>Creates a deep copy of this bag.</summary>
    public NativeXmlPreserveBag Clone()
    {
        var copy = new NativeXmlPreserveBag();
        foreach (var (key, value) in _entries)
            copy._entries[key] = value;
        return copy;
    }
}
