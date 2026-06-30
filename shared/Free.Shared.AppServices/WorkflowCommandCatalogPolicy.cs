namespace Free.Shared.AppServices;

public static class WorkflowCommandCatalogPolicy
{
    public static TDescriptor GetById<TDescriptor, TId>(
        IEnumerable<TDescriptor> descriptors,
        TId id,
        Func<TDescriptor, TId> idSelector)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(idSelector);

        foreach (var descriptor in descriptors)
        {
            if (EqualityComparer<TId>.Default.Equals(idSelector(descriptor), id))
                return descriptor;
        }

        throw new ArgumentOutOfRangeException(nameof(id), id, null);
    }
}
