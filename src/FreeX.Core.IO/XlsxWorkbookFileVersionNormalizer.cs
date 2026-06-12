using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Normalizer for workbook.xml <c>fileVersion</c>. Behavior is declared in
/// <see cref="XlsxWorkbookLeafElementSchemas"/>; this class is a thin dispatch shim.
/// </summary>
internal static class XlsxWorkbookFileVersionNormalizer
{
    private static readonly XlsxWorkbookLeafElementSchema Schema =
        XlsxWorkbookLeafElementSchemas.ByLocalName["fileVersion"];

    public static bool NormalizeElement(XElement fileVersion) =>
        XlsxWorkbookLeafElementNormalizer.Normalize(fileVersion, Schema);
}
