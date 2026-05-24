using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetDrawingReferencePreserver
{
    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
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
            return;
        }

        var sourceWorkbookXml = XlsxPackageXmlEditor.LoadXml(sourceWorkbookEntry);
        var sourceWorkbookRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var targetWorkbookXml = XlsxPackageXmlEditor.LoadXml(targetWorkbookEntry);
        var targetWorkbookRels = XlsxRelationshipReader.LoadTargets(
            targetArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);

        var sourceSheets = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(sourceWorkbookXml, sourceWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        var targetSheets = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(targetWorkbookXml, targetWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetEntry is null || targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = XlsxPackageXmlEditor.LoadXml(sourceWorksheetEntry);
            var sourceDrawing = sourceWorksheetXml.Root?.Element(workbookNs + "drawing");
            var sourceRelId = sourceDrawing?.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(sourceRelId))
                continue;

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null || targetRoot.Element(workbookNs + "drawing") is not null)
                continue;

            var sourceWorksheetRels = XlsxRelationshipReader.LoadTargets(
                sourceArchive,
                XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath),
                sourceWorksheetPath,
                packageRelNs);
            if (!sourceWorksheetRels.TryGetValue(sourceRelId, out var drawingPath))
                continue;

            var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
            var targetWorksheetRelsEntry = targetArchive.GetEntry(targetWorksheetRelsPath);
            var targetWorksheetRelsXml = targetWorksheetRelsEntry is not null
                ? XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                targetWorksheetRelsXml,
                packageRelNs,
                targetWorksheetPath,
                drawingPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing");
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

            targetRoot.Add(new XElement(workbookNs + "drawing", new XAttribute(relNs + "id", targetRelId)));
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    public static void Preserve(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context)
    {
        if (context is null)
        {
            Preserve(sourceArchive, targetArchive);
            return;
        }

        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            if (!context.TargetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceArchive, sourceWorksheetPath);
            if (sourceWorksheetXml is null || targetWorksheetEntry is null)
                continue;

            var sourceDrawing = sourceWorksheetXml.Root?.Element(context.WorkbookNs + "drawing");
            var sourceRelId = sourceDrawing?.Attribute(context.RelNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(sourceRelId))
                continue;

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null || targetRoot.Element(context.WorkbookNs + "drawing") is not null)
                continue;

            var sourceWorksheetRels = XlsxRelationshipReader.LoadTargets(
                sourceArchive,
                XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath),
                sourceWorksheetPath,
                context.PackageRelNs);
            if (!sourceWorksheetRels.TryGetValue(sourceRelId, out var drawingPath))
                continue;

            var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
            var targetWorksheetRelsEntry = targetArchive.GetEntry(targetWorksheetRelsPath);
            var targetWorksheetRelsXml = targetWorksheetRelsEntry is not null
                ? XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry)
                : new XDocument(new XElement(context.PackageRelNs + "Relationships"));
            var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                targetWorksheetRelsXml,
                context.PackageRelNs,
                targetWorksheetPath,
                drawingPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing");
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

            targetRoot.Add(new XElement(context.WorkbookNs + "drawing", new XAttribute(context.RelNs + "id", targetRelId)));
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    public static void Preserve(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context,
        XlsxWorksheetDrawingPathMap drawingPaths)
    {
        if (context is null || drawingPaths == XlsxWorksheetDrawingPathMap.Empty)
        {
            Preserve(sourceArchive, targetArchive, context);
            return;
        }

        foreach (var (sheetName, drawingPath) in drawingPaths.SourceDrawingPaths)
        {
            if (drawingPaths.TargetDrawingPaths.ContainsKey(sheetName))
                continue;
            if (!context.TargetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (targetWorksheetEntry is null)
                continue;

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null || targetRoot.Element(context.WorkbookNs + "drawing") is not null)
                continue;

            var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
            var targetWorksheetRelsEntry = targetArchive.GetEntry(targetWorksheetRelsPath);
            var targetWorksheetRelsXml = targetWorksheetRelsEntry is not null
                ? XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry)
                : new XDocument(new XElement(context.PackageRelNs + "Relationships"));
            var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                targetWorksheetRelsXml,
                context.PackageRelNs,
                targetWorksheetPath,
                drawingPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing");
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

            targetRoot.Add(new XElement(context.WorkbookNs + "drawing", new XAttribute(context.RelNs + "id", targetRelId)));
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }
}
