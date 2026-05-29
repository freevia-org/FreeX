using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class SparklineValueCache
{
    private readonly record struct Source(Sheet Sheet, ulong Revision);

    private readonly Dictionary<Source, IReadOnlyDictionary<Guid, IReadOnlyList<double>>> _valuesBySource = [];

    public IReadOnlyDictionary<Guid, IReadOnlyList<double>> GetOrCreate(
        Sheet sheet,
        ulong revision,
        Func<IReadOnlyDictionary<Guid, IReadOnlyList<double>>> create)
    {
        var source = new Source(sheet, revision);
        if (_valuesBySource.TryGetValue(source, out var cached))
            return cached;

        var values = create();
        _valuesBySource[source] = values;
        return values;
    }

    public void Clear()
    {
        _valuesBySource.Clear();
    }
}
