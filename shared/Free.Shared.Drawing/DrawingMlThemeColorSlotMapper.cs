namespace Free.Shared.Drawing;

public static class DrawingMlThemeColorSlotMapper
{
    private static readonly IReadOnlyDictionary<string, DrawingMlThemeColorSlot> DefaultRoleMap =
        new Dictionary<string, DrawingMlThemeColorSlot>(StringComparer.OrdinalIgnoreCase)
        {
            ["dk1"] = DrawingMlThemeColorSlot.Dark1,
            ["tx1"] = DrawingMlThemeColorSlot.Dark1,
            ["lt1"] = DrawingMlThemeColorSlot.Light1,
            ["bg1"] = DrawingMlThemeColorSlot.Light1,
            ["dk2"] = DrawingMlThemeColorSlot.Dark2,
            ["tx2"] = DrawingMlThemeColorSlot.Dark2,
            ["lt2"] = DrawingMlThemeColorSlot.Light2,
            ["bg2"] = DrawingMlThemeColorSlot.Light2,
            ["accent1"] = DrawingMlThemeColorSlot.Accent1,
            ["accent2"] = DrawingMlThemeColorSlot.Accent2,
            ["accent3"] = DrawingMlThemeColorSlot.Accent3,
            ["accent4"] = DrawingMlThemeColorSlot.Accent4,
            ["accent5"] = DrawingMlThemeColorSlot.Accent5,
            ["accent6"] = DrawingMlThemeColorSlot.Accent6,
            ["hlink"] = DrawingMlThemeColorSlot.Hyperlink,
            ["folhlink"] = DrawingMlThemeColorSlot.FollowedHyperlink,
        };

    private static readonly (DrawingMlThemeColorSlot Slot, string ElementName)[] ColorSchemeElementEntries =
    [
        (DrawingMlThemeColorSlot.Dark1, "dk1"),
        (DrawingMlThemeColorSlot.Light1, "lt1"),
        (DrawingMlThemeColorSlot.Dark2, "dk2"),
        (DrawingMlThemeColorSlot.Light2, "lt2"),
        (DrawingMlThemeColorSlot.Accent1, "accent1"),
        (DrawingMlThemeColorSlot.Accent2, "accent2"),
        (DrawingMlThemeColorSlot.Accent3, "accent3"),
        (DrawingMlThemeColorSlot.Accent4, "accent4"),
        (DrawingMlThemeColorSlot.Accent5, "accent5"),
        (DrawingMlThemeColorSlot.Accent6, "accent6"),
        (DrawingMlThemeColorSlot.Hyperlink, "hlink"),
        (DrawingMlThemeColorSlot.FollowedHyperlink, "folHlink")
    ];

    public static IReadOnlyList<(DrawingMlThemeColorSlot Slot, string ElementName)> ColorSchemeElements =>
        ColorSchemeElementEntries;

    public static bool TryMapRole(string? roleName, out DrawingMlThemeColorSlot slot)
    {
        slot = default;
        return !string.IsNullOrWhiteSpace(roleName) &&
               DefaultRoleMap.TryGetValue(roleName.Trim(), out slot);
    }

    public static DrawingMlThemeColorSlot MapRoleToSlot(
        string? roleName,
        IReadOnlyDictionary<string, string>? effectiveColorMap,
        DrawingMlThemeColorSlot fallbackSlot = DrawingMlThemeColorSlot.Dark1)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return fallbackSlot;

        var normalizedRole = roleName.Trim();
        if (TryGetMappedSlotName(effectiveColorMap, roleName, normalizedRole, out var targetSlotName) &&
            TryMapRole(targetSlotName, out var mappedSlot))
        {
            return mappedSlot;
        }

        return TryMapRole(normalizedRole, out var defaultSlot)
            ? defaultSlot
            : fallbackSlot;
    }

    public static string ToSchemeColorValue(DrawingMlThemeColorSlot slot) =>
        slot switch
        {
            DrawingMlThemeColorSlot.Dark1 => "dk1",
            DrawingMlThemeColorSlot.Light1 => "lt1",
            DrawingMlThemeColorSlot.Dark2 => "dk2",
            DrawingMlThemeColorSlot.Light2 => "lt2",
            DrawingMlThemeColorSlot.Accent1 => "accent1",
            DrawingMlThemeColorSlot.Accent2 => "accent2",
            DrawingMlThemeColorSlot.Accent3 => "accent3",
            DrawingMlThemeColorSlot.Accent4 => "accent4",
            DrawingMlThemeColorSlot.Accent5 => "accent5",
            DrawingMlThemeColorSlot.Accent6 => "accent6",
            DrawingMlThemeColorSlot.Hyperlink => "hlink",
            DrawingMlThemeColorSlot.FollowedHyperlink => "folHlink",
            _ => "dk1"
        };

    private static bool TryGetMappedSlotName(
        IReadOnlyDictionary<string, string>? effectiveColorMap,
        string roleName,
        string normalizedRole,
        out string targetSlotName)
    {
        targetSlotName = string.Empty;
        if (effectiveColorMap is null)
            return false;

        if (effectiveColorMap.TryGetValue(roleName, out var mappedSlotName) &&
            !string.IsNullOrWhiteSpace(mappedSlotName))
        {
            targetSlotName = mappedSlotName;
            return true;
        }

        if (!string.Equals(roleName, normalizedRole, StringComparison.Ordinal) &&
            effectiveColorMap.TryGetValue(normalizedRole, out mappedSlotName) &&
            !string.IsNullOrWhiteSpace(mappedSlotName))
        {
            targetSlotName = mappedSlotName;
            return true;
        }

        return false;
    }
}
