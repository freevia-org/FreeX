using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookProtectionNormalizer
{
    private static readonly HashSet<string> WorkbookProtectionAttributes =
    [
        "workbookPassword",
        "revisionsPassword",
        "lockStructure",
        "lockWindows",
        "lockRevision",
        "revisionsAlgorithmName",
        "revisionsHashValue",
        "revisionsSaltValue",
        "revisionsSpinCount",
        "workbookAlgorithmName",
        "workbookHashValue",
        "workbookSaltValue",
        "workbookSpinCount"
    ];

    private static readonly string[] BooleanAttributes =
    [
        "lockStructure",
        "lockWindows",
        "lockRevision"
    ];

    private static readonly string[] UnsignedIntAttributes =
    [
        "workbookSpinCount",
        "revisionsSpinCount"
    ];

    public static bool NormalizeElement(XElement workbookProtection)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(workbookProtection, WorkbookProtectionAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(workbookProtection);

        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookProtection, "workbookPassword", NormalizeLegacyPasswordHashOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookProtection, "revisionsPassword", NormalizeLegacyPasswordHashOrNull);
        foreach (var attributeName in BooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookProtection, attributeName, NormalizeBoolean);
        foreach (var attributeName in UnsignedIntAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookProtection, attributeName, NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookProtection, "revisionsHashValue", NormalizeBase64BinaryOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookProtection, "revisionsSaltValue", NormalizeBase64BinaryOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookProtection, "workbookHashValue", NormalizeBase64BinaryOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookProtection, "workbookSaltValue", NormalizeBase64BinaryOrNull);

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

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
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
