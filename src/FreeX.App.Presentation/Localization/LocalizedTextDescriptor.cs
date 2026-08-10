namespace FreeX.App.Presentation.Localization;

/// <summary>FreeX-compatible facade over the shared localized-text contract.</summary>
public sealed record LocalizedTextDescriptor : Free.Shared.Localization.LocalizedTextDescriptor
{
    public LocalizedTextDescriptor(
        string? ResourceKey,
        string? LiteralText,
        IReadOnlyList<object?> Arguments)
        : base(ResourceKey, LiteralText, Arguments)
    {
    }

    public new static LocalizedTextDescriptor Resource(string resourceKey, params object?[] arguments) =>
        new(resourceKey, null, arguments);

    public new static LocalizedTextDescriptor Literal(string text) =>
        new(null, text, []);
}
