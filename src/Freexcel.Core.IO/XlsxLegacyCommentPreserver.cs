using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxLegacyCommentPreserver
{
    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive, Workbook workbook)
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

        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceCommentsPath = GetLegacyCommentPartPath(sourceArchive, sourceWorksheetPath, packageRelNs);
            var targetCommentsPath = GetLegacyCommentPartPath(targetArchive, targetWorksheetPath, packageRelNs);
            if (sourceCommentsPath is null || targetCommentsPath is null)
                continue;

            var sourceCommentsEntry = sourceArchive.GetEntry(sourceCommentsPath);
            var targetCommentsEntry = targetArchive.GetEntry(targetCommentsPath);
            if (sourceCommentsEntry is null || targetCommentsEntry is null)
                continue;

            var sourceCommentsXml = XlsxPackageXmlEditor.LoadXml(sourceCommentsEntry);
            var targetCommentsXml = XlsxPackageXmlEditor.LoadXml(targetCommentsEntry);
            if (!CanRestoreLegacyCommentPart(sourceCommentsXml, targetCommentsXml, workbookNs))
                continue;

            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetCommentsPath, sourceCommentsXml);
        }
    }

    private static string? GetLegacyCommentPartPath(
        ZipArchive archive,
        string worksheetPath,
        XNamespace packageRelNs)
    {
        var relsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
        if (relsEntry is null)
            return null;

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        var target = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(relationship =>
                (relationship.Attribute("Type")?.Value ?? "").EndsWith("/comments", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("Target")
            ?.Value;
        return string.IsNullOrWhiteSpace(target)
            ? null
            : XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
    }

    private static bool CanRestoreLegacyCommentPart(
        XDocument sourceCommentsXml,
        XDocument targetCommentsXml,
        XNamespace workbookNs)
    {
        var sourceComments = ReadLegacyCommentPlainTextByReference(sourceCommentsXml, workbookNs);
        var targetComments = ReadLegacyCommentPlainTextByReference(targetCommentsXml, workbookNs);
        return sourceComments.Count > 0 &&
               sourceComments.Count == targetComments.Count &&
               sourceComments.All(pair =>
                   targetComments.TryGetValue(pair.Key, out var targetText) &&
                   string.Equals(pair.Value, targetText, StringComparison.Ordinal));
    }

    private static Dictionary<string, string> ReadLegacyCommentPlainTextByReference(
        XDocument commentsXml,
        XNamespace workbookNs)
    {
        return commentsXml.Root?
            .Element(workbookNs + "commentList")?
            .Elements(workbookNs + "comment")
            .Where(comment => !string.IsNullOrWhiteSpace(comment.Attribute("ref")?.Value))
            .ToDictionary(
                comment => comment.Attribute("ref")!.Value,
                comment => string.Concat(comment.Element(workbookNs + "text")?.Descendants(workbookNs + "t").Select(text => text.Value) ?? []),
                StringComparer.OrdinalIgnoreCase) ?? [];
    }
}
