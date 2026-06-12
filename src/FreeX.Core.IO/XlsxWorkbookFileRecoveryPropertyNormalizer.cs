using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Normalizer for workbook.xml <c>fileRecoveryPr</c>. Behavior is declared in
/// <see cref="XlsxWorkbookLeafElementSchemas"/>; this class is a thin dispatch shim.
/// </summary>
internal static class XlsxWorkbookFileRecoveryPropertyNormalizer
{
    private static readonly XlsxWorkbookLeafElementSchema Schema =
        XlsxWorkbookLeafElementSchemas.ByLocalName["fileRecoveryPr"];

    public static bool NormalizeElement(XElement fileRecoveryPr) =>
        XlsxWorkbookLeafElementNormalizer.Normalize(fileRecoveryPr, Schema);
}
