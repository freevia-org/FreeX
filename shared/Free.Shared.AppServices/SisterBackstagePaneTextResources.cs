using Free.Shared.Localization;

namespace Free.Shared.AppServices;

public sealed record ResourceTextDescriptor(string ResourceKey, string FallbackText)
{
    public string Resolve(
        Func<string, string?>? getText = null,
        bool stripMnemonics = false) =>
        LocalizedFallbackTextResolver.Resolve(
            ResourceKey,
            FallbackText,
            getText,
            stripMnemonics);
}

public sealed record SisterBackstageExportPaneTextDescriptor(
    ResourceTextDescriptor Heading,
    ResourceTextDescriptor Description,
    ResourceTextDescriptor FixedLayoutGroupHeading,
    ResourceTextDescriptor PdfActionLabel,
    ResourceTextDescriptor PdfActionDescription,
    ResourceTextDescriptor? XpsActionLabel = null,
    ResourceTextDescriptor? XpsActionDescription = null)
{
    public IReadOnlyList<ResourceTextDescriptor> Texts =>
        new[]
        {
            Heading,
            Description,
            FixedLayoutGroupHeading,
            PdfActionLabel,
            PdfActionDescription,
            XpsActionLabel,
            XpsActionDescription
        }.OfType<ResourceTextDescriptor>().ToArray();
}

public sealed record SisterBackstagePaneTextDescriptor(
    ResourceTextDescriptor RecentEmptyText,
    ResourceTextDescriptor TemplateHeading,
    ResourceTextDescriptor TemplateTileCaption,
    ResourceTextDescriptor TemplateFooterText,
    ResourceTextDescriptor OptionsDescription,
    SisterBackstageExportPaneTextDescriptor Export,
    ResourceTextDescriptor? OptionsEditText = null)
{
    public IReadOnlyList<ResourceTextDescriptor> Texts =>
        new[]
        {
            RecentEmptyText,
            TemplateHeading,
            TemplateTileCaption,
            TemplateFooterText,
            OptionsDescription,
            OptionsEditText
        }.OfType<ResourceTextDescriptor>()
            .Concat(Export.Texts)
            .ToArray();

    public IReadOnlyList<string> ResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();
}
