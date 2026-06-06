using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxLegacyCommentFontNormalizer
{
    public static bool SanitizeRunFontNames(XDocument commentsXml)
    {
        var root = commentsXml.Root;
        if (root is null)
            return false;

        var changed = false;
        var workbookNs = root.Name.Namespace;
        foreach (var richTextFont in root.Descendants(workbookNs + "rFont"))
            changed |= XlsxFontNameSanitizer.SanitizeValAttribute(richTextFont);

        return changed;
    }
}
