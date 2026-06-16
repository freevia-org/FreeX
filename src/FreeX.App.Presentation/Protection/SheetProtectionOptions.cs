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
        new(SheetProtectionPermission.SelectLockedCells, DefaultEnabled: true),
        new(SheetProtectionPermission.SelectUnlockedCells, DefaultEnabled: true),
        new(SheetProtectionPermission.FormatCells, DefaultEnabled: false),
        new(SheetProtectionPermission.FormatColumns, DefaultEnabled: false),
        new(SheetProtectionPermission.FormatRows, DefaultEnabled: false),
        new(SheetProtectionPermission.InsertColumns, DefaultEnabled: false),
        new(SheetProtectionPermission.InsertRows, DefaultEnabled: false),
        new(SheetProtectionPermission.InsertHyperlinks, DefaultEnabled: false),
        new(SheetProtectionPermission.DeleteColumns, DefaultEnabled: false),
        new(SheetProtectionPermission.DeleteRows, DefaultEnabled: false),
        new(SheetProtectionPermission.Sort, DefaultEnabled: false),
        new(SheetProtectionPermission.UseAutoFilter, DefaultEnabled: false),
        new(SheetProtectionPermission.UsePivotTableReports, DefaultEnabled: false),
        new(SheetProtectionPermission.EditObjects, DefaultEnabled: false),
        new(SheetProtectionPermission.EditScenarios, DefaultEnabled: false),
    ];

    /// <summary>The permissions, in dialog order, that start checked for an unprotected sheet.</summary>
    public static IReadOnlyList<SheetProtectionPermission> DefaultEnabledPermissions { get; } =
        All.Where(option => option.DefaultEnabled).Select(option => option.Permission).ToList();

    /// <summary>All permissions in dialog order, regardless of default state.</summary>
    public static IReadOnlyList<SheetProtectionPermission> OrderedPermissions { get; } =
        All.Select(option => option.Permission).ToList();
}
