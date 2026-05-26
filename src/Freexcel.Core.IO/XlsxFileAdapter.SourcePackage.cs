using System.IO.Compression;
using System.Xml.Linq;
using System.Xml;

using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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
        if (HasAnySourcePackagePart(sourceArchive, "xl/pivotCache/", "xl/pivotTables/"))
            XlsxPivotXmlReferencePreserver.Preserve(sourceArchive, generatedArchive, context);
        if (HasSourcePackagePart(sourceArchive, "xl/tables/"))
            XlsxStructuredTableReferencePreserver.Preserve(sourceArchive, generatedArchive, context);
        if (HasSourcePackagePart(sourceArchive, "xl/externalLinks/"))
            XlsxExternalLinkReferencePreserver.Preserve(sourceArchive, generatedArchive);
        if (HasUnsupportedSheetPackagePart(sourceArchive))
            XlsxUnsupportedSheetReferencePreserver.Preserve(sourceArchive, generatedArchive);
        if (HasSourcePackagePart(sourceArchive, "xl/drawings/"))
        {
            var drawingPaths = XlsxWorksheetDrawingPartMerger.MergeAndGetDrawingPaths(sourceArchive, generatedArchive, context);
            XlsxWorksheetDrawingReferencePreserver.Preserve(sourceArchive, generatedArchive, context, drawingPaths);
        }
        if (HasSourcePackagePart(sourceArchive, "xl/printerSettings/"))
            XlsxWorksheetPrinterSettingsReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorksheetMetadataPreserver.Preserve(sourceArchive, generatedArchive, workbook, context);
        XlsxLegacyCommentPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        if (HasSourcePackagePart(sourceArchive, "xl/sharedStrings.xml"))
            XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, generatedArchive);
        if (HasUnsupportedConditionalFormatting(sourceArchive))
            XlsxUnsupportedConditionalFormattingPreserver.Preserve(sourceArchive, generatedArchive);
    }

    private static bool HasSourcePackagePart(ZipArchive archive, string prefix) =>
        archive.Entries.Any(entry => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

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
                !GeneratedChartMatchesSourceModel(sourceEntry, generatedEntry, includeTitle: false))
            {
                continue;
            }

            if (generatedEntry is not null)
                CopyChartExWithModeledTitle(sourceEntry, generatedEntry, generatedArchive);
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
                   .Any(chart => ChartModelsMatch(sourceChart, chart, includeTitle: false));
    }

    private static bool GeneratedChartMatchesSourceModel(
        ZipArchiveEntry sourceEntry,
        ZipArchiveEntry generatedEntry,
        bool includeTitle)
    {
        var sheetId = SheetId.New();
        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var generatedXml = XlsxPackageXmlEditor.LoadXml(generatedEntry);
        if (!XlsxChartPartReader.TryReadSupportedChart(sourceXml, sheetId, out var sourceChart) ||
            !XlsxChartPartReader.TryReadSupportedChart(generatedXml, sheetId, out var generatedChart))
        {
            return false;
        }

        return ChartModelsMatch(sourceChart, generatedChart, includeTitle);
    }

    private static bool ChartModelsMatch(ChartModel sourceChart, ChartModel candidate) =>
        ChartModelsMatch(sourceChart, candidate, includeTitle: true);

    private static bool ChartModelsMatch(ChartModel sourceChart, ChartModel candidate, bool includeTitle) =>
        sourceChart.Type == candidate.Type &&
        RangesMatchIgnoringSheet(sourceChart.DataRange, candidate.DataRange) &&
        sourceChart.FirstRowIsHeader == candidate.FirstRowIsHeader &&
        sourceChart.FirstColIsCategories == candidate.FirstColIsCategories &&
        (!includeTitle || string.Equals(sourceChart.Title ?? "", candidate.Title ?? "", StringComparison.Ordinal));

    private static void CopyChartExWithModeledTitle(
        ZipArchiveEntry sourceEntry,
        ZipArchiveEntry generatedEntry,
        ZipArchive generatedArchive)
    {
        XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var generatedXml = XlsxPackageXmlEditor.LoadXml(generatedEntry);
        var sourceChart = sourceXml.Root?.Element(chartExNs + "chart");
        var generatedTitle = generatedXml.Root?.Element(chartExNs + "chart")?.Element(chartExNs + "title");
        if (sourceChart is not null)
        {
            sourceChart.Element(chartExNs + "title")?.Remove();
            if (generatedTitle is not null)
                sourceChart.AddFirst(new XElement(generatedTitle));
        }

        generatedArchive.GetEntry(sourceEntry.FullName)?.Delete();
        XlsxPackageXmlEditor.ReplaceXml(generatedArchive, sourceEntry.FullName, sourceXml);
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

    private static bool HasAnySourcePackagePart(ZipArchive archive, params string[] prefixes) =>
        archive.Entries.Any(entry => prefixes.Any(prefix => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

    private static bool HasUnsupportedSheetPackagePart(ZipArchive archive) =>
        archive.Entries.Any(entry =>
            entry.FullName.StartsWith("xl/dialogSheets/", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.StartsWith("xl/macrosheets/", StringComparison.OrdinalIgnoreCase));

    private static bool HasUnsupportedConditionalFormatting(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = worksheetEntry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
            });

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element ||
                    !string.Equals(reader.LocalName, "cfRule", StringComparison.Ordinal) ||
                    !string.Equals(reader.NamespaceURI, "http://schemas.openxmlformats.org/spreadsheetml/2006/main", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!IsSupportedConditionalFormatRuleType(reader.GetAttribute("type")))
                    return true;
            }
        }

        return false;
    }

    private static bool IsSupportedConditionalFormatRuleType(string? type) =>
        string.IsNullOrWhiteSpace(type) ||
        string.Equals(type, "cellIs", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "expression", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "colorScale", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "dataBar", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "iconSet", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "aboveAverage", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "top10", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "uniqueValues", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "duplicateValues", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "containsText", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "notContainsText", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "beginsWith", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "endsWith", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "timePeriod", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "containsBlanks", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "notContainsBlanks", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "containsErrors", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "notContainsErrors", StringComparison.OrdinalIgnoreCase);

    private sealed record XlsxSourcePackage(byte[] Buffer, int Offset, int Count)
    {
        public static XlsxSourcePackage Capture(MemoryStream stream)
        {
            if (stream.TryGetBuffer(out var buffer))
                return new XlsxSourcePackage(buffer.Array!, buffer.Offset, buffer.Count);

            var bytes = new byte[stream.Length];
            var previousPosition = stream.Position;
            stream.Position = 0;
            stream.ReadExactly(bytes);
            stream.Position = previousPosition;
            return new XlsxSourcePackage(bytes, 0, bytes.Length);
        }

        public MemoryStream OpenRead() => new(Buffer, Offset, Count, writable: false);
    }
}
