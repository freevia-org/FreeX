using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookFileVersionNormalizer
{
    private static readonly HashSet<string> FileVersionAttributes =
    [
        "appName",
        "lastEdited",
        "lowestEdited",
        "rupBuild",
        "codeName"
    ];

    public static bool NormalizeElement(XElement fileVersion)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(fileVersion, FileVersionAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(fileVersion);
        return changed;
    }
}
