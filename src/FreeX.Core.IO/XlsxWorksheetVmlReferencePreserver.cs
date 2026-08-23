using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetVmlReferencePreserver
{
    private const string VmlDrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";

    public static void Preserve(
        XlsxSourcePackagePreservationContext? context,
        Workbook workbook)
    {
        if (context is null)
            return;

        var targetArchive = context.TargetArchive;
        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            // R105: rename-tolerant lookup removed as inert (proven dead this round) --
            // XlsxLegacyCommentPreserver re-establishes legacyDrawing later with its own correct
            // rename handling, and XlsxHeaderFooterPicturePackageWriter regenerates the
            // header/footer marker and VML fresh from the model before this preserver runs.
            if (!context.TargetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sheet = workbook.GetSheet(sheetName);
            if (sheet is null)
                continue;

            var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetXml?.Root is null || targetWorksheetEntry is null)
                continue;

            PreserveMarker(
                context,
                sourceWorksheetPath,
                targetWorksheetPath,
                targetWorksheetEntry,
                sourceWorksheetXml.Root,
                context.WorkbookNs + "legacyDrawing",
                CanPreserveLegacyDrawing(sheet));
            PreserveMarker(
                context,
                sourceWorksheetPath,
                targetWorksheetPath,
                targetWorksheetEntry,
                sourceWorksheetXml.Root,
                context.WorkbookNs + "legacyDrawingHF",
                XlsxHeaderFooterPictureReaderWriter.HasPictures(sheet));
        }
    }

    private static bool CanPreserveLegacyDrawing(Sheet sheet) => sheet.Comments.Count > 0;

    private static void PreserveMarker(
        XlsxSourcePackagePreservationContext context,
        string sourceWorksheetPath,
        string targetWorksheetPath,
        ZipArchiveEntry targetWorksheetEntry,
        XElement sourceRoot,
        XName markerName,
        bool shouldPreserve)
    {
        if (!shouldPreserve)
            return;

        var targetArchive = context.TargetArchive;
        var sourceMarker = sourceRoot.Element(markerName);
        var sourceRelId = sourceMarker?.Attribute(context.RelNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(sourceRelId) ||
            !context.TryGetSourceRelationshipTarget(
                sourceWorksheetPath,
                sourceRelId,
                VmlDrawingRelationshipType,
                out var vmlPath) ||
            targetArchive.GetEntry(vmlPath) is null ||
            (markerName.LocalName == "legacyDrawingHF" &&
             !XlsxHeaderFooterPicturePackageGraphNormalizer.Normalize(targetArchive, vmlPath)))
        {
            return;
        }

        var (targetWorksheetRelsPath, targetWorksheetRelsXml) =
            context.LoadOrCreateTargetRelationships(targetWorksheetPath);
        var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            targetWorksheetRelsXml,
            context.PackageRelNs,
            targetWorksheetPath,
            vmlPath,
            VmlDrawingRelationshipType);
        context.ReplaceTargetPartXml(targetWorksheetRelsPath, targetWorksheetRelsXml);

        var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
        var targetRoot = targetWorksheetXml.Root;
        if (targetRoot is null)
            return;

        targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", context.RelNs.NamespaceName);
        SetSingleMarker(targetRoot, markerName, context.RelNs, targetRelId);
        context.ReplaceTargetPartXml(targetWorksheetPath, targetWorksheetXml);
    }

    private static void SetSingleMarker(
        XElement worksheetRoot,
        XName markerName,
        XNamespace relNs,
        string relationshipId)
    {
        var existingMarkers = worksheetRoot.Elements(markerName).ToList();
        var marker = existingMarkers.Count > 0 ? existingMarkers[0] : null;
        if (marker is null)
        {
            marker = new XElement(markerName);
            XlsxWorksheetElementOrder.Insert(worksheetRoot, marker);
        }

        for (var i = 1; i < existingMarkers.Count; i++)
            existingMarkers[i].Remove();

        marker.RemoveAttributes();
        marker.RemoveNodes();
        marker.SetAttributeValue(relNs + "id", relationshipId);
    }

}
