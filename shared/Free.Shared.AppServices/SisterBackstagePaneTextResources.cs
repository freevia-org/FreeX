namespace Free.Shared.AppServices;

public enum SisterBackstageAppKind
{
    FreeW,
    FreeP
}

public sealed record ResourceTextDescriptor(string ResourceKey, string FallbackText);

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

public static class SisterBackstagePaneResourceKeys
{
    public const string FreeWRecentEmptyText = "FreeW_Backstage_Recent_EmptyText";
    public const string FreeWTemplateHeading = "FreeW_Backstage_New_Heading";
    public const string FreeWTemplateTileCaption = "FreeW_Backstage_New_BlankDocument";
    public const string FreeWTemplateFooterText = "FreeW_Backstage_New_FooterText";
    public const string FreeWOptionsDescription = "FreeW_Backstage_Options_Description";
    public const string FreeWOptionsEditText = "FreeW_Backstage_Options_EditText";
    public const string FreeWExportHeading = "FreeW_Backstage_Export_Heading";
    public const string FreeWExportDescription = "FreeW_Backstage_Export_Description";
    public const string FreeWExportFixedLayoutGroupHeading = "FreeW_Backstage_Export_FixedLayoutGroupHeading";
    public const string FreeWExportPdfActionLabel = "FreeW_Backstage_Export_PdfActionLabel";
    public const string FreeWExportPdfActionDescription = "FreeW_Backstage_Export_PdfActionDescription";
    public const string FreeWExportXpsActionLabel = "FreeW_Backstage_Export_XpsActionLabel";
    public const string FreeWExportXpsActionDescription = "FreeW_Backstage_Export_XpsActionDescription";

    public const string FreePRecentEmptyText = "FreeP_Backstage_Recent_EmptyText";
    public const string FreePTemplateHeading = "FreeP_Backstage_New_Heading";
    public const string FreePTemplateTileCaption = "FreeP_Backstage_New_BlankPresentation";
    public const string FreePTemplateFooterText = "FreeP_Backstage_New_FooterText";
    public const string FreePOptionsDescription = "FreeP_Backstage_Options_Description";
    public const string FreePOptionsEditText = "FreeP_Backstage_Options_EditText";
    public const string FreePExportHeading = "FreeP_Backstage_Export_Heading";
    public const string FreePExportDescription = "FreeP_Backstage_Export_Description";
    public const string FreePExportFixedLayoutGroupHeading = "FreeP_Backstage_Export_FixedLayoutGroupHeading";
    public const string FreePExportPdfActionLabel = "FreeP_Backstage_Export_PdfActionLabel";
    public const string FreePExportPdfActionDescription = "FreeP_Backstage_Export_PdfActionDescription";
}

public static class SisterBackstagePaneTextDescriptorPlanner
{
    public static SisterBackstagePaneTextDescriptor Build(SisterBackstageAppKind appKind) =>
        appKind switch
        {
            SisterBackstageAppKind.FreeW => BuildFreeW(),
            SisterBackstageAppKind.FreeP => BuildFreeP(),
            _ => throw new ArgumentOutOfRangeException(nameof(appKind), appKind, null)
        };

    public static IReadOnlyList<string> RequiredResourceKeys(SisterBackstageAppKind appKind) =>
        Build(appKind).ResourceKeys;

    private static SisterBackstagePaneTextDescriptor BuildFreeW() =>
        new(
            Text(SisterBackstagePaneResourceKeys.FreeWRecentEmptyText, "No recent documents."),
            Text(SisterBackstagePaneResourceKeys.FreeWTemplateHeading, "New"),
            Text(SisterBackstagePaneResourceKeys.FreeWTemplateTileCaption, "Blank document"),
            Text(SisterBackstagePaneResourceKeys.FreeWTemplateFooterText, "More templates are not available in this build."),
            Text(SisterBackstagePaneResourceKeys.FreeWOptionsDescription, "FreeW application settings. These persist between sessions and apply immediately."),
            new SisterBackstageExportPaneTextDescriptor(
                Text(SisterBackstagePaneResourceKeys.FreeWExportHeading, "Export"),
                Text(SisterBackstagePaneResourceKeys.FreeWExportDescription, "Create a fixed-layout copy or choose an editable document format."),
                Text(SisterBackstagePaneResourceKeys.FreeWExportFixedLayoutGroupHeading, "Create PDF/XPS Document"),
                Text(SisterBackstagePaneResourceKeys.FreeWExportPdfActionLabel, "Create PDF or XPS"),
                Text(SisterBackstagePaneResourceKeys.FreeWExportPdfActionDescription, "Publish a fixed-layout copy for sharing or printing."),
                Text(SisterBackstagePaneResourceKeys.FreeWExportXpsActionLabel, "Export to XPS"),
                Text(SisterBackstagePaneResourceKeys.FreeWExportXpsActionDescription, "Publish an XPS document with selectable, searchable vector text.")),
            Text(SisterBackstagePaneResourceKeys.FreeWOptionsEditText, "Edit options\u2026"));

    private static SisterBackstagePaneTextDescriptor BuildFreeP() =>
        new(
            Text(SisterBackstagePaneResourceKeys.FreePRecentEmptyText, "No recent presentations."),
            Text(SisterBackstagePaneResourceKeys.FreePTemplateHeading, "New"),
            Text(SisterBackstagePaneResourceKeys.FreePTemplateTileCaption, "Blank presentation"),
            Text(SisterBackstagePaneResourceKeys.FreePTemplateFooterText, "More templates are not available in this build."),
            Text(SisterBackstagePaneResourceKeys.FreePOptionsDescription, "FreeP application settings. These persist between sessions."),
            new SisterBackstageExportPaneTextDescriptor(
                Text(SisterBackstagePaneResourceKeys.FreePExportHeading, "Export"),
                Text(SisterBackstagePaneResourceKeys.FreePExportDescription, "Create a PDF copy of this presentation - one page per slide, with selectable text."),
                Text(SisterBackstagePaneResourceKeys.FreePExportFixedLayoutGroupHeading, "Create PDF Copy"),
                Text(SisterBackstagePaneResourceKeys.FreePExportPdfActionLabel, "Export to PDF..."),
                Text(SisterBackstagePaneResourceKeys.FreePExportPdfActionDescription, "Publish a fixed-layout copy for sharing or presenting.")),
            Text(SisterBackstagePaneResourceKeys.FreePOptionsEditText, "Edit options…"));

    private static ResourceTextDescriptor Text(string key, string fallbackText) =>
        new(key, fallbackText);
}
