using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace FreeW.App.Presentation.Backstage;

public static class FreeWBackstagePaneResourceKeys
{
    public const string RecentEmptyText = "FreeW_Backstage_Recent_EmptyText";
    public const string TemplateHeading = "Common_New";
    public const string TemplateTileCaption = "FreeW_Backstage_New_BlankDocument";
    public const string TemplateFooterText = "FreeW_Backstage_New_FooterText";
    public const string OptionsDescription = "FreeW_Backstage_Options_Description";
    public const string OptionsEditText = "FreeW_Backstage_Options_EditText";
    public const string ExportHeading = "FreeW_Backstage_Export_Heading";
    public const string ExportDescription = "FreeW_Backstage_Export_Description";
    public const string ExportFixedLayoutGroupHeading = "FreeW_Backstage_Export_FixedLayoutGroupHeading";
    public const string ExportPdfActionLabel = "FreeW_Backstage_Export_PdfActionLabel";
    public const string ExportPdfActionDescription = "FreeW_Backstage_Export_PdfActionDescription";
    public const string ExportXpsActionLabel = "FreeW_Backstage_Export_XpsActionLabel";
    public const string ExportXpsActionDescription = "FreeW_Backstage_Export_XpsActionDescription";
}

/// <summary>Owns FreeW-specific Backstage resource keys and fallback copy.</summary>
public static class FreeWBackstagePaneTextCatalog
{
    public static SisterBackstagePaneTextDescriptor Descriptor { get; } = new(
        Text(FreeWBackstagePaneResourceKeys.RecentEmptyText, "No recent documents."),
        Text(FreeWBackstagePaneResourceKeys.TemplateHeading, "New"),
        Text(FreeWBackstagePaneResourceKeys.TemplateTileCaption, "Blank document"),
        Text(FreeWBackstagePaneResourceKeys.TemplateFooterText, "More templates are not available in this build."),
        Text(FreeWBackstagePaneResourceKeys.OptionsDescription, "FreeW application settings. These persist between sessions and apply immediately."),
        new SisterBackstageExportPaneTextDescriptor(
            Text(FreeWBackstagePaneResourceKeys.ExportHeading, "Export"),
            Text(FreeWBackstagePaneResourceKeys.ExportDescription, "Create a fixed-layout copy or choose an editable document format."),
            Text(FreeWBackstagePaneResourceKeys.ExportFixedLayoutGroupHeading, "Create PDF/XPS Document"),
            Text(FreeWBackstagePaneResourceKeys.ExportPdfActionLabel, "Create PDF or XPS"),
            Text(FreeWBackstagePaneResourceKeys.ExportPdfActionDescription, "Publish a fixed-layout copy for sharing or printing."),
            Text(FreeWBackstagePaneResourceKeys.ExportXpsActionLabel, "Export to XPS"),
            Text(FreeWBackstagePaneResourceKeys.ExportXpsActionDescription, "Publish an XPS document with selectable, searchable vector text.")),
        Text(FreeWBackstagePaneResourceKeys.OptionsEditText, "Edit options\u2026"));

    public static IReadOnlyList<string> RequiredResourceKeys => Descriptor.ResourceKeys;

    public static SisterBackstagePaneTextSpec BuildTextSpec(Func<string, string?>? getText = null) =>
        SisterBackstagePaneTextSpec.FromDescriptor(Descriptor, getText);

    private static ResourceTextDescriptor Text(string key, string fallbackText) => new(key, fallbackText);
}
