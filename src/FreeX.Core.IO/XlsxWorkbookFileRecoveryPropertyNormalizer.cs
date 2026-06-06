using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookFileRecoveryPropertyNormalizer
{
    private static readonly string[] BooleanAttributes =
    [
        "autoRecover",
        "crashSave",
        "dataExtractLoad",
        "repairLoad"
    ];

    public static bool NormalizeElement(XElement fileRecoveryPr)
    {
        var changed = false;
        foreach (var attributeName in BooleanAttributes)
            changed |= NormalizeAttribute(fileRecoveryPr, attributeName, NormalizeBoolean);

        return changed;
    }

    private static bool NormalizeAttribute(
        XElement element,
        string attributeName,
        Func<string?, string?> normalize)
    {
        var attribute = element.Attribute(attributeName);
        var normalized = normalize(attribute?.Value);
        if (normalized is null)
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        if (attribute is not null && string.Equals(attribute.Value, normalized, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, normalized);
        return true;
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
