using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Normalizer for workbook.xml <c>workbookProtection</c>. Behavior is declared in
/// <see cref="XlsxWorkbookLeafElementSchemas"/>; this class is a thin dispatch shim.
/// </summary>
internal static class XlsxWorkbookProtectionNormalizer
{
    private static readonly XlsxWorkbookLeafElementSchema Schema =
        XlsxWorkbookLeafElementSchemas.ByLocalName["workbookProtection"];

    public static bool NormalizeElement(XElement workbookProtection) =>
        XlsxWorkbookLeafElementNormalizer.Normalize(workbookProtection, Schema);
}
