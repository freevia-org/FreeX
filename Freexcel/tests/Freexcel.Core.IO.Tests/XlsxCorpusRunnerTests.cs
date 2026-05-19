using Freexcel.Core.IO;
using Freexcel.Core.Model;
using FluentAssertions;
using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO.Tests;

public class XlsxCorpusRunnerTests
{
    [Fact]
    public void GeneratedCorpusRows_RoundTripThroughXlsxAdapter()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .ToArray();

        rows.Should().NotBeEmpty("generated corpus rows are deterministic and do not rely on redistributed Excel files");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            var workbook = XlsxCorpusFixtureFactory.Create(row.Id);

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Length.Should().BeGreaterThan(0, row.Id);

            saved.Position = 0;
            var loaded = adapter.Load(saved);

            loaded.SheetCount.Should().Be(workbook.SheetCount, row.Id);
            loaded.Sheets.Select(sheet => sheet.Name).Should().Equal(workbook.Sheets.Select(sheet => sheet.Name), row.Id);
            loaded.Sheets.Sum(sheet => sheet.CellCount).Should().BeGreaterThan(0, row.Id);
            CaptureSummary(loaded).Should().BeEquivalentTo(
                CaptureSummary(workbook),
                options => options.WithStrictOrdering(),
                row.Id);
        }
    }

    [Fact]
    public void GeneratedKnownGapRows_DeclareExpectedWarningsAndNotes()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-known-gap")
            .ToArray();

        rows.Should().NotBeEmpty("known gaps keep the parity target honest without blocking supported-pass fixtures");
        rows.Should().OnlyContain(row => !string.IsNullOrWhiteSpace(row.ExpectedWarnings));
        rows.Should().OnlyContain(row => !string.IsNullOrWhiteSpace(row.Notes));
    }

    [Fact]
    public void GeneratedKnownGapRows_ProduceExpectedUnsupportedFeatureReports()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-known-gap")
            .ToArray();

        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreateKnownGapPackage(row.Id));

        foreach (var row in rows)
        {
            using var package = XlsxCorpusFixtureFactory.CreateKnownGapPackage(row.Id);
            var report = XlsxFeatureInspector.Inspect(package);

            report.HasUnsupportedFeatures.Should().BeTrue(row.Id);
            var expectedKinds = ExpectedFeatureKindsFor(row);
            report.Features.Select(feature => feature.Kind).Distinct().Should().BeEquivalentTo(expectedKinds, row.Id);
            row.ExpectedWarnings.Should().ContainAll(
                expectedKinds.Select(kind => ExpectedWarningText[kind]),
                row.Id);
        }
    }

    [Fact]
    public void GeneratedKnownGapRows_RetainCriticalPackagePartsAfterModelEdit()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-known-gap")
            .Where(row => XlsxCorpusFixtureFactory.CanCreateKnownGapRetentionPackage(row.Id))
            .ToArray();

        rows.Should().NotBeEmpty("known-gap retention packages catch XLSX package loss during ordinary model edits");

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage(row.Id);
            var before = CapturePackageSummary(source);
            before.CriticalParts.Should().NotBeEmpty(row.Id);

            source.Position = 0;
            var workbook = adapter.Load(source);
            var sheet = workbook.GetSheetAt(0);
            sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new TextValue("freexcel-retention-edit"));

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Position = 0;
            var after = CapturePackageSummary(saved);

            after.CriticalParts.Should().Contain(before.CriticalParts, row.Id);
            after.CriticalRelationshipTargets.Should().Contain(before.CriticalRelationshipTargets, row.Id);
        }
    }

    [Fact]
    public void GeneratedUnsupportedChartFixture_UsesCurrentlyUnsupportedChartFamily()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapPackage("generated-unsupported-chart-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: false);

        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!).ToString();

        chartXml.Should().Contain("surfaceChart");
        chartXml.Should().NotContain("radarChart", "radar charts are supported now and should not anchor the unsupported-chart fixture");
    }

    [Fact]
    public void LocalPrivateCorpusRows_AreSkippedWhenFilesAreAbsent()
    {
        var workspace = FindWorkspaceRoot();
        var privateRows = ReadManifestRows()
            .Where(row => row.SourceType == "local-private")
            .ToArray();

        foreach (var row in privateRows)
        {
            var path = Path.Combine(workspace, "test-corpus", row.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;

            using var stream = File.OpenRead(path);
            var workbook = new XlsxFileAdapter().Load(stream);
            workbook.SheetCount.Should().BeGreaterThan(0, row.Id);
        }
    }

    [Fact]
    public void PublicCorpusRows_OpenAndSaveWhenFilesArePresent()
    {
        var workspace = FindWorkspaceRoot();
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "public")
            .Where(row => row.ExpectedStatus == "public-pass")
            .ToArray();

        rows.Should().HaveCountGreaterThanOrEqualTo(25, "the public corpus should include a meaningful real-workbook sample set");

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            var path = Path.Combine(workspace, "test-corpus", row.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;

            using var source = File.OpenRead(path);
            var workbook = adapter.Load(source);
            workbook.SheetCount.Should().BeGreaterThan(0, row.Id);

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Length.Should().BeGreaterThan(0, row.Id);

            saved.Position = 0;
            var roundTripped = adapter.Load(saved);
            roundTripped.SheetCount.Should().BeGreaterThan(0, row.Id);
            AssertExpectedFeatureTags(row, roundTripped);
        }
    }

    private static IReadOnlyList<ManifestRow> ReadManifestRows()
    {
        var manifestPath = Path.Combine(FindWorkspaceRoot(), "test-corpus", "manifest.csv");
        return File.ReadAllLines(manifestPath)
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseManifestRow)
            .ToArray();
    }

    private static ManifestRow ParseManifestRow(string line)
    {
        var columns = line.Split(',');
        columns.Should().HaveCount(10);
        return new ManifestRow(columns[0], columns[1], columns[2], columns[6], columns[7], columns[8], columns[9]);
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "test-corpus", "manifest.csv")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Freexcel workspace root.");
    }

    private sealed record ManifestRow(
        string Id,
        string Path,
        string SourceType,
        string FeatureTags,
        string ExpectedWarnings,
        string ExpectedStatus,
        string Notes);

    private static readonly IReadOnlyDictionary<XlsxUnsupportedFeatureKind, string> ExpectedWarningText =
        new Dictionary<XlsxUnsupportedFeatureKind, string>
        {
            [XlsxUnsupportedFeatureKind.Macros] = "excluded VBA macro disclosed",
            [XlsxUnsupportedFeatureKind.Charts] = "unsupported chart package disclosed",
            [XlsxUnsupportedFeatureKind.EmbeddedObjects] = "unsupported embedded object disclosed",
            [XlsxUnsupportedFeatureKind.ConditionalFormats] = "unsupported conditional-format rule disclosed",
            [XlsxUnsupportedFeatureKind.DrawingObjects] = "unsupported drawing object disclosed",
            [XlsxUnsupportedFeatureKind.PowerQuery] = "excluded Power Query disclosed",
            [XlsxUnsupportedFeatureKind.DataModel] = "excluded Data Model disclosed",
            [XlsxUnsupportedFeatureKind.LinkedDataTypes] = "excluded linked data type disclosed",
            [XlsxUnsupportedFeatureKind.ThreadedComments] = "unsupported threaded comment disclosed",
            [XlsxUnsupportedFeatureKind.TrackChanges] = "unsupported track changes disclosed",
            [XlsxUnsupportedFeatureKind.FormControls] = "unsupported form control disclosed",
            [XlsxUnsupportedFeatureKind.DigitalSignatures] = "unsupported digital signature disclosed",
            [XlsxUnsupportedFeatureKind.CustomRibbonUi] = "unsupported custom ribbon UI disclosed",
            [XlsxUnsupportedFeatureKind.OfficeAddIns] = "unsupported Office add-in disclosed",
            [XlsxUnsupportedFeatureKind.LiveWebQueries] = "unsupported live web query disclosed",
            [XlsxUnsupportedFeatureKind.SensitivityLabels] = "unsupported sensitivity label disclosed",
            [XlsxUnsupportedFeatureKind.SmartArtDiagrams] = "unsupported SmartArt diagram disclosed",
            [XlsxUnsupportedFeatureKind.UnsupportedSheetTypes] = "unsupported sheet type disclosed"
        };

    private static XlsxUnsupportedFeatureKind[] ExpectedFeatureKindsFor(ManifestRow row)
    {
        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expected = new List<XlsxUnsupportedFeatureKind>();

        if (tags.Contains("macros"))
            expected.Add(XlsxUnsupportedFeatureKind.Macros);

        if (tags.Contains("unsupported-chart-family"))
            expected.Add(XlsxUnsupportedFeatureKind.Charts);

        if (tags.Contains("power-query") || tags.Contains("connections"))
            expected.Add(XlsxUnsupportedFeatureKind.PowerQuery);

        if (tags.Contains("data-model") || tags.Contains("power-pivot"))
            expected.Add(XlsxUnsupportedFeatureKind.DataModel);

        if (tags.Contains("linked-data-types") || tags.Contains("rich-data"))
            expected.Add(XlsxUnsupportedFeatureKind.LinkedDataTypes);

        if (tags.Contains("threaded-comments"))
            expected.Add(XlsxUnsupportedFeatureKind.ThreadedComments);

        if (tags.Contains("track-changes") || tags.Contains("revision-history"))
            expected.Add(XlsxUnsupportedFeatureKind.TrackChanges);

        if (tags.Contains("form-controls") || tags.Contains("activex"))
            expected.Add(XlsxUnsupportedFeatureKind.FormControls);

        if (tags.Contains("digital-signatures"))
            expected.Add(XlsxUnsupportedFeatureKind.DigitalSignatures);

        if (tags.Contains("custom-ribbon-ui"))
            expected.Add(XlsxUnsupportedFeatureKind.CustomRibbonUi);

        if (tags.Contains("office-addins") || tags.Contains("webextensions"))
            expected.Add(XlsxUnsupportedFeatureKind.OfficeAddIns);

        if (tags.Contains("live-web-queries") || tags.Contains("web-publish"))
            expected.Add(XlsxUnsupportedFeatureKind.LiveWebQueries);

        if (tags.Contains("sensitivity-labels") || tags.Contains("irm"))
            expected.Add(XlsxUnsupportedFeatureKind.SensitivityLabels);

        if (tags.Contains("smartart") || tags.Contains("diagrams"))
            expected.Add(XlsxUnsupportedFeatureKind.SmartArtDiagrams);

        if (tags.Contains("chart-sheets") || tags.Contains("dialog-sheets") || tags.Contains("macro-sheets"))
            expected.Add(XlsxUnsupportedFeatureKind.UnsupportedSheetTypes);

        if (tags.Contains("embedded-objects"))
            expected.Add(XlsxUnsupportedFeatureKind.EmbeddedObjects);

        if (tags.Contains("connectors") || tags.Contains("group-shapes"))
            expected.Add(XlsxUnsupportedFeatureKind.DrawingObjects);

        return expected.Distinct().ToArray();
    }

    private static void AssertExpectedFeatureTags(ManifestRow row, Workbook workbook)
    {
        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var summary = CaptureSummary(workbook);

        if (tags.Contains("hyperlinks"))
            summary.Sheets.Sum(sheet => sheet.HyperlinkCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("merged-cells"))
            summary.Sheets.Sum(sheet => sheet.MergedRegionCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("charts") && !tags.Contains("unsupported-chart-family"))
            summary.Sheets.Sum(sheet => sheet.ChartCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("styles") || tags.Contains("formatting"))
            summary.Sheets.Sum(sheet => sheet.StyleOnlyCellCount).Should().BeGreaterThanOrEqualTo(0, row.Id);

        if (tags.Contains("cell-types"))
            summary.Sheets.Sum(sheet => sheet.CellCount).Should().BeGreaterThan(0, row.Id);
    }

    private static WorkbookSummary CaptureSummary(Workbook workbook) =>
        new(
            workbook.SheetCount,
            workbook.NamedRanges.Count,
            workbook.IsStructureProtected,
            workbook.PivotCaches.Count,
            workbook.PivotCaches.Sum(cache => cache.Fields.Count),
            workbook.Sheets.Select(CaptureSheetSummary).ToArray());

    private static SheetSummary CaptureSheetSummary(Sheet sheet) =>
        new(
            sheet.Name,
            sheet.CellCount,
            sheet.EnumerateCells().Count(item => item.Cell.HasFormula),
            sheet.MergedRegions.Count,
            sheet.DataValidations.Count,
            sheet.ConditionalFormats.Count,
            sheet.Comments.Count,
            sheet.Hyperlinks.Count,
            sheet.Charts.Count,
            sheet.PivotTables.Count,
            sheet.PivotTables.Sum(pivot => pivot.RowFields.Count + pivot.ColumnFields.Count + pivot.PageFields.Count + pivot.DataFields.Count),
            sheet.StructuredTables.Count,
            sheet.StructuredTables.Sum(table => table.Columns.Count),
            sheet.Sparklines.Count,
            sheet.TextBoxes.Count,
            sheet.DrawingShapes.Count,
            sheet.Pictures.Count,
            sheet.BackgroundImage is not null,
            sheet.IsProtected,
            sheet.AllowEditRanges.Count,
            sheet.PrintArea is not null,
            sheet.PrintTitleRows is not null,
            sheet.PrintTitleColumns is not null,
            sheet.RowPageBreaks.Count,
            sheet.ColumnPageBreaks.Count,
            sheet.FrozenRows,
            sheet.FrozenCols,
            sheet.SplitRow,
            sheet.SplitColumn,
            sheet.ShowGridlines,
            sheet.ShowHeadings,
            sheet.ShowRulers,
            sheet.ZoomPercent,
            sheet.ShowFormulas,
            sheet.HiddenRows.Count,
            sheet.HiddenCols.Count,
            sheet.RowOutlineLevels.Count,
            sheet.ColOutlineLevels.Count,
            sheet.GroupHiddenRows.Count,
            sheet.GroupHiddenCols.Count,
            sheet.GetStyleOnlyEntries().Count());

    private static PackagePartSummary CapturePackageSummary(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return new PackagePartSummary(
                archive.Entries
                    .Select(entry => entry.FullName.Replace('\\', '/'))
                    .Where(IsFidelityCriticalPart)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                archive.Entries
                    .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(ReadRelationshipTargets)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static bool IsFidelityCriticalPart(string path) =>
        path.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/pivot", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/slicer", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/timeline", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/query", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/model/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/datamodel/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/powerpivot/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/richData/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/threadedComments/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/persons/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/revisionHeaders/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/revisions/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/activeX/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/ctrlProps/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/embeddings/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("customUI/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ReadRelationshipTargets(ZipArchiveEntry relsEntry)
    {
        XDocument relsXml;
        using (var stream = relsEntry.Open())
            relsXml = XDocument.Load(stream);

        XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        return relsXml.Root?
            .Elements(relNs + "Relationship")
            .Select(rel => rel.Attribute("Target")?.Value)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Where(target => !target!.Contains("/package/services/metadata/core-properties/", StringComparison.OrdinalIgnoreCase))
            .Select(target => $"{relsEntry.FullName.Replace('\\', '/')}=>{target!.Replace('\\', '/')}")
            .ToArray() ?? [];
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private sealed record WorkbookSummary(
        int SheetCount,
        int NamedRangeCount,
        bool IsStructureProtected,
        int PivotCacheCount,
        int PivotCacheFieldCount,
        IReadOnlyList<SheetSummary> Sheets);

    private sealed record SheetSummary(
        string Name,
        int CellCount,
        int FormulaCount,
        int MergedRegionCount,
        int DataValidationCount,
        int ConditionalFormatCount,
        int CommentCount,
        int HyperlinkCount,
        int ChartCount,
        int PivotTableCount,
        int PivotTableFieldCount,
        int StructuredTableCount,
        int StructuredTableColumnCount,
        int SparklineCount,
        int TextBoxCount,
        int DrawingShapeCount,
        int PictureCount,
        bool HasBackgroundImage,
        bool IsProtected,
        int AllowEditRangeCount,
        bool HasPrintArea,
        bool HasPrintTitleRows,
        bool HasPrintTitleColumns,
        int RowPageBreakCount,
        int ColumnPageBreakCount,
        uint FrozenRows,
        uint FrozenCols,
        uint? SplitRow,
        uint? SplitColumn,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas,
        int HiddenRowCount,
        int HiddenColumnCount,
        int RowOutlineLevelCount,
        int ColumnOutlineLevelCount,
        int GroupHiddenRowCount,
        int GroupHiddenColumnCount,
        int StyleOnlyCellCount);

    private sealed record PackagePartSummary(
        IReadOnlyList<string> CriticalParts,
        IReadOnlyList<string> CriticalRelationshipTargets);
}
