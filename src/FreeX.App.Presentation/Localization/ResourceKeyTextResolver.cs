namespace FreeX.App.Presentation.Localization;

/// <summary>
/// Resolves portable resource-key plans through a renderer's localization implementation.
/// </summary>
public sealed class ResourceKeyTextResolver(
    Func<string, string> get,
    Func<string, object?[], string> format)
{
    public string Get(string resourceKey) => get(resourceKey);

    public string Format(string resourceKey, params object?[] arguments) =>
        format(resourceKey, arguments);
}
