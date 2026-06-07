using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetScenarioNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly IReadOnlySet<string> ScenarioContainerAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "current", "show", "sqref" };

    private static readonly IReadOnlySet<string> ScenarioAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "name", "locked", "hidden", "count", "user", "comment" };

    private static readonly IReadOnlySet<string> InputCellAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "r", "deleted", "undone", "val", "numFmtId" };

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var scenarioContainers = worksheetRoot.Elements(WorksheetNs + "scenarios").ToList();
        if (scenarioContainers.Count == 0)
            return false;

        var changed = false;
        var scenarios = scenarioContainers[0];
        foreach (var duplicate in scenarioContainers.Skip(1))
        {
            scenarios.Add(duplicate.Elements(WorksheetNs + "scenario").Select(scenario => new XElement(scenario)));
            duplicate.Remove();
            changed = true;
        }

        changed |= NormalizeScenariosElement(scenarios);
        if (!scenarios.Elements(WorksheetNs + "scenario").Any())
        {
            scenarios.Remove();
            changed = true;
        }

        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static bool NormalizeScenariosElement(XElement scenarios)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(scenarios, ScenarioContainerAttributes);
        changed |= NormalizeAttribute(scenarios, "current", NormalizeUnsignedIntOrNull);
        changed |= NormalizeAttribute(scenarios, "show", NormalizeUnsignedIntOrNull);
        changed |= NormalizeAttribute(scenarios, "sqref", NormalizeSqref);
        changed |= RemoveUnexpectedChildren(scenarios, WorksheetNs + "scenario");

        foreach (var scenario in scenarios.Elements(WorksheetNs + "scenario").ToList())
        {
            changed |= NormalizeScenarioElement(scenario);
            if (!ShouldRemoveScenarioElement(scenario))
                continue;

            scenario.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeScenarioElement(XElement scenario)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(scenario, ScenarioAttributes);
        changed |= NormalizeAttribute(scenario, "name", NormalizeRequiredText);
        changed |= NormalizeAttribute(scenario, "locked", NormalizeBooleanOrNull);
        changed |= NormalizeAttribute(scenario, "hidden", NormalizeBooleanOrNull);
        changed |= NormalizeAttribute(scenario, "user", NormalizeOptionalText);
        changed |= NormalizeAttribute(scenario, "comment", NormalizeOptionalText);
        changed |= RemoveUnexpectedChildren(scenario, WorksheetNs + "inputCells");

        foreach (var inputCell in scenario.Elements(WorksheetNs + "inputCells").ToList())
        {
            changed |= NormalizeInputCellElement(inputCell);
            if (!ShouldRemoveInputCellElement(inputCell))
                continue;

            inputCell.Remove();
            changed = true;
        }

        var inputCellCount = scenario.Elements(WorksheetNs + "inputCells").Count();
        changed |= SetAttributeIfChanged(
            scenario,
            "count",
            inputCellCount.ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    private static bool NormalizeInputCellElement(XElement inputCell)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(inputCell, InputCellAttributes);
        changed |= NormalizeAttribute(inputCell, "r", NormalizeCellReference);
        changed |= NormalizeAttribute(inputCell, "deleted", NormalizeBooleanOrNull);
        changed |= NormalizeAttribute(inputCell, "undone", NormalizeBooleanOrNull);
        changed |= NormalizeAttribute(inputCell, "numFmtId", NormalizeUnsignedIntOrNull);
        changed |= RemoveAllNodes(inputCell);
        return changed;
    }

    private static bool ShouldRemoveScenarioElement(XElement scenario) =>
        string.IsNullOrWhiteSpace(scenario.Attribute("name")?.Value) ||
        !scenario.Elements(WorksheetNs + "inputCells").Any();

    private static bool ShouldRemoveInputCellElement(XElement inputCell) =>
        string.IsNullOrWhiteSpace(inputCell.Attribute("r")?.Value) ||
        inputCell.Attribute("val") is null;

    private static bool RemoveUnexpectedChildren(XElement element, XName allowedChildName)
    {
        var changed = false;
        foreach (var child in element.Elements().ToList())
        {
            if (child.Name == allowedChildName)
                continue;

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && allowedNames.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

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

    private static bool SetAttributeIfChanged(XElement element, string attributeName, string value)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is not null && string.Equals(attribute.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, value);
        return true;
    }

    private static bool RemoveAllNodes(XElement element)
    {
        if (!element.Nodes().Any())
            return false;

        element.RemoveNodes();
        return true;
    }

    private static string? NormalizeRequiredText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeBooleanOrNull(string? value)
    {
        var trimmed = value?.Trim();
        if (string.Equals(trimmed, "1", StringComparison.Ordinal) ||
            string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
        {
            return "1";
        }

        if (string.Equals(trimmed, "0", StringComparison.Ordinal) ||
            string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
        {
            return "0";
        }

        return null;
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeCellReference(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && CellAddress.TryParse(trimmed, SheetId.New(), out var address)
            ? address.ToA1()
            : null;
    }

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

        return normalizedTokens.Count == 0 ? null : string.Join(' ', normalizedTokens);
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

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
