using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetOleControlNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string ControlPropertiesRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp";
    private const string ControlPropertiesContentType = "application/vnd.ms-excel.controlproperties+xml";

    private static readonly HashSet<string> NoAttributes = [];

    private static readonly HashSet<string> OleObjectAttributes =
    [
        "progId",
        "dvAspect",
        "link",
        "oleUpdate",
        "autoLoad",
        "shapeId"
    ];

    private static readonly HashSet<string> ControlAttributes =
    [
        "shapeId",
        "name"
    ];

    private static readonly HashSet<string> ControlPropertiesAttributes =
    [
        "locked",
        "defaultSize",
        "print",
        "disabled",
        "recalcAlways",
        "uiObject",
        "autoFill",
        "autoLine",
        "autoPict",
        "macro",
        "altText",
        "linkedCell",
        "listFillRange",
        "cf"
    ];

    private static readonly HashSet<string> OleUpdateValues =
    [
        "OLEUPDATE_ALWAYS",
        "OLEUPDATE_ONCALL"
    ];

    public static void NormalizeWorksheets(ZipArchive archive)
        => NormalizePackage(archive);

    public static void NormalizePackage(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var changed = NormalizeWorksheetRoot(root);
            changed |= RebindControlPropertiesRelationships(archive, worksheetEntry.FullName, worksheetXml);
            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var changed = false;
        changed |= NormalizeOleObjects(worksheetRoot);
        changed |= NormalizeControls(worksheetRoot);
        return changed;
    }

    private static bool NormalizeOleObjects(XElement worksheetRoot)
    {
        var changed = false;
        var keptOleObjects = false;
        foreach (var oleObjects in worksheetRoot.Elements(WorksheetNs + "oleObjects").ToList())
        {
            if (keptOleObjects)
            {
                oleObjects.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeOleObjectsElement(oleObjects);
            if (!oleObjects.Elements(WorksheetNs + "oleObject").Any())
            {
                oleObjects.Remove();
                changed = true;
                continue;
            }

            keptOleObjects = true;
        }

        return changed;
    }

    private static bool NormalizeControls(XElement worksheetRoot)
    {
        var changed = false;
        var keptControls = false;
        foreach (var controls in worksheetRoot.Elements(WorksheetNs + "controls").ToList())
        {
            if (keptControls)
            {
                controls.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeControlsElement(controls);
            if (!controls.Elements(WorksheetNs + "control").Any())
            {
                controls.Remove();
                changed = true;
                continue;
            }

            keptControls = true;
        }

        return changed;
    }

    private static bool NormalizeOleObjectsElement(XElement oleObjects)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(oleObjects, NoAttributes, RelNs + "id");
        changed |= RemoveUnexpectedChildElements(oleObjects, WorksheetNs + "oleObject");

        foreach (var oleObject in oleObjects.Elements(WorksheetNs + "oleObject").ToList())
        {
            changed |= NormalizeOleObjectElement(oleObject);
            if (!ShouldRemoveRelationshipBackedElement(oleObject))
                continue;

            oleObject.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeControlsElement(XElement controls)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(controls, NoAttributes, RelNs + "id");
        changed |= RemoveUnexpectedChildElements(controls, WorksheetNs + "control");

        foreach (var control in controls.Elements(WorksheetNs + "control").ToList())
        {
            changed |= NormalizeControlElement(control);
            if (!ShouldRemoveRelationshipBackedElement(control))
                continue;

            control.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeOleObjectElement(XElement oleObject)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(oleObject, OleObjectAttributes, RelNs + "id");
        changed |= RemoveUnexpectedChildElements(oleObject, WorksheetNs + "objectPr");
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(oleObject, "shapeId", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(oleObject, "autoLoad", NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(oleObject, "oleUpdate", NormalizeOleUpdate);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(oleObject, "progId", NormalizeOptionalText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(oleObject, "dvAspect", NormalizeOptionalText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(oleObject, "link", NormalizeOptionalText);
        return changed;
    }

    private static bool NormalizeControlElement(XElement control)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(control, ControlAttributes, RelNs + "id");
        changed |= RemoveUnexpectedChildElements(control, WorksheetNs + "controlPr");
        changed |= NormalizeControlProperties(control);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(control, "shapeId", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(control, "name", NormalizeOptionalText);
        return changed;
    }

    private static bool NormalizeControlProperties(XElement control)
    {
        var changed = false;
        var keptControlProperties = false;
        foreach (var controlProperties in control.Elements(WorksheetNs + "controlPr").ToList())
        {
            if (keptControlProperties)
            {
                controlProperties.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(controlProperties, ControlPropertiesAttributes, RelNs + "id");
            changed |= RemoveUnexpectedChildElements(controlProperties, WorksheetNs + "anchor");
            changed |= NormalizeBooleanAttribute(controlProperties, "locked");
            changed |= NormalizeBooleanAttribute(controlProperties, "defaultSize");
            changed |= NormalizeBooleanAttribute(controlProperties, "print");
            changed |= NormalizeBooleanAttribute(controlProperties, "disabled");
            changed |= NormalizeBooleanAttribute(controlProperties, "recalcAlways");
            changed |= NormalizeBooleanAttribute(controlProperties, "uiObject");
            changed |= NormalizeBooleanAttribute(controlProperties, "autoFill");
            changed |= NormalizeBooleanAttribute(controlProperties, "autoLine");
            changed |= NormalizeBooleanAttribute(controlProperties, "autoPict");
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "macro", NormalizeOptionalText);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "altText", NormalizeOptionalText);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "linkedCell", NormalizeOptionalText);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "listFillRange", NormalizeOptionalText);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "cf", NormalizeOptionalText);

            if (controlProperties.Attribute(RelNs + "id") is null && !controlProperties.HasAttributes && !controlProperties.HasElements)
            {
                controlProperties.Remove();
                changed = true;
                continue;
            }

            keptControlProperties = true;
        }

        return changed;
    }

    private static bool ShouldRemoveRelationshipBackedElement(XElement element) =>
        element.Attribute(RelNs + "id") is null ||
        element.Attribute("shapeId") is null;

    private static bool RebindControlPropertiesRelationships(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        var controls = worksheetXml.Root?
            .Element(WorksheetNs + "controls")?
            .Elements(WorksheetNs + "control")
            .ToList();
        if (controls is null || controls.Count == 0)
            return false;

        var controlPropertiesParts = archive.Entries
            .Select(entry => XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/')))
            .Where(path => path.StartsWith("xl/ctrlProps/", StringComparison.OrdinalIgnoreCase) &&
                           path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (controlPropertiesParts.Count == 0)
            return false;

        var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relationshipsXml = archive.GetEntry(relationshipsPath) is { } relationshipsEntry
            ? XlsxPackageXmlEditor.LoadXml(relationshipsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));

        var relationshipsChanged = false;
        var controlPropertiesRelationships = relationshipsXml.Root?
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship => IsControlPropertiesRelationship(worksheetPath, relationship, archive))
            .ToList()
            ?? [];

        var usedRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextControlPropertiesPartIndex = 0;
        var worksheetChanged = false;
        foreach (var control in controls)
        {
            var relationshipId = control.Attribute(RelNs + "id")?.Value;
            var relationship = FindUnusedValidControlRelationship(
                controlPropertiesRelationships,
                relationshipId,
                usedRelationshipIds);
            if (relationship is null)
            {
                relationship = FindNextUnusedControlRelationship(controlPropertiesRelationships, usedRelationshipIds);
            }

            if (relationship is null)
            {
                while (nextControlPropertiesPartIndex < controlPropertiesParts.Count &&
                       controlPropertiesRelationships.Any(candidate =>
                           string.Equals(
                               ResolveRelationshipTarget(worksheetPath, candidate),
                               controlPropertiesParts[nextControlPropertiesPartIndex],
                               StringComparison.OrdinalIgnoreCase)))
                {
                    nextControlPropertiesPartIndex++;
                }

                if (nextControlPropertiesPartIndex >= controlPropertiesParts.Count)
                    continue;

                var targetPart = controlPropertiesParts[nextControlPropertiesPartIndex++];
                relationship = AddControlPropertiesRelationship(relationshipsXml, worksheetPath, targetPart);
                controlPropertiesRelationships.Add(relationship);
                relationshipsChanged = true;
            }

            var reboundId = relationship.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(reboundId))
                continue;

            usedRelationshipIds.Add(reboundId);
            worksheetChanged |= SetRelationshipId(control, reboundId);
            foreach (var controlProperties in control.Elements(WorksheetNs + "controlPr"))
                worksheetChanged |= SetRelationshipId(controlProperties, reboundId);
            EnsureControlPropertiesContentType(archive, relationship, worksheetPath);
        }

        if (relationshipsChanged)
            XlsxPackageXmlEditor.ReplaceXml(archive, relationshipsPath, relationshipsXml);

        return worksheetChanged;
    }

    private static XElement? FindUnusedValidControlRelationship(
        IReadOnlyList<XElement> relationships,
        string? relationshipId,
        ISet<string> usedRelationshipIds)
    {
        if (string.IsNullOrWhiteSpace(relationshipId) || usedRelationshipIds.Contains(relationshipId))
            return null;

        return relationships.FirstOrDefault(relationship =>
            string.Equals(relationship.Attribute("Id")?.Value, relationshipId, StringComparison.OrdinalIgnoreCase));
    }

    private static XElement? FindNextUnusedControlRelationship(
        IReadOnlyList<XElement> relationships,
        ISet<string> usedRelationshipIds)
        => relationships.FirstOrDefault(relationship =>
        {
            var relationshipId = relationship.Attribute("Id")?.Value;
            return !string.IsNullOrWhiteSpace(relationshipId) && !usedRelationshipIds.Contains(relationshipId);
        });

    private static XElement AddControlPropertiesRelationship(
        XDocument relationshipsXml,
        string worksheetPath,
        string controlPropertiesPart)
    {
        var root = relationshipsXml.Root;
        if (root is null)
        {
            root = new XElement(PackageRelNs + "Relationships");
            relationshipsXml.Add(root);
        }

        var relationship = new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", XlsxPackageXmlEditor.NextRelationshipId(relationshipsXml, PackageRelNs)),
            new XAttribute("Type", ControlPropertiesRelationshipType),
            new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(worksheetPath, controlPropertiesPart)));
        root.Add(relationship);
        return relationship;
    }

    private static bool SetRelationshipId(XElement element, string relationshipId)
    {
        if (string.Equals(element.Attribute(RelNs + "id")?.Value, relationshipId, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(RelNs + "id", relationshipId);
        return true;
    }

    private static bool IsControlPropertiesRelationship(
        string worksheetPath,
        XElement relationship,
        ZipArchive archive)
    {
        if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            return false;

        var relationshipType = relationship.Attribute("Type")?.Value;
        if (!string.Equals(relationshipType, ControlPropertiesRelationshipType, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                relationshipType,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/control",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var targetPart = ResolveRelationshipTarget(worksheetPath, relationship);
        return targetPart.StartsWith("xl/ctrlProps/", StringComparison.OrdinalIgnoreCase) &&
               targetPart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               archive.GetEntry(targetPart) is not null;
    }

    private static string ResolveRelationshipTarget(string worksheetPath, XElement relationship)
    {
        var target = relationship.Attribute("Target")?.Value;
        return string.IsNullOrWhiteSpace(target)
            ? ""
            : XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
    }

    private static void EnsureControlPropertiesContentType(
        ZipArchive archive,
        XElement relationship,
        string worksheetPath)
    {
        var targetPart = ResolveRelationshipTarget(worksheetPath, relationship);
        if (!string.IsNullOrWhiteSpace(targetPart))
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, targetPart, ControlPropertiesContentType);
    }

    private static bool RemoveUnexpectedChildElements(XElement element, XName allowedChildName)
    {
        var changed = false;
        foreach (var child in element.Elements().Where(child => child.Name != allowedChildName).ToList())
        {
            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" or "false" => trimmed,
            _ => null
        };
    }

    private static bool NormalizeBooleanAttribute(XElement element, string attributeName) =>
        XlsxXmlNormalizationHelpers.NormalizeAttribute(element, attributeName, NormalizeBoolean);

    private static string? NormalizeOleUpdate(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && OleUpdateValues.Contains(trimmed) ? trimmed : null;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
