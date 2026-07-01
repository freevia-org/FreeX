using FreeX.Core.Model;

namespace FreeX.App.Presentation.Protection;

/// <summary>
/// The canonical, ordered set of Protect Sheet allowed-action toggles and their defaults.
/// </summary>
/// <remarks>
/// The order and default checked state mirror the Protect Sheet checklist the desktop hosts
/// already render: only "Select locked cells" and "Select unlocked cells" start checked; the
/// remaining thirteen actions are restricted by default. Every entry maps one-to-one onto a Core
/// <see cref="SheetProtectionPermission"/>, so the Core model is the single source of truth for
/// which actions exist — this type only adds ordering and defaults on top.
/// </remarks>
public static class SheetProtectionOptions
{
    /// <summary>
    /// All fifteen allowed-action toggles in the exact order they appear in the dialog checklist,
    /// each carrying its default checked state.
    /// </summary>
    public static IReadOnlyList<SheetProtectionOption> All { get; } =
    [
        new(SheetProtectionPermission.SelectLockedCells, DefaultEnabled: true, "Protection_PermissionSelectLockedCells"),
        new(SheetProtectionPermission.SelectUnlockedCells, DefaultEnabled: true, "Protection_PermissionSelectUnlockedCells"),
        new(SheetProtectionPermission.FormatCells, DefaultEnabled: false, "Protection_PermissionFormatCells"),
        new(SheetProtectionPermission.FormatColumns, DefaultEnabled: false, "Protection_PermissionFormatColumns"),
        new(SheetProtectionPermission.FormatRows, DefaultEnabled: false, "Protection_PermissionFormatRows"),
        new(SheetProtectionPermission.InsertColumns, DefaultEnabled: false, "Protection_PermissionInsertColumns"),
        new(SheetProtectionPermission.InsertRows, DefaultEnabled: false, "Protection_PermissionInsertRows"),
        new(SheetProtectionPermission.InsertHyperlinks, DefaultEnabled: false, "Protection_PermissionInsertHyperlinks"),
        new(SheetProtectionPermission.DeleteColumns, DefaultEnabled: false, "Protection_PermissionDeleteColumns"),
        new(SheetProtectionPermission.DeleteRows, DefaultEnabled: false, "Protection_PermissionDeleteRows"),
        new(SheetProtectionPermission.Sort, DefaultEnabled: false, "Protection_PermissionSort"),
        new(SheetProtectionPermission.UseAutoFilter, DefaultEnabled: false, "Protection_PermissionUseAutoFilter"),
        new(SheetProtectionPermission.UsePivotTableReports, DefaultEnabled: false, "Protection_PermissionUsePivotTableReports"),
        new(SheetProtectionPermission.EditObjects, DefaultEnabled: false, "Protection_PermissionEditObjects"),
        new(SheetProtectionPermission.EditScenarios, DefaultEnabled: false, "Protection_PermissionEditScenarios"),
    ];

    /// <summary>The permissions, in dialog order, that start checked for an unprotected sheet.</summary>
    public static IReadOnlyList<SheetProtectionPermission> DefaultEnabledPermissions { get; } =
        All.Where(option => option.DefaultEnabled).Select(option => option.Permission).ToList();

    /// <summary>All permissions in dialog order, regardless of default state.</summary>
    public static IReadOnlyList<SheetProtectionPermission> OrderedPermissions { get; } =
        All.Select(option => option.Permission).ToList();
}
