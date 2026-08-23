using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetCustomPropertyMapper
{
    public static IReadOnlyList<WorksheetCustomProperty> Read(XDocument worksheetXml, XNamespace worksheetNs) =>
        Read(worksheetXml, worksheetNs, null, null);

    public static IReadOnlyList<WorksheetCustomProperty> Read(
        XDocument worksheetXml,
        XNamespace worksheetNs,
        ZipArchive? archive,
        string? worksheetPath)
    {
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipTargets = archive is not null && !string.IsNullOrWhiteSpace(worksheetPath)
            ? XlsxRelationshipReader.LoadTargets(
                archive,
                XlsxPackagePath.GetRelationshipPartPath(worksheetPath),
                worksheetPath,
                packageRelNs)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var properties = new List<WorksheetCustomProperty>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var customProperty in worksheetXml.Root?
                     .Element(worksheetNs + "customProperties")?
                     .Elements(worksheetNs + "customPr") ?? [])
        {
            var name = customProperty.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                !TryReadCustomPropertyId(customProperty, relNs, relationshipTargets, out var id, out var targetPath) ||
                id <= 0 ||
                !seen.Add(name))
            {
                continue;
            }

            var binPayload = archive is not null ? TryReadCustomPropertyBinPayload(archive, targetPath, name) : null;
            properties.Add(new WorksheetCustomProperty(name, id, ReadMetadata(customProperty, binPayload)));
        }

        return properties;
    }

    public static void Save(Stream packageStream, Workbook workbook)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var workbookRels = XlsxRelationshipReader.LoadTargets(
            archive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var sheetPaths = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in workbook.Sheets)
        {
            var properties = sheet.CustomProperties
                .Where(property => !string.IsNullOrWhiteSpace(property.Name) && property.Id > 0)
                .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(property => property.Id)
                .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (properties.Count == 0 || !sheetPaths.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var worksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
            var worksheetRelsEntry = archive.GetEntry(worksheetRelsPath);
            var worksheetRelsXml = worksheetRelsEntry is null
                ? new XDocument(new XElement(packageRelNs + "Relationships"))
                : XlsxPackageXmlEditor.LoadXml(worksheetRelsEntry);

            root.Element(workbookNs + "customProperties")?.Remove();
            XlsxWorksheetElementOrder.Insert(root, new XElement(
                workbookNs + "customProperties",
                properties.Select(property =>
                {
                    var customPropertyPath = GetCustomPropertyPartPath(worksheetPath, property);
                    WriteCustomPropertyPart(archive, customPropertyPath, property);
                    XlsxPackageXmlEditor.EnsureSpecificContentType(
                        archive,
                        customPropertyPath,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.customProperty");
                    var relationshipId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                        worksheetRelsXml,
                        packageRelNs,
                        worksheetPath,
                        customPropertyPath,
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customProperty");
                    return ToXml(property, workbookNs, relNs, relationshipId);
                })));
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetRelsPath, worksheetRelsXml);
        }
    }

    public static HashSet<string> GetModeledNames(Workbook workbook, string sheetName)
    {
        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return sheet.CustomProperties
            .Where(property => !string.IsNullOrWhiteSpace(property.Name) && property.Id > 0)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static NativeXmlPreserveBag? ReadMetadata(XElement customProperty, string? binPayloadBase64)
    {
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(customProperty, attrs, ModeledCustomPropertyAttributes);

        var children = customProperty.Elements()
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();

        var serialized = XmlNativeBagSerializer.Serialize(attrs, children);
        if (serialized is null && binPayloadBase64 is null)
            return null;

        var bag = new NativeXmlPreserveBag();
        if (serialized is not null)
            bag.Set("customPr", serialized);
        if (binPayloadBase64 is not null)
            bag.Set(CustomPropertyBinPayloadMetadataKey, binPayloadBase64);
        return bag;
    }

    private static string? TryReadCustomPropertyBinPayload(ZipArchive archive, string? targetPath, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return null;

        var entry = archive.GetEntry(targetPath);
        if (entry is null)
            return null;

        using var entryStream = entry.Open();
        using var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        // FreeX's own placeholder writer encodes just the property's name as UTF-16LE with
        // no BOM. Skip capturing that trivial, fully-reconstructible case so a plain
        // FreeX-authored round trip (nothing beyond the modeled Name/Id) stays exactly as
        // before -- no Metadata noise -- while genuinely different (real Excel/VBA-authored)
        // payload bytes are still captured and preserved.
        if (bytes.AsSpan().SequenceEqual(Encoding.Unicode.GetBytes(propertyName)))
            return null;

        return Convert.ToBase64String(bytes);
    }

    private static readonly IReadOnlyCollection<string> ModeledCustomPropertyAttributes = ["name", "id"];

    // Key used to stash the original xl/customProperty/*.bin bytes (base64-encoded) in the
    // property's preserve bag so a later full-rebuild save can write back the real,
    // Excel/VBA-authored payload instead of a fabricated placeholder. See
    // R28-io-unknown-part-passthrough-deep-1.
    private const string CustomPropertyBinPayloadMetadataKey = "customPropertyBin";

    private static XElement ToXml(WorksheetCustomProperty property, XNamespace workbookNs, XNamespace relNs, string relationshipId)
    {
        var element = new XElement(
            workbookNs + "customPr",
            new XAttribute("name", property.Name),
            new XAttribute(relNs + "id", relationshipId));

        XmlNativeBagSerializer.ApplyToElement(element, property.Metadata?.Get("customPr"), ModeledCustomPropertyAttributes);

        return element;
    }

    private static bool TryReadCustomPropertyId(
        XElement customProperty,
        XNamespace relNs,
        IReadOnlyDictionary<string, string> relationshipTargets,
        out int id,
        out string? targetPath)
    {
        targetPath = null;
        var legacyId = customProperty.Attribute("id")?.Value;
        if (TryReadCustomPropertyId(legacyId, out id))
            return true;

        var relationshipId = customProperty.Attribute(relNs + "id")?.Value;
        if (!string.IsNullOrWhiteSpace(relationshipId) &&
            relationshipTargets.TryGetValue(relationshipId, out var resolvedTarget))
        {
            targetPath = resolvedTarget;
            if (TryReadCustomPropertyIdFromPartPath(resolvedTarget, out id))
                return true;
        }

        return TryReadCustomPropertyId(relationshipId, out id);
    }

    private static bool TryReadCustomPropertyId(string? value, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            return true;

        if (trimmed.StartsWith("rId", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(trimmed[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            return true;
        }

        return false;
    }

    private static bool TryReadCustomPropertyIdFromPartPath(string targetPath, out int id)
    {
        id = 0;
        var normalized = targetPath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var fileName = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        var dot = fileName.LastIndexOf('.');
        if (dot > 0)
            fileName = fileName[..dot];

        var firstSeparator = fileName.IndexOf('-');
        if (firstSeparator < 0)
            return false;

        var secondSeparator = fileName.IndexOf('-', firstSeparator + 1);
        return secondSeparator > firstSeparator + 1 &&
            int.TryParse(
                fileName[(firstSeparator + 1)..secondSeparator],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out id);
    }

    private static string GetCustomPropertyPartPath(string worksheetPath, WorksheetCustomProperty property)
    {
        var worksheetName = Path.GetFileNameWithoutExtension(worksheetPath);
        var safeName = string.Concat(property.Name.Select(character =>
            char.IsLetterOrDigit(character) ? character : '_'));
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "property";

        return $"xl/customProperty/{worksheetName}-{property.Id}-{safeName}.bin";
    }

    private static void WriteCustomPropertyPart(
        ZipArchive archive,
        string customPropertyPath,
        WorksheetCustomProperty property)
    {
        archive.GetEntry(customPropertyPath)?.Delete();
        var entry = archive.CreateEntry(customPropertyPath, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = TryGetPreservedCustomPropertyBinPayload(property) ?? Encoding.Unicode.GetBytes(property.Name);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[]? TryGetPreservedCustomPropertyBinPayload(WorksheetCustomProperty property)
    {
        var base64 = property.Metadata?.Get(CustomPropertyBinPayloadMetadataKey);
        if (string.IsNullOrEmpty(base64))
            return null;

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
