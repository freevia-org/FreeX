using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static partial class XlsxExcelCompatibilityNormalizer
{
    private const string WorksheetRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
    private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
    private const string PivotCacheDefinitionRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition";
    private const string PivotCacheRecordsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheRecords";
    private const string PivotTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable";
    private const string CalcChainRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain";

    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    public static void NormalizeSourcePackageSave(Stream packageStream)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        var changedWorkbook = RemoveWorkbookCustomViews(archive);
        changedWorkbook |= NormalizeCorruptPivotCaches(archive);
        if (changedWorkbook)
            RemoveCalcChain(archive);

        RemoveInvalidWorkbookExtensionAttributes(archive);

        var changedWorksheets = RemoveWorksheetCustomViews(archive);
        changedWorksheets |= ConvertPhoneLikeFormulaText(archive);
        changedWorksheets |= RemoveDuplicateWorksheetDrawingTargets(archive);
        if (changedWorksheets)
            RemoveCalcChain(archive);

        PruneMissingContentTypeOverrides(archive);
    }

    private static void RemoveInvalidWorkbookExtensionAttributes(ZipArchive archive)
    {
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var root = workbookXml?.Root;
        if (root is null)
            return;

        var changed = false;
        foreach (var workbookProperties in root
                     .Element(WorkbookNs + "extLst")?
                     .Elements(WorkbookNs + "ext")
                     .Elements(X14Ns + "workbookPr") ?? [])
        {
            var defaultImageDpi = workbookProperties.Attribute("defaultImageDpi");
            if (defaultImageDpi is null || IsValidDefaultImageDpi(defaultImageDpi.Value))
                continue;

            defaultImageDpi.Remove();
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml!);
    }

    private static bool IsValidDefaultImageDpi(string value) =>
        value is "96" or "150" or "220";

    private static bool RemoveWorkbookCustomViews(ZipArchive archive)
    {
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var root = workbookXml?.Root;
        if (root is null)
            return false;

        var customViews = root.Elements(WorkbookNs + "customWorkbookViews").ToList();
        if (customViews.Count == 0)
            return false;

        foreach (var customView in customViews)
            customView.Remove();

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml!);
        return true;
    }

    private static bool RemoveWorksheetCustomViews(ZipArchive archive)
    {
        var changed = false;
        foreach (var worksheetPath in GetWorkbookWorksheetPaths(archive))
        {
            var worksheetXml = LoadXml(archive, worksheetPath);
            var root = worksheetXml?.Root;
            if (root is null)
                continue;

            var customSheetViews = root.Elements(WorkbookNs + "customSheetViews").ToList();
            if (customSheetViews.Count == 0)
                continue;

            foreach (var customSheetView in customSheetViews)
                customSheetView.Remove();

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml!);
            changed = true;
        }

        return changed;
    }

    private static bool ConvertPhoneLikeFormulaText(ZipArchive archive)
    {
        var changed = false;
        foreach (var worksheetPath in GetWorkbookWorksheetPaths(archive))
        {
            var worksheetXml = LoadXml(archive, worksheetPath);
            var root = worksheetXml?.Root;
            if (root is null)
                continue;

            var worksheetChanged = false;
            foreach (var cell in root.Descendants(WorkbookNs + "c").ToList())
            {
                var formula = cell.Element(WorkbookNs + "f");
                if (formula is null || !IsPhoneLikeFormulaText(formula.Value))
                    continue;

                var text = formula.Value.Trim();
                formula.Remove();
                cell.Elements(WorkbookNs + "v").Remove();
                cell.SetAttributeValue("t", "str");
                cell.Add(new XElement(WorkbookNs + "v", text));
                worksheetChanged = true;
            }

            if (!worksheetChanged)
                continue;

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml!);
            changed = true;
        }

        return changed;
    }

    private static bool IsPhoneLikeFormulaText(string formulaText)
    {
        var text = formulaText.Trim();
        return text.Length > 0 &&
               text[0] == '+' &&
               text.Any(char.IsWhiteSpace) &&
               text.All(ch => char.IsDigit(ch) || ch is '+' or '-' or '.' or '(' or ')' || char.IsWhiteSpace(ch));
    }

    private static bool RemoveDuplicateWorksheetDrawingTargets(ZipArchive archive)
    {
        var changed = false;
        var activeDrawingTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var worksheetPath in GetWorkbookWorksheetPaths(archive))
        {
            var worksheetXml = LoadXml(archive, worksheetPath);
            var root = worksheetXml?.Root;
            var drawing = root?.Element(WorkbookNs + "drawing");
            var drawingRelId = drawing?.Attribute(RelNs + "id")?.Value;
            if (root is null || drawing is null || string.IsNullOrWhiteSpace(drawingRelId))
                continue;

            var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
            var relationshipsXml = LoadXml(archive, relationshipsPath);
            var relationshipsRoot = relationshipsXml?.Root;
            if (relationshipsRoot is null)
                continue;

            var drawingRelationship = relationshipsRoot.Elements(PackageRelNs + "Relationship")
                .FirstOrDefault(relationship =>
                    string.Equals(relationship.Attribute("Id")?.Value, drawingRelId, StringComparison.Ordinal) &&
                    string.Equals(relationship.Attribute("Type")?.Value, DrawingRelationshipType, StringComparison.OrdinalIgnoreCase));
            var target = drawingRelationship?.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
                continue;

            var resolvedTarget = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
            if (activeDrawingTargets.Add(resolvedTarget))
                continue;

            drawing.Remove();
            drawingRelationship!.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml!);
            XlsxPackageXmlEditor.ReplaceXml(archive, relationshipsPath, relationshipsXml!);
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeCorruptPivotCaches(ZipArchive archive)
    {
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var workbookRoot = workbookXml?.Root;
        if (workbookRoot is null)
            return false;

        var workbookRelationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var workbookRelationshipRoot = workbookRelationships?.Root;
        if (workbookRelationshipRoot is null)
            return false;

        var changed = false;
        var cachePartByCacheId = (workbookRoot
            .Element(WorkbookNs + "pivotCaches")?
            .Elements(WorkbookNs + "pivotCache")
            .Select(cache => new
            {
                CacheId = cache.Attribute("cacheId")?.Value,
                RelationshipId = cache.Attribute(RelNs + "id")?.Value
            })
            .Where(cache => !string.IsNullOrWhiteSpace(cache.CacheId) && !string.IsNullOrWhiteSpace(cache.RelationshipId))
            .Select(cache => new
            {
                cache.CacheId,
                Part = ResolveWorkbookRelationshipTarget(workbookRelationshipRoot, cache.RelationshipId!, PivotCacheDefinitionRelationshipType)
            })
            .Where(cache => !string.IsNullOrWhiteSpace(cache.Part))
            .ToDictionary(cache => cache.CacheId!, cache => cache.Part!, StringComparer.OrdinalIgnoreCase)) ??
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (cacheId, cachePart) in cachePartByCacheId)
        {
            var cacheXml = LoadXml(archive, cachePart);
            var cacheRoot = cacheXml?.Root;
            if (cacheRoot is null)
                continue;

            var cacheFields = cacheRoot.Element(WorkbookNs + "cacheFields")?
                .Elements(WorkbookNs + "cacheField")
                .ToList() ?? [];
            var sourceColumnCount = TryGetWorksheetSourceColumnCount(cacheRoot);
            if (sourceColumnCount <= 0 || cacheFields.Count <= sourceColumnCount)
                continue;

            var pivotTables = FindPivotTablesForCache(archive, cacheId).ToList();
            if (pivotTables.Count == 0)
                continue;

            var sanitizedFieldNames = cacheFields
                .Take(sourceColumnCount)
                .Select((field, index) => SanitizePivotFieldName(field.Attribute("name")?.Value, index))
                .ToList();

            RewritePivotCacheDefinition(archive, cachePart, cacheXml!, sanitizedFieldNames);
            foreach (var pivotTable in pivotTables)
                RewritePivotTableAsRefreshableSkeleton(archive, pivotTable, cacheId, sanitizedFieldNames.Count);

            EnsurePivotCacheRecordsPart(archive, cachePart);
            changed = true;
        }

        return changed;
    }

    private static void RewritePivotCacheDefinition(
        ZipArchive archive,
        string cachePart,
        XDocument cacheXml,
        IReadOnlyList<string> fieldNames)
    {
        var root = cacheXml.Root!;
        var cacheSource = root.Element(WorkbookNs + "cacheSource") is { } source
            ? new XElement(source)
            : new XElement(WorkbookNs + "cacheSource", new XAttribute("type", "worksheet"));

        var rewritten = new XDocument(
            new XElement(
                WorkbookNs + "pivotCacheDefinition",
                new XAttribute(XNamespace.Xmlns + "r", RelNs.NamespaceName),
                new XAttribute(RelNs + "id", "rId1"),
                new XAttribute("refreshedVersion", "8"),
                new XAttribute("minRefreshableVersion", "3"),
                new XAttribute("recordCount", "0"),
                cacheSource,
                new XElement(
                    WorkbookNs + "cacheFields",
                    new XAttribute("count", fieldNames.Count),
                    fieldNames.Select(name =>
                        new XElement(
                            WorkbookNs + "cacheField",
                            new XAttribute("name", name),
                            new XAttribute("numFmtId", "0"),
                            new XElement(WorkbookNs + "sharedItems"))))));

        XlsxPackageXmlEditor.ReplaceXml(archive, cachePart, rewritten);
    }

    private static IEnumerable<PivotTablePartInfo> FindPivotTablesForCache(ZipArchive archive, string cacheId)
    {
        foreach (var entry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith("xl/pivotTables/pivotTable", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var pivotTableXml = XlsxPackageXmlEditor.LoadXml(entry);
            var root = pivotTableXml.Root;
            if (root is null || !string.Equals(root.Attribute("cacheId")?.Value, cacheId, StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new PivotTablePartInfo(entry.FullName, pivotTableXml);
        }
    }

    private static void RewritePivotTableAsRefreshableSkeleton(
        ZipArchive archive,
        PivotTablePartInfo pivotTable,
        string cacheId,
        int fieldCount)
    {
        var root = pivotTable.Xml.Root!;
        var name = string.IsNullOrWhiteSpace(root.Attribute("name")?.Value)
            ? Path.GetFileNameWithoutExtension(pivotTable.Path)
            : root.Attribute("name")!.Value;
        var locationRef = root.Element(WorkbookNs + "location")?.Attribute("ref")?.Value;
        if (string.IsNullOrWhiteSpace(locationRef))
            locationRef = "A1";

        var styleName = root.Element(WorkbookNs + "pivotTableStyleInfo")?.Attribute("name")?.Value;
        if (string.IsNullOrWhiteSpace(styleName))
            styleName = "PivotStyleLight16";

        var rewritten = new XDocument(
            new XElement(
                WorkbookNs + "pivotTableDefinition",
                new XAttribute("name", name),
                new XAttribute("cacheId", cacheId),
                new XAttribute("dataCaption", "Values"),
                new XAttribute("updatedVersion", "8"),
                new XAttribute("minRefreshableVersion", "3"),
                new XAttribute("createdVersion", "8"),
                new XAttribute("useAutoFormatting", "1"),
                new XElement(
                    WorkbookNs + "location",
                    new XAttribute("ref", locationRef),
                    new XAttribute("firstHeaderRow", "1"),
                    new XAttribute("firstDataRow", "1"),
                    new XAttribute("firstDataCol", "0")),
                new XElement(
                    WorkbookNs + "pivotFields",
                    new XAttribute("count", fieldCount),
                    Enumerable.Range(0, fieldCount).Select(_ =>
                        new XElement(WorkbookNs + "pivotField", new XAttribute("showAll", "0")))),
                new XElement(
                    WorkbookNs + "pivotTableStyleInfo",
                    new XAttribute("name", styleName),
                    new XAttribute("showRowHeaders", "1"),
                    new XAttribute("showColHeaders", "1"),
                    new XAttribute("showRowStripes", "0"),
                    new XAttribute("showColStripes", "0"),
                    new XAttribute("showLastColumn", "1"))));

        XlsxPackageXmlEditor.ReplaceXml(archive, pivotTable.Path, rewritten);
    }

    private static void EnsurePivotCacheRecordsPart(ZipArchive archive, string cachePart)
    {
        var recordsPath = PivotCacheRecordsPath(cachePart);
        XlsxPackageXmlEditor.ReplaceXml(
            archive,
            recordsPath,
            new XDocument(
                new XElement(
                    WorkbookNs + "pivotCacheRecords",
                    new XAttribute("count", "0"))));

        var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(cachePart);
        var relationshipsXml = new XDocument(
            new XElement(
                PackageRelNs + "Relationships",
                new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", PivotCacheRecordsRelationshipType),
                    new XAttribute("Target", Path.GetFileName(recordsPath)))));
        XlsxPackageXmlEditor.ReplaceXml(archive, relationshipsPath, relationshipsXml);

        EnsureContentTypeOverride(
            archive,
            "/" + recordsPath,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheRecords+xml");
    }

    private static string PivotCacheRecordsPath(string cachePart)
    {
        var fileName = Path.GetFileNameWithoutExtension(cachePart);
        var suffix = Regex.Match(fileName, @"\d+$").Value;
        if (string.IsNullOrWhiteSpace(suffix))
            suffix = "1";

        return "xl/pivotCache/pivotCacheRecords" + suffix + ".xml";
    }

    private static string SanitizePivotFieldName(string? name, int index) =>
        string.IsNullOrWhiteSpace(name) ? $"Field{index + 1}" : name.Trim();

    private static int TryGetWorksheetSourceColumnCount(XElement cacheRoot)
    {
        var reference = cacheRoot
            .Element(WorkbookNs + "cacheSource")?
            .Element(WorkbookNs + "worksheetSource")?
            .Attribute("ref")?
            .Value;
        if (string.IsNullOrWhiteSpace(reference))
            return 0;

        var parts = reference.Split(':', 2);
        if (parts.Length != 2)
            return 1;

        return TryGetColumnIndex(parts[0], out var first) &&
               TryGetColumnIndex(parts[1], out var last) &&
               last >= first
            ? last - first + 1
            : 0;
    }

    private static bool TryGetColumnIndex(string cellReference, out int columnIndex)
    {
        columnIndex = 0;
        foreach (var ch in cellReference)
        {
            if (!char.IsAsciiLetter(ch))
                break;

            columnIndex = (columnIndex * 26) + char.ToUpperInvariant(ch) - 'A' + 1;
        }

        return columnIndex > 0;
    }

    private static string? ResolveWorkbookRelationshipTarget(XElement relationshipsRoot, string relationshipId, string relationshipType)
    {
        var relationship = relationshipsRoot
            .Elements(PackageRelNs + "Relationship")
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal) &&
                string.Equals(candidate.Attribute("Type")?.Value, relationshipType, StringComparison.OrdinalIgnoreCase));
        var target = relationship?.Attribute("Target")?.Value;
        return string.IsNullOrWhiteSpace(target)
            ? null
            : XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target);
    }

    private static void RemoveCalcChain(ZipArchive archive)
    {
        archive.GetEntry("xl/calcChain.xml")?.Delete();

        var workbookRelationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var relationshipRoot = workbookRelationships?.Root;
        if (relationshipRoot is not null)
        {
            var calcRelationships = relationshipRoot
                .Elements(PackageRelNs + "Relationship")
                .Where(relationship =>
                    string.Equals(relationship.Attribute("Type")?.Value, CalcChainRelationshipType, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relationship.Attribute("Target")?.Value, "calcChain.xml", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var relationship in calcRelationships)
                relationship.Remove();

            if (calcRelationships.Count > 0)
                XlsxPackageXmlEditor.ReplaceXml(archive, "xl/_rels/workbook.xml.rels", workbookRelationships!);
        }

        RemoveContentTypeOverride(archive, "/xl/calcChain.xml");
    }

    private static List<string> GetWorkbookWorksheetPaths(ZipArchive archive)
    {
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var workbookRelationships = XlsxRelationshipReader.LoadTargets(
            archive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            PackageRelNs);

        return workbookXml?.Root is null
            ? []
            : XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRelationships, WorkbookNs, RelNs)
                .Select(pair => pair.WorksheetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    private static XDocument? LoadXml(ZipArchive archive, string path) =>
        archive.GetEntry(path) is { } entry ? XlsxPackageXmlEditor.LoadXml(entry) : null;

    private static void EnsureContentTypeOverride(ZipArchive archive, string partName, string contentType)
    {
        var contentTypes = LoadXml(archive, "[Content_Types].xml");
        var root = contentTypes?.Root;
        if (root is null)
            return;

        var normalizedPartName = NormalizePartName(partName);
        var existing = root.Elements(ContentTypeNs + "Override")
            .FirstOrDefault(overrideElement =>
                string.Equals(
                    NormalizePartName(overrideElement.Attribute("PartName")?.Value),
                    normalizedPartName,
                    StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.SetAttributeValue("ContentType", contentType);
        }
        else
        {
            root.Add(new XElement(
                ContentTypeNs + "Override",
                new XAttribute("PartName", normalizedPartName),
                new XAttribute("ContentType", contentType)));
        }

        XlsxPackageXmlEditor.ReplaceXml(archive, "[Content_Types].xml", contentTypes!);
    }

    private static void RemoveContentTypeOverride(ZipArchive archive, string partName)
    {
        var contentTypes = LoadXml(archive, "[Content_Types].xml");
        var root = contentTypes?.Root;
        if (root is null)
            return;

        var normalizedPartName = NormalizePartName(partName);
        var removed = false;
        foreach (var overrideElement in root.Elements(ContentTypeNs + "Override").ToList())
        {
            if (!string.Equals(
                    NormalizePartName(overrideElement.Attribute("PartName")?.Value),
                    normalizedPartName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            overrideElement.Remove();
            removed = true;
        }

        if (removed)
            XlsxPackageXmlEditor.ReplaceXml(archive, "[Content_Types].xml", contentTypes!);
    }

    private static void PruneMissingContentTypeOverrides(ZipArchive archive)
    {
        var contentTypes = LoadXml(archive, "[Content_Types].xml");
        var root = contentTypes?.Root;
        if (root is null)
            return;

        var changed = false;
        foreach (var overrideElement in root.Elements(ContentTypeNs + "Override").ToList())
        {
            var partName = overrideElement.Attribute("PartName")?.Value;
            var zipPath = NormalizePartName(partName).TrimStart('/');
            if (!string.IsNullOrWhiteSpace(zipPath) && archive.GetEntry(zipPath) is null)
            {
                overrideElement.Remove();
                changed = true;
            }
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(archive, "[Content_Types].xml", contentTypes!);
    }

    private static string NormalizePartName(string? partName)
    {
        if (string.IsNullOrWhiteSpace(partName))
            return "";

        return "/" + XlsxPackagePath.NormalizeZipPath(partName.Trim().Replace('\\', '/').TrimStart('/'));
    }

    private sealed record PivotTablePartInfo(string Path, XDocument Xml);
}
