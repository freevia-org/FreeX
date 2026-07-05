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
    ImageBulletPlaceholder,
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
    string DeferredImageBulletCommandId)
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
                    PresentationListGalleryItemKind.ImageBulletPlaceholder,
                    null,
                    IsEnabled: false,
                    "Picture bullet chooser is deferred until media-part authoring and picker execution are implemented."))
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
        string preview = preset.BulletKind == BulletKind.Char
            ? $"{preset.BulletChar}  {preset.DisplayName}"
            : $"{GetNumberingPreview(preset)}  {preset.DisplayName}";

        return new PresentationListGalleryItem(
            $"{ownerCommandId}.{preset.Id}",
            preset.DisplayName,
            preview,
            kind,
            preset,
            IsEnabled: true,
            $"{preset.DisplayName} list preset");
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
