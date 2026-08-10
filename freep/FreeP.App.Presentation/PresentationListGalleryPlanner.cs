using Free.Shared.IO;
using Free.Shared.Opc;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationListGalleryKind
{
    Bullets,
    Numbering,
}

public enum PresentationListGalleryItemKind
{
    CharacterBullet,
    Numbering,
    ImageBullet,
}

public sealed record PresentationPictureBulletPayload(
    byte[] ImageBytes,
    string ContentType,
    string? SourceName = null)
{
    public bool IsValid =>
        ImageBytes.Length > 0 &&
        !string.IsNullOrWhiteSpace(ContentType);
}

public sealed record PresentationListGalleryItem(
    string CommandId,
    string DisplayName,
    string PreviewText,
    PresentationListGalleryItemKind Kind,
    TableCellListPresetDescriptor? ListPreset,
    bool IsEnabled,
    string AccessibilityName);

public sealed record PresentationListGalleryPlan(
    PresentationListGalleryKind Kind,
    string OwnerCommandId,
    string DisplayName,
    IReadOnlyList<PresentationListGalleryItem> Items,
    string ImageBulletCommandId)
{
    public IReadOnlyList<PresentationListGalleryItem> EnabledItems =>
        Items.Where(item => item.IsEnabled).ToArray();
}

public static class PresentationListGalleryPlanner
{
    public const string BulletsCommandId = "freep.bullets";
    public const string NumberingCommandId = "freep.numbering";
    public const string ImageBulletCommandId = "freep.bullets.picture";

    private static readonly IReadOnlyList<TableCellListPresetDescriptor> BulletPresets =
    [
        TableCellListPresetCatalog.BulletDisc,
        TableCellListPresetCatalog.BulletHollowCircle,
        TableCellListPresetCatalog.BulletSquare,
        TableCellListPresetCatalog.BulletDash,
        TableCellListPresetCatalog.BulletCheck,
    ];

    private static readonly IReadOnlyList<TableCellListPresetDescriptor> NumberingPresets =
    [
        TableCellListPresetCatalog.NumberArabicPeriod,
        TableCellListPresetCatalog.NumberRomanUpperPeriod,
        TableCellListPresetCatalog.NumberRomanLowerPeriod,
        TableCellListPresetCatalog.NumberAlphaUpperPeriod,
        TableCellListPresetCatalog.NumberAlphaLowerPeriod,
    ];

    public static PresentationListGalleryPlan BuildBulletGalleryPlan() =>
        new(
            PresentationListGalleryKind.Bullets,
            BulletsCommandId,
            "Bullets",
            BulletPresets
                .Select(preset => CreatePresetItem(BulletsCommandId, preset, PresentationListGalleryItemKind.CharacterBullet))
                .Append(new PresentationListGalleryItem(
                    ImageBulletCommandId,
                    "Picture...",
                    "[image]",
                    PresentationListGalleryItemKind.ImageBullet,
                    null,
                    IsEnabled: true,
                    "Choose a picture bullet image"))
                .ToArray(),
            ImageBulletCommandId);

    public static PresentationListGalleryPlan BuildNumberingGalleryPlan() =>
        new(
            PresentationListGalleryKind.Numbering,
            NumberingCommandId,
            "Numbering",
            NumberingPresets
                .Select(preset => CreatePresetItem(NumberingCommandId, preset, PresentationListGalleryItemKind.Numbering))
                .ToArray(),
            ImageBulletCommandId);

    public static IReadOnlyList<PresentationListGalleryPlan> BuildPlans() =>
    [
        BuildBulletGalleryPlan(),
        BuildNumberingGalleryPlan(),
    ];

    public static bool TryGetPresetCommand(
        string? commandId,
        out TableCellListPresetDescriptor? preset)
    {
        preset = null;
        if (string.IsNullOrWhiteSpace(commandId))
            return false;

        foreach (var item in BuildPlans().SelectMany(plan => plan.Items))
        {
            if (!item.IsEnabled ||
                !StringComparer.Ordinal.Equals(item.CommandId, commandId) ||
                item.ListPreset is null)
            {
                continue;
            }

            preset = item.ListPreset;
            return true;
        }

        return false;
    }

    private static PresentationListGalleryItem CreatePresetItem(
        string ownerCommandId,
        TableCellListPresetDescriptor preset,
        PresentationListGalleryItemKind kind)
    {
        return new PresentationListGalleryItem(
            $"{ownerCommandId}.{preset.Id}",
            preset.DisplayName,
            GetPresetPreviewText(preset),
            kind,
            preset,
            IsEnabled: true,
            $"{preset.DisplayName} list preset");
    }

    public static string GetPresetPreviewText(TableCellListPresetDescriptor preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return preset.BulletKind == BulletKind.Char
            ? $"{preset.BulletChar}  {preset.DisplayName}"
            : $"{GetNumberingPreview(preset)}  {preset.DisplayName}";
    }

    private static string GetNumberingPreview(TableCellListPresetDescriptor preset) =>
        preset.AutoNumType switch
        {
            AutoNumType.RomanUcPeriod => "I.",
            AutoNumType.RomanLcPeriod => "i.",
            AutoNumType.AlphaUcPeriod => "A.",
            AutoNumType.AlphaLcPeriod => "a.",
            _ => "1.",
        };
}

public static class PresentationPictureBulletAuthoringPlanner
{
    public const string DefaultContentType = "image/png";

    public static PresentationPictureBulletPayload CreatePayload(
        byte[] imageBytes,
        string? contentType,
        string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var normalizedContentType = string.IsNullOrWhiteSpace(contentType)
            ? InferContentType(sourceName)
            : contentType.Trim();

        return new PresentationPictureBulletPayload(
            imageBytes.ToArray(),
            normalizedContentType,
            sourceName);
    }

    public static PresentationPictureBulletPayload CreatePayloadFromFileName(
        byte[] imageBytes,
        string? fileName) =>
        CreatePayload(imageBytes, InferContentType(fileName), fileName);

    public static ImagePart CreateImagePart(PresentationPictureBulletPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return new ImagePart
        {
            Bytes = payload.ImageBytes.ToArray(),
            ContentType = string.IsNullOrWhiteSpace(payload.ContentType)
                ? DefaultContentType
                : payload.ContentType
        };
    }

    public static void ApplyToParagraph(
        Paragraph paragraph,
        PresentationPictureBulletPayload payload)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(payload);

        ApplyToParagraph(paragraph, CreateImagePart(payload));
    }

    public static void ApplyToParagraph(
        Paragraph paragraph,
        ImagePart image)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(image);

        paragraph.BulletKind = BulletKind.Image;
        paragraph.BulletImage = CloneImagePart(image);
        paragraph.BulletChar = null;
        paragraph.AutoNumType = AutoNumType.ArabicPeriod;
        paragraph.AutoNumStartAt = 1;
        paragraph.BulletSuppressed = false;
    }

    public static string InferContentType(string? fileName) =>
        OpcMediaTypes.GetContentTypeForFileNameOrExtension(
            fileName,
            OpcMediaContentTypeProfile.PresentationListGalleryPicture);

    private static ImagePart CloneImagePart(ImagePart source) =>
        new()
        {
            Bytes = source.Bytes.ToArray(),
            ContentType = string.IsNullOrWhiteSpace(source.ContentType)
                ? DefaultContentType
                : source.ContentType
        };
}
