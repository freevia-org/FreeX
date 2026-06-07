using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookFileSharingNormalizer
{
    private static readonly HashSet<string> FileSharingAttributes =
    [
        "readOnlyRecommended",
        "userName",
        "reservationPassword",
        "algorithmName",
        "hashValue",
        "saltValue",
        "spinCount"
    ];

    public static bool NormalizeElement(XElement fileSharing)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(fileSharing, FileSharingAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(fileSharing);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(fileSharing, "readOnlyRecommended", XlsxXmlNormalizationHelpers.NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(fileSharing, "reservationPassword", NormalizeLegacyPasswordHashOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(fileSharing, "hashValue", NormalizeBase64BinaryOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(fileSharing, "saltValue", NormalizeBase64BinaryOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(fileSharing, "spinCount", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        return changed;
    }

    private static string? NormalizeBase64BinaryOrNull(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        try
        {
            _ = Convert.FromBase64String(trimmed);
            return trimmed;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? NormalizeLegacyPasswordHashOrNull(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed is not { Length: 4 } ||
            !trimmed.All(static c => char.IsAsciiHexDigit(c)))
        {
            return null;
        }

        return trimmed.ToUpperInvariant();
    }
}
