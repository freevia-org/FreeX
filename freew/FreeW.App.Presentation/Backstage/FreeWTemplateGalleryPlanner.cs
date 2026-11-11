using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Backstage;

public static class FreeWTemplateGalleryResourceKeys
{
    public const string FeaturedHeading = "FreeW_Backstage_New_FeaturedHeading";
    public const string PersonalHeading = "FreeW_Backstage_New_PersonalHeading";
    public const string PersonalEmptyText = "FreeW_Backstage_New_PersonalEmptyText";
    public const string PersonalFolderFormat = "FreeW_Backstage_New_PersonalFolderFormat";
    public const string PersonalTileDescriptionFormat = "FreeW_Backstage_New_PersonalTileDescriptionFormat";

    public static string TemplateName(string templateId) => $"FreeW_Template_{KeyToken(templateId)}_Name";

    public static string TemplateDescription(string templateId) => $"FreeW_Template_{KeyToken(templateId)}_Description";

    // "business-letter" -> "BusinessLetter"
    private static string KeyToken(string templateId) =>
        string.Concat(templateId
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}

/// <summary>One clickable template tile: caption, miniature page, and the action that creates the document.</summary>
public sealed record FreeWTemplateTile(
    string Id,
    string Caption,
    string Description,
    string AutomationId,
    IReadOnlyList<TemplateThumbnailMark> Thumbnail,
    Action Invoke);

/// <summary>A titled row of tiles ("Featured", "Personal") with optional empty-state and footer text.</summary>
public sealed record FreeWTemplateSection(
    string Heading,
    IReadOnlyList<FreeWTemplateTile> Tiles,
    string? EmptyText = null,
    string? FooterText = null);

public sealed record FreeWTemplateGallerySpec(
    string Heading,
    IReadOnlyList<FreeWTemplateSection> Sections);

/// <summary>
/// Plans FreeW's File &gt; New template gallery: the built-in templates from
/// <see cref="BuiltInDocumentTemplates"/> and the user's personal .dotx/.dotm files. Renderer-neutral;
/// hosts draw the tiles and supply the callbacks that actually create the document.
/// </summary>
public static class FreeWTemplateGalleryPlanner
{
    public const string TileAutomationIdPrefix = "BackstageTemplate_";
    public const string PersonalTileAutomationIdPrefix = "BackstagePersonalTemplate_";

    private static readonly ResourceTextDescriptor FeaturedHeading =
        new(FreeWTemplateGalleryResourceKeys.FeaturedHeading, "Featured");
    private static readonly ResourceTextDescriptor PersonalHeading =
        new(FreeWTemplateGalleryResourceKeys.PersonalHeading, "Personal");
    private static readonly ResourceTextDescriptor PersonalEmptyText = new(
        FreeWTemplateGalleryResourceKeys.PersonalEmptyText,
        "No personal templates yet. Use Save As and choose Word Template (*.dotx) to add one.");
    private static readonly ResourceTextDescriptor PersonalFolderFormat =
        new(FreeWTemplateGalleryResourceKeys.PersonalFolderFormat, "Templates folder: {0}");
    private static readonly ResourceTextDescriptor PersonalTileDescriptionFormat = new(
        FreeWTemplateGalleryResourceKeys.PersonalTileDescriptionFormat,
        "Create a new document based on {0}.");

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<TemplateThumbnailMark>>> BuiltInThumbnails =
        new(() => BuiltInDocumentTemplates.All.ToDictionary(
            template => template.Id,
            template => FreeWTemplateThumbnailPlanner.Build(template.Create()),
            StringComparer.OrdinalIgnoreCase));

    /// <summary>Every resource key the gallery resolves, for the neutral-catalog integrity tests.</summary>
    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
        new[]
        {
            FreeWBackstagePaneResourceKeys.TemplateHeading,
            FreeWBackstagePaneResourceKeys.TemplateTileCaption,
            FeaturedHeading.ResourceKey,
            PersonalHeading.ResourceKey,
            PersonalEmptyText.ResourceKey,
            PersonalFolderFormat.ResourceKey,
            PersonalTileDescriptionFormat.ResourceKey,
        }
        .Concat(BuiltInDocumentTemplates.All.SelectMany(template => new[]
        {
            FreeWTemplateGalleryResourceKeys.TemplateName(template.Id),
            FreeWTemplateGalleryResourceKeys.TemplateDescription(template.Id),
        }))
        .ToArray();

