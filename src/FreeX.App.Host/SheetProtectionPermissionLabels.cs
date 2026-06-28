using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class SheetProtectionPermissionLabels
{
    public static IReadOnlyList<string> GetDefaultSheetPermissions() =>
        SheetProtectionOptions.All.Select(FormatSheetPermission).ToList();

    public static IReadOnlyList<string> GetDefaultSelectedSheetPermissions() =>
        SheetProtectionOptions.All
            .Where(option => option.DefaultEnabled)
            .Select(FormatSheetPermission)
            .ToList();

    public static IReadOnlyList<SheetProtectionPermission> ParseSheetPermissions(IEnumerable<string> labels) =>
        labels.Select(ParseSheetPermission)
            .Where(permission => permission is not null)
            .Select(permission => permission!.Value)
            .Distinct()
            .ToList();

    public static string FormatSheetPermission(SheetProtectionPermission permission)
    {
        foreach (var option in SheetProtectionOptions.All)
        {
            if (option.Permission == permission)
                return UiText.Get(option.LabelKey);
        }

        return permission.ToString();
    }

    private static string FormatSheetPermission(SheetProtectionOption option) =>
        UiText.Get(option.LabelKey);

    private static SheetProtectionPermission? ParseSheetPermission(string label)
    {
        foreach (var option in SheetProtectionOptions.All)
        {
            if (string.Equals(FormatSheetPermission(option), label, StringComparison.Ordinal))
                return option.Permission;
        }

        return null;
    }
}
