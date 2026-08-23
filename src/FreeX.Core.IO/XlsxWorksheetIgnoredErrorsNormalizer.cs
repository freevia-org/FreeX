using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetIgnoredErrorsNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly IReadOnlySet<string> EmptyAttributes = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> IgnoredErrorAttributes = new HashSet<string>(StringComparer.Ordinal)
    {
        "sqref",
        "numberStoredAsText",
        "evalError",
        "formula",
        "formulaRange",
        "unlockedFormula",
        "emptyCellReference",
        "listDataValidation",
        "calculatedColumn",
        "twoDigitTextYear"
    };

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var ignoredErrorContainers = worksheetRoot.Elements(WorksheetNs + "ignoredErrors").ToList();
        if (ignoredErrorContainers.Count == 0)
            return false;

        var changed = false;
        var ignoredErrors = ignoredErrorContainers[0];
        foreach (var duplicate in ignoredErrorContainers.Skip(1))
        {
            ignoredErrors.Add(duplicate.Elements(WorksheetNs + "ignoredError").Select(error => new XElement(error)));
            duplicate.Remove();
            changed = true;
        }

        changed |= NormalizeElement(ignoredErrors);
        if (!ignoredErrors.Elements(WorksheetNs + "ignoredError").Any())
        {
            ignoredErrors.Remove();
            changed = true;
        }

        return changed;
    }

    public static bool NormalizeElement(XElement ignoredErrors)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(ignoredErrors, EmptyAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(ignoredErrors, WorksheetNs + "ignoredError");

        var seenSqrefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ignoredError in ignoredErrors.Elements(WorksheetNs + "ignoredError").ToList())
        {
            var normalizedSqref = NormalizeSqref(ignoredError.Attribute("sqref")?.Value);
            if (normalizedSqref is null)
            {
                ignoredError.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(ignoredError, IgnoredErrorAttributes);
            changed |= NormalizeBooleanAttributes(ignoredError);
            if (!HasTruthyFlag(ignoredError) || !seenSqrefs.Add(normalizedSqref))
            {
                ignoredError.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(ignoredError, "sqref", normalizedSqref);
            changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(ignoredError);
        }

        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static bool NormalizeBooleanAttributes(XElement element)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.Name.NamespaceName.Length != 0 ||
                !IgnoredErrorAttributes.Contains(attribute.Name.LocalName) ||
                string.Equals(attribute.Name.LocalName, "sqref", StringComparison.Ordinal))
            {
                continue;
            }

            var normalized = XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric(attribute.Value);
            if (normalized is null)
            {
                attribute.Remove();
                changed = true;
                continue;
            }

            if (!string.Equals(attribute.Value, normalized, StringComparison.Ordinal))
            {
                attribute.Value = normalized;
                changed = true;
            }
        }

        return changed;
    }

    private static bool HasTruthyFlag(XElement ignoredError) =>
        IgnoredErrorAttributes
            .Where(attributeName => !string.Equals(attributeName, "sqref", StringComparison.Ordinal))
            .Any(attributeName => string.Equals(ignoredError.Attribute(attributeName)?.Value, "1", StringComparison.Ordinal));

    private static string? NormalizeSqref(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var seenTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedTokens = new List<string>();
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = NormalizeSqrefToken(token);
            if (normalized is null || !seenTokens.Add(normalized))
                continue;

            normalizedTokens.Add(normalized);
        }

        return normalizedTokens.Count == 0
            ? null
            : string.Join(' ', normalizedTokens);
    }

    private static string? NormalizeSqrefToken(string token)
    {
        var parts = token.Split(':');
        var sheet = SheetId.New();
        if (parts.Length == 1)
        {
            return CellAddress.TryParse(parts[0], sheet, out var address)
                ? address.ToA1()
                : null;
        }

        if (parts.Length == 2 &&
            CellAddress.TryParse(parts[0], sheet, out var start) &&
            CellAddress.TryParse(parts[1], sheet, out var end))
        {
            var range = new GridRange(start, end);
            return range.Start == range.End
                ? range.Start.ToA1()
                : $"{range.Start.ToA1()}:{range.End.ToA1()}";
        }

        return null;
    }

}
