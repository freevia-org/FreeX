namespace Free.Shared.AppServices;

/// <summary>
/// Tracks live autosave owners and invokes each owner's best-effort emergency snapshot callback.
/// </summary>
public sealed class EmergencySnapshotFanOut<T> where T : class
{
    private readonly List<Registration> _registrations = [];
    private readonly Action<T> _snapshot;
    private readonly object _gate = new();

    public EmergencySnapshotFanOut(Action<T> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
    }

    /// <summary>Gets the number of owners currently eligible for emergency fan-out.</summary>
    public int ActiveCount
    {
        get
        {
            lock (_gate)
                return _registrations.Count;
        }
    }

    /// <summary>Registers one live owner until the returned lease is disposed.</summary>
    public IDisposable Register(T owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var registration = new Registration(this, owner);
        lock (_gate)
            _registrations.Add(registration);
        return registration;
    }

    /// <summary>
    /// Invokes a stable snapshot of the live registrations. One failing owner does not prevent
    /// the remaining owners from receiving the best-effort emergency callback.
    /// </summary>
    public void TrySnapshotAll()
    {
        Registration[] registrations;
        lock (_gate)
            registrations = _registrations.ToArray();

        foreach (var registration in registrations)
        {
            try
            {
                registration.TrySnapshot(_snapshot);
            }
            catch
            {
                // Crash-time snapshots are best-effort; continue through every live owner.
            }
        }
    }

    private void Unregister(Registration registration)
    {
        lock (_gate)
            _registrations.Remove(registration);
        registration.Deactivate();
    }

    private sealed class Registration : IDisposable
    {
        private readonly object _gate = new();
        private EmergencySnapshotFanOut<T>? _owner;
        private T? _instance;

        public Registration(EmergencySnapshotFanOut<T> owner, T instance)
        {
            _owner = owner;
            _instance = instance;
        }

        public void TrySnapshot(Action<T> snapshot)
        {
            lock (_gate)
            {
                if (_instance is { } instance)
                    snapshot(instance);
            }
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Unregister(this);
        }

        public void Deactivate()
        {
            lock (_gate)
                _instance = null;
        }
    }
}
