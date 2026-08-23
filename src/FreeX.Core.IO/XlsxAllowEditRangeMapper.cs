using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxAllowEditRangeMapper
{
    public static IReadOnlyList<GridRange> Read(XDocument worksheetXml, XNamespace worksheetNs) =>
        Read(worksheetXml, worksheetNs, out _);

    /// <summary>
    /// Reads the protected ranges, additionally returning each range's own password (Excel's
    /// per-range "Range Password", distinct from the sheet password) encoded in the same form
    /// <see cref="ProtectionPasswordHelper"/> understands. A range with no <c>password</c>/
    /// <c>hashValue</c> attribute maps to a null password (freely editable once the range itself is
    /// reachable).
    /// </summary>
    public static IReadOnlyList<GridRange> Read(
        XDocument worksheetXml,
        XNamespace worksheetNs,
        out Dictionary<GridRange, string?> passwordsByRange)
    {
        var ranges = new List<GridRange>();
        passwordsByRange = [];
        var tempSheet = SheetId.New();
        foreach (var protectedRange in worksheetXml.Root?
                     .Element(worksheetNs + "protectedRanges")?
                     .Elements(worksheetNs + "protectedRange") ?? [])
        {
            var sqref = protectedRange.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref))
                continue;

            // A protectedRange's sqref may list several disjoint areas separated by spaces (Excel's
            // "Allow Users to Edit Ranges" supports multi-area ranges, e.g. "B2:B10 D2:D10"). Model
            // each area as its own AllowEditRange (sharing the same range password) so
            // CommandGuards.CanEditCell honors every area, not just the first.
            var tokens = sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
                continue;

            var password = ReadRangePassword(protectedRange);
            foreach (var token in tokens)
            {
                if (!XlsxSqrefParser.TryParseRangeToken(token, tempSheet, out var range))
                    continue;

                ranges.Add(range);
                if (password is not null)
                    passwordsByRange[range] = password;
            }
        }

        return ranges;
    }

    /// <summary>
    /// Decodes a <c>&lt;protectedRange&gt;</c> element's password into the stored-password string
    /// form <see cref="ProtectionPasswordHelper"/> understands. Excel writes either the legacy
    /// <c>password</c> attribute (a 4-hex-digit XOR/rotate verifier) or the modern
    /// <c>algorithmName</c>/<c>hashValue</c>/<c>saltValue</c>/<c>spinCount</c> quartet; the modern
    /// form takes precedence when both are somehow present, mirroring sheetProtection handling.
    /// </summary>
    private static string? ReadRangePassword(XElement protectedRange)
    {
        var hashValue = protectedRange.Attribute("hashValue")?.Value;
        if (!string.IsNullOrWhiteSpace(hashValue))
        {
            return ProtectionPasswordHelper.EncodeIso29500Hash(
                protectedRange.Attribute("algorithmName")?.Value,
                protectedRange.Attribute("spinCount")?.Value,
                protectedRange.Attribute("saltValue")?.Value,
                hashValue);
        }

        var legacyPassword = protectedRange.Attribute("password")?.Value;
        return string.IsNullOrWhiteSpace(legacyPassword)
            ? null
            : ProtectionPasswordHelper.ToLegacyPasswordHash(legacyPassword);
    }

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XlsxWorksheetPackageEditTraversal.EditSourceMapped(
            xlsxStream,
            workbook,
            sheet => sheet.AllowEditRanges.Count > 0,
            (sheet, root) =>
        {
            root.Elements(workbookNs + "protectedRanges").Remove();
            var protectedRanges = new XElement(workbookNs + "protectedRanges",
                sheet.AllowEditRanges.Select((range, index) =>
                    BuildProtectedRangeElement(workbookNs, range, index, sheet.AllowEditRangePasswords)));

            XlsxWorksheetElementOrder.Insert(root, protectedRanges);
        });
    }

    public static IReadOnlySet<string> GetModeledReferences(Workbook workbook, string sheetName)
    {
        var sheet = workbook.GetSheet(sheetName);
        return sheet is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : sheet.AllowEditRanges
                .Select(range => range.ToString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a single <c>&lt;protectedRange&gt;</c> element, including its own password attributes
    /// (legacy <c>password</c> or modern <c>algorithmName</c>/<c>hashValue</c>/<c>saltValue</c>/
    /// <c>spinCount</c>) when <paramref name="passwordsByRange"/> has an entry for it.
    /// </summary>
    private static XElement BuildProtectedRangeElement(
        XNamespace workbookNs,
        GridRange range,
        int index,
        IReadOnlyDictionary<GridRange, string?> passwordsByRange)
    {
        var element = new XElement(workbookNs + "protectedRange",
            new XAttribute("name", $"FreeXAllowEditRange{index + 1}"),
            new XAttribute("sqref", range.ToString()));

        if (!passwordsByRange.TryGetValue(range, out var storedPassword) || string.IsNullOrEmpty(storedPassword))
            return element;

        if (ProtectionPasswordHelper.IsIso29500Hash(storedPassword))
        {
            var parts = storedPassword.Split(':', 5);
            if (parts.Length == 5)
            {
                element.SetAttributeValue("algorithmName", parts[1]);
                element.SetAttributeValue("hashValue", parts[4]);
                element.SetAttributeValue("saltValue", parts[3]);
                element.SetAttributeValue("spinCount", parts[2]);
            }
        }
        else
        {
            element.SetAttributeValue("password", ProtectionPasswordHelper.ToLegacyPasswordHash(storedPassword));
        }

        return element;
    }

}
