using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace FreeP.App.Compositor;

public static class FreePBackstagePaneResourceKeys
{
    public const string RecentEmptyText = "FreeP_Backstage_Recent_EmptyText";
    public const string TemplateHeading = "FreeP_Backstage_New_Heading";
    public const string TemplateTileCaption = "FreeP_Backstage_New_BlankPresentation";
    public const string TemplateFooterText = "FreeP_Backstage_New_FooterText";
    public const string OptionsDescription = "FreeP_Backstage_Options_Description";
    public const string OptionsEditText = "FreeP_Backstage_Options_EditText";
    public const string InfoHeading = "FreeP_Backstage_Info_Heading";
    public const string ExportHeading = "FreeP_Backstage_Export_Heading";
    public const string ExportDescription = "FreeP_Backstage_Export_Description";
    public const string ExportFixedLayoutGroupHeading = "FreeP_Backstage_Export_FixedLayoutGroupHeading";
    public const string ExportPdfActionLabel = "FreeP_Backstage_Export_PdfActionLabel";
    public const string ExportPdfActionDescription = "FreeP_Backstage_Export_PdfActionDescription";
}

/// <summary>Owns FreeP-specific Backstage resource keys and fallback copy.</summary>
public static class FreePBackstagePaneTextCatalog
{
    public static SisterBackstagePaneTextDescriptor Descriptor { get; } = new(
        Text(FreePBackstagePaneResourceKeys.RecentEmptyText, "No recent presentations."),
        Text(FreePBackstagePaneResourceKeys.TemplateHeading, "New"),
        Text(FreePBackstagePaneResourceKeys.TemplateTileCaption, "Blank presentation"),
        Text(FreePBackstagePaneResourceKeys.TemplateFooterText, "More templates are not available in this build."),
        Text(FreePBackstagePaneResourceKeys.OptionsDescription, "FreeP application settings. These persist between sessions."),
        new SisterBackstageExportPaneTextDescriptor(
            Text(FreePBackstagePaneResourceKeys.ExportHeading, "Export"),
            Text(FreePBackstagePaneResourceKeys.ExportDescription, "Create a PDF copy of this presentation - one page per slide, with selectable text."),
            Text(FreePBackstagePaneResourceKeys.ExportFixedLayoutGroupHeading, "Create PDF Copy"),
            Text(FreePBackstagePaneResourceKeys.ExportPdfActionLabel, "Export to PDF..."),
            Text(FreePBackstagePaneResourceKeys.ExportPdfActionDescription, "Publish a fixed-layout copy for sharing or presenting.")),
        Text(FreePBackstagePaneResourceKeys.OptionsEditText, "Edit options…"),
        Info: SisterBackstagePaneTextResources.CreateInfoDescriptor(
            Text(FreePBackstagePaneResourceKeys.InfoHeading, "Info")),
        OptionsSummary: SisterBackstagePaneTextResources.ApplicationOptionsSummaryDescriptor);

    public static IReadOnlyList<string> RequiredResourceKeys => Descriptor.ResourceKeys;

    public static SisterBackstagePaneTextSpec BuildTextSpec(Func<string, string?>? getText = null) =>
        SisterBackstagePaneTextSpec.FromDescriptor(Descriptor, getText);

    private static ResourceTextDescriptor Text(string key, string fallbackText) => new(key, fallbackText);
}
