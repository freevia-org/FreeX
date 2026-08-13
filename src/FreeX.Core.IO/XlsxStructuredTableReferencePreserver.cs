using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxStructuredTableReferencePreserver
{
    private const string TableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table";

    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive);
        Preserve(context);
    }

    public static void Preserve(XlsxSourcePackagePreservationContext? context)
    {
        if (context is null)
            return;

        var sourceArchive = context.SourceArchive;
        var targetArchive = context.TargetArchive;
        var tableWorksheetPaths = GetWorksheetPathsWithTableRelationships(sourceArchive, context);
        if (tableWorksheetPaths.Count == 0)
            return;

        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            if (!tableWorksheetPaths.Contains(sourceWorksheetPath))
                continue;

            // R105: rename-tolerant lookup removed as inert (proven dead this round) --
            // XlsxStructuredTableWriter.Save unconditionally regenerates <tableParts> and the table
            // relationship from the model on every save, for every table FreeX can model.
            if (!context.TargetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceWorksheetPath);
            if (sourceWorksheetXml is null || targetWorksheetEntry is null)
                continue;

            var sourceTableParts = sourceWorksheetXml.Root?
                .Element(context.WorkbookNs + "tableParts")?
                .Elements(context.WorkbookNs + "tablePart")
                .ToList() ?? [];
            if (sourceTableParts.Count == 0)
                continue;

            var sourceWorksheetRels = context.GetSourceRelationshipTargets(sourceWorksheetPath);
            var (targetWorksheetRelsPath, targetWorksheetRelsXml) =
                context.LoadOrCreateTargetRelationships(targetWorksheetPath);

            var preservedTableParts = new List<XElement>();
            foreach (var sourceTablePart in sourceTableParts)
            {
                var sourceRelId = sourceTablePart.Attribute(context.RelNs + "id")?.Value;
                if (string.IsNullOrWhiteSpace(sourceRelId) ||
                    !sourceWorksheetRels.TryGetValue(sourceRelId, out var tablePath))
                {
                    continue;
                }

                var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                    targetWorksheetRelsXml,
                    context.PackageRelNs,
                    targetWorksheetPath,
                    tablePath,
                    TableRelationshipType);
                preservedTableParts.Add(new XElement(context.WorkbookNs + "tablePart", new XAttribute(context.RelNs + "id", targetRelId)));
            }

            if (preservedTableParts.Count == 0)
                continue;

            context.ReplaceTargetPartXml(targetWorksheetRelsPath, targetWorksheetRelsXml);

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            targetRoot.Elements(context.WorkbookNs + "tableParts").Remove();
            InsertWorksheetTablePartsInOrder(targetRoot, context.WorkbookNs, new XElement(
                context.WorkbookNs + "tableParts",
                new XAttribute("count", preservedTableParts.Count.ToString(CultureInfo.InvariantCulture)),
                preservedTableParts));
            context.ReplaceTargetPartXml(targetWorksheetPath, targetWorksheetXml);
        }
    }

    private static void InsertWorksheetTablePartsInOrder(
        XElement worksheetRoot,
        XNamespace workbookNs,
        XElement tableParts)
    {
        XElement? extLst = null;
        foreach (var element in worksheetRoot.Elements(workbookNs + "extLst"))
        {
            extLst = element;
            break;
        }

        if (extLst is null)
            worksheetRoot.Add(tableParts);
        else
            extLst.AddBeforeSelf(tableParts);
    }

    private static HashSet<string> GetWorksheetPathsWithTableRelationships(
        ZipArchive sourceArchive,
        XlsxSourcePackagePreservationContext context)
    {
        var worksheetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceWorksheetPath in context.SourceSheets.Values)
        {
            var relationshipsEntry = sourceArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath));
            if (relationshipsEntry is null)
                continue;

            using var relationshipsStream = relationshipsEntry.Open();
            using var reader = new StreamReader(relationshipsStream);
            if (reader.ReadToEnd().Contains(TableRelationshipType, StringComparison.OrdinalIgnoreCase))
                worksheetPaths.Add(sourceWorksheetPath);
        }

        return worksheetPaths;
    }
}
