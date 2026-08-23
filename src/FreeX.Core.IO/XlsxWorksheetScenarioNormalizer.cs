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

    private static bool NormalizeScenariosElement(XElement scenarios)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(scenarios, ScenarioContainerAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(scenarios, "current", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(scenarios, "show", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(scenarios, "sqref", NormalizeSqref);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(scenarios, WorksheetNs + "scenario");

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
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(scenario, ScenarioAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(scenario, "name", NormalizeRequiredText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(scenario, "locked", XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(scenario, "hidden", XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(scenario, "user", NormalizeOptionalText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(scenario, "comment", NormalizeOptionalText);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(scenario, WorksheetNs + "inputCells");

        foreach (var inputCell in scenario.Elements(WorksheetNs + "inputCells").ToList())
        {
            changed |= NormalizeInputCellElement(inputCell);
            if (!ShouldRemoveInputCellElement(inputCell))
                continue;

            inputCell.Remove();
            changed = true;
        }

        var inputCellCount = scenario.Elements(WorksheetNs + "inputCells").Count();
        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(
            scenario,
            "count",
            inputCellCount.ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    private static bool NormalizeInputCellElement(XElement inputCell)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(inputCell, InputCellAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(inputCell, "r", NormalizeCellReference);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(inputCell, "deleted", XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(inputCell, "undone", XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(inputCell, "numFmtId", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(inputCell);
        return changed;
    }

    private static bool ShouldRemoveScenarioElement(XElement scenario) =>
        string.IsNullOrWhiteSpace(scenario.Attribute("name")?.Value) ||
        !scenario.Elements(WorksheetNs + "inputCells").Any();

    private static bool ShouldRemoveInputCellElement(XElement inputCell) =>
        string.IsNullOrWhiteSpace(inputCell.Attribute("r")?.Value) ||
        inputCell.Attribute("val") is null;

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

}
