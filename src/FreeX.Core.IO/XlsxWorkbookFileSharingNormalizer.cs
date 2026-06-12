using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Normalizer for workbook.xml <c>fileSharing</c>. Behavior is declared in
/// <see cref="XlsxWorkbookLeafElementSchemas"/>; this class is a thin dispatch shim.
/// </summary>
internal static class XlsxWorkbookFileSharingNormalizer
{
    private static readonly XlsxWorkbookLeafElementSchema Schema =
        XlsxWorkbookLeafElementSchemas.ByLocalName["fileSharing"];

    public static bool NormalizeElement(XElement fileSharing) =>
        XlsxWorkbookLeafElementNormalizer.Normalize(fileSharing, Schema);
}
