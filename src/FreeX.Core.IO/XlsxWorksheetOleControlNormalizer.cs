using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetOleControlNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

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
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
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
        changed |= RemoveUnknownAttributes(oleObjects, NoAttributes);
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
        changed |= RemoveUnknownAttributes(controls, NoAttributes);
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
        changed |= RemoveUnknownAttributes(oleObject, OleObjectAttributes);
        changed |= RemoveUnexpectedChildElements(oleObject, WorksheetNs + "objectPr");
        changed |= NormalizeAttribute(oleObject, "shapeId", NormalizeUnsignedIntOrNull);
        changed |= NormalizeAttribute(oleObject, "autoLoad", NormalizeBoolean);
        changed |= NormalizeAttribute(oleObject, "oleUpdate", NormalizeOleUpdate);
        changed |= NormalizeAttribute(oleObject, "progId", NormalizeOptionalText);
        changed |= NormalizeAttribute(oleObject, "dvAspect", NormalizeOptionalText);
        changed |= NormalizeAttribute(oleObject, "link", NormalizeOptionalText);
        return changed;
    }

    private static bool NormalizeControlElement(XElement control)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(control, ControlAttributes);
        changed |= RemoveUnexpectedChildElements(control, WorksheetNs + "controlPr");
        changed |= NormalizeControlProperties(control);
        changed |= NormalizeAttribute(control, "shapeId", NormalizeUnsignedIntOrNull);
        changed |= NormalizeAttribute(control, "name", NormalizeOptionalText);
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

            changed |= RemoveUnknownAttributes(controlProperties, ControlPropertiesAttributes);
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
            changed |= NormalizeAttribute(controlProperties, "macro", NormalizeOptionalText);
            changed |= NormalizeAttribute(controlProperties, "altText", NormalizeOptionalText);
            changed |= NormalizeAttribute(controlProperties, "linkedCell", NormalizeOptionalText);
            changed |= NormalizeAttribute(controlProperties, "listFillRange", NormalizeOptionalText);
            changed |= NormalizeAttribute(controlProperties, "cf", NormalizeOptionalText);

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

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                attribute.Name == RelNs + "id" ||
                (attribute.Name.NamespaceName.Length == 0 && allowedNames.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
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

    private static bool NormalizeAttribute(
        XElement element,
        string attributeName,
        Func<string?, string?> normalize)
    {
        var attribute = element.Attribute(attributeName);
        var normalized = normalize(attribute?.Value);
        if (normalized is null)
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        if (attribute is not null && string.Equals(attribute.Value, normalized, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, normalized);
        return true;
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
        NormalizeAttribute(element, attributeName, NormalizeBoolean);

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
