using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

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
    private const string WorksheetContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
    private const string ChartsheetContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml";
    private const string SharedStringsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml";
    private const string StylesContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml";

    private static readonly XNamespace PackageContentTypeNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelationshipNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace WorkbookNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelationshipNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly HashSet<string> WorkbookMainContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
        "application/vnd.ms-excel.sheet.macroEnabled.main+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml",
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

        try
        {
            target = Uri.UnescapeDataString(target);
        }
        catch (UriFormatException ex)
        {
            error = ex.Message;
            return false;
        }

        string combined;
        if (target.StartsWith("/", StringComparison.Ordinal))
        {
            combined = target.TrimStart('/');
        }
        else
        {
            var ownerPart = GetRelationshipOwnerPart(relationshipPart);
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
    {
        var normalizedPath = NormalizePackagePart(sourcePartPath);
        var slashIndex = normalizedPath.LastIndexOf('/');
        if (slashIndex < 0)
            return $"_rels/{normalizedPath}.rels";

        return string.Concat(
            normalizedPath.AsSpan(0, slashIndex),
            "/_rels/",
            normalizedPath.AsSpan(slashIndex + 1),
            ".rels");
    }

    private static string NormalizePackagePart(string part) =>
        part.Replace('\\', '/').TrimStart('/');

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
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private sealed record SharedStringCellReference(
        string WorksheetPart,
        string CellReference,
        string? ValueText);

    private sealed record StyleReference(
        string WorksheetPart,
        string Description,
        string? ValueText);
}
