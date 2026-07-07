using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetProtectionMetadataWriter
{
    private static readonly SheetProtectionPermission[] DefaultProtectionPermissions =
    [
        SheetProtectionPermission.SelectLockedCells,
        SheetProtectionPermission.SelectUnlockedCells
    ];

    public static bool HasProtectionState(Sheet sheet) =>
        sheet.ProtectionMetadata is not null ||
        sheet.IsProtected &&
            (!string.IsNullOrWhiteSpace(sheet.ProtectionPassword) || HasNonDefaultPermissions(sheet));

    private static bool HasNonDefaultPermissions(Sheet sheet) =>
        sheet.ProtectionPermissions.Count != DefaultProtectionPermissions.Length ||
        !DefaultProtectionPermissions.All(sheet.ProtectionPermissions.Contains);

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        Save(session, workbook);
    }

    internal static void Save(XlsxWorksheetXmlEditSession session, Workbook workbook)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets)
        {
            var metadata = sheet.ProtectionMetadata;
            if (metadata is null)
            {
                if (!sheet.IsProtected)
                    continue;
                if (string.IsNullOrWhiteSpace(sheet.ProtectionPassword) && !HasNonDefaultPermissions(sheet))
                    continue;
            }

            var hasProtectionPassword = !string.IsNullOrWhiteSpace(sheet.ProtectionPassword);

            if (!session.TryGetWorksheet(sheet, out var worksheetEdit))
                continue;

            var root = worksheetEdit.Root;
            if (!sheet.IsProtected)
            {
                root.Element(worksheetNs + "sheetProtection")?.Remove();
                session.MarkDirty(worksheetEdit);
                continue;
            }

            var protection = root.Element(worksheetNs + "sheetProtection");
            if (protection is null)
            {
                protection = new XElement(worksheetNs + "sheetProtection");
                InsertSheetProtection(root, worksheetNs, protection);
            }

            if (metadata is not null)
            {
                var (protAttrs, _) = XmlNativeBagSerializer.Deserialize(metadata.Get("sheetProtection"));
                // "sheet"/"password" are modeled directly on Sheet; every permission boolean is
                // modeled via Sheet.ProtectionPermissions (applied below by
                // XlsxSheetProtectionPermissionMapper.Write) — none of those are sourced from the
                // opaque metadata bag even if an old/foreign bag happens to still carry them.
                XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(
                    protection,
                    protAttrs,
                    ["sheet", "password", .. XlsxSheetProtectionPermissionMapper.AttributeNames]);
            }

            if (sheet.IsProtected)
            {
                protection.SetAttributeValue("sheet", "1");
                XlsxSheetProtectionPermissionMapper.Write(protection, sheet.ProtectionPermissions);
            }

            XlsxWorksheetProtectionNormalizer.NormalizeElement(protection);

            // The modern ISO 29500 hash quartet (algorithmName/hashValue/saltValue/spinCount) only
            // ever reaches this element via the preserved ProtectionMetadata bag applied above.
            // ProtectSheetCommand/UnprotectSheetCommand clear sheet.ProtectionMetadata whenever the
            // password changes (see SheetProtectionCommands.cs), so a hashValue surviving to this
            // point is always the still-current verifier for the sheet's current password -- never a
            // stale one left behind by a since-superseded password.
            var hasAdvancedHash = protection.Attribute("hashValue") is not null;
            if (hasAdvancedHash)
            {
                // The modern ISO 29500 hash is authoritative (preserved verbatim from
                // ProtectionMetadata above); sheet.ProtectionPassword only ever holds the encoded
                // mirror of that same hash in this case (see
                // ProtectionPasswordHelper.EncodeIso29500Hash), never a real legacy password to
                // re-derive a hash from.
                protection.Attribute("password")?.Remove();
            }
            else if (hasProtectionPassword && !ProtectionPasswordHelper.IsIso29500Hash(sheet.ProtectionPassword))
            {
                protection.SetAttributeValue(
                    "password",
                    XlsxWorkbookMetadataXmlHelper.ToLegacyPasswordHash(sheet.ProtectionPassword!));
            }

            XlsxWorksheetProtectionNormalizer.NormalizeElement(protection);
            session.MarkDirty(worksheetEdit);
        }
    }

    private static void InsertSheetProtection(XElement root, XNamespace worksheetNs, XElement protection)
    {
        var protectedRanges = root.Element(worksheetNs + "protectedRanges");
        if (protectedRanges is not null)
        {
            protectedRanges.AddBeforeSelf(protection);
            return;
        }

        var sheetData = root.Element(worksheetNs + "sheetData");
        if (sheetData is not null)
        {
            sheetData.AddAfterSelf(protection);
            return;
        }

        root.Add(protection);
    }

}
