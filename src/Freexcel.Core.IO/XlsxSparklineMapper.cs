using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxSparklineMapper
{
    public static IReadOnlyList<SparklineModel> Read(XDocument worksheetXml)
    {
        var result = new List<SparklineModel>();
        var tempSheet = SheetId.New();
        foreach (var group in worksheetXml.Descendants().Where(element =>
                     string.Equals(element.Name.LocalName, "sparklineGroup", StringComparison.OrdinalIgnoreCase)))
        {
            var kind = group.Attribute("type")?.Value switch
            {
                "column" => SparklineKind.Column,
                "stacked" or "winLoss" => SparklineKind.WinLoss,
                _ => SparklineKind.Line
            };

            foreach (var sparkline in group.Descendants().Where(element =>
                         string.Equals(element.Name.LocalName, "sparkline", StringComparison.OrdinalIgnoreCase)))
            {
                var formula = sparkline.Elements().FirstOrDefault(element =>
                    string.Equals(element.Name.LocalName, "f", StringComparison.OrdinalIgnoreCase))?.Value;
                var location = sparkline.Elements().FirstOrDefault(element =>
                    string.Equals(element.Name.LocalName, "sqref", StringComparison.OrdinalIgnoreCase))?.Value;
                if (string.IsNullOrWhiteSpace(formula) || string.IsNullOrWhiteSpace(location))
                    continue;

                var bang = formula.LastIndexOf('!');
                var rangeText = bang >= 0 ? formula[(bang + 1)..] : formula;
                rangeText = rangeText.Replace("$", "", StringComparison.Ordinal);
                location = location.Replace("$", "", StringComparison.Ordinal);
                try
                {
                    result.Add(new SparklineModel
                    {
                        DataRange = GridRange.Parse(rangeText, tempSheet),
                        Location = CellAddress.Parse(location, tempSheet),
                        Kind = kind
                    });
                }
                catch
                {
                    // Skip malformed sparkline references.
                }
            }
        }

        return result;
    }

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        XNamespace xmNs = "http://schemas.microsoft.com/office/excel/2006/main";

        var relTargets = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => XlsxPackagePath.NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                sheet.Sparklines.Count == 0 ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
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

            root.Elements(workbookNs + "extLst").Remove();
            root.SetAttributeValue(XNamespace.Xmlns + "x14", x14Ns.NamespaceName);
            root.SetAttributeValue(XNamespace.Xmlns + "xm", xmNs.NamespaceName);
            root.Add(new XElement(
                workbookNs + "extLst",
                new XElement(
                    workbookNs + "ext",
                    new XAttribute("uri", "{05C60535-1F16-4fd2-B633-F4F36F0B64E0}"),
                    new XElement(
                        x14Ns + "sparklineGroups",
                        sheet.Sparklines
                            .Where(sparkline =>
                                sparkline.DataRange.Start.Sheet == sheet.Id &&
                                sparkline.DataRange.End.Sheet == sheet.Id &&
                                sparkline.Location.Sheet == sheet.Id &&
                                Enum.IsDefined(sparkline.Kind))
                            .GroupBy(sparkline => sparkline.Kind)
                            .Select(group => ToSparklineGroupXml(sheet, group.Key, group, x14Ns, xmNs))))));
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static XElement ToSparklineGroupXml(
        Sheet sheet,
        SparklineKind kind,
        IEnumerable<SparklineModel> sparklines,
        XNamespace x14Ns,
        XNamespace xmNs) =>
        new(x14Ns + "sparklineGroup",
            new XAttribute("type", ToSparklineType(kind)),
            new XElement(
                x14Ns + "sparklines",
                sparklines.Select(sparkline => new XElement(
                    x14Ns + "sparkline",
                    new XElement(xmNs + "f", $"{QuoteSheetName(sheet.Name)}!{sparkline.DataRange}"),
                    new XElement(xmNs + "sqref", sparkline.Location.ToA1())))));

    private static string ToSparklineType(SparklineKind kind) =>
        kind switch
        {
            SparklineKind.Column => "column",
            SparklineKind.WinLoss => "stacked",
            _ => "line"
        };

    private static string QuoteSheetName(string sheetName) =>
        sheetName.Any(ch => char.IsWhiteSpace(ch) || ch == '\'')
            ? $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'"
            : sheetName;
}
