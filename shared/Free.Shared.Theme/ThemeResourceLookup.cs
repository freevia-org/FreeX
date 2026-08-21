namespace Free.Shared.Theme;

/// <summary>
/// Describes an ordered set of native resource keys for one semantic theme value.
/// The shared layer owns key order only; renderers retain native resource types and fallbacks.
/// </summary>
public sealed class ThemeResourceDescriptor
{
    private readonly IReadOnlyList<string> _resourceKeys;

    public ThemeResourceDescriptor(string primaryKey, params string[] fallbackKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryKey);
        ArgumentNullException.ThrowIfNull(fallbackKeys);

        if (fallbackKeys.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Fallback resource keys cannot be empty.", nameof(fallbackKeys));

        _resourceKeys = Array.AsReadOnly<string>([primaryKey, .. fallbackKeys]);
    }

    public string PrimaryKey => _resourceKeys[0];

    public IReadOnlyList<string> ResourceKeys => _resourceKeys;

    public override string ToString() => string.Join(" -> ", _resourceKeys);
}

/// <summary>
/// Canonical product-prefixed theme resource names consumed by application renderers.
/// </summary>
public sealed record ProductThemeResourceProfile(string KeyPrefix, string BadgeColorRole)
{
    public ThemeResourceDescriptor Brush(string role) =>
        Describe(role, "Brush");

    public ThemeResourceDescriptor Color(string role) =>
        Describe(role, "Color");

    public ThemeResourceDescriptor Metric(string role) =>
        Describe(role, string.Empty);

    public ThemeResourceDescriptor FontSize(string role) =>
        Describe(role, "FontSize");

    public ThemeResourceDescriptor FontFamily(string role) =>
        Describe(role, "FontFamily");

    public ThemeResourceDescriptor FontWeight(string role) =>
        Describe(role, "FontWeight");

    public ThemeResourceDescriptor TitleBarBrush => Brush("TitleBar");

    public ThemeResourceDescriptor BadgeBrush => Brush(BadgeColorRole);

    public ThemeResourceDescriptor WhiteBrush => Brush("White");

    public ThemeResourceDescriptor SheetSurfaceBrush => Brush("SheetSurface");

    public ThemeResourceDescriptor StatusSurfaceBrush => Brush("StatusSurface");

    public ThemeResourceDescriptor StatusForegroundBrush => Brush("StatusForeground");

    public ThemeResourceDescriptor StatusBarHeight => Metric("StatusBarHeight");

    public ThemeResourceDescriptor StatusBarTextFontSize => FontSize("StatusBarText");

    private ThemeResourceDescriptor Describe(string role, string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(KeyPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return new ThemeResourceDescriptor($"{KeyPrefix}{role}{suffix}");
    }
}

public static class ProductThemeResourceProfiles
{
    public static ProductThemeResourceProfile FreeX { get; } = new("FreeX", "Accent");

    public static ProductThemeResourceProfile FreeW { get; } = new("FreeW", "Accent");

    public static ProductThemeResourceProfile FreeP { get; } = new("FreeP", "AccentDark");
}

/// <summary>
/// Resolves portable resource descriptors through a renderer-supplied native resource lookup.
/// </summary>
public static class ThemeResourceLookup
{
    public static bool TryResolve<T>(
        ThemeResourceDescriptor descriptor,
        Func<string, object?> lookup,
        out T value)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(lookup);

        foreach (var key in descriptor.ResourceKeys)
        {
            if (lookup(key) is T typedValue)
            {
                value = typedValue;
                return true;
            }
        }

        value = default!;
        return false;
    }

    public static T ResolveOr<T>(
        ThemeResourceDescriptor descriptor,
        Func<string, object?> lookup,
        T fallback) =>
        TryResolve(descriptor, lookup, out T value) ? value : fallback;

    public static TResult ResolveProjectedOr<TResource, TResult>(
        ThemeResourceDescriptor descriptor,
        Func<string, object?> lookup,
        Func<TResource, TResult> projector,
        TResult fallback)
    {
        ArgumentNullException.ThrowIfNull(projector);
        return TryResolve(descriptor, lookup, out TResource resource)
            ? projector(resource)
            : fallback;
    }
}
