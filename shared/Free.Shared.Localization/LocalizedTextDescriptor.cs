namespace Free.Shared.Localization;

/// <summary>Portable localized text selected by app presentation and service planners.</summary>
public record LocalizedTextDescriptor(
    string? ResourceKey,
    string? LiteralText,
    IReadOnlyList<object?> Arguments)
{
    public static LocalizedTextDescriptor Resource(string resourceKey, params object?[] arguments) =>
        new(resourceKey, null, arguments);

    public static LocalizedTextDescriptor Literal(string text) =>
        new(null, text, []);

    public string Resolve(ResourceKeyTextResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return ResourceKey is null
            ? LiteralText ?? string.Empty
            : Arguments.Count == 0
                ? resolver.Get(ResourceKey)
                : resolver.Format(ResourceKey, Arguments.ToArray());
    }

    public string Resolve(
        Func<string, string> getText,
        Func<string, object?[], string> formatText)
    {
        ArgumentNullException.ThrowIfNull(getText);
        ArgumentNullException.ThrowIfNull(formatText);
        return Resolve(new ResourceKeyTextResolver(getText, formatText));
    }
}
