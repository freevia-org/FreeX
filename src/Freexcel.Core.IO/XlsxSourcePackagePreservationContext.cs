using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal sealed class XlsxSourcePackagePreservationContext
{
    private readonly Dictionary<string, XDocument> _sourceWorksheetXmlByPath = new(StringComparer.OrdinalIgnoreCase);

    private XlsxSourcePackagePreservationContext(
        XDocument sourceWorkbookXml,
        XDocument targetWorkbookXml,
        IReadOnlyDictionary<string, string> sourceWorkbookRels,
        IReadOnlyDictionary<string, string> targetWorkbookRels,
        IReadOnlyDictionary<string, string> sourceSheets,
        IReadOnlyDictionary<string, string> targetSheets)
    {
        SourceWorkbookXml = sourceWorkbookXml;
        TargetWorkbookXml = targetWorkbookXml;
        SourceWorkbookRels = sourceWorkbookRels;
        TargetWorkbookRels = targetWorkbookRels;
        SourceSheets = sourceSheets;
        TargetSheets = targetSheets;
    }

    public XNamespace WorkbookNs { get; } = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public XNamespace RelNs { get; } = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public XNamespace PackageRelNs { get; } = "http://schemas.openxmlformats.org/package/2006/relationships";

    public XDocument SourceWorkbookXml { get; }
    public XDocument TargetWorkbookXml { get; }
    public IReadOnlyDictionary<string, string> SourceWorkbookRels { get; }
    public IReadOnlyDictionary<string, string> TargetWorkbookRels { get; }
    public IReadOnlyDictionary<string, string> SourceSheets { get; }
    public IReadOnlyDictionary<string, string> TargetSheets { get; }

    public XDocument? GetSourceWorksheetXml(ZipArchive sourceArchive, string worksheetPath)
    {
        if (_sourceWorksheetXmlByPath.TryGetValue(worksheetPath, out var worksheetXml))
            return worksheetXml;

        var worksheetEntry = sourceArchive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return null;

        worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        _sourceWorksheetXmlByPath[worksheetPath] = worksheetXml;
        return worksheetXml;
    }

    public static XlsxSourcePackagePreservationContext? TryCreate(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var sourceWorkbookRelsEntry = sourceArchive.GetEntry("xl/_rels/workbook.xml.rels");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookRelsEntry = targetArchive.GetEntry("xl/_rels/workbook.xml.rels");
        if (sourceWorkbookEntry is null || sourceWorkbookRelsEntry is null ||
            targetWorkbookEntry is null || targetWorkbookRelsEntry is null)
        {
            return null;
        }

        var sourceWorkbookXml = XlsxPackageXmlEditor.LoadXml(sourceWorkbookEntry);
        var targetWorkbookXml = XlsxPackageXmlEditor.LoadXml(targetWorkbookEntry);
        var sourceWorkbookRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var targetWorkbookRels = XlsxRelationshipReader.LoadTargets(
            targetArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);

        var sourceSheets = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(sourceWorkbookXml, sourceWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        var targetSheets = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(targetWorkbookXml, targetWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        return new XlsxSourcePackagePreservationContext(
            sourceWorkbookXml,
            targetWorkbookXml,
            sourceWorkbookRels,
            targetWorkbookRels,
            sourceSheets,
            targetSheets);
    }
}
