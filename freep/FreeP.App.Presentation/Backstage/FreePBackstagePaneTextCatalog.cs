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
    public const string InfoLocationLabel = "FreeP_Backstage_Info_LocationLabel";
    public const string InfoNotSavedYet = "FreeP_Backstage_Info_NotSavedYet";
    public const string InfoPropertiesHeading = "FreeP_Backstage_Info_PropertiesHeading";
    public const string InfoStatisticsHeading = "FreeP_Backstage_Info_StatisticsHeading";
    public const string InfoDirtySuffix = "FreeP_Backstage_Info_DirtySuffix";
    public const string InfoTitleLabel = "FreeP_Backstage_Info_TitleLabel";
    public const string InfoAuthorLabel = "FreeP_Backstage_Info_AuthorLabel";
    public const string InfoSubjectLabel = "FreeP_Backstage_Info_SubjectLabel";
    public const string InfoKeywordsLabel = "FreeP_Backstage_Info_KeywordsLabel";
    public const string InfoEmptyValue = "FreeP_Backstage_Info_EmptyValue";
    public const string OptionsSummaryRecentFilesKeptLabel =
        "FreeP_Backstage_OptionsSummary_RecentFilesKeptLabel";
    public const string OptionsSummaryDefaultSaveFormatLabel =
        "FreeP_Backstage_OptionsSummary_DefaultSaveFormatLabel";
    public const string OptionsSummaryUiLanguageLabel =
        "FreeP_Backstage_OptionsSummary_UiLanguageLabel";
    public const string OptionsSummaryDataFolderLabel =
        "FreeP_Backstage_OptionsSummary_DataFolderLabel";
    public const string OptionsSummarySystemDefaultLanguageLabel =
        "FreeP_Backstage_OptionsSummary_SystemDefaultLanguageLabel";
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
        Info: new SisterBackstageInfoPaneTextDescriptor(
            Text(FreePBackstagePaneResourceKeys.InfoHeading, "Info"),
            Text(FreePBackstagePaneResourceKeys.InfoLocationLabel, "Location"),
            Text(FreePBackstagePaneResourceKeys.InfoNotSavedYet, "Not saved yet"),
            Text(FreePBackstagePaneResourceKeys.InfoPropertiesHeading, "Properties"),
            Text(FreePBackstagePaneResourceKeys.InfoStatisticsHeading, "Statistics"),
            Text(FreePBackstagePaneResourceKeys.InfoDirtySuffix, "  (unsaved changes)"),
            new SisterBackstageCorePropertiesTextDescriptor(
                Text(FreePBackstagePaneResourceKeys.InfoTitleLabel, "Title"),
                Text(FreePBackstagePaneResourceKeys.InfoAuthorLabel, "Author"),
                Text(FreePBackstagePaneResourceKeys.InfoSubjectLabel, "Subject"),
                Text(FreePBackstagePaneResourceKeys.InfoKeywordsLabel, "Keywords"),
                Text(FreePBackstagePaneResourceKeys.InfoEmptyValue, "\u2014"))),
        OptionsSummary: new ApplicationOptionsSummaryTextDescriptor(
            Text(FreePBackstagePaneResourceKeys.OptionsSummaryRecentFilesKeptLabel, "Recent files kept"),
            Text(FreePBackstagePaneResourceKeys.OptionsSummaryDefaultSaveFormatLabel, "Default save format"),
            Text(FreePBackstagePaneResourceKeys.OptionsSummaryUiLanguageLabel, "UI language"),
            Text(FreePBackstagePaneResourceKeys.OptionsSummaryDataFolderLabel, "Data folder"),
            Text(FreePBackstagePaneResourceKeys.OptionsSummarySystemDefaultLanguageLabel, "System default")));

    public static IReadOnlyList<string> RequiredResourceKeys => Descriptor.ResourceKeys;

    public static SisterBackstagePaneTextSpec BuildTextSpec(Func<string, string?>? getText = null) =>
        SisterBackstagePaneTextSpec.FromDescriptor(Descriptor, getText);

    private static ResourceTextDescriptor Text(string key, string fallbackText) => new(key, fallbackText);
}
