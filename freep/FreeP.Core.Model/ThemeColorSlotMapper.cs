namespace FreeP.Core.Model;

/// <summary>
/// Shared DrawingML theme color role mapping for FreeP import and render-time resolution.
/// </summary>
public static class ThemeColorSlotMapper
{
    private static readonly IReadOnlyDictionary<string, ThemeColorSlot> DefaultRoleMap =
        new Dictionary<string, ThemeColorSlot>(StringComparer.OrdinalIgnoreCase)
        {
            ["dk1"] = ThemeColorSlot.Dk1,
            ["tx1"] = ThemeColorSlot.Dk1,
            ["lt1"] = ThemeColorSlot.Lt1,
            ["bg1"] = ThemeColorSlot.Lt1,
            ["dk2"] = ThemeColorSlot.Dk2,
            ["tx2"] = ThemeColorSlot.Dk2,
            ["lt2"] = ThemeColorSlot.Lt2,
            ["bg2"] = ThemeColorSlot.Lt2,
            ["accent1"] = ThemeColorSlot.Accent1,
            ["accent2"] = ThemeColorSlot.Accent2,
            ["accent3"] = ThemeColorSlot.Accent3,
            ["accent4"] = ThemeColorSlot.Accent4,
            ["accent5"] = ThemeColorSlot.Accent5,
            ["accent6"] = ThemeColorSlot.Accent6,
            ["hlink"] = ThemeColorSlot.HLink,
            ["folhlink"] = ThemeColorSlot.FolHLink,
        };

    public static bool TryMapRole(string? roleName, out ThemeColorSlot slot)
    {
        slot = default;
        return !string.IsNullOrWhiteSpace(roleName) &&
               DefaultRoleMap.TryGetValue(roleName.Trim(), out slot);
    }

    public static ThemeColorSlot MapRoleToSlot(
        string? roleName,
        IReadOnlyDictionary<string, string>? effectiveClrMap,
        ThemeColorSlot fallbackSlot = ThemeColorSlot.Dk1)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return fallbackSlot;

        var normalizedRole = roleName.Trim();
        if (TryGetMappedSlotName(effectiveClrMap, roleName, normalizedRole, out var targetSlotName) &&
            TryMapRole(targetSlotName, out var mappedSlot))
        {
            return mappedSlot;
        }

        return TryMapRole(normalizedRole, out var defaultSlot)
            ? defaultSlot
            : fallbackSlot;
    }

    public static string ToSchemeColorString(ThemeColorSlot slot) =>
        slot switch
        {
            ThemeColorSlot.Dk1 => "dk1",
            ThemeColorSlot.Lt1 => "lt1",
            ThemeColorSlot.Dk2 => "dk2",
            ThemeColorSlot.Lt2 => "lt2",
            ThemeColorSlot.Accent1 => "accent1",
            ThemeColorSlot.Accent2 => "accent2",
            ThemeColorSlot.Accent3 => "accent3",
            ThemeColorSlot.Accent4 => "accent4",
            ThemeColorSlot.Accent5 => "accent5",
            ThemeColorSlot.Accent6 => "accent6",
            ThemeColorSlot.HLink => "hlink",
            ThemeColorSlot.FolHLink => "folHlink",
            _ => "dk1"
        };

    private static bool TryGetMappedSlotName(
        IReadOnlyDictionary<string, string>? effectiveClrMap,
        string roleName,
        string normalizedRole,
        out string targetSlotName)
    {
        targetSlotName = string.Empty;
        if (effectiveClrMap is null)
            return false;

        if (effectiveClrMap.TryGetValue(roleName, out var mappedSlotName) &&
            !string.IsNullOrWhiteSpace(mappedSlotName))
        {
            targetSlotName = mappedSlotName;
            return true;
        }

        if (!string.Equals(roleName, normalizedRole, StringComparison.Ordinal) &&
            effectiveClrMap.TryGetValue(normalizedRole, out mappedSlotName) &&
            !string.IsNullOrWhiteSpace(mappedSlotName))
        {
            targetSlotName = mappedSlotName;
            return true;
        }

        return false;
    }
}
