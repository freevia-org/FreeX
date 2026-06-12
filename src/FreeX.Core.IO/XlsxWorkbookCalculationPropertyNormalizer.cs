using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Normalizer for workbook.xml <c>calcPr</c>. Behavior is declared in
/// <see cref="XlsxWorkbookLeafElementSchemas"/>; this class is a thin dispatch shim.
/// </summary>
internal static class XlsxWorkbookCalculationPropertyNormalizer
{
    private static readonly XlsxWorkbookLeafElementSchema Schema =
        XlsxWorkbookLeafElementSchemas.ByLocalName["calcPr"];

    public static bool NormalizeElement(XElement calcPr) =>
        XlsxWorkbookLeafElementNormalizer.Normalize(calcPr, Schema);
}
