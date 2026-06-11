namespace FreeX.Core.Model;

/// <summary>
/// A list of <see cref="ConditionalFormat"/> rules that tracks mutations via a monotonic
/// <see cref="Version"/> counter. Consumers can cache derived data and invalidate when
/// the version changes.
/// </summary>
public sealed class ConditionalFormatCollection : List<ConditionalFormat>
{
    private int _version;

    /// <summary>
    /// Monotonically increasing counter that increments whenever any rule is added, removed,
    /// or replaced. Callers may call <see cref="NotifyRulesChanged"/> explicitly when they
    /// mutate a rule object in-place (e.g., changing its <see cref="ConditionalFormat.Priority"/>).
    /// </summary>
    public int Version => _version;

    public new ConditionalFormat this[int index]
    {
        get => base[index];
        set
        {
            base[index] = value;
            _version++;
        }
    }

    public new void Add(ConditionalFormat item)
    {
        base.Add(item);
        _version++;
    }

    public new void AddRange(IEnumerable<ConditionalFormat> collection)
    {
        var count = Count;
        base.AddRange(collection);
        if (Count != count)
            _version++;
    }

    public new void Insert(int index, ConditionalFormat item)
    {
        base.Insert(index, item);
        _version++;
    }

    public new bool Remove(ConditionalFormat item)
    {
        var removed = base.Remove(item);
        if (removed)
            _version++;
        return removed;
    }

    public new void RemoveAt(int index)
    {
        base.RemoveAt(index);
        _version++;
    }

    public new int RemoveAll(Predicate<ConditionalFormat> match)
    {
        var removed = base.RemoveAll(match);
        if (removed != 0)
            _version++;
        return removed;
    }

    public new void Clear()
    {
        if (Count == 0)
            return;

        base.Clear();
        _version++;
    }

    /// <summary>
    /// Call this after mutating a rule object in-place (e.g., changing its Priority or style)
    /// to ensure caches that depend on rule content are invalidated.
    /// </summary>
    public void NotifyRulesChanged() => _version++;
}
