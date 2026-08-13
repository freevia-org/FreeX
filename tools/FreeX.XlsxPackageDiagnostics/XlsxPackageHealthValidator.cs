using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeX.Core.IO;

namespace FreeX.XlsxPackageDiagnostics;

public static class XlsxPackageHealthValidator
{
    private const string RelationshipPartContentType =
        "application/vnd.openxmlformats-package.relationships+xml";
    private const string OfficeDocumentRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string WorksheetRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
    private const string ChartsheetRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chartsheet";
    private const string SharedStringsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings";
    private const string StylesRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    private const string ExternalLinkRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink";
    private const string ExternalLinkPathRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath";
    private const string VbaProjectRelationshipType =
        "http://schemas.microsoft.com/office/2006/relationships/vbaProject";
    private const string PivotCacheDefinitionRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition";
    private const string PivotCacheRecordsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheRecords";
    private const string PivotTableRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable";
    private const string DrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
    private const string ChartRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string ImageRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string TableRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table";
    private const string WorksheetContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
    private const string ChartsheetContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml";
    private const string SharedStringsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml";
    private const string StylesContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml";
    private const string ExternalLinkContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml";
    private const string VbaProjectContentType =
        "application/vnd.ms-office.vbaProject";
    private const string PivotCacheDefinitionContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml";
    private const string PivotCacheRecordsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheRecords+xml";
    private const string PivotTableContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml";
    private const string DrawingContentType =
        "application/vnd.openxmlformats-officedocument.drawing+xml";
    private const string ChartContentType =
        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    private const string TableContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml";

