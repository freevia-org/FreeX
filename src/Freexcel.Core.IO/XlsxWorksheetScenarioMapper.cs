using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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
                scenarios.Add(new WorkbookScenario(name, changes));
        }

        return scenarios;
    }

    public static void Save(Stream packageStream, Workbook workbook)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var workbookRels = XlsxRelationshipReader.LoadTargets(
            archive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var sheetPaths = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in workbook.Sheets)
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
            if (scenariosForSheet.Count == 0 ||
                !sheetPaths.TryGetValue(sheet.Name, out var worksheetPath))
            {
                continue;
            }

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            root.Element(workbookNs + "scenarios")?.Remove();
            InsertScenariosInOrder(root, workbookNs, new XElement(
                workbookNs + "scenarios",
                scenariosForSheet.Select(item => new XElement(
                    workbookNs + "scenario",
                    new XAttribute("name", item.Scenario.Name),
                    new XAttribute("count", item.Changes.Count.ToString(CultureInfo.InvariantCulture)),
                    item.Changes.Select(change => new XElement(
                        workbookNs + "inputCells",
                        new XAttribute("r", change.Address.ToA1()),
                        new XAttribute("val", FormatValue(change.Value))))))));

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
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
        TextValue or BoolValue or ErrorValue => true,
        _ => false
    };

    private static ScalarValue ParseValue(string rawValue)
    {
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

    private static string FormatValue(ScalarValue value) => value switch
    {
        NumberValue number => number.Value.ToString("G17", CultureInfo.InvariantCulture),
        DateTimeValue dateTime => dateTime.Value.ToString("G17", CultureInfo.InvariantCulture),
        TextValue text => text.Value,
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        _ => string.Empty
    };

    private static void InsertScenariosInOrder(XElement worksheetRoot, XNamespace workbookNs, XElement scenarios)
    {
        string[] laterWorksheetElements =
        [
            "autoFilter",
            "sortState",
            "dataConsolidate",
            "customSheetViews",
            "mergeCells",
            "phoneticPr",
            "conditionalFormatting",
            "dataValidations",
            "hyperlinks",
            "printOptions",
            "pageMargins",
            "pageSetup",
            "headerFooter",
            "rowBreaks",
            "colBreaks",
            "customProperties",
            "cellWatches",
            "ignoredErrors",
            "smartTags",
            "drawing",
            "legacyDrawing",
            "legacyDrawingHF",
            "picture",
            "oleObjects",
            "controls",
            "webPublishItems",
            "tableParts",
            "extLst"
        ];

        var insertionPoint = worksheetRoot.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == workbookNs &&
                laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is null)
            worksheetRoot.Add(scenarios);
        else
            insertionPoint.AddBeforeSelf(scenarios);
    }
}
