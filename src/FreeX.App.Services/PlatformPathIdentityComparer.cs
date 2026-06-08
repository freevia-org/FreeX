namespace FreeX.App.Services;

public sealed class PlatformPathIdentityComparer : IEqualityComparer<string>
{
    private readonly StringComparer _comparer;
    private readonly Func<string, string> _normalizer;

    private PlatformPathIdentityComparer(StringComparer comparer, Func<string, string> normalizer)
    {
        _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
    }

    public static PlatformPathIdentityComparer Windows { get; } =
        new(StringComparer.OrdinalIgnoreCase, path => path.Replace('/', '\\'));

    public static PlatformPathIdentityComparer Unix { get; } =
        new(StringComparer.Ordinal, static path => path);

    public static PlatformPathIdentityComparer Current { get; } =
        OperatingSystem.IsWindows() ? Windows : Unix;

    public string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return _normalizer(path);
    }

    public bool Equals(string? left, string? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return _comparer.Equals(Normalize(left), Normalize(right));
    }

    public int GetHashCode(string path) => _comparer.GetHashCode(Normalize(path));
}
