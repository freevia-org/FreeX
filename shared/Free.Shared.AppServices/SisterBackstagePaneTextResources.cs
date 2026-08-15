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

public sealed record SisterBackstageCorePropertiesTextDescriptor(
    ResourceTextDescriptor TitleLabel,
    ResourceTextDescriptor AuthorLabel,
    ResourceTextDescriptor SubjectLabel,
    ResourceTextDescriptor KeywordsLabel,
    ResourceTextDescriptor EmptyValue)
{
    public IReadOnlyList<ResourceTextDescriptor> Texts =>
        [TitleLabel, AuthorLabel, SubjectLabel, KeywordsLabel, EmptyValue];
}

public sealed record SisterBackstageInfoPaneTextDescriptor(
    ResourceTextDescriptor Heading,
    ResourceTextDescriptor LocationLabel,
    ResourceTextDescriptor NotSavedYet,
    ResourceTextDescriptor PropertiesHeading,
    ResourceTextDescriptor StatisticsHeading,
    ResourceTextDescriptor DirtySuffix,
    SisterBackstageCorePropertiesTextDescriptor CoreProperties)
{
    public IReadOnlyList<ResourceTextDescriptor> Texts =>
        new[]
        {
            Heading,
            LocationLabel,
            NotSavedYet,
            PropertiesHeading,
            StatisticsHeading,
            DirtySuffix
        }.Concat(CoreProperties.Texts).ToArray();
}

public sealed record ApplicationOptionsSummaryTextDescriptor(
    ResourceTextDescriptor RecentFilesKeptLabel,
    ResourceTextDescriptor DefaultSaveFormatLabel,
    ResourceTextDescriptor UiLanguageLabel,
    ResourceTextDescriptor DataFolderLabel,
    ResourceTextDescriptor SystemDefaultLanguageLabel)
{
    public IReadOnlyList<ResourceTextDescriptor> Texts =>
        [RecentFilesKeptLabel, DefaultSaveFormatLabel, UiLanguageLabel, DataFolderLabel, SystemDefaultLanguageLabel];
}

public sealed record SisterBackstagePaneTextDescriptor(
    ResourceTextDescriptor RecentEmptyText,
    ResourceTextDescriptor TemplateHeading,
    ResourceTextDescriptor TemplateTileCaption,
    ResourceTextDescriptor TemplateFooterText,
    ResourceTextDescriptor OptionsDescription,
    SisterBackstageExportPaneTextDescriptor Export,
    ResourceTextDescriptor? OptionsEditText = null,
    ResourceTextDescriptor? RecentHeading = null,
    ResourceTextDescriptor? OptionsHeading = null,
    SisterBackstageInfoPaneTextDescriptor? Info = null,
    ApplicationOptionsSummaryTextDescriptor? OptionsSummary = null)
{
    public IReadOnlyList<ResourceTextDescriptor> Texts =>
        new[]
        {
            RecentEmptyText,
            TemplateHeading,
            TemplateTileCaption,
            TemplateFooterText,
            OptionsDescription,
            OptionsEditText,
            RecentHeading,
            OptionsHeading
        }.OfType<ResourceTextDescriptor>()
            .Concat(Export.Texts)
            .Concat(Info?.Texts ?? [])
            .Concat(OptionsSummary?.Texts ?? [])
            .ToArray();

    public IReadOnlyList<string> ResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();
}
