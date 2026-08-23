using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetScenarioMapper
{
    public static IReadOnlyList<WorkbookScenario> Read(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var scenarios = new List<WorkbookScenario>();
        var tempSheet = SheetId.New();
        foreach (var scenario in worksheetXml.Root?
                     .Element(worksheetNs + "scenarios")?
                     .Elements(worksheetNs + "scenario") ?? [])
        {
            var name = scenario.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var changes = new List<ScenarioCellValue>();
            var supported = true;
            foreach (var inputCell in scenario.Elements(worksheetNs + "inputCells"))
            {
                var reference = inputCell.Attribute("r")?.Value;
                var rawValue = inputCell.Attribute("val")?.Value;
                if (string.IsNullOrWhiteSpace(reference) ||
                    rawValue is null ||
                    !CellAddress.TryParse(reference, tempSheet, out var address))
                {
                    supported = false;
                    break;
                }

                changes.Add(new ScenarioCellValue(address, ParseValue(rawValue)));
            }

            if (supported && changes.Count > 0)
                scenarios.Add(new WorkbookScenario(
                    name,
                    changes,
                    NullIfWhiteSpace(scenario.Attribute("comment")?.Value),
                    XlsxWorksheetXmlValueParser.IsTruthy(scenario.Attribute("hidden")?.Value),
                    XlsxWorksheetXmlValueParser.IsTruthy(scenario.Attribute("locked")?.Value),
                    NullIfWhiteSpace(scenario.Attribute("user")?.Value)));
        }

        return scenarios;
    }

    public static void Save(Stream packageStream, Workbook workbook)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XlsxWorksheetPackageEditTraversal.Edit(packageStream, workbook, (session, sheet, edit) =>
        {
            var scenariosForSheet = workbook.Scenarios
                .Select(scenario => new
                {
                    Scenario = scenario,
                    Changes = scenario.ChangingCells
                        .Where(change => change.Address.Sheet == sheet.Id && IsSupportedValue(change.Value))
                        .GroupBy(change => change.Address)
                        .Select(group => group.Last())
                        .OrderBy(change => change.Address.Row)
                        .ThenBy(change => change.Address.Col)
                        .ToList()
                })
                .Where(item => item.Changes.Count > 0)
                .ToList();
            if (scenariosForSheet.Count == 0)
                return;

            var root = edit.Root;
            root.Element(workbookNs + "scenarios")?.Remove();
            XlsxWorksheetElementOrder.Insert(root, new XElement(
                workbookNs + "scenarios",
                scenariosForSheet.Select(item =>
                {
                    var scenario = new XElement(
                        workbookNs + "scenario",
                        new XAttribute("name", item.Scenario.Name),
                        new XAttribute("count", item.Changes.Count.ToString(CultureInfo.InvariantCulture)),
                        item.Changes.Select(change => new XElement(
                            workbookNs + "inputCells",
                            new XAttribute("r", change.Address.ToA1()),
                            new XAttribute("val", FormatValue(change.Value)))));
                    if (!string.IsNullOrWhiteSpace(item.Scenario.Comment))
                        scenario.SetAttributeValue("comment", item.Scenario.Comment);
                    if (item.Scenario.Hidden)
                        scenario.SetAttributeValue("hidden", "1");
                    if (item.Scenario.Locked)
                        scenario.SetAttributeValue("locked", "1");
                    if (!string.IsNullOrWhiteSpace(item.Scenario.User))
                        scenario.SetAttributeValue("user", item.Scenario.User);

                    return scenario;
                })));

            session.MarkDirty(edit);
        });
    }

    public static HashSet<string> GetModeledNamesForSheet(Workbook workbook, string sheetName)
    {
        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return workbook.Scenarios
            .Where(scenario => scenario.ChangingCells.Any(change =>
                change.Address.Sheet == sheet.Id &&
                IsSupportedValue(change.Value)))
            .Select(scenario => scenario.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsSupportedValue(ScalarValue value) => value switch
    {
        NumberValue number => double.IsFinite(number.Value),
        DateTimeValue dateTime => double.IsFinite(dateTime.Value),
        TextValue or BoolValue or ErrorValue or BlankValue => true,
        _ => false
    };

    private static ScalarValue ParseValue(string rawValue)
    {
        if (rawValue.Length == 0)
            return BlankValue.Instance;
        if (string.Equals(rawValue, "TRUE", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(true);
        if (string.Equals(rawValue, "FALSE", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(false);
        if (rawValue.StartsWith('#'))
            return rawValue.ToUpperInvariant() switch
            {
                "#DIV/0!" => ErrorValue.DivByZero,
                "#VALUE!" => ErrorValue.Value,
                "#REF!" => ErrorValue.Ref,
                "#NAME?" => ErrorValue.Name,
                "#NULL!" => ErrorValue.Null,
                "#N/A" => ErrorValue.NA,
                "#NUM!" => ErrorValue.Num,
                _ => new ErrorValue(rawValue)
            };
        if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return new NumberValue(number);

        return new TextValue(rawValue);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FormatValue(ScalarValue value) => value switch
    {
        NumberValue number => XlsxNumberFormatting.ToXmlString(number.Value),
        DateTimeValue dateTime => XlsxNumberFormatting.ToXmlString(dateTime.Value),
        TextValue text => text.Value,
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        BlankValue => string.Empty,
        _ => string.Empty
    };

}
