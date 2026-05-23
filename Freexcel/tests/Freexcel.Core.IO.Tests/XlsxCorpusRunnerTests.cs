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
            AssertPackageHealth(saved, row.Id);

            saved.Position = 0;
            var loaded = adapter.Load(saved);

            loaded.SheetCount.Should().Be(workbook.SheetCount, row.Id);
            loaded.Sheets.Select(sheet => sheet.Name).Should().Equal(workbook.Sheets.Select(sheet => sheet.Name), row.Id);
            loaded.Sheets.Sum(sheet => sheet.CellCount).Should().BeGreaterThan(0, row.Id);
            CaptureSummary(loaded).Should().BeEquivalentTo(
                CaptureSummary(workbook),
                options => options.WithStrictOrdering(),
                row.Id);
            AssertExpectedFeatureTags(row, loaded);
        }
    }

    [Fact]
    public void GeneratedCorpusRows_IncludeSurfaceChartCoverage()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .Where(row => row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains("surface-charts"))
            .ToArray();

        rows.Should().ContainSingle("surface charts are now a supported native chart family and need deterministic corpus coverage");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        var workbook = XlsxCorpusFixtureFactory.Create(rows[0].Id);
        workbook.Sheets
            .SelectMany(sheet => sheet.Charts)
            .Select(chart => chart.Type)
            .Should().Contain([ChartType.Surface, ChartType.ThreeDSurface]);
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
            var fixtureParts = CaptureKnownGapFixtureParts(row.Id);
            before.CriticalParts.Should().Contain(fixtureParts, row.Id);

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
    public void GeneratedMetadataPassRows_RetainCriticalPackagePartsAfterModelEdit()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-metadata-pass")
            .ToArray();

        rows.Should().NotBeEmpty("metadata-pass rows cover supported native package features that should retain without warnings");
        rows.Should().HaveCount(5, "the generated metadata-pass manifest currently declares five deterministic package-retention rows");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreateKnownGapRetentionPackage(row.Id));

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage(row.Id);
            var before = CapturePackageSummary(source);
            var fixtureParts = CaptureKnownGapFixtureParts(row.Id);
            before.CriticalParts.Should().Contain(fixtureParts, row.Id);

            source.Position = 0;
            XlsxFeatureInspector.Inspect(source).HasUnsupportedFeatures.Should().BeFalse(row.Id);

            source.Position = 0;
            var workbook = adapter.Load(source);
            var sheet = workbook.GetSheetAt(0);
            sheet.SetCell(new CellAddress(sheet.Id, 11, 1), new TextValue("freexcel-metadata-retention-edit"));

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Position = 0;
            AssertPackageHealth(saved, row.Id);
            var after = CapturePackageSummary(saved);

            after.CriticalParts.Should().Contain(before.CriticalParts, row.Id);
            after.CriticalRelationshipTargets.Should().Contain(before.CriticalRelationshipTargets, row.Id);
        }
    }

    [Theory]
    [InlineData("generated-slicers-001", "xl/drawings/drawing1.xml", "../slicers/slicer1.xml")]
    [InlineData("generated-timelines-001", "xl/drawings/drawing1.xml", "../timelines/timeline1.xml")]
    public void GeneratedSlicerTimelineRows_RetainFloatingDrawingAnchorsAfterModelEdit(
        string id,
        string drawingPart,
        string drawingRelationshipTarget)
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage(id);
        var before = CapturePackageSummary(source);
        before.CriticalParts.Should().Contain(drawingPart, id);
        before.CriticalRelationshipTargets.Should().Contain(target =>
            target.EndsWith($"=>{drawingRelationshipTarget}", StringComparison.OrdinalIgnoreCase), id);

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-floating-anchor-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, id);
        var after = CapturePackageSummary(saved);

        after.CriticalParts.Should().Contain(drawingPart, id);
        after.CriticalRelationshipTargets.Should().Contain(target =>
            target.EndsWith($"=>{drawingRelationshipTarget}", StringComparison.OrdinalIgnoreCase), id);
    }

    [Fact]
    public void PackageSummary_TreatsDocumentPropertiesAsFidelityCriticalParts()
    {
        var workbook = new Workbook("DocumentPropertiesCriticalParts");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("document properties"));

        using var package = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, package);
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            ReplacePackageXml(
                archive,
                "docProps/core.xml",
                new XDocument(new XElement(
                    XName.Get("coreProperties", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"),
                    new XElement(XName.Get("subject", "http://purl.org/dc/elements/1.1/"), "Freexcel parity subject"))));
            ReplacePackageXml(
                archive,
                "docProps/app.xml",
                new XDocument(new XElement(
                    XName.Get("Properties", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                    new XElement(XName.Get("Company", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"), "Freexcel Test Lab"))));
        }

        package.Position = 0;
        var summary = CapturePackageSummary(package);

        summary.CriticalParts.Should().Contain("docProps/core.xml");
        summary.CriticalParts.Should().Contain("docProps/app.xml");
    }

    [Fact]
    public void PackageHealth_AllowsPercentEncodedInternalRelationshipTargets()
    {
        using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                </Types>
                """);
            WritePackageEntry(archive, "xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"/>
                """);
            WritePackageEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                                Target="../media/image%201.png"/>
                </Relationships>
                """);
            archive.CreateEntry("xl/media/image 1.png");
        }

        package.Position = 0;
        var act = () => AssertPackageHealth(package, "percent-encoded relationship target");

        act.Should().NotThrow();
    }

    private static string[] CaptureKnownGapFixtureParts(string id)
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapPackage(id);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: false);
        return archive.Entries
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .Where(IsFidelityCriticalPart)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    [Fact]
    public void GeneratedUnsupportedChartFixture_UsesCurrentlyUnsupportedChartFamily()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapPackage("generated-unsupported-chart-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: false);

        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!).ToString();

        chartXml.Should().Contain("treemapChart");
        chartXml.Should().NotContain("radarChart", "radar charts are supported now and should not anchor the unsupported-chart fixture");
        chartXml.Should().NotContain("surfaceChart", "surface charts are supported now and should not anchor the unsupported-chart fixture");
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
            var before = CapturePublicComparableSummary(workbook);

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Length.Should().BeGreaterThan(0, row.Id);
            AssertPackageHealth(saved, row.Id);

            saved.Position = 0;
            var roundTripped = adapter.Load(saved);
            roundTripped.SheetCount.Should().BeGreaterThan(0, row.Id);
            CapturePublicComparableSummary(roundTripped).Should().BeEquivalentTo(
                before,
                options => options.WithStrictOrdering(),
                row.Id);
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

        return expected.Distinct().ToArray();
    }

    private static void AssertExpectedFeatureTags(ManifestRow row, Workbook workbook)
    {
        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var summary = CaptureSummary(workbook);

        if (tags.Contains("hyperlinks"))
            summary.Sheets.Sum(sheet => sheet.HyperlinkCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("comments") || tags.Contains("notes"))
            summary.Sheets.Sum(sheet => sheet.CommentCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("merged-cells"))
            summary.Sheets.Sum(sheet => sheet.MergedRegionCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("formulas"))
            summary.Sheets.Sum(sheet => sheet.FormulaCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("cross-sheet"))
        {
            summary.SheetCount.Should().BeGreaterThan(1, row.Id);
            workbook.Sheets
                .SelectMany(sheet => sheet.EnumerateCells())
                .Count(item => item.Cell.FormulaText?.Contains('!') == true)
                .Should().BeGreaterThan(0, row.Id);
        }

        if (tags.Contains("named-ranges"))
            summary.NamedRangeCount.Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("data-validation"))
            summary.Sheets.Sum(sheet => sheet.DataValidationCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("conditional-formatting"))
            summary.Sheets.Sum(sheet => sheet.ConditionalFormatCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("color-scales"))
            summary.Sheets.Sum(sheet => sheet.ColorScaleConditionalFormatCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("data-bars"))
            summary.Sheets.Sum(sheet => sheet.DataBarConditionalFormatCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("icon-sets"))
            summary.Sheets.Sum(sheet => sheet.IconSetConditionalFormatCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("charts") && !tags.Contains("unsupported-chart-family"))
            summary.Sheets.Sum(sheet => sheet.ChartCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("surface-charts"))
        {
            var chartTypes = workbook.Sheets
                .SelectMany(sheet => sheet.Charts)
                .Select(chart => chart.Type)
                .ToArray();
            chartTypes.Should().Contain([ChartType.Surface, ChartType.ThreeDSurface], row.Id);
        }

        if (row.SourceType == "generated" && (tags.Contains("styles") || tags.Contains("formatting")))
            (workbook.Sheets.Sum(sheet => sheet.EnumerateCells().Count(item => item.Cell.StyleId != StyleId.Default)) +
             summary.Sheets.Sum(sheet => sheet.StyleOnlyCellCount)).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("cell-types"))
            summary.Sheets.Sum(sheet => sheet.CellCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("text-boxes"))
            summary.Sheets.Sum(sheet => sheet.TextBoxCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("shapes"))
            summary.Sheets.Sum(sheet => sheet.DrawingShapeCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("images"))
            summary.Sheets.Sum(sheet => sheet.PictureCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("sparklines"))
            summary.Sheets.Sum(sheet => sheet.SparklineCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("pivottables"))
            summary.Sheets.Sum(sheet => sheet.PivotTableCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("pivot-caches"))
            summary.PivotCacheCount.Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("pivot-styles"))
        {
            summary.PivotTableStyleCount.Should().BeGreaterThan(0, row.Id);
            summary.PivotTableStyleElementCount.Should().BeGreaterThan(0, row.Id);
        }

        if (tags.Contains("structured-tables") || tags.Contains("listobjects") || tags.Contains("tables"))
            summary.Sheets.Sum(sheet => sheet.StructuredTableCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("protection"))
        {
            summary.IsStructureProtected.Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.IsProtected).Should().BeTrue(row.Id);
            summary.Sheets.Sum(sheet => sheet.AllowEditRangeCount).Should().BeGreaterThan(0, row.Id);
        }

        if (tags.Contains("page-setup"))
        {
            summary.Sheets.Any(sheet => sheet.HasPrintArea || sheet.HasPrintTitleRows || sheet.HasPrintTitleColumns).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.PageOrientation == WorksheetPageOrientation.Landscape).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.PaperSize == WorksheetPaperSize.Letter).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.PageMargins == WorksheetPageMargins.Narrow).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.ScaleToFit.FitToPagesWide == 1 && sheet.ScaleToFit.FitToPagesTall == 1).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.PrintGridlines && sheet.PrintHeadings).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.HasPageHeader).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.HasPageFooter).Should().BeTrue(row.Id);
        }

        if (tags.Contains("structure"))
        {
            summary.Sheets.Sum(sheet => sheet.MergedRegionCount).Should().BeGreaterThan(0, row.Id);
            summary.Sheets.Any(sheet => sheet.FrozenRows > 0 || sheet.FrozenCols > 0).Should().BeTrue(row.Id);
            summary.Sheets.Sum(sheet => sheet.HiddenRowCount + sheet.HiddenColumnCount).Should().BeGreaterThan(0, row.Id);
        }
    }

    private static WorkbookSummary CaptureSummary(Workbook workbook) =>
        new(
            workbook.SheetCount,
            workbook.NamedRanges.Count,
            workbook.IsStructureProtected,
            workbook.PivotCaches.Count,
            workbook.PivotCaches.Sum(cache => cache.Fields.Count),
            workbook.PivotTableStyles.Count,
            workbook.PivotTableStyles.Sum(style => style.Elements.Count),
            workbook.Sheets.Select(CaptureSheetSummary).ToArray());

    private static SheetSummary CaptureSheetSummary(Sheet sheet) =>
        new(
            sheet.Name,
            sheet.CellCount,
            sheet.EnumerateCells().Count(item => item.Cell.HasFormula),
            sheet.MergedRegions.Count,
            sheet.DataValidations.Count,
            sheet.ConditionalFormats.Count,
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.ColorScale),
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.DataBar),
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.IconSet),
            sheet.Comments.Count,
            sheet.Hyperlinks.Count,
            sheet.Charts.Select(CaptureChartSummary).ToArray(),
            sheet.Charts.Count,
            sheet.PivotTables.Count,
            sheet.PivotTables.Sum(pivot => pivot.RowFields.Count + pivot.ColumnFields.Count + pivot.PageFields.Count + pivot.DataFields.Count),
            sheet.StructuredTables.Select(CaptureStructuredTableSummary).ToArray(),
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
            sheet.PageOrientation,
            sheet.PaperSize,
            sheet.PageMargins,
            sheet.ScaleToFit,
            sheet.PrintGridlines,
            sheet.PrintHeadings,
            !sheet.PageHeader.Equals(new WorksheetHeaderFooter("", "", "")),
            !sheet.PageFooter.Equals(new WorksheetHeaderFooter("", "", "")),
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

    private static ChartSummary CaptureChartSummary(ChartModel chart) =>
        new(
            chart.Type,
            chart.Title ?? "",
            chart.ShowLegend,
            chart.IsPivotChart,
            new ChartRangeSummary(
                chart.DataRange.Start.Row,
                chart.DataRange.Start.Col,
                chart.DataRange.End.Row,
                chart.DataRange.End.Col));

    private static StructuredTableSummary CaptureStructuredTableSummary(StructuredTableModel table) =>
        new(
            table.Name,
            table.DisplayName,
            table.StyleName ?? "",
            table.HasAutoFilter,
            table.TotalsRowShown,
            table.ShowFirstColumn,
            table.ShowLastColumn,
            table.ShowRowStripes,
            table.ShowColumnStripes,
            new ChartRangeSummary(
                table.Range.Start.Row,
                table.Range.Start.Col,
                table.Range.End.Row,
                table.Range.End.Col),
            table.Columns
                .Select(column => new StructuredTableColumnSummary(
                    column.Id,
                    column.Name,
                    column.TotalsRowLabel ?? "",
                    column.TotalsRowFunction ?? "",
                    column.CalculatedColumnFormula ?? "",
                    column.TotalsRowFormula ?? ""))
                .ToArray());

    private static WorkbookSummary CapturePublicComparableSummary(Workbook workbook)
    {
        var summary = CaptureSummary(workbook);
        return summary with
        {
            Sheets = summary.Sheets
                .Select(sheet => sheet with { StyleOnlyCellCount = 0 })
                .ToArray()
        };
    }

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

    private static void AssertPackageHealth(Stream stream, string because)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var entries = archive.Entries
                .Select(entry => entry.FullName.Replace('\\', '/'))
                .ToArray();
            entries.Should().OnlyHaveUniqueItems(because);

            var entrySet = entries.ToHashSet(StringComparer.OrdinalIgnoreCase);
            archive.GetEntry("[Content_Types].xml").Should().NotBeNull(because);
            foreach (var xmlEntry in archive.Entries.Where(entry =>
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                         entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
            {
                using var xmlStream = xmlEntry.Open();
                var load = () => XDocument.Load(xmlStream);
                load.Should().NotThrow($"{because}: {xmlEntry.FullName} should be parseable XML");
            }

            foreach (var relsEntry in archive.Entries.Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
            {
                var sourcePart = RelationshipSourcePart(relsEntry.FullName.Replace('\\', '/'));
                var sourceDirectory = Path.GetDirectoryName(sourcePart)?.Replace('\\', '/') ?? string.Empty;
                var relsXml = LoadPackageXml(relsEntry);
                XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
                foreach (var relationship in relsXml.Root?.Elements(relNs + "Relationship") ?? [])
                {
                    if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var target = relationship.Attribute("Target")?.Value;
                    if (string.IsNullOrWhiteSpace(target) || target.StartsWith("/", StringComparison.Ordinal))
                        continue;

                    target = Uri.UnescapeDataString(target);
                    var resolved = NormalizePackagePath(string.IsNullOrWhiteSpace(sourceDirectory)
                        ? target
                        : $"{sourceDirectory}/{target}");
                    entrySet.Should().Contain(resolved, $"{because}: {relsEntry.FullName} relationship target should exist");
                }
            }
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static string RelationshipSourcePart(string relsPath)
    {
        if (string.Equals(relsPath, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var relsMarker = "/_rels/";
        var markerIndex = relsPath.IndexOf(relsMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || !relsPath.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var prefix = relsPath[..markerIndex];
        var fileName = relsPath[(markerIndex + relsMarker.Length)..^".rels".Length];
        return string.IsNullOrWhiteSpace(prefix) ? fileName : $"{prefix}/{fileName}";
    }

    private static string NormalizePackagePath(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return string.Join("/", parts);
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
        path.Equals("xl/connections.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/query", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/queries/", StringComparison.OrdinalIgnoreCase) ||
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
        path.StartsWith("xl/webextensions/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/webPublishItems.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/diagrams/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/dialogSheets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/macroSheets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/printerSettings/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/vbaProject.bin", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/core.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/app.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/custom.xml", StringComparison.OrdinalIgnoreCase) ||
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

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static void WritePackageEntry(ZipArchive archive, string entryName, string content)
    {
        try
        {
            archive.GetEntry(entryName)?.Delete();
        }
        catch (NotSupportedException)
        {
            // ZipArchiveMode.Create does not allow entry lookup.
        }

        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private sealed record WorkbookSummary(
        int SheetCount,
        int NamedRangeCount,
        bool IsStructureProtected,
        int PivotCacheCount,
        int PivotCacheFieldCount,
        int PivotTableStyleCount,
        int PivotTableStyleElementCount,
        IReadOnlyList<SheetSummary> Sheets);

    private sealed record SheetSummary(
        string Name,
        int CellCount,
        int FormulaCount,
        int MergedRegionCount,
        int DataValidationCount,
        int ConditionalFormatCount,
        int ColorScaleConditionalFormatCount,
        int DataBarConditionalFormatCount,
        int IconSetConditionalFormatCount,
        int CommentCount,
        int HyperlinkCount,
        IReadOnlyList<ChartSummary> Charts,
        int ChartCount,
        int PivotTableCount,
        int PivotTableFieldCount,
        IReadOnlyList<StructuredTableSummary> StructuredTables,
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
        WorksheetPageOrientation PageOrientation,
        WorksheetPaperSize PaperSize,
        WorksheetPageMargins PageMargins,
        WorksheetScaleToFit ScaleToFit,
        bool PrintGridlines,
        bool PrintHeadings,
        bool HasPageHeader,
        bool HasPageFooter,
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

    private sealed record ChartSummary(
        ChartType Type,
        string Title,
        bool ShowLegend,
        bool IsPivotChart,
        ChartRangeSummary DataRange);

    private sealed record ChartRangeSummary(
        uint StartRow,
        uint StartColumn,
        uint EndRow,
        uint EndColumn);

    private sealed record StructuredTableSummary(
        string Name,
        string DisplayName,
        string StyleName,
        bool HasAutoFilter,
        bool TotalsRowShown,
        bool ShowFirstColumn,
        bool ShowLastColumn,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        ChartRangeSummary Range,
        IReadOnlyList<StructuredTableColumnSummary> Columns);

    private sealed record StructuredTableColumnSummary(
        int Id,
        string Name,
        string TotalsRowLabel,
        string TotalsRowFunction,
        string CalculatedColumnFormula,
        string TotalsRowFormula);

    private sealed record PackagePartSummary(
        IReadOnlyList<string> CriticalParts,
        IReadOnlyList<string> CriticalRelationshipTargets);
}
