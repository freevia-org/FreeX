namespace FreeX.Core.Model;

public sealed class DataValidationCollection : List<DataValidation>
{
    private int _version;

    public int Version => _version;

    public new DataValidation this[int index]
    {
        get => base[index];
        set
        {
            base[index] = value;
            _version++;
        }
    }

    public new void Add(DataValidation item)
    {
        base.Add(item);
        _version++;
    }

    public new void AddRange(IEnumerable<DataValidation> collection)
    {
        var count = Count;
        base.AddRange(collection);
        if (Count != count)
            _version++;
    }

    public new void Insert(int index, DataValidation item)
    {
        base.Insert(index, item);
        _version++;
    }

    public new bool Remove(DataValidation item)
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

    public new int RemoveAll(Predicate<DataValidation> match)
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

    public void NotifyRulesChanged() => _version++;
}
