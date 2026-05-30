using System.IO.Compression;
using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    // Source package snapshot and native package-part preservation for loaded workbook saves.
    private static void PreserveSourcePackageParts(Workbook workbook, Stream generatedPackage)
    {
        if (!SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return;

        using var sourceStream = sourcePackage.OpenRead();
        using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false);
        using var generatedArchive = new ZipArchive(generatedPackage, ZipArchiveMode.Update, leaveOpen: true);
        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, generatedArchive);
        var sourceParts = InspectSourcePackageParts(sourceArchive);
        var removedWorksheetPackageParts = GetExcludedWorksheetPackagePartPaths(sourceArchive, context, workbook);
        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(
            sourceArchive,
            generatedArchive,
            removedWorksheetPackageParts);

        XlsxPackageMetadataMerger.MergeContentTypes(sourceArchive, generatedArchive, removedWorksheetPackageParts);
        PreserveSourceChartExParts(workbook, sourceArchive, generatedArchive, generatedEntriesBeforeMerge);
        XlsxPackageMetadataMerger.MergeRelationshipParts(
            sourceArchive,
            generatedArchive,
            generatedEntriesBeforeMerge,
            removedWorksheetPackageParts);
        XlsxDocumentPropertiesPreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorkbookMetadataPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        XlsxStylesheetMetadataPreserver.Preserve(sourceArchive, generatedArchive);
        if (sourceParts.HasPivotPackageParts)
            XlsxPivotXmlReferencePreserver.Preserve(sourceArchive, generatedArchive, context);
        if (sourceParts.HasStructuredTables)
            XlsxStructuredTableReferencePreserver.Preserve(sourceArchive, generatedArchive, context);
        if (sourceParts.HasExternalLinks)
            XlsxExternalLinkReferencePreserver.Preserve(sourceArchive, generatedArchive);
        if (sourceParts.HasUnsupportedSheetParts)
            XlsxUnsupportedSheetReferencePreserver.Preserve(sourceArchive, generatedArchive);
        if (sourceParts.HasDrawings)
        {
            var drawingPaths = XlsxWorksheetDrawingPartMerger.MergeAndGetDrawingPaths(sourceArchive, generatedArchive, context);
            XlsxWorksheetDrawingReferencePreserver.Preserve(sourceArchive, generatedArchive, context, drawingPaths);
        }
        if (sourceParts.HasPrinterSettings)
            XlsxWorksheetPrinterSettingsReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorksheetMetadataPreserver.Preserve(sourceArchive, generatedArchive, workbook, context);
        XlsxLegacyCommentPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        if (sourceParts.HasSharedStrings)
            XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, generatedArchive);
        if (HasUnsupportedConditionalFormatting(sourceArchive))
            XlsxUnsupportedConditionalFormattingPreserver.Preserve(sourceArchive, generatedArchive);
    }

    private struct SourcePackagePartSummary
    {
        public bool HasPivotPackageParts;
        public bool HasStructuredTables;
        public bool HasExternalLinks;
        public bool HasUnsupportedSheetParts;
        public bool HasDrawings;
        public bool HasPrinterSettings;
        public bool HasSharedStrings;
    }

    private static SourcePackagePartSummary InspectSourcePackageParts(ZipArchive archive)
    {
        var summary = new SourcePackagePartSummary();
        foreach (var entry in archive.Entries)
        {
            var fullName = entry.FullName;
            summary.HasPivotPackageParts |=
                fullName.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase) ||
                fullName.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase);
            summary.HasStructuredTables |= fullName.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase);
            summary.HasExternalLinks |= fullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase);
            summary.HasUnsupportedSheetParts |=
                fullName.StartsWith("xl/dialogSheets/", StringComparison.OrdinalIgnoreCase) ||
                fullName.StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase) ||
                fullName.StartsWith("xl/macrosheets/", StringComparison.OrdinalIgnoreCase);
            summary.HasDrawings |= fullName.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase);
            summary.HasPrinterSettings |= fullName.StartsWith("xl/printerSettings/", StringComparison.OrdinalIgnoreCase);
            summary.HasSharedStrings |= fullName.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase);

            if (summary.HasPivotPackageParts &&
                summary.HasStructuredTables &&
                summary.HasExternalLinks &&
                summary.HasUnsupportedSheetParts &&
                summary.HasDrawings &&
                summary.HasPrinterSettings &&
                summary.HasSharedStrings)
            {
                break;
            }
        }

        return summary;
    }

    private static IReadOnlySet<string> GetExcludedWorksheetPackagePartPaths(
        ZipArchive sourceArchive,
        XlsxSourcePackagePreservationContext? context,
        Workbook workbook)
    {
        var excludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (context is null)
            return excludedPaths;

        var sourceWorksheetPaths = context.SourceSheets
            .Select(pair => new
            {
                pair.Key,
                SourcePath = XlsxPackagePath.NormalizeZipPath(pair.Value.Replace('\\', '/'))
            })
            .Where(pair => IsWorksheetPartPath(pair.SourcePath))
            .ToList();

        foreach (var sourceSheet in sourceWorksheetPaths)
        {
            if (!context.TargetSheets.TryGetValue(sourceSheet.Key, out var targetPath) ||
                !string.Equals(
                    sourceSheet.SourcePath,
                    XlsxPackagePath.NormalizeZipPath(targetPath.Replace('\\', '/')),
                    StringComparison.OrdinalIgnoreCase))
            {
                excludedPaths.Add(sourceSheet.SourcePath);
                excludedPaths.Add(XlsxPackagePath.GetRelationshipPartPath(sourceSheet.SourcePath));
            }
        }

        var removedWorksheetPaths = sourceWorksheetPaths
            .Where(pair => !context.TargetSheets.ContainsKey(pair.Key))
            .Select(pair => pair.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (removedWorksheetPaths.Count == 0)
            return excludedPaths;

        var retainedWorksheetPaths = sourceWorksheetPaths
            .Where(pair => context.TargetSheets.ContainsKey(pair.Key))
            .Select(pair => pair.SourcePath);
        var retainedTargets = retainedWorksheetPaths
            .SelectMany(path => GetRelationshipDependencyPaths(sourceArchive, path, context.PackageRelNs))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var worksheetPath in removedWorksheetPaths)
        {
            foreach (var targetPath in GetRelationshipDependencyPaths(sourceArchive, worksheetPath, context.PackageRelNs))
            {
                if (!retainedTargets.Contains(targetPath))
                    excludedPaths.Add(targetPath);
            }
        }

        foreach (var sourceSheet in sourceWorksheetPaths)
        {
            if (!context.TargetSheets.ContainsKey(sourceSheet.Key))
                continue;

            var sheet = workbook.GetSheet(sourceSheet.Key);
            if (sheet is null || XlsxHeaderFooterPictureReaderWriter.HasPictures(sheet))
                continue;

            var retainedTargetsOutsideSheet = sourceWorksheetPaths
                .Where(candidate =>
                    context.TargetSheets.ContainsKey(candidate.Key) &&
                    !string.Equals(candidate.SourcePath, sourceSheet.SourcePath, StringComparison.OrdinalIgnoreCase))
                .SelectMany(path => GetRelationshipDependencyPaths(sourceArchive, path.SourcePath, context.PackageRelNs))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var targetPath in GetLegacyDrawingHfDependencyPaths(
                         sourceArchive,
                         sourceSheet.SourcePath,
                         context.WorkbookNs,
                         context.RelNs,
                         context.PackageRelNs))
            {
                if (!retainedTargetsOutsideSheet.Contains(targetPath))
                    excludedPaths.Add(targetPath);
            }
        }

        return excludedPaths;
    }

    private static IEnumerable<string> GetLegacyDrawingHfDependencyPaths(
        ZipArchive archive,
        string worksheetPath,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            yield break;

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var legacyDrawingRelIds = worksheetXml.Root?
            .Element(workbookNs + "legacyDrawingHF")?
            .Attribute(relNs + "id")?
            .Value is { Length: > 0 } relId
            ? new HashSet<string>([relId], StringComparer.Ordinal)
            : [];

        var relationshipPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relationshipEntry = archive.GetEntry(relationshipPath);
        if (relationshipEntry is null)
            yield break;

        var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipEntry);
        var targets = relationshipsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                legacyDrawingRelIds.Contains(relationship.Attribute("Id")?.Value ?? "") ||
                string.Equals(
                    relationship.Attribute("Type")?.Value,
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing",
                    StringComparison.OrdinalIgnoreCase))
            .Select(relationship => relationship.Attribute("Target")?.Value)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .ToList()
            ?? [];
        foreach (var target in targets)
        {
            var vmlPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target!);
            yield return vmlPath;
            yield return XlsxPackagePath.GetRelationshipPartPath(vmlPath);
            foreach (var dependencyPath in GetRelationshipDependencyPaths(archive, vmlPath, packageRelNs))
                yield return dependencyPath;
        }
    }

    private static IEnumerable<string> GetRelationshipDependencyPaths(
        ZipArchive archive,
        string sourcePartPath,
        XNamespace packageRelNs)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(sourcePartPath);

        while (pending.Count > 0)
        {
            var currentPath = pending.Dequeue();
            foreach (var targetPath in GetDirectRelationshipTargets(archive, currentPath, packageRelNs))
            {
                if (!visited.Add(targetPath))
                    continue;

                yield return targetPath;
                var targetRelationshipsPath = XlsxPackagePath.GetRelationshipPartPath(targetPath);
                if (archive.GetEntry(targetRelationshipsPath) is not null)
                {
                    yield return targetRelationshipsPath;
                    pending.Enqueue(targetPath);
                }
            }
        }
    }

    private static IEnumerable<string> GetDirectRelationshipTargets(
        ZipArchive archive,
        string sourcePartPath,
        XNamespace packageRelNs)
    {
        var relationshipPath = XlsxPackagePath.GetRelationshipPartPath(sourcePartPath);
        var relationshipEntry = archive.GetEntry(relationshipPath);
        if (relationshipEntry is null)
            yield break;

        var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipEntry);
        foreach (var relationship in relationshipsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
        {
            if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                continue;

            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
                continue;

            yield return XlsxPackagePath.ResolveRelationshipTarget(sourcePartPath, target);
        }
    }

    private static bool IsWorksheetPartPath(string path) =>
        path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static void PreserveSourceChartExParts(
        Workbook workbook,
        ZipArchive sourceArchive,
        ZipArchive generatedArchive,
        IReadOnlySet<string> generatedEntriesBeforeMerge)
    {
        foreach (var chartExPartPath in GetChartExPartPaths(sourceArchive))
        {
            var sourceEntry = sourceArchive.GetEntry(chartExPartPath);
            if (sourceEntry is null)
                continue;

            if (!WorkbookStillContainsSourceChartModel(workbook, sourceEntry))
                continue;

            var generatedEntry = generatedArchive.GetEntry(chartExPartPath);
            if (generatedEntry is not null &&
                !GeneratedChartIsCompatibleWithSourceChartEx(sourceEntry, generatedEntry))
            {
                continue;
            }

            if (generatedEntry is not null)
                CopyChartExWithModeledContent(sourceEntry, generatedEntry, generatedArchive);
            else
            {
                generatedArchive.GetEntry(chartExPartPath)?.Delete();
                XlsxPackageMetadataMerger.CopyEntry(sourceEntry, generatedArchive);
            }
        }
    }

    private static bool WorkbookStillContainsSourceChartModel(Workbook workbook, ZipArchiveEntry sourceEntry)
    {
        var sheetId = SheetId.New();
        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        return XlsxChartPartReader.TryReadSupportedChart(sourceXml, sheetId, out var sourceChart) &&
               workbook.Sheets
                   .SelectMany(sheet => sheet.Charts)
                   .Any(chart => sourceChart.Type == chart.Type);
    }

    private static bool GeneratedChartIsCompatibleWithSourceChartEx(ZipArchiveEntry sourceEntry, ZipArchiveEntry generatedEntry)
    {
        var sheetId = SheetId.New();
        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var generatedXml = XlsxPackageXmlEditor.LoadXml(generatedEntry);
        if (!XlsxChartPartReader.TryReadSupportedChart(sourceXml, sheetId, out var sourceChart) ||
            !XlsxChartPartReader.TryReadSupportedChart(generatedXml, sheetId, out var generatedChart))
        {
            return false;
        }

        return sourceChart.Type == generatedChart.Type;
    }

    private static bool ChartModelsMatch(ChartModel sourceChart, ChartModel candidate) =>
        sourceChart.Type == candidate.Type &&
        RangesMatchIgnoringSheet(sourceChart.DataRange, candidate.DataRange) &&
        sourceChart.FirstRowIsHeader == candidate.FirstRowIsHeader &&
        sourceChart.FirstColIsCategories == candidate.FirstColIsCategories &&
        string.Equals(sourceChart.Title ?? "", candidate.Title ?? "", StringComparison.Ordinal);

    private static void CopyChartExWithModeledContent(
        ZipArchiveEntry sourceEntry,
        ZipArchiveEntry generatedEntry,
        ZipArchive generatedArchive)
    {
        XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var generatedXml = XlsxPackageXmlEditor.LoadXml(generatedEntry);
        var sourceChart = sourceXml.Root?.Element(chartExNs + "chart");
        var generatedTitle = generatedXml.Root?.Element(chartExNs + "chart")?.Element(chartExNs + "title");
        var generatedLegend = generatedXml.Root?.Element(chartExNs + "chart")?.Element(chartExNs + "legend");
        var generatedChartData = generatedXml.Root?.Element(chartExNs + "chartData");
        if (sourceChart is not null)
        {
            sourceChart.Element(chartExNs + "title")?.Remove();
            if (generatedTitle is not null)
                sourceChart.AddFirst(new XElement(generatedTitle));

            sourceChart.Element(chartExNs + "legend")?.Remove();
            if (generatedLegend is not null)
                sourceChart.Add(new XElement(generatedLegend));

            MergeChartExSeries(sourceChart, generatedXml, chartExNs);
        }

        sourceXml.Root?.Element(chartExNs + "chartData")?.Remove();
        if (generatedChartData is not null)
            sourceXml.Root?.AddFirst(new XElement(generatedChartData));

        generatedArchive.GetEntry(sourceEntry.FullName)?.Delete();
        XlsxPackageXmlEditor.ReplaceXml(generatedArchive, sourceEntry.FullName, sourceXml);
    }

    private static void MergeChartExSeries(XElement sourceChart, XDocument generatedXml, XNamespace chartExNs)
    {
        var sourceRegion = sourceChart
            .Element(chartExNs + "plotArea")
            ?.Element(chartExNs + "plotAreaRegion");
        var generatedSeries = generatedXml.Root?
            .Element(chartExNs + "chart")
            ?.Element(chartExNs + "plotArea")
            ?.Element(chartExNs + "plotAreaRegion")
            ?.Elements(chartExNs + "series")
            .Select(element => new XElement(element))
            .ToList();
        if (sourceRegion is null || generatedSeries is null)
            return;

        sourceRegion.Elements(chartExNs + "series").Remove();
        sourceRegion.Add(generatedSeries);
    }

    private static bool RangesMatchIgnoringSheet(GridRange left, GridRange right) =>
        left.Start.Row == right.Start.Row &&
        left.Start.Col == right.Start.Col &&
        left.End.Row == right.End.Row &&
        left.End.Col == right.End.Col;

    private static IEnumerable<string> GetChartExPartPaths(ZipArchive archive)
    {
        const string chartExContentType = "application/vnd.ms-office.chartex+xml";
        XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            yield break;

        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        foreach (var partName in contentTypesXml.Root?
                     .Elements(contentTypesNs + "Override")
                     .Where(element => string.Equals(element.Attribute("ContentType")?.Value, chartExContentType, StringComparison.OrdinalIgnoreCase))
                     .Select(element => element.Attribute("PartName")?.Value)
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                 ?? [])
        {
            yield return partName!.TrimStart('/');
        }

        foreach (var chartEntry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var chartXml = XlsxPackageXmlEditor.LoadXml(chartEntry);
            if (chartXml.Root?.Name.NamespaceName == "http://schemas.microsoft.com/office/drawing/2014/chartex")
                yield return chartEntry.FullName;
        }
    }

    private static bool HasUnsupportedConditionalFormatting(ZipArchive archive) =>
        XlsxConditionalFormatRuleSupport.HasUnsupportedRuleInWorksheets(archive, allowBlankType: true);
}
