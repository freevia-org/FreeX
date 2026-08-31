namespace FreeW.Core.Model;

/// <summary>
/// Reserves caller-preferred integer IDs when available and allocates monotonically increasing fresh IDs
/// after the greatest ID that was already in use. The caller's set is the source of truth and is updated by
/// every successful reservation, so model dictionaries and their associated reference maps can continue to
/// share one collision view without repeatedly rescanning the full set for its maximum.
/// </summary>
internal sealed class IntegerIdAllocator
{
    private readonly HashSet<int> _usedIds;
    private long _nextFreshId;

    public IntegerIdAllocator(HashSet<int> usedIds, int firstFreshId)
    {
        ArgumentNullException.ThrowIfNull(usedIds);
        if (firstFreshId < 0)
            throw new ArgumentOutOfRangeException(nameof(firstFreshId));

        _usedIds = usedIds;
        _nextFreshId = usedIds.Count == 0
            ? firstFreshId
            : Math.Max((long)firstFreshId, (long)usedIds.Max() + 1);
    }

    public bool TryReservePreferred(int preferredId)
    {
        if (!_usedIds.Add(preferredId))
            return false;

        if (preferredId >= _nextFreshId)
            _nextFreshId = (long)preferredId + 1;
        return true;
    }

    public int ReservePreferredOrNext(int preferredId) =>
        TryReservePreferred(preferredId) ? preferredId : AllocateNext();

    public int AllocateNext()
    {
        while (_nextFreshId <= int.MaxValue)
        {
            var candidate = (int)_nextFreshId++;
            if (_usedIds.Add(candidate))
                return candidate;
        }

        throw new InvalidOperationException("No fresh integer IDs remain.");
    }
}
