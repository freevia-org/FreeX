using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Normalizer for workbook.xml <c>webPublishing</c>. Attribute behavior is declared in
/// <see cref="XlsxWorkbookLeafElementSchemas"/>; this class provides the container loop
/// (keep first, remove duplicates) and delegates element normalization to the schema table.
/// </summary>
internal static class XlsxWorkbookWebPublishingNormalizer
{
    private static readonly XlsxWorkbookLeafElementSchema Schema =
        XlsxWorkbookLeafElementSchemas.ByLocalName["webPublishing"];

    public static bool NormalizeWorkbookRoot(XElement workbookRoot, XNamespace workbookNs)
    {
        var changed = false;
        var keptWebPublishing = false;
        foreach (var webPublishing in workbookRoot.Elements(workbookNs + "webPublishing").ToList())
        {
            if (keptWebPublishing)
            {
                webPublishing.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeElement(webPublishing);
            keptWebPublishing = true;
        }

        return changed;
    }

    public static bool NormalizeElement(XElement webPublishing) =>
        XlsxWorkbookLeafElementNormalizer.Normalize(webPublishing, Schema);
}
