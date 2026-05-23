using Freexcel.Core.IO;
using Freexcel.Core.Model;
using FluentAssertions;
using System.Globalization;
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
    public void GeneratedCorpusRows_IncludeNamedVisualObjects()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .Where(row => row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(tag => tag is "images" or "text-boxes" or "shapes"))
            .ToArray();

        rows.Should().NotBeEmpty("visual object identity should be covered by deterministic generated fixtures");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        var workbooks = rows.Select(row => XlsxCorpusFixtureFactory.Create(row.Id)).ToArray();
        workbooks
            .SelectMany(workbook => workbook.Sheets)
            .SelectMany(sheet => sheet.Pictures)
            .Should().Contain(picture => !string.IsNullOrWhiteSpace(picture.Name));
        workbooks
            .SelectMany(workbook => workbook.Sheets)
            .SelectMany(sheet => sheet.TextBoxes)
            .Should().Contain(textBox => !string.IsNullOrWhiteSpace(textBox.Name));
        workbooks
            .SelectMany(workbook => workbook.Sheets)
            .SelectMany(sheet => sheet.DrawingShapes)
            .Should().Contain(shape => !string.IsNullOrWhiteSpace(shape.Name));
    }

    [Fact]
    public void GeneratedCorpusRows_IncludeDataValidationMessages()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .Where(row => row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains("data-validation"))
            .ToArray();

        rows.Should().NotBeEmpty("validation prompt/error message metadata should be covered by deterministic generated fixtures");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        rows.Select(row => XlsxCorpusFixtureFactory.Create(row.Id))
            .SelectMany(workbook => workbook.Sheets)
            .SelectMany(sheet => sheet.DataValidations)
            .Should().Contain(validation =>
                !string.IsNullOrWhiteSpace(validation.ErrorTitle) &&
                !string.IsNullOrWhiteSpace(validation.ErrorMessage) &&
                !string.IsNullOrWhiteSpace(validation.PromptTitle) &&
                !string.IsNullOrWhiteSpace(validation.PromptMessage));
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
            var beforeMetadata = CaptureWorkbookMetadataSummary(workbook);
            var sheet = workbook.GetSheetAt(0);
            sheet.SetCell(new CellAddress(sheet.Id, 11, 1), new TextValue("freexcel-metadata-retention-edit"));

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Position = 0;
            AssertPackageHealth(saved, row.Id);
            var after = CapturePackageSummary(saved);

            after.CriticalParts.Should().Contain(before.CriticalParts, row.Id);
            after.CriticalRelationshipTargets.Should().Contain(before.CriticalRelationshipTargets, row.Id);

            saved.Position = 0;
            var roundTripped = adapter.Load(saved);
            CaptureWorkbookMetadataSummary(roundTripped).Should().BeEquivalentTo(
                beforeMetadata,
                options => options.WithStrictOrdering(),
                row.Id);
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
                options => options
                    .Using<double>(ctx => ctx.Subject.Should().BeApproximately(ctx.Expectation, 0.0001))
                    .WhenTypeIs<double>()
                    .WithStrictOrdering(),
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
            workbook.NamedRanges
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => CaptureNamedRangeSummary(workbook, pair.Key, pair.Value))
                .ToArray(),
            workbook.NamedRanges.Count,
            workbook.IsStructureProtected,
            workbook.PivotCaches.Select(CapturePivotCacheSummary).ToArray(),
            workbook.PivotCaches.Count,
            workbook.PivotCaches.Sum(cache => cache.Fields.Count),
            workbook.PivotTableStyles.Count,
            workbook.PivotTableStyles.Sum(style => style.Elements.Count),
            workbook.CustomViews
                .OrderBy(view => view.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CaptureCustomViewSummary)
                .ToArray(),
            workbook.CustomViews.Count,
            CaptureWorkbookMetadataSummary(workbook),
            workbook.Sheets.Select(sheet => CaptureSheetSummary(workbook, sheet)).ToArray());

    private static WorkbookMetadataSummary CaptureWorkbookMetadataSummary(Workbook workbook) =>
        new(
            workbook.Slicers
                .OrderBy(slicer => slicer.PackagePart, StringComparer.OrdinalIgnoreCase)
                .Select(slicer => new SlicerSummary(
                    slicer.Name,
                    slicer.Caption ?? "",
                    slicer.CacheName,
                    slicer.SourcePivotTableName ?? "",
                    slicer.SourceFieldName ?? "",
                    slicer.StyleName ?? "",
                    slicer.SelectedItems.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                    slicer.PackagePart))
                .ToArray(),
            workbook.Timelines
                .OrderBy(timeline => timeline.PackagePart, StringComparer.OrdinalIgnoreCase)
                .Select(timeline => new TimelineSummary(
                    timeline.Name,
                    timeline.Caption ?? "",
                    timeline.CacheName,
                    timeline.SourcePivotTableName ?? "",
                    timeline.SourceFieldName ?? "",
                    timeline.StyleName ?? "",
                    timeline.StartDate ?? "",
                    timeline.EndDate ?? "",
                    timeline.SelectedStartDate ?? "",
                    timeline.SelectedEndDate ?? "",
                    timeline.PackagePart))
                .ToArray(),
            workbook.ExternalLinks
                .OrderBy(link => link.PackagePart, StringComparer.OrdinalIgnoreCase)
                .Select(link => new ExternalLinkSummary(
                    link.PackagePart,
                    link.TargetUri ?? "",
                    link.TargetMode ?? ""))
                .ToArray());

    private static SheetSummary CaptureSheetSummary(Workbook workbook, Sheet sheet) =>
        new(
            sheet.Name,
            sheet.EnumerateCells()
                .OrderBy(item => item.Address.Row)
                .ThenBy(item => item.Address.Col)
                .Select(item => CaptureCellSummary(workbook, item.Address, item.Cell))
                .ToArray(),
            sheet.CellCount,
            sheet.EnumerateCells().Count(item => item.Cell.HasFormula),
            sheet.MergedRegions.Count,
            sheet.DataValidations.Select(CaptureDataValidationSummary).ToArray(),
            sheet.DataValidations.Count,
            sheet.ConditionalFormats
                .OrderBy(format => format.AppliesTo.Start.Row)
                .ThenBy(format => format.AppliesTo.Start.Col)
                .ThenBy(format => format.AppliesTo.End.Row)
                .ThenBy(format => format.AppliesTo.End.Col)
                .ThenBy(format => format.Priority)
                .ThenBy(format => format.RuleType)
                .Select(CaptureConditionalFormatSummary)
                .ToArray(),
            sheet.ConditionalFormats.Count,
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.ColorScale),
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.DataBar),
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.IconSet),
            sheet.Comments
                .OrderBy(pair => pair.Key.Row)
                .ThenBy(pair => pair.Key.Col)
                .Select(pair => new CommentSummary(pair.Key.Row, pair.Key.Col, pair.Value))
                .ToArray(),
            sheet.Comments.Count,
            sheet.Hyperlinks
                .OrderBy(pair => pair.Key.Row)
                .ThenBy(pair => pair.Key.Col)
                .Select(pair => new HyperlinkSummary(pair.Key.Row, pair.Key.Col, pair.Value))
                .ToArray(),
            sheet.Hyperlinks.Count,
            sheet.Charts.Select(CaptureChartSummary).ToArray(),
            sheet.Charts.Count,
            sheet.PivotTables.Select(CapturePivotTableSummary).ToArray(),
            sheet.PivotTables.Count,
            sheet.PivotTables.Sum(pivot => pivot.RowFields.Count + pivot.ColumnFields.Count + pivot.PageFields.Count + pivot.DataFields.Count),
            sheet.StructuredTables.Select(CaptureStructuredTableSummary).ToArray(),
            sheet.StructuredTables.Count,
            sheet.StructuredTables.Sum(table => table.Columns.Count),
            sheet.Sparklines.Select(sparkline => new SparklineSummary(sparkline.Kind, ToRangeSummary(sparkline.DataRange), sparkline.Location.Row, sparkline.Location.Col)).ToArray(),
            sheet.Sparklines.Count,
            sheet.TextBoxes.Select(CaptureTextBoxSummary).ToArray(),
            sheet.TextBoxes.Count,
            sheet.DrawingShapes.Select(CaptureDrawingShapeSummary).ToArray(),
            sheet.DrawingShapes.Count,
            sheet.Pictures.Select(CapturePictureSummary).ToArray(),
            sheet.Pictures.Count,
            CaptureBackgroundImageSummary(sheet.BackgroundImage),
            sheet.BackgroundImage is not null,
            sheet.IsProtected,
            sheet.AllowEditRanges
                .OrderBy(range => range.Start.Row)
                .ThenBy(range => range.Start.Col)
                .ThenBy(range => range.End.Row)
                .ThenBy(range => range.End.Col)
                .Select(ToRangeSummary)
                .ToArray(),
            sheet.AllowEditRanges.Count,
            sheet.PrintArea.HasValue ? ToRangeSummary(sheet.PrintArea.Value) : null,
            sheet.PrintArea is not null,
            sheet.PrintTitleRows.HasValue ? ToRepeatRangeSummary(sheet.PrintTitleRows.Value) : null,
            sheet.PrintTitleRows is not null,
            sheet.PrintTitleColumns.HasValue ? ToRepeatRangeSummary(sheet.PrintTitleColumns.Value) : null,
            sheet.PrintTitleColumns is not null,
            sheet.PageOrientation,
            sheet.PaperSize,
            sheet.PageMargins,
            sheet.HeaderMargin,
            sheet.FooterMargin,
            sheet.ScaleToFit,
            sheet.PrintGridlines,
            sheet.PrintHeadings,
            CaptureHeaderFooterSummary(sheet.PageHeader),
            !sheet.PageHeader.Equals(new WorksheetHeaderFooter("", "", "")),
            CaptureHeaderFooterSummary(sheet.PageFooter),
            !sheet.PageFooter.Equals(new WorksheetHeaderFooter("", "", "")),
            sheet.DifferentFirstPageHeaderFooter ? CaptureHeaderFooterSummary(sheet.FirstPageHeader) : HeaderFooterSummary.Empty,
            sheet.DifferentFirstPageHeaderFooter ? CaptureHeaderFooterSummary(sheet.FirstPageFooter) : HeaderFooterSummary.Empty,
            sheet.DifferentOddEvenHeaderFooter ? CaptureHeaderFooterSummary(sheet.EvenPageHeader) : HeaderFooterSummary.Empty,
            sheet.DifferentOddEvenHeaderFooter ? CaptureHeaderFooterSummary(sheet.EvenPageFooter) : HeaderFooterSummary.Empty,
            sheet.DifferentFirstPageHeaderFooter,
            sheet.DifferentOddEvenHeaderFooter,
            sheet.HeaderFooterScaleWithDocument,
            sheet.HeaderFooterAlignWithMargins,
            sheet.CenterHorizontallyOnPage,
            sheet.CenterVerticallyOnPage,
            sheet.PageOrder,
            sheet.FirstPageNumber,
            sheet.PrintBlackAndWhite,
            sheet.PrintDraftQuality,
            sheet.PrintQualityDpi,
            sheet.PrintErrorValue,
            sheet.PrintComments,
            sheet.RowPageBreaks.OrderBy(row => row).ToArray(),
            sheet.RowPageBreaks.Count,
            sheet.ColumnPageBreaks.OrderBy(column => column).ToArray(),
            sheet.ColumnPageBreaks.Count,
            sheet.FrozenRows,
            sheet.FrozenCols,
            sheet.SplitRow,
            sheet.SplitColumn,
            sheet.ViewMode,
            sheet.ViewTopRow,
            sheet.ViewLeftCol,
            sheet.ActiveRow,
            sheet.ActiveCol,
            sheet.ShowGridlines,
            sheet.ShowHeadings,
            sheet.ShowRulers,
            sheet.ZoomPercent,
            sheet.ShowFormulas,
            sheet.HiddenRows.OrderBy(row => row).ToArray(),
            sheet.HiddenRows.Count,
            sheet.HiddenCols.OrderBy(column => column).ToArray(),
            sheet.HiddenCols.Count,
            sheet.RowOutlineLevels
                .OrderBy(pair => pair.Key)
                .Select(pair => new OutlineLevelSummary(pair.Key, pair.Value))
                .ToArray(),
            sheet.RowOutlineLevels.Count,
            sheet.ColOutlineLevels
                .OrderBy(pair => pair.Key)
                .Select(pair => new OutlineLevelSummary(pair.Key, pair.Value))
                .ToArray(),
            sheet.ColOutlineLevels.Count,
            sheet.GroupHiddenRows.OrderBy(row => row).ToArray(),
            sheet.GroupHiddenRows.Count,
            sheet.GroupHiddenCols.OrderBy(column => column).ToArray(),
            sheet.GroupHiddenCols.Count,
            sheet.GetStyleOnlyEntries()
                .OrderBy(entry => entry.Key.Row)
                .ThenBy(entry => entry.Key.Col)
                .Select(entry => new StyleOnlyCellSummary(
                    entry.Key.Row,
                    entry.Key.Col,
                    CaptureStyleSummary(workbook.GetStyle(entry.StyleId))))
                .ToArray(),
            sheet.GetStyleOnlyEntries().Count());

    private static BackgroundImageSummary? CaptureBackgroundImageSummary(WorksheetBackgroundImage? background) =>
        background is null
            ? null
            : new BackgroundImageSummary(
                background.ContentType,
                background.FileName ?? "",
                background.ImageBytes.Length);

    private static NamedRangeSummary CaptureNamedRangeSummary(Workbook workbook, string name, GridRange range)
    {
        var metadata = workbook.TryGetNamedRangeMetadata(name, out var savedMetadata)
            ? savedMetadata
            : NamedRangeMetadata.WorkbookScope;

        return new NamedRangeSummary(
            name,
            metadata.Scope,
            metadata.Comment,
            ToRangeSummary(range));
    }

    private static CellSummary CaptureCellSummary(Workbook workbook, CellAddress address, Cell cell) =>
        new(
            address.Row,
            address.Col,
            cell.HasFormula ? new ScalarValueSummary("FormulaCachedValue", "") : CaptureScalarValueSummary(cell.Value),
            cell.FormulaText ?? "",
            cell.IgnoreFormulaError);

    private static ScalarValueSummary CaptureScalarValueSummary(ScalarValue value) =>
        value switch
        {
            BlankValue => new ScalarValueSummary("Blank", ""),
            NumberValue number => new ScalarValueSummary("Number", number.Value.ToString("R", CultureInfo.InvariantCulture)),
            BoolValue boolean => new ScalarValueSummary("Boolean", boolean.Value ? "TRUE" : "FALSE"),
            TextValue text => new ScalarValueSummary("Text", text.Value),
            DateTimeValue dateTime => new ScalarValueSummary("DateTime", dateTime.Value.ToString("R", CultureInfo.InvariantCulture)),
            ErrorValue error => new ScalarValueSummary("Error", error.Code),
            _ => new ScalarValueSummary(value.GetType().Name, value.ToString() ?? "")
        };

    private static CustomViewSummary CaptureCustomViewSummary(WorkbookCustomView view) =>
        new(
            view.Name,
            view.IncludePrintSettings,
            view.IncludeHiddenRowsColumnsAndFilterSettings,
            view.Sheets
                .OrderBy(sheet => sheet.SheetName, StringComparer.OrdinalIgnoreCase)
                .Select(sheet => new CustomViewSheetSummary(
                    sheet.SheetName,
                    sheet.ViewMode,
                    sheet.FrozenRows,
                    sheet.FrozenCols,
                    sheet.SplitRow,
                    sheet.SplitColumn,
                    sheet.ShowGridlines,
                    sheet.ShowHeadings,
                    sheet.ShowRulers,
                    sheet.ZoomPercent,
                    sheet.ShowFormulas))
                .ToArray());

    private static ChartSummary CaptureChartSummary(ChartModel chart) =>
        new(
            chart.Type,
            chart.Title ?? "",
            chart.ShowLegend,
            chart.IsPivotChart,
            chart.ChartStyleId,
            chart.RoundedCorners,
            chart.BlankDisplayMode,
            chart.ShowDataInHiddenRowsAndColumns,
            chart.LegendPosition,
            chart.LegendOverlay,
            chart.ShowDataLabels,
            chart.ShowDataLabelCategoryName,
            chart.ShowDataLabelSeriesName,
            chart.ShowDataLabelPercentage,
            chart.BarGapWidth,
            chart.BarOverlap,
            chart.VaryColorsByPoint,
            CaptureChartDataTableSummary(chart.DataTable),
            CaptureChart3DViewSummary(chart.ThreeDView),
            new ChartRangeSummary(
                chart.DataRange.Start.Row,
                chart.DataRange.Start.Col,
                chart.DataRange.End.Row,
                chart.DataRange.End.Col));

    private static ChartDataTableSummary? CaptureChartDataTableSummary(ChartDataTableModel? dataTable) =>
        dataTable is null
            ? null
            : new ChartDataTableSummary(
                dataTable.ShowHorizontalBorder,
                dataTable.ShowVerticalBorder,
                dataTable.ShowOutline,
                dataTable.ShowLegendKeys);

    private static Chart3DViewSummary? CaptureChart3DViewSummary(Chart3DViewModel? view) =>
        view is null
            ? null
            : new Chart3DViewSummary(
                view.RotationX,
                view.HeightPercent,
                view.RotationY,
                view.DepthPercent,
                view.RightAngleAxes,
                view.Perspective);

    private static PivotCacheSummary CapturePivotCacheSummary(PivotCacheModel cache) =>
        new(
            cache.CacheId,
            cache.SourceType,
            cache.SourceSheetName ?? "",
            cache.SourceReference ?? "",
            cache.SourceTableName ?? "",
            cache.ConnectionId,
            cache.IsOlap,
            cache.RefreshOnLoad,
            cache.SaveData,
            cache.EnableRefresh,
            cache.MissingItemsLimit,
            cache.RefreshedVersion,
            cache.Fields
                .Select(field => new PivotCacheFieldSummary(
                    field.Name,
                    field.NumberFormatId,
                    field.SharedItemCount,
                    field.ContainsBlank,
                    field.ContainsString,
                    field.ContainsNumber,
                    field.ContainsDate,
                    field.ContainsMixedTypes,
                    field.ContainsSemiMixedTypes,
                    field.ContainsNonDate,
                    field.ContainsInteger,
                    field.ContainsLongText,
                    field.MinValue,
                    field.MaxValue,
                    field.MinDate ?? "",
                    field.MaxDate ?? "",
                    field.SharedItems?.ToArray() ?? []))
                .ToArray());

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
                .ToArray(),
            table.FilterColumns
                .OrderBy(filter => filter.ColumnId)
                .Select(filter => new StructuredTableFilterColumnSummary(
                    filter.ColumnId,
                    filter.Values.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    filter.IncludeBlank))
                .ToArray());

    private static PivotTableSummary CapturePivotTableSummary(PivotTableModel pivot) =>
        new(
            pivot.Name,
            pivot.CacheId,
            ToRangeSummary(pivot.SourceRange),
            ToRangeSummary(pivot.TargetRange),
            pivot.ShowSubtotals,
            pivot.SubtotalPlacement,
            pivot.ShowRowGrandTotals,
            pivot.ShowColumnGrandTotals,
            pivot.RepeatItemLabels,
            pivot.BlankLineAfterItems,
            pivot.ReportLayout,
            pivot.StyleName,
            pivot.ShowRowHeaders,
            pivot.ShowColumnHeaders,
            pivot.ShowRowStripes,
            pivot.ShowColumnStripes,
            pivot.ShowFieldHeaders,
            pivot.PageOverThenDown,
            pivot.PageWrap,
            pivot.EmptyValueText ?? "",
            pivot.AutofitColumnsOnUpdate,
            pivot.PreserveFormattingOnUpdate,
            pivot.ShowExpandCollapseButtons,
            pivot.PrintTitles,
            pivot.PrintExpandCollapseButtons,
            pivot.RowFields.Select(CapturePivotFieldSummary).ToArray(),
            pivot.ColumnFields.Select(CapturePivotFieldSummary).ToArray(),
            pivot.PageFields.Select(CapturePivotFieldSummary).ToArray(),
            pivot.DataFields.Select(CapturePivotDataFieldSummary).ToArray());

    private static PivotFieldSummary CapturePivotFieldSummary(PivotFieldModel field) =>
        new(
            field.SourceFieldIndex,
            field.SelectedItem ?? "",
            field.SelectedItems?.ToArray() ?? [],
            field.Grouping,
            field.GroupStart,
            field.GroupEnd,
            field.GroupInterval);

    private static PivotDataFieldSummary CapturePivotDataFieldSummary(PivotDataFieldModel field) =>
        new(
            field.SourceFieldIndex,
            field.Name,
            field.SummaryFunction,
            field.NumberFormatId,
            field.CalculatedFieldName ?? "",
            field.ShowValuesAs,
            field.BaseFieldIndex,
            field.BaseItem ?? "",
            field.NumberFormatCode ?? "");

    private static ChartRangeSummary ToRangeSummary(GridRange range) =>
        new(
            range.Start.Row,
            range.Start.Col,
            range.End.Row,
            range.End.Col);

    private static RepeatRangeSummary ToRepeatRangeSummary(WorksheetRepeatRange range) =>
        new(range.Start, range.End);

    private static HeaderFooterSummary CaptureHeaderFooterSummary(WorksheetHeaderFooter value) =>
        new(
            NormalizeHeaderFooterText(value.Left),
            NormalizeHeaderFooterText(value.Center),
            NormalizeHeaderFooterText(value.Right));

    private static string NormalizeHeaderFooterText(string text) =>
        text
            .Replace("&[Page]", "&P", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Pages]", "&N", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Date]", "&D", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Time]", "&T", StringComparison.OrdinalIgnoreCase)
            .Replace("&[File]", "&F", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Tab]", "&A", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Path]", "&Z", StringComparison.OrdinalIgnoreCase);

    private static TextBoxSummary CaptureTextBoxSummary(TextBoxModel textBox) =>
        new(
            textBox.Name ?? "",
            textBox.Text,
            textBox.AltText ?? "",
            textBox.Anchor.Row,
            textBox.Anchor.Col,
            textBox.Width,
            textBox.Height,
            textBox.RotationDegrees,
            textBox.IsVisible);

    private static DrawingShapeSummary CaptureDrawingShapeSummary(DrawingShapeModel shape) =>
        new(
            shape.Name ?? "",
            shape.Kind,
            shape.AltText ?? "",
            shape.Anchor.Row,
            shape.Anchor.Col,
            shape.Width,
            shape.Height,
            shape.RotationDegrees,
            shape.IsVisible);

    private static PictureSummary CapturePictureSummary(PictureModel picture) =>
        new(
            picture.Name ?? "",
            picture.Kind,
            picture.AltText ?? "",
            picture.Anchor.Row,
            picture.Anchor.Col,
            picture.Width,
            picture.Height,
            picture.RotationDegrees,
            picture.IsVisible,
            picture.ContentType ?? "",
            picture.ImageBytes?.Length ?? 0);

    private static ConditionalFormatSummary CaptureConditionalFormatSummary(ConditionalFormat format) =>
        new(
            format.RuleType,
            format.Priority,
            format.Operator,
            format.Value1 ?? "",
            format.Value2 ?? "",
            CaptureStyleSummary(format.FormatIfTrue),
            format.MinColor,
            format.MidColor,
            format.MaxColor,
            format.UseThreeColorScale,
            format.MinThresholdType,
            format.MinThresholdValue ?? "",
            format.MidThresholdType,
            format.MidThresholdValue ?? "",
            format.MaxThresholdType,
            format.MaxThresholdValue ?? "",
            format.DataBarColor,
            format.DataBarMinThresholdType,
            format.DataBarMinThresholdValue ?? "",
            format.DataBarMaxThresholdType,
            format.DataBarMaxThresholdValue ?? "",
            format.DataBarShowValue,
            format.DataBarMinLength,
            format.DataBarMaxLength,
            format.AboveAverage,
            format.FormulaText ?? "",
            format.IconSetStyle ?? "",
            format.IconSetShowValue,
            format.IconSetReverse,
            format.IconSetThresholds.Select(threshold => new ConditionalFormatThresholdSummary(threshold.Type, threshold.Value ?? "")).ToArray(),
            format.TopBottomRank,
            format.TopBottomPercent,
            format.TextRuleText ?? "",
            format.DateOccurringPeriod ?? "",
            format.StopIfTrue,
            ToRangeSummary(format.AppliesTo));

    private static CellStyleSummary? CaptureStyleSummary(CellStyle? style) =>
        style is null
            ? null
            : new(
                style.FontName,
                style.FontSize,
                style.Bold,
                style.Italic,
                style.Underline,
                style.Strikethrough,
                style.FontColor,
                style.FillColor,
                NormalizeFillPatternStyle(style),
                style.FillPatternColor,
                style.NumberFormat);

    private static CellFillPatternStyle NormalizeFillPatternStyle(CellStyle style) =>
        style.FillColor.HasValue && style.FillPatternStyle == CellFillPatternStyle.None
            ? CellFillPatternStyle.Solid
            : style.FillPatternStyle;

    private static DataValidationSummary CaptureDataValidationSummary(DataValidation validation) =>
        new(
            validation.Type,
            validation.Operator,
            validation.Formula1 ?? "",
            validation.Formula2 ?? "",
            validation.AllowBlank,
            validation.ShowDropdown,
            validation.AlertStyle,
            validation.ShowInputMessage,
            validation.ShowErrorMessage,
            validation.ErrorTitle ?? "",
            validation.ErrorMessage ?? "",
            validation.PromptTitle ?? "",
            validation.PromptMessage ?? "",
            ToRangeSummary(validation.AppliesTo));

    private static WorkbookSummary CapturePublicComparableSummary(Workbook workbook)
    {
        var summary = CaptureSummary(workbook);
        return summary with
        {
            Sheets = summary.Sheets
                .Select(sheet => sheet with
                {
                    Cells = [],
                    HeaderFooterAlignWithMargins = true,
                    HeaderFooterScaleWithDocument = true,
                    StyleOnlyCells = [],
                    StyleOnlyCellCount = 0
                })
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
        IReadOnlyList<NamedRangeSummary> NamedRanges,
        int NamedRangeCount,
        bool IsStructureProtected,
        IReadOnlyList<PivotCacheSummary> PivotCaches,
        int PivotCacheCount,
        int PivotCacheFieldCount,
        int PivotTableStyleCount,
        int PivotTableStyleElementCount,
        IReadOnlyList<CustomViewSummary> CustomViews,
        int CustomViewCount,
        WorkbookMetadataSummary Metadata,
        IReadOnlyList<SheetSummary> Sheets);

    private sealed record WorkbookMetadataSummary(
        IReadOnlyList<SlicerSummary> Slicers,
        IReadOnlyList<TimelineSummary> Timelines,
        IReadOnlyList<ExternalLinkSummary> ExternalLinks);

    private sealed record SlicerSummary(
        string Name,
        string Caption,
        string CacheName,
        string SourcePivotTableName,
        string SourceFieldName,
        string StyleName,
        IReadOnlyList<string> SelectedItems,
        string PackagePart);

    private sealed record TimelineSummary(
        string Name,
        string Caption,
        string CacheName,
        string SourcePivotTableName,
        string SourceFieldName,
        string StyleName,
        string StartDate,
        string EndDate,
        string SelectedStartDate,
        string SelectedEndDate,
        string PackagePart);

    private sealed record ExternalLinkSummary(
        string PackagePart,
        string TargetUri,
        string TargetMode);

    private sealed record NamedRangeSummary(
        string Name,
        string Scope,
        string Comment,
        ChartRangeSummary Range);

    private sealed record SheetSummary(
        string Name,
        IReadOnlyList<CellSummary> Cells,
        int CellCount,
        int FormulaCount,
        int MergedRegionCount,
        IReadOnlyList<DataValidationSummary> DataValidations,
        int DataValidationCount,
        IReadOnlyList<ConditionalFormatSummary> ConditionalFormats,
        int ConditionalFormatCount,
        int ColorScaleConditionalFormatCount,
        int DataBarConditionalFormatCount,
        int IconSetConditionalFormatCount,
        IReadOnlyList<CommentSummary> Comments,
        int CommentCount,
        IReadOnlyList<HyperlinkSummary> Hyperlinks,
        int HyperlinkCount,
        IReadOnlyList<ChartSummary> Charts,
        int ChartCount,
        IReadOnlyList<PivotTableSummary> PivotTables,
        int PivotTableCount,
        int PivotTableFieldCount,
        IReadOnlyList<StructuredTableSummary> StructuredTables,
        int StructuredTableCount,
        int StructuredTableColumnCount,
        IReadOnlyList<SparklineSummary> Sparklines,
        int SparklineCount,
        IReadOnlyList<TextBoxSummary> TextBoxes,
        int TextBoxCount,
        IReadOnlyList<DrawingShapeSummary> DrawingShapes,
        int DrawingShapeCount,
        IReadOnlyList<PictureSummary> Pictures,
        int PictureCount,
        BackgroundImageSummary? BackgroundImage,
        bool HasBackgroundImage,
        bool IsProtected,
        IReadOnlyList<ChartRangeSummary> AllowEditRanges,
        int AllowEditRangeCount,
        ChartRangeSummary? PrintArea,
        bool HasPrintArea,
        RepeatRangeSummary? PrintTitleRows,
        bool HasPrintTitleRows,
        RepeatRangeSummary? PrintTitleColumns,
        bool HasPrintTitleColumns,
        WorksheetPageOrientation PageOrientation,
        WorksheetPaperSize PaperSize,
        WorksheetPageMargins PageMargins,
        double HeaderMargin,
        double FooterMargin,
        WorksheetScaleToFit ScaleToFit,
        bool PrintGridlines,
        bool PrintHeadings,
        HeaderFooterSummary PageHeader,
        bool HasPageHeader,
        HeaderFooterSummary PageFooter,
        bool HasPageFooter,
        HeaderFooterSummary FirstPageHeader,
        HeaderFooterSummary FirstPageFooter,
        HeaderFooterSummary EvenPageHeader,
        HeaderFooterSummary EvenPageFooter,
        bool DifferentFirstPageHeaderFooter,
        bool DifferentOddEvenHeaderFooter,
        bool HeaderFooterScaleWithDocument,
        bool HeaderFooterAlignWithMargins,
        bool CenterHorizontallyOnPage,
        bool CenterVerticallyOnPage,
        WorksheetPageOrder PageOrder,
        int? FirstPageNumber,
        bool PrintBlackAndWhite,
        bool PrintDraftQuality,
        int? PrintQualityDpi,
        WorksheetPrintErrorValue PrintErrorValue,
        WorksheetPrintComments PrintComments,
        IReadOnlyList<uint> RowPageBreaks,
        int RowPageBreakCount,
        IReadOnlyList<uint> ColumnPageBreaks,
        int ColumnPageBreakCount,
        uint FrozenRows,
        uint FrozenCols,
        uint? SplitRow,
        uint? SplitColumn,
        WorksheetViewMode ViewMode,
        uint? ViewTopRow,
        uint? ViewLeftColumn,
        uint? ActiveRow,
        uint? ActiveColumn,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas,
        IReadOnlyList<uint> HiddenRows,
        int HiddenRowCount,
        IReadOnlyList<uint> HiddenColumns,
        int HiddenColumnCount,
        IReadOnlyList<OutlineLevelSummary> RowOutlineLevels,
        int RowOutlineLevelCount,
        IReadOnlyList<OutlineLevelSummary> ColumnOutlineLevels,
        int ColumnOutlineLevelCount,
        IReadOnlyList<uint> GroupHiddenRows,
        int GroupHiddenRowCount,
        IReadOnlyList<uint> GroupHiddenColumns,
        int GroupHiddenColumnCount,
        IReadOnlyList<StyleOnlyCellSummary> StyleOnlyCells,
        int StyleOnlyCellCount);

    private sealed record CellSummary(
        uint Row,
        uint Column,
        ScalarValueSummary Value,
        string FormulaText,
        bool IgnoreFormulaError);

    private sealed record ScalarValueSummary(string Kind, string Value);

    private sealed record CustomViewSummary(
        string Name,
        bool IncludePrintSettings,
        bool IncludeHiddenRowsColumnsAndFilterSettings,
        IReadOnlyList<CustomViewSheetSummary> Sheets);

    private sealed record CustomViewSheetSummary(
        string SheetName,
        WorksheetViewMode ViewMode,
        uint FrozenRows,
        uint FrozenCols,
        uint? SplitRow,
        uint? SplitColumn,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas);

    private sealed record CommentSummary(uint Row, uint Column, string Text);

    private sealed record HyperlinkSummary(uint Row, uint Column, string Target);

    private sealed record OutlineLevelSummary(uint Index, int Level);

    private sealed record StyleOnlyCellSummary(uint Row, uint Column, CellStyleSummary? Style);

    private sealed record RepeatRangeSummary(uint Start, uint End);

    private sealed record BackgroundImageSummary(string ContentType, string FileName, int ImageByteCount);

    private sealed record HeaderFooterSummary(string Left, string Center, string Right)
    {
        public static HeaderFooterSummary Empty { get; } = new("", "", "");
    }

    private sealed record ChartSummary(
        ChartType Type,
        string Title,
        bool ShowLegend,
        bool IsPivotChart,
        int? ChartStyleId,
        bool RoundedCorners,
        ChartBlankDisplayMode BlankDisplayMode,
        bool ShowDataInHiddenRowsAndColumns,
        ChartLegendPosition LegendPosition,
        bool LegendOverlay,
        bool ShowDataLabels,
        bool ShowDataLabelCategoryName,
        bool ShowDataLabelSeriesName,
        bool ShowDataLabelPercentage,
        int? BarGapWidth,
        int? BarOverlap,
        bool? VaryColorsByPoint,
        ChartDataTableSummary? DataTable,
        Chart3DViewSummary? ThreeDView,
        ChartRangeSummary DataRange);

    private sealed record ChartDataTableSummary(
        bool? ShowHorizontalBorder,
        bool? ShowVerticalBorder,
        bool? ShowOutline,
        bool? ShowLegendKeys);

    private sealed record Chart3DViewSummary(
        int? RotationX,
        int? HeightPercent,
        int? RotationY,
        int? DepthPercent,
        bool? RightAngleAxes,
        int? Perspective);

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
        IReadOnlyList<StructuredTableColumnSummary> Columns,
        IReadOnlyList<StructuredTableFilterColumnSummary> FilterColumns);

    private sealed record StructuredTableColumnSummary(
        int Id,
        string Name,
        string TotalsRowLabel,
        string TotalsRowFunction,
        string CalculatedColumnFormula,
        string TotalsRowFormula);

    private sealed record StructuredTableFilterColumnSummary(
        int ColumnId,
        IReadOnlyList<string> Values,
        bool IncludeBlank);

    private sealed record PivotTableSummary(
        string Name,
        int CacheId,
        ChartRangeSummary SourceRange,
        ChartRangeSummary TargetRange,
        bool ShowSubtotals,
        PivotSubtotalPlacement SubtotalPlacement,
        bool ShowRowGrandTotals,
        bool ShowColumnGrandTotals,
        bool RepeatItemLabels,
        bool BlankLineAfterItems,
        PivotReportLayout ReportLayout,
        string StyleName,
        bool ShowRowHeaders,
        bool ShowColumnHeaders,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        bool ShowFieldHeaders,
        bool PageOverThenDown,
        int PageWrap,
        string EmptyValueText,
        bool AutofitColumnsOnUpdate,
        bool PreserveFormattingOnUpdate,
        bool ShowExpandCollapseButtons,
        bool PrintTitles,
        bool PrintExpandCollapseButtons,
        IReadOnlyList<PivotFieldSummary> RowFields,
        IReadOnlyList<PivotFieldSummary> ColumnFields,
        IReadOnlyList<PivotFieldSummary> PageFields,
        IReadOnlyList<PivotDataFieldSummary> DataFields);

    private sealed record PivotCacheSummary(
        int CacheId,
        PivotCacheSourceType SourceType,
        string SourceSheetName,
        string SourceReference,
        string SourceTableName,
        int? ConnectionId,
        bool IsOlap,
        bool RefreshOnLoad,
        bool SaveData,
        bool EnableRefresh,
        int? MissingItemsLimit,
        int? RefreshedVersion,
        IReadOnlyList<PivotCacheFieldSummary> Fields);

    private sealed record PivotCacheFieldSummary(
        string Name,
        int? NumberFormatId,
        int? SharedItemCount,
        bool ContainsBlank,
        bool ContainsString,
        bool ContainsNumber,
        bool ContainsDate,
        bool ContainsMixedTypes,
        bool ContainsSemiMixedTypes,
        bool ContainsNonDate,
        bool ContainsInteger,
        bool ContainsLongText,
        double? MinValue,
        double? MaxValue,
        string MinDate,
        string MaxDate,
        IReadOnlyList<string> SharedItems);

    private sealed record PivotFieldSummary(
        int SourceFieldIndex,
        string SelectedItem,
        IReadOnlyList<string> SelectedItems,
        PivotFieldGrouping Grouping,
        double? GroupStart,
        double? GroupEnd,
        double? GroupInterval);

    private sealed record PivotDataFieldSummary(
        int SourceFieldIndex,
        string Name,
        string SummaryFunction,
        int? NumberFormatId,
        string CalculatedFieldName,
        PivotShowValuesAs ShowValuesAs,
        int? BaseFieldIndex,
        string BaseItem,
        string NumberFormatCode);

    private sealed record SparklineSummary(
        SparklineKind Kind,
        ChartRangeSummary DataRange,
        uint LocationRow,
        uint LocationColumn);

    private sealed record TextBoxSummary(
        string Name,
        string Text,
        string AltText,
        uint AnchorRow,
        uint AnchorColumn,
        double Width,
        double Height,
        double RotationDegrees,
        bool IsVisible);

    private sealed record DrawingShapeSummary(
        string Name,
        DrawingShapeKind Kind,
        string AltText,
        uint AnchorRow,
        uint AnchorColumn,
        double Width,
        double Height,
        double RotationDegrees,
        bool IsVisible);

    private sealed record PictureSummary(
        string Name,
        PictureKind Kind,
        string AltText,
        uint AnchorRow,
        uint AnchorColumn,
        double Width,
        double Height,
        double RotationDegrees,
        bool IsVisible,
        string ContentType,
        int ImageByteCount);

    private sealed record DataValidationSummary(
        DvType Type,
        DvOperator Operator,
        string Formula1,
        string Formula2,
        bool AllowBlank,
        bool ShowDropdown,
        DvAlertStyle AlertStyle,
        bool ShowInputMessage,
        bool ShowErrorMessage,
        string ErrorTitle,
        string ErrorMessage,
        string PromptTitle,
        string PromptMessage,
        ChartRangeSummary AppliesTo);

    private sealed record ConditionalFormatSummary(
        CfRuleType RuleType,
        int Priority,
        CfOperator Operator,
        string Value1,
        string Value2,
        CellStyleSummary? FormatIfTrue,
        RgbColor MinColor,
        RgbColor MidColor,
        RgbColor MaxColor,
        bool UseThreeColorScale,
        CfThresholdType MinThresholdType,
        string MinThresholdValue,
        CfThresholdType MidThresholdType,
        string MidThresholdValue,
        CfThresholdType MaxThresholdType,
        string MaxThresholdValue,
        RgbColor DataBarColor,
        CfThresholdType DataBarMinThresholdType,
        string DataBarMinThresholdValue,
        CfThresholdType DataBarMaxThresholdType,
        string DataBarMaxThresholdValue,
        bool DataBarShowValue,
        int? DataBarMinLength,
        int? DataBarMaxLength,
        bool AboveAverage,
        string FormulaText,
        string IconSetStyle,
        bool IconSetShowValue,
        bool IconSetReverse,
        IReadOnlyList<ConditionalFormatThresholdSummary> IconSetThresholds,
        int TopBottomRank,
        bool TopBottomPercent,
        string TextRuleText,
        string DateOccurringPeriod,
        bool StopIfTrue,
        ChartRangeSummary AppliesTo);

    private sealed record ConditionalFormatThresholdSummary(CfThresholdType Type, string Value);

    private sealed record CellStyleSummary(
        string FontName,
        double FontSize,
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strikethrough,
        CellColor FontColor,
        CellColor? FillColor,
        CellFillPatternStyle FillPatternStyle,
        CellColor? FillPatternColor,
        string NumberFormat);

    private sealed record PackagePartSummary(
        IReadOnlyList<string> CriticalParts,
        IReadOnlyList<string> CriticalRelationshipTargets);
}
