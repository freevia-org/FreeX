using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

public static class XlsxPackageHealthValidator
{
    private const string RelationshipPartContentType =
        "application/vnd.openxmlformats-package.relationships+xml";

    private static readonly XNamespace PackageContentTypeNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelationshipNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public static IReadOnlyList<string> Validate(ZipArchive archive)
    {
        var issues = new List<string>();
        AddPackageEntryIssues(archive, issues);
        AddPackageContentTypeIssues(archive, issues);
        AddPackageRelationshipIssues(archive, issues);
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
        var overrideContentType = contentTypesXml.Root?
            .Elements(PackageContentTypeNs + "Override")
            .FirstOrDefault(element => string.Equals(
                NormalizeContentTypePartName(element.Attribute("PartName")?.Value),
                normalizedContentTypePartName,
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")
            ?.Value;

        if (!string.IsNullOrWhiteSpace(overrideContentType))
            return overrideContentType;

        var extension = GetPackagePartExtension(normalizedPartName);
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        return contentTypesXml.Root?
            .Elements(PackageContentTypeNs + "Default")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("Extension")?.Value?.Trim(),
                extension,
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")
            ?.Value;
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

    private static string NormalizePackagePart(string part) =>
        part.Replace('\\', '/').TrimStart('/');

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
