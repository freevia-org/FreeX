using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Normalizer for workbook.xml <c>workbookPr</c>. Behavior is declared in
/// <see cref="XlsxWorkbookLeafElementSchemas"/>; this class is a thin dispatch shim.
/// </summary>
internal static class XlsxWorkbookPropertiesNormalizer
{
    private static readonly XlsxWorkbookLeafElementSchema Schema =
        XlsxWorkbookLeafElementSchemas.ByLocalName["workbookPr"];

    public static bool NormalizeElement(XElement workbookPr) =>
        XlsxWorkbookLeafElementNormalizer.Normalize(workbookPr, Schema);
}
