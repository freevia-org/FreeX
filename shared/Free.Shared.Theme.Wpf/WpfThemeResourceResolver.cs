using System.Windows;
using Free.Shared.Theme;

namespace Free.Shared.Theme.Wpf;

/// <summary>Reads typed values from the current WPF application's theme resources.</summary>
public static class WpfThemeResourceResolver
{
    public static T? Find<T>(ThemeResourceDescriptor descriptor)
        where T : class =>
        ThemeResourceLookup.TryResolve(descriptor, Lookup, out T value) ? value : null;

    public static T ResolveOr<T>(ThemeResourceDescriptor descriptor, T fallback) =>
        ThemeResourceLookup.ResolveOr(descriptor, Lookup, fallback);

    public static TResult ResolveProjectedOr<TResource, TResult>(
        ThemeResourceDescriptor descriptor,
        Func<TResource, TResult> projector,
        TResult fallback) =>
        ThemeResourceLookup.ResolveProjectedOr(descriptor, Lookup, projector, fallback);

    private static object? Lookup(string key) =>
        Application.Current?.Resources[key];
}
