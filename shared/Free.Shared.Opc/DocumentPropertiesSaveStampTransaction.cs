namespace Free.Shared.Opc;

/// <summary>Applies successful-save metadata while preserving rollback ownership in a scope.</summary>
public static class DocumentPropertiesSaveStampTransaction
{
    public static DocumentPropertiesSaveStampScope Begin(
        DocumentProperties properties,
        string fallbackLastModifiedBy) =>
        Begin(properties, fallbackLastModifiedBy, DateTimeOffset.Now, Environment.UserName);

    public static DocumentPropertiesSaveStampScope Begin(
        DocumentProperties properties,
        string fallbackLastModifiedBy,
        DateTimeOffset modified,
        string? operatingSystemAuthor)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackLastModifiedBy);

        return new DocumentPropertiesSaveStampScope(
            properties,
            modified,
            ResolveLastModifiedBy(
                properties.Author,
                operatingSystemAuthor,
                fallbackLastModifiedBy));
    }

    public static string ResolveLastModifiedBy(
        string? documentAuthor,
        string? operatingSystemAuthor,
        string fallbackLastModifiedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackLastModifiedBy);

        if (!string.IsNullOrWhiteSpace(documentAuthor))
            return documentAuthor.Trim();
        if (!string.IsNullOrWhiteSpace(operatingSystemAuthor))
            return operatingSystemAuthor.Trim();

        return fallbackLastModifiedBy.Trim();
    }
}

public sealed class DocumentPropertiesSaveStampScope : IDisposable
{
    private readonly DocumentProperties _properties;
    private readonly DateTimeOffset? _previousModified;
    private readonly string? _previousLastModifiedBy;
    private bool _committed;
    private bool _disposed;

    internal DocumentPropertiesSaveStampScope(
        DocumentProperties properties,
        DateTimeOffset modified,
        string lastModifiedBy)
    {
        _properties = properties;
        _previousModified = properties.Modified;
        _previousLastModifiedBy = properties.LastModifiedBy;
        properties.Modified = modified;
        properties.LastModifiedBy = lastModifiedBy;
    }

    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _committed = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_committed)
            return;

        _properties.Modified = _previousModified;
        _properties.LastModifiedBy = _previousLastModifiedBy;
    }
}
