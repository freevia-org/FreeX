namespace FreeX.Core.Model;

/// <summary>
/// Resolves the shared Light2–6 accent ordering used by Excel's built-in slicer and timeline
/// styles while keeping each control's palette construction separate.
/// </summary>
internal static class BuiltInFilterControlStylePolicy
{
    internal static WorkbookThemeColorSlot? ResolveLightAccentSlot(
        string? styleName,
        ReadOnlySpan<char> exactFamilyPrefix)
    {
        if (string.IsNullOrWhiteSpace(styleName) || exactFamilyPrefix.IsEmpty)
            return null;

        var trimmed = styleName.AsSpan().Trim();
        if (!trimmed.StartsWith(exactFamilyPrefix, StringComparison.Ordinal))
            return null;

        var suffix = trimmed[exactFamilyPrefix.Length..];
        if (suffix.Length != 1)
            return null;

        return suffix[0] switch
        {
            '2' => WorkbookThemeColorSlot.Accent2,
            '3' => WorkbookThemeColorSlot.Accent3,
            '4' => WorkbookThemeColorSlot.Accent4,
            '5' => WorkbookThemeColorSlot.Accent5,
            '6' => WorkbookThemeColorSlot.Accent6,
            _ => null,
        };
    }
}
