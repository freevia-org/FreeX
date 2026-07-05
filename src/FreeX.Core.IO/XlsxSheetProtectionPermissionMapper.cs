using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Maps <see cref="SheetProtectionPermission"/> values to/from the boolean attributes of the
/// OOXML <c>&lt;sheetProtection&gt;</c> element (ECMA-376 §18.3.1.85, CT_SheetProtection).
/// </summary>
/// <remarks>
/// Every attribute below except <c>selectLockedCells</c>/<c>selectUnlockedCells</c> defaults to
/// <c>true</c> ("this action is prevented while the sheet is protected") when absent; the two
/// "select" attributes default to <c>false</c> ("selection is allowed"). This matches Excel's own
/// default Protect Sheet dialog state, where only the two "Select" checkboxes start checked
/// (allowed) and every other action starts unchecked (denied).
/// </remarks>
internal static class XlsxSheetProtectionPermissionMapper
{
    private static readonly (SheetProtectionPermission Permission, string AttributeName, bool DefaultAllowed)[] Entries =
    [
        (SheetProtectionPermission.SelectLockedCells, "selectLockedCells", true),
        (SheetProtectionPermission.SelectUnlockedCells, "selectUnlockedCells", true),
        (SheetProtectionPermission.FormatCells, "formatCells", false),
        (SheetProtectionPermission.FormatColumns, "formatColumns", false),
        (SheetProtectionPermission.FormatRows, "formatRows", false),
        (SheetProtectionPermission.InsertColumns, "insertColumns", false),
        (SheetProtectionPermission.InsertRows, "insertRows", false),
        (SheetProtectionPermission.InsertHyperlinks, "insertHyperlinks", false),
        (SheetProtectionPermission.DeleteColumns, "deleteColumns", false),
        (SheetProtectionPermission.DeleteRows, "deleteRows", false),
        (SheetProtectionPermission.Sort, "sort", false),
        (SheetProtectionPermission.UseAutoFilter, "autoFilter", false),
        (SheetProtectionPermission.UsePivotTableReports, "pivotTables", false),
        (SheetProtectionPermission.EditObjects, "objects", false),
        (SheetProtectionPermission.EditScenarios, "scenarios", false)
    ];

    /// <summary>
    /// The OOXML attribute names this mapper owns. Excluded from <c>Sheet.ProtectionMetadata</c>
    /// preservation/apply (alongside <c>sheet</c>/<c>password</c>) since they are now fully modeled
    /// via <see cref="SheetProtectionPermission"/>/<c>Sheet.ProtectionPermissions</c> rather than
    /// opaque native metadata.
    /// </summary>
    public static IReadOnlyCollection<string> AttributeNames { get; } =
        Entries.Select(entry => entry.AttributeName).ToArray();

    /// <summary>
    /// Reads the effective set of allowed actions from a <c>&lt;sheetProtection&gt;</c> element,
    /// applying each attribute's documented default when absent.
    /// </summary>
    public static List<SheetProtectionPermission> Read(XElement? protection)
    {
        var permissions = new List<SheetProtectionPermission>();
        foreach (var (permission, attributeName, defaultAllowed) in Entries)
        {
            var isAllowed = protection?.Attribute(attributeName) is { } attribute
                ? !IsTruthy(attribute.Value)
                : defaultAllowed;
            if (isAllowed)
                permissions.Add(permission);
        }

        return permissions;
    }

    /// <summary>
    /// Writes the "denied" (prevented) attributes explicitly as <c>"1"</c> for every permission not
    /// present in <paramref name="permissions"/> — this is always schema-correct regardless of the
    /// attribute's default. For an allowed permission, the attribute is removed only when the
    /// attribute's documented default already means "allowed"; otherwise (most permissions default
    /// to "prevented" when absent) it must be written out explicitly as <c>"0"</c>, or the absence
    /// would silently fall back to "prevented" and the granted permission would be lost.
    /// </summary>
    public static void Write(XElement protection, IReadOnlyCollection<SheetProtectionPermission> permissions)
    {
        foreach (var (permission, attributeName, defaultAllowed) in Entries)
        {
            var isAllowed = permissions.Contains(permission);
            if (!isAllowed)
                protection.SetAttributeValue(attributeName, "1");
            else if (defaultAllowed)
                protection.Attribute(attributeName)?.Remove();
            else
                protection.SetAttributeValue(attributeName, "0");
        }
    }

    private static bool IsTruthy(string? value) =>
        string.Equals(value?.Trim(), "1", StringComparison.Ordinal) ||
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}