    private static readonly XNamespace PackageContentTypeNs = OpcMediaTypes.ContentTypesNamespace;
    private static readonly XNamespace PackageRelationshipNs = OpcRelationships.Namespace;
    private static readonly XNamespace WorkbookNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelationshipNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs =
        "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace DrawingChartNs =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly HashSet<string> WorkbookMainContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
        "application/vnd.ms-excel.sheet.macroEnabled.main+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml",
        "application/vnd.ms-excel.template.macroEnabledTemplate.main+xml",
        "application/vnd.ms-excel.addin.macroEnabled.main+xml"
    };
    private static readonly HashSet<string> MacroEnabledWorkbookMainContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.ms-excel.sheet.macroEnabled.main+xml",
        "application/vnd.ms-excel.template.macroEnabledTemplate.main+xml",
        "application/vnd.ms-excel.addin.macroEnabled.main+xml"
    };

    public static IReadOnlyList<string> Validate(ZipArchive archive)
    {
        var issues = new List<string>();
        AddPackageEntryIssues(archive, issues);
        AddPackageContentTypeIssues(archive, issues);
        AddPackageRelationshipIssues(archive, issues);
        AddPackageRootWorkbookIssues(archive, issues);
        AddWorkbookSheetMapIssues(archive, issues);
        AddSharedStringTableIssues(archive, issues);
        AddStylesPackageIssues(archive, issues);
        AddExternalLinkPackageIssues(archive, issues);
        AddVbaProjectPackageIssues(archive, issues);
        AddPivotCachePackageIssues(archive, issues);
        AddPivotTablePackageIssues(archive, issues);
        AddWorksheetDrawingPackageIssues(archive, issues);
        AddWorksheetBackgroundPicturePackageIssues(archive, issues);
        AddWorksheetTablePackageIssues(archive, issues);
        return issues;
    }

    public static IReadOnlyList<string> Validate(Stream packageStream)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        return Validate(archive);
    }

    private static void AddPackageEntryIssues(ZipArchive archive, List<string> issues)
    {
        var exactNames = new HashSet<string>(StringComparer.Ordinal);
        var packagePartNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            var rawName = entry.FullName;
            var normalizedName = rawName.Replace('\\', '/');

            if (rawName.Contains('\\', StringComparison.Ordinal))
                issues.Add($"{rawName} uses a backslash in the package part name");
            if (normalizedName.StartsWith("/", StringComparison.Ordinal))
                issues.Add($"{rawName} starts with '/'");
            if (normalizedName.Contains("//", StringComparison.Ordinal))
                issues.Add($"{rawName} has an empty path segment");

            var segments = normalizedName.Split('/', StringSplitOptions.None);
            if (segments.Any(segment => segment is "." or ".."))
                issues.Add($"{rawName} has a relative path segment");

            if (!exactNames.Add(normalizedName))
            {
                issues.Add($"{rawName} duplicates package part {normalizedName}");
                continue;
            }

            if (packagePartNames.TryGetValue(normalizedName, out var existingName))
                issues.Add($"{rawName} collides with package part {existingName} when compared case-insensitively");
            else
                packagePartNames.Add(normalizedName, normalizedName);
        }
    }

    private static void AddPackageContentTypeIssues(ZipArchive archive, List<string> issues)
    {
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
        {
            issues.Add("missing [Content_Types].xml");
            return;
        }

        XDocument contentTypesXml;
        try
        {
            contentTypesXml = LoadPackageXml(contentTypesEntry);
        }
        catch (Exception ex) when (ex is InvalidOperationException or XmlException)
        {
            issues.Add($"[Content_Types].xml is not parseable XML: {ex.Message}");
            return;
        }

        if (contentTypesXml.Root?.Name != PackageContentTypeNs + "Types")
        {
            issues.Add("[Content_Types].xml has an invalid root element");
            return;
        }

        var packageParts = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        issues.AddRange(FindPackageContentTypeDeclarationIssues(contentTypesXml, packageParts));

        var missing = packageParts
            .Where(part => !string.Equals(part, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
            .Where(part => string.IsNullOrWhiteSpace(GetEffectivePackageContentType(contentTypesXml, part)))
            .Select(part => $"{part} has no effective package content type");
        issues.AddRange(missing);

        issues.AddRange(FindPackageContentTypeConsistencyIssues(contentTypesXml, packageParts));
    }

    private static IEnumerable<string> FindPackageContentTypeConsistencyIssues(
        XDocument contentTypesXml,
        IReadOnlySet<string> packageParts)
    {
        foreach (var part in packageParts
                     .Where(part => !string.Equals(part, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(part => part, StringComparer.OrdinalIgnoreCase))
        {
            var contentType = GetEffectivePackageContentType(contentTypesXml, part);
            if (string.IsNullOrWhiteSpace(contentType))
                continue;

            var isRelationshipPart = IsPackageRelationshipPart(part);
            var hasRelationshipExtension = part.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);
            var hasRelationshipContentType = string.Equals(
                contentType,
                RelationshipPartContentType,
                StringComparison.OrdinalIgnoreCase);

            if (isRelationshipPart && !hasRelationshipContentType)
                yield return $"{part} must use relationship content type {RelationshipPartContentType}; actual {contentType}";
            else if (!isRelationshipPart && hasRelationshipContentType)
                yield return $"{part} uses relationship content type but is not a valid relationship part";

            if (hasRelationshipExtension && !isRelationshipPart)
                yield return $"{part} has .rels extension outside a valid relationship part location";
        }
    }

    private static IEnumerable<string> FindPackageContentTypeDeclarationIssues(
        XDocument contentTypesXml,
        HashSet<string> packageParts)
    {
        var root = contentTypesXml.Root;
        if (root is null)
            yield break;

        foreach (var element in root.Elements())
        {
            if (element.Name != PackageContentTypeNs + "Default" &&
                element.Name != PackageContentTypeNs + "Override")
            {
                yield return $"unexpected [Content_Types].xml child element '{element.Name}'";
            }
        }

        var defaultExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in root.Elements(PackageContentTypeNs + "Default"))
        {
            var extension = element.Attribute("Extension")?.Value;
            var declarationLabel = string.IsNullOrWhiteSpace(extension)
                ? "Default declaration"
                : $"Default extension '{extension}'";

            if (string.IsNullOrWhiteSpace(extension))
            {
                yield return "Default declaration missing Extension";
            }
            else
            {
                var trimmedExtension = extension.Trim();
                declarationLabel = $"Default extension '{trimmedExtension}'";

                if (!string.Equals(extension, trimmedExtension, StringComparison.Ordinal))
                    yield return $"Default extension '{extension}' has leading or trailing whitespace";

                if (trimmedExtension.IndexOf('/') >= 0 ||
                    trimmedExtension.IndexOf('\\') >= 0 ||
                    trimmedExtension.IndexOf('.') >= 0 ||
                    trimmedExtension.Any(char.IsWhiteSpace))
                {
                    yield return $"Default extension '{trimmedExtension}' is not a bare package extension";
                }

                if (!defaultExtensions.Add(trimmedExtension))
                    yield return $"duplicate Default extension '{trimmedExtension}'";
            }

            foreach (var issue in FindContentTypeAttributeIssues(element, declarationLabel))
                yield return issue;
        }

        var overridePartNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in root.Elements(PackageContentTypeNs + "Override"))
        {
            var partName = element.Attribute("PartName")?.Value;
            var declarationLabel = string.IsNullOrWhiteSpace(partName)
                ? "Override declaration"
                : $"Override PartName '{partName}'";

            if (string.IsNullOrWhiteSpace(partName))
            {
                yield return "Override declaration missing PartName";
            }
            else
            {
                var trimmedPartName = partName.Trim();

                if (!string.Equals(partName, trimmedPartName, StringComparison.Ordinal))
                    yield return $"Override PartName '{partName}' has leading or trailing whitespace";

                if (!trimmedPartName.StartsWith("/", StringComparison.Ordinal))
                    yield return $"Override PartName '{partName}' must start with '/'";

                if (trimmedPartName.IndexOf('\\') >= 0)
                    yield return $"Override PartName '{partName}' must use forward slashes";

                if (trimmedPartName.IndexOf('?') >= 0 || trimmedPartName.IndexOf('#') >= 0)
                    yield return $"Override PartName '{partName}' must not include query or fragment text";

                var pathWithoutRootSlash = trimmedPartName.TrimStart('/');
                if (!TryNormalizePackagePathSegments(pathWithoutRootSlash, out var overridePart))
                {
                    yield return $"Override PartName '{partName}' escapes the package root";
                }
                else if (string.IsNullOrWhiteSpace(overridePart))
                {
                    yield return $"Override PartName '{partName}' does not reference a package part";
                }
                else
                {
                    declarationLabel = $"Override PartName '/{overridePart}'";
                    var rawNormalizedPart = NormalizePackagePart(trimmedPartName);
                    if (!string.Equals(overridePart, rawNormalizedPart, StringComparison.Ordinal))
                        yield return $"Override PartName '{partName}' is not canonical";

                    if (!overridePartNames.Add(overridePart))
                        yield return $"duplicate Override PartName '/{overridePart}'";

                    if (!packageParts.Contains(overridePart))
                        yield return $"Override PartName '/{overridePart}' references missing package part";
                }
            }

            foreach (var issue in FindContentTypeAttributeIssues(element, declarationLabel))
                yield return issue;
        }
    }

    private static IEnumerable<string> FindContentTypeAttributeIssues(XElement element, string declarationLabel)
    {
        var contentType = element.Attribute("ContentType")?.Value;
        if (string.IsNullOrWhiteSpace(contentType))
        {
            yield return $"{declarationLabel} missing ContentType";
            yield break;
        }

        if (!string.Equals(contentType, contentType.Trim(), StringComparison.Ordinal))
            yield return $"{declarationLabel} ContentType has leading or trailing whitespace";

        if (!contentType.Contains("/", StringComparison.Ordinal))
            yield return $"{declarationLabel} ContentType '{contentType}' is not a media type";
    }

    private static string? GetEffectivePackageContentType(XDocument contentTypesXml, string normalizedPartName)
    {
        var normalizedContentTypePartName = $"/{NormalizePackagePart(normalizedPartName)}";
        var overrideContentType = FindOverrideContentType(contentTypesXml.Root, normalizedContentTypePartName);

        if (!string.IsNullOrWhiteSpace(overrideContentType))
            return overrideContentType;

        var extension = GetPackagePartExtension(normalizedPartName);
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        return FindDefaultContentType(contentTypesXml.Root, extension);
    }

    private static string? FindOverrideContentType(XElement? root, string normalizedContentTypePartName)
    {
        if (root is null)
            return null;

        foreach (var element in root.Elements(PackageContentTypeNs + "Override"))
        {
            if (string.Equals(
                NormalizeContentTypePartName(element.Attribute("PartName")?.Value),
                normalizedContentTypePartName,
                StringComparison.OrdinalIgnoreCase))
            {
                return element.Attribute("ContentType")?.Value;
            }
        }

        return null;
    }

    private static string? FindDefaultContentType(XElement? root, string extension)
    {
        if (root is null)
            return null;

        foreach (var element in root.Elements(PackageContentTypeNs + "Default"))
        {
            if (string.Equals(
                element.Attribute("Extension")?.Value?.Trim(),
                extension,
                StringComparison.OrdinalIgnoreCase))
            {
                return element.Attribute("ContentType")?.Value;
            }
        }

        return null;
    }

    private static string NormalizeContentTypePartName(string? partName) =>
        $"/{NormalizePackagePart(partName ?? string.Empty)}";

    private static string GetPackagePartExtension(string partName)
    {
        var fileName = NormalizePackagePart(partName);
        var slash = fileName.LastIndexOf('/');
        if (slash >= 0)
            fileName = fileName[(slash + 1)..];

        var dot = fileName.LastIndexOf('.');
        return dot >= 0 && dot < fileName.Length - 1 ? fileName[(dot + 1)..] : string.Empty;
    }

    private static void AddPackageRelationshipIssues(ZipArchive archive, List<string> issues)
    {
        var entryNames = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries.Where(entry => IsPackageRelationshipPart(entry.FullName)))
        {
            var relationshipPart = NormalizePackagePart(entry.FullName);
            if (!string.Equals(relationshipPart, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            {
                var ownerPart = GetRelationshipOwnerPart(relationshipPart);
                if (string.IsNullOrWhiteSpace(ownerPart) || !entryNames.Contains(ownerPart))
                    issues.Add($"{relationshipPart} has no owning package part {ownerPart}");
            }

            XDocument relationshipsXml;
            try
            {
                relationshipsXml = LoadPackageXml(entry);
            }
            catch (Exception ex) when (ex is InvalidOperationException or XmlException)
            {
                issues.Add($"{relationshipPart} is not parseable relationship XML: {ex.Message}");
                continue;
            }

            if (relationshipsXml.Root?.Name != PackageRelationshipNs + "Relationships")
            {
                issues.Add($"{relationshipPart} has an invalid Relationships root element");
                continue;
            }

            foreach (var element in relationshipsXml.Root.Elements())
            {
                if (element.Name != PackageRelationshipNs + "Relationship")
                    issues.Add($"{relationshipPart} has unexpected child element '{element.Name}'");
            }

            var relationships = relationshipsXml.Root
                .Elements(PackageRelationshipNs + "Relationship")
                .ToArray();
            if (relationships.Length == 0)
            {
                issues.Add($"{relationshipPart} has no Relationship elements");
                continue;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var relationship in relationships)
                ValidatePackageRelationship(relationshipPart, relationship, entryNames, ids, issues);
        }
    }

    private static void AddPackageRootWorkbookIssues(ZipArchive archive, List<string> issues)
    {
        var entryNames = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rootRelationshipsEntry = archive.GetEntry("_rels/.rels");
        if (rootRelationshipsEntry is null)
        {
            issues.Add("missing package root relationships part _rels/.rels");
            return;
        }

        XDocument relationshipsXml;
        try
        {
            relationshipsXml = LoadPackageXml(rootRelationshipsEntry);
        }
        catch (Exception ex) when (ex is InvalidOperationException or XmlException)
        {
            issues.Add($"_rels/.rels is not parseable relationship XML: {ex.Message}");
            return;
        }

        if (relationshipsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            issues.Add("_rels/.rels has an invalid Relationships root element");
            return;
        }

        var officeDocumentRelationships = relationshipsXml.Root
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(relationship => string.Equals(
                relationship.Attribute("Type")?.Value,
                OfficeDocumentRelationshipType,
                StringComparison.Ordinal))
            .ToArray();

        if (officeDocumentRelationships.Length == 0)
        {
            issues.Add($"_rels/.rels has no {OfficeDocumentRelationshipType} relationship");
            return;
        }

        if (officeDocumentRelationships.Length > 1)
            issues.Add($"_rels/.rels has multiple {OfficeDocumentRelationshipType} relationships");

        XDocument? contentTypesXml = null;
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            try
            {
                contentTypesXml = LoadPackageXml(contentTypesEntry);
            }
            catch (Exception ex) when (ex is InvalidOperationException or XmlException)
            {
                issues.Add($"[Content_Types].xml is not parseable XML: {ex.Message}");
            }
        }

        foreach (var relationship in officeDocumentRelationships)
            ValidateRootOfficeDocumentRelationship(relationship, entryNames, contentTypesXml, issues);
    }

    private static void ValidateRootOfficeDocumentRelationship(
        XElement relationship,
        IReadOnlySet<string> entryNames,
        XDocument? contentTypesXml,
        List<string> issues)
    {
        var id = relationship.Attribute("Id")?.Value;
        var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"_rels/.rels Relationship {FormatRelationshipIssueId(id)} must target the workbook package part internally");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return;

        if (!TryResolvePackageRelationshipTarget("_rels/.rels", target.Trim(), out var resolvedTarget, out _))
            return;

        if (!entryNames.Contains(resolvedTarget))
            return;

        if (contentTypesXml?.Root?.Name != PackageContentTypeNs + "Types")
            return;

        var contentType = GetEffectivePackageContentType(contentTypesXml, resolvedTarget);
        if (!WorkbookMainContentTypes.Contains(contentType ?? string.Empty))
        {
            issues.Add(
                $"_rels/.rels Relationship {FormatRelationshipIssueId(id)} targets {resolvedTarget} with non-workbook content type {contentType ?? "(none)"}");
        }
    }

    private static void AddWorkbookSheetMapIssues(ZipArchive archive, List<string> issues)
    {
        if (!TryLoadPackageXml(archive, "[Content_Types].xml", issues, out var contentTypesXml) ||
            contentTypesXml.Root?.Name != PackageContentTypeNs + "Types")
        {
            return;
        }

        var entryNames = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!TryFindWorkbookPart(archive, entryNames, out var workbookPart))
            return;

        if (!TryLoadPackageXml(archive, workbookPart, issues, out var workbookXml) ||
            workbookXml.Root?.Name != WorkbookNs + "workbook")
        {
            return;
        }

        var workbookRelsPart = GetRelationshipPartPath(workbookPart);
        if (!TryLoadPackageXml(archive, workbookRelsPart, issues, out var workbookRelsXml) ||
            workbookRelsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            return;
        }

        var workbookRelationships = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var relationship in workbookRelsXml.Root.Elements(PackageRelationshipNs + "Relationship"))
        {
            var id = relationship.Attribute("Id")?.Value;
            if (!string.IsNullOrWhiteSpace(id) && !workbookRelationships.ContainsKey(id))
                workbookRelationships.Add(id, relationship);
        }

        foreach (var sheet in workbookXml.Root
                     .Element(WorkbookNs + "sheets")
                     ?.Elements(WorkbookNs + "sheet") ?? [])
        {
            ValidateWorkbookSheetMap(workbookPart, workbookRelsPart, sheet, workbookRelationships, contentTypesXml, entryNames, issues);
        }
    }

    private static void AddSharedStringTableIssues(ZipArchive archive, List<string> issues)
    {
        if (!TryLoadPackageXml(archive, "[Content_Types].xml", issues, out var contentTypesXml) ||
            contentTypesXml.Root?.Name != PackageContentTypeNs + "Types")
        {
            return;
        }

        var entryNames = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sharedStringCells = FindSharedStringCells(archive, issues);
        var hasSharedStringsPart = entryNames.Contains("xl/sharedStrings.xml");
        if (sharedStringCells.Count == 0 && !hasSharedStringsPart)
            return;

        if (!hasSharedStringsPart)
        {
            issues.Add("missing xl/sharedStrings.xml for shared-string cells");
            return;
        }

        var sharedStringsContentType = GetEffectivePackageContentType(contentTypesXml, "xl/sharedStrings.xml");
        if (!string.Equals(sharedStringsContentType, SharedStringsContentType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(
                $"xl/sharedStrings.xml has content type {sharedStringsContentType ?? "(none)"}; expected {SharedStringsContentType}");
        }

        ValidateSharedStringWorkbookRelationship(archive, entryNames, issues);

        if (!TryLoadPackageXml(archive, "xl/sharedStrings.xml", issues, out var sharedStringsXml))
            return;

        if (sharedStringsXml.Root?.Name != WorkbookNs + "sst")
        {
            issues.Add("xl/sharedStrings.xml has an invalid shared-string table root element");
            return;
        }

        var sharedStringCount = sharedStringsXml.Root.Elements(WorkbookNs + "si").Count();
        foreach (var sharedStringCell in sharedStringCells)
        {
            if (string.IsNullOrWhiteSpace(sharedStringCell.ValueText))
            {
                issues.Add($"{sharedStringCell.WorksheetPart} cell {sharedStringCell.CellReference} has no shared-string index");
                continue;
            }

            if (!int.TryParse(sharedStringCell.ValueText, NumberStyles.None, CultureInfo.InvariantCulture, out var sharedStringIndex))
            {
                issues.Add($"{sharedStringCell.WorksheetPart} cell {sharedStringCell.CellReference} has invalid shared-string index '{sharedStringCell.ValueText}'");
                continue;
            }

            if (sharedStringIndex < 0 || sharedStringIndex >= sharedStringCount)
            {
                issues.Add(
                    $"{sharedStringCell.WorksheetPart} cell {sharedStringCell.CellReference} references shared-string index {sharedStringIndex}, but xl/sharedStrings.xml contains {sharedStringCount} entries");
            }
        }
    }

    private static void ValidateSharedStringWorkbookRelationship(
        ZipArchive archive,
        IReadOnlySet<string> entryNames,
        List<string> issues)
    {
        if (!TryFindWorkbookPart(archive, entryNames, out var workbookPart))
            return;

        var workbookRelsPart = GetRelationshipPartPath(workbookPart);
        if (!TryLoadPackageXml(archive, workbookRelsPart, issues, out var workbookRelsXml) ||
            workbookRelsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            return;
        }

        var hasRelationship = workbookRelsXml.Root
            .Elements(PackageRelationshipNs + "Relationship")
            .Any(relationship =>
                string.Equals(relationship.Attribute("Type")?.Value, SharedStringsRelationshipType, StringComparison.Ordinal) &&
                !string.Equals(relationship.Attribute("TargetMode")?.Value?.Trim(), "External", StringComparison.OrdinalIgnoreCase) &&
                TryResolvePackageRelationshipTarget(workbookRelsPart, relationship.Attribute("Target")?.Value?.Trim() ?? string.Empty, out var resolvedTarget, out _) &&
                string.Equals(resolvedTarget, "xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase));

        if (!hasRelationship)
            issues.Add($"{workbookRelsPart} has no workbook relationship to xl/sharedStrings.xml");
    }

    private static List<SharedStringCellReference> FindSharedStringCells(ZipArchive archive, List<string> issues)
    {
        var cells = new List<SharedStringCellReference>();
        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            XDocument worksheetXml;
            try
            {
                worksheetXml = LoadPackageXml(worksheetEntry);
            }
            catch (Exception ex) when (ex is InvalidOperationException or XmlException)
            {
                issues.Add($"{worksheetPart} is not parseable XML: {ex.Message}");
                continue;
            }

            foreach (var cell in worksheetXml.Descendants(WorkbookNs + "c"))
            {
                if (!string.Equals(cell.Attribute("t")?.Value, "s", StringComparison.Ordinal))
                    continue;

                cells.Add(new SharedStringCellReference(
                    worksheetPart,
                    cell.Attribute("r")?.Value ?? "(unknown ref)",
                    cell.Element(WorkbookNs + "v")?.Value));
            }
        }

        return cells;
    }

    private static void AddStylesPackageIssues(ZipArchive archive, List<string> issues)
    {
        if (!TryLoadPackageXml(archive, "[Content_Types].xml", issues, out var contentTypesXml) ||
            contentTypesXml.Root?.Name != PackageContentTypeNs + "Types")
        {
            return;
        }

        var entryNames = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var styleReferences = FindStyleReferences(archive, issues);
        var hasStylesPart = entryNames.Contains("xl/styles.xml");
        if (styleReferences.Count == 0 && !hasStylesPart)
            return;

        if (!hasStylesPart)
        {
            issues.Add("missing xl/styles.xml for style references");
            return;
        }

        var stylesContentType = GetEffectivePackageContentType(contentTypesXml, "xl/styles.xml");
        if (!string.Equals(stylesContentType, StylesContentType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(
                $"xl/styles.xml has content type {stylesContentType ?? "(none)"}; expected {StylesContentType}");
        }

        ValidateWorkbookRelationshipToPart(archive, entryNames, StylesRelationshipType, "xl/styles.xml", issues);

        if (!TryLoadPackageXml(archive, "xl/styles.xml", issues, out var stylesXml))
            return;

        if (stylesXml.Root?.Name != WorkbookNs + "styleSheet")
        {
            issues.Add("xl/styles.xml has an invalid stylesheet root element");
            return;
        }

        var cellXfs = stylesXml.Root.Element(WorkbookNs + "cellXfs");
        var cellFormatCount = cellXfs?.Elements(WorkbookNs + "xf").Count() ?? 0;
        if (cellFormatCount == 0)
            issues.Add("xl/styles.xml has no cellXfs xf entries");

        AddStyleCountAttributeIssues(issues, "cellXfs", cellXfs, cellFormatCount);
        foreach (var styleReference in styleReferences)
        {
            if (string.IsNullOrWhiteSpace(styleReference.ValueText))
            {
                issues.Add($"{styleReference.WorksheetPart} {styleReference.Description} has no style index");
                continue;
            }

            if (!int.TryParse(styleReference.ValueText, NumberStyles.None, CultureInfo.InvariantCulture, out var styleIndex))
            {
                issues.Add($"{styleReference.WorksheetPart} {styleReference.Description} has invalid style index '{styleReference.ValueText}'");
                continue;
            }

            if (styleIndex < 0 || styleIndex >= cellFormatCount)
            {
                issues.Add(
                    $"{styleReference.WorksheetPart} {styleReference.Description} references style index {styleIndex}, but xl/styles.xml cellXfs contains {cellFormatCount} entries");
            }
        }
    }

    private static void ValidateWorkbookRelationshipToPart(
        ZipArchive archive,
        IReadOnlySet<string> entryNames,
        string relationshipType,
        string targetPart,
        List<string> issues)
    {
        if (!TryFindWorkbookPart(archive, entryNames, out var workbookPart))
            return;

        var workbookRelsPart = GetRelationshipPartPath(workbookPart);
        if (!TryLoadPackageXml(archive, workbookRelsPart, issues, out var workbookRelsXml) ||
            workbookRelsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            return;
        }

        var hasRelationship = workbookRelsXml.Root
            .Elements(PackageRelationshipNs + "Relationship")
            .Any(relationship =>
                string.Equals(relationship.Attribute("Type")?.Value, relationshipType, StringComparison.Ordinal) &&
                !string.Equals(relationship.Attribute("TargetMode")?.Value?.Trim(), "External", StringComparison.OrdinalIgnoreCase) &&
                TryResolvePackageRelationshipTarget(workbookRelsPart, relationship.Attribute("Target")?.Value?.Trim() ?? string.Empty, out var resolvedTarget, out _) &&
                string.Equals(resolvedTarget, targetPart, StringComparison.OrdinalIgnoreCase));

        if (!hasRelationship)
            issues.Add($"{workbookRelsPart} has no workbook relationship to {targetPart}");
    }

    private static List<StyleReference> FindStyleReferences(ZipArchive archive, List<string> issues)
    {
        var styleReferences = new List<StyleReference>();
        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            XDocument worksheetXml;
            try
            {
                worksheetXml = LoadPackageXml(worksheetEntry);
            }
            catch (Exception ex) when (ex is InvalidOperationException or XmlException)
            {
                issues.Add($"{worksheetPart} is not parseable XML: {ex.Message}");
                continue;
            }

            foreach (var cell in worksheetXml.Descendants(WorkbookNs + "c"))
            {
                var styleIndex = cell.Attribute("s")?.Value;
                if (styleIndex is null)
                    continue;

                styleReferences.Add(new StyleReference(
                    worksheetPart,
                    $"cell {cell.Attribute("r")?.Value ?? "(unknown ref)"}",
                    styleIndex));
            }

            foreach (var row in worksheetXml.Descendants(WorkbookNs + "row"))
            {
                var styleIndex = row.Attribute("s")?.Value;
                if (styleIndex is null)
                    continue;

                styleReferences.Add(new StyleReference(
                    worksheetPart,
                    $"row {row.Attribute("r")?.Value ?? "(unknown row)"}",
                    styleIndex));
            }

            foreach (var column in worksheetXml.Descendants(WorkbookNs + "col"))
            {
                var styleIndex = column.Attribute("style")?.Value;
                if (styleIndex is null)
                    continue;

                var min = column.Attribute("min")?.Value ?? "?";
                var max = column.Attribute("max")?.Value ?? "?";
                styleReferences.Add(new StyleReference(
                    worksheetPart,
                    $"column span {min}:{max}",
                    styleIndex));
            }
        }

        return styleReferences;
    }

    private static void AddStyleCountAttributeIssues(
        List<string> issues,
        string elementName,
        XElement? element,
        int actualCount)
    {
        var countText = element?.Attribute("count")?.Value;
        if (string.IsNullOrWhiteSpace(countText))
            return;

        if (!int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var declaredCount))
        {
            issues.Add($"xl/styles.xml {elementName} has invalid count '{countText}'");
            return;
        }

        if (declaredCount != actualCount)
            issues.Add($"xl/styles.xml {elementName} count is {declaredCount}, but contains {actualCount} child entries");
    }

    private static void AddExternalLinkPackageIssues(ZipArchive archive, List<string> issues)
    {
        if (!TryLoadPackageXml(archive, "[Content_Types].xml", issues, out var contentTypesXml) ||
            contentTypesXml.Root?.Name != PackageContentTypeNs + "Types")
        {
            return;
        }

        var entryNames = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!TryFindWorkbookPart(archive, entryNames, out var workbookPart))
            return;

        if (!TryLoadPackageXml(archive, workbookPart, issues, out var workbookXml) ||
            workbookXml.Root?.Name != WorkbookNs + "workbook")
        {
            return;
        }

        var externalReferences = workbookXml.Root
            .Elements(WorkbookNs + "externalReferences")
            .SelectMany(externalReferences => externalReferences.Elements(WorkbookNs + "externalReference"))
            .Select((externalReference, index) => new WorkbookExternalReference(
                index + 1,
                externalReference.Attribute(OfficeRelationshipNs + "id")?.Value))
            .ToArray();
        if (externalReferences.Length == 0)
            return;

        var workbookRelsPart = GetRelationshipPartPath(workbookPart);
        if (!TryLoadPackageXml(archive, workbookRelsPart, issues, out var workbookRelsXml) ||
            workbookRelsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            return;
        }

        var workbookRelationships = workbookRelsXml.Root
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Attribute("Id")?.Value))
            .GroupBy(relationship => relationship.Attribute("Id")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var validatedExternalLinkParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var externalReference in externalReferences)
        {
            if (string.IsNullOrWhiteSpace(externalReference.RelationshipId))
            {
                issues.Add($"workbook externalReference #{externalReference.Ordinal} has no relationship id");
                continue;
            }

            if (!workbookRelationships.TryGetValue(externalReference.RelationshipId, out var relationship))
            {
                issues.Add($"workbook externalReference #{externalReference.Ordinal} targets missing relationship {externalReference.RelationshipId} in {workbookRelsPart}");
                continue;
            }

            AddWorkbookExternalReferencePackageIssues(
                archive,
                contentTypesXml,
                entryNames,
                workbookRelsPart,
                externalReference,
                relationship,
                validatedExternalLinkParts,
                issues);
        }
    }

    private static void AddWorkbookExternalReferencePackageIssues(
        ZipArchive archive,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        string workbookRelsPart,
        WorkbookExternalReference externalReference,
        XElement relationship,
        HashSet<string> validatedExternalLinkParts,
        List<string> issues)
    {
        var relationshipType = relationship.Attribute("Type")?.Value;
        if (!string.Equals(relationshipType, ExternalLinkRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"workbook externalReference #{externalReference.Ordinal} relationship {externalReference.RelationshipId} has Type={relationshipType ?? "(none)"}; expected {ExternalLinkRelationshipType}");
            return;
        }

        var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"workbook externalReference #{externalReference.Ordinal} relationship {externalReference.RelationshipId} must target an externalLink package part internally");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"workbook externalReference #{externalReference.Ordinal} relationship {externalReference.RelationshipId} has no Target");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(workbookRelsPart, target.Trim(), out var externalLinkPart, out var targetIssue))
        {
            issues.Add($"workbook externalReference #{externalReference.Ordinal} relationship {externalReference.RelationshipId} has invalid Target {target}: {targetIssue}");
            return;
        }

        if (!entryNames.Contains(externalLinkPart))
        {
            issues.Add($"workbook externalReference #{externalReference.Ordinal} relationship {externalReference.RelationshipId} targets missing package part {externalLinkPart}");
            return;
        }

        var contentType = GetEffectivePackageContentType(contentTypesXml, externalLinkPart);
        if (!string.Equals(contentType, ExternalLinkContentType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{externalLinkPart} has content type {contentType ?? "(none)"}; expected {ExternalLinkContentType}");
        }

        if (!validatedExternalLinkParts.Add(externalLinkPart))
            return;

        if (!TryLoadPackageXml(archive, externalLinkPart, issues, out var externalLinkXml))
            return;

        if (externalLinkXml.Root?.Name != WorkbookNs + "externalLink")
        {
            issues.Add($"{externalLinkPart} has an invalid external link root element");
            return;
        }

        AddExternalBookPackageIssues(archive, externalLinkPart, externalLinkXml, issues);
    }

    private static void AddExternalBookPackageIssues(
        ZipArchive archive,
        string externalLinkPart,
        XDocument externalLinkXml,
        List<string> issues)
    {
        var externalBooks = externalLinkXml.Root!
            .Elements(WorkbookNs + "externalBook")
            .Select((externalBook, index) => new ExternalBookReference(
                index + 1,
                externalBook.Attribute(OfficeRelationshipNs + "id")?.Value))
            .ToArray();
        if (externalBooks.Length == 0)
            return;

        var relationshipPart = GetRelationshipPartPath(externalLinkPart);
        if (!TryLoadPackageXml(archive, relationshipPart, issues, out var relationshipsXml) ||
            relationshipsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            issues.Add($"{externalLinkPart} has no relationship part for externalBook references");
            return;
        }

        var relationships = relationshipsXml.Root
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Attribute("Id")?.Value))
            .GroupBy(relationship => relationship.Attribute("Id")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var externalBook in externalBooks)
        {
            if (string.IsNullOrWhiteSpace(externalBook.RelationshipId))
            {
                issues.Add($"{externalLinkPart} externalBook #{externalBook.Ordinal} has no relationship id");
                continue;
            }

            if (!relationships.TryGetValue(externalBook.RelationshipId, out var relationship))
            {
                issues.Add($"{externalLinkPart} externalBook #{externalBook.Ordinal} targets missing relationship {externalBook.RelationshipId} in {relationshipPart}");
                continue;
            }

            var relationshipType = relationship.Attribute("Type")?.Value;
            if (!string.Equals(relationshipType, ExternalLinkPathRelationshipType, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"{externalLinkPart} externalBook #{externalBook.Ordinal} relationship {externalBook.RelationshipId} has Type={relationshipType ?? "(none)"}; expected {ExternalLinkPathRelationshipType}");
            }

            if (string.IsNullOrWhiteSpace(relationship.Attribute("Target")?.Value))
                issues.Add($"{externalLinkPart} externalBook #{externalBook.Ordinal} relationship {externalBook.RelationshipId} has no Target");

            if (!string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                issues.Add($"{externalLinkPart} externalBook #{externalBook.Ordinal} relationship {externalBook.RelationshipId} is not external");
        }
    }

    private static void AddVbaProjectPackageIssues(ZipArchive archive, List<string> issues)
    {
        if (!TryLoadPackageXml(archive, "[Content_Types].xml", issues, out var contentTypesXml) ||
            contentTypesXml.Root?.Name != PackageContentTypeNs + "Types")
        {
            return;
        }

        var entryNames = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!entryNames.Contains("xl/vbaProject.bin"))
            return;

        var vbaContentType = GetEffectivePackageContentType(contentTypesXml, "xl/vbaProject.bin");
        if (!string.Equals(vbaContentType, VbaProjectContentType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"xl/vbaProject.bin has content type {vbaContentType ?? "(none)"}; expected {VbaProjectContentType}");
        }

        if (!TryFindWorkbookPart(archive, entryNames, out var workbookPart))
            return;

        var workbookContentType = GetEffectivePackageContentType(contentTypesXml, workbookPart);
        if (!MacroEnabledWorkbookMainContentTypes.Contains(workbookContentType ?? string.Empty))
        {
            issues.Add($"{workbookPart} has content type {workbookContentType ?? "(none)"} but contains xl/vbaProject.bin; expected a macro-enabled workbook content type");
        }

        ValidateWorkbookRelationshipToPart(archive, entryNames, VbaProjectRelationshipType, "xl/vbaProject.bin", issues);
    }

    private static void AddPivotCachePackageIssues(ZipArchive archive, List<string> issues)
    {
        if (!TryLoadPackageXml(archive, "[Content_Types].xml", issues, out var contentTypesXml) ||
            contentTypesXml.Root?.Name != PackageContentTypeNs + "Types")
        {
            return;
        }

        var entryNames = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!TryFindWorkbookPart(archive, entryNames, out var workbookPart))
            return;

        if (!TryLoadPackageXml(archive, workbookPart, issues, out var workbookXml) ||
            workbookXml.Root?.Name != WorkbookNs + "workbook")
        {
            return;
        }

        var pivotCaches = workbookXml.Root
            .Elements(WorkbookNs + "pivotCaches")
            .SelectMany(pivotCaches => pivotCaches.Elements(WorkbookNs + "pivotCache"))
            .Select((pivotCache, index) => new WorkbookPivotCacheReference(
                index + 1,
                pivotCache.Attribute("cacheId")?.Value,
                pivotCache.Attribute(OfficeRelationshipNs + "id")?.Value))
            .ToArray();
        if (pivotCaches.Length == 0)
            return;

        var workbookRelsPart = GetRelationshipPartPath(workbookPart);
        if (!TryLoadPackageXml(archive, workbookRelsPart, issues, out var workbookRelsXml) ||
            workbookRelsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            return;
        }

        var workbookRelationships = workbookRelsXml.Root
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Attribute("Id")?.Value))
            .GroupBy(relationship => relationship.Attribute("Id")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var seenCacheIds = new HashSet<int>();

        foreach (var pivotCache in pivotCaches)
        {
            if (!int.TryParse(pivotCache.CacheId, NumberStyles.None, CultureInfo.InvariantCulture, out var cacheId) ||
                cacheId < 0)
            {
                issues.Add($"workbook pivotCache #{pivotCache.Ordinal} has invalid cacheId '{pivotCache.CacheId}'");
                continue;
            }

            if (!seenCacheIds.Add(cacheId))
            {
                issues.Add($"workbook pivotCache #{pivotCache.Ordinal} duplicates cacheId {cacheId}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(pivotCache.RelationshipId))
            {
                issues.Add($"workbook pivotCache #{pivotCache.Ordinal} has no relationship id");
                continue;
            }

            if (!workbookRelationships.TryGetValue(pivotCache.RelationshipId, out var relationship))
            {
                issues.Add($"workbook pivotCache #{pivotCache.Ordinal} references missing workbook relationship {pivotCache.RelationshipId}");
                continue;
            }

            ValidateWorkbookPivotCacheDefinition(
                archive,
                contentTypesXml,
                entryNames,
                workbookRelsPart,
                pivotCache,
                relationship,
                issues);
        }
    }

    private static void ValidateWorkbookPivotCacheDefinition(
        ZipArchive archive,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        string workbookRelsPart,
        WorkbookPivotCacheReference pivotCache,
        XElement relationship,
        List<string> issues)
    {
        var relationshipType = relationship.Attribute("Type")?.Value;
        if (!string.Equals(relationshipType, PivotCacheDefinitionRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"workbook pivotCache #{pivotCache.Ordinal} relationship {pivotCache.RelationshipId} has Type={relationshipType ?? "(none)"}; expected {PivotCacheDefinitionRelationshipType}");
            return;
        }

        if (string.Equals(relationship.Attribute("TargetMode")?.Value?.Trim(), "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"workbook pivotCache #{pivotCache.Ordinal} relationship {pivotCache.RelationshipId} must target a pivot cache definition package part internally");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"workbook pivotCache #{pivotCache.Ordinal} relationship {pivotCache.RelationshipId} has no Target");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(workbookRelsPart, target.Trim(), out var cacheDefinitionPart, out var targetIssue))
        {
            issues.Add($"workbook pivotCache #{pivotCache.Ordinal} relationship {pivotCache.RelationshipId} has invalid Target {target}: {targetIssue}");
            return;
        }

        if (!entryNames.Contains(cacheDefinitionPart))
        {
            issues.Add($"workbook pivotCache #{pivotCache.Ordinal} relationship {pivotCache.RelationshipId} targets missing package part {cacheDefinitionPart}");
            return;
        }

        var contentType = GetEffectivePackageContentType(contentTypesXml, cacheDefinitionPart);
        if (!string.Equals(contentType, PivotCacheDefinitionContentType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{cacheDefinitionPart} has content type {contentType ?? "(none)"}; expected {PivotCacheDefinitionContentType}");
        }

        if (!TryLoadPackageXml(archive, cacheDefinitionPart, issues, out var cacheDefinitionXml))
            return;

        if (cacheDefinitionXml.Root?.Name != WorkbookNs + "pivotCacheDefinition")
        {
            issues.Add($"{cacheDefinitionPart} has an invalid pivot cache definition root element");
            return;
        }

        ValidatePivotCacheRecords(archive, contentTypesXml, entryNames, cacheDefinitionPart, cacheDefinitionXml, issues);
    }

    private static void ValidatePivotCacheRecords(
        ZipArchive archive,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        string cacheDefinitionPart,
        XDocument cacheDefinitionXml,
        List<string> issues)
    {
        var recordsRelationshipId = cacheDefinitionXml.Root?.Attribute(OfficeRelationshipNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(recordsRelationshipId))
            return;

        var cacheDefinitionRelsPart = GetRelationshipPartPath(cacheDefinitionPart);
        if (!TryLoadPackageXml(archive, cacheDefinitionRelsPart, issues, out var relationshipsXml) ||
            relationshipsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            issues.Add($"{cacheDefinitionPart} has no relationship part for pivot cache records reference {recordsRelationshipId}");
            return;
        }

        var relationship = relationshipsXml.Root
            .Elements(PackageRelationshipNs + "Relationship")
            .FirstOrDefault(relationship => string.Equals(relationship.Attribute("Id")?.Value, recordsRelationshipId, StringComparison.Ordinal));
        if (relationship is null)
        {
            issues.Add($"{cacheDefinitionPart} pivot cache records reference {recordsRelationshipId} targets missing relationship in {cacheDefinitionRelsPart}");
            return;
        }

        var relationshipType = relationship.Attribute("Type")?.Value;
        if (!string.Equals(relationshipType, PivotCacheRecordsRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{cacheDefinitionPart} pivot cache records relationship {recordsRelationshipId} has Type={relationshipType ?? "(none)"}; expected {PivotCacheRecordsRelationshipType}");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{cacheDefinitionPart} pivot cache records relationship {recordsRelationshipId} has no Target");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(cacheDefinitionRelsPart, target.Trim(), out var recordsPart, out var targetIssue))
        {
            issues.Add($"{cacheDefinitionPart} pivot cache records relationship {recordsRelationshipId} has invalid Target {target}: {targetIssue}");
            return;
        }

        if (!entryNames.Contains(recordsPart))
        {
            issues.Add($"{cacheDefinitionPart} pivot cache records relationship {recordsRelationshipId} targets missing package part {recordsPart}");
            return;
        }

        var contentType = GetEffectivePackageContentType(contentTypesXml, recordsPart);
        if (!string.Equals(contentType, PivotCacheRecordsContentType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{recordsPart} has content type {contentType ?? "(none)"}; expected {PivotCacheRecordsContentType}");
        }

        if (!TryLoadPackageXml(archive, recordsPart, issues, out var recordsXml))
            return;

        if (recordsXml.Root?.Name != WorkbookNs + "pivotCacheRecords")
            issues.Add($"{recordsPart} has an invalid pivot cache records root element");
    }

    private static void AddPivotTablePackageIssues(ZipArchive archive, List<string> issues)
    {
        if (!TryLoadPackageXml(archive, "[Content_Types].xml", issues, out var contentTypesXml) ||
            contentTypesXml.Root?.Name != PackageContentTypeNs + "Types")
        {
            return;
        }

        var entryNames = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var workbookPivotCacheDefinitions = FindWorkbookPivotCacheDefinitionParts(archive, entryNames);
        var workbookPivotCacheIds = workbookPivotCacheDefinitions.Keys.ToHashSet();
        var worksheetParts = entryNames
            .Where(part => part.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase))
            .Where(part => part.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Where(part => !IsPackageRelationshipPart(part))
            .OrderBy(part => part, StringComparer.OrdinalIgnoreCase);

        foreach (var worksheetPart in worksheetParts)
            AddWorksheetPivotTablePackageIssues(archive, contentTypesXml, entryNames, workbookPivotCacheIds, workbookPivotCacheDefinitions, worksheetPart, issues);
    }

    private static Dictionary<int, string> FindWorkbookPivotCacheDefinitionParts(ZipArchive archive, IReadOnlySet<string> entryNames)
    {
        var cacheDefinitions = new Dictionary<int, string>();
        if (!TryFindWorkbookPart(archive, entryNames, out var workbookPart) ||
            !TryLoadPackageXml(archive, workbookPart, [], out var workbookXml) ||
            workbookXml.Root?.Name != WorkbookNs + "workbook")
        {
            return cacheDefinitions;
        }

        var workbookRelsPart = GetRelationshipPartPath(workbookPart);
        if (!TryLoadPackageXml(archive, workbookRelsPart, [], out var workbookRelsXml) ||
            workbookRelsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            return cacheDefinitions;
        }

        var workbookRelationships = workbookRelsXml.Root
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Attribute("Id")?.Value))
            .GroupBy(relationship => relationship.Attribute("Id")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var pivotCache in workbookXml.Root
                     .Elements(WorkbookNs + "pivotCaches")
                     .SelectMany(pivotCaches => pivotCaches.Elements(WorkbookNs + "pivotCache")))
        {
            var cacheIdText = pivotCache.Attribute("cacheId")?.Value;
            var relationshipId = pivotCache.Attribute(OfficeRelationshipNs + "id")?.Value;
            if (!int.TryParse(cacheIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var cacheId) ||
                cacheId < 0 ||
                string.IsNullOrWhiteSpace(relationshipId) ||
                !workbookRelationships.TryGetValue(relationshipId, out var relationship) ||
                !string.Equals(relationship.Attribute("Type")?.Value, PivotCacheDefinitionRelationshipType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target) ||
                !TryResolvePackageRelationshipTarget(workbookRelsPart, target.Trim(), out var cacheDefinitionPart, out _) ||
                !entryNames.Contains(cacheDefinitionPart))
            {
                continue;
            }

            cacheDefinitions.TryAdd(cacheId, cacheDefinitionPart);
        }

        return cacheDefinitions;
    }

    private static void AddWorksheetPivotTablePackageIssues(
        ZipArchive archive,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        IReadOnlySet<int> workbookPivotCacheIds,
        IReadOnlyDictionary<int, string> workbookPivotCacheDefinitions,
        string worksheetPart,
        List<string> issues)
    {
        if (!TryLoadPackageXml(archive, worksheetPart, issues, out var worksheetXml) ||
            worksheetXml.Root?.Name != WorkbookNs + "worksheet")
        {
            return;
        }

        var pivotTableReferences = worksheetXml.Root
            .Elements(WorkbookNs + "pivotTableDefinition")
            .Select((pivotTable, index) => new WorksheetPivotTableReference(
                worksheetPart,
                index + 1,
                pivotTable.Attribute(OfficeRelationshipNs + "id")?.Value))
            .ToArray();
        if (pivotTableReferences.Length == 0)
            return;

        var worksheetRelsPart = GetRelationshipPartPath(worksheetPart);
        if (!TryLoadPackageXml(archive, worksheetRelsPart, issues, out var worksheetRelsXml) ||
            worksheetRelsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            foreach (var reference in pivotTableReferences.Where(reference => !string.IsNullOrWhiteSpace(reference.RelationshipId)))
                issues.Add($"{worksheetPart} has no relationship part for pivotTableDefinition reference {reference.RelationshipId}");
            return;
        }

        var worksheetRelationships = worksheetRelsXml.Root
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Attribute("Id")?.Value))
            .GroupBy(relationship => relationship.Attribute("Id")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var reference in pivotTableReferences)
            ValidateWorksheetPivotTableReference(archive, contentTypesXml, entryNames, workbookPivotCacheIds, workbookPivotCacheDefinitions, worksheetRelsPart, reference, worksheetRelationships, issues);
    }

    private static void ValidateWorksheetPivotTableReference(
        ZipArchive archive,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        IReadOnlySet<int> workbookPivotCacheIds,
        IReadOnlyDictionary<int, string> workbookPivotCacheDefinitions,
        string worksheetRelsPart,
        WorksheetPivotTableReference reference,
        IReadOnlyDictionary<string, XElement> worksheetRelationships,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(reference.RelationshipId))
        {
            issues.Add($"{reference.WorksheetPart} pivotTableDefinition #{reference.Ordinal} has no relationship id");
            return;
        }

        if (!worksheetRelationships.TryGetValue(reference.RelationshipId, out var relationship))
        {
            issues.Add($"{reference.WorksheetPart} pivotTableDefinition #{reference.Ordinal} references missing relationship {reference.RelationshipId}");
            return;
        }

        var relationshipType = relationship.Attribute("Type")?.Value;
        if (!string.Equals(relationshipType, PivotTableRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{reference.WorksheetPart} pivotTableDefinition #{reference.Ordinal} relationship {reference.RelationshipId} has Type={relationshipType ?? "(none)"}; expected {PivotTableRelationshipType}");
            return;
        }

        if (string.Equals(relationship.Attribute("TargetMode")?.Value?.Trim(), "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{reference.WorksheetPart} pivotTableDefinition #{reference.Ordinal} relationship {reference.RelationshipId} must target a pivot table package part internally");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{reference.WorksheetPart} pivotTableDefinition #{reference.Ordinal} relationship {reference.RelationshipId} has no Target");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(worksheetRelsPart, target.Trim(), out var pivotTablePart, out var targetIssue))
        {
            issues.Add($"{reference.WorksheetPart} pivotTableDefinition #{reference.Ordinal} relationship {reference.RelationshipId} has invalid Target {target}: {targetIssue}");
            return;
        }

        if (!entryNames.Contains(pivotTablePart))
        {
            issues.Add($"{reference.WorksheetPart} pivotTableDefinition #{reference.Ordinal} relationship {reference.RelationshipId} targets missing package part {pivotTablePart}");
            return;
        }

        var contentType = GetEffectivePackageContentType(contentTypesXml, pivotTablePart);
        if (!string.Equals(contentType, PivotTableContentType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{pivotTablePart} has content type {contentType ?? "(none)"}; expected {PivotTableContentType}");
        }

        if (!TryLoadPackageXml(archive, pivotTablePart, issues, out var pivotTableXml))
            return;

        if (pivotTableXml.Root?.Name != WorkbookNs + "pivotTableDefinition")
        {
            issues.Add($"{pivotTablePart} has an invalid pivot table definition root element");
            return;
        }

        var cacheIdText = pivotTableXml.Root.Attribute("cacheId")?.Value;
        if (!int.TryParse(cacheIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var cacheId) ||
            cacheId < 0)
        {
            issues.Add($"{pivotTablePart} has invalid cacheId '{cacheIdText}'");
            return;
        }

        if (!workbookPivotCacheIds.Contains(cacheId))
        {
            issues.Add($"{pivotTablePart} references cacheId {cacheId}, but workbook has no matching pivotCache");
            return;
        }

        ValidatePivotTableCacheDefinitionRelationship(
            archive,
            contentTypesXml,
            entryNames,
            workbookPivotCacheDefinitions,
            pivotTablePart,
            cacheId,
            issues);
    }

    private static void ValidatePivotTableCacheDefinitionRelationship(
        ZipArchive archive,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        IReadOnlyDictionary<int, string> workbookPivotCacheDefinitions,
        string pivotTablePart,
        int cacheId,
        List<string> issues)
    {
        var pivotTableRelsPart = GetRelationshipPartPath(pivotTablePart);
        if (!TryLoadPackageXml(archive, pivotTableRelsPart, issues, out var pivotTableRelsXml) ||
            pivotTableRelsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            issues.Add($"{pivotTablePart} has no relationship part for pivot cache definition");
            return;
        }

        var cacheDefinitionRelationships = pivotTableRelsXml.Root
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(relationship => string.Equals(relationship.Attribute("Type")?.Value, PivotCacheDefinitionRelationshipType, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (cacheDefinitionRelationships.Length == 0)
        {
            issues.Add($"{pivotTablePart} has no pivot cache definition relationship");
            return;
        }

        if (cacheDefinitionRelationships.Length > 1)
            issues.Add($"{pivotTablePart} has {cacheDefinitionRelationships.Length} pivot cache definition relationships; expected 1");

        var relationship = cacheDefinitionRelationships[0];
        var relationshipId = relationship.Attribute("Id")?.Value ?? "(none)";
        if (string.Equals(relationship.Attribute("TargetMode")?.Value?.Trim(), "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{pivotTablePart} pivot cache definition relationship {relationshipId} must target a package part internally");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{pivotTablePart} pivot cache definition relationship {relationshipId} has no Target");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(pivotTableRelsPart, target.Trim(), out var cacheDefinitionPart, out var targetIssue))
        {
            issues.Add($"{pivotTablePart} pivot cache definition relationship {relationshipId} has invalid Target {target}: {targetIssue}");
            return;
        }

        if (!entryNames.Contains(cacheDefinitionPart))
        {
            issues.Add($"{pivotTablePart} pivot cache definition relationship {relationshipId} targets missing package part {cacheDefinitionPart}");
            return;
        }

        if (workbookPivotCacheDefinitions.TryGetValue(cacheId, out var expectedCacheDefinitionPart) &&
            !string.Equals(cacheDefinitionPart, expectedCacheDefinitionPart, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{pivotTablePart} pivot cache definition relationship {relationshipId} targets {cacheDefinitionPart}, but workbook cacheId {cacheId} targets {expectedCacheDefinitionPart}");
        }

        var contentType = GetEffectivePackageContentType(contentTypesXml, cacheDefinitionPart);
        if (!string.Equals(contentType, PivotCacheDefinitionContentType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{cacheDefinitionPart} has content type {contentType ?? "(none)"}; expected {PivotCacheDefinitionContentType}");
        }

        if (!TryLoadPackageXml(archive, cacheDefinitionPart, issues, out var cacheDefinitionXml))
            return;

        if (cacheDefinitionXml.Root?.Name != WorkbookNs + "pivotCacheDefinition")
            issues.Add($"{cacheDefinitionPart} has an invalid pivot cache definition root element");
    }

    private static void AddWorksheetDrawingPackageIssues(ZipArchive archive, List<string> issues)
    {
        if (!TryLoadPackageXml(archive, "[Content_Types].xml", issues, out var contentTypesXml) ||
            contentTypesXml.Root?.Name != PackageContentTypeNs + "Types")
        {
            return;
        }

        var entryNames = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var worksheetParts = entryNames
            .Where(part => part.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase))
            .Where(part => part.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Where(part => !IsPackageRelationshipPart(part))
            .OrderBy(part => part, StringComparer.OrdinalIgnoreCase);

        foreach (var worksheetPart in worksheetParts)
            AddWorksheetDrawingPackageIssues(archive, contentTypesXml, entryNames, worksheetPart, issues);
    }

    private static void AddWorksheetDrawingPackageIssues(
        ZipArchive archive,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        string worksheetPart,
        List<string> issues)
    {
        if (!TryLoadPackageXml(archive, worksheetPart, issues, out var worksheetXml) ||
            worksheetXml.Root?.Name != WorkbookNs + "worksheet")
        {
            return;
        }

        var drawingReferences = worksheetXml.Root
            .Descendants(WorkbookNs + "drawing")
            .Select((drawing, index) => new WorksheetDrawingReference(
                worksheetPart,
                index + 1,
                drawing.Attribute(OfficeRelationshipNs + "id")?.Value))
            .ToArray();
        if (drawingReferences.Length == 0)
            return;

        var worksheetRelsPart = GetRelationshipPartPath(worksheetPart);
        if (!TryLoadPackageXml(archive, worksheetRelsPart, issues, out var worksheetRelsXml) ||
            worksheetRelsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            foreach (var reference in drawingReferences.Where(reference => !string.IsNullOrWhiteSpace(reference.RelationshipId)))
                issues.Add($"{worksheetPart} has no relationship part for drawing reference {reference.RelationshipId}");
            return;
        }

        var worksheetRelationships = worksheetRelsXml.Root
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Attribute("Id")?.Value))
            .GroupBy(relationship => relationship.Attribute("Id")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var reference in drawingReferences)
            ValidateWorksheetDrawingReference(archive, contentTypesXml, entryNames, worksheetRelsPart, reference, worksheetRelationships, issues);
    }

    private static void ValidateWorksheetDrawingReference(
        ZipArchive archive,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        string worksheetRelsPart,
        WorksheetDrawingReference reference,
        IReadOnlyDictionary<string, XElement> worksheetRelationships,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(reference.RelationshipId))
        {
            issues.Add($"{reference.WorksheetPart} drawing #{reference.Ordinal} has no relationship id");
            return;
        }

        if (!worksheetRelationships.TryGetValue(reference.RelationshipId, out var relationship))
        {
            issues.Add($"{reference.WorksheetPart} drawing #{reference.Ordinal} references missing relationship {reference.RelationshipId}");
            return;
        }

        var relationshipType = relationship.Attribute("Type")?.Value;
        if (!string.Equals(relationshipType, DrawingRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{reference.WorksheetPart} drawing #{reference.Ordinal} relationship {reference.RelationshipId} has Type={relationshipType ?? "(none)"}; expected {DrawingRelationshipType}");
            return;
        }

        if (string.Equals(relationship.Attribute("TargetMode")?.Value?.Trim(), "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{reference.WorksheetPart} drawing #{reference.Ordinal} relationship {reference.RelationshipId} must target a worksheet drawing package part internally");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{reference.WorksheetPart} drawing #{reference.Ordinal} relationship {reference.RelationshipId} has no Target");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(worksheetRelsPart, target.Trim(), out var drawingPart, out var targetIssue))
        {
            issues.Add($"{reference.WorksheetPart} drawing #{reference.Ordinal} relationship {reference.RelationshipId} has invalid Target {target}: {targetIssue}");
            return;
        }

        if (!entryNames.Contains(drawingPart))
        {
            issues.Add($"{reference.WorksheetPart} drawing #{reference.Ordinal} relationship {reference.RelationshipId} targets missing package part {drawingPart}");
            return;
        }

        var contentType = GetEffectivePackageContentType(contentTypesXml, drawingPart);
        if (!string.Equals(contentType, DrawingContentType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{drawingPart} has content type {contentType ?? "(none)"}; expected {DrawingContentType}");
        }

        if (!TryLoadPackageXml(archive, drawingPart, issues, out var drawingXml))
            return;

        if (drawingXml.Root?.Name != SpreadsheetDrawingNs + "wsDr")
        {
            issues.Add($"{drawingPart} has an invalid worksheet drawing root element");
            return;
        }

        AddDrawingPartReferenceIssues(archive, contentTypesXml, entryNames, drawingPart, drawingXml, issues);
    }

    private static void AddDrawingPartReferenceIssues(
        ZipArchive archive,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        string drawingPart,
        XDocument drawingXml,
        List<string> issues)
    {
        var drawingRelsPart = GetRelationshipPartPath(drawingPart);
        XDocument? drawingRelationshipsXml = null;
        IReadOnlyDictionary<string, XElement>? drawingRelationships = null;

        foreach (var chartRelationshipId in drawingXml
                     .Descendants(DrawingChartNs + "chart")
                     .Select(chart => chart.Attribute(OfficeRelationshipNs + "id")?.Value)
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Select(id => id!))
        {
            ValidateDrawingOwnedRelationship(
                archive,
                contentTypesXml,
                entryNames,
                drawingPart,
                drawingRelsPart,
                ref drawingRelationshipsXml,
                ref drawingRelationships,
                chartRelationshipId,
                "chart",
                ChartRelationshipType,
                ChartContentType,
                DrawingChartNs + "chartSpace",
                issues);
        }

        foreach (var imageRelationshipId in drawingXml
                     .Descendants(DrawingNs + "blip")
                     .Select(blip => blip.Attribute(OfficeRelationshipNs + "embed")?.Value)
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Select(id => id!))
        {
            ValidateDrawingOwnedRelationship(
                archive,
                contentTypesXml,
                entryNames,
                drawingPart,
                drawingRelsPart,
                ref drawingRelationshipsXml,
                ref drawingRelationships,
                imageRelationshipId,
                "embedded image",
                ImageRelationshipType,
                expectedContentType: null,
                expectedRoot: null,
                issues);
        }
    }

    private static void ValidateDrawingOwnedRelationship(
        ZipArchive archive,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        string drawingPart,
        string drawingRelsPart,
        ref XDocument? drawingRelationshipsXml,
        ref IReadOnlyDictionary<string, XElement>? drawingRelationships,
        string relationshipId,
        string description,
        string expectedRelationshipType,
        string? expectedContentType,
        XName? expectedRoot,
        List<string> issues)
    {
        if (!TryGetDrawingRelationships(archive, drawingRelsPart, issues, ref drawingRelationshipsXml, ref drawingRelationships))
        {
            issues.Add($"{drawingPart} {description} reference {relationshipId}: missing relationship part {drawingRelsPart}");
            return;
        }

        if (!drawingRelationships!.TryGetValue(relationshipId, out var relationship))
        {
            issues.Add($"{drawingPart} {description} reference {relationshipId}: targets missing relationship {relationshipId} in {drawingRelsPart}");
            return;
        }

        var relationshipType = relationship.Attribute("Type")?.Value;
        if (!string.Equals(relationshipType, expectedRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{drawingPart} {description} reference {relationshipId}: relationship has Type={relationshipType ?? "(none)"}; expected {expectedRelationshipType}");
            return;
        }

        if (string.Equals(relationship.Attribute("TargetMode")?.Value?.Trim(), "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{drawingPart} {description} reference {relationshipId}: relationship must target a package part internally");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{drawingPart} {description} reference {relationshipId}: relationship has no Target");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(drawingRelsPart, target.Trim(), out var packagePart, out var targetIssue))
        {
            issues.Add($"{drawingPart} {description} reference {relationshipId} has invalid Target {target}: {targetIssue}");
            return;
        }

        if (!entryNames.Contains(packagePart))
        {
            issues.Add($"{drawingPart} {description} reference {relationshipId} targets missing package part {packagePart}");
            return;
        }

        var contentType = GetEffectivePackageContentType(contentTypesXml, packagePart);
        if (expectedContentType is null)
        {
            if (contentType is null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                issues.Add($"{packagePart} has content type {contentType ?? "(none)"}; expected an image/* content type");
            return;
        }

        if (!string.Equals(contentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
            issues.Add($"{packagePart} has content type {contentType ?? "(none)"}; expected {expectedContentType}");

        if (expectedRoot is null || !TryLoadPackageXml(archive, packagePart, issues, out var packageXml))
            return;

        if (packageXml.Root?.Name != expectedRoot)
            issues.Add($"{packagePart} has an invalid {description} root element");
    }

    private static bool TryGetDrawingRelationships(
        ZipArchive archive,
        string drawingRelsPart,
        List<string> issues,
        ref XDocument? drawingRelationshipsXml,
        ref IReadOnlyDictionary<string, XElement>? drawingRelationships)
    {
        if (drawingRelationships is not null)
            return true;

        if (!TryLoadPackageXml(archive, drawingRelsPart, issues, out var loadedRelationshipsXml) ||
            loadedRelationshipsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            return false;
        }

        drawingRelationshipsXml = loadedRelationshipsXml;
        drawingRelationships = drawingRelationshipsXml.Root
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Attribute("Id")?.Value))
            .GroupBy(relationship => relationship.Attribute("Id")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return true;
    }

    private static void AddWorksheetBackgroundPicturePackageIssues(ZipArchive archive, List<string> issues)
    {
        if (!TryLoadPackageXml(archive, "[Content_Types].xml", issues, out var contentTypesXml) ||
            contentTypesXml.Root?.Name != PackageContentTypeNs + "Types")
        {
            return;
        }

        var entryNames = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var worksheetParts = entryNames
            .Where(part => part.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase))
            .Where(part => part.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Where(part => !IsPackageRelationshipPart(part))
            .OrderBy(part => part, StringComparer.OrdinalIgnoreCase);

        foreach (var worksheetPart in worksheetParts)
            AddWorksheetBackgroundPicturePackageIssues(archive, contentTypesXml, entryNames, worksheetPart, issues);
    }

    private static void AddWorksheetBackgroundPicturePackageIssues(
        ZipArchive archive,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        string worksheetPart,
        List<string> issues)
    {
        if (!TryLoadPackageXml(archive, worksheetPart, issues, out var worksheetXml) ||
            worksheetXml.Root?.Name != WorkbookNs + "worksheet")
        {
            return;
        }

        var backgroundPictureReferences = worksheetXml.Root
            .Descendants(WorkbookNs + "picture")
            .Select((picture, index) => new WorksheetBackgroundPictureReference(
                worksheetPart,
                index + 1,
                picture.Attribute(OfficeRelationshipNs + "id")?.Value))
            .ToArray();
        if (backgroundPictureReferences.Length == 0)
            return;

        var worksheetRelsPart = GetRelationshipPartPath(worksheetPart);
        XDocument? worksheetRelationshipsXml = null;
        IReadOnlyDictionary<string, XElement>? worksheetRelationships = null;

        foreach (var reference in backgroundPictureReferences)
        {
            if (string.IsNullOrWhiteSpace(reference.RelationshipId))
            {
                issues.Add($"{worksheetPart} background picture #{reference.Ordinal} has no relationship id");
                continue;
            }

            ValidateDrawingOwnedRelationship(
                archive,
                contentTypesXml,
                entryNames,
                worksheetPart,
                worksheetRelsPart,
                ref worksheetRelationshipsXml,
                ref worksheetRelationships,
                reference.RelationshipId,
                $"background picture #{reference.Ordinal}",
                ImageRelationshipType,
                expectedContentType: null,
                expectedRoot: null,
                issues);
        }
    }

    private static void AddWorksheetTablePackageIssues(ZipArchive archive, List<string> issues)
    {
        if (!TryLoadPackageXml(archive, "[Content_Types].xml", issues, out var contentTypesXml) ||
            contentTypesXml.Root?.Name != PackageContentTypeNs + "Types")
        {
            return;
        }

        var entryNames = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var worksheetParts = entryNames
            .Where(part => part.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase))
            .Where(part => part.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Where(part => !IsPackageRelationshipPart(part))
            .OrderBy(part => part, StringComparer.OrdinalIgnoreCase);

        foreach (var worksheetPart in worksheetParts)
            AddWorksheetTablePackageIssues(archive, contentTypesXml, entryNames, worksheetPart, issues);
    }

    private static void AddWorksheetTablePackageIssues(
        ZipArchive archive,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        string worksheetPart,
        List<string> issues)
    {
        if (!TryLoadPackageXml(archive, worksheetPart, issues, out var worksheetXml) ||
            worksheetXml.Root?.Name != WorkbookNs + "worksheet")
        {
            return;
        }

        var tablePartReferences = worksheetXml.Root
            .Descendants(WorkbookNs + "tableParts")
            .SelectMany(tableParts =>
            {
                var references = tableParts
                    .Elements(WorkbookNs + "tablePart")
                    .Select((tablePart, index) => new WorksheetTablePartReference(
                        worksheetPart,
                        index + 1,
                        tablePart.Attribute(OfficeRelationshipNs + "id")?.Value))
                    .ToArray();
                AddTablePartsCountIssues(worksheetPart, tableParts, references.Length, issues);
                return references;
            })
            .ToArray();
        if (tablePartReferences.Length == 0)
            return;

        var worksheetRelsPart = GetRelationshipPartPath(worksheetPart);
        if (!TryLoadPackageXml(archive, worksheetRelsPart, issues, out var worksheetRelsXml) ||
            worksheetRelsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            foreach (var reference in tablePartReferences.Where(reference => !string.IsNullOrWhiteSpace(reference.RelationshipId)))
                issues.Add($"{worksheetPart} has no relationship part for tablePart reference {reference.RelationshipId}");
            return;
        }

        var worksheetRelationships = worksheetRelsXml.Root
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Attribute("Id")?.Value))
            .GroupBy(relationship => relationship.Attribute("Id")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var reference in tablePartReferences)
            ValidateWorksheetTablePartReference(archive, contentTypesXml, entryNames, worksheetRelsPart, reference, worksheetRelationships, issues);
    }

    private static void AddTablePartsCountIssues(
        string worksheetPart,
        XElement tableParts,
        int actualCount,
        List<string> issues)
    {
        var countText = tableParts.Attribute("count")?.Value;
        if (string.IsNullOrWhiteSpace(countText))
            return;

        if (!int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var declaredCount) ||
            declaredCount < 0)
        {
            issues.Add($"{worksheetPart} tableParts has invalid count '{countText}'");
            return;
        }

        if (declaredCount != actualCount)
            issues.Add($"{worksheetPart} tableParts count is {declaredCount}, but contains {actualCount} tablePart entries");
    }

    private static void ValidateWorksheetTablePartReference(
        ZipArchive archive,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        string worksheetRelsPart,
        WorksheetTablePartReference reference,
        IReadOnlyDictionary<string, XElement> worksheetRelationships,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(reference.RelationshipId))
        {
            issues.Add($"{reference.WorksheetPart} tablePart #{reference.Ordinal} has no relationship id");
            return;
        }

        if (!worksheetRelationships.TryGetValue(reference.RelationshipId, out var relationship))
        {
            issues.Add($"{reference.WorksheetPart} tablePart #{reference.Ordinal} references missing relationship {reference.RelationshipId}");
            return;
        }

        var relationshipType = relationship.Attribute("Type")?.Value;
        if (!string.Equals(relationshipType, TableRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{reference.WorksheetPart} tablePart #{reference.Ordinal} relationship {reference.RelationshipId} has Type={relationshipType ?? "(none)"}; expected {TableRelationshipType}");
            return;
        }

        if (string.Equals(relationship.Attribute("TargetMode")?.Value?.Trim(), "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{reference.WorksheetPart} tablePart #{reference.Ordinal} relationship {reference.RelationshipId} must target a table package part internally");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{reference.WorksheetPart} tablePart #{reference.Ordinal} relationship {reference.RelationshipId} has no Target");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(worksheetRelsPart, target.Trim(), out var tablePart, out var targetIssue))
        {
            issues.Add($"{reference.WorksheetPart} tablePart #{reference.Ordinal} relationship {reference.RelationshipId} has invalid Target {target}: {targetIssue}");
            return;
        }

        if (!entryNames.Contains(tablePart))
        {
            issues.Add($"{reference.WorksheetPart} tablePart #{reference.Ordinal} relationship {reference.RelationshipId} targets missing package part {tablePart}");
            return;
        }

        var contentType = GetEffectivePackageContentType(contentTypesXml, tablePart);
        if (!string.Equals(contentType, TableContentType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{tablePart} has content type {contentType ?? "(none)"}; expected {TableContentType}");
        }

        if (!TryLoadPackageXml(archive, tablePart, issues, out var tableXml))
            return;

        if (tableXml.Root?.Name != WorkbookNs + "table")
        {
            issues.Add($"{tablePart} has an invalid table root element");
            return;
        }

        AddWorksheetTableMetadataIssues(tablePart, tableXml.Root, issues);
    }

    private static void AddWorksheetTableMetadataIssues(string tablePart, XElement table, List<string> issues)
    {
        var tableIdText = table.Attribute("id")?.Value;
        if (!int.TryParse(tableIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var tableId) ||
            tableId <= 0)
        {
            issues.Add($"{tablePart} table has invalid id '{tableIdText}'");
        }

        if (string.IsNullOrWhiteSpace(table.Attribute("ref")?.Value))
            issues.Add($"{tablePart} table has no ref");
        if (string.IsNullOrWhiteSpace(table.Attribute("displayName")?.Value))
            issues.Add($"{tablePart} table has no displayName");

        var tableColumns = table.Elements(WorkbookNs + "tableColumns").ToArray();
        if (tableColumns.Length == 0)
        {
            issues.Add($"{tablePart} table has no tableColumns element");
            return;
        }

        if (tableColumns.Length > 1)
            issues.Add($"{tablePart} table has {tableColumns.Length} tableColumns elements; expected at most one");

        AddWorksheetTableColumnsIssues(tablePart, tableColumns[0], issues);
    }

    private static void AddWorksheetTableColumnsIssues(string tablePart, XElement tableColumns, List<string> issues)
    {
        var columns = tableColumns.Elements(WorkbookNs + "tableColumn").ToArray();
        var countText = tableColumns.Attribute("count")?.Value;
        if (!int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var declaredCount) ||
            declaredCount < 0)
        {
            issues.Add($"{tablePart} tableColumns has invalid count '{countText}'");
        }
        else if (declaredCount != columns.Length)
        {
            issues.Add($"{tablePart} tableColumns count is {declaredCount}, but contains {columns.Length} tableColumn entries");
        }

        if (columns.Length == 0)
            issues.Add($"{tablePart} tableColumns has no tableColumn entries");

        var seenColumnIds = new HashSet<int>();
        var seenColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            var columnIdText = column.Element.Attribute("id")?.Value;
            if (!int.TryParse(columnIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var columnId) ||
                columnId <= 0)
            {
                issues.Add($"{tablePart} tableColumn #{column.Ordinal} has invalid id '{columnIdText}'");
            }
            else if (!seenColumnIds.Add(columnId))
            {
                issues.Add($"{tablePart} tableColumns has duplicate tableColumn id {columnId}");
            }

            var columnName = column.Element.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(columnName))
            {
                issues.Add($"{tablePart} tableColumn #{column.Ordinal} has no name");
            }
            else if (!seenColumnNames.Add(columnName))
            {
                issues.Add($"{tablePart} tableColumns has duplicate tableColumn name '{columnName}'");
            }
        }
    }

    private static bool TryFindWorkbookPart(
        ZipArchive archive,
        IReadOnlySet<string> entryNames,
        out string workbookPart)
    {
        workbookPart = string.Empty;
        if (!TryLoadPackageXml(archive, "_rels/.rels", [], out var rootRelationshipsXml) ||
            rootRelationshipsXml.Root?.Name != PackageRelationshipNs + "Relationships")
        {
            return false;
        }

        foreach (var relationship in rootRelationshipsXml.Root
                     .Elements(PackageRelationshipNs + "Relationship")
                     .Where(relationship => string.Equals(
                         relationship.Attribute("Type")?.Value,
                         OfficeDocumentRelationshipType,
                         StringComparison.Ordinal)))
        {
            var targetMode = relationship.Attribute("TargetMode")?.Value;
            var target = relationship.Attribute("Target")?.Value;
            if (string.Equals(targetMode?.Trim(), "External", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            if (TryResolvePackageRelationshipTarget("_rels/.rels", target.Trim(), out var resolvedTarget, out _) &&
                entryNames.Contains(resolvedTarget))
            {
                workbookPart = resolvedTarget;
                return true;
            }
        }

        return false;
    }

    private static void ValidateWorkbookSheetMap(
        string workbookPart,
        string workbookRelsPart,
        XElement sheet,
        IReadOnlyDictionary<string, XElement> workbookRelationships,
        XDocument contentTypesXml,
        IReadOnlySet<string> entryNames,
        List<string> issues)
    {
        var sheetName = sheet.Attribute("name")?.Value;
        var sheetLabel = string.IsNullOrWhiteSpace(sheetName) ? "(unnamed sheet)" : sheetName;
        var relationshipId = sheet.Attribute(OfficeRelationshipNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            issues.Add($"{workbookPart} sheet {sheetLabel} has no relationship id");
            return;
        }

        if (!workbookRelationships.TryGetValue(relationshipId, out var relationship))
        {
            issues.Add($"{workbookPart} sheet {sheetLabel} references missing workbook relationship {relationshipId}");
            return;
        }

        var relationshipType = relationship.Attribute("Type")?.Value;
        if (relationshipType is not WorksheetRelationshipType and not ChartsheetRelationshipType)
        {
            issues.Add($"{workbookPart} sheet {sheetLabel} relationship {relationshipId} has non-sheet Type {relationshipType ?? "(none)"}");
            return;
        }

        var targetMode = relationship.Attribute("TargetMode")?.Value;
        if (string.Equals(targetMode?.Trim(), "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{workbookPart} sheet {sheetLabel} relationship {relationshipId} must target a sheet package part internally");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target) ||
            !TryResolvePackageRelationshipTarget(workbookRelsPart, target.Trim(), out var resolvedTarget, out _) ||
            !entryNames.Contains(resolvedTarget))
        {
            return;
        }

        var contentType = GetEffectivePackageContentType(contentTypesXml, resolvedTarget);
        var expectedContentType = relationshipType == WorksheetRelationshipType
            ? WorksheetContentType
            : ChartsheetContentType;
        if (!string.Equals(contentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{workbookPart} sheet {sheetLabel} relationship {relationshipId} targets {resolvedTarget} with content type {contentType ?? "(none)"}; expected {expectedContentType}");
        }
    }

    private static bool IsPackageRelationshipPart(string part)
    {
        var normalizedPart = NormalizePackagePart(part);
        return normalizedPart.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(normalizedPart, "_rels/.rels", StringComparison.OrdinalIgnoreCase) ||
                normalizedPart.Contains("/_rels/", StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidatePackageRelationship(
        string relationshipPart,
        XElement relationship,
        IReadOnlySet<string> entryNames,
        HashSet<string> ids,
        List<string> issues)
    {
        var id = relationship.Attribute("Id")?.Value;
        var relationshipLabel = $"{relationshipPart} Relationship {FormatRelationshipIssueId(id)}";
        if (relationship.Elements().Any())
            issues.Add($"{relationshipLabel} must not contain child elements");

        foreach (var attribute in relationship.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            if (attribute.Name.NamespaceName.Length == 0 &&
                attribute.Name.LocalName is "Id" or "Type" or "Target" or "TargetMode")
            {
                continue;
            }

            issues.Add($"{relationshipLabel} has unexpected attribute '{attribute.Name}'");
        }

        if (string.IsNullOrWhiteSpace(id))
            issues.Add($"{relationshipPart} has a Relationship without Id");
        else if (!string.Equals(id, id.Trim(), StringComparison.Ordinal))
            issues.Add($"{relationshipPart} Relationship Id '{id}' has leading or trailing whitespace");
        else if (!ids.Add(id))
            issues.Add($"{relationshipPart} has duplicate Relationship Id {id}");

        var type = relationship.Attribute("Type")?.Value;
        if (string.IsNullOrWhiteSpace(type))
        {
            issues.Add($"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} has no Type");
        }
        else
        {
            if (!string.Equals(type, type.Trim(), StringComparison.Ordinal))
                issues.Add($"{relationshipLabel} Type has leading or trailing whitespace");

            if (!Uri.TryCreate(type.Trim(), UriKind.Absolute, out var typeUri) ||
                string.IsNullOrWhiteSpace(typeUri.Scheme))
            {
                issues.Add($"{relationshipLabel} Type '{type}' is not an absolute URI");
            }
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} has no Target");
            return;
        }

        if (!string.Equals(target, target.Trim(), StringComparison.Ordinal))
            issues.Add($"{relationshipLabel} Target has leading or trailing whitespace");
        target = target.Trim();

        var targetMode = relationship.Attribute("TargetMode")?.Value;
        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, targetMode.Trim(), StringComparison.Ordinal))
        {
            issues.Add($"{relationshipLabel} TargetMode has leading or trailing whitespace");
        }

        targetMode = targetMode?.Trim();
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
            return;

        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} has invalid TargetMode {targetMode}");
            return;
        }

        if (target.IndexOf('\\') >= 0)
            issues.Add($"{relationshipLabel} Target uses backslashes instead of package URI separators");

        if (IsAbsoluteRelationshipTarget(target))
        {
            issues.Add(
                $"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} targets external URI without TargetMode=External: {target}");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(relationshipPart, target, out var resolvedTarget, out var error))
        {
            issues.Add(
                $"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} has invalid Target {target}: {error}");
            return;
        }

        if (!entryNames.Contains(resolvedTarget))
            issues.Add($"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} targets missing package part {resolvedTarget}");
    }

    private static string FormatRelationshipIssueId(string? id) =>
        string.IsNullOrWhiteSpace(id) ? "(no Id)" : id;

    private static bool IsAbsoluteRelationshipTarget(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
        !string.IsNullOrWhiteSpace(uri.Scheme);

    private static bool TryResolvePackageRelationshipTarget(
        string relationshipPart,
        string target,
        out string resolvedTarget,
        out string error)
    {
        resolvedTarget = string.Empty;
        error = string.Empty;

        target = StripRelationshipTargetFragment(target.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(target))
        {
            error = "empty internal target";
            return false;
        }

        var ownerPart = GetRelationshipOwnerPart(relationshipPart);
        string combined;
        if (target.StartsWith("/", StringComparison.Ordinal))
        {
            combined = target.TrimStart('/');
        }
        else
        {
            var ownerDirectory = ownerPart.Contains('/', StringComparison.Ordinal)
                ? ownerPart[..ownerPart.LastIndexOf('/')]
                : string.Empty;
            combined = string.IsNullOrWhiteSpace(ownerDirectory)
                ? target
                : $"{ownerDirectory}/{target}";
        }

        if (!TryNormalizePackagePathSegments(combined, out resolvedTarget))
        {
            error = "target escapes the package root";
            return false;
        }

        resolvedTarget = XlsxPackagePath.ResolveRelationshipTarget(ownerPart, target);
        return !string.IsNullOrWhiteSpace(resolvedTarget);
    }

    private static string StripRelationshipTargetFragment(string target)
    {
        var fragmentIndex = target.IndexOf('#', StringComparison.Ordinal);
        var queryIndex = target.IndexOf('?', StringComparison.Ordinal);
        var endIndex = fragmentIndex < 0
            ? queryIndex
            : queryIndex < 0
                ? fragmentIndex
                : Math.Min(fragmentIndex, queryIndex);
        return endIndex < 0 ? target : target[..endIndex];
    }

    private static bool TryNormalizePackagePathSegments(string path, out string normalizedPath)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    normalizedPath = string.Empty;
                    return false;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        normalizedPath = NormalizePackagePart(string.Join("/", segments));
        return true;
    }

    private static string GetRelationshipOwnerPart(string relationshipPart)
    {
        relationshipPart = NormalizePackagePart(relationshipPart);
        if (string.Equals(relationshipPart, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        const string relationshipMarker = "/_rels/";
        var markerIndex = relationshipPart.LastIndexOf(relationshipMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return relationshipPart.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
                ? relationshipPart[..^".rels".Length]
                : relationshipPart;

        var directory = relationshipPart[..markerIndex];
        var fileName = relationshipPart[(markerIndex + relationshipMarker.Length)..];
        if (fileName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            fileName = fileName[..^".rels".Length];
        return string.IsNullOrWhiteSpace(directory)
            ? fileName
            : $"{directory}/{fileName}";
    }

    private static string GetRelationshipPartPath(string sourcePartPath)
        => OpcPathHelper.GetRelationshipPartPath(NormalizePackagePart(sourcePartPath));

    private static string NormalizePackagePart(string part) =>
        OpcPathHelper.ToZipEntryPath(part);

    private static bool TryLoadPackageXml(
        ZipArchive archive,
        string entryName,
        List<string> issues,
        out XDocument xml)
    {
        xml = new XDocument();
        var entry = archive.GetEntry(entryName);
        if (entry is null)
            return false;

        try
        {
            xml = LoadPackageXml(entry);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or XmlException)
        {
            issues.Add($"{entryName} is not parseable XML: {ex.Message}");
            return false;
        }
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        return OpcXml.LoadXml(entry);
    }

    private sealed record SharedStringCellReference(
        string WorksheetPart,
        string CellReference,
        string? ValueText);

    private sealed record StyleReference(
        string WorksheetPart,
        string Description,
        string? ValueText);

    private sealed record WorkbookExternalReference(
        int Ordinal,
        string? RelationshipId);

    private sealed record ExternalBookReference(
        int Ordinal,
        string? RelationshipId);

    private sealed record WorkbookPivotCacheReference(
        int Ordinal,
        string? CacheId,
        string? RelationshipId);

    private sealed record WorksheetPivotTableReference(
        string WorksheetPart,
        int Ordinal,
        string? RelationshipId);

    private sealed record WorksheetDrawingReference(
        string WorksheetPart,
        int Ordinal,
        string? RelationshipId);

    private sealed record WorksheetBackgroundPictureReference(
        string WorksheetPart,
        int Ordinal,
        string? RelationshipId);

    private sealed record WorksheetTablePartReference(
        string WorksheetPart,
        int Ordinal,
        string? RelationshipId);
}
