using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetVmlReferencePreserver
{
    private const string VmlDrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";

    public static void Preserve(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context,
        Workbook workbook)
    {
        if (context is null)
            return;

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

            var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceArchive, sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetXml?.Root is null || targetWorksheetEntry is null)
                continue;

            PreserveMarker(
                sourceArchive,
                targetArchive,
                context,
                sourceWorksheetPath,
                targetWorksheetPath,
                targetWorksheetEntry,
                sourceWorksheetXml.Root,
                context.WorkbookNs + "legacyDrawing",
                CanPreserveLegacyDrawing(sheet));
            PreserveMarker(
                sourceArchive,
                targetArchive,
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
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
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

        var sourceMarker = sourceRoot.Element(markerName);
        var sourceRelId = sourceMarker?.Attribute(context.RelNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(sourceRelId) ||
            !TryGetInternalRelationshipTarget(
                sourceArchive,
                XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath),
                sourceWorksheetPath,
                sourceRelId,
                VmlDrawingRelationshipType,
                context.PackageRelNs,
                out var vmlPath) ||
            targetArchive.GetEntry(vmlPath) is null ||
            (markerName.LocalName == "legacyDrawingHF" &&
             !XlsxHeaderFooterPicturePackageGraphNormalizer.Normalize(targetArchive, vmlPath)))
        {
            return;
        }

        var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
        var targetWorksheetRelsXml = targetArchive.GetEntry(targetWorksheetRelsPath) is { } targetWorksheetRelsEntry
            ? XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry)
            : new XDocument(new XElement(context.PackageRelNs + "Relationships"));
        var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            targetWorksheetRelsXml,
            context.PackageRelNs,
            targetWorksheetPath,
            vmlPath,
            VmlDrawingRelationshipType);
        XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

        var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
        var targetRoot = targetWorksheetXml.Root;
        if (targetRoot is null)
            return;

        targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", context.RelNs.NamespaceName);
        SetSingleMarker(targetRoot, markerName, context.RelNs, targetRelId);
        XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
    }

    private static bool TryGetInternalRelationshipTarget(
        ZipArchive archive,
        string relationshipsPath,
        string sourcePartPath,
        string relationshipId,
        string relationshipType,
        XNamespace packageRelNs,
        out string targetPath)
    {
        targetPath = "";
        var relationshipsEntry = archive.GetEntry(relationshipsPath);
        if (relationshipsEntry is null)
            return false;

        var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        var relationship = FindInternalRelationship(
            relationshipsXml.Root,
            packageRelNs,
            relationshipId,
            relationshipType);
        var target = relationship?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        targetPath = XlsxPackagePath.ResolveRelationshipTarget(sourcePartPath, target);
        return !string.IsNullOrWhiteSpace(targetPath);
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
            InsertMarkerInWorksheetOrder(worksheetRoot, marker);
        }

        for (var i = 1; i < existingMarkers.Count; i++)
            existingMarkers[i].Remove();

        marker.RemoveAttributes();
        marker.RemoveNodes();
        marker.SetAttributeValue(relNs + "id", relationshipId);
    }

    private static void InsertMarkerInWorksheetOrder(XElement worksheetRoot, XElement marker)
    {
        var laterElementNames = marker.Name.LocalName == "legacyDrawing"
            ? new[] { "legacyDrawingHF", "picture", "oleObjects", "controls", "webPublishItems", "tableParts", "extLst" }
            : new[] { "picture", "oleObjects", "controls", "webPublishItems", "tableParts", "extLst" };
        var insertionPoint = FindInsertionPoint(worksheetRoot, marker.Name.Namespace, laterElementNames);

        if (insertionPoint is null)
            worksheetRoot.Add(marker);
        else
            insertionPoint.AddBeforeSelf(marker);
    }

    private static XElement? FindInternalRelationship(
        XElement? relationshipsRoot,
        XNamespace packageRelNs,
        string relationshipId,
        string relationshipType)
    {
        if (relationshipsRoot is null)
            return null;

        foreach (var candidate in relationshipsRoot.Elements(packageRelNs + "Relationship"))
        {
            if (string.Equals(candidate.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal) &&
                string.Equals(candidate.Attribute("Type")?.Value, relationshipType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static XElement? FindInsertionPoint(
        XElement worksheetRoot,
        XNamespace markerNamespace,
        string[] laterElementNames)
    {
        foreach (var element in worksheetRoot.Elements())
        {
            if (element.Name.Namespace == markerNamespace &&
                laterElementNames.Contains(element.Name.LocalName, StringComparer.Ordinal))
            {
                return element;
            }
        }

        return null;
    }
}
