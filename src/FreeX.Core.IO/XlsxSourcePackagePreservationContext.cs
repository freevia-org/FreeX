using System.IO.Compression;
using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal sealed class XlsxSourcePackagePreservationContext
{
    private readonly Dictionary<string, XDocument> _sourceWorksheetXmlByPath = new(StringComparer.OrdinalIgnoreCase);

    private XlsxSourcePackagePreservationContext(
        XDocument sourceWorkbookXml,
        XDocument targetWorkbookXml,
        IReadOnlyDictionary<string, string> sourceWorkbookRels,
        IReadOnlyDictionary<string, string> targetWorkbookRels,
        IReadOnlyDictionary<string, string> sourceSheets,
        IReadOnlyDictionary<string, string> targetSheets,
        IReadOnlyDictionary<string, string>? verifiedCurrentNameByLoadTimeName)
    {
        SourceWorkbookXml = sourceWorkbookXml;
        TargetWorkbookXml = targetWorkbookXml;
        SourceWorkbookRels = sourceWorkbookRels;
        TargetWorkbookRels = targetWorkbookRels;
        SourceSheets = sourceSheets;
        TargetSheets = targetSheets;
        VerifiedCurrentNameByLoadTimeName = verifiedCurrentNameByLoadTimeName;
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

    // R103-io-rename-name-reuse-identity-gap: maps each LOAD-TIME sheet name to its CURRENT name,
    // verified via the sheet's rename-stable Sheet.Id (not by name string alone) -- built only when the
    // caller supplied both the live Workbook and its load-time Sheet.Id-by-position snapshot. A load-time
    // name is present here ONLY when the physical sheet that originally held it can still be found (by
    // Id) in the live workbook; a name whose original sheet was deleted (even if some OTHER sheet has
    // since been renamed to reuse that exact string) is deliberately ABSENT, so callers must not treat a
    // missing entry as "unchanged" -- see XlsxRenamedSourceSheetResolver, the sole consumer, for why.
    // Null when the caller didn't supply identity data (keeps this a purely additive addition for any
    // future/legacy call site that doesn't have a Workbook + snapshot handy).
    public IReadOnlyDictionary<string, string>? VerifiedCurrentNameByLoadTimeName { get; }

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

    public static XlsxSourcePackagePreservationContext? TryCreate(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        Workbook? workbook = null,
        IReadOnlyList<SheetId>? sourceSheetIdsByLocalId = null)
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

        var verifiedCurrentNameByLoadTimeName = workbook is null || sourceSheetIdsByLocalId is null || sourceSheetIdsByLocalId.Count == 0
            ? null
            : ResolveVerifiedCurrentNameByLoadTimeName(sourceWorkbookXml, workbookNs, workbook, sourceSheetIdsByLocalId);

        return new XlsxSourcePackagePreservationContext(
            sourceWorkbookXml,
            targetWorkbookXml,
            sourceWorkbookRels,
            targetWorkbookRels,
            sourceSheets,
            targetSheets,
            verifiedCurrentNameByLoadTimeName);
    }

    // R103-io-rename-name-reuse-identity-gap: mirrors XlsxFileAdapter.SourcePackage.cs's
    // ResolveCurrentSheetNamesByLoadTimeName (the fix already proven for
    // GetExcludedWorksheetPackagePartPaths) -- sourceSheetIdsByLocalId[localId] is the rename-stable
    // Sheet.Id the sheet at that <sheet> position had AT LOAD TIME; looking that Id up in the LIVE
    // workbook recovers the sheet's CURRENT name if it still exists, regardless of any rename(s) since,
    // and correctly omits it if the sheet has since been deleted -- even if some OTHER sheet has been
    // renamed to reuse its old name string.
    private static Dictionary<string, string> ResolveVerifiedCurrentNameByLoadTimeName(
        XDocument sourceWorkbookXml,
        XNamespace workbookNs,
        Workbook workbook,
        IReadOnlyList<SheetId> sourceSheetIdsByLocalId)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sheetElements = sourceWorkbookXml.Root?
            .Element(workbookNs + "sheets")?
            .Elements(workbookNs + "sheet")
            .ToList()
            ?? [];

        for (var localId = 0; localId < sheetElements.Count && localId < sourceSheetIdsByLocalId.Count; localId++)
        {
            var loadTimeName = sheetElements[localId].Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(loadTimeName))
                continue;

            var currentSheet = workbook.GetSheet(sourceSheetIdsByLocalId[localId]);
            if (currentSheet is not null)
                map[loadTimeName] = currentSheet.Name;
        }

        return map;
    }
}
