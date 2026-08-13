using Avalonia.Styling;
using Free.Shared.Theme;

namespace Free.Shared.Theme.Avalonia;

/// <summary>Reads typed values from the current Avalonia application's theme resources.</summary>
public static class AvaloniaThemeResourceResolver
{
    public static T? Find<T>(ThemeResourceDescriptor descriptor)
        where T : class =>
        ThemeResourceLookup.TryResolve(descriptor, Lookup, out T value) ? value : null;

    public static T ResolveOr<T>(ThemeResourceDescriptor descriptor, T fallback) =>
        ThemeResourceLookup.ResolveOr(descriptor, Lookup, fallback);

    private static object? Lookup(string key)
    {
        var application = global::Avalonia.Application.Current;
        return application is not null &&
               application.TryGetResource(key, ThemeVariant.Default, out var value)
            ? value
            : null;
    }
}
