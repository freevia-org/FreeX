using System.IO.Compression;
using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal sealed class XlsxSourcePackagePreservationContext
{
    private readonly Dictionary<string, XDocument> _sourceWorksheetXmlByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, XDocument> _sourceRelationshipsXmlByPart = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingSourceRelationshipsParts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _sourceRelationshipTargetsByPart =
        new(StringComparer.OrdinalIgnoreCase);

    private XlsxSourcePackagePreservationContext(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XDocument sourceWorkbookXml,
        XDocument targetWorkbookXml,
        XDocument? sourceWorkbookRelationshipsXml,
        IReadOnlyDictionary<string, string> sourceWorkbookRels,
        IReadOnlyDictionary<string, string> targetWorkbookRels,
        IReadOnlyDictionary<string, string> sourceSheets,
        IReadOnlyDictionary<string, string> targetSheets,
        IReadOnlyDictionary<string, string>? verifiedCurrentNameByLoadTimeName)
    {
        SourceArchive = sourceArchive;
        TargetArchive = targetArchive;
        SourceWorkbookXml = sourceWorkbookXml;
        TargetWorkbookXml = targetWorkbookXml;
        SourceWorkbookRelationshipsXml = sourceWorkbookRelationshipsXml;
        SourceWorkbookRels = sourceWorkbookRels;
        TargetWorkbookRels = targetWorkbookRels;
        SourceSheets = sourceSheets;
        TargetSheets = targetSheets;
        VerifiedCurrentNameByLoadTimeName = verifiedCurrentNameByLoadTimeName;

        if (sourceWorkbookRelationshipsXml is not null)
            _sourceRelationshipsXmlByPart["xl/workbook.xml"] = sourceWorkbookRelationshipsXml;
        _sourceRelationshipTargetsByPart["xl/workbook.xml"] = sourceWorkbookRels;
    }

    public XNamespace WorkbookNs { get; } = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public XNamespace RelNs { get; } = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public XNamespace PackageRelNs { get; } = "http://schemas.openxmlformats.org/package/2006/relationships";

    // The archives are borrowed from PreserveSourcePackageParts. This context never owns or disposes them.
    public ZipArchive SourceArchive { get; }
    public ZipArchive TargetArchive { get; }
    public XDocument SourceWorkbookXml { get; }
    public XDocument TargetWorkbookXml { get; private set; }
    public XDocument? SourceWorkbookRelationshipsXml { get; }
    public bool HasSourceWorkbookRelationshipsPart => SourceWorkbookRelationshipsXml is not null;
    public bool HasTargetWorkbookRelationshipsPart { get; private set; }
    public IReadOnlyDictionary<string, string> SourceWorkbookRels { get; }
    public IReadOnlyDictionary<string, string> TargetWorkbookRels { get; private set; }
    public IReadOnlyDictionary<string, string> SourceSheets { get; }
    public IReadOnlyDictionary<string, string> TargetSheets { get; private set; }

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

    public XDocument? GetSourceWorksheetXml(string worksheetPath)
    {
        if (_sourceWorksheetXmlByPath.TryGetValue(worksheetPath, out var worksheetXml))
            return worksheetXml;

        var worksheetEntry = SourceArchive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return null;

        worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        _sourceWorksheetXmlByPath[worksheetPath] = worksheetXml;
        return worksheetXml;
    }

    public XDocument? LoadTargetPartXml(string partPath)
    {
        var entry = TargetArchive.GetEntry(partPath);
        return entry is null ? null : XlsxPackageXmlEditor.LoadXml(entry);
    }

    public IReadOnlyDictionary<string, string> GetSourceRelationshipTargets(string sourcePartPath)
    {
        if (_sourceRelationshipTargetsByPart.TryGetValue(sourcePartPath, out var targets))
            return targets;

        var relationshipsXml = GetSourceRelationshipsXml(sourcePartPath);
        targets = relationshipsXml is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : XlsxRelationshipReader.ReadTargets(
                relationshipsXml,
                PackageRelNs,
                target => XlsxPackagePath.ResolveRelationshipTarget(sourcePartPath, target));
        _sourceRelationshipTargetsByPart[sourcePartPath] = targets;
        return targets;
    }

    public XDocument? GetSourceRelationshipsXml(string sourcePartPath)
    {
        if (_sourceRelationshipsXmlByPart.TryGetValue(sourcePartPath, out var relationshipsXml))
            return relationshipsXml;
        if (_missingSourceRelationshipsParts.Contains(sourcePartPath))
            return null;

        var entry = SourceArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(sourcePartPath));
        if (entry is null)
        {
            _missingSourceRelationshipsParts.Add(sourcePartPath);
            return null;
        }

        relationshipsXml = XlsxPackageXmlEditor.LoadXml(entry);
        _sourceRelationshipsXmlByPart[sourcePartPath] = relationshipsXml;
        return relationshipsXml;
    }

    public bool TryGetSourceRelationshipTarget(
        string sourcePartPath,
        string relationshipId,
        string relationshipType,
        out string targetPath)
    {
        targetPath = "";
        var relationshipsXml = GetSourceRelationshipsXml(sourcePartPath);
        var relationship = relationshipsXml?.Root?
            .Elements(PackageRelNs + "Relationship")
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal) &&
                string.Equals(candidate.Attribute("Type")?.Value, relationshipType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase));
        var target = relationship?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        targetPath = XlsxPackagePath.ResolveRelationshipTarget(sourcePartPath, target);
        return !string.IsNullOrWhiteSpace(targetPath);
    }

    public (string Path, XDocument Xml) LoadOrCreateTargetRelationships(string sourcePartPath)
    {
        var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(sourcePartPath);
        var relationshipsEntry = TargetArchive.GetEntry(relationshipsPath);
        var relationshipsXml = relationshipsEntry is null
            ? new XDocument(new XElement(PackageRelNs + "Relationships"))
            : XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        return (relationshipsPath, relationshipsXml);
    }

    public void ReplaceTargetPartXml(string partPath, XDocument document) =>
        XlsxPackageXmlEditor.ReplaceXml(TargetArchive, partPath, document);

    public XDocument LoadCurrentTargetWorkbookXml()
    {
        var entry = TargetArchive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException("The target package no longer contains xl/workbook.xml.");
        TargetWorkbookXml = XlsxPackageXmlEditor.LoadXml(entry);
        return TargetWorkbookXml;
    }

    public XDocument LoadCurrentTargetWorkbookRelationshipsXml()
    {
        var entry = TargetArchive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("The target package no longer contains xl/_rels/workbook.xml.rels.");
        return XlsxPackageXmlEditor.LoadXml(entry);
    }

    public void ReplaceTargetWorkbookXml(XDocument document, bool refreshSheetPaths = false)
    {
        ReplaceTargetPartXml("xl/workbook.xml", document);
        TargetWorkbookXml = document;
        if (refreshSheetPaths)
            RefreshTargetSheetPaths();
    }

    public void ReplaceTargetWorkbookRelationshipsXml(XDocument document, bool refreshSheetPaths = false)
    {
        ReplaceTargetPartXml("xl/_rels/workbook.xml.rels", document);
        HasTargetWorkbookRelationshipsPart = true;
        TargetWorkbookRels = XlsxRelationshipReader.ReadTargets(
            document,
            PackageRelNs,
            target => XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target));
        if (refreshSheetPaths)
            RefreshTargetSheetPaths();
    }

    public void RefreshTargetSheetPaths()
    {
        TargetWorkbookXml = LoadCurrentTargetWorkbookXml();
        TargetWorkbookRels = TargetArchive.GetEntry("xl/_rels/workbook.xml.rels") is { } relationshipsEntry
            ? XlsxRelationshipReader.ReadTargets(
                XlsxPackageXmlEditor.LoadXml(relationshipsEntry),
                PackageRelNs,
                target => XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target))
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        HasTargetWorkbookRelationshipsPart = TargetArchive.GetEntry("xl/_rels/workbook.xml.rels") is not null;
        TargetSheets = ReadSheetPaths(TargetWorkbookXml, TargetWorkbookRels, WorkbookNs, RelNs);
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
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        if (sourceWorkbookEntry is null || targetWorkbookEntry is null)
        {
            return null;
        }

        var sourceWorkbookRelsEntry = sourceArchive.GetEntry("xl/_rels/workbook.xml.rels");
        var targetWorkbookRelsEntry = targetArchive.GetEntry("xl/_rels/workbook.xml.rels");
        var sourceWorkbookXml = XlsxPackageXmlEditor.LoadXml(sourceWorkbookEntry);
        var targetWorkbookXml = XlsxPackageXmlEditor.LoadXml(targetWorkbookEntry);
        var sourceWorkbookRelationshipsXml = sourceWorkbookRelsEntry is null
            ? null
            : XlsxPackageXmlEditor.LoadXml(sourceWorkbookRelsEntry);
        var targetWorkbookRelationshipsXml = targetWorkbookRelsEntry is null
            ? null
            : XlsxPackageXmlEditor.LoadXml(targetWorkbookRelsEntry);
        var sourceWorkbookRels = sourceWorkbookRelationshipsXml is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : XlsxRelationshipReader.ReadTargets(
                sourceWorkbookRelationshipsXml,
                packageRelNs,
                target => XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target));
        var targetWorkbookRels = targetWorkbookRelationshipsXml is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : XlsxRelationshipReader.ReadTargets(
                targetWorkbookRelationshipsXml,
                packageRelNs,
                target => XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target));

        var sourceSheets = sourceWorkbookRelsEntry is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : ReadSheetPaths(sourceWorkbookXml, sourceWorkbookRels, workbookNs, relNs);
        var targetSheets = ReadSheetPaths(targetWorkbookXml, targetWorkbookRels, workbookNs, relNs);

        var verifiedCurrentNameByLoadTimeName = workbook is null || sourceSheetIdsByLocalId is null || sourceSheetIdsByLocalId.Count == 0
            ? null
            : ResolveVerifiedCurrentNameByLoadTimeName(sourceWorkbookXml, workbookNs, workbook, sourceSheetIdsByLocalId);

        var context = new XlsxSourcePackagePreservationContext(
            sourceArchive,
            targetArchive,
            sourceWorkbookXml,
            targetWorkbookXml,
            sourceWorkbookRelationshipsXml,
            sourceWorkbookRels,
            targetWorkbookRels,
            sourceSheets,
            targetSheets,
            verifiedCurrentNameByLoadTimeName);
        context.HasTargetWorkbookRelationshipsPart = targetWorkbookRelsEntry is not null;
        return context;
    }

    private static Dictionary<string, string> ReadSheetPaths(
        XDocument workbookXml,
        IReadOnlyDictionary<string, string> workbookRelationships,
        XNamespace workbookNs,
        XNamespace relNs) =>
        XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(
                workbookXml,
                workbookRelationships,
                workbookNs,
                relNs)
            .ToDictionary(
                pair => pair.SheetName,
                pair => pair.WorksheetPath,
                StringComparer.OrdinalIgnoreCase);

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
