namespace FreeX.App.Presentation.Localization;

/// <summary>
/// Compatibility facade for the shared resource-key resolver.
/// </summary>
public sealed class ResourceKeyTextResolver(
    Func<string, string> get,
    Func<string, object?[], string> format)
    : Free.Shared.Localization.ResourceKeyTextResolver(get, format)
{
}
