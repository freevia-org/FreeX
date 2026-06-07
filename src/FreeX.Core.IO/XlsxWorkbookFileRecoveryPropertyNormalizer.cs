using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookFileRecoveryPropertyNormalizer
{
    private static readonly HashSet<string> BooleanAttributes =
    [
        "autoRecover",
        "crashSave",
        "dataExtractLoad",
        "repairLoad"
    ];

    public static bool NormalizeElement(XElement fileRecoveryPr)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(fileRecoveryPr, BooleanAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(fileRecoveryPr);
        foreach (var attributeName in BooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(fileRecoveryPr, attributeName, XlsxXmlNormalizationHelpers.NormalizeBoolean);

        return changed;
    }
}
