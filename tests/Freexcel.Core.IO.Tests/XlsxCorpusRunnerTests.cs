using Freexcel.Core.IO;
using Freexcel.Core.Model;
using FluentAssertions;

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
            [XlsxUnsupportedFeatureKind.PivotTables] = "excluded PivotTable disclosed",
            [XlsxUnsupportedFeatureKind.Charts] = "unsupported chart package disclosed",
            [XlsxUnsupportedFeatureKind.Slicers] = "excluded slicer disclosed",
            [XlsxUnsupportedFeatureKind.Timelines] = "excluded timeline disclosed",
            [XlsxUnsupportedFeatureKind.ExternalLinks] = "unsupported external link disclosed",
            [XlsxUnsupportedFeatureKind.EmbeddedObjects] = "unsupported embedded object disclosed",
            [XlsxUnsupportedFeatureKind.CustomXmlParts] = "unsupported custom XML disclosed",
            [XlsxUnsupportedFeatureKind.ConditionalFormats] = "unsupported conditional-format rule disclosed",
            [XlsxUnsupportedFeatureKind.DrawingObjects] = "unsupported drawing object disclosed",
            [XlsxUnsupportedFeatureKind.Sparklines] = "unsupported sparkline disclosed",
            [XlsxUnsupportedFeatureKind.PowerQuery] = "excluded Power Query disclosed",
            [XlsxUnsupportedFeatureKind.DataModel] = "excluded Data Model disclosed",
            [XlsxUnsupportedFeatureKind.LinkedDataTypes] = "excluded linked data type disclosed",
            [XlsxUnsupportedFeatureKind.ThreadedComments] = "unsupported threaded comment disclosed",
            [XlsxUnsupportedFeatureKind.TrackChanges] = "unsupported track changes disclosed",
            [XlsxUnsupportedFeatureKind.FormControls] = "unsupported form control disclosed"
        };

    private static XlsxUnsupportedFeatureKind[] ExpectedFeatureKindsFor(ManifestRow row)
    {
        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expected = new List<XlsxUnsupportedFeatureKind>();

        if (tags.Contains("macros"))
            expected.Add(XlsxUnsupportedFeatureKind.Macros);

        if (tags.Contains("pivottables") || tags.Contains("pivot-caches"))
            expected.Add(XlsxUnsupportedFeatureKind.PivotTables);

        if (tags.Contains("unsupported-chart-family"))
            expected.Add(XlsxUnsupportedFeatureKind.Charts);

        if (tags.Contains("slicers"))
            expected.Add(XlsxUnsupportedFeatureKind.Slicers);

        if (tags.Contains("timelines"))
            expected.Add(XlsxUnsupportedFeatureKind.Timelines);

        if (tags.Contains("external-links"))
            expected.Add(XlsxUnsupportedFeatureKind.ExternalLinks);

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

        if (tags.Contains("embedded-objects"))
            expected.Add(XlsxUnsupportedFeatureKind.EmbeddedObjects);

        if (tags.Contains("custom-xml"))
            expected.Add(XlsxUnsupportedFeatureKind.CustomXmlParts);

        if (tags.Contains("color-scales") || tags.Contains("data-bars"))
            expected.Add(XlsxUnsupportedFeatureKind.ConditionalFormats);

        if (tags.Contains("text-boxes") || tags.Contains("shapes") || tags.Contains("images"))
            expected.Add(XlsxUnsupportedFeatureKind.DrawingObjects);

        if (tags.Contains("sparklines"))
            expected.Add(XlsxUnsupportedFeatureKind.Sparklines);

        return expected.Distinct().ToArray();
    }

    private static WorkbookSummary CaptureSummary(Workbook workbook) =>
        new(
            workbook.SheetCount,
            workbook.NamedRanges.Count,
            workbook.IsStructureProtected,
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
            sheet.TextBoxes.Count,
            sheet.DrawingShapes.Count,
            sheet.IsProtected,
            sheet.PrintArea is not null,
            sheet.FrozenRows,
            sheet.FrozenCols,
            sheet.HiddenRows.Count,
            sheet.HiddenCols.Count);

    private sealed record WorkbookSummary(
        int SheetCount,
        int NamedRangeCount,
        bool IsStructureProtected,
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
        int TextBoxCount,
        int DrawingShapeCount,
        bool IsProtected,
        bool HasPrintArea,
        uint FrozenRows,
        uint FrozenCols,
        int HiddenRowCount,
        int HiddenColumnCount);
}
