namespace Free.Shared.Localization;

/// <summary>Resolves app-owned resource keys without coupling planners to a resource catalog.</summary>
public class ResourceKeyTextResolver(
    Func<string, string> get,
    Func<string, object?[], string> format)
{
    public string Get(string resourceKey) => get(resourceKey);

    public string Format(string resourceKey, params object?[] arguments) =>
        format(resourceKey, arguments);
}
