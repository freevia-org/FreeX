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
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(fileRecoveryPr, attributeName, NormalizeBoolean);

        return changed;
    }

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" or "false" => trimmed,
            _ => null
        };
    }
}