    /// <summary>The localized display name of a built-in template (English fallback when unresolved).</summary>
    public static string TemplateName(BuiltInDocumentTemplate template, Func<string, string?>? getText = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new ResourceTextDescriptor(
            FreeWTemplateGalleryResourceKeys.TemplateName(template.Id),
            template.DefaultName).Resolve(getText);
    }

    public static string TemplateDescription(BuiltInDocumentTemplate template, Func<string, string?>? getText = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new ResourceTextDescriptor(
            FreeWTemplateGalleryResourceKeys.TemplateDescription(template.Id),
            template.DefaultDescription).Resolve(getText);
    }

    /// <summary>
    /// Builds the gallery. <paramref name="newFromBuiltIn"/> null means the host cannot create documents
    /// from built-in templates, so only "Blank document" is offered; <paramref name="newFromTemplateFile"/>
    /// null hides the Personal section entirely rather than listing templates that cannot be used.
    /// </summary>
    public static FreeWTemplateGallerySpec Build(
        Action newBlankDocument,
        Action<string>? newFromBuiltIn,
        Action<string>? newFromTemplateFile,
        IReadOnlyList<PersonalTemplateEntry>? personalTemplates,
        string? personalTemplatesFolder,
        Func<string, string?>? getText = null)
    {
        ArgumentNullException.ThrowIfNull(newBlankDocument);

        var paneText = FreeWBackstagePaneTextCatalog.BuildTextSpec(getText);
        var sections = new List<FreeWTemplateSection>
        {
            new(
                FeaturedHeading.Resolve(getText),
                BuiltInDocumentTemplates.All
                    .Where(template => newFromBuiltIn is not null || IsBlank(template))
                    .Select(template => new FreeWTemplateTile(
                        template.Id,
                        TemplateName(template, getText),
                        TemplateDescription(template, getText),
                        TileAutomationIdPrefix + AutomationIdToken.KeepLettersAndDigits(template.Id),
                        BuiltInThumbnails.Value[template.Id],
                        IsBlank(template) ? newBlankDocument : () => newFromBuiltIn!(template.Id)))
                    .ToArray()),
        };

        if (newFromTemplateFile is not null)
        {
            var descriptionFormat = PersonalTileDescriptionFormat.Resolve(getText);
            var tiles = (personalTemplates ?? [])
                .Select(entry => new FreeWTemplateTile(
                    entry.Path,
                    entry.DisplayName,
                    SafeFormat(descriptionFormat, System.IO.Path.GetFileName(entry.Path)),
                    PersonalTileAutomationIdPrefix + AutomationIdToken.KeepLettersAndDigits(entry.DisplayName),
                    FreeWTemplateThumbnailPlanner.Generic,
                    () => newFromTemplateFile(entry.Path)))
                .ToArray();
            sections.Add(new FreeWTemplateSection(
                PersonalHeading.Resolve(getText),
                tiles,
                EmptyText: tiles.Length == 0 ? PersonalEmptyText.Resolve(getText) : null,
                FooterText: string.IsNullOrWhiteSpace(personalTemplatesFolder)
                    ? null
                    : SafeFormat(PersonalFolderFormat.Resolve(getText), personalTemplatesFolder)));
        }

        return new FreeWTemplateGallerySpec(paneText.TemplateHeading, sections);
    }

    private static bool IsBlank(BuiltInDocumentTemplate template) =>
        string.Equals(template.Id, BuiltInDocumentTemplates.BlankId, StringComparison.Ordinal);

    private static string SafeFormat(string format, string argument)
    {
        try
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, format, argument);
        }
        catch (FormatException)
        {
            return format + " " + argument;
        }
    }
}
