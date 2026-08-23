using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookWebPublishObjectsNormalizer
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly HashSet<string> WebPublishObjectsAttributes =
    [
        "count"
    ];

    private static readonly HashSet<string> WebPublishObjectAttributes =
    [
        "id",
        "divId",
        "sourceObject",
        "destinationFile",
        "title",
        "autoRepublish"
    ];

    private static readonly string[] TextAttributes =
    [
        "divId",
        "sourceObject",
        "destinationFile",
        "title"
    ];

    public static bool NormalizeWorkbookRoot(XElement workbookRoot, XNamespace workbookNs)
    {
        var changed = false;
        var keptWebPublishObjects = false;
        foreach (var webPublishObjects in workbookRoot.Elements(workbookNs + "webPublishObjects").ToList())
        {
            if (keptWebPublishObjects)
            {
                webPublishObjects.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeWebPublishObjectsElement(webPublishObjects);
            if (ShouldRemoveWebPublishObjectsElement(webPublishObjects))
            {
                webPublishObjects.Remove();
                changed = true;
                continue;
            }

            keptWebPublishObjects = true;
        }

        return changed;
    }

    public static bool NormalizeWebPublishObjectsElement(XElement webPublishObjects)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(webPublishObjects, WebPublishObjectsAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(webPublishObjects, WorkbookNs + "webPublishObject");

        foreach (var webPublishObject in webPublishObjects.Elements(WorkbookNs + "webPublishObject").ToList())
        {
            changed |= NormalizeWebPublishObjectElement(webPublishObject);
            if (!ShouldRemoveWebPublishObjectElement(webPublishObject))
                continue;

            webPublishObject.Remove();
            changed = true;
        }

        changed |= NormalizeCount(webPublishObjects);
        return changed;
    }

    public static bool ShouldRemoveWebPublishObjectsElement(XElement webPublishObjects) =>
        !webPublishObjects.Elements(WorkbookNs + "webPublishObject").Any();

    private static bool NormalizeWebPublishObjectElement(XElement webPublishObject)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(webPublishObject, WebPublishObjectAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(webPublishObject);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(webPublishObject, "id", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(webPublishObject, "autoRepublish", XlsxXmlNormalizationHelpers.NormalizeBoolean);

        foreach (var attributeName in TextAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(webPublishObject, attributeName, XlsxXmlNormalizationHelpers.NormalizeOptionalText);

        return changed;
    }

    private static bool ShouldRemoveWebPublishObjectElement(XElement webPublishObject) =>
        webPublishObject.Attribute("id") is null ||
        string.IsNullOrWhiteSpace(webPublishObject.Attribute("divId")?.Value) ||
        string.IsNullOrWhiteSpace(webPublishObject.Attribute("sourceObject")?.Value) ||
        string.IsNullOrWhiteSpace(webPublishObject.Attribute("destinationFile")?.Value);

    private static bool NormalizeCount(XElement webPublishObjects)
    {
        var count = webPublishObjects.Elements(WorkbookNs + "webPublishObject").Count().ToString(CultureInfo.InvariantCulture);
        return XlsxXmlNormalizationHelpers.SetAttributeIfChanged(webPublishObjects, "count", count);
    }

}
