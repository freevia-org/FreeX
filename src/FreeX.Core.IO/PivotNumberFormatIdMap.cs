using System.Collections;

namespace FreeX.Core.IO;

/// <summary>
/// R118-io-numfmt-pivot-sentinel-collision: the result of <see cref="XlsxNumberFormatCatalogWriter.Save"/>.
/// Behaves exactly like the plain <c>id -&gt; id</c> remap it used to return (every existing consumer that
/// only has a numFmtId -- e.g. a pivotCacheField, which carries no format-code text at all -- keeps working
/// unchanged via the <see cref="IReadOnlyDictionary{TKey,TValue}"/> surface), but ALSO carries a secondary
/// <c>(sentinelId, formatCode) -&gt; finalId</c> map so a pivot DATA FIELD's numFmtId can be resolved by its
/// own format-code text rather than purely by id.
///
/// This matters because <c>PivotValueFieldPlanner.ResolveNumberFormatState</c> hardcodes the SAME sentinel
/// id (164) for every distinct custom format string a user types into Value Field Settings. Two data fields
/// with different custom formats (e.g. "kg" vs "lb") therefore share one <c>NumberFormatId</c> in the model
/// but carry different <c>NumberFormatCode</c> strings -- a plain <c>int -&gt; int</c> map cannot represent
/// "the same source id maps to two different final ids depending on which field it is." Resolving by
/// (id, code) instead lets both fields keep their own distinct final numFmtId.
/// </summary>
internal sealed class PivotNumberFormatIdMap : IReadOnlyDictionary<int, int>
{
    public static readonly PivotNumberFormatIdMap Empty = new(
        new Dictionary<int, int>(),
        new Dictionary<(int NumberFormatId, string FormatCode), int>());

    private readonly IReadOnlyDictionary<int, int> _idMap;
    private readonly IReadOnlyDictionary<(int NumberFormatId, string FormatCode), int> _codeMap;

    public PivotNumberFormatIdMap(
        IReadOnlyDictionary<int, int> idMap,
        IReadOnlyDictionary<(int NumberFormatId, string FormatCode), int> codeMap)
    {
        _idMap = idMap;
        _codeMap = codeMap;
    }

    /// <summary>
    /// Resolves the final OOXML numFmtId a pivot data field's own (sentinel id, format code) pair should be
    /// written under. Falls back to the plain id-only remap when the exact (id, code) pair was never seen
    /// while building the catalog (e.g. <paramref name="formatCode"/> is null/blank, or this id never
    /// collided across distinct pivot format codes) so ordinary single-format pivots keep today's behavior.
    /// </summary>
    public int ResolveDataFieldNumberFormatId(int numberFormatId, string? formatCode)
    {
        if (!string.IsNullOrWhiteSpace(formatCode) &&
            _codeMap.TryGetValue((numberFormatId, formatCode), out var mappedByCode))
        {
            return mappedByCode;
        }

        return _idMap.TryGetValue(numberFormatId, out var mapped) ? mapped : numberFormatId;
    }

    public int this[int key] => _idMap[key];

    public IEnumerable<int> Keys => _idMap.Keys;

    public IEnumerable<int> Values => _idMap.Values;

    public int Count => _idMap.Count;

    public bool ContainsKey(int key) => _idMap.ContainsKey(key);

    public bool TryGetValue(int key, out int value) => _idMap.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<int, int>> GetEnumerator() => _idMap.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
